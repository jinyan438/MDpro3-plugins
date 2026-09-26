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
        private bool enemyDrawPressure;
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
            AddExecutor(ExecutorType.Activate, _CardId.MulcharmyFuwalos, OpponentDrawWindow);
            AddExecutor(ExecutorType.Activate, _CardId.MulcharmyPurulia, OpponentDrawWindow);
            AddExecutor(ExecutorType.Activate, _CardId.MulcharmyNyalus, OpponentDrawWindow);
            AddExecutor(ExecutorType.Activate, _CardId.NibiruThePrimalBeing, StoryNibiru);
            AddExecutor(ExecutorType.Activate, _CardId.LightningStorm, StoryLightningStorm);
            AddExecutor(ExecutorType.Activate, _CardId.EvenlyMatched, () => Enemy.GetFieldCount() > Bot.GetFieldCount() + 1);
            AddExecutor(ExecutorType.Activate, 83764718, StoryReborn); // Monster Reborn
            AddExecutor(ExecutorType.Activate, 55144522, () => Bot.Deck.Count > 2); // Pot of Greed
            AddExecutor(ExecutorType.Activate, 70368879, () => Bot.Deck.Count > 1); // Upstart Goblin
            AddExecutor(ExecutorType.Activate, 81439173, () => Bot.Deck.Count > 0); // Foolish Burial
            AddExecutor(ExecutorType.Activate, 44095762, StoryMirrorForce);

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
            return !DefaultCheckWhetherCardIsNegated(card) && base.OnPreActivate(card) &&
                (!HasSpecific(ExecutorType.Activate, card) || EffectAllowed(card, ActivateDescription, out _));
        }

        public override void OnNewTurn()
        {
            base.OnNewTurn();
            enemyDrawPressure = false;
            accesscodeAttributes.Clear();
            effectIntent = null;
            CommitExtraPlan(null);
            evaluation.ResetDevelopmentTurn();
        }

        public override void OnChainSolved(int chainIndex)
        {
            base.OnChainSolved(chainIndex);
            if (chainIndex > 0 && chainIndex <= Duel.CurrentChainInfo.Count && !Duel.NegatedChainIndexList.Contains(chainIndex))
            {
                var info = Duel.CurrentChainInfo[chainIndex - 1];
                if (info.ActivatePlayer == 1 && (info.ActivateId == _CardId.MaxxC || info.ActivateId == _CardId.MulcharmyFuwalos))
                    enemyDrawPressure = true;
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
            if (!Bot.Deck.Any(c => c.Id == LastChain.Id)) return false;
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

        private bool StoryRaigeki() => Enemy.GetMonsterCount() > 0 &&
            (Enemy.GetMonsterCount() >= 2 || Enemy.GetMonsters().Any(c => evaluation.Threat(c) >= 2800) || CanPushDamage());
        private bool StoryFeatherDuster() => Enemy.GetSpellCount() > 0 &&
            (Enemy.GetSpellCount() >= 2 || Enemy.GetSpells().Any(c => c.IsFaceup() && c.IsFloodgate()) || CanPushDamage());
        private bool StoryDarkHole() => Enemy.GetMonsterCount() > 0 &&
            Enemy.GetMonsters().Sum(evaluation.Threat) > Bot.GetMonsters().Sum(evaluation.Keep) + 900;
        private bool StoryTorrentialTribute() => Duel.LastSummonPlayer == 1 && StoryDarkHole();
        private bool StoryNibiru() => Duel.Player == 1 && Enemy.GetMonsters().Count(c => c.IsFaceup()) >= 2 &&
            Enemy.GetMonsters().Sum(evaluation.Threat) > Bot.GetMonsters().Sum(evaluation.Keep) + 2200;
        private bool StoryEvenlyBattle() => Duel.Player == 0 && Duel.Phase == DuelPhase.Main1 && Duel.Turn > 1 &&
            Bot.GetFieldCount() == 0 && Enemy.GetFieldCount() >= 3 && Bot.Hand.Any(c => c.Id == _CardId.EvenlyMatched &&
                !DefaultCheckWhetherCardIsNegated(c));
        private bool CanPushDamage() => IsOurMain && Duel.Phase == DuelPhase.Main1 && Duel.Turn > 1 && Bot.GetMonsters().Any(c => c.IsAttack() && c.Attack > 0);
        private bool StoryMirrorForce() => Bot.UnderAttack && Enemy.GetMonsters().Any(c => c.IsAttack()) &&
            (Enemy.GetMonsters().Count(c => c.IsAttack()) >= 2 || Bot.BattlingMonster == null ||
             Enemy.BattlingMonster != null && Enemy.BattlingMonster.Attack >= Bot.BattlingMonster.GetDefensePower());

        private bool StoryLightningStorm()
        {
            float monsters = Enemy.GetMonsters().Where(c => c.IsAttack()).Sum(evaluation.Threat);
            float spells = Enemy.GetSpells().Sum(evaluation.Threat);
            if (Math.Max(monsters, spells) < 1200) return false;
            // Store the real description id, since the core may offer only one of the modes.
            lightningStormOption = monsters >= spells ? 0 : 1;
            return true;
        }

        private bool StoryReborn()
        {
            var target = Bot.Graveyard.Concat(Enemy.Graveyard).Where(c => StoryAiEvaluation.Has(c, CardType.Monster) && c.IsCanRevive())
                .OrderByDescending(evaluation.SummonValue).FirstOrDefault();
            if (target == null || Bot.MonsterZone.Take(5).All(c => c != null)) return false;
            AI.SelectCard(target);
            return true;
        }

        private float ActivationValue(ClientCard card)
        {
            var role = evaluation.Roles(card);
            float score = 1000;
            if (card.Location == CardLocation.Grave || card.Location == CardLocation.Removed) score += 800;
            if ((role & StoryAiEvaluation.Role.Draw) != 0) score += 1300;
            if ((role & StoryAiEvaluation.Role.Search) != 0) score += 1600;
            if ((role & StoryAiEvaluation.Role.Extend) != 0) score += Bot.GetMonsterCount() < 3 ? 1100 : 250;
            if (StoryAiEvaluation.Has(card, CardType.Field | CardType.Continuous)) score += 400;
            return score;
        }

        private bool GenericAllowed(ClientCard candidate, int description)
        {
            if (HasSpecific(ExecutorType.Activate, candidate) || DefaultCheckWhetherCardIsNegated(candidate) || !base.OnPreActivate(candidate)) return false;
            if (!EffectAllowed(candidate, description, out _)) return false;
            if (Duel.LastChainPlayer == 0 && candidate.Location != CardLocation.Grave && candidate.Location != CardLocation.Removed &&
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
            if (Duel.CurrentChainInfo.Any(c => c.ActivatePlayer == 0 && c.RelatedCard == Card && c.ActivateDescription == ActivateDescription)) return false;
            if (IsOurMain && Duel.CurrentChain.Count == 0 && Duel.MainPhase != null)
            {
                float score = ActivationValue(Card);
                for (int i = 0; i < Duel.MainPhase.ActivableCards.Count; i++)
                {
                    var candidate = Duel.MainPhase.ActivableCards[i];
                    int description = Duel.MainPhase.ActivableDescs[i];
                    if ((candidate != Card || description != ActivateDescription) && GenericAllowed(candidate, description) &&
                        ActivationValue(candidate) > score + .01f) return false;
                }
            }
            if (!EffectAllowed(Card, ActivateDescription, out var intent)) return false;
            CommitEffect(intent);
            return true;
        }

        private bool NormalAllowed(ClientCard card)
        {
            if (StoryAiEvaluation.Exodia(card)) return false;
            if (StoryAiEvaluation.HandTraps.Contains(card.Id) && (Bot.GetMonsterCount() > 0 || Enemy.GetMonsterCount() == 0)) return false;
            int level = StoryAiEvaluation.Level(card);
            if (level <= 4) return true;
            // Special no-tribute summons (Timelords etc.) are handled by dedicated executors.
            int count = level >= 7 ? 2 : 1;
            var materials = Bot.GetMonsters().OrderBy(c => evaluation.MaterialCost(c, HintMsg.Release)).Take(count).ToList();
            return materials.Count == count && evaluation.SummonValue(card) + 900 > materials.Sum(c => evaluation.MaterialCost(c, HintMsg.Release));
        }

        private bool StrategicNormalSummon()
        {
            if (HasSpecific(ExecutorType.Summon, Card) || HasSpecific(ExecutorType.SummonOrSet, Card) || !NormalAllowed(Card)) return false;
            if (Duel.MainPhase == null) return true;
            var candidates = Duel.MainPhase.SummonableCards.Where(c => NormalAllowed(c) && !HasSpecific(ExecutorType.Summon, c)).ToList();
            var potential = evaluation.DevelopmentBonuses(candidates.Where(c => StoryAiEvaluation.Level(c) <= 4).ToList());
            Func<ClientCard, float> score = c => evaluation.SummonValue(c) + (potential.TryGetValue(c, out float bonus) ? bonus : 0);
            return !candidates.Any(c => c != Card && score(c) > score(Card) + .01f);
        }

        private bool SpecialAllowed(ClientCard card, out StoryAiEvaluation.ExtraPlan plan)
        {
            plan = null;
            if (enemyDrawPressure && Bot.GetMonsters().Any(c => c.IsFaceup() && !c.IsDisabled()) &&
                Enemy.GetMonsterCount() == 0 && Bot.GetMonsters().Sum(StoryAiEvaluation.Attack) < Enemy.LifePoints) return false;
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
            return true;
        }

        private bool StrategicSpecialSummon()
        {
            if (HasSpecific(ExecutorType.SpSummon, Card) || !SpecialAllowed(Card, out var plan)) return false;
            // Preserve an already available direct lethal instead of consuming attackers.
            if (Card.Location == CardLocation.Extra && CanPushDamage() && Enemy.GetMonsterCount() == 0 &&
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
            if (StoryAiEvaluation.Has(card, CardType.Flip)) return true;
            // Do not hide an on-summon starter merely because the opponent has a large monster.
            if ((evaluation.Roles(card) & (StoryAiEvaluation.Role.Starter | StoryAiEvaluation.Role.Search | StoryAiEvaluation.Role.Extend)) != 0) return false;
            return base.OnSelectMonsterSummonOrSet(card);
        }

        private bool StrategicMonsterSet() => !HasSpecific(ExecutorType.Summon, Card) && NormalAllowed(Card) &&
            !StoryAiEvaluation.HandTraps.Contains(Card.Id) && (Bot.GetMonsterCount() == 0 || StoryAiEvaluation.Has(Card, CardType.Flip));
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
