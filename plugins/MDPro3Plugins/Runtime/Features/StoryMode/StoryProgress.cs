using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Preserve the original saved/native values; SR is an additional story-only finish.
    public enum StoryRarity { N = 1, R = 2, SR = 32, UR = 4, GR = 8, MR = 16 }

    [Serializable]
    public sealed class StoryCard
    {
        public int id;
        public StoryRarity rarity;
        public StoryCard(int id, StoryRarity rarity) { this.id = id; this.rarity = rarity; }
    }

    [Serializable]
    public sealed class StoryRules
    {
        public int initialPacks = 5;
        public int duelsPerUnlock = 3;
        public int packsPerUnlock = 1;
        public int winDP = 100;
        public int packPrice = 100;
        public int[] rarityWeights = { 550, 325, 75, 25, 15, 10 };

        public void Validate()
        {
            if (initialPacks < 1 || duelsPerUnlock < 1 || packsPerUnlock < 1
                || winDP < 1 || packPrice < 1)
                throw new InvalidDataException("故事模式规则必须全部为正整数。");
            if (rarityWeights == null || rarityWeights.Length != StoryProgress.Rarities.Length)
                throw new InvalidDataException("罕贵度权重需要按 N/R/SR/UR/GR/MR 配置六个值。");
            long total = 0;
            foreach (int weight in rarityWeights)
            {
                if (weight < 0) throw new InvalidDataException("罕贵度权重不能为负数。");
                total += weight;
            }
            if (total <= 0 || total > int.MaxValue) throw new InvalidDataException("罕贵度权重总和无效。");
        }

        public int RequiredDuels(int packIndex)
        {
            if (packIndex < initialPacks) return 0;
            long value = ((long)(packIndex - initialPacks) / packsPerUnlock + 1) * duelsPerUnlock;
            return (int)Math.Min(int.MaxValue, value);
        }

        public int WinReward(int level)
        {
            if (!StoryProgress.ValidLevel(level)) throw new ArgumentOutOfRangeException(nameof(level));
            return checked(winDP * level);
        }
    }

    [Serializable]
    public sealed class StoryDeck
    {
        public List<int> main = new List<int>();
        public List<int> extra = new List<int>();
        public List<int> side = new List<int>();
        // Empty means all N, including old decks and plain YDK imports.
        public List<StoryRarity> mainRarities = new List<StoryRarity>();
        public List<StoryRarity> extraRarities = new List<StoryRarity>();
        public List<StoryRarity> sideRarities = new List<StoryRarity>();
        public IEnumerable<StoryCard> Copies()
        {
            for (int i = 0; i < main.Count; i++) yield return new StoryCard(main[i], At(mainRarities, i));
            for (int i = 0; i < extra.Count; i++) yield return new StoryCard(extra[i], At(extraRarities, i));
            for (int i = 0; i < side.Count; i++) yield return new StoryCard(side[i], At(sideRarities, i));
        }
        public static StoryRarity At(List<StoryRarity> rarities, int index) => rarities.Count == 0 ? StoryRarity.N : rarities[index];
        public bool ValidRarities() => ValidSection(main, mainRarities) && ValidSection(extra, extraRarities) && ValidSection(side, sideRarities);
        private static bool ValidSection(List<int> ids, List<StoryRarity> rarities)
        {
            if (ids == null || rarities == null || (rarities.Count != 0 && rarities.Count != ids.Count)) return false;
            foreach (var rarity in rarities) if (!StoryProgress.ValidRarity(rarity)) return false;
            return true;
        }
        public IEnumerable<int> All()
        {
            foreach (int id in main) yield return id;
            foreach (int id in extra) yield return id;
            foreach (int id in side) yield return id;
        }
        public int Count(int id)
        {
            int count = 0;
            foreach (int value in All()) if (value == id) count++;
            return count;
        }
        public StoryDeck Copy() => new StoryDeck
        {
            main = new List<int>(main), extra = new List<int>(extra), side = new List<int>(side),
            mainRarities = new List<StoryRarity>(mainRarities), extraRarities = new List<StoryRarity>(extraRarities),
            sideRarities = new List<StoryRarity>(sideRarities)
        };
        public string ToYdk() => "#created by MDPro3 story mode\n#main\n"
            + string.Join("\n", main) + "\n#extra\n" + string.Join("\n", extra)
            + "\n!side\n" + string.Join("\n", side) + "\n";
    }

    [Serializable]
    public sealed class StorySave
    {
        public const int CurrentVersion = 3;
        public const int InitialDP = 1000;
        public int version = CurrentVersion;
        public int dp;
        public int completedDuels;
        public int wins;
        public string lastSettledDuel = "";
        public Dictionary<int, int> owned = new Dictionary<int, int>();
        public Dictionary<int, Dictionary<StoryRarity, int>> ownedRarities = new Dictionary<int, Dictionary<StoryRarity, int>>();
        public int Owned(int id, StoryRarity rarity) => ownedRarities.TryGetValue(id, out var versions)
            && versions.TryGetValue(rarity, out int count) ? count : 0;
        public StoryDeck player = new StoryDeck();
        public Dictionary<string, Dictionary<int, StoryDeck>> opponents = new Dictionary<string, Dictionary<int, StoryDeck>>();
        public Dictionary<string, int> characterWins = new Dictionary<string, int>();
        public StorySave Copy() => JsonConvert.DeserializeObject<StorySave>(JsonConvert.SerializeObject(this));

        public static StorySave New(StoryDeck starter)
        {
            var save = new StorySave { dp = InitialDP, player = starter.Copy() };
            foreach (int id in starter.All())
                save.owned[id] = save.owned.TryGetValue(id, out int n) ? n + 1 : 1;
            foreach (var card in starter.Copies())
            {
                if (!save.ownedRarities.TryGetValue(card.id, out var versions))
                    save.ownedRarities[card.id] = versions = new Dictionary<StoryRarity, int>();
                versions[card.rarity] = versions.TryGetValue(card.rarity, out int count) ? count + 1 : 1;
            }
            return save;
        }

        public bool TryGetOpponent(string character, int level, out StoryDeck deck)
        {
            deck = null;
            return !string.IsNullOrEmpty(character) && StoryProgress.ValidLevel(level)
                && opponents.TryGetValue(character, out var levels) && levels != null
                && levels.TryGetValue(level, out deck) && deck != null;
        }

        public void SetOpponent(string character, int level, StoryDeck deck)
        {
            if (string.IsNullOrEmpty(character) || !StoryProgress.ValidLevel(level) || deck == null)
                throw new ArgumentException("角色、难度或卡组无效。");
            if (!opponents.TryGetValue(character, out var levels) || levels == null)
                opponents[character] = levels = new Dictionary<int, StoryDeck>();
            levels[level] = deck.Copy();
        }

        public void Validate()
        {
            if (version != CurrentVersion || dp < 0 || completedDuels < 0 || wins < 0 || wins > completedDuels
                || player == null || player.main == null || player.extra == null || player.side == null
                || owned == null || owned.Count == 0 || ownedRarities == null || opponents == null || characterWins == null
                || !player.ValidRarities())
                throw new InvalidDataException("故事存档结构无效或版本不受支持。");
            foreach (var pair in owned)
            {
                if (pair.Key <= 0 || pair.Value <= 0 || !ownedRarities.TryGetValue(pair.Key, out var versions) || versions == null)
                    throw new InvalidDataException("持有卡片数据无效。");
                long total = 0;
                foreach (var version in versions)
                {
                    if (!StoryProgress.ValidRarity(version.Key) || version.Value <= 0)
                        throw new InvalidDataException("卡片罕贵度库存无效。");
                    total += version.Value;
                }
                if (total != pair.Value) throw new InvalidDataException("卡片罕贵度库存与总数不一致。");
            }
            if (ownedRarities.Count != owned.Count) throw new InvalidDataException("卡片罕贵度库存含未知卡片。");
            foreach (var character in opponents)
            {
                if (string.IsNullOrEmpty(character.Key) || character.Value == null)
                    throw new InvalidDataException("角色卡组数据无效。");
                foreach (var level in character.Value)
                    if (!StoryProgress.ValidLevel(level.Key) || level.Value == null || level.Value.main == null
                        || level.Value.extra == null || level.Value.side == null)
                        throw new InvalidDataException("角色卡组难度数据无效。");
            }
        }
    }

    // Pure rules shared by runtime and executable regression tests.
    public static class StoryProgress
    {
        public const int CardsPerPack = 3;
        public const int MinLevel = 1;
        public const int MaxLevel = 10;
        public static readonly StoryRarity[] Rarities = { StoryRarity.N, StoryRarity.R, StoryRarity.SR, StoryRarity.UR, StoryRarity.GR, StoryRarity.MR };
        public static bool ValidRarity(StoryRarity rarity) => Array.IndexOf(Rarities, rarity) >= 0;

        public static bool ValidLevel(int level) => level >= MinLevel && level <= MaxLevel;

        public static List<StoryCard> Buy(StorySave save, StoryRules rules, int packIndex,
            IReadOnlyList<int> pool, Func<int, int> randomIndex)
        {
            if (packIndex < 0 || save.completedDuels < rules.RequiredDuels(packIndex))
                throw new InvalidOperationException("该卡包尚未解锁。");
            if (pool == null || pool.Count == 0) throw new InvalidOperationException("该卡包没有可用卡片。");
            if (save.dp < rules.packPrice) throw new InvalidOperationException("DP 不足，请先挑战角色赢取 DP。");
            rules.Validate();
            int totalWeight = 0;
            foreach (int weight in rules.rarityWeights) totalWeight += weight;
            var draws = new List<StoryCard>(CardsPerPack);
            for (int i = 0; i < CardsPerPack; i++)
            {
                int id = pool[randomIndex(pool.Count)];
                int roll = randomIndex(totalWeight);
                if (id <= 0 || roll < 0 || roll >= totalWeight) throw new InvalidOperationException("抽卡数据无效。");
                int tier = 0;
                while (roll >= rules.rarityWeights[tier]) roll -= rules.rarityWeights[tier++];
                draws.Add(new StoryCard(id, Rarities[tier]));
            }
            // Compute before mutating, including overflow checks. The caller then commits atomically.
            var owned = new Dictionary<int, int>(save.owned);
            var finishes = new Dictionary<int, Dictionary<StoryRarity, int>>();
            foreach (var pair in save.ownedRarities) finishes[pair.Key] = new Dictionary<StoryRarity, int>(pair.Value);
            foreach (var card in draws)
            {
                owned[card.id] = checked((owned.TryGetValue(card.id, out int n) ? n : 0) + 1);
                if (!finishes.TryGetValue(card.id, out var versions)) finishes[card.id] = versions = new Dictionary<StoryRarity, int>();
                versions[card.rarity] = checked((versions.TryGetValue(card.rarity, out n) ? n : 0) + 1);
            }
            save.owned = owned;
            save.ownedRarities = finishes;
            save.dp -= rules.packPrice;
            return draws;
        }

        public static bool Settle(StorySave save, StoryRules rules, string duelId, string character, int level, bool won)
        {
            if (!ValidLevel(level)) throw new ArgumentOutOfRangeException(nameof(level));
            if (string.IsNullOrEmpty(duelId) || duelId == save.lastSettledDuel) return false;
            int total = checked(save.completedDuels + 1);
            int dp = won ? checked(save.dp + rules.WinReward(level)) : save.dp;
            int wins = won ? checked(save.wins + 1) : save.wins;
            int characterWins = won ? checked((save.characterWins.TryGetValue(character, out int n) ? n : 0) + 1) : 0;
            save.completedDuels = total;
            save.dp = dp;
            save.wins = wins;
            if (won) save.characterWins[character] = characterWins;
            save.lastSettledDuel = duelId;
            return true;
        }

        public static string ValidateDeck(StoryDeck deck, Dictionary<int, int> owned,
            Func<int, bool> exists, Func<int, bool> isExtra, Func<int, int> identity, bool requireComplete = true,
            StorySave inventory = null)
        {
            if (deck == null || deck.main == null || deck.extra == null || deck.side == null)
                return "卡组数据不完整。";
            if (!deck.ValidRarities()) return "卡组的罕贵度记录无效。";
            if ((requireComplete && deck.main.Count < 40) || deck.main.Count > 60) return "主卡组需要 40–60 张卡。";
            if (deck.extra.Count > 15 || deck.side.Count > 15) return "额外卡组、副卡组各最多 15 张卡。";
            var copies = new Dictionary<int, int>();
            var actual = new Dictionary<int, int>();
            foreach (int id in deck.All())
            {
                if (!exists(id)) return "卡组包含当前卡库无法用于决斗的卡片：" + id;
                int key = identity(id);
                copies[key] = copies.TryGetValue(key, out int n) ? n + 1 : 1;
                if (copies[key] > 3) return "同名卡在主卡组、额外卡组和副卡组合计最多 3 张。";
                actual[id] = actual.TryGetValue(id, out n) ? n + 1 : 1;
                if (owned != null && (!owned.TryGetValue(id, out n) || actual[id] > n))
                    return "卡组使用了未持有的卡片或超过持有张数：" + id;
            }
            foreach (int id in deck.main) if (isExtra(id)) return "额外卡组怪兽不能放入主卡组。";
            foreach (int id in deck.extra) if (!isExtra(id)) return "额外卡组只能放入融合、同调、超量或连接怪兽。";
            if (inventory != null)
            {
                var used = new Dictionary<string, int>();
                foreach (var card in deck.Copies())
                {
                    string key = card.id + ":" + card.rarity;
                    used[key] = used.TryGetValue(key, out int count) ? count + 1 : 1;
                    if (used[key] > inventory.Owned(card.id, card.rarity))
                        return "卡组超过该罕贵度的持有张数：" + card.id + " / " + card.rarity;
                }
            }
            return null;
        }
    }

    public sealed class StoryStore
    {
        public string DirectoryPath { get; }
        public string SavePath => Path.Combine(DirectoryPath, "progress.json");
        public string LoadNotice { get; private set; }
        public StorySave Current { get; private set; }
        public StoryStore(string directory) { DirectoryPath = directory; }

        public void Load(Func<StoryDeck> starter)
        {
            if (!File.Exists(SavePath) && !File.Exists(SavePath + ".bak"))
            {
                Commit(StorySave.New(starter()));
                return;
            }
            bool migrated;
            try { Current = Read(SavePath, out migrated); }
            catch (Exception primary)
            {
                try { Current = Read(SavePath + ".bak", out migrated); }
                catch { throw new InvalidDataException("存档及备份无法读取，已保留原文件，请先修复存档。", primary); }
                LoadNotice = "主存档无法读取，已加载上一次备份。";
                // Preserve the unreadable primary and restore a valid primary before the next transaction.
                if (File.Exists(SavePath)) File.Copy(SavePath, SavePath + ".damaged-" + DateTime.UtcNow.Ticks);
                File.Copy(SavePath + ".bak", SavePath, true);
            }
            if (migrated)
            {
                Commit(Current.Copy());
                LoadNotice = string.IsNullOrEmpty(LoadNotice)
                    ? "旧故事存档已升级：原有卡片保留为 N，角色卡组与进度已保留。"
                    : LoadNotice + " 旧卡片已迁移为 N，角色卡组与进度已保留。";
            }
        }

        private static StorySave Read(string path, out bool migrated)
        {
            var document = JObject.Parse(File.ReadAllText(path));
            int version = document.Value<int?>("version") ?? 0;
            migrated = version == 1 || version == 2;
            StorySave value = version == 1 ? Upgrade(document.ToObject<StorySaveV1>())
                : version == 2 || version == StorySave.CurrentVersion ? document.ToObject<StorySave>() : null;
            if (value == null) throw new InvalidDataException("存档为空。");
            if (migrated)
            {
                value.version = StorySave.CurrentVersion;
                value.ownedRarities = new Dictionary<int, Dictionary<StoryRarity, int>>();
                foreach (var pair in value.owned)
                    value.ownedRarities[pair.Key] = new Dictionary<StoryRarity, int> { { StoryRarity.N, pair.Value } };
                value.player.mainRarities.Clear(); value.player.extraRarities.Clear(); value.player.sideRarities.Clear();
            }
            value.Validate();
            return value;
        }

        private static StorySave Upgrade(StorySaveV1 legacy)
        {
            if (legacy == null) return null;
            var value = new StorySave
            {
                dp = legacy.dp,
                completedDuels = legacy.completedDuels,
                wins = legacy.wins,
                lastSettledDuel = legacy.lastSettledDuel,
                owned = legacy.owned,
                player = legacy.player,
                characterWins = legacy.characterWins
            };
            if (legacy.opponents != null)
                foreach (var opponent in legacy.opponents)
                    value.SetOpponent(opponent.Key, 1, opponent.Value);
            return value;
        }

        [Serializable]
        private sealed class StorySaveV1
        {
            public int dp = 0;
            public int completedDuels = 0;
            public int wins = 0;
            public string lastSettledDuel = "";
            public Dictionary<int, int> owned = new Dictionary<int, int>();
            public StoryDeck player = new StoryDeck();
            public Dictionary<string, StoryDeck> opponents = new Dictionary<string, StoryDeck>();
            public Dictionary<string, int> characterWins = new Dictionary<string, int>();
        }

        public void Commit(StorySave next)
        {
            next.Validate();
            Directory.CreateDirectory(DirectoryPath);
            string temporary = SavePath + ".tmp";
            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(next, Formatting.Indented));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                // Flush() is available in the game's stripped Mono profile on every target.
                stream.Flush();
            }
            if (File.Exists(SavePath)) File.Replace(temporary, SavePath, SavePath + ".bak");
            else File.Move(temporary, SavePath);
            Current = next;
        }
    }
}
