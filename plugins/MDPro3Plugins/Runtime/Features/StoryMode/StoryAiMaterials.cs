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
            internal bool Unparsed;
        }

        private sealed class Recipe
        {
            internal int Min = 2, Max = 7;
            internal bool Effect, Normal, NonLink, DifferentNames, NonToken, ExtraOnly, EnemyOne, Tuner;
            internal int Race, NonTunerRace;
            internal List<MaterialRequirement> Requirements;
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
        internal float BoardValue(ClientCard c, int projectedAttack = -1, int projectedMaterials = -1)
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
            if (Has(c, CardType.Xyz) && Has(c, CardType.Effect))
                value += Math.Min(2, projectedMaterials >= 0 ? projectedMaterials : c.Overlays.Count) * 700;
            if (c.IsDisabled()) value *= .65f;
            if (c.Attacked && duel.Player == 0) value -= 180;
            return Math.Max(100, value);
        }

        internal bool PremiumBody(ClientCard c)
        {
            if (c == null || c.Controller != 0 || c.Location != CardLocation.MonsterZone ||
                c.IsDisabled() || Has(c, CardType.Token) || !Has(c, CardType.Effect)) return false;
            if (Attack(c) < 2800) return false;
            var role = Roles(c);
            return LiveInteraction(c) || (role & (Role.Starter | Role.Search | Role.Extend | Role.Interrupt | Role.Removal)) != 0;
        }

        internal bool ProtectedBody(ClientCard c) => c.Controller == 0 && c.Location == CardLocation.MonsterZone &&
            !c.IsDisabled() && (PremiumBody(c) || LiveInteraction(c) || Has(c, CardType.Link) && LinkRating(c) >= 3 ||
            Has(c, CardType.Synchro | CardType.Fusion) && Level(c) >= 7 || Has(c, CardType.Xyz) && Attack(c) >= 2500);

        private static bool SameCardName(ClientCard a, ClientCard b)
        {
            if (a == null || b == null) return false;
            if (a.Id != 0 && a.Id == b.Id) return true;
            string left = a.Data?.Name, right = b.Data?.Name;
            return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) &&
                string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        internal bool DuplicateTerminalLoss(ClientCard destination, List<ClientCard> material, float bodyValue)
        {
            // The caller supplies only current face-up bodies (or their forward-model
            // counterparts), so location is intentionally not part of this check.  A
            // simulated body can retain its pre-summon location while it is still a
            // real material candidate.
            return material.Any(c => c.Controller == 0 &&
                SameCardName(c, destination) && BoardValue(c) >= bodyValue - 400);
        }

        private static int RecipeRace(string text)
        {
            if (Contains(text, "机械族", "机械", "Machine-Type", "Machine")) return 32;
            if (Contains(text, "天使族", "天使", "Fairy-Type", "Fairy")) return 4;
            if (Contains(text, "海龙族", "海龍族", "Sea Serpent-Type", "Sea Serpent")) return 262144;
            if (Contains(text, "幻龙族", "幻龍族", "Wyrm-Type", "Wyrm")) return 8388608;
            if (Contains(text, "龙族", "龍族", "Dragon-Type", "Dragon")) return 8192;
            if (Contains(text, "兽战士族", "獸戰士族", "Beast-Warrior-Type", "Beast-Warrior")) return 32768;
            if (Contains(text, "战士族", "戰士族", "Warrior-Type", "Warrior")) return 1;
            if (Contains(text, "魔法使族", "Spellcaster-Type", "Spellcaster")) return 2;
            if (Contains(text, "恶魔族", "惡魔族", "Fiend-Type", "Fiend")) return 8;
            if (Contains(text, "不死族", "Zombie-Type", "Zombie")) return 16;
            if (Contains(text, "水族", "Aqua-Type", "Aqua")) return 64;
            if (Contains(text, "炎族", "Pyro-Type", "Pyro")) return 128;
            if (Contains(text, "岩石族", "Rock-Type", "Rock")) return 256;
            if (Contains(text, "鸟兽族", "鳥獸族", "Winged Beast-Type", "Winged Beast")) return 512;
            if (Contains(text, "植物族", "Plant-Type", "Plant")) return 1024;
            if (Contains(text, "昆虫族", "Insect-Type", "Insect")) return 2048;
            if (Contains(text, "雷族", "Thunder-Type", "Thunder")) return 4096;
            if (Contains(text, "兽族", "獸族", "Beast-Type", "Beast")) return 16384;
            if (Contains(text, "恐龙族", "恐龍族", "Dinosaur-Type", "Dinosaur")) return 65536;
            if (Contains(text, "鱼族", "魚族", "Fish-Type", "Fish")) return 131072;
            if (Contains(text, "爬虫类族", "爬蟲類族", "Reptile-Type", "Reptile")) return 524288;
            if (Contains(text, "念动力族", "念動力族", "Psychic-Type", "Psychic")) return 1048576;
            if (Contains(text, "电子界族", "電子界族", "Cyberse-Type", "Cyberse")) return 16777216;
            return 0;
        }

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
            if (Has(destination, CardType.Synchro)) return SynchroRecipe(recipe);
            if (Has(destination, CardType.Fusion))
            {
                var clauses = Regex.Split(recipe, @"＋|(?<!\d)\+");
                if (clauses.Length < 2 || clauses.Length > 7) return null;
                var requirements = clauses.Select(ParseMaterial).ToList();
                if (requirements.Any(r => r == null)) return null;
                return new Recipe { Min = requirements.Sum(r => r.Min), Max = Math.Min(7, requirements.Sum(r => r.Max)),
                    Requirements = requirements };
            }
            if (Has(destination, CardType.Xyz))
            {
                // Database clauses use several equivalent forms, for example
                // “机械族5星怪兽×2”, “5星怪兽×2只以上” and
                // “2 Machine-Type Level 5 monsters”. Parse level, count and
                // optional race instead of treating the first unfamiliar wording
                // as an unknown procedure.
                var level = Regex.Match(recipe, @"(?<level>\d+)\s*(?:星|阶|階)\s*怪[兽獸]|\bLevel\s*(?<levelEn>\d+)\b", RegexOptions.IgnoreCase);
                var count = Regex.Match(recipe, @"[×x]\s*(?<n>[2-7])\s*(?<plus>以上|\+)?|^(?<nEn>[2-7])\s+.*?\bLevel\s*\d+\b.*?monsters?\s*(?<plusEn>\+|以上|or more)?$|(?<nZh>[2-7])\s*(?:只|体|隻)(?<plusZh>以上|\+)?", RegexOptions.IgnoreCase);
                if (level.Success && count.Success)
                {
                    string levelText = level.Groups["level"].Success ? level.Groups["level"].Value : level.Groups["levelEn"].Value;
                    string countText = count.Groups["n"].Success ? count.Groups["n"].Value :
                        count.Groups["nEn"].Success ? count.Groups["nEn"].Value : count.Groups["nZh"].Value;
                    int parsedLevel = int.Parse(levelText), parsedCount = int.Parse(countText);
                    if (parsedLevel == Level(destination))
                    {
                        bool plus = count.Groups["plus"].Success || count.Groups["plusEn"].Success || count.Groups["plusZh"].Success;
                        return new Recipe { Min = parsedCount, Max = plus ? 7 : parsedCount, Race = RecipeRace(recipe) };
                    }
                }
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
            if (Has(destination, CardType.Fusion) && recipe.Requirements != null)
                return AssignMaterials(recipe.Requirements, cards);
            if (Has(destination, CardType.Synchro) && recipe.Requirements != null) return
                cards.Any(c => Has(c, CardType.Tuner)) && cards.All(c => !Has(c, CardType.Link | CardType.Xyz)) &&
                cards.Sum(Level) == Level(destination) && AssignMaterials(recipe.Requirements, cards);
            if (Has(destination, CardType.Synchro)) return cards.Count(c => Has(c, CardType.Tuner)) == 1 && cards.Count > 1 &&
                cards.All(c => !Has(c, CardType.Link | CardType.Xyz)) && cards.Sum(Level) == Level(destination) &&
                (recipe.NonTunerRace == 0 || cards.Where(c => !Has(c, CardType.Tuner)).All(c => (Race(c) & recipe.NonTunerRace) != 0));
            return false;
        }

        private static bool HasImmediateSynchroDraw(ClientCard c)
        {
            if (!Has(c, CardType.Synchro)) return false;
            string text = c.Data?.Description ?? "";
            int recipeEnd = text.IndexOf('\n');
            if (recipeEnd < 0) return false;
            text = text.Substring(recipeEnd + 1).TrimStart();
            text = Regex.Replace(text,
                @"^(?:这个卡名的①的效果1回合只能使用1次。|You can only use this effect of ""[^""]+"" once per turn\.)\s*", "", RegexOptions.IgnoreCase);
            // Only the complete first activated clause qualifies. Costs, conditions,
            // draw-then-discard and a different effect's draw must not become free cards.
            return Regex.IsMatch(text,
                @"\A(?:①\s*[:：]\s*)?(?:这张卡同调召唤(?:成功)?(?:时|的场合)(?:才能发动|可以发动)[。:：]\s*自己抽1张。|(?:If|When) this card is Synchro Summoned\s*:\s*(?:You can )?draw 1 card\.)[ \t\r]*(?:\n|$)",
                RegexOptions.IgnoreCase);
        }

        private bool AvailableSynchroDraw(ClientCard c) => HasImmediateSynchroDraw(c) &&
            !c.IsDisabled() && Bot.Deck.Count > 0 && !usedDevelopmentEffects.Contains(c.Id);

        private float ImmediatePayoff(ClientCard c)
        {
            // This trigger is valued through actual remaining deck bodies in the forward model.
            if (ParseDeckTunerTrigger(c) != null) return 0;
            if (HasImmediateSynchroDraw(c)) return AvailableSynchroDraw(c) ? 2000 : 0;
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
            if ((role & Role.Interrupt) != 0) return 1200;
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
            int hint = ExtraMaterialHint(destination);
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
                float body = BoardValue(destination, attack, hint == HintMsg.XyzMaterial ? material.Count : 0), payoff = ImmediatePayoff(destination);
                float removed = material.Where(c => c.Controller == 1).Sum(BoardScore);
                float cost = material.Where(c => c.Controller == 0).Sum(c => BoardValue(c) -
                    (hint != HintMsg.XyzMaterial && (Roles(c) & Role.Grave) != 0 ? Math.Min(400, BoardValue(c) * .2f) : 0));
                bool lethal = duel.Player == 0 && duel.Phase == DuelPhase.Main1 && duel.Turn > 1 && Enemy.GetMonsterCount() == 0 &&
                    attack + Bot.GetMonsters().Where(c => !material.Contains(c) && c.IsAttack() && !c.Attacked).Sum(Attack) >= Enemy.LifePoints;
                if (DuplicateTerminalLoss(destination, material, body)) continue;
                if (material.Any(c => ProtectedBody(c) &&
                    (body + removed < BoardValue(c) + (PremiumBody(c) ? 400 : 250) || LiveInteraction(c) && !LiveInteraction(destination) &&
                     Enemy.GetFieldCount() == 0 && !lethal))) continue;
                float gain = body + payoff + removed - cost + (lethal ? 6000 : 0);
                if (gain < 100) continue;
                if (best == null || gain > best.Gain) best = new ExtraPlan { Destination = destination, Materials = material, Hint = hint, Gain = gain };
            }
            return best;
        }

        private static int ExtraMaterialHint(ClientCard card) => Has(card, CardType.Link) ? HintMsg.LinkMaterial :
            Has(card, CardType.Xyz) ? HintMsg.XyzMaterial : Has(card, CardType.Fusion) ? HintMsg.FusionMaterial : HintMsg.SynchroMaterial;

        internal ExtraPlan PlanCoreOfferedExtra(ClientCard destination)
        {
            if (destination.Location != CardLocation.Extra) return null;
            var pool = Bot.GetMonsters().Where(c => c.IsFaceup()).ToList();
            if (Has(destination, CardType.Fusion)) pool.AddRange(Bot.Hand.Where(c => Has(c, CardType.Monster)));
            // The core confirms a legal procedure, but does not expose its complete cost
            // here. Commit only while every possible field material is expendable.
            bool oneBodyUpgrade = pool.Count == 1 && Has(destination, CardType.Xyz) &&
                Has(pool[0], CardType.Xyz) && Level(destination) > Level(pool[0]);
            if (pool.Count < (oneBodyUpgrade ? 1 : 2)) return null;
            int hint = ExtraMaterialHint(destination);
            var material = pool.OrderBy(c => MaterialCost(c, hint)).Take(oneBodyUpgrade ? 1 : 2).ToList();
            int overlays = oneBodyUpgrade ? pool[0].Overlays.Count + 1 : material.Count;
            float body = BoardValue(destination, projectedMaterials: hint == HintMsg.XyzMaterial ? overlays : 0);
            if (material.Any(c => ProtectedBody(c)) && (!oneBodyUpgrade ||
                body < BoardValue(pool[0]) + 500 || LiveInteraction(pool[0]) && !LiveInteraction(destination))) return null;
            if (DuplicateTerminalLoss(destination, material, body)) return null;
            float cost = material.Sum(c => c.Location == CardLocation.Hand ? MaterialCost(c, hint) : BoardValue(c));
            float gain = body + ImmediatePayoff(destination) - cost;
            if (gain < 300) return null;
            return new ExtraPlan { Destination = destination, Materials = material, Hint = hint, Gain = gain,
                Unparsed = true };
        }

        internal ExtraPlan PlanFusionSelection(ClientCard destination)
        {
            if (!Has(destination, CardType.Fusion)) return null;
            var recipe = MaterialRecipe(destination);
            if (recipe == null) return null;
            var pool = Bot.GetMonsters().Where(c => c.IsFaceup()).Concat(Bot.Hand).ToList();
            if (pool.Count > 14) return null;
            ExtraPlan best = null;
            for (int mask = 1; mask < (1 << pool.Count); mask++)
            {
                var material = pool.Where((c, i) => (mask & (1 << i)) != 0).ToList();
                if (!ValidMaterials(destination, recipe, material)) continue;
                float body = BoardValue(destination), cost = material.Sum(c => MaterialCost(c, HintMsg.FusionMaterial));
                if (material.Any(c => ProtectedBody(c) && body < BoardValue(c) + 250) ||
                    DuplicateTerminalLoss(destination, material, body)) continue;
                float gain = body + ImmediatePayoff(destination) - cost;
                if (best == null || gain > best.Gain)
                    best = new ExtraPlan { Destination = destination, Materials = material, Hint = HintMsg.FusionMaterial, Gain = gain };
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
            if (extraPlan.Unparsed)
            {
                var legal = cards.Where(c => !selectedMaterials.Contains(c))
                    .OrderBy(c => evaluation.MaterialCost(c, hint)).Take(min).ToList();
                if (legal.Count < min || cancelable && legal.Any(c => evaluation.ProtectedBody(c) &&
                    !extraPlan.Materials.Contains(c))) return new List<ClientCard>();
                foreach (var c in legal) selectedMaterials.Add(c);
                return legal;
            }
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
