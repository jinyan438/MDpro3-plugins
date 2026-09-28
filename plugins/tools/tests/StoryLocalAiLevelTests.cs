using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static object LevelProfile(Fixture f, ClientCard source, int description) =>
        CoreField(CoreInvoke(f.Executor, "DescribeEffect", source, description), "LevelEffect");
    private static object LevelDecision(Fixture f, ClientCard source, int description, ClientCard[] targets = null,
        bool improve = true, bool paid = false, bool bound = false, int[] numbers = null, int[] options = null) =>
        CoreInvoke(Evaluation(f), "PlanLevelEffect", source, LevelProfile(f, source, description), targets, improve, paid, bound, numbers, options);
    private static ClientCard LevelBoss(int level, bool synchro) => Card(4500, 3500, Monster | (synchro ? CardType.Synchro : CardType.Xyz),
        CardLocation.Extra, level: level, text: (synchro ? "1 Tuner + 1+ non-Tuner monsters" : "2 Level " + level + " monsters") +
        "\nOnce per turn, when your opponent activates a card or effect (Quick Effect): negate the activation.");
    private static List<ClientCard> LevelChosen(object plan) => (List<ClientCard>)CoreField(plan, "Targets");

    private static void GeneralLevelRegressions()
    {
        var failures = new List<string>();
        foreach (Action test in new Action[] { GeneralLevelSelf, GeneralLevelNormalBridge, GeneralLevelSynchro,
            GeneralLevelFixedTargets, GeneralLevelMultiTargets, GeneralLevelAll, GeneralLevelOptions,
            GeneralLevelLimits, GeneralLevelCosts, GeneralLevelChainBinding, GeneralLevelAnonymousOracle, GeneralLevelBudget })
        {
            try { test(); }
            catch (Exception e) { failures.Add(test.Method.Name + ": " + (e.InnerException ?? e).Message); }
        }
        foreach (var failure in failures) Console.WriteLine(failure);
        Check(failures.Count == 0, "generic level scenario failures: " + failures.Count);
        Console.WriteLine("Local AI: generic level transforms, costs, multi-targets, future actions and independent sum oracle passed");
    }

    private static void GeneralLevelSelf()
    {
        var f = new Fixture(); var source = LevelCard(98555327); var partner = Card(100, level: 8);
        f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = partner; f.Bot.ExtraDeck.Add(LevelBoss(8, false));
        ComboOffer(f, source, source.Id * 16);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Activate, "a self-level increase is a real planned opening action");
        var intent = CoreInvoke(f.Executor, "DescribeEffect", source, source.Id * 16);
        Check(CoreField(intent, "Purpose").ToString() == "ChangeLevel", "self-level ignition uses the generic effect purpose");
        Check(CoreField(CoreInvoke(f.Executor, "DescribeEffect", source, source.Id * 16 + 1), "Purpose").ToString() != "ChangeLevel",
            "its unrelated tribute/search effect is not mistaken for a level adjustment");
        Check(source.Level == 4 && partner.Level == 8 && f.Bot.Graveyard.Count == 0, "self-level search never mutates live cards");
        f = new Fixture(); source = LevelCard(98555327); partner = Card(100, level: 4);
        f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = partner; f.Bot.ExtraDeck.Add(LevelBoss(4, false));
        Check(!f.AI.OnSelectEffectYn(source, source.Id * 16), "preserve an existing rank-four route instead of always raising a self effect");
    }

    private static void GeneralLevelNormalBridge()
    {
        var f = new Fixture(); var source = LevelCard(98555327, CardLocation.Hand);
        f.Bot.Hand.Add(source); f.Bot.MonsterZone[0] = Card(100, level: 8); f.Bot.ExtraDeck.Add(LevelBoss(8, false));
        f.Duel.MainPhase.SummonableCards.Add(source);
        Check(PlannedCard(MainSummonPlan(f)) == source, "future self-level change makes an otherwise mismatched normal summon useful");
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Summon, "execute the first normal summon of the level bridge");
        f.Bot.Hand.Remove(source); source.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[1] = source;
        f.Duel.MainPhase = new MainPhase(); ComboOffer(f, source, source.Id * 16);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Activate, "replan and execute its level effect after the live normal summon");
    }

    private static void GeneralLevelSynchro()
    {
        var f = new Fixture(); var source = LevelCard(83334932); var other = Card(100, level: 3);
        Set(other, "Race", (int)CardRace.Machine); Set(other.Data, "Race", (int)CardRace.Machine);
        f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = other; f.Bot.ExtraDeck.Add(LevelBoss(7, true));
        Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "a generic plus-two effect enables a level-seven Synchro");
        var chosen = f.AI.OnSelectCard(new[] { other, source }, 1, 1, HintMsg.Faceup, false).Single();
        Check(chosen == source || chosen == other, "pick an actually offered Machine target");
        Check(source.Level + other.Level + 2 == 7, "independent level sum confirms either legal target opens exactly seven");
        var wrongRace = Card(100, level: 3);
        Check(LevelDecision(f, source, source.Id * 16 + 1, new[] { wrongRace }, false, true) == null, "race filters reject unrelated monsters");
        f = new Fixture(); source = LevelCard(50482813, CardLocation.Grave); f.Bot.Graveyard.Add(source);
        var tuner = Card(100, type: Monster | CardType.Tuner, level: 3); other = Card(100, level: 5);
        Set(other, "Attribute", (int)CardAttribute.Wind); Set(other.Data, "Attribute", (int)CardAttribute.Wind);
        f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = other; f.Bot.ExtraDeck.Add(LevelBoss(6, true));
        Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "a grave effect pays its banish to enable a lower Synchro sum");
    }

    private static void GeneralLevelFixedTargets()
    {
        var f = GalaxyField(out var dragon, out var soldier, 46659709, 58069384);
        var source = LevelCard(863795); f.Bot.MonsterZone[3] = source;
        Check(!f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "another card's fixed-to-four effect also preserves the double-five route");
        f.Bot.ExtraDeck.Clear(); f.Bot.ExtraDeck.Add(LevelBoss(4, false));
        Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "fixed-to-four is useful when the actual extra deck rewards it");
        var offered = new[] { source, soldier, f.Bot.MonsterZone[2] };
        Check(f.AI.OnSelectCard(offered, 1, 1, HintMsg.Faceup, false).Single() != source, "exclude-self target restrictions survive generic selection");
        var plan = LevelDecision(f, source, source.Id * 16 + 1, new[] { source }, false, true);
        Check(plan == null, "a source cannot be invented as its own excluded target");
    }

    private static void GeneralLevelMultiTargets()
    {
        var f = new Fixture(); var source = LevelCard(29092121); var other = LevelCard(29092121);
        f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = other; f.Bot.ExtraDeck.Add(LevelBoss(8, false));
        Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "exactly two changed targets create a rank-eight route");
        var selected = f.AI.OnSelectCard(new[] { other, source }, 2, 2, HintMsg.Faceup, false);
        Check(selected.Count == 2 && selected.Distinct().Count() == 2, "the two-card level plan selects distinct physical copies");
        f = new Fixture(); source = LevelCard(29092121); other = LevelCard(29092121);
        f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = other; f.Bot.ExtraDeck.Add(LevelBoss(8, false));
        ComboOffer(f, source, source.Id * 16 + 1, 0); ComboOffer(f, other, other.Id * 16 + 1, 1);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Activate,
            "equivalent level sources cannot each defer to the other and deadlock the selected main-phase route");
        f = new Fixture(); source = LevelCard(29092121); f.Bot.MonsterZone[0] = source;
        f.Bot.MonsterZone[1] = Card(100, level: 8); f.Bot.ExtraDeck.Add(LevelBoss(8, false));
        Check(!f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "cannot turn a mandatory two-target effect into a one-target conversion");
        f = new Fixture(); source = LevelCard(8129306); var tuner = Card(100, type: Monster | CardType.Tuner, level: 3);
        Set(tuner, "Race", (int)CardRace.Plant); Set(tuner.Data, "Race", (int)CardRace.Plant);
        f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = tuner; f.Bot.ExtraDeck.Add(LevelBoss(9, true));
        Check(f.AI.OnSelectEffectYn(source, source.Id * 16 + 1), "up-to-two level changes enable a Synchro with one changed material");
        selected = f.AI.OnSelectCard(new[] { tuner, source }, 1, 2, HintMsg.Faceup, false);
        Check(selected.Count == 1 && source.Level + tuner.Level + 2 == 9, "selecting the maximum target count would overshoot; choose exactly one");
    }

    private static void GeneralLevelAll()
    {
        var f = new Fixture(); var spell = LevelCard(74741494, CardLocation.Hand);
        var tuner = Card(100, type: Monster | CardType.Tuner, level: 3); var other = Card(100, level: 5);
        var xyz = GalaxyCard(85747929); f.Bot.Hand.Add(spell);
        f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = other; f.Bot.MonsterZone[2] = xyz;
        f.Enemy.MonsterZone[0] = Card(100, level: 6, controller: 1); f.Bot.ExtraDeck.Add(LevelBoss(6, true));
        var outcomes = ComboRoots(f, CoreInitial(f), spell, 0);
        Check(outcomes.Count == 1, "all-monster changes are one joint state, never independently chosen targets");
        var bodies = ComboBodies(outcomes[0]);
        Check((int)CoreField(bodies.Single(b => CoreField(b, "Card") == tuner), "ProjectedLevel") == 2 &&
            (int)CoreField(bodies.Single(b => CoreField(b, "Card") == other), "ProjectedLevel") == 4,
            "simultaneously reduce both materials to the real six-level sum");
        Check((int)CoreField(bodies.Single(b => CoreField(b, "Card") == xyz), "ProjectedLevel") == 0,
            "Xyz ranks never become levels during a group change");
        Check(!CoreCards(outcomes[0], "Reserve").Contains(spell) && CoreCards(outcomes[0], "Grave").Contains(spell),
            "a normal spell is spent by the level route");
        ComboOffer(f, spell, 0);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Activate, "the real idle callback takes a useful all-monster level spell");
        Check(tuner.Level == 3 && other.Level == 5 && f.Enemy.MonsterZone[0].Level == 6 && f.Bot.Hand.Contains(spell),
            "group planning does not mutate either side's live levels or hand");
    }

    private static Fixture LevelOptionField(bool increase, out ClientCard source, out ClientCard tuner)
    {
        var f = new Fixture(); source = LevelCard(11234702, CardLocation.Grave); f.Bot.Graveyard.Add(source);
        tuner = Card(100, type: Monster | CardType.Tuner, level: 3); Set(tuner.Data, "Setcode", (long)0x27);
        f.Bot.MonsterZone[0] = tuner; f.Bot.MonsterZone[1] = Card(100, level: 4);
        f.Bot.ExtraDeck.Add(LevelBoss(increase ? 8 : 6, true)); return f;
    }

    private static void GeneralLevelOptions()
    {
        foreach (bool increase in new[] { true, false })
        {
            var f = LevelOptionField(increase, out var source, out var tuner);
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16), "T.G. plus/minus option is evaluated against the real Synchro route");
            Check(f.AI.OnSelectCard(new[] { tuner }, 1, 1, HintMsg.Target, false).Single() == tuner, "keep the selected T.G. material bound to its choice");
            f.Bot.Graveyard.Remove(source); source.Location = CardLocation.Removed; f.Bot.Banished.Add(source);
            GalaxyResolving(f, source, source.Id * 16);
            int up = source.Id * 16 + 1, down = up + 1;
            Check(f.AI.OnSelectOption(new[] { up, down }) == (increase ? 0 : 1), "resolution works after the source paid its banish cost");
            Check(f.AI.OnSelectOption(new[] { down, up }) == (increase ? 1 : 0), "semantic option descriptions survive a reordered core list");
            Check((int)CoreField(f.AI, "m_option") == -1 && (int)CoreField(f.AI, "m_number") == -1, "generic level decisions never write global option/number queues");
        }
    }

    private static void GeneralLevelLimits()
    {
        var f = new Fixture(); var a = LevelCard(83334932); var b = LevelCard(83334932);
        f.Bot.MonsterZone[0] = a; f.Bot.MonsterZone[1] = b;
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", a, a.Id * 16 + 1);
        Check(ComboRoots(f, CoreInitial(f), a, a.Id * 16 + 1).Count == 0, "a spent soft count cannot be replayed by search");
        Check(ComboRoots(f, CoreInitial(f), b, b.Id * 16 + 1).Count > 0, "another physical copy retains its own soft count");
        CoreInvoke(Evaluation(f), "NoteResourceMove", a, (int)CardLocation.MonsterZone, (int)CardLocation.Grave);
        Check(ComboRoots(f, CoreInitial(f), a, a.Id * 16 + 1).Count > 0, "leaving the field resets only that physical copy's soft count");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", a, a.Id * 16 + 1);
        f.Executor.OnNewTurn();
        Check(ComboRoots(f, CoreInitial(f), a, a.Id * 16 + 1).Count > 0, "turn reset restores the generic soft limit");
        f = new Fixture(); a = LevelCard(8129306); b = LevelCard(8129306);
        f.Bot.MonsterZone[0] = a; f.Bot.MonsterZone[1] = b;
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", a, a.Id * 16 + 1);
        CoreInvoke(Evaluation(f), "NoteResourceMove", a, (int)CardLocation.MonsterZone, (int)CardLocation.Grave);
        Check(ComboRoots(f, CoreInitial(f), b, b.Id * 16 + 1).Count == 0, "name-based count is shared across copies");
        Set(b, "Disabled", 1); f.Executor.OnNewTurn();
        Check(ComboRoots(f, CoreInitial(f), b, b.Id * 16 + 1).Count == 0, "disabled sources cannot supply speculative level changes");
    }

    private static void GeneralLevelChainBinding()
    {
        var f = LevelOptionField(true, out var snake, out var tuner);
        Set(tuner, "Level", 2); Set(tuner.Data, "Level", 2);
        Set(f.Bot.MonsterZone[1], "Level", 5); Set(f.Bot.MonsterZone[1].Data, "Level", 5);
        Check(f.AI.OnSelectEffectYn(snake, snake.Id * 16), "commit the first level link");
        var dragon = GalaxyCard(85747929); var soldier = GalaxyCard(46659709);
        f.Bot.MonsterZone[2] = dragon; f.Bot.MonsterZone[3] = soldier; f.Bot.MonsterZone[4] = Card(100, level: 8);
        var rankBoss = LevelBoss(8, false); Set(rankBoss, "Attack", 7000); Set(rankBoss.Data, "Attack", 7000);
        f.Bot.ExtraDeck.Add(rankBoss); f.Duel.LastSummonedCards.Add(soldier);
        f.Duel.LastSummonPlayer = 0;
        Check(f.AI.OnSelectEffectYn(dragon, -1), "commit a second independent level link");
        f.Duel.CurrentChainInfo.Add(new ChainInfo(snake, 0, snake.Id * 16));
        f.Duel.CurrentChainInfo.Add(new ChainInfo(dragon, 0, GalaxyLevelDescription));
        f.Duel.SolvingChainIndex = 2;
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 1, "second chain link keeps its own target and number");
        f.Duel.SolvingChainIndex = 1;
        Check(f.AI.OnSelectOption(new[] { snake.Id * 16 + 2, snake.Id * 16 + 1 }) == 1,
            "the first link's plus/minus choice survives another committed level effect");
    }

    private static void GeneralLevelCosts()
    {
        var f = new Fixture(); var source = Card(100, level: 4); f.Bot.MonsterZone[0] = source;
        var profile = AnonymousLevelProfile(source, 1);
        Set(profile, "Cost", Enum.Parse(profile.GetType().DeclaringType.GetNestedType("Cost", BindingFlags.NonPublic), "Life"));
        Set(profile, "Life", 500); f.Bot.LifePoints = 500;
        Check(LevelDecision(f, source, source.Id * 16, improve: false) == null, "level planning cannot pay the last LP as a cost");
        f.Bot.LifePoints = 501;
        var state = ComboRoots(f, CoreInitial(f), source, source.Id * 16).Single();
        Check((int)CoreField(state, "Life") == 1 && f.Bot.LifePoints == 501, "a level branch pays its exact LP cost without touching live LP");
        f = LevelOptionField(true, out source, out var tuner);
        state = ComboRoots(f, CoreInitial(f), source, source.Id * 16).First();
        Check(!CoreCards(state, "Grave").Contains(source) && f.Bot.Graveyard.Contains(source), "banish-self level cost consumes the simulated grave resource only");
        f = new Fixture(); source = Card(100, id: 49655592, level: 4); f.Bot.MonsterZone[0] = source;
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", source, source.Id * 16);
        Check(LevelDecision(f, source, source.Id * 16, improve: false) != null,
            "a new core offer for a once-per-chain level trigger is not blocked for the rest of the turn");
    }

    private static object AnonymousLevelProfile(ClientCard source, params int[] deltas)
    {
        var registry = typeof(GameAI).Assembly.GetType("MDPro3.Plugins.Features.StoryMode.StoryAiLevelEffects");
        var type = registry.GetNestedType("Profile", BindingFlags.NonPublic);
        var profile = Activator.CreateInstance(type, true);
        Set(profile, "SourceId", source.Id); Set(profile, "Description", source.Id * 16); Set(profile, "From", CardLocation.MonsterZone);
        Set(profile, "Scope", Enum.Parse(registry.GetNestedType("Scope", BindingFlags.NonPublic), "Self"));
        Set(profile, "Values", deltas); Set(profile, "Numbers", deltas.Select(Math.Abs).ToArray());
        Set(profile, "Delta", true); Set(profile, "Once", true); Set(profile, "Predictable", true);
        var map = (IDictionary)registry.GetField("bySource", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type)); list.Add(profile); map.Add(source.Id, list);
        return profile;
    }

    private static void GeneralLevelAnonymousOracle()
    {
        var random = new Random(928101);
        for (int i = 0; i < 60; i++)
        {
            int level = random.Next(2, 7), partner = random.Next(2, 7), adjustment = i % 2 == 0 ? 2 : -1;
            int destination = level + partner + adjustment;
            var f = new Fixture(); var source = Card(100, type: Monster | CardType.Tuner, level: level);
            f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = Card(100, level: partner);
            f.Bot.ExtraDeck.Add(LevelBoss(destination, true)); AnonymousLevelProfile(source, -1, 2);
            Check(f.AI.OnSelectEffectYn(source, source.Id * 16), "anonymous-ID level effect selects a useful branch " + i);
            GalaxyResolving(f, source, source.Id * 16);
            int index = f.AI.OnAnnounceNumber(new[] { 1, 2 }); int actualDelta = index == 0 ? -1 : 2;
            Check(level + partner + actualDelta == destination, "independent arithmetic oracle verifies the resulting Synchro sum " + i);
            Check(source.Level == level, "oracle search preserved live level " + i);
        }
    }

    private static void GeneralLevelBudget()
    {
        var f = new Fixture(); var source = Card(100, type: Monster | CardType.Tuner, level: 4);
        var profile = AnonymousLevelProfile(source, -1, 1, 2, 3, 4, 5);
        var registry = profile.GetType().DeclaringType;
        Set(profile, "Scope", Enum.Parse(registry.GetNestedType("Scope", BindingFlags.NonPublic), "Target"));
        Set(profile, "Maximum", 5);
        f.Bot.MonsterZone[0] = source;
        for (int i = 1; i < 5; i++) f.Bot.MonsterZone[i] = Card(100, level: i + 1);
        for (int i = 5; i <= 12; i++) f.Bot.ExtraDeck.Add(LevelBoss(i, true));
        var clock = Stopwatch.StartNew(); var plan = LevelDecision(f, source, source.Id * 16); clock.Stop();
        int nodes = (int)Evaluation(f).GetType().GetProperty("LastLevelNodes", CoreFlags).GetValue(Evaluation(f));
        Console.WriteLine("Generic level stress: " + nodes + " continuation nodes, " + clock.ElapsedMilliseconds + " ms");
        Check(nodes <= 10000, "multi-target/numeric alternatives share a bounded total continuation budget");
        Check(clock.ElapsedMilliseconds < 10000, "generic level stress remains a bounded interactive decision");
    }
}
