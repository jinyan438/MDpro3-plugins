// Read-only zh-CN database snapshots for native-core regressions.
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;
internal static partial class StoryLocalAiTests
{
    private static ClientCard CoreCard(int id, CardLocation location = CardLocation.MonsterZone)
    {
        ClientCard card;
        switch (id)
        {
            case 24094653:
                card = Card(0, 0, (CardType)2, location, id: id, level: 0, text: "①：自己的手卡·场上的怪兽作为融合素材，把1只融合怪兽融合召唤。");
                Set(card.Data, "Name", "融合"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)70); return card;
            case 87931906:
                card = Card(0, 0, (CardType)2, location, id: id, level: 0, text: "这个卡名的卡在1回合只能发动1张。\r\n①：从自己的手卡·场上把「月光」融合怪兽卡决定的融合素材怪兽送去墓地，把那1只融合怪兽从额外卡组融合召唤。对方场上有从额外卡组特殊召唤的怪兽存在的场合自己的卡组·额外卡组的「月光」怪兽也能有最多1只作为融合素材。");
                Set(card.Data, "Name", "月光融合"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)4587743); return card;
            case 1845204:
                card = Card(0, 0, (CardType)2, location, id: id, level: 0, text: "这个卡名的卡在1回合只能发动1张。\r\n①：支付1000基本分才能发动。把1只5星以下的融合怪兽当作融合召唤从额外卡组特殊召唤。这个效果特殊召唤的怪兽不能攻击，结束阶段破坏。");
                Set(card.Data, "Name", "简易融合"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)70); return card;
            case 11317977:
                card = Card(100, 600, (CardType)33, location, id: id, level: 2, text: "①：可以把这张卡从手卡丢弃，从以下效果选择1个发动。\r\n●从自己墓地把「月光黑羊」以外的1只「月光」怪兽加入手卡。\r\n●从卡组把1张「融合」加入手卡。\r\n②：这张卡成为融合召唤的素材送去墓地的场合才能发动。从自己的额外卡组（表侧）·墓地把「月光黑羊」以外的1只「月光」怪兽加入手卡。");
                Set(card.Data, "Name", "月光黑羊"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 81196066:
                card = Card(2000, 1500, (CardType)97, location, id: id, level: 6, text: "「月光」怪兽×2\r\n这个卡名的①②③的效果1回合各能使用1次。\r\n①：这张卡融合召唤的场合才能发动。从卡组把1张「月华香」加入手卡。\r\n②：以自己场上1张其他的「月光」卡为对象才能发动。那张卡回到手卡·额外卡组。那之后，可以从手卡把1只「月光」怪兽特殊召唤。\r\n③：把墓地的这张卡除外才能发动。这个回合中，对方场上的怪兽的攻击力下降自身的原本守备力数值。");
                Set(card.Data, "Name", "月光舞香姬"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 48444114:
                card = Card(0, 0, (CardType)2, location, id: id, level: 0, text: "①：以自己墓地1只「月光」怪兽为对象才能发动。那只怪兽特殊召唤。\r\n②：把墓地的这张卡除外，丢弃1张手卡才能发动。从卡组把1只「月光」怪兽加入手卡。");
                Set(card.Data, "Name", "月华香"); Set(card.Data, "Race", 0); Set(card, "Race", 0);
                Set(card.Data, "Attribute", 0); Set(card, "Attribute", 0); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            case 35763582:
                card = Card(1400, 400, (CardType)33, location, id: id, level: 3, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡被效果送去墓地的场合才能发动。从卡组把「月光银狗」以外的1只「月光」怪兽特殊召唤。只要这个效果特殊召唤的怪兽在自己场上表侧表示存在，自己不是「月光」怪兽不能从额外卡组特殊召唤。\r\n②：魔法·陷阱卡的效果在场上发动时，从自己墓地把这张卡和1只「月光」融合怪兽除外才能发动。那个发动无效。");
                Set(card.Data, "Name", "月光银狗"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 14152693:
                card = Card(1200, 1000, (CardType)33, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡召唤·特殊召唤的场合才能发动。从手卡把1张「月光」卡送去墓地，自己抽1张。\r\n②：这张卡被效果送去墓地的场合，以除「月光翠鸟」外的自己的墓地·除外状态的1只4星以下的「月光」怪兽为对象才能发动。那只怪兽守备表示特殊召唤。这个效果特殊召唤的怪兽的效果无效化。");
                Set(card.Data, "Name", "月光翠鸟"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 50546208:
                card = Card(800, 2000, (CardType)33, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡在手卡·墓地存在的场合，以「月光黄鼬」以外的自己场上1张「月光」卡为对象才能发动。那张卡回到手卡，这张卡守备表示特殊召唤。这个效果特殊召唤的这张卡从场上离开的场合除外。\r\n②：这张卡被效果送去墓地的场合才能发动。从卡组把1张「月光」魔法·陷阱卡加入手卡。");
                Set(card.Data, "Name", "月光黄鼬"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 35618217:
                card = Card(1400, 800, (CardType)33, location, id: id, level: 4, text: "这个卡名的②的效果1回合只能使用1次。\r\n①：1回合1次，从卡组·额外卡组把1只「月光」怪兽送去墓地才能发动。这个回合，把表侧表示的这张卡作为融合素材的场合，可以作为送去墓地的那只怪兽的同名卡来成为融合素材。\r\n②：这张卡被效果送去墓地的场合，以自己墓地1张「融合」为对象才能发动。那张卡加入手卡。\r\n③：这张卡被除外的场合才能发动。这个回合，对方在战斗阶段中不能把效果发动。");
                Set(card.Data, "Name", "月光彩雏"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 8379983:
                card = Card(600, 1600, (CardType)33, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡召唤·特殊召唤的场合才能发动。从卡组把「月光金狮子」以外的1只「月光」怪兽加入手卡。那之后，选自己1张手卡丢弃。\r\n②：这张卡在怪兽区域存在的状态，「月光」怪兽被送去自己墓地的场合，以那之内的1只为对象才能发动（伤害步骤也能发动）。那只怪兽加入手卡。");
                Set(card.Data, "Name", "月光金狮子"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 51777272:
                card = Card(2400, 2000, (CardType)97, location, id: id, level: 7, text: "「月光」怪兽×2\r\n①：这张卡不会被战斗破坏。\r\n②：1回合1次，自己主要阶段1把这张卡以外的自己场上1只「月光」怪兽解放才能发动。这个回合，对方怪兽各有1次不会被战斗破坏，这张卡可以向全部对方怪兽各作2次攻击。\r\n③：这张卡的攻击宣言时发动。给与对方100伤害。");
                Set(card.Data, "Name", "月光舞猫姬"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 97165977:
                card = Card(2800, 2500, (CardType)97, location, id: id, level: 8, text: "「月光舞猫姬」＋「月光」怪兽\r\n这张卡用以上记的卡为融合素材的融合召唤才能从额外卡组特殊召唤。\r\n①：这张卡不会被对方的效果破坏。\r\n②：1回合1次，自己主要阶段1才能发动。这个回合，对方怪兽各有1次不会被战斗破坏，这张卡可以向全部对方怪兽各作2次攻击。\r\n③：这张卡战斗破坏对方怪兽时发动。这张卡的攻击力直到战斗阶段结束时上升200。");
                Set(card.Data, "Name", "月光舞豹姬"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 24550676:
                card = Card(3500, 3000, (CardType)97, location, id: id, level: 10, text: "「月光舞豹姬」＋「月光」怪兽×2\r\n这张卡用以上记的卡为融合素材的融合召唤才能特殊召唤。\r\n①：场上的这张卡不会被对方的效果破坏，对方不能把场上的这张卡作为效果的对象。\r\n②：这张卡在同1次的战斗阶段中可以作2次攻击。\r\n③：1回合1次，这张卡向怪兽攻击的伤害步骤结束时才能发动。对方场上的特殊召唤的怪兽全部破坏。");
                Set(card.Data, "Name", "月光舞狮子姬"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 54701958:
                card = Card(3800, 3500, (CardType)97, location, id: id, level: 12, text: "「月光舞狮子姬」＋「月光」怪兽×3\r\n这张卡用以上记的卡为融合素材的融合召唤才能特殊召唤。\r\n①：这张卡只要在怪兽区域存在，不受「月光」卡以外的卡的效果影响。\r\n②：这张卡在同1次的战斗阶段中可以作2次攻击。\r\n③：自己·对方回合1次，从额外卡组把1只「月光」怪兽送去墓地才能发动。对方场上的特殊召唤的怪兽全部破坏。");
                Set(card.Data, "Name", "月光舞狮子神姬"); Set(card.Data, "Race", 32768); Set(card, "Race", 32768);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)223); return card;
            case 65305469:
                card = Card(0, 0, (CardType)8388641, location, id: id, level: 1, text: "「No.」怪兽以外的相同阶级的超量怪兽×2\r\n规则上，这张卡的阶级当作1阶使用。\r\n①：这张卡不会被战斗破坏，这张卡的战斗发生的双方的战斗伤害变成0。\r\n②：这张卡和对方怪兽进行战斗的伤害步骤结束时才能发动。那只对方怪兽的控制权直到战斗阶段结束时得到。\r\n③：场上的这张卡被效果破坏的场合，可以作为代替把这张卡1个超量素材取除。");
                Set(card.Data, "Name", "未来No.0 未来皇 霍普"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 65305468);
                Set(card.Data, "Setcode", (long)545194056); return card;
            case 26973555:
                card = Card(3000, 2000, (CardType)8388641, location, id: id, level: 1, text: "「No.」怪兽以外的相同阶级的超量怪兽×3\r\n规则上，这张卡的阶级当作1阶使用，这个卡名也当作「未来皇 霍普」卡使用。这张卡也能在自己场上的「未来No.0 未来皇 霍普」上面重叠来超量召唤。\r\n①：这张卡不会被战斗·效果破坏。\r\n②：1回合1次，对方把怪兽的效果发动时，把这张卡1个超量素材取除才能发动。那个发动无效。这个效果把场上的怪兽的效果的发动无效的场合，再得到那个控制权。");
                Set(card.Data, "Name", "未来No.0 未来龙皇 霍普"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)545194056); return card;
            case 41522092:
                card = Card(0, 0, (CardType)8388641, location, id: id, level: 1, text: "相同阶级的超量怪兽×2\r\n规则上，这张卡的阶级当作1阶使用。\r\n①：这张卡的攻击力·守备力上升自己场上以及对方墓地的超量怪兽的阶级合计×500。\r\n②：对方怪兽不能选择其他怪兽作为攻击对象，对方不能把场上的其他卡作为效果的对象。\r\n③：1回合1次，对方在场上把效果发动时，把这张卡1个超量素材取除才能发动。得到对方场上1只怪兽的控制权。这个回合，这张卡不会被战斗·效果破坏。");
                Set(card.Data, "Name", "未来No.0 未来皇 霍普·异热同心"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)541711073352); return card;
            case 86331741:
                card = Card(2000, 2000, (CardType)8388641, location, id: id, level: 4, text: "4星怪兽×2\r\n这个卡名的①的效果1回合只能使用1次。\r\n①：把这张卡1个超量素材取除，以「我我我我魔术师」以外的自己墓地1只超量怪兽为对象才能发动。那只怪兽效果无效特殊召唤。\r\n②：有这张卡在作为超量素材中的「未来皇 霍普」超量怪兽得到以下效果。\r\n●把这张卡2个超量素材取除，以自己场上1只超量怪兽为对象才能发动。直到回合结束时，那只怪兽的攻击力变成4000，效果无效化。");
                Set(card.Data, "Name", "我我我我魔术师"); Set(card.Data, "Race", 2); Set(card, "Race", 2);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)84); return card;
            case 45852939:
                card = Card(2500, 1200, (CardType)8388641, location, id: id, level: 4, text: "4星怪兽×2\r\n这张卡超量召唤的场合，可以让自己场上的4阶怪兽作为4星怪兽来成为素材。这个卡名的②的效果1回合只能使用1次。\r\n①：把这张卡1个超量素材取除才能发动。这个回合，这张卡在同1次的战斗阶段中最多2次可以向怪兽攻击。\r\n②：这张卡被送去墓地的场合，以自己墓地2只其他的4阶以下的超量怪兽为对象才能发动。那2只之内的1只特殊召唤，另1只作为那超量素材。");
                Set(card.Data, "Name", "蚀之双子"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            case 291414:
                card = Card(0, 0, (CardType)4129, location, id: id, level: 1, text: "自己对「集心龙」1回合只能有1次特殊召唤，把这张卡作为同调素材的场合，不是「救世」怪兽的同调召唤不能使用。\r\n①：这张卡的卡名只要在场上·墓地存在当作「救世龙」使用。\r\n②：把这张卡抽到时，把这张卡给对方观看才能发动。这张卡从手卡特殊召唤。自己场上有8星以上的龙族同调怪兽存在的场合，可以再从卡组把1只龙族·1星怪兽特殊召唤。");
                Set(card.Data, "Name", "集心龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            case 40939228:
                card = Card(4000, 3300, (CardType)8225, location, id: id, level: 11, text: "「救世龙」＋包含龙族同调怪兽的除调整以外的怪兽1只以上\r\n这张卡用同调召唤才能从额外卡组特殊召唤。\r\n①：1回合1次，可以发动。选对方场上1只效果怪兽，那个效果无效。\r\n②：这张卡在通常攻击外加上可以作出最多有自己墓地的「星尘龙」以及有那个卡名记述的同调怪兽数量的攻击。\r\n③：1回合1次，对方把效果发动时才能发动。这张卡直到结束阶段除外，那个发动无效并除外。");
                Set(card.Data, "Name", "流天救世星龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 8); Set(card, "Attribute", 8); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)63); return card;
            case 44508094:
                card = Card(2500, 2000, (CardType)8225, location, id: id, level: 8, text: "调整＋调整以外的怪兽1只以上\r\n①：要让场上的卡破坏的魔法·陷阱·怪兽的效果发动时，把这张卡解放才能发动。那个发动无效并破坏。\r\n②：这张卡的①的效果适用的回合的结束阶段才能发动。为那个效果发动而解放的这张卡从墓地特殊召唤。");
                Set(card.Data, "Name", "星尘龙"); Set(card.Data, "Race", 8192); Set(card, "Race", 8192);
                Set(card.Data, "Attribute", 8); Set(card, "Attribute", 8); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)163); return card;
            case 63977008:
                card = Card(1300, 500, (CardType)4129, location, id: id, level: 3, text: "①：这张卡召唤时，以自己墓地1只2星以下的怪兽为对象才能发动。那只怪兽守备表示特殊召唤。这个效果特殊召唤的怪兽的效果无效化。");
                Set(card.Data, "Name", "废品同调士"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)269942851); return card;
            case 11069680:
                card = Card(400, 200, (CardType)33, location, id: id, level: 2, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：从手卡把这张卡和1只调整丢弃才能发动。从卡组把1只「同调士」怪兽加入手卡。\r\n②：这张卡作为同调素材送去墓地的场合，以自己墓地1只调整为对象才能发动。那只怪兽守备表示特殊召唤。这个回合，这个效果特殊召唤的怪兽的效果不能发动。");
                Set(card.Data, "Name", "废品转换者"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 1); Set(card, "Attribute", 1); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)67); return card;
            case 77075360:
                card = Card(1800, 1000, (CardType)8225, location, id: id, level: 5, text: "「同调士」调整＋调整以外的怪兽1只以上\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡同调召唤的场合才能发动（这个效果发动的回合，自己不是同调怪兽不能从额外卡组特殊召唤）。从卡组把「同调士」调整尽可能守备表示特殊召唤（相同等级最多1只）。\r\n②：这个回合同调召唤的这张卡和怪兽进行战斗的攻击宣言时才能发动。这张卡的攻击力直到回合结束时变成原本攻击力的2倍。");
                Set(card.Data, "Name", "废品增速者"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 8); Set(card, "Attribute", 8); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)67); return card;
            case 37799519:
                card = Card(1500, 1000, (CardType)4129, location, id: id, level: 4, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡在手卡·墓地存在的场合，把自己场上1只怪兽解放才能发动。这张卡特殊召唤。这个效果特殊召唤的这张卡从场上离开的场合除外。这个效果的发动后，直到回合结束时自己不是同调怪兽不能从额外卡组特殊召唤。\r\n②：这张卡召唤·特殊召唤的场合才能发动。从卡组把有「星尘龙」的卡名记述的1张魔法·陷阱卡加入手卡。");
                Set(card.Data, "Name", "星尘同调士"); Set(card.Data, "Race", 32); Set(card, "Race", 32);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)269942947); return card;
            case 29301450:
                card = Card(1600, 0, (CardType)67108897, location, id: id, level: 2, text: "效果怪兽2只\r\n这个卡名的①②的效果1回合各能使用1次。\r\n①：这张卡用融合·同调·超量·连接怪兽的其中任意种为素材作连接召唤的场合，以自己或对方的场上·墓地1张卡为对象才能发动。那张卡除外。这个回合，自己怪兽不能直接攻击。\r\n②：对方的效果发动时，以包含自己场上的怪兽的场上2只表侧表示怪兽为对象才能发动。那2只怪兽直到结束阶段除外。");
                Set(card.Data, "Name", "S：P小夜骑士"); Set(card.Data, "Race", 1); Set(card, "Race", 1);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            case 55285840:
                card = Card(2400, 2000, (CardType)8388641, location, id: id, level: 4, text: "4星怪兽×2\r\n这个卡名的②的效果1回合只能使用1次。\r\n①：自己·对方的准备阶段才能发动。把对方卡组最上面的卡作为这张卡的超量素材。\r\n②：自己·对方回合可以发动。这张卡的超量素材最多3种类（怪兽·魔法·陷阱）取除。那之后，那些种类的以下效果适用。\r\n●怪兽：这张卡直到结束阶段除外。\r\n●魔法：自己抽1张。\r\n●陷阱：对方场上1张表侧表示卡回到卡组最上面。");
                Set(card.Data, "Name", "时间潜行者·表盘修复师"); Set(card.Data, "Race", 1048576); Set(card, "Race", 1048576);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)294); return card;
            case 14558128:
                card = Card(0, 1800, (CardType)4129, location, id: id, level: 3, text: "这个卡名的效果1回合只能使用1次。\r\n①：包含以下其中任意种效果的魔法·陷阱·怪兽的效果发动时，把这张卡从手卡丢弃才能发动。那个效果无效。\r\n●从卡组把卡加入手卡的效果\r\n●从卡组把怪兽特殊召唤的效果\r\n●从卡组把卡送去墓地的效果");
                Set(card.Data, "Name", "灰流丽"); Set(card.Data, "Race", 16); Set(card, "Race", 16);
                Set(card.Data, "Attribute", 4); Set(card, "Attribute", 4); Set(card.Data, "Alias", 14558127);
                Set(card.Data, "Setcode", (long)0); return card;
            case 65741787:
                card = Card(800, 0, (CardType)67108897, location, id: id, level: 2, text: "连接怪兽以外的怪兽2只\r\n这个卡名的①的效果1回合只能使用1次。\r\n①：对方主要阶段才能发动。用包含这张卡的自己场上的怪兽为素材进行连接召唤。\r\n②：这张卡为连接素材的连接怪兽不会被对方的效果破坏。");
                Set(card.Data, "Name", "I：P百变莱娜"); Set(card.Data, "Race", 16777216); Set(card, "Race", 16777216);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 65741786);
                Set(card.Data, "Setcode", (long)0); return card;
            case 39552864:
                card = Card(500, 500, (CardType)17, location, id: id, level: 2, text: "被神圣光辉包住的天使灵魂。看见其美丽姿态者，据说能够实现愿望。");
                Set(card.Data, "Name", "神圣球体"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            case 64734921:
                card = Card(1600, 0, (CardType)33, location, id: id, level: 3, text: "①：支付500基本分才能发动。从手卡·卡组把1只「神圣球体」特殊召唤。");
                Set(card.Data, "Name", "创造之代行者 维纳斯"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)68); return card;
            case 91188343:
                card = Card(1000, 800, (CardType)4129, location, id: id, level: 2, text: "①：这张卡召唤成功时才能发动。从卡组把「神秘之代行者 厄斯」以外的1只「代行者」怪兽加入手卡。场上有「天空的圣域」存在的场合，可以把加入手卡的怪兽改成1只「主宰者·许珀里翁」。");
                Set(card.Data, "Name", "神秘之代行者 厄斯"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)68); return card;
            case 38529357:
                card = Card(0, 600, (CardType)33, location, id: id, level: 1, text: "这个卡名的①②的效果1回合各能使用1次。\r\n①：把这张卡从手卡丢弃才能发动。从自己的手卡·墓地选「命之代行者 尼普顿」以外的1只「代行者」怪兽特殊召唤。场上或者墓地有「天空的圣域」存在的场合，可以把特殊召唤的怪兽改成1只「许珀里翁」怪兽。直到对方回合结束时，双方不能把这个效果特殊召唤的怪兽解放。\r\n②：这张卡被除外的场合才能发动。从卡组把1张「天空的圣域」加入手卡。");
                Set(card.Data, "Name", "命之代行者 尼普顿"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 16); Set(card, "Attribute", 16); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)68); return card;
            case 3544583:
                card = Card(0, 2100, (CardType)4161, location, id: id, level: 2, text: "通常怪兽×2");
                Set(card.Data, "Name", "无之毕竟"); Set(card.Data, "Race", 4); Set(card, "Race", 4);
                Set(card.Data, "Attribute", 32); Set(card, "Attribute", 32); Set(card.Data, "Alias", 0);
                Set(card.Data, "Setcode", (long)0); return card;
            default: throw new System.Exception("Missing core card snapshot: " + id);
        }
    }
}
