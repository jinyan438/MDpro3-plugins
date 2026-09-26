using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using MDPro3.Plugins.Features.StoryMode;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper;
using YGOSharp.OCGWrapper.Enums;
using Duel = WindBot.Game.Duel;

// Tests use the actual compiled WindBot and woven GameAI, with synthetic public board states.
// Only transport/dialog construction and card database loading are replaced. No network/Unity
// process is needed, and these fixtures never touch a real deck, config, save, or database.
internal sealed class TestStoryExecutor : StoryLuckyExecutor
{
    internal TestStoryExecutor(GameAI ai, Duel duel) : base(ai, duel) { }
}
internal sealed class TestDefaultExecutor : DefaultExecutor
{
    internal TestDefaultExecutor(GameAI ai, Duel duel) : base(ai, duel) { }
}
internal static partial class StoryLocalAiTests
{
    private static int checks, nextId = 90000000;
    private static readonly Dictionary<int, NamedCard> database = new Dictionary<int, NamedCard>();
    private const CardType Monster = CardType.Monster | CardType.Effect;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
    }
    private static void Set(object target, string name, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property?.GetSetMethod(true) != null) { property.SetValue(target, value); return; }
        }
        throw new Exception("Fixture field missing: " + name);
    }
    private static ClientCard Card(int attack = 1500, int defense = 1000, CardType type = Monster,
        CardLocation location = CardLocation.MonsterZone, int controller = 0, int id = 0,
        string text = "", int level = 4, CardPosition position = CardPosition.FaceUpAttack)
    {
        if (id == 0) id = nextId++;
        var data = (NamedCard)FormatterServices.GetUninitializedObject(typeof(NamedCard));
        Set(data, "Id", id); Set(data, "Type", (int)type); Set(data, "Level", level);
        Set(data, "Attack", attack); Set(data, "Defense", defense); Set(data, "Name", "Fixture " + id); Set(data, "Description", text);
        database[id] = data;
        var card = new ClientCard(id, location, 0, (int)position) { Controller = controller };
        Set(card, "Attack", attack); Set(card, "Defense", defense); Set(card, "Type", (int)type); Set(card, "Level", level);
        card.ActionIndex[(int)MainPhaseAction.MainAction.Summon] = id % 100;
        return card;
    }
    private sealed class Fixture
    {
        internal readonly Duel Duel;
        internal readonly GameAI AI;
        internal readonly TestStoryExecutor Executor;
        internal ClientField Bot => Duel.Fields[0];
        internal ClientField Enemy => Duel.Fields[1];
        internal Fixture(string deckFile = null)
        {
            Duel = new Duel { Turn = 2, Player = 0, Phase = DuelPhase.Main1, MainPhase = new MainPhase() };
            foreach (var field in Duel.Fields) { field.Init(30, 0); field.LifePoints = 8000; }
            AI = (GameAI)FormatterServices.GetUninitializedObject(typeof(GameAI));
            foreach (var field in typeof(GameAI).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                if (field.FieldType.IsGenericType)
                {
                    var generic = field.FieldType.GetGenericTypeDefinition();
                    if (generic == typeof(IList<>) || generic == typeof(List<>))
                        field.SetValue(AI, Activator.CreateInstance(typeof(List<>).MakeGenericType(field.FieldType.GetGenericArguments())));
                    if (generic == typeof(Dictionary<,>)) field.SetValue(AI, Activator.CreateInstance(field.FieldType));
                }
            foreach (string name in new[] { "m_selector_pointer", "m_option", "m_yesno", "m_number" }) Set(AI, name, -1);
            var game = (GameClient)FormatterServices.GetUninitializedObject(typeof(GameClient));
            game.DeckFile = deckFile;
            var dialogs = (Dialogs)FormatterServices.GetUninitializedObject(typeof(Dialogs));
            Set(dialogs, "_game", game); Set(AI, "_dialogs", dialogs); Set(AI, "Game", game); Set(AI, "Duel", Duel);
            Executor = new TestStoryExecutor(AI, Duel); AI.Executor = Executor;
        }
        internal void Chain(ClientCard card, int player)
        {
            Duel.CurrentChain.Add(card); Duel.CurrentChainInfo.Add(new ChainInfo(card, player, card.Id * 16)); Duel.LastChainPlayer = player;
        }
        internal int Respond(params ClientCard[] cards) => AI.OnSelectChain(cards, cards.Select(c => c.Id * 16).ToList(), cards.Select(c => false).ToList());
        internal bool Rule(ClientCard card, ExecutorType type)
        {
            Executor.SetCard(type, card, card.Id * 16);
            return Executor.Executors.Any(e => e.Type == type && (e.CardId == -1 || e.CardId == card.Id) && (e.Func == null || e.Func()));
        }
    }

    private static void Rules()
    {
        var f = new Fixture();
        var ash = Card(id: 14558127, location: CardLocation.Hand);
        Check(f.Respond(ash) == -1, "Ash is not spent without an enemy chain");
        f.Chain(Card(type: CardType.Spell, location: CardLocation.SpellZone), 0);
        Check(f.Respond(ash) == -1, "Ash never negates our own search");
        f = new Fixture();
        f.Chain(Card(id: 60600126, type: CardType.Spell, location: CardLocation.SpellZone, controller: 1), 1);
        Check(f.Respond(ash) == 0, "Ash answers Cyber Emergency instead of the legacy blacklist");
        f = new Fixture();
        f.Chain(Card(controller: 1), 1); f.Duel.NegatedChainIndexList.Add(1);
        Check(f.Respond(ash) == -1, "one-based negated chain is not interrupted twice");

        f = new Fixture();
        var veiler = Card(id: 97268402, location: CardLocation.Hand);
        var threat = Card(controller: 1, text: "Once per turn: add 1 card from your Deck to your hand.");
        f.Enemy.MonsterZone[0] = threat; f.Chain(threat, 1);
        Check(f.Respond(veiler) == 0, "Veiler answers a resolving monster effect");
        Check(f.AI.OnSelectCard(new[] { threat }, 1, 1, HintMsg.Disable, false).Single() == threat, "dedicated negation target survives generic selection");
        f = new Fixture();
        threat = Card(controller: 1); Set(threat, "Disabled", 1); f.Enemy.MonsterZone[0] = threat; f.Chain(threat, 1);
        Check(f.Respond(veiler) == -1, "do not spend Veiler on an already disabled monster");
        f = new Fixture();
        f.Bot.MonsterZone[0] = Card(); f.Chain(f.Bot.MonsterZone[0], 0);
        Check(f.Respond(veiler) == -1, "own monster never becomes the generic negate target");

        f = new Fixture();
        var called = Card(id: 24224830, type: CardType.Spell | CardType.QuickPlay, location: CardLocation.Hand);
        var graveAsh = Card(id: 14558127, location: CardLocation.Grave, controller: 1);
        f.Enemy.Graveyard.Add(graveAsh); f.Chain(graveAsh, 1);
        Check(f.Respond(called) == 0, "Called by the Grave protects our combo");
        Check(f.AI.OnSelectCard(new[] { graveAsh }, 1, 1, HintMsg.Remove, false).Single() == graveAsh, "Called by targets the matching grave monster");
        f = new Fixture(); f.Chain(Card(id: 14558127, location: CardLocation.Hand, controller: 1), 1);
        Check(f.Respond(called) == -1, "Called by requires a matching card actually in the graveyard");

        f = new Fixture();
        var darkHole = Card(id: 53129443, type: CardType.Spell, location: CardLocation.Hand);
        f.Bot.MonsterZone[0] = Card(3000, text: "Negate the activation."); f.Enemy.MonsterZone[0] = Card(500, controller: 1);
        Check(!f.Rule(darkHole, ExecutorType.Activate), "Dark Hole preserves a superior own board");
        f = new Fixture(); f.Enemy.MonsterZone[0] = Card(3000, controller: 1);
        Check(f.Rule(darkHole, ExecutorType.Activate), "Dark Hole clears a losing board");
        f = new Fixture();
        var exodia = Card(id: 33396948, location: CardLocation.Hand);
        Check(!f.Rule(exodia, ExecutorType.Summon) && !f.Rule(exodia, ExecutorType.SummonOrSet), "Exodia summon veto cannot be bypassed");
        Check(!f.Rule(Card(id: 14558127, location: CardLocation.Hand), ExecutorType.SummonOrSet), "hold hand traps on an empty turn-one board");

        f = new Fixture();
        var beatstick = Card(2000, location: CardLocation.Hand, type: CardType.Monster | CardType.Normal);
        var starter = Card(1200, location: CardLocation.Hand, text: "If this card is Normal Summoned: add 1 monster from your Deck to your hand.");
        f.Duel.MainPhase.SummonableCards.Add(beatstick); f.Duel.MainPhase.SummonableCards.Add(starter);
        Check(!f.Rule(beatstick, ExecutorType.SummonOrSet) && f.Rule(starter, ExecutorType.SummonOrSet), "normal summon prioritizes a starter over raw ATK");
        f.Enemy.MonsterZone[0] = Card(4000, controller: 1);
        Check(!f.Executor.OnSelectMonsterSummonOrSet(starter), "starter is not set just because the opponent is stronger");
        f = new Fixture();
        var highLevel = Card(2500, location: CardLocation.Hand, level: 12, type: CardType.Monster | CardType.Normal);
        f.Bot.MonsterZone[0] = Card(0, type: CardType.Monster | CardType.Token);
        f.Bot.MonsterZone[1] = Card(0, type: CardType.Monster | CardType.Token);
        Check(f.Rule(highLevel, ExecutorType.SummonOrSet), "level 12 ordinary tribute estimate is two, not four");

        f = new Fixture();
        var linkTwo = Card(1600, type: Monster | CardType.Link, location: CardLocation.Extra, level: 2,
            text: "2 Effect Monsters\nIf this card is Link Summoned: add 1 card from your Deck to your hand.");
        f.Bot.MonsterZone[0] = Card(1000, text: "Negate the activation.");
        f.Bot.MonsterZone[1] = Card(1000, text: "Negate the activation.");
        Check(!f.Rule(linkTwo, ExecutorType.SpSummon), "do not trade two live negates for an inferior Link-2");
        f.Bot.MonsterZone[0] = Card(300); f.Bot.MonsterZone[1] = Card(300);
        Check(f.Rule(linkTwo, ExecutorType.SpSummon), "allow a useful Link upgrade from weak materials");

        f = new Fixture();
        f.Bot.MonsterZone[0] = Card(2000); var extender = Card(1500, location: CardLocation.Hand);
        f.Chain(Card(id: 23434538, location: CardLocation.Grave, controller: 1), 1); f.Duel.SolvingChainIndex = 1;
        f.Executor.OnChainSolved(1);
        Check(!f.Rule(extender, ExecutorType.SpSummon), "stop unnecessary special summons under resolved Maxx C");
        f.Executor.OnNewTurn();
        Check(f.Rule(extender, ExecutorType.SpSummon), "draw pressure resets next turn");
        Check(f.Executor.OnSelectHand(), "generic strategy chooses first consistently");
        f = new Fixture(); f.Duel.Player = 1;
        var summoned = Card(3000, controller: 1); f.Enemy.MonsterZone[0] = summoned;
        f.Duel.LastSummonPlayer = 1; f.Duel.LastSummonedCards.Add(summoned);
        Check(f.Respond(Card(id: 52038441, location: CardLocation.Hand)) == 0, "Ghost Mourner handles its summon trigger without an existing chain");
        f.AI.OnSelectCard(new[] { summoned }, 1, 1, HintMsg.Disable, false);
        var unrelated = Card(controller: 1); f.Enemy.MonsterZone[1] = unrelated; f.Chain(unrelated, 1);
        Check(f.Respond(Card(id: 52038441, location: CardLocation.Hand)) == 0 &&
            f.AI.OnSelectCard(new[] { unrelated, summoned }, 1, 1, HintMsg.Disable, false).Single() == summoned,
            "Ghost Mourner keeps the summon-event target when another monster chains");
        f = new Fixture(); f.Duel.MainPhase.CanBattlePhase = true;
        f.Bot.Hand.Add(Card(id: 15693423, type: CardType.Trap, location: CardLocation.Hand));
        for (int i = 0; i < 3; i++) f.Enemy.MonsterZone[i] = Card(controller: 1);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.ToBattlePhase,
            "empty-field Evenly Matched line enters battle before committing cards");
        f = new Fixture();
        f.Enemy.SpellZone[0] = Card(id: 61740673, type: CardType.Trap | CardType.Continuous, location: CardLocation.SpellZone, controller: 1);
        var blockedSearch = Card(type: CardType.Spell, location: CardLocation.Hand, text: "Draw 2 cards, then add 1 card from your Deck to your hand.");
        var graveExtender = Card(location: CardLocation.Grave, text: "Special Summon this card.");
        foreach (var card in new[] { blockedSearch, graveExtender })
        {
            f.Duel.MainPhase.ActivableCards.Add(card); f.Duel.MainPhase.ActivableDescs.Add(card.Id * 16);
            card.ActionActivateIndex[card.Id * 16] = card == graveExtender ? 1 : 0;
        }
        var liveAction = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(liveAction.Action == MainPhaseAction.MainAction.Activate && liveAction.Index == 1,
            "a high-scoring spell suppressed by Imperial Order does not starve a live grave extender");
        Console.WriteLine("Local AI: action/chain regressions passed");
    }

    private static void Selection()
    {
        var f = new Fixture();
        var floodgate = Card(1000, id: 82732705, type: CardType.Trap | CardType.Continuous, location: CardLocation.SpellZone, controller: 1);
        var big = Card(3000, controller: 1); var own = Card(1000);
        Check(f.AI.OnSelectCard(new[] { big, own, floodgate }, 1, 1, HintMsg.Destroy, false).Single() == floodgate, "remove a floodgate before a vanilla body");
        var selected = f.AI.OnSelectCard(new[] { big, own, floodgate }, 1, 3, HintMsg.Destroy, false);
        Check(selected.Count == 2 && !selected.Contains(own), "up-to destruction does not destroy our own cards");
        var boss = Card(1000, text: "Once per turn: negate the activation.");
        var token = Card(1500, type: CardType.Monster | CardType.Token);
        selected = f.AI.OnSelectCard(new[] { boss, token }, 1, 2, HintMsg.LinkMaterial, false);
        Check(selected.Count == 1 && selected[0] == token, "use one token, preserve live interaction, do not overpay materials");
        Check(f.AI.OnSelectCard(new[] { boss, token }, 0, 1, HintMsg.LinkMaterial, true).Count == 0, "finish a legal incremental link selection");
        var ash = Card(0, id: 14558127, location: CardLocation.Hand);
        var brick = Card(1800, location: CardLocation.Hand, level: 8, type: CardType.Monster | CardType.Normal);
        Check(f.AI.OnSelectCard(new[] { ash, brick }, 1, 2, HintMsg.Discard, false).Single() == brick, "discard a brick before the hand trap");
        var exodia = Card(id: 33396948, location: CardLocation.Hand);
        Check(f.AI.OnSelectCard(new[] { exodia, brick }, 1, 1, HintMsg.Remove, false).Single() == brick, "protect Exodia pieces from costs");
        var mill = Card(location: CardLocation.Deck, text: "If this card is sent to the GY: Special Summon 1 monster.");
        var blank = Card(3000, location: CardLocation.Deck);
        Check(f.AI.OnSelectCard(new[] { blank, mill }, 1, 1, HintMsg.ToGrave, false).Single() == mill, "Foolish Burial selects a grave payoff");
        Check(f.AI.OnSelectCard(new[] { blank, mill }, 1, 2, HintMsg.ToGrave, false).Count == 1, "deck mill does not become a mandatory max selection");
        f.AI.SelectCard(boss);
        Check(f.Executor.OnSelectCard(new[] { boss, token }, 1, 1, HintMsg.LinkMaterial, false) == null, "preserve an explicitly queued material");
        Check(f.AI.OnSelectCard(new[] { boss, token }, 1, 1, HintMsg.LinkMaterial, false).Single() == boss, "queued material consumed by real GameAI");
        f.AI.SelectOption(1);
        Check(f.AI.OnSelectOption(new[] { 100, 101 }) == 1, "generic option callback preserves the queued option");
        var link = Card(type: Monster | CardType.Link);
        Check(f.Executor.OnSelectPosition(link.Id, new[] { CardPosition.FaceUpAttack }) == CardPosition.FaceUpAttack, "never select unavailable defense for a Link");
        var small = Card(0);
        Check(f.Executor.OnSelectPosition(small.Id, new[] { CardPosition.FaceDownDefence }) == CardPosition.FaceDownDefence, "position always belongs to offered positions");

        f = new Fixture();
        // This call runs through the woven production GameAI tribute entry point.
        Check(f.AI.OnSelectTribute(new[] { boss, token }, 1, 1, HintMsg.Release, false).Single() == token, "tribute hook preserves low-ATK interaction");
        f.AI.Executor = new TestDefaultExecutor(f.AI, f.Duel);
        Check(f.AI.OnSelectTribute(new[] { boss, token }, 1, 1, HintMsg.Release, false).Single() == boss, "non-story executor retains original tribute behavior");

        f = new Fixture();
        var hiddenA = Card(9999, id: 82732705, controller: 1, position: CardPosition.FaceDownDefence);
        var hiddenB = Card(0, controller: 1, position: CardPosition.FaceDownDefence);
        Check(f.AI.OnSelectCard(new[] { hiddenB, hiddenA }, 1, 1, HintMsg.Destroy, false).Single() == hiddenB, "hidden id/ATK/floodgate metadata does not affect target ranking");
        Console.WriteLine("Local AI: target/material/queue/privacy regressions passed");
    }

    private static bool HasSum(IList<ClientCard> cards, int target)
    {
        var reachable = new HashSet<int> { 0 };
        foreach (var card in cards) reachable = new HashSet<int>(reachable.SelectMany(n =>
            new[] { card.OpParam1, card.OpParam2 }.Where(w => w > 0).Select(w => n + w)));
        return reachable.Contains(target);
    }
    private static void SumsAndProperties()
    {
        var f = new Fixture();
        var a = Card(500); a.OpParam1 = 1; a.OpParam2 = 4;
        var b = Card(800); b.OpParam1 = 3; b.OpParam2 = 6;
        var result = f.Executor.OnSelectSum(new[] { a, b }, 7, 2, 2, HintMsg.SynchroMaterial, true);
        Check(result != null && result.Count == 2 && HasSum(result, 7), "mixed alternative levels solve exact synchro sum");
        var expensive = Card(3000, text: "Negate the activation."); expensive.OpParam1 = 7;
        var cheap = Card(0, type: CardType.Monster | CardType.Token); cheap.OpParam1 = 7;
        Check(f.Executor.OnSelectSum(new[] { expensive, cheap }, 7, 1, 1, HintMsg.SynchroMaterial, true).Single() == cheap, "sum solver chooses the lowest resource loss");
        Check(f.Executor.OnSelectSum(new[] { a }, 7, 1, 1, HintMsg.SynchroMaterial, true) == null, "impossible sum falls back without fabricating cards");
        Check(f.Executor.OnSelectSum(new[] { a }, 0, 0, 1, HintMsg.SynchroMaterial, true).Count == 0, "zero optional sum is legal");
        var random = new Random(731);
        for (int iteration = 0; iteration < 250; iteration++)
        {
            f = new Fixture();
            var cards = Enumerable.Range(0, random.Next(1, 9)).Select(_ => Card(random.Next(0, 3500), controller: random.Next(2))).ToArray();
            int min = random.Next(cards.Length + 1), max = random.Next(min, cards.Length + 1);
            int[] hints = { HintMsg.Destroy, HintMsg.Discard, HintMsg.AddToHand, HintMsg.LinkMaterial, HintMsg.Remove, HintMsg.ToGrave, HintMsg.Equip, HintMsg.Select };
            int hint = hints[iteration % hints.Length];
            result = f.AI.OnSelectCard(cards, min, max, hint, min == 0);
            Check(result.Count >= min && result.Count <= max && result.Distinct().Count() == result.Count && result.All(cards.Contains), "selection bounds/membership/uniqueness " + iteration);
            foreach (var card in cards) { card.OpParam1 = random.Next(1, 7); card.OpParam2 = random.Next(1, 10); }
            int target = random.Next(1, 20);
            result = f.Executor.OnSelectSum(cards, target, min, max, HintMsg.SynchroMaterial, true);
            bool exists = false;
            for (int mask = 0; mask < (1 << cards.Length); mask++)
            {
                var subset = cards.Where((c, i) => (mask & (1 << i)) != 0).ToList();
                if (subset.Count >= min && subset.Count <= max && HasSum(subset, target)) { exists = true; break; }
            }
            Check((result != null) == exists, "sum existence matches exhaustive independent oracle " + iteration);
            if (result != null) Check(result.Count >= min && result.Count <= max && result.Distinct().Count() == result.Count && HasSum(result, target), "sum legality " + iteration);
        }
        Console.WriteLine("Local AI: 250 seeded selection/sum property scenarios passed");
    }

    private static void Battles()
    {
        var f = new Fixture();
        f.Duel.Phase = DuelPhase.Battle; f.Duel.BattlePhase = new BattlePhase { CanMainPhaseTwo = true, CanEndPhase = true };
        ClientCard big = Card(3000), small = Card(1600);
        big.ActionIndex[1] = 0; small.ActionIndex[1] = 1;
        f.Bot.MonsterZone[0] = big; f.Bot.MonsterZone[1] = small;
        f.Enemy.MonsterZone[0] = Card(1000, 1500, controller: 1, position: CardPosition.FaceUpDefence);
        f.Enemy.LifePoints = 3000;
        f.Duel.BattlePhase.AttackableCards.Add(big); f.Duel.BattlePhase.AttackableCards.Add(small);
        var action = f.AI.OnSelectBattleCmd(f.Duel.BattlePhase);
        Check(action.Action == BattlePhaseAction.BattleAction.Attack && action.Index == 1, "small attacker clears blocker so big attacker can deliver lethal");
        Check(f.AI.OnSelectCard(new[] { f.Enemy.MonsterZone[0] }, 1, 1, HintMsg.AttackTarget, false).Single() == f.Enemy.MonsterZone[0], "battle plan queues its exact target");
        f = new Fixture();
        ClientCard weak = Card(1000), wall = Card(4000, controller: 1);
        f.Duel.BattlePhase = new BattlePhase { CanMainPhaseTwo = true };
        Check(f.Executor.OnBattle(new[] { weak }, new[] { wall }).Action == BattlePhaseAction.BattleAction.ToMainPhaseTwo, "do not suicide into an unbeatable monster");
        f.Bot.LifePoints = 100;
        var hidden = Card(0, controller: 1, position: CardPosition.FaceDownDefence);
        Check(f.Executor.OnBattle(new[] { Card(1500) }, new[] { hidden }).Action == BattlePhaseAction.BattleAction.ToMainPhaseTwo, "avoid a speculative facedown probe at critical LP");

        f = new Fixture(); f.Duel.BattlePhase = new BattlePhase { CanMainPhaseTwo = true };
        ClientCard equalA = Card(2000), equalD = Card(2000, controller: 1);
        equalA.RealPower = 37; equalD.RealPower = 42;
        f.Executor.OnBattle(new[] { equalA }, new[] { equalD });
        Check(equalA.RealPower == 37 && equalD.RealPower == 42, "search restores legacy battle scratch values");
        f = new Fixture(); f.Duel.BattlePhase = new BattlePhase { CanMainPhaseTwo = true };
        var attackers = Enumerable.Range(0, 7).Select(i => Card(1800 + i * 300)).ToArray();
        var defenders = Enumerable.Range(0, 7).Select(i => Card(1500 + i * 200, controller: 1)).ToArray();
        for (int i = 0; i < 7; i++) { attackers[i].ActionIndex[1] = i; f.Bot.MonsterZone[i] = attackers[i]; f.Enemy.MonsterZone[i] = defenders[i]; }
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) f.Executor.OnBattle(attackers, defenders);
        watch.Stop();
        Check(watch.ElapsedMilliseconds < 5000, "seven-by-seven bounded battle search completes in budget");
        Console.WriteLine("Local AI: battle ordering/safety passed; 10 full-board plans = " + watch.ElapsedMilliseconds + " ms");
    }

    private static int PlayBattle(int[] attacks, int[] enemyPowers, bool[] defense, int enemyLp, bool story)
    {
        var f = new Fixture();
        if (!story) f.AI.Executor = new TestDefaultExecutor(f.AI, f.Duel);
        f.Duel.Phase = DuelPhase.Battle;
        var battle = new BattlePhase { CanMainPhaseTwo = true, CanEndPhase = true }; f.Duel.BattlePhase = battle;
        f.Enemy.LifePoints = enemyLp;
        for (int i = 0; i < attacks.Length; i++)
        {
            var card = Card(attacks[i], type: CardType.Monster | CardType.Normal);
            card.ActionIndex[1] = i; f.Bot.MonsterZone[i] = card; battle.AttackableCards.Add(card);
        }
        for (int i = 0; i < enemyPowers.Length; i++) f.Enemy.MonsterZone[i] = Card(enemyPowers[i], enemyPowers[i],
            type: CardType.Monster | CardType.Normal, controller: 1, position: defense[i] ? CardPosition.FaceUpDefence : CardPosition.FaceUpAttack);
        while (battle.AttackableCards.Count > 0 && f.Enemy.LifePoints > 0 && f.Bot.LifePoints > 0)
        {
            var action = f.AI.OnSelectBattleCmd(battle);
            if (action.Action != BattlePhaseAction.BattleAction.Attack) break;
            var attacker = f.Bot.MonsterZone[action.Index];
            Check(attacker != null && battle.AttackableCards.Contains(attacker), "battle action names a legal unused attacker");
            if (attacker.ShouldDirectAttack) f.Enemy.LifePoints -= attacker.Attack;
            else
            {
                var defender = f.AI.OnSelectCard(f.Enemy.GetMonsters(), 1, 1, HintMsg.AttackTarget, false).Single();
                int difference = attacker.Attack - defender.GetDefensePower();
                if (defender.IsAttack())
                {
                    if (difference >= 0) f.Enemy.LifePoints -= difference;
                    else f.Bot.LifePoints += difference;
                    if (difference <= 0) f.Bot.MonsterZone[action.Index] = null;
                }
                else if (difference < 0) f.Bot.LifePoints += difference;
                if (difference > 0 || difference == 0 && defender.IsAttack() && attacker.Attack > 0)
                    f.Enemy.MonsterZone[Array.IndexOf(f.Enemy.MonsterZone, defender)] = null;
            }
            battle.AttackableCards.Remove(attacker);
        }
        return f.Enemy.LifePoints;
    }

    private static void BattleComparison()
    {
        int oldWins = 0, newWins = 0;
        var random = new Random(20260926);
        for (int i = 0; i < 200; i++)
        {
            int[] attackers, defenders; bool[] defense; int life;
            if (i < 40)
            {
                int small = random.Next(1400, 2200), big = small + random.Next(800, 1800);
                attackers = new[] { big, small }; defenders = new[] { small - 100 }; defense = new[] { true }; life = big;
            }
            else
            {
                attackers = Enumerable.Range(0, random.Next(2, 5)).Select(_ => random.Next(5, 36) * 100).ToArray();
                defenders = Enumerable.Range(0, random.Next(1, 4)).Select(_ => random.Next(5, 36) * 100).ToArray();
                defense = defenders.Select(_ => random.Next(2) == 0).ToArray(); life = random.Next(10, 61) * 100;
            }
            bool oldWin = PlayBattle(attackers, defenders, defense, life, false) <= 0;
            bool newWin = PlayBattle(attackers, defenders, defense, life, true) <= 0;
            if (oldWin) oldWins++; if (newWin) newWins++;
            Check(!oldWin || newWin, "preserve baseline lethal on paired public-board scenario " + i);
        }
        Check(newWins >= oldWins + 40, "battle planner recovers the 40 constructed missed-lethal lines");
        Console.WriteLine("Paired battle benchmark (200 public boards, no effects/hidden cards): legacy lethal " + oldWins + ", story lethal " + newWins);
    }

    private static void Main()
    {
        typeof(NamedCardsManager).GetField("_cards", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, database);
        DevelopmentRegressions(); SafetyRegressions(); Rules(); Selection(); SumsAndProperties(); Battles(); BattleComparison();
        Console.WriteLine("Story local AI tests: PASS (" + checks + " checks)");
    }
}
