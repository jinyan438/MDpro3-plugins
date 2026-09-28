using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using MDPro3.Plugins.Features.StoryMode;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static void DevelopmentRegressions()
    {
        var f = new Fixture();
        var tuner = Card(100, type: Monster | CardType.Tuner, level: 2);
        var eight = Card(100, level: 8);
        var a = Card(700); var b = Card(700);
        f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = eight;
        f.Bot.MonsterZone[2] = a; f.Bot.MonsterZone[3] = b;
        var ip = Real(65741786, CardLocation.Extra); var baronne = Real(84815190, CardLocation.Extra);
        f.Bot.ExtraDeck.Add(ip); f.Bot.ExtraDeck.Add(baronne);
        Check(f.Rule(ip, ExecutorType.SpSummon), "development accepts I:P plus a subsequent Baronne route");
        var materials = f.AI.OnSelectCard(new[] { tuner, eight, a, b }, 2, 2, HintMsg.LinkMaterial, false);
        Check(materials.Contains(a) && materials.Contains(b), "Link materials preserve the sole Tuner and matching level 8 for the second terminal");
        Check(tuner.Location == CardLocation.MonsterZone && ip.Location == CardLocation.Extra && f.Bot.ExtraDeck.Count == 2,
            "lookahead never mutates the live board or extra deck");

        f = new Fixture(); var moon = Real(90290572); moon.LastLocation = CardLocation.Extra; f.Bot.MonsterZone[0] = moon;
        a = Card(600); b = Card(600); f.Bot.MonsterZone[1] = a; f.Bot.MonsterZone[2] = b;
        var bridge = Card(500, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra, text: "2 Effect Monsters");
        var avramax = Real(21887175, CardLocation.Extra);
        f.Bot.ExtraDeck.Add(bridge); f.Bot.ExtraDeck.Add(avramax);
        Check(f.Rule(bridge, ExecutorType.SpSummon), "a locally losing Link-2 bridge is accepted when it demonstrably opens a stronger Avramax end board");
        materials = f.AI.OnSelectCard(new[] { moon, a, b }, 2, 2, HintMsg.LinkMaterial, false);
        Check(materials.Contains(a) && materials.Contains(b), "bridge route retains the other extra-deck summoned Link-2");

        f = new Fixture();
        f.Bot.MonsterZone[0] = Card(100, type: Monster | CardType.Tuner, level: 2);
        f.Bot.MonsterZone[1] = Card(100, level: 8);
        f.Bot.MonsterZone[2] = Card(100, type: Monster | CardType.Tuner, level: 2);
        f.Bot.MonsterZone[3] = Card(100, level: 6);
        var apo = Real(4280258, CardLocation.Extra); baronne = Real(84815190, CardLocation.Extra);
        var savage = Real(27548199, CardLocation.Extra);
        f.Bot.Graveyard.Add(Real(90290572, CardLocation.Grave));
        foreach (var c in new[] { apo, baronne, savage })
        {
            c.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = c == apo ? 0 : c == baronne ? 1 : 2;
            f.Bot.ExtraDeck.Add(c); f.Duel.MainPhase.SpecialSummonableCards.Add(c);
        }
        var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index != 0,
            "two independent Synchro terminals beat spending all four bodies on a single Apollousa");
        ApplyDevelopmentSummon(f, action);
        f.Duel.MainPhase = new MainPhase();
        var remaining = f.Bot.ExtraDeck.Single(c => c.Id == (action.Index == 1 ? 27548199 : 84815190));
        remaining.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 0;
        f.Duel.MainPhase.SpecialSummonableCards.Add(remaining);
        var followup = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(followup.Action == MainPhaseAction.MainAction.SpSummon, "development route continues after the first real board update");
        ApplyDevelopmentSummon(f, followup);
        Check(f.Bot.GetMonsters().Select(c => c.Id).OrderBy(i => i).SequenceEqual(new[] { 27548199, 84815190 }),
            "executing both steps reaches the predicted Savage plus Baronne board");

        f = new Fixture(); f.Bot.MonsterZone[0] = Card(900, level: 8);
        tuner = Card(0, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Hand);
        var beatstick = Card(2000, location: CardLocation.Hand);
        baronne = Real(84815190, CardLocation.Extra); f.Bot.ExtraDeck.Add(baronne);
        foreach (var c in new[] { beatstick, tuner }) { f.Bot.Hand.Add(c); f.Duel.MainPhase.SummonableCards.Add(c); }
        Check(!f.Rule(beatstick, ExecutorType.SummonOrSet) && f.Rule(tuner, ExecutorType.SummonOrSet),
            "normal summon chooses the Tuner that completes a terminal instead of a higher standalone body");
        var wrongTuner = Card(2500, type: Monster | CardType.Tuner, level: 3, location: CardLocation.Deck);
        var rightTuner = Card(100, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Deck);
        Check(f.AI.OnSelectCard(new[] { wrongTuner, rightTuner }, 1, 1, HintMsg.SpSummon, false).Single() == rightTuner,
            "effect summon selection uses the offered Tuner that completes the best next board");
        f = new Fixture(); f.Bot.MonsterZone[0] = Card(900, level: 8); f.Bot.ExtraDeck.Add(Real(84815190, CardLocation.Extra));
        wrongTuner.Location = CardLocation.Hand; rightTuner.Location = CardLocation.Hand;
        foreach (var c in new[] { wrongTuner, rightTuner }) { f.Bot.Hand.Add(c); f.Duel.MainPhase.SpecialSummonableCards.Add(c); }
        wrongTuner.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 0;
        rightTuner.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 1;
        action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == 1,
            "direct hand special summons use the same continuation objective as effect summons and normals");
        DevelopmentResources();
        DevelopmentPartitions();
        SynchroDevelopmentRegressions();
        ExtraSummonCoverage();
        DevelopmentBudget();
        Console.WriteLine("Local AI: multi-step development regressions passed");
    }

    private static void ExtraSummonCoverage()
    {
        foreach (int level in new[] { 4, 6, 8, 10 })
        {
            var f = new Fixture();
            var tuner = Card(400, type: Monster | CardType.Tuner, level: 2);
            var body = Card(400, level: level - 2);
            f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = body;
            var target = Card(2800, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: level,
                text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation.");
            target.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = level;
            f.Bot.ExtraDeck.Add(target); f.Duel.MainPhase.SpecialSummonableCards.Add(target);
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == level,
                "core-offered Synchro of level " + level + " is taken with matching materials");
        }
        foreach (int rank in new[] { 3, 4, 5, 8 })
        {
            var f = new Fixture();
            f.Bot.MonsterZone[0] = Card(1800, level: rank);
            f.Bot.MonsterZone[1] = Card(1800, level: rank);
            var target = Card(2100, type: Monster | CardType.Xyz, location: CardLocation.Extra, level: rank,
                text: rank + "星怪兽×2\nOnce per turn: detach 1 material from this card; Special Summon 1 monster from your GY.");
            target.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = rank;
            f.Bot.ExtraDeck.Add(target); f.Duel.MainPhase.SpecialSummonableCards.Add(target);
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == rank,
                "core-offered rank " + rank + " Xyz retains material utility instead of ending");
        }
        foreach (var type in new[] { CardType.Link, CardType.Fusion })
        {
            var f = new Fixture();
            var a = Card(300); var b = Card(300);
            f.Bot.MonsterZone[0] = a; f.Bot.MonsterZone[1] = b;
            string recipe = type == CardType.Link ? "2 Effect Monsters" : "\"Fixture " + a.Id + "\" + \"Fixture " + b.Id + "\"";
            var target = Card(2600, type: Monster | type, location: CardLocation.Extra, level: type == CardType.Link ? 2 : 8,
                text: recipe + "\nIf this card is summoned: add 1 card from your Deck to your hand.");
            target.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 7;
            f.Bot.ExtraDeck.Add(target); f.Duel.MainPhase.SpecialSummonableCards.Add(target);
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == 7,
                "core-offered " + type + " uses a profitable generic material route");
            int hint = type == CardType.Link ? HintMsg.LinkMaterial : HintMsg.FusionMaterial;
            var materials = f.AI.OnSelectCard(new[] { b, a }, 2, 2, hint, false);
            Check(materials.Count == 2 && materials.Contains(a) && materials.Contains(b), type + " material plan is executed");
        }
        var unknown = new Fixture();
        unknown.Bot.MonsterZone[0] = Card(300); unknown.Bot.MonsterZone[1] = Card(300);
        var offered = Card(3000, type: Monster | CardType.Fusion, location: CardLocation.Extra,
            text: "Alternate procedure whose printed material wording is unavailable");
        offered.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 9;
        unknown.Bot.ExtraDeck.Add(offered); unknown.Duel.MainPhase.SpecialSummonableCards.Add(offered);
        var fallback = unknown.AI.OnSelectIdleCmd(unknown.Duel.MainPhase);
        Check(fallback.Action == MainPhaseAction.MainAction.SpSummon && fallback.Index == 9,
            "a core-offered high-value extra summon does not stall solely on unparsed wording");
        unknown.Bot.MonsterZone[2] = Card(3000, text: "When your opponent activates a card or effect: negate the activation.");
        Check(unknown.AI.OnSelectIdleCmd(unknown.Duel.MainPhase).Action == MainPhaseAction.MainAction.SpSummon,
            "unknown procedure can proceed using cheap bodies while preserving a live boss");
        var bossOnly = unknown.AI.OnSelectCard(new[] { unknown.Bot.MonsterZone[2] }, 1, 1, HintMsg.FusionMaterial, true);
        Check(bossOnly.Count == 0, "cancelable core prompt will not unexpectedly spend a live boss");
        var unknownFusion = new Fixture();
        unknownFusion.Bot.MonsterZone[0] = Card(300);
        unknownFusion.Bot.Hand.Add(Card(300, location: CardLocation.Hand));
        var unparsedFusion = Card(3400, type: Monster | CardType.Fusion, location: CardLocation.Extra,
            text: "Alternate fusion procedure\nIf this card is summoned: add 1 card from your Deck to your hand.");
        unparsedFusion.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 4;
        unknownFusion.Bot.ExtraDeck.Add(unparsedFusion); unknownFusion.Duel.MainPhase.SpecialSummonableCards.Add(unparsedFusion);
        var unknownAction = unknownFusion.AI.OnSelectIdleCmd(unknownFusion.Duel.MainPhase);
        Check(unknownAction.Action == MainPhaseAction.MainAction.SpSummon && unknownAction.Index == 4,
            "core-offered Fusion fallback can count an expendable hand material");
        var revisedMaterial = Card(100, location: CardLocation.Hand);
        Check(unknownFusion.AI.OnSelectCard(new[] { revisedMaterial }, 1, 1, HintMsg.FusionMaterial, true)
            .Single() == revisedMaterial, "unparsed fusion follows the core's actual legal material prompt");
        var rankUp = new Fixture();
        var xyzBody = Card(2100, type: Monster | CardType.Xyz, level: 5);
        rankUp.Bot.MonsterZone[0] = xyzBody;
        var nextRank = Card(2700, type: Monster | CardType.Xyz, location: CardLocation.Extra, level: 6,
            text: "6星怪兽×2\nWhen your opponent activates a card or effect: negate the activation.");
        nextRank.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 3;
        rankUp.Bot.ExtraDeck.Add(nextRank); rankUp.Duel.MainPhase.SpecialSummonableCards.Add(nextRank);
        var rankAction = rankUp.AI.OnSelectIdleCmd(rankUp.Duel.MainPhase);
        Check(rankAction.Action == MainPhaseAction.MainAction.SpSummon && rankAction.Index == 3,
            "a core-offered one-body Xyz upgrade is not blocked by rank mismatch");

        var directFusion = new Fixture();
        var fieldBody = Card(300); var handBody = Card(300, location: CardLocation.Hand);
        directFusion.Bot.MonsterZone[0] = fieldBody; directFusion.Bot.Hand.Add(handBody);
        var handFusion = Card(3300, type: Monster | CardType.Fusion, location: CardLocation.Extra,
            text: "\"Fixture " + fieldBody.Id + "\" + \"Fixture " + handBody.Id +
                "\"\nIf this card is summoned: add 1 card from your Deck to your hand.");
        handFusion.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 8;
        directFusion.Bot.ExtraDeck.Add(handFusion); directFusion.Duel.MainPhase.SpecialSummonableCards.Add(handFusion);
        var handAction = directFusion.AI.OnSelectIdleCmd(directFusion.Duel.MainPhase);
        Check(handAction.Action == MainPhaseAction.MainAction.SpSummon && handAction.Index == 8,
            "core-offered direct Fusion can use a field and a hand material with hand cost accounted");
        var handMaterials = directFusion.AI.OnSelectCard(new[] { handBody, fieldBody }, 2, 2, HintMsg.FusionMaterial, false);
        Check(handMaterials.Contains(handBody) && handMaterials.Contains(fieldBody),
            "direct Fusion follows its field-plus-hand plan");

        var fusion = new Fixture();
        var boss = Card(3000, text: "When your opponent activates a card or effect: negate the activation.");
        var field = Card(300); var hand = Card(300, location: CardLocation.Hand);
        fusion.Bot.MonsterZone[0] = boss; fusion.Bot.MonsterZone[1] = field; fusion.Bot.Hand.Add(hand);
        var costly = Card(3100, type: Monster | CardType.Fusion, location: CardLocation.Extra,
            text: "\"Fixture " + boss.Id + "\" + \"Fixture " + field.Id + "\"");
        var efficient = Card(2700, type: Monster | CardType.Fusion, location: CardLocation.Extra,
            text: "\"Fixture " + field.Id + "\" + \"Fixture " + hand.Id + "\"");
        var selected = fusion.AI.OnSelectCard(new[] { costly, efficient }, 1, 1, HintMsg.SpSummon, false);
        Check(selected.Single() == efficient, "fusion effect target selection preserves a live board boss");
        var fusionMaterials = fusion.AI.OnSelectCard(new[] { boss, hand, field }, 2, 2, HintMsg.FusionMaterial, false);
        Check(fusionMaterials.Contains(field) && fusionMaterials.Contains(hand) && !fusionMaterials.Contains(boss),
            "fusion effect pays the material combination chosen with its target");
    }

    private static void SynchroDevelopmentRegressions()
    {
        LevelSixSynchroRegressions();
        SynchroDrawResources();
        foreach (string recipe in new[] { "「同调士」调整＋调整以外的怪兽1只以上", "1 \"Synchron\" Tuner + 1+ non-Tuner monsters" })
        {
            var f = new Fixture(); f.Duel.Turn = 3; f.Duel.MainPhase.CanBattlePhase = true;
            var revolution = Card(900, 1400, Monster | CardType.Tuner, level: 3);
            var warrior = Card(900, 400, level: 2);
            var assault = Card(700, 0, Monster | CardType.Tuner, level: 2);
            Set(revolution.Data, "Name", recipe[0] == '1' ? "Revolution Synchron" : "革命同调士");
            Set(assault.Data, "Name", recipe[0] == '1' ? "Assault Synchron" : "强袭同调士");
            string trigger = recipe[0] == '1'
                ? "If this card is Synchro Summoned: You can Special Summon as many \"Synchron\" Tuners as possible with different Levels from your Deck, in Defense Position. You cannot Special Summon from the Extra Deck, except Synchro Monsters, the turn you activate this effect."
                : "①：这张卡同调召唤的场合才能发动（这个效果发动的回合，自己不是同调怪兽不能从额外卡组特殊召唤）。从卡组把「同调士」调整尽可能守备表示特殊召唤（相同等级最多1只）。";
            f.Bot.MonsterZone[0] = revolution; f.Bot.MonsterZone[1] = warrior; f.Bot.MonsterZone[2] = assault;
            var speeder = Card(1800, 1000, Monster | CardType.Synchro, CardLocation.Extra, text: recipe + "\n" + trigger, level: 5);
            speeder.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 6;
            f.Bot.ExtraDeck.Add(speeder); f.Duel.MainPhase.SpecialSummonableCards.Add(speeder);
            for (int level = 1; level <= 3; level++)
            {
                var tuner = Card(200, type: Monster | CardType.Tuner, location: CardLocation.Deck, level: level);
                Set(tuner.Data, "Name", recipe[0] == '1' ? "Deck Synchron " + level : "卡组同调士" + level);
                f.Bot.Deck.Add(tuner);
            }
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == 6,
                "matching named Tuners develop through a Synchro starter before entering battle: " + recipe);
            var materials = f.AI.OnSelectCard(new[] { assault, warrior, revolution }, 2, 3, HintMsg.SynchroMaterial, false);
            Check(materials.Count == 2 && materials.Contains(revolution) && materials.Contains(warrior) && !materials.Contains(assault),
                "level 5 starter uses level 3 Tuner plus level 2 non-Tuner, preserving the second Tuner");
            Check(f.Bot.Deck.Count == 33 && f.Bot.GetMonsterCount() == 3 && speeder.Location == CardLocation.Extra,
                "deck trigger planning does not mutate live cards or generate unknown deck resources");
        }
        SynchroMaterialGroups();
        SynchroRestrictionRegressions();
        SynchroTriggerResources();
    }

    private static void LevelSixSynchroRegressions()
    {
        var six = new Fixture();
        var levelThreeTuner = Card(1500, type: Monster | CardType.Tuner, level: 3);
        var levelThreeBody = Card(1500, level: 3);
        six.Bot.MonsterZone[0] = levelThreeTuner; six.Bot.MonsterZone[1] = levelThreeBody;
        var drawingSynchro = Card(2000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 6,
            text: "调整＋调整以外的怪兽1只以上\n这张卡同调召唤时才能发动。自己抽1张。");
        drawingSynchro.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 4;
        six.Bot.ExtraDeck.Add(drawingSynchro); six.Duel.MainPhase.SpecialSummonableCards.Add(drawingSynchro);
        Check(six.Rule(drawingSynchro, ExecutorType.SpSummon),
            "level 3 Tuner plus level 3 non-Tuner is a valid level 6 Synchro route");
        var sixAction = six.AI.OnSelectIdleCmd(six.Duel.MainPhase);
        Check(sixAction.Action == MainPhaseAction.MainAction.SpSummon && sixAction.Index == 4,
            "main phase takes a legal level 6 Synchro with an immediate draw rather than ending");
        var sixMaterials = six.AI.OnSelectCard(new[] { levelThreeTuner, levelThreeBody }, 2, 2, HintMsg.SynchroMaterial, false);
        Check(sixMaterials.Count == 2 && sixMaterials.Contains(levelThreeTuner) && sixMaterials.Contains(levelThreeBody),
            "level 6 Synchro executes the planned level 3 plus level 3 materials");

        var english = new Fixture();
        english.Bot.MonsterZone[0] = Card(2000, type: Monster | CardType.Tuner, level: 3);
        english.Bot.MonsterZone[1] = Card(2000, level: 3);
        var englishDraw = Card(2000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 6,
            text: "1 Tuner + 1+ non-Tuner monsters\nIf this card is Synchro Summoned: You can draw 1 card.");
        englishDraw.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 3;
        english.Bot.ExtraDeck.Add(englishDraw); english.Duel.MainPhase.SpecialSummonableCards.Add(englishDraw);
        var englishAction = english.AI.OnSelectIdleCmd(english.Duel.MainPhase);
        Check(englishAction.Action == MainPhaseAction.MainAction.SpSummon && englishAction.Index == 3,
            "immediate English Synchro draw justifies combining two established level 3 attackers");

        var weak = new Fixture();
        weak.Bot.MonsterZone[0] = Card(1500, type: Monster | CardType.Tuner, level: 3);
        weak.Bot.MonsterZone[1] = Card(1500, level: 3);
        var weakSix = Card(2000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 6,
            text: "调整＋调整以外的怪兽1只以上");
        weak.Bot.ExtraDeck.Add(weakSix); weak.Duel.MainPhase.SpecialSummonableCards.Add(weakSix);
        Check(!weak.Rule(weakSix, ExecutorType.SpSummon),
            "a weaker effectless Synchro does not consume two established bodies just because levels match");
        var breaker = new Fixture(); breaker.Duel.Turn = 3; breaker.Duel.MainPhase.CanBattlePhase = true;
        breaker.Bot.MonsterZone[0] = Card(1500, type: Monster | CardType.Tuner, level: 3);
        breaker.Bot.MonsterZone[1] = Card(1500, level: 3);
        breaker.Enemy.MonsterZone[0] = Card(2500, controller: 1);
        var battleSix = Card(2800, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 6,
            text: "调整＋调整以外的怪兽1只以上");
        battleSix.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 5;
        breaker.Bot.ExtraDeck.Add(battleSix); breaker.Duel.MainPhase.SpecialSummonableCards.Add(battleSix);
        var breakAction = breaker.AI.OnSelectIdleCmd(breaker.Duel.MainPhase);
        Check(breakAction.Action == MainPhaseAction.MainAction.SpSummon && breakAction.Index == 5,
            "a stronger level 6 Synchro converts two ineffective attackers into a battle breaker");
        breaker.Duel.MainPhase.CanBattlePhase = false;
        Check(!breaker.Rule(battleSix, ExecutorType.SpSummon), "battle-only payoff disappears when battle phase is unavailable");
        breaker.Duel.MainPhase.CanBattlePhase = true;
        breaker.Duel.Turn = 1;
        Check(!breaker.Rule(battleSix, ExecutorType.SpSummon), "turn one never predicts a battle payoff even with a stale battle flag");
        breaker.Duel.Turn = 3;
        Set(breaker.Enemy.MonsterZone[0], "Position", (int)CardPosition.FaceDownDefence);
        Check(!breaker.Rule(battleSix, ExecutorType.SpSummon), "unknown facedown monster stats never justify a Synchro battle payoff");
        Set(breaker.Enemy.MonsterZone[0], "Position", (int)CardPosition.FaceUpAttack);
        Set(breaker.Enemy.MonsterZone[0].Data, "Description", "Cannot be destroyed by battle.");
        Check(!breaker.Rule(battleSix, ExecutorType.SpSummon), "battle indestructibility prevents a speculative conversion payoff");
        var delayed = new Fixture();
        delayed.Bot.MonsterZone[0] = Card(1500, type: Monster | CardType.Tuner, level: 3);
        delayed.Bot.MonsterZone[1] = Card(1500, level: 3);
        var delayedDraw = Card(2000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 6,
            text: "调整＋调整以外的怪兽1只以上\n这张卡战斗破坏怪兽时才能发动。自己抽1张。");
        delayed.Bot.ExtraDeck.Add(delayedDraw); delayed.Duel.MainPhase.SpecialSummonableCards.Add(delayedDraw);
        Check(!delayed.Rule(delayedDraw, ExecutorType.SpSummon),
            "a conditional later draw is not valued as an immediate Synchro-summon draw");
        six.Bot.Deck.Clear();
        Set(levelThreeTuner, "Attack", 1000); Set(levelThreeBody, "Attack", 1000);
        Check(!six.Rule(drawingSynchro, ExecutorType.SpSummon),
            "a Synchro draw receives no payoff when no deck card remains");
        six.Bot.Deck.Add(Card(location: CardLocation.Deck));
        var resolvedDraw = Card(2000, type: Monster | CardType.Synchro, level: 6, id: drawingSynchro.Id,
            text: "调整＋调整以外的怪兽1只以上\n这张卡同调召唤时才能发动。自己抽1张。");
        Check(six.AI.OnSelectEffectYn(resolvedDraw, resolvedDraw.Id * 16),
            "a direct Synchro-summon draw can be accepted when offered");
        Check(!six.Rule(drawingSynchro, ExecutorType.SpSummon),
            "a used draw trigger is not credited again to another copy this turn");
        six.Executor.OnNewTurn();
        Check(six.Rule(drawingSynchro, ExecutorType.SpSummon), "draw-trigger usage resets on a new turn");
        var illegal = new Fixture();
        illegal.Bot.MonsterZone[0] = Card(1500, type: Monster | CardType.Tuner, level: 3);
        illegal.Bot.MonsterZone[1] = Card(1500, type: Monster | CardType.Tuner, level: 3);
        illegal.Bot.ExtraDeck.Add(drawingSynchro);
        Check(!illegal.Rule(drawingSynchro, ExecutorType.SpSummon),
            "two level 3 Tuners cannot satisfy an ordinary level 6 Synchro procedure");

        foreach (int turn in new[] { 1, 3 })
        {
            var f = new Fixture(); f.Duel.Turn = turn; f.Duel.MainPhase.CanBattlePhase = turn > 1;
            f.Bot.MonsterZone[0] = Card(1500, type: Monster | CardType.Tuner, level: 3);
            f.Bot.MonsterZone[1] = Card(1500, level: 3);
            var terminal = Card(2400, type: Monster | CardType.Synchro, level: 6, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nOnce per turn (Quick Effect): You can negate the activation.");
            terminal.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 8;
            f.Bot.ExtraDeck.Add(terminal); f.Duel.MainPhase.SpecialSummonableCards.Add(terminal);
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == 8,
                "3 plus 3 develops a live level 6 interaction before ending or battling on turn " + turn);
        }
    }

    private static void SynchroMaterialGroups()
    {
        foreach (string recipe in new[] { "调整＋调整以外的龙族怪兽1只以上", "1 Tuner + 1+ non-Tuner Dragon monsters" })
        {
            var f = new Fixture();
            var tuner = Card(100, type: Monster | CardType.Tuner, level: 2);
            var nonTuner = Card(100, level: 6);
            f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = nonTuner;
            var boss = Card(2800, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 8,
                text: recipe + "\nNegate the activation.");
            Set(nonTuner, "Race", 1);
            Check(!f.Rule(boss, ExecutorType.SpSummon), "Synchro parser rejects the wrong non-Tuner race: " + recipe);
            Set(nonTuner, "Race", 8192);
            Check(f.Rule(boss, ExecutorType.SpSummon), "Synchro parser accepts matching non-Tuner race: " + recipe);
        }
        var multi = new Fixture();
        multi.Bot.MonsterZone[0] = Card(100, type: Monster | CardType.Tuner, level: 2);
        multi.Bot.MonsterZone[1] = Card(100, type: Monster | CardType.Tuner, level: 3);
        multi.Bot.MonsterZone[2] = Card(100, level: 5); Set(multi.Bot.MonsterZone[2], "Attribute", 16);
        var lightBoss = Card(3000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 10,
            text: "调整1只以上＋光属性怪兽1只\nNegate the activation.");
        Check(multi.Rule(lightBoss, ExecutorType.SpSummon), "explicit multi-Tuner Synchro procedure accepts two Tuners");
        Set(multi.Bot.MonsterZone[2], "Attribute", 32);
        Check(!multi.Rule(lightBoss, ExecutorType.SpSummon), "multi-Tuner recipe still enforces the non-Tuner attribute");
        var ordinary = Card(3000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 10,
            text: "调整＋调整以外的怪兽1只以上\nNegate the activation.");
        Check(!multi.Rule(ordinary, ExecutorType.SpSummon), "ordinary Synchro procedure never treats a second Tuner as a non-Tuner");
    }

    private static void SynchroDrawResources()
    {
        foreach (string effect in new[]
        {
            "①：这张卡同调召唤时才能发动。自己抽1张。",
            "这个卡名的①的效果1回合只能使用1次。\n①：这张卡同调召唤时才能发动。自己抽1张。",
            "If this card is Synchro Summoned: You can draw 1 card."
        })
        {
            var f = new Fixture();
            f.Bot.MonsterZone[0] = Card(1500, type: Monster | CardType.Tuner, level: 3);
            f.Bot.MonsterZone[1] = Card(1500, level: 3);
            var draw = Card(2000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 6,
                text: "调整＋调整以外的怪兽1只以上\n" + effect);
            f.Bot.ExtraDeck.Add(draw);
            Check(f.Rule(draw, ExecutorType.SpSummon), "printed immediate Synchro draw is recognized: " + effect);
        }
        foreach (string effect in new[]
        {
            "这张卡同调召唤时才能发动。自己抽1张。那之后，手卡丢弃1张。",
            "If this card is Synchro Summoned: You can draw 1 card, then discard 1 card.",
            "①：这张卡同调召唤时才能发动。从自己的卡组抽1张。",
            "①：这张卡同调召唤时，以对方场上1张卡为对象才能发动。自己抽1张。",
            "①：这张卡在墓地存在的场合才能发动。自己抽1张。②：这张卡同调召唤时才能发动。自己抽1张。"
        })
        {
            var f = new Fixture();
            f.Bot.MonsterZone[0] = Card(1500, type: Monster | CardType.Tuner, level: 3);
            f.Bot.MonsterZone[1] = Card(1500, level: 3);
            var draw = Card(2000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 6,
                text: "调整＋调整以外的怪兽1只以上\n" + effect);
            f.Bot.ExtraDeck.Add(draw);
            Check(!f.Rule(draw, ExecutorType.SpSummon), "conditional or extended draw clause receives no guaranteed profit: " + effect);
        }
    }

    private static void SynchroRestrictionRegressions()
    {
        foreach (string recipe in new[] { "「指定调整」＋调整以外的怪兽1只以上", "1 \"Named Tuner\" + 1+ non-Tuner monsters" })
        {
            var f = new Fixture();
            var tuner = Card(100, type: Monster | CardType.Tuner, level: 2);
            var other = Card(100, level: 6);
            f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = other;
            var boss = Card(3000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 8,
                text: recipe + "\nNegate the activation.");
            Check(!f.Rule(boss, ExecutorType.SpSummon), "explicit named material rejects an unrelated Tuner");
            Set(tuner.Data, "Name", recipe[0] == '1' ? "Named Tuner" : "指定调整");
            f.Executor.OnNewTurn();
            Check(f.Rule(boss, ExecutorType.SpSummon), "explicit named material accepts the exact name without a card ID rule");
        }
        foreach (string recipe in new[] { "调整＋调整以外的怪兽2只", "1 Tuner + 2 non-Tuner monsters" })
        {
            var f = new Fixture();
            f.Bot.MonsterZone[0] = Card(100, type: Monster | CardType.Tuner, level: 2);
            f.Bot.MonsterZone[1] = Card(100, level: 6);
            var boss = Card(3000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 8,
                text: recipe + "\nNegate the activation.");
            Check(!f.Rule(boss, ExecutorType.SpSummon), "level sum alone never bypasses an exact non-Tuner count");
            f.Bot.MonsterZone[1] = Card(100, level: 3); f.Bot.MonsterZone[2] = Card(100, level: 3);
            Check(f.Rule(boss, ExecutorType.SpSummon), "two matching non-Tuners satisfy an exact count");
        }
        foreach (string recipe in new[] { "同调怪兽调整＋调整以外的同调怪兽1只以上", "1 Tuner Synchro Monster + 1+ non-Tuner Synchro Monsters" })
        {
            var f = new Fixture();
            f.Bot.MonsterZone[0] = Card(100, type: Monster | CardType.Tuner, level: 2);
            f.Bot.MonsterZone[1] = Card(100, level: 6);
            var boss = Card(4000, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 8,
                text: recipe + "\nNegate the activation.");
            Check(!f.Rule(boss, ExecutorType.SpSummon), "Synchro-only materials cannot be ordinary main-deck monsters");
            Set(f.Bot.MonsterZone[0], "Type", (int)(Monster | CardType.Tuner | CardType.Synchro));
            Set(f.Bot.MonsterZone[1], "Type", (int)(Monster | CardType.Synchro));
            Check(f.Rule(boss, ExecutorType.SpSummon), "Synchro-only procedure accepts matching types and levels");
        }
    }

    private static void SynchroTriggerResources()
    {
        string recipe = "1 \"Group\" Tuner + 1+ non-Tuner monsters\n";
        string trigger = "If this card is Synchro Summoned: You can Special Summon as many \"Group\" Tuners as possible with different Levels from your Deck, in Defense Position.";
        foreach (string resource in new[] { "known", "unknown", "wrong-name", "same-level", "cost", "condition", "disabled" })
        {
            var f = new Fixture();
            var tuner = Card(900, type: Monster | CardType.Tuner, level: 3); Set(tuner.Data, "Name", "Group Tuner");
            f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = Card(900, level: 2);
            string effect = trigger;
            if (resource == "cost") effect = effect.Replace("You can Special Summon", "You can discard 1 card; Special Summon");
            if (resource == "condition") effect = effect.Replace("You can Special Summon", "If you control no Spells, you can Special Summon");
            var starter = Card(100, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 5, text: recipe + effect);
            if (resource == "disabled") Set(starter, "Disabled", 1);
            for (int i = 1; i <= 3 && resource != "unknown"; i++)
            {
                var c = Card(1000, type: Monster | CardType.Tuner, location: CardLocation.Deck, level: resource == "same-level" ? 1 : i);
                Set(c.Data, "Name", resource == "wrong-name" ? "Unrelated Tuner " + i : "Group Tuner " + i);
                f.Bot.Deck.Add(c);
            }
            Check(f.Rule(starter, ExecutorType.SpSummon) == (resource == "known"),
                "low-value Synchro starter requires concrete, matching and usable deck trigger resources: " + resource);
        }
        var locked = new Fixture();
        locked.Bot.MonsterZone[0] = Card(100);
        var addition = Card(100, location: CardLocation.Hand); locked.Bot.Hand.Add(addition);
        locked.Bot.ExtraDeck.Add(Card(6000, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra, text: "2 Effect Monsters"));
        Check(DevelopmentBonus(locked, addition) > 1000, "unrestricted development can reach a beneficial Link");
        var source = Card(100, type: Monster | CardType.Synchro, level: 5, text: recipe + trigger +
            " You cannot Special Summon from the Extra Deck, except Synchro Monsters, the turn you activate this effect.");
        Check(locked.AI.OnSelectEffectYn(source, source.Id * 16), "actual generic summon trigger is accepted");
        source.Location = CardLocation.Grave; locked.Bot.Graveyard.Add(source);
        Check(DevelopmentBonus(locked, addition) == 0, "activated Synchro lock persists after its source leaves the board");
        locked.Executor.OnNewTurn();
        Check(DevelopmentBonus(locked, addition) > 1000, "turn change releases the activated Synchro lock");

        var continuous = new Fixture();
        var lockSource = Card(100, type: Monster | CardType.Tuner, level: 2, text:
            "While it is face-up in the Monster Zone, you cannot Special Summon from the Extra Deck, except Synchro Monsters.");
        continuous.Bot.MonsterZone[0] = lockSource;
        continuous.Bot.MonsterZone[1] = Card(100, level: 9);
        addition = Card(100, location: CardLocation.Hand); continuous.Bot.Hand.Add(addition);
        continuous.Bot.ExtraDeck.Add(Card(6000, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra, text: "2 Effect Monsters"));
        Check(DevelopmentBonus(continuous, addition) == 0, "continuous face-up Synchro lock excludes a predicted Link continuation");
        continuous.Bot.ExtraDeck.Add(Card(500, type: Monster | CardType.Synchro, level: 6, location: CardLocation.Extra,
            text: "1 Tuner + 1 non-Tuner monster"));
        Check(DevelopmentBonus(continuous, addition) > 1000,
            "consuming a continuous lock source in a legal Synchro releases the restriction for later steps");

        var defense = new Fixture(); defense.Enemy.LifePoints = 5000;
        var material = Card(2600, type: Monster | CardType.Tuner, level: 3); Set(material.Data, "Name", "Group Tuner");
        defense.Bot.MonsterZone[0] = material; defense.Bot.MonsterZone[1] = Card(2600, level: 2);
        defense.Bot.Deck.Add(Card(5000, type: Monster | CardType.Tuner, location: CardLocation.Deck, level: 1));
        Set(defense.Bot.Deck.Last().Data, "Name", "Group Tuner 1");
        var small = Card(100, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 5, text: recipe + trigger);
        Check(!defense.Rule(small, ExecutorType.SpSummon), "defense-only deck summons never fabricate direct-attack lethal to justify a losing conversion");

        var used = new Fixture();
        material = Card(900, type: Monster | CardType.Tuner, level: 3); Set(material.Data, "Name", "Group Tuner");
        used.Bot.MonsterZone[0] = material; used.Bot.MonsterZone[1] = Card(900, level: 2);
        var starterUsed = Card(100, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 5, text: recipe + trigger);
        for (int i = 1; i <= 3; i++)
        {
            var c = Card(1000, type: Monster | CardType.Tuner, location: CardLocation.Deck, level: i);
            Set(c.Data, "Name", "Group Tuner " + i); used.Bot.Deck.Add(c);
        }
        Check(used.Rule(starterUsed, ExecutorType.SpSummon), "available deck trigger can justify a low-value starter");
        var activated = Card(100, type: Monster | CardType.Synchro, level: 5, text: recipe + trigger, id: starterUsed.Id);
        Check(used.AI.OnSelectEffectYn(activated, activated.Id * 16), "actual deck trigger records its use");
        Check(!used.Rule(starterUsed, ExecutorType.SpSummon), "a used shared trigger never creates a second set of deck resources in planning");

        material = Card(900, type: Monster | CardType.Tuner, level: 3); Set(material.Data, "Name", "Group Tuner");
        var nonTuner = Card(900, level: 2);
        var list = new List<string> { "#main", material.Id.ToString(), nonTuner.Id.ToString() };
        for (int i = 1; i <= 3; i++)
        {
            var c = Card(1000, type: Monster | CardType.Tuner, location: CardLocation.Deck, level: i);
            Set(c.Data, "Name", "Group Tuner " + i); list.Add(c.Id.ToString());
        }
        for (int i = 0; i < 27; i++) list.Add(Card(100, location: CardLocation.Deck).Id.ToString());
        string ownList = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "synchro-own-list.ydk");
        File.WriteAllLines(ownList, list.Concat(new[] { "#extra", "!side" }));
        foreach (bool known in new[] { false, true })
        {
            var f = new Fixture(known ? ownList : null);
            f.Bot.MonsterZone[0] = material; f.Bot.MonsterZone[1] = nonTuner;
            var starter = Card(100, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 5, text: recipe + trigger);
            Check(f.Rule(starter, ExecutorType.SpSummon) == known,
                "hidden-order deck trigger uses only verified own deck composition: " + known);
            Check(f.Bot.Deck.Count == 30 && f.Bot.Deck.All(c => c.Id == 0), "composition inference never mutates live deck order or reveals shuffled cards");
        }

        var crowded = new Fixture();
        for (int i = 0; i < 5; i++)
        {
            crowded.Bot.MonsterZone[i] = Card(900, type: i < 2 ? Monster | CardType.Tuner : Monster,
                level: i == 0 || i == 3 ? 3 : 2);
            if (i < 2) Set(crowded.Bot.MonsterZone[i].Data, "Name", "Group Tuner " + i);
        }
        for (int i = 0; i < 14; i++)
        {
            var c = Card(2400, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 5,
                text: "1 Tuner + 1 non-Tuner monster\nNegate the activation.");
            c.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = i;
            crowded.Bot.ExtraDeck.Add(c); crowded.Duel.MainPhase.SpecialSummonableCards.Add(c);
        }
        var bridge = Card(100, type: Monster | CardType.Synchro, location: CardLocation.Extra, level: 5, text: recipe + trigger);
        bridge.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 14;
        crowded.Bot.ExtraDeck.Add(bridge); crowded.Duel.MainPhase.SpecialSummonableCards.Add(bridge);
        var highTuner = Card(7000, type: Monster | CardType.Tuner, location: CardLocation.Deck, level: 1);
        Set(highTuner.Data, "Name", "Group Tuner 1"); crowded.Bot.Deck.Add(highTuner);
        var action = crowded.AI.OnSelectIdleCmd(crowded.Duel.MainPhase);
        // Equivalent final boards can now prefer establishing a negate before
        // resolving the exposed starter. Verify the real continuation, not a tie's
        // historical first-card ordering.
        if (action.Action == MainPhaseAction.MainAction.SpSummon && action.Index != 14)
        {
            ApplyDevelopmentSummon(crowded, action);
            crowded.Duel.MainPhase = new MainPhase();
            if (SynchroSubsets(crowded.Bot.GetMonsters(), 5).Any()) crowded.Duel.MainPhase.SpecialSummonableCards.Add(bridge);
            action = crowded.AI.OnSelectIdleCmd(crowded.Duel.MainPhase);
        }
        Check(action.Action == MainPhaseAction.MainAction.SpSummon && action.Index == 14,
            "more than 40 first-step branches retain a real route to the small starter and its concrete summon trigger");
    }

    private static float DevelopmentBonus(Fixture f, ClientCard addition)
    {
        var evaluation = typeof(StoryLuckyExecutor).GetField("evaluation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.Executor);
        var bonuses = (Dictionary<ClientCard, float>)evaluation.GetType().GetMethod("DevelopmentBonuses", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(evaluation, new object[] { new[] { addition } });
        return bonuses[addition];
    }

    private static void ApplyDevelopmentSummon(Fixture f, MainPhaseAction action)
    {
        var target = f.Duel.MainPhase.SpecialSummonableCards.Single(c => c.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] == action.Index);
        var mats = f.AI.OnSelectCard(f.Bot.GetMonsters(), 2, f.Bot.GetMonsterCount(), HintMsg.SynchroMaterial, false);
        Check(mats.Count(c => c.HasType(CardType.Tuner)) == 1 && mats.Sum(c => c.Level) == target.Level,
            "executed Synchro materials satisfy an independent level/Tuner check");
        foreach (var c in mats)
        {
            for (int i = 0; i < 7; i++) if (f.Bot.MonsterZone[i] == c) f.Bot.MonsterZone[i] = null;
            c.Location = CardLocation.Grave; f.Bot.Graveyard.Add(c);
        }
        f.Bot.ExtraDeck.Remove(target); target.Location = CardLocation.MonsterZone; target.LastLocation = CardLocation.Extra;
        int zone = Enumerable.Range(0, 5).First(i => f.Bot.MonsterZone[i] == null);
        target.Sequence = zone; f.Bot.MonsterZone[zone] = target;
        f.Executor.OnSpSummoned();
    }

    private static void DevelopmentResources()
    {
        var venus = Real(64734921, CardLocation.Hand);
        var ball = Real(39552864, CardLocation.Deck);
        var big = Card(5000, level: 4, location: CardLocation.Hand);
        string ownList = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "development-own-list.ydk");
        File.WriteAllLines(ownList, new[] { "#main", venus.Id.ToString(), big.Id.ToString(), ball.Id.ToString(), ball.Id.ToString(), ball.Id.ToString(), "#extra", "4280258", "!side" });
        foreach (bool known in new[] { false, true })
        {
            var f = new Fixture(known ? ownList : null); f.Bot.Deck.Clear();
            for (int i = 0; i < 3; i++) f.Bot.Deck.Add(new ClientCard(0, CardLocation.Deck, -1));
            foreach (var c in new[] { venus, big }) { f.Bot.Hand.Add(c); f.Duel.MainPhase.SummonableCards.Add(c); }
            f.Bot.ExtraDeck.Add(Real(4280258, CardLocation.Extra));
            Check(f.Rule(venus, ExecutorType.SummonOrSet) == known,
                "Venus uses known remaining Shine Balls for a four-material terminal, never invented hidden deck contents " + known);
            Check(f.Bot.Deck.All(c => c.Id == 0) && f.Bot.LifePoints == 8000 && f.Bot.GetMonsterCount() == 0,
                "own-list analysis does not reveal shuffled order or mutate LP/board " + known);
            if (known)
            {
                f.Bot.LifePoints = 500;
                Check(!f.Rule(venus, ExecutorType.SummonOrSet), "Venus development cannot spend the player's last 500 LP");
            }
        }

        var h = new Fixture();
        h.Bot.MonsterZone[0] = Card(100, type: Monster | CardType.Tuner, level: 2);
        h.Bot.MonsterZone[1] = Card(700); h.Bot.MonsterZone[2] = Card(100, level: 8);
        h.Bot.Deck.Add(Card(100, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Deck));
        var halq = Real(50588353, CardLocation.Extra); var boss = Real(84815190, CardLocation.Extra);
        h.Bot.ExtraDeck.Add(halq); h.Bot.ExtraDeck.Add(boss);
        Check(h.Rule(halq, ExecutorType.SpSummon), "Halq replacement Tuner is a concrete resource in the continuation");
        var used = Real(50588353); h.Bot.MonsterZone[3] = used;
        Check(h.AI.OnSelectEffectYn(used, used.Id * 16), "actual Halq trigger records its once-per-turn use");
        h.Bot.MonsterZone[3] = null; used.Location = CardLocation.Grave; h.Bot.Graveyard.Add(used);
        Check(!h.Rule(halq, ExecutorType.SpSummon), "a second Halq cannot fabricate another replacement Tuner after its shared effect was used");
        h.Executor.OnNewTurn();
        Check(h.Rule(halq, ExecutorType.SpSummon), "next turn restores the Halq continuation");
    }

    private static void DevelopmentBudget()
    {
        var f = new Fixture(); var random = new Random(727);
        for (int i = 0; i < 5; i++) f.Bot.MonsterZone[i] = Card(random.Next(0, 900), type: i < 2 ? Monster | CardType.Tuner : Monster, level: i + 1);
        int[] ids = { 84815190, 27548199, 4280258, 65741786, 90290572, 48589580, 50588353, 21887175, 98127546, 38342335, 2857636, 63101468, 74586817, 73580471, 86066372 };
        foreach (var id in ids) { var c = Real(id, CardLocation.Extra); f.Bot.ExtraDeck.Add(c); f.Duel.MainPhase.SpecialSummonableCards.Add(c); }
        for (int i = 0; i < 5; i++) f.Enemy.MonsterZone[i] = Card(2500, controller: 1);
        var watch = Stopwatch.StartNew(); f.AI.OnSelectIdleCmd(f.Duel.MainPhase); watch.Stop();
        var evaluation = typeof(StoryLuckyExecutor).GetField("evaluation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.Executor);
        int nodes = (int)evaluation.GetType().GetProperty("LastDevelopmentNodes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(evaluation);
        Check(nodes <= 5000 && watch.ElapsedMilliseconds < 5000, "crowded mixed-extra search respects its work budget");
        Console.WriteLine("Development search: 15 extra options / 10 monsters, " + nodes + " nodes, " + watch.ElapsedMilliseconds + " ms");
    }

    private static IEnumerable<List<ClientCard>> SynchroSubsets(IList<ClientCard> cards, int level)
    {
        for (int mask = 1; mask < (1 << cards.Count); mask++)
        {
            var subset = cards.Where((c, i) => (mask & (1 << i)) != 0).ToList();
            if (subset.Count >= 2 && subset.Count(c => c.HasType(CardType.Tuner)) == 1 && subset.Sum(c => c.Level) == level)
                yield return subset;
        }
    }

    // Independent exhaustive partition oracle: how many distinct Synchro terminals can
    // these five starting bodies form? It has no access to the AI's score/search/recipe code.
    private static int PartitionTerminals(IList<ClientCard> bodies, IList<int> levels)
    {
        int best = 0;
        foreach (int level in levels)
            foreach (var subset in SynchroSubsets(bodies, level))
                best = Math.Max(best, 1 + PartitionTerminals(bodies.Except(subset).ToList(), levels.Where(i => i != level).ToList()));
        return best;
    }

    private static void DevelopmentPartitions()
    {
        var random = new Random(8415); int doubles = 0;
        var watch = Stopwatch.StartNew();
        for (int sample = 0; sample < 40; sample++)
        {
            var f = new Fixture(); f.Enemy.LifePoints = 100000;
            for (int i = 0; i < 5; i++) f.Bot.MonsterZone[i] = Card(random.Next(0, 600),
                type: i < 2 ? Monster | CardType.Tuner : Monster, level: i < 2 ? random.Next(1, 4) : random.Next(1, 9));
            var six = Card(2400, type: Monster | CardType.Synchro, level: 6, location: CardLocation.Extra,
                text: "1 Tuner + 1+ non-Tuner monsters\nOnce per turn: negate the activation.");
            foreach (var c in new[] { six, Real(27548199, CardLocation.Extra), Real(84815190, CardLocation.Extra) }) f.Bot.ExtraDeck.Add(c);
            f.Bot.Graveyard.Add(Real(90290572, CardLocation.Grave));
            int optimal = PartitionTerminals(f.Bot.GetMonsters(), new[] { 6, 8, 10 });
            if (optimal == 2) doubles++;
            for (int step = 0; step < 3; step++)
            {
                f.Duel.MainPhase = new MainPhase();
                foreach (var c in f.Bot.ExtraDeck.Where(c => SynchroSubsets(f.Bot.GetMonsters(), c.Level).Any()))
                {
                    c.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = f.Duel.MainPhase.SpecialSummonableCards.Count;
                    f.Duel.MainPhase.SpecialSummonableCards.Add(c);
                }
                var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
                if (action.Action != MainPhaseAction.MainAction.SpSummon) break;
                ApplyDevelopmentSummon(f, action);
            }
            int actual = f.Bot.GetMonsters().Count(c => c.HasType(CardType.Synchro));
            Check(actual == optimal, "seeded material partition reaches the exhaustive maximum terminal count " + sample + ": " + actual + "/" + optimal);
        }
        Check(doubles > 5, "partition oracle includes multiple genuine two-terminal routes");
        Console.WriteLine("Development partition oracle: 40 boards, " + doubles + " two-terminal opportunities, " + watch.ElapsedMilliseconds + " ms");
    }
}
