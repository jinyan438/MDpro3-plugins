// Read-only snapshots of installed cards.cdb for resource conversion regressions.
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;
internal static partial class StoryLocalAiTests
{
    private static ClientCard ExtensionCard(int id, CardLocation location = CardLocation.MonsterZone)
    {
        ClientCard card;
        switch (id)
        {
            case 47705572:
                card = Card(2000, 1800, (CardType)16777249, location, id: id, level: 6, text: "←1 【灵摆】 1→\r\n①：自己不是「月光」怪兽不能灵摆召唤。这个效果不会被无效化。\r\n②：1回合1次，自己主要阶段才能发动。自己的场上·墓地的怪兽作为融合素材除外，把1只「月光」融合怪兽融合召唤。\r\n【怪兽效果】\r\n①：只要这张卡在怪兽区域存在，自己的「月光」怪兽向守备表示怪兽攻击的场合，给与对方为攻击力超过那个守备力的数值的战斗伤害。");
                Set(card.Data, "Name", "月光狼"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 83190280:
                card = Card(1200, 800, (CardType)16777249, location, id: id, level: 3, text: "←5 【灵摆】 5→\r\n①：1回合1次，以自己墓地1只「月光」怪兽为对象才能发动。那只怪兽特殊召唤。这个效果特殊召唤的怪兽不能攻击，效果无效化，结束阶段破坏。\r\n【怪兽效果】\r\n这个卡名的怪兽效果1回合只能使用1次。\r\n①：场上的这张卡被战斗·效果破坏的场合，以自己墓地1只「月光」怪兽为对象才能发动。那只怪兽特殊召唤。");
                Set(card.Data, "Name", "月光虎"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 58570206:
                card = Card(0, 0, (CardType)2, location, id: id, level: 0, text: "①：自己的手卡·场上的怪兽作为融合素材，把1只融合怪兽融合召唤（融合素材怪兽必须是3只以上）。对方场上有怪兽存在的场合，也能把最多有那个数量的自己的额外卡组的怪兽除外作为融合素材。那个场合，融合召唤时自己失去那些除外的怪兽的攻击力合计数值的基本分。");
                Set(card.Data, "Name", "多层融合"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)70); return card;
            case 97682931:
                card = Card(900, 1400, (CardType)4129, location, id: id, level: 3, text: "这个卡名的①的效果1回合只能使用1次，②的效果在决斗中只能使用1次。\r\n①：「动力工具」同调怪兽或者7·8星的龙族同调怪兽同调召唤的场合，手卡的这张卡也能作为同调素材。\r\n②：这张卡在墓地存在，自己场上有7星以上的同调怪兽存在的场合才能发动。自己卡组最上面的卡送去墓地，这张卡特殊召唤。这个效果特殊召唤的这张卡的等级变成1星。");
                Set(card.Data, "Name", "革命同调士"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 1); Set(card, "Attribute", 1); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)4119); return card;
            case 90290572:
                card = Card(1800, 36, (CardType)67108897, location, id: id, level: 2, text: "天使族怪兽2只\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡连接召唤成功的场合才能发动。从卡组把1张「天空的圣域」或者有那个卡名记述的卡送去墓地。场上或者墓地有「天空的圣域」存在的场合，可以作为代替从自己的卡组·墓地选1只「神秘之代行者 厄斯」加入手卡。\r\n②：把自己场上1只天使族怪兽解放，以对方场上1张卡为对象才能发动。那张卡破坏。");
                Set(card.Data, "Name", "代行者的近卫 穆恩"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)68); return card;
            case 48589580:
                card = Card(2400, 7, (CardType)67108897, location, id: id, level: 3, text: "天使族怪兽2只以上\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：丢弃1张手卡才能发动。把1张「天空的圣域」或者有那个卡名记述的卡从卡组加入手卡。场上有「天空的圣域」存在的场合，可以把加入手卡的卡改成1只天使族怪兽。\r\n②：自己场上的表侧表示的天使族怪兽被送去墓地的场合，从自己墓地把1只天使族怪兽除外才能发动。比除外的怪兽等级高的1只天使族怪兽从手卡特殊召唤。");
                Set(card.Data, "Name", "天空神骑士 罗德珀耳修斯"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)266); return card;
            case 2344618:
                card = Card(0, 0, (CardType)131074, location, id: id, level: 0, text: "（注：暂时无法正常使用）\r\n\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡发动的回合的自己主要阶段才能发动。从卡组把1只「月光」怪兽送去墓地。\r\n②：自己把「月光」怪兽融合召唤的场合才能发动。自己的墓地·除外状态的1张「融合」加入手卡。那之后，可以选自己1张手卡丢弃。那个场合，这个回合，自己把「月光」怪兽融合召唤的场合只有1次，也能把自己墓地的怪兽除外作为融合素材。");
                Set(card.Data, "Name", "月光舞蹈会"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 63184227:
                card = Card(500, 2000, (CardType)33, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡在手卡·墓地存在，自己场上的怪兽被解放的场合才能发动。这张卡特殊召唤。这个效果特殊召唤的这张卡从场上离开的场合除外。\r\n②：用这张卡为同调素材把「战士」、「同调士」、「星尘」同调怪兽之内任意种同调召唤的场合才能发动。在自己场上把1只「星尘衍生物」（龙族·光·1星·攻/守0）特殊召唤。");
                Set(card.Data, "Name", "星尘尾迹"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)163); return card;
            case 365213:
                card = Card(0, 0, (CardType)131074, location, id: id, level: 0, text: "①：作为这张卡的发动时的效果处理，从手卡·卡组选1只龙族·1星怪兽在卡组最上面放置。\r\n②：双方不能让场上的「星尘龙」以及有那个卡名记述的同调怪兽回到额外卡组。\r\n③：同调怪兽特殊召唤的场合才能发动。从以下效果选1个适用。这个回合，自己的「光来的奇迹」的效果不能有相同效果适用。\r\n●自己从卡组抽1张。\r\n●从手卡把1只调整特殊召唤。");
                Set(card.Data, "Name", "光来的奇迹"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            case 37799519:
                card = Card(1500, 1000, (CardType)4129, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡在手卡·墓地存在的场合，把自己场上1只怪兽解放才能发动。这张卡特殊召唤。这个效果特殊召唤的这张卡从场上离开的场合除外。这个效果的发动后，直到回合结束时自己不是同调怪兽不能从额外卡组特殊召唤。\r\n②：这张卡召唤·特殊召唤的场合才能发动。从卡组把有「星尘龙」的卡名记述的1张魔法·陷阱卡加入手卡。");
                Set(card.Data, "Name", "星尘同调士"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)269942947); return card;
            case 58481572:
                card = Card(2400, 1800, (CardType)97, location, id: id, level: 6, text: "这张卡用「假面变化」的效果才能特殊召唤。\r\n①：只要这张卡在怪兽区域存在，被送去对方墓地的卡不去墓地而除外。\r\n②：1回合1次，对方在抽卡阶段以外从卡组把卡加入手卡的场合才能发动。对方手卡随机1张除外。");
                Set(card.Data, "Name", "假面英雄 暗爪"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)40968); return card;
            case 59509952:
                card = Card(2800, 2300, (CardType)33, location, id: id, level: 8, text: "①：自己墓地的天使族怪兽只有4只的场合，这张卡可以从手卡特殊召唤。\r\n②：这张卡的①的方法特殊召唤成功的场合，以自己墓地1只天使族怪兽为对象发动。那只天使族怪兽加入手卡。\r\n③：只要这张卡在怪兽区域存在，双方不能把怪兽特殊召唤。\r\n④：场上的表侧表示的这张卡被送去墓地的场合，不去墓地回到持有者卡组最上面。");
                Set(card.Data, "Name", "大天使 克里斯提亚"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            case 55794644:
                card = Card(2700, 2100, (CardType)33, location, id: id, level: 8, text: "①：这张卡可以把自己的手卡·场上·墓地1只「代行者」怪兽除外，从手卡特殊召唤。\r\n②：1回合1次，从自己墓地把1只天使族·光属性怪兽除外，以场上1张卡为对象才能发动。那张卡破坏。场上有「天空的圣域」存在的场合，这个效果1回合可以使用最多2次。");
                Set(card.Data, "Name", "主宰者·许珀里翁"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)367); return card;
            default: return ComboCard(id, location);
        }
    }
}
