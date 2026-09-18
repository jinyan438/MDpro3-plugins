using MDPro3.Utility;

namespace MDPro3.Plugins.Features.PackBrowser
{
    /// <summary>
    /// Texts of the pack browser. The plugin may not write into the game translation files, so
    /// every label is translated here for each language the game supports.
    /// </summary>
    internal static class PackBrowserLabels
    {
        public static string Title => Pick("Card packs", "Sobres", "Pacotes", "Boites", "Kartenpacks",
            "Buste", "\u30ab\u30fc\u30c9\u30d1\u30c3\u30af", "\uce74\ub4dc \ud32c", "\u5361\u5305\u5716\u9451");

        public static string MenuEntry => Pick("Card packs", "Sobres", "Pacotes", "Boites", "Kartenpacks",
            "Buste", "\u30ab\u30fc\u30c9\u30d1\u30c3\u30af", "\uce74\ub4dc \ud32c", "\u5361\u5305");

        public static string Hint => Pick("Click a pack to see its cards / Esc to close",
            "Clic en un sobre para ver sus cartas / Esc para cerrar",
            "Clique num pacote para ver as cartas / Esc para fechar",
            "Cliquez sur un pack pour voir ses cartes / Echap pour fermer",
            "Pack anklicken um die Karten zu sehen / Esc zum Schliessen",
            "Clicca un pacchetto per vedere le carte / Esc per chiudere",
            "\u30d1\u30c3\u30af\u3092\u9078\u3093\u3067\u53ce\u9332\u30ab\u30fc\u30c9\u3092\u8868\u793a / Esc\u3067\u9589\u3058\u308b",
            "\ud32c\uc744 \uc120\ud0dd\ud558\uc5ec \uce74\ub4dc \ubcf4\uae30 / Esc\ub85c \ub2eb\uae30",
            "\u9ede\u64ca\u5361\u5305\u67e5\u770b\u5167\u542b\u5361\u724c / Esc \u95dc\u9589");

        public static string Back => Pick("Esc: back to the pack list", "Esc: volver a la lista",
            "Esc: voltar a lista", "Echap : retour a la liste", "Esc: zurueck zur Liste",
            "Esc: torna all'elenco", "Esc\uff1a\u30d1\u30c3\u30af\u4e00\u89a7\u306b\u623b\u308b",
            "Esc: \ud32c \ubaa9\ub85d\uc73c\ub85c", "Esc\uff1a\u8fd4\u56de\u5361\u5305\u5217\u8868");

        public static string Cards => Pick("cards", "cartas", "cartas", "cartes", "Karten",
            "carte", "\u679a", "\uc7a5", "\u5f35");

        public static string CoverSource => Pick("cover source", "origen de portada", "origem da capa",
            "source de la couverture", "Quelle des Covers", "origine copertina",
            "\u8868\u7d19\u306e\u51fa\u5178", "\ud45c\uc9c0 \ucd9c\ucc98", "\u5c01\u9762\u4f86\u6e90");

        public static string Empty => Pick("no card data for this pack", "sin datos de cartas",
            "sem dados de cartas", "aucune donnee de carte", "keine Kartendaten",
            "nessun dato delle carte", "\u30ab\u30fc\u30c9\u30c7\u30fc\u30bf\u306a\u3057",
            "\uce74\ub4dc \ub370\uc774\ud130 \uc5c6\uc74c", "\u6c92\u6709\u6b64\u5361\u5305\u7684\u5361\u724c\u8cc7\u6599");

        public static string EmptyCategory => Pick("No packs", "Sin sobres", "Nenhum pacote",
            "Aucun pack", "Keine Packs", "Nessuna busta", "\u30d1\u30c3\u30af\u306a\u3057",
            "\ud329 \uc5c6\uc74c", "\u6b64\u5206\u985e\u66ab\u7121\u5361\u5305", "\u6b64\u5206\u7c7b\u6682\u65e0\u5361\u5305");

