using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        // A bounded forward model, not a second duel core. Only the FIRST action comes
        // from the core's current legal list. Every response starts from the new live state.
        private const int DevelopmentDepth = 7, DevelopmentWidth = 40, DevelopmentChecks = 45000, DevelopmentNodes = 5000;
        private sealed class Body
        {
            internal ClientCard Card;
            internal ClientCard MaterialView;
            internal List<ClientCard> OverlayCards = new List<ClientCard>();
            internal int Attack, Zone, OverlayCount, ProjectedLevel, FusionName;
            internal bool SynchroOnly, CannotTribute;
            internal int ExtraSetcode;
            internal bool Fresh, FromExtra, EffectsBlocked, DefensePosition, CannotAttack, Temporary, BanishOnLeave;
        }
        private sealed class DevelopmentState
        {
            internal List<Body> Board;
            internal List<ClientCard> Extra, Enemy, Reserve;
            internal Dictionary<ClientCard, int> Spells;
            internal HashSet<ClientCard> ActivatedSpells;
            internal List<ClientCard> Normals;
            internal List<ClientCard> Grave;
            internal List<ClientCard> ResourceActions;
            internal List<ClientCard> FusionActions;
            internal List<ClientCard> PendingFusionMaterials;
            internal HashSet<ClientCard> EffectSentOnly;
            internal HashSet<ClientCard> Acquired, ProperlySummoned;
            internal ClientCard PendingSearch;
            internal ClientCard PendingRecruit;
            internal HashSet<int> HandSummons;
            internal HashSet<int> SpecialSummons;
            internal HashSet<int> ComboUsed;
            internal HashSet<string> LevelUses;
            internal HashSet<ClientCard> ComboInstances;
            internal HashSet<(ClientCard, int)> ComboEffectInstances;
            internal List<Tuple<ClientCard, ComboEffect>> PendingCombos;
            internal List<Tuple<ClientCard, StoryAiLevelEffects.Profile, ClientCard>> PendingLevels;
            internal bool XyzOnly;
            internal bool NormalUsed;
            internal SummonPlan FirstSummon;
            internal HashSet<int> Credited;
            internal bool HalqUsed;
            internal bool SynchroOnly;
            internal ClientCard PendingHalq, PendingDeckTuner, Addition;
            internal ExtraPlan First;
            internal float Credit, Score;
            internal int Depth, Life, DeckCount;
            internal int AllowedAttributes = -1;
        }
        private sealed class SearchBudget
        {
            internal int Checks, Nodes;
            internal int CheckLimit = DevelopmentChecks, NodeLimit = DevelopmentNodes;
            internal bool Exhausted => Checks >= CheckLimit || Nodes >= NodeLimit;
        }
        private string developmentCacheKey;
        private ExtraPlan developmentCache;
        private readonly Dictionary<ClientCard, string> cancelledExtras = new Dictionary<ClientCard, string>();
        internal void NoteCancelledExtra(ClientCard card)
        {
            cancelledExtras[card] = DevelopmentFingerprint(new ClientCard[0]);
            developmentCacheKey = summonCacheKey = null;
        }
        internal bool ExtraNotCancelled(ClientCard card) => !cancelledExtras.TryGetValue(card, out var fingerprint) ||
            fingerprint != DevelopmentFingerprint(new ClientCard[0]);
        private readonly HashSet<int> usedDevelopmentEffects = new HashSet<int>();
        private readonly HashSet<int> usedSpecialSummons = new HashSet<int>();
        private readonly HashSet<ClientCard> summonedThisTurn = new HashSet<ClientCard>();
        private bool developmentSynchroOnly;
        private readonly string developmentDeckFile;
        private List<int> developmentDeckList;
        private bool developmentDeckLoaded;
        internal int LastDevelopmentNodes { get; private set; }

        private IEnumerable<ClientCard> KnownDevelopmentDeck()
        {
            var known = Bot.Deck.Where(c => c.Id != 0).ToList();
            if (known.Count == Bot.Deck.Count || string.IsNullOrEmpty(developmentDeckFile)) return known;
            if (!developmentDeckLoaded)
            {
                developmentDeckLoaded = true;
                // Own submitted deck composition, never opponent information or deck order.
                developmentDeckList = WindBot.Game.Deck.Load(developmentDeckFile)?.Cards.Select(c => c.Id).ToList();
            }
            if (developmentDeckList == null) return known;
            var outside = Bot.Hand.Concat(Bot.GetMonsters()).Concat(Bot.GetSpells()).Concat(Bot.Graveyard).Concat(Bot.Banished)
                .Concat(Enemy.GetMonsters().Where(c => c.Owner == 0)).Distinct().ToList();
            if (outside.Any(c => c.Id == 0)) return known;
            var remaining = developmentDeckList.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
            foreach (int id in outside.Where(c => c.Owner == 0).Select(c => c.Id).Concat(outside.SelectMany(c => c.Overlays)))
                if (remaining.ContainsKey(id)) remaining[id]--;
            if (remaining.Values.Any(i => i < 0) || remaining.Values.Sum() != Bot.Deck.Count) return known;
            return remaining.OrderBy(p => p.Key).SelectMany(p => Enumerable.Range(0, p.Value)
                .Select(_ => new ClientCard(p.Key, CardLocation.Deck, -1) { Controller = 0 })).ToList();
        }

        internal bool KnownOwnDeckContains(int id) => KnownDevelopmentDeck().Any(c => c.Id == id);

        internal void ResetDevelopmentTurn() { ResetResourceTurn(); developmentAllowedAttributes = -1; usedSpecialSummons.Clear(); summonedThisTurn.Clear(); usedLevelEffects.Clear(); usedDevelopmentEffects.Clear(); usedHandSummons.Clear(); usedComboEffects.Clear(); usedComboInstances.Clear(); usedComboEffectInstances.Clear(); liveFusionNames.Clear(); pendingFusionNames.Clear(); developmentXyzOnly = false; temporaryDevelopmentBodies.Clear(); cancelledExtras.Clear(); developmentSynchroOnly = false; normalSummonSpent = false; developmentCacheKey = summonCacheKey = null; }
        internal void NoteDevelopmentEffect(ClientCard card, int description)
        {
            NoteLevelEffect(card, description);
            if (card != null)
            {
                var limit = RegisteredLimit(card, description);
                if (limit != null)
                {
                    usedComboEffects.Add(limit.CountKey);
                    if (limit.DuelOnce) usedDuelComboEffects.Add(limit.CountKey);
                }
                foreach (var combo in ComboProfiles(card).Where(e => !e.Direct && (e.From & card.Location) != 0 && ComboDescription(card, e, description)))
                {
                    if (combo.Once) usedComboEffects.Add(ComboKey(card, combo));
                    if (combo.DuelOnce) usedDuelComboEffects.Add(ComboKey(card, combo));
                    if (combo.SoftOnce) { usedComboInstances.Add(card); usedComboEffectInstances.Add((card, combo.InstanceKey)); }
                    if (combo.SummonTrigger) usedDevelopmentEffects.Add(card.Id);
                    if (combo.SentEffectTrigger && combo.Once) usedDevelopmentEffects.Add(-card.Id);
                }
                int id = CardIdentity(card), offset = description - card.Id * 16;
                // Galaxy Trance applies its Photon/Galaxy oath while paying cost,
                // even if its summon effect is subsequently negated.
                if (Facts(card).SummonSets.Length > 0 && description == 0) usedDevelopmentEffects.Add(id);
                if (ModelledFusionAction(card, description) && ReadFusionSpell(card).Once) usedDevelopmentEffects.Add(id);
                if (ModelledFusionAction(card, description) && ReadFusionSpell(card).Persistent) usedComboInstances.Add(card);
            }
            var trigger = card != null && description == card.Id * 16 ? ParseDeckTunerTrigger(card) : null;
            if (trigger != null)
            {
                usedDevelopmentEffects.Add(card.Id);
                developmentSynchroOnly |= trigger.SynchroOnly;
            }
            if (card != null && description == card.Id * 16 && HasImmediateSynchroDraw(card))
                usedDevelopmentEffects.Add(card.Id);
            if (card != null && ReadRecruitTrigger(card) != null)
                usedDevelopmentEffects.Add(card.Id);
            var resource = card == null ? null : ReadResourceEffect(card);
            if (resource?.Once == true && (description == card.Id * 16 + resource.DescriptionOffset ||
                description == CardIdentity(card) * 16 + resource.DescriptionOffset ||
                description == 0 && Has(card, CardType.Spell) || description == -1 && resource.Trigger))
                usedDevelopmentEffects.Add(card.Id);
            if (card != null && card.Location == CardLocation.Hand && description == card.Id * 16 && ReadHandProcedure(card)?.Once == true)
                usedHandSummons.Add(card.Id);
            developmentCacheKey = summonCacheKey = null;
        }

        private DevelopmentState InitialDevelopment(IEnumerable<ClientCard> roots)
        {
            return new DevelopmentState
            {
                Board = Bot.MonsterZone.Select((c, i) => c == null ? null : new Body
                    { Card = c, Attack = Attack(c), Zone = i, OverlayCount = c.Overlays.Count,
                        OverlayCards = c.Overlays.Where(id => id != 0).Select(id => new ClientCard(id, CardLocation.Overlay, 0) { Controller = 0 }).ToList(),
                        FromExtra = c.LastLocation == CardLocation.Extra, Temporary = temporaryDevelopmentBodies.Contains(c),
                        BanishOnLeave = banishOnLeaveBodies.Contains(c),
                        SynchroOnly = comboSynchroBodies.Contains(c),
                        CannotTribute = c.IsFaceup() && comboUntributableUntil.TryGetValue(c, out var until) && duel.Turn <= until,
                        FusionName = c.IsFaceup() && liveFusionNames.TryGetValue(c, out var name) ? name : 0,
                        ExtraSetcode = c.IsFaceup() && bodyExtraSetcodes.TryGetValue(c, out var extraSet) ? extraSet : 0 }).Where(b => b != null).ToList(),
                Extra = Bot.ExtraDeck.Concat(roots).Where(c => c.Id != 0 && c.Data != null).Distinct().OrderBy(c => c.Id).ToList(),
                Enemy = Enemy.GetMonsters(),
                Spells = Bot.SpellZone.Take(5).Select((c, i) => new { c, i }).Where(p => p.c != null).ToDictionary(p => p.c, p => p.i),
                ActivatedSpells = new HashSet<ClientCard>(spellActivationTurns.Where(p => p.Value == duel.Turn).Select(p => p.Key)),
                Reserve = Bot.Hand.Concat(KnownDevelopmentDeck()).Where(c => c.Id != 0 && c.Data != null).ToList(),
                Normals = duel.MainPhase?.SummonableCards.Where(SimpleNormal).Distinct().ToList() ?? new List<ClientCard>(),
                NormalUsed = normalSummonSpent && duel.MainPhase?.SummonableCards.Count == 0,
                HandSummons = new HashSet<int>(usedHandSummons),
                SpecialSummons = new HashSet<int>(usedSpecialSummons),
                AllowedAttributes = developmentAllowedAttributes,
                ComboUsed = new HashSet<int>(usedComboEffects.Concat(usedDuelComboEffects)), PendingCombos = new List<Tuple<ClientCard, ComboEffect>>(), XyzOnly = developmentXyzOnly,
                ComboInstances = new HashSet<ClientCard>(usedComboInstances),
                ComboEffectInstances = new HashSet<(ClientCard, int)>(usedComboEffectInstances),
                PendingLevels = new List<Tuple<ClientCard, StoryAiLevelEffects.Profile, ClientCard>>(),
                LevelUses = new HashSet<string>(usedLevelEffects),
                Grave = new List<ClientCard>(Bot.Graveyard),
                ResourceActions = Bot.Hand.Where(c => ReadResourceEffect(c) != null && !ReadResourceEffect(c).Trigger).ToList(),
                Acquired = new HashSet<ClientCard>(), ProperlySummoned = new HashSet<ClientCard>(),
                FusionActions = Bot.Hand.Concat(Bot.GetSpells().Where(c => c.IsFaceup())).Where(c => ReadFusionSpell(c) != null).ToList(),
                PendingFusionMaterials = new List<ClientCard>(), EffectSentOnly = new HashSet<ClientCard>(),
                Credited = new HashSet<int>(usedDevelopmentEffects), HalqUsed = false, Life = Bot.LifePoints,
                SynchroOnly = developmentSynchroOnly, DeckCount = Bot.Deck.Count
            };
        }

        private DevelopmentState CopyDevelopment(DevelopmentState state)
        {
            return new DevelopmentState
            {
                Board = new List<Body>(state.Board), Extra = new List<ClientCard>(state.Extra), Enemy = new List<ClientCard>(state.Enemy),
                Spells = new Dictionary<ClientCard, int>(state.Spells),
                ActivatedSpells = new HashSet<ClientCard>(state.ActivatedSpells),
                Reserve = new List<ClientCard>(state.Reserve), Credited = new HashSet<int>(state.Credited),
                Normals = state.Normals, NormalUsed = state.NormalUsed, HandSummons = new HashSet<int>(state.HandSummons), FirstSummon = state.FirstSummon,
                SpecialSummons = new HashSet<int>(state.SpecialSummons),
                AllowedAttributes = state.AllowedAttributes,
                ComboUsed = new HashSet<int>(state.ComboUsed), PendingCombos = new List<Tuple<ClientCard, ComboEffect>>(state.PendingCombos), XyzOnly = state.XyzOnly,
                ComboInstances = new HashSet<ClientCard>(state.ComboInstances),
                ComboEffectInstances = new HashSet<(ClientCard, int)>(state.ComboEffectInstances),
                PendingLevels = new List<Tuple<ClientCard, StoryAiLevelEffects.Profile, ClientCard>>(state.PendingLevels),
                LevelUses = new HashSet<string>(state.LevelUses),
                Grave = new List<ClientCard>(state.Grave), PendingRecruit = state.PendingRecruit,
                ResourceActions = new List<ClientCard>(state.ResourceActions), Acquired = new HashSet<ClientCard>(state.Acquired),
                FusionActions = new List<ClientCard>(state.FusionActions),
                PendingFusionMaterials = new List<ClientCard>(state.PendingFusionMaterials), EffectSentOnly = new HashSet<ClientCard>(state.EffectSentOnly),
                ProperlySummoned = new HashSet<ClientCard>(state.ProperlySummoned), PendingSearch = state.PendingSearch,
                HalqUsed = state.HalqUsed, PendingHalq = state.PendingHalq, First = state.First, Addition = state.Addition,
                PendingDeckTuner = state.PendingDeckTuner, SynchroOnly = state.SynchroOnly,
                Credit = state.Credit, Depth = state.Depth, Life = state.Life, DeckCount = state.DeckCount
            };
        }

        private float TerminalValue(DevelopmentState state)
        {
            float value = state.Board.Sum(b => BodyValue(b)) + state.Credit;
            // Delayed advantage belongs to the surviving source, not to the
            // summon that briefly placed it. Never spend this card in a main-
            // phase continuation before its actual End Phase trigger resolves.
            if (duel.Player == 0)
                foreach (var body in state.Board)
                    foreach (var effect in ComboProfiles(body.Card).Where(e => e.EndPhase && e.Kind == ComboKind.Search))
                        if (ComboReady(state, body.Card, effect))
                            value += ComboPool(state, effect.TargetFrom).Where(c => effect.Filter(ComboView(state, c)) &&
                                (!effect.ExcludeGraveName || !state.Grave.Any(g => CardIdentity(g) == CardIdentity(c))))
                                .Select(c => HandCommitment(c)).DefaultIfEmpty(0).Max();
            // Reward a newly gained live interaction equally whether it was made
            // from the Extra Deck or revived, so material-efficient routes win.
            value += state.Board.Where(b => b.Fresh && !b.EffectsBlocked && LiveInteraction(b.Card))
                .Sum(b => NewInteractionValue(b.Card, b.Attack));
            // I:P is an interaction only with a legal, worthwhile partner/conversion.
            // A lone I:P must not outscore a working engine just because it says Quick Effect.
            foreach (var body in state.Board.Where(b => Facts(b.Card).Effects.Any(f => f.Purpose == StoryLuckyExecutor.EffectPurpose.QuickLink)))
                if (!state.Extra.Where(c => Has(c, CardType.Link)).Any(c =>
                    CanQuickLink(state, body.Card, c))) value -= 1900;
            // Lion Dancer Goddess's quick sweep needs a real remaining extra-deck
            // Lunalight as cost. Its immunity remains useful when that stock is gone.
            foreach (var body in state.Board.Where(b => !b.EffectsBlocked && LiveInteraction(b.Card)))
            {
                var facts = Facts(body.Card).Effects.Where(InteractionFact).ToList();
                if (facts.Count > 0 && facts.All(f => f.CostLocations == (int)CardLocation.Extra &&
                    f.CostFilter != null && !state.Extra.Any(f.CostFilter))) value -= InteractionValue(body.Card, body.Attack);
                if (body.Fresh && facts.Any(f => f.LinkCounters) && !state.Grave.Any(c => Has(c, CardType.Link))) value -= 1900;
            }
            foreach (var group in state.Board.Where(b => !b.EffectsBlocked && LiveInteraction(b.Card)).GroupBy(b => CardIdentity(b.Card)))
                if (group.Count() > 1 && Facts(group.Key).Effects.Any(f => InteractionFact(f) && f.SharedLimit))
                    value -= (group.Count() - 1) * 1600;
            value += Enemy.GetMonsters().Where(c => !state.Enemy.Contains(c)).Sum(BoardScore);
            if (duel.Player == 0 && duel.Turn > 1 && duel.Phase == DuelPhase.Main1 && duel.MainPhase?.CanBattlePhase == true)
            {
                // Credit at most one favorable public battle, bounded by the target's
                // value. Unknown defenders and special battle effects are not simulated.
                value += state.Enemy.Where(c => !Hidden(c) && !c.IsMonsterInvincible() && !c.IsMonsterDangerous() &&
                    !Regex.IsMatch(c.Data?.Description ?? "", @"cannot be destroyed by battle|不会被战斗破坏", RegexOptions.IgnoreCase) &&
                    state.Board.Any(b => !b.CannotAttack && (b.Fresh ? !b.DefensePosition : b.Card.IsAttack() && !b.Card.Attacked) &&
                        b.Attack > c.GetDefensePower() && !b.Card.IsMonsterDangerous() &&
                        !Regex.IsMatch(b.Card.Data?.Description ?? "", @"cannot attack|不能攻击", RegexOptions.IgnoreCase)))
                    .Select(c => Math.Min(1200, BoardScore(c))).DefaultIfEmpty(0).Max();
            }
            if (!BattleDamageBlocked && duel.MainPhase?.CanBattlePhase == true && duel.Player == 0 && duel.Phase == DuelPhase.Main1 && duel.Turn > 1 && state.Enemy.Count == 0 &&
                state.Board.Where(b => !b.CannotAttack && (b.Fresh ? !b.DefensePosition : b.Card.IsAttack() && !b.Card.Attacked)).Sum(b => b.Attack) >= Enemy.LifePoints)
                value += 6000;
            // Equivalent end boards prefer the shorter route with fewer exposure windows.
            return value - state.Depth * 45;
        }

        private float BodyValue(Body body) => (BoardValue(body.Card, body.Attack, body.OverlayCount) -
            (body.EffectsBlocked && LiveInteraction(body.Card) ? InteractionValue(body.Card, body.Attack) : 0)) * (body.Temporary ? .2f : 1);
        private float NewInteractionValue(ClientCard card, int attack) =>
            Math.Min(1000, InteractionValue(card, attack) * .5f);
        private bool ProtectedDevelopmentBody(Body body) => !body.Temporary && !body.EffectsBlocked && !body.Card.IsDisabled() &&
            (PremiumBody(body.Card) || LiveInteraction(body.Card) || Has(body.Card, CardType.Link) && LinkRating(body.Card) >= 3 ||
            Has(body.Card, CardType.Synchro | CardType.Fusion) && Level(body.Card) >= 7 || Has(body.Card, CardType.Xyz) && body.Attack >= 2500);

        private static int Markers(ClientCard c) => c.LinkMarker != 0 ? c.LinkMarker : c.Data?.Defense ?? 0;
        private static int LinkedMainZones(IEnumerable<Body> board)
        {
            int result = 0;
            foreach (var body in board.Where(b => Has(b.Card, CardType.Link)))
            {
                int marker = Markers(body.Card), zone = body.Zone;
                if (zone >= 5)
                {
                    int center = zone == 5 ? 1 : 3;
                    if ((marker & 1) != 0) result |= 1 << (center - 1);
                    if ((marker & 2) != 0) result |= 1 << center;
                    if ((marker & 4) != 0) result |= 1 << (center + 1);
                }
                else
                {
                    if ((marker & 8) != 0 && zone > 0) result |= 1 << (zone - 1);
                    if ((marker & 32) != 0 && zone < 4) result |= 1 << (zone + 1);
                }
            }
            return result;
        }

        private int DevelopmentPlace(DevelopmentState state, ClientCard card, bool extra, bool coreOffered = false)
        {
            int summonKey = Facts(card).SummonOnceKey;
            if (extra && summonKey != 0 && state.SpecialSummons?.Contains(summonKey) == true) return -1;
            if (extra && !SpecialAttributeAllowed(state, card)) return -1;
            if (usedDevelopmentEffects.Any(id => Facts(id).SummonSets.Length > 0 && !SetAny(card, Facts(id).SummonSets))) return -1;
            int occupied = state.Board.Aggregate(0, (mask, b) => mask | 1 << b.Zone);
            int main = (~occupied) & 31;
            int linked = LinkedMainZones(state.Board);
            foreach (var opponent in state.Enemy.Where(c => Has(c, CardType.Link) && c.IsFaceup()))
                linked |= ((opponent.GetLinkedZones() >> 16) & 31);
            // Since the 2020 revision, face-down Fusion/Synchro/Xyz monsters
            // can use any Main Monster Zone. Face-up Pendulums and Links still
            // need an EMZ or an arrow; MR4 applies that restriction to all extras.
            bool arrowRequired = extra && duel.IsNewRule && (!duel.IsNewRule2020 || Has(card, CardType.Link) ||
                Has(card, CardType.Pendulum) && card.IsFaceup());
            if (arrowRequired)
            {
                // The two Extra Monster Zones are a shared pair. A player may
                // use one only while both of their own EMZ are empty; the
                // opposing mirrored zone can independently block that slot.
                if ((occupied & 96) == 0)
                {
                    bool blocked5 = Enemy.MonsterZone[6] != null && state.Enemy.Contains(Enemy.MonsterZone[6]);
                    bool blocked6 = Enemy.MonsterZone[5] != null && state.Enemy.Contains(Enemy.MonsterZone[5]);
                    if (!blocked5) return 5;
                    if (!blocked6) return 6;
                }
                main &= linked;
            }
            else if ((main & ~linked) != 0) main &= ~linked; // reserve arrows for later Links
            for (int i = 0; i < 5; i++) if ((main & (1 << i)) != 0) return i;
            if (extra && duel.IsNewRule && !arrowRequired && (occupied & 96) == 0)
            {
                if (Enemy.MonsterZone[6] == null || !state.Enemy.Contains(Enemy.MonsterZone[6])) return 5;
                if (Enemy.MonsterZone[5] == null || !state.Enemy.Contains(Enemy.MonsterZone[5])) return 6;
            }
            return -1;
        }

        private IEnumerable<DevelopmentState> ExtraSuccessors(DevelopmentState state, IEnumerable<ClientCard> destinations, SearchBudget budget, bool root)
        {
            if (!root && BlanketExtraSummonLock(state)) yield break;
            bool synchroOnly = state.SynchroOnly || state.Board.Any(b => !b.EffectsBlocked && !b.Card.IsDisabled() &&
                (b.Fresh || b.Card.IsFaceup()) && Regex.IsMatch(b.Card.Data?.Description ?? "",
                    @"只要[^。]*在怪兽区域表侧表示存在[^。]*不是同调怪兽不能从额外卡组特殊召唤|while it is face-up in the Monster Zone[^.]*cannot Special Summon from the Extra Deck, except Synchro Monsters", RegexOptions.IgnoreCase));
            foreach (var destination in destinations.OrderBy(c => c.Id))
            {
                if (budget.Exhausted) yield break;
                // Materials alone do not grant a Fusion/Ritual/Pendulum procedure.
                // Only currently core-offered roots may bypass this forward-model limit.
                if (!root && (!Has(destination, CardType.Link | CardType.Synchro | CardType.Xyz) ||
                    Has(destination, CardType.Fusion | CardType.Ritual))) continue;
                if (!root && synchroOnly && !Has(destination, CardType.Synchro)) continue;
                if (!root && !ExtraDestinationAllowed(state, destination)) continue;
                var recipe = MaterialRecipe(destination);
                if (recipe == null || !state.Extra.Contains(destination)) continue;
                var own = state.Board.Where(b => b.Fresh || b.Card.IsFaceup()).ToList();
                var pool = own.Select(b => b.Card).ToList();
                if (root && Has(destination, CardType.Fusion)) pool.AddRange(Bot.Hand.Where(c => Has(c, CardType.Monster)));
                int ownPoolCount = pool.Count;
                if (recipe.EnemyOne) pool.AddRange(state.Enemy.Where(c => c.IsFaceup()));
                if (pool.Count > 14) continue;
                for (int mask = 1; mask < (1 << pool.Count); mask++)
                {
                    int enemyMask = mask >> ownPoolCount;
                    if ((enemyMask & (enemyMask - 1)) != 0) continue;
                    if (budget.Exhausted) yield break;
                    budget.Checks++;
                    var materials = pool.Where((c, i) => (mask & (1 << i)) != 0).ToList();
                    if (!ValidDevelopmentMaterials(state, destination, recipe, materials, own)) continue;
                    int attack = ProjectedSummonAttack(destination, materials);
                    int overlays = ProjectedOverlays(state, destination, materials);
                    float bodyValue = BoardValue(destination, attack, overlays),
                        removed = materials.Where(c => c.Controller == 1).Sum(BoardScore);
                    bool lethal = !BattleDamageBlocked && duel.MainPhase?.CanBattlePhase == true && duel.Player == 0 && duel.Turn > 1 && duel.Phase == DuelPhase.Main1 && state.Enemy.Count == 0 &&
                        attack + state.Board.Where(b => !b.CannotAttack && !materials.Contains(b.Card) && (b.Fresh ? !b.DefensePosition : b.Card.IsAttack() && !b.Card.Attacked)).Sum(b => b.Attack) >= Enemy.LifePoints;
                    // Previously established bosses keep their protection. New bridge bodies
                    // may be converted only because a complete, higher-scoring route exists.
                    if (DuplicateTerminalLoss(destination, materials, bodyValue)) continue;
                    if (own.Any(b => materials.Contains(b.Card) && ProtectedDevelopmentBody(b) && !(recipe.XyzBodies && !LiveInteraction(b.Card)) &&
                        (bodyValue + removed < BodyValue(b) + (PremiumBody(b.Card) ? 400 : 250) || LiveInteraction(b.Card) && !LiveInteraction(destination) &&
                         state.Enemy.Count == 0 && Enemy.GetSpellCount() == 0 && !lethal))) continue;
                    var next = CopyDevelopment(state);
                    next.Board.RemoveAll(b => materials.Contains(b.Card)); next.Enemy.RemoveAll(materials.Contains);
                    if (!Has(destination, CardType.Xyz)) next.Grave.AddRange(materials.Where(c => c.Controller == 0 &&
                        !Has(c, CardType.Token | CardType.Pendulum) && !state.Board.Any(b => b.Card == c && b.BanishOnLeave)));
                    foreach (var body in own.Where(b => materials.Contains(b.Card)))
                    {
                        next.ComboInstances.Remove(body.Card);
                        next.ComboEffectInstances.RemoveWhere(p => p.Item1 == body.Card);
                        if (!Has(destination, CardType.Xyz) || materials.Count != 1) next.Grave.AddRange(body.OverlayCards);
                    }
                    if (Has(destination, CardType.Synchro))
                        foreach (var material in materials.Where(next.Grave.Contains))
                            foreach (var effect in ComboProfiles(material).Where(e => e.MaterialTrigger))
                                if (!next.ComboUsed.Contains(ComboKey(material, effect))) next.PendingCombos.Add(Tuple.Create(material, effect));
                    if (root && Has(destination, CardType.Fusion))
                        foreach (var handMaterial in materials.Where(c => c.Location == CardLocation.Hand))
                        {
                            next.Reserve.Remove(handMaterial);
                            next.Credit -= MaterialCost(handMaterial, HintMsg.FusionMaterial);
                        }
                    int zone = DevelopmentPlace(next, destination, true, root);
                    if (zone < 0) continue;
                    next.Board.Add(new Body { Card = destination, Attack = attack, Zone = zone, Fresh = true, FromExtra = true,
                        OverlayCount = overlays, OverlayCards = !Has(destination, CardType.Xyz) ? new List<ClientCard>() :
                            materials.Concat(materials.Count == 1 ? own.Where(b => materials.Contains(b.Card)).SelectMany(b => b.OverlayCards) : Enumerable.Empty<ClientCard>()).ToList() });
                    QueueLevelArrival(next, destination, false);
                    next.ProperlySummoned.Add(destination);
                    next.Extra.Remove(destination); next.Depth++; next.Credit -= DrawPenalty(CardLocation.Extra) + SummonExposure(state);
                    if (!destination.IsDisabled() && !state.Credited.Contains(destination.Id) && ParseDeckTunerTrigger(destination) != null)
                        next.PendingDeckTuner = destination;
                    QueueDevelopmentSearch(next, destination, false, true);
                    foreach (var trigger in ComboProfiles(destination).Where(e => e.SummonTrigger && e != ReadResourceEffect(destination)?.Combo && (e.ExtraSummonTrigger || e.SpecialTrigger)))
                        if (!next.ComboUsed.Contains(ComboKey(destination, trigger))) next.PendingCombos.Add(Tuple.Create(destination, trigger));
                    if (next.Credited.Add(destination.Id))
                    {
                        if (HasImmediateSynchroDraw(destination))
                        {
                            if (next.DeckCount > 0 && AvailableSynchroDraw(destination))
                            {
                                next.Credit += ImmediatePayoff(destination, next);
                                next.DeckCount--;
                            }
                        }
                        else if (next.PendingSearch == null && !next.PendingCombos.Any(p => p.Item1 == destination)) next.Credit += ImmediatePayoff(destination, next) * .7f;
                    }
                    if (next.First == null && next.Addition == null && next.FirstSummon == null)
                    {
                        next.First = new ExtraPlan { Destination = destination, Materials = materials, Zone = zone,
                            Hint = ExtraMaterialHint(destination) };
                        next.FirstSummon = new SummonPlan { Card = destination, Extra = next.First };
                    }
                    next.Score = TerminalValue(next); budget.Nodes++;
                    // Resolve the known summon trigger before beam pruning; the starter's
                    // small body alone is not the resulting board's value.
                    if (next.PendingDeckTuner != null)
                        foreach (var expanded in EffectSuccessors(next, budget)) yield return expanded;
                    else yield return next;
                }
            }
        }

        private IEnumerable<DevelopmentState> EffectSuccessors(DevelopmentState state, SearchBudget budget)
        {
            if (state.PendingLevels.Count > 0)
            {
                foreach (var child in PendingLevelSuccessors(state, budget)) yield return child;
                yield break;
            }
            if (state.PendingCombos.Count > 0)
            {
                foreach (var child in PendingComboSuccessors(state, budget)) yield return child;
                yield break;
            }
            if (state.PendingFusionMaterials.Count > 0)
            {
                foreach (var child in FusionMaterialSuccessors(state, budget)) yield return child;
                yield break;
            }
            if (state.PendingSearch != null)
            {
                var decline = CopyDevelopment(state); decline.PendingSearch = null; decline.Score = TerminalValue(decline);
                yield return decline;
                foreach (var child in ResourceChoices(state, state.PendingSearch, ReadResourceEffect(state.PendingSearch), budget, false)) yield return child;
                yield break;
            }
            if (state.PendingRecruit != null)
            {
                foreach (var child in RecruitSuccessors(state, budget)) yield return child;
                yield break;
            }
            if (state.PendingDeckTuner != null)
            {
                var trigger = ParseDeckTunerTrigger(state.PendingDeckTuner);
                var choices = state.Reserve.Where(c => !state.Acquired.Contains(c) && c.Location == CardLocation.Deck && trigger.Material.Matches(c) &&
                    !Has(c, CardType.SpSummon) && SpecialAttributeAllowed(state, c)).GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.Id).ToList();
                int count = Math.Min(state.DeckCount, Math.Min(5 - state.Board.Count(b => b.Zone < 5),
                    trigger.DifferentLevels ? choices.Select(Level).Distinct().Count() : 1));
                var decline = CopyDevelopment(state); decline.PendingDeckTuner = null; decline.Score = TerminalValue(decline);
                yield return decline;
                if (count > 0)
                    foreach (var next in DeckTunerSuccessors(state, trigger, choices, 0, new HashSet<int>(), count, budget)) yield return next;
                yield break;
            }

        }

        private IEnumerable<DevelopmentState> DeckTunerSuccessors(DevelopmentState state, DeckTunerTrigger trigger,
            List<ClientCard> choices, int start, HashSet<int> levels, int count, SearchBudget budget)
        {
            if (levels.Count == count)
            {
                var complete = CopyDevelopment(state); complete.PendingDeckTuner = null;
                // All tuners arrive simultaneously, so Maxx C / Fuwalos draw once.
                complete.Credit -= DrawPenalty(CardLocation.Deck) + SummonExposure(state);
                complete.SynchroOnly |= trigger.SynchroOnly; complete.Score = TerminalValue(complete);
                yield return complete;
                yield break;
            }
            if (budget.Exhausted) yield break;
            for (int i = start; i < choices.Count; i++)
            {
                var card = choices[i];
                if (levels.Contains(Level(card))) continue;
                int zone = DevelopmentPlace(state, card, false);
                if (zone < 0 || budget.Exhausted) yield break;
                var next = CopyDevelopment(state); next.Reserve.Remove(card); next.DeckCount--;
                next.Board.Add(new Body { Card = card, Attack = Attack(card), Zone = zone, Fresh = true,
                    EffectsBlocked = trigger.EffectsBlocked, DefensePosition = trigger.DefensePosition });
                QueueLevelArrival(next, card, false);
                if (!trigger.EffectsBlocked) QueueComboArrival(next, card, false);
                budget.Nodes++;
                var used = new HashSet<int>(levels) { Level(card) };
                foreach (var child in DeckTunerSuccessors(next, trigger, choices, i + 1, used, count, budget)) yield return child;
            }
        }

        // Physical copies remain separate resources inside a branch. Only equivalent
        // search STATES are merged: swapping two identical, unused copies must not
        // occupy two beam slots. Stats, origin, revival status and instance limits
        // distinguish copies that actually behave differently.
        private sealed class DevelopmentKeys
        {
            private readonly Dictionary<ClientCard, int> observations = new Dictionary<ClientCard, int>();
            private readonly Dictionary<string, int> classes = new Dictionary<string, int>();
            internal int Key(DevelopmentState state, ClientCard card)
            {
                if (!observations.TryGetValue(card, out var observed))
                {
                    string signature = card.Id + ":" + card.Controller + ":" + card.Owner + ":" + (int)card.Location + ":" + card.Position + ":" +
                        card.Type + ":" + card.Level + ":" + card.Attack + ":" + card.Defense + ":" + card.Race + ":" + card.Attribute + ":" +
                        card.Disabled + ":" + card.Attacked + ":" + (int)card.LastLocation + ":" + card.IsCanRevive() + ":" + card.LinkMarker + ":" +
                        card.Data?.Name + ":" + card.Data?.Description;
                    if (!classes.TryGetValue(signature, out observed)) classes[signature] = observed = classes.Count + 1;
                    observations[card] = observed;
                }
                string uses = string.Join(",", state.ComboEffectInstances.Where(p => p.Item1 == card).Select(p => p.Item2).OrderBy(i => i));
                if (uses.Length > 0)
                {
                    string signature = "soft:" + observed + ":" + uses;
                    if (!classes.TryGetValue(signature, out observed)) classes[signature] = observed = classes.Count + 1;
                }
                return observed * 32 + (state.Acquired.Contains(card) ? 1 : 0) + (state.Normals.Contains(card) ? 2 : 0) +
                    (state.ProperlySummoned.Contains(card) ? 4 : 0) + (state.ComboInstances.Contains(card) ? 8 : 0) + (state.ActivatedSpells.Contains(card) ? 16 : 0);
            }
        }

        private static string DevelopmentStateKey(DevelopmentState state, DevelopmentKeys identities)
        {
            Func<ClientCard, int> key = c => identities.Key(state, c);
            return string.Join(",", state.Board.OrderBy(b => key(b.Card)).Select(b => key(b.Card) + ":" + b.Attack + ":" + b.Zone + ":" +
                b.OverlayCount + ":" + string.Join(".", b.OverlayCards.Select(c => c.Id)) + ":" + b.EffectsBlocked + ":" + b.DefensePosition + ":" + b.CannotAttack + ":" + b.Temporary + ":" + b.Fresh + ":" + b.FromExtra + ":" + b.ExtraSetcode + ":" + b.BanishOnLeave + ":" + b.ProjectedLevel + ":" + b.SynchroOnly + ":" + b.CannotTribute + ":" + b.FusionName)) + "/" +
                string.Join(",", state.Extra.Select(key).OrderBy(i => i)) + "/" + string.Join(",", state.Reserve.Select(key).OrderBy(i => i)) + "/" +
                string.Join(",", state.Enemy.Select(key).OrderBy(i => i)) + "/" + string.Join(",", state.Credited.OrderBy(i => i)) + "/" + state.HalqUsed + "/" +
                (state.PendingHalq == null ? 0 : key(state.PendingHalq)) + "/" +
                (state.PendingDeckTuner == null ? 0 : key(state.PendingDeckTuner)) + "/" + state.SynchroOnly + "/" + state.Life + "/" +
                state.DeckCount + "/" + (state.Addition == null ? 0 : RuntimeHelpers.GetHashCode(state.Addition)) + "/" + state.NormalUsed + "/" +
                string.Join(",", state.HandSummons.OrderBy(i => i)) + "/" + string.Join(",", state.Grave.Select(key).OrderBy(i => i)) + "/" +
                string.Join(",", state.SpecialSummons.OrderBy(i => i)) + "/" +
                state.AllowedAttributes + "/" +
                (state.PendingRecruit == null ? 0 : key(state.PendingRecruit)) + "/" +
                (state.PendingSearch == null ? 0 : key(state.PendingSearch)) + "/" + string.Join(",", state.Acquired.Select(key).OrderBy(i => i)) + "/" +
                string.Join(",", state.ResourceActions.Select(key).OrderBy(i => i)) + "/" + string.Join(",", state.ProperlySummoned.Select(key).OrderBy(i => i)) + "/" +
                string.Join(",", state.FusionActions.Select(key).OrderBy(i => i)) + "/" + string.Join(",", state.PendingFusionMaterials.Select(key)) + "/" +
                string.Join(",", state.ComboUsed.OrderBy(i => i)) + "/" + string.Join(",", state.ComboInstances.Select(key).OrderBy(i => i)) + "/" + state.XyzOnly + "/" +
                string.Join(",", state.PendingCombos.Select(p => key(p.Item1) + ":" + p.Item2.Offset)) + "/" +
                string.Join(",", state.PendingLevels.Select(p => key(p.Item1) + ":" + p.Item2.Description + ":" + key(p.Item3))) + "/" +
                string.Join(",", state.EffectSentOnly.Select(key).OrderBy(k => k)) + "/" +
                string.Join(",", state.Spells.OrderBy(p => p.Value).Select(p => key(p.Key) + ":" + p.Value)) + "/" +
                string.Join(",", state.LevelUses.OrderBy(k => k)) + "/" + state.Depth;
        }

        private void SearchDevelopment(List<DevelopmentState> frontier, SearchBudget budget, Action<DevelopmentState> visit)
        {
            var visited = new Dictionary<string, float>();
            var identities = new DevelopmentKeys();
            for (int depth = 0; depth < DevelopmentDepth * 3 && frontier.Count > 0; depth++)
            {
                var next = new List<DevelopmentState>();
                int width = depth == 0 && frontier.All(s => s.Addition != null) ? 64 : DevelopmentWidth;
                foreach (var state in DevelopmentFrontier(frontier, width))
                {
                    visit(state);
                    bool pending = state.PendingHalq != null || state.PendingDeckTuner != null || state.PendingRecruit != null ||
                        state.PendingSearch != null || state.PendingFusionMaterials.Count > 0 || state.PendingCombos.Count > 0 || state.PendingLevels.Count > 0;
                    if (budget.Exhausted || state.Depth >= DevelopmentDepth && !pending) continue;
                    var successors = EffectSuccessors(state, budget);
                    if (state.PendingHalq == null && state.PendingDeckTuner == null && state.PendingRecruit == null && state.PendingSearch == null && state.PendingFusionMaterials.Count == 0 && state.PendingCombos.Count == 0 && state.PendingLevels.Count == 0) successors = successors
                        .Concat(HandSuccessors(state, budget)).Concat(ComboSuccessors(state, budget)).Concat(LevelSuccessors(state, budget)).Concat(ResourceSuccessors(state, budget)).Concat(FusionSuccessors(state, budget)).Concat(ExtraSuccessors(state, state.Extra, budget, false));
                    foreach (var child in successors)
                    {
                        visit(child);
                        var key = DevelopmentStateKey(child, identities);
                        if (visited.TryGetValue(key, out float score) && score >= child.Score) continue;
                        visited[key] = child.Score;
                        next.Add(child);
                    }
                }
                frontier = DevelopmentFrontier(next, DevelopmentWidth).ToList();
                if (budget.Exhausted) break;
            }
            LastDevelopmentNodes = budget.Nodes;
        }

        private string DevelopmentFingerprint(IList<ClientCard> roots)
        {
            var text = new StringBuilder().Append(duel.Turn).Append('/').Append(duel.Player).Append('/').Append((int)duel.Phase)
                .Append('/').Append(Bot.LifePoints).Append('/').Append(Enemy.LifePoints).Append('/').Append(Bot.Deck.Count)
                .Append('/').Append(duel.MainPhase?.CanBattlePhase);
            text.Append('/').Append(duel.IsNewRule).Append('/').Append(duel.IsNewRule2020);
            foreach (var c in Bot.GetMonsters().Concat(Bot.GetSpells()).Concat(Enemy.GetMonsters()).Concat(Enemy.GetSpells()).Concat(Bot.Hand)
                .Concat(Bot.Graveyard).Concat(Bot.Banished).Concat(Bot.ExtraDeck).Concat(Bot.Deck.Where(c => c.Id != 0)))
                text.Append('|').Append(RuntimeHelpers.GetHashCode(c)).Append(':').Append(c.Id).Append(':').Append((int)c.Location)
                    .Append(':').Append(c.Controller).Append(':').Append(c.Owner).Append(':').Append(c.Sequence).Append(':').Append(c.Position).Append(':').Append(c.Type).Append(':').Append(c.Level)
                    .Append(':').Append(c.Race).Append(':').Append(c.Attribute).Append(':').Append(c.Attack).Append(':').Append(c.Defense).Append(':').Append(c.Disabled)
                    .Append(':').Append(c.Attacked).Append(':').Append((int)c.LastLocation).Append(':').Append(c.LinkMarker)
                    .Append(':').Append(string.Join(",", c.Overlays));
            text.Append(" roots ");
            foreach (var c in roots.OrderBy(c => c.Id)) text.Append(RuntimeHelpers.GetHashCode(c)).Append(',');
            text.Append(" effects ").Append(string.Join(",", usedDevelopmentEffects.OrderBy(i => i))).Append('/').Append(developmentSynchroOnly);
            text.Append(" level uses ").Append(string.Join(",", usedLevelEffects.OrderBy(k => k)));
            text.Append(" hand summons ").Append(string.Join(",", usedHandSummons.OrderBy(i => i)));
            text.Append(" special summons ").Append(string.Join(",", usedSpecialSummons.OrderBy(i => i)));
            text.Append(" allowed attributes ").Append(developmentAllowedAttributes);
            text.Append(" arrivals ").Append(string.Join(",", summonedThisTurn.Select(RuntimeHelpers.GetHashCode).OrderBy(i => i)));
            text.Append(" normal spent ").Append(normalSummonSpent);
            text.Append(" combo ").Append(string.Join(",", usedComboEffects.OrderBy(i => i))).Append('/').Append(developmentXyzOnly);
            text.Append(" fusion names ").Append(string.Join(",", liveFusionNames.Select(p => RuntimeHelpers.GetHashCode(p.Key) + ":" + p.Value)));
            text.Append(" instance uses ").Append(string.Join(",", usedComboInstances.Select(RuntimeHelpers.GetHashCode).OrderBy(i => i)));
            text.Append(" effect uses ").Append(string.Join(",", usedComboEffectInstances.Select(p => RuntimeHelpers.GetHashCode(p.Item1) + ":" + p.Item2).OrderBy(s => s)));
            text.Append(" normals ");
            if (duel.MainPhase != null) foreach (var c in duel.MainPhase.SummonableCards) text.Append(RuntimeHelpers.GetHashCode(c)).Append(',');
            if (duel.MainPhase != null) foreach (var c in duel.MainPhase.ActivableCards) text.Append(RuntimeHelpers.GetHashCode(c)).Append(',');
            return text.ToString();
        }

        internal ExtraPlan PlanDevelopment(IList<ClientCard> offered)
        {
            var roots = offered.Where(c => c.Location == CardLocation.Extra && ExtraNotCancelled(c)).Distinct().OrderBy(c => c.Id).ToList();
            var key = DevelopmentFingerprint(roots);
            if (key == developmentCacheKey) return developmentCache;
            var initial = InitialDevelopment(roots); float baseline = TerminalValue(initial), best = baseline + 100;
            var budget = new SearchBudget(); ExtraPlan result = null;
            var seeds = ExtraSuccessors(initial, roots, budget, true).ToList();
            Action<DevelopmentState> consider = state =>
            {
                if (state.First != null && state.Score > best + .01f)
                {
                    best = state.Score; result = state.First;
                    result.Gain = best - baseline;
                }
            };
            foreach (var seed in seeds) consider(seed);
            SearchDevelopment(seeds, budget, consider);
            foreach (var root in roots.Where(c => duel.MainPhase?.SpecialSummonableCards.Contains(c) == true &&
                !seeds.Any(s => s.First?.Destination == c)))
            {
                var fallback = PlanCoreOfferedExtra(root);
                if (fallback != null) fallback.Gain -= DrawPenalty(CardLocation.Extra);
                if (fallback != null && fallback.Gain > best - baseline)
                {
                    best = baseline + fallback.Gain;
                    result = fallback;
                }
            }
            developmentCacheKey = key; developmentCache = result;
            return result;
        }

        internal Dictionary<ClientCard, float> DevelopmentBonuses(IList<ClientCard> offered)
        {
            var result = offered.Distinct().ToDictionary(c => c, _ => 0f);
            if (offered.Count == 0 || Bot.GetMonsterCount() >= 6 || Bot.ExtraDeck.Count == 0) return result;
            var initial = InitialDevelopment(new ClientCard[0]);
            var bases = new Dictionary<ClientCard, float>(); var seeds = new List<DevelopmentState>();
            foreach (var card in offered.Distinct().OrderBy(c => c.Id))
            {
                if (!Has(card, CardType.Monster) || card.Controller != 0 || card.Location == CardLocation.MonsterZone) continue;
                int zone = DevelopmentPlace(initial, card, card.Location == CardLocation.Extra);
                if (zone < 0) continue;
                var seed = CopyDevelopment(initial); seed.Addition = card; seed.Reserve.Remove(card); seed.Extra.Remove(card);
                // Selection prompts can run inside a chain with a stale MainPhase
                // snapshot. Only the idle-command planner may spend that allowance.
                seed.NormalUsed = true;
                seed.Board.Add(new Body { Card = card, Attack = Attack(card), Zone = zone, Fresh = true, FromExtra = card.Location == CardLocation.Extra });
                QueueLevelArrival(seed, card, false);
                seed.Score = TerminalValue(seed); bases[card] = seed.Score; seeds.Add(seed);
            }
            SearchDevelopment(seeds, new SearchBudget(), state =>
            {
                if (state.Addition != null) result[state.Addition] = Math.Max(result[state.Addition], state.Score - bases[state.Addition]);
            });
            return result;
        }
    }
}
