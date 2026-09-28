// Read-only snapshots of the installed card database for Galaxy level regressions.
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;
internal static partial class StoryLocalAiTests
{
    private static ClientCard GalaxyCard(int id, CardLocation location = CardLocation.MonsterZone)
    {
        ClientCard card;
        switch (id)
        {
            case 85747929:
                card = Card(2000, 0, (CardType)8388641, location, id: id, level: 4, text: "4星怪兽×2\r\n这个卡名的②③的效果1回合各能使用1次。\r\n①：自己场上的其他的光属性怪兽的攻击力上升500。\r\n②：把这张卡1个超量素材取除才能发动。从卡组选1张「光子」卡或「银河」卡加入手卡或送去墓地。\r\n③：自己场上有光属性怪兽特殊召唤的场合，以那之内的1只为对象才能发动。那只怪兽的等级直到回合结束时变成4星或8星。");
                Set(card.Data, "Name", "银河光子龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)5570683); return card;
            case 46659709:
                card = Card(2000, 0, (CardType)33, location, id: id, level: 5, text: "这个卡名的②的效果1回合只能使用1次。\r\n①：从手卡把1只其他的光属性怪兽送去墓地才能发动。这张卡从手卡守备表示特殊召唤。\r\n②：这张卡特殊召唤时才能发动。从卡组把1只「银河」怪兽加入手卡。");
                Set(card.Data, "Name", "银河战士"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)123); return card;
            case 58069384:
                card = Card(2100, 1600, (CardType)8388641, location, id: id, level: 5, text: "机械族5星怪兽×2\r\n①：1回合1次，把这张卡1个超量素材取除，以自己墓地1只「电子龙」为对象才能发动。那只怪兽特殊召唤。\r\n②：自己·对方回合1次，从自己的手卡·场上（表侧表示）把1只「电子龙」除外才能发动。这张卡的攻击力直到回合结束时上升2100。\r\n③：这张卡被对方的效果送去墓地的场合才能发动。从额外卡组把1只机械族融合怪兽特殊召唤。");
                Set(card.Data, "Name", "电子龙·新星"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)4243); return card;
            case 63767246:
                card = Card(3000, 2500, (CardType)8388641, location, id: id, level: 8, text: "8星怪兽×2\r\n①：1回合1次，魔法卡的效果在场上发动时才能发动。那个效果无效，场上的那张卡作为这张卡的超量素材。\r\n②：对方的攻击宣言时，把这张卡1个超量素材取除才能发动。攻击对象转移为这张卡进行伤害计算。\r\n③：自己场上的其他的表侧表示的超量怪兽被战斗·效果破坏的场合，以自己场上1只超量怪兽为对象才能发动。那只怪兽的攻击力上升那些破坏的怪兽之内1只的原本攻击力数值。");
                Set(card.Data, "Name", "No.38 希望魁龙 银河巨神"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)8061000); return card;
            case 93717133:
                card = Card(3000, 2500, (CardType)33, location, id: id, level: 8, text: "①：这张卡可以把自己场上2只攻击力2000以上的怪兽解放从手卡特殊召唤。\r\n②：这张卡和对方怪兽进行战斗的战斗步骤，以那1只对方怪兽为对象才能发动。那只对方怪兽和场上的这张卡除外。这个效果除外的怪兽在战斗阶段结束时回到场上，这个效果把超量怪兽除外的场合，这张卡的攻击力上升把那只超量怪兽除外时的超量素材数量×500。");
                Set(card.Data, "Name", "银河眼光子龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)276496469); return card;
            case 89132148:
                card = Card(500, 2000, (CardType)33, location, id: id, level: 4, text: "这个卡名的②的效果1回合只能使用1次。\r\n①：以自己场上1只「光子」怪兽或「银河」怪兽为对象才能发动。从自己的手卡·场上把这张卡当作持有以下效果的装备魔法卡使用给那只自己怪兽装备。\r\n●装备怪兽的攻击力上升500，不会被战斗破坏。\r\n②：把装备的这张卡送去墓地才能发动。除「光子轨道」外的1只「光子」怪兽或「银河」怪兽从卡组加入手卡。");
                Set(card.Data, "Name", "光子轨道"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)85); return card;
            case 63956833:
                card = Card(0, 0, (CardType)2, location, id: id, level: 0, text: "这个卡名的卡在1回合只能发动1张，这张卡发动的回合，自己不是「光子」怪兽以及「银河」怪兽不能召唤·特殊召唤。\r\n①：支付2000基本分，以自己墓地1只「光子」怪兽为对象才能发动。把持有和那只怪兽相同等级的卡组1只「银河」怪兽和作为对象的墓地的怪兽守备表示特殊召唤。这个效果特殊召唤的怪兽的攻击力变成2000，效果无效化。");
                Set(card.Data, "Name", "银河天翔"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)123); return card;
            case 16643334:
                card = Card(1800, 2500, (CardType)8388641, location, id: id, level: 4, text: "4星怪兽×2\r\n①：这张卡超量召唤的场合才能发动。从手卡把1只「光子」怪兽特殊召唤。\r\n②：只要超量召唤的这张卡在怪兽区域存在，自己场上的攻击力2000以上的怪兽不会被对方的效果破坏，对方不能把那些作为效果的对象。\r\n③：对方回合1次，把这张卡1个超量素材取除，以自己的墓地·除外状态的1只「银河眼光子龙」为对象才能发动。那只怪兽特殊召唤。");
                Set(card.Data, "Name", "辉光龙 光子爆龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)85); return card;
            case 3356494:
                card = Card(2000, 5, (CardType)67108897, location, id: id, level: 2, text: "包含攻击力2000以上的怪兽的光属性怪兽2只\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡连接召唤成功的场合，以自己墓地1只「光子」怪兽或者「银河」怪兽为对象才能发动。那只怪兽加入手卡。\r\n②：对方主要阶段，把「光子」卡和「银河」卡共2张或者「银河眼光子龙」1只从手卡丢弃，以对方场上1只特殊召唤的怪兽为对象才能发动。那只怪兽破坏。");
                Set(card.Data, "Name", "银河眼煌星龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)4219); return card;
            default: throw new System.Exception("Missing Galaxy snapshot: " + id);
        }
    }
}
