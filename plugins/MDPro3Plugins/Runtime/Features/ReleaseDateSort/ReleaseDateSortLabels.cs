using MDPro3.Utility;

namespace MDPro3.Plugins.Features.ReleaseDateSort
{
    /// <summary>
    /// Label of the new sort entry.
    ///
    /// The plugin must not write into the game translation files, so the label is
    /// translated here. Every language the game supports gets an entry.
    /// </summary>
    public static class ReleaseDateSortLabels
    {
        public static string Label
        {
            get
            {
                switch (Language.GetConfig())
                {
                    case Language.English:
                        return "Release date";
                    case Language.Spanish:
                        return "Fecha de lanzamiento";
                    case Language.Portuguese:
                        return "Data de lancamento";
                    case Language.French:
                        return "Date de sortie";
                    case Language.German:
                        return "Erscheinungsdatum";
                    case Language.Italian:
                        return "Data di uscita";
                    case Language.Japanese:
                        return "\u767a\u58f2\u65e5";
                    case Language.Korean:
                        return "\ubc1c\ub9e4\uc77c";
                    case Language.TraditionalChinese:
                        return "\u767c\u884c\u65e5\u671f";
                    default:
                        return "\u53d1\u5e03\u65f6\u95f4";
                }
            }
        }

        /// <summary>Label used in logs (always the same, language independent).</summary>
        public const string DiagnosticLabel = "Release date / \u53d1\u5e03\u65f6\u95f4";
    }
}
