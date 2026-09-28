using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MDPro3.Plugins.Features.StoryMode;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static void SummonPlanningRegressions()
    {
        foreach (bool chinese in new[] { false, true })
        {
            var f = new Fixture();
            var beater = Card(2200, location: CardLocation.Hand);
            var tuner = Card(100, type: Monster | CardType.Tuner, level: 1, location: CardLocation.Hand);
            var extender = Card(500, level: 7, location: CardLocation.Hand, text: chinese
                ? "①：自己场上有调整存在的场合，这张卡可以从手卡特殊召唤。"
                : "If you control a Tuner monster, you can Special Summon this card (from your hand).");
            var boss = Card(3000, type: Monster | CardType.Synchro, level: 8, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation.");
            f.Bot.Hand.Add(beater); f.Bot.Hand.Add(tuner); f.Bot.Hand.Add(extender);
            f.Bot.ExtraDeck.Add(boss);
            f.Duel.MainPhase.SummonableCards.Add(beater); f.Duel.MainPhase.SummonableCards.Add(tuner);
            tuner.ActionIndex[(int)MainPhaseAction.MainAction.Summon] = 1;
            beater.ActionIndex[(int)MainPhaseAction.MainAction.Summon] = 0;
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.Summon && action.Index == 1,
                "normal summon sees the conditional hand extender and complete Synchro route (Chinese=" + chinese + ")");
            Check(f.Bot.Hand.Count == 3 && f.Bot.GetMonsterCount() == 0 && f.Bot.ExtraDeck.Count == 1,
                "hand continuation search does not mutate live resources");
            f.Bot.Hand.Remove(tuner); tuner.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[0] = tuner;
            f.Duel.MainPhase = new MainPhase();
            extender.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 2;
            f.Duel.MainPhase.SpecialSummonableCards.Add(extender);
            action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == 2,
                "conditional extender is actually selected after the normal summon resolves");
            f.Bot.Hand.Remove(extender); extender.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[1] = extender;
            f.Duel.MainPhase = new MainPhase(); f.Duel.MainPhase.SpecialSummonableCards.Add(boss);
            boss.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 0;
            action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon, "hand route reaches its actual terminal summon");
            ApplyDevelopmentSummon(f, action);
            Check(f.Bot.GetMonsters().Single() == boss && f.Bot.Hand.Single() == beater,
                "three real action steps preserve the unneeded hand resource and reach the predicted end board");
        }
        RecruitPlanningRegressions();
        HandPlanningSafety();
        SummonCacheAndTriggerSafety();
        SummonOrderingAndRestrictions();
        ProtectedExtensionSequence();
        HandPlanningOracle();
        Console.WriteLine("Local AI: hand-aware summon planning regressions passed");
    }

    private static object Evaluation(Fixture f) => typeof(StoryLuckyExecutor)
        .GetField("evaluation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.Executor);

    private static object MainSummonPlan(Fixture f)
    {
        var eval = Evaluation(f);
        return eval.GetType().GetMethod("PlanMainSummons", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(eval, new object[] { f.Duel.MainPhase.SummonableCards, f.Duel.MainPhase.SpecialSummonableCards });
    }

    private static ClientCard PlannedCard(object plan) => plan == null ? null : (ClientCard)plan.GetType()
        .GetField("Card", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(plan);

    private static void RecruitPlanningRegressions()
    {
        foreach (string source in new[] { "hand", "Deck", "GY" })
        foreach (bool chinese in new[] { false, true })
        {
            var f = new Fixture();
            string zhSource = source == "hand" ? "手卡" : source == "Deck" ? "卡组" : "墓地";
            var starter = Card(200, type: Monster | CardType.Tuner, level: 3, location: CardLocation.Hand, text: chinese
                ? "①：这张卡召唤成功时才能发动。从" + zhSource + "把1只2星以下的怪兽守备表示特殊召唤。这个效果特殊召唤的怪兽的效果无效化。"
                : "If this card is Normal Summoned: You can Special Summon 1 Level 2 or lower monster from your " + source + " in Defense Position. Its effects are negated.");
            var material = Card(100, level: 2, location: source == "hand" ? CardLocation.Hand : source == "Deck" ? CardLocation.Deck : CardLocation.Grave);
            f.Bot.Hand.Add(starter); f.Duel.MainPhase.SummonableCards.Add(starter);
            if (source == "hand") f.Bot.Hand.Add(material);
            else if (source == "Deck") f.Bot.Deck.Add(material);
            else f.Bot.Graveyard.Add(material);
            f.Bot.ExtraDeck.Add(Card(2600, type: Monster | CardType.Synchro, level: 5, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
            Check(PlannedCard(MainSummonPlan(f)) == starter, "models an actual Normal Summon recruitment from " + source + " Chinese=" + chinese);
            Check(f.Bot.GetMonsterCount() == 0 && material.Location != CardLocation.MonsterZone, "recruitment leaves live cards untouched");
            if (source == "Deck") f.Bot.Deck.Remove(material);
            else if (source == "hand") f.Bot.Hand.Remove(material);
            else f.Bot.Graveyard.Remove(material);
            Check(MainSummonPlan(f) == null, "recruitment cannot fabricate a missing " + source + " monster");
        }
        var junk = new Fixture();
        var synchron = Card(1300, type: Monster | CardType.Tuner, level: 3, location: CardLocation.Hand,
            text: "①：这张卡召唤时，以自己墓地1只2星以下的怪兽为对象才能发动。那只怪兽守备表示特殊召唤。这个效果特殊召唤的怪兽的效果无效化。");
        junk.Bot.Hand.Add(synchron); junk.Duel.MainPhase.SummonableCards.Add(synchron);
        junk.Bot.Graveyard.Add(Card(300, level: 2, location: CardLocation.Grave));
        junk.Bot.ExtraDeck.Add(Card(2600, type: Monster | CardType.Synchro, level: 5, location: CardLocation.Extra,
            text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
        Check(PlannedCard(MainSummonPlan(junk)) == synchron, "recognizes the installed Junk Synchron targeted revival wording");
    }

    private static void HandPlanningSafety()
    {
        foreach (string text in new[] {
            "If you control a Tuner monster, you can Special Summon this card (from your hand) by discarding 1 card.",
            "If you control a Tuner monster, you can Special Summon this card (from your hand). You cannot Special Summon from the Extra Deck this turn.",
            "If your opponent activates a monster effect: Special Summon this card (from your hand).",
            "If you control a Dragon monster, you can Special Summon this card (from your hand).",
            "If you control a Level 4 monster, you can Special Summon this card (from your hand).",
            "If you control no monsters, you can Special Summon this card (from your hand)."
        })
        {
            var f = new Fixture();
            var tuner = Card(100, type: Monster | CardType.Tuner, level: 1, location: CardLocation.Hand);
            f.Bot.Hand.Add(tuner); f.Duel.MainPhase.SummonableCards.Add(tuner);
            f.Bot.Hand.Add(Card(100, level: 7, location: CardLocation.Hand, text: text));
            f.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 8, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
            Check(MainSummonPlan(f) == null, "unmet conditions, unknown costs and summon locks do not create a hand route: " + text);
        }
        var normal = new Fixture();
        var one = Card(100, type: Monster | CardType.Tuner, level: 1, location: CardLocation.Hand);
        var seven = Card(100, level: 3, location: CardLocation.Hand);
        foreach (var card in new[] { one, seven }) { normal.Bot.Hand.Add(card); normal.Duel.MainPhase.SummonableCards.Add(card); }
        normal.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 4, location: CardLocation.Extra,
            text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
        Check(MainSummonPlan(normal) == null, "one normal summon allowance cannot place two hand monsters");

        var fusion = new Fixture(); var small = Card(100); fusion.Bot.MonsterZone[0] = small;
        var hand = Card(100, location: CardLocation.Hand); fusion.Bot.Hand.Add(hand); fusion.Duel.MainPhase.SummonableCards.Add(hand);
        fusion.Bot.ExtraDeck.Add(Card(6000, type: Monster | CardType.Fusion, location: CardLocation.Extra,
            text: "\"Fixture " + small.Id + "\" + \"Fixture " + hand.Id + "\"\nWhen your opponent activates a card or effect: negate the activation."));
        Check(MainSummonPlan(fusion) == null, "a Fusion recipe does not supply the missing fusion spell or contact procedure");

        var once = new Fixture();
        once.Bot.MonsterZone[0] = Card(100, type: Monster | CardType.Tuner, level: 1);
        var repeat = Card(200, level: 7, location: CardLocation.Hand, text:
            "If you control a Tuner monster, you can Special Summon this card (from your hand).\nYou can only Special Summon this card once per turn.");
        once.Bot.Hand.Add(repeat);
        var arrival = Card(100, level: 1, location: CardLocation.Hand); once.Bot.Hand.Add(arrival); once.Duel.MainPhase.SummonableCards.Add(arrival);
        once.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 9, location: CardLocation.Extra,
            text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
        Check(PlannedCard(MainSummonPlan(once)) == arrival, "recognizes a once-per-turn hand extender before it is spent");
        var spent = Card(200, id: repeat.Id); spent.LastLocation = CardLocation.Hand;
        once.Duel.LastSummonedCards.Add(spent); once.Executor.OnSpSummoned();
        Check(MainSummonPlan(once) == null, "a consumed name-based hand summon cannot be reused by a second copy");
        once.Executor.OnNewTurn();
        Check(PlannedCard(MainSummonPlan(once)) == arrival, "new turn restores the modelled hand procedure");
    }

    private static void SummonOrderingAndRestrictions()
    {
        // Going second: keep the tuner face-up despite its low ATK and high DEF.
        var f = new Fixture();
        var tuner = Card(100, 2000, Monster | CardType.Tuner, CardLocation.Hand, level: 1);
        var body = Card(500, level: 7, location: CardLocation.Hand,
            text: "If you control a Tuner monster, you can Special Summon this card (from your hand).");
        f.Bot.Hand.Add(tuner); f.Bot.Hand.Add(body);
        f.Duel.MainPhase.SummonableCards.Add(tuner); f.Duel.MainPhase.MonsterSetableCards.Add(tuner);
        f.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 8, location: CardLocation.Extra,
            text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
        f.Enemy.MonsterZone[0] = Card(4000, controller: 1);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Summon,
            "a planned low-ATK tuner is summoned face-up instead of set under an opposing boss");

        // Special summon the empty-field extender before spending the normal summon.
        f = new Fixture();
        tuner = Card(100, type: Monster | CardType.Tuner, level: 3, location: CardLocation.Hand);
        var dragon = Card(2100, level: 5, location: CardLocation.Hand,
            text: "①：只有对方场上才有怪兽存在的场合，这张卡可以从手卡特殊召唤。");
        f.Bot.Hand.Add(tuner); f.Bot.Hand.Add(dragon); f.Enemy.MonsterZone[0] = Card(1500, controller: 1);
        f.Duel.MainPhase.SummonableCards.Add(tuner); f.Duel.MainPhase.SpecialSummonableCards.Add(dragon);
        f.Bot.ExtraDeck.Add(Real(27548199, CardLocation.Extra)); f.Bot.Graveyard.Add(Real(90290572, CardLocation.Grave));
        dragon.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 8;
        var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == 8,
            "empty-field hand summon precedes the normal summon that would turn it off");
        f.Bot.Hand.Remove(dragon); dragon.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[0] = dragon;
        f.Duel.MainPhase = new MainPhase(); f.Duel.MainPhase.SummonableCards.Add(tuner);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Summon,
            "core refresh continues with the preserved normal summon");

        foreach (string restriction in new[] {
            "Cannot be used as Synchro Material.", "这张卡不能作为同调素材。",
            "Cannot be used as Synchro Material, except for the Synchro Summon of a Dragon monster.",
            "把这张卡作为同调素材的场合，不是「救世」怪兽的同调召唤不能使用。"
        })
        {
            f = new Fixture();
            tuner = Card(100, type: Monster | CardType.Tuner, level: 1, text: restriction);
            body = Card(100, level: 7, location: CardLocation.Hand);
            f.Bot.MonsterZone[0] = tuner; f.Bot.Hand.Add(body);
            f.Duel.MainPhase.SpecialSummonableCards.Add(body);
            f.Bot.ExtraDeck.Add(Real(84815190, CardLocation.Extra)); // does not match total level
            f.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 8, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
            Check(MainSummonPlan(f) == null, "material's own restriction rejects a fictitious future Synchro: " + restriction);
        }
        f = new Fixture(); tuner = Card(100, type: Monster | CardType.Tuner, level: 1,
            text: "Cannot be used as Synchro Material, except for the Synchro Summon of a Dragon monster.");
        f.Bot.MonsterZone[0] = tuner;
        body = Card(100, level: 7, location: CardLocation.Hand); f.Bot.Hand.Add(body); f.Duel.MainPhase.SpecialSummonableCards.Add(body);
        f.Bot.ExtraDeck.Add(Real(27548199, CardLocation.Extra)); f.Bot.Graveyard.Add(Real(90290572, CardLocation.Grave));
        Check(PlannedCard(MainSummonPlan(f)) == body, "an explicit permitted Dragon Synchro material exception remains playable");

        // A special-only recruitment must not be credited after a normal summon.
        f = new Fixture();
        var starter = Card(100, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Hand,
            text: "If this card is Special Summoned: You can Special Summon 1 Level 4 monster from your Deck.");
        f.Bot.Hand.Add(starter); f.Duel.MainPhase.SummonableCards.Add(starter); f.Bot.Deck.Add(Card(100, level: 4, location: CardLocation.Deck));
        f.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 6, location: CardLocation.Extra,
            text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
        Check(MainSummonPlan(f) == null, "normal summon does not activate a special-summon-only recruitment");
        f.Duel.MainPhase.SpecialSummonableCards.Add(starter);
        Check(PlannedCard(MainSummonPlan(f)) == starter, "the core-offered special summon unlocks that same real recruitment");
    }

    private static void SummonCacheAndTriggerSafety()
    {
        var f = new Fixture();
        f.Bot.MonsterZone[0] = Card(100, level: 8);
        var tuner = Card(100, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Hand);
        f.Bot.Hand.Add(tuner); f.Duel.MainPhase.SummonableCards.Add(tuner); f.Bot.ExtraDeck.Add(Real(84815190, CardLocation.Extra));
        Check(PlannedCard(MainSummonPlan(f)) == tuner, "cached plan begins with a profitable normal-to-Baronne route");
        var eval = Evaluation(f);
        var tax = eval.GetType().GetMethod("NoteResolvedResourceEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 4; i++) tax.Invoke(eval, new object[] { 23434538, 1 });
        Check(MainSummonPlan(f) == null, "resolved draw pressure invalidates the unified plan even when all cards stay in place");
        f.Executor.OnNewTurn();
        Check(PlannedCard(MainSummonPlan(f)) == tuner, "turn reset invalidates the cached stopping decision");

        foreach (string effect in new[] {
            "If this card is Normal Summoned: You can Special Summon 1 Level 2 monster from your Deck. You cannot Special Summon from the Extra Deck this turn.",
            "If this card is Normal Summoned: You can discard 1 card; Special Summon 1 Level 2 monster from your Deck.",
            "①：这张卡召唤时才能发动。从卡组把1只2星怪兽特殊召唤。②：自己的全部怪兽的等级变成1星。",
            "自己不能从额外卡组特殊召唤。①：这张卡召唤时才能发动。从卡组把1只2星怪兽特殊召唤。"
        })
        {
            f = new Fixture();
            tuner = Card(100, type: Monster | CardType.Tuner, level: 3, location: CardLocation.Hand, text: effect);
            f.Bot.Hand.Add(tuner); f.Duel.MainPhase.SummonableCards.Add(tuner);
            f.Bot.Deck.Add(Card(100, level: 2, location: CardLocation.Deck));
            f.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 5, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
            Check(MainSummonPlan(f) == null, "unmodelled trigger costs, restrictions and multiple effect indices do not create a body");
        }
        // A revived monster whose effects are negated cannot recruit another tuner.
        foreach (bool disabled in new[] { false, true })
        {
            f = new Fixture();
            var starter = Card(100, level: 4, location: CardLocation.Hand, text:
                "If this card is Normal Summoned: You can Special Summon 1 Level 3 monster from your Deck." + (disabled ? " Its effects are negated." : ""));
            var relay = Card(100, level: 3, location: CardLocation.Deck,
                text: "If this card is Special Summoned: You can Special Summon 1 Level 1 Tuner monster from your Deck.");
            tuner = Card(100, level: 1, type: Monster | CardType.Tuner, location: CardLocation.Deck);
            f.Bot.Hand.Add(starter); f.Duel.MainPhase.SummonableCards.Add(starter); f.Bot.Deck.Add(relay); f.Bot.Deck.Add(tuner);
            f.Bot.ExtraDeck.Add(Real(84815190, CardLocation.Extra));
            f.Bot.ExtraDeck.Add(Card(3000, type: Monster | CardType.Synchro, level: 8, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation."));
            Check((MainSummonPlan(f) != null) == !disabled, "recruit chains respect the summoned monster's actual effect availability " + disabled);
        }
    }

    private static void HandPlanningOracle()
    {
        var random = new Random(270927); int reached = 0; var watch = Stopwatch.StartNew();
        for (int sample = 0; sample < 80; sample++)
        {
            var f = new Fixture();
            int tunerLevel = random.Next(1, 5), bodyLevel = random.Next(4, 9);
            var tuner = Card(random.Next(0, 400), type: Monster | CardType.Tuner, level: tunerLevel, location: CardLocation.Hand);
            var beater = Card(random.Next(1800, 2400), level: 4, location: CardLocation.Hand);
            var body = Card(random.Next(0, 600), level: bodyLevel, location: CardLocation.Hand,
                text: "If you control a Tuner monster, you can Special Summon this card (from your hand).");
            var boss = Card(3000, type: Monster | CardType.Synchro, level: tunerLevel + bodyLevel, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation.");
            foreach (var c in new[] { tuner, beater, body }.OrderBy(_ => random.Next())) f.Bot.Hand.Add(c);
            foreach (var c in new[] { tuner, beater }.OrderBy(_ => random.Next())) f.Duel.MainPhase.SummonableCards.Add(c);
            f.Bot.ExtraDeck.Add(boss);
            // Opposing hidden information changes without changing public opportunities.
            f.Enemy.Hand.Add(Card(random.Next(100, 9000), controller: 1, location: CardLocation.Hand));
            int oracle = PartitionTerminals(new[] { tuner, body }, new[] { boss.Level });
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(oracle == 1 && action.Action == MainPhaseAction.MainAction.Summon && action.Index == tuner.ActionIndex[(int)MainPhaseAction.MainAction.Summon],
                "independent hand-to-Synchro oracle selects a reachable first action " + sample);
            f.Bot.Hand.Remove(tuner); tuner.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[0] = tuner;
            f.Bot.Hand.Remove(body); body.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[1] = body;
            f.Duel.MainPhase = new MainPhase(); boss.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 0;
            f.Duel.MainPhase.SpecialSummonableCards.Add(boss);
            action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon, "hand oracle route still works after the live board changes " + sample);
            ApplyDevelopmentSummon(f, action);
            if (f.Bot.GetMonsters().Contains(boss)) reached++;
        }
        Check(reached == 80 && watch.ElapsedMilliseconds < 10000, "all bounded hand oracle scenarios reach their terminal");
        Console.WriteLine("Hand development oracle: " + reached + "/80 terminal boards, shuffled choices and hidden hands, " + watch.ElapsedMilliseconds + " ms");
    }

    private static void ProtectedExtensionSequence()
    {
        var f = new Fixture();
        f.Bot.MonsterZone[0] = Card(100, type: Monster | CardType.Tuner, level: 2);
        f.Bot.MonsterZone[1] = Card(100, level: 6);
        var tuner = Card(100, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Hand);
        var extender = Card(100, level: 8, location: CardLocation.Hand,
            text: "If you control a monster, you can Special Summon this card (from your hand).");
        f.Bot.Hand.Add(tuner); f.Bot.Hand.Add(extender);
        f.Bot.Graveyard.Add(Real(90290572, CardLocation.Grave));
        var savage = Real(27548199, CardLocation.Extra); var baronne = Real(84815190, CardLocation.Extra);
        f.Bot.ExtraDeck.Add(savage); f.Bot.ExtraDeck.Add(baronne);
        bool summonedNormally = false; int steps = 0;
        for (; steps < 5; steps++)
        {
            f.Duel.MainPhase = new MainPhase();
            if (!summonedNormally && f.Bot.Hand.Contains(tuner)) f.Duel.MainPhase.SummonableCards.Add(tuner);
            if (f.Bot.Hand.Contains(extender) && f.Bot.GetMonsterCount() > 0)
            {
                f.Duel.MainPhase.ActivableCards.Add(extender); f.Duel.MainPhase.ActivableDescs.Add(extender.Id * 16);
                extender.ActionActivateIndex[extender.Id * 16] = 17;
            }
            foreach (var extra in f.Bot.ExtraDeck.Where(c => SynchroSubsets(f.Bot.GetMonsters(), c.Level).Any()))
            {
                extra.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = f.Duel.MainPhase.SpecialSummonableCards.Count;
                f.Duel.MainPhase.SpecialSummonableCards.Add(extra);
            }
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            if (steps == 0)
                Check(action.Action == MainPhaseAction.MainAction.SpSummon,
                    "equivalent two-terminal lines establish a negate before activating the exposed hand extender");
            if (action.Action == MainPhaseAction.MainAction.SpSummon) ApplyDevelopmentSummon(f, action);
            else if (action.Action == MainPhaseAction.MainAction.Summon || action.Action == MainPhaseAction.MainAction.Activate)
            {
                bool normal = action.Action == MainPhaseAction.MainAction.Summon;
                var body = normal ? tuner : extender;
                Check(f.Bot.Hand.Contains(body) && (!normal || !summonedNormally), "planned hand action consumes a real, unspent resource");
                if (!normal) Check(action.Index == 17, "activation retains the core-provided action index");
                summonedNormally |= normal;
                f.Bot.Hand.Remove(body); body.LastLocation = body.Location; body.Location = CardLocation.MonsterZone;
                f.Bot.MonsterZone[Enumerable.Range(0, 5).First(i => f.Bot.MonsterZone[i] == null)] = body;
                if (!normal) { f.Duel.LastSummonedCards.Clear(); f.Duel.LastSummonedCards.Add(body); f.Executor.OnSpSummoned(); }
            }
            else break;
            if (f.Bot.GetMonsters().Contains(savage) && f.Bot.GetMonsters().Contains(baronne)) break;
        }
        Check(steps == 3 && f.Bot.GetMonsters().Count == 2 && f.Bot.GetMonsters().Contains(savage) && f.Bot.GetMonsters().Contains(baronne),
            "actual extra summon, normal summon and activated hand extender reach two distinct terminals in four actions");
    }
}
