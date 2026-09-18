using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MDPro3.Plugins.Features.PackBrowser
{
    internal enum PackCategory
    {
        All,
        Basic,
        Structure,
        ThemeBuild,
        ThemeSupport,
        Animation,
        Manga,
        Overseas,
        Special,
        Event,
    }

    /// <summary>Product families, based on Reko Wiki's series pack list. See plugins/README.md.</summary>
    internal static class PackCategories
    {
        public const int Count = 10;

        // Core boosters have unrelated codes. The value is the official period + release sequence
        // shown by Reko Wiki (401 = fourth period, first set). Period-one products have no such
        // number and therefore keep their original Vol./Booster caption.
        private static readonly Dictionary<string, string> BasicSequences = BuildBasicSequences();
        private static readonly HashSet<string> BasicCodes = new HashSet<string>(
            BasicSequences.Keys, StringComparer.OrdinalIgnoreCase);

        private static readonly Regex EarlyBasicNames = Rule(@"^(VOL\.\s*[1-7]|BOOSTER\s+[1-7])$");
        private static readonly Regex StructureCodes = Rule(@"^(SD\d+|SDMY|SDKS|SDM|SR\d+|ST\d+|YSD\d+|TD\d+|TTD\d*|CH\d+|DS\d+|VS\d+|YU|KA|JY|PE|SY2|SK2|SJ2)$");
        private static readonly Regex StructureNames = Rule(@"^(STRUCTURE DECK|STARTER (DECK|BOX)|TACTICAL[ -]TRY DECK|THE CHRONICLES DECK|EX($|[ -]))|^\u30b9\u30c8\u30e9\u30af\u30c1\u30e3\u30fc\u30c7\u30c3\u30ad|^\u30c7\u30e5\u30a8\u30ea\u30b9\u30c8(\u30a8\u30f3\u30c8\u30ea\u30fc\u30c7\u30c3\u30ad|\u30bb\u30c3\u30c8)");
        private static readonly Regex ThemeBuildCodes = Rule(@"^(DB(?!LE$)[A-Z]{2}|SP(RG|TR|HR|WR|DS|FE)|DT\d+)$");
        private static readonly Regex ThemeBuildNames = Rule(@"^(DECK BUILD PACK|BOOSTER SP|DUEL TERMINAL)|^\u30c7\u30c3\u30ad\u30d3\u30eb\u30c9\u30d1\u30c3\u30af|^\u30d6\u30fc\u30b9\u30bf\u30fcSP|^\u30c7\u30e5\u30a8\u30eb\u30bf\u30fc\u30df\u30ca\u30eb");
        private static readonly Regex ThemeSupportCodes = Rule(@"^(DP\d+|TW\d+|TTP\d+|RV\d+|LVP\d+|LVDS|SLT\d+|SLF\d+)$");
        private static readonly Regex ThemeSupportNames = Rule(@"^(DUELIST PACK|TERMINAL WORLD|TACTICAL[ -]TRY PACK|REVOLUTION BOOSTER|LINK VRAINS (PACK|DUELIST SET)|SELECTION\s*\d)|^\u30c7\u30e5\u30a8\u30ea\u30b9\u30c8\u30d1\u30c3\u30af");
        private static readonly Regex AnimationCodes = Rule(@"^(CP(\d+|[ZLDF]\d+)|AC\d+|MVP\d+|YMP\d+|LPG\d+)$");
        private static readonly Regex AnimationNames = Rule(@"^(COLLECT(ION|OR'?S) PACK|ANIMATION CHRONICLE|LIMITED PACK GX)|MOVIE PACK|^\u30b3\u30ec\u30af(\u30b7\u30e7\u30f3|\u30bf\u30fc\u30ba)\u30d1\u30c3\u30af|^\u30a2\u30cb\u30e1\u30fc\u30b7\u30e7\u30f3[ \u30fb]*\u30af\u30ed\u30cb\u30af\u30eb");
        private static readonly Regex MangaCodes = Rule(@"^(PP\d+|\d{2}PP|P[246]|VJ(MP|C)?|WJ(MP|C)?|SJMP|JHS|JCY|Y[AFGORSZ]\d+|YOS\d+|LE\d+|L3|VE\d+|VP\d+)$");
        private static readonly Regex MangaNames = Rule(@"^(PREMIUM PACK|LIMITED EDITION|V JUMP EDITION)|^(V\u30b8\u30e3\u30f3\u30d7|VJ)(\s*\d|\s*\u5b9a\u671f)|^(\u9031\u520a\u5c11\u5e74|\u6700\u5f37)\u30b8\u30e3\u30f3\u30d7|\u7b2c?\d+\u5dfb");
        private static readonly Regex OverseasCodes = Rule(@"^(EXP\d+|EP\d+|WPP\d+)$");
        private static readonly Regex OverseasNames = Rule(@"^(EXTRA PACK|WORLD PREMIERE PACK|WORLD PREMIUM PACK)");
        private static readonly Regex EventCodes = Rule(@"^(JF\d+|VF\d+|VJCF|AT\d+|TP\d+|\d{2}TP|\d{2}PR|\d{2}SP|\d{2}CP|\d{2}CC|\d{2}YC|YCSW|TDPR|PRTX|PR\d+|DF\d+|CCC\d+|NKC\d+|PPC\d+|MSC\d+|DEC\d+|YGOPR|MOV\d*|MVPC|MVPL)$");
        private static readonly Regex EventNames = Rule(@"PROMOTION PACK|CHAMPIONSHIP PRIZE|TOURNAMENT"
            + @"|\u30d7\u30ed\u30e2\u30fc\u30b7\u30e7\u30f3|\u30c8\u30fc\u30ca\u30e1\u30f3\u30c8"
            + @"|\u30b8\u30e3\u30f3\u30d7(\u30d5\u30a7\u30b9\u30bf|\u30d3\u30af\u30c8\u30ea\u30fc\u30ab\u30fc\u30cb\u30d0\u30eb)"
            + @"|\u30ad\u30e3\u30f3\u30da\u30fc\u30f3(?!\u30e2\u30fc\u30c9)|\u30b3\u30e9\u30dc|\u914d\u5e03"
            + @"|\u53c2\u52a0\u8005|\u53c2\u52a0\u8cde|\u5165\u5834\u8005|\u5165\u8cde\u8005|\u666f\u54c1|\u30d7\u30ec\u30bc\u30f3\u30c8");

        public static PackCategory Classify(string code, string name)
        {
            code = (code ?? string.Empty).Trim().ToUpperInvariant();
            name = (name ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();

            if (BasicCodes.Contains(code) || EarlyBasicNames.IsMatch(name))
                return PackCategory.Basic;
            if (StructureCodes.IsMatch(code) || StructureNames.IsMatch(name))
                return PackCategory.Structure;
            if (ThemeBuildCodes.IsMatch(code) || ThemeBuildNames.IsMatch(name))
                return PackCategory.ThemeBuild;
            if (ThemeSupportCodes.IsMatch(code) || ThemeSupportNames.IsMatch(name))
                return PackCategory.ThemeSupport;
            if (AnimationCodes.IsMatch(code) || AnimationNames.IsMatch(name))
                return PackCategory.Animation;
            if (OverseasCodes.IsMatch(code) || OverseasNames.IsMatch(name))
                return PackCategory.Overseas;

            // Festival codes are distinct from magazine gifts, even when both mention Jump.
            if (EventCodes.IsMatch(code))
                return PackCategory.Event;
            if (MangaCodes.IsMatch(code) || MangaNames.IsMatch(name))
                return PackCategory.Manga;
            if (EventNames.IsMatch(name))
                return PackCategory.Event;

            // Anniversary sets, reprints, books, game bundles and unrecognized products stay visible.
            return PackCategory.Special;
        }

        public static string BasicSequence(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            return BasicSequences.TryGetValue(code.Trim(), out string sequence)
                ? sequence
                : string.Empty;
        }

        private static Dictionary<string, string> BuildBasicSequences()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddPeriod(result, 2, "MR PS CA TB SM LN SC MA PH");
            AddPeriod(result, 3, "301 302 303 304 305 306 307 308 309");
            AddPeriod(result, 4, "SOD RDS FET TLM CRV EEN SOI EOJ");
            AddPeriod(result, 5, "POTD CDIP STON FOTB TAEV GLAS PTDN LODT");
            AddPeriod(result, 6, "TDGS CSOC CRMS RGBT ANPR SOVR ABPF TSHD");
            AddPeriod(result, 7, "DREV STBL STOR EXVC GENF PHSW ORCS GAOV");
            AddPeriod(result, 8, "REDU ABYR CBLZ LTGY JOTL SHSP LVAL PRIO");
            AddPeriod(result, 9, "DUEA NECH SECE CROS CORE DOCS BOSH SHVI TDIL INOV RATE MACR");
            AddPeriod(result, 10, "COTD CIBR EXFO FLOD CYHO SOFU SAST DANE RIRA CHIM IGAS ETCO");
            AddPeriod(result, 11, "ROTD PHRA BLVO LIOV DAMA BODE BACH DIFO POTE DABL PHHY CYAC");
            AddPeriod(result, 12, "DUNE AGOV PHNI LEDE INFO ROTA SUDA ALIN");
            AddPeriod(result, 13, "DUAD DOOD BPRO BLZD CORI BETB IMPH");
            return result;
        }

        private static void AddPeriod(Dictionary<string, string> result, int period, string codes)
        {
            string[] values = codes.Split(' ');
            for (int i = 0; i < values.Length; i++)
                result[values[i]] = period.ToString() + (i + 1).ToString("D2");
        }

        private static Regex Rule(string pattern)
        {
            return new Regex(pattern, RegexOptions.CultureInvariant);
        }
    }
}
