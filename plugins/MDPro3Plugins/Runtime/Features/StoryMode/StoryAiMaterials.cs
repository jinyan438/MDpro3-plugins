using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        internal sealed class ExtraPlan
        {
            internal ClientCard Destination;
            internal List<ClientCard> Materials;
            internal int Hint;
            internal int Zone = -1;
            internal float Gain;
        }

        private sealed class Recipe
        {
            internal int Min = 2, Max = 7;
            internal bool Effect, Normal, NonLink, DifferentNames, NonToken, ExtraOnly, EnemyOne, Tuner;
            internal int Race, NonTunerRace;
        }

        internal static int LinkRating(ClientCard c) => Has(c, CardType.Link) ? Math.Max(c.LinkCount, Level(c)) : 0;
        internal static int Race(ClientCard c) => c.Race != 0 ? c.Race : c.Data?.Race ?? 0;
        internal static int Attribute(ClientCard c) => c.Attribute != 0 ? c.Attribute : c.Data?.Attribute ?? 0;

        internal bool LiveInteraction(ClientCard c)
        {
            if (c.IsDisabled() || HandTraps.Contains(c.Id)) return false;
            switch (c.Id)
            {
                case 84815190: case 27548199: case 88581108: case 98127546: case 63101468: case 65741786: return true;
                case 4280258: return c.Location != CardLocation.MonsterZone || Attack(c) >= 800;
            }
            return c.IsFloodgate() || Regex.IsMatch(c.Data?.Description ?? "",
                @"negate (?:the|that|its) (?:activation|effect)|(?:发动|發動|发動|效果)[^。\n]{0,12}(?:无效|無效|無効)", RegexOptions.IgnoreCase);
        }

        // The same scale values a body before and after a summon. Text ranking bonuses for
        // searches, draws and summons are deliberately NOT added together as guaranteed profit.
        internal float BoardValue(ClientCard c, int projectedAttack = -1)
        {
            if (Hidden(c)) return Threat(c);
            if (!Has(c, CardType.Monster)) return Threat(c);
            int attack = projectedAttack >= 0 ? projectedAttack : Attack(c);
            if (Has(c, CardType.Token)) return 180 + attack * .18f;
            float value = 350 + Math.Max(attack, Defense(c) * .45f) * .55f;
            if (Has(c, CardType.Effect)) value += 250;
            if (LiveInteraction(c)) value += c.Id == 4280258 ? Math.Min(4, attack / 800) * 900 : 1900;
            if (!c.IsDisabled() && c.IsFloodgate()) value += 2000;
            if (!c.IsDisabled() && (c.IsMonsterInvincible() || c.IsMonsterDangerous())) value += 700;
            if (!c.IsDisabled() && c.Id == 21887175) value += 1400; // targeting protection, attack redirection and battle effect
            if (Has(c, CardType.Link)) value += LinkRating(c) * 180;
            if (c.IsDisabled()) value *= .65f;
            if (c.Attacked && duel.Player == 0) value -= 180;
            return Math.Max(100, value);
        }

        internal bool ProtectedBody(ClientCard c) => c.Controller == 0 && c.Location == CardLocation.MonsterZone &&
            !c.IsDisabled() && (LiveInteraction(c) || Has(c, CardType.Link) && LinkRating(c) >= 3 ||
            Has(c, CardType.Synchro | CardType.Fusion) && Level(c) >= 7 || Has(c, CardType.Xyz) && Attack(c) >= 2500);

        private Recipe MaterialRecipe(ClientCard destination)
        {
            switch (destination.Id)
            {
                case 86066372: return new Recipe { Effect = true };
                case 4280258: return new Recipe { DifferentNames = true, NonToken = true };
                case 65741786: return new Recipe { NonLink = true, Max = 2 };
                case 90290572: return new Recipe { Race = (int)CardRace.Fairy, Max = 2 };
                case 48589580: return new Recipe { Race = (int)CardRace.Fairy };
                case 50588353: return new Recipe { Tuner = true, Max = 2 };
                case 21887175: return new Recipe { ExtraOnly = true };
                case 98127546: return new Recipe { Effect = true, Min = 4, EnemyOne = true };
                case 38342335: return new Recipe { DifferentNames = true };
                case 2857636: return new Recipe { DifferentNames = true, Max = 2 };
                case 63101468: return new Recipe { NonTunerRace = (int)CardRace.Fairy };
                case 84815190: case 27548199: case 74586817: case 73580471: return new Recipe();
                case 88581108: return new Recipe();
                case 46772449: return new Recipe { Max = 2 };
            }
            string recipe = (destination.Data?.Description ?? "").Split('\n')[0].Trim();
            // Only accept recipes whose entire material clause is understood. An unknown
            // archetype/alternate procedure is unknown cost, never a free extra-deck summon.
            if (Has(destination, CardType.Link))
            {
                bool differentNames = recipe.StartsWith("卡名不同的", StringComparison.Ordinal) ||
                    recipe.IndexOf(" with different names", StringComparison.OrdinalIgnoreCase) >= 0;
                recipe = Regex.Replace(recipe, @"^卡名不同的| with different names", "", RegexOptions.IgnoreCase);
                var en = Regex.Match(recipe, @"^(?<n>[1-7])(?<plus>\+)?\s+(?<kind>Effect |non-Link |Normal )?[Mm]onsters?(?<tail>, except Tokens)?\s*$", RegexOptions.IgnoreCase);
                var zh = Regex.Match(recipe, @"^(?<kind>效果|连接怪兽以外的|連接怪獸以外的|通常)?怪[兽獸](?<n>[1-7])只(?<plus>以上)?(?<tail>.*)$");
                var m = en.Success ? en : zh;
                if (!m.Success) return null;
                var result = new Recipe { Min = int.Parse(m.Groups["n"].Value), DifferentNames = differentNames };
                result.Max = m.Groups["plus"].Success ? 7 : result.Min;
                string kind = m.Groups["kind"].Value;
                result.Normal = Contains(kind, "Normal", "通常");
                result.Effect = Contains(kind, "Effect", "效果");
                result.NonLink = Contains(kind, "non-Link", "连接", "連接");
                string tail = m.Groups["tail"].Value;
                if (tail.Length > 0 && !Regex.IsMatch(tail, @"^(, except Tokens|[（(]衍生物除外[）)])$", RegexOptions.IgnoreCase)) return null;
                result.NonToken = tail.Length > 0;
                return result;
            }
            if (Has(destination, CardType.Synchro) && Regex.IsMatch(recipe,
                @"^(?:1 )?Tuner\s*\+\s*1\+\s*non-Tuner monsters?$|^(?:调整|調整)[＋+](?:调整|調整)以外的怪[兽獸]1只以上$", RegexOptions.IgnoreCase)) return new Recipe();
            if (Has(destination, CardType.Xyz))
            {
                var m = Regex.Match(recipe, @"^(?:(?<level>\d+)[星阶階]怪[兽獸][×x](?<n>[2-7])|(?<n>[2-7]) Level (?<level>\d+) monsters)\s*$", RegexOptions.IgnoreCase);
                if (m.Success && int.Parse(m.Groups["level"].Value) == Level(destination))
                    return new Recipe { Min = int.Parse(m.Groups["n"].Value), Max = int.Parse(m.Groups["n"].Value) };
            }
            return null;
        }

        private bool ValidMaterials(ClientCard destination, Recipe recipe, List<ClientCard> cards, Func<ClientCard, bool> fromExtra = null)
        {
            if (cards.Count < recipe.Min || cards.Count > recipe.Max) return false;
            if (cards.Count(c => c.Controller == 1) > (recipe.EnemyOne ? 1 : 0)) return false;
            if (recipe.Effect && cards.Any(c => !Has(c, CardType.Effect))) return false;
            if (recipe.Normal && cards.Any(c => !Has(c, CardType.Normal))) return false;
            if (recipe.NonLink && cards.Any(c => Has(c, CardType.Link))) return false;
            if (recipe.NonToken && cards.Any(c => Has(c, CardType.Token))) return false;
            if (recipe.Race != 0 && cards.Any(c => (Race(c) & recipe.Race) == 0)) return false;
            if (recipe.DifferentNames && cards.Select(c => c.Id).Distinct().Count() != cards.Count) return false;
            if (recipe.ExtraOnly && cards.Any(c => fromExtra != null ? !fromExtra(c) : c.LastLocation != CardLocation.Extra)) return false;
            if (recipe.Tuner && !cards.Any(c => Has(c, CardType.Tuner))) return false;
            if (Has(destination, CardType.Link))
            {
                int totals = 1;
                foreach (var c in cards)
                {
                    int rating = LinkRating(c);
                    totals = (totals << 1) | (rating > 0 && rating <= 8 ? totals << rating : 0);
                }
                int required = LinkRating(destination);
                return required > 0 && required <= 8 && (totals & (1 << required)) != 0;
            }
            if (Has(destination, CardType.Xyz)) return cards.All(c => !Has(c, CardType.Link | CardType.Xyz | CardType.Token) && Level(c) == Level(destination));
            if (Has(destination, CardType.Synchro)) return cards.Count(c => Has(c, CardType.Tuner)) == 1 && cards.Count > 1 &&
                cards.All(c => !Has(c, CardType.Link | CardType.Xyz)) && cards.Sum(Level) == Level(destination) &&
                (recipe.NonTunerRace == 0 || cards.Where(c => !Has(c, CardType.Tuner)).All(c => (Race(c) & recipe.NonTunerRace) != 0));
            return false;
        }

        private float ImmediatePayoff(ClientCard c)
        {
            switch (c.Id)
            {
                case 50588353: return Bot.Deck.Concat(Bot.Hand).Any(x => Has(x, CardType.Tuner) && Level(x) <= 3) ? 2200 : 0;
                case 90290572: return 1400; // one search/mill, not both plus a fictitious summon
                case 48589580: return Bot.Hand.Count > 0 ? 600 : 0;
                case 63101468: return 900;
                case 27548199: return Bot.Graveyard.Any(x => Has(x, CardType.Link)) ? 1100 : 0;
                case 4280258: case 65741786: case 21887175: case 88581108: case 74586817: return 0;
                case 84815190: return Enemy.GetFieldCount() > 0 ? 900 : 0;
                case 86066372: return Math.Min(2, Enemy.GetFieldCount()) * 1300;
                case 73580471: return Math.Max(0, Enemy.GetMonsters().Sum(BoardScore) + Enemy.GetSpells().Sum(Threat) -
                    Bot.GetMonsters().Sum(BoardScore) - Bot.GetSpells().Sum(Threat));
            }
            var role = Roles(c);
            // A small bounded potential, not all the text's conditional benefits at once.
            if ((role & (Role.Search | Role.Draw)) != 0) return 1100;
            if ((role & Role.Extend) != 0) return 800;
            if ((role & Role.Removal) != 0 && Enemy.GetFieldCount() > 0) return 700;
            return 0;
        }

        internal float BoardScore(ClientCard c) => BoardValue(c);

        internal ExtraPlan PlanExtra(ClientCard destination, ClientCard required = null)
        {
            var recipe = MaterialRecipe(destination);
            if (recipe == null) return null;
            var pool = Bot.GetMonsters().Where(c => c.IsFaceup()).ToList();
            int ownCount = pool.Count;
            if (recipe.EnemyOne) pool.AddRange(Enemy.GetMonsters().Where(c => c.IsFaceup()));
            if (pool.Count == 0 || pool.Count > 14) return null;
            int hint = Has(destination, CardType.Link) ? HintMsg.LinkMaterial : Has(destination, CardType.Xyz) ? HintMsg.XyzMaterial : HintMsg.SynchroMaterial;
            ExtraPlan best = null;
            for (int mask = 1; mask < (1 << pool.Count); mask++)
            {
                // Goddess can use one opposing monster; reject multi-opponent subsets
                // before allocating/scoring them, even on a crowded 7-versus-7 board.
                int enemyMask = mask >> ownCount;
                if ((enemyMask & (enemyMask - 1)) != 0) continue;
                var material = pool.Where((c, i) => (mask & (1 << i)) != 0).ToList();
                if (required != null && !material.Contains(required) || !ValidMaterials(destination, recipe, material)) continue;
                int attack = Attack(destination);
                if (destination.Id == 4280258) attack = material.Count * 800;
                if (destination.Id == 86066372) attack += material.Max(LinkRating) * 1000;
                float body = BoardValue(destination, attack), payoff = ImmediatePayoff(destination);
                float removed = material.Where(c => c.Controller == 1).Sum(BoardScore);
                float cost = material.Where(c => c.Controller == 0).Sum(c => BoardValue(c) -
                    (hint != HintMsg.XyzMaterial && (Roles(c) & Role.Grave) != 0 ? Math.Min(400, BoardValue(c) * .2f) : 0));
                bool lethal = duel.Player == 0 && duel.Phase == DuelPhase.Main1 && duel.Turn > 1 && Enemy.GetMonsterCount() == 0 &&
                    attack + Bot.GetMonsters().Where(c => !material.Contains(c) && c.IsAttack() && !c.Attacked).Sum(Attack) >= Enemy.LifePoints;
                if (material.Any(c => ProtectedBody(c) &&
                    (body + removed < BoardValue(c) + 250 || LiveInteraction(c) && !LiveInteraction(destination) &&
                     Enemy.GetFieldCount() == 0 && !lethal))) continue;
                float gain = body + payoff + removed - cost + (lethal ? 6000 : 0);
                if (gain < 100) continue;
                if (best == null || gain > best.Gain) best = new ExtraPlan { Destination = destination, Materials = material, Hint = hint, Gain = gain };
            }
            return best;
        }
    }

    public abstract partial class StoryLuckyExecutor
    {
        private StoryAiEvaluation.ExtraPlan extraPlan;
        private readonly HashSet<ClientCard> selectedMaterials = new HashSet<ClientCard>();

        private void CommitExtraPlan(StoryAiEvaluation.ExtraPlan plan)
        {
            extraPlan = plan;
            selectedMaterials.Clear();
        }

        private IList<ClientCard> PlannedMaterials(IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            if (extraPlan == null || extraPlan.Hint != hint || extraPlan.Destination.Location != CardLocation.Extra) return null;
            var wanted = cards.Where(c => extraPlan.Materials.Contains(c) && !selectedMaterials.Contains(c))
                .OrderBy(c => evaluation.BoardValue(c)).Take(max).ToList();
            // Finish only after the planned set is complete: e.g. Apollousa's ATK depends
            // on the number of bodies, even if two Link-2s could already finish the procedure.
            if (min == 0 && wanted.Count == 0) return new List<ClientCard>();
            if (wanted.Count < min)
            {
                if (cancelable) { CommitExtraPlan(null); return new List<ClientCard>(); }
                // A mandatory prompt cannot be undone here. Follow only the core's legal list
                // and pay as little as possible, never CardSelector's arbitrary first-card fill.
                wanted.AddRange(cards.Where(c => !wanted.Contains(c)).OrderBy(c => evaluation.MaterialCost(c, hint)).Take(min - wanted.Count));
            }
            foreach (var c in wanted) selectedMaterials.Add(c);
            return wanted;
        }

        public override void OnSpSummoned()
        {
            base.OnSpSummoned();
            CommitExtraPlan(null);
        }
    }
}
