using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MDPro3.Plugins.Features.StoryMode;
using Newtonsoft.Json;

internal static class StoryModeTests
{
    private static int checks;
    private static void Expect(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception) { rejected = true; }
        Expect(rejected, message);
    }
    private static StoryDeck Starter()
    {
        var deck = new StoryDeck();
        for (int i = 1; i <= 20; i++) { deck.main.Add(i); deck.main.Add(i); }
        return deck;
    }
    private static string Validate(StoryDeck deck, StorySave save = null) => StoryProgress.ValidateDeck(deck, save?.owned,
        id => id > 0 && id <= 100, id => id >= 90, id => id == 21 ? 1 : id);
    private static byte[] Start(int player)
    { var packet = new byte[19]; packet[0] = 1; packet[1] = 4; packet[2] = (byte)player; return packet; }

    private static void Main(string[] args)
    {
        var rules = new StoryRules(); rules.Validate();
        Expect(rules.RequiredDuels(0) == 0 && rules.RequiredDuels(4) == 0, "oldest five initially unlocked");
        Expect(rules.RequiredDuels(5) == 3 && rules.RequiredDuels(6) == 6, "chronological unlock thresholds");
        var grouped = new StoryRules { packsPerUnlock = 2 };
        Expect(grouped.RequiredDuels(5) == 3 && grouped.RequiredDuels(6) == 3 && grouped.RequiredDuels(7) == 6, "group unlocks");
        Expect(rules.WinReward(1) == 100 && rules.WinReward(2) == 200 && rules.WinReward(10) == 1000,
            "difficulty scales win reward");
        Reject(() => rules.WinReward(0), "level zero reward rejected");
        Reject(() => rules.WinReward(11), "level eleven reward rejected");
        Reject(() => new StoryRules { packPrice = 0 }.Validate(), "invalid price rejected");
        var save = StorySave.New(Starter());
        Expect(save.dp == 0 && save.owned.Values.Sum() == 40, "initial ownership granted once");
        var levelOne = Starter();
        var levelSeven = Starter(); levelSeven.main[0] = 22;
        save.SetOpponent("0001", 1, levelOne);
        save.SetOpponent("0001", 7, levelSeven);
        levelSeven.main[0] = 23;
        Expect(save.TryGetOpponent("0001", 1, out var storedOne) && storedOne.main[0] == 1,
            "level one opponent deck stored");
        Expect(save.TryGetOpponent("0001", 7, out var storedSeven) && storedSeven.main[0] == 22,
            "level seven opponent deck stored independently by copy");
        Expect(!save.TryGetOpponent("0001", 2, out _), "unconfigured opponent level remains absent");
        Reject(() => save.SetOpponent("0001", 0, Starter()), "level zero deck rejected");
        Reject(() => save.SetOpponent("0001", 11, Starter()), "level eleven deck rejected");
        Expect(Validate(save.player, save) == null, "starter is playable");
        var deck = save.player.Copy(); deck.main.RemoveAt(0);
        Expect(Validate(deck, save) != null, "short deck rejected");
        Expect(StoryProgress.ValidateDeck(deck, save.owned, id => id > 0 && id <= 100, id => id >= 90, id => id, false) == null,
            "incomplete native editor draft may be imported");
        deck.side.Add(55);
        Expect(StoryProgress.ValidateDeck(deck, save.owned, id => id > 0 && id <= 100, id => id >= 90, id => id, false) != null,
            "incomplete import still enforces ownership");
        deck = save.player.Copy(); deck.main.Add(21);
        Expect(Validate(deck, save) != null && Validate(deck) == null, "opponent may use unowned card");
        deck.main.Add(21);
        Expect(Validate(deck) != null, "alternate arts share three-copy limit");
        deck = save.player.Copy(); deck.side.Add(1);
        Expect(Validate(deck, save) != null && Validate(deck) == null, "side shares ownership count");
        deck = save.player.Copy(); deck.extra.Add(2);
        Expect(Validate(deck) != null, "ordinary card in extra rejected");
        deck = save.player.Copy(); deck.main.Add(90);
        Expect(Validate(deck) != null, "extra monster in main rejected");
        deck = save.player.Copy(); deck.main.Add(999);
        Expect(Validate(deck) != null, "missing card rejected");
        Reject(() => StoryProgress.Buy(save, rules, 0, new[] { 25 }, _ => 0), "no free packs");
        Expect(save.dp == 0 && !save.owned.ContainsKey(25), "failed purchase has no side effects");
        int beforeInvalidDuels = save.completedDuels;
        Reject(() => StoryProgress.Settle(save, rules, "invalid-low", "0001", 0, true), "level zero settlement rejected");
        Reject(() => StoryProgress.Settle(save, rules, "invalid-high", "0001", 11, true), "level eleven settlement rejected");
        Expect(save.dp == 0 && save.completedDuels == beforeInvalidDuels && save.lastSettledDuel == "",
            "invalid levels do not mutate progress");
        Expect(StoryProgress.Settle(save, rules, "a", "0001", 1, true), "level one win counted");
        Expect(save.dp == 100 && save.wins == 1 && save.completedDuels == 1, "win rewards");
        Expect(!StoryProgress.Settle(save, rules, "a", "0001", 10, true) && save.dp == 100, "duplicate result ignored");
        Reject(() => StoryProgress.Buy(save, rules, 5, new[] { 25 }, _ => 0), "locked pack rejected");
        Reject(() => StoryProgress.Buy(save, rules, -1, new[] { 25 }, _ => 0), "foreign pack rejected");
        Reject(() => StoryProgress.Buy(save, rules, 0, new int[0], _ => 0), "empty pack rejected");
        var draw = StoryProgress.Buy(save, rules, 0, new[] { 25 }, _ => 0);
        Expect(draw.Count == 3 && save.owned[25] == 3 && save.dp == 0, "exactly three, repeats accumulate");
        StoryProgress.Settle(save, rules, "b", "0001", 2, false);
        StoryProgress.Settle(save, rules, "c", "0001", 10, false);
        Expect(save.completedDuels == 3 && save.dp == 0 && save.wins == 1, "loss/draw progress without DP");
        StoryProgress.Settle(save, rules, "d", "0001", 2, true);
        StoryProgress.Buy(save, rules, 5, new[] { 25 }, _ => 0);
        Expect(save.owned[25] == 6 && save.dp == 100, "level two reward and unlock boundary purchase");
        var topReward = StorySave.New(Starter());
        StoryProgress.Settle(topReward, rules, "top", "0001", 10, true);
        Expect(topReward.dp == 1000, "level ten settlement grants one thousand DP");

        var packets = new StoryDuelPackets();
        packets.Observe(new byte[] { 1, 5, 0, 1 }); Expect(!packets.Finished, "win without started challenge ignored");
        packets.Observe(Start(0x10)); Expect(!packets.Started, "spectator start ignored");
        packets.Observe(new byte[] { 1, 4, 0 }); Expect(!packets.Started, "truncated start ignored");
        packets.Observe(Start(0)); packets.Observe(new byte[] { 1, 5, 0, 1 });
        Expect(packets.Finished && packets.Won, "first-player victory");
        packets.Observe(new byte[] { 1, 5, 1, 1 }); Expect(packets.Won, "duplicate can't replace result");
        packets = new StoryDuelPackets(); packets.Observe(Start(1)); packets.Observe(new byte[] { 1, 5, 1, 1 });
        Expect(packets.Won, "second-player victory mapped correctly");
        packets = new StoryDuelPackets(); packets.Observe(Start(1)); packets.Observe(new byte[] { 1, 5, 0, 1 });
        Expect(packets.Finished && !packets.Won, "loss mapped correctly");
        packets = new StoryDuelPackets(); packets.Observe(Start(0)); packets.Observe(new byte[] { 1, 5, 2, 1 });
        Expect(packets.Finished && !packets.Won, "draw not a win");
        packets = new StoryDuelPackets(); packets.Observe(Start(0)); packets.Observe(new byte[] { 0x16 });
        Expect(!packets.Finished, "disconnect/end without core WIN not rewarded");

        string root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        var store = new StoryStore(Path.Combine(root, Guid.NewGuid().ToString("N")));
        store.Load(Starter);
        var next = store.Current.Copy(); StoryProgress.Settle(next, rules, "saved", "0001", 1, true); store.Commit(next);
        var loaded = new StoryStore(store.DirectoryPath); loaded.Load(() => throw new Exception("starter must not be re-granted"));
        Expect(loaded.Current.dp == 100 && loaded.Current.owned.Values.Sum() == 40, "restart preserves economy");
        Expect(!StoryProgress.Settle(loaded.Current, rules, "saved", "0001", 1, true), "settlement id persisted");

        string migrationPath = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(migrationPath);
        var legacyDeck = Starter(); legacyDeck.main[0] = 22;
        File.WriteAllText(Path.Combine(migrationPath, "progress.json"), JsonConvert.SerializeObject(new
        {
            version = 1,
            dp = 350,
            completedDuels = 4,
            wins = 2,
            lastSettledDuel = "legacy",
            owned = StorySave.New(Starter()).owned,
            player = Starter(),
            opponents = new Dictionary<string, StoryDeck> { { "0002", legacyDeck } },
            characterWins = new Dictionary<string, int> { { "0002", 2 } }
        }));
        var migrated = new StoryStore(migrationPath); migrated.Load(() => throw new Exception("migration must retain starter"));
        Expect(migrated.Current.version == StorySave.CurrentVersion && migrated.Current.dp == 350
            && migrated.LoadNotice != null, "version one save migrates with progress");
        Expect(migrated.Current.TryGetOpponent("0002", 1, out var migratedDeck) && migratedDeck.main[0] == 22
            && !migrated.Current.TryGetOpponent("0002", 2, out _), "legacy opponent deck migrates to level one only");
        migrated = new StoryStore(migrationPath); migrated.Load(() => throw new Exception("migrated save must reload"));
        Expect(migrated.Current.version == StorySave.CurrentVersion
            && migrated.Current.TryGetOpponent("0002", 1, out _), "migrated version two save persists");
        File.WriteAllText(store.SavePath, "{bad json");
        loaded = new StoryStore(store.DirectoryPath); loaded.Load(Starter);
        Expect(loaded.Current.dp == 0 && loaded.LoadNotice != null, "backup recovery, no silent reset");
        Expect(Directory.GetFiles(store.DirectoryPath, "*.damaged-*").Length == 1, "damaged primary retained");
        next = loaded.Current.Copy(); next.dp = 200;
        File.WriteAllText(loaded.SavePath + ".tmp", "occupied");
        using (File.Open(loaded.SavePath + ".tmp", FileMode.Open, FileAccess.Read, FileShare.None))
            Reject(() => loaded.Commit(next), "write failure reported");
        Expect(loaded.Current.dp == 0, "failed save doesn't commit memory");
        File.WriteAllText(store.SavePath, "{}"); File.WriteAllText(store.SavePath + ".bak", "{}");
        Reject(() => new StoryStore(store.DirectoryPath).Load(Starter), "both bad copies preserved, no new profile");
        Console.WriteLine("StoryMode tests: PASS (" + checks + " checks)");
    }
}
