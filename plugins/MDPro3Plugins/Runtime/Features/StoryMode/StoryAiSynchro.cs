using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        private sealed class MaterialRequirement
        {
            internal int Min = 1, Max = 1, Tuner, Race, Attribute, Level, MaximumLevel, MinimumLevel;
            internal int Setcode, ExcludeId;
            internal bool Synchro, Fusion, Xyz, Link, ExactName, Effect, Normal, NonLink, NonToken;
            internal string Name;
            internal bool Matches(ClientCard card) => MatchesNamed(card, null);
            internal bool MatchesNamed(ClientCard card, string fusionName) =>
                (Tuner == 0 || Has(card, CardType.Tuner) == (Tuner > 0)) &&
                (!Synchro || Has(card, CardType.Synchro)) &&
                (!Fusion || Has(card, CardType.Fusion)) && (!Xyz || Has(card, CardType.Xyz)) && (!Link || Has(card, CardType.Link)) &&
                (Setcode == 0 || card.HasSetcode(Setcode)) && (ExcludeId == 0 || CardIdentity(card) != ExcludeId) &&
                (!Effect || Has(card, CardType.Effect)) && (!Normal || Has(card, CardType.Normal)) &&
                (!NonLink || !Has(card, CardType.Link)) && (!NonToken || !Has(card, CardType.Token)) &&
                (Level == 0 || !Has(card, CardType.Link | CardType.Xyz) && StoryAiEvaluation.Level(card) == Level) &&
                (MaximumLevel == 0 || !Has(card, CardType.Link | CardType.Xyz) && StoryAiEvaluation.Level(card) <= MaximumLevel) &&
                (MinimumLevel == 0 || !Has(card, CardType.Link | CardType.Xyz) && StoryAiEvaluation.Level(card) >= MinimumLevel) &&
                (Race == 0 || (StoryAiEvaluation.Race(card) & Race) != 0) &&
                (Attribute == 0 || (StoryAiEvaluation.Attribute(card) & Attribute) != 0) &&
                (Name == null || (ExactName ? MatchesMaterialName(card, Name) || string.Equals(fusionName, Name, StringComparison.OrdinalIgnoreCase) :
                    (card.Data?.Name ?? "").IndexOf(Name, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool MatchesMaterialName(ClientCard card, string name)
        {
            if (string.Equals(card.Data?.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            // Continuous name substitution is valid only in its stated zones.
            if (card.Location != CardLocation.MonsterZone && card.Location != CardLocation.Grave) return false;
            string text = card.Data?.Description ?? "";
            var match = Regex.Match(text, @"卡名只要在场上·墓地存在当作「(?<name>[^」]+)」使用|name becomes ""(?<name>[^""]+)"" while on the field or in the (?:GY|Graveyard)", RegexOptions.IgnoreCase);
            return match.Success && string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase);
        }

        private const string MaterialRaces = @"Machine(?:-Type)?|Fairy(?:-Type)?|Sea Serpent(?:-Type)?|Wyrm(?:-Type)?|Dragon(?:-Type)?|Beast-Warrior(?:-Type)?|Warrior(?:-Type)?|Spellcaster(?:-Type)?|Fiend(?:-Type)?|Zombie(?:-Type)?|Aqua(?:-Type)?|Pyro(?:-Type)?|Rock(?:-Type)?|Winged Beast(?:-Type)?|Plant(?:-Type)?|Insect(?:-Type)?|Thunder(?:-Type)?|Beast(?:-Type)?|Dinosaur(?:-Type)?|Fish(?:-Type)?|Reptile(?:-Type)?|Psychic(?:-Type)?|Cyberse(?:-Type)?|机械族|天使族|海[龙龍]族|幻[龙龍]族|[龙龍]族|[兽獸]战士族|战士族|魔法使族|恶魔族|不死族|水族|炎族|岩石族|鸟兽族|植物族|昆虫族|雷族|[兽獸]族|恐[龙龍]族|鱼族|爬虫类族|念动力族|电子界族";

        private static MaterialRequirement ParseMaterial(string clause)
        {
            var result = new MaterialRequirement();
            string text = clause.Trim();
            result.NonToken = Regex.IsMatch(text, @", except Tokens$|[（(]衍生物除外[）)]$", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @", except Tokens$|[（(]衍生物除外[）)]$", "", RegexOptions.IgnoreCase);
            var count = Regex.Match(text, @"^(?<n>[1-7])(?<plus>\+)?\s+|[×x]\s*(?<n>[1-7])\s*(?:只|隻|体)?(?<plus>以上)?$|(?<n>[1-7])\s*(?:只|隻|体)(?<plus>以上)?$", RegexOptions.IgnoreCase);
            if (count.Success)
            {
                result.Min = int.Parse(count.Groups["n"].Value);
                result.Max = count.Groups["plus"].Success ? 7 : result.Min;
                text = text.Remove(count.Index, count.Length).Trim();
            }
            // Quoted card/archetype names are opaque. Words such as "Normal",
            // "Effect" or "Level" inside them are not material conditions.
            var name = Regex.Match(text, "「(?<name>[^」]+)」|\"(?<name>[^\"]+)\"");
            if (name.Success)
            {
                result.Name = name.Groups["name"].Value;
                result.ExactName = text == name.Value;
                text = text.Remove(name.Index, name.Length).Trim();
            }
            var level = Regex.Match(text, @"\bLevel (?<n>1[0-2]|[1-9])(?:(?<lower> or lower)|(?<higher> or higher))?\b|(?<n>1[0-2]|[1-9])星(?:(?<lower>以下)|(?<higher>以上))?", RegexOptions.IgnoreCase);
            if (level.Success)
            {
                int n = int.Parse(level.Groups["n"].Value);
                if (level.Groups["lower"].Success) result.MaximumLevel = n;
                else if (level.Groups["higher"].Success) result.MinimumLevel = n;
                else result.Level = n;
                text = text.Remove(level.Index, level.Length).Trim();
            }
            result.NonLink = Regex.IsMatch(text, @"\bnon-Link\b|连接怪兽以外的|連接怪獸以外的", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bnon-Link\b|连接怪兽以外的|連接怪獸以外的", "", RegexOptions.IgnoreCase);
            result.Effect = Regex.IsMatch(text, @"\bEffect\b|效果", RegexOptions.IgnoreCase);
            result.Normal = Regex.IsMatch(text, @"\bNormal\b|通常", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bEffect\b|\bNormal\b|效果|通常", "", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(text, @"non-Tuner|(?:调整|調整)以外|除(?:调整|調整)以外", RegexOptions.IgnoreCase))
            {
                result.Tuner = -1;
                text = Regex.Replace(text, @"non-Tuner|(?:调整|調整)以外的?|除(?:调整|調整)以外的?", "", RegexOptions.IgnoreCase);
            }
            else if (Regex.IsMatch(text, @"\bTuners?\b|调整|調整", RegexOptions.IgnoreCase))
            {
                result.Tuner = 1;
                text = Regex.Replace(text, @"\bTuners?\b|调整|調整", "", RegexOptions.IgnoreCase);
            }
            if (Regex.IsMatch(text, @"\bSynchro\b|同[调調]", RegexOptions.IgnoreCase))
            {
                result.Synchro = true;
                text = Regex.Replace(text, @"\bSynchro\b|同[调調]", "", RegexOptions.IgnoreCase);
            }
            var race = Regex.Match(text, MaterialRaces, RegexOptions.IgnoreCase);
            if (race.Success)
            {
                result.Race = RecipeRace(race.Value);
                if (result.Race == 0) return null;
                text = text.Remove(race.Index, race.Length);
            }
            string[] attributes = { "EARTH|地属性", "WATER|水属性", "FIRE|炎属性", "WIND|风属性", "LIGHT|光属性", "DARK|暗属性", "DIVINE|神属性" };
            for (int i = 0; i < attributes.Length; i++)
            {
                var attribute = Regex.Match(text, attributes[i], RegexOptions.IgnoreCase);
                if (!attribute.Success) continue;
                result.Attribute = 1 << i;
                text = text.Remove(attribute.Index, attribute.Length);
                break;
            }
            result.Fusion = Regex.IsMatch(text, @"\bFusion\b|融合", RegexOptions.IgnoreCase);
            result.Xyz = Regex.IsMatch(text, @"\bXyz\b|超量", RegexOptions.IgnoreCase);
            result.Link = Regex.IsMatch(text, @"\bLink\b|连接|連接", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bFusion\b|融合|\bXyz\b|超量|\bLink\b|连接|連接", "", RegexOptions.IgnoreCase);
            bool monster = Regex.IsMatch(text, @"\bmonsters?\b|怪[兽獸]", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bmonsters?\b|怪[兽獸]|名字带有|的|[\s·・]", "", RegexOptions.IgnoreCase);
            return text.Length == 0 && (monster || result.Tuner != 0 || result.Synchro || result.Name != null || result.Race != 0 || result.Attribute != 0 || result.Effect || result.Normal || result.NonLink) ? result : null;
        }

        private static Recipe SynchroRecipe(string text)
        {
            // Numeric 1+ is a quantity; the other plus signs separate material groups.
            var clauses = Regex.Split(text, @"＋|(?<!\d)\+");
            if (clauses.Length < 2 || clauses.Length > 7) return null;
            if (clauses.Length == 2)
            {
                var include = Regex.Match(clauses[1], @"^包含(?<kind>.+?)的(?:除)?调整以外的怪兽(?<n>[1-7])只以上$");
                if (include.Success)
                {
                    var tuner = ParseMaterial(clauses[0]);
                    var required = ParseMaterial(include.Groups["kind"].Value);
                    if (tuner == null || required == null) return null;
                    tuner.Tuner = 1; required.Tuner = -1;
                    return new Recipe { Min = 1 + int.Parse(include.Groups["n"].Value), Max = 7,
                        Requirements = new List<MaterialRequirement> { tuner, required,
                            new MaterialRequirement { Tuner = -1, Min = Math.Max(0, int.Parse(include.Groups["n"].Value) - 1), Max = 5 } } };
                }
            }
            var requirements = clauses.Select(ParseMaterial).ToList();
            if (requirements.Any(r => r == null)) return null;
            return new Recipe { Min = requirements.Sum(r => r.Min), Max = Math.Min(7, requirements.Sum(r => r.Max)), Requirements = requirements };
        }

        private static bool AssignMaterials(List<MaterialRequirement> requirements, List<ClientCard> cards)
            => AssignNamedMaterials(requirements, cards, _ => null);

        private static bool AssignNamedMaterials(List<MaterialRequirement> requirements, List<ClientCard> cards, Func<ClientCard, string> name)
        {
            var ordered = cards.OrderBy(c => requirements.Count(r => r.MatchesNamed(c, name(c)))).ToList();
            var counts = new int[requirements.Count];
            Func<int, bool> assign = null;
            assign = index =>
            {
                if (index == ordered.Count) return requirements.Select((r, i) => counts[i] >= r.Min).All(b => b);
                for (int i = 0; i < requirements.Count; i++)
                {
                    if (counts[i] >= requirements[i].Max || !requirements[i].MatchesNamed(ordered[index], name(ordered[index]))) continue;
                    counts[i]++;
                    if (assign(index + 1)) return true;
                    counts[i]--;
                }
                return false;
            };
            return assign(0);
        }

        private sealed class DeckTunerTrigger
        {
            internal MaterialRequirement Material;
            internal bool DifferentLevels, SynchroOnly, EffectsBlocked, DefensePosition;
        }

        private static DeckTunerTrigger ParseDeckTunerTrigger(ClientCard card)
        {
            if (!Has(card, CardType.Synchro)) return null;
            string text = card.Data?.Description ?? "";
            var start = Regex.Match(text, @"这张卡同调召唤(?:成功)?的场合才能发动|If this card is Synchro Summoned\s*:", RegexOptions.IgnoreCase);
            if (!start.Success) return null;
            string effect = text.Substring(start.Index + start.Length);
            effect = Regex.Split(effect, @"[②③④⑤⑥]|When an attack is declared|You can only use", RegexOptions.IgnoreCase)[0];
            var summon = Regex.Match(effect, "从卡组把(?<kind>「[^」]+」)?调整(?<many>尽可能|1只)守备表示特殊召唤|Special Summon (?<many>as many|1) (?<kind>\"[^\"]+\" )?Tuners?(?: as possible)?(?: with different Levels)? from your Deck", RegexOptions.IgnoreCase);
            if (!summon.Success) return null;
            string before = effect.Substring(0, summon.Index);
            if (Regex.IsMatch(before, @"discard|banish|pay|Tribute|送去墓地|丢弃|除外|支付|解放", RegexOptions.IgnoreCase)) return null;
            // Recognize only this direct trigger, including its understood restrictions.
            // A matching summon sentence inside a conditional/costly effect is insufficient.
            string remainder = effect.Remove(summon.Index, summon.Length);
            remainder = Regex.Replace(remainder,
                @"这个效果发动的回合，自己不是同调怪兽不能从额外卡组特殊召唤|You cannot Special Summon from the Extra Deck, except Synchro Monsters, the turn you activate this effect|相同等级最多1只|这个效果特殊召唤的怪兽在这个回合不能把效果发动|You cannot activate their effects this turn|in Defense Position|You can|[\s。.,，、（ ）()]", "", RegexOptions.IgnoreCase);
            if (remainder.Length != 0) return null;
            string kind = summon.Groups["kind"].Value.Trim();
            var material = ParseMaterial(kind.Length == 0 ? "Tuner" : kind + " Tuner");
            bool many = summon.Groups["many"].Value != "1" && summon.Groups["many"].Value != "1只";
            bool different = Regex.IsMatch(effect, @"相同等级最多1只|with different Levels", RegexOptions.IgnoreCase);
            if (material == null || many && !different) return null;
            return new DeckTunerTrigger
            {
                Material = material, DifferentLevels = many,
                SynchroOnly = Regex.IsMatch(effect, @"不是同调怪兽不能从额外卡组特殊召唤|cannot Special Summon from the Extra Deck, except Synchro Monsters", RegexOptions.IgnoreCase),
                EffectsBlocked = Regex.IsMatch(effect, @"不能把效果发动|cannot activate (?:their|its) effects", RegexOptions.IgnoreCase),
                DefensePosition = Regex.IsMatch(effect, @"守备表示特殊召唤|in Defense Position", RegexOptions.IgnoreCase)
            };
        }
    }
}
