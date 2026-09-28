using System;
using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public abstract partial class StoryLuckyExecutor
    {
        private readonly StoryAiEvaluation evaluation;
        private static bool JustDontIt() => false;
        private bool HasSpecific(ExecutorType type, ClientCard card) => Executors.Any(e => e.Type == type && e.CardId == card.Id);
        private bool IsOurMain => Duel.Player == 0 && (Duel.Phase == DuelPhase.Main1 || Duel.Phase == DuelPhase.Main2);
        private ClientCard LastChain => Duel.CurrentChain.LastOrDefault();
        private bool EnemyChain => LastChain != null && Duel.LastChainPlayer == 1 &&
            !Duel.NegatedChainIndexList.Contains(Duel.CurrentChain.Count);

        private void RegisterStoryPolicy()
        {
            AddExecutor(ExecutorType.Activate, _CardId.CrossoutDesignator, StoryCrossout);
            AddExecutor(ExecutorType.Activate, _CardId.LockBird, OpponentDrawWindow);
            AddExecutor(ExecutorType.Activate, _CardId.GhostMournerMoonlitChill, StoryDisableMonster);
            AddExecutor(ExecutorType.Activate, _CardId.MulcharmyFuwalos, StoryDrawPressure);
            AddExecutor(ExecutorType.Activate, _CardId.MulcharmyPurulia, StoryDrawPressure);
            AddExecutor(ExecutorType.Activate, _CardId.MulcharmyNyalus, StoryDrawPressure);
            AddExecutor(ExecutorType.Activate, _CardId.NibiruThePrimalBeing, StoryNibiru);

            // These are last, and explicitly exclude every card with a dedicated rule.
            AddExecutor(ExecutorType.Activate, GenericActivate);
            AddExecutor(ExecutorType.SpSummon, () => Card.Location != CardLocation.Extra && StrategicSpecialSummon());
            AddExecutor(ExecutorType.SummonOrSet, StrategicNormalSummon);
            AddExecutor(ExecutorType.SpSummon, StrategicSpecialSummon);
            AddExecutor(ExecutorType.Repos, StrategicReposition);
            AddExecutor(ExecutorType.MonsterSet, StrategicMonsterSet);
            AddExecutor(ExecutorType.SpellSet, StrategicSpellSet);
        }

        public override bool OnSelectHand() => true;

        private bool OpponentDrawWindow() => Duel.Player == 1 && Duel.LastChainPlayer != 0 &&
            !Duel.CurrentChain.Any(c => c.Controller == 0 && c.Id == Card.Id);

        public override bool OnPreActivate(ClientCard card)
        {
            // GameAI invokes this before checking each executor's type/id. Keep expensive
            // generic planning in GenericActivate; dedicated rules still get the safety gate.
            return PlannedResponseAllowed(card, ActivateDescription) && !DefaultCheckWhetherCardIsNegated(card) && base.OnPreActivate(card) && SpellColumnAllows(card) &&
                (!HasSpecific(ExecutorType.Activate, card) || EffectAllowed(card, ActivateDescription, out _));
        }

        public override void OnNewTurn()
        {
            base.OnNewTurn();
            AI.SelectNumber(-1);
            ownResolvedDrawEffects.Clear();
            levelChangeIntents.Clear();
            ClearChainResponses();
            accesscodeAttributes.Clear();
            effectIntent = null;
            resourceSelection = null;
            CommitExtraPlan(null);
            evaluation.ResetDevelopmentTurn();
        }

        public override void OnChainSolved(int chainIndex)
        {
            base.OnChainSolved(chainIndex);
            if (chainIndex > 0 && chainIndex <= Duel.CurrentChainInfo.Count)
            {
                var combo = Duel.CurrentChainInfo[chainIndex - 1];
                if (combo.ActivatePlayer == 0) evaluation.NoteComboResolution(combo.RelatedCard, combo.ActivateDescription, Duel.NegatedChainIndexList.Contains(chainIndex));
            }
            if (chainIndex > 0 && chainIndex <= Duel.CurrentChainInfo.Count && !Duel.NegatedChainIndexList.Contains(chainIndex))
            {
                var info = Duel.CurrentChainInfo[chainIndex - 1];
                evaluation.NoteResolvedResourceEffect(info.ActivateId, info.ActivatePlayer);
                if (info.ActivatePlayer == 0 && DrawPressureCard(info.ActivateId)) ownResolvedDrawEffects.Add(info.ActivateId);
            }
        }

        private bool StoryAshBlossom()
        {
            if (!EnemyChain) return false;
            // Macro Cosmos's optional Helios summon is a poor exchange; do not miss real starters
            // such as Cyber Emergency just because the legacy strategy blacklisted them.
            if (LastChain.IsCode(_CardId.MacroCosmos)) return false;
            if (LastChain.HasSetcode(0x11e) && LastChain.Location == CardLocation.Hand) return false;
            return !DefaultCheckWhetherCardIsNegated(LastChain);
        }

        private bool StoryDisableMonster()
        {
            ClientCard target = null;
            if (Card.Id == _CardId.GhostMournerMoonlitChill)
            {
                if (Duel.LastSummonPlayer != 1) return false;
                target = Duel.LastSummonedCards.Where(c => c.Controller == 1 && c.Location == CardLocation.MonsterZone &&
                    c.IsFaceup() && !c.IsDisabled() && evaluation.CanTarget(c, Card)).OrderByDescending(evaluation.Threat).FirstOrDefault();
                if (target == null) return false;
                AI.SelectCard(target);
                return true;
            }
            // A normal spell/trap being chained to our effect is not a reason to negate our own monster.
            if (EnemyChain && LastChain.Location == CardLocation.MonsterZone && LastChain.Controller == 1 &&
                !LastChain.IsDisabled() && evaluation.CanTarget(LastChain, Card)) target = LastChain;
            if (target == null && Duel.CurrentChain.Count == 0)
            {
                target = Enemy.GetMonsters().Where(c => c.IsFaceup() && !c.IsDisabled() && evaluation.CanTarget(c, Card) &&
                    (c.IsMonsterShouldBeDisabledBeforeItUseEffect() || IsOurMain && c.IsFloodgate()))
                    .OrderByDescending(evaluation.Threat).FirstOrDefault();
            }
            if (target == null && Bot.UnderAttack && Enemy.BattlingMonster != null && !Enemy.BattlingMonster.IsDisabled() &&
                evaluation.CanTarget(Enemy.BattlingMonster, Card) && Enemy.BattlingMonster.IsMonsterDangerous()) target = Enemy.BattlingMonster;
            if (target == null) return false;
            AI.SelectCard(target);
            return true;
        }

        private bool StoryCalledByTheGrave()
        {
            if (!EnemyChain || !StoryAiEvaluation.Has(LastChain, CardType.Monster)) return false;
            var target = Enemy.Graveyard.FirstOrDefault(c => c.Id == LastChain.Id && evaluation.CanTarget(c, Card));
            if (target == null || DefaultCheckWhetherCardIdIsNegated(target.Id)) return false;
            AI.SelectCard(target);
            return true;
        }

        private bool StoryCrossout()
        {
            if (!EnemyChain || DefaultCheckWhetherCardIdIsNegated(LastChain.Id)) return false;
            // WindBot does not always know the ids still in the deck. Never guess a declaration.
            if (!evaluation.KnownOwnDeckContains(LastChain.Id)) return false;
            AI.SelectAnnounceID(LastChain.Id);
            AI.SelectCard(LastChain.Id);
            return true;
        }

        private bool StoryBookOfMoon()
        {
            var targets = Enemy.GetMonsters().Where(c => c.IsFaceup() && !StoryAiEvaluation.Has(c, CardType.Link | CardType.Token) && evaluation.CanTarget(c, Card));
            var target = targets.Where(c => EnemyChain && c == LastChain || c.IsFloodgate() ||
                Bot.UnderAttack && c == Enemy.BattlingMonster || IsOurMain && Bot.GetMonsters().Any(b =>
                    b.IsAttack() && StoryAiEvaluation.Attack(b) <= c.GetDefensePower() && StoryAiEvaluation.Attack(b) > c.Defense))
                .OrderByDescending(evaluation.Threat).FirstOrDefault();
            if (target == null && EnemyChain)
            {
                // Dodge Veiler / Impermanence when the targeted monster's effect is already on chain.
                if (LastChain.IsCode(_CardId.EffectVeiler, _CardId.InfiniteImpermanence, _CardId.BreakthroughSkill))
                    target = Bot.GetMonsters().FirstOrDefault(c => c.IsFaceup() && !StoryAiEvaluation.Has(c, CardType.Link | CardType.Token) &&
                        Duel.ChainTargets.Contains(c) && Duel.CurrentChain.Contains(c));
            }
            if (target == null) return false;
            AI.SelectCard(target);
            return true;
        }

        private bool StoryRaigeki() => Enemy.GetMonsters().Any(CanDestroyByEffect) &&
            (Enemy.GetMonsters().Count(CanDestroyByEffect) >= 2 || Enemy.GetMonsters().Any(c => CanDestroyByEffect(c) && evaluation.Threat(c) >= 2800) || CanPushDamage());
        private bool StoryFeatherDuster() => Enemy.GetSpellCount() > 0 &&
            (Enemy.GetSpellCount() >= 2 || Enemy.GetSpells().Any(c => c.IsFaceup() && c.IsFloodgate()) || CanPushDamage());
        private bool StoryDarkHole() => Enemy.GetMonsters().Any(CanDestroyByEffect) &&
            Enemy.GetMonsters().Where(CanDestroyByEffect).Sum(evaluation.Threat) > Bot.GetMonsters().Where(CanDestroyByEffect).Sum(evaluation.Keep) + 900;
        private bool StoryTorrentialTribute() => Duel.LastSummonPlayer == 1 && StoryDarkHole();
        private bool StoryNibiru() => Duel.Player == 1 && Enemy.GetMonsters().Count(c => c.IsFaceup()) >= 2 &&
            Enemy.GetMonsters().Sum(evaluation.Threat) > Bot.GetMonsters().Sum(evaluation.Keep) + 2200;
        private bool StoryEvenlyBattle() => Duel.Player == 0 && Duel.Phase == DuelPhase.Main1 && Duel.Turn > 1 &&
            Bot.GetFieldCount() == 0 && Enemy.GetFieldCount() >= 3 && Bot.Hand.Any(c => StoryAiEvaluation.Facts(c).BattleEndBalance &&
                !DefaultCheckWhetherCardIsNegated(c));
        private bool CanPushDamage() => !evaluation.BattleDamageBlocked && IsOurMain && Duel.Phase == DuelPhase.Main1 && Duel.Turn > 1 &&
            Duel.MainPhase?.CanBattlePhase == true && Bot.GetMonsters().Any(c => c.IsAttack() && !c.Attacked && c.Attack > 0);
        private bool StoryMirrorForce() => Bot.UnderAttack && Enemy.GetMonsters().Any(c => c.IsAttack()) &&
            (Enemy.GetMonsters().Count(c => c.IsAttack()) >= 2 || Bot.BattlingMonster == null ||
             Enemy.BattlingMonster != null && Enemy.BattlingMonster.Attack >= Bot.BattlingMonster.GetDefensePower());

        private bool StoryReborn()
        {
            if (IsOurMain && Duel.CurrentChain.Count == 0)
            {
                var route = MainSummonRoute();
                if (route != null)
                {
                    if (!route.Activation || route.Card != Card) return false;
                    // Stealing an already established opposing boss can be better
                    // than using two of our bodies to construct a similar terminal.
                    var enemyTarget = Enemy.Graveyard.Where(c => StoryAiEvaluation.Has(c, CardType.Monster) &&
                        c.IsCanRevive() && DrawPressureAllows(c, null)).OrderByDescending(c => evaluation.ImmediateRevivalGain(Card, c)).FirstOrDefault();
                    if (enemyTarget != null && evaluation.ImmediateRevivalGain(Card, enemyTarget) > route.Gain + 100)
                    {
                        AI.SelectCard(enemyTarget);
                        return true;
                    }
                    CommitResourceRoute(route);
                    return true;
                }
            }
            var candidates = Bot.Graveyard.Concat(Enemy.Graveyard).Where(c => StoryAiEvaluation.Has(c, CardType.Monster) && c.IsCanRevive() && DrawPressureAllows(c, null)).ToList();
            var potential = IsOurMain ? evaluation.DevelopmentBonuses(candidates) : new Dictionary<ClientCard, float>();
            var target = candidates.OrderByDescending(c => evaluation.SummonValue(c) + (potential.TryGetValue(c, out var bonus) ? bonus : 0)).FirstOrDefault();
            if (target == null || Bot.MonsterZone.Take(5).All(c => c != null)) return false;
            AI.SelectCard(target);
            return true;
        }

        private float ActivationValue(ClientCard card, int description)
        {
            var role = evaluation.Roles(card);
            float score = 1000;
            if (card.Location == CardLocation.Grave || card.Location == CardLocation.Removed) score += 800;
            if ((role & StoryAiEvaluation.Role.Draw) != 0) score += 1300;
            if ((role & StoryAiEvaluation.Role.Search) != 0) score += 1600;
            if ((role & StoryAiEvaluation.Role.Extend) != 0) score += Bot.GetMonsterCount() < 3 ? 1100 : 250;
            if (StoryAiEvaluation.Has(card, CardType.Field | CardType.Continuous)) score += 400;
            if (EffectAllowed(card, description, out var intent) && intent.Target != null &&
                (intent.Purpose == EffectPurpose.TargetRemoval || intent.Purpose == EffectPurpose.EnemyRemoval ||
                 intent.Purpose == EffectPurpose.LinkBanishCost || intent.Purpose == EffectPurpose.TargetNegate))
            {
                // Clear a known lock/interaction before exposing starters to it. The
                // current effect description matters: another paragraph may be a search.
                if (intent.Target.IsFaceup() && !intent.Target.IsDisabled() && intent.Target.IsFloodgate()) score += 7000;
                else if (evaluation.LiveInteraction(intent.Target)) score += 2600;
                else score += Math.Min(1200, Math.Max(0, RemovalValue(intent.Target, intent)) * .25f);
            }
            return score;
        }

        private bool GenericAllowed(ClientCard candidate, int description)
        {
            if (HasSpecific(ExecutorType.Activate, candidate) || !CandidatePreActivate(candidate, description)) return false;
            // Admit core-offered pure level actions to the unified root comparison.
            // The selected route is checked/committed below; independently vetoing
            // equivalent roots here can reject every copy of the same useful effect.
            bool levelRoot = !selectingChainResponse && IsOurMain && Duel.CurrentChain.Count == 0 &&
                Duel.MainPhase?.ActivableCards.Contains(candidate) == true && evaluation.ModelledLevelAction(candidate, description);
            if (!levelRoot && !EffectAllowed(candidate, description, out _)) return false;
            if (!evaluation.WorthKnownSummonEffect(candidate)) return false;
            // At an open Main Phase, a generic card from the hand is a legal action in
            // its own right.  The old check treated LastChainPlayer's default value (0)
            // as an active own chain and rejected every unregistered hand spell, so a
            // story deck could set its traps once and then end every later turn.  Keep
            // the stale-candidate guard while a real chain is resolving.
            if (Duel.LastChainPlayer == 0 && Duel.CurrentChain.Count > 0 && candidate.Location != CardLocation.Grave && candidate.Location != CardLocation.Removed &&
                !Duel.ChainTargets.Contains(candidate) && !Duel.LastSummonedCards.Contains(candidate)) return false;
            if (StoryAiEvaluation.Has(candidate, CardType.Field) && candidate.Location == CardLocation.Hand && Bot.SpellZone[5] != null &&
                Bot.SpellZone[5].Id == candidate.Id) return false;
            if (Duel.Player == 1 && Duel.CurrentChain.Count == 0 && candidate.Location == CardLocation.MonsterZone &&
                !Bot.UnderAttack && !Duel.LastSummonedCards.Contains(candidate) && Duel.Phase != DuelPhase.End && Duel.Phase != DuelPhase.Standby)
                return false;
            return true;
        }

        private bool GenericActivate()
        {
            if (!GenericAllowed(Card, ActivateDescription)) return false;
            if (StoryAiEvaluation.Facts(Card).UniversalRevival) return StoryReborn();
            if (Duel.CurrentChainInfo.Any(c => c.ActivatePlayer == 0 && c.RelatedCard == Card && c.ActivateDescription == ActivateDescription)) return false;
            if (!selectingChainResponse && IsOurMain && Duel.CurrentChain.Count == 0 && Duel.MainPhase != null &&
                Duel.MainPhase.ActivableCards.Contains(Card))
            {
                var planned = MainSummonRoute();
                // Secure a concrete conversion before an unmodelled ignition effect
                // can tribute/discard its irreplaceable material. Summon triggers and
                // verified removal/interaction retain their own timing and priorities.
                if (planned != null && (!planned.Activation || planned.Card != Card || planned.Description != ActivateDescription) &&
                    DescribeEffect(Card, ActivateDescription).Purpose == EffectPurpose.Unknown && evaluation.ConsumesDevelopmentResource(Card))
                    return false;
            }
            if (selectingChainResponse && IsOurMain)
            {
                var triggerRoute = evaluation.PlanComboAction(Card, ActivateDescription, trigger: true);
                if (triggerRoute != null)
                {
                    if (!EffectAllowed(Card, ActivateDescription, out var triggerIntent)) return false;
                    CommitEffect(triggerIntent); CommitResourceRoute(triggerRoute); return true;
                }
                if (evaluation.ModelledComboTrigger(Card, ActivateDescription)) return false;
            }
            if (!selectingChainResponse && IsOurMain && Duel.CurrentChain.Count == 0 && (evaluation.ModelledComboAction(Card, ActivateDescription) || evaluation.ModelledHandSummon(Card, ActivateDescription) ||
                evaluation.ModelledResourceAction(Card, ActivateDescription) || evaluation.ModelledFusionAction(Card, ActivateDescription)))
            {
                var route = MainSummonRoute();
                if (route != null)
                {
                    if (!route.Activation || route.Card != Card || route.Description != ActivateDescription) return false;
                    if (!EffectAllowed(Card, ActivateDescription, out var summonIntent)) return false;
                    CommitEffect(summonIntent);
                    CommitResourceRoute(route);
                    return true;
                }
                if (evaluation.ModelledComboAction(Card, ActivateDescription))
                {
                    var fallback = evaluation.PlanComboAction(Card, ActivateDescription);
                    if (fallback == null || !EffectAllowed(Card, ActivateDescription, out var comboIntent)) return false;
                    CommitEffect(comboIntent); CommitResourceRoute(fallback); return true;
                }
                if (evaluation.ModelledResourceAction(Card, ActivateDescription) && !evaluation.ResourcePressureAllows(Card)) return false;
                if (evaluation.ModelledFusionAction(Card, ActivateDescription) &&
                    evaluation.UsesBanishFusion(Card)) return false;
            }
            if (!selectingChainResponse && IsOurMain && Duel.CurrentChain.Count == 0 && Duel.MainPhase != null)
            {
                var orderedRoute = MainSummonRoute();
                float score = ActivationValue(Card, ActivateDescription);
                for (int i = 0; i < Duel.MainPhase.ActivableCards.Count; i++)
                {
                    var candidate = Duel.MainPhase.ActivableCards[i];
                    int description = Duel.MainPhase.ActivableDescs[i];
                    if (orderedRoute != null && (!orderedRoute.Activation || orderedRoute.Card != candidate || orderedRoute.Description != description) &&
                        (evaluation.ModelledComboAction(candidate, description) || evaluation.ModelledHandSummon(candidate, description) ||
                         evaluation.ModelledResourceAction(candidate, description) || evaluation.ModelledFusionAction(candidate, description) ||
                         DescribeEffect(candidate, description).Purpose == EffectPurpose.Unknown && evaluation.ConsumesDevelopmentResource(candidate))) continue;
                    if ((candidate != Card || description != ActivateDescription) && GenericAllowed(candidate, description) &&
                        ActivationValue(candidate, description) > score + .01f) return false;
                }
            }
            if (!EffectAllowed(Card, ActivateDescription, out var intent)) return false;
            CommitEffect(intent);
            return true;
        }

        private bool NormalAllowed(ClientCard card)
        {
            if (StoryAiEvaluation.Exodia(card)) return false;
            // Hand traps are interaction reserved for the opponent.  Never walk one
            // onto the field as a normal summon, even when the opponent already has a
            // monster and the old empty-board exception would otherwise allow it.
            if (StoryAiEvaluation.IsHandTrap(card)) return false;
            // The core has offered this normal summon. Its actual tribute cost and
            // continuation are compared with passing by MainSummonRoute.
            int level = StoryAiEvaluation.Level(card);
            return level <= 4 || Bot.GetMonsters().Count >= (level >= 7 ? 2 : 1);
        }

        private bool StrategicNormalSummon()
        {
            if (HasSpecific(ExecutorType.Summon, Card) || HasSpecific(ExecutorType.SummonOrSet, Card) || !NormalAllowed(Card)) return false;
            if (Duel.MainPhase == null) return true;
            var route = MainSummonRoute();
            if (route != null && Duel.MainPhase.SummonableCards.Contains(Card))
            {
                if (!route.Normal || route.Card != Card) return false;
                CommitResourceRoute(route);
                return true;
            }
            if (StoryAiEvaluation.Level(Card) >= 5 && Duel.MainPhase.SummonableCards.Contains(Card)) return false;
            var candidates = Duel.MainPhase.SummonableCards.Where(c => NormalAllowed(c) && !HasSpecific(ExecutorType.Summon, c)).ToList();
            var potential = evaluation.DevelopmentBonuses(candidates.Where(c => StoryAiEvaluation.Level(c) <= 4).ToList());
            Func<ClientCard, float> score = c => evaluation.SummonValue(c) + (potential.TryGetValue(c, out float bonus) ? bonus : 0);
            return !candidates.Any(c => c != Card && score(c) > score(Card) + .01f);
        }

        private bool SpecialAllowed(ClientCard card, out StoryAiEvaluation.ExtraPlan plan)
        {
            plan = null;
            if (card.Location == CardLocation.Extra)
            {
                if (IsOurMain)
                {
                    var roots = Duel.MainPhase?.SpecialSummonableCards.Where(c => c.Location == CardLocation.Extra && !HasSpecific(ExecutorType.SpSummon, c)).ToList()
                        ?? new List<ClientCard>();
                    if (!roots.Contains(card)) roots.Add(card);
                    plan = evaluation.PlanDevelopment(roots);
                }
                else plan = evaluation.PlanExtra(card);
                if (plan == null || plan.Destination != card) return false;
            }
            return DrawPressureAllows(card, plan);
        }

        private bool StrategicSpecialSummon()
        {
            if (HasSpecific(ExecutorType.SpSummon, Card)) return false;
            var route = MainSummonRoute();
            if (route != null && Duel.MainPhase.SpecialSummonableCards.Contains(Card))
            {
                if (route.Normal || route.Activation || route.Card != Card) return false;
                effectIntent = null;
                CommitExtraPlan(route.Extra);
                CommitResourceRoute(route);
                return true;
            }
            if (evaluation.ModelledDirectCost(Card))
            {
                var costed = evaluation.PlanComboAction(Card, 0, direct: true);
                if (costed == null) return false;
                effectIntent = null; CommitResourceRoute(costed); return true;
            }
            if (!SpecialAllowed(Card, out var plan)) return false;
            // Preserve an already available direct lethal instead of consuming attackers.
            if (!evaluation.BattleDamageBlocked && Card.Location == CardLocation.Extra && CanPushDamage() && Enemy.GetMonsterCount() == 0 &&
                Bot.GetMonsters().Where(c => c.IsAttack()).Sum(StoryAiEvaluation.Attack) >= Enemy.LifePoints) return false;
            var arrivals = Duel.MainPhase?.SpecialSummonableCards.Where(c => c.Location != CardLocation.Extra &&
                !HasSpecific(ExecutorType.SpSummon, c)).ToList() ?? new List<ClientCard>();
            var potential = IsOurMain ? evaluation.DevelopmentBonuses(arrivals) : new Dictionary<ClientCard, float>();
            Func<ClientCard, float> arrivalValue = c => evaluation.SummonValue(c) + (potential.TryGetValue(c, out float bonus) ? bonus : 0);
            float score = plan?.Gain ?? arrivalValue(Card);
            if (Duel.MainPhase != null)
                foreach (var candidate in Duel.MainPhase.SpecialSummonableCards)
                    if (candidate != Card && !HasSpecific(ExecutorType.SpSummon, candidate) && SpecialAllowed(candidate, out var alternative) &&
                        (alternative?.Gain ?? arrivalValue(candidate)) > score + .01f) return false;
            effectIntent = null;
            CommitExtraPlan(plan);
            return true;
        }

        private StoryAiEvaluation.SummonPlan MainSummonRoute()
        {
            if (!IsOurMain || Duel.MainPhase == null) return null;
            // Dedicated rules retain their veto. The plan compares only actions this
            // generic policy is responsible for, using the same end-board objective.
            var normals = Duel.MainPhase.SummonableCards.Where(c => NormalAllowed(c) &&
                !HasSpecific(ExecutorType.Summon, c) && !HasSpecific(ExecutorType.SummonOrSet, c)).ToList();
            var specials = Duel.MainPhase.SpecialSummonableCards.Where(c => !HasSpecific(ExecutorType.SpSummon, c)).ToList();
            // An available lethal attack should not be traded for another extra body.
            if (!evaluation.BattleDamageBlocked && CanPushDamage() && Enemy.GetMonsterCount() == 0 &&
                Bot.GetMonsters().Where(c => c.IsAttack() && !c.Attacked).Sum(StoryAiEvaluation.Attack) >= Enemy.LifePoints)
                specials.RemoveAll(c => c.Location == CardLocation.Extra);
            var activations = new List<KeyValuePair<ClientCard, int>>();
            if (Duel.CurrentChain.Count == 0)
                for (int i = 0; i < Duel.MainPhase.ActivableCards.Count; i++)
                {
                    var candidate = Duel.MainPhase.ActivableCards[i];
                    int description = Duel.MainPhase.ActivableDescs[i];
                    if ((evaluation.ModelledComboAction(candidate, description) || evaluation.ModelledHandSummon(candidate, description) || evaluation.ModelledResourceAction(candidate, description) || evaluation.ModelledFusionAction(candidate, description)) &&
                        GenericAllowed(candidate, description))
                        activations.Add(new KeyValuePair<ClientCard, int>(candidate, description));
                }
            var plan = evaluation.PlanSummonActions(normals, specials, activations);
            // Resource actions already pay source-specific draw pressure for their
            // real target, never tax the activating Spell as a summoned monster.
            if (plan != null && !plan.Combo && !plan.Normal && plan.Target == null && !DrawPressureAllows(plan.Card, plan.Extra)) return null;
            return plan;
        }

        private bool StoryComboSpell()
        {
            var route = MainSummonRoute();
            if (route != null && (!route.Activation || route.Card != Card || route.Description != ActivateDescription)) return false;
            route = route ?? evaluation.PlanComboAction(Card, ActivateDescription);
            if (route == null || !EffectAllowed(Card, ActivateDescription, out var intent)) return false;
            CommitEffect(intent); CommitResourceRoute(route); return true;
        }

        private bool StoryExcitonSummon()
        {
            if (!DefaultEvilswarmExcitonKnightSummon()) return false;
            var plan = evaluation.PlanExtra(Card);
            if (plan == null) return false;
            CommitExtraPlan(plan);
            return true;
        }

        public override bool OnSelectMonsterSummonOrSet(ClientCard card)
        {
            var route = MainSummonRoute();
            if (route != null && route.Normal && route.Card == card) return false;
            if (StoryAiEvaluation.Has(card, CardType.Flip)) return true;
            // Do not hide an on-summon starter merely because the opponent has a large monster.
            if ((evaluation.Roles(card) & (StoryAiEvaluation.Role.Starter | StoryAiEvaluation.Role.Search | StoryAiEvaluation.Role.Extend)) != 0) return false;
            // DefaultExecutor is deliberately conservative when the opponent controls a
            // larger monster and can answer by setting every ordinary attacker.  That
            // creates a deadlock in story turns: the hand is set, the phase ends, and
            // the next turn repeats the same choice.  A legal attacker with ATK above
            // DEF is a real forward action, so keep it face-up and summon it; the core
            // still decides whether the summon itself is legal.
            if (IsOurMain && StoryAiEvaluation.Attack(card) > StoryAiEvaluation.Defense(card) &&
                !StoryAiEvaluation.IsHandTrap(card)) return false;
            return base.OnSelectMonsterSummonOrSet(card);
        }

        private bool StrategicMonsterSet() => !HasSpecific(ExecutorType.Summon, Card) && NormalAllowed(Card) &&
            !StoryAiEvaluation.IsHandTrap(Card) && (Bot.GetMonsterCount() == 0 || StoryAiEvaluation.Has(Card, CardType.Flip));
        private bool StrategicReposition()
        {
            if (StoryAiEvaluation.Has(Card, CardType.Link)) return false;
            if (Card.IsFacedown()) return StoryAiEvaluation.Has(Card, CardType.Flip) || evaluation.SummonValue(Card) > 3000 || DefaultMonsterRepos();
            return DefaultMonsterRepos();
        }
        private bool StrategicSpellSet()
        {
            if (!DefaultSpellSet()) return false;
            // Set after battle so Quick-Play spells remain usable from hand during our turn.
            if (Duel.Phase == DuelPhase.Main1 && Duel.MainPhase != null && Duel.MainPhase.CanBattlePhase &&
                StoryAiEvaluation.Has(Card, CardType.QuickPlay) && Bot.GetMonsters().Any(c => c.IsAttack())) return false;
            return !StoryAiEvaluation.Has(Card, CardType.Continuous) || !Bot.GetSpells().Any(c => c.Id == Card.Id);
        }
    }
}
