using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using MDPro3.Plugins.Features.StoryMode;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static int RespondEffects(Fixture f, ClientCard[] cards, params int[] indices) =>
        f.AI.OnSelectChain(cards, cards.Select((c, i) => c.Id * 16 + indices[i]).ToList(), cards.Select(c => false).ToList());

    private static object Evaluate(Fixture f, string method, params object[] args)
    {
        var evaluation = typeof(StoryLuckyExecutor).GetField("evaluation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.Executor);
        return evaluation.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(evaluation, args);
    }

    private static void ResolveResource(Fixture f, int id, int player, bool negated = false)
    {
        f.Chain(Card(id: id, controller: player, location: CardLocation.Grave), player);
        int link = f.Duel.CurrentChain.Count; f.Duel.SolvingChainIndex = link;
        if (negated) f.Duel.NegatedChainIndexList.Add(link);
        f.Executor.OnChainSolved(link); f.Executor.OnChainEnd();
        f.Duel.CurrentChain.Clear(); f.Duel.CurrentChainInfo.Clear(); f.Duel.NegatedChainIndexList.Clear();
        f.Duel.LastChainPlayer = -1; f.Duel.SolvingChainIndex = 0;
    }

    private static float BattleScore(Fixture f, ClientCard[] attackers, ClientCard[] defenders)
    {
        var plan = typeof(StoryLuckyExecutor).GetMethod("PlanBattle", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(f.Executor, new object[] { attackers, defenders });
        return (float)plan.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(plan);
    }

    private static void InteractionRegressions()
    {
        var f = new Fixture(); f.Duel.Player = 1;
        var starter = Card(controller: 1, text: "If this card is Normal Summoned: add 1 monster from your Deck to your hand.");
        f.Enemy.MonsterZone[0] = starter; f.Chain(starter, 1);
        var ash = Card(id: 14558127, location: CardLocation.Hand);
        var veiler = Card(id: 97268402, location: CardLocation.Hand);
        Check(f.Respond(ash, veiler) == 1, "use the field-only, Main Phase hand trap before spending flexible Ash");
        Check(f.AI.OnSelectCard(new[] { starter }, 1, 1, HintMsg.Disable, false).Single() == starter,
            "ranked response still queues the correct target through the native selector");

        f = new Fixture(); f.Duel.Player = 1;
        starter = Card(controller: 1); f.Enemy.MonsterZone[0] = starter; f.Chain(starter, 1);
        var baronne = Real(84815190); var apollousa = Real(4280258); Set(apollousa, "Attack", 2400);
        f.Bot.MonsterZone[0] = baronne; f.Bot.MonsterZone[1] = apollousa;
        Check(RespondEffects(f, new[] { baronne, apollousa }, 1, 0) == 1,
            "monster-only reusable negate preserves Baronne's one-use omni negate");
        f = new Fixture(); f.Duel.Player = 1; f.Enemy.MonsterZone[0] = starter; f.Chain(starter, 1);
        f.Bot.MonsterZone[0] = baronne; f.Bot.MonsterZone[1] = apollousa;
        Check(RespondEffects(f, new[] { apollousa, baronne }, 0, 1) == 0, "response choice is independent of offered order");

        f = new Fixture(); f.Duel.Player = 1; f.Enemy.MonsterZone[0] = starter; f.Chain(starter, 1);
        Set(starter, "Disabled", 1);
        Check(f.Respond(ash, veiler) == -1, "all ineffective responses are declined");
        Set(starter, "Disabled", 0);
        f = new Fixture(); f.Duel.Player = 1;
        var immune = Card(controller: 1, text: "Cannot be targeted by card effects. If this card is Normal Summoned: add 1 monster from your Deck to your hand.");
        f.Enemy.MonsterZone[0] = immune; f.Chain(immune, 1);
        Check(f.Respond(veiler, ash) == 1, "an unavailable targeted answer never suppresses a usable hand trap");

        f = new Fixture();
        var removal = Card(text: "Once per turn: You can target 1 monster on the field; destroy it.");
        f.Bot.MonsterZone[0] = removal;
        f.Enemy.SpellZone[0] = Card(type: CardType.Trap, location: CardLocation.SpellZone, controller: 1);
        Check(!f.AI.OnSelectEffectYn(removal, removal.Id * 16), "enemy backrow cannot justify an effect that only destroys monsters");
        var monster = Card(2400, controller: 1); f.Enemy.MonsterZone[0] = monster;
        Check(f.AI.OnSelectEffectYn(removal, removal.Id * 16), "single-effect monster removal finds an appropriate target");
        Check(f.AI.OnSelectCard(new[] { removal, monster }, 1, 1, HintMsg.Destroy, false).Single() == monster,
            "generic removal selection preserves our own monster");

        f = new Fixture();
        var multi = Card(text: "①：这张卡召唤成功的场合才能发动。从卡组把1只怪兽加入手卡。\n②：以场上1张卡为对象才能发动。那张卡破坏。");
        f.Bot.MonsterZone[0] = multi;
        Check(f.AI.OnSelectEffectYn(multi, multi.Id * 16), "unknown multi-effect search is not vetoed by another paragraph's removal");
        var negate = Card(text: "When your opponent activates a monster effect (Quick Effect): You can negate the activation.");
        f.Chain(Card(), 0);
        Check(!f.AI.OnSelectEffectYn(negate, negate.Id * 16), "generic chain negation never responds to our own effect");
        f = new Fixture(); f.Duel.Player = 1; f.Chain(Card(controller: 1), 1);
        Check(f.AI.OnSelectEffectYn(negate, negate.Id * 16), "generic single-effect negation still answers an enemy effect");

        f = new Fixture();
        removal = Card(type: CardType.Spell, location: CardLocation.Hand,
            text: "Discard 1 card, then target 1 card on the field; destroy it.");
        f.Bot.Hand.Add(removal); f.Bot.Hand.Add(Card(id: 33396948, location: CardLocation.Hand));
        f.Enemy.MonsterZone[0] = Card(100, controller: 1);
        Check(!f.AI.OnSelectEffectYn(removal, removal.Id * 16), "do not discard Exodia to destroy a negligible target");

        f = new Fixture();
        f.Enemy.MonsterZone[0] = Card(3000, controller: 1, text: "Cannot be destroyed by card effects.");
        var raigeki = Card(id: 12580477, type: CardType.Spell, location: CardLocation.Hand);
        Check(!f.AI.OnSelectEffectYn(raigeki, raigeki.Id * 16), "Raigeki is conserved when every enemy monster resists destruction");

        f = new Fixture(); f.Duel.Player = 1;
        var maxx = Card(id: 23434538, location: CardLocation.Hand);
        f.Chain(maxx, 0);
        Check(f.Respond(Card(id: 23434538, location: CardLocation.Hand)) == -1, "never chain a second Maxx C to our own pending Maxx C");
        f.Duel.SolvingChainIndex = 1; f.Executor.OnChainSolved(1); f.Executor.OnChainEnd();
        f.Duel.CurrentChain.Clear(); f.Duel.CurrentChainInfo.Clear(); f.Duel.LastChainPlayer = -1;
        Check(f.Respond(Card(id: 23434538, location: CardLocation.Hand)) == -1, "resolved draw pressure is not duplicated later in the turn");

        f = new Fixture();
        f.Chain(Card(id: 42141493, location: CardLocation.Grave, controller: 1), 1);
        f.Duel.SolvingChainIndex = 1; f.Executor.OnChainSolved(1);
        f.Bot.MonsterZone[0] = Card(2000);
        var freeHandBody = Card(1500, location: CardLocation.Hand);
        Check(f.Rule(freeHandBody, ExecutorType.SpSummon), "Fuwalos does not tax a special summon from the hand");
        ResponseProtocolRegressions();
        ResourceStateRegressions();
        RemovalAndDamageRegressions();
        Console.WriteLine("Local AI: modern interaction/resource regressions passed");
    }

    private static void ResponseProtocolRegressions()
    {
        var f = new Fixture(); f.Duel.Player = 1;
        var enemy = Card(controller: 1); f.Enemy.MonsterZone[0] = enemy; f.Chain(enemy, 1);
        var ash = Card(id: 14558127, location: CardLocation.Hand);
        var veiler = Card(id: 97268402, location: CardLocation.Hand);
        Check(f.Respond(ash, veiler) == 1, "prefer the expiring narrow response");
        Check(f.AI.OnSelectEffectYn(ash, ash.Id * 16), "chain preference does not leak into a later yes/no prompt");
        f = new Fixture(); f.Duel.Player = 1; f.Enemy.MonsterZone[0] = enemy; f.Chain(enemy, 1);
        Check(f.AI.OnSelectChain(new[] { ash, veiler }, new[] { ash.Id * 16, veiler.Id * 16 }, new[] { true, false }) >= 0,
            "forced offers retain native response handling");
        f = new Fixture(); f.Duel.Player = 1; f.AI.Executor = new TestDefaultExecutor(f.AI, f.Duel);
        f.Chain(enemy, 1);
        Check(f.Respond(ash, veiler) == -1, "new chain hook leaves non-story executor behavior intact");

        f = new Fixture(); f.Duel.Player = 1; f.Enemy.MonsterZone[0] = enemy; f.Chain(enemy, 1);
        f.Duel.LastChainTargets.Add(ash);
        Check(f.Respond(ash, veiler) == 0, "use a threatened flexible resource before it is lost");

        f = new Fixture(); f.Duel.Player = 1;
        var b = Real(84815190); var savage = Real(27548199);
        f.Bot.MonsterZone[0] = b; f.Bot.MonsterZone[1] = savage;
        f.Chain(Card(type: CardType.Spell, location: CardLocation.SpellZone, controller: 1), 1);
        Check(RespondEffects(f, new[] { b, savage }, 1, 1) == 1, "renewable Savage negate preserves Baronne against a spell");
        f = new Fixture(); f.Duel.Player = 1;
        var apo = Real(4280258); Set(apo, "Attack", 2400);
        var spell = Card(type: CardType.Spell, location: CardLocation.SpellZone, controller: 1); f.Chain(spell, 1);
        Check(RespondEffects(f, new[] { apo, b }, 0, 1) == 1, "monster-only negate cannot displace an omni answer to a spell");

        f = new Fixture(); f.Duel.Player = 1; f.Enemy.MonsterZone[0] = enemy; f.Chain(enemy, 1);
        var impermFirst = Card(id: 10045474, type: CardType.Trap, location: CardLocation.Hand);
        Check(RespondEffects(f, new[] { b, impermFirst }, 1, 0) == 1,
            "a trap offered on the first callback is checked in its own context, not a null previous Card");
        f = new Fixture(); f.Duel.Player = 1; f.Enemy.MonsterZone[0] = enemy; f.Chain(enemy, 1);
        var ogre = Card(id: 59438930, location: CardLocation.Hand);
        Check(f.Respond(ogre, veiler) == 1, "a true negate precedes destruction that would let the search resolve");

        f = new Fixture();
        var low = Card(text: "When a Spell/Trap Card or monster effect is activated (Quick Effect): You can negate the activation.");
        f.Chain(spell, 1);
        Check(f.AI.OnSelectEffectYn(low, low.Id * 16), "generic omni negation is not misclassified as monster-only");

        f = new Fixture(); Set(f.Executor, "infiniteImpermanenceNegatedColumns", new List<int> { 0, 1, 2, 3, 4 });
        var handSpell = Card(type: CardType.Spell, location: CardLocation.Hand, text: "Draw 2 cards.");
        Check(!f.AI.OnSelectEffectYn(handSpell, handSpell.Id * 16), "do not activate a spell into an entirely negated backrow");
        var fieldSpell = Card(type: CardType.Spell | CardType.Field, location: CardLocation.Hand);
        Check(f.AI.OnSelectEffectYn(fieldSpell, fieldSpell.Id * 16), "field zone remains available when all main backrow columns are negated");
        var columnTrap = Card(type: CardType.Trap, location: CardLocation.SpellZone, position: CardPosition.FaceDownDefence);
        Check(!f.AI.OnSelectEffectYn(columnTrap, columnTrap.Id * 16), "known Impermanence column also blocks traps");
        f.Executor.OnNewTurn();
        Check(f.AI.OnSelectEffectYn(columnTrap, columnTrap.Id * 16), "column negation expires next turn");

        // Permute legal cards, include unusable targets and vary irrelevant hidden data.
        var random = new Random(20260927); var watch = Stopwatch.StartNew();
        for (int i = 0; i < 80; i++)
        {
            f = new Fixture(); f.Duel.Player = 1;
            enemy = Card(controller: 1, text: "If this card is Normal Summoned: add 1 monster from your Deck to your hand.");
            f.Enemy.MonsterZone[0] = enemy; f.Chain(enemy, 1);
            f.Enemy.Hand.Add(Card(random.Next(500, 9000), controller: 1, location: CardLocation.Hand,
                text: i % 2 == 0 ? "Negate every effect." : "Special Summon 5 monsters."));
            var imperm = Card(id: 10045474, type: CardType.Trap, location: CardLocation.Hand);
            var offered = new[] { ash, veiler, imperm }.OrderBy(_ => random.Next()).ToArray();
            int choice = f.Respond(offered);
            Check(choice >= 0 && offered[choice] == veiler, "response choice ignores candidate order and opponent hidden contents " + i);
        }
        Check(watch.ElapsedMilliseconds < 5000, "chain arbitration remains bounded across repeated full offers");
        Console.WriteLine("Modern response permutations: 80 boards, " + watch.ElapsedMilliseconds + " ms");
    }

    private static void ResourceStateRegressions()
    {
        var origins = new[] { CardLocation.Hand, CardLocation.Deck, CardLocation.Extra, CardLocation.Grave, CardLocation.Removed };
        var ids = new[] { 23434538, 84192580, 42141493, 87126721 };
        var expected = new[] { new[] { 1, 1, 1, 1, 1 }, new[] { 1, 0, 0, 0, 0 }, new[] { 0, 1, 1, 0, 0 }, new[] { 0, 0, 0, 1, 1 } };
        for (int i = 0; i < ids.Length; i++)
        {
            var taxed = new Fixture(); ResolveResource(taxed, ids[i], 1);
            for (int j = 0; j < origins.Length; j++)
                Check((int)Evaluate(taxed, "DrawTax", origins[j], false) == expected[i][j], "draw pressure matches actual summon origin " + ids[i] + "/" + origins[j]);
            Check((int)Evaluate(taxed, "DrawTax", CardLocation.Hand, true) == (ids[i] == 84192580 ? 1 : 0), "only Purulia taxes a normal summon");
            taxed.Executor.OnNewTurn();
            Check((int)Evaluate(taxed, "DrawTax", CardLocation.Extra, false) == 0, "draw pressure expires at the turn boundary");
        }
        var f = new Fixture(); ResolveResource(f, 42141493, 1); ResolveResource(f, 42141493, 1);
        Check((int)Evaluate(f, "DrawTax", CardLocation.Extra, false) == 2, "two resolved Fuwalos effects are counted separately");
        ResolveResource(f, 23434538, 1);
        Check((int)Evaluate(f, "DrawTax", CardLocation.Extra, false) == 3, "different resolved draw effects stack");
        ResolveResource(f, 94145021, 0);
        Check((int)Evaluate(f, "DrawTax", CardLocation.Extra, false) == 0, "Droll shuts off either player's draw pressure");
        f = new Fixture(); ResolveResource(f, 23434538, 1, true);
        Check((int)Evaluate(f, "DrawTax", CardLocation.Hand, false) == 0, "negated Maxx C creates no summon tax");

        f = new Fixture(); ResolveResource(f, 23434538, 1);
        f.Bot.MonsterZone[0] = Card(300, level: 3, type: Monster | CardType.Tuner);
        f.Bot.MonsterZone[1] = Card(300, level: 7);
        var baronne = Real(84815190, CardLocation.Extra); f.Bot.ExtraDeck.Add(baronne);
        f.Duel.MainPhase.SpecialSummonableCards.Add(baronne);
        Check(f.Rule(baronne, ExecutorType.SpSummon), "under Maxx C, finish the first meaningful interruption instead of passing on small bodies");
        Check(f.Bot.MonsterZone[0].Location == CardLocation.MonsterZone && f.Bot.Deck.Count == 30,
            "taxed planning does not mutate real resources");

        f = new Fixture(); ResolveResource(f, 23434538, 1);
        var venus = Card(1600, id: 64734921, text: "Pay 500 LP; Special Summon 1 Mystical Shine Ball from your hand or Deck.");
        f.Bot.MonsterZone[0] = venus; f.Bot.Deck.Clear();
        f.Bot.Deck.Add(Card(500, id: 39552864, location: CardLocation.Deck));
        f.Duel.MainPhase.ActivableCards.Add(venus); f.Duel.MainPhase.ActivableDescs.Add(venus.Id * 16);
        Check(!f.AI.OnSelectEffectYn(venus, venus.Id * 16), "repeatable summon effect does not buy a useless ball with LP and an opponent draw");
        var reborn = Card(id: 83764718, type: CardType.Spell, location: CardLocation.Hand);
        f.Bot.Graveyard.Add(Card(1200, location: CardLocation.Grave));
        Check(!f.AI.OnSelectEffectYn(reborn, reborn.Id * 16), "revival spells obey the same draw-pressure policy as direct summons");

        f = new Fixture(); var boss = Real(84815190); f.Bot.MonsterZone[0] = boss;
        Check((bool)Evaluate(f, "LiveInteraction", boss), "fresh Baronne counts as live interaction");
        f.Chain(Card(controller: 1), 1);
        Check(f.AI.OnSelectEffectYn(boss, boss.Id * 16 + 1), "Baronne spends its negate");
        Check(!(bool)Evaluate(f, "LiveInteraction", boss), "spent Baronne is not valued as an additional negate");
        f.Executor.OnNewTurn();
        Check(!(bool)Evaluate(f, "LiveInteraction", boss), "Baronne's once-faceup negate does not refresh next turn");
        f.Executor.OnMove(boss, 0, (int)CardLocation.MonsterZone, 0, (int)CardLocation.Grave);
        Check((bool)Evaluate(f, "LiveInteraction", boss), "leaving the field resets Baronne's faceup lifetime for a later revival");
        f = new Fixture(); boss = Real(27548199); f.Bot.MonsterZone[0] = boss; f.Chain(Card(controller: 1), 1);
        Check(f.AI.OnSelectEffectYn(boss, boss.Id * 16 + 1) && !(bool)Evaluate(f, "LiveInteraction", boss), "Savage's spent turn is reflected in board value");
        f.Executor.OnNewTurn();
        Check((bool)Evaluate(f, "LiveInteraction", boss), "Savage refreshes its once-per-turn interaction");
    }

    private static void RemovalAndDamageRegressions()
    {
        var f = new Fixture();
        var floodgate = Card(id: 82732705, type: CardType.Trap | CardType.Continuous, location: CardLocation.SpellZone, controller: 1);
        f.Enemy.SpellZone[0] = floodgate;
        var breaker = Card(type: CardType.Spell, location: CardLocation.Hand, text: "Target 1 card on the field; banish it.");
        var searchSpell = Card(type: CardType.Spell, location: CardLocation.Hand, text: "Add 1 monster from your Deck to your hand. Draw 1 card.");
        foreach (var c in new[] { searchSpell, breaker })
        {
            f.Duel.MainPhase.ActivableCards.Add(c); f.Duel.MainPhase.ActivableDescs.Add(c.Id * 16);
            c.ActionActivateIndex[c.Id * 16] = c == breaker ? 7 : 1;
        }
        var opening = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(opening.Action == MainPhaseAction.MainAction.Activate && opening.Index == 7,
            "remove a known floodgate before exposing generic development to it");
        f = new Fixture();
        var mst = Card(id: 5318639, type: CardType.Spell | CardType.QuickPlay, location: CardLocation.Hand);
        var engine = Card(type: CardType.Spell | CardType.Continuous, location: CardLocation.SpellZone, controller: 1,
            text: "Once per turn: add 1 monster from your Deck to your hand.");
        f.Enemy.SpellZone[0] = engine;
        Check(f.AI.OnSelectEffectYn(mst, mst.Id * 16), "MST removes a face-up resource engine on our turn");
        f = new Fixture(); f.Duel.Player = 1;
        var normal = Card(type: CardType.Spell, location: CardLocation.SpellZone, controller: 1); f.Enemy.SpellZone[0] = normal; f.Chain(normal, 1);
        Check(!f.AI.OnSelectEffectYn(mst, mst.Id * 16), "MST is not mistaken for a negate of an activated normal spell");
        f = new Fixture(); f.Duel.Player = 1; f.Enemy.SpellZone[0] = engine; f.Chain(engine, 1);
        Check(f.AI.OnSelectEffectYn(mst, mst.Id * 16), "MST can stop an activated continuous engine by removing it");

        f = new Fixture();
        var onlyFaceup = Card(text: "Once per turn: You can target 1 face-up monster on the field; banish it.");
        f.Bot.MonsterZone[0] = onlyFaceup;
        f.Enemy.MonsterZone[0] = Card(controller: 1, position: CardPosition.FaceDownDefence);
        Check(!f.AI.OnSelectEffectYn(onlyFaceup, onlyFaceup.Id * 16), "a hidden defender cannot justify face-up-only removal");
        var onlySpells = Card(text: "Once per turn: You can target 1 Spell/Trap your opponent controls; destroy it.");
        f.Enemy.MonsterZone[0] = Card(4000, controller: 1);
        Check(!f.AI.OnSelectEffectYn(onlySpells, onlySpells.Id * 16), "a monster cannot justify backrow-only removal");
        var targetedNegate = Card(text: "Once per turn (Quick Effect): You can target 1 face-up monster your opponent controls; negate its effects.");
        f.Duel.Player = 1; var searcher = f.Enemy.MonsterZone[0]; f.Chain(searcher, 1);
        Check(f.AI.OnSelectEffectYn(targetedNegate, targetedNegate.Id * 16), "generic targeted monster negation reacts to an active threat");
        var giant = Card(6000, controller: 1); f.Enemy.MonsterZone[1] = giant;
        Check(f.AI.OnSelectCard(new[] { giant, searcher }, 1, 1, HintMsg.Disable, false).Single() == searcher,
            "targeted negation selects the resolving effect over the largest body");

        f = new Fixture();
        var hiddenTrap = Card(id: 82732705, type: CardType.Trap | CardType.Continuous, location: CardLocation.SpellZone,
            controller: 1, position: CardPosition.FaceDownDefence, text: "Negate the activation.");
        Check(!(bool)Evaluate(f, "LiveInteraction", hiddenTrap), "face-down backrow is not identified through cached text or card ids");
        var bounce = Card(type: CardType.Spell, location: CardLocation.Hand, text: "Target 1 card on the field; return it to the hand.");
        var hiddenNormal = Card(controller: 1, position: CardPosition.FaceDownDefence);
        var hiddenFusion = Card(type: Monster | CardType.Fusion, controller: 1, position: CardPosition.FaceDownDefence);
        f.Enemy.MonsterZone[0] = hiddenNormal; f.Enemy.MonsterZone[1] = hiddenFusion;
        Check(f.AI.OnSelectEffectYn(bounce, bounce.Id * 16) &&
            f.AI.OnSelectCard(new[] { hiddenNormal, hiddenFusion }, 1, 1, HintMsg.ReturnToHand, false).Single() == hiddenNormal,
            "bounce scoring never reads a face-down defender's extra-deck identity");

        f = new Fixture();
        var effectOnly = Card(1500, location: CardLocation.Hand, text: "If this card is sent to the GY by a card effect: draw 1 card.");
        var anySend = Card(1500, location: CardLocation.Hand, text: "If this card is sent to the GY: draw 1 card.");
        Check(f.AI.OnSelectCard(new[] { effectOnly, anySend }, 1, 1, HintMsg.Discard, false).Single() == anySend,
            "discard costs prefer an actual trigger over an effect-only grave trigger");

        f = new Fixture(); f.Duel.Phase = DuelPhase.Battle; f.Enemy.LifePoints = 2500;
        var piercer = Card(3500, text: "If this card attacks a Defense Position monster, inflict piercing battle damage.");
        var wall = Card(1000, 1000, controller: 1, position: CardPosition.FaceUpDefence);
        Check(BattleScore(f, new[] { piercer }, new[] { wall }) >= 1000000, "plain piercing damage creates a real lethal line");
        Set(piercer, "Disabled", 1);
        Check(BattleScore(f, new[] { piercer }, new[] { wall }) < 1000000, "disabled piercing effect cannot create lethal");
        f = new Fixture(); f.Enemy.LifePoints = 3500;
        wall = Card(1000, 1000, controller: 1, position: CardPosition.FaceUpDefence, text: "Cannot be destroyed by battle.");
        Check(BattleScore(f, new[] { Card(2000), Card(3500) }, new[] { wall }) < 1000000,
            "battle-indestructible blockers are not removed to invent a direct lethal");
        f = new Fixture(); f.Duel.Phase = DuelPhase.Battle; f.Enemy.LifePoints = 1000;
        var attacker = Card(3000);
        Check(BattleScore(f, new[] { attacker }, new ClientCard[0]) >= 1000000, "unrestricted direct damage remains lethal");
        ResolveResource(f, 54693926, 0);
        Check(BattleScore(f, new[] { attacker }, new ClientCard[0]) == 0, "resolved Dark Ruler No More prevents false battle lethal");
        wall = Card(1000, controller: 1);
        float score = BattleScore(f, new[] { attacker }, new[] { wall });
        Check(score > 0 && score < 1000000, "under Dark Ruler, useful battle destruction still happens without damage");
        f.Executor.OnNewTurn();
        Check(BattleScore(f, new[] { attacker }, new ClientCard[0]) >= 1000000, "Dark Ruler damage lock expires next turn");
        f = new Fixture(); f.Enemy.LifePoints = 1000; ResolveResource(f, 54693926, 0, true);
        Check(BattleScore(f, new[] { attacker }, new ClientCard[0]) >= 1000000, "negated Dark Ruler does not lock damage");
    }
}
