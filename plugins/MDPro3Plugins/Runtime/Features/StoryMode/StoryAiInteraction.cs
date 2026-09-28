using System;
using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public abstract partial class StoryLuckyExecutor
    {
        private ClientCard preferredResponse;
        private bool selectingChainResponse;
        private int preferredResponseDescription;
        private readonly HashSet<(ClientCard, int)> rankedResponses = new HashSet<(ClientCard, int)>();
        private readonly HashSet<int> ownResolvedDrawEffects = new HashSet<int>();

        // The native virtual callback omits descriptions. The plugin hook supplies the
        // complete legal offer, then leaves execution, forced effects and queues to GameAI.
        internal void PrepareChainResponses(IList<ClientCard> cards, IList<int> descriptions, IList<bool> forced)
        {
            ClearChainResponses();
            selectingChainResponse = true;
            if (!EnemyChain || cards.Count != descriptions.Count || forced.Any(x => x)) return;
            float best = float.MaxValue;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (!CandidatePreActivate(card, descriptions[i]) ||
                    !TryResponseCost(card, descriptions[i], out float cost)) continue;
                rankedResponses.Add((card, descriptions[i]));
                // Stable across the core's candidate order; two identical copies are equivalent.
                if (cost < best || cost == best && card.Id < preferredResponse.Id)
                {
                    best = cost; preferredResponse = card; preferredResponseDescription = descriptions[i];
                }
            }
        }

        internal void ClearChainResponses() { selectingChainResponse = false; preferredResponse = null; rankedResponses.Clear(); }

        private bool CandidatePreActivate(ClientCard candidate, int description)
        {
            // DefaultExecutor's spell/trap checks read the current Card property even
            // though OnPreActivate also takes a card argument. Hypothetical candidates
            // must have their own context, then restore the action being evaluated.
            var oldCard = Card; var oldType = Type; int oldDescription = ActivateDescription, oldTiming = CurrentTiming;
            try
            {
                SetCard(ExecutorType.Activate, candidate, description, oldTiming);
                return !DefaultCheckWhetherCardIsNegated(candidate) && base.OnPreActivate(candidate) && SpellColumnAllows(candidate);
            }
            finally { SetCard(oldType, oldCard, oldDescription, oldTiming); }
        }

        private bool SpellColumnAllows(ClientCard card) => !StoryAiEvaluation.Has(card, CardType.Spell | CardType.Trap) ||
            card.Location != CardLocation.Hand && card.Location != CardLocation.SpellZone ||
            card.Location == CardLocation.Hand && StoryAiEvaluation.Has(card, CardType.Field) ||
            !DefaultCheckWhetherSpellActivateWillBeNegated(card);

        private bool PlannedResponseAllowed(ClientCard card, int description) => preferredResponse == null ||
            !rankedResponses.Contains((card, description)) || card == preferredResponse && description == preferredResponseDescription;

        private bool TryResponseCost(ClientCard card, int description, out float cost)
        {
            cost = 0;
            if (!EnemyChain || DefaultCheckWhetherCardIsNegated(LastChain)) return false;
            switch (card.Id)
            {
                case _CardId.AshBlossom:
                    if (LastChain.IsCode(_CardId.MacroCosmos) || LastChain.HasSetcode(0x11e) && LastChain.Location == CardLocation.Hand) return false;
                    cost = 160; break;
                case _CardId.EffectVeiler:
                case _CardId.InfiniteImpermanence:
                case _CardId.BreakthroughSkill:
                    if (LastChain.Controller != 1 || LastChain.Location != CardLocation.MonsterZone || !LastChain.IsFaceup() ||
                        LastChain.IsDisabled() || !evaluation.CanTarget(LastChain, card)) return false;
                    cost = card.Id == _CardId.EffectVeiler ? 80 : card.Id == _CardId.BreakthroughSkill ? 70 : 110;
                    if (card.Location == CardLocation.Grave) cost -= 25;
                    break;
                case _CardId.GhostBelle: cost = 120; break;
                case _CardId.GhostOgreAndSnowRabbit:
                    if (LastChain.Controller != 1 || !LastChain.IsFaceup() || !CanDestroyByEffect(LastChain) ||
                        LastChain.Location != CardLocation.MonsterZone && LastChain.Location != CardLocation.SpellZone) return false;
                    if (LastChain.Location == CardLocation.SpellZone &&
                        !StoryAiEvaluation.Has(LastChain, CardType.Continuous | CardType.Field | CardType.Equip)) return false;
                    cost = LastChain.Location == CardLocation.SpellZone ? 100 : 280; break;
                case _CardId.CalledByTheGrave:
                    if (!StoryAiEvaluation.Has(LastChain, CardType.Monster) || DefaultCheckWhetherCardIdIsNegated(LastChain.Id) ||
                        !Enemy.Graveyard.Any(c => c.Id == LastChain.Id && evaluation.CanTarget(c, card))) return false;
                    cost = 170;
                    // Its name-wide restriction can also switch off our matching hand trap.
                    if (Bot.Hand.Any(c => c.Id == LastChain.Id)) cost += 70;
                    break;
                case _CardId.CrossoutDesignator:
                    if (DefaultCheckWhetherCardIdIsNegated(LastChain.Id) || !evaluation.KnownOwnDeckContains(LastChain.Id)) return false;
                    cost = 210; break;
                default:
                    var intent = DescribeEffect(card, description);
                    if (intent.Purpose != EffectPurpose.Negate || intent.MonsterOnly && !StoryAiEvaluation.Has(LastChain, CardType.Monster)) return false;
                    if (!EffectAllowed(card, description, out intent)) return false;
                    var fact = intent.Fact;
                    if (fact?.AttackCost > 0 && StoryAiEvaluation.Attack(card) < fact.AttackCost) return false;
                    cost = fact == null ? 300 : fact.AttackCost > 0 ? 30 : fact.FaceupOnce ? 250 :
                        fact.LinkCounters ? 75 : fact.Narrow ? 45 : fact.InstanceOnce ? 160 : 50;
                    if (fact?.CostFilter != null && intent.Cost != null) cost += evaluation.MaterialCost(intent.Cost, fact.CostHint) * .1f;
                    cost += intent.DiscardCount * 100;
                    break;
            }
            // Use an endangered resource while it is still live. This does not assume
            // destruction negates an activated normal Spell or an off-field effect.
            if (Duel.LastChainTargets.Contains(card)) cost -= 300;
            return true;
        }

        private static bool DrawPressureCard(int id) => id == _CardId.MaxxC || id == _CardId.MulcharmyFuwalos ||
            id == _CardId.MulcharmyPurulia || id == _CardId.MulcharmyNyalus;

        private bool StoryDrawPressure()
        {
            if (Duel.Player != 1 || ownResolvedDrawEffects.Contains(Card.Id) ||
                Duel.CurrentChainInfo.Any(c => c.ActivatePlayer == 0 && c.ActivateId == Card.Id)) return false;
            // Droll prevents either player adding from the Deck, including these draws.
            return !resolvedEffectIdList.Contains(_CardId.LockBird);
        }

        private int SummonDrawTax(ClientCard card, bool normal = false)
        {
            return evaluation.DrawTax(card.Location, normal);
        }

        private bool DrawPressureAllows(ClientCard card, StoryAiEvaluation.ExtraPlan plan)
        {
            int tax = SummonDrawTax(card);
            if (tax == 0 || !IsOurMain) return true;
            bool canBattle = !evaluation.BattleDamageBlocked && Duel.Turn > 1 && Duel.Phase == DuelPhase.Main1 && Duel.MainPhase?.CanBattlePhase == true;
            var survivors = Bot.GetMonsters().Where(c => plan == null || !plan.Materials.Contains(c)).ToList();
            int damage = survivors.Where(c => c.IsAttack() && !c.Attacked).Sum(StoryAiEvaluation.Attack) + StoryAiEvaluation.Attack(card);
            if (canBattle && Enemy.GetMonsterCount() == 0 && damage >= Enemy.LifePoints) return true;
            bool interaction = Bot.GetMonsters().Any(c => c.IsFaceup() && evaluation.LiveInteraction(c));
            // One draw for the first real interruption is better than passing on tokens.
            if (!interaction && evaluation.LiveInteraction(card) && (plan == null || plan.Gain > 250 * tax)) return true;
            if (Enemy.GetMonsterCount() > 0 && (plan?.Gain ?? 0) > 1600 * tax) return true;
            // Do not let fear of draws force a naked pass. Reassess after the first body.
            if (Bot.GetMonsterCount() == 0 && card.Location != CardLocation.Extra) return true;
            return false;
        }

        public override void OnMove(ClientCard card, int previousControler, int previousLocation, int currentControler, int currentLocation)
        {
            base.OnMove(card, previousControler, previousLocation, currentControler, currentLocation);
            evaluation.NoteResourceMove(card, previousLocation, currentLocation);
        }
    }
}
