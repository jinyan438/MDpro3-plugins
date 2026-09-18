"""Build the pack cover table for the MDPro3 pack browser plugin.

For every pack in Data/pack/pack.db one cover card is picked:

  1. the wiki list (pack_covers_rekowiki.json, next to this script) when its cover name matches a
     card of that pack closely enough,
  2. otherwise the rarity rule: the single card of the pack with HR, else the single card with
     QCSE, else the single card with UL (this is what OCG prints on the booster artwork),
  3. otherwise a low confidence wiki match,
  4. otherwise a representative card (highest rarity, lowest card id).

The result is written as a generated C# table because plugins may not ship asset files.
"""

import difflib
import io
import json
import re
import sqlite3
import sys
import unicodedata
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

# this script lives in <repo>/plugins/tools
ROOT = Path(__file__).resolve().parents[2]
PACK_DB = ROOT / "MDPro3" / "Data" / "pack" / "pack.db"
COVERS = Path(__file__).resolve().parent / "pack_covers_rekowiki.json"
TARGET = (
    ROOT
    / "plugins"
    / "MDPro3Plugins"
    / "Runtime"
    / "Features"
    / "PackBrowser"
    / "PackCoverTable.g.cs"
)

WIKI_HIGH = 0.90
WIKI_LOW = 0.55

# 1 = wiki (close match), 2 = HR, 3 = QCSE, 4 = UL, 5 = wiki (loose match), 6 = representative
KIND_WIKI = 1
KIND_HR = 2
KIND_QCSE = 3
KIND_UL = 4
KIND_WIKI_WEAK = 5
KIND_FALLBACK = 6

DROP = re.compile(
    r'[\s\u3000()（）\[\]【】「」『』《》〔〕<>＜＞,，、。!！?？"“”\'‘’/／\\*＊＋+＝=~～^\-_—―─・·．.]'
)


def normalize(text):
    if not text:
        return ""
    text = unicodedata.normalize("NFKC", text)
    return DROP.sub("", text).lower()


def locale_names(locale):
    path = ROOT / "MDPro3" / "Data" / "locales" / locale / "cards.cdb"
    con = sqlite3.connect(path)
    return {r[0]: r[1] for r in con.execute("select id, name from texts")}


def load_packs():
    """[(fullName, code, date, [(card id, rarity), ...]), ...] in pack.db order."""
    con = sqlite3.connect(PACK_DB)
    order = []
    packs = {}
    for card_id, pack_id, pack, rarity, date in con.execute(
        "select id, pack_id, pack, rarity, date from pack"
    ):
        if pack not in packs:
            packs[pack] = {"code": pack_id.split("-")[0], "date": date, "cards": []}
            order.append(pack)
        packs[pack]["cards"].append((card_id, rarity))
    return [
        (pack, packs[pack]["code"], packs[pack]["date"], packs[pack]["cards"])
        for pack in order
    ]


def tokens(rarity):
    return {t.strip() for t in (rarity or "").split(",") if t.strip()}


def unique_by_token(cards, token):
    hits = [cid for cid, rar in cards if token in tokens(rar)]
    return hits[0] if len(hits) == 1 else 0


def unique_exact(cards, token):
    hits = [cid for cid, rar in cards if (rar or "").strip() == token]
    return hits[0] if len(hits) == 1 else 0


RARITY_RANK = {"UR": 4, "SR": 3, "R": 2, "N": 1}


def monster_ids():
    """Card ids of monsters; a pack cover is nearly always a monster."""
    path = ROOT / "MDPro3" / "Data" / "cards.cdb"
    try:
        con = sqlite3.connect(path)
        return {r[0] for r in con.execute("select id from datas where type & 1 = 1")}
    except sqlite3.Error:
        return set()


def representative(cards, monsters):
    best = (-1, -1, 0)
    for cid, rar in cards:
        rank = max((RARITY_RANK.get(t, 0) for t in tokens(rar)), default=0)
        key = (1 if cid in monsters else 0, rank)
        if key > best[:2] or (key == best[:2] and (best[2] == 0 or cid < best[2])):
            best = (key[0], key[1], cid)
    return best[2]


def rarity_cover(cards):
    """The card the OCG prints on the booster artwork, if the pack has one."""
    cid = unique_by_token(cards, "HR")
    if cid:
        return cid, KIND_HR

    # the 25th anniversary special slot is the only card with QCSE as its only rarity
    cid = unique_exact(cards, "QCSE")
    if cid:
        return cid, KIND_QCSE

    cid = unique_by_token(cards, "UL")
    if cid:
        return cid, KIND_UL

    cid = unique_exact(cards, "SE")
    if cid:
        return cid, KIND_UL

    return 0, 0


def wiki_match(name, cards, tw, cn):
    """Best name match of the wiki cover among the cards of that pack -> (id, ratio)."""
    want = normalize(name)
    if not want:
        return 0, 0.0

    best = (0.0, 0)
    for cid, _rar in cards:
        for candidate in (tw.get(cid, ""), cn.get(cid, "")):
            text = normalize(candidate)
            if not text:
                continue
            if want == text:
                return cid, 1.0
            ratio = difflib.SequenceMatcher(None, want, text).ratio()
            if ratio > best[0]:
                best = (ratio, cid)
    return best[1], best[0]


