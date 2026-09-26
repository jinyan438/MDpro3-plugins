using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static void SafetyRegressions()
    {
        var f = new Fixture();
        var baronne = Real(84815190); f.Bot.MonsterZone[0] = baronne;
        Check(!f.AI.OnSelectEffectYn(baronne, baronne.Id * 16), "real Baronne never activates destruction into an empty enemy field");
        f.Bot.Graveyard.Add(Card(400, location: CardLocation.Grave));
        Check(!f.AI.OnSelectEffectYn(baronne, baronne.Id * 16 + 2), "healthy Baronne does not return itself for a weak revival");
        var enemy = Card(2600, controller: 1); f.Enemy.MonsterZone[0] = enemy;
        Check(f.AI.OnSelectEffectYn(baronne, baronne.Id * 16), "Baronne still removes an enemy threat");
        Check(f.AI.OnSelectCard(new[] { baronne, enemy }, 1, 1, HintMsg.Destroy, false).Single() == enemy, "Baronne selects the enemy, never itself");
        f = new Fixture(); f.Bot.MonsterZone[0] = baronne;
        f.Enemy.MonsterZone[0] = Card(300, id: 31305911, controller: 1, text: "Cannot be destroyed by battle.");
        Check(f.AI.OnSelectEffectYn(baronne, baronne.Id * 16), "battle immunity does not prevent useful effect destruction");
        f.Enemy.MonsterZone[0] = Card(3000, controller: 1, text: "Cannot be destroyed by card effects.");
        Check(!f.AI.OnSelectEffectYn(baronne, baronne.Id * 16), "do not waste destruction against explicit effect destruction immunity");
        f.Enemy.MonsterZone[0] = Real(21887175, controller: 1);
        Check(!f.AI.OnSelectEffectYn(baronne, baronne.Id * 16), "an untargetable enemy is not a reason to activate any-field destruction");
        f = new Fixture(); var hyperion = Real(55794644); f.Bot.MonsterZone[0] = hyperion;
        Check(!f.AI.OnSelectEffectYn(hyperion, hyperion.Id * 16), "Hyperion does not pay a banish cost just to destroy our board");
        var generic = Card(text: "Once per turn: You can target 1 card on the field; destroy it.");
        Check(!f.AI.OnSelectEffectYn(generic, generic.Id * 16), "unknown single-effect any-field destruction also has a preflight guard");
        generic = Card(text: "①：以场上1张卡为对象才能发动。那张卡破坏。");
        Check(!f.AI.OnSelectEffectYn(generic, generic.Id * 16), "Chinese target and destruction clauses remain linked across punctuation");
        var masterflare = Real(63101468); f.Bot.MonsterZone[1] = masterflare;
        Check(!f.AI.OnSelectEffectYn(masterflare, hyperion.Id * 16), "copied Hyperion effect retains its destructive safety profile");
        f = new Fixture(); baronne = Real(84815190); f.Bot.MonsterZone[0] = baronne;
        f.Chain(Card(type: CardType.Spell, location: CardLocation.SpellZone), 0);
        Check(!f.AI.OnSelectEffectYn(baronne, baronne.Id * 16 + 1), "Baronne does not negate our own effect");
        f = new Fixture(); f.Bot.MonsterZone[0] = baronne;
        f.Chain(Card(type: CardType.Spell, location: CardLocation.Grave, controller: 1), 1);
        Check(f.AI.OnSelectEffectYn(baronne, baronne.Id * 16 + 1), "negation works even when the enemy has no field cards");

        f = new Fixture(); var accesscode = Real(86066372); Set(accesscode, "Attack", 5300); f.Bot.MonsterZone[0] = accesscode;
        Check(f.AI.OnSelectEffectYn(accesscode, accesscode.Id * 16), "Accesscode attack gain is not confused with its removal effect");
        var link1 = Card(1000, type: Monster | CardType.Link, level: 1, location: CardLocation.Grave);
        var link3 = Real(48589580, CardLocation.Grave);
        Check(f.AI.OnSelectCard(new[] { link1, link3 }, 1, 1, HintMsg.Target, false).Single() == link3, "Accesscode attack gain chooses highest Link rating");
        f.Enemy.MonsterZone[0] = Card(500, controller: 1);
        Check(!f.AI.OnSelectEffectYn(accesscode, accesscode.Id * 16 + 1), "Accesscode does not banish itself for a weak enemy");
        f.Bot.Graveyard.Add(link3);
        Check(f.AI.OnSelectEffectYn(accesscode, accesscode.Id * 16 + 1), "Accesscode uses a legal grave Link to remove an enemy");
        Check(f.AI.OnSelectCard(new[] { accesscode, link3 }, 1, 1, HintMsg.Remove, false).Single() == link3, "Accesscode cost preserves the finisher");
        Check(!f.AI.OnSelectEffectYn(accesscode, accesscode.Id * 16 + 1), "an already spent Link attribute is not assumed to be a legal cost");
        f.Executor.OnNewTurn();
        Check(f.AI.OnSelectEffectYn(accesscode, accesscode.Id * 16 + 1), "Accesscode attribute restrictions reset next turn");
        f = new Fixture(); var equipSavage = Real(27548199); f.Bot.MonsterZone[0] = equipSavage;
        Check(f.AI.OnSelectEffectYn(equipSavage, equipSavage.Id * 16), "Savage equip effect remains usable");
        Check(f.AI.OnSelectCard(new[] { link1, link3 }, 1, 1, HintMsg.Equip, false).Single() == link3, "Savage equips a Link with more negate counters");
        f = new Fixture(); var rose = Real(73580471); f.Bot.MonsterZone[0] = rose;
        f.Bot.MonsterZone[1] = Real(84815190); f.Enemy.MonsterZone[0] = Card(500, controller: 1);
        Check(!f.AI.OnSelectEffectYn(rose, rose.Id * 16), "Black Rose does not wipe a superior friendly board");
        f.Bot.MonsterZone[1] = null; f.Enemy.MonsterZone[0] = Card(3500, controller: 1);
        f.Enemy.MonsterZone[1] = Card(3000, controller: 1);
        Check(f.AI.OnSelectEffectYn(rose, rose.Id * 16), "profitable Black Rose board wipes remain enabled");
        f = new Fixture(); var moonCost = Real(90290572); f.Bot.MonsterZone[0] = moonCost;
        var parshath = Real(48589580); f.Bot.MonsterZone[1] = parshath; f.Enemy.MonsterZone[0] = Card(0, controller: 1);
        Check(!f.AI.OnSelectEffectYn(moonCost, moonCost.Id * 16 + 1), "Moon does not tribute a substantial Fairy for a tiny enemy");

        f = new Fixture(); baronne = Real(84815190); var savage = Real(27548199);
        f.Bot.MonsterZone[0] = baronne; f.Bot.MonsterZone[1] = savage;
        var weakLink = Card(1000, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra,
            text: "2 Effect Monsters\nIf this card is Link Summoned: add 1 monster from your Deck to your hand. Special Summon it.");
        Check(!f.Rule(weakLink, ExecutorType.SpSummon), "strong extra bosses are not traded for a weak search Link");
        for (int i = 2; i < 5; i++) f.Bot.MonsterZone[i] = Real(84815190);
        Check(!f.Rule(weakLink, ExecutorType.SpSummon), "a full field never bypasses the extra summon loss check");
        var unknown = Card(4000, type: Monster | CardType.Fusion, level: 12, location: CardLocation.Extra, text: "A special alternate procedure");
        Check(!f.Rule(unknown, ExecutorType.SpSummon), "an unknown material procedure is not assumed to cost zero");

        f = new Fixture(); var ip = Real(65741786, CardLocation.Extra);
        f.Bot.MonsterZone[0] = Real(86066372); f.Bot.MonsterZone[1] = Card(0);
        Check(!f.Rule(ip, ExecutorType.SpSummon), "I:P cannot plan using a Link monster for its non-Link recipe");
        f = new Fixture(); var moon = Real(90290572, CardLocation.Extra);
        f.Bot.MonsterZone[0] = Card(300); f.Bot.MonsterZone[1] = Card(300);
        Check(!f.Rule(moon, ExecutorType.SpSummon), "Moon does not estimate a Fairy recipe using non-Fairies");
        foreach (var c in f.Bot.GetMonsters()) { Set(c, "Race", (int)CardRace.Fairy); Set(c.Data, "Race", (int)CardRace.Fairy); }
        Check(f.Rule(moon, ExecutorType.SpSummon), "cheap legal Fairies still produce a useful Moon");

        f = new Fixture(); baronne = Real(84815190); f.Bot.MonsterZone[0] = baronne;
        var a = Card(300); var b = Card(300); f.Bot.MonsterZone[1] = a; f.Bot.MonsterZone[2] = b;
        Check(f.Rule(weakLink, ExecutorType.SpSummon), "use cheap bodies while retaining the established boss");
        Check(f.AI.OnSelectCard(new[] { baronne, b, a }, 1, 1, HintMsg.LinkMaterial, false).Single() != baronne,
            "incremental material selection follows the planned cheap combination");
        Check(f.AI.OnSelectCard(new[] { baronne }, 1, 1, HintMsg.LinkMaterial, true).Count == 0,
            "cancel a changed recipe instead of silently filling it with a protected boss");
        f = new Fixture();
        Check(f.AI.OnSelectCard(new[] { baronne }, 1, 1, HintMsg.Destroy, true).Count == 0,
            "a cancellable self-only destructive prompt is declined");
        Check(f.AI.OnSelectCard(new[] { baronne }, 1, 1, HintMsg.Destroy, false).Single() == baronne,
            "forced prompts still obey the core protocol");
        ExtraPlanRegressions();
        Console.WriteLine("Local AI: real-card effect/material safety regressions passed");
    }

    private static void ExtraPlanRegressions()
    {
        var f = new Fixture(); var apo = Real(4280258); Set(apo, "Attack", 3200);
        f.Bot.MonsterZone[0] = apo; f.Bot.MonsterZone[1] = Real(90290572); f.Bot.MonsterZone[2] = Card(300);
        var access = Real(86066372, CardLocation.Extra);
        Check(!f.Rule(access, ExecutorType.SpSummon), "do not consume a live four-negate Apollousa for idle Accesscode");
        f = new Fixture(); var ip = Real(65741786); f.Bot.MonsterZone[0] = ip;
        var moon = Real(90290572); f.Bot.MonsterZone[1] = moon;
        f.Enemy.MonsterZone[0] = Card(3000, controller: 1); f.Enemy.SpellZone[0] = Card(type: CardType.Trap, location: CardLocation.SpellZone, controller: 1);
        Check(f.Rule(access, ExecutorType.SpSummon), "a useful Accesscode upgrade against a real opposing board remains enabled");
        var chosen = f.AI.OnSelectCard(new[] { moon, ip }, 1, 1, HintMsg.LinkMaterial, false).Single();
        var rest = chosen == moon ? ip : moon;
        Check(f.AI.OnSelectCard(new[] { rest }, 1, 1, HintMsg.LinkMaterial, false).Single() == rest, "Link ratings 2+2 complete the accepted Accesscode plan");
        Check(f.AI.OnSelectCard(new ClientCard[0], 0, 1, HintMsg.LinkMaterial, true).Count == 0, "complete incremental material plan finishes cleanly");

        f = new Fixture(); var goddess = Real(98127546, CardLocation.Extra);
        for (int i = 0; i < 2; i++) f.Bot.MonsterZone[i] = Card(300);
        f.Enemy.MonsterZone[0] = Card(4000, controller: 1); f.Enemy.MonsterZone[1] = Card(3500, controller: 1);
        Check(!f.Rule(goddess, ExecutorType.SpSummon), "Goddess cannot use two opposing monsters to reach its four-material minimum");
        f.Bot.MonsterZone[2] = Card(300, type: Monster | CardType.Link, level: 2);
        Check(f.Rule(goddess, ExecutorType.SpSummon), "Goddess uses exactly one opposing monster with three legal own bodies");
        var materials = f.AI.OnSelectCard(f.Bot.GetMonsters().Concat(f.Enemy.GetMonsters()).ToList(), 4, 5, HintMsg.LinkMaterial, false);
        Check(materials.Count == 4 && materials.Count(c => c.Controller == 1) == 1, "Goddess selection preserves its planned opponent-material limit");

        f = new Fixture(); var halq = Real(50588353, CardLocation.Extra);
        f.Bot.MonsterZone[0] = Card(300); f.Bot.MonsterZone[1] = Card(300);
        Check(!f.Rule(halq, ExecutorType.SpSummon), "Halqifibrax recipe requires an actual Tuner");
        f.Bot.MonsterZone[0] = Card(300, type: Monster | CardType.Tuner);
        f.Bot.Deck.Add(Card(300, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Deck));
        Check(f.Rule(halq, ExecutorType.SpSummon), "Halqifibrax converts cheap legal bodies into live extension");
        f = new Fixture(); var baronne = Real(84815190, CardLocation.Extra);
        f.Bot.MonsterZone[0] = Card(500, type: Monster | CardType.Tuner, level: 2);
        f.Bot.MonsterZone[1] = Card(800, level: 8);
        Check(f.Rule(baronne, ExecutorType.SpSummon), "legal level 2+8 Synchro plan builds Baronne");
        var masterflare = Real(63101468, CardLocation.Extra);
        Check(!f.Rule(masterflare, ExecutorType.SpSummon), "Masterflare requires Fairy non-Tuners, not arbitrary matching levels");
        var avramax = Real(21887175, CardLocation.Extra);
        f = new Fixture(); f.Bot.MonsterZone[0] = Real(90290572); f.Bot.MonsterZone[1] = Real(90290572);
        Check(!f.Rule(avramax, ExecutorType.SpSummon), "revived extra-deck monsters do not satisfy Avramax's summoned-from-extra recipe");
        foreach (var c in f.Bot.GetMonsters()) c.LastLocation = CardLocation.Extra;
        f.Enemy.MonsterZone[0] = Card(3000, controller: 1);
        Check(f.Rule(avramax, ExecutorType.SpSummon), "actual extra-deck summoned materials can build Avramax");
        f = new Fixture(); var spider = Card(1000, type: Monster | CardType.Link, level: 1, location: CardLocation.Extra, text: "1 Normal Monster");
        f.Bot.MonsterZone[0] = Card(0, type: CardType.Monster | CardType.Normal | CardType.Token);
        Check(f.Rule(spider, ExecutorType.SpSummon), "a known one-Normal-monster recipe can use a cheap token");
        f.Bot.MonsterZone[0] = Card(0);
        Check(!f.Rule(spider, ExecutorType.SpSummon), "an Effect monster cannot satisfy a Normal-monster recipe");

        f = new Fixture(); apo = Real(4280258, CardLocation.Extra);
        for (int i = 0; i < 4; i++) f.Bot.MonsterZone[i] = Card(100, type: Monster | CardType.Link, level: 2);
        Check(f.Rule(apo, ExecutorType.SpSummon), "four cheap bodies can form a four-negate Apollousa");
        var available = f.Bot.GetMonsters().ToList();
        for (int i = 0; i < 4; i++)
        {
            var picks = f.AI.OnSelectCard(available, i < 2 ? 1 : 0, 1, HintMsg.LinkMaterial, i >= 2);
            Check(picks.Count == 1, "Apollousa keeps the planned body count even when an earlier finish becomes legal " + i);
            available.Remove(picks[0]);
        }
        Check(f.AI.OnSelectCard(available, 0, 1, HintMsg.LinkMaterial, true).Count == 0, "Apollousa finishes after all planned bodies, without overpaying");

        // Exercise the real GameAI action loop, including duplicate card references with
        // distinct effect descriptions, then chain resolution rather than only delegates.
        f = new Fixture(); var accessBody = Real(86066372); f.Bot.MonsterZone[0] = accessBody;
        f.Duel.MainPhase.ActivableCards.Add(accessBody); f.Duel.MainPhase.ActivableDescs.Add(accessBody.Id * 16 + 1);
        f.Duel.MainPhase.ActivableCards.Add(accessBody); f.Duel.MainPhase.ActivableDescs.Add(accessBody.Id * 16);
        accessBody.ActionActivateIndex[accessBody.Id * 16 + 1] = 0; accessBody.ActionActivateIndex[accessBody.Id * 16] = 1;
        var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.Activate && action.Index == 1, "idle loop skips harmful removal but accepts the same card's attack effect");
        f.Duel.CurrentChainInfo.Add(new ChainInfo(accessBody, 0, accessBody.Id * 16)); f.Duel.SolvingChainIndex = 1;
        var low = Card(100, type: Monster | CardType.Link, level: 1, location: CardLocation.Grave);
        var high = Real(48589580, CardLocation.Grave);
        Check(f.AI.OnSelectCard(new[] { low, high }, 1, 1, HintMsg.Target, false).Single() == high, "resolving chain uses its own effect description");

        foreach (bool reverse in new[] { false, true })
        {
            f = new Fixture(); f.Bot.MonsterZone[0] = Card(100); f.Bot.MonsterZone[1] = Card(100);
            var weaker = Card(1000, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra,
                text: "2 Effect Monsters\nIf this card is Link Summoned: add 1 card from your Deck to your hand.");
            var stronger = Card(2000, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra,
                text: "2 Effect Monsters\nIf this card is Link Summoned: add 1 card from your Deck to your hand.");
            weaker.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 3;
            stronger.ActionIndex[(int)MainPhaseAction.MainAction.SpSummon] = 8;
            foreach (var c in reverse ? new[] { stronger, weaker } : new[] { weaker, stronger }) f.Duel.MainPhase.SpecialSummonableCards.Add(c);
            var picked = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(picked.Action == MainPhaseAction.MainAction.SpSummon && picked.Index == 8,
                "actual action loop selects the better extra destination regardless of which lights up first " + reverse);
        }

        var random = new Random(2049); var timer = Stopwatch.StartNew();
        for (int scenario = 0; scenario < 100; scenario++)
        {
            f = new Fixture(); f.Enemy.LifePoints = 100000;
            var boss = Real(scenario % 2 == 0 ? 84815190 : 27548199); f.Bot.MonsterZone[0] = boss;
            var small = new List<ClientCard>();
            for (int i = 1; i < 5; i++) { var c = Card(random.Next(0, 501)); small.Add(c); f.Bot.MonsterZone[i] = c; }
            var destination = Card(1600, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra,
                text: "2 Effect Monsters\nIf this card is Link Summoned: add 1 card from your Deck to your hand.");
            Check(f.Rule(destination, ExecutorType.SpSummon), "seeded board finds cheap useful Link materials " + scenario);
            var offered = small.Concat(new[] { boss }).OrderBy(c => random.Next()).ToList();
            var first = f.AI.OnSelectCard(offered, 1, 1, HintMsg.LinkMaterial, false).Single(); offered.Remove(first);
            var second = f.AI.OnSelectCard(offered, 1, 1, HintMsg.LinkMaterial, false).Single();
            Check(first != boss && second != boss && first != second, "material order cannot sacrifice or duplicate a protected boss " + scenario);
        }
        Console.WriteLine("Extra-deck material properties: 100 seeded full boards = " + timer.ElapsedMilliseconds + " ms");
    }
}
