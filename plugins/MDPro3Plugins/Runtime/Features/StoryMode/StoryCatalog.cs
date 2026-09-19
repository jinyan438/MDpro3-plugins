using System;
using System.Collections.Generic;
using System.Linq;
using MDPro3.Duel.YGOSharp;
using MDPro3.Plugins.Features.PackBrowser;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal static class StoryCatalog
    {
        public static bool Playable(int id)
        {
            var card = CardsManager.GetCardRaw(id);
            return card != null && (card.Type & 7) != 0 && !card.HasType(CardType.Token);
        }
        public static bool IsExtra(int id) => CardsManager.GetCardRaw(id)?.IsExtraCard() == true;
        public static int Identity(int id)
        {
            var card = CardsManager.GetCardRaw(id);
            return card != null && card.Alias > 0 ? card.Alias : id;
        }
        public static string Validate(StoryDeck deck, StorySave save = null) =>
            StoryProgress.ValidateDeck(deck, save?.owned, Playable, IsExtra, Identity);

        public static StoryDeck Starter()
        {
            var deck = new StoryDeck();
            // Twenty-four early normal monsters, ten general spells and six general traps.
            // Select the normal monsters from the live database, so localized names never matter.
            var monsters = CardsManager._cards.Values.Where(c => c.Alias == 0 && c.year > 0
                && c.year <= 2000 && c.HasType(CardType.Normal) && c.HasType(CardType.Monster)
                && !c.IsExtraCard() && c.Level >= 1 && c.Level <= 4 && c.Attack >= 1000
                && c.Attack <= 1800 && Playable(c.Id))
                .OrderBy(c => c.year).ThenBy(c => c.month).ThenBy(c => c.day).ThenBy(c => c.Id)
                .Take(12).ToList();
            if (monsters.Count != 12) throw new InvalidOperationException("初始卡组所需的早期卡片尚未加载。");
            foreach (var card in monsters) { deck.main.Add(card.Id); deck.main.Add(card.Id); }
            int[] support = { 53129443, 83764718, 5318639, 66788016, 72302403, 4206964, 44095762, 97077563 };
            foreach (int id in support)
            {
                if (!Playable(id)) throw new InvalidOperationException("初始卡组缺少卡片：" + id);
                deck.main.Add(id); deck.main.Add(id);
            }
            return deck;
        }

        public static List<PackEntry> Packs()
        {
            PackCatalog.Invalidate();
            var packs = new List<PackEntry>();
            foreach (var pack in PackCatalog.All)
            {
                if (pack.IsPrerelease || pack.Year <= 0) continue;
                var copy = new PackEntry
                {
                    FullName = pack.FullName, Code = pack.Code, Name = pack.Name,
                    Category = pack.Category, Year = pack.Year, Month = pack.Month, Day = pack.Day,
                    CoverCard = pack.CoverCard, CoverKind = pack.CoverKind
                };
                copy.Cards.AddRange(pack.Cards.Where(Playable).Distinct());
                if (copy.Count > 0) packs.Add(copy);
            }
            return packs.OrderBy(p => p.Year).ThenBy(p => p.Month).ThenBy(p => p.Day)
                .ThenBy(p => p.FullName, StringComparer.Ordinal).ToList();
        }
    }
}
