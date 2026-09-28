using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        internal sealed class LevelPlan
        {
            internal StoryAiLevelEffects.Profile Effect;
            internal ClientCard Source, Target;
            internal List<ClientCard> Targets;
            internal int Level, Choice;
            internal float Gain;
        }
        private readonly HashSet<string> usedLevelEffects = new HashSet<string>();
        internal int LastLevelNodes { get; private set; }
        private static string LevelUse(ClientCard source, StoryAiLevelEffects.Profile effect) => effect.Limit.Length > 0 ?
            "name:" + effect.Limit : RuntimeHelpers.GetHashCode(source) + ":" + effect.Description;
        internal bool ModelledLevelAction(ClientCard source, int description) => StoryAiLevelEffects.Find(source, description)?.Trigger == false;
        private void NoteLevelEffect(ClientCard source, int description)
        {
            var effect = StoryAiLevelEffects.Find(source, description);
            if (effect?.Once == true) usedLevelEffects.Add(LevelUse(source, effect));
        }
        private bool LevelOffered(ClientCard source, StoryAiLevelEffects.Profile effect)
        {
            if (duel.MainPhase == null) return false;
            for (int i = 0; i < duel.MainPhase.ActivableCards.Count; i++)
                if (duel.MainPhase.ActivableCards[i] == source && effect.MatchesDescription(duel.MainPhase.ActivableDescs[i])) return true;
            return false;
        }
        private IEnumerable<Body> LevelTargets(DevelopmentState state, ClientCard source, StoryAiLevelEffects.Profile effect,
            IList<ClientCard> offered = null, bool bound = false)
        {
            return state.Board.Where(b => b.Card.Controller == 0 && (b.Fresh || b.Card.Location == CardLocation.MonsterZone && b.Card.IsFaceup()) &&
                !Has(b.Card, CardType.Xyz | CardType.Link) && (b.ProjectedLevel > 0 ? b.ProjectedLevel : Level(b.Card)) > 0 &&
                (offered == null || offered.Contains(b.Card)) && (bound || !effect.ExcludeSource || b.Card != source) &&
                (bound || effect.Scope != StoryAiLevelEffects.Scope.Self || b.Card == source) &&
                (bound || effect.Scope != StoryAiLevelEffects.Scope.Summoned || offered != null || duel.LastSummonedCards.Contains(b.Card)) &&
                (bound || effect.Scope != StoryAiLevelEffects.Scope.Target && effect.Scope != StoryAiLevelEffects.Scope.Summoned || CanTarget(b.Card, source)) &&
                (bound || effect.Filter.Matches(b.Card, b.ProjectedLevel > 0 ? b.ProjectedLevel : Level(b.Card))));
        }
        private IEnumerable<List<ClientCard>> LevelTargetSets(List<ClientCard> candidates, StoryAiLevelEffects.Profile effect, bool bound)
        {
            if (effect.Scope == StoryAiLevelEffects.Scope.All || bound)
            {
                if (candidates.Count > 0) yield return candidates;
                yield break;
            }
            for (int mask = 1; mask < 1 << candidates.Count; mask++)
            {
                var selected = candidates.Where((c, i) => (mask & 1 << i) != 0).ToList();
                if (selected.Count >= effect.Minimum && selected.Count <= effect.Maximum) yield return selected;
            }
        }
        private IEnumerable<DevelopmentState> LevelChoices(DevelopmentState state, ClientCard source, StoryAiLevelEffects.Profile effect,
            SearchBudget budget, bool root, int description, IList<ClientCard> offered = null, bool paid = false, bool bound = false,
            IList<int> numbers = null, IList<int> options = null)
        {
            if (budget.Exhausted || !paid && (state.LevelUses.Contains(LevelUse(source, effect)) ||
                (effect.From & ComboLocation(state, source)) == 0 || effect.Life > 0 && state.Life <= effect.Life)) yield break;
            var sourceBody = state.Board.FirstOrDefault(b => b.Card == source);
            if (!paid && (source.IsDisabled() || sourceBody != null && (sourceBody.EffectsBlocked || !sourceBody.Fresh && source.IsFacedown()))) yield break;
            var candidates = LevelTargets(state, source, effect, offered, bound).Select(b => b.Card).Distinct().ToList();
            foreach (var targets in LevelTargetSets(candidates, effect, bound))
                for (int choice = 0; choice < effect.Values.Length; choice++)
                {
                    if (budget.Exhausted) yield break;
                    budget.Checks++;
                    if (numbers != null && (effect.Numbers.Length <= choice || !numbers.Contains(effect.Numbers[choice])) ||
                        options != null && (effect.Options.Length <= choice || !options.Contains(effect.Options[choice]))) continue;
                    var affected = targets.Concat(effect.SelfAlso ? new[] { source } : new ClientCard[0]).Distinct().ToList();
                    var bodies = affected.Select(c => state.Board.FirstOrDefault(b => b.Card == c)).ToList();
                    if (bodies.Any(b => b == null || Has(b.Card, CardType.Xyz | CardType.Link))) continue;
                    var levels = bodies.Select(b => effect.Result(b.ProjectedLevel > 0 ? b.ProjectedLevel : Level(b.Card), choice)).ToList();
                    if (levels.Any(l => l <= 0) || bodies.Select((b, i) => levels[i] == (b.ProjectedLevel > 0 ? b.ProjectedLevel : Level(b.Card))).All(same => same)) continue;
                    var next = CopyDevelopment(state);
                    next.LevelUses.Add(LevelUse(source, effect));
                    if (!paid)
                    {
                        next.Life -= effect.Life;
                        if (effect.Cost == StoryAiLevelEffects.Cost.BanishSelf) SpendComboCard(next, source, true);
                        if (effect.Consume) SpendComboCard(next, source);
                    }
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        var changed = CopyBody(bodies[i]); changed.ProjectedLevel = levels[i];
                        changed.MaterialView = ProjectComboMaterial(changed.MaterialView ?? changed.Card, levels[i]);
                        int index = next.Board.IndexOf(bodies[i]);
                        if (index >= 0) next.Board[index] = changed;
                    }
                    var plan = new LevelPlan { Effect = effect, Source = source, Targets = targets, Target = targets[0],
                        Choice = choice, Level = levels[0] };
                    if (root) next.FirstSummon = new SummonPlan { Card = source, Description = description, Activation = true, Combo = true, LevelChange = plan };
                    next.Depth++; next.Score = TerminalValue(next); budget.Nodes++;
                    yield return next;
                }
        }
        private IEnumerable<DevelopmentState> LevelSuccessors(DevelopmentState state, SearchBudget budget)
        {
            foreach (var source in state.Board.Select(b => b.Card).Concat(state.Reserve.Where(c => InDevelopmentHand(state, c)))
                .Concat(state.Grave).Concat(state.Spells.Keys).Distinct())
                foreach (var effect in StoryAiLevelEffects.For(source).Where(p => !p.Trigger && p.Predictable))
                {
                    // Existing effects absent from the core offer may have spent
                    // a shared limit or have an unrepresented activation lock.
                    var body = state.Board.FirstOrDefault(b => b.Card == source);
                    if (body?.Fresh != true && !state.Acquired.Contains(source) && !LevelOffered(source, effect)) continue;
                    foreach (var next in LevelChoices(state, source, effect, budget, false, effect.Description)) yield return next;
                }
        }
        private void QueueLevelArrival(DevelopmentState state, ClientCard arrival, bool normal = false)
        {
            if (normal || Has(arrival, CardType.Link | CardType.Xyz)) return;
            foreach (var body in state.Board.Where(b => !b.EffectsBlocked && !b.Card.IsDisabled() && (b.Fresh || b.Card.IsFaceup())))
                foreach (var effect in StoryAiLevelEffects.For(body.Card).Where(p => p.SpecialArrival &&
                    (p.From & CardLocation.MonsterZone) != 0 && p.Filter.Matches(arrival, Level(arrival))))
                    if (!state.LevelUses.Contains(LevelUse(body.Card, effect))) state.PendingLevels.Add(Tuple.Create(body.Card, effect, arrival));
        }
        private IEnumerable<DevelopmentState> PendingLevelSuccessors(DevelopmentState state, SearchBudget budget)
        {
            var pending = state.PendingLevels[0]; var ready = CopyDevelopment(state); ready.PendingLevels.RemoveAt(0);
            ready.Score = TerminalValue(ready); yield return ready;
            foreach (var child in LevelChoices(ready, pending.Item1, pending.Item2, budget, false, pending.Item2.Description,
                new[] { pending.Item3 })) yield return child;
        }
        private float LevelContinuation(DevelopmentState state, SearchBudget budget)
        {
            float best = TerminalValue(state); state.Score = best;
            SearchDevelopment(new List<DevelopmentState> { state }, budget, s => best = Math.Max(best, s.Score));
            LastLevelNodes += budget.Nodes;
            return best;
        }
        internal LevelPlan PlanLevelEffect(ClientCard source, StoryAiLevelEffects.Profile effect, IList<ClientCard> targets = null,
            bool requireImprovement = true, bool paid = false, bool bound = false, IList<int> numbers = null, IList<int> options = null)
        {
            if (effect == null || duel.Player != 0 || duel.Phase != DuelPhase.Main1 && duel.Phase != DuelPhase.Main2) return null;
            LastLevelNodes = 0;
            var initial = InitialDevelopment(new ClientCard[0]); PrepareResolutionNormals(initial);
            var decline = CopyDevelopment(initial);
            decline.LevelUses.Add(LevelUse(source, effect));
            float baseline = requireImprovement ? LevelContinuation(decline, new SearchBudget()) : 0;
            float best = requireImprovement ? baseline + 100 : float.NegativeInfinity;
            var seeds = LevelChoices(initial, source, effect, new SearchBudget(), true, effect.Description, targets, paid, bound, numbers, options).ToList();
            LevelPlan result = null;
            // Divide one continuation budget equally across candidates. A broad
            // numeric or multi-target effect must not multiply the response budget.
            foreach (var seed in seeds)
            {
                var budget = new SearchBudget { NodeLimit = Math.Max(1, DevelopmentNodes / seeds.Count),
                    CheckLimit = Math.Max(1, DevelopmentChecks / seeds.Count) };
                float score = LevelContinuation(seed, budget);
                if (score <= best) continue;
                best = score; result = seed.FirstSummon.LevelChange; result.Gain = score - baseline;
            }
            return result;
        }
    }

    public abstract partial class StoryLuckyExecutor
    {
        private readonly List<EffectIntent> levelChangeIntents = new List<EffectIntent>();
        private void CommitLevelIntent(EffectIntent intent)
        {
            levelChangeIntents.RemoveAll(p => p.Source == intent.Source && p.LevelEffect == intent.LevelEffect);
            levelChangeIntents.Add(intent);
        }
        private bool PlanLevelEffect(EffectIntent intent)
        {
            // The unified main-phase search already compares competing roots.
            // Re-vetoing its winner against an equivalent second source causes
            // both physical copies to defer to one another forever.
            var route = !selectingChainResponse && IsOurMain && Duel.CurrentChain.Count == 0 ? MainSummonRoute() : null;
            var plan = route?.Card == intent.Source && route.LevelChange?.Effect == intent.LevelEffect ?
                route.LevelChange : evaluation.PlanLevelEffect(intent.Source, intent.LevelEffect);
            if (plan == null) return false;
            intent.LevelPlan = plan; intent.Target = plan.Target;
            return true;
        }
        private IList<ClientCard> SelectLevelTargets(IList<ClientCard> cards, int min, int max, int hint, EffectIntent intent)
        {
            var selected = levelChangeIntents.LastOrDefault(p => p.Source == intent.Source && p.LevelEffect == intent.LevelEffect);
            if (intent.LevelEffect == null || selected?.Source != intent.Source || selected.LevelEffect != intent.LevelEffect ||
                (hint != HintMsg.Target && hint != HintMsg.Faceup) || selected.LevelEffect.Scope == StoryAiLevelEffects.Scope.Self ||
                selected.LevelEffect.Scope == StoryAiLevelEffects.Scope.All) return null;
            var targets = selected.LevelPlan?.Targets;
            if (targets != null && targets.Count >= min && targets.Count <= max && targets.All(cards.Contains)) return targets;
            var plan = evaluation.PlanLevelEffect(selected.Source, selected.LevelEffect, cards, false, true);
            if (plan == null || plan.Targets.Count < min || plan.Targets.Count > max) return null;
            selected.LevelPlan = plan; selected.Target = plan.Target;
            return plan.Targets;
        }
        private EffectIntent SelectingLevelChoice()
        {
            int solving = Duel.SolvingChainIndex;
            if (solving > 0 && solving <= Duel.CurrentChainInfo.Count)
            {
                var chain = Duel.CurrentChainInfo[solving - 1];
                if (chain.ActivatePlayer == 0)
                    return levelChangeIntents.LastOrDefault(p => p.LevelPlan != null && chain.RelatedCard == p.Source &&
                        p.LevelEffect.MatchesDescription(chain.ActivateDescription));
            }
            else return levelChangeIntents.LastOrDefault(p => p.LevelPlan != null && p.LevelEffect.AtTarget && p.Source == Card && p.Description == ActivateDescription);
            return null;
        }
        internal int SelectLevelNumber(IList<int> numbers, int fallback)
        {
            var selected = SelectingLevelChoice();
            if (selected == null || selected.LevelEffect.Numbers.Length == 0 || !numbers.Any(selected.LevelEffect.Numbers.Contains)) return fallback;
            var plan = evaluation.PlanLevelEffect(selected.Source, selected.LevelEffect, selected.LevelPlan.Targets, false, true, true, numbers);
            int choice = plan?.Choice ?? selected.LevelPlan.Choice;
            int index = numbers.IndexOf(selected.LevelEffect.Numbers[choice]);
            return index >= 0 ? index : fallback;
        }
        private int SelectLevelOption(IList<int> options)
        {
            var selected = SelectingLevelChoice();
            if (selected == null || selected.LevelEffect.Options.Length == 0) return -1;
            var plan = evaluation.PlanLevelEffect(selected.Source, selected.LevelEffect, selected.LevelPlan.Targets, false, true, true, null, options);
            int choice = plan?.Choice ?? selected.LevelPlan.Choice;
            return options.IndexOf(selected.LevelEffect.Options[choice]);
        }
    }
}
