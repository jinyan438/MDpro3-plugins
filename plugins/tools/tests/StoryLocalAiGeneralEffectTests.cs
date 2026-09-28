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
    private static object NewSemanticObject(Fixture f, string name) => Activator.CreateInstance(
        Evaluation(f).GetType().GetNestedType(name, BindingFlags.NonPublic), true);
    private static void RegisterSemantic(Fixture f, string field, int id, object value) =>
        ((IDictionary)Evaluation(f).GetType().GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))[id] = value;
    private static object GeneralSend(Fixture f, int offset, int key, bool soft)
    {
        var effect = NewSemanticObject(f, "ComboEffect");
        Set(effect, "From", CardLocation.MonsterZone); Set(effect, "TargetFrom", CardLocation.Deck);
        Set(effect, "Kind", Enum.Parse(effect.GetType().GetField("Kind", CoreFlags).FieldType, "Send"));
        Set(effect, "Offset", offset); Set(effect, "ExplicitDescription", true);
        Set(effect, "Once", !soft); Set(effect, "SoftOnce", soft); Set(effect, "InstanceKey", offset + 1); Set(effect, "KeyCode", key);
        Set(effect, "Filter", new Func<ClientCard, bool>(c => (c.Type & (int)CardType.Monster) != 0));
        return effect;
    }
    private static void RegisterCombos(Fixture f, ClientCard source, params object[] effects)
    {
        var array = Array.CreateInstance(effects[0].GetType(), effects.Length);
        for (int i = 0; i < effects.Length; i++) array.SetValue(effects[i], i);
        RegisterSemantic(f, "comboEffects", source.Id, array);
    }
    private static void GeneralEffectRegressions()
    {
        GeneralSoftEffects(); GeneralSharedEffects(); GeneralTacticalSemantics(); GeneralEffectZones(); GeneralArrival(); GeneralTributeNormal();
        Console.WriteLine("Local AI: anonymous semantic costs, independent/shared counts, resource conservation and effect-zone regressions passed");
    }
    private static void GeneralSoftEffects()
    {
        for (int sample = 0; sample < 16; sample++)
        {
            var f = new Fixture(); var source = Card(100, id: 91000100 + sample);
            f.Bot.Deck.Clear();
            f.Bot.MonsterZone[0] = source;
            var first = Card(100, location: CardLocation.Deck); var second = Card(100, location: CardLocation.Deck);
            f.Bot.Deck.Add(first); f.Bot.Deck.Add(second);
            RegisterCombos(f, source, GeneralSend(f, 0, 0, true), GeneralSend(f, 1, 0, true));
            var initial = CoreInitial(f); var sent = ComboRoots(f, initial, source, source.Id * 16).First();
            Check(ComboRoots(f, sent, source, source.Id * 16).Count == 0, "same soft effect cannot repeat " + sample);
            var next = ComboRoots(f, sent, source, source.Id * 16 + 1);
            Check(next.Count > 0, "another independent soft effect remains available " + sample);
            Check(CoreCards(next[0], "Grave").Count == 2 && CoreCards(next[0], "Reserve").Count == 0,
                "two transitions consume two distinct physical deck cards " + sample);
            Check(CoreCards(initial, "Grave").Count == 0 && f.Bot.Deck.Count == 2, "branches preserve parent and live resources " + sample);
            CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", source, source.Id * 16);
            Check(ComboRoots(f, CoreInitial(f), source, source.Id * 16).Count == 0, "live callback records selected soft effect " + sample);
            Check(ComboRoots(f, CoreInitial(f), source, source.Id * 16 + 1).Count > 0, "live callback preserves other soft effect " + sample);
            CoreInvoke(Evaluation(f), "NoteResourceMove", source, (int)CardLocation.MonsterZone, (int)CardLocation.Grave);
            Check(ComboRoots(f, CoreInitial(f), source, source.Id * 16).Count > 0, "leaving field resets the instance's effects " + sample);
        }
    }
    private static void GeneralSharedEffects()
    {
        var f = new Fixture(); var a = Card(100, id: 92000111); var b = Card(100, id: 92000112);
        f.Bot.Deck.Clear();
        f.Bot.MonsterZone[0] = a; f.Bot.MonsterZone[1] = b;
        f.Bot.Deck.Add(Card(100, location: CardLocation.Deck)); f.Bot.Deck.Add(Card(100, location: CardLocation.Deck));
        RegisterCombos(f, a, GeneralSend(f, 0, 990011, false)); RegisterCombos(f, b, GeneralSend(f, 0, 990011, false));
        var state = ComboRoots(f, CoreInitial(f), a, a.Id * 16).First();
        Check(ComboRoots(f, state, b, b.Id * 16).Count == 0, "different names can share the script's count key");
        CoreInvoke(Evaluation(f), "NoteDevelopmentEffect", a, a.Id * 16);
        Check(ComboRoots(f, CoreInitial(f), b, b.Id * 16).Count == 0, "live shared count uses script key rather than card name");
        f.Executor.OnNewTurn();
        Check(ComboRoots(f, CoreInitial(f), b, b.Id * 16).Count > 0, "turn reset restores shared count");
    }
    private static void GeneralTacticalSemantics()
    {
        var f = new Fixture(); var source = Card(type: CardType.Spell, location: CardLocation.Hand, id: 93000111);
        var fact = NewSemanticObject(f, "TacticalFact");
        Set(fact, "DefaultDescription", true); Set(fact, "Origin", 10); Set(fact, "Locations", 4);
        Set(fact, "Purpose", Enum.Parse(fact.GetType().GetField("Purpose", CoreFlags).FieldType, "TargetRemoval"));
        Set(fact, "Life", 600); Set(fact, "Hint", HintMsg.Destroy);
        Set(fact, "TargetFilter", new Func<ClientCard, bool>(c => c.Level == 3));
        var facts = NewSemanticObject(f, "CardFacts"); var array = Array.CreateInstance(fact.GetType(), 1); array.SetValue(fact, 0);
        Set(facts, "Effects", array); RegisterSemantic(f, "semanticFacts", source.Id, facts);
        f.Bot.LifePoints = 600; f.Enemy.MonsterZone[0] = Card(2000, level: 3, controller: 1);
        Check(!CoreEffect(f, source, 0), "anonymous effect cannot pay final LP");
        f.Bot.LifePoints = 601;
        Check(CoreEffect(f, source, 0), "anonymous effect accepts payable LP with eligible target");
        Set(f.Enemy.MonsterZone[0], "Level", 4);
        Check(!CoreEffect(f, source, 0), "semantic removal cannot select a target outside its filter");
        f = new Fixture(); var cosmic = Card(type: CardType.Spell, location: CardLocation.Hand, id: 8267140);
        var backrow = Card(type: CardType.Trap, location: CardLocation.SpellZone, controller: 1,
            position: CardPosition.FaceDownDefence); f.Enemy.SpellZone[0] = backrow;
        f.Bot.LifePoints = 1000;
        Check(!CoreEffect(f, cosmic, 0), "real script-derived LP cost uses same rule");
        f.Bot.LifePoints = 1001;
        Check(CoreEffect(f, cosmic, 0), "real same-family banish accepts an affordable legal target");
    }
    private static void GeneralEffectZones()
    {
        var f = new Fixture(); var snake = LevelCard(11234702, CardLocation.Grave); f.Bot.Graveyard.Add(snake);
        Check(!(bool)CoreInvoke(Evaluation(f), "ModelledComboTrigger", snake, snake.Id * 16),
            "grave ignition cannot be captured by same-number field trigger");
        var hound = CoreCard(35763582, CardLocation.Grave); f.Bot.Graveyard.Add(hound);
        Check(CoreInvoke(Evaluation(f), "PlanComboAction", hound, hound.Id * 16, false, true) == null,
            "a recruit trigger cannot manufacture a missing deck target");
        var g = GalaxyField(out _, out var soldier, 46659709, 58069384);
        Check(CoreInvoke(Evaluation(g), "PlanExtra", g.Bot.ExtraDeck[0], null) == null,
            "a modelled revival gets no speculative free-body bonus when its target is absent");
        Check(soldier.Level == 5, "declining an unprofitable conversion still preserves live levels");
    }
    private static void GeneralArrival()
    {
        var f = GalaxyField(out var source, out var soldier, 93717133, 63767246);
        f.Bot.MonsterZone[1] = null; soldier.Location = CardLocation.Hand; f.Bot.Hand.Add(soldier);
        f.Duel.LastSummonedCards.Clear(); f.Bot.Deck.Clear();
        var cost = Card(3000, 2500, CardType.Monster, CardLocation.Hand, id: 89631139, level: 8);
        Set(cost, "Attribute", (int)CardAttribute.Light); Set(cost.Data, "Attribute", (int)CardAttribute.Light); f.Bot.Hand.Add(cost);
        // This is a hand ignition that performs the Special Summon itself. It must
        // enter the unified planner through the core's activation offer, rather than
        // being represented as a normal summon candidate.
        ComboOffer(f, soldier, soldier.Id * 16);
        f.Duel.MainPhase.SpecialSummonableCards.Add(f.Bot.ExtraDeck[0]);
        var initial = CoreInitial(f); var roots = ComboRoots(f, initial, soldier, soldier.Id * 16);
        Check(roots.Count > 0, "payable special summon has a simulated root");
        Check(((IEnumerable)CoreField(roots[0], "PendingLevels")).Cast<object>().Count() == 1,
            "special summon queues another field card's level trigger");
        var levels = CoreStates(Evaluation(f), "PendingLevelSuccessors", roots[0], CoreBudget(f));
        var continued = levels.SelectMany(level => CoreStates(Evaluation(f), "ExtraSuccessors", level, f.Bot.ExtraDeck.ToArray(), CoreBudget(f), false)).ToList();
        Check(continued.Count > 0, "level-arrival branch exposes a legal extra-deck continuation");
        var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.Activate && action.Index == 0,
            "costed special summon anticipates a profitable level trigger and Xyz continuation instead of tributing the enabler");
        var held = GalaxyField(out _, out var heldSoldier, 93717133, 63767246);
        held.Bot.MonsterZone[1] = null; heldSoldier.Location = CardLocation.Hand; held.Bot.Hand.Add(heldSoldier);
        held.Duel.LastSummonedCards.Clear(); held.Bot.Deck.Clear();
        var handTrap = Card(3000, 600, Monster, CardLocation.Hand, id: 27204311, level: 11);
        Set(handTrap, "Attribute", (int)CardAttribute.Light); Set(handTrap.Data, "Attribute", (int)CardAttribute.Light); held.Bot.Hand.Add(handTrap);
        ComboOffer(held, heldSoldier, heldSoldier.Id * 16);
        held.Bot.ExtraDeck.Clear(); held.Duel.MainPhase.SpecialSummonableCards.Clear();
        var heldAction = held.AI.OnSelectIdleCmd(held.Duel.MainPhase);
        Check(heldAction.Action != MainPhaseAction.MainAction.Activate && heldAction.Action != MainPhaseAction.MainAction.Summon,
            "planner preserves a valuable hand trap and existing board when neither conversion repays its cost");
    }
    private static void GeneralTributeNormal()
    {
        var f = new Fixture();
        var tribute = Card(100, 100, Monster, id: 91000440, level: 4);
        var boss = Card(3000, 2500, Monster, CardLocation.Hand, id: 91000441, level: 5,
            text: "Quick Effect: When your opponent activates a card or effect: negate the activation.");
        f.Bot.MonsterZone[0] = tribute; f.Bot.Hand.Add(boss);
        f.Duel.MainPhase.SummonableCards.Add(boss);
        var plan = CoreInvoke(Evaluation(f), "PlanSummonActions", f.Duel.MainPhase.SummonableCards,
            new List<ClientCard>(), new KeyValuePair<ClientCard, int>[0]);
        Check(plan != null && (bool)CoreField(plan, "Normal") && (ClientCard)CoreField(plan, "Card") == boss,
            "profitable high-level normal summon is planned with its tribute cost");
        var selections = ((IEnumerable)CoreField(plan, "Choices")).Cast<object>().ToList();
        Check(selections.Count == 1 && (ClientCard)CoreField(selections[0], "Card") == tribute,
            "high-level normal route binds the actual tribute");
    }
}
