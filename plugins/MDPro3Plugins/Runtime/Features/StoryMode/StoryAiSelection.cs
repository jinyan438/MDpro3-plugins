using System;
using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public abstract partial class StoryLuckyExecutor
    {
        private static bool MaterialHint(int hint) => hint == HintMsg.FusionMaterial || hint == HintMsg.SynchroMaterial ||
            hint == HintMsg.XyzMaterial || hint == HintMsg.LinkMaterial || hint == HintMsg.Release || hint == HintMsg.Tribute;
        private static bool RemovalHint(int hint) => hint == HintMsg.Destroy || hint == HintMsg.Remove || hint == HintMsg.ToGrave ||
            hint == HintMsg.ReturnToHand || hint == HintMsg.ToDeck || hint == HintMsg.Disable || hint == HintMsg.Control || hint == HintMsg.Facedown;

        private float SelectionValue(ClientCard c, int hint)
        {
            if (MaterialHint(hint) || hint == HintMsg.Discard || hint == HintMsg.RemoveXyz || hint == HintMsg.DeattachFrom)
                return -evaluation.MaterialCost(c, hint);
            if (hint == HintMsg.SpSummon || hint == HintMsg.Summon || hint == HintMsg.ToField)
                return evaluation.SummonValue(c);
            if (hint == HintMsg.AddToHand) return evaluation.Acquire(c);
            if (hint == HintMsg.Equip) return c.Controller == 0 ? evaluation.Keep(c) : -evaluation.Threat(c);
            if (RemovalHint(hint))
            {
                if (c.Controller == 1)
                {
                    float value = evaluation.Threat(c);
                    if (!StoryAiEvaluation.Hidden(c))
                    {
                        if (hint == HintMsg.Disable && c.IsDisabled()) value -= 6000;
                        if (hint == HintMsg.ReturnToHand && c.IsExtraCard()) value += 1200;
                        if (hint == HintMsg.Destroy && (evaluation.Roles(c) & StoryAiEvaluation.Role.Grave) != 0) value -= 700;
                    }
                    return value;
                }
                if (c.Location == CardLocation.Deck && hint == HintMsg.ToGrave)
                    return (evaluation.Roles(c) & StoryAiEvaluation.Role.Grave) != 0 ? 4500 + evaluation.Acquire(c) * .15f : -evaluation.Keep(c);
                return -evaluation.MaterialCost(c, hint);
            }
            // An unknown prompt does not justify spending every offered card.
            return c.Controller == 1 ? evaluation.Threat(c) : -evaluation.MaterialCost(c, hint);
        }

        public override IList<ClientCard> OnSelectCard(IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            if (cards == null || min < 0 || max < min || min > cards.Count) return null;
            max = Math.Min(max, cards.Count);
            var planned = PlannedMaterials(cards, min, max, hint, cancelable);
            if (planned != null) return planned;
            // Dedicated effects and AI.Attack have already queued their exact targets/materials.
            if (AI.HaveSelectedCards()) return null;
            var effect = EffectSelection(cards, min, max, hint, cancelable);
            if (effect != null) return effect;
            if (cancelable && min > 0 && RemovalHint(hint) && cards.All(c => c.Controller == 0 &&
                (c.Location == CardLocation.MonsterZone || c.Location == CardLocation.SpellZone))) return new List<ClientCard>();
            var potential = new Dictionary<ClientCard, float>();
            if (IsOurMain && max == 1 && (hint == HintMsg.SpSummon || hint == HintMsg.ToField ||
                hint == HintMsg.AddToHand && Duel.MainPhase != null && Duel.MainPhase.SummonableCards.Count > 0))
                potential = evaluation.DevelopmentBonuses(cards.Where(c => hint != HintMsg.AddToHand ||
                    StoryAiEvaluation.Level(c) <= 4 && !StoryAiEvaluation.Has(c, CardType.SpSummon)).ToList());
            var ranked = cards.Select((card, index) => new { card, index, score = SelectionValue(card, hint) +
                (potential.TryGetValue(card, out float bonus) ? bonus : 0) })
                .OrderByDescending(x => x.score).ThenBy(x => x.index).ToList();
            int count = min;
            bool beneficial = hint == HintMsg.AddToHand || hint == HintMsg.SpSummon || hint == HintMsg.ToField;
            bool removal = RemovalHint(hint);
            // Optional extra costs/materials are never selected merely because max is large.
            while (count < max && (beneficial && ranked[count].score > 0 ||
                removal && ranked[count].card.Controller == 1 && ranked[count].score > 0)) count++;
            return ranked.Take(count).Select(x => x.card).ToList();
        }

        public override int OnSelectOption(IList<int> options)
        {
            if (Card != null && Card.Id == _CardId.LightningStorm && lightningStormOption >= 0)
            {
                int preferred = _CardId.LightningStorm * 16 + lightningStormOption;
                int index = options.IndexOf(preferred);
                if (index >= 0) return index;
            }
            // -1 allows GameAI to consume AI.SelectOption from a dedicated strategy.
            return -1;
        }

        public override CardPosition OnSelectPosition(int cardId, IList<CardPosition> positions)
        {
            if (positions == null || positions.Count == 0) return 0;
            var data = NamedCard.Get(cardId);
            if (data == null) return positions[0];
            bool canBattle = Duel.Player == 0 && Duel.Turn > 1 && Duel.Phase < DuelPhase.Main2;
            bool usefulAttack = canBattle && data.Attack > 0 && (Enemy.GetMonsterCount() == 0 ||
                Enemy.GetMonsters().Any(c => !StoryAiEvaluation.Hidden(c) && data.Attack > c.GetDefensePower()));
            CardPosition desired = usefulAttack || data.HasType(CardType.Link) || data.Attack < 0
                ? CardPosition.FaceUpAttack : CardPosition.FaceUpDefence;
            if (positions.Contains(desired)) return desired;
            if (positions.Contains(CardPosition.FaceUpAttack)) return CardPosition.FaceUpAttack;
            return positions[0];
        }

        public override int OnSelectPlace(int cardId, int player, CardLocation location, int available)
        {
            if (player == 0 && location == CardLocation.MonsterZone)
            {
                if (extraPlan != null && extraPlan.Destination.Id == cardId && extraPlan.Zone >= 0 && (available & (1 << extraPlan.Zone)) != 0)
                    return 1 << extraPlan.Zone;
                int unlinked = available & 31 & ~Bot.GetLinkedZones();
                if (NamedCard.Get(cardId)?.HasType(CardType.Link) != true && unlinked != 0)
                    for (int i = 0; i < 5; i++) if ((unlinked & (1 << i)) != 0) return 1 << i;
                return 0;
            }
            if (player != 0 || location != CardLocation.SpellZone) return 0;
            int best = -1, score = int.MinValue;
            for (int i = 0; i < 5; i++)
            {
                if ((available & (1 << i)) == 0) continue;
                int value = infiniteImpermanenceNegatedColumns.Contains(i) ? -100 : 0;
                if (Enemy.SpellZone[4 - i] != null && Enemy.SpellZone[4 - i].IsFacedown()) value -= 2;
                // Leave the outer zones available to a pendulum deck's scales.
                if (i == 0 || i == 4) value -= Bot.Hand.Any(c => StoryAiEvaluation.Has(c, CardType.Pendulum)) ? 4 : 0;
                if (value > score) { score = value; best = i; }
            }
            return best >= 0 ? 1 << best : 0;
        }

        private sealed class SumChoice
        {
            internal float Cost;
            internal List<ClientCard> Cards;
        }

        public override IList<ClientCard> OnSelectSum(IList<ClientCard> cards, int sum, int min, int max, int hint, bool mode)
        {
            // mode=true means exact. The >= protocol has additional minimality rules; retain
            // WindBot's solver there. Mandatory contributions have already been removed by it.
            if (!mode || AI.HaveSelectedCards() || cards == null || min < 0 || max < min || sum < 0 || sum > 32767 || cards.Count > 64) return null;
            max = Math.Min(max, cards.Count);
            var states = new Dictionary<long, SumChoice> { [0] = new SumChoice { Cards = new List<ClientCard>() } };
            foreach (var card in cards)
            {
                var next = new Dictionary<long, SumChoice>(states);
                foreach (var entry in states)
                {
                    int count = entry.Value.Cards.Count;
                    if (count >= max) continue;
                    int total = (int)(entry.Key & uint.MaxValue);
                    foreach (int weight in new[] { card.OpParam1, card.OpParam2 }.Distinct())
                    {
                        if (weight <= 0 || total + weight > sum) continue;
                        long key = ((long)(count + 1) << 32) | (uint)(total + weight);
                        float cost = entry.Value.Cost + evaluation.MaterialCost(card, hint) + 100;
                        if (extraPlan != null && extraPlan.Hint == hint && !extraPlan.Materials.Contains(card)) cost += 20000;
                        if (next.TryGetValue(key, out var previous) && previous.Cost <= cost) continue;
                        next[key] = new SumChoice { Cost = cost, Cards = entry.Value.Cards.Concat(new[] { card }).ToList() };
                    }
                }
                if (next.Count > 32768) return null;
                states = next;
            }
            return states.Where(p => (int)(p.Key & uint.MaxValue) == sum && p.Value.Cards.Count >= min)
                .OrderBy(p => p.Value.Cost).Select(p => (IList<ClientCard>)p.Value.Cards).FirstOrDefault();
        }
    }
}
