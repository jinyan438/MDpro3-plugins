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

        private static string Pick(string en, string es, string pt, string fr, string de, string it,
            string ja, string ko, string zh)
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
                case Language.TraditionalChinese: return zh;
                default: return zh;
            }
        }
    }
}