        public static string Category(PackCategory category)
        {
            switch (category)
            {
                case PackCategory.Basic:
                    return Pick("Core boosters", "Sobres basicos", "Pacotes basicos", "Packs de base",
                        "Hauptsets", "Buste base", "\u57fa\u672c\u30d1\u30c3\u30af", "\uae30\ubcf8 \ud329",
                        "\u57fa\u672c\u5361\u5305", "\u57fa\u672c\u5361\u5305");
                case PackCategory.Structure:
                    return Pick("Prebuilt decks", "Mazos preconstruidos", "Decks pre-montados", "Decks preconstruits",
                        "Vorgefertigte Decks", "Mazzi precostruiti", "\u69cb\u7bc9\u6e08\u307f\u30c7\u30c3\u30ad", "\uad6c\ucd95 \ub371",
                        "\u9810\u7d44\u5361\u5305", "\u9884\u7ec4\u5361\u5305");
                case PackCategory.ThemeBuild:
                    return Pick("Deck building", "Construccion tematica", "Construcao tematica", "Construction de decks",
                        "Deckbau-Packs", "Costruzione mazzi", "\u30c7\u30c3\u30ad\u69cb\u7bc9\u30d1\u30c3\u30af", "\ud14c\ub9c8 \uad6c\ucd95 \ud329",
                        "\u4e3b\u984c\u69cb\u7bc9\u5361\u5305", "\u4e3b\u9898\u6784\u7b51\u5361\u5305");
                case PackCategory.ThemeSupport:
                    return Pick("Theme support", "Refuerzo tematico", "Reforco tematico", "Renforts de themes",
                        "Themen-Verstaerkung", "Supporto tematico", "\u30c6\u30fc\u30de\u5f37\u5316\u30d1\u30c3\u30af", "\ud14c\ub9c8 \uac15\ud654 \ud329",
                        "\u4e3b\u984c\u5f37\u5316\u5361\u5305", "\u4e3b\u9898\u5f3a\u5316\u5361\u5305");
                case PackCategory.Animation:
                    return Pick("Anime packs", "Sobres de anime", "Pacotes de anime", "Packs anime",
                        "Anime-Packs", "Buste anime", "\u30a2\u30cb\u30e1\u30d1\u30c3\u30af", "\uc560\ub2c8\uba54\uc774\uc158 \ud329",
                        "\u52d5\u756b\u5361\u5305", "\u52a8\u753b\u5361\u5305");
                case PackCategory.Manga:
                    return Pick("Manga packs", "Sobres de manga", "Pacotes de manga", "Packs manga",
                        "Manga-Packs", "Buste manga", "\u6f2b\u753b\u30d1\u30c3\u30af", "\ub9cc\ud654 \ud329",
                        "\u6f2b\u756b\u5361\u5305", "\u6f2b\u753b\u5361\u5305");
                case PackCategory.Overseas:
                    return Pick("Overseas packs", "Sobres de importacion", "Pacotes internacionais", "Packs internationaux",
                        "Import-Packs", "Buste internazionali", "\u6d77\u5916\u30d1\u30c3\u30af", "\ud574\uc678 \ud329",
                        "\u6d77\u5916\u5361\u5305", "\u6d77\u5916\u5361\u5305");
                case PackCategory.Special:
                    return Pick("Special packs", "Sobres especiales", "Pacotes especiais", "Packs speciaux",
                        "Spezial-Packs", "Buste speciali", "\u7279\u5225\u30d1\u30c3\u30af", "\uc2a4\ud398\uc15c \ud329",
                        "\u7279\u5225\u5361\u5305", "\u7279\u522b\u5361\u5305");
                case PackCategory.Event:
                    return Pick("Event packs", "Sobres de eventos", "Pacotes de eventos", "Packs evenementiels",
                        "Event-Packs", "Buste evento", "\u30a4\u30d9\u30f3\u30c8\u30d1\u30c3\u30af", "\uc774\ubca4\ud2b8 \ud329",
                        "\u6d3b\u52d5\u5361\u5305", "\u6d3b\u52a8\u5361\u5305");
                default:
                    return Pick("All packs", "Todos los sobres", "Todos os pacotes", "Tous les packs",
                        "Alle Packs", "Tutte le buste", "\u3059\u3079\u3066\u306e\u30d1\u30c3\u30af", "\ubaa8\ub4e0 \ud329",
                        "\u5168\u90e8\u5361\u5305", "\u5168\u90e8\u5361\u5305");
            }
        }

        private static string Pick(string en, string es, string pt, string fr, string de, string it,
            string ja, string ko, string zh, string zhHans = null)
        {
            switch (Language.GetConfig())
            {
                case Language.English: return en;
                case Language.Spanish: return es;
                case Language.Portuguese: return pt;
                case Language.French: return fr;
                case Language.German: return de;
                case Language.Italian: return it;
                case Language.Japanese: return ja;
                case Language.Korean: return ko;
                case Language.SimplifiedChinese: return zhHans ?? zh;
                case Language.TraditionalChinese: return zh;
                default: return zh;
            }
        }
    }
}
