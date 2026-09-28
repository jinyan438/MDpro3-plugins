using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        private sealed class ResourceEffect
        {
            internal ComboEffect Combo;
            internal bool Search, Normal, Special, ExtraTrigger, Defense, Disabled, Temporary, Once, EmptyBoard, SendMonster;
            internal CardLocation Source;
            internal int TargetId, DescriptionOffset;
            internal RecruitTrigger Filter;
            internal bool Trigger => Normal || Special || ExtraTrigger;
        }

        private readonly Dictionary<int, ResourceEffect> resourceEffects = new Dictionary<int, ResourceEffect>();
        private bool normalSummonSpent;
        private readonly HashSet<ClientCard> temporaryDevelopmentBodies = new HashSet<ClientCard>();

        internal void NoteResourceSummons(IEnumerable<ClientCard> cards, ClientCard source, int description = 0)
        {
            if (source != null)
                foreach (var combo in ComboProfiles(source).Where(e => ComboDescription(source, e, description)))
                {
                    developmentSynchroOnly |= combo.SynchroOnly; developmentXyzOnly |= combo.XyzOnly;
                    foreach (var card in cards.Where(c => c.Controller == 0))
                    {
                        if (combo.BanishOnLeave) banishOnLeaveBodies.Add(card);
                        if (combo.BodySynchroOnly && card == source) comboSynchroBodies.Add(card);
                        if (combo.CannotTribute) comboUntributableUntil[card] = duel.Turn + (duel.Player == 0 ? 1 : 0);
                        if (combo.Temporary) temporaryDevelopmentBodies.Add(card);
                        if (combo.ExtraSetcode != 0) bodyExtraSetcodes[card] = combo.ExtraSetcode;
                    }
                }
            if (source == null || ReadResourceEffect(source)?.Temporary != true && ReadFusionSpell(source)?.Instant != true) return;
            foreach (var card in cards.Where(c => c.Controller == 0)) temporaryDevelopmentBodies.Add(card);
            developmentCacheKey = summonCacheKey = null;
        }

        internal void NoteNormalCommitment()
        {
            normalSummonSpent = true;
            developmentCacheKey = summonCacheKey = null;
        }

        internal bool NormalSearchCandidate(ClientCard card) => !normalSummonSpent && SimpleNormalBody(card);

        internal float ImmediateRevivalGain(ClientCard source, ClientCard target) =>
            BoardValue(target) + (LiveInteraction(target) ? NewInteractionValue(target, Attack(target)) : 0) -
            HandCommitment(source) - DrawPenalty(CardLocation.Grave) - 45;

        private bool AcquiredNormalAllowed(DevelopmentState state, ClientCard card) => state.Acquired.Contains(card) && SimpleNormalBody(card) &&
            !state.Board.Where(b => !b.EffectsBlocked && !b.Card.IsDisabled() && (b.Fresh || b.Card.IsFaceup())).Select(b => b.Card)
                .Concat(Bot.GetSpells().Concat(state.Enemy).Concat(Enemy.GetSpells()).Where(c => c.IsFaceup() && !c.IsDisabled())).Any(c =>
                Regex.IsMatch(c.Data?.Description ?? "",
                    @"cannot Normal Summon|can only Normal Summon|不能通常召唤|不能召唤|只能通常召唤", RegexOptions.IgnoreCase));

        private static bool InDevelopmentHand(DevelopmentState state, ClientCard card) =>
            state.Reserve.Contains(card) && (card.Location == CardLocation.Hand || state.Acquired.Contains(card));

        // Complete, single-effect clauses only. Unparsed costs, restrictions and extra
        // effects cannot become a free search/recruit just because they contain keywords.
        private ResourceEffect ReadResourceEffect(ClientCard card)
        {
            if (resourceEffects.TryGetValue(card.Id, out var cached)) return cached;
            ResourceEffect result = null;
            if (Facts(card).UniversalRevival)
                return resourceEffects[card.Id] = new ResourceEffect { Source = CardLocation.Grave,
                    Filter = new RecruitTrigger { Requirement = new MaterialRequirement() } };
            var scriptTrigger = ComboProfiles(card).FirstOrDefault(e => e.SummonTrigger && e.Kind == ComboKind.Search);
            if (scriptTrigger != null) return resourceEffects[card.Id] = new ResourceEffect { Combo = scriptTrigger, Search = true,
                Normal = scriptTrigger.NormalTrigger, Special = scriptTrigger.SpecialTrigger, ExtraTrigger = scriptTrigger.ExtraSummonTrigger,
                Once = scriptTrigger.Once, DescriptionOffset = scriptTrigger.Offset };
            // Script actions already own their costs, trigger events and count
            // ledger; do not create a second free text-derived copy of them.
            if (ComboProfiles(card).Any(e => e.Kind != ComboKind.PlaceSpell)) return null;
            if (Has(card, CardType.Monster) || Has(card, CardType.Spell) && !Has(card, CardType.Continuous | CardType.Field | CardType.Equip))
            {
                string text = card.Data?.Description ?? "";
                if (Has(card, CardType.Link | CardType.Synchro | CardType.Xyz | CardType.Fusion))
                {
                    int end = text.IndexOf('\n');
                    text = end >= 0 ? text.Substring(end + 1) : "";
                }
                bool once = Regex.IsMatch(text, @"once per turn|1回合[^。]*1[次张]", RegexOptions.IgnoreCase);
                text = Regex.Replace(text,
                    @"这个卡名的(?:卡|①的效果)1回合只能(?:发动1张|使用1次)。|You can only (?:activate 1 ""[^""]+""|use this effect of ""[^""]+"") (?:per turn|once per turn)\.", "", RegexOptions.IgnoreCase).Trim();
                if (Regex.Matches(text, @"[①-⑳]").Count <= 1)
                {
                    text = Regex.Replace(text, @"^①\s*[:：]\s*", "").Trim();
                    bool empty = false, sendMonster = false;
                    if (Has(card, CardType.Spell))
                    {
                        var condition = Regex.Match(text, @"^(?:If you control no monsters: |自己场上没有怪兽存在的场合才能发动。)", RegexOptions.IgnoreCase);
                        if (condition.Success) { empty = true; text = text.Substring(condition.Length).Trim(); }
                        var cost = Regex.Match(text, @"^(?:Send 1 monster from your hand to the (?:GY|Graveyard); |从手卡把1只怪兽送去墓地才能发动。)", RegexOptions.IgnoreCase);
                        if (cost.Success) { sendMonster = true; text = text.Substring(cost.Length).Trim(); }
                    }
                    var trigger = Regex.Match(text,
                        @"^(?:(?:If|When) this card is (?<kind>Normal or Special|Normal|Special|Link|Synchro|Xyz|Fusion) Summoned: (?:You can )?|这张卡(?<kind>召唤·特殊召唤|特殊召唤|连接召唤|同调召唤|超量召唤|融合召唤|召唤)(?:成功)?(?:时|的场合)(?:才能发动)?[。：])",
                        RegexOptions.IgnoreCase);
                    if (trigger.Success) text = text.Substring(trigger.Length).Trim();
                    var search = Regex.Match(text,
                        @"^(?:Add 1 (?<kind>[^.;:]+) from your Deck to your hand\.|从(?:自己)?卡组把1只(?<kind>[^。]+)加入手卡。)$", RegexOptions.IgnoreCase);
                    var recruit = Regex.Match(text,
                        @"^(?:Special Summon 1 (?<kind>[^.;:]+?) from your (?<source>hand or Deck|Deck|hand|GY)(?<defense> in Defense Position)?\.|从(?:自己)?(?<source>手卡·卡组|卡组|手卡|墓地)把1只(?<kind>[^。]+?)(?<defense>守备表示)?特殊召唤。)(?<tail>.*)$",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    var match = search.Success ? search : recruit;
                    if (match.Success && (Has(card, CardType.Monster) ? trigger.Success && search.Success : !trigger.Success))
                    {
                        string kind = match.Groups["kind"].Value.Trim();
                        var requirement = ParseMaterial(kind);
                        string tail = match.Groups["tail"].Value.Trim();
                        bool temporary = Regex.IsMatch(tail, @"End Phase|结束阶段", RegexOptions.IgnoreCase);
                        bool disabled = Regex.IsMatch(tail, @"effects are negated|效果无效化", RegexOptions.IgnoreCase);
                        tail = Regex.Replace(tail,
                            @"(?:but )?its effects are negated\.|这个效果特殊召唤的怪兽的效果无效化。|这个效果特殊召唤的怪兽在这个回合的结束阶段除外。|During the End Phase of this turn, banish (?:that monster|the monster Special Summoned by this effect)\.",
                            "", RegexOptions.IgnoreCase).Trim();
                        if (requirement != null && tail.Length == 0)
                        {
                            string summon = trigger.Groups["kind"].Value, source = match.Groups["source"].Value;
                            result = new ResourceEffect { Search = search.Success, Once = once, EmptyBoard = empty, SendMonster = sendMonster,
                                Normal = Contains(summon, "Normal") || summon == "召唤" || summon == "召唤·特殊召唤",
                                Special = Contains(summon, "Special", "特殊"),
                                ExtraTrigger = trigger.Success && Contains(summon, "Link", "Synchro", "Xyz", "Fusion", "连接", "同调", "超量", "融合"),
                                Source = search.Success ? CardLocation.Deck : Contains(source, "GY", "墓地") ? CardLocation.Grave :
                                    (Contains(source, "hand", "手卡") ? CardLocation.Hand : 0) | (Contains(source, "Deck", "卡组") ? CardLocation.Deck : 0),
                                Defense = match.Groups["defense"].Success, Disabled = disabled, Temporary = temporary,
                                Filter = new RecruitTrigger { Requirement = requirement } };
                        }
                    }
                }
            }
            if (resourceEffects.Count < 4096) resourceEffects[card.Id] = result;
            return result;
        }

        internal bool ModelledResourceAction(ClientCard card, int description)
        {
            var effect = ReadResourceEffect(card);
            return effect != null && !effect.Trigger && (description == 0 && Has(card, CardType.Spell) ||
                description == CardIdentity(card) * 16 + effect.DescriptionOffset || description == card.Id * 16 + effect.DescriptionOffset) &&
                card.Controller == 0 && (card.Location == CardLocation.Hand || card.Location == CardLocation.SpellZone) && !card.IsDisabled();
        }

        private bool DevelopmentSummonsBlocked(DevelopmentState state) => BlanketExtraSummonLock(state) || state.Board.Any(b =>
            !b.EffectsBlocked && !b.Card.IsDisabled() && (b.Fresh || b.Card.IsFaceup()) &&
            UnmodelledSummonRestriction(b.Card)) ||
            Bot.GetSpells().Concat(Enemy.GetSpells()).Concat(state.Enemy).Any(c => c.IsFaceup() && !c.IsDisabled() &&
                Regex.IsMatch(c.Data?.Description ?? "", @"(?:Neither player|Your opponent|Players) can(?:not|'t) Special Summon|双方不能特殊召唤|对方不能特殊召唤", RegexOptions.IgnoreCase));

        private static bool UnmodelledSummonRestriction(ClientCard card)
        {
            string text = card.Data?.Description ?? "";
            if (ComboProfiles(card).Any(e => e.SynchroOnly || e.XyzOnly || e.BodySynchroOnly || e.ExtraSetcode != 0))
                text = Regex.Replace(text, @"不是[^。\n]*不能从额外卡组特殊召唤|不能从额外卡组特殊召唤[^。\n]*以外的怪兽|cannot Special Summon from (?:the|your) Extra Deck, except[^.\n]+", "", RegexOptions.IgnoreCase);
            return Regex.IsMatch(text, @"cannot Special Summon|不能特殊召唤|不是[^。\n]*不能", RegexOptions.IgnoreCase);
        }

        // Category restrictions are checked by ExtraDestinationAllowed and the
        // Synchro-only parser. They must not be treated as a blanket summon lock.
        private bool BlanketExtraSummonLock(DevelopmentState state) => state.Board.Where(b =>
            !b.EffectsBlocked && !b.Card.IsDisabled() && (b.Fresh || b.Card.IsFaceup())).Select(b => b.Card)
            .Concat(state.Enemy.Where(c => c.IsFaceup() && !c.IsDisabled()))
            .Concat(Bot.GetSpells().Concat(Enemy.GetSpells()).Where(c => c.IsFaceup() && !c.IsDisabled()))
            .Any(c => Regex.IsMatch(c.Data?.Description ?? "", @"双方不能(?:把怪兽)?特殊召唤|Neither player can Special Summon", RegexOptions.IgnoreCase) ||
                c.Controller == 1 && Regex.IsMatch(c.Data?.Description ?? "", @"对方不能(?:把怪兽)?特殊召唤|Your opponent cannot Special Summon monsters[.]", RegexOptions.IgnoreCase));

        private void QueueDevelopmentSearch(DevelopmentState state, ClientCard card, bool normal, bool extra = false)
        {
            var effect = ReadResourceEffect(card);
            if (effect == null || !effect.Search || !effect.Trigger || card.IsDisabled() || state.Credited.Contains(card.Id)) return;
            if (normal ? effect.Normal : effect.Special || extra && effect.ExtraTrigger) state.PendingSearch = card;
        }

        private IEnumerable<DevelopmentState> ResourceChoices(DevelopmentState state, ClientCard source,
            ResourceEffect effect, SearchBudget budget, bool root, int description = 0)
        {
            if (effect.Combo != null)
            {
                var ready = CopyDevelopment(state); ready.PendingSearch = null;
                foreach (var child in ComboChoices(ready, source, effect.Combo, budget, root, description)) yield return child;
                yield break;
            }
            if (effect.EmptyBoard && state.Board.Count > 0 || effect.Search && DrawLocked || !effect.Search && DevelopmentSummonsBlocked(state)) yield break;
            if (effect.SendMonster)
            {
                foreach (var cost in state.Reserve.Where(c => InDevelopmentHand(state, c) && Has(c, CardType.Monster) && !Exodia(c))
                    .OrderBy(c => HandCommitment(c)).ThenBy(c => c.Id))
                {
                    if (budget.Exhausted) yield break;
                    var paid = CopyDevelopment(state);
                    paid.Reserve.Remove(cost); paid.Acquired.Remove(cost); paid.Grave.Add(cost);
                    paid.Credit -= HandCommitment(cost) + (IsHandTrap(cost) ? 1600 : 0);
                    foreach (var child in ResourceOutcomes(paid, source, effect, budget, root, description, cost)) yield return child;
                }
                yield break;
            }
            foreach (var child in ResourceOutcomes(state, source, effect, budget, root, description, null)) yield return child;
        }

        private IEnumerable<DevelopmentState> ResourceOutcomes(DevelopmentState state, ClientCard source,
            ResourceEffect effect, SearchBudget budget, bool root, int description, ClientCard cost)
        {
            var pool = state.Reserve.Where(c => InDevelopmentHand(state, c)
                ? (effect.Source & CardLocation.Hand) != 0 : c.Location == CardLocation.Deck && (effect.Source & CardLocation.Deck) != 0).ToList();
            if ((effect.Source & CardLocation.Grave) != 0) pool.AddRange(state.Grave);
            // Opposing grave monsters require a controller-aware virtual copy. The
            // dedicated live revival selector handles those; never assume ownership.
            foreach (var target in pool.Where(c => (effect.TargetId != 0 ? CardIdentity(c) == effect.TargetId : effect.Filter.Matches(c)) && c != source).Distinct().OrderBy(c => c.Id))
            {
                if (budget.Exhausted) yield break;
                CardLocation origin = state.Grave.Contains(target) ? CardLocation.Grave :
                    InDevelopmentHand(state, target) ? CardLocation.Hand : CardLocation.Deck;
                if (origin == CardLocation.Deck && state.DeckCount <= 0) continue;
                if (!effect.Search && (Has(target, CardType.SpSummon) || origin == CardLocation.Grave &&
                    !target.IsCanRevive() && !state.ProperlySummoned.Contains(target))) continue;
                int zone = effect.Search ? -1 : DevelopmentPlace(state, target, false);
                if (!effect.Search && zone < 0) yield break;
                var next = CopyDevelopment(state);
                if (next.PendingSearch == source) next.PendingSearch = null;
                if (!effect.Trigger)
                {
                    next.ResourceActions.Remove(source); next.Reserve.Remove(source); next.Acquired.Remove(source);
                    next.Grave.Add(source); next.Credit -= HandCommitment(source); next.Depth++;
                    if (effect.Once) next.Credited.Add(source.Id);
                }
                next.Reserve.Remove(target); next.Acquired.Remove(target); next.Grave.Remove(target);
                if (origin == CardLocation.Deck) next.DeckCount--;
                if (effect.Search)
                {
                    next.Reserve.Add(target); next.Acquired.Add(target);
                    next.Credit += HandCommitment(target);
                    AddAcquiredActions(next, target);
                }
                else
                {
                    if (origin == CardLocation.Hand) next.Credit -= HandCommitment(target);
                    next.Credit -= DrawPenalty(origin) + SummonExposure(state);
                    next.Board.Add(new Body { Card = target, Attack = Attack(target), Zone = zone, Fresh = true,
                        EffectsBlocked = effect.Disabled, DefensePosition = effect.Defense, Temporary = effect.Temporary });
                    QueueLevelArrival(next, target, false);
                    if (!effect.Disabled)
                    {
                        QueueComboArrival(next, target, false);
                        QueueDevelopmentSearch(next, target, false);
                        if (!next.Credited.Contains(target.Id) && ReadRecruitTrigger(target)?.Special == true) next.PendingRecruit = target;
                        next.Credited.Add(target.Id);
                    }
                }
                if (root) next.FirstSummon = new SummonPlan { Card = source, Activation = true, Description = description,
                    Target = target, TargetHint = effect.Search ? HintMsg.AddToHand : HintMsg.SpSummon, Cost = cost };
                next.Score = TerminalValue(next); budget.Nodes++;
                yield return next;
            }
        }

        private IEnumerable<DevelopmentState> ResourceSuccessors(DevelopmentState state, SearchBudget budget)
        {
            foreach (var source in state.ResourceActions.OrderBy(c => c.Id))
            {
                var effect = ReadResourceEffect(source);
                if (effect == null || effect.Once && state.Credited.Contains(source.Id) ||
                    !InDevelopmentHand(state, source) && source.Location != CardLocation.SpellZone) continue;
                foreach (var child in ResourceChoices(state, source, effect, budget, false)) yield return child;
            }
        }

        internal bool ResourcePressureAllows(ClientCard source)
        {
            var effect = ReadResourceEffect(source);
            if (effect == null || effect.Search) return true;
            int tax = ((effect.Source & CardLocation.Hand) != 0 ? DrawTax(CardLocation.Hand) : 0) +
                ((effect.Source & CardLocation.Deck) != 0 ? DrawTax(CardLocation.Deck) : 0) +
                ((effect.Source & CardLocation.Grave) != 0 ? DrawTax(CardLocation.Grave) : 0);
            if (tax == 0) return true;
            var initial = InitialDevelopment(new ClientCard[0]);
            float baseline = TerminalValue(initial), best = baseline;
            var budget = new SearchBudget();
            var seeds = ResourceChoices(initial, source, effect, budget, true).ToList();
            foreach (var seed in seeds) best = Math.Max(best, seed.Score);
            SearchDevelopment(seeds, budget, state => best = Math.Max(best, state.Score));
            return best > baseline + 100;
        }

        internal Dictionary<ClientCard, float> AcquisitionBonuses(IList<ClientCard> cards)
        {
            var result = cards.Distinct().ToDictionary(c => c, _ => 0f);
            if (cards.Count == 0 || Bot.ExtraDeck.Count == 0 || DrawLocked) return result;
            var initial = InitialDevelopment(new ClientCard[0]);
            // The submitted action ledger, not the old MainPhase list, determines
            // whether a searched starter still has the turn's normal summon.
            PrepareResolutionNormals(initial);
            var seeds = new List<DevelopmentState>();
            float baseline = TerminalValue(initial);
            foreach (var card in cards.Where(c => c.Controller == 0).Distinct())
            {
                var seed = CopyDevelopment(initial); seed.Addition = card;
                seed.Reserve.Remove(card); seed.Reserve.Add(card); seed.Acquired.Add(card);
                AddAcquiredActions(seed, card);
                seed.Grave.Remove(card);
                if (card.Location == CardLocation.Deck && seed.DeckCount > 0) seed.DeckCount--;
                seed.Credit += HandCommitment(card); seed.Score = TerminalValue(seed);
                seeds.Add(seed);
            }
            SearchDevelopment(seeds, new SearchBudget(), state =>
            {
                if (state.Addition != null && !state.Reserve.Contains(state.Addition))
                    result[state.Addition] = Math.Max(result[state.Addition], state.Score - baseline);
            });
            return result;
        }

        private void AddAcquiredActions(DevelopmentState state, ClientCard card)
        {
            if (ReadFusionSpell(card) != null && !state.FusionActions.Contains(card)) state.FusionActions.Add(card);
            var effect = ReadResourceEffect(card);
            if (effect != null && !effect.Trigger && !state.ResourceActions.Contains(card)) state.ResourceActions.Add(card);
        }

        private void PrepareResolutionNormals(DevelopmentState state)
        {
            state.NormalUsed = normalSummonSpent;
            state.Normals = normalSummonSpent ? new List<ClientCard>() : Bot.Hand.Where(SimpleNormal).ToList();
        }

        internal bool ConsumesDevelopmentResource(ClientCard card)
        {
            var profiles = ComboProfiles(card).Where(e => !e.Trigger && e.Kind != ComboKind.PlaceSpell).ToList();
            if (profiles.Count > 0) return profiles.Any(e => e.Cost != ComboCost.None && e.Cost != ComboCost.Self || e.Kind == ComboKind.DrawDiscard);
            string text = card.Data?.Description ?? "";
            return Regex.IsMatch(text, @"解放|丢弃|从手卡把|把自己场上|回到手卡|Tribute|discard|send[^.]*from your hand|return[^.]*you control", RegexOptions.IgnoreCase);
        }

        internal Dictionary<ClientCard, float> DiscardBonuses(IList<ClientCard> cards)
        {
            var result = cards.Distinct().ToDictionary(c => c, _ => 0f);
            if (cards.Count > 12 || Bot.ExtraDeck.Count == 0) return result;
            var initial = InitialDevelopment(new ClientCard[0]); PrepareResolutionNormals(initial);
            float baseline = TerminalValue(initial); var seeds = new List<DevelopmentState>();
            foreach (var card in cards.Where(c => c.Controller == 0 && c.Location == CardLocation.Hand).Distinct())
            {
                var next = CopyDevelopment(initial); next.Addition = card;
                next.Reserve.Remove(card); next.Acquired.Remove(card); next.Grave.Add(card);
                next.FusionActions.Remove(card); next.ResourceActions.Remove(card);
                next.Score = TerminalValue(next); seeds.Add(next);
            }
            SearchDevelopment(seeds, new SearchBudget(), state =>
            {
                if (state.Addition != null) result[state.Addition] = Math.Max(result[state.Addition], state.Score - baseline);
            });
            return result;
        }
    }

    public abstract partial class StoryLuckyExecutor
    {
        private StoryAiEvaluation.SummonPlan resourceSelection;
        private bool resourceCostSelected;

        internal void NoteMainAction(MainPhaseAction action)
        {
            if (action.Action == MainPhaseAction.MainAction.Summon || action.Action == MainPhaseAction.MainAction.SetMonster)
                evaluation.NoteNormalCommitment();
        }

        private void CommitResourceRoute(StoryAiEvaluation.SummonPlan route)
        {
            if (route.LevelChange != null)
            {
                effectIntent = new EffectIntent { Source = route.Card, Description = route.Description, Purpose = EffectPurpose.ChangeLevel,
                    LevelEffect = route.LevelChange.Effect, LevelPlan = route.LevelChange, Target = route.LevelChange.Target };
                CommitLevelIntent(effectIntent); resourceSelection = null; resourceCostSelected = false;
                return;
            }
            if (route.Extra != null) CommitExtraPlan(route.Extra);
            resourceSelection = route.Target == null && route.Choices?.Count > 0 != true ? null : route;
            resourceCostSelected = false;
        }

        private IList<ClientCard> PlannedResourceSelection(IList<ClientCard> cards, int min, int max, int hint)
        {
            var route = resourceSelection;
            if (route == null) return null;
            int solving = Duel.SolvingChainIndex;
            if (solving > 0 && solving <= Duel.CurrentChainInfo.Count && Duel.CurrentChainInfo[solving - 1].RelatedCard != route.Card) return null;
            if (route.Choices != null)
            {
                var selected = new List<ClientCard>(); var consumed = new List<StoryAiEvaluation.ComboSelection>();
                foreach (var choice in route.Choices.Where(c => c.Hint == hint || hint == HintMsg.Target ||
                    (c.Hint == HintMsg.Discard || c.Hint == HintMsg.ToGrave) && (hint == HintMsg.Discard || hint == HintMsg.ToGrave)))
                {
                    var match = cards.FirstOrDefault(c => c == choice.Card && !selected.Contains(c)) ?? cards.FirstOrDefault(c => c.Controller == 0 &&
                        !selected.Contains(c) && c.Id == choice.Card.Id && c.Location == (choice.Location != 0 ? choice.Location : choice.Card.Location));
                    if (match == null) continue;
                    selected.Add(match); consumed.Add(choice); if (selected.Count == max) break;
                }
                if (selected.Count < Math.Max(1, min)) return null;
                foreach (var card in selected) evaluation.NoteComboSelection(route.Card, card, hint);
                // Consume a private copy: cached search plans are immutable decisions.
                route = new StoryAiEvaluation.SummonPlan { Card = route.Card, Choices = route.Choices.Where(c => !consumed.Contains(c)).ToList() };
                resourceSelection = route.Choices.Count == 0 ? null : route;
                return selected;
            }
            if (min > 1 || max != 1) return null;
            if (!resourceCostSelected && route.Cost != null && (hint == HintMsg.ToGrave || hint == HintMsg.Discard))
            {
                var cost = cards.FirstOrDefault(c => c == route.Cost) ?? cards.FirstOrDefault(c =>
                    c.Id == route.Cost.Id && c.Controller == 0 && c.Location == CardLocation.Hand);
                if (cost == null) { resourceSelection = null; return null; }
                resourceCostSelected = true;
                return new List<ClientCard> { cost };
            }
            if (hint != route.TargetHint && hint != HintMsg.Target) return null;
            var target = cards.FirstOrDefault(c => c == route.Target) ?? cards.FirstOrDefault(c =>
                c.Controller == route.Target.Controller && c.Location == route.Target.Location && c.Id == route.Target.Id);
            resourceSelection = null;
            return target == null ? null : new List<ClientCard> { target };
        }
    }
}
