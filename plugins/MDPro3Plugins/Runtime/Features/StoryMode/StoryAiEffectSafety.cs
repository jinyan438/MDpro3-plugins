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
        private bool ReadScriptEffect(EffectIntent intent)
        {
            var profile = StoryAiScriptEffects.Find(intent.Description);
            if (profile == null) return false;
            intent.DiscardCount = (profile.Flags & 32) != 0 ? 1 : 0;
            if (profile.Purpose == StoryAiScriptEffects.Kind.Negate) intent.Purpose = EffectPurpose.Negate;
            else if (profile.Purpose == StoryAiScriptEffects.Kind.StopAttack) intent.Purpose = EffectPurpose.StopAttack;
            else
            {
                intent.Purpose = EffectPurpose.TargetRemoval;
                intent.Hint = profile.Purpose == StoryAiScriptEffects.Kind.Destroy ? HintMsg.Destroy :
                    profile.Purpose == StoryAiScriptEffects.Kind.Banish ? HintMsg.Remove :
                    profile.Purpose == StoryAiScriptEffects.Kind.SendGrave ? HintMsg.ToGrave :
                    profile.Purpose == StoryAiScriptEffects.Kind.ReturnHand ? HintMsg.ReturnToHand : HintMsg.ToDeck;
                intent.TargetLocations = profile.Locations;
                if ((profile.Flags & 4) != 0) intent.TargetLocations &= CardLocation.MonsterZone;
                if ((profile.Flags & 8) != 0) intent.TargetLocations &= CardLocation.SpellZone;
                intent.FaceupOnly = (profile.Flags & 1) != 0;
                intent.FacedownOnly = (profile.Flags & 2) != 0;
                intent.OwnOnly = (profile.Flags & 16) != 0;
                intent.MinimumTargets = profile.Minimum;
            }
            return true;
        }

        private bool HasDirectLethal() => CanPushDamage() && Enemy.GetMonsterCount() == 0 &&
            Bot.GetMonsters().Where(c => c.IsAttack() && !c.Attacked).Sum(StoryAiEvaluation.Attack) >= Enemy.LifePoints;

        private bool AttackInterruptionUseful()
        {
            // Battle state comes from MSG_ATTACK, not merely whose turn it is.
            // This also handles an opposing effect that forces an unusual attack.
            var attacker = Bot.UnderAttack ? Enemy.BattlingMonster : Enemy.UnderAttack ? Bot.BattlingMonster : null;
            var defender = Bot.UnderAttack ? Bot.BattlingMonster : Enemy.BattlingMonster;
            if (attacker == null) return false;
            int attack = StoryAiEvaluation.Attack(attacker);
            if (Bot.UnderAttack)
            {
                int damage = evaluation.OwnBattleDamageBlocked ? 0 : defender == null ? attack : defender.IsAttack() ? Math.Max(0, attack - StoryAiEvaluation.Attack(defender)) : 0;
                bool losesMonster = defender != null && attack > 0 && attack >= defender.GetDefensePower() &&
                    (attack > defender.GetDefensePower() || defender.IsAttack()) && !BattleProtected(defender);
                return damage >= Bot.LifePoints || losesMonster || defender == null && damage > 0 || damage >= 1500;
            }
            // Never stop a winning/direct own attack. Cancelling a now-losing
            // attack after an opposing battle trick is a legitimate rescue.
            return defender != null && !StoryAiEvaluation.Hidden(defender) &&
                (defender.GetDefensePower() > attack && (!evaluation.OwnBattleDamageBlocked || defender.IsAttack() && !BattleProtected(attacker)) ||
                 defender.IsAttack() && defender.GetDefensePower() == attack && attack > 0 && !BattleProtected(attacker) &&
                 evaluation.Keep(attacker) > evaluation.Threat(defender) + 500);
        }

        private bool ThreatenedByLastEffect(ClientCard card)
        {
            if (!EnemyChain || DefaultCheckWhetherCardIsNegated(LastChain)) return false;
            int description = Duel.CurrentChainInfo.LastOrDefault()?.ActivateDescription ?? 0;
            var threat = DescribeEffect(LastChain, description);
            // A board wipe has no target list. S:P can save two own monsters
            // from Raigeki/Dark Hole; rejecting that pair would be self-harm too.
            if (card.Location == CardLocation.MonsterZone && CanDestroyByEffect(card) &&
                (threat.Purpose == EffectPurpose.BoardWipe || threat.Purpose == EffectPurpose.EnemyRemoval && threat.Fact?.AllTargets == true) &&
                (threat.TargetLocations & card.Location) != 0 && (threat.Fact?.TargetFilter == null || threat.Fact.TargetFilter(card))) return true;
            if (!Duel.LastChainTargets.Contains(card)) return false;
            if (LastChain.IsCode(_CardId.EffectVeiler, _CardId.InfiniteImpermanence, _CardId.BreakthroughSkill)) return true;
            return threat.Purpose == EffectPurpose.TargetRemoval || threat.Purpose == EffectPurpose.TargetNegate ||
                threat.Purpose == EffectPurpose.TemporaryPair;
        }

        private float TemporaryOwnCost(ClientCard card)
        {
            // Tokens do not return; an Xyz monster also loses all attached stock.
            float value = evaluation.BoardValue(card) * (Duel.Player == 0 && !card.Attacked ? .8f : .22f) + card.Overlays.Count * 450;
            if (StoryAiEvaluation.Has(card, CardType.Token)) value += evaluation.BoardValue(card);
            if (ThreatenedByLastEffect(card)) value -= evaluation.BoardValue(card) + 1800;
            return value;
        }

        private bool PlanTemporaryPair(EffectIntent intent)
        {
            var own = Bot.GetMonsters().Where(c => c.IsFaceup() && evaluation.CanTarget(c, intent.Source)).ToList();
            var enemy = Enemy.GetMonsters().Where(c => c.IsFaceup() && evaluation.CanTarget(c, intent.Source)).ToList();
            float best = 250;
            foreach (var first in own)
                foreach (var second in enemy.Concat(own.Where(c => c != first)))
                {
                    // Two own monsters are only worth hiding to escape a concrete
                    // hostile target. A random enemy spell is not such a threat.
                    if (second.Controller == 0 && !ThreatenedByLastEffect(first) && !ThreatenedByLastEffect(second)) continue;
                    float benefit = second.Controller == 1 ? evaluation.Threat(second) * .65f +
                        (evaluation.LiveInteraction(second) && second != LastChain ? 900 : 0) : -TemporaryOwnCost(second);
                    float gain = benefit - TemporaryOwnCost(first);
                    if (gain <= best) continue;
                    best = gain; intent.Target = first; intent.Partner = second;
                }
            return intent.Target != null && intent.Partner != null;
        }

        private float BanishedRecoveryValue(ClientCard card)
        {
            if (card.Controller != 0 || !card.IsFaceup()) return -10000;
            float value = (evaluation.Roles(card) & StoryAiEvaluation.Role.Grave) != 0 ? 2600 : 0;
            if (card.IsCanRevive() && StoryAiEvaluation.Has(card, CardType.Monster)) value += 600;
            // Recover our public resource; never feed the enemy graveyard merely
            // because a generic ToGrave hint regards every opposing card as good.
            return value;
        }

        private IList<ClientCard> SelectInteractionTargets(EffectIntent intent, IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            if (intent.Purpose == EffectPurpose.RecycleBanished && (hint == HintMsg.ToGrave || hint == HintMsg.Target ||
                intent.Fact != null && hint == intent.Fact.CustomHint))
                return cards.OrderByDescending(BanishedRecoveryValue).Take(min).ToList();
            if (intent.Purpose != EffectPurpose.TemporaryPair || hint != HintMsg.Remove && hint != HintMsg.Target) return null;
            // The core asks twice. Keep the pair across these target prompts,
            // rather than greedily spending a different own boss at each step.
            if (effectIntent != null && effectIntent.Source == intent.Source && effectIntent.Description == intent.Description)
                intent = effectIntent;
            if (intent.Target == null || intent.Partner == null) PlanTemporaryPair(intent);
            if (max >= 2 && min == 2 && cards.Contains(intent.Target) && cards.Contains(intent.Partner))
            { intent.TargetSelections = 2; return new List<ClientCard> { intent.Target, intent.Partner }; }
            ClientCard chosen = intent.TargetSelections == 0 && cards.Contains(intent.Target) ? intent.Target :
                cards.Contains(intent.Partner) ? intent.Partner : cards.Contains(intent.Target) ? intent.Target : null;
            if (chosen != null && min <= 1 && max >= 1) { intent.TargetSelections++; return new List<ClientCard> { chosen }; }
            if (cancelable) return new List<ClientCard>();
            return cards.OrderByDescending(c => c.Controller == 1 ? evaluation.Threat(c) : -TemporaryOwnCost(c)).Take(min).ToList();
        }
    }
}
