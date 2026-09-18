using MDPro3.Duel.YGOSharp;
using System.Collections.Generic;

namespace MDPro3.Plugins.Features.PackBrowser
{
    /// <summary>One pack of Data/pack/pack.db together with the card used as its cover.</summary>
    internal sealed class PackEntry
    {
        /// <summary>Card.packFullName, the key used by the generated cover table.</summary>
        public string FullName = string.Empty;

        /// <summary>Pack code from pack.db (pack_id without the card number), e.g. "DUEA".</summary>
        public string Code = string.Empty;

        /// <summary>Pack name without the date prefix.</summary>
        public string Name = string.Empty;

        public PackCategory Category;

        public int Year;
        public int Month;
        public int Day;

        /// <summary>Card id drawn on the pack tile.</summary>
        public int CoverCard;

        /// <summary>Where the cover came from, see PackCoverTable.</summary>
        public int CoverKind;

        /// <summary>Card ids of the pack, cover first.</summary>
        public readonly List<int> Cards = new List<int>();

        public int Count => Cards.Count;

        public string DateText =>
            Year.ToString("D4") + "-" + Month.ToString("D2") + "-" + Day.ToString("D2");

        public string CoverSourceText => PackCoverTable.DescribeKind(CoverKind);
    }

    /// <summary>
    /// Builds the pack list out of the live game data (PacksManager + CardsManager) so it always
    /// matches Data/pack/pack.db. The cover card comes from the generated wiki table, and for the
    /// packs the wiki does not list it is derived from the pack rarity data.
    /// </summary>
    internal static class PackCatalog
    {
        private const int KindRepresentative = 6;

        private static List<PackEntry> cache;

        /// <summary>All packs, newest first (the order of PacksManager).</summary>
        public static List<PackEntry> All => cache ?? (cache = Build());

        /// <summary>Drops the cache, used when the card database was reloaded.</summary>
        public static void Invalidate()
        {
            cache = null;
        }

        public static List<PackEntry> Build()
        {
            var result = new List<PackEntry>();

            var cardsByPack = new Dictionary<string, List<int>>();
            foreach (var pair in CardsManager._cards)
            {
                string full = pair.Value.packFullName;
                if (string.IsNullOrEmpty(full))
                    continue;

                if (!cardsByPack.TryGetValue(full, out var list))
                {
                    list = new List<int>();
                    cardsByPack[full] = list;
                }

                list.Add(pair.Key);
            }

            foreach (var pack in PacksManager.packs)
            {
                if (pack == null || string.IsNullOrEmpty(pack.fullName))
                    continue;

                var entry = new PackEntry
                {
                    FullName = pack.fullName,
                    Code = pack.shortName ?? string.Empty,
                    Year = pack.year,
                    Month = pack.month,
                    Day = pack.day,
                };

                // fullName is "yyyy-MM-dd <pack name>"
                entry.Name = pack.fullName.Length > 11 ? pack.fullName.Substring(11) : pack.fullName;
                entry.Category = PackCategories.Classify(entry.Code, entry.Name);

                if (cardsByPack.TryGetValue(pack.fullName, out var cards))
                    entry.Cards.AddRange(cards);

                ResolveCover(entry);
                SortCards(entry);

                result.Add(entry);
            }

            return result;
        }

        #region Cover

        private static void ResolveCover(PackEntry entry)
        {
            int fromTable = PackCoverTable.Get(entry.FullName);
            if (fromTable != 0 && (entry.Cards.Count == 0 || entry.Cards.Contains(fromTable)))
            {
                entry.CoverCard = fromTable;
                entry.CoverKind = PackCoverTable.Kind(entry.FullName);
                return;
            }

            int rule = RuleCover(entry.Cards, out int kind);
            if (rule != 0)
            {
                entry.CoverCard = rule;
                entry.CoverKind = kind;
                return;
            }

            entry.CoverCard = Representative(entry.Cards);
            entry.CoverKind = KindRepresentative;
        }

