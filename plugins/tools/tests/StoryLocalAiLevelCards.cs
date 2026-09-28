// Snapshots of installed card data; the runtime planner contains no branches for these test identities.
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;
internal static partial class StoryLocalAiTests
{
    private static ClientCard LevelCard(int id, CardLocation location = CardLocation.MonsterZone)
    {
        ClientCard card;
        switch (id)
        {
            case 863795:
                card = Card(1600, 1400, (CardType)33, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡召唤·特殊召唤的场合，以「银河召唤师」以外的自己墓地1只「光子」怪兽或「银河」怪兽为对象才能发动。那只怪兽守备表示特殊召唤。\r\n②：以自己场上1只其他的光属性怪兽为对象才能发动。那只怪兽的等级直到回合结束时变成4星。");
                Set(card.Data, "Name", "银河召唤师"); Set(card.Data, "Race", 2); Set(card, "Race", 2);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Setcode", (long)123); return card;
            case 8129306:
                card = Card(800, 1800, (CardType)33, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：自己场上的怪兽被解放的场合才能发动。这张卡从手卡守备表示特殊召唤。\r\n②：以自己场上最多2只植物族怪兽为对象才能发动。那些怪兽的等级直到回合结束时上升2星。");
                Set(card.Data, "Name", "六花精 樱草"); Set(card.Data, "Race", 1024); Set(card, "Race", 1024);
                Set(card.Data, "Attribute", 2); Set(card, "Attribute", 2); Set(card.Data, "Setcode", (long)321); return card;
            case 11234702:
                card = Card(1300, 500, (CardType)4129, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡召唤·特殊召唤的场合，以除「科技属 螺旋桨蛇」外的自己墓地1只4星以下的「科技属」怪兽为对象才能发动。那只怪兽特殊召唤。这个效果特殊召唤的怪兽的效果无效化。\r\n②：把墓地的这张卡除外，以自己场上1只「科技属」怪兽为对象才能发动。那只怪兽的等级直到回合结束时上升或下降1星。");
                Set(card.Data, "Name", "科技属 螺旋桨蛇"); Set(card.Data, "Race", 262144); Set(card, "Race", 262144);
                Set(card.Data, "Attribute", 2); Set(card, "Attribute", 2); Set(card.Data, "Setcode", (long)39); return card;
            case 29092121:
                card = Card(1000, 2000, (CardType)33, location, id: id, level: 6, text: "这个卡名的①③的效果1回合各能使用1次。\r\n①：自己场上有「光波」怪兽召唤·特殊召唤的场合才能发动。这张卡从手卡特殊召唤。\r\n②：1回合1次，以自己场上2只「光波」怪兽为对象才能发动。那些怪兽的等级直到回合结束时变成8星。\r\n③：这张卡被战斗·效果破坏送去墓地的场合，把墓地的这张卡除外才能发动。从卡组把1只「光波」怪兽加入手卡。");
                Set(card.Data, "Name", "光波复叶机"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Setcode", (long)229); return card;
            case 50482813:
                card = Card(1000, 1000, (CardType)4129, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡召唤·特殊召唤成功的场合才能发动。把这张卡以外的自己场上的风属性怪兽数量的卡从自己卡组上面翻开，从那之中选1张加入手卡，剩下的卡用喜欢的顺序回到卡组最下面。\r\n②：把墓地的这张卡除外，以自己场上1只3星以上的风属性怪兽为对象才能发动。那只怪兽的等级下降2星。");
                Set(card.Data, "Name", "疾行机人 吹持童子"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 8); Set(card, "Attribute", 8); Set(card.Data, "Setcode", (long)8214); return card;
            case 60283232:
                card = Card(800, 1000, (CardType)4129, location, id: id, level: 5, text: "这个卡名的②③的效果1回合各能使用1次。\r\n①：把自己场上的这张卡作为同调素材的场合，可以把这张卡当作调整以外的怪兽使用。\r\n②：自己主要阶段才能发动。进行1只4星以下的怪兽的召唤。这个效果的发动后，直到回合结束时自己不是同调怪兽不能从额外卡组特殊召唤。\r\n③：把墓地的这张卡除外，以自己场上1只同调怪兽为对象才能发动。那只怪兽的等级下降最多4星。");
                Set(card.Data, "Name", "车轮同调士"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Setcode", (long)4119); return card;
            case 74741494:
                card = Card(0, 0, (CardType)2, location, id: id, level: 0, text: "自己场上表侧表示存在的全部怪兽的等级直到结束阶段时下降1星。");
                Set(card.Data, "Name", "能力调整"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Setcode", (long)0); return card;
            case 83334932:
                card = Card(800, 1200, (CardType)4129, location, id: id, level: 2, text: "这个卡名的①的效果1回合只能使用1次。\r\n①：自己墓地没有魔法·陷阱卡存在的场合，把这张卡从手卡丢弃才能发动。从卡组把「超重武者 摩托-Q」以外的1只「超重武者」怪兽加入手卡。\r\n②：1回合1次，以自己场上1只机械族怪兽为对象才能发动。那只怪兽的等级上升2星。");
                Set(card.Data, "Name", "超重武者 摩托-Q"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 1); Set(card, "Attribute", 1); Set(card.Data, "Setcode", (long)154); return card;
            case 88552992:
                card = Card(1300, 1400, (CardType)33, location, id: id, level: 4, text: "1回合1次，自己的主要阶段时才能发动。自己场上的全部名字带有「先史遗产」的怪兽的等级上升1星。");
                Set(card.Data, "Name", "先史遗产 黄金航天飞机"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Setcode", (long)112); return card;
            case 88774734:
                card = Card(2500, 2500, (CardType)33, location, id: id, level: 8, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡在手卡·墓地存在，自己场上有光·暗属性的龙族怪兽2只以上存在的场合才能发动。这张卡守备表示特殊召唤。这个效果特殊召唤的这张卡从场上离开的场合除外。\r\n②：自己主要阶段才能发动。自己场上的全部怪兽的等级直到回合结束时变成8星。");
                Set(card.Data, "Name", "螺旋龙 核球龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Setcode", (long)0); return card;
            case 98555327:
                card = Card(0, 1800, (CardType)33, location, id: id, level: 4, text: "①：1回合1次，自己主要阶段才能发动。这张卡的等级直到回合结束时上升4星。\r\n②：把这张卡解放才能发动。从卡组把「银河魔导师」以外的1张「银河」卡加入手卡。");
                Set(card.Data, "Name", "银河魔导师"); Set(card.Data, "Race", 2); Set(card, "Race", 2);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Setcode", (long)123); return card;
            case 98154550:
                card = Card(1200, 900, (CardType)33, location, id: id, level: 5, text: "①：自己场上没有怪兽存在的场合，这张卡可以从手卡特殊召唤。\r\n②：1回合1次，自己主要阶段才能发动。自己场上的全部昆虫族怪兽的等级上升1星。");
                Set(card.Data, "Name", "原生蝴蝶"); Set(card.Data, "Race", 2048); Set(card, "Race", 2048);
                Set(card.Data, "Attribute", 8); Set(card, "Attribute", 8); Set(card.Data, "Setcode", (long)0); return card;
            default: throw new System.Exception("Missing level card snapshot: " + id);
        }
    }
}
