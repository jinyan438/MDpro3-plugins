using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        // Small, script-verified action descriptions, shared by every story deck.
        // They describe costs and resource transitions, never a prescribed combo order.
        private enum ComboKind { Self, Search, Recruit, Send, DrawDiscard, SearchPair, FusionName, Normal, PlaceSpell }
        private enum ComboCost { None, Self, SelfAndTuner, SelfDiscard, Discard, BanishSelf, BanishSelfDiscard, Tribute, BanishField, ReturnField, SendDeck, Detach, BanishResource }
        private sealed class ComboEffect
        {
            internal int Offset, InstanceKey, Life, Level, CostHint;
            internal int KeyCode, Attack = -1, LevelFromTarget, ConditionCount = 1, ExtraSetcode, PreferredOption, DeclineYesNo;
            internal CardLocation From, TargetFrom;
            internal CardLocation CostFrom, ConditionFrom, RevealFrom, FollowupFrom;
            internal ComboKind Kind;
            internal ComboCost Cost;
            internal Func<ClientCard, bool> Filter = c => true, CostFilter, ConditionFilter, OverlayFilter, RevealFilter, FollowupFilter;
            internal int[] SearchSets = new int[0];
            internal bool Once = true, Direct, ActivationOnly, DefaultDescription, SynchroOnly, XyzOnly, BodySynchroOnly, BanishOnLeave, Defense, SoftOnce;
            internal bool SummonTrigger, MaterialTrigger, TributeTrigger, DetachTrigger, Mill, Disabled;
            internal bool ExtraSummonTrigger, DuelOnce, Temporary, CannotAttack;
            internal bool ExplicitDescription;
            internal bool NormalTrigger, SpecialTrigger, SentEffectTrigger, FusionMaterialTrigger, GenericTrigger;
            internal bool CopyTributeAttack, CannotTribute, ConditionExact, ActivationTurnOnly, AfterDiscard, MillNeedsGrave, ReturnNeedsHand;
            internal bool Trigger => SummonTrigger || MaterialTrigger || TributeTrigger || DetachTrigger || SentEffectTrigger || FusionMaterialTrigger;
        }
        internal sealed class ComboSelection
        {
            internal ClientCard Card;
            internal int Hint;
            internal CardLocation Location;
        }
        private readonly HashSet<int> usedComboEffects = new HashSet<int>();
        private readonly HashSet<int> usedDuelComboEffects = new HashSet<int>();
        private readonly Dictionary<ClientCard, int> spellActivationTurns = new Dictionary<ClientCard, int>();
        private readonly HashSet<ClientCard> usedComboInstances = new HashSet<ClientCard>();
        private readonly HashSet<(ClientCard, int)> usedComboEffectInstances = new HashSet<(ClientCard, int)>();
        private readonly Dictionary<ClientCard, int> liveFusionNames = new Dictionary<ClientCard, int>();
        private readonly Dictionary<ClientCard, int> pendingFusionNames = new Dictionary<ClientCard, int>();
        private bool developmentXyzOnly;
        private readonly HashSet<ClientCard> comboSynchroBodies = new HashSet<ClientCard>();
        private readonly Dictionary<ClientCard, int> comboUntributableUntil = new Dictionary<ClientCard, int>();
        private static bool SetAny(ClientCard card, params int[] sets) => sets.Any(card.HasSetcode);
        private static readonly Dictionary<int, ComboEffect[]> comboEffects = MakeComboEffects();

        private static bool MentionsCode(ClientCard card, int code)
        {
            if (Facts(card).ListedCodes.Contains(code)) return true;
            var name = YGOSharp.OCGWrapper.NamedCard.Get(code)?.Name;
            return !string.IsNullOrEmpty(name) && (card.Data?.Description ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CanBeNonTuner(ClientCard card) => !card.IsDisabled() && (Facts(card).FlexibleTuner || System.Text.RegularExpressions.Regex.IsMatch(
            card.Data?.Description ?? "", @"(?:当作|作为)调整以外的怪兽|treated as a non-Tuner", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        private static IEnumerable<ComboEffect> ComboProfiles(ClientCard card) => comboEffects.TryGetValue(CardIdentity(card), out var effects)
            ? effects : Enumerable.Empty<ComboEffect>();
        private static int ComboKey(ClientCard card, ComboEffect effect) => effect.KeyCode != 0 ? effect.KeyCode : CardIdentity(card) * 16 + effect.Offset;
        private static bool ComboDescription(ClientCard card, ComboEffect effect, int description) =>
            effect.Kind == ComboKind.PlaceSpell ? description == (Has(card, CardType.Pendulum) ? 1160 : 0) :

            effect.ActivationOnly ? description == 0 :
            description == 0 && effect.DefaultDescription ||
            description == card.Id * 16 + effect.Offset || description == CardIdentity(card) * 16 + effect.Offset ||
            // SelectEffectYn normalizes the core's generic trigger marker 221/0 to -1.
            // These profiles have one verified trigger in this zone; never map an
            // unspecified marker to an ignition effect on a multi-effect card.
            (description == -1 || description == 0 && !ComboProfiles(card).Any(e => e.DefaultDescription && !e.Trigger && (e.From & card.Location) != 0)) &&
                effect.GenericTrigger && (effect.From & card.Location) != 0 ||
            description == 0 && effect.Offset == 0 && Has(card, CardType.Spell) && !effect.ExplicitDescription;
        internal bool ModelledComboAction(ClientCard card, int description) => ModelledLevelAction(card, description) || ComboProfiles(card).Any(e => !e.Direct && !e.Trigger &&
            (e.From & card.Location) != 0 && ComboDescription(card, e, description));
        internal bool ModelledDirectCost(ClientCard card) => ComboProfiles(card).Any(e => e.Direct);
        internal int ComboOption(ClientCard card, int description) => ComboProfiles(card)
            .FirstOrDefault(e => ComboDescription(card, e, description))?.PreferredOption ?? 0;
        internal bool DeclineComboAlternative(ClientCard card, int description, int prompt) => ComboProfiles(card)
            .Any(e => ComboDescription(card, e, description) && e.DeclineYesNo != 0 && e.DeclineYesNo == prompt);
        internal bool IsReturnCombo(ClientCard card, int description) => ComboProfiles(card)
            .Any(e => e.Cost == ComboCost.ReturnField && ComboDescription(card, e, description));
        internal bool ReturnComboTargetAllowed(ClientCard source, int description, ClientCard target) => target != source &&
            target.Controller == 0 && target.IsFaceup() && ComboProfiles(source).Any(e => e.Cost == ComboCost.ReturnField &&
                ComboDescription(source, e, description) && (!e.ReturnNeedsHand || !target.IsExtraCard()) &&
                (e.CostFrom & target.Location) != 0 && (e.CostFilter == null || e.CostFilter(target)));
        internal bool CanPayReturnCombo(ClientCard card, int description) => Bot.GetMonsters().Concat(Bot.GetSpells())
            .Any(c => ReturnComboTargetAllowed(card, description, c));
        internal bool ModelledComboTrigger(ClientCard card, int description) => ComboProfiles(card).Any(e => e.Trigger &&
            (e.From & card.Location) != 0 && ComboDescription(card, e, description));

        private CardLocation ComboLocation(DevelopmentState state, ClientCard card) => state.Board.Any(b => b.Card == card) ? CardLocation.MonsterZone :
            InDevelopmentHand(state, card) ? CardLocation.Hand : state.Grave.Contains(card) ? CardLocation.Grave :
            state.Reserve.Contains(card) && !state.Acquired.Contains(card) ? CardLocation.Deck : state.Extra.Contains(card) ? CardLocation.Extra :
            state.Spells.ContainsKey(card) ? CardLocation.SpellZone : 0;

        private ClientCard ComboView(DevelopmentState state, ClientCard card)
        {
            var body = state.Board.FirstOrDefault(b => b.Card == card);
            if (body?.MaterialView != null) return body.MaterialView;
            var location = ComboLocation(state, card);
            if (location == card.Location && body?.Fresh != true) return card;
            var view = ProjectComboMaterial(card, body?.ProjectedLevel > 0 ? body.ProjectedLevel : Level(card), disabled: body?.EffectsBlocked == true);
            view.Location = location;
            return view;
        }

        private bool ComboReady(DevelopmentState state, ClientCard source, ComboEffect effect)
        {
            var location = ComboLocation(state, source);
            if ((effect.From & location) == 0 || effect.Once && state.ComboUsed.Contains(ComboKey(source, effect)) ||
                effect.SoftOnce && state.ComboEffectInstances.Contains((source, effect.InstanceKey))) return false;
            var body = state.Board.FirstOrDefault(b => b.Card == source);
            if (body != null && (body.EffectsBlocked || source.IsDisabled() || !body.Fresh && source.IsFacedown())) return false;
            if (state.Spells.ContainsKey(source) && source.Location != CardLocation.Hand && !state.Acquired.Contains(source) &&
                (source.IsDisabled() || source.IsFacedown())) return false;
            if ((effect.Kind == ComboKind.Search || effect.Kind == ComboKind.SearchPair || effect.Kind == ComboKind.DrawDiscard) && DrawLocked ||
                effect.Life > 0 && state.Life <= effect.Life || effect.Kind == ComboKind.DrawDiscard && state.DeckCount <= 0) return false;
            if ((effect.Kind == ComboKind.Self || effect.Kind == ComboKind.Recruit) && DevelopmentSummonsBlocked(state)) return false;
            if (effect.Mill && state.DeckCount <= 0 || effect.MillNeedsGrave && GraveReplacementActive()) return false;
            if (effect.ActivationTurnOnly && !state.ActivatedSpells.Contains(source)) return false;
            if (effect.OverlayFilter != null && (body == null || !body.OverlayCards.Any(effect.OverlayFilter))) return false;
            if (effect.RevealFilter != null && !ComboPool(state, effect.RevealFrom).Any(c => effect.RevealFilter(ComboView(state, c)))) return false;
            if (effect.ConditionFilter != null)
            {
                int count = ComboPool(state, effect.ConditionFrom).Count(c =>
                    (ComboLocation(state, c) != CardLocation.MonsterZone || state.Board.Any(b => b.Card == c && (b.Fresh || c.IsFaceup()))) &&
                    effect.ConditionFilter(ComboView(state, c)));
                if (effect.ConditionExact ? count != effect.ConditionCount : count < effect.ConditionCount) return false;
            }
            return true;
        }

        private IEnumerable<ClientCard> ComboPool(DevelopmentState state, CardLocation locations) =>
            state.Reserve.Where(c => ((locations & (InDevelopmentHand(state, c) ? CardLocation.Hand : CardLocation.Deck)) != 0))
            .Concat((locations & CardLocation.Grave) != 0 ? state.Grave : Enumerable.Empty<ClientCard>())
            .Concat((locations & CardLocation.Extra) != 0 ? state.Extra : Enumerable.Empty<ClientCard>())
            .Concat((locations & CardLocation.MonsterZone) != 0 ? state.Board.Where(b => b.Fresh || b.Card.IsFaceup()).Select(b => b.Card) : Enumerable.Empty<ClientCard>()).Distinct();

        private void SpendComboCard(DevelopmentState state, ClientCard card, bool banish = false, bool bounce = false)
        {
            bool hand = InDevelopmentHand(state, card);
            var body = state.Board.FirstOrDefault(b => b.Card == card);
            if (hand) state.Credit -= HandCommitment(card) + (IsHandTrap(card) ? 1600 : 0);
            else if (body == null && state.Reserve.Contains(card)) state.DeckCount--;
            state.Reserve.Remove(card); state.Acquired.Remove(card); state.Grave.Remove(card);
            state.Extra.Remove(card);
            state.Board.RemoveAll(b => b.Card == card);
            if (state.Spells.Remove(card)) state.ComboInstances.Remove(card);
            state.ActivatedSpells.Remove(card);
            if (body != null) state.ComboInstances.Remove(card);
            if (body != null || bounce) state.ComboEffectInstances.RemoveWhere(p => p.Item1 == card);
            if (body != null) state.Grave.AddRange(body.OverlayCards);
            state.ResourceActions.Remove(card); state.FusionActions.Remove(card);
            if (body?.BanishOnLeave == true) banish = true;
            if (bounce && !banish)
            {
                if (Has(card, CardType.Link | CardType.Xyz | CardType.Synchro | CardType.Fusion)) state.Extra.Add(card);
                else { state.Reserve.Add(card); state.Acquired.Add(card); state.Credit += HandCommitment(card); AddAcquiredActions(state, card); }
            }
            else if (!banish && !Has(card, CardType.Token) && !(body != null && Has(card, CardType.Pendulum))) state.Grave.Add(card);
        }

        private IEnumerable<DevelopmentState> ComboChoices(DevelopmentState state, ClientCard source, ComboEffect effect,
            SearchBudget budget, bool root, int description)
        {
            if (!ComboReady(state, source, effect) || budget.Exhausted) yield break;
            if (effect.Kind == ComboKind.PlaceSpell)
            {
                int slot = (Has(source, CardType.Pendulum) ? new[] { 0, 4 } : new[] { 2, 1, 3, 0, 4 })
                    .Where(i => !state.Spells.Values.Contains(i)).DefaultIfEmpty(-1).First();
                if (slot < 0) yield break;
                var placed = CopyDevelopment(state);
                placed.Reserve.Remove(source); placed.Acquired.Remove(source); placed.Spells[source] = slot;
                placed.ActivatedSpells.Add(source);
                placed.Credit -= HandCommitment(source); placed.Depth++;
                if (ReadFusionSpell(source) != null && !placed.FusionActions.Contains(source)) placed.FusionActions.Add(source);
                if (root) placed.FirstSummon = new SummonPlan { Card = source, Activation = true, Combo = true, Description = description };
                placed.Score = TerminalValue(placed); budget.Nodes++; yield return placed;
                yield break;
            }
            IEnumerable<ClientCard> costs = new ClientCard[] { null };
            switch (effect.Cost)
            {
                case ComboCost.Discard: case ComboCost.SelfDiscard: case ComboCost.BanishSelfDiscard: case ComboCost.SelfAndTuner:
                    costs = state.Reserve.Where(c => InDevelopmentHand(state, c) && c != source && !Exodia(c) &&
                        (effect.CostFilter == null || effect.CostFilter(ComboView(state, c)))); break;
                case ComboCost.Tribute: case ComboCost.BanishField: case ComboCost.ReturnField:
                    costs = state.Board.Where(b => b.Card != source && (b.Fresh || b.Card.IsFaceup()) &&
                        (effect.Cost != ComboCost.Tribute || !b.CannotTribute) && (effect.Cost != ComboCost.ReturnField || !b.BanishOnLeave) &&
                        (effect.CostFilter == null || effect.CostFilter(ComboView(state, b.Card)))).Select(b => b.Card); break;
                case ComboCost.SendDeck:
                    costs = ComboPool(state, effect.CostFrom != 0 ? effect.CostFrom : CardLocation.Deck).Where(effect.CostFilter ?? effect.Filter); break;
                case ComboCost.Detach:
                    costs = state.Board.First(b => b.Card == source).OverlayCards; break;
                case ComboCost.BanishResource:
                    costs = ComboPool(state, effect.CostFrom != 0 ? effect.CostFrom : CardLocation.Hand | CardLocation.MonsterZone | CardLocation.Grave).Where(c => c != source && (effect.CostFilter == null || effect.CostFilter(c))); break;
            }
            if (effect.Cost == ComboCost.ReturnField)
                costs = costs.Concat(state.Spells.Keys.Where(c => c != source && (effect.CostFilter == null || effect.CostFilter(c)) &&
                    (c.Location == CardLocation.Hand || c.IsFaceup())));
            foreach (var cost in costs.OrderBy(c => c == null ? 0 : MaterialCost(c, HintMsg.Discard)).ThenBy(c => c?.Id ?? 0).ToList())
            {
                if (budget.Exhausted) yield break;
                if (effect.ReturnNeedsHand && cost != null && cost.IsExtraCard()) continue;
                var paid = CopyDevelopment(state);
                var selections = new List<ComboSelection>();
                if (cost != null)
                {
                    int hint = effect.CostHint != 0 ? effect.CostHint : effect.Cost == ComboCost.Detach ? HintMsg.RemoveXyz : effect.Cost == ComboCost.Tribute ? HintMsg.Release : effect.Cost == ComboCost.BanishField || effect.Cost == ComboCost.BanishResource ? HintMsg.Remove :
                        effect.Cost == ComboCost.ReturnField ? HintMsg.ReturnToHand : effect.Cost == ComboCost.SendDeck || effect.Cost == ComboCost.Discard ? HintMsg.ToGrave : HintMsg.Discard;
                    selections.Add(new ComboSelection { Card = cost, Hint = hint, Location = effect.Cost == ComboCost.Detach ? CardLocation.Overlay : ComboLocation(state, cost) });
                    SpendComboCard(paid, cost, effect.Cost == ComboCost.BanishField || effect.Cost == ComboCost.BanishResource, effect.Cost == ComboCost.ReturnField);
                    if (effect.Cost == ComboCost.Detach)
                    {
                        var old = paid.Board.First(b => b.Card == source); var changed = CopyBody(old);
                        changed.OverlayCards.Remove(cost); changed.OverlayCount--; paid.Board[paid.Board.IndexOf(old)] = changed;
                        foreach (var trigger in ComboProfiles(cost).Where(e => e.DetachTrigger)) paid.PendingCombos.Add(Tuple.Create(cost, trigger));
                    }
                    if (effect.Cost == ComboCost.Tribute && paid.Grave.Contains(cost))
                        foreach (var trigger in ComboProfiles(cost).Where(e => e.TributeTrigger)) paid.PendingCombos.Add(Tuple.Create(cost, trigger));
                }
                if (effect.Cost == ComboCost.Self || effect.Cost == ComboCost.SelfAndTuner || effect.Cost == ComboCost.SelfDiscard || effect.Cost == ComboCost.BanishSelfDiscard || effect.Cost == ComboCost.BanishSelf)
                    SpendComboCard(paid, source, effect.Cost == ComboCost.BanishSelfDiscard || effect.Cost == ComboCost.BanishSelf);
                else if (!effect.Trigger && Has(source, CardType.Spell) && InDevelopmentHand(paid, source))
                { paid.Reserve.Remove(source); paid.Acquired.Remove(source); paid.Credit -= HandCommitment(source); }
                if (effect.Once) paid.ComboUsed.Add(ComboKey(source, effect));
                if (effect.SoftOnce) { paid.ComboInstances.Add(source); paid.ComboEffectInstances.Add((source, effect.InstanceKey)); }
                paid.Life -= effect.Life; paid.Credit -= effect.Life * .2f;
                paid.SynchroOnly |= effect.SynchroOnly; paid.XyzOnly |= effect.XyzOnly;
                if (!effect.Trigger) paid.Depth++;
                IEnumerable<ClientCard> targets = effect.Kind == ComboKind.Self ? new[] { source } :
                    ComboPool(paid, effect.TargetFrom).Where(c => c != source && effect.Filter(ComboView(paid, c)));
                // Equal names in hand and grave are different resources. Preserve
                // those alternatives (and distinct field bodies), while merging
                // interchangeable copies still in the same deck/hand zone.
                foreach (var target in targets.GroupBy(c => (c.Id, ComboLocation(paid, c),
                    c.IsCanRevive() || paid.ProperlySummoned.Contains(c), paid.Board.Any(b => b.Card == c) ? c : null))
                    .Select(g => g.First()).OrderBy(c => c.Id).ToList())
                {
                    if (budget.Exhausted) yield break;
                    var next = CopyDevelopment(paid); var choices = new List<ComboSelection>(selections);
                    var origin = ComboLocation(next, target);
                    if (origin == CardLocation.Deck && next.DeckCount <= 0) continue;
                    if (effect.Kind == ComboKind.Search || effect.Kind == ComboKind.SearchPair || effect.Kind == ComboKind.Send || effect.Kind == ComboKind.DrawDiscard || effect.Kind == ComboKind.FusionName)
                    {
                        SpendComboCard(next, target);
                        if (effect.Kind == ComboKind.Search || effect.Kind == ComboKind.SearchPair)
                        {
                            next.Grave.Remove(target); next.Reserve.Add(target); next.Acquired.Add(target);
                            next.Credit += HandCommitment(target); AddAcquiredActions(next, target);
                        }
                        else if (effect.Kind != ComboKind.FusionName) QueueSentByEffect(next, target);
                        if (effect.Kind == ComboKind.FusionName)
                        {
                            var old = next.Board.First(b => b.Card == source); var changed = CopyBody(old); changed.FusionName = CardIdentity(target);
                            next.Board[next.Board.IndexOf(old)] = changed;
                        }
                        choices.Add(new ComboSelection { Card = target, Location = origin,
                            Hint = effect.Kind == ComboKind.Search || effect.Kind == ComboKind.SearchPair ? HintMsg.AddToHand : HintMsg.ToGrave });
                        if (effect.Kind == ComboKind.DrawDiscard) { next.DeckCount--; next.Credit += 1100; }
                        if (effect.LevelFromTarget == 1)
                        {
                            var old = next.Board.First(b => b.Card == source);
                            var changed = CopyBody(old); changed.ProjectedLevel = (old.ProjectedLevel > 0 ? old.ProjectedLevel : Level(source)) + Level(target);
                            changed.MaterialView = ProjectComboMaterial(source, changed.ProjectedLevel);
                            next.Board[next.Board.IndexOf(old)] = changed;
                        }
                        if (effect.LevelFromTarget == 2)
                        {
                            var old = next.Board.First(b => b.Card == source); var changed = CopyBody(old);
                            changed.ProjectedLevel = Level(target); changed.MaterialView = ProjectComboMaterial(source, changed.ProjectedLevel);
                            next.Board[next.Board.IndexOf(old)] = changed;
                        }
                        // Unknown topdeck: consume the count only, never invent its identity or a grave trigger.
                        if (effect.Mill && next.DeckCount > 0) next.DeckCount--;
                    }
                    else
                    {
                        if (effect.Kind != ComboKind.Self && (Has(target, CardType.SpSummon) || origin == CardLocation.Grave &&
                            !target.IsCanRevive() && !next.ProperlySummoned.Contains(target))) continue;
                        int zone = DevelopmentPlace(next, target, false);
                        if (zone < 0) continue;
                        if (origin == CardLocation.Hand) next.Credit -= HandCommitment(target);
                        if (origin == CardLocation.Deck) next.DeckCount--;
                        next.Reserve.Remove(target); next.Acquired.Remove(target); next.Grave.Remove(target);
                        next.Credit -= effect.Kind == ComboKind.Normal ? DrawPenalty(origin, true) : DrawPenalty(origin) + SummonExposure(state);
                        next.Board.Add(new Body { Card = target, Zone = zone, Attack = effect.Attack >= 0 ? effect.Attack :
                            Attack(target) + (effect.CopyTributeAttack ? Math.Max(0, cost?.Data?.Attack ?? 0) : 0),
                            ProjectedLevel = effect.Level, MaterialView = effect.Level > 0 ? ProjectComboMaterial(target, effect.Level) : null,
                            Fresh = true, BanishOnLeave = effect.BanishOnLeave, DefensePosition = effect.Defense,
                            SynchroOnly = effect.BodySynchroOnly, EffectsBlocked = effect.Disabled, CannotTribute = effect.CannotTribute, ExtraSetcode = effect.ExtraSetcode });
                        QueueLevelArrival(next, target, effect.Kind == ComboKind.Normal);
                        if (effect.Temporary || effect.CannotAttack)
                        { next.Board.Last().Temporary = effect.Temporary; next.Board.Last().CannotAttack = effect.CannotAttack; }
                        if (effect.Mill) next.DeckCount--;
                        if (!effect.MaterialTrigger && !effect.Disabled) QueueComboArrival(next, target, effect.Kind == ComboKind.Normal);
                        if (effect.Kind != ComboKind.Self) choices.Add(new ComboSelection { Card = target, Location = origin, Hint = effect.Kind == ComboKind.Normal ? HintMsg.Summon : HintMsg.SpSummon });
                    }
                    if (root) next.FirstSummon = new SummonPlan { Card = source, Activation = !effect.Direct, Description = description,
                        Combo = true, Choices = choices };
                    if (effect.Kind == ComboKind.SearchPair)
                        foreach (var second in ComboSecondSearch(next, source, target, effect, budget, root)) yield return second;
                    if (effect.AfterDiscard)
                    {
                        foreach (var looted in ComboLionDiscard(next, budget, root)) yield return looted;
                        continue;
                    }
                    if (effect.RevealFilter != null)
                    {
                        if (root) next.FirstSummon.Choices.Insert(0, new ComboSelection { Card = ComboPool(state, effect.RevealFrom).First(effect.RevealFilter), Hint = HintMsg.Confirm, Location = effect.RevealFrom });
                        foreach (var second in ComboFollowupRecruit(next, source, effect, budget, root)) yield return second;
                    }
                    next.Score = TerminalValue(next); budget.Nodes++; yield return next;
                }
            }
        }

        private IEnumerable<DevelopmentState> ComboFollowupRecruit(DevelopmentState state, ClientCard source, ComboEffect effect, SearchBudget budget, bool root)
        {
            if (effect.FollowupFilter == null || effect.FollowupFrom != CardLocation.Hand) yield break;
            foreach (var card in ComboPool(state, effect.FollowupFrom).Where(c => effect.FollowupFilter(c) && !Has(c, CardType.SpSummon)))
            {
                if (budget.Exhausted) yield break;
                int zone = DevelopmentPlace(state, card, false); if (zone < 0) yield break;
                var next = CopyDevelopment(state); next.Reserve.Remove(card); next.Acquired.Remove(card);
                next.Credit -= HandCommitment(card) + DrawPenalty(CardLocation.Hand) + SummonExposure(state);
                next.Board.Add(new Body { Card = card, Zone = zone, Attack = Attack(card), Fresh = true });
                QueueLevelArrival(next, card, false);
                QueueComboArrival(next, card, false);
                if (root) next.FirstSummon = new SummonPlan { Card = source, Activation = true, Combo = true, Description = state.FirstSummon.Description,
                    Choices = state.FirstSummon.Choices.Concat(new[] { new ComboSelection { Card = card, Hint = HintMsg.SpSummon, Location = CardLocation.Hand } }).ToList() };
                next.Score = TerminalValue(next); budget.Nodes++; yield return next;
            }
        }

        private static Body CopyBody(Body b) => new Body { Card = b.Card, MaterialView = b.MaterialView, Attack = b.Attack, Zone = b.Zone,
            OverlayCards = new List<ClientCard>(b.OverlayCards), FusionName = b.FusionName,
            OverlayCount = b.OverlayCount, ProjectedLevel = b.ProjectedLevel, SynchroOnly = b.SynchroOnly, CannotTribute = b.CannotTribute,
            ExtraSetcode = b.ExtraSetcode, Fresh = b.Fresh, FromExtra = b.FromExtra, EffectsBlocked = b.EffectsBlocked,
            DefensePosition = b.DefensePosition, CannotAttack = b.CannotAttack, Temporary = b.Temporary, BanishOnLeave = b.BanishOnLeave };

        private SummonPlan CopyComboPlan(SummonPlan plan, ComboSelection choice) => new SummonPlan { Card = plan.Card, Activation = plan.Activation,
            Description = plan.Description, Combo = true, Choices = plan.Choices.Concat(new[] { choice }).ToList() };

        private IEnumerable<DevelopmentState> ComboSecondSearch(DevelopmentState state, ClientCard source, ClientCard first, ComboEffect effect, SearchBudget budget, bool root)
        {
            var sets = effect.SearchSets;
            foreach (var second in ComboPool(state, effect.CostFrom != 0 ? effect.CostFrom : CardLocation.Deck).Where(effect.CostFilter ?? effect.Filter).GroupBy(c => c.Id).Select(g => g.First()))
            {
                if (budget.Exhausted) yield break;
                if (state.DeckCount <= 0 || !sets.Any(a => first.HasSetcode(a) && sets.Any(b => b != a && second.HasSetcode(b)))) continue;
                var next = CopyDevelopment(state); next.Reserve.Remove(second); next.Reserve.Add(second); next.Acquired.Add(second); next.DeckCount--;
                next.Credit += HandCommitment(second); AddAcquiredActions(next, second);
                if (root) next.FirstSummon = CopyComboPlan(state.FirstSummon, new ComboSelection { Card = second, Hint = HintMsg.AddToHand, Location = CardLocation.Deck });
                next.Score = TerminalValue(next); budget.Nodes++; yield return next;
            }
        }

        private IEnumerable<DevelopmentState> ComboLionDiscard(DevelopmentState state, SearchBudget budget, bool root)
        {
            foreach (var cost in ComboPool(state, CardLocation.Hand).Where(c => !Exodia(c)).OrderBy(c => HandCommitment(c)))
            {
                if (budget.Exhausted) yield break;
                var next = CopyDevelopment(state); SpendComboCard(next, cost); QueueSentByEffect(next, cost);
                if (root) next.FirstSummon = CopyComboPlan(state.FirstSummon, new ComboSelection { Card = cost, Hint = HintMsg.Discard, Location = CardLocation.Hand });
                next.Score = TerminalValue(next); budget.Nodes++; yield return next;
            }
        }

        private void QueueSentByEffect(DevelopmentState state, ClientCard card)
        {
            if (state.Grave.Contains(card) && ComboProfiles(card).Any(e => e.SentEffectTrigger) && !state.PendingFusionMaterials.Contains(card))
            { state.PendingFusionMaterials.Add(card); state.EffectSentOnly.Add(card); }
        }
        private void QueueComboArrival(DevelopmentState state, ClientCard card, bool normal)
        {
            QueueDevelopmentSearch(state, card, normal);
            if (!state.Credited.Contains(card.Id) && ReadRecruitTrigger(card) is RecruitTrigger recruit && (normal ? recruit.Normal : recruit.Special))
                state.PendingRecruit = card;
            foreach (var effect in ComboProfiles(card).Where(e => e.SummonTrigger && !e.ExtraSummonTrigger &&
                e != ReadResourceEffect(card)?.Combo && (normal ? e.NormalTrigger : e.SpecialTrigger)))
                if (!state.ComboUsed.Contains(ComboKey(card, effect))) state.PendingCombos.Add(Tuple.Create(card, effect));
        }

        private IEnumerable<DevelopmentState> ComboSuccessors(DevelopmentState state, SearchBudget budget)
        {
            foreach (var source in state.Reserve.Where(c => InDevelopmentHand(state, c)).Concat(state.Grave).Concat(state.Board.Select(b => b.Card)).Concat(state.Spells.Keys).Distinct().OrderBy(c => c.Id))
                foreach (var effect in ComboProfiles(source).Where(e => !e.Trigger))
                    foreach (var child in ComboChoices(state, source, effect, budget, false, source.Id * 16 + effect.Offset)) yield return child;
        }
        private IEnumerable<DevelopmentState> PendingComboSuccessors(DevelopmentState state, SearchBudget budget)
        {
            var pending = state.PendingCombos[0]; var ready = CopyDevelopment(state); ready.PendingCombos.RemoveAt(0);
            ready.Score = TerminalValue(ready); yield return ready;
            foreach (var child in ComboChoices(ready, pending.Item1, pending.Item2, budget, false, 0)) yield return child;
        }

        private IEnumerable<DevelopmentState> ComboRoots(DevelopmentState state, ClientCard card, int description, bool direct, SearchBudget budget)
        {
            var level = !direct ? StoryAiLevelEffects.Find(card, description) : null;
            return level?.Trigger == false ? LevelChoices(state, card, level, budget, true, description) :
                ComboProfiles(card).Where(e => e.Direct == direct && !e.Trigger && (direct || ComboDescription(card, e, description)))
                    .SelectMany(e => ComboChoices(state, card, e, budget, true, description));
        }

        internal SummonPlan PlanComboAction(ClientCard card, int description, bool direct = false, bool trigger = false)
        {
            var initial = InitialDevelopment(new ClientCard[0]);
            if (trigger) PrepareResolutionNormals(initial);
            float continuation = TerminalValue(initial);
            if (trigger && ComboProfiles(card).Any(e => e.Kind == ComboKind.DrawDiscard && ComboDescription(card, e, description)))
                SearchDevelopment(new List<DevelopmentState> { initial }, new SearchBudget(), s => continuation = Math.Max(continuation, s.Score));
            var budget = new SearchBudget();
            var seeds = trigger ? ComboProfiles(card).Where(e => e.Trigger && ComboDescription(card, e, description))
                .SelectMany(e => ComboChoices(initial, card, e, budget, true, description)).ToList() : ComboRoots(initial, card, description, direct, budget).ToList();
            float baseline = TerminalValue(initial), best = Math.Max(baseline, continuation) + (trigger ? -1 : 50); SummonPlan result = null;
            Action<DevelopmentState> visit = s => { if (s.FirstSummon != null && s.Score > best) { best = s.Score; result = s.FirstSummon; result.Gain = best - baseline; } };
            foreach (var seed in seeds) visit(seed);
            SearchDevelopment(seeds, budget, visit);
            return result;
        }

        private bool ValidDevelopmentMaterials(DevelopmentState state, ClientCard destination, Recipe recipe, List<ClientCard> materials, List<Body> own)
        {
            // Per-branch stat changes must not mutate ClientCard or leak into sibling routes.
            var projected = materials.Select(c =>
            {
                var b = own.FirstOrDefault(x => x.Card == c);
                if (b?.EffectsBlocked == true && CanBeNonTuner(c))
                    return ProjectComboMaterial(c, b.ProjectedLevel > 0 ? b.ProjectedLevel : Level(c), disabled: true);
                return b?.MaterialView ?? (b?.Fresh == true && c.Location != CardLocation.MonsterZone
                    ? ProjectComboMaterial(c, Level(c)) : c);
            }).ToList();
            return ValidMaterials(destination, recipe, projected, c => own.Any(b => b.Card == materials[projected.IndexOf(c)] && b.FromExtra));
        }

        private ClientCard ProjectComboMaterial(ClientCard card, int level, bool nonTuner = false, bool disabled = false)
        {
            var view = new ClientCard(card.Id, CardLocation.MonsterZone, 0, (int)CardPosition.FaceUpAttack) { Controller = card.Controller };
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((int)(Query.Type | Query.Level | Query.Attribute | Query.Race | Query.Status));
                    int type = card.Type != 0 ? card.Type : card.Data.Type;
                    writer.Write(nonTuner ? type & ~(int)CardType.Tuner : type); writer.Write(level);
                    writer.Write(Attribute(card)); writer.Write(Race(card));
                    writer.Write(disabled || card.IsDisabled() ? 1 : 0);
                }
                stream.Position = 0;
                using (var reader = new BinaryReader(stream)) view.Update(reader, duel);
            }
            return view;
        }

        internal void NoteComboSelection(ClientCard source, ClientCard selected, int hint)
        {
            if (source != null && ComboProfiles(source).Any(e => e.Kind == ComboKind.FusionName) && hint == HintMsg.ToGrave &&
                source.Location == CardLocation.MonsterZone && (selected.Location == CardLocation.Deck || selected.Location == CardLocation.Extra))
                pendingFusionNames[source] = CardIdentity(selected);
        }
        internal void NoteComboResolution(ClientCard source, int description, bool negated)
        {
            if (source == null) return;
            if (!negated && ComboProfiles(source).Any(e => e.ActivationTurnOnly) && description == 0) spellActivationTurns[source] = duel.Turn;
            if (!negated)
                foreach (var effect in ComboProfiles(source).Where(e => ComboDescription(source, e, description)))
                { developmentSynchroOnly |= effect.SynchroOnly; developmentXyzOnly |= effect.XyzOnly; }
            developmentCacheKey = summonCacheKey = null;
            if (!ComboProfiles(source).Any(e => e.Kind == ComboKind.FusionName && ComboDescription(source, e, description)) ||
                !pendingFusionNames.TryGetValue(source, out int name)) return;
            if (!negated && source.Location == CardLocation.MonsterZone && source.IsFaceup()) liveFusionNames[source] = name;
            pendingFusionNames.Remove(source); developmentCacheKey = summonCacheKey = null;
        }
    }
}
