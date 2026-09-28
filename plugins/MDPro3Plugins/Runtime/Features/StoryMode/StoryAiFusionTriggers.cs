using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        private readonly Dictionary<ClientCard, int> bodyExtraSetcodes = new Dictionary<ClientCard, int>();
        private readonly HashSet<ClientCard> banishOnLeaveBodies = new HashSet<ClientCard>();
        private static bool HasFusionMaterialTrigger(ClientCard card) =>
            ComboProfiles(card).Any(e => e.SentEffectTrigger || e.FusionMaterialTrigger);

        private IEnumerable<DevelopmentState> FusionMaterialSuccessors(DevelopmentState state, SearchBudget budget)
        {
            if (state.PendingFusionMaterials.Count == 0) yield break;
            var source = state.PendingFusionMaterials[0];
            bool effectOnly = state.EffectSentOnly.Contains(source);
            var next = CopyDevelopment(state); next.PendingFusionMaterials.RemoveAt(0); next.EffectSentOnly.Remove(source);
            next.Score = TerminalValue(next); yield return next;
            if (!next.Grave.Contains(source)) yield break;
            foreach (var effect in ComboProfiles(source).Where(e => e.SentEffectTrigger || !effectOnly && e.FusionMaterialTrigger))
                foreach (var child in ComboChoices(next, source, effect, budget, false, 0)) yield return child;
        }

        private bool ExtraDestinationAllowed(DevelopmentState state, ClientCard destination) =>
            (!state.SynchroOnly || Has(destination, CardType.Synchro)) && (!state.XyzOnly || Has(destination, CardType.Xyz)) && state.Board.All(b =>
                (!b.SynchroOnly || Has(destination, CardType.Synchro)) && (b.ExtraSetcode == 0 || destination.HasSetcode(b.ExtraSetcode)));
    }
}
