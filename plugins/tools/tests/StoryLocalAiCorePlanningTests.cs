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
    private const BindingFlags CoreFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void CoreAdd<T>(IList<T> list, params T[] values) { foreach (var value in values) list.Add(value); }
    private static object CoreInvoke(object target, string method, params object[] args)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var member = type.GetMethod(method, CoreFlags);
            if (member != null) return member.Invoke(target, args);
        }
        throw new Exception("Missing core regression method: " + method);
    }
    private static object CoreField(object target, string field) => target.GetType().GetField(field, CoreFlags).GetValue(target);
    private static List<object> CoreStates(object target, string method, params object[] args) =>
        ((IEnumerable)CoreInvoke(target, method, args)).Cast<object>().ToList();
    private static object CoreInitial(Fixture f) => CoreInvoke(Evaluation(f), "InitialDevelopment", new object[] { new ClientCard[0] });
    private static object CoreBudget(Fixture f) => Activator.CreateInstance(Evaluation(f).GetType()
        .GetNestedType("SearchBudget", BindingFlags.NonPublic), true);
    private static List<ClientCard> CoreCards(object state, string field) => (List<ClientCard>)CoreField(state, field);
    private static List<object> CoreFusions(Fixture f, object state, ClientCard spell, bool root = false) =>
        CoreStates(Evaluation(f), "FusionChoices", state, spell, CoreBudget(f), root, 0, null);
    private static bool CoreMaterials(Fixture f, ClientCard destination, params ClientCard[] cards)
    {
        var eval = Evaluation(f); var recipe = CoreInvoke(eval, "MaterialRecipe", destination);
        return recipe != null && (bool)CoreInvoke(eval, "ValidMaterials", destination, recipe, cards.ToList(), null);
    }
    private static bool CoreEffect(Fixture f, ClientCard card, int description) =>
        (bool)CoreInvoke(f.Executor, "EffectAllowed", new object[] { card, description, null });

    private static void CorePlanningRegressions()
    {
        ComboPlanningRegressions();
        ExtensionPlanningRegressions();
        CoreFusionRecipes(); CoreFusionResources(); CoreFusionEvents();
        CoreXyzAndSynchro(); CoreTimingAndArtwork();
        Console.WriteLine("Local AI: native-core-discovered material, timing and resource regressions passed");
    }

    private static void CoreFusionRecipes()
    {
        var f = new Fixture();
        var cat = CoreCard(51777272, CardLocation.Extra);
        var lion = CoreCard(24550676, CardLocation.Extra);
        var goddess = CoreCard(54701958, CardLocation.Extra);
        var panther = CoreCard(97165977);
        var a = CoreCard(35763582, CardLocation.Hand); var b = CoreCard(35763582, CardLocation.Hand);
        var c = CoreCard(14152693, CardLocation.Hand);
        Check(CoreMaterials(f, cat, a, b), "repeated Fusion clause accepts two distinct same-name physical cards");
        Check(!CoreMaterials(f, cat, a) && !CoreMaterials(f, cat, a, b, c), "Fusion multiplication is an exact material count");
        Check(!CoreMaterials(f, cat, a, Card()), "archetype Fusion materials cannot use an unrelated body");
        Check(CoreMaterials(f, lion, panther, a, b), "named Fusion plus archetype times two needs all three materials");
        Check(!CoreMaterials(f, lion, cat, a, b), "named Fusion material is not interchangeable with another Fusion");
        Check(CoreMaterials(f, goddess, lion, a, b, c) && !CoreMaterials(f, goddess, lion, a, b),
            "named Fusion plus times three requires four physical materials");
        for (int count = 1; count <= 6; count++)
        {
            var cards = Enumerable.Range(0, count).Select(_ => CoreCard(35763582, CardLocation.Hand)).ToArray();
            Check(CoreMaterials(f, cat, cards) == (count == 2), "exact Fusion count property " + count);
        }
        f.Bot.Hand.Add(a); f.Bot.Hand.Add(b); f.Bot.ExtraDeck.Add(cat);
        Check(MainSummonPlan(f) == null, "having Fusion materials never invents a fusion spell");
        var poly = CoreCard(24094653, CardLocation.Hand); OfferResource(f, poly);
        var state = CoreInitial(f); var outcomes = CoreFusions(f, state, poly);
        Check(outcomes.Count == 1 && CoreCards(outcomes[0], "Grave").Contains(a) && CoreCards(outcomes[0], "Grave").Contains(b),
            "Polymerization consumes exactly its two physical materials and itself");
        Check(f.Bot.Hand.Count == 3 && f.Bot.ExtraDeck.Single() == cat && f.Bot.Graveyard.Count == 0,
            "Fusion branches do not mutate the live duel");
        CoreCards(state, "Reserve").Remove(poly);
        Check(CoreFusions(f, state, poly).Count == 0, "discarded fusion source cannot survive in stale action list");
        CoreCards(state, "Reserve").Add(poly);
        var selected = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(selected.Action == MainPhaseAction.MainAction.Activate,
            "actual woven main-phase policy activates a legal Poly route");
    }

    private static void CoreFusionResources()
    {
        var f = new Fixture(); var spell = CoreCard(87931906, CardLocation.Hand);
        var a = CoreCard(35763582, CardLocation.Hand); var deck = CoreCard(14152693, CardLocation.Deck);
        var cat = CoreCard(51777272, CardLocation.Extra);
        f.Bot.Hand.Add(spell); f.Bot.Hand.Add(a); f.Bot.Deck.Add(deck); f.Bot.ExtraDeck.Add(cat);
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "Lunalight Fusion cannot freely use deck materials turn one");
        var enemy = Card(controller: 1); f.Enemy.MonsterZone[0] = enemy;
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "ordinary opponent monster does not unlock external Fusion material");
        enemy.LastLocation = CardLocation.Extra;
        var state = CoreInitial(f); var outcomes = CoreFusions(f, state, spell);
        Check(outcomes.Count == 1 && CoreCards(outcomes[0], "Grave").Contains(deck),
            "visible opponent extra-deck arrival unlocks one Lunalight deck material");
        f.Bot.Hand.Remove(a); f.Bot.Deck.Add(CoreCard(35763582, CardLocation.Deck));
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "external Fusion material allowance is one, not two");
        f.Bot.Hand.Add(a);
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", spell, 0);
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "live Lunalight Fusion activation consumes the turn's name allowance");
        CoreInvoke(Evaluation(f), "ResetDevelopmentTurn");
        Check(CoreFusions(f, CoreInitial(f), spell).Count > 0, "Fusion name allowance resets on a new turn");

        f = new Fixture(); spell = CoreCard(1845204, CardLocation.Hand);
        var instant = CoreCard(3544583, CardLocation.Extra);
        f.Bot.Hand.Add(spell); f.Bot.ExtraDeck.Add(instant);
        f.Bot.LifePoints = 1000;
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "Instant Fusion cannot pay the final 1000 LP");
        f.Bot.LifePoints = 1001;
        outcomes = CoreFusions(f, CoreInitial(f), spell);
        Check(outcomes.Count == 1 && (int)CoreField(outcomes[0], "Life") == 1, "Instant Fusion pays exactly 1000 LP");
        var body = ((IEnumerable)CoreField(outcomes[0], "Board")).Cast<object>().Single();
        Check((bool)CoreField(body, "Temporary") && (bool)CoreField(body, "CannotAttack"),
            "Instant Fusion temporary body cannot be valued as a permanent attacker");
        Set(instant, "Level", 6);
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "Instant Fusion rejects level six");
        Set(instant, "Level", 2); Set(instant.Data, "Description", "通常怪兽×2\n这张卡用以上记的卡为融合素材的融合召唤才能特殊召唤。");
        Check(CoreFusions(f, CoreInitial(f), spell).Count == 0, "Instant Fusion respects printed material requirements");
    }

    private static void CoreFusionEvents()
    {
        var f = new Fixture(); var poly = CoreCard(24094653, CardLocation.Hand);
        var hound = CoreCard(35763582, CardLocation.Hand); var chick = CoreCard(35618217, CardLocation.Hand);
        var liger = CoreCard(81196066, CardLocation.Extra); var bird = CoreCard(14152693, CardLocation.Deck);
        CoreAdd(f.Bot.Hand, poly, hound, chick); f.Bot.ExtraDeck.Add(liger);
        f.Bot.Deck.Add(bird); f.Bot.Deck.Add(CoreCard(48444114, CardLocation.Deck));
        var fused = CoreFusions(f, CoreInitial(f), poly).Single();
        Check(CoreCards(fused, "PendingFusionMaterials").Count == 2 && CoreField(fused, "PendingSearch") == liger,
            "Fusion material events coexist with the Fusion monster's own search");
        var effects = CoreStates(Evaluation(f), "FusionMaterialSuccessors", fused, CoreBudget(f));
        Check(!((HashSet<int>)CoreField(effects[0], "Credited")).Contains(-35763582),
            "exploring a material trigger does not mutate its already-yielded decline branch");
        Check(effects.All(s => CoreField(s, "PendingSearch") == liger), "material events do not erase the queued Fusion Summon search");
        Check(CoreCards(fused, "PendingFusionMaterials").Count == 2, "event traversal preserves parent queue");

        f = new Fixture(); poly = CoreCard(24094653, CardLocation.Hand);
        var marten = CoreCard(50546208); hound = CoreCard(35763582, CardLocation.Hand);
        f.Bot.MonsterZone[0] = marten; CoreAdd(f.Bot.Hand, poly, hound);
        f.Bot.ExtraDeck.Add(CoreCard(51777272, CardLocation.Extra));
        CoreInvoke(Evaluation(f), "NoteResourceSummons", new[] { marten }, marten, 50546208 * 16);
        fused = CoreFusions(f, CoreInitial(f), poly).Single();
        Check(!CoreCards(fused, "Grave").Contains(marten) && !CoreCards(fused, "PendingFusionMaterials").Contains(marten),
            "self-revived Yellow Marten is banished and cannot create a fake grave search");
        CoreInvoke(Evaluation(f), "NoteResourceMove", marten, (int)CardLocation.MonsterZone, (int)CardLocation.Removed);
        Check(!((HashSet<ClientCard>)CoreField(Evaluation(f), "banishOnLeaveBodies")).Contains(marten),
            "leaving field clears the individual redirect ledger");
        hound.Location = CardLocation.Grave;
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", hound, hound.Id * 16 + 1);
        Check(!((HashSet<int>)CoreField(CoreInitial(f), "Credited")).Contains(-hound.Id), "Hound negate does not spend its recruit trigger");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", hound, hound.Id * 16);
        Check(((HashSet<int>)CoreField(CoreInitial(f), "Credited")).Contains(-hound.Id), "live Hound recruit cannot be replayed by forward search");
        CoreInvoke(Evaluation(f), "NoteResourceSummons", new[] { marten }, hound, 35763582 * 16);
        var locked = CoreInitial(f); var sp = CoreCard(29301450, CardLocation.Extra);
        Check(!(bool)CoreInvoke(Evaluation(f), "ExtraDestinationAllowed", locked, sp), "Silver Hound recruit's live archetype lock forbids generic Link conversion");
        Check((bool)CoreInvoke(Evaluation(f), "ExtraDestinationAllowed", locked, CoreCard(51777272, CardLocation.Extra)), "archetype lock permits Lunalight Fusion");
        CoreInvoke(Evaluation(f), "NoteResourceMove", marten, (int)CardLocation.MonsterZone, (int)CardLocation.Grave);
        Check((bool)CoreInvoke(Evaluation(f), "ExtraDestinationAllowed", CoreInitial(f), sp), "archetype lock ends when its recruited body leaves");
    }

    private static void CoreXyzAndSynchro()
    {
        var f = new Fixture(); var a = CoreCard(86331741); var b = CoreCard(45852939);
        var f0 = CoreCard(65305469, CardLocation.Extra); var draco = CoreCard(26973555, CardLocation.Extra);
        Check(CoreMaterials(f, f0, a, b), "Future accepts two non-Number Xyz with equal ranks");
        Set(b, "Level", 3); Check(!CoreMaterials(f, f0, a, b), "Future rejects different ranks"); Set(b, "Level", 4);
        Set(b.Data, "Name", "No.39 希望皇 霍普"); Check(!CoreMaterials(f, f0, a, b), "Future excludes Number monsters");
        Set(b.Data, "Name", "蚀之双子");
        f.Bot.MonsterZone[0] = a; f.Bot.MonsterZone[1] = b; CoreAdd(f.Bot.ExtraDeck, f0, draco);
        f.Duel.MainPhase.SpecialSummonableCards.Add(f0);
        Check(PlannedCard(MainSummonPlan(f)) == f0, "weak Future bridge is chosen for a real Draco Future continuation");
        Check(CoreMaterials(f, draco, f0), "Draco Future accepts its unconditional single-card overlay procedure");
        f.Bot.MonsterZone[0] = f0; f.Bot.MonsterZone[1] = null; f0.Location = CardLocation.MonsterZone;
        f0.Overlays.Add(a.Id); f0.Overlays.Add(b.Id);
        Check((int)CoreInvoke(Evaluation(f), "ProjectedOverlays", CoreInitial(f), draco, new List<ClientCard> { f0 }) == 3,
            "overlay upgrade transfers underlying materials instead of discarding them");
        CoreInvoke(Evaluation(f), "NoteCancelledExtra", draco);
        Check(!(bool)CoreInvoke(Evaluation(f), "ExtraNotCancelled", draco), "cancelled extra is not retried on identical state");
        f.Bot.Hand.Add(Card(location: CardLocation.Hand));
        Check((bool)CoreInvoke(Evaluation(f), "ExtraNotCancelled", draco), "a genuine state change allows a fresh extra attempt");

        f = new Fixture(); var converging = CoreCard(291414); var stardust = CoreCard(44508094);
        var two = Card(100, level: 2); var majestic = CoreCard(40939228, CardLocation.Extra);
        Check(CoreMaterials(f, majestic, converging, stardust, two), "field name substitution and required Dragon Synchro complete Majestic recipe");
        converging.Location = CardLocation.Hand;
        Check(!CoreMaterials(f, majestic, converging, stardust, two), "field/GY name substitution is not applied in hand");
        converging.Location = CardLocation.MonsterZone; Set(stardust, "Race", (int)CardRace.Warrior);
        Check(!CoreMaterials(f, majestic, converging, stardust, two), "Majestic requires an actual Dragon Synchro non-tuner");
    }

    private static void CoreTimingAndArtwork()
    {
        CoreCard(14558128, CardLocation.Hand); var ash = CoreCard(14558128, CardLocation.Hand);
        var f = new Fixture();
        Check(!f.Rule(ash, ExecutorType.SummonOrSet), "alternate-art Ash retains the hand-trap normal summon veto");
        f.Chain(Card(type: CardType.Spell, location: CardLocation.SpellZone), 0);
        Check(f.Respond(ash) == -1, "alternate-art Ash cannot negate our own chain");
        f = new Fixture(); var marten = CoreCard(50546208, CardLocation.Grave);
        f.Bot.MonsterZone[0] = CoreCard(81196066);
        Check(!CoreEffect(f, marten, marten.Id * 16), "Yellow Marten does not bounce a Fusion that cannot reach the hand");
        f.Bot.MonsterZone[1] = CoreCard(14152693);
        Check(CoreEffect(f, marten, marten.Id * 16), "Yellow Marten can return a main-deck Lunalight");
        var liger = CoreCard(81196066, CardLocation.Grave);
        Check(!CoreEffect(f, liger, 0), "Liger is not banished for an empty-board battle debuff");
        var redoer = CoreCard(55285840); redoer.Overlays.Add(marten.Id);
        Check(!CoreEffect(f, redoer, redoer.Id * 16 + 1), "monster-only Redoer stays on field during own Main Phase");
        f.Duel.ChainTargets.Add(redoer);
        Check(CoreEffect(f, redoer, redoer.Id * 16 + 1), "targeted Redoer may dodge removal");

        f = new Fixture(); var hound = CoreCard(35763582, CardLocation.Grave);
        f.Bot.Graveyard.Add(hound); f.Bot.MonsterZone[0] = CoreCard(81196066);
        // The core only offers this trigger with a legal recruit remaining.
        f.Bot.Deck.Add(CoreCard(14152693, CardLocation.Deck));
        var stale = Card(type: CardType.Spell, location: CardLocation.Hand, text: "Draw 2 cards."); OfferResource(f, stale);
        Check(f.AI.OnSelectChain(new[] { hound }, new[] { hound.Id * 16 }, new[] { false }) == 0,
            "summon-trigger window is not suppressed by an unrelated stale main-phase activation");
        // Chain-list and yes/no callbacks are alternative offers of this hard
        // once-per-turn effect, not two activations on one turn.
        f = new Fixture(); hound = CoreCard(35763582, CardLocation.Grave);
        f.Bot.Graveyard.Add(hound); f.Bot.MonsterZone[0] = CoreCard(81196066);
        f.Bot.Deck.Add(CoreCard(14152693, CardLocation.Deck)); OfferResource(f, stale);
        Check(f.AI.OnSelectEffectYn(hound, hound.Id * 16), "single-effect yes/no trigger uses the same correct timing as chain selection");
        Check(!(bool)typeof(MDPro3.Plugins.Features.StoryMode.StoryLuckyExecutor)
            .GetField("selectingChainResponse", CoreFlags).GetValue(f.Executor), "yes/no callback always clears response timing after return");
        var speeder = CoreCard(77075360); f.Bot.MonsterZone[0] = speeder;
        f.Duel.LastSummonedCards.Add(speeder);
        f.Bot.Deck.Add(CoreCard(63977008, CardLocation.Deck));
        Check(f.AI.OnSelectEffectYn(speeder, speeder.Id * 16), "Junk Speeder recruitment is not rejected by stale main-phase ordering");

        f = new Fixture(); var sheep = CoreCard(11317977, CardLocation.Hand); var poly = CoreCard(24094653, CardLocation.Deck);
        var a = CoreCard(35763582, CardLocation.Hand); var b = CoreCard(14152693, CardLocation.Hand);
        CoreAdd(f.Bot.Hand, sheep, a, b); f.Bot.Deck.Add(poly); f.Bot.ExtraDeck.Add(CoreCard(81196066, CardLocation.Extra));
        f.Bot.Deck.Add(CoreCard(48444114, CardLocation.Deck));
        f.Bot.Deck.Add(CoreCard(35618217, CardLocation.Deck));
        f.Bot.ExtraDeck.Add(CoreCard(51777272, CardLocation.Extra));
        CoreAdd(f.Duel.MainPhase.ActivableCards, sheep, sheep);
        CoreAdd(f.Duel.MainPhase.ActivableDescs, sheep.Id * 16, sheep.Id * 16 + 1);
        sheep.ActionActivateIndex[sheep.Id * 16] = 0; sheep.ActionActivateIndex[sheep.Id * 16 + 1] = 1;
        var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.Activate && action.Index == 1,
            "Black Sheep chooses Polymerization search rather than the unrelated recovery effect");
    }
}
