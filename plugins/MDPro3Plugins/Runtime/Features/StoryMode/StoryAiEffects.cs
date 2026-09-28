using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public abstract partial class StoryLuckyExecutor
    {
        internal enum EffectPurpose { Unknown, Safe, TargetRemoval, TargetNegate, EnemyRemoval, BoardWipe, Negate, LinkAttack, LinkEquip, RevivalSwap, LinkBanishCost, FairyTribute, QuickLink, ReturnForBody, StopAttack, TemporaryPair, TemporaryHandRemoval, RecycleBanished, ChangeLevel, BattleDebuff, OverlayModes, Draw, BalanceField }
        private sealed class EffectIntent
        {
            internal ClientCard Source, Target, Cost;
            internal int Description, Hint = HintMsg.Destroy;
            internal EffectPurpose Purpose;
            internal StoryAiEvaluation.ExtraPlan Summon;
            internal CardLocation TargetLocations = CardLocation.MonsterZone | CardLocation.SpellZone;
            internal bool FaceupOnly, FacedownOnly, MonsterOnly, OwnOnly, NonTargeting;
            internal int DiscardCount, MinimumTargets = 1, TargetSelections;
            internal StoryAiEvaluation.TacticalFact Fact;
            internal StoryAiLevelEffects.Profile LevelEffect;
            internal StoryAiEvaluation.LevelPlan LevelPlan;
            internal ClientCard Partner;
        }
        private EffectIntent effectIntent;
        private readonly Dictionary<int, HashSet<int>> accesscodeAttributes = new Dictionary<int, HashSet<int>>();
        private HashSet<int> CostAttributes(ClientCard card)
        {
            int id = StoryAiEvaluation.CardIdentity(card);
            if (!accesscodeAttributes.TryGetValue(id, out var used)) accesscodeAttributes[id] = used = new HashSet<int>();
            return used;
        }

        // Description indices come from the installed Lua scripts, not printed paragraph
        // numbers (passive effects and copied effects make those different).
        private EffectIntent DescribeEffect(ClientCard card, int description)
        {
            var intent = new EffectIntent { Source = card, Description = description };
            if (StoryAiEvaluation.IsHandTrap(card)) { intent.Purpose = EffectPurpose.Safe; return intent; }
            intent.LevelEffect = StoryAiLevelEffects.Find(card, description);
            if (intent.LevelEffect != null) { intent.Purpose = EffectPurpose.ChangeLevel; return intent; }
            int sourceId = description >= 16000 ? description / 16 : StoryAiEvaluation.CardIdentity(card);
            int index = description >= 16000 ? description % 16 : 0;
            intent.Fact = StoryAiEvaluation.Tactic(card, description);
            if (intent.Fact != null)
            {
                var fact = intent.Fact;
                if (fact.Modes?.Length > 0)
                    intent.Fact = fact = fact.Modes.OrderByDescending(f => Enemy.GetMonsters().Concat(Enemy.GetSpells())
                        .Where(c => ((int)c.Location & f.Locations) != 0 && (f.TargetFilter == null || f.TargetFilter(c)) && CanDestroyByEffect(c))
                        .Sum(evaluation.Threat)).ThenBy(f => f.Option).First();
                intent.Purpose = fact.Purpose; intent.Hint = fact.Hint != 0 ? fact.Hint : HintMsg.Destroy;
                intent.TargetLocations = (CardLocation)fact.Locations;
                intent.MonsterOnly = fact.MonsterOnly; intent.NonTargeting = fact.NonTargeting;
                intent.DiscardCount = fact.DiscardCount;
                return intent;
            }
            if (evaluation.ModelledComboAction(card, description) || evaluation.ModelledComboTrigger(card, description))
            { intent.Purpose = evaluation.IsReturnCombo(card, description) ? EffectPurpose.ReturnForBody : EffectPurpose.Safe; return intent; }
            if (ReadScriptEffect(intent)) return intent;
            string text = (sourceId == card.Id ? card.Data : NamedCard.Get(sourceId))?.Description ?? "";
            ReadGenericEffect(intent, text, index);
            return intent;
        }

        private IEnumerable<ClientCard> RemovalTargets(EffectIntent intent)
        {
            var targets = Enemy.GetMonsters().Concat(Enemy.GetSpells()).Concat(Enemy.Graveyard).Concat(Enemy.Banished).Where(c =>
                (c.Location & intent.TargetLocations) != 0 && (!intent.FaceupOnly || c.IsFaceup()) && (!intent.FacedownOnly || c.IsFacedown()));
            if (intent.OwnOnly) return Enumerable.Empty<ClientCard>();
            if (intent.Fact?.SpecialOnly == true) targets = targets.Where(c => c.IsSpecialSummoned);
            if (intent.Fact?.TargetFilter != null) targets = targets.Where(intent.Fact.TargetFilter);
            if (StoryAiEvaluation.Has(intent.Source, CardType.Spell | CardType.Trap) && intent.Fact?.AllTargets != true)
                targets = targets.Where(c => c.Location != CardLocation.SpellZone || IsOurMain || Duel.Phase == DuelPhase.End ||
                    Duel.ChainTargets.Contains(intent.Source) || c.IsFaceup() && (c.IsFloodgate() || Duel.CurrentChain.Contains(c) ||
                    StoryAiEvaluation.Has(c, CardType.Continuous | CardType.Field | CardType.Equip)));
            if (!intent.NonTargeting && intent.Purpose != EffectPurpose.EnemyRemoval && intent.Purpose != EffectPurpose.LinkBanishCost)
                targets = targets.Where(c => evaluation.CanTarget(c, intent.Source));
            if (intent.Hint == HintMsg.Destroy) targets = targets.Where(CanDestroyByEffect);
            if (intent.Hint == HintMsg.Disable) targets = targets.Where(c => c.IsFaceup() && !c.IsDisabled() &&
                (EnemyChain && c == LastChain || IsOurMain && (c.IsFloodgate() || evaluation.LiveInteraction(c)) || c.IsMonsterShouldBeDisabledBeforeItUseEffect()));
            return targets;
        }

        private static bool CanDestroyByEffect(ClientCard c) => StoryAiEvaluation.Hidden(c) || c.IsDisabled() ||
            !Regex.IsMatch(c.Data?.Description ?? "", @"cannot be destroyed by (?:battle or )?card effects|不会被(?:战斗[·・和以及]*|战斗以及)?效果破坏|不會被(?:戰鬥[·・和以及]*|戰鬥以及)?效果破壞", RegexOptions.IgnoreCase);

        private bool EffectAllowed(ClientCard card, int description, out EffectIntent intent)
        {
            intent = DescribeEffect(card, description);
            var described = intent;
            if (intent.Fact?.Life > 0 && Bot.LifePoints <= intent.Fact.Life) return false;
            switch (intent.Purpose)
            {
                case EffectPurpose.Draw: return !evaluation.DrawLocked && Bot.Deck.Count > intent.Fact.DrawCount &&
                    (!(StoryAiEvaluation.Facts(card).BothDamageShield || StoryAiEvaluation.Facts(card).OpponentDamageShield) || !HasDirectLethal());
                case EffectPurpose.BalanceField: return Enemy.GetFieldCount() > Bot.GetFieldCount() + (card.Location == CardLocation.Hand ? 1 : 0);
                case EffectPurpose.BattleDebuff: return Enemy.GetMonsterCount() > 0 && CanPushDamage();
                case EffectPurpose.OverlayModes:
                    return Duel.Player == 1 || Duel.Phase == DuelPhase.End || Duel.ChainTargets.Contains(card) ||
                        card.Overlays.Any(code =>
                            (NamedCard.Get(code)?.Type & described.Fact.DrawOverlayType) != 0 ||
                            Enemy.GetFieldCount() > 0 && (NamedCard.Get(code)?.Type & described.Fact.RemovalOverlayType) != 0);
                case EffectPurpose.ChangeLevel: return PlanLevelEffect(intent);
                case EffectPurpose.StopAttack: return AttackInterruptionUseful();
                case EffectPurpose.TemporaryPair: return PlanTemporaryPair(intent);
                case EffectPurpose.TemporaryHandRemoval:
                    return Enemy.Hand.Count > 0 && (Duel.Player == 1 || !CanPushDamage() ||
                        EnemyChain && Duel.LastChainTargets.Contains(card));
                case EffectPurpose.RecycleBanished:
                    intent.Target = Bot.Banished.Where(c => c.IsFaceup()).OrderByDescending(BanishedRecoveryValue).FirstOrDefault();
                    return intent.Target != null && BanishedRecoveryValue(intent.Target) > 0;
                case EffectPurpose.ReturnForBody: return evaluation.CanPayReturnCombo(card, description);
                case EffectPurpose.Negate:
                    intent.Target = LastChain;
                    return EnemyChain && !DefaultCheckWhetherCardIsNegated(LastChain) &&
                        (!intent.MonsterOnly || StoryAiEvaluation.Has(LastChain, CardType.Monster)) &&
                        (intent.Fact?.SpellTrapOnly != true || StoryAiEvaluation.Has(LastChain, CardType.Spell | CardType.Trap)) &&
                        (intent.Fact == null || intent.Fact.AttackCost <= 0 || StoryAiEvaluation.Attack(card) >= intent.Fact.AttackCost) && PlanDiscard(intent) && PlanTacticalCost(intent);
                case EffectPurpose.TargetRemoval:
                case EffectPurpose.TargetNegate:
                case EffectPurpose.EnemyRemoval:
                case EffectPurpose.LinkBanishCost:
                case EffectPurpose.FairyTribute:
                    intent.Target = RemovalTargets(intent).OrderByDescending(c => RemovalValue(c, described)).FirstOrDefault();
                    if (intent.Target == null || RemovalValue(intent.Target, intent) <= 0 || !PlanDiscard(intent)) return false;
                    if (RemovalTargets(intent).Count(c => RemovalValue(c, described) > 0) < intent.MinimumTargets) return false;
                    if (intent.Fact?.PreventDirect == true && intent.Target.Location == CardLocation.Grave && HasDirectLethal()) return false;
                    if (intent.Purpose == EffectPurpose.TargetNegate && StoryAiEvaluation.Facts(card).OpponentDamageShield && !StoryDarkRuler()) return false;
                    if (!PlanTacticalCost(intent)) return false;
                    return true;
                case EffectPurpose.BoardWipe:
                    bool Affected(ClientCard c) => (c.Location & described.TargetLocations) != 0 &&
                        (described.Fact?.TargetFilter == null || described.Fact.TargetFilter(c)) && CanDestroyByEffect(c);
                    float own = Bot.GetMonsters().Concat(Bot.GetSpells()).Where(c => Affected(c) &&
                        (described.Fact?.ExcludeSelf != true || c != card)).Sum(evaluation.BoardScore);
                    float enemy = Enemy.GetMonsters().Concat(Enemy.GetSpells()).Where(Affected).Sum(evaluation.BoardScore);
                    return enemy > own + 700 && PlanTacticalCost(intent);
                case EffectPurpose.RevivalSwap:
                    intent.Target = Bot.Graveyard.Where(c => StoryAiEvaluation.Has(c, CardType.Monster) && c.IsCanRevive() && (described.Fact?.TargetFilter == null || described.Fact.TargetFilter(c)))
                        .OrderByDescending(evaluation.BoardScore).FirstOrDefault();
                    return intent.Target != null && evaluation.BoardValue(intent.Target) > evaluation.BoardValue(card) + 500;
                case EffectPurpose.QuickLink:
                    intent.Summon = Bot.ExtraDeck.Where(c => StoryAiEvaluation.Has(c, CardType.Link)).Select(c => evaluation.PlanExtra(c, card))
                        .Where(p => p != null).OrderByDescending(p => p.Gain).FirstOrDefault();
                    return intent.Summon != null;
                default: return true;
            }
        }

        private bool PlanTacticalCost(EffectIntent intent)
        {
            var fact = intent.Fact;
            if (fact != null && intent.Source.Overlays.Count < fact.OverlayCost) return false;
            if (fact?.SelfCost == true && intent.Purpose != EffectPurpose.Negate && intent.Target != null &&
                evaluation.BoardValue(intent.Source) > RemovalValue(intent.Target, intent) + 500) return false;
            if (fact?.CostFilter == null) return true;
            if (fact.DiscardCount > 0) return PlanDiscard(intent);
            intent.Cost = Bot.Hand.Concat(Bot.GetMonsters()).Concat(Bot.GetSpells()).Concat(Bot.Graveyard).Concat(Bot.ExtraDeck)
                .Where(c => ((int)c.Location & fact.CostLocations) != 0 && fact.CostFilter(c) &&
                    (!fact.AttributeCost || !CostAttributes(intent.Source).Contains(StoryAiEvaluation.Attribute(c))))
                .OrderBy(c => evaluation.MaterialCost(c, fact.CostHint)).FirstOrDefault();
            if (intent.Cost == null) return false;
            float benefit = intent.Target == null ? 0 : evaluation.Threat(intent.Target) + (intent.Purpose == EffectPurpose.Negate ? 1400 : 0);
            return intent.Cost.Location != CardLocation.MonsterZone || intent.Cost != intent.Source && evaluation.BoardValue(intent.Cost) + 300 < benefit;
        }

        private void CommitEffect(EffectIntent intent)
        {
            effectIntent = intent;
            if (intent.Purpose == EffectPurpose.ChangeLevel) CommitLevelIntent(intent);
            evaluation.NoteDevelopmentEffect(intent.Source, intent.Description);
            evaluation.NoteInteractionUse(intent.Source, intent.Description);
            CommitExtraPlan(intent.Summon);
        }

        private EffectIntent SelectingEffect()
        {
            // During resolution, the latest effect selected may belong to another chain link.
            int solving = Duel.SolvingChainIndex;
            if (solving > 0 && solving <= Duel.CurrentChainInfo.Count)
            {
                var chain = Duel.CurrentChainInfo[solving - 1];
                if (chain.ActivatePlayer == 0 && chain.RelatedCard != null)
                    return levelChangeIntents.LastOrDefault(p => p.Source == chain.RelatedCard && p.LevelEffect.MatchesDescription(chain.ActivateDescription)) ??
                        DescribeEffect(chain.RelatedCard, chain.ActivateDescription);
                return null;
            }
            return effectIntent != null && effectIntent.Source == Card && effectIntent.Description == ActivateDescription ? effectIntent : null;
        }

        private IList<ClientCard> EffectSelection(IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            var intent = SelectingEffect();
            if (intent == null) return null;
            var levelTarget = SelectLevelTargets(cards, min, max, hint, intent);
            if (levelTarget != null) return levelTarget;
            var safetySelection = SelectInteractionTargets(intent, cards, min, max, hint, cancelable);
            if (safetySelection != null) return safetySelection;
            if (intent.Purpose == EffectPurpose.ReturnForBody && hint != HintMsg.SpSummon && max == 1)
            {
                var target = cards.Where(c => evaluation.ReturnComboTargetAllowed(intent.Source, intent.Description, c))
                    .OrderBy(evaluation.BoardScore).FirstOrDefault();
                if (target != null) return new List<ClientCard> { target };
            }
            if (intent.Fact?.CostFilter != null && hint == intent.Fact.CostHint)
            {
                var costs = cards.Where(c => c.Controller == 0 && ((int)c.Location & intent.Fact.CostLocations) != 0 &&
                    intent.Fact.CostFilter(c) && (!intent.Fact.AttributeCost || !CostAttributes(intent.Source).Contains(StoryAiEvaluation.Attribute(c))))
                    .OrderBy(c => evaluation.MaterialCost(c, hint)).Take(Math.Max(1, min)).ToList();
                if (costs.Count >= min)
                {
                    if (intent.Fact.AttributeCost) foreach (var cost in costs) CostAttributes(intent.Source).Add(StoryAiEvaluation.Attribute(cost));
                    return costs;
                }
            }
            if (intent.DiscardCount > 0 && hint == HintMsg.Discard && intent.Cost != null && cards.Contains(intent.Cost) && min <= 1 && max >= 1)
                return new List<ClientCard> { intent.Cost };
            if (intent.Purpose == EffectPurpose.QuickLink && hint == HintMsg.SpSummon)
            {
                var revised = cards.Where(c => c.Location == CardLocation.Extra && StoryAiEvaluation.Has(c, CardType.Link))
                    .Select(c => evaluation.PlanExtra(c, intent.Source)).Where(p => p != null).OrderByDescending(p => p.Gain).FirstOrDefault();
                if (revised != null) { CommitExtraPlan(revised); return new List<ClientCard> { revised.Destination }; }
                if (cancelable) return new List<ClientCard>();
            }
            if (intent.Purpose == EffectPurpose.LinkAttack && hint == HintMsg.Target ||
                intent.Purpose == EffectPurpose.LinkEquip && (hint == HintMsg.Equip || hint == HintMsg.Target))
                return cards.OrderByDescending(c => StoryAiEvaluation.LinkRating(c) * 1000 + StoryAiEvaluation.Attack(c) * .1f).Take(Math.Max(1, min)).ToList();
            if (intent.Purpose == EffectPurpose.RevivalSwap && hint == HintMsg.SpSummon)
                return cards.OrderByDescending(evaluation.BoardScore).Take(min).ToList();
            if (intent.Purpose == EffectPurpose.FairyTribute && (hint == HintMsg.Release || hint == HintMsg.Tribute))
            {
                if (intent.Cost != null && cards.Contains(intent.Cost)) return new List<ClientCard> { intent.Cost };
                if (cancelable) return new List<ClientCard>();
                return cards.OrderBy(evaluation.BoardScore).Take(min).ToList();
            }
            if (intent.Purpose == EffectPurpose.LinkBanishCost && hint == HintMsg.Remove)
            {
                var cost = cards.Where(c => c != intent.Source && !CostAttributes(intent.Source).Contains(StoryAiEvaluation.Attribute(c)))
                    .OrderBy(c => evaluation.MaterialCost(c, hint)).FirstOrDefault();
                if (cost == null) return cancelable ? new List<ClientCard>() : null;
                CostAttributes(intent.Source).Add(StoryAiEvaluation.Attribute(cost));
                return new List<ClientCard> { cost };
            }
            if ((intent.Purpose == EffectPurpose.TargetRemoval || intent.Purpose == EffectPurpose.TargetNegate || intent.Purpose == EffectPurpose.EnemyRemoval ||
                intent.Purpose == EffectPurpose.LinkBanishCost || intent.Purpose == EffectPurpose.FairyTribute) && (hint == intent.Hint || hint == HintMsg.Target))
            {
                // Some scripts use the same hint for a banish cost and its subsequent target.
                if (!cards.Any(c => (c.Location & intent.TargetLocations) != 0)) return null;
                var eligible = new HashSet<ClientCard>(RemovalTargets(intent));
                var targets = cards.Where(c => eligible.Contains(c) && RemovalValue(c, intent) > 0)
                    .OrderByDescending(c => RemovalValue(c, intent)).Take(max).ToList();
                if (targets.Count >= min) return targets;
                if (cancelable) return new List<ClientCard>();
            }
            return null;
        }

        public override void OnChainEnd()
        {
            base.OnChainEnd();
            AI.SelectNumber(-1);
            effectIntent = null;
            levelChangeIntents.Clear();
            resourceSelection = null;
            CommitExtraPlan(null);
        }

        public override void OnNewPhase()
        {
            base.OnNewPhase();
            AI.SelectNumber(-1);
            effectIntent = null;
            levelChangeIntents.Clear();
            resourceSelection = null;
            CommitExtraPlan(null);
        }
    }
}
