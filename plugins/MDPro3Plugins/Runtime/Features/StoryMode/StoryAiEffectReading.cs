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

        private static int RequiredTextTargets(string subject)
        {
            if (MatchesEffect(subject, @"\bup to\b|最多|至多")) return 1;
            var count = Regex.Match(subject, @"\btarget\s+(2|two)\b|(?:场上|場上)\s*([2-5])\s*(?:张|張|只|隻|枚)", RegexOptions.IgnoreCase);
            return count.Success ? count.Groups[2].Success ? int.Parse(count.Groups[2].Value) : 2 : 1;
        }

        private bool ReadUnambiguousFieldInteraction(EffectIntent intent, string text, int index)
        {
            // Text is only a fallback when the exact Lua descriptor is unavailable.
            // A nonzero descriptor can name a different paragraph, and multiple target
            // clauses are ambiguous without executing the script. Restrict this to the
            // first descriptor and one complete field-target sentence.
            if (index != 0) return false;
            var targets = Regex.Matches(text,
                @"\btarget\s+(?:up to\s+)?(?:1|2|one|two)\b|以[^。\n①-⑳]*?(?:为对象|為對象)",
                RegexOptions.IgnoreCase);
            if (targets.Count != 1) return false;
            var target = Regex.Match(text,
                @"\btarget\s+(?:up to\s+)?(?:1|2|one|two)\s+(?<target>[^;:]+)[;:]\s*(?<result>[^.\n]+)",
                RegexOptions.IgnoreCase);
            if (!target.Success)
                target = Regex.Match(text, @"以(?<target>[^。\n①-⑳]*?)(?:为对象|為對象)(?:才能发动|才能發動)?[。．，,;；:：]*(?<result>[^①-⑳\n]+)");
            if (!target.Success) return false;
            // A sole targeting clause may still belong to a second activated
            // effect. Only passive numbered paragraphs can be skipped safely.
            var paragraphs = Regex.Matches(text, "[①-⑳]");
            if (paragraphs.Count > 1)
            {
                int start = 0, end = text.Length;
                foreach (Match paragraph in paragraphs)
                    if (paragraph.Index <= target.Index) start = paragraph.Index;
                    else { end = paragraph.Index; break; }
                string other = text.Substring(0, start) + text.Substring(end);
                if (MatchesEffect(other, @"发动|發動|\b(?:activate|you can)\b")) return false;
            }
            string subject = target.Groups["target"].Value, result = target.Groups["result"].Value;
            if (!StoryAiEvaluation.Contains(subject, "on the field", "场上", "場上") ||
                StoryAiEvaluation.Contains(subject, "grave", "gy", "墓地", "hand", "手卡", "手牌", "banished", "除外")) return false;
            bool banishes = MatchesEffect(result, @"^(?:banish\b|(?:那|这|這|作为|作為).{0,30}除外)");
            bool disables = MatchesEffect(result,
                @"(?:negat(?:e|ed|es|ing)|无效|無效|無効).{0,80}(?:effect|效果|効果)|(?:effect|效果|効果).{0,80}(?:negat(?:e|ed|es|ing)|无效|無效|無効)");
            if (!banishes && !disables) return false;
            intent.Purpose = banishes ? EffectPurpose.TargetRemoval : EffectPurpose.TargetNegate;
            intent.Hint = banishes ? HintMsg.Remove : HintMsg.Disable;
            intent.MinimumTargets = RequiredTextTargets(target.Value);
            intent.OwnOnly = MatchesEffect(subject, @"(?<!opponent )\byou control\b|自己(?:场上|場上)") &&
                !StoryAiEvaluation.Contains(subject, "opponent", "对方", "對方");
            if (StoryAiEvaluation.Contains(subject, "monster", "怪兽", "怪獸", "モンスター")) intent.TargetLocations = CardLocation.MonsterZone;
            else if (StoryAiEvaluation.Contains(subject, "spell", "trap", "魔法", "陷阱", "罠")) intent.TargetLocations = CardLocation.SpellZone;
            intent.FaceupOnly = disables || StoryAiEvaluation.Contains(subject, "face-up", "face up", "表侧", "表側");
            intent.FacedownOnly = StoryAiEvaluation.Contains(subject, "face-down", "face down", "里侧", "裏側");
            return true;
        }

        private void ReadGenericEffect(EffectIntent intent, string text, int index)
        {
            ReadGenericEffectPurpose(intent, text, index);
            // All text readers share cost accounting, including the early
            // multi-paragraph interaction path.
            if (intent.Purpose == EffectPurpose.TargetRemoval || intent.Purpose == EffectPurpose.TargetNegate || intent.Purpose == EffectPurpose.Negate)
            {
                if (MatchesEffect(text, @"\bdiscard 1 card\s*[,;]|(?:丢弃|捨棄)1张手卡.{0,70}才能发动|(?:丟棄|捨棄)1張手卡.{0,70}才能發動"))
                    intent.DiscardCount = 1;
            }
        }

        private void ReadGenericEffectPurpose(EffectIntent intent, string text, int index)
        {
            // Effects such as "destroy both 1 card you control and 1 card on the
            // field" are commonly embedded in a multi-paragraph description. The
            // printed paragraph number is not a reliable Lua description index, so
            // recognize this unambiguous mixed-target shape before the index guard.
            bool knownTrigger = StoryAiEvaluation.Facts(intent.Description >= 16000 ? intent.Description / 16 : StoryAiEvaluation.CardIdentity(intent.Source))
                .Limits.Any(l => l.Trigger && !l.DefaultDescription && l.Offset == index);
            if ((intent.Source.Location & (CardLocation.MonsterZone | CardLocation.SpellZone)) != 0 && !knownTrigger && MatchesEffect(text,
                @"destroy(?:\s+both)?\s+(?:1|one)\s+(?:other\s+)?(?:face-up\s+)?card\s+you\s+control\s*(?:,?\s+and\s+)\s*(?:1|one)\s+(?:face-up\s+)?card\s+(?:on\s+the\s+field|your\s+opponent\s+controls)|自己(?:场上|場上)\s*1\s*(?:张|張|枚|个|個)?\s*卡\s*(?:和|與|及|、)\s*(?:(?:对方|對方)(?:场上|場上)|(?:场上|場上))\s*1\s*(?:张|張|枚|个|個)?\s*卡\s*(?:破坏|破壞)"))
            {
                intent.Purpose = EffectPurpose.MixedTargetDestruction;
                intent.Hint = HintMsg.Destroy;
                intent.TargetLocations = CardLocation.MonsterZone | CardLocation.SpellZone;
                intent.MinimumTargets = 2;
                intent.NonTargeting = !MatchesEffect(text, @"\btarget\b|为对象|為對象");
                return;
            }
            if (ReadUnambiguousFieldInteraction(intent, text, index)) return;
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
                intent.MinimumTargets = RequiredTextTargets(target.Value);
                // Graveyard costs/revivals and self-targeted buffs need a different policy.
                if (StoryAiEvaluation.Contains(subject, "grave", "gy", "墓地", "hand", "手卡", "手牌", "banished", "除外")) return;
                intent.OwnOnly = MatchesEffect(subject, @"(?<!opponent )\byou control\b|自己(?:场上|場上)") &&
                    !StoryAiEvaluation.Contains(subject, "opponent", "对方", "對方");
                if (StoryAiEvaluation.Contains(subject, "monster", "怪兽", "怪獸", "モンスター")) intent.TargetLocations = CardLocation.MonsterZone;
                else if (StoryAiEvaluation.Contains(subject, "spell", "trap", "魔法", "陷阱", "罠")) intent.TargetLocations = CardLocation.SpellZone;
                intent.FaceupOnly = StoryAiEvaluation.Contains(subject, "face-up", "face up", "表侧", "表側");
                intent.FacedownOnly = StoryAiEvaluation.Contains(subject, "face-down", "face down", "里侧", "裏側");
                // Some generic effects put the useful-looking clause first:
                // "gains 400 ATK, but its effects are negated". Read the whole
                // result so a missing enemy target cannot turn this into a
                // self-negation of a freshly summoned monster. Graveyard
                // summon/revival targets returned above keep their own policy.
                bool negatesTargetEffects = MatchesEffect(result,
                    @"(?:negat(?:e|ed|es|ing)|无效|無效|無効).{0,80}(?:effect|效果|効果)|(?:effect|效果|効果).{0,80}(?:negat(?:e|ed|es|ing)|无效|無效|無効)");
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
                else if (negatesTargetEffects)
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
