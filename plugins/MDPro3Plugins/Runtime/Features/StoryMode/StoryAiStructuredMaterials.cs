using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        private static Recipe StructuredLinkRecipe(string text, bool differentNames)
        {
            var include = Regex.Match(text, @"^(?<base>[1-7]\+? monsters?), including (?:an? |1 )(?<kind>.+)$", RegexOptions.IgnoreCase);
            if (!include.Success) include = Regex.Match(text, @"^包含(?<kind>.+?)的(?<base>怪兽[1-7]只(?:以上)?)$");
            if (include.Success)
            {
                var total = ParseMaterial(include.Groups["base"].Value);
                var required = ParseMaterial(include.Groups["kind"].Value);
                if (total == null || required == null || required.Min > total.Min || required.Max > total.Max) return null;
                return new Recipe { Min = total.Min, Max = total.Max, DifferentNames = differentNames,
                    Requirements = new List<MaterialRequirement> { required,
                        new MaterialRequirement { Min = total.Min - required.Min, Max = total.Max - required.Min } } };
            }
            var clauses = Regex.Split(text, @"＋|(?<!\d)\+");
            var requirements = clauses.Select(ParseMaterial).ToList();
            if (requirements.Any(r => r == null)) return null;
            // A printed Link recipe always specifies its quantity. Never interpret
            // prose or an omitted number as permission for an arbitrary one-card Link.
            if (clauses.Any(c => !Regex.IsMatch(c, @"^\s*[1-7]\+?\s+|[1-7]\s*只(?:以上)?(?:[（(]衍生物除外[）)])?\s*$"))) return null;
            return new Recipe { Min = requirements.Sum(r => r.Min), Max = Math.Min(7, requirements.Sum(r => r.Max)),
                DifferentNames = differentNames, Requirements = requirements };
        }
    }
}
