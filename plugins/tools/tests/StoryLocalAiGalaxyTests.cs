using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private const int GalaxyLevelDescription = 85747929 * 16 + 1;

    private static void GalaxyLevelRegressions()
    {
        typeof(GameAI).Assembly.GetType("WindBot.Program")
            .GetField("Rand", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, new Random(123));
        var failures = new List<string>();
        foreach (Action test in new Action[] { GalaxyKeepFives, GalaxyMakeEight, GalaxyMakeFour,
            GalaxyForcedLevels, GalaxyLegalTargets, GalaxyTargetBinding, GalaxyNumberIsolation,
            GalaxyResolutionReplan, GalaxyReset, GalaxyTranceLock })
        {
            try { test(); }
            catch (Exception e) { failures.Add(test.Method.Name + ": " + (e.InnerException ?? e).Message); }
        }
        foreach (var failure in failures) Console.WriteLine(failure);
        Check(failures.Count == 0, "Galaxy level regression scenarios: " + failures.Count + " failures");
        Console.WriteLine("Local AI: Galaxy optional level, target/number binding and Trance oath regressions passed");
    }

    private static Fixture GalaxyField(out ClientCard source, out ClientCard soldier, int partner, int extra)
    {
        var f = new Fixture(); source = GalaxyCard(85747929); soldier = GalaxyCard(46659709);
        f.Bot.MonsterZone[0] = source; f.Bot.MonsterZone[1] = soldier;
        f.Bot.MonsterZone[2] = GalaxyCard(partner);
        f.Bot.ExtraDeck.Add(GalaxyCard(extra, CardLocation.Extra));
        f.Duel.LastSummonPlayer = 0; f.Duel.LastSummonedCards.Add(soldier);
        return f;
    }

    private static object GalaxyPlan(Fixture f, bool improve, params ClientCard[] targets)
    {
        var source = f.Bot.MonsterZone[0];
        var profile = CoreField(CoreInvoke(f.Executor, "DescribeEffect", source, GalaxyLevelDescription), "LevelEffect");
        return CoreInvoke(Evaluation(f), "PlanLevelEffect", source, profile, targets, improve, false, false, null, null);
    }
    private static int GalaxyPlannedLevel(object plan) => plan == null ? 0 : (int)CoreField(plan, "Level");
    private static void GalaxyResolving(Fixture f, ClientCard source, int description = GalaxyLevelDescription, int player = 0)
    {
        f.Duel.CurrentChainInfo.Clear(); f.Duel.CurrentChain.Clear();
        f.Duel.CurrentChainInfo.Add(new ChainInfo(source, player, description));
        f.Duel.CurrentChain.Add(source); f.Duel.SolvingChainIndex = 1;
    }

    private static void GalaxyKeepFives()
    {
        foreach (int desc in new[] { -1, GalaxyLevelDescription })
        {
            var f = GalaxyField(out var source, out var soldier, 46659709, 58069384);
            // Give Nova an actual revival payoff. Its printed summon text alone
            // must no longer award a free body when no Cyber Dragon is in GY.
            f.Bot.Graveyard.Add(Card(2100, 1600, Monster, CardLocation.Grave, id: 70095154, level: 5));
            Check(!f.AI.OnSelectEffectYn(source, desc), "two Galaxy Soldiers keep the legal rank-five route, descriptor " + desc);
            Check(soldier.Level == 5 && f.Bot.MonsterZone[2].Level == 5, "declining the optional trigger leaves live levels unchanged");
            Check(CoreMaterials(f, f.Bot.ExtraDeck[0], soldier, f.Bot.MonsterZone[2]), "preserved Soldiers still satisfy Nova's real Machine recipe");
            f.Duel.MainPhase.SpecialSummonableCards.Add(f.Bot.ExtraDeck[0]);
            Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.SpSummon,
                "woven idle policy can actually take the preserved Nova summon");
        }
        var search = GalaxyField(out var dragon, out _, 46659709, 58069384);
        Check(CoreField(CoreInvoke(search.Executor, "DescribeEffect", dragon, 85747929 * 16), "Purpose").ToString() != "ChangeLevel",
            "Photon Dragon search is distinct from its level trigger");
    }

    private static void GalaxyMakeEight()
    {
        foreach (int desc in new[] { -1, GalaxyLevelDescription })
        {
            var f = GalaxyField(out var source, out var soldier, 93717133, 63767246);
            Check(f.AI.OnSelectEffectYn(source, desc), "accept beneficial rank-eight setup, descriptor " + desc);
            Check(f.AI.OnSelectCard(new[] { soldier }, 1, 1, HintMsg.Target, false).Single() == soldier, "target the actual summoned Soldier");
            GalaxyResolving(f, source);
            Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 1, "woven number callback chooses eight by value");
            Check(f.AI.OnAnnounceNumber(new[] { 8, 4 }) == 0, "reordered numeric options preserve the chosen level");
            Check(soldier.Level == 5 && f.Bot.ExtraDeck.Single().Id == 63767246 && f.Bot.Graveyard.Count == 0,
                "planning never mutates live levels, extra deck or grave");
        }
    }

    private static void GalaxyMakeFour()
    {
        var f = GalaxyField(out var source, out var soldier, 89132148, 16643334);
        Check(f.AI.OnSelectEffectYn(source, -1), "accept a real Photon Blast rank-four continuation");
        GalaxyResolving(f, source);
        Check(f.AI.OnAnnounceNumber(new[] { 8, 4 }) == 1, "select four when the real rank-four continuation is better");
    }

    private static void GalaxyForcedLevels()
    {
        var f = GalaxyField(out var source, out var soldier, 93717133, 63767246);
        Set(soldier, "Level", 8);
        Check(!f.AI.OnSelectEffectYn(source, -1), "do not break an existing rank-eight pair");
        f = GalaxyField(out source, out soldier, 93717133, 63767246);
        Set(soldier, "Level", 4);
        Check(GalaxyPlannedLevel(GalaxyPlan(f, true, soldier)) == 8 && f.AI.OnSelectEffectYn(source, -1), "level four can only become eight");
        f = GalaxyField(out source, out soldier, 89132148, 16643334); Set(soldier, "Level", 8);
        Check(GalaxyPlannedLevel(GalaxyPlan(f, true, soldier)) == 4 && f.AI.OnSelectEffectYn(source, -1), "level eight can only become four");
        f.Bot.ExtraDeck.Clear();
        Check(!f.AI.OnSelectEffectYn(source, -1), "an empty extra deck cannot justify a speculative level change");
    }

    private static void GalaxyLegalTargets()
    {
        var f = GalaxyField(out var source, out var soldier, 93717133, 63767246);
        f.Duel.LastSummonedCards.Clear(); f.Duel.LastSummonedCards.Add(source);
        Check(!f.AI.OnSelectEffectYn(source, -1), "an older Soldier outside the summon batch is not an eligible target");
        foreach (Action<ClientCard> invalidate in new Action<ClientCard>[] {
            c => c.Controller = 1, c => c.Location = CardLocation.Grave,
            c => Set(c, "Position", (int)CardPosition.FaceDownDefence),
            c => { Set(c, "Attribute", (int)CardAttribute.Dark); Set(c.Data, "Attribute", (int)CardAttribute.Dark); },
            c => { Set(c, "Type", (int)(Monster | CardType.Xyz)); Set(c.Data, "Type", (int)(Monster | CardType.Xyz)); },
            c => { Set(c, "Type", (int)(Monster | CardType.Link)); Set(c.Data, "Type", (int)(Monster | CardType.Link)); }
        })
        {
            f = GalaxyField(out source, out soldier, 93717133, 63767246); invalidate(soldier);
            Check(GalaxyPlan(f, false, soldier) == null, "exclude an illegal level target: " + invalidate.Method.Name);
        }
        f = GalaxyField(out source, out soldier, 93717133, 63767246); f.Duel.Player = 1;
        Check(!f.AI.OnSelectEffectYn(source, -1), "own-turn development does not invent an opponent-turn Xyz summon");
    }

    private static void GalaxyTargetBinding()
    {
        var f = GalaxyField(out var source, out var soldier, 93717133, 63767246);
        var partner = f.Bot.MonsterZone[2]; f.Duel.LastSummonedCards.Add(partner);
        Check(f.AI.OnSelectEffectYn(source, -1), "plan over multiple actual summon-event targets");
        f.Executor.SetCard(ExecutorType.Activate, source, -1);
        Check(f.AI.OnSelectCard(new[] { partner, soldier }, 1, 1, HintMsg.Target, false).Single() == soldier,
            "target and number come from the same continuation");
        // The prompt can carry -1 while the resolving chain supplies the precise descriptor.
        GalaxyResolving(f, source);
        Check(f.AI.OnSelectCard(new[] { partner, soldier }, 1, 1, HintMsg.Target, false).Single() == soldier,
            "generic activation descriptor and precise chain descriptor match the same level intent");
        var replacement = GalaxyCard(46659709); f.Bot.MonsterZone[1] = replacement; soldier.Location = CardLocation.Grave;
        Check(f.AI.OnSelectCard(new[] { replacement }, 1, 1, HintMsg.Target, false).Single() == replacement,
            "replan only within authoritative core target offers");
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 1, "rebound target retains the correct number");
    }

    private static void GalaxyNumberIsolation()
    {
        var f = GalaxyField(out var source, out _, 93717133, 63767246);
        Check(f.AI.OnSelectEffectYn(source, -1), "commit a useful numeric plan");
        f.AI.SelectNumber(4);
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 0, "outside resolution, preserve the native numeric queue");
        GalaxyResolving(f, source, 85747929 * 16);
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 0, "same source's different effect keeps the fallback");
        GalaxyResolving(f, GalaxyCard(85747929));
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 0, "another physical copy cannot consume this plan");
        GalaxyResolving(f, source, GalaxyLevelDescription, 1);
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 0, "opponent effect keeps the fallback");
        GalaxyResolving(f, source);
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 1, "the actual bound chain overrides a stale native number");
        Check(f.AI.OnAnnounceNumber(new[] { 4, 6 }) == 0, "unrelated option values keep the native result");
        f.Duel.SolvingChainIndex = 0;
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 0, "level choice did not overwrite the global numeric queue");
        f.AI.Executor = new TestDefaultExecutor(f.AI, f.Duel); f.AI.SelectNumber(8);
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 1, "non-story executors retain their native number selection");
    }

    private static void GalaxyResolutionReplan()
    {
        var f = GalaxyField(out var source, out var soldier, 93717133, 63767246);
        Check(f.AI.OnSelectEffectYn(source, -1), "commit an initial rank-eight route");
        f.Bot.MonsterZone[2] = GalaxyCard(89132148);
        f.Bot.ExtraDeck.Clear(); f.Bot.ExtraDeck.Add(GalaxyCard(16643334, CardLocation.Extra));
        GalaxyResolving(f, source);
        Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 0, "a changed resolving board switches the bound target to the real rank-four route");
        Check(soldier.Level == 5, "resolution re-planning remains side-effect-free");
    }

    private static void GalaxyReset()
    {
        foreach (Action<Fixture> reset in new Action<Fixture>[] {
            f => f.Executor.OnChainEnd(), f => f.Executor.OnNewPhase(), f => f.Executor.OnNewTurn() })
        {
            var f = GalaxyField(out var source, out _, 93717133, 63767246);
            Check(f.AI.OnSelectEffectYn(source, -1), "create a plan before lifecycle reset");
            reset(f); GalaxyResolving(f, source); f.AI.SelectNumber(4);
            Check(f.AI.OnAnnounceNumber(new[] { 4, 8 }) == 0, "stale level plan cleared by " + reset.Method.Name);
        }
    }

    private static void GalaxyTranceLock()
    {
        var f = GalaxyField(out var source, out var soldier, 46659709, 58069384);
        var trance = GalaxyCard(63956833, CardLocation.Hand); f.Bot.Hand.Add(trance);
        var nova = f.Bot.ExtraDeck[0];
        Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", CoreInitial(f), nova, true, false) >= 0,
            "Nova has a legal destination before Galaxy Trance");
        // Exercise the same commit path used by the live activation, including descriptor zero.
        CoreInvoke(f.Executor, "CommitEffect", CoreInvoke(f.Executor, "DescribeEffect", trance, 0));
        foreach (bool coreOffered in new[] { false, true })
            Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", CoreInitial(f), nova, true, coreOffered) == -1,
                "Galaxy Trance oath blocks a foreign extra summon even with stale core offers");
        Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", CoreInitial(f), Card(location: CardLocation.Hand), false, false) == -1,
            "Galaxy Trance also blocks a foreign normal/main-zone arrival");
        foreach (int id in new[] { 46659709, 89132148, 63767246, 16643334 })
            Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", CoreInitial(f), GalaxyCard(id), id == 63767246 || id == 16643334, false) >= 0,
                "oath permits actual Photon/Galaxy card " + id);
        f.Duel.NegatedChainIndexList.Add(1); f.Executor.OnChainEnd();
        Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", CoreInitial(f), nova, true, false) == -1,
            "cost-applied oath survives a negated effect and chain end");
        Check(!f.AI.OnSelectEffectYn(source, -1), "do not make a useless level change after Galaxy Trance");
        f.Executor.OnNewTurn();
        Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", CoreInitial(f), nova, true, false) >= 0,
            "Galaxy Trance oath expires on the next turn");
    }
}
