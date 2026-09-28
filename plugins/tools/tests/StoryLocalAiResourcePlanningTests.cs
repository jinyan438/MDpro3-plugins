using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using MDPro3.Plugins.Features.StoryMode;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

internal static partial class StoryLocalAiTests
{
    private static void OfferResource(Fixture f, ClientCard spell, int index = 0)
    {
        f.Bot.Hand.Add(spell);
        f.Duel.MainPhase.ActivableCards.Add(spell);
        f.Duel.MainPhase.ActivableDescs.Add(spell.Id * 16);
        spell.ActionActivateIndex[spell.Id * 16] = index;
    }

    private static ClientCard ResourceBoss(Fixture f, int level = 8)
    {
        var boss = Card(3000, type: Monster | CardType.Synchro, level: level, location: CardLocation.Extra,
            text: "1 Tuner + 1+ non-Tuner monsters\nWhen your opponent activates a card or effect: negate the activation.");
        f.Bot.ExtraDeck.Add(boss);
        return boss;
    }

    private static void ResourcePlanningRegressions()
    {
        // The Normal Summon is already spent. The useful search is the Level 6
        // extender, not another attractive Normal Summon starter.
        var f = new Fixture();
        f.Bot.MonsterZone[0] = Card(100, level: 2, type: Monster | CardType.Tuner);
        var extender = Card(300, level: 6, location: CardLocation.Deck,
            text: "If you control a Tuner monster, you can Special Summon this card (from your hand).");
        var stranded = Card(2300, location: CardLocation.Deck,
            text: "If this card is Normal Summoned: add 1 monster from your Deck to your hand.");
        ResourceBoss(f);
        var selected = f.AI.OnSelectCard(new[] { stranded, extender }, 1, 1, HintMsg.AddToHand, false);
        Check(selected.Single() == extender, "search follows a self-summoning extender after the Normal Summon is spent");
        f.Duel.MainPhase.SummonableCards.Add(Card(100, location: CardLocation.Hand));
        StoryLocalAiHooks.FinishMain(new MainPhaseAction(MainPhaseAction.MainAction.Summon), f.AI);
        Check(f.AI.OnSelectCard(new[] { stranded, extender }, 1, 1, HintMsg.AddToHand, false).Single() == extender,
            "a stale pre-summon main-phase list cannot recreate the spent Normal Summon during resolution");

        // A spell must not take away the empty-field window of a free body.
        f = new Fixture();
        var free = Card(400, level: 5, location: CardLocation.Hand,
            text: "If you control no monsters, you can Special Summon this card (from your hand).");
        var tuner = Card(100, level: 3, type: Monster | CardType.Tuner, location: CardLocation.Deck);
        var recruit = Card(type: CardType.Spell, location: CardLocation.Hand,
            text: "Special Summon 1 Level 3 Tuner monster from your Deck.");
        f.Bot.Hand.Add(free); f.Bot.Deck.Add(tuner); ResourceBoss(f);
        f.Duel.MainPhase.SpecialSummonableCards.Add(free);
        OfferResource(f, recruit);
        var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.SpSummon,
            "empty-field extender precedes a recruiting spell that would close its summon window");

