using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using MDPro3.Plugins.Features.StoryMode;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp;
using YGOSharp.Network;
using YGOSharp.Network.Enums;
using YGOSharp.Network.Utils;
using YGOSharp.OCGWrapper;
using YGOSharp.OCGWrapper.Enums;
using CoreDuel = YGOSharp.OCGWrapper.Duel;
using BotDuel = WindBot.Game.Duel;
using ClientCard = WindBot.Game.ClientCard;

internal sealed class CoreStoryExecutor : StoryLuckyExecutor
{
    internal CoreStoryExecutor(GameAI ai, BotDuel duel) : base(ai, duel) { }
    public override IList<ClientCard> OnSelectCard(IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
    {
        var selected = base.OnSelectCard(cards, min, max, hint, cancelable);
        StoryCoreAiTests.SelectionTrace(Card, ActivateDescription, Duel, hint, cards, selected);
        return selected;
    }
}

// The opponent's policy is identical in both assemblies: use the one supplied
// interruption at its first core-legal window. It never runs the revised AI.
internal sealed class CoreInterruptExecutor : Executor
{
    internal CoreInterruptExecutor(GameAI ai, BotDuel duel) : base(ai, duel)
    {
        AddExecutor(ExecutorType.Activate, 14558127);
        AddExecutor(ExecutorType.Activate, 10045474);
        AddExecutor(ExecutorType.Activate, 27204311);
        // Deterministic pressure opponent for multi-turn regression runs.
        AddExecutor(ExecutorType.Activate, 12580477, () => Enemy.GetMonsterCount() > 0);
        AddExecutor(ExecutorType.Activate, 53129443, () => Enemy.GetMonsterCount() > Bot.GetMonsterCount());
        AddExecutor(ExecutorType.SummonOrSet);
    }
    public override IList<ClientCard> OnSelectCard(IList<ClientCard> cards, int min, int max, int hint, bool cancelable) =>
        cards.OrderByDescending(c => c.Controller == 1 && Duel.CurrentChain.Contains(c))
            .ThenByDescending(c => c.Controller == 1).ThenByDescending(c => c.Attack).ThenBy(c => c.Id).Take(Math.Max(1, min)).ToList();
    public override BattlePhaseAction OnBattle(IList<ClientCard> attackers, IList<ClientCard> defenders)
    {
        foreach (var attacker in attackers.OrderByDescending(c => c.Attack))
        {
            if (defenders.Count == 0) return AI.Attack(attacker, null);
            var target = defenders.Where(c => c.GetDefensePower() < attacker.Attack).OrderBy(c => c.GetDefensePower()).FirstOrDefault();
            if (target != null) return AI.Attack(attacker, target);
        }
        return null;
    }
}

// Actual native ocgcore + installed Lua scripts + server filtering + woven
// WindBot callbacks. Only network transport and dialogs are replaced. Saves and
// databases are copied/read only. No hypothetical state is passed off as a duel.
internal static unsafe class StoryCoreAiTests
{
    private static readonly Dictionary<object, Action<byte[]>> endpoints = new Dictionary<object, Action<byte[]>>();
    private static Api.ScriptReader scriptReader;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCard
    {
        internal uint Code, Alias;
        internal fixed ushort Setcodes[16];
        internal uint Type, Level, Attribute, Race;
        internal int Attack, Defense;
        internal uint LScale, RScale, LinkMarker;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadNativeCard(uint code, NativeCard* data);
    [DllImport("ocgcore", CallingConvention = CallingConvention.Cdecl)]
    private static extern void set_card_reader(ReadNativeCard reader);
    private static ReadNativeCard cardReader;
    private static IntPtr scriptBuffer;
    private static ZipArchive scripts;
    private static readonly Dictionary<string, byte[]> scriptCache = new Dictionary<string, byte[]>();
    private static StreamWriter trace;
    private static StreamWriter interactions;
    private static string interruption;
    private static int turnLimit = 1;
    internal static void SelectionTrace(ClientCard source, int description, BotDuel duel, int hint, IList<ClientCard> offered, IList<ClientCard> selected)
    {
        if (trace == null) return;
        int solving = duel.SolvingChainIndex;
        string chain = solving > 0 && solving <= duel.CurrentChainInfo.Count ? duel.CurrentChainInfo[solving - 1].ActivateId.ToString() : "-";
        trace.WriteLine("SELECT " + (source?.Id ?? 0) + " " + description + " resolving=" + chain + " hint=" + hint +
            " offered=" + Cards(offered) + " chosen=" + (selected == null ? "queued" : Cards(selected)));
    }
    private static void Set(object target, string name, object value)
    {
        Type type = target as Type ?? target.GetType();
        object instance = target is Type ? null : target;
        while (type != null)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var field = type.GetField(name, flags);
            if (field != null) { field.SetValue(instance, value); return; }
            var property = type.GetProperty(name, flags);
            if (property?.GetSetMethod(true) != null) { property.SetValue(instance, value); return; }
            type = type.BaseType;
        }
        throw new Exception("Missing fixture member " + name);
    }
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static GameBehavior Client(YGOClient connection, string deckFile, out GameAI ai)
    {
        var game = Empty<GameClient>(); game.DeckFile = deckFile; game._chat = false;
        Set(game, "Connection", connection);
        var duel = new BotDuel();
        ai = Empty<GameAI>();
        foreach (var field in typeof(GameAI).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            if (field.FieldType.IsGenericType)
            {
                var generic = field.FieldType.GetGenericTypeDefinition();
                if (generic == typeof(IList<>) || generic == typeof(List<>))
                    field.SetValue(ai, Activator.CreateInstance(typeof(List<>).MakeGenericType(field.FieldType.GetGenericArguments())));
                if (generic == typeof(Dictionary<,>)) field.SetValue(ai, Activator.CreateInstance(field.FieldType));
            }
        foreach (var name in new[] { "m_selector_pointer", "m_option", "m_yesno", "m_number" }) Set(ai, name, -1);
        var dialogs = Empty<Dialogs>(); Set(dialogs, "_game", game);
        Set(ai, "_dialogs", dialogs); Set(ai, "Game", game); Set(ai, "Duel", duel);
        ai.Executor = deckFile == null ? (Executor)new CoreInterruptExecutor(ai, duel) : new CoreStoryExecutor(ai, duel);
        var behavior = Empty<GameBehavior>();
        Set(behavior, "Game", game); Set(behavior, "Connection", connection); Set(behavior, "_ai", ai);
        Set(behavior, "_duel", duel); Set(behavior, "_room", new Room());
        Set(behavior, "_packets", new Dictionary<StocMessage, Action<BinaryReader>>());
        Set(behavior, "_messages", new Dictionary<GameMessage, Action<BinaryReader>>());
        typeof(GameBehavior).GetMethod("RegisterPackets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behavior, null);
        return behavior;
    }
    private static string Name(int id) => NamedCard.Get(id)?.Name ?? id.ToString();
    private static string Cards(IEnumerable<ClientCard> cards) => string.Join(" | ", cards.Select(c => c.Id + ":" + Name(c.Id)));
    private static string Ids(IEnumerable<ClientCard> cards) => string.Join(",", cards.Select(c => c.Id));
    private static string CardStates(IEnumerable<ClientCard> cards) => string.Join(";", cards.Select(c =>
        c.Id + ":" + c.Attack + ":" + c.Defense + ":" + (c.IsDisabled() ? 1 : 0) + ":" + (c.IsFaceup() ? 1 : 0) + ":" + string.Join(",", c.Overlays)));
    private static void Run(string[] row, StreamWriter results, StreamWriter endstates)
    {
        string label = row[0]; uint seed = uint.Parse(row[1]);
        Set(typeof(WindBot.Program), "Rand", new Random((int)seed));
        int[] main = row[2].Split(',').Select(int.Parse).ToArray(), extra = row[3].Split(',').Select(int.Parse).ToArray();
        string deckFile = Path.Combine(Path.GetDirectoryName(results.BaseStream is FileStream file ? file.Name : ""), label + ".ydk");
        File.WriteAllLines(deckFile, new[] { "#main" }.Concat(main.Select(i => i.ToString())).Concat(new[] { "#extra" }).Concat(extra.Select(i => i.ToString())).Concat(new[] { "!side" }));
        // Optional fifth column creates an explicit mid-combo fixture, never a
        // measured opening. Remove the physical copies from their original decks.
        int[] setup = row.Length > 4 && row[4].Length > 0 ? row[4].Split(',').Select(int.Parse).ToArray() : new int[0];
        int[] setupGrave = row.Length > 5 && row[5].Length > 0 ? row[5].Split(',').Select(int.Parse).ToArray() : new int[0];
        if (setup.Length > 5) throw new Exception("Fixture exceeds the main monster zones");
        var setupMain = main.ToList(); var setupExtra = extra.ToList();
        foreach (int id in setup.Concat(setupGrave))
            if (!setupMain.Remove(id) && !setupExtra.Remove(id)) throw new Exception("Fixture card absent from its deck: " + id);
        main = setupMain.ToArray(); extra = setupExtra.ToArray();
        var server = new Game(null) { MasterRule = 5, StartHand = 5, Timer = 9999 };
        var core = CoreDuel.Create(seed);
        Set(server, "_duel", core); Set(server, "State", GameState.Duel);
        var replay = Empty<Replay>(); Set(replay, "Disabled", true); Set(server, "Replay", replay);
        var responses = new Queue<byte[]>(); GameAI[] ais = new GameAI[2];
        int actions = 0, summons = 0, prompts = 0; bool done = false; string luaError = null;
        int ownNegates = 0, ownAttackStops = 0, friendlyEffectMoves = 0, attacks = 0, spOwnBanishes = 0;
        GameMessage lastPrompt = 0;
        endpoints.Clear();
        for (int player = 0; player < 2; player++)
        {
            int p = player;
            var incoming = new YGOClient(); var outgoing = new YGOClient();
            var behavior = Client(outgoing, player == 0 ? deckFile : null, out ais[player]);
            var participant = new Player(server, incoming) { Type = player };
            server.Players[player] = server.CurPlayers[player] = participant;
            endpoints[incoming] = bytes =>
            {
                if (bytes[0] != (byte)StocMessage.GameMsg) return;
                var msg = (GameMessage)bytes[1];
                if (msg == GameMessage.Retry) throw new Exception("Core rejected response after " + lastPrompt);
                if (msg == GameMessage.SelectIdleCmd && p == 0)
                {
                    trace.WriteLine("BOARD " + Cards(ais[p].Duel.Fields[0].GetMonsters()) + " HAND " + Cards(ais[p].Duel.Fields[0].Hand));
                    trace.WriteLine("LEVELS " + string.Join(",", ais[p].Duel.Fields[0].GetMonsters().Select(c => c.Id + ":" + c.Level + "@" + c.Sequence)));
                }
                behavior.OnPacket(new BinaryReader(new MemoryStream(bytes)));
            };
            endpoints[outgoing] = bytes =>
            {
                if (bytes[0] != (byte)CtosMessage.Response) return;
                prompts++; if (prompts > 800 * turnLimit) throw new Exception("Prompt limit exceeded");
                if (p == 0 && lastPrompt == GameMessage.SelectIdleCmd)
                {
                    actions++; int response = BitConverter.ToInt32(bytes, 1), kind = response & 0xffff, index = response >> 16;
                    var phase = ais[p].Duel.MainPhase;
                    trace.WriteLine("OFFER " + string.Join(" | ", phase.ActivableCards.Select((c, i) => c.Id + ":" + phase.ActivableDescs[i])));
                    var list = kind == 0 ? phase.SummonableCards : kind == 1 ? phase.SpecialSummonableCards : kind == 5 ? phase.ActivableCards : null;
                    string selected = list != null && index < list.Count ? Name(list[index].Id) : "";
                    trace.WriteLine("ACTION " + kind + " " + index + " " + selected);
                }
                trace.WriteLine("RESPONSE " + p + " " + lastPrompt + " " + BitConverter.ToString(bytes.Skip(1).ToArray()));
                responses.Enqueue(bytes.Skip(1).ToArray());
            };
        }
        var analyser = new GameAnalyser(server);
        core.SetAnalyzer((msg, reader, raw) =>
        {
            lastPrompt = msg;
            trace.WriteLine("CORE " + msg + " " + reader.BaseStream.Position);
            var observed = ais[0].Duel;
            int solving = observed.SolvingChainIndex;
            var solvingLink = solving > 0 && solving <= observed.CurrentChainInfo.Count ? observed.CurrentChainInfo[solving - 1] : null;
            long position = reader.BaseStream.Position;
            if (msg == GameMessage.Attack) attacks++;
            if ((msg == GameMessage.ChainNegated || msg == GameMessage.ChainDisabled) && solvingLink?.ActivatePlayer == 0)
            {
                int link = reader.ReadByte();
                // When the disabled link itself starts resolving, the core can
                // emit CHAIN_DISABLED for an earlier Impermanence. That is not
                // the monster negating itself; only a distinct resolving link
                // can establish this causal attribution.
                if (link > 0 && link != solving && link <= observed.CurrentChainInfo.Count && observed.CurrentChainInfo[link - 1].ActivatePlayer == 0)
                {
                    ownNegates++;
                    trace.WriteLine("OWN-NEGATE " + solvingLink.ActivateId + " target=" + observed.CurrentChainInfo[link - 1].ActivateId);
                }
            }
            if (msg == GameMessage.AttackDisabled && solvingLink?.ActivatePlayer == 0 && observed.Fields[1].UnderAttack)
            {
                ownAttackStops++;
                trace.WriteLine("OWN-ATTACK-STOP " + solvingLink.ActivateId);
            }
            if (msg == GameMessage.Move && solvingLink?.ActivatePlayer == 0)
            {
                int movingId = reader.ReadInt32(); int previousPlayer = reader.ReadByte(), previousLocation = reader.ReadByte();
                reader.ReadInt16(); reader.ReadByte(); int destination = reader.ReadByte(); reader.ReadInt16(); int reason = reader.ReadInt32();
                if (previousPlayer == 0 && destination == 32 && (reason & 0x40) != 0 && (reason & 0x80) == 0 &&
                    solvingLink.ActivateId == 29301450 && solvingLink.ActivateDescription == 29301450 * 16)
                {
                    spOwnBanishes++;
                    trace.WriteLine("SP-OWN-PERMANENT-BANISH " + movingId + " from=" + previousLocation);
                }
                // REASON_EFFECT=0x40, REASON_COST=0x80. This is deliberately an
                // audit count, NOT a claim every own effect movement is harmful.
                if (previousPlayer == 0 && (previousLocation == 4 || previousLocation == 8) &&
                    destination != previousLocation && (reason & 0x40) != 0 && (reason & 0x80) == 0)
                {
                    friendlyEffectMoves++;
                    trace.WriteLine("OWN-EFFECT-MOVE " + solvingLink.ActivateId + " target=" + movingId + " destination=" + destination + " reason=" + reason);
                }
            }
            reader.BaseStream.Position = position;
            if (msg == GameMessage.Win || msg == GameMessage.NewTurn && server.TurnCount >= turnLimit) { done = true; return 1; }
            if (msg == GameMessage.SpSummoning) summons++;
            if (msg == GameMessage.ConfirmCards)
            {
                // Current core/WindBot include skip_panel, absent in the legacy server.
                byte player = reader.ReadByte(), skip = reader.ReadByte(), count = reader.ReadByte();
                var packet = YGOSharp.GamePacketFactory.Create(msg);
                packet.Write(player); packet.Write(skip); packet.Write(count); packet.Write(reader.ReadBytes(count * 7));
                if (player == 0 || player == 1) server.SendToTeam(packet, player); else server.SendToAll(packet);
                return 0;
            }
            if (msg == GameMessage.SelectChain)
            {
                byte player = reader.ReadByte(), count = reader.ReadByte();
                var packet = YGOSharp.GamePacketFactory.Create(msg);
                packet.Write(player); packet.Write(count); packet.Write(reader.ReadBytes(9 + count * 14));
                if (count == 0) { core.SetResponse(-1); return 0; }
                server.WaitForResponse(player); server.SendToTeam(packet, player); return 1;
            }
            if (msg == GameMessage.AnnounceCard)
            {
                byte player = reader.ReadByte(), count = reader.ReadByte();
                var packet = YGOSharp.GamePacketFactory.Create(msg);
                packet.Write(player); packet.Write(count); packet.Write(reader.ReadBytes(count * 4));
                server.WaitForResponse(player); server.SendToTeam(packet, player); return 1;
            }
            int result = analyser.Analyse(msg, reader, raw);
            if (msg == GameMessage.Chaining)
            {
                var link = ais[0].Duel.CurrentChainInfo.LastOrDefault();
                if (link != null) trace.WriteLine("CHAIN " + link.ActivatePlayer + " " + link.ActivateId + " " + link.ActivateDescription);
            }
            return result;
        });
        core.SetErrorHandler(error => { luaError = error; });
        core.InitPlayers(8000, 5, 1);
        foreach (int id in main.Reverse()) core.AddCard(id, 0, CardLocation.Deck);
        foreach (int id in extra) core.AddCard(id, 0, CardLocation.Extra);
        for (int i = 0; i < setup.Length; i++)
            Api.new_card(core.GetNativePtr(), (uint)setup[i], 0, 0, (byte)CardLocation.MonsterZone, (byte)i, (byte)CardPosition.FaceUpAttack);
        foreach (int id in setupGrave) core.AddCard(id, 0, CardLocation.Grave);
        int interruptId = interruption == "ash" ? 14558127 : interruption == "imperm" ? 10045474 : interruption == "nibiru" ? 27204311 : 89631139;
        var opponentDeck = interruption == "pressure" ?
            new[] { 10045474, 69247929, 12580477, 69247929, 53129443 }.Concat(Enumerable.Repeat(69247929, 35)) :
            new[] { interruptId }.Concat(Enumerable.Repeat(89631139, 39));
        foreach (int id in opponentDeck.Reverse()) core.AddCard(id, 1, CardLocation.Deck);
        var start = YGOSharp.GamePacketFactory.Create(GameMessage.Start);
        start.Write((byte)0); start.Write((byte)5); start.Write(8000); start.Write(8000);
        start.Write((short)main.Length); start.Write((short)extra.Length); start.Write((short)40); start.Write((short)0);
        server.SendToTeam(start, 0); start.BaseStream.Position = 2; start.Write((byte)1); server.SendToTeam(start, 1);
        server.RefreshExtra(0); server.RefreshExtra(1);
        if (setup.Length > 0 || setupGrave.Length > 0)
        {
            // UpdateData updates existing client objects; a mid-combo fixture
            // has no preceding Move packets to create them as an opening does.
            for (int player = 0; player < 2; player++)
            {
                for (int i = 0; i < setup.Length; i++)
                    ais[player].Duel.Fields[player].MonsterZone[i] = new ClientCard(setup[i], CardLocation.MonsterZone, i,
                        (int)CardPosition.FaceUpAttack) { Controller = player };
                for (int i = 0; i < setupGrave.Length; i++)
                    ais[player].Duel.Fields[player].Graveyard.Add(new ClientCard(setupGrave[i], CardLocation.Grave, i,
                        (int)CardPosition.FaceUpAttack) { Controller = player });
            }
            server.RefreshAll();
        }
        var watch = Stopwatch.StartNew();
        trace.WriteLine("SCENARIO " + label + " " + seed);
        if (setup.Length > 0) trace.WriteLine("FIXTURE " + string.Join(",", setup));
        if (setupGrave.Length > 0) trace.WriteLine("FIXTURE-GRAVE " + string.Join(",", setupGrave));
        try
        {
            core.Start((5 << 16) | 0x10);
            while (!done)
            {
                int status = core.Process();
                if (luaError != null) throw new Exception("Lua: " + luaError);
                if (done) break;
                if (responses.Count != 1) throw new Exception("Expected one response, got " + responses.Count + " status " + status + " after " + lastPrompt);
                var response = responses.Dequeue();
                // Current core copies SIZE_RETURN_VALUE (512), while the legacy
                // managed wrapper allocates 64. Match the installed core ABI.
                var nativeResponse = Marshal.AllocHGlobal(512);
                try
                {
                    Marshal.Copy(new byte[512], 0, nativeResponse, 512);
                    Marshal.Copy(response, 0, nativeResponse, response.Length);
                    Api.set_responseb(core.GetNativePtr(), nativeResponse);
                }
                finally { Marshal.FreeHGlobal(nativeResponse); }
                if (watch.ElapsedMilliseconds > 60000) throw new Exception("Decision time limit exceeded");
            }
            server.RefreshAll();
            var board = ais[0].Duel.Fields[0].GetMonsters();
            var own = ais[0].Duel.Fields[0];
            endstates.WriteLine(label + "\t" + own.LifePoints + "\t" + Ids(own.Hand) + "\t" + CardStates(board) + "\t" +
                CardStates(own.GetSpells()) + "\t" + Ids(own.Graveyard) + "\t" + Ids(own.Banished) + "\t" + Ids(own.ExtraDeck));
            endstates.Flush();
            string summary = label + "\t" + seed + "\t" + actions + "\t" + summons + "\t" + watch.ElapsedMilliseconds + "\t" + Cards(board);
            results.WriteLine(summary); results.Flush(); Console.WriteLine(summary); trace.WriteLine("END " + summary);
            interactions.WriteLine(label + "\t" + server.TurnCount + "\t" + attacks + "\t" + ownNegates + "\t" + ownAttackStops + "\t" +
                friendlyEffectMoves + "\t" + own.LifePoints + "\t" + ais[0].Duel.Fields[1].LifePoints + "\t" + spOwnBanishes);
            interactions.Flush();
        }
        finally { core.End(); trace.Flush(); }
    }
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        interruption = args.Length > 3 ? args[3] : "none";
        turnLimit = args.Length > 4 ? int.Parse(args[4]) : 1;
        Config.Load(new string[0]); Set(typeof(BanlistManager), "Banlists", new List<Banlist>());
        Api.Init(args[0], "script", "cards.cdb"); NamedCardsManager.Init(Path.Combine(args[0], "cards.cdb"));
        // The installed core uses the same 16-setcode ABI as MDPro3.CoreWrapper;
        // the legacy YGOSharp Api card callback still has a single Int64 setcode.
        cardReader = (uint code, NativeCard* data) =>
        {
            *data = new NativeCard(); var card = NamedCard.Get((int)code); if (card == null) return code;
            data->Code = code; data->Alias = (uint)card.Alias; data->Type = (uint)card.Type;
            data->Level = (uint)card.Level; data->Attribute = (uint)card.Attribute; data->Race = (uint)card.Race;
            data->Attack = card.Attack; data->Defense = card.Defense;
            data->LScale = (uint)card.LScale; data->RScale = (uint)card.RScale; data->LinkMarker = (uint)card.LinkMarker;
            ulong codes = (ulong)card.Setcode;
            for (int i = 0; i < 4; i++) { data->Setcodes[i] = (ushort)(codes & 0xffff); codes >>= 16; }
            return code;
        };
        set_card_reader(cardReader);
        scripts = ZipFile.OpenRead(args[1]); scriptBuffer = Marshal.AllocHGlobal(1024 * 1024);
        scriptReader = (string name, int* length) =>
        {
            string key = name.Replace("./script/", "").Replace("script/", "");
            if (!scriptCache.TryGetValue(key, out var bytes))
            {
                var entry = scripts.GetEntry(key) ?? scripts.GetEntry("script/" + key);
                if (entry == null) { *length = 0; return IntPtr.Zero; }
                using (var stream = entry.Open()) using (var data = new MemoryStream()) { stream.CopyTo(data); bytes = data.ToArray(); }
                scriptCache[key] = bytes;
            }
            if (bytes.Length > 1024 * 1024) throw new Exception("Script too large");
            *length = bytes.Length; Marshal.Copy(bytes, 0, scriptBuffer, bytes.Length); return scriptBuffer;
        };
        Api.set_script_reader(scriptReader);
        typeof(YGOClient).GetField("StoryTestSend").SetValue(null, new Action<object, byte[]>((client, bytes) => endpoints[client](bytes)));
        using (trace = new StreamWriter(Path.Combine(args[0], "trace.log"), false, Encoding.UTF8))
        using (var results = new StreamWriter(Path.Combine(args[0], "results.tsv"), false, Encoding.UTF8))
        using (var endstates = new StreamWriter(Path.Combine(args[0], "endstates.tsv"), false, Encoding.UTF8))
        using (interactions = new StreamWriter(Path.Combine(args[0], "interactions.tsv"), false, Encoding.UTF8))
            foreach (string line in File.ReadLines(args[2]))
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#")) Run(line.Split('\t'), results, endstates);
        Api.Dispose(); scripts.Dispose(); Marshal.FreeHGlobal(scriptBuffer);
        return 0;
    }
}
