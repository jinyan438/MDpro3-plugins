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
        private sealed class FusionSpell
        {
            internal bool Instant, Once, Grave, Banish, Persistent, Layered, ExternalOnExtraOpponent;
            internal int DestinationSet, ExternalSet, Life, MaximumLevel = 99, MinimumMaterials;
        }
        private readonly Dictionary<int, FusionSpell> fusionSpells = new Dictionary<int, FusionSpell>();
        private static readonly Dictionary<int, FusionSpell> fusionFacts = BuildFusionFacts();
        private FusionSpell ReadFusionSpell(ClientCard source)
        {
            if (fusionSpells.TryGetValue(source.Id, out var cached)) return cached;
            fusionFacts.TryGetValue(CardIdentity(source), out var result);
            if (result == null && Has(source, CardType.Spell) && !Has(source, CardType.Continuous | CardType.Field | CardType.Equip))
            {
                string text = (source.Data?.Description ?? "").Trim();
                if (Regex.IsMatch(text,
                    @"^(?:①[:：]\s*)?(?:自己的手卡·场上的怪兽作为融合素材，把1只融合怪兽融合召唤。|Fusion Summon 1 Fusion Monster from your Extra Deck, using monsters from your hand or field as Fusion Material\.)$", RegexOptions.IgnoreCase))
                    result = new FusionSpell();
            }
            if (fusionSpells.Count < 4096) fusionSpells[source.Id] = result;
            return result;
        }
        internal bool UsesBanishFusion(ClientCard card) => ReadFusionSpell(card)?.Banish == true || ReadFusionSpell(card)?.Layered == true;

        internal bool ModelledFusionAction(ClientCard source, int description) =>
            (source.Location == CardLocation.Hand || source.Location == CardLocation.SpellZone) &&
            (description == 0 || description == source.Id * 16 || description == CardIdentity(source) * 16) && ReadFusionSpell(source) != null &&
            (!ReadFusionSpell(source).Persistent || source.Location == CardLocation.SpellZone && source.IsFaceup() && !source.IsDisabled());

        private static bool RequiresPrintedFusionMaterials(ClientCard destination) => Regex.IsMatch(destination.Data?.Description ?? "",
            @"用以上记的卡为融合素材的融合召唤才能|Must be Fusion Summoned with the above Fusion Materials|Must first be Fusion Summoned with the above", RegexOptions.IgnoreCase);

        private string DevelopmentFusionName(DevelopmentState state, ClientCard card)
        {
            int code = state.Board.FirstOrDefault(b => b.Card == card)?.FusionName ?? 0;
            return code == 0 ? null : YGOSharp.OCGWrapper.NamedCard.Get(code)?.Name;
        }

        private IEnumerable<List<ClientCard>> FusionCombinations(DevelopmentState state, List<ClientCard> pool, ClientCard destination, Recipe recipe,
            HashSet<ClientCard> external, SearchBudget budget, int maximumExternal = 1, int minimum = 0)
        {
            var chosen = new List<ClientCard>();
            IEnumerable<List<ClientCard>> Walk(int start, int outside)
            {
                if (budget.Exhausted) yield break;
                if (chosen.Count >= Math.Max(recipe.Min, minimum))
                {
                    budget.Checks++;
                    if (AssignNamedMaterials(recipe.Requirements, chosen, c => DevelopmentFusionName(state, c))) yield return new List<ClientCard>(chosen);
                }
                if (chosen.Count >= recipe.Max) yield break;
                for (int i = start; i < pool.Count; i++)
                {
                    if (budget.Exhausted) yield break;
                    int more = external.Contains(pool[i]) ? 1 : 0;
                    if (outside + more > maximumExternal) continue;
                    chosen.Add(pool[i]);
                    foreach (var materials in Walk(i + 1, outside + more)) yield return materials;
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }
            return Walk(0, 0);
        }

        private IEnumerable<DevelopmentState> FusionChoices(DevelopmentState state, ClientCard source, SearchBudget budget,
            bool root, int description = 0, ClientCard offeredDestination = null)
        {
            var spell = ReadFusionSpell(source);
            if (spell?.Persistent == true && (!state.Spells.ContainsKey(source) || state.ComboInstances.Contains(source) || source.IsDisabled())) yield break;
            if (spell == null || !root && !InDevelopmentHand(state, source) && !state.Spells.ContainsKey(source) ||
                !root && spell.Once && state.Credited.Contains(CardIdentity(source)) ||
                !root && (DevelopmentSummonsBlocked(state) || state.SynchroOnly) || state.Life <= spell.Life) yield break;
            bool outsideAllowed = spell.ExternalOnExtraOpponent && state.Enemy.Any(c => c.IsFaceup() && c.LastLocation == CardLocation.Extra);
            var external = new HashSet<ClientCard>();
            if (outsideAllowed)
                foreach (var card in state.Reserve.Where(c => c.Location == CardLocation.Deck && !state.Acquired.Contains(c) && c.HasSetcode(spell.ExternalSet))
                    .Concat(state.Extra.Where(c => c.HasSetcode(spell.ExternalSet)))) external.Add(card);
            if (spell.Layered && state.Enemy.Count > 0)
                foreach (var card in state.Extra.Where(c => Has(c, CardType.Monster))) external.Add(card);
            var own = state.Board.Select(b => b.Card).Concat(spell.Grave ? state.Grave.Where(c => Has(c, CardType.Monster)) :
                state.Reserve.Where(c => InDevelopmentHand(state, c) && Has(c, CardType.Monster))).ToList();
            foreach (var destination in state.Extra.Where(c => Has(c, CardType.Fusion) && (spell.DestinationSet == 0 || c.HasSetcode(spell.DestinationSet)) &&
                (offeredDestination == null || c == offeredDestination) && ExtraDestinationAllowed(state, c)).OrderBy(c => c.Id))
            {
                if (budget.Exhausted) yield break;
                var recipe = MaterialRecipe(destination);
                IEnumerable<List<ClientCard>> combinations;
                if (spell.Instant)
                {
                    if (Level(destination) > spell.MaximumLevel || RequiresPrintedFusionMaterials(destination)) continue;
                    combinations = new[] { new List<ClientCard>() };
                }
                else
                {
                    if (recipe?.Requirements == null) continue;
                    var pool = own.Concat(external).Where(c => c != destination && Has(c, CardType.Monster) &&
                        MaterialPermits(c, destination) && recipe.Requirements.Any(r => r.MatchesNamed(c, DevelopmentFusionName(state, c)))).Distinct().OrderBy(c => c.Id).ToList();
                    combinations = FusionCombinations(state, pool, destination, recipe, external, budget,
                        spell.Layered ? state.Enemy.Count : 1, spell.MinimumMaterials);
                }
                foreach (var materials in combinations)
                {
                    if (budget.Exhausted) yield break;
                    if (DuplicateTerminalLoss(destination, materials, BoardValue(destination))) continue;
                    int lifeLoss = spell.Layered ? materials.Where(external.Contains).Sum(c => Math.Max(0, c.Data?.Attack ?? 0)) : 0;
                    if (lifeLoss >= state.Life) continue;
                    var next = CopyDevelopment(state);
                    next.FusionActions.Remove(source);
                    if (spell.Persistent) next.ComboInstances.Add(source);
                    else
                    {
                        next.Reserve.Remove(source); next.Acquired.Remove(source); next.Spells.Remove(source);
                        next.Grave.Add(source); next.Credit -= HandCommitment(source);
                    }
                    next.Life -= lifeLoss; next.Credit -= lifeLoss * .2f;
                    next.Credited.Add(CardIdentity(source)); next.Depth++;
                    foreach (var material in materials)
                    {
                        var body = state.Board.FirstOrDefault(b => b.Card == material);
                        if (body != null) { next.Grave.AddRange(body.OverlayCards); next.ComboInstances.Remove(material); next.ComboEffectInstances.RemoveWhere(p => p.Item1 == material); }
                        if (InDevelopmentHand(state, material)) next.Credit -= HandCommitment(material);
                        if (external.Contains(material) && material.Location == CardLocation.Deck) next.DeckCount--;
                        next.Board.RemoveAll(b => b.Card == material); next.Reserve.Remove(material); next.Acquired.Remove(material);
                        next.Extra.Remove(material);
                        next.Grave.Remove(material);
                        bool banish = spell.Banish || spell.Layered && external.Contains(material);
                        if (!banish && !Has(material, CardType.Token) && !(Has(material, CardType.Pendulum) && state.Board.Any(b => b.Card == material)) &&
                            !state.Board.Any(b => b.Card == material && b.BanishOnLeave)) next.Grave.Add(material);
                        if (!banish && next.Grave.Contains(material) && HasFusionMaterialTrigger(material)) next.PendingFusionMaterials.Add(material);
                    }
                    int zone = DevelopmentPlace(next, destination, true);
                    if (zone < 0 || next.DeckCount < 0) continue;
                    next.Board.Add(new Body { Card = destination, Attack = Attack(destination), Zone = zone,
                        Fresh = true, FromExtra = true, CannotAttack = spell.Instant, Temporary = spell.Instant });
                    QueueLevelArrival(next, destination, false);
                    next.Extra.Remove(destination); next.ProperlySummoned.Add(destination);
                    next.Life -= spell.Life; next.Credit -= spell.Life * .2f;
                    next.Credit -= DrawPenalty(CardLocation.Extra) + SummonExposure(state);
                    QueueDevelopmentSearch(next, destination, false, true);
                    foreach (var effect in ComboProfiles(destination).Where(e => e.SummonTrigger && e != ReadResourceEffect(destination)?.Combo && (e.ExtraSummonTrigger || e.SpecialTrigger)))
                        next.PendingCombos.Add(Tuple.Create(destination, effect));
                    if (next.Credited.Add(destination.Id) && next.PendingSearch == null && !next.PendingCombos.Any(p => p.Item1 == destination)) next.Credit += ImmediatePayoff(destination, next) * .7f;
                    if (root)
                    {
                        var plan = new ExtraPlan { Destination = destination, Materials = materials, Hint = HintMsg.FusionMaterial, Zone = zone };
                        next.FirstSummon = new SummonPlan { Card = source, Activation = true, Description = description, Extra = plan,
                            Target = destination, TargetHint = HintMsg.SpSummon };
                    }
                    next.Score = TerminalValue(next); budget.Nodes++; yield return next;
                }
            }
        }

        private IEnumerable<DevelopmentState> FusionSuccessors(DevelopmentState state, SearchBudget budget)
        {
            foreach (var source in state.FusionActions.OrderBy(c => c.Id))
                foreach (var next in FusionChoices(state, source, budget, false)) yield return next;
        }

        internal ExtraPlan PlanResolvedFusion(ClientCard source, IList<ClientCard> destinations)
        {
            if (source == null || ReadFusionSpell(source) == null) return null;
            var initial = InitialDevelopment(new ClientCard[0]);
            PrepareResolutionNormals(initial);
            initial.FusionActions.Remove(source);
            var budget = new SearchBudget(); var seeds = new List<DevelopmentState>();
            foreach (var destination in destinations)
                seeds.AddRange(FusionChoices(initial, source, budget, true, offeredDestination: destination));
            ExtraPlan result = null; float best = float.MinValue;
            Action<DevelopmentState> consider = state =>
            {
                if (state.FirstSummon?.Extra != null && state.Score > best)
                { best = state.Score; result = state.FirstSummon.Extra; result.Gain = best; }
            };
            foreach (var seed in seeds) consider(seed);
            SearchDevelopment(seeds, budget, consider);
            return result;
        }
    }
}
