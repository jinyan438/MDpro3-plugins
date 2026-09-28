using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        private sealed class RecruitTrigger
        {
            internal bool Normal, Special, Defense, Disabled, CannotAttack;
            internal CardLocation Source;
            internal int Level, MaximumLevel;
            internal MaterialRequirement Requirement;
            internal bool Matches(ClientCard card) => Has(card, CardType.Monster) && Requirement.Matches(card) &&
                (Level == 0 || StoryAiEvaluation.Level(card) == Level) &&
                (MaximumLevel == 0 || StoryAiEvaluation.Level(card) <= MaximumLevel);
        }

        private readonly Dictionary<int, RecruitTrigger> recruitTriggers = new Dictionary<int, RecruitTrigger>();

        private RecruitTrigger ReadRecruitTrigger(ClientCard card)
        {
            if (recruitTriggers.TryGetValue(card.Id, out var cached)) return cached;
            string text = card.Data?.Description ?? "";
            // Printed paragraph numbers do not identify Lua effect descriptions.
            // This generic trigger model accepts only one complete printed effect.
            if (Regex.Matches(text, @"[①-⑳]").Count > 1) return null;
            int numbered = text.IndexOf('①');
            if (numbered >= 0)
            {
                string header = Regex.Replace(text.Substring(0, numbered), @"这个卡名的[^。]*1回合[^。]*。", "").Trim();
                if (header.Length != 0) return null;
                text = text.Substring(numbered + 1).TrimStart(':', '：', ' ', '\r', '\n');
            }
            text = Regex.Replace(text, @"^(?:这个卡名的[^。]*1回合[^。]*。|You can only use this effect of ""[^""]+"" once per turn\.)\s*", "", RegexOptions.IgnoreCase).Trim();
            var en = Regex.Match(text,
                @"^(?:If|When) this card is (?<summon>Normal or Special|Normal|Special) Summoned: (?:You can )?Special Summon 1 (?<kind>[^:;.]+?) from your (?<source>hand|Deck|GY)(?<defense> in Defense Position)?\.(?<tail>.*)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var zh = Regex.Match(text,
                @"^这张卡(?<summon>召唤·特殊召唤|特殊召唤|召唤)(?:成功)?(?:时|的场合)(?:才能发动)?[。：]从(?:自己)?(?<source>手卡|卡组|墓地)把1只(?<kind>[^。]+?)(?<defense>守备表示)?特殊召唤。(?<tail>.*)$",
                RegexOptions.Singleline);
            var targeted = Regex.Match(text,
                @"^这张卡(?<summon>召唤·特殊召唤|特殊召唤|召唤)(?:成功)?(?:时|的场合)，以自己(?<source>墓地)1只(?<kind>[^。]+?)为对象才能发动。那只怪兽(?<defense>守备表示)?特殊召唤。(?<tail>.*)$",
                RegexOptions.Singleline);
            var match = en.Success ? en : zh.Success ? zh : targeted;
            RecruitTrigger result = null;
            if (match.Success)
            {
                string tail = match.Groups["tail"].Value.Trim();
                bool disabled = Regex.IsMatch(tail, @"(?:its|their) effects? (?:are|is) negated|效果无效化", RegexOptions.IgnoreCase);
                bool noAttack = Regex.IsMatch(tail, @"cannot attack|不能攻击", RegexOptions.IgnoreCase);
                tail = Regex.Replace(tail,
                    @"(?:but )?its effects are negated[.,]?|(?:It|That monster) cannot attack this turn\.|这个效果特殊召唤的怪兽的效果无效化。|这个效果特殊召唤的怪兽在这个回合不能攻击。|You can only use this effect of ""[^""]+"" once per turn\.|[\r\n]", "", RegexOptions.IgnoreCase).Trim();
                string kind = match.Groups["kind"].Value.Trim();
                var level = Regex.Match(kind, @"(?:Level (?<n>[1-9]|1[0-2])(?<lower> or lower)? |(?<n>[1-9]|1[0-2])星(?<lower>以下)?的?)", RegexOptions.IgnoreCase);
                int exact = 0, maximum = 0;
                if (level.Success)
                {
                    if (level.Groups["lower"].Success) maximum = int.Parse(level.Groups["n"].Value);
                    else exact = int.Parse(level.Groups["n"].Value);
                    kind = kind.Remove(level.Index, level.Length).Trim();
                }
                var requirement = Regex.IsMatch(kind, @"^(monsters?|怪兽)$", RegexOptions.IgnoreCase)
                    ? new MaterialRequirement() : ParseMaterial(kind);
                if (tail.Length == 0 && requirement != null)
                {
                    string summon = match.Groups["summon"].Value, source = match.Groups["source"].Value;
                    result = new RecruitTrigger
                    {
                        Normal = summon == "召唤" || summon == "召唤·特殊召唤" || Contains(summon, "Normal"),
                        Special = Contains(summon, "Special", "特殊"),
                        Source = Contains(source, "Deck", "卡组") ? CardLocation.Deck : Contains(source, "GY", "墓地") ? CardLocation.Grave : CardLocation.Hand,
                        Defense = match.Groups["defense"].Success, Disabled = disabled, CannotAttack = noAttack,
                        Level = exact, MaximumLevel = maximum, Requirement = requirement
                    };
                }
            }
            if (recruitTriggers.Count < 4096) recruitTriggers[card.Id] = result;
            return result;
        }

        private IEnumerable<DevelopmentState> RecruitSuccessors(DevelopmentState state, SearchBudget budget)
        {
            var trigger = ReadRecruitTrigger(state.PendingRecruit);
            var decline = CopyDevelopment(state); decline.PendingRecruit = null; decline.Score = TerminalValue(decline);
            yield return decline;
            if (DevelopmentSummonsBlocked(state)) yield break;
            IEnumerable<ClientCard> pool = trigger.Source == CardLocation.Grave ? state.Grave :
                state.Reserve.Where(c => trigger.Source == CardLocation.Hand ? InDevelopmentHand(state, c) :
                    !state.Acquired.Contains(c) && c.Location == trigger.Source);
            foreach (var card in pool.Where(trigger.Matches).OrderBy(c => c.Id))
            {
                if (budget.Exhausted) yield break;
                // The core still decides revival legality. Do not predict a summon of
                // a restricted monster from deck/hand, or an improperly summoned boss.
                if (Has(card, CardType.SpSummon) || trigger.Source == CardLocation.Grave && !card.IsCanRevive() && !state.ProperlySummoned.Contains(card) ||
                    trigger.Source == CardLocation.Deck && state.DeckCount <= 0) continue;
                int zone = DevelopmentPlace(state, card, false);
                if (zone < 0) yield break;
                var next = CopyDevelopment(state); next.PendingRecruit = null;
                next.Reserve.Remove(card); next.Acquired.Remove(card); next.Grave.Remove(card);
                if (trigger.Source == CardLocation.Deck) next.DeckCount--;
                if (trigger.Source == CardLocation.Hand) next.Credit -= HandCommitment(card);
                next.Credit -= DrawPenalty(trigger.Source) + SummonExposure(state);
                next.Board.Add(new Body { Card = card, Attack = Attack(card), Zone = zone, Fresh = true,
                    EffectsBlocked = trigger.Disabled, DefensePosition = trigger.Defense, CannotAttack = trigger.CannotAttack });
                QueueLevelArrival(next, card, false);
                if (!trigger.Disabled)
                {
                    QueueComboArrival(next, card, false);
                    QueueDevelopmentSearch(next, card, false);
                    if (!next.Credited.Contains(card.Id) && ReadRecruitTrigger(card)?.Special == true) next.PendingRecruit = card;
                    next.Credited.Add(card.Id);
                }
                next.Score = TerminalValue(next); budget.Nodes++;
                yield return next;
            }
        }
    }
}
