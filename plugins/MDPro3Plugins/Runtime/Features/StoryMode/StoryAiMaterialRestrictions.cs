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
        private sealed class MaterialLimit
        {
            internal CardType Type;
            internal MaterialRequirement Exception;
        }
        private readonly Dictionary<int, List<MaterialLimit>> materialLimits = new Dictionary<int, List<MaterialLimit>>();

        private bool MaterialPermits(ClientCard card, ClientCard destination)
        {
            if (!materialLimits.TryGetValue(card.Id, out var limits))
            {
                limits = new List<MaterialLimit>();
                string text = card.Data?.Description ?? "";
                var kinds = new[] { CardType.Synchro, CardType.Xyz, CardType.Link, CardType.Fusion };
                var english = new[] { "Synchro", "Xyz", "Link", "Fusion" };
                var chinese = new[] { "同调", "超量", "连接", "融合" };
                for (int i = 0; i < kinds.Length; i++)
                {
                    var blocked = Regex.Match(text, @"cannot be used as (?:a )?" + english[i] +
                        @" Material(?<tail>[^.\n]*)", RegexOptions.IgnoreCase);
                    if (blocked.Success)
                    {
                        string tail = blocked.Groups["tail"].Value.Trim();
                        var exception = Regex.Match(tail, @"^,? except for the " + english[i] + @" Summon of (?:an? )?(?<kind>.+)$", RegexOptions.IgnoreCase);
                        limits.Add(new MaterialLimit { Type = kinds[i], Exception = exception.Success ? ParseMaterial(exception.Groups["kind"].Value) : null });
                    }
                    else if (Regex.IsMatch(text, @"(?:这张卡)?不能(?:作为|用作)" + chinese[i] + "素材"))
                        limits.Add(new MaterialLimit { Type = kinds[i] });
                    else
                    {
                        var only = Regex.Match(text, @"把这张卡作为" + chinese[i] + @"素材的场合，不是(?<kind>[^。]+?)的" + chinese[i] + "召唤不能使用");
                        if (only.Success) limits.Add(new MaterialLimit { Type = kinds[i], Exception = ParseMaterial(only.Groups["kind"].Value) });
                    }
                }
                if (materialLimits.Count < 4096) materialLimits[card.Id] = limits;
            }
            return limits.All(limit => !Has(destination, limit.Type) || limit.Exception != null && limit.Exception.Matches(destination));
        }
    }
}
