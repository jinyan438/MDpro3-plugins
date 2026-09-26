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
        DevelopmentBudget();
        Console.WriteLine("Local AI: multi-step development regressions passed");
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
