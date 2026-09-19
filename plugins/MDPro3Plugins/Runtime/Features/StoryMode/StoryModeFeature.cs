using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using MDPro3.Duel.YGOSharp;
using MDPro3.Net;
using MDPro3.Plugins.Features.PackBrowser;
using MDPro3.Servant;
using MDPro3.UI;
using MDPro3.Utility;
using Newtonsoft.Json;
using UnityEngine;

namespace MDPro3.Plugins.Features.StoryMode
{
    public sealed class StoryModeFeature : PluginFeature
    {
        public const string FeatureId = "storyMode";
        public override string Id => FeatureId;
        public override string DisplayName => "Story mode: character challenges, DP and card collection";
        internal StoryStore Store { get; private set; }
        internal StoryRules Rules { get; private set; }
        internal List<PackEntry> Packs { get; private set; }
        internal string Notice { get; private set; } = "欢迎来到故事模式。先为角色编辑卡组，再开始挑战。";
        private StoryOverlay overlay;
        private PackBrowserOverlay shop;
        private StoryDuelObserver observer;
        private StoryDeckEditor editor;
        private bool menuPending;
        private bool reopen;
        private StoryDuelPackets duel;
        private string duelId;
        private string opponentId;
        private int opponentLevel;
        private string port;
        private TcpClient connection;
        private bool deckSent, startSent, enteredDuel, settled;
        private bool settlementErrorShown;
        private float launchTime, nextSaveAttempt;
        private string error;
        private string savedCharacter, savedName;
        private Servant.Servant savedReturn;
        private bool ownsServer;
        private bool savedFromSolo, savedFromHost, savedFromHand;
        private readonly System.Random random = new System.Random();
        internal bool Launching => duel != null && !enteredDuel;
        internal bool SuppressRoomChat => duel != null;
        internal string SelectedCharacter;
        internal int SelectedSeries, SelectedPage, SelectedLevel = StoryProgress.MinLevel;

