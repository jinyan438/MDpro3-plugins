// Read-only snapshots from the installed zh-CN card database.
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;
internal static partial class StoryLocalAiTests
{
    private static ClientCard InteractionCard(int id, CardLocation location = CardLocation.MonsterZone)
    {
        switch (id)
        {
            case 63767246:
                var c63767246 = Card(3000, 2500, (CardType)8388641, location, id: id, level: 8, text: "8星怪兽×2\r\n①：1回合1次，魔法卡的效果在场上发动时才能发动。那个效果无效，场上的那张卡作为这张卡的超量素材。\r\n②：对方的攻击宣言时，把这张卡1个超量素材取除才能发动。攻击对象转移为这张卡进行伤害计算。\r\n③：自己场上的其他的表侧表示的超量怪兽被战斗·效果破坏的场合，以自己场上1只超量怪兽为对象才能发动。那只怪兽的攻击力上升那些破坏的怪兽之内1只的原本攻击力数值。");
                Set(c63767246.Data, "Name", "No.38 希望魁龙 银河巨神"); Set(c63767246.Data, "Alias", 0);
                Set(c63767246.Data, "Race", 8192); Set(c63767246, "Race", 8192); Set(c63767246.Data, "Attribute", 16); Set(c63767246, "Attribute", 16);
                Set(c63767246.Data, "Setcode", (long)8061000); return c63767246;
            case 24696097:
                var c24696097 = Card(3300, 2500, (CardType)8225, location, id: id, level: 10, text: "同调怪兽调整＋「星尘龙」\r\n①：1回合1次，可以发动。从自己卡组上面翻开5张并回到卡组。这个回合这张卡可以作出最多有所翻开之中的调整数量的攻击。\r\n②：1回合1次，要让场上的卡破坏的效果的发动时才能发动。那个效果无效并破坏。\r\n③：1回合1次，对方的攻击宣言时以攻击怪兽为对象才能发动。场上的这张卡除外，那次攻击无效。\r\n④：这个③的效果除外的回合的结束阶段发动。这张卡特殊召唤。");
                Set(c24696097.Data, "Name", "流星龙"); Set(c24696097.Data, "Alias", 0);
                Set(c24696097.Data, "Race", 8192); Set(c24696097, "Race", 8192); Set(c24696097.Data, "Attribute", 8); Set(c24696097, "Attribute", 8);
                Set(c24696097.Data, "Setcode", (long)0); return c24696097;
            case 63180841:
                var c63180841 = Card(3300, 2500, (CardType)8225, location, id: id, level: 10, text: "同调怪兽调整＋调整以外的同调怪兽1只以上\r\n这个卡名的②③的效果1回合各能使用1次。\r\n①：自己场上的怪兽为对象的怪兽的效果发动时，从自己墓地把1只调整除外才能发动。那个发动无效并破坏。\r\n②：对方怪兽的攻击宣言时才能发动。那次攻击无效。\r\n③：对方回合，这张卡在墓地存在的场合，把自己场上2只同调怪兽解放才能发动。这张卡特殊召唤。");
                Set(c63180841.Data, "Name", "流星龙·科技属扩张"); Set(c63180841.Data, "Alias", 0);
                Set(c63180841.Data, "Race", 8192); Set(c63180841, "Race", 8192); Set(c63180841.Data, "Attribute", 8); Set(c63180841, "Attribute", 8);
                Set(c63180841.Data, "Setcode", (long)39); return c63180841;
            case 74860293:
                var c74860293 = Card(2600, 2500, (CardType)8225, location, id: id, level: 8, text: "「废品同调士」＋调整以外的怪兽1只以上\r\n①：这张卡同调召唤成功时，以最多有那些作为同调素材的怪兽之内除调整以外的怪兽数量的场上的卡为对象才能发动。那些卡破坏。");
                Set(c74860293.Data, "Name", "废品破坏王"); Set(c74860293.Data, "Alias", 0);
                Set(c74860293.Data, "Race", 1); Set(c74860293, "Race", 1); Set(c74860293.Data, "Attribute", 1); Set(c74860293, "Attribute", 1);
                Set(c74860293.Data, "Setcode", (long)67); return c74860293;
            case 29301450:
                var c29301450 = Card(1600, 40, (CardType)67108897, location, id: id, level: 2, text: "效果怪兽2只\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡用融合·同调·超量·连接怪兽的其中任意种为素材作连接召唤的场合，以自己或对方的场上·墓地1张卡为对象才能发动。那张卡除外。这个回合，自己怪兽不能直接攻击。\r\n②：对方的效果发动时，以包含自己场上的怪兽的场上2只表侧表示怪兽为对象才能发动。那2只怪兽直到结束阶段除外。");
                Set(c29301450.Data, "Name", "S：P小夜骑士"); Set(c29301450.Data, "Alias", 0);
                Set(c29301450.Data, "Race", 1); Set(c29301450, "Race", 1); Set(c29301450.Data, "Attribute", 32); Set(c29301450, "Attribute", 32);
                Set(c29301450.Data, "Setcode", (long)0); return c29301450;
            case 74586817:
                var c74586817 = Card(2800, 2200, (CardType)8225, location, id: id, level: 8, text: "调整＋调整以外的怪兽1只以上\r\n①：1回合1次，自己·对方的主要阶段才能发动。对方手卡随机选1张，那张卡和表侧表示的这张卡直到下次的自己准备阶段表侧除外。\r\n②：对方准备阶段，以自己或对方的除外状态的1张卡为对象才能发动。那张卡回到墓地。\r\n③：这张卡在墓地存在的场合，以自己或对方的墓地1张其他卡为对象才能发动。那张卡和这张卡回到卡组。");
                Set(c74586817.Data, "Name", "PSY骨架王·Ω"); Set(c74586817.Data, "Alias", 0);
                Set(c74586817.Data, "Race", 1048576); Set(c74586817, "Race", 1048576); Set(c74586817.Data, "Attribute", 16); Set(c74586817, "Attribute", 16);
                Set(c74586817.Data, "Setcode", (long)193); return c74586817;
            case 26973555:
                var c26973555 = Card(3000, 2000, (CardType)8388641, location, id: id, level: 1, text: "「No.」怪兽以外的相同阶级的超量怪兽×3\r\n规则上，这张卡的阶级当作1阶使用，这个卡名也当作「未来皇 霍普」卡使用。这张卡也能在自己场上的「未来No.0 未来皇 霍普」上面重叠来超量召唤。\r\n①：这张卡不会被战斗·效果破坏。\r\n②：1回合1次，对方把怪兽的效果发动时，把这张卡1个超量素材取除才能发动。那个发动无效。这个效果把场上的怪兽的效果的发动无效的场合，再得到那个控制权。");
                Set(c26973555.Data, "Name", "未来No.0 未来龙皇 霍普"); Set(c26973555.Data, "Alias", 0);
                Set(c26973555.Data, "Race", 1); Set(c26973555, "Race", 1); Set(c26973555.Data, "Attribute", 16); Set(c26973555, "Attribute", 16);
                Set(c26973555.Data, "Setcode", (long)545194056); return c26973555;
            case 84013237:
                var c84013237 = Card(2500, 2000, (CardType)8388641, location, id: id, level: 4, text: "4星怪兽×2\r\n①：自己或对方的怪兽的攻击宣言时，把这张卡1个超量素材取除才能发动。那次攻击无效。\r\n②：这张卡没有超量素材的状态被选择作为攻击对象的场合发动。这张卡破坏。");
                Set(c84013237.Data, "Name", "No.39 希望皇 霍普"); Set(c84013237.Data, "Alias", 0);
                Set(c84013237.Data, "Race", 1); Set(c84013237, "Race", 1); Set(c84013237.Data, "Attribute", 16); Set(c84013237, "Attribute", 16);
                Set(c84013237.Data, "Setcode", (long)276758600); return c84013237;
            case 44508094:
                var c44508094 = Card(2500, 2000, (CardType)8225, location, id: id, level: 8, text: "调整＋调整以外的怪兽1只以上\r\n①：要让场上的卡破坏的魔法·陷阱·怪兽的效果发动时，把这张卡解放才能发动。那个发动无效并破坏。\r\n②：这张卡的①的效果适用的回合的结束阶段才能发动。为那个效果发动而解放的这张卡从墓地特殊召唤。");
                Set(c44508094.Data, "Name", "星尘龙"); Set(c44508094.Data, "Alias", 0);
                Set(c44508094.Data, "Race", 8192); Set(c44508094, "Race", 8192); Set(c44508094.Data, "Attribute", 8); Set(c44508094, "Attribute", 8);
                Set(c44508094.Data, "Setcode", (long)163); return c44508094;
            case 40939228:
                var c40939228 = Card(4000, 3300, (CardType)8225, location, id: id, level: 11, text: "「救世龙」＋包含龙族同调怪兽的除调整以外的怪兽1只以上\r\n这张卡用同调召唤才能从额外卡组特殊召唤。\r\n①：1回合1次，可以发动。选对方场上1只效果怪兽，那个效果无效。\r\n②：这张卡在通常攻击外加上可以作出最多有自己墓地的「星尘龙」以及有那个卡名记述的同调怪兽数量的攻击。\r\n③：1回合1次，对方把效果发动时才能发动。这张卡直到结束阶段除外，那个发动无效并除外。");
                Set(c40939228.Data, "Name", "流天救世星龙"); Set(c40939228.Data, "Alias", 0);
                Set(c40939228.Data, "Race", 8192); Set(c40939228, "Race", 8192); Set(c40939228.Data, "Attribute", 8); Set(c40939228, "Attribute", 8);
                Set(c40939228.Data, "Setcode", (long)63); return c40939228;
            case 95134949:
                var c95134949 = Card(3000, 3000, (CardType)8388641, location, id: id, level: 12, text: "12星怪兽×3只以上\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：自己·对方回合，把这张卡2个超量素材取除才能发动（这个效果发动的回合，自己不是超量怪兽不能从额外卡组特殊召唤，其他的自己怪兽不能直接攻击）。把1只「No.1」～「No.100」其中任意种的「No.」怪兽当作超量召唤从额外卡组特殊召唤。\r\n②：对方怪兽的攻击宣言时才能发动。那只对方怪兽的攻击力变成0。");
                Set(c95134949.Data, "Name", "No.99 希望皇 霍普德拉戈纳"); Set(c95134949.Data, "Alias", 95134948);
                Set(c95134949.Data, "Race", 1); Set(c95134949, "Race", 1); Set(c95134949.Data, "Attribute", 16); Set(c95134949, "Attribute", 16);
                Set(c95134949.Data, "Setcode", (long)276766792); return c95134949;
            case 41522092:
                var c41522092 = Card(0, 0, (CardType)8388641, location, id: id, level: 1, text: "相同阶级的超量怪兽×2\r\n规则上，这张卡的阶级当作1阶使用。\r\n①：这张卡的攻击力·守备力上升自己场上以及对方墓地的超量怪兽的阶级合计×500。\r\n②：对方怪兽不能选择其他怪兽作为攻击对象，对方不能把场上的其他卡作为效果的对象。\r\n③：1回合1次，对方在场上把效果发动时，把这张卡1个超量素材取除才能发动。得到对方场上1只怪兽的控制权。这个回合，这张卡不会被战斗·效果破坏。");
                Set(c41522092.Data, "Name", "未来No.0 未来皇 霍普·异热同心"); Set(c41522092.Data, "Alias", 0);
                Set(c41522092.Data, "Race", 1); Set(c41522092, "Race", 1); Set(c41522092.Data, "Attribute", 16); Set(c41522092, "Attribute", 16);
                Set(c41522092.Data, "Setcode", (long)541711073352); return c41522092;
            case 66011101:
                var c66011101 = Card(1200, 1200, (CardType)8388641, location, id: id, level: 4, text: "4星怪兽×2\r\n这个卡名的效果1回合只能使用1次。\r\n①：可以把这张卡2个超量素材取除，从以下效果选择1个发动。\r\n●自己抽2张。那之后，选自己1张手卡丢弃。下次的自己抽卡阶段跳过。\r\n●从自己墓地把1只怪兽守备表示特殊召唤。下次的自己主要阶段1跳过。\r\n●自己场上1只怪兽的攻击力直到回合结束时变成2倍。下次的自己回合的战斗阶段跳过。");
                Set(c66011101.Data, "Name", "No.60 刻不知之杜加雷斯"); Set(c66011101.Data, "Alias", 0);
                Set(c66011101.Data, "Race", 8); Set(c66011101, "Race", 8); Set(c66011101.Data, "Attribute", 4); Set(c66011101, "Attribute", 4);
                Set(c66011101.Data, "Setcode", (long)72); return c66011101;
            default: return CoreCard(id, location);
        }
    }
}
