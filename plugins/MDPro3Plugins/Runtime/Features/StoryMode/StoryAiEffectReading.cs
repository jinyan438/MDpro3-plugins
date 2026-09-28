using System;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public abstract partial class StoryLuckyExecutor
    {
        private static bool MatchesEffect(string text, string pattern) => Regex.IsMatch(text, pattern,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private void ReadGenericEffect(EffectIntent intent, string text, int index)
        {
            // Lua description indices are NOT printed paragraph numbers. Never borrow
            // a destructive clause from a different effect to veto a search/trigger.
            if (index != 0 || Regex.Matches(text, "[①-⑳]").Count > 1 ||
                Regex.Matches(text, @"[:：]").Count > 1) return;
            // A single effect can negate the effect rather than its activation,
            // or stop an attack without negating any chain link.
            if (MatchesEffect(text, @"negate (?:the|that|this) attack|(?:那次|这次|那個|那次|這次)(?:的)?攻击无效|(?:那次|這次)(?:的)?攻擊無效"))
            { intent.Purpose = EffectPurpose.StopAttack; return; }
            var target = Regex.Match(text,
                @"\btarget\s+(?:up to\s+)?(?:1|2|one|two)\s+(?<target>[^;:]+)[;:]\s*(?<result>[^.]+)",
                RegexOptions.IgnoreCase);
            if (!target.Success)
                target = Regex.Match(text, @"以(?<target>[^。\n①-⑳]*?)(?:为对象|為對象)(?:才能发动|才能發動)?[。．，,:：]*(?<result>[^①-⑳\n]+)");
            if (target.Success)
            {
                string subject = target.Groups["target"].Value, result = target.Groups["result"].Value;
                var count = Regex.Match(target.Value, @"\btarget\s+(2|two)\b|(?:场上|場上)([2-5])(?:张|張|只)", RegexOptions.IgnoreCase);
                if (count.Success && !StoryAiEvaluation.Contains(subject, "最多", "至多"))
                    intent.MinimumTargets = count.Groups[2].Success ? int.Parse(count.Groups[2].Value) : 2;
                // Graveyard costs/revivals and self-targeted buffs need a different policy.
                if (StoryAiEvaluation.Contains(subject, "grave", "gy", "墓地", "hand", "手卡", "手牌", "banished", "除外")) return;
                intent.OwnOnly = MatchesEffect(subject, @"(?<!opponent )\byou control\b|自己(?:场上|場上)") &&
                    !StoryAiEvaluation.Contains(subject, "opponent", "对方", "對方");
                if (StoryAiEvaluation.Contains(subject, "monster", "怪兽", "怪獸", "モンスター")) intent.TargetLocations = CardLocation.MonsterZone;
                else if (StoryAiEvaluation.Contains(subject, "spell", "trap", "魔法", "陷阱", "罠")) intent.TargetLocations = CardLocation.SpellZone;
                intent.FaceupOnly = StoryAiEvaluation.Contains(subject, "face-up", "face up", "表侧", "表側");
                intent.FacedownOnly = StoryAiEvaluation.Contains(subject, "face-down", "face down", "里侧", "裏側");
                if (MatchesEffect(result, @"^(?:destroy\b|(?:那|这|這|作为|作為).{0,30}(?:破坏|破壞))"))
                    intent.Purpose = EffectPurpose.TargetRemoval;
                else if (MatchesEffect(result, @"^(?:banish\b|(?:那|这|這|作为|作為).{0,30}除外)"))
                { intent.Purpose = EffectPurpose.TargetRemoval; intent.Hint = HintMsg.Remove; }
                else if (MatchesEffect(result, @"^send (?:it|that (?:card|monster)|them) to (?:the (?:gy|graveyard))|(?:那|这|這|作为|作為).{0,30}(?:送去墓地|送入墓地)"))
                { intent.Purpose = EffectPurpose.TargetRemoval; intent.Hint = HintMsg.ToGrave; }
                else if (MatchesEffect(result, @"^(?:return|shuffle)\b|(?:那|这|這|作为|作為).{0,30}(?:回到|返回)"))
                {
                    intent.Purpose = EffectPurpose.TargetRemoval;
                    intent.Hint = StoryAiEvaluation.Contains(result, "hand", "手卡", "手牌") ? HintMsg.ReturnToHand : HintMsg.ToDeck;
                }
                else if (MatchesEffect(result, @"^negate (?:its|that monster's|their) effects|(?:那|这|這|作为|作為).{0,30}(?:效果无效|效果無效|效果無効)"))
                { intent.Purpose = EffectPurpose.TargetNegate; intent.Hint = HintMsg.Disable; intent.FaceupOnly = true; }
            }
            else if (MatchesEffect(text, @"negate (?:the|that) activation|(?:那个|那個|其)发动无效|(?:那個|那个|其)發動無效") ||
                MatchesEffect(text, @"(?:effect|效果).{0,18}(?:is activated|发动时|發動時).*(?:negate that effect|那个效果无效|那個效果無效)"))
            {
                intent.Purpose = EffectPurpose.Negate;
                intent.MonsterOnly = !StoryAiEvaluation.Contains(text, "spell", "trap", "魔法", "陷阱", "罠") &&
                    MatchesEffect(text, @"(?:activates? a monster effect|monster effect is activated|把怪兽的效果发动|怪兽的效果发动时|怪獸的效果發動時)");
            }
            else if (MatchesEffect(text, @"\bdestroy all cards on the field\b|场上的卡全部破坏|場上的卡全部破壞"))
                intent.Purpose = EffectPurpose.BoardWipe;

            if (intent.Purpose == EffectPurpose.TargetRemoval || intent.Purpose == EffectPurpose.TargetNegate || intent.Purpose == EffectPurpose.Negate)
            {
                if (MatchesEffect(text, @"\bdiscard 1 card\s*[,;]|(?:丢弃|捨棄)1张手卡.{0,70}才能发动|(?:丟棄|捨棄)1張手卡.{0,70}才能發動"))
                    intent.DiscardCount = 1;
            }
        }

        private float RemovalValue(ClientCard target, EffectIntent intent)
        {
            float value = evaluation.Threat(target);
            if (intent.Hint == HintMsg.ReturnToHand && !StoryAiEvaluation.Hidden(target) && target.IsExtraCard()) value += 1200;
            if (intent.Hint == HintMsg.Destroy && (evaluation.Roles(target) & StoryAiEvaluation.Role.Grave) != 0) value -= 800;
            if (intent.Hint == HintMsg.Disable && target == LastChain && EnemyChain) value += 5000;
            // A normal Spell/Trap still resolves after it leaves the field. Removing it
            // on chain is not interaction; continuous/field/equip effects are different.
            if (target.Location == CardLocation.SpellZone && Duel.CurrentChain.Contains(target) &&
                !StoryAiEvaluation.Has(target, CardType.Continuous | CardType.Field | CardType.Equip)) value -= 5000;
            return value;
        }

        private bool PlanDiscard(EffectIntent intent)
        {
            if (intent.DiscardCount == 0) return true;
            var costs = Bot.Hand.Where(c => c != intent.Source && (intent.Fact?.CostFilter == null || intent.Fact.CostFilter(c)))
                .OrderBy(c => evaluation.MaterialCost(c, HintMsg.Discard)).Take(intent.DiscardCount).ToList();
            float benefit = intent.Purpose == EffectPurpose.Negate ? evaluation.Threat(intent.Target) + 1400 : RemovalValue(intent.Target, intent);
            if (costs.Count < intent.DiscardCount || costs.Sum(c => evaluation.MaterialCost(c, HintMsg.Discard)) >
                benefit + 500) return false;
            intent.Cost = costs[0];
            return true;
        }
    }
}
