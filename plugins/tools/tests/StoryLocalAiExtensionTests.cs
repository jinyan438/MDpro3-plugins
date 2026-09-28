using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static void ExtensionPlanningRegressions()
    {
        var failures = new List<string>();
        foreach (Action test in new Action[] { ExtensionScales, ExtensionTiger, ExtensionWolf, ExtensionLayered,
            ExtensionRevolution, ExtensionMaterialZones, ExtensionSearchCopies, ExtensionLinkZones, ExtensionExtraTriggers,
            ExtensionDance, ExtensionLockOrdering, ExtensionResourceBanish, ExtensionHolyWaterMode, ExtensionMoonMode })
        {
            try { test(); }
            catch (Exception e) { failures.Add(test.Method.Name + ": " + (e.InnerException ?? e).Message); }
        }
        foreach (var failure in failures) Console.WriteLine(failure);
        Check(failures.Count == 0, "resource conversion regression scenarios: " + failures.Count + " failures");
        Console.WriteLine("Local AI: scale, grave fusion, duel limits, equivalent resources and Link zone regressions passed");
    }

    private static void ExtensionScales()
    {
        foreach (int id in new[] { 47705572, 83190280 })
        {
            var f = new Fixture(); var scale = ExtensionCard(id, CardLocation.Hand); f.Bot.Hand.Add(scale);
            Check((bool)CoreInvoke(Evaluation(f), "ModelledComboAction", scale, 1160), "the actual scale activation descriptor is recognized " + id);
            Check(!(bool)CoreInvoke(Evaluation(f), "ModelledComboAction", scale, 0), "scale placement is distinct from its ignition effect " + id);
            var initial = CoreInitial(f); var placed = ComboRoots(f, initial, scale, 1160).Single();
            Check(ComboBodies(placed).Count == 0 && !CoreCards(placed, "Reserve").Contains(scale), "a scale is consumed from hand without becoming a monster");
            Check(((Dictionary<ClientCard, int>)CoreField(placed, "Spells"))[scale] == 0, "placement reserves an actual outer spell zone");
            Check(ComboRoots(f, placed, scale, 1160).Count == 0, "a placed scale cannot be placed from hand twice");
            Check(CoreCards(initial, "Reserve").Contains(scale) && f.Bot.Hand.Contains(scale), "scale planning leaves parent and live hand untouched");
            for (int side = 0; side < 2; side++)
            {
                f.Bot.SpellZone[side == 0 ? 0 : 4] = Card(type: CardType.Trap, location: CardLocation.SpellZone, position: CardPosition.FaceDownDefence);
                var choices = ComboRoots(f, CoreInitial(f), scale, 1160);
                Check(choices.Count == (side == 0 ? 1 : 0), "face-down cards occupy scale slots");
                if (side == 0) Check(((Dictionary<ClientCard, int>)CoreField(choices.Single(), "Spells"))[scale] == 4, "placement uses the remaining outer zone");
            }
        }
    }

    private static void ExtensionTiger()
    {
        var f = new Fixture(); var tiger = ExtensionCard(83190280, CardLocation.SpellZone); f.Bot.SpellZone[0] = tiger;
        var hound = ExtensionCard(35763582, CardLocation.Grave); f.Bot.Graveyard.Add(hound);
        var initial = CoreInitial(f); var revived = ComboRoots(f, initial, tiger).Single();
        var body = ComboBodies(revived).Single();
        Check(BodyCard(body) == hound && (bool)CoreField(body, "EffectsBlocked") && (bool)CoreField(body, "Temporary") &&
            (bool)CoreField(body, "CannotAttack"), "Tiger revival is disabled, temporary and cannot attack");
        Check(!CoreCards(revived, "Grave").Contains(hound) && ((Dictionary<ClientCard, int>)CoreField(revived, "Spells")).ContainsKey(tiger), "revival moves one body and retains its scale");
        Check(ComboRoots(f, revived, tiger).Count == 0, "Tiger's field instance can revive only once");
        var liger = ExtensionCard(81196066); f.Bot.MonsterZone[0] = liger;
        f.Bot.Hand.Add(ExtensionCard(35618217, CardLocation.Hand));
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", tiger, 0);
        var bounce = ComboRoots(f, CoreInitial(f), liger).First(s => CoreCards(s, "Reserve").Contains(tiger));
        Check(!((HashSet<ClientCard>)CoreField(bounce, "ComboInstances")).Contains(tiger), "returning a scale resets that physical instance's usage");
        Check(!((Dictionary<ClientCard, int>)CoreField(bounce, "Spells")).ContainsKey(tiger), "a returned scale frees its spell zone");
        CoreInvoke(Evaluation(f), "NoteResourceSummons", new[] { hound }, tiger, 0);
        Check(((HashSet<ClientCard>)CoreField(Evaluation(f), "temporaryDevelopmentBodies")).Contains(hound), "live Tiger summon retains its end-phase expiry");
        CoreInvoke(Evaluation(f), "NoteResourceMove", tiger, (int)CardLocation.SpellZone, (int)CardLocation.Hand);
        Check(!((HashSet<ClientCard>)CoreField(Evaluation(f), "usedComboInstances")).Contains(tiger), "live scale removal resets the same once-per-instance ledger");
    }

    private static void ExtensionWolf()
    {
        var f = new Fixture(); var wolf = ExtensionCard(47705572, CardLocation.Hand); f.Bot.Hand.Add(wolf);
        var a = ExtensionCard(35763582, CardLocation.Grave); var b = ExtensionCard(14152693, CardLocation.Grave);
        CoreAdd(f.Bot.Graveyard, a, b); var liger = ExtensionCard(81196066, CardLocation.Extra); f.Bot.ExtraDeck.Add(liger);
        Check(CoreFusions(f, CoreInitial(f), wolf).Count == 0, "Wolf cannot perform a grave fusion directly from hand");
        var placed = ComboRoots(f, CoreInitial(f), wolf, 1160).Single();
        var fused = CoreFusions(f, placed, wolf).Single();
        Check(ComboBodies(fused).Any(x => BodyCard(x) == liger), "placing Wolf opens a concrete grave-fusion route");
        Check(!CoreCards(fused, "Grave").Contains(a) && !CoreCards(fused, "Grave").Contains(b), "Wolf banishes both concrete grave materials");
        Check(CoreCards(fused, "PendingFusionMaterials").Count == 0, "banished materials do not invent sent-to-grave triggers");
        Check(((Dictionary<ClientCard, int>)CoreField(fused, "Spells")).ContainsKey(wolf) && !CoreCards(fused, "Grave").Contains(wolf), "persistent fusion source stays in its spell zone");
        Check(CoreFusions(f, fused, wolf).Count == 0, "Wolf fusion cannot repeat while its field instance remains");
        f.Bot.Hand.Remove(wolf); wolf.Location = CardLocation.SpellZone; f.Bot.SpellZone[0] = wolf;
        ComboOffer(f, wolf, 0);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Activate, "real idle callback uses available grave fusion");
        Check(f.AI.OnSelectCard(new[] { liger }, 1, 1, HintMsg.SpSummon, false).Single() == liger, "fusion destination matches its activation plan");
        var materials = f.AI.OnSelectCard(new[] { b, a }, 2, 2, HintMsg.FusionMaterial, false);
        Check(materials.Contains(a) && materials.Contains(b), "real fusion prompt accepts the planned grave materials");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", wolf, 0);
        Check(CoreFusions(f, CoreInitial(f), wolf).Count == 0, "actual Wolf effect commitment spends its instance allowance");
        f.Executor.OnNewTurn(); Check(CoreFusions(f, CoreInitial(f), wolf).Count == 1, "Wolf allowance returns on a new turn");
    }

    private static void ExtensionLayered()
    {
        var f = new Fixture(); var spell = ExtensionCard(58570206, CardLocation.Hand); f.Bot.Hand.Add(spell);
        f.Bot.ExtraDeck.Add(ExtensionCard(81196066, CardLocation.Extra));
        CoreAdd(f.Bot.Hand, ExtensionCard(35763582, CardLocation.Hand), ExtensionCard(14152693, CardLocation.Hand));
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "Layered Fusion cannot use a two-material recipe");
        var lion = ExtensionCard(24550676, CardLocation.Extra); var goddess = ExtensionCard(54701958, CardLocation.Extra);
        CoreAdd(f.Bot.ExtraDeck, lion, goddess); f.Bot.Hand.Add(ExtensionCard(11317977, CardLocation.Hand));
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "Layered Fusion needs an opponent monster for an extra-deck material");
        f.Enemy.MonsterZone[0] = Card(controller: 1);
        var initial = CoreInitial(f); var choices = CoreFusions(f, initial, spell);
        var fused = choices.Single(s => ComboBodies(s).Any(b => BodyCard(b) == goddess));
        Check((int)CoreField(fused, "Life") == 4500 && !CoreCards(fused, "Extra").Contains(lion) && !CoreCards(fused, "Grave").Contains(lion),
            "Layered Fusion pays 3500 LP and banishes the real extra material");
        Check(CoreCards(fused, "Grave").Count == 4, "ordinary hand materials and the fusion spell reach the graveyard");
        Check(CoreCards(initial, "Extra").Contains(lion) && (int)CoreField(initial, "Life") == 8000, "fusion branches do not share LP or extra-deck mutations");
        foreach (int life in new[] { 1, 3499, 3500, 3501, 8000 })
        {
            f.Bot.LifePoints = life;
            Check(CoreFusions(f, CoreInitial(f), spell).Any(s => ComboBodies(s).Any(b => BodyCard(b) == goddess)) == (life > 3500),
                "Layered Fusion must leave positive LP at " + life);
        }
    }

    private static void ExtensionRevolution()
    {
        var f = new Fixture(); var revolution = ExtensionCard(97682931, CardLocation.Grave); f.Bot.Graveyard.Add(revolution);
        Check(ComboRoots(f, CoreInitial(f), revolution, revolution.Id * 16).Count == 0, "Revolution needs a level-seven-or-higher Synchro");
        var stardust = ExtensionCard(44508094); f.Bot.MonsterZone[0] = stardust;
        var initial = CoreInitial(f); var revived = ComboRoots(f, initial, revolution, revolution.Id * 16).Single();
        Check((int)CoreField(ComboBodies(revived).Single(b => BodyCard(b) == revolution), "ProjectedLevel") == 1 && revolution.Level == 3,
            "Revolution's level-one revival is branch-local");
        Check((int)CoreField(revived, "DeckCount") == f.Bot.Deck.Count - 1 && CoreCards(revived, "Grave").Count == 0,
            "unknown mill consumes a card count without inventing a grave identity");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", revolution, revolution.Id * 16);
        f.Executor.OnNewTurn();
        Check(ComboRoots(f, CoreInitial(f), revolution, revolution.Id * 16).Count == 0, "once per duel must not reset on the next turn");
        f = new Fixture(); revolution = ExtensionCard(97682931, CardLocation.Grave); f.Bot.Graveyard.Add(revolution);
        f.Bot.MonsterZone[0] = ExtensionCard(44508094); f.Bot.Deck.Clear();
        Check(ComboRoots(f, CoreInitial(f), revolution, revolution.Id * 16).Count == 0, "an empty deck cannot pay Revolution's required mill");
        f.Bot.Deck.Add(Card(location: CardLocation.Deck));
        CoreInvoke(Evaluation(f), "NoteResolvedResourceEffect", 91800273, 1);
        Check(ComboRoots(f, CoreInitial(f), revolution, revolution.Id * 16).Count == 0, "Shifter prevents the mill reaching the graveyard");
    }

    private static void ExtensionMaterialZones()
    {
        var f = new Fixture(); var converging = ExtensionCard(291414, CardLocation.Hand); f.Bot.Hand.Add(converging);
        f.Bot.MonsterZone[0] = ExtensionCard(44508094); f.Bot.MonsterZone[1] = Card(level: 2, attack: 100);
        var majestic = ExtensionCard(40939228, CardLocation.Extra); f.Bot.ExtraDeck.Add(majestic);
        var state = CoreInvoke(Evaluation(f), "AddDevelopmentBody", CoreInitial(f), converging, true, true, CoreBudget(f));
        Check(CoreStates(Evaluation(f), "ExtraSuccessors", state, new[] { majestic }, CoreBudget(f), false).Count == 1,
            "a simulated arrival receives field-only name substitution for its Synchro materials");
        Check(converging.Location == CardLocation.Hand, "material projection never changes the actual hand card");
    }

    private static void ExtensionSearchCopies()
    {
        var f = new Fixture(); var a = ExtensionCard(35763582, CardLocation.Hand); var b = ExtensionCard(35763582, CardLocation.Hand);
        CoreAdd(f.Bot.Hand, a, b); var first = CoreInitial(f); var second = CoreInitial(f);
        CoreInvoke(Evaluation(f), "SpendComboCard", first, a, false, false);
        CoreInvoke(Evaluation(f), "SpendComboCard", second, b, false, false);
        var type = Evaluation(f).GetType(); var keys = Activator.CreateInstance(type.GetNestedType("DevelopmentKeys", BindingFlags.NonPublic), true);
        var method = type.GetMethod("DevelopmentStateKey", BindingFlags.NonPublic | BindingFlags.Static);
        Func<object, string> key = s => (string)method.Invoke(null, new[] { s, keys });
        Check(key(first) == key(second), "interchangeable physical copies share one search state");
        Check(key(first) != key(CoreInitial(f)), "state equivalence still preserves actual card counts and zones");
        Set(second, "Depth", 6); Check(key(first) != key(second), "a deeper route cannot dominate a shallower route with more horizon left");
        Set(second, "Depth", 0); ((HashSet<ClientCard>)CoreField(second, "ComboInstances")).Add(b);
        Check(key(first) != key(second), "different per-instance usage is never merged");
        Check(CoreCards(first, "Reserve").Single() == b && CoreCards(second, "Reserve").Single() == a,
            "equivalence never merges physical resources inside a branch");
    }

    private static void ExtensionLinkZones()
    {
        var f = new Fixture(); var sp = ExtensionCard(29301450); sp.Sequence = 5; f.Bot.MonsterZone[5] = sp;
        var target = Card(type: Monster | CardType.Link, level: 2, location: CardLocation.Extra, text: "2 Effect Monsters");
        var initial = CoreInitial(f);
        Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", initial, target, true, true) == -1,
            "a core-offered Link does not grant a zone when chosen materials leave the EMZ occupied and no arrows point down");
        var opponent = Card(type: Monster | CardType.Link, controller: 1, level: 2); opponent.Sequence = 5;
        Set(opponent, "LinkMarker", 128); f.Enemy.MonsterZone[5] = opponent;
        Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", CoreInitial(f), target, true, true) == 3,
            "a public opposing top arrow legitimately opens our main zone");
        initial = CoreInitial(f); CoreInvoke(Evaluation(f), "SpendComboCard", initial, sp, false, false);
        Check((int)CoreInvoke(Evaluation(f), "DevelopmentPlace", initial, target, true, false) == 5,
            "consuming the occupied EMZ body makes the next Link placement legal");
    }

    private static void ExtensionExtraTriggers()
    {
        var f = new Fixture(); var a = ExtensionCard(91188343); var b = Card();
        Set(b, "Race", (int)CardRace.Fairy); Set(b.Data, "Race", (int)CardRace.Fairy);
        f.Bot.MonsterZone[0] = a; f.Bot.MonsterZone[1] = b;
        var moon = ExtensionCard(90290572, CardLocation.Extra); f.Bot.ExtraDeck.Add(moon);
        var child = CoreStates(Evaluation(f), "ExtraSuccessors", CoreInitial(f), new[] { moon }, CoreBudget(f), false).Single();
        Check(((IEnumerable)CoreField(child, "PendingCombos")).Cast<object>().Count() == 1, "Moon queues its mill specifically on a Link Summon");
        var initial = CoreInitial(f); CoreInvoke(Evaluation(f), "QueueComboArrival", initial, moon, false);
        Check(!((IEnumerable)CoreField(initial, "PendingCombos")).Cast<object>().Any(), "reviving Moon does not invent a Link Summon trigger");
    }

    private static void ExtensionDance()
    {
        var f = new Fixture(); var dance = ExtensionCard(2344618, CardLocation.Hand); f.Bot.Hand.Add(dance);
        var hound = ExtensionCard(35763582, CardLocation.Deck); f.Bot.Deck.Add(hound);
        var placed = ComboRoots(f, CoreInitial(f), dance, 0).Single();
        var sent = ComboRoots(f, placed, dance, dance.Id * 16).Single();
        Check(CoreCards(sent, "Grave").Contains(hound) && CoreCards(sent, "PendingFusionMaterials").Contains(hound),
            "Dance activation enables a real deck send and Hound's sent-by-effect trigger");
        Check(ComboRoots(f, sent, dance, dance.Id * 16).Count == 0, "Dance's mill remains once per name");
        dance.Location = CardLocation.SpellZone; f.Bot.Hand.Clear(); f.Bot.SpellZone[2] = dance;
        Check(ComboRoots(f, CoreInitial(f), dance, dance.Id * 16).Count == 0, "an old continuous spell cannot mill outside its activation turn");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", dance, 0);
        CoreInvoke(Evaluation(f), "NoteComboResolution", dance, 0, false);
        Check(ComboRoots(f, CoreInitial(f), dance, dance.Id * 16).Count == 1, "activating Dance does not prematurely consume its separate mill effect");
        f.Duel.Turn++; f.Executor.OnNewTurn();
        Check(ComboRoots(f, CoreInitial(f), dance, dance.Id * 16).Count == 0, "Dance's activation-turn permission expires");
    }

    private static void ExtensionLockOrdering()
    {
        var f = new Fixture(); f.Duel.Turn = 1;
        var kristya = ExtensionCard(59509952, CardLocation.Hand); f.Bot.Hand.Add(kristya);
        for (int i = 0; i < 5; i++)
        {
            var card = Card(location: CardLocation.Grave); Set(card, "Race", (int)CardRace.Fairy); Set(card.Data, "Race", (int)CardRace.Fairy);
            f.Bot.Graveyard.Add(card);
            Check((ComboRoots(f, CoreInitial(f), kristya, direct: true).Count > 0) == (i == 3), "Kristya needs exactly four actual grave Fairies: " + (i + 1));
        }
        f.Bot.Graveyard.RemoveAt(4);
        var tuner = Card(100, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Hand);
        f.Bot.Hand.Add(tuner); f.Duel.MainPhase.SummonableCards.Add(tuner); f.Duel.MainPhase.SpecialSummonableCards.Add(kristya);
        f.Bot.MonsterZone[0] = Card(100, level: 8);
        var baronne = ExtensionCard(84815190, CardLocation.Extra); f.Bot.ExtraDeck.Add(baronne);
        Check(PlannedCard(MainSummonPlan(f)) == tuner, "build the Synchro before committing a symmetric summon lock");
        var blocked = ComboRoots(f, CoreInitial(f), kristya, direct: true).Single();
        var developed = CoreInvoke(Evaluation(f), "AddDevelopmentBody", blocked, tuner, true, false, CoreBudget(f));
        Check(CoreStates(Evaluation(f), "ExtraSuccessors", developed, new[] { baronne }, CoreBudget(f), false).Count == 0,
            "a projected Kristya blocks later extra summons even when the material levels fit");
        Check((bool)CoreInvoke(Evaluation(f), "DevelopmentSummonsBlocked", blocked),
            "Kristya also blocks simulated main-deck extenders and repeatable Venus summons");
        Check(f.Bot.Hand.Contains(kristya) && f.Bot.Graveyard.Count == 4, "lock-order planning leaves actual resources alone");

        var g = new Fixture(); var ip = Real(65741786); var goddess = ExtensionCard(4280258, CardLocation.Extra);
        g.Bot.MonsterZone[0] = ip; g.Bot.MonsterZone[1] = ExtensionCard(64734921); g.Bot.MonsterZone[2] = ExtensionCard(91188343);
        Check((bool)CoreInvoke(Evaluation(g), "CanQuickLink", CoreInitial(g), ip, goddess), "I:P and distinct partners have a legal future conversion");
        g.Bot.MonsterZone[3] = ExtensionCard(59509952);
        Check(!(bool)CoreInvoke(Evaluation(g), "CanQuickLink", CoreInitial(g), ip, goddess),
            "I:P cannot count a future Link Summon through its own Kristya, even by spending Kristya");
        g.Bot.MonsterZone[3] = null;
        g.Bot.MonsterZone[5] = Card(type: Monster | CardType.Link, level: 1);
        Set(g.Bot.MonsterZone[5], "LinkMarker", 0); Set(g.Bot.MonsterZone[5].Data, "Defense", 0);
        Set(g.Bot.MonsterZone[5].Data, "Description", "这张卡不能作为连接素材。");
        Check(!(bool)CoreInvoke(Evaluation(g), "CanQuickLink", CoreInitial(g), ip, goddess),
            "a future I:P conversion needs a legal zone after its selected materials leave");
    }

    private static void ExtensionResourceBanish()
    {
        foreach (var location in new[] { CardLocation.Hand, CardLocation.Grave, CardLocation.MonsterZone })
        {
            var f = new Fixture(); var hyperion = ExtensionCard(55794644, CardLocation.Hand); f.Bot.Hand.Add(hyperion);
            Check(ComboRoots(f, CoreInitial(f), hyperion, direct: true).Count == 0, "Hyperion cannot summon without its Agent cost");
            var agent = ExtensionCard(91188343, location);
            if (location == CardLocation.Hand) f.Bot.Hand.Add(agent);
            else if (location == CardLocation.Grave) f.Bot.Graveyard.Add(agent);
            else f.Bot.MonsterZone[0] = agent;
            var state = ComboRoots(f, CoreInitial(f), hyperion, direct: true).Single();
            Check(BodyCard(ComboBodies(state).Single()) == hyperion,
                "Hyperion pays its legal Agent cost from " + location);
            Check(!CoreCards(state, "Reserve").Contains(agent) && !CoreCards(state, "Grave").Contains(agent), "banished Agent cannot be recycled again");
        }
    }

    private static void ExtensionHolyWaterMode()
    {
        var f = new Fixture(); var water = ExtensionCard(26684111, CardLocation.Hand);
        var neptune = ExtensionCard(38529357, CardLocation.Deck);
        f.Bot.Hand.Add(water); f.Bot.Deck.Add(neptune);
        var searched = ComboRoots(f, CoreInitial(f), water, 0).Single();
        CoreInvoke(f.Executor, "CommitResourceRoute", CoreField(searched, "FirstSummon"));
        f.Chain(water, 0); f.Duel.SolvingChainIndex = 1;
        var unrelated = Card(); f.Executor.SetCard(ExecutorType.Activate, unrelated, 0);
        Check(f.AI.OnSelectOption(new[] { water.Id * 16, water.Id * 16 + 1 }) == 1,
            "Holy Water executes the planned search even after another offered effect changes Card");
        Check(f.AI.OnSelectOption(new[] { water.Id * 16 + 1, water.Id * 16 }) == 0,
            "Holy Water mode follows effect descriptor rather than a fixed option index");
        Check(f.AI.OnSelectCard(new[] { neptune }, 1, 1, HintMsg.AddToHand, false).Single() == neptune,
            "Holy Water mode and exact searched resource belong to the same route");
        CoreInvoke(f.Executor, "CommitResourceRoute", CoreField(searched, "FirstSummon"));
        f.Chain(unrelated, 0); f.Duel.SolvingChainIndex = 2;
        Check(f.Executor.OnSelectOption(new[] { water.Id * 16, water.Id * 16 + 1 }) == -1,
            "a route cannot select the mode of another resolving chain link");
        f.Duel.SolvingChainIndex = 1; f.Executor.OnChainEnd();
        Check(f.Executor.OnSelectOption(new[] { water.Id * 16, water.Id * 16 + 1 }) == -1,
            "a finished Holy Water route cannot affect a later prompt");
    }

    private static void ExtensionMoonMode()
    {
        var f = new Fixture(); var moon = ExtensionCard(90290572); f.Bot.MonsterZone[0] = moon;
        var neptune = ExtensionCard(38529357, CardLocation.Deck); f.Bot.Deck.Add(neptune);
        var route = CoreInvoke(Evaluation(f), "PlanComboAction", moon, moon.Id * 16, false, true);
        Check(route != null, "Moon has a concrete, legal mill route");
        CoreInvoke(f.Executor, "CommitResourceRoute", route);
        f.Chain(moon, 0); f.Duel.SolvingChainIndex = 1;
        Check(!f.AI.OnSelectYesNo(moon.Id * 16 + 2), "Moon retains its planned mill when Earth retrieval is optionally offered");
        Check(f.AI.OnSelectYesNo(26684111 * 16 + 2), "Moon's answer does not suppress an unrelated optional recovery");
        Check(f.AI.OnSelectCard(new[] { neptune }, 1, 1, HintMsg.ToGrave, false).Single() == neptune,
            "Moon's mill selection consumes the resource recorded by its route");
        CoreInvoke(f.Executor, "CommitResourceRoute", route);
        f.Chain(Card(), 1); f.Duel.SolvingChainIndex = 2;
        Check(f.AI.OnSelectYesNo(moon.Id * 16 + 2), "another chain link cannot consume Moon's decision");
        f.Duel.SolvingChainIndex = 1; f.Executor.OnChainEnd();
        Check(f.AI.OnSelectYesNo(moon.Id * 16 + 2), "an absent optional prompt leaves no queued no-answer behind");
    }
}
