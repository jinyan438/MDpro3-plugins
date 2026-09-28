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
        private static Recipe XyzBodyRecipe(string text)
        {
            var match = Regex.Match(text, @"^(?<except>「No\.」怪兽以外的)?相同阶级的超量怪兽[×x](?<n>[2-3])$");
            if (!match.Success) match = Regex.Match(text,
                @"^(?<n>[2-3]) Xyz Monsters with the same Rank(?<except>, except ""Number"" monsters)?$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            return new Recipe { Min = int.Parse(match.Groups["n"].Value), Max = int.Parse(match.Groups["n"].Value),
                XyzBodies = true, ExcludeNumbers = match.Groups["except"].Success };
        }

        private readonly Dictionary<int, string> overlayNames = new Dictionary<int, string>();
        private bool IsOverlayUpgrade(ClientCard destination, ClientCard material)
        {
            if (!Has(destination, CardType.Xyz) || !Has(material, CardType.Xyz) || material.Controller != 0) return false;
            if (!overlayNames.TryGetValue(destination.Id, out var name))
            {
                string text = destination.Data?.Description ?? "";
                // Complete unconditional alternative. Conditional Zeus/Downerd/Gaia
                // procedures are deliberately not inferred from a substring.
                var match = Regex.Match(text,
                    @"(?:^|[。\r\n])这张卡也能在自己场上的「(?<name>[^」]+)」上面重叠来超量召唤。|(?:^|[.\r\n])\s*You can also Xyz Summon this card by using (?:an? )?""(?<name>[^""]+)"" you control as material(?:\. | \()", RegexOptions.IgnoreCase);
                name = match.Success ? match.Groups["name"].Value : null;
                if (overlayNames.Count < 4096) overlayNames[destination.Id] = name;
            }
            return name != null && string.Equals(name, material.Data?.Name, StringComparison.OrdinalIgnoreCase) && MaterialPermits(material, destination);
        }

        private int ProjectedOverlays(DevelopmentState state, ClientCard destination, List<ClientCard> materials)
        {
            if (!Has(destination, CardType.Xyz)) return 0;
            if (materials.Count == 1 && IsOverlayUpgrade(destination, materials[0]))
                return 1 + (state.Board.FirstOrDefault(b => b.Card == materials[0])?.OverlayCount ?? materials[0].Overlays.Count);
            return materials.Count;
        }

        private static bool NumberMonster(ClientCard card) => card.HasSetcode(0x48) ||
            Regex.IsMatch(card.Data?.Name ?? "", @"^(?:No\.|Number )", RegexOptions.IgnoreCase);

        private bool CanQuickLink(DevelopmentState state, ClientCard ip, ClientCard destination)
        {
            if (state.Board.Count < 2 || !Has(destination, CardType.Link) || BlanketExtraSummonLock(state)) return false;
            var recipe = MaterialRecipe(destination);
            if (recipe == null) return false;
            var pool = state.Board.Where(b => b.Fresh || b.Card.IsFaceup()).Select(b => b.Card).ToList();
            for (int mask = 1; mask < (1 << pool.Count); mask++)
            {
                var materials = pool.Where((c, i) => (mask & (1 << i)) != 0).ToList();
                if (!materials.Contains(ip) || !ValidMaterials(destination, recipe, materials, c => state.Board.Any(b => b.Card == c && b.FromExtra))) continue;
                // An opponent-turn conversion still needs a legal destination
                // after THESE materials leave, just like a Main Phase Link Summon.
                var remaining = new DevelopmentState { Board = state.Board.Where(b => !materials.Contains(b.Card)).ToList(), Enemy = state.Enemy };
                if (DevelopmentPlace(remaining, destination, true) >= 0) return true;
            }
            return false;
        }
    }
}