        f = new Fixture();
        f.Bot.MonsterZone[0] = Card(100, level: 7);
        tuner = Card(100, level: 1, type: Monster | CardType.Tuner, location: CardLocation.Grave);
        var beater = Card(2800, level: 6, location: CardLocation.Grave);
        f.Bot.Graveyard.Add(tuner); f.Bot.Graveyard.Add(beater); ResourceBoss(f);
        var reborn = Card(id: 83764718, type: CardType.Spell, location: CardLocation.Hand);
        OfferResource(f, reborn);
        action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(action.Action == MainPhaseAction.MainAction.Activate, "revival starts the available Synchro route");
        Check(f.AI.OnSelectCard(new[] { beater, tuner }, 1, 1, HintMsg.SpSummon, false).Single() == tuner,
            "Monster Reborn revives the missing tuner instead of choosing raw ATK");
        f.Executor.OnChainEnd();
        f.Bot.ExtraDeck.Clear(); f.Bot.ExtraDeck.Add(Real(84815190, CardLocation.Extra));
        Set(f.Bot.MonsterZone[0], "Level", 9);
        var enemyBoss = Real(84815190, CardLocation.Grave, 1); Set(enemyBoss, "ProcCompleted", 1);
        f.Enemy.Graveyard.Add(enemyBoss);
        Check(f.AI.OnSelectIdleCmd(f.Duel.MainPhase).Action == MainPhaseAction.MainAction.Activate,
            "a valuable opposing revival remains available during resource planning");
        Check(f.AI.OnSelectCard(new[] { tuner, enemyBoss }, 1, 1, HintMsg.SpSummon, false).Single() == enemyBoss,
            "reviving an opposing terminal can beat spending our bodies to make the same terminal");
        ResourceParsingAndSafety();
        SearchTriggerRoutes();
        ResourceSequenceOracle();
        StructuredMaterialRegressions();
        TerminalResourceRegressions();
        ResourcePlanningStress();
        Console.WriteLine("Local AI: resource-to-summon planning regressions passed");
    }

    private static object FullResourcePlan(Fixture f) => typeof(StoryLuckyExecutor)
        .GetMethod("MainSummonRoute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.Executor, new object[0]);

    private static bool ModelledResource(Fixture f, ClientCard card, int? description = null) => (bool)Evaluation(f).GetType()
        .GetMethod("ModelledResourceAction", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(Evaluation(f), new object[] { card, description ?? card.Id * 16 });

    private static void ResourceParsingAndSafety()
    {
        var f = new Fixture();
        foreach (string text in new[] {
            "①：从卡组把1只4星以下的战士族怪兽加入手卡。",
            "①：从卡组把1只6星以下的恐龙族怪兽加入手卡。",
            "①：从手卡·卡组把1只3星以下的念动力族怪兽特殊召唤。这个效果特殊召唤的怪兽在这个回合的结束阶段除外。",
            "Add 1 Level 4 or lower Warrior monster from your Deck to your hand.",
            "Special Summon 1 Level 3 or lower Psychic monster from your hand or Deck. During the End Phase of this turn, banish that monster.",
            "You can only activate 1 \"Test\" per turn. Add 1 Level 4 monster from your Deck to your hand." })
        {
            var spell = Card(type: CardType.Spell, location: CardLocation.Hand, text: text);
            Check(ModelledResource(f, spell), "complete resource clause accepted: " + text);
            Check(ModelledResource(f, spell, 0), "normal spell description zero is supported");
            Check(!ModelledResource(f, spell, spell.Id * 16 + 1), "another effect index is never mistaken for the modelled spell");
        }
        foreach (string text in new[] {
            "Discard 1 card; Special Summon 1 monster from your Deck.",
            "Special Summon 1 monster from your Deck. You cannot Special Summon for the rest of this turn, except Dragons.",
            "①：从卡组把1只怪兽加入手卡。\n②：从手卡把1只怪兽特殊召唤。",
            "从手卡把2只怪兽送去墓地才能发动。从卡组把1只怪兽特殊召唤。",
            "Add 1 Spell from your Deck to your hand.",
            "Add 1 monster from your Deck to your hand, then discard 1 card.",
            "Special Summon 1 monster from your Deck, but you cannot conduct your Battle Phase this turn.",
            "If you control exactly 2 monsters: Special Summon 1 monster from your Deck." })
            Check(!ModelledResource(f, Card(type: CardType.Spell, location: CardLocation.Hand, text: text)),
                "unmodelled costs, timing, locks and follow-up effects stay outside the search: " + text);

        f = new Fixture();
        var field = Card(100, level: 4, type: Monster | CardType.Tuner); f.Bot.MonsterZone[0] = field;
        var spellSearch = Card(type: CardType.Spell, location: CardLocation.Hand,
            text: "①：从卡组把1只4星以下的战士族怪兽加入手卡。");
        var warrior = Card(100, location: CardLocation.Deck, level: 4);
        Set(warrior, "Race", 1); Set(warrior.Data, "Race", 1);
        var dragon = Card(2500, location: CardLocation.Deck, level: 4);
        Set(dragon, "Race", 8192); Set(dragon.Data, "Race", 8192);
        f.Bot.Deck.Add(warrior); f.Bot.Deck.Add(dragon); ResourceBoss(f); OfferResource(f, spellSearch);
        Check(PlannedCard(FullResourcePlan(f)) == spellSearch, "search into an unused Normal Summon works even with no monsters initially in hand");
        int handCount = f.Bot.Hand.Count, deckCount = f.Bot.Deck.Count;
        f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
        Check(f.AI.OnSelectCard(new[] { dragon, warrior }, 1, 1, HintMsg.AddToHand, false).Single() == warrior,
            "search execution preserves the actual planned race-qualified target");
        Check(f.Bot.Hand.Count == handCount && f.Bot.Deck.Count == deckCount && warrior.Location == CardLocation.Deck,
            "virtual acquisition leaves the live hand, deck and card location unchanged");
        f.Executor.OnChainEnd();
        StoryLocalAiHooks.FinishMain(new MainPhaseAction(MainPhaseAction.MainAction.Summon), f.AI);
        Check(FullResourcePlan(f) == null, "committed Normal Summon cannot be spent again after a search");
        f.Executor.OnNewTurn();
        Check(FullResourcePlan(f) != null, "Normal Summon allowance recovers on the next turn");
        f.Bot.Deck.Remove(warrior);
        Check(FullResourcePlan(f) == null, "wrong-race target cannot stand in for an unavailable searched monster");
        f.Bot.Deck.Add(warrior);
        typeof(StoryLuckyExecutor).GetField("resourceSelection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(f.Executor, null);
        var eval = Evaluation(f);
        eval.GetType().GetMethod("NoteResolvedResourceEffect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(eval, new object[] { 94145021, 1 });
        Check(FullResourcePlan(f) == null, "Droll prevents the planner from inventing a search");

        var ordinary = new Fixture(); ordinary.AI.Executor = new TestDefaultExecutor(ordinary.AI, ordinary.Duel);
        var action = new MainPhaseAction(MainPhaseAction.MainAction.Summon);
        Check(StoryLocalAiHooks.FinishMain(action, ordinary.AI) == action,
            "main-phase bookkeeping preserves non-story action objects");
        CostedRecruitRoutes();
    }

    private static void CostedRecruitRoutes()
    {
        foreach (bool chinese in new[] { false, true })
        {
            var f = new Fixture();
            var oneForOne = Card(type: CardType.Spell, location: CardLocation.Hand, text: chinese
                ? "①：从手卡把1只怪兽送去墓地才能发动。从手卡·卡组把1只1星怪兽特殊召唤。"
                : "Send 1 monster from your hand to the GY; Special Summon 1 Level 1 monster from your hand or Deck.");
            var extender = Card(100, level: 7, location: CardLocation.Hand,
                text: "If you control a Tuner monster, you can Special Summon this card (from your hand).");
            var brick = Card(2600, level: 6, location: CardLocation.Hand);
            var tuner = Card(100, level: 1, type: Monster | CardType.Tuner, location: CardLocation.Deck);
            var ash = Card(id: 14558127, location: CardLocation.Hand);
            f.Bot.Hand.Add(extender); f.Bot.Hand.Add(brick); f.Bot.Hand.Add(ash); f.Bot.Deck.Add(tuner);
            ResourceBoss(f); OfferResource(f, oneForOne);
            Check(ModelledResource(f, oneForOne), "One for One's precise monster-send cost is understood");
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.Activate, "costed recruiter starts the complete extender route");
            Check(f.AI.OnSelectCard(new[] { extender, ash, brick }, 1, 1, HintMsg.ToGrave, false).Single() == brick,
                "pay a real discard while preserving the required extender and hand trap");
            Check(f.AI.OnSelectCard(new[] { tuner }, 1, 1, HintMsg.SpSummon, false).Single() == tuner,
                "recruit selection follows the same plan after its cost prompt");
            f.Bot.Hand.Remove(brick); f.Bot.Hand.Remove(ash); f.Executor.OnChainEnd();
            Check(FullResourcePlan(f) == null, "the only extender cannot simultaneously pay the cost and complete the route");
        }
        var dai = new Fixture();
        var spell = Card(type: CardType.Spell, location: CardLocation.Hand,
            text: "①：自己场上没有怪兽存在的场合才能发动。从卡组把1只4星以下的通常怪兽特殊召唤。");
        var normal = Card(100, type: CardType.Monster | CardType.Normal, location: CardLocation.Deck);
        var tunerInHand = Card(100, type: Monster | CardType.Tuner, location: CardLocation.Hand);
        dai.Bot.Deck.Add(normal); dai.Bot.Hand.Add(tunerInHand); dai.Duel.MainPhase.SummonableCards.Add(tunerInHand);
        OfferResource(dai, spell); ResourceBoss(dai);
        Check(PlannedCard(FullResourcePlan(dai)) == spell, "Unexpected Dai is used while its empty-field condition still holds");
        dai.Bot.MonsterZone[0] = Card(100);
        Check(PlannedCard(FullResourcePlan(dai)) != spell, "future resource steps recheck the empty-field condition");
    }

    private static void SearchTriggerRoutes()
    {
        foreach (bool chinese in new[] { false, true })
        {
            var f = new Fixture();
            var starter = Card(100, type: Monster | CardType.Tuner, level: 2, location: CardLocation.Hand,
                text: chinese ? "①：这张卡召唤成功时才能发动。从卡组把1只6星怪兽加入手卡。"
                    : "If this card is Normal Summoned: You can add 1 Level 6 monster from your Deck to your hand.");
            var extension = Card(100, level: 6, location: CardLocation.Deck,
                text: "If you control a Tuner monster, you can Special Summon this card (from your hand).");
            f.Bot.Hand.Add(starter); f.Bot.Deck.Add(extension); ResourceBoss(f);
            f.Duel.MainPhase.SummonableCards.Add(starter);
            Check(PlannedCard(MainSummonPlan(f)) == starter, "Normal Summon search trigger creates an actual extender, Chinese=" + chinese);
            f.Bot.Deck.Remove(extension);
            Check(MainSummonPlan(f) == null, "search trigger cannot invent an absent extender");

            f = new Fixture();
            f.Bot.Hand.Add(starter); f.Bot.Deck.Add(extension); ResourceBoss(f);
            f.Duel.MainPhase.SummonableCards.Add(starter);
            Set(starter, "Disabled", 1);
            Check(MainSummonPlan(f) == null, "negated starter cannot provide a searched body");
            Set(starter, "Disabled", 0);
            Set(extension.Data, "Description", "Cannot be Normal Summoned/Set. Must be Special Summoned by a card effect.");
            Check(MainSummonPlan(f) == null, "search is not itself a Special Summon procedure");
        }
    }

    private static void ResourceSequenceOracle()
    {
        var timer = Stopwatch.StartNew();
        var random = new Random(270927);
        const int cases = 64;
        for (int iteration = 0; iteration < cases; iteration++)
        {
            var f = new Fixture(); bool search = iteration % 2 == 0;
            int tunerLevel = random.Next(1, 5), bodyLevel = random.Next(3, 8);
            var free = Card(random.Next(100, 701), level: bodyLevel, location: CardLocation.Hand,
                text: "If you control no monsters, you can Special Summon this card (from your hand).");
            var good = Card(100, level: tunerLevel, type: Monster | CardType.Tuner, location: CardLocation.Deck,
                text: "If you control a monster, you can Special Summon this card (from your hand).");
            var wrong = Card(2400, level: tunerLevel + 1, type: Monster | CardType.Tuner, location: CardLocation.Deck);
            var resource = Card(type: CardType.Spell, location: CardLocation.Hand, text: search
                ? "Add 1 Tuner monster from your Deck to your hand." : "Special Summon 1 Tuner monster from your Deck.");
            var boss = ResourceBoss(f, tunerLevel + bodyLevel);
            f.Bot.Hand.Add(free); f.Bot.Deck.Add(good); f.Bot.Deck.Add(wrong);
            OfferResource(f, resource); f.Duel.MainPhase.SpecialSummonableCards.Add(free);
            StoryLocalAiHooks.FinishMain(new MainPhaseAction(MainPhaseAction.MainAction.Summon), f.AI);
            // The independent oracle is level equality and the one-Tuner rule;
            // it never calls the planner's material parser or scoring functions.
            Check(good.Level + free.Level == boss.Level && wrong.Level + free.Level != boss.Level,
                "independent resource oracle has exactly one useful target");
            f.Enemy.Hand.Add(Card(random.Next(0, 4000), location: CardLocation.Hand, controller: 1));
            var action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            // Searching first is harmless; summoning from Deck first closes the
            // empty-field window. Both acceptable search orders are executed.
            if (action.Action == MainPhaseAction.MainAction.SpSummon)
            {
                f.Bot.Hand.Remove(free); free.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[0] = free;
                f.Duel.MainPhase.SpecialSummonableCards.Clear();
                action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            }
            else Check(search, "a recruiting spell never precedes the required empty-field body");
            Check(action.Action == MainPhaseAction.MainAction.Activate, "resource route actually activates its search/recruit spell");
            var offers = random.Next(2) == 0 ? new[] { wrong, good } : new[] { good, wrong };
            Check(f.AI.OnSelectCard(offers, 1, 1, search ? HintMsg.AddToHand : HintMsg.SpSummon, false).Single() == good,
                "resource target survives shuffled candidates and opponent hidden-hand changes");
            f.Bot.Hand.Remove(resource); resource.Location = CardLocation.Grave; f.Bot.Graveyard.Add(resource);
            f.Bot.Deck.Remove(good); f.Duel.MainPhase = new MainPhase(); f.Executor.OnChainEnd();
            if (search)
            {
                good.Location = CardLocation.Hand; f.Bot.Hand.Add(good);
                if (free.Location == CardLocation.Hand)
                {
                    f.Duel.MainPhase.SpecialSummonableCards.Add(free);
                    action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
                    Check(action.Action == MainPhaseAction.MainAction.SpSummon, "searched route retains the empty-field body");
                    f.Bot.Hand.Remove(free); free.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[0] = free;
                    f.Duel.MainPhase = new MainPhase();
                }
                f.Duel.MainPhase.SpecialSummonableCards.Add(good);
                action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
                Check(action.Action == MainPhaseAction.MainAction.SpSummon, "searched tuner uses its own legal hand procedure");
                f.Bot.Hand.Remove(good);
            }
            good.Location = CardLocation.MonsterZone; f.Bot.MonsterZone[1] = good;
            f.Duel.MainPhase = new MainPhase(); f.Duel.MainPhase.SpecialSummonableCards.Add(boss);
            action = f.AI.OnSelectIdleCmd(f.Duel.MainPhase);
            Check(action.Action == MainPhaseAction.MainAction.SpSummon, "resource route reaches a core-offered terminal");
            ApplyDevelopmentSummon(f, action);
            Check(f.Bot.GetMonsters().Single() == boss && f.Bot.Deck.Contains(wrong), "actual material selections realize the oracle terminal without spending the wrong card");
        }
        timer.Stop();
        Console.WriteLine("Resource sequence oracle: " + cases + "/" + cases + " executed terminal boards, " + timer.ElapsedMilliseconds + " ms");
    }

    private static bool MaterialsMatch(Fixture f, ClientCard destination, params ClientCard[] materials)
    {
        var eval = Evaluation(f); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var recipe = eval.GetType().GetMethod("MaterialRecipe", flags).Invoke(eval, new object[] { destination });
        return recipe != null && (bool)eval.GetType().GetMethod("ValidMaterials", flags)
            .Invoke(eval, new object[] { destination, recipe, materials.ToList(), null });
    }

    private static void StructuredMaterialRegressions()
    {
        var f = new Fixture();
        var cyberse = Card(100, level: 3); Set(cyberse, "Race", (int)CardRace.Cyberse);
        var dragon = Card(100, level: 3); Set(dragon, "Race", (int)CardRace.Dragon);
        foreach (string text in new[] { "1 Level 4 or lower Cyberse monster", "4星以下的电子界族怪兽1只" })
        {
            var link = Card(1000, type: Monster | CardType.Link, level: 1, location: CardLocation.Extra, text: text);
            Check(MaterialsMatch(f, link, cyberse), "typed Link-1 recipe accepts its legal material: " + text);
            Check(!MaterialsMatch(f, link, dragon), "typed Link-1 rejects another race");
            Set(cyberse, "Level", 5); Check(!MaterialsMatch(f, link, cyberse), "typed Link-1 honors its level cap"); Set(cyberse, "Level", 3);
        }
        foreach (string text in new[] { "2+ monsters, including a Cyberse monster", "包含电子界族怪兽的怪兽2只以上" })
        {
            var link = Card(2000, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra, text: text);
            Check(MaterialsMatch(f, link, cyberse, dragon), "including-one material is assigned without constraining all materials");
            Check(!MaterialsMatch(f, link, dragon, Card(100)), "including-one recipe cannot omit its required material");
        }
        var named = Card(100); Set(named.Data, "Name", "Oracle Dragon");
        var otherName = Card(100); Set(otherName.Data, "Name", "Other Dragon");
        var namedLink = Card(2000, type: Monster | CardType.Link, level: 2, location: CardLocation.Extra, text: "1 \"Oracle Dragon\" + 1 Cyberse monster");
        Check(MaterialsMatch(f, namedLink, named, cyberse), "mixed exact-name and race Link materials are assigned correctly");
        Check(!MaterialsMatch(f, namedLink, otherName, cyberse), "mixed Link materials retain exact card names");
        foreach (string text in new[] { "2 Level 4 DARK monsters", "暗属性4星怪兽×2只", "4星暗属性怪兽2只" })
        {
            var xyz = Card(2500, type: Monster | CardType.Xyz, level: 4, location: CardLocation.Extra, text: text);
            var a = Card(100); var b = Card(100); Set(a, "Attribute", 32); Set(b, "Attribute", 32);
            Check(MaterialsMatch(f, xyz, a, b), "Xyz recipe accepts exact level and attribute: " + text);
            Set(b, "Attribute", 16); Check(!MaterialsMatch(f, xyz, a, b), "Xyz recipe rejects wrong attribute");
        }
        var unknown = Card(2500, type: Monster | CardType.Xyz, level: 4, location: CardLocation.Extra,
            text: "2 Level 4 monsters that were Special Summoned this turn");
        Check(!MaterialsMatch(f, unknown, Card(100), Card(100)), "unparsed Xyz clause cannot authorize a speculative summon");
        Set(named.Data, "Name", "Normal Effect Level 4 Dragon");
        namedLink = Card(1000, type: Monster | CardType.Link, level: 1, location: CardLocation.Extra, text: "1 \"Normal Effect Level 4 Dragon\"");
        Check(MaterialsMatch(f, namedLink, named), "keywords within an exact card name are not parsed as material restrictions");
        var token = Card(100, type: CardType.Monster | CardType.Normal | CardType.Token); Set(token, "Race", (int)CardRace.Cyberse);
        var noToken = Card(1000, type: Monster | CardType.Link, level: 1, location: CardLocation.Extra, text: "电子界族怪兽1只（衍生物除外）");
        Check(MaterialsMatch(f, noToken, cyberse) && !MaterialsMatch(f, noToken, token), "typed Link recipes retain token exclusions");
        var optionalXyz = Card(2000, type: Monster | CardType.Xyz, level: 4, location: CardLocation.Extra, text: "4星怪兽×2只以上");
        Check(MaterialsMatch(f, optionalXyz, Card(100), Card(100), Card(100)), "database Xyz wording keeps the optional third material");
    }

    private static void TerminalResourceRegressions()
    {
        var f = new Fixture();
        var recruiter = Card(100, text: "①：这张卡召唤成功时才能发动。从手卡把1只怪兽特殊召唤。这个效果特殊召唤的怪兽的效果无效化。");
        Check(!(bool)Evaluate(f, "LiveInteraction", recruiter), "negating a recruited ally does not count as an end-board interruption");
        var ignition = Card(1800, text: "Once per turn: You can target 1 face-up monster your opponent controls; negate its effects.");
        Check(!(bool)Evaluate(f, "LiveInteraction", ignition), "ignition-only effect negation is not opponent-turn interaction");
        foreach (string text in new[] {
            "Once per turn (Quick Effect): You can target 1 face-up monster your opponent controls; negate its effects.",
            "①：对方把怪兽的效果发动时才能发动。那个效果无效。",
            "①：以对方场上1只怪兽为对象才能发动。那只怪兽的效果无效。这个效果在对方回合也能发动。" })
            Check((bool)Evaluate(f, "LiveInteraction", Card(text: text)), "real reactive negation remains an end-board interruption");

        var temporary = Card(2500);
        var teleport = Card(type: CardType.Spell, location: CardLocation.SpellZone,
            text: "Special Summon 1 monster from your Deck. During the End Phase of this turn, banish that monster.");
        f.Bot.MonsterZone[0] = temporary; f.Chain(teleport, 0); f.Duel.SolvingChainIndex = 1;
        f.Duel.LastSummonedCards.Add(temporary); f.Executor.OnSpSummoned();
        var eval = Evaluation(f); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var tracked = (HashSet<ClientCard>)eval.GetType().GetField("temporaryDevelopmentBodies", flags).GetValue(eval);
        Check(tracked.Contains(temporary), "actual resolved temporary recruits retain their expiry in subsequent planning");
        f.Executor.OnMove(temporary, 0, (int)CardLocation.MonsterZone, 0, (int)CardLocation.Grave);
        Check(!tracked.Contains(temporary), "a temporary body's expiry is not transferred to a later revival");

        f = new Fixture(); f.Bot.MonsterZone[0] = Real(84815190);
        var lowTuner = Card(100, level: 1, type: Monster | CardType.Tuner, location: CardLocation.Deck);
        f.Bot.Deck.Add(lowTuner);
        teleport = Card(type: CardType.Spell | CardType.QuickPlay, location: CardLocation.Hand,
            text: "Special Summon 1 Level 1 Tuner monster from your Deck.");
        OfferResource(f, teleport);
        Evaluate(f, "NoteResolvedResourceEffect", 23434538, 1);
        Evaluate(f, "NoteResolvedResourceEffect", 42141493, 1);
        Check(!f.Rule(teleport, ExecutorType.Activate), "generic activation cannot bypass the planned stop under stacked draw pressure");
        f.Executor.OnNewTurn();
        Check(f.Rule(teleport, ExecutorType.Activate), "source-specific draw pressure resets next turn");
    }

    private static void ResourcePlanningStress()
    {
        var f = new Fixture();
        f.Bot.Deck.Clear();
        for (int i = 0; i < 30; i++)
            f.Bot.Deck.Add(Card(100 + i * 30, level: 1 + i % 8, location: CardLocation.Deck,
                type: i % 3 == 0 ? Monster | CardType.Tuner : Monster,
                text: "If you control a monster, you can Special Summon this card (from your hand)."));
        for (int i = 0; i < 4; i++)
        {
            var card = Card(500, level: i + 1, location: CardLocation.Hand,
                type: i % 2 == 0 ? Monster | CardType.Tuner : Monster,
                text: "If you control a monster, you can Special Summon this card (from your hand).");
            f.Bot.Hand.Add(card); f.Duel.MainPhase.SummonableCards.Add(card);
        }
        f.Bot.MonsterZone[0] = Card(500, level: 3);
        for (int i = 0; i < 3; i++)
            OfferResource(f, Card(type: CardType.Spell, location: CardLocation.Hand, text: i == 0
                ? "Send 1 monster from your hand to the GY; Special Summon 1 Level 1 monster from your hand or Deck."
                : i == 1 ? "Add 1 monster from your Deck to your hand." : "Special Summon 1 monster from your Deck."), i);
        for (int i = 0; i < 15; i++) ResourceBoss(f, 4 + i % 9);
        var timer = Stopwatch.StartNew(); var plan = FullResourcePlan(f); timer.Stop();
        int nodes = (int)Evaluation(f).GetType().GetProperty("LastDevelopmentNodes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Evaluation(f));
        Check(plan != null && nodes <= 5000, "dense resource planning obeys its state budget and retains a useful action");
        Check(f.Bot.Hand.Count == 7 && f.Bot.Deck.Count == 30 && f.Bot.ExtraDeck.Count == 15 && f.Bot.GetMonsterCount() == 1,
            "dense resource search leaves all live resource collections intact");
        var watch = Stopwatch.StartNew(); var cached = FullResourcePlan(f); watch.Stop();
        Check(ReferenceEquals(plan, cached), "identical legal actions reuse the resource route cache");
        Console.WriteLine("Resource planning stress: 3 spells / 30 deck targets / 15 extras, " + nodes + " nodes, " +
            timer.ElapsedMilliseconds + " ms; cached " + watch.ElapsedMilliseconds + " ms");
    }
}