        /// <summary>
        /// The card OCG prints on the booster artwork: the pack's single HR card, else its single
        /// QCSE card, else its single UL card. Packs outside that pattern return 0.
        /// </summary>
        private static int RuleCover(List<int> cards, out int kind)
        {
            kind = 0;
            if (cards == null || cards.Count == 0)
                return 0;

            int id = UniqueWithToken(cards, "HR");
            if (id != 0)
            {
                kind = 2;
                return id;
            }

            id = UniqueWithExactRarity(cards, "QCSE");
            if (id != 0)
            {
                kind = 3;
                return id;
            }

            id = UniqueWithToken(cards, "UL");
            if (id != 0)
            {
                kind = 4;
                return id;
            }

            id = UniqueWithExactRarity(cards, "SE");
            if (id != 0)
            {
                kind = 4;
                return id;
            }

            return 0;
        }

        private static int UniqueWithToken(List<int> cards, string token)
        {
            int found = 0;
            int count = 0;
            foreach (int code in cards)
            {
                if (!HasToken(code, token))
                    continue;

                found = code;
                if (++count > 1)
                    return 0;
            }

            return count == 1 ? found : 0;
        }

        private static int UniqueWithExactRarity(List<int> cards, string rarity)
        {
            int found = 0;
            int count = 0;
            foreach (int code in cards)
            {
                if (!string.Equals(GetRarity(code), rarity, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                found = code;
                if (++count > 1)
                    return 0;
            }

            return count == 1 ? found : 0;
        }

        private static bool HasToken(int code, string token)
        {
            string rarity = GetRarity(code);
            if (string.IsNullOrEmpty(rarity))
                return false;

            foreach (string part in rarity.Split(','))
                if (string.Equals(part.Trim(), token, System.StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        /// <summary>Highest rarity first, monsters preferred, lowest card id as the tie break.</summary>
        private static int Representative(List<int> cards)
        {
            int bestCode = 0;
            int bestMonster = -1;
            int bestRank = -1;

            foreach (int code in cards)
            {
                int monster = IsMonster(code) ? 1 : 0;
                int rank = RarityRank(GetRarity(code));

                if (monster > bestMonster
                    || (monster == bestMonster && rank > bestRank)
                    || (monster == bestMonster && rank == bestRank && (bestCode == 0 || code < bestCode)))
                {
                    bestCode = code;
                    bestMonster = monster;
                    bestRank = rank;
                }
            }

            return bestCode;
        }

        private static int RarityRank(string rarity)
        {
            if (string.IsNullOrEmpty(rarity))
                return 0;

            int best = 0;
            foreach (string part in rarity.Split(','))
            {
                int value;
                switch (part.Trim().ToUpperInvariant())
                {
                    case "UR": value = 4; break;
                    case "SR": value = 3; break;
                    case "R": value = 2; break;
                    case "N": value = 1; break;
                    default: value = 0; break;
                }

                if (value > best)
                    best = value;
            }

            return best;
        }

        private static bool IsMonster(int code)
        {
            var card = CardsManager.GetCardRaw(code);
            return card != null && card.HasType(CardType.Monster);
        }

        private static string GetRarity(int code)
        {
            var card = CardsManager.GetCardRaw(code);
            return card != null ? card.reality : string.Empty;
        }

        #endregion

        #region Order

        /// <summary>Cover card first, then highest rarity, then lowest card id.</summary>
        private static void SortCards(PackEntry entry)
        {
            int cover = entry.CoverCard;
            entry.Cards.Sort((left, right) =>
            {
                if (left == right)
                    return 0;
                if (left == cover)
                    return -1;
                if (right == cover)
                    return 1;

                int rank = RarityRank(GetRarity(right)).CompareTo(RarityRank(GetRarity(left)));
                return rank != 0 ? rank : left.CompareTo(right);
            });
        }

        #endregion
    }
}