def build():
    try:
        wiki = json.loads(COVERS.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        print("cannot read the crawled cover list:", exc)
        return 1, [], {}, [0, 0], []

    tw = locale_names("zh-TW")
    cn = locale_names("zh-CN")
    monsters = monster_ids()
    agree = [0, 0]
    disagreements = []
    wiki_by_code = {}
    for entry in wiki["packs"]:
        code = entry.get("packCode") or ""
        cover = entry.get("coverCard") or ""
        if code and cover:
            wiki_by_code.setdefault(code, []).append(cover)

    rows = []
    stats = {}

    for pack, code, date, cards in load_packs():
        full_name = f"{date} {pack}"
        rule_id, rule_kind = rarity_cover(cards)

        wiki_id, ratio = 0, 0.0
        for cover in wiki_by_code.get(code, []):
            cid, r = wiki_match(cover, cards, tw, cn)
            if r > ratio:
                wiki_id, ratio = cid, r

        if wiki_id and ratio >= WIKI_HIGH:
            cover_id, kind = wiki_id, KIND_WIKI
            if rule_id:
                agree[0] += 1
                if rule_id == wiki_id:
                    agree[1] += 1
                else:
                    disagreements.append((full_name, wiki_id, rule_id))
        elif rule_id:
            cover_id, kind = rule_id, rule_kind
        elif wiki_id and ratio >= WIKI_LOW:
            cover_id, kind = wiki_id, KIND_WIKI_WEAK
        else:
            cover_id, kind = representative(cards, monsters), KIND_FALLBACK

        stats[kind] = stats.get(kind, 0) + 1
        rows.append((full_name, cover_id, kind))

    return 0, rows, stats, agree, disagreements


def write_table(rows, stats):
    kinds = {
        KIND_WIKI: "wiki",
        KIND_HR: "hr",
        KIND_QCSE: "qcse",
        KIND_UL: "ul",
        KIND_WIKI_WEAK: "wikiWeak",
        KIND_FALLBACK: "fallback",
    }

    lines = [
        "// <auto-generated>",
        "// Generated by plugins/tools/build_pack_cover_table.py. Do not edit by hand.",
        "//",
        "// Cover card of every pack of Data/pack/pack.db. The wiki list",
        "// (Reko/Komica pack list, plugins/tools/pack_covers_rekowiki.json) wins when its cover name",
        "// matches a card of that pack; otherwise the OCG rarity rule is used (a single HR card",
        "// marks the booster artwork, then QCSE, then UL); otherwise a representative card.",
        "//",
        "// kind: 1 = wiki cover, 2 = single HR card, 3 = single QCSE card, 4 = single UL card,",
        "//       5 = loose wiki match, 6 = representative card (highest rarity)",
        "// </auto-generated>",
        "",
        "namespace MDPro3.Plugins.Features.PackBrowser",
        "{",
        "    /// <summary>Cover card of every pack, keyed by the pack name of Data/pack/pack.db.</summary>",
        "    internal static class PackCoverTable",
        "    {",
        "        public static int Count => packs.Length;",
        "",
        "        /// <summary>Cover card id of a pack, or 0 when the pack is not in the table.</summary>",
        "        public static int Get(string packFullName)",
        "        {",
        "            int index = System.Array.IndexOf(packs, packFullName);",
        "            return index < 0 ? 0 : ids[index];",
        "        }",
        "",
        "        /// <summary>Where the cover came from, see the file header.</summary>",
        "        public static int Kind(string packFullName)",
        "        {",
        "            int index = System.Array.IndexOf(packs, packFullName);",
        "            return index < 0 ? 0 : kinds[index];",
        "        }",
        "",
        "        public static string DescribeKind(int kind)",
        "        {",
        "            switch (kind)",
        "            {",
        '                case 1: return "wiki cover";',
        '                case 2: return "single HR card";',
        '                case 3: return "single QCSE card";',
        '                case 4: return "single UL card";',
        '                case 5: return "wiki cover (loose)";',
        '                case 6: return "representative card";',
        '                default: return "unknown";',
        "            }",
        "        }",
        "",
    ]

    lines.append("        private static readonly string[] packs =")
    lines.append("        {")
    for full_name, _cid, _kind in rows:
        lines.append(
            '            "' + full_name.replace("\\", "\\\\").replace('"', '\\"') + '",'
        )
    lines.append("        };")
    lines.append("")
    lines.append("        private static readonly int[] ids =")
    lines.append("        {")
    values = [str(cid) for _n, cid, _k in rows]
    for i in range(0, len(values), 12):
        lines.append("            " + ", ".join(values[i : i + 12]) + ",")
    lines.append("        };")
    lines.append("")
    lines.append("        private static readonly byte[] kinds =")
    lines.append("        {")
    values = [str(k) for _n, _c, k in rows]
    for i in range(0, len(values), 24):
        lines.append("            " + ", ".join(values[i : i + 24]) + ",")
    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text("\n".join(lines), encoding="utf-8")
    return kinds


def main():
    code, rows, stats, agree, disagreements = build()
    if code != 0:
        return code

    print(f"packs in pack.db: {len(rows)}")
    total = sum(stats.values())
    for kind in sorted(stats):
        label = {
            1: "wiki cover (close)",
            2: "single HR card",
            3: "single QCSE card",
            4: "single UL card",
            5: "wiki cover (loose)",
            6: "representative card",
        }[kind]
        print(f"   {label:<22} {stats[kind]:>4}  ({stats[kind] * 100.0 / total:.1f}%)")

    print()
    print(f"packs where the wiki cover and the rarity rule both exist: {agree[0]}")
    if agree[0]:
        print(f"   same card: {agree[1]}, different: {agree[0] - agree[1]}")
    for full_name, wiki_id, rule_id in disagreements[:10]:
        print(f"   differ: {full_name} -> wiki {wiki_id}, rule {rule_id}")

    labels = write_table(rows, stats)
    print()
    print(f"written {TARGET}")
    print(f"  rows: {len(rows)}, kind labels: {len(labels)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
