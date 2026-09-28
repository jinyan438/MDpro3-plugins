using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static void ComboPlanningRegressions()
    {
        var f = new Fixture();
        var boss = Card(2400, type: Monster | CardType.Link, level: 4, id: 4280258);
        f.Bot.MonsterZone[5] = boss;
        var sprite = Card(400, 800, Monster | CardType.Tuner, CardLocation.Hand, id: 16638212, level: 1,
            text: "①：这张卡可以把自己场上1只表侧表示怪兽除外，从手卡特殊召唤。");
        f.Bot.Hand.Add(sprite); f.Duel.MainPhase.SpecialSummonableCards.Add(sprite);
        var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action != MainPhaseAction.MainAction.SpSummon,
            "a direct summon cannot banish the only working Apollousa for a lone Sprite");
        ComboCostsAndLimits(); ComboOverlayResources(); ComboTriggerProtocols(); ComboFusionNames(); ComboWheelSynchron(); ComboEffectLifetimes(); ComboLunarContinuations();
        Console.WriteLine("Local AI: costed combo planning regressions passed");
    }

    private static List<object> ComboRoots(Fixture f, object state, ClientCard card, int description = 0, bool direct = false) =>
        CoreStates(Evaluation(f), "ComboRoots", state, card, description, direct, CoreBudget(f));
    private static List<object> ComboBodies(object state) => ((IEnumerable)CoreField(state, "Board")).Cast<object>().ToList();
    private static ClientCard BodyCard(object body) => (ClientCard)CoreField(body, "Card");
    private static void ComboOffer(Fixture f, ClientCard card, int description, int index = 0)
    {
        f.Duel.MainPhase.ActivableCards.Add(card); f.Duel.MainPhase.ActivableDescs.Add(description); card.ActionActivateIndex[description] = index;
    }
    private static void ComboCostsAndLimits()
    {
        var f = new Fixture(); var jet = ComboCard(9742784, CardLocation.Grave); f.Bot.Graveyard.Add(jet);
        Check(ComboRoots(f, CoreInitial(f), jet).Count == 0, "Jet revival needs an actual hand cost");
        var cost = Card(location: CardLocation.Hand); f.Bot.Hand.Add(cost);
        var original = CoreInitial(f); var revived = ComboRoots(f, original, jet).Single();
        Check(!CoreCards(revived, "Reserve").Contains(cost) && CoreCards(revived, "Grave").Contains(cost) && !CoreCards(revived, "Grave").Contains(jet),
            "Jet revival pays its hand card exactly once and moves the real grave body");
        Check((bool)CoreField(ComboBodies(revived).Single(), "BanishOnLeave"), "Jet's leave-field banish is part of its body");
        Check(CoreCards(original, "Grave").Single() == jet && CoreCards(original, "Reserve").Contains(cost) && f.Bot.Hand.Contains(cost),
            "a costed branch cannot mutate its parent or live hand");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", jet, -1);
        Check(ComboRoots(f, CoreInitial(f), jet).Count == 0, "Jet material search and revival share their name limit");
        f.Executor.OnNewTurn(); Check(ComboRoots(f, CoreInitial(f), jet).Count > 0, "once-per-turn revival resets on the next turn");

        foreach (int life in new[] { 600, 700, 701, 8000 })
        {
            f = new Fixture(); var assault = ComboCard(77202120, CardLocation.Hand); f.Bot.Hand.Add(assault); f.Bot.LifePoints = life;
            var states = ComboRoots(f, CoreInitial(f), assault, assault.Id * 16);
            Check((states.Count > 0) == (life > 700), "Assault preserves survival at LP " + life);
            if (states.Count == 0) continue;
            var state = states.Single();
            Check((int)CoreField(state, "Life") == life - 700 && (bool)CoreField(ComboBodies(state).Single(), "SynchroOnly"), "Assault damage and attached lock are paid");
            Check(!(bool)CoreInvoke(Evaluation(f), "ExtraDestinationAllowed", state, ComboCard(88917691, CardLocation.Extra)) &&
                (bool)CoreInvoke(Evaluation(f), "ExtraDestinationAllowed", state, ComboCard(84815190, CardLocation.Extra)), "Assault permits only Synchro extra conversion");
            CoreInvoke(Evaluation(f), "SpendComboCard", state, assault, false, false);
            Check((bool)CoreInvoke(Evaluation(f), "ExtraDestinationAllowed", state, ComboCard(88917691, CardLocation.Extra)), "the body-bound lock ends after its body leaves");
        }

        f = new Fixture(); var converter = ComboCard(11069680, CardLocation.Hand); var tuner = ComboCard(9742784, CardLocation.Hand);
        var synchron = ComboCard(63977008, CardLocation.Deck); CoreAdd(f.Bot.Hand, converter, tuner); f.Bot.Deck.Add(synchron);
        original = CoreInitial(f); var searched = ComboRoots(f, original, converter, converter.Id * 16).Single();
        Check(CoreCards(searched, "Grave").Contains(converter) && CoreCards(searched, "Grave").Contains(tuner) &&
            !CoreCards(searched, "Reserve").Contains(converter) && !CoreCards(searched, "Reserve").Contains(tuner) &&
            ((HashSet<ClientCard>)CoreField(searched, "Acquired")).Contains(synchron), "Converter exchanges itself and an actual tuner for one actual deck card");
        Check(CoreCards(searched, "Reserve").Count(c => c == synchron) == 1 && (int)CoreField(searched, "DeckCount") == f.Bot.Deck.Count - 1,
            "a searched target is not duplicated or left in the deck");
        Check(ComboRoots(f, searched, converter, converter.Id * 16).Count == 0, "a consumed Converter cannot repeat from a stale source list");

        f = new Fixture(); var warrior = ComboCard(62880279, CardLocation.Hand); var dwarf = ComboCard(59724555, CardLocation.Deck);
        f.Bot.Hand.Add(warrior); f.Bot.Deck.Add(dwarf); f.Bot.MonsterZone[0] = ComboCard(55088578);
        var xyz = ComboCard(88917691, CardLocation.Extra); f.Bot.ExtraDeck.Add(xyz);
        var developed = ComboRoots(f, CoreInitial(f), warrior, warrior.Id * 16).Single();
        Check(CoreCards(developed, "Grave").Contains(dwarf) && (bool)CoreField(developed, "XyzOnly"), "Dodododo pays the deck send and applies its turn lock");
        var body = ComboBodies(developed).Single(b => BodyCard(b) == warrior);
        Check((int)CoreField(body, "ProjectedLevel") == 4 && (int)CoreField(body, "Attack") == 1800 && warrior.Level == 6,
            "branch-local level and ATK changes preserve printed/live stats");
        Check(CoreStates(Evaluation(f), "ExtraSuccessors", developed, new[] { xyz }, CoreBudget(f), false).Count > 0,
            "the level-four virtual warrior really enables a Rank 4 procedure");
        Check(!(bool)CoreInvoke(Evaluation(f), "ExtraDestinationAllowed", developed, ComboCard(84815190, CardLocation.Extra)), "an Xyz-only turn cannot invent a Synchro continuation");
    }

    private static void ComboOverlayResources()
    {
        var f = new Fixture(); var girl = ComboCard(88917691); girl.Overlays.Add(9491461); girl.Overlays.Add(55088578); f.Bot.MonsterZone[0] = girl;
        var coat = ComboCard(23720856, CardLocation.Deck); var dwarf = ComboCard(59724555, CardLocation.Deck);
        CoreAdd(f.Bot.Deck, coat, dwarf);
        var initial = CoreInitial(f); var children = ComboRoots(f, initial, girl, girl.Id * 16 + 1);
        Check(children.Count >= 2, "Girl compares concrete detach choices instead of treating materials as anonymous counters");
        foreach (var child in children)
        {
            var body = ComboBodies(child).Single();
            Check((int)CoreField(body, "OverlayCount") == 1 && ((List<ClientCard>)CoreField(body, "OverlayCards")).Count == 1,
                "each search spends exactly one attached material");
            Check(CoreCards(child, "Grave").Count == 1 && CoreCards(child, "Grave").Single().Id != girl.Id,
                "only the detached material enters the graveyard");
        }
        Check(girl.Overlays.Count == 2 && ((List<ClientCard>)CoreField(ComboBodies(initial).Single(), "OverlayCards")).Count == 2,
            "overlay branches never share mutable material lists");
        var knight = children.First(c => CoreCards(c, "Grave").Any(x => x.Id == 9491461));
        Check(((IEnumerable)CoreField(knight, "PendingCombos")).Cast<object>().Count() == 1, "detaching the knight schedules its verified search event");
        var resolved = CoreStates(Evaluation(f), "PendingComboSuccessors", knight, CoreBudget(f));
        Check(resolved.Any(s => ((HashSet<ClientCard>)CoreField(s, "Acquired")).Contains(dwarf)), "detach trigger acquires an actual Gogogo target");

        f = new Fixture(); var lizard = ComboCard(55088578); f.Bot.MonsterZone[0] = lizard;
        var hand = ComboCard(55088578, CardLocation.Hand); f.Bot.Hand.Add(hand);
        var state = ComboRoots(f, CoreInitial(f), hand, hand.Id * 16).Single();
        CoreInvoke(Evaluation(f), "SpendComboCard", state, hand, false, false);
        Check(!CoreCards(state, "Grave").Contains(hand), "a lizard removed from the field is banished instead of recycled as a grave extender");
    }

    private static void ComboTriggerProtocols()
    {
        var f = new Fixture(); var diviner = ComboCard(92919429); f.Bot.MonsterZone[0] = diviner; f.Duel.LastSummonedCards.Add(diviner);
        var trias = ComboCard(26866984, CardLocation.Deck); var sprite = ComboCard(16638212, CardLocation.Deck);
        var brick = Card(2800, level: 8, location: CardLocation.Deck); Set(brick, "Race", (int)CardRace.Fairy); Set(brick.Data, "Race", (int)CardRace.Fairy);
        CoreAdd(f.Bot.Deck, trias, sprite, brick); f.Bot.ExtraDeck.Add(ComboCard(84815190, CardLocation.Extra));
        CoreInvoke(Evaluation(f), "NoteNormalCommitment");
        Check(f.AI.OnSelectEffectYn(diviner, -1), "the native unspecified summon-trigger marker starts Diviner's real route");
        Check(f.AI.OnSelectCard(new[] { brick, trias, sprite }, 1, 1, HintMsg.ToGrave, false).Single() == trias,
            "Diviner sends the tribute extender instead of the largest arbitrary Fairy");
        Check(!(bool)CoreInvoke(Evaluation(f), "ModelledComboAction", diviner, -1), "an unspecified trigger marker never grants an ignition action");

        f = new Fixture(); var bird = ComboCard(14152693); f.Bot.MonsterZone[0] = bird; f.Duel.LastSummonedCards.Add(bird);
        var hound = ComboCard(35763582, CardLocation.Hand); var fusion = ComboCard(87931906, CardLocation.Hand);
        CoreAdd(f.Bot.Hand, hound, fusion);
        var terminal = Card(3000, type: Monster | CardType.Fusion, level: 8, location: CardLocation.Extra,
            text: "「月光」怪兽×2\nWhen your opponent activates a card or effect: negate the activation.");
        Set(terminal.Data, "Setcode", (long)0xdf); f.Bot.ExtraDeck.Add(terminal);
        CoreInvoke(Evaluation(f), "NoteNormalCommitment"); var usedHound = ComboCard(35763582, CardLocation.Grave);
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", usedHound, usedHound.Id * 16);
        Check(!f.AI.OnSelectEffectYn(bird, -1), "optional draw declines when it would discard the material or spell of the stronger fusion continuation");
        Check(f.Bot.Hand.Contains(fusion) && f.Bot.Hand.Contains(hound), "declining the optional draw leaves both fusion resources available");
    }

    private static void ComboFusionNames()
    {
        var f = new Fixture(); var chick = ComboCard(35618217); f.Bot.MonsterZone[0] = chick;
        for (int i = 1; i <= 3; i++) f.Bot.MonsterZone[i] = ComboCard(35763582);
        var lion = ComboCard(24550676, CardLocation.Extra); var goddess = ComboCard(54701958, CardLocation.Extra);
        CoreAdd(f.Bot.ExtraDeck, lion, goddess); var poly = ComboCard(24094653, CardLocation.Hand); f.Bot.Hand.Add(poly);
        var initial = CoreInitial(f);
        Check(!CoreFusions(f, initial, poly).Any(s => ComboBodies(s).Any(b => BodyCard(b) == goddess)), "four Lunalights alone cannot replace a named Lion Dancer");
        var states = ComboRoots(f, initial, chick, chick.Id * 16);
        var named = states.Single(s => (int)CoreField(ComboBodies(s).Single(b => BodyCard(b) == chick), "FusionName") == lion.Id);
        Check(CoreCards(named, "Grave").Contains(lion) && !CoreCards(named, "Extra").Contains(lion), "Kaleido name setup consumes its real extra-deck cost");
        Check(((IEnumerable)CoreField(named, "PendingFusionMaterials")).Cast<object>().Count() == 0, "name setup is a cost, never a sent-by-effect trigger");
        Check(CoreFusions(f, named, poly).Any(s => ComboBodies(s).Any(b => BodyCard(b) == goddess)), "the verified temporary name enables the exact four-material Goddess recipe");
        Check((int)CoreField(ComboBodies(initial).Single(b => BodyCard(b) == chick), "FusionName") == 0 && chick.Data.Name == "月光彩雏",
            "temporary Fusion names do not change the live database or sibling bodies");
        Check(ComboRoots(f, named, chick, chick.Id * 16).Count == 0, "soft once-per-turn name effect is spent while this body stays on field");
        CoreInvoke(Evaluation(f), "SpendComboCard", named, chick, false, true);
        Check(!((HashSet<ClientCard>)CoreField(named, "ComboInstances")).Contains(chick), "returning the body resets its instance limit");
        CoreInvoke(Evaluation(f), "NoteComboSelection", chick, lion, HintMsg.ToGrave);
        CoreInvoke(Evaluation(f), "NoteComboResolution", chick, chick.Id * 16, true);
        Check((int)CoreField(ComboBodies(CoreInitial(f)).First(), "FusionName") == 0, "a negated name effect does not grant an alias");
        CoreInvoke(Evaluation(f), "NoteComboSelection", chick, lion, HintMsg.ToGrave);
        CoreInvoke(Evaluation(f), "NoteComboResolution", chick, chick.Id * 16, false);
        Check((int)CoreField(ComboBodies(CoreInitial(f)).First(), "FusionName") == lion.Id, "successful live resolution installs the selected name");
        f.Executor.OnNewTurn();
        Check((int)CoreField(ComboBodies(CoreInitial(f)).First(), "FusionName") == 0, "the temporary fusion name expires at the next turn");
    }

    private static void ComboWheelSynchron()
    {
        var f = new Fixture(); var wheel = ComboCard(60283232); var junk = ComboCard(63977008);
        var stardust = ComboCard(44508094, CardLocation.Extra);
        Check(CoreMaterials(f, stardust, wheel, junk), "Wheel can be the non-Tuner beside a level-three Tuner for Stardust");
        Set(wheel, "Disabled", 1);
        Check(!CoreMaterials(f, stardust, wheel, junk), "disabled Wheel cannot turn two Tuners into a legal ordinary Synchro recipe");
        Set(wheel, "Disabled", 0); var nonTuner = Card(level: 3);
        Check(CoreMaterials(f, stardust, wheel, nonTuner), "flexible Wheel still works as the actual Tuner");
        Set(nonTuner, "Level", 4);
        Check(!CoreMaterials(f, stardust, wheel, nonTuner), "flexibility never bypasses the exact Synchro level total");

        f.Bot.MonsterZone[0] = wheel;
        var bird = ComboCard(14152693, CardLocation.Hand); f.Bot.Hand.Add(bird);
        CoreInvoke(Evaluation(f), "NoteNormalCommitment");
        var initial = CoreInitial(f); var summoned = ComboRoots(f, initial, wheel, wheel.Id * 16).Single();
        Check((bool)CoreField(summoned, "NormalUsed") && (bool)CoreField(summoned, "SynchroOnly"),
            "Wheel grants its effect summon after normal use without restoring the ordinary normal summon");
        Check(ComboBodies(summoned).Any(b => BodyCard(b) == bird) && !CoreCards(summoned, "Reserve").Contains(bird), "Wheel consumes the actual chosen hand body");
        Check(((IEnumerable)CoreField(summoned, "PendingCombos")).Cast<object>().Any(), "Wheel's normal summon still schedules a real summon trigger");
        Check(ComboRoots(f, summoned, wheel, wheel.Id * 16).Count == 0, "Wheel's extra normal effect is once per name");
        Check(ComboBodies(initial).Count == 1 && f.Bot.Hand.Contains(bird), "the extra-normal branch cannot alter live resources");

        f = new Fixture(); wheel = ComboCard(60283232, CardLocation.Grave); f.Bot.Graveyard.Add(wheel);
        var bridge = Card(1500, type: Monster | CardType.Synchro, level: 8); var tuner = Card(100, type: Monster | CardType.Tuner, level: 1);
        f.Bot.MonsterZone[0] = bridge; f.Bot.MonsterZone[1] = tuner;
        var boss = Card(3000, type: Monster | CardType.Synchro, level: 6, location: CardLocation.Extra,
            text: "1 Tuner + 1+ non-Tuner monsters\nOnce per turn, when your opponent activates a card or effect (Quick Effect): negate the activation.");
        f.Bot.ExtraDeck.Add(boss);
        initial = CoreInitial(f); var reduced = ComboRoots(f, initial, wheel, wheel.Id * 16 + 1);
        Check(reduced.Count == 4 && reduced.All(s => !CoreCards(s, "Grave").Contains(wheel)), "level reduction pays the grave banish for each of four concrete amounts");
        Check(reduced.All(s => ComboRoots(f, s, wheel, wheel.Id * 16 + 1).Count == 0), "a banished Wheel cannot repeat its grave effect");
        Check(reduced.Count(s => CoreStates(Evaluation(f), "ExtraSuccessors", s, new[] { boss }, CoreBudget(f), false).Count > 0) == 1,
            "only the exact three-level reduction opens the intended level-six Synchro");
        ComboOffer(f, wheel, wheel.Id * 16 + 1);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Activate, "the real main-phase callback activates the useful level adjustment");
        // The numeric choice belongs to resolution, after the target prompt.
        f.Duel.CurrentChainInfo.Add(new ChainInfo(wheel, 0, wheel.Id * 16 + 1)); f.Duel.SolvingChainIndex = 1;
        Check(f.AI.OnSelectCard(new[] { bridge }, 1, 1, HintMsg.Faceup, false).Single() == bridge &&
            f.AI.OnAnnounceNumber(new[] { 1, 2, 3, 4 }) == 2, "the target and announced amount execute the same planned route");
        f.Executor.OnChainEnd();
        Check((int)CoreField(f.AI, "m_number") == -1, "a resolved level choice cannot leak into an unrelated chain");
        Check(bridge.Level == 8 && f.Bot.Graveyard.Contains(wheel), "planning never applies the level or banish to live state");
        f.Executor.OnNewTurn();
        Set(bridge, "Level", 2);
        reduced = ComboRoots(f, CoreInitial(f), wheel, wheel.Id * 16 + 1);
        Check(reduced.Count == 1 && (int)CoreField(ComboBodies(reduced.Single()).First(), "ProjectedLevel") == 1,
            "level reduction cannot create a level-zero body");
    }

    private static void ComboEffectLifetimes()
    {
        var f = new Fixture(); var pickup = ComboCard(85119159, CardLocation.SpellZone); f.Bot.SpellZone[0] = pickup;
        Check((bool)CoreInvoke(Evaluation(f), "ModelledComboAction", pickup, 0) &&
            !(bool)CoreInvoke(Evaluation(f), "ModelledComboAction", pickup, pickup.Id * 16),
            "Onomatopickup's card activation search is distinct from its field level-alignment effect");
        var neptune = ComboCard(38529357, CardLocation.Hand); var venus = Card(id: 64734921); f.Bot.MonsterZone[0] = venus;
        CoreInvoke(Evaluation(f), "NoteResourceSummons", new[] { venus }, neptune, neptune.Id * 16);
        Check((bool)CoreField(ComboBodies(CoreInitial(f)).Single(), "CannotTribute"), "Neptune's revived body cannot be tributed this turn");
        f.Duel.Turn++; f.Duel.Player = 1; f.Executor.OnNewTurn();
        Check((bool)CoreField(ComboBodies(CoreInitial(f)).Single(), "CannotTribute"), "the tribute restriction survives until the opponent's end phase");
        f.Duel.Turn++; f.Duel.Player = 0; f.Executor.OnNewTurn();
        Check(!(bool)CoreField(ComboBodies(CoreInitial(f)).Single(), "CannotTribute"), "Neptune's restriction expires on the next own turn");
    }

    private static void ComboLunarContinuations()
    {
        var f = new Fixture(); var hound = ComboCard(35763582, CardLocation.Grave); f.Bot.Graveyard.Add(hound);
        f.Bot.Deck.Add(ComboCard(35618217, CardLocation.Deck));
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", hound, -1);
        var state = CoreInitial(f); CoreInvoke(Evaluation(f), "QueueSentByEffect", state, hound);
        var children = CoreStates(Evaluation(f), "FusionMaterialSuccessors", state, CoreBudget(f));
        Check(children.Count == 1 && ComboBodies(children.Single()).Count == 0,
            "native generic Silver Hound trigger consumes its name limit, so another send cannot recruit twice");

        f = new Fixture(); var liger = ComboCard(81196066); var chick = ComboCard(35618217);
        f.Bot.MonsterZone[0] = liger; f.Bot.MonsterZone[1] = chick;
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", chick, chick.Id * 16);
        CoreInvoke(Evaluation(f), "NoteResourceSummons", new[] { chick }, hound, hound.Id * 16);
        state = CoreInitial(f); var bounced = ComboRoots(f, state, liger, 0).Single();
        var body = ComboBodies(bounced).Single(b => BodyCard(b) == chick);
        Check(!(bool)CoreField(body, "EffectsBlocked") && (int)CoreField(body, "ExtraSetcode") == 0 &&
            !((HashSet<ClientCard>)CoreField(bounced, "ComboInstances")).Contains(chick),
            "Liger bounce and resummon resets the physical body's soft limit and attached extra restriction");
        var plan = CoreField(bounced, "FirstSummon");
        var choices = ((IEnumerable)CoreField(plan, "Choices")).Cast<object>().ToList();
        Check(choices.Count == 2 && (int)CoreField(choices[0], "Hint") == HintMsg.Select && (int)CoreField(choices[1], "Hint") == HintMsg.SpSummon,
            "Liger carries both the real return target and subsequent hand recruit selection");
        Check(ComboRoots(f, bounced, liger, 0).Count == 0, "Liger cannot repeat its bounce to create an infinite soft-limit reset");
        Check((int)CoreField(ComboBodies(state).Single(b => BodyCard(b) == chick), "ExtraSetcode") == 0xdf,
            "resetting a returned body does not clear the sibling branch's summon restriction");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", liger, 0);
        Check(!((HashSet<int>)CoreField(CoreInitial(f), "Credited")).Contains(liger.Id), "Liger bounce does not spend its independent Fusion-Summon search limit");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", liger, liger.Id * 16);
        Check(((HashSet<int>)CoreField(CoreInitial(f), "Credited")).Contains(liger.Id), "the actual Fusion-Summon search spends its own limit");

        f = new Fixture(); var marten = ComboCard(50546208, CardLocation.Hand); f.Bot.Hand.Add(marten);
        f.Bot.MonsterZone[0] = ComboCard(81196066);
        Check(ComboRoots(f, CoreInitial(f), marten, marten.Id * 16).Count == 0, "Marten cannot complete a required return to hand by returning a Fusion to Extra");

        f = new Fixture(); var goddess = ComboCard(54701958); f.Bot.MonsterZone[0] = goddess;
        Check(!(bool)CoreInvoke(Evaluation(f), "LiveInteraction", goddess), "Goddess with an empty Extra has no payable quick sweep");
        f.Bot.ExtraDeck.Add(ComboCard(51777272, CardLocation.Extra));
        Check((bool)CoreInvoke(Evaluation(f), "LiveInteraction", goddess), "an actual spare Lunalight funds Goddess's interaction");
        var normal = Card(controller: 1); f.Enemy.MonsterZone[0] = normal;
        Check(!CoreEffect(f, goddess, goddess.Id * 16 + 2), "Goddess does not spend a sweep on a Normal Summoned body");
        normal.IsSpecialSummoned = true;
        Check(CoreEffect(f, goddess, goddess.Id * 16 + 2), "Goddess can use its non-targeting sweep against a Special Summoned threat");

        f = new Fixture(); var neptune = ComboCard(38529357, CardLocation.Hand); f.Bot.Hand.Add(neptune);
        var handEarth = ComboCard(91188343, CardLocation.Hand); var graveEarth = ComboCard(91188343, CardLocation.Grave);
        f.Bot.Hand.Add(handEarth); f.Bot.Graveyard.Add(graveEarth);
        children = ComboRoots(f, CoreInitial(f), neptune, neptune.Id * 16);
        Check(children.Any(s => ComboBodies(s).Any(b => BodyCard(b) == handEarth)) &&
            children.Any(s => ComboBodies(s).Any(b => BodyCard(b) == graveEarth) && CoreCards(s, "Reserve").Contains(handEarth)),
            "same-name hand and grave targets remain distinct, so a revival can preserve the hand copy");

        f = new Fixture(); var leo = ComboCard(8379983); f.Bot.MonsterZone[0] = leo;
        var searchedHound = ComboCard(35763582, CardLocation.Deck); f.Bot.Deck.Add(searchedHound);
        var imperm = Card(type: CardType.Trap, location: CardLocation.Hand, id: 10045474); f.Bot.Hand.Add(imperm);
        var effect = ((IEnumerable)Evaluation(f).GetType().GetMethod("ComboProfiles", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { leo })).Cast<object>().Single();
        children = CoreStates(Evaluation(f), "ComboChoices", CoreInitial(f), leo, effect, CoreBudget(f), true, -1);
        var sendSearched = children.Single(s => CoreCards(s, "Grave").Contains(searchedHound));
        CoreInvoke(f.Executor, "CommitResourceRoute", CoreField(sendSearched, "FirstSummon"));
        Check(f.AI.OnSelectCard(new[] { searchedHound }, 1, 1, HintMsg.AddToHand, false).Single() == searchedHound,
            "Gold Leo executes the planned search first");
        f.Bot.Deck.Remove(searchedHound);
        var actualHandHound = ComboCard(35763582, CardLocation.Hand); f.Bot.Hand.Add(actualHandHound);
        Check(f.AI.OnSelectCard(new[] { imperm, actualHandHound }, 1, 1, HintMsg.Discard, false).Single() == actualHandHound,
            "the subsequent discard finds the searched card in its new zone instead of sacrificing Imperm");
    }
}
