using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MDPro3.Plugins.Features.StoryMode
{
    [Serializable]
    public sealed class StoryRules
    {
        public int initialPacks = 5;
        public int duelsPerUnlock = 3;
        public int packsPerUnlock = 1;
        public int winDP = 100;
        public int packPrice = 100;

        public void Validate()
        {
            if (initialPacks < 1 || duelsPerUnlock < 1 || packsPerUnlock < 1
                || winDP < 1 || packPrice < 1)
                throw new InvalidDataException("故事模式规则必须全部为正整数。");
        }

        public int RequiredDuels(int packIndex)
        {
            if (packIndex < initialPacks) return 0;
            long value = ((long)(packIndex - initialPacks) / packsPerUnlock + 1) * duelsPerUnlock;
            return (int)Math.Min(int.MaxValue, value);
        }
    }

    [Serializable]
    public sealed class StoryDeck
    {
        public List<int> main = new List<int>();
        public List<int> extra = new List<int>();
        public List<int> side = new List<int>();
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
            main = new List<int>(main), extra = new List<int>(extra), side = new List<int>(side)
        };
        public string ToYdk() => "#created by MDPro3 story mode\n#main\n"
            + string.Join("\n", main) + "\n#extra\n" + string.Join("\n", extra)
            + "\n!side\n" + string.Join("\n", side) + "\n";
    }

    [Serializable]
    public sealed class StorySave
    {
        public int version = 1;
        public int dp;
        public int completedDuels;
        public int wins;
        public string lastSettledDuel = "";
        public Dictionary<int, int> owned = new Dictionary<int, int>();
        public StoryDeck player = new StoryDeck();
        public Dictionary<string, StoryDeck> opponents = new Dictionary<string, StoryDeck>();
        public Dictionary<string, int> characterWins = new Dictionary<string, int>();
        public StorySave Copy() => JsonConvert.DeserializeObject<StorySave>(JsonConvert.SerializeObject(this));

        public static StorySave New(StoryDeck starter)
        {
            var save = new StorySave { player = starter.Copy() };
            foreach (int id in starter.All())
                save.owned[id] = save.owned.TryGetValue(id, out int n) ? n + 1 : 1;
            return save;
        }

        public void Validate()
        {
            if (version != 1 || dp < 0 || completedDuels < 0 || wins < 0 || wins > completedDuels
                || player == null || player.main == null || player.extra == null || player.side == null
                || owned == null || owned.Count == 0 || opponents == null || characterWins == null)
                throw new InvalidDataException("故事存档结构无效或版本不受支持。");
            foreach (var pair in owned)
                if (pair.Key <= 0 || pair.Value <= 0) throw new InvalidDataException("持有卡片数据无效。");
            foreach (var pair in opponents)
                if (pair.Value == null || pair.Value.main == null || pair.Value.extra == null || pair.Value.side == null)
                    throw new InvalidDataException("角色卡组数据无效。");
        }
    }

    // Pure rules shared by runtime and executable regression tests.
    public static class StoryProgress
    {
        public const int CardsPerPack = 3;

        public static List<int> Buy(StorySave save, StoryRules rules, int packIndex,
            IReadOnlyList<int> pool, Func<int, int> randomIndex)
        {
            if (packIndex < 0 || save.completedDuels < rules.RequiredDuels(packIndex))
                throw new InvalidOperationException("该卡包尚未解锁。");
            if (pool == null || pool.Count == 0) throw new InvalidOperationException("该卡包没有可用卡片。");
            if (save.dp < rules.packPrice) throw new InvalidOperationException("DP 不足，请先挑战角色赢取 DP。");
            var draws = new List<int>(CardsPerPack);
            for (int i = 0; i < CardsPerPack; i++) draws.Add(pool[randomIndex(pool.Count)]);
            // Compute before mutating, including overflow checks. The caller then commits atomically.
            var owned = new Dictionary<int, int>(save.owned);
            foreach (int id in draws) owned[id] = checked((owned.TryGetValue(id, out int n) ? n : 0) + 1);
            save.owned = owned;
            save.dp -= rules.packPrice;
            return draws;
        }

        public static bool Settle(StorySave save, StoryRules rules, string duelId, string character, bool won)
        {
            if (string.IsNullOrEmpty(duelId) || duelId == save.lastSettledDuel) return false;
            int total = checked(save.completedDuels + 1);
            int dp = won ? checked(save.dp + rules.winDP) : save.dp;
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
            Func<int, bool> exists, Func<int, bool> isExtra, Func<int, int> identity, bool requireComplete = true)
        {
            if (deck == null || deck.main == null || deck.extra == null || deck.side == null)
                return "卡组数据不完整。";
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
            try { Current = Read(SavePath); }
            catch (Exception primary)
            {
                try { Current = Read(SavePath + ".bak"); }
                catch { throw new InvalidDataException("存档及备份无法读取，已保留原文件，请先修复存档。", primary); }
                LoadNotice = "主存档无法读取，已加载上一次备份。";
                // Preserve the unreadable primary and restore a valid primary before the next transaction.
                if (File.Exists(SavePath)) File.Copy(SavePath, SavePath + ".damaged-" + DateTime.UtcNow.Ticks);
                File.Copy(SavePath + ".bak", SavePath, true);
            }
        }

        private static StorySave Read(string path)
        {
            var value = JsonConvert.DeserializeObject<StorySave>(File.ReadAllText(path));
            if (value == null) throw new InvalidDataException("存档为空。");
            value.Validate();
            return value;
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
