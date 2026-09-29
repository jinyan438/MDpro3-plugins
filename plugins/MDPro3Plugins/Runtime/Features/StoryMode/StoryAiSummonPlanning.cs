using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        internal sealed class SummonPlan
        {
            internal ClientCard Card;
            internal bool Normal;
            internal bool Activation;
            internal int Description;
            internal ExtraPlan Extra;
            internal ClientCard Target, Cost;
            internal int TargetHint;
            internal bool Combo;
            internal LevelPlan LevelChange;
            internal List<ComboSelection> Choices;
            internal float Gain;
        }

        private sealed class HandProcedure
        {
            internal bool Empty, EmptyCards, NoEffect, EnemyRequired, Once;
            internal int Level;
            internal MaterialRequirement Requirement;
        }

        private readonly HashSet<int> usedHandSummons = new HashSet<int>();
        private readonly Dictionary<int, HandProcedure> handProcedures = new Dictionary<int, HandProcedure>();
        private string summonCacheKey;
        private SummonPlan summonCache;

        private static bool SimpleNormal(ClientCard card) => card.Location == CardLocation.Hand && SimpleNormalBody(card);
        private static bool TributeNormal(ClientCard card) => card.Location == CardLocation.Hand && card.Controller == 0 &&
            Has(card, CardType.Monster) && !Has(card, CardType.SpSummon | CardType.Ritual | CardType.Fusion | CardType.Link | CardType.Xyz | CardType.Synchro) &&
            Level(card) >= 5 && !IsHandTrap(card) && !Exodia(card);
        private static bool SimpleNormalBody(ClientCard card) => card.Controller == 0 &&
            Has(card, CardType.Monster) && !Has(card, CardType.SpSummon | CardType.Ritual | CardType.Fusion | CardType.Link | CardType.Xyz | CardType.Synchro) &&
            Level(card) <= 4 && !IsHandTrap(card) && !Exodia(card);

        // Deliberately parse a complete, cost-free self-summon clause. A substring such
        // as "Special Summon" is not enough to invent a playable extender. Unknown
        // costs, summon locks, trigger windows and compound conditions stay with the core.
        private HandProcedure ReadHandProcedure(ClientCard card)
        {
            if (handProcedures.TryGetValue(card.Id, out var cached)) return cached;
            string text = card.Data?.Description ?? "";
            HandProcedure result = null;
            string restrictions = Regex.Replace(text,
                @"You can only Special Summon (?:this card|""[^""]+"") once per turn\.", "", RegexOptions.IgnoreCase);
            if (!Regex.IsMatch(restrictions,
                @"cannot Special Summon|can only Special Summon|cannot Normal Summon|不能特殊召唤|不能通常召唤|不是[^。\n]*不能|只能特殊召唤|特殊召唤[^。\n]*限制",
                RegexOptions.IgnoreCase))
            {
                string[] clauses = Regex.Split(restrictions, @"[\r\n①②③④⑤⑥]");
                foreach (string raw in clauses)
                {
                    string clause = raw.Trim().TrimStart(':', '：').Trim();
                    var en = Regex.Match(clause,
                        @"^If (?<condition>you control [^:;,\.]+|your opponent controls a monster and you control no monsters)(?:,|:) (?:you can )?Special Summon this card \(from your hand\)\.?$",
                        RegexOptions.IgnoreCase);
                    var zh = Regex.Match(clause,
                        @"^(?<condition>自己场上[^。]+|对方场上有怪兽存在，自己场上没有怪兽存在|只有对方场上才有怪兽存在)的场合(?:[，：]|才能发动。)(?:这张卡可以从手卡特殊召唤|这张卡从手卡特殊召唤|可以发动[。]?这张卡从手卡特殊召唤)[。]?$",
                        RegexOptions.IgnoreCase);
                    var match = en.Success ? en : zh;
                    if (!match.Success) continue;
                    string condition = match.Groups["condition"].Value;
                    var parsed = new HandProcedure();
                    if (Regex.IsMatch(condition, @"^(you control no monsters|自己场上没有怪兽存在|自己场上没有怪兽)$", RegexOptions.IgnoreCase))
                        parsed.Empty = true;
                    else if (Regex.IsMatch(condition, @"^(your opponent controls a monster and you control no monsters|对方场上有怪兽存在，自己场上没有怪兽存在|只有对方场上才有怪兽存在)$", RegexOptions.IgnoreCase))
                        parsed.Empty = parsed.EnemyRequired = true;
                    else if (condition == "自己场上没有卡存在") parsed.Empty = parsed.EmptyCards = true;
                    else if (Regex.IsMatch(condition, @"^(you control no Effect Monsters|自己场上没有效果怪兽存在)$", RegexOptions.IgnoreCase))
                        parsed.NoEffect = true;
                    else
                    {
                        string kind = Regex.Replace(condition, @"^you control (?:an? )?|^自己场上有|存在$", "", RegexOptions.IgnoreCase).Trim();
                        var level = Regex.Match(kind, @"^Level (?<n>[1-9]|1[0-2]) |^(?<n>[1-9]|1[0-2])星", RegexOptions.IgnoreCase);
                        if (level.Success)
                        {
                            parsed.Level = int.Parse(level.Groups["n"].Value);
                            kind = kind.Substring(level.Length);
                        }
                        if (Regex.IsMatch(kind, @"^(monsters?|怪兽)$", RegexOptions.IgnoreCase))
                            parsed.Requirement = new MaterialRequirement();
                        else parsed.Requirement = ParseMaterial(kind);
                        if (parsed.Requirement == null) continue;
                    }
                    parsed.Once = Regex.IsMatch(text, @"once per turn|1回合(?:各)?只能|一回合(?:各)?只能", RegexOptions.IgnoreCase);
                    result = parsed;
                    break;
                }
            }
            if (handProcedures.Count < 4096) handProcedures[card.Id] = result;
            return result;
        }

        private bool CanDevelopFromHand(DevelopmentState state, ClientCard card)
        {
            if (!InDevelopmentHand(state, card) || card.Controller != 0 || !Has(card, CardType.Monster) ||
                IsHandTrap(card) || Exodia(card)) return false;
            var procedure = ReadHandProcedure(card);
            if (procedure == null || procedure.Once && state.HandSummons.Contains(card.Id)) return false;
            if (procedure.Empty && state.Board.Count != 0 || procedure.EnemyRequired && state.Enemy.Count == 0) return false;
            if (procedure.EmptyCards && Bot.GetSpellCount() != 0) return false;
            if (procedure.NoEffect && state.Board.Any(b => !b.Fresh && b.Card.IsFacedown() || Has(b.Card, CardType.Effect))) return false;
            if (procedure.Requirement != null && !state.Board.Any(b => (b.Fresh || b.Card.IsFaceup()) &&
                procedure.Requirement.Matches(b.Card) && (procedure.Level == 0 || Level(b.Card) == procedure.Level))) return false;
            // An unmodelled live summon restriction must never become permission for
            // a speculative hand summon. Current core-offered actions remain usable.
            if (DevelopmentSummonsBlocked(state)) return false;
            return true;
        }

        internal void NoteHandSummons(IEnumerable<ClientCard> cards)
        {
            foreach (var card in cards.Where(c => c.Controller == 0))
            {
                summonedThisTurn.Add(card);
                if (Facts(card).SummonOnceKey != 0) usedSpecialSummons.Add(Facts(card).SummonOnceKey);
            }
            foreach (var card in cards.Where(c => c.Controller == 0 && c.LastLocation == CardLocation.Hand))
                usedHandSummons.Add(card.Id);
            // Both caches also change when the live board/hand changes. Clear the
            // unified cache explicitly for a same-name summon followed by removal.
            summonCacheKey = null;
        }

        private float HandCommitment(ClientCard card) => Math.Max(500, Math.Min(1400, Keep(card) * .27f));

        internal bool ModelledHandSummon(ClientCard card, int description) => card.Location == CardLocation.Hand &&
            Has(card, CardType.Monster) && description == card.Id * 16 &&
            Regex.Matches(card.Data?.Description ?? "", @"[①-⑳]|[:：]").Count <= 2 && ReadHandProcedure(card) != null;

        // A small tie-break between equivalent end boards, not a probability model
        // of an unseen hand trap. Prefer a working negate before the next extension.
        private float SummonExposure(DevelopmentState state) => state.Board.Any(b =>
            !b.EffectsBlocked && !b.Card.IsDisabled() && (b.Fresh || b.Card.IsFaceup()) && LiveInteraction(b.Card) &&
            Facts(b.Card).Effects.Any(f => InteractionFact(f) && f.Purpose == StoryLuckyExecutor.EffectPurpose.Negate && !f.Narrow &&
                (f.AttackCost <= 0 || b.Attack >= f.AttackCost) && (!f.LinkCounters || state.Grave.Any(c => Has(c, CardType.Link))))) ? 0 : 2;

        private DevelopmentState AddDevelopmentBody(DevelopmentState state, ClientCard card, bool normal, bool root, SearchBudget budget)
        {
            if (!normal && !SpecialAttributeAllowed(state, card)) return null;
            int zone = DevelopmentPlace(state, card, false);
            if (zone < 0 || budget.Exhausted) return null;
            var next = CopyDevelopment(state);
            bool inHand = InDevelopmentHand(state, card);
            next.Reserve.Remove(card); next.Acquired.Remove(card); next.Grave.Remove(card); next.NormalUsed |= normal; next.Depth++;
            if (inHand) next.Credit -= HandCommitment(card);
            next.Credit -= DrawPenalty(inHand ? CardLocation.Hand : card.Location, normal);
            if (!normal) next.Credit -= SummonExposure(state);
            if (!normal) next.HandSummons.Add(card.Id);
            next.Board.Add(new Body { Card = card, Attack = Attack(card), Zone = zone, Fresh = true });
            QueueLevelArrival(next, card, normal);
            var recruit = !card.IsDisabled() ? ReadRecruitTrigger(card) : null;
            if (!state.Credited.Contains(card.Id) && recruit != null && (normal ? recruit.Normal : recruit.Special))
                next.PendingRecruit = card;
            QueueDevelopmentSearch(next, card, normal);
            QueueComboArrival(next, card, normal);
            // Only a small potential for unknown summon triggers; never create an
            // unknown searched/drawn body. Known repeatable effects expand separately.
            if (next.Credited.Add(card.Id) && next.PendingRecruit == null && next.PendingSearch == null && next.PendingCombos.Count == 0 && !ComboProfiles(card).Any() && (Roles(card) & Role.Starter) != 0)
                next.Credit += Math.Min(1000, ImmediatePayoff(card) * .7f + 250);
            if (root) next.FirstSummon = new SummonPlan { Card = card, Normal = normal };
            next.Score = TerminalValue(next); budget.Nodes++;
            return next;
        }

        private IEnumerable<DevelopmentState> TributeNormalSuccessors(DevelopmentState state, ClientCard card, bool root, SearchBudget budget)
        {
            if (state.NormalUsed || !TributeNormal(card) || !InDevelopmentHand(state, card)) yield break;
            int count = Level(card) >= 7 ? 2 : 1;
            var available = state.Board.Where(b => b.Card.Controller == 0 && !b.CannotTribute).ToList();
            if (available.Count < count || available.Count > 7) yield break;
            for (int mask = 1; mask < 1 << available.Count; mask++)
            {
                if (budget.Exhausted) yield break;
                var tributes = available.Where((_, i) => (mask & 1 << i) != 0).ToList();
                if (tributes.Count != count) continue;
                var paid = CopyDevelopment(state);
                foreach (var body in tributes)
                {
                    SpendComboCard(paid, body.Card);
                    QueueTributeTriggers(paid, body.Card);
                }
                var next = AddDevelopmentBody(paid, card, true, root, budget);
                if (next == null) continue;
                if (root) next.FirstSummon.Choices = tributes.Select(b => new ComboSelection
                { Card = b.Card, Hint = HintMsg.Release, Location = CardLocation.MonsterZone }).ToList();
                yield return next;
            }
        }

        private IEnumerable<DevelopmentState> HandSuccessors(DevelopmentState state, SearchBudget budget)
        {
            foreach (var card in state.Reserve.Where(c => InDevelopmentHand(state, c)).OrderBy(c => c.Id))
            {
                if (budget.Exhausted) yield break;
                // Do not use a currently offered special procedure after its conditions
                // changed. Re-evaluate only the explicitly modelled hand procedures.
                if (CanDevelopFromHand(state, card))
                {
                    var next = AddDevelopmentBody(state, card, false, false, budget);
                    if (next != null) yield return next;
                }
                if (!state.NormalUsed && (state.Normals.Contains(card) || AcquiredNormalAllowed(state, card)))
                {
                    if (TributeNormal(card))
                    {
                        foreach (var next in TributeNormalSuccessors(state, card, false, budget)) yield return next;
                    }
                    else
                    {
                        var next = AddDevelopmentBody(state, card, true, false, budget);
                        if (next != null) yield return next;
                    }
                }
            }
        }

        private static string DevelopmentFirstKey(DevelopmentState state)
        {
            return (state.FirstSummon == null ? 0 : RuntimeHelpers.GetHashCode(state.FirstSummon.Card)) + ":" +
                (state.FirstSummon?.Normal == true) + ":" + (state.FirstSummon?.Description ?? 0) + ":" +
                (state.Addition == null ? 0 : RuntimeHelpers.GetHashCode(state.Addition));
        }

        // Keep at least one promising continuation for each first decision before
        // spending the rest of the beam on variants of the same tempting starter.
        private static IEnumerable<DevelopmentState> DevelopmentFrontier(IEnumerable<DevelopmentState> states, int width)
        {
            // Unresolved, modelled triggers are work still to do, not a weak final
            // board. This affects beam allocation ONLY; no unearned card or payoff
            // is added to the terminal score used to commit an actual action.
            var ordered = states.OrderByDescending(s => s.Score +
                (s.PendingSearch != null ? 1100 : 0) + (s.PendingRecruit != null ? 1100 : 0) +
                (s.PendingHalq != null || s.PendingDeckTuner != null ? 1600 : 0) +
                Math.Min(3, s.PendingCombos.Count + s.PendingFusionMaterials.Count) * 850).ToList();
            var groups = ordered.GroupBy(DevelopmentFirstKey).Select(g => g.ToList()).ToList();
            var selected = new List<DevelopmentState>();
            var seenContinuations = new HashSet<string>();
            var identities = new DevelopmentKeys();
            // First give every root one candidate. This preserves starter
            // diversity even when one route has a temporarily lower score.
            foreach (var group in groups)
            {
                if (selected.Count >= width) break;
                var first = group.First();
                selected.Add(first);
                seenContinuations.Add(DevelopmentFirstKey(first) + "/" + DevelopmentStateKey(first, identities));
            }
            // Spend remaining slots in score order, but only when the state adds
            // a distinct resource/board continuation under that same root.
            foreach (var state in ordered)
            {
                if (selected.Count >= width) break;
                if (selected.Contains(state)) continue;
                var key = DevelopmentFirstKey(state) + "/" + DevelopmentStateKey(state, identities);
                if (!seenContinuations.Add(key)) continue;
                selected.Add(state);
            }
            return selected;
        }

        internal SummonPlan PlanMainSummons(IList<ClientCard> normals, IList<ClientCard> specials) =>
            PlanAllSummons(normals, specials, new Dictionary<ClientCard, int>());

        internal SummonPlan PlanAllSummons(IList<ClientCard> normals, IList<ClientCard> specials, IDictionary<ClientCard, int> activations)
            => PlanSummonActions(normals, specials, activations.ToList());

        internal SummonPlan PlanSummonActions(IList<ClientCard> normals, IList<ClientCard> specials, IList<KeyValuePair<ClientCard, int>> activations)
        {
            var roots = specials.Where(c => c.Location == CardLocation.Extra && ExtraNotCancelled(c)).Distinct().ToList();
            string key = DevelopmentFingerprint(roots) + " main " + string.Join(",", normals.Select(RuntimeHelpers.GetHashCode)) + "/" +
                string.Join(",", specials.Select(RuntimeHelpers.GetHashCode)) + "/" +
                string.Join(",", activations.Select(p => RuntimeHelpers.GetHashCode(p.Key) + ":" + p.Value));
            if (summonCacheKey == key) return summonCache;
            var initial = InitialDevelopment(roots);
            initial.Normals = normals.Where(c => SimpleNormal(c) || TributeNormal(c)).Distinct().ToList();
            initial.ResourceActions = initial.ResourceActions.Concat(activations.Where(p => ModelledResourceAction(p.Key, p.Value)).Select(p => p.Key)).Distinct().ToList();
            initial.FusionActions = initial.FusionActions.Concat(activations.Where(p => ModelledFusionAction(p.Key, p.Value)).Select(p => p.Key)).Distinct().ToList();
            float baseline = TerminalValue(initial), best = baseline + 100;
            var budget = new SearchBudget();
            var seeds = new List<DevelopmentState>();
            // Seed every legal action before letting any one branch use the budget.
            foreach (var card in initial.Normals.OrderBy(c => c.Id))
            {
                if (TributeNormal(card)) seeds.AddRange(TributeNormalSuccessors(initial, card, true, budget));
                else
                {
                    var next = AddDevelopmentBody(initial, card, true, true, budget);
                    if (next != null) seeds.Add(next);
                }
            }
            foreach (var card in specials.Where(c => c.Location != CardLocation.Extra).Distinct().OrderBy(c => c.Id))
            {
                if (ModelledDirectCost(card)) { seeds.AddRange(ComboRoots(initial, card, 0, true, budget)); continue; }
                var next = AddDevelopmentBody(initial, card, false, true, budget);
                if (next != null) seeds.Add(next);
            }
            foreach (var pair in activations.OrderBy(p => p.Key.Id))
            {
                if (ModelledComboAction(pair.Key, pair.Value))
                {
                    seeds.AddRange(ComboRoots(initial, pair.Key, pair.Value, false, budget)); continue;
                }
                if (ModelledFusionAction(pair.Key, pair.Value))
                {
                    seeds.AddRange(FusionChoices(initial, pair.Key, budget, true, pair.Value));
                    continue;
                }
                if (ModelledResourceAction(pair.Key, pair.Value))
                {
                    seeds.AddRange(ResourceChoices(initial, pair.Key, ReadResourceEffect(pair.Key), budget, true, pair.Value));
                    continue;
                }
                var next = AddDevelopmentBody(initial, pair.Key, false, true, budget);
                if (next == null) continue;
                next.FirstSummon.Activation = true; next.FirstSummon.Description = pair.Value;
                seeds.Add(next);
            }
            var extras = ExtraSuccessors(initial, roots, budget, true).ToList();
            seeds.AddRange(extras);
            SummonPlan result = null; bool converted = false;
            Action<DevelopmentState> consider = state =>
            {
                if (state.FirstSummon != null && state.Score > best + .01f)
                {
                    best = state.Score;
                    result = state.FirstSummon; result.Gain = best - baseline;
                    converted = state.Extra.Count < initial.Extra.Count;
                    if (result.Extra != null) result.Extra.Gain = result.Gain;
                }
            };
            foreach (var seed in seeds) consider(seed);
            SearchDevelopment(seeds, budget, consider);
            foreach (var root in roots.Where(c => !extras.Any(s => s.First?.Destination == c)))
            {
                var fallback = PlanCoreOfferedExtra(root);
                if (fallback == null) continue;
                fallback.Gain -= DrawPenalty(CardLocation.Extra);
                if (fallback.Gain <= best - baseline) continue;
                best = baseline + fallback.Gain;
                result = new SummonPlan { Card = root, Extra = fallback, Gain = fallback.Gain };
                converted = true;
            }
            // Without a concrete extra-deck conversion, retain the established
            // starter/tribute/effect heuristics rather than flatten them into ATK.
            if (!converted && result?.Combo != true && !(result?.Normal == true && TributeNormal(result.Card))) result = null;
            summonCacheKey = key; summonCache = result;
            return result;
        }
    }
}