        public override void Enable()
        {
            PluginEvents.ServantChanged += OnServantChanged;
            menuPending = true;
            var go = new GameObject("StoryDuelObserver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            observer = go.AddComponent<StoryDuelObserver>();
            observer.Owner = this;
        }

        public override void Disable()
        {
            PluginEvents.ServantChanged -= OnServantChanged;
            if (shop != null) shop.Close();
            if (overlay != null) overlay.Close();
            RestoreAppearance();
            editor?.Restore();
            if (observer != null) UnityEngine.Object.Destroy(observer.gameObject);
        }

        private void OnServantChanged(Servant.Servant servant)
        {
            menuPending = servant is MainMenu;
            if (duel != null) return;
            if (!(servant is MainMenu))
            {
                if (shop != null) shop.Close();
                if (overlay != null) overlay.Close();
            }
        }

        public override void Tick()
        {
            editor?.Tick();
            if (menuPending && PluginGame.CurrentServant is MainMenu menu && menu.servantUI != null)
            {
                if (MainMenuPackEntry.Inject(menu, Show, "ButtonStoryMode", "故事模式")) menuPending = false;
                if (reopen && duel == null) { reopen = false; Show(); }
            }
            if (duel == null) return;

            // Settle from the live server's WIN packet only, never from replay or UI result flags.
            if (duel.Finished && !settled && Time.unscaledTime >= nextSaveAttempt)
            {
                nextSaveAttempt = Time.unscaledTime + 5f;
                try
                {
                    var next = Store.Current.Copy();
                    StoryProgress.Settle(next, Rules, duelId, opponentId, opponentLevel, duel.Won);
                    Store.Commit(next);
                    settled = true;
                    Notice = duel.Won ? opponentLevel + "级挑战胜利！获得 " + Rules.WinReward(opponentLevel) + " DP。"
                        : opponentLevel + "级挑战结束，已计入卡包解锁进度。";
                    MessageManager.Cast(Notice);
                }
                catch (Exception ex)
                {
                    Notice = "结算保存失败，将重试：" + ex.Message;
                    if (!settlementErrorShown) { MessageManager.Cast(Notice); PluginLog.Error(Notice); settlementErrorShown = true; }
                }
            }

            if (PluginGame.CurrentServant == Program.instance.ocgcore)
            {
                if (!enteredDuel) OcgCore.sideChangedDeck = ToGameDeck(Store.Current.player);
                enteredDuel = true;
                // The native solo Retry button replays its own previous launch settings.
                // Story challenges return through our hub and must never launch that unrelated deck.
                RoomServant.FromSolo = false;
                Program.instance.ocgcore.returnServant = Program.instance.menu;
                if (overlay != null) overlay.Close();
                return;
            }
            if (enteredDuel)
            {
                if (duel.Finished && !settled) return; // retain the pending reward until durable
                if (!duel.Finished) Notice = "挑战中断，未获得 DP，也不增加解锁进度。";
                EndSession();
                return;
            }
            if (!string.IsNullOrEmpty(error) || Time.unscaledTime - launchTime > (startSent ? 180f : 45f))
            {
                CancelChallenge(error ?? "连接 AI 超时，请检查本体对战组件是否可用。");
                return;
            }
            if (PluginGame.CurrentServant != Program.instance.room || !IsChallengeConnection) return;
            int seat = RoomServant.SelfType;
            if (seat < 0 || seat > 1 || RoomServant.players[seat] == null) return;
            if (!deckSent)
            {
                TcpHelper.CtosMessage_UpdateDeck(ToGameDeck(Store.Current.player));
                TcpHelper.CtosMessage_HsReady();
                deckSent = true;
            }
            if (!startSent && RoomServant.IsHost && RoomServant.players[0]?.ready == true && RoomServant.players[1]?.ready == true)
            {
                startSent = true;
                TcpHelper.CtosMessage_HsStart();
            }
        }

        private bool EnsureData()
        {
            if (Store != null) return true;
            try
            {
                if (CharacterSelector.characters == null || CardsManager._cards.Count == 0)
                    throw new InvalidOperationException("角色或卡片数据尚未加载，请稍后重试。");
                string root = PluginConfig.Found ? Path.GetDirectoryName(PluginConfig.LoadedPath)
                    : Path.Combine(Directory.GetCurrentDirectory(), "plugins");
                string rulePath = Path.Combine(root, "story-mode.json");
                Rules = File.Exists(rulePath) ? JsonConvert.DeserializeObject<StoryRules>(File.ReadAllText(rulePath)) : new StoryRules();
                if (Rules == null) throw new InvalidDataException("故事模式配置为空。");
                Rules.Validate();
                Packs = StoryCatalog.Packs();
                if (Packs.Count == 0) throw new InvalidOperationException("卡包数据尚未加载，请稍后重试。");
                var store = new StoryStore(Path.Combine(root, "StoryMode"));
                store.Load(StoryCatalog.Starter);
                Store = store;
                if (!string.IsNullOrEmpty(store.LoadNotice)) Notice = store.LoadNotice;
                return true;
            }
            catch (Exception ex) { MessageManager.Cast(ex.Message); PluginLog.Error("storyMode: " + ex); return false; }
        }

        public void Show()
        {
            if (!EnsureData() || !(PluginGame.CurrentServant is MainMenu) || duel != null) return;
            var browser = PluginRegistry.Get<PackBrowserFeature>(PackBrowserFeature.FeatureId);
            browser?.CloseBrowser();
            if (overlay == null) overlay = StoryOverlay.Open(this);
        }

        internal int PackIndex(PackEntry pack) => Packs.FindIndex(p => p.FullName == pack.FullName);
        internal bool Unlocked(PackEntry pack)
        {
            int index = PackIndex(pack);
            return index >= 0 && Store.Current.completedDuels >= Rules.RequiredDuels(index);
        }
        internal string PackStatus(PackEntry pack)
        {
            int index = PackIndex(pack);
            if (index < 0) return "不可购买";
            int remaining = Rules.RequiredDuels(index) - Store.Current.completedDuels;
            return remaining > 0 ? "再完成 " + remaining + " 场挑战解锁" : Rules.packPrice + " DP / 包 · 每包 3 张";
        }
        internal List<int> Buy(PackEntry pack)
        {
            int index = PackIndex(pack);
            if (index < 0) throw new InvalidOperationException("卡包不在故事商店中。");
            var next = Store.Current.Copy();
            var cards = StoryProgress.Buy(next, Rules, index, Packs[index].Cards, random.Next);
            Store.Commit(next);
            return cards;
        }

        internal void OpenShop()
        {
            if (overlay != null) overlay.Close();
            shop = PackBrowserOverlay.OpenShop(this, () => { shop = null; Show(); });
        }

        internal void EditDeck(string character, int level = 0)
        {
            if (duel != null || editor != null || !(PluginGame.CurrentServant is MainMenu)) return;
            if (character != null && !StoryProgress.ValidLevel(level))
            { MessageManager.Cast("角色卡组难度必须为 1–10 级。"); return; }
            try
            {
                editor = new StoryDeckEditor(this, character, level);
                if (overlay != null) overlay.Close();
                editor.Open();
            }
            catch (Exception ex)
            {
                editor?.Restore(); editor = null;
                MessageManager.Cast(ex.Message); PluginLog.Error("story editor: " + ex); Show();
            }
        }

        internal void ReturnFromEditor()
        {
            editor = null;
            reopen = true; menuPending = true;
        }

        internal bool SaveDeck(string character, int level, StoryDeck deck)
        {
            if (character != null && !StoryProgress.ValidLevel(level))
            { MessageManager.Cast("角色卡组难度必须为 1–10 级。"); return false; }
            string invalid = StoryCatalog.Validate(deck, character == null ? Store.Current : null);
            if (invalid != null) { MessageManager.Cast(invalid); return false; }
            try
            {
                var next = Store.Current.Copy();
                if (character == null) next.player = deck.Copy(); else next.SetOpponent(character, level, deck);
                Store.Commit(next);
                Notice = character == null ? "玩家卡组已保存。" : CharacterSelector.characters.GetName(character)
                    + "的 " + level + "级卡组已保存。";
                return true;
            }
            catch (Exception ex) { MessageManager.Cast("保存失败：" + ex.Message); return false; }
        }

        internal void Challenge(string character, int level)
        {
            if (duel != null) return;
            if (!StoryProgress.ValidLevel(level)) { MessageManager.Cast("挑战难度必须为 1–10 级。"); return; }
            if (!Store.Current.TryGetOpponent(character, level, out var opponent))
            { MessageManager.Cast("请先编辑并保存这个角色的 " + level + "级卡组。"); return; }
            string invalid = StoryCatalog.Validate(Store.Current.player, Store.Current) ?? StoryCatalog.Validate(opponent);
            if (invalid != null) { MessageManager.Cast(invalid); return; }
            if (!StoryDuelObserver.Available) { MessageManager.Cast("当前本体无法读取决斗结算，故事挑战不可用。"); return; }
            if (!TcpHelper.canJoin || (TcpHelper.tcpClient != null && TcpHelper.tcpClient.Connected) || YgoServer.ServerRunning())
            { MessageManager.Cast("请先结束当前本地对战，再开始故事挑战。"); return; }
            try
            {
                int number = 7911;
                while (number < 8011 && !TcpHelper.IsPortAvailable(number)) number++;
                if (number >= 8011) throw new InvalidOperationException("找不到可用的本地对战端口。");
                string path = Path.Combine(Store.DirectoryPath, "opponent.ydk");
                File.WriteAllText(path, opponent.ToYdk());
                var name = CharacterSelector.characters.GetName(character);
                string dialogs = Path.Combine(Program.PATH_DATA, "Windbot/Dialogs");
                string dialog = Path.Combine(dialogs, Language.GetConfig() + ".json");
                if (!File.Exists(dialog)) dialog = Path.Combine(dialogs, "default.json");
                if (!File.Exists(dialog))
                {
                    var available = Directory.GetFiles(dialogs, "*.json");
                    Array.Sort(available, StringComparer.Ordinal);
                    if (available.Length == 0) throw new FileNotFoundException("本体缺少 AI 对话资源。");
                    dialog = available[0];
                }
                // WindBot receives a path argument directly; no shell or command-string parsing.
                string[] args = { "Name=" + name, "Deck=MDPro3Story", "DeckFile=" + path,
                    "Dialog=" + Path.GetFullPath(dialog), "Host=127.0.0.1", "Port=" + number, "Chat=false" };
                duel = new StoryDuelPackets();
                duelId = Guid.NewGuid().ToString("N"); opponentId = character; opponentLevel = level; port = number.ToString();
                connection = null; deckSent = startSent = enteredDuel = settled = settlementErrorShown = false; error = null;
                launchTime = Time.unscaledTime; nextSaveAttempt = 0;
                savedCharacter = Config.Get("DuelCharacter1", "0001");
                savedName = Config.Get("DuelPlayerName1", "AI");
                savedReturn = Program.instance.ocgcore.returnServant;
                savedFromSolo = RoomServant.FromSolo; savedFromHost = RoomServant.FromLocalHost; savedFromHand = RoomServant.FromHandTest;
                Config.Set("DuelCharacter1", character); Config.Set("DuelPlayerName1", name);
                RoomServant.FromSolo = true; RoomServant.FromLocalHost = false;
                RoomServant.FromHandTest = false; RoomServant.SoloLockHand = false;
                SoloSelector.port = port;
                // Unlimited banlist; local validation enforces sizes, sections and max three copies.
                YgoServer.StartServer(port + " -1 5 0 F T F 8000 5 1 0 0");
                ownsServer = true;
                if (!TcpHelper.LinkStart("127.0.0.1", Config.Get("DuelPlayerName0", "Player"), port, "", true,
                    () => new System.Threading.Thread(() => WindBot.Program.Main(args)) { IsBackground = true }.Start()))
                    throw new InvalidOperationException("本地连接未能启动。");
                overlay.ShowConnecting(name, level);
            }
            catch (Exception ex) { CancelChallenge("挑战启动失败：" + ex.Message); }
        }

        internal bool IsChallengeConnection
        {
            get
            {
                if (duel == null || TcpHelper.tcpClient == null || TcpHelper.joinedAddress != "127.0.0.1"
                    || TcpHelper.joinedPort != port) return false;
                if (connection == null) connection = TcpHelper.tcpClient;
                return ReferenceEquals(connection, TcpHelper.tcpClient);
            }
        }

        internal void Observe(byte[] packet)
        {
            duel?.Observe(packet);
            if (duel != null && !duel.Started && packet.Length > 1 && packet[0] == 2)
                error = "本体拒绝了对战设置或卡组，请检查卡片是否支持当前规则。";
        }

        internal void CancelChallenge(string reason = "已取消挑战。")
        {
            Notice = reason;
            if (duel != null)
            {
                if (IsChallengeConnection) TcpHelper.CtosMessage_LeaveGame();
                EndSession();
            }
            else MessageManager.Cast(reason);
        }

        private void EndSession()
        {
            if (ownsServer && YgoServer.ServerRunning()) YgoServer.StopServer();
            ownsServer = false;
            RestoreAppearance();
            duel = null; connection = null; opponentLevel = 0;
            if (overlay != null) overlay.Close();
            reopen = true; menuPending = true;
            if (PluginGame.CurrentServant != Program.instance.menu) Program.instance.ShiftToServant(Program.instance.menu);
        }

        internal void RestoreAppearance()
        {
            if (savedCharacter == null) return;
            Config.Set("DuelCharacter1", savedCharacter); Config.Set("DuelPlayerName1", savedName);
            if (Program.instance != null) Program.instance.ocgcore.returnServant = savedReturn;
            RoomServant.FromSolo = savedFromSolo; RoomServant.FromLocalHost = savedFromHost; RoomServant.FromHandTest = savedFromHand;
            savedCharacter = savedName = null;
        }

        private static Deck ToGameDeck(StoryDeck deck) => new Deck
        { Main = new List<int>(deck.main), Extra = new List<int>(deck.extra), Side = new List<int>(deck.side) };
    }
}
