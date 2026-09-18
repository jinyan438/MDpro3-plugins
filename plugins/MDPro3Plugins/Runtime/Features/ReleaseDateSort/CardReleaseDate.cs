using MDPro3.Duel.YGOSharp;

namespace MDPro3.Plugins.Features.ReleaseDateSort
{
    /// <summary>
    /// Release date ("pack date") helper.
    ///
    /// MDPro3 stores the first pack appearance of every card in Data/pack/*.db
    /// (table "pack") and PacksManager writes the parsed date into
    /// Card.year / Card.month / Card.day. Those are the very same values the game
    /// shows in the card detail view, so sorting by them stays consistent with the UI.
    /// Cards without pack information have year == 0 and are treated as unknown.
    /// </summary>
    public static class CardReleaseDate
    {
        public const long Unknown = 0L;

        public static long GetKey(int code)
        {
            Card card = CardsManager.GetCardRaw(code);
            return GetKey(card);
        }

        public static long GetKey(Card card)
        {
            if (card == null || card.year <= 0)
                return Unknown;

            int month = card.month > 0 ? card.month : 0;
            int day = card.day > 0 ? card.day : 0;
            return (long)card.year * 10000L + month * 100L + day;
        }

        public static bool HasDate(long key)
        {
            return key > Unknown;
        }

        /// <summary>Readable form of a key, only used for logs and diagnostics.</summary>
        public static string FormatKey(long key)
        {
            if (!HasDate(key))
                return "unknown";

            long year = key / 10000L;
            long month = key / 100L % 100L;
            long day = key % 100L;
            return year.ToString("D4") + "-" + month.ToString("D2") + "-" + day.ToString("D2");
        }
    }
}
