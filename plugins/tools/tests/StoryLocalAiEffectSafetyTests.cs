using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static void EffectSafetyRegressions()
    {
        var failures = new List<string>(); int before = checks;
        Action<string, Action> test = (name, run) =>
        {
            try { run(); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); }
        };
        foreach (var pair in new[] { new[] { 63767246, 0 }, new[] { 24696097, 1 }, new[] { 63180841, 0 },
            new[] { 44508094, 0 }, new[] { 26973555, 1 }, new[] { 40939228, 1 } })
        {
            int id = pair[0], index = pair[1];
            foreach (int player in new[] { 0, 1 })
            {
                test("chain allegiance " + id + "/" + player, () =>
                {
                    var f = new Fixture(); f.Duel.Player = 1;
                    var response = InteractionCard(id); f.Bot.MonsterZone[0] = response;
                    if (id == 26973555) response.Overlays.Add(100001);
                    if (id == 63180841) f.Bot.Graveyard.Add(Card(100, type: Monster | CardType.Tuner, location: CardLocation.Grave));
                    bool monsterOnly = id == 26973555 || id == 63180841;
                    var source = Card(type: monsterOnly ? Monster : CardType.Spell,
                        location: monsterOnly ? CardLocation.MonsterZone : CardLocation.SpellZone, controller: player);
                    f.Chain(source, player); f.Duel.LastSummonedCards.Add(response);
                    Check(f.AI.OnSelectEffectYn(response, id * 16 + index) == (player == 1), "negate only the opposing chain");
                });
            }
            test("already negated " + id, () =>
            {
                var f = new Fixture(); f.Duel.Player = 1; var response = InteractionCard(id);
                f.Chain(Card(controller: 1), 1); f.Duel.NegatedChainIndexList.Add(1);
                Check(!f.AI.OnSelectEffectYn(response, id * 16 + index), "do not spend a second interruption on a negated link");
            });
        }

        foreach (int id in new[] { 63180841, 84013237 })
            foreach (int side in new[] { 0, 1 })
                foreach (bool winning in new[] { false, true })
                    test("attack " + id + "/" + side + "/" + winning, () =>
                    {
                        var f = new Fixture(); f.Duel.Phase = DuelPhase.BattleStep; f.Duel.Player = side;
                        var response = InteractionCard(id); f.Bot.MonsterZone[0] = response;
                        var own = Card(winning ? 3000 : 1000); var enemy = Card(2000, controller: 1);
                        f.Bot.BattlingMonster = own; f.Enemy.BattlingMonster = enemy;
                        f.Bot.UnderAttack = side == 1; f.Enemy.UnderAttack = side == 0;
                        Check(f.AI.OnSelectEffectYn(response, id * 16 + (id == 63180841 ? 1 : 0)) == !winning,
                            "stop only a battle that costs us a monster/damage");
                    });
        test("no active attack", () =>
        {
            var f = new Fixture(); var response = InteractionCard(63180841);
            Check(!f.AI.OnSelectEffectYn(response, response.Id * 16 + 1), "battle phase alone does not imply an attack to stop");
        });
        test("winning direct attack", () =>
        {
            var f = new Fixture(); f.Duel.Phase = DuelPhase.BattleStep;
            var utopia = InteractionCard(84013237); f.Bot.MonsterZone[0] = utopia;
            f.Bot.BattlingMonster = utopia; f.Enemy.UnderAttack = true;
            Check(!f.AI.OnSelectEffectYn(utopia, utopia.Id * 16), "do not negate our direct attack");
        });
        foreach (int id in new[] { 84013237, 63180841 })
            foreach (bool direct in new[] { false, true })
                test("generic trigger prompt preserves our attack " + id + "/" + direct, () =>
                {
                    var f = new Fixture(); f.Duel.Phase = DuelPhase.BattleStep;
                    var source = InteractionCard(id); source.Overlays.Add(100001);
                    f.Bot.MonsterZone[0] = source;
                    f.Bot.BattlingMonster = Card(3000); f.Enemy.UnderAttack = true;
                    if (!direct) f.Enemy.BattlingMonster = Card(1500, controller: 1);
                    Check(!f.AI.OnSelectEffectYn(source, -1),
                        "the core's unspecified trigger prompt must not bypass attack-negation safety");
                });
        foreach (int id in new[] { 84013237, 63180841 })
            foreach (int side in new[] { 0, 1 })
                test("generic attack trigger retains meaningful protection " + id + "/" + side, () =>
                {
                    var f = new Fixture(); f.Duel.Phase = DuelPhase.BattleStep; f.Duel.Player = side;
                    var source = InteractionCard(id); source.Overlays.Add(100001);
                    f.Bot.MonsterZone[0] = source;
                    f.Bot.BattlingMonster = Card(1000); f.Enemy.BattlingMonster = Card(3000, controller: 1);
                    f.Bot.UnderAttack = side == 1; f.Enemy.UnderAttack = side == 0;
                    Check(f.AI.OnSelectEffectYn(source, -1),
                        "resolve the generic trigger while preventing a visible battle loss");
                });
        test("generic attack trigger requires a live attack", () =>
        {
            var f = new Fixture(); var source = InteractionCard(84013237);
            f.Bot.MonsterZone[0] = source; f.Duel.Phase = DuelPhase.BattleStep;
            f.Bot.BattlingMonster = Card(1000); f.Enemy.BattlingMonster = Card(3000, controller: 1);
            Check(!f.AI.OnSelectEffectYn(source, -1), "stale battling monsters alone do not justify activation");
        });
        test("alternate artwork shares the verified optional attack trigger", () =>
        {
            var f = new Fixture(); f.Duel.Phase = DuelPhase.BattleStep;
            var source = Card(2500, 2000, Monster | CardType.Xyz, id: 84013238);
            Set(source.Data, "Alias", 84013237); f.Bot.MonsterZone[0] = source;
            f.Bot.BattlingMonster = source; f.Enemy.UnderAttack = true;
            Check(!f.AI.OnSelectEffectYn(source, -1), "artwork aliases cannot bypass attack safety");
        });
        var attackProfiles = (IDictionary)typeof(TestStoryExecutor).BaseType.Assembly
            .GetType("MDPro3.Plugins.Features.StoryMode.StoryAiScriptEffects")
            .GetField("attackTriggers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (DictionaryEntry entry in attackProfiles)
            test("generated generic attack trigger " + entry.Key, () =>
            {
                var profile = ((int Description, int Origin))entry.Value;
                var location = (CardLocation)profile.Origin;
                var f = new Fixture(); f.Duel.Phase = DuelPhase.BattleStep;
                var source = Card(id: (int)entry.Key, type: location == CardLocation.MonsterZone ? Monster : CardType.Trap,
                    location: location);
                if (location == CardLocation.MonsterZone) f.Bot.MonsterZone[0] = source;
                else f.Bot.SpellZone[0] = source;
                f.Bot.BattlingMonster = Card(3000); f.Enemy.UnderAttack = true;
                Check(!f.AI.OnSelectEffectYn(source, -1),
                    "every generated optional attack trigger protects a profitable own attack");
            });
        test("damage prevention preserves attack negation", () =>
        {
            var f = new Fixture(); ResolveResource(f, 33782437, 0); f.Duel.Player = 1; f.Duel.Phase = DuelPhase.BattleStep;
            var utopia = InteractionCard(84013237); f.Bot.MonsterZone[0] = utopia;
            f.Enemy.BattlingMonster = Card(2000, controller: 1); f.Bot.UnderAttack = true;
            Check(!f.AI.OnSelectEffectYn(utopia, utopia.Id * 16), "a zero-damage direct attack does not justify spending a material");
            f.Duel.Turn++; f.Executor.OnNewTurn();
            Check(!f.AI.OnSelectEffectYn(utopia, utopia.Id * 16), "Peace still protects through the following turn");
            f.Duel.Turn++; f.Executor.OnNewTurn();
            Check(f.AI.OnSelectEffectYn(utopia, utopia.Id * 16), "protection expires after the following turn");
        });
        test("Dark Ruler protection direction", () =>
        {
            var f = new Fixture(); ResolveResource(f, 54693926, 0); f.Duel.Phase = DuelPhase.BattleStep;
            var utopia = InteractionCard(84013237); f.Bot.MonsterZone[0] = utopia;
            f.Enemy.BattlingMonster = Card(2000, controller: 1); f.Bot.UnderAttack = true;
            Check(f.AI.OnSelectEffectYn(utopia, utopia.Id * 16), "our Dark Ruler does not protect our own LP");
            f = new Fixture(); ResolveResource(f, 54693926, 1); f.Duel.Phase = DuelPhase.BattleStep;
            f.Bot.MonsterZone[0] = utopia; f.Enemy.BattlingMonster = Card(2000, controller: 1); f.Bot.UnderAttack = true;
            Check(!f.AI.OnSelectEffectYn(utopia, utopia.Id * 16), "the opponent's Dark Ruler does protect our LP");
        });
        test("Peace does not sacrifice lethal for a draw", () =>
        {
            var f = new Fixture(); f.Duel.MainPhase.CanBattlePhase = true; f.Enemy.LifePoints = 2000;
            f.Bot.MonsterZone[0] = Card(3000);
            var peace = Card(id: 33782437, type: CardType.Spell, location: CardLocation.Hand);
            Check(!f.AI.OnSelectEffectYn(peace, peace.Id * 16), "do not turn a visible direct lethal into zero damage for one draw");
        });

        test("S:P self banish", () =>
        {
            var f = new Fixture(); f.Duel.Player = 1; var sp = InteractionCard(29301450);
            var own = Real(84815190); f.Bot.MonsterZone[0] = sp; f.Bot.MonsterZone[1] = own;
            f.Chain(Card(type: CardType.Spell, controller: 1, location: CardLocation.SpellZone), 1);
            Check(!f.AI.OnSelectEffectYn(sp, sp.Id * 16 + 1), "S:P does not hide two own bosses for no benefit");
        });
        test("S:P preserve lethal", () =>
        {
            var f = new Fixture(); var sp = InteractionCard(29301450); f.Bot.MonsterZone[0] = sp;
            f.Duel.MainPhase.CanBattlePhase = true; f.Enemy.LifePoints = 1000;
            var grave = Card(3000, location: CardLocation.Grave, controller: 1); f.Enemy.Graveyard.Add(grave);
            Check(!f.AI.OnSelectEffectYn(sp, sp.Id * 16), "graveyard banish must not lock out a visible direct lethal");
        });
        test("S:P real removal", () =>
        {
            var f = new Fixture(); var sp = InteractionCard(29301450); f.Bot.MonsterZone[0] = sp;
            var own = Real(84815190); f.Bot.MonsterZone[1] = own;
            var grave = Card(3000, location: CardLocation.Grave, controller: 1); f.Enemy.Graveyard.Add(grave);
            Check(f.AI.OnSelectEffectYn(sp, sp.Id * 16), "S:P may remove an opposing grave resource");
            Check(f.AI.OnSelectCard(new[] { own, grave, sp }, 1, 1, HintMsg.Remove, false).Single() == grave,
                "grave removal has the same domain at activation and selection");
        });
        test("S:P paired dodge", () =>
        {
            var f = new Fixture(); f.Duel.Player = 1; var sp = InteractionCard(29301450);
            var threatened = Real(84815190); var expendable = Card(100);
            var enemy = Card(3000, controller: 1); f.Enemy.MonsterZone[0] = enemy;
            f.Bot.MonsterZone[0] = sp; f.Bot.MonsterZone[1] = threatened; f.Bot.MonsterZone[2] = expendable;
            f.Chain(Card(id: 10045474, type: CardType.Trap, location: CardLocation.SpellZone, controller: 1), 1);
            f.Duel.LastChainTargets.Add(threatened);
            Check(f.AI.OnSelectEffectYn(sp, sp.Id * 16 + 1), "S:P can dodge an opposing target while removing a threat");
            var first = f.AI.OnSelectCard(new[] { expendable, sp, threatened }, 1, 1, HintMsg.Remove, false).Single();
            Check(first == threatened, "save the threatened boss rather than greedily choose the cheapest body");
            var second = f.AI.OnSelectCard(new[] { expendable, sp, enemy }, 1, 1, HintMsg.Remove, false).Single();
            Check(second == enemy, "the second target completes the planned exchange");
        });
        test("Omega recycling allegiance", () =>
        {
            var f = new Fixture(); f.Duel.Player = 1; f.Duel.Phase = DuelPhase.Standby;
            var omega = InteractionCard(74586817); f.Bot.MonsterZone[0] = omega;
            var enemy = Card(3000, location: CardLocation.Removed, controller: 1, text: "If this card is in your GY: Special Summon it.");
            f.Enemy.Banished.Add(enemy);
            Check(!f.AI.OnSelectEffectYn(omega, omega.Id * 16 + 1), "do not give an opponent their grave resource back");
            var own = Card(500, location: CardLocation.Removed, text: "If this card is in your GY: Special Summon it.");
            f.Bot.Banished.Add(own);
            Check(f.AI.OnSelectEffectYn(omega, omega.Id * 16 + 1), "recover our own reusable grave resource");
            Check(f.AI.OnSelectCard(new[] { enemy, own }, 1, 1, omega.Id * 16 + 2, false).Single() == own,
                "custom Lua selection hint does not reverse recovery allegiance");
        });
        test("S:P saves two own monsters from a board wipe", () =>
        {
            var f = new Fixture(); f.Duel.Player = 1; var sp = InteractionCard(29301450); var boss = Real(84815190);
            f.Bot.MonsterZone[0] = sp; f.Bot.MonsterZone[1] = boss;
            f.Chain(Card(id: 12580477, type: CardType.Spell, location: CardLocation.SpellZone, controller: 1), 1);
            Check(f.AI.OnSelectEffectYn(sp, sp.Id * 16 + 1), "a non-targeting board wipe justifies hiding two friendly bodies");
            var first = f.AI.OnSelectCard(new[] { sp, boss }, 1, 1, HintMsg.Remove, false).Single();
            var second = f.AI.OnSelectCard(new[] { sp, boss }.Where(c => c != first).ToArray(), 1, 1, HintMsg.Remove, false).Single();
            Check(first != second, "the consecutive prompts consume the same planned pair without duplication");
        });
        test("S:P does not dodge an already ineffective wipe", () =>
        {
            var f = new Fixture(); f.Duel.Player = 1; var sp = InteractionCard(29301450);
            f.Bot.MonsterZone[0] = sp; f.Bot.MonsterZone[1] = Real(84815190);
            var raigeki = Card(id: 12580477, type: CardType.Spell, location: CardLocation.SpellZone, controller: 1);
            f.Chain(raigeki, 1); f.Duel.NegatedChainIndexList.Add(1);
            Check(!f.AI.OnSelectEffectYn(sp, sp.Id * 16 + 1), "a negated board wipe does not justify hiding two own monsters");
        });

        foreach (string operation in new[] { "destroy it", "banish it", "send it to the GY", "negate its effects" })
            test("own-only field " + operation, () =>
            {
                var f = new Fixture(); var source = Card(text: "Target 1 monster you control; " + operation + ".");
                f.Bot.MonsterZone[0] = source; f.Enemy.MonsterZone[0] = Card(3000, controller: 1);
                Check(!f.AI.OnSelectEffectYn(source, source.Id * 16), "an enemy board cannot justify an own-only harmful effect");
            });
        test("legitimate self cost", () =>
        {
            var f = new Fixture(); var stardust = InteractionCard(44508094);
            f.Bot.MonsterZone[0] = stardust; f.Chain(Card(type: CardType.Spell, controller: 1, location: CardLocation.SpellZone), 1);
            Check(f.AI.OnSelectEffectYn(stardust, stardust.Id * 16), "self tribute is a useful negate cost, not harmful removal");
            Check(f.AI.OnSelectCard(new[] { stardust }, 1, 1, HintMsg.Release, false).Single() == stardust, "pay the mandatory cost");
        });
        test("own revival disable", () =>
        {
            var f = new Fixture(); var source = Card(text: "Target 1 monster in your GY; Special Summon it, but negate its effects.");
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16), "a revived body's negated effects are part of a beneficial summon");
        });
        foreach (var sourceType in new[] { CardType.Spell, CardType.Trap, Monster })
            test("mixed target negation has an opposing target " + sourceType, () =>
            {
                var f = new Fixture();
                var source = Card(type: sourceType, location: sourceType == Monster ? CardLocation.MonsterZone : CardLocation.Hand,
                    text: "Target 1 face-up monster on the field; that target gains 400 ATK, but its effects are negated until the end of this turn.");
                var own = Card(2400);
                f.Bot.MonsterZone[0] = own;
                Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "a mixed stat/negation effect does not select our own monster when no enemy target exists");
                var enemy = Card(2800, controller: 1,
                    text: "If this card is Normal Summoned: add 1 card from your Deck to your hand.");
                f.Enemy.MonsterZone[0] = enemy;
                Check(f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "the same generic effect remains available against a live opposing monster");
                Check(f.AI.OnSelectCard(new[] { own, enemy }, 1, 1, HintMsg.Target, false).Single() == enemy,
                    "activation and resolution use the same opposing target policy");
            });
        test("localized target negation has an opposing target", () =>
        {
            var f = new Fixture();
            var source = Card(type: CardType.Spell, location: CardLocation.Hand,
                text: "以场上1只表侧表示怪兽为对象；那只怪兽的效果无效化。");
            f.Bot.MonsterZone[0] = Card(2400);
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                "localized target negation does not harm our only monster");
            var enemy = Card(2800, controller: 1,
                text: "If this card is Normal Summoned: add 1 card from your Deck to your hand."); f.Enemy.MonsterZone[0] = enemy;
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16),
                "localized target negation can answer an opposing monster");
            Check(f.AI.OnSelectCard(new[] { f.Bot.MonsterZone[0], enemy }, 1, 1, HintMsg.Target, false).Single() == enemy,
                "localized target negation selects the opposing monster");
        });
        test("script-identified field negation never targets itself", () =>
        {
            var f = new Fixture();
            var source = Card(id: 23204029, type: Monster,
                text: "这个卡名在规则上也当作「元素英雄」卡使用。\n①：只要这张卡在怪兽区域存在，这张卡的属性也当作「光」使用。\n②：自己·对方回合1次，以场上1张表侧表示卡为对象才能发动。那张卡的效果直到回合结束时无效。");
            f.Bot.MonsterZone[0] = source;
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                "a target-effect negation with no opposing payoff does not target its source");
            var enemy = Card(2800, controller: 1,
                text: "If this card is Normal Summoned: add 1 card from your Deck to your hand.");
            f.Enemy.MonsterZone[0] = enemy;
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16),
                "a script-identified field negation remains available against an opposing effect monster");
            Check(f.AI.OnSelectCard(new[] { source, enemy }, 1, 1, HintMsg.Disable, false).Single() == enemy,
                "a field-negation target is always selected from the opposing payoff pool");
        });
        foreach (var sourceType in new[] { Monster, CardType.Spell, CardType.Trap })
            test("normal monster flavour text cannot justify negation " + sourceType, () =>
            {
                var f = new Fixture();
                var source = Card(id: sourceType == Monster ? 23204029 : 0, type: sourceType,
                    text: "Target 1 face-up card on the field; negate its effects.");
                if (sourceType == Monster) f.Bot.MonsterZone[0] = source;
                else { source.Location = CardLocation.SpellZone; f.Bot.SpellZone[0] = source; }
                f.Bot.MonsterZone[1] = Card(1800, text: "Once per turn: add 1 card from your Deck to your hand.");
                f.Enemy.MonsterZone[0] = Card(2000, type: CardType.Monster | CardType.Normal, controller: 1,
                    text: "经遗传因子操作而强化的狼人。原本的和蔼之心已被完全破坏。其力量只为战斗而存在，那个破坏力是无可估量的。");
                f.Duel.LastSummonedCards.Add(source);
                Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "flavour text mentioning destruction is not a negatable effect");
                Check(f.Respond(source) == -1,
                    "the post-summon quick-effect prompt must not activate using a fictitious enemy target");
            });
        foreach (var pair in new[]
        {
            new { Id = 93004029, Hint = HintMsg.Disable, Text = "①：这张卡得到500攻击力。\n②：以场上1张表侧表示卡为对象才能发动。那张卡的效果直到回合结束时无效。" },
            new { Id = 93004030, Hint = HintMsg.Remove, Text = "①：这张卡不能被战斗破坏。\n②：以场上1张表侧表示卡为对象才能发动。那张卡除外。" },
        })
            test("unambiguous multi-effect field interaction avoids self target " + pair.Hint, () =>
            {
                var f = new Fixture();
                var source = Card(id: pair.Id, type: Monster, text: pair.Text);
                f.Bot.MonsterZone[0] = source;
                Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "a multi-effect field interaction does not spend its only target on our own source");
                var enemy = Card(2800, controller: 1,
                    text: "If this card is Normal Summoned: add 1 card from your Deck to your hand.");
                f.Enemy.MonsterZone[0] = enemy;
                Check(f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "an unambiguous multi-effect interaction remains usable against an opposing target");
                Check(f.AI.OnSelectCard(new[] { source, enemy }, 1, 1, pair.Hint, false).Single() == enemy,
                    "a multi-effect interaction selects the opposing target at resolution");
            });
        foreach (var result in new[] { "那些卡除外。", "那些卡的效果无效。" })
            test("multi-effect interaction preserves mandatory target count " + result, () =>
            {
                var f = new Fixture();
                var source = Card(text: "①：这张卡不能被战斗破坏。\n②：以场上2张表侧表示卡为对象才能发动。" + result);
                f.Bot.MonsterZone[0] = source;
                f.Enemy.MonsterZone[0] = Card(2800, controller: 1,
                    text: "Once per turn: add 1 card from your Deck to your hand.");
                Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "one enemy cannot justify filling a second compulsory target with a friendly card");
                f.Enemy.MonsterZone[1] = Card(2500, controller: 1,
                    text: "Once per turn: add 1 card from your Deck to your hand.");
                Check(f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "two useful opposing targets satisfy the required count");
                var selected = f.AI.OnSelectCard(new[] { source, f.Enemy.MonsterZone[0], f.Enemy.MonsterZone[1] }, 2, 2, HintMsg.Target, false);
                Check(selected.Count == 2 && selected.All(c => c.Controller == 1),
                    "both mandatory targets remain opposing through selection");
            });
        test("text field interaction retains its discard cost", () =>
        {
            var f = new Fixture();
            var source = Card(text: "Discard 1 card, then target 1 face-up card on the field; banish it.");
            f.Bot.MonsterZone[0] = source;
            f.Enemy.MonsterZone[0] = Card(2800, controller: 1);
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                "recognising a field interaction cannot bypass its discard requirement");
        });
        test("field interaction does not borrow another activated paragraph", () =>
        {
            var f = new Fixture();
            var source = Card(text: "①：可以发动。自己抽1张。\n②：以场上1张表侧表示卡为对象才能发动。那张卡除外。");
            f.Bot.MonsterZone[0] = source;
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16),
                "the first drawing effect must not inherit the second effect's harmful target requirement");
        });
        test("mixed destruction refuses an own-only board", () =>
        {
            var f = new Fixture();
            var source = Card(type: Monster, text: "If this card is destroyed: Special Summon 1 monster from your GY.\n(Quick Effect): You can destroy both 1 card you control and 1 card on the field.");
            f.Bot.MonsterZone[0] = source;
            f.Bot.MonsterZone[1] = Card(1800);
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16 + 1),
                "a mandatory mixed destruction does not fire when the second target would be ours");
        });
        test("localized multi-effect mixed destruction uses its active description", () =>
        {
            var f = new Fixture();
            var source = Card(id: 60461804, type: Monster,
                text: "①：对方场上的怪兽的攻击力下降。\n②：自己·对方回合可以发动。自己场上1张卡和场上1张卡破坏。\n③：这张卡被战斗·效果破坏的场合才能发动。下个回合的准备阶段，从自己墓地把1只怪兽特殊召唤。");
            f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = Card(1800);
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                "a multi-effect description cannot hide an own-board destruction behind offset zero");
        });
        test("mixed destruction plans one useful own target and one enemy target", () =>
        {
            var f = new Fixture();
            var source = Card(type: Monster, text: "If this card is destroyed: Special Summon 1 monster from your GY.\n(Quick Effect): You can destroy both 1 card you control and 1 card on the field.");
            var own = Card(1800);
            var enemy = Card(2600, controller: 1, text: "If this card is Normal Summoned: add 1 card from your Deck to your hand.");
            f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = own; f.Enemy.MonsterZone[0] = enemy;
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1),
                "a mixed destruction can convert a concrete destruction trigger while removing an opposing threat");
            var first = f.AI.OnSelectCard(new[] { own, source, enemy }, 1, 1, HintMsg.Destroy, false).Single();
            Check(first == source, "the useful friendly destruction target is selected first");
            var second = f.AI.OnSelectCard(new[] { own, enemy }, 1, 1, HintMsg.Destroy, false).Single();
            Check(second == enemy, "the second mandatory target is always opposing");
        });
        test("mixed destruction keeps its pair during chain resolution", () =>
        {
            var f = new Fixture();
            var source = Card(type: Monster, text: "If this card is destroyed: Special Summon 1 monster from your GY.\n(Quick Effect): You can destroy both 1 card you control and 1 card on the field.");
            var own = Card(1800);
            var enemy = Card(2600, controller: 1, text: "If this card is Normal Summoned: add 1 card from your Deck to your hand.");
            f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = own; f.Enemy.MonsterZone[0] = enemy;
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "mixed destruction activates before resolution");
            f.Duel.CurrentChain.Add(source);
            f.Duel.CurrentChainInfo.Add(new ChainInfo(source, 0, source.Id * 16 + 1));
            f.Duel.SolvingChainIndex = 1;
            Check(f.AI.OnSelectCard(new[] { own, source, enemy }, 1, 1, HintMsg.Destroy, false).Single() == source,
                "resolving prompt keeps the planned friendly trigger target");
            Check(f.AI.OnSelectCard(new[] { own, enemy }, 1, 1, HintMsg.Destroy, false).Single() == enemy,
                "resolving second prompt keeps the opposing target");
        });
        const string renewableDestruction = "6星以上的「英雄」怪兽＋「命运英雄」怪兽\n这个卡名的②③的效果1回合各能使用1次。\n①：对方场上的怪兽的攻击力下降自己墓地的「英雄」卡数量×200。\n②：自己·对方回合可以发动。自己场上1张卡和场上1张卡破坏。\n③：这张卡被战斗·效果破坏的场合才能发动。下个回合的准备阶段，从自己墓地把1只「命运英雄」怪兽特殊召唤。";
        foreach (bool untargetable in new[] { false, true })
            test("renewable mixed destruction trades against an enemy " + untargetable, () =>
            {
                var f = new Fixture(); f.Duel.Player = 1;
                var source = Card(2500, 2100, Monster | CardType.Fusion, id: 60461804, text: renewableDestruction, level: 8);
                Set(source, "ProcCompleted", 8); f.Bot.MonsterZone[0] = source;
                var valuable = Card(3500, text: "Once per turn (Quick Effect): negate the activation.");
                f.Bot.MonsterZone[1] = valuable;
                var enemy = Card(2600, controller: 1, text: untargetable ? "Cannot be targeted by card effects." : "");
                f.Enemy.MonsterZone[0] = enemy; f.Chain(enemy, 1);
                Check(f.Respond(source) == 0, "a useful destruction exchange can sacrifice a recoverable boss");
                f.Duel.CurrentChain.Add(source); f.Duel.CurrentChainInfo.Add(new ChainInfo(source, 0, source.Id * 16));
                f.Duel.SolvingChainIndex = 2;
                Check(f.AI.OnSelectCard(new[] { valuable, source }, 1, 1, HintMsg.Destroy, false).Single() == source,
                    "use the recoverable body instead of another live boss");
                Check(f.AI.OnSelectCard(new[] { valuable, enemy }, 1, 1, HintMsg.Destroy, false).Single() == enemy,
                    "non-targeting destruction selects the opposing half of the trade");
            });
        test("destroyed recovery is separate from field destruction", () =>
        {
            var f = new Fixture();
            var source = Card(2500, 2100, Monster | CardType.Fusion, location: CardLocation.Grave,
                id: 60461804, text: renewableDestruction, level: 8);
            Set(source, "ProcCompleted", 8); f.Bot.Graveyard.Add(source);
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1),
                "the destroyed trigger must schedule revival even when both fields are empty");
            Check(f.AI.OnSelectCard(new[] { source }, 1, 1, HintMsg.SpSummon, false).Single() == source,
                "the delayed revival keeps its summon selection policy");
        });
        test("renewable destruction still refuses an empty enemy field", () =>
        {
            var f = new Fixture();
            var source = Card(2500, 2100, Monster | CardType.Fusion, id: 60461804, text: renewableDestruction, level: 8);
            Set(source, "ProcCompleted", 8); f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = Card(3500);
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16), "recovery alone does not justify destroying two friendly cards");
        });
        test("mixed destruction does not trade a premium body for a token", () =>
        {
            var f = new Fixture();
            var source = Card(3500, text: "You can destroy both 1 card you control and 1 card on the field.");
            f.Bot.MonsterZone[0] = source;
            f.Enemy.MonsterZone[0] = Card(0, 0, CardType.Monster | CardType.Token, controller: 1);
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16), "a very small removal payoff cannot pay for a valuable friendly body");
        });
        test("mixed destruction survives an intervening friendly chain link", () =>
        {
            var f = new Fixture();
            var source = Card(2500, 2100, Monster | CardType.Fusion, id: 60461804, text: renewableDestruction, level: 8);
            Set(source, "ProcCompleted", 8); f.Bot.MonsterZone[0] = source;
            var own = Card(1800); var enemy = Card(3000, controller: 1);
            f.Bot.MonsterZone[1] = own; f.Enemy.MonsterZone[0] = enemy;
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16), "initial destruction exchange is useful");
            f.Chain(source, 0);
            var other = Card(location: CardLocation.Grave, text: "Draw 1 card.");
            f.Bot.Graveyard.Add(other);
            Check(f.AI.OnSelectEffectYn(other, other.Id * 16), "a second own effect commits another selection context");
            f.Chain(other, 0); f.Duel.SolvingChainIndex = 1;
            Check(f.AI.OnSelectCard(new[] { own, source }, 1, 1, HintMsg.Destroy, false).Single() == source,
                "the first chain link keeps its recoverable friendly choice");
            Check(f.AI.OnSelectCard(new[] { own, enemy }, 1, 1, HintMsg.Destroy, false).Single() == enemy,
                "the original pair survives both selection prompts");
        });
        foreach (int code in new[] { 60461804, 93561804 })
            test("mixed trade policy is independent of printed card identity " + code, () =>
            {
                var f = new Fixture(); f.Duel.Phase = DuelPhase.End;
                var source = Card(4500, 2100, Monster | CardType.Fusion, id: code, text: renewableDestruction, level: 8);
                Set(source, "ProcCompleted", 8); f.Bot.MonsterZone[0] = source;
                f.Enemy.MonsterZone[0] = Card(3000, controller: 1);
                Check(f.Respond(source) == 0, "the same renewable exchange works for a changed card identity");
            });
        foreach (bool graveRedirect in new[] { false, true })
            test("unavailable recovery is not valued as a free body " + graveRedirect, () =>
            {
                var f = new Fixture();
                var source = Card(2500, 2100, Monster | CardType.Fusion, id: 60461804, text: renewableDestruction, level: 8);
                if (graveRedirect) { Set(source, "ProcCompleted", 8); ResolveResource(f, 91800273, 1); }
                f.Bot.MonsterZone[0] = source;
                f.Enemy.MonsterZone[0] = Card(2000, controller: 1);
                Check(!f.AI.OnSelectEffectYn(source, source.Id * 16),
                    "an unrevivable or grave-replaced boss cannot trade on an invented recovery discount");
            });
        test("forced harmful effect", () =>
        {
            var f = new Fixture(); var source = InteractionCard(74860293); f.Bot.MonsterZone[0] = source;
            Check(f.AI.OnSelectChain(new[] { source }, new[] { source.Id * 16 }, new[] { true }) == 0,
                "safety cannot submit an illegal pass to a mandatory core prompt");
            Check(f.AI.OnSelectCard(new[] { source }, 1, 1, HintMsg.Destroy, false).Count == 1,
                "mandatory targets still satisfy the core's lower bound");
        });
        test("two mandatory targets", () =>
        {
            var f = new Fixture(); var source = Card(text: "Target 2 cards on the field; destroy them.");
            f.Bot.MonsterZone[0] = source; f.Enemy.MonsterZone[0] = Card(2500, controller: 1);
            Check(!f.AI.OnSelectEffectYn(source, source.Id * 16), "one enemy must not force the second target to be our own card");
            f.Enemy.MonsterZone[1] = Card(2300, controller: 1);
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16), "two real opposing targets satisfy the required count");
        });
        test("up to two targets", () =>
        {
            var f = new Fixture(); var source = Card(text: "Target up to 2 cards on the field; destroy them.");
            f.Bot.MonsterZone[0] = source; var enemy = Card(2500, controller: 1); f.Enemy.MonsterZone[0] = enemy;
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16), "up to two still permits removing a single enemy");
            Check(f.AI.OnSelectCard(new[] { source, enemy }, 1, 2, HintMsg.Destroy, false).Single() == enemy,
                "optional count never spends a friendly extra target");
        });
        test("script cost survives metadata", () =>
        {
            var f = new Fixture(); var abc = Card(3000, id: 1561110);
            f.Bot.MonsterZone[0] = abc; f.Bot.Hand.Add(Card(id: 33396948, location: CardLocation.Hand));
            f.Enemy.MonsterZone[0] = Card(100, controller: 1);
            Check(!f.AI.OnSelectEffectYn(abc, abc.Id * 16), "a Lua-recognised banish still prices its discard cost");
        });
        test("copied negate identity", () =>
        {
            var f = new Fixture(); var copier = Card(id: 63101468); f.Bot.MonsterZone[0] = copier;
            f.Chain(Card(controller: 0), 0); f.Duel.LastSummonedCards.Add(copier);
            Check(!f.AI.OnSelectEffectYn(copier, 63767246 * 16), "copied effect uses original script identity and cannot negate our own chain");
        });
        test("own targeting protection", () =>
        {
            var f = new Fixture(); var source = Card();
            var own = Card(text: "Your opponent cannot target this card with card effects.");
            Check((bool)Evaluate(f, "CanTarget", own, source), "opponent-only protection permits our own rescue");
            var immune = Card(text: "Cannot be targeted by card effects.");
            Check(!(bool)Evaluate(f, "CanTarget", immune, source), "unconditional targeting protection remains respected");
        });
        test("resolving chain owns selection", () =>
        {
            var f = new Fixture(); var omega = InteractionCard(74586817); var other = Card();
            var own = Card(location: CardLocation.Removed, text: "If this card is in your GY: Special Summon it.");
            var enemy = Card(4000, location: CardLocation.Removed, controller: 1);
            f.Bot.Banished.Add(own); f.Enemy.Banished.Add(enemy);
            f.Duel.CurrentChain.Add(omega); f.Duel.CurrentChainInfo.Add(new ChainInfo(omega, 0, omega.Id * 16 + 1));
            f.Duel.SolvingChainIndex = 1; f.Executor.SetCard(ExecutorType.Activate, other, other.Id * 16);
            Check(f.AI.OnSelectCard(new[] { enemy, own }, 1, 1, omega.Id * 16 + 2, false).Single() == own,
                "resolution uses its own link rather than the most recently considered card");
        });
        foreach (int renewable in new[] { 26973555, 63767246, 24696097, 63180841 })
            test("renewable response " + renewable, () =>
            {
                var f = new Fixture(); f.Duel.Player = 1; var b = Real(84815190); var other = InteractionCard(renewable);
                f.Bot.MonsterZone[0] = b; f.Bot.MonsterZone[1] = other;
                if (renewable == 26973555) other.Overlays.Add(100001);
                if (renewable == 63180841) f.Bot.Graveyard.Add(Card(100, type: Monster | CardType.Tuner, location: CardLocation.Grave));
                bool monsterOnly = renewable == 26973555 || renewable == 63180841;
                f.Chain(Card(type: monsterOnly ? Monster : CardType.Spell, controller: 1,
                    location: monsterOnly ? CardLocation.MonsterZone : CardLocation.SpellZone), 1);
                int offset = renewable == 26973555 || renewable == 24696097 ? 1 : 0;
                Check(RespondEffects(f, new[] { b, other }, 1, offset) == 1, "preserve Baronne's once-per-face-up omni negate");
            });

        var random = new Random(20260928);
        for (int i = 0; i < 100; i++)
        {
            int sample = i;
            test("public target permutation " + sample, () =>
            {
                var f = new Fixture(); var source = InteractionCard(74860293); f.Bot.MonsterZone[0] = source;
                var own = Real(84815190); f.Bot.MonsterZone[1] = own;
                // Vary private cached data without changing the visible state.
                f.Enemy.Hand.Add(Card(random.Next(100, 9000), location: CardLocation.Hand, controller: 1));
                var enemy = Card(2600, controller: 1); f.Enemy.MonsterZone[0] = enemy;
                Check(f.AI.OnSelectEffectYn(source, source.Id * 16), "removal remains available");
                var candidates = new[] { source, own, enemy }.OrderBy(_ => random.Next()).ToArray();
                var selected = f.AI.OnSelectCard(candidates, 1, 3, HintMsg.Destroy, false);
                Check(selected.Count == 1 && selected[0] == enemy, "up-to removal never pads its count with own cards");
            });
        }
        // Check every generated identity through real GameAI dispatch, not by
        // duplicating the implementation's flags in a separate fake selector.
        // The frozen baseline lacks this optional table, so its comparable
        // behavioural cases above remain runnable against the old assembly.
        var metadata = typeof(MDPro3.Plugins.Features.StoryMode.StoryLuckyExecutor).Assembly
            .GetType("MDPro3.Plugins.Features.StoryMode.StoryAiScriptEffects");
        var profiles = metadata?.GetField("profiles", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as IDictionary;
        if (profiles != null)
            foreach (DictionaryEntry entry in profiles)
            {
                int description = (int)entry.Key;
                string kind = entry.Value.GetType().GetField("Purpose", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(entry.Value).ToString();
                if (kind != "Negate" && kind != "StopAttack") continue;
                test("generated identity " + description, () =>
                {
                    var f = new Fixture(); var source = Card(3000, id: description / 16); f.Bot.MonsterZone[0] = source;
                    f.Duel.LastSummonedCards.Add(source);
                    if (kind == "Negate") f.Chain(Card(), 0);
                    else
                    {
                        f.Duel.Phase = DuelPhase.BattleStep; f.Bot.BattlingMonster = source; f.Enemy.UnderAttack = true;
                    }
                    Check(!f.AI.OnSelectEffectYn(source, description), "exact script identity rejects an unprofitable own interruption");
                });
            }
        Console.WriteLine("Effect safety: " + (checks - before) + " passing checks, " + failures.Count + " failed scenarios");
        foreach (string failure in failures) Console.WriteLine("EFFECT-SAFETY-FAIL " + failure);
        if (failures.Count != 0) throw new Exception("Effect safety failures: " + failures.Count);
    }
}
