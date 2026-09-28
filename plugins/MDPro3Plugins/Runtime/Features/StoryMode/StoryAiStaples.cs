using System.Linq;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public abstract partial class StoryLuckyExecutor
    {
        private bool StoryGhostOgre()
        {
            if (!EnemyChain || LastChain.Controller != 1 || !LastChain.IsFaceup() ||
                (LastChain.Location != CardLocation.MonsterZone && LastChain.Location != CardLocation.SpellZone) ||
                !CanDestroyByEffect(LastChain)) return false;
            // Destroying a body denies future material/effects; continuous cards must
            // stay face-up to resolve. No claim that destruction negates every effect.
            return LastChain.Location == CardLocation.MonsterZone ||
                StoryAiEvaluation.Has(LastChain, CardType.Continuous | CardType.Field | CardType.Equip);
        }

        private bool StoryDarkRuler()
        {
            var targets = Enemy.GetMonsters().Where(c => c.IsFaceup() && !c.IsDisabled() && StoryAiEvaluation.Has(c, CardType.Effect)).ToList();
            if (targets.Count == 0) return false;
            if (CanPushDamage() && Duel.MainPhase?.CanBattlePhase == true &&
                !targets.Any(c => c.IsMonsterDangerous() || c.IsFloodgate()) &&
                PlanBattle(Bot.GetMonsters().Where(c => c.IsAttack() && !c.Attacked).ToList(), Enemy.GetMonsters()).Value >= 1000000) return false;
            return targets.Any(c => evaluation.LiveInteraction(c) || c.IsFloodgate() || c.IsMonsterDangerous()) ||
                targets.Count >= 2 && targets.Any(c => evaluation.Threat(c) > 2500);
        }
    }
}
