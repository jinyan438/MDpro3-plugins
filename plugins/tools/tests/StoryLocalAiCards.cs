// Read-only snapshots from the installed zh-CN cards.cdb; effect indices verified against script.zip.
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;
internal static partial class StoryLocalAiTests
{
    private static ClientCard Real(int id, CardLocation location = CardLocation.MonsterZone, int controller = 0)
    {
        ClientCard c;
        switch (id)
        {
            case 84815190: // 鲜花女男爵
                c = Card(3000, 2400, (CardType)8225, location, controller, id, "调整＋调整以外的怪兽1只以上\r\n这个卡名的②的效果1回合只能使用1次。\r\n①：1回合1次，以场上1张卡为对象才能发动。那张卡破坏。\r\n②：只在这张卡在场上表侧表示存在才有1次，魔法·陷阱·怪兽的效果发动时才能发动。那个发动无效并破坏。\r\n③：双方的准备阶段，以自己墓地1只9星以下的怪兽为对象才能发动。这张卡回到持有者的额外卡组，作为对象的怪兽特殊召唤。", 10);
                Set(c.Data, "Race", 1); Set(c, "Race", 1);
                Set(c.Data, "Attribute", 8); Set(c, "Attribute", 8);
                Set(c.Data, "Name", "鲜花女男爵"); return c;
            case 55794644: // 主宰者·许珀里翁
                c = Card(2700, 2100, (CardType)33, location, controller, id, "①：这张卡可以把自己的手卡·场上·墓地1只「代行者」怪兽除外，从手卡特殊召唤。\r\n②：1回合1次，从自己墓地把1只天使族·光属性怪兽除外，以场上1张卡为对象才能发动。那张卡破坏。场上有「天空的圣域」存在的场合，这个效果1回合可以使用最多2次。", 8);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "主宰者·许珀里翁"); return c;
            case 63101468: // 耀斑主宰者·许珀里翁
                c = Card(3200, 2600, (CardType)8225, location, controller, id, "调整＋调整以外的天使族怪兽1只以上\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：把1只「代行者」怪兽或者1只有「天空的圣域」的卡名记述的怪兽从手卡·卡组·额外卡组送去墓地才能发动。直到结束阶段，这张卡当作和那只怪兽同名卡使用，得到相同效果。\r\n②：对方把卡的效果发动时，从自己的手卡·墓地把1只天使族怪兽除外，以场上1张卡为对象才能发动。那张卡除外。", 10);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "耀斑主宰者·许珀里翁"); return c;
            case 90290572: // 代行者的近卫 穆恩
                c = Card(1800, 36, (CardType)67108897, location, controller, id, "天使族怪兽2只\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡连接召唤成功的场合才能发动。从卡组把1张「天空的圣域」或者有那个卡名记述的卡送去墓地。场上或者墓地有「天空的圣域」存在的场合，可以作为代替从自己的卡组·墓地选1只「神秘之代行者 厄斯」加入手卡。\r\n②：把自己场上1只天使族怪兽解放，以对方场上1张卡为对象才能发动。那张卡破坏。", 2);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "代行者的近卫 穆恩"); return c;
            case 86066372: // 访问码语者
                c = Card(2300, 170, (CardType)67108897, location, controller, id, "效果怪兽2只以上\r\n对方不能对应这张卡的效果的发动把效果发动。\r\n①：这张卡连接召唤的场合，以那1只作为连接素材的连接怪兽为对象才能发动。这张卡的攻击力上升那只怪兽的连接标记数量×1000。\r\n②：从自己的场上·墓地把1只连接怪兽除外才能发动。对方场上1张卡破坏。这个回合，自己不能为让「访问码语者」的效果发动而把相同属性的怪兽除外。", 4);
                Set(c.Data, "Race", 16777216); Set(c, "Race", 16777216);
                Set(c.Data, "Attribute", 32); Set(c, "Attribute", 32);
                Set(c.Data, "Name", "访问码语者"); return c;
            case 27548199: // 装弹枪管狞猛龙
                c = Card(3000, 2500, (CardType)8225, location, controller, id, "调整＋调整以外的怪兽1只以上\r\n这个卡名的③的效果1回合只能使用1次。\r\n①：这张卡同调召唤成功的场合才能发动。从自己墓地选1只连接怪兽当作装备卡使用给这张卡装备，那个连接标记数量的枪管指示物给这张卡放置。\r\n②：这张卡的攻击力上升这张卡的效果装备的怪兽的攻击力一半数值。\r\n③：对方的效果发动时，把这张卡1个枪管指示物取除才能发动。那个发动无效。", 8);
                Set(c.Data, "Race", 8192); Set(c, "Race", 8192);
                Set(c.Data, "Attribute", 32); Set(c, "Attribute", 32);
                Set(c.Data, "Name", "装弹枪管狞猛龙"); return c;
            case 4280258: // 召命之神弓-阿波罗萨
                c = Card(-2, 135, (CardType)67108897, location, controller, id, "衍生物以外的卡名不同的怪兽2只以上\r\n①：「召命之神弓-阿波罗萨」在自己场上只能有1张表侧表示存在。\r\n②：这张卡的原本攻击力变成作为这张卡的连接素材的怪兽数量×800。\r\n③：对方把怪兽的效果发动时才能发动（同一连锁上最多1次）。这张卡的攻击力下降800，那个发动无效。", 4);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 8); Set(c, "Attribute", 8);
                Set(c.Data, "Name", "召命之神弓-阿波罗萨"); return c;
            case 65741786: // I：P百变莱娜
                c = Card(800, 5, (CardType)67108897, location, controller, id, "连接怪兽以外的怪兽2只\r\n这个卡名的①的效果1回合只能使用1次。\r\n①：对方主要阶段才能发动。用包含这张卡的自己场上的怪兽为素材进行连接召唤。\r\n②：这张卡为连接素材的连接怪兽不会被对方的效果破坏。", 2);
                Set(c.Data, "Race", 16777216); Set(c, "Race", 16777216);
                Set(c.Data, "Attribute", 32); Set(c, "Attribute", 32);
                Set(c.Data, "Name", "I：P百变莱娜"); return c;
            case 21887175: // 双穹之骑士 阿斯特拉姆
                c = Card(3000, 45, (CardType)67108897, location, controller, id, "从额外卡组特殊召唤的怪兽2只以上\r\n①：只要连接召唤的这张卡在怪兽区域存在，对方怪兽不能选择其他怪兽作为攻击对象，对方不能把这张卡作为效果的对象。\r\n②：这张卡和特殊召唤的对方怪兽进行战斗的伤害计算时才能发动1次。这张卡的攻击力只在那次伤害计算时上升那只对方怪兽的攻击力数值。\r\n③：连接召唤的这张卡被对方送去墓地的场合才能发动。场上1张卡回到卡组。", 4);
                Set(c.Data, "Race", 16777216); Set(c, "Race", 16777216);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "双穹之骑士 阿斯特拉姆"); return c;
            case 98127546: // 闭锁世界的冥神
                c = Card(3000, 422, (CardType)67108897, location, controller, id, "效果怪兽4只以上\r\n这张卡连接召唤的场合，对方场上1只怪兽也能作为连接素材。\r\n①：这张卡连接召唤的场合才能发动。对方场上的全部表侧表示怪兽的效果无效化。\r\n②：连接召唤的这张卡不受除以这张卡为对象的效果以外的对方发动的效果影响。\r\n③：1回合1次，包含从墓地把怪兽特殊召唤效果的魔法·陷阱·怪兽的效果由对方发动时才能发动。那个发动无效。", 5);
                Set(c.Data, "Race", 8); Set(c, "Race", 8);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "闭锁世界的冥神"); return c;
            case 48589580: // 天空神骑士 罗德珀耳修斯
                c = Card(2400, 7, (CardType)67108897, location, controller, id, "天使族怪兽2只以上\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：丢弃1张手卡才能发动。把1张「天空的圣域」或者有那个卡名记述的卡从卡组加入手卡。场上有「天空的圣域」存在的场合，可以把加入手卡的卡改成1只天使族怪兽。\r\n②：自己场上的表侧表示的天使族怪兽被送去墓地的场合，从自己墓地把1只天使族怪兽除外才能发动。比除外的怪兽等级高的1只天使族怪兽从手卡特殊召唤。", 3);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "天空神骑士 罗德珀耳修斯"); return c;
            case 50588353: // 水晶机巧-继承玻纤
                c = Card(1500, 5, (CardType)67108897, location, controller, id, "包含调整的怪兽2只\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡连接召唤成功的场合才能发动。从手卡·卡组把1只3星以下的调整守备表示特殊召唤。这个效果特殊召唤的怪兽在这个回合不能把效果发动。\r\n②：对方的主要阶段以及战斗阶段把场上的这张卡除外才能发动。从额外卡组把1只同调怪兽调整当作同调召唤作特殊召唤。", 2);
                Set(c.Data, "Race", 32); Set(c, "Race", 32);
                Set(c.Data, "Attribute", 2); Set(c, "Attribute", 2);
                Set(c.Data, "Name", "水晶机巧-继承玻纤"); return c;
            case 3544583: // 无之毕竟
                c = Card(0, 2100, (CardType)4161, location, controller, id, "通常怪兽×2", 2);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 32); Set(c, "Attribute", 32);
                Set(c.Data, "Name", "无之毕竟"); return c;
            case 88581108: // 真龙皇 法·王·兽
                c = Card(3000, 3000, (CardType)8388641, location, controller, id, "9星怪兽×2只以上\r\n①：1回合1次，把这张卡1个超量素材取除，宣言1个属性才能发动。这个回合，以下效果适用。这个效果在对方回合也能发动。\r\n●场上的表侧表示怪兽变成宣言的属性，宣言的属性的对方怪兽不能攻击，不能把效果发动。\r\n②：只要这张卡在怪兽区域存在，自己手卡的「真龙」怪兽的效果破坏的怪兽从对方场上也能选。", 9);
                Set(c.Data, "Race", 8388608); Set(c, "Race", 8388608);
                Set(c.Data, "Attribute", 32); Set(c, "Attribute", 32);
                Set(c.Data, "Name", "真龙皇 法·王·兽"); return c;
            case 74586817: // PSY骨架王·Ω
                c = Card(2800, 2200, (CardType)8225, location, controller, id, "调整＋调整以外的怪兽1只以上\r\n①：1回合1次，自己·对方的主要阶段才能发动。对方手卡随机选1张，那张卡和表侧表示的这张卡直到下次的自己准备阶段表侧除外。\r\n②：对方准备阶段，以自己或对方的除外状态的1张卡为对象才能发动。那张卡回到墓地。\r\n③：这张卡在墓地存在的场合，以自己或对方的墓地1张其他卡为对象才能发动。那张卡和这张卡回到卡组。", 8);
                Set(c.Data, "Race", 1048576); Set(c, "Race", 1048576);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "PSY骨架王·Ω"); return c;
            case 73580471: // 黑蔷薇龙
                c = Card(2400, 1800, (CardType)8225, location, controller, id, "调整＋调整以外的怪兽1只以上\r\n①：这张卡同调召唤时才能发动。场上的卡全部破坏。\r\n②：1回合1次，从自己墓地把1只植物族怪兽除外，以对方场上1只守备表示怪兽为对象才能发动。那只对方的守备表示怪兽变成表侧攻击表示，那个攻击力直到回合结束时变成0。", 7);
                Set(c.Data, "Race", 8192); Set(c, "Race", 8192);
                Set(c.Data, "Attribute", 4); Set(c, "Attribute", 4);
                Set(c.Data, "Name", "黑蔷薇龙"); return c;
            case 38342335: // 梦幻崩影·独角兽
                c = Card(2200, 42, (CardType)67108897, location, controller, id, "卡名不同的怪兽2只以上\r\n这个卡名的①的效果1回合只能使用1次。\r\n①：这张卡连接召唤的场合，丢弃1张手卡，以场上1张卡为对象才能发动。那张卡回到卡组。这个效果的发动时这张卡是互相连接状态的场合，再让自己可以抽1张。\r\n②：只要互相连接状态的「幻崩」怪兽存在，自己抽卡阶段的通常抽卡数量变成那些「幻崩」怪兽种类的数量。", 3);
                Set(c.Data, "Race", 8); Set(c, "Race", 8);
                Set(c.Data, "Attribute", 32); Set(c, "Attribute", 32);
                Set(c.Data, "Name", "梦幻崩影·独角兽"); return c;
            case 2857636: // 梦幻崩影·凤凰
                c = Card(1900, 160, (CardType)67108897, location, controller, id, "卡名不同的怪兽2只\r\n这个卡名的①的效果1回合只能使用1次。\r\n①：这张卡连接召唤的场合，丢弃1张手卡，以对方场上1张魔法·陷阱卡为对象才能发动。那张卡破坏。这个效果的发动时这张卡是互相连接状态的场合，再让自己可以抽1张。\r\n②：只要这张卡在怪兽区域存在，互相连接状态的自己怪兽不会被战斗破坏。", 2);
                Set(c.Data, "Race", 8); Set(c, "Race", 8);
                Set(c.Data, "Attribute", 4); Set(c, "Attribute", 4);
                Set(c.Data, "Name", "梦幻崩影·凤凰"); return c;
            case 46772449: // 励辉士 入魔蝇王
                c = Card(1900, 0, (CardType)8388641, location, controller, id, "4星怪兽×2\r\n①：自己主要阶段以及对方战斗阶段，对方的手卡·场上的卡数量比自己的手卡·场上的卡数量多的场合，把这张卡1个超量素材取除才能发动（同一连锁上最多1次）。场上的其他卡全部破坏。这个效果的发动后，直到回合结束时对方受到的全部伤害变成0。", 4);
                Set(c.Data, "Race", 8); Set(c, "Race", 8);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "励辉士 入魔蝇王"); return c;
            case 64734921: // 创造之代行者 维纳斯
                c = Card(1600, 0, (CardType)33, location, controller, id, "①：支付500基本分才能发动。从手卡·卡组把1只「神圣球体」特殊召唤。", 3);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "创造之代行者 维纳斯"); return c;
            case 39552864: // 神圣球体
                c = Card(500, 500, (CardType)17, location, controller, id, "被神圣光辉包住的天使灵魂。看见其美丽姿态者，据说能够实现愿望。", 2);
                Set(c.Data, "Race", 4); Set(c, "Race", 4);
                Set(c.Data, "Attribute", 16); Set(c, "Attribute", 16);
                Set(c.Data, "Name", "神圣球体"); return c;
            default: throw new System.ArgumentException("Unknown real-card fixture: " + id);
        }
    }
}
