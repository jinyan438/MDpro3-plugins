using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public abstract partial class StoryLuckyExecutor
    {
        private enum EffectPurpose { Unknown, Safe, TargetRemoval, EnemyRemoval, BoardWipe, Negate, LinkAttack, LinkEquip, RevivalSwap, LinkBanishCost, FairyTribute, QuickLink }
        private sealed class EffectIntent
        {
            internal ClientCard Source, Target, Cost;
            internal int Description, Hint = HintMsg.Destroy;
            internal EffectPurpose Purpose;
            internal StoryAiEvaluation.ExtraPlan Summon;
        }
        private EffectIntent effectIntent;
        private readonly HashSet<int> accesscodeAttributes = new HashSet<int>();

        // Description indices come from the installed Lua scripts, not printed paragraph
        // numbers (passive effects and copied effects make those different).
        private EffectIntent DescribeEffect(ClientCard card, int description)
        {
            var intent = new EffectIntent { Source = card, Description = description };
            int sourceId = description >= 16000 ? description / 16 : card.Id;
            int index = description >= 16000 ? description % 16 : 0;
            switch (sourceId)
            {
                case 84815190:
                    intent.Purpose = index == 0 ? EffectPurpose.TargetRemoval : index == 1 ? EffectPurpose.Negate :
                        index == 2 ? EffectPurpose.RevivalSwap : EffectPurpose.Unknown; return intent;
                case 55794644: intent.Purpose = EffectPurpose.TargetRemoval; return intent;
                case 63101468:
                    intent.Purpose = index == 1 ? EffectPurpose.TargetRemoval : EffectPurpose.Safe;
                    intent.Hint = HintMsg.Remove; return intent;
                case 86066372:
                    intent.Purpose = index == 0 ? EffectPurpose.LinkAttack : EffectPurpose.LinkBanishCost; return intent;
                case 27548199:
                    intent.Purpose = index == 0 ? EffectPurpose.LinkEquip : EffectPurpose.Negate; return intent;
                case 4280258: intent.Purpose = EffectPurpose.Negate; return intent;
                case 98127546: intent.Purpose = index == 0 ? EffectPurpose.Safe : EffectPurpose.Negate; return intent;
                case 90290572:
                    intent.Purpose = index == 1 ? EffectPurpose.FairyTribute : EffectPurpose.Safe; return intent;
                case 65741786: intent.Purpose = EffectPurpose.QuickLink; return intent;
                case 73580471:
                    intent.Purpose = index == 0 ? EffectPurpose.BoardWipe : EffectPurpose.EnemyRemoval; return intent;
                case 46772449: intent.Purpose = EffectPurpose.BoardWipe; return intent;
                case 38342335: intent.Purpose = EffectPurpose.TargetRemoval; intent.Hint = HintMsg.ToDeck; return intent;
                case 2857636: intent.Purpose = EffectPurpose.TargetRemoval; return intent;
                case 21887175:
                    intent.Purpose = card.Location == CardLocation.Grave ? EffectPurpose.EnemyRemoval : EffectPurpose.Safe;
                    intent.Hint = HintMsg.ToDeck; return intent;
                case 74586817: case 50588353: case 48589580: intent.Purpose = EffectPurpose.Safe; return intent;
            }
            string text = (sourceId == card.Id ? card.Data : NamedCard.Get(sourceId))?.Description ?? "";
            // Recognize actual activated removal clauses, not "cannot be destroyed" or
            // "if this card is destroyed". Ambiguous multiple effects stay conservative.
            bool targeted = Regex.IsMatch(text, @"target\s+.*?(?:on the field|your opponent controls).*?[;:].*?(?:destroy|banish|return)", RegexOptions.IgnoreCase | RegexOptions.Singleline) ||
                Regex.IsMatch(text, @"以[^。\n]*(?:场上|場上)[^。\n]*(?:为对象|為對象)[^\n①-⑳]*(?:破坏|破壞|除外|回到)");
            if (targeted)
            {
                intent.Purpose = EffectPurpose.TargetRemoval;
                if (StoryAiEvaluation.Contains(text, "banish", "除外")) intent.Hint = HintMsg.Remove;
                if (StoryAiEvaluation.Contains(text, "return", "回到", "返回")) intent.Hint = HintMsg.ToDeck;
            }
            else if (Regex.IsMatch(text, @"(?:destroy|破坏|破壞).{0,16}(?:all cards on the field|场上的卡全部|場上的卡全部)", RegexOptions.IgnoreCase) ||
                StoryAiEvaluation.Contains(text, "场上的卡全部破坏", "場上的卡全部破壞")) intent.Purpose = EffectPurpose.BoardWipe;
            return intent;
        }

        private IEnumerable<ClientCard> RemovalTargets(EffectIntent intent)
        {
            var targets = Enemy.GetMonsters().Concat(Enemy.GetSpells());
            if (intent.Source.Id == 2857636) targets = targets.Where(c => c.Location == CardLocation.SpellZone);
            if (intent.Purpose != EffectPurpose.EnemyRemoval && intent.Purpose != EffectPurpose.LinkBanishCost)
                targets = targets.Where(c => evaluation.CanTarget(c, intent.Source));
            if (intent.Hint == HintMsg.Destroy) targets = targets.Where(CanDestroyByEffect);
            return targets;
        }

        private static bool CanDestroyByEffect(ClientCard c) => StoryAiEvaluation.Hidden(c) || c.IsDisabled() ||
            !Regex.IsMatch(c.Data?.Description ?? "", @"cannot be destroyed by (?:battle or )?card effects|不会被(?:战斗[·・和以及]*|战斗以及)?效果破坏|不會被(?:戰鬥[·・和以及]*|戰鬥以及)?效果破壞", RegexOptions.IgnoreCase);

        private bool EffectAllowed(ClientCard card, int description, out EffectIntent intent)
        {
            intent = DescribeEffect(card, description);
            switch (intent.Purpose)
            {
                case EffectPurpose.Negate:
                    return EnemyChain && !DefaultCheckWhetherCardIsNegated(LastChain) &&
                        (card.Id != 4280258 || StoryAiEvaluation.Has(LastChain, CardType.Monster));
                case EffectPurpose.TargetRemoval:
                case EffectPurpose.EnemyRemoval:
                case EffectPurpose.LinkBanishCost:
                case EffectPurpose.FairyTribute:
                    intent.Target = RemovalTargets(intent).OrderByDescending(evaluation.Threat).FirstOrDefault();
                    if (intent.Target == null) return false;
                    if (intent.Purpose == EffectPurpose.LinkBanishCost)
                    {
                        intent.Cost = Bot.Graveyard.Concat(Bot.GetMonsters()).Where(c => StoryAiEvaluation.Has(c, CardType.Link) &&
                            !accesscodeAttributes.Contains(StoryAiEvaluation.Attribute(c)))
                            .OrderBy(c => evaluation.MaterialCost(c, HintMsg.Remove)).FirstOrDefault();
                        // Preserve the finisher; banishing itself also loses its attack boost.
                        if (intent.Cost == null || intent.Cost == card) return false;
                        if (intent.Cost.Location == CardLocation.MonsterZone &&
                            evaluation.BoardValue(intent.Cost) + 300 >= evaluation.Threat(intent.Target)) return false;
                    }
                    if (intent.Purpose == EffectPurpose.FairyTribute)
                    {
                        intent.Cost = Bot.GetMonsters().Where(c => (StoryAiEvaluation.Race(c) & (int)CardRace.Fairy) != 0)
                            .OrderBy(c => evaluation.BoardValue(c)).FirstOrDefault();
                        if (intent.Cost == null || evaluation.BoardValue(intent.Cost) + 300 >= evaluation.Threat(intent.Target)) return false;
                    }
                    if (card.Id == 38342335 || card.Id == 2857636)
                    {
                        var discard = Bot.Hand.OrderBy(c => evaluation.MaterialCost(c, HintMsg.Discard)).FirstOrDefault();
                        if (discard == null || evaluation.MaterialCost(discard, HintMsg.Discard) > evaluation.Threat(intent.Target) + 500) return false;
                    }
                    return true;
                case EffectPurpose.BoardWipe:
                    float own = Bot.GetMonsters().Where(c => card.Id != 46772449 || c != card).Sum(evaluation.BoardScore) + Bot.GetSpells().Sum(evaluation.Threat);
                    float enemy = Enemy.GetMonsters().Sum(evaluation.BoardScore) + Enemy.GetSpells().Sum(evaluation.Threat);
                    return enemy > own + 700;
                case EffectPurpose.RevivalSwap:
                    intent.Target = Bot.Graveyard.Where(c => StoryAiEvaluation.Has(c, CardType.Monster) && StoryAiEvaluation.Level(c) <= 9 && c.IsCanRevive())
                        .OrderByDescending(evaluation.BoardScore).FirstOrDefault();
                    return intent.Target != null && evaluation.BoardValue(intent.Target) > evaluation.BoardValue(card) + 500;
                case EffectPurpose.QuickLink:
                    intent.Summon = Bot.ExtraDeck.Where(c => StoryAiEvaluation.Has(c, CardType.Link)).Select(c => evaluation.PlanExtra(c, card))
                        .Where(p => p != null).OrderByDescending(p => p.Gain).FirstOrDefault();
                    return intent.Summon != null;
                default: return true;
            }
        }

        private void CommitEffect(EffectIntent intent)
        {
            effectIntent = intent;
            evaluation.NoteDevelopmentEffect(intent.Description);
            CommitExtraPlan(intent.Summon);
        }

        private EffectIntent SelectingEffect()
        {
            // During resolution, the latest effect selected may belong to another chain link.
            int solving = Duel.SolvingChainIndex;
            if (solving > 0 && solving <= Duel.CurrentChainInfo.Count)
            {
                var chain = Duel.CurrentChainInfo[solving - 1];
                if (chain.ActivatePlayer == 0 && chain.RelatedCard != null)
                    return DescribeEffect(chain.RelatedCard, chain.ActivateDescription);
                return null;
            }
            return effectIntent != null && effectIntent.Source == Card && effectIntent.Description == ActivateDescription ? effectIntent : null;
        }

        private IList<ClientCard> EffectSelection(IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            var intent = SelectingEffect();
            if (intent == null) return null;
            if (intent.Purpose == EffectPurpose.QuickLink && hint == HintMsg.SpSummon)
            {
                var revised = cards.Where(c => c.Location == CardLocation.Extra && StoryAiEvaluation.Has(c, CardType.Link))
                    .Select(c => evaluation.PlanExtra(c, intent.Source)).Where(p => p != null).OrderByDescending(p => p.Gain).FirstOrDefault();
                if (revised != null) { CommitExtraPlan(revised); return new List<ClientCard> { revised.Destination }; }
                if (cancelable) return new List<ClientCard>();
            }
            if (intent.Purpose == EffectPurpose.LinkAttack && hint == HintMsg.Target ||
                intent.Purpose == EffectPurpose.LinkEquip && (hint == HintMsg.Equip || hint == HintMsg.Target))
                return cards.OrderByDescending(c => StoryAiEvaluation.LinkRating(c) * 1000 + StoryAiEvaluation.Attack(c) * .1f).Take(Math.Max(1, min)).ToList();
            if (intent.Purpose == EffectPurpose.RevivalSwap && hint == HintMsg.SpSummon)
                return cards.OrderByDescending(evaluation.BoardScore).Take(min).ToList();
            if (intent.Purpose == EffectPurpose.FairyTribute && (hint == HintMsg.Release || hint == HintMsg.Tribute))
            {
                if (intent.Cost != null && cards.Contains(intent.Cost)) return new List<ClientCard> { intent.Cost };
                if (cancelable) return new List<ClientCard>();
                return cards.OrderBy(evaluation.BoardScore).Take(min).ToList();
            }
            if (intent.Purpose == EffectPurpose.LinkBanishCost && hint == HintMsg.Remove)
            {
                var cost = cards.Where(c => c != intent.Source && !accesscodeAttributes.Contains(StoryAiEvaluation.Attribute(c)))
                    .OrderBy(c => evaluation.MaterialCost(c, hint)).FirstOrDefault();
                if (cost == null) return cancelable ? new List<ClientCard>() : null;
                accesscodeAttributes.Add(StoryAiEvaluation.Attribute(cost));
                return new List<ClientCard> { cost };
            }
            if ((intent.Purpose == EffectPurpose.TargetRemoval || intent.Purpose == EffectPurpose.EnemyRemoval ||
                intent.Purpose == EffectPurpose.LinkBanishCost || intent.Purpose == EffectPurpose.FairyTribute) && hint == intent.Hint)
            {
                // Some scripts use the same hint for a banish cost and its subsequent target.
                if (!cards.Any(c => c.Location == CardLocation.MonsterZone || c.Location == CardLocation.SpellZone)) return null;
                var targets = cards.Where(c => c.Controller == 1 && (hint != HintMsg.Destroy || CanDestroyByEffect(c)))
                    .OrderByDescending(evaluation.Threat).Take(max).ToList();
                if (targets.Count >= min) return targets;
                if (cancelable) return new List<ClientCard>();
            }
            return null;
        }

        public override void OnChainEnd()
        {
            base.OnChainEnd();
            effectIntent = null;
            CommitExtraPlan(null);
        }

        public override void OnNewPhase()
        {
            base.OnNewPhase();
            effectIntent = null;
            CommitExtraPlan(null);
        }
    }
}
