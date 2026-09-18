using System;
using System.IO;
using MDPro3.Plugins.Features.PackBrowser;

internal static class PackCategoryTests
{
    private static int checks;

    private static void Expect(PackCategory expected, string code, string name = "")
    {
        var actual = PackCategories.Classify(code, name);
        if (actual != expected)
            throw new Exception(code + " / " + name + ": expected " + expected + ", got " + actual);
        checks++;
    }

    private static void ExpectSequence(string expected, string code)
    {
        string actual = PackCategories.BasicSequence(code);
        if (actual != expected)
            throw new Exception(code + ": expected sequence " + expected + ", got " + actual);
        checks++;
    }

    private static void Main(string[] args)
    {
        Expect(PackCategory.Basic, "DUEA");
        Expect(PackCategory.Basic, "IMPH");
        Expect(PackCategory.Basic, "309");
        Expect(PackCategory.Basic, " tb ");
        Expect(PackCategory.Basic, "Vol.1", "Vol.1");
        Expect(PackCategory.Basic, "Booster 7", "Booster 7");
        Expect(PackCategory.Special, "", "BOOSTER PACK COLLECTORS TIN 2003");
        Expect(PackCategory.Structure, "SD48");
        Expect(PackCategory.Structure, "SR14");
        Expect(PackCategory.Structure, "ST19");
        Expect(PackCategory.Structure, "CH02");
        Expect(PackCategory.Structure, "", "TACTICAL-TRY DECK");
        Expect(PackCategory.Structure, "EX", "EX-R [ deck set ]");
        Expect(PackCategory.Structure, "STARTER BO...", "STARTER BOX");
        Expect(PackCategory.ThemeBuild, "DBPR");
        Expect(PackCategory.ThemeBuild, "SPFE");
        Expect(PackCategory.ThemeBuild, "DT14");
        Expect(PackCategory.Special, "DBLE");
        Expect(PackCategory.Special, "DB12");
        Expect(PackCategory.Special, "SP99");
        Expect(PackCategory.ThemeSupport, "DP1");
        Expect(PackCategory.ThemeSupport, "DP29");
        Expect(PackCategory.ThemeSupport, "TW03");
        Expect(PackCategory.ThemeSupport, "TTP1");
        Expect(PackCategory.ThemeSupport, "RV01");
        Expect(PackCategory.ThemeSupport, "LVDS");
        Expect(PackCategory.ThemeSupport, "", "SELECTION\uff15");
        Expect(PackCategory.Animation, "CPZ1");
        Expect(PackCategory.Animation, "CP20");
        Expect(PackCategory.Animation, "AC04");
        Expect(PackCategory.Animation, "MVP1");
        Expect(PackCategory.Animation, "YMP1");
        Expect(PackCategory.Animation, "LPG1");
        Expect(PackCategory.Manga, "", "PREMIUM PACK 2");
        Expect(PackCategory.Manga, "26PP");
        Expect(PackCategory.Manga, "VJMP");
        Expect(PackCategory.Manga, "VP26");
        Expect(PackCategory.Manga, "YOS3");
        Expect(PackCategory.Manga, "YR03");
        Expect(PackCategory.Manga, "", "LIMITED EDITION");
        Expect(PackCategory.Manga, "", "V\u30b8\u30e3\u30f3\u30d7 1999\u5e74 8\u6708\u53f7 \u7279\u5225\u30d7\u30ec\u30bc\u30f3\u30c8 A\u30bb\u30c3\u30c8");
        Expect(PackCategory.Overseas, "EXP4");
        Expect(PackCategory.Overseas, "EP19");
        Expect(PackCategory.Overseas, "WPP7");
        Expect(PackCategory.Overseas, "", "EXTRA PACK");
        Expect(PackCategory.Event, "26TP");
        Expect(PackCategory.Event, "AT09");
        Expect(PackCategory.Event, "JF20");
        Expect(PackCategory.Event, "VJCF");
        Expect(PackCategory.Event, "MVPC");
        Expect(PackCategory.Event, "G3", "\u5927\u4f1a\u5165\u8cde\u8005\u7279\u5178\u30ab\u30fc\u30c9");
        Expect(PackCategory.Special, "G3", "\u540c\u68b1\u30ab\u30fc\u30c9");
        Expect(PackCategory.Special, "GBI", "V\u30b8\u30e3\u30f3\u30d7\u30d6\u30c3\u30af\u30b9");
        Expect(PackCategory.Special, "", "V\u30b8\u30e3\u30f3\u30d7\u30d6\u30c3\u30af\u30b9 \u7a76\u6975\u653b\u7565BOOK \u4e0a\u5dfb \u30ad\u30e3\u30f3\u30da\u30fc\u30f3\u30e2\u30fc\u30c9\u5f81\u670d\u7de8");
        Expect(PackCategory.Special, "20TH");
        Expect(PackCategory.Special, "RC02");
        Expect(PackCategory.Special, "UNKNOWN", "New unknown product");
        Expect(PackCategory.Special, null, null);
        ExpectSequence("201", "MR");
        ExpectSequence("209", "PH");
        ExpectSequence("301", "301");
        ExpectSequence("309", "309");
        ExpectSequence("401", "SOD");
        ExpectSequence("1008", "DANE");
        ExpectSequence("1112", "CYAC");
        ExpectSequence("1201", "DUNE");
        ExpectSequence("1208", "ALIN");
        ExpectSequence("1301", "DUAD");
        ExpectSequence("1305", "CORI");
        ExpectSequence("1307", "IMPH");
        ExpectSequence("", "Vol.1");
        ExpectSequence("", "UNKNOWN");

        // The optional TSV comes from pack.db, with the same code fallback as PacksManager.
        var counts = new int[PackCategories.Count];
        if (args.Length > 0)
        {
            foreach (var row in File.ReadAllLines(args[0]))
            {
                var fields = row.Split('\t');
                var category = PackCategories.Classify(fields[0], fields[1]);
                if (category == PackCategory.All || (int)category >= counts.Length)
                    throw new Exception("Invalid category for " + row);
                counts[(int)category]++;
                Console.WriteLine(category + "\t" + row);
            }
        }

        Console.WriteLine("PASS: " + checks + " classification regressions");
        for (int i = 1; i < counts.Length; i++)
            Console.WriteLine((PackCategory)i + ": " + counts[i]);
    }
}
