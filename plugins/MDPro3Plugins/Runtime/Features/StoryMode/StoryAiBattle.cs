using System;
using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    // A bounded search over attack assignments. Rebuilt after every response/chain/replay;
    // never continues a stale line after the opponent changes the board.
    internal static class StoryAiBattlePlanner
    {
        internal struct Exchange
        {
            internal bool Allowed, RemovesDefender;
            internal int EnemyDamage, OwnDamage;
            internal float Value;
        }
        internal sealed class Plan
        {
            internal int Attacker = -1, Defender = -1;
            internal float Value;
            internal int Nodes;
        }
        internal static Plan Find(Exchange[,] exchanges, int[] directDamage, bool[] canDirect,
            int enemyLife, int ownLife, int nodeLimit = 12000)
        {
            int attackers = exchanges.GetLength(0), defenders = exchanges.GetLength(1);
            if (attackers > 7 || defenders > 7 || attackers == 0) return new Plan();
            var memo = new Dictionary<(int, int, int, int), Plan>();
            int nodes = 0;
            Plan Search(int remaining, int targets, int enemyLp, int ownLp)
            {
                if (ownLp <= 0) return new Plan { Value = -2000000 };
                if (enemyLp <= 0) return new Plan { Value = 1000000 };
                var key = (remaining, targets, enemyLp, ownLp);
                if (memo.TryGetValue(key, out var cached)) return cached;
                var best = new Plan(); // Passing an attack is always preferable to a losing exchange.
                if (++nodes > nodeLimit) return best;
                for (int a = 0; a < attackers; a++)
                {
                    if ((remaining & (1 << a)) == 0) continue;
                    int next = remaining & ~(1 << a);
                    if ((targets == 0 || canDirect[a]) && directDamage[a] > 0)
                    {
                        int damage = directDamage[a];
                        float value = Math.Min(damage, enemyLp) * .65f + Search(next, targets, enemyLp - damage, ownLp).Value;
                        if (value > best.Value) best = new Plan { Attacker = a, Value = value };
                    }
                    for (int d = 0; d < defenders; d++)
                    {
                        if ((targets & (1 << d)) == 0) continue;
                        var move = exchanges[a, d];
                        if (!move.Allowed || move.OwnDamage >= ownLp) continue;
                        int afterTargets = move.RemovesDefender ? targets & ~(1 << d) : targets;
                        float value = move.Value + Math.Min(move.EnemyDamage, enemyLp) * .65f - move.OwnDamage * .85f +
                            Search(next, afterTargets, enemyLp - move.EnemyDamage, ownLp - move.OwnDamage).Value;
                        if (value > best.Value) best = new Plan { Attacker = a, Defender = d, Value = value };
                    }
                }
                memo[key] = best;
                return best;
            }
            var result = Search((1 << attackers) - 1, (1 << defenders) - 1, enemyLife, ownLife);
            result.Nodes = nodes;
            return result;
        }
    }

    public abstract partial class StoryLuckyExecutor
    {
        private StoryAiBattlePlanner.Plan PlanBattle(IList<ClientCard> attackers, IList<ClientCard> defenders)
        {
            var exchanges = new StoryAiBattlePlanner.Exchange[attackers.Count, defenders.Count];
            var directDamage = new int[attackers.Count];
            var canDirect = new bool[attackers.Count];
            for (int a = 0; a < attackers.Count; a++)
            {
                var attacker = attackers[a];
                string text = attacker.Data?.Description ?? "";
                bool noDirect = !attacker.CanDirectAttack && StoryAiEvaluation.Contains(text, "cannot attack directly", "不能直接攻击", "不能直接攻擊");
                directDamage[a] = noDirect ? 0 : Math.Max(0, attacker.Attack);
                canDirect[a] = attacker.CanDirectAttack && !noDirect;
                if (canDirect[a] && defenders.Count > 0 && StoryAiEvaluation.Contains(text, "half", "一半", "半分")) directDamage[a] /= 2;
                for (int d = 0; d < defenders.Count; d++)
                {
                    var defender = defenders[d];
                    var move = new StoryAiBattlePlanner.Exchange();
                    if (StoryAiEvaluation.Hidden(defender))
                    {
                        // A set monster's cached id/stats may be present in the client. Do not use
                        // them. Probe with a suitable attacker, then replan after it is revealed.
                        int risk = Math.Max(0, 1800 - attacker.Attack);
                        move.Allowed = attacker.Attack >= 1400 && risk < Bot.LifePoints;
                        move.OwnDamage = risk;
                        move.Value = 450 - risk * .5f;
                        // No speculative destruction or damage can turn this probe into a lethal.
                    }
                    else
                    {
                        int oldAttack = attacker.RealPower, oldDefense = defender.RealPower;
                        attacker.RealPower = attacker.Attack;
                        defender.RealPower = defender.GetDefensePower();
                        bool allowed = OnPreBattleBetween(attacker, defender);
                        int attack = attacker.RealPower, defense = defender.RealPower;
                        attacker.RealPower = oldAttack;
                        defender.RealPower = oldDefense;
                        bool ordinary = attack == attacker.Attack && defense == defender.GetDefensePower() &&
                            !attacker.IsMonsterInvincible() && !defender.IsMonsterInvincible();
                        if (allowed && ordinary)
                        {
                            bool kills = attack > defense || attack == defense && defender.IsAttack() && attack > 0;
                            bool loses = defender.IsAttack() && attack <= defense && defense > 0;
                            move.Allowed = kills;
                            move.RemovesDefender = kills;
                            move.EnemyDamage = defender.IsAttack() ? Math.Max(0, attack - defense) : 0;
                            move.OwnDamage = Math.Max(0, defense - attack);
                            move.Value = (kills ? evaluation.Threat(defender) : 0) - (loses ? evaluation.Keep(attacker) : 0);
                        }
                        else if (allowed && attack > defense)
                        {
                            // Legacy RealPower sometimes uses 9999 for battle immunity/removal.
                            // It is a tactic flag, not real ATK, damage, or guaranteed destruction.
                            move.Allowed = true;
                            move.Value = defender.IsMonsterInvincible() ? 0 : evaluation.Threat(defender) * .35f;
                        }
                    }
                    exchanges[a, d] = move;
                }
            }
            return StoryAiBattlePlanner.Find(exchanges, directDamage, canDirect, Enemy.LifePoints, Bot.LifePoints);
        }

        public override BattlePhaseAction OnBattle(IList<ClientCard> attackers, IList<ClientCard> defenders)
        {
            var plan = PlanBattle(attackers, defenders);
            if (plan.Attacker >= 0)
                return AI.Attack(attackers[plan.Attacker], plan.Defender < 0 ? null : defenders[plan.Defender]);
            if (Duel.BattlePhase?.CanMainPhaseTwo == true) return AI.ToMainPhase2();
            if (Duel.BattlePhase?.CanEndPhase == true) return AI.ToEndPhase();
            return null;
        }

        public override BattlePhaseAction OnSelectAttackTarget(ClientCard attacker, IList<ClientCard> defenders)
        {
            var plan = PlanBattle(new[] { attacker }, defenders);
            return plan.Attacker >= 0 ? AI.Attack(attacker, plan.Defender < 0 ? null : defenders[plan.Defender]) : null;
        }
    }
}
