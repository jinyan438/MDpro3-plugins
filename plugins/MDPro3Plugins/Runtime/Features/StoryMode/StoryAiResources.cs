using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        private readonly Dictionary<int, int> drawTaxes = new Dictionary<int, int>();
        private readonly HashSet<int> spentInteractionNames = new HashSet<int>();
        private readonly HashSet<ClientCard> spentInteractionCards = new HashSet<ClientCard>();
        private readonly HashSet<ClientCard> exhaustedFaceupInteractions = new HashSet<ClientCard>();
        internal bool DrawLocked { get; private set; }
        private bool noDamageToEnemy, noDamageToBot;
        private int peaceThroughTurn = -1;
        private int shifterThroughTurn = -1;
        private bool GraveReplacementActive() => duel.Turn <= shifterThroughTurn ||
            Bot.GetSpells().Concat(Enemy.GetSpells()).Concat(Bot.GetMonsters()).Concat(Enemy.GetMonsters()).Any(c =>
                c.IsFaceup() && !c.IsDisabled() && (Facts(c).GraveRedirect || c.Controller == 1 && Facts(c).OpponentGraveRedirect));
        internal bool DestructionCanReachGrave => !GraveReplacementActive();
        internal bool BattleDamageBlocked => noDamageToEnemy || duel.Turn <= peaceThroughTurn;
        internal bool OwnBattleDamageBlocked => noDamageToBot || duel.Turn <= peaceThroughTurn;

        internal void NoteResolvedResourceEffect(int id, int player)
        {
            if (id == 94145021) DrawLocked = true;
            if (id == 91800273) shifterThroughTurn = System.Math.Max(shifterThroughTurn, duel.Turn + 1);
            if (Facts(id).OpponentDamageShield) { if (player == 0) noDamageToEnemy = true; else noDamageToBot = true; }
            // One Day of Peace protects BOTH players through the end of the
            // next opponent turn. Dark Ruler protects only the opposing player
            // of its activator, for this turn; those are different resources.
            if (Facts(id).BothDamageShield) peaceThroughTurn = System.Math.Max(peaceThroughTurn, duel.Turn + 1);
            if (player == 1 && (id == 23434538 || id == 42141493 || id == 84192580 || id == 87126721))
                drawTaxes[id] = (drawTaxes.TryGetValue(id, out int count) ? count : 0) + 1;
            developmentCacheKey = summonCacheKey = null;
        }

        internal int DrawTax(CardLocation origin, bool normal = false)
        {
            if (DrawLocked) return 0;
            int Count(int id) => drawTaxes.TryGetValue(id, out int count) ? count : 0;
            return (normal ? 0 : Count(23434538)) + (origin == CardLocation.Hand ? Count(84192580) : 0) +
                (!normal && (origin == CardLocation.Deck || origin == CardLocation.Extra) ? Count(42141493) : 0) +
                (!normal && (origin == CardLocation.Grave || origin == CardLocation.Removed) ? Count(87126721) : 0);
        }

        private float DrawPenalty(CardLocation origin, bool normal = false) => DrawTax(origin, normal) * 1600;

        internal void NoteInteractionUse(ClientCard card, int description)
        {
            var fact = Tactic(card, description);
            if (fact != null && InteractionFact(fact) && fact.Once)
            {
                if (fact.FaceupOnce) exhaustedFaceupInteractions.Add(card);
                if (fact.SharedLimit) spentInteractionNames.Add(CardIdentity(card));
                else if (fact.InstanceOnce) spentInteractionCards.Add(card);
            }
            developmentCacheKey = summonCacheKey = null;
        }

        internal void NoteResourceMove(ClientCard card, int previous, int current)
        {
            if (previous != current)
            {
                usedComboEffectInstances.RemoveWhere(p => p.Item1 == card);
                usedLevelEffects.RemoveWhere(key => key.StartsWith(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card) + ":", System.StringComparison.Ordinal));
            }
            if (previous == (int)CardLocation.SpellZone && current != previous)
            { usedComboInstances.Remove(card); spellActivationTurns.Remove(card); developmentCacheKey = summonCacheKey = null; }
            if (previous != (int)CardLocation.MonsterZone || current == previous) return;
            summonedThisTurn.Remove(card);
            exhaustedFaceupInteractions.Remove(card);
            temporaryDevelopmentBodies.Remove(card);
            bodyExtraSetcodes.Remove(card);
            banishOnLeaveBodies.Remove(card);
            comboSynchroBodies.Remove(card); comboUntributableUntil.Remove(card);
            usedComboInstances.Remove(card); liveFusionNames.Remove(card); pendingFusionNames.Remove(card);
            spentInteractionCards.Remove(card);
            developmentCacheKey = summonCacheKey = null;
        }

        private bool InteractionSpent(ClientCard card) => card.Controller == 0 && (spentInteractionNames.Contains(card.Id) ||
            spentInteractionCards.Contains(card) || exhaustedFaceupInteractions.Contains(card));

        private void ResetResourceTurn()
        {
            drawTaxes.Clear(); DrawLocked = false; noDamageToEnemy = noDamageToBot = false;
            spentInteractionNames.Clear(); spentInteractionCards.Clear();
            foreach (var card in comboUntributableUntil.Where(p => p.Value < duel.Turn || p.Key.Location != CardLocation.MonsterZone || p.Key.IsFacedown()).Select(p => p.Key).ToList())
                comboUntributableUntil.Remove(card);
            exhaustedFaceupInteractions.RemoveWhere(c => c.Location != CardLocation.MonsterZone || c.IsFacedown());
        }

        internal bool WorthKnownSummonEffect(ClientCard card)
        {
            // Venus is repeatable but each resolution spends LP and exposes one draw
            // window. Search from its actual remaining resources before buying a body.
            if (!ComboProfiles(card).Any(e => e.Kind == ComboKind.Recruit && !e.Once) || DrawTax(CardLocation.Deck) + DrawTax(CardLocation.Hand) == 0) return true;
            var initial = InitialDevelopment(new ClientCard[0]);
            float baseline = TerminalValue(initial), best = baseline;
            var budget = new SearchBudget();
            var seeds = ComboProfiles(card).Where(e => !e.Trigger).SelectMany(e => ComboChoices(initial, card, e, budget, true, 0)).ToList();
            SearchDevelopment(seeds, budget, state => { if (state.Score > best) best = state.Score; });
            return best > baseline + 100;
        }
    }
}
