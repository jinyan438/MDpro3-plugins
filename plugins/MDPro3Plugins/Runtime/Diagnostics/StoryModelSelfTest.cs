using System;
using System.Linq;
using MDPro3.Plugins.Features.StoryMode;
using MDPro3.UI.Popup;
using UnityEngine;
using UnityEngine.UI;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Diagnostics
{
    internal static class StoryModelSelfTest
    {
        internal static bool Active { get; private set; }
        private static StoryModeFeature feature;
        private static StoryModelSession session;
        private static string character;
        private static int phase, beforeDuels, beforeDp;
        private static float next;
        private static Popup lastPopup;
        private static bool real;
        private static int idleWindows;
        private static bool checkedState, captured, capturedDecision, chatHidden;
        private static float reportAt;
        private static bool combo, comboTurnComplete, extraSummoned;
        private static string tracePath;
        internal static bool FixedOpening { get; private set; }
        private static bool interactionBoard;
        private static string failureReason;

        internal static void Begin(StoryModeFeature owner, string opponent)
        {
            Active = true; feature = owner; character = opponent;
            real = Environment.GetCommandLineArgs().Contains("-story-real-model");
            string testDeck = Environment.GetEnvironmentVariable("STORY_AI_TEST_DECK");
            combo = !string.IsNullOrEmpty(testDeck);
            tracePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(PluginConfig.LoadedPath), "model-decisions.jsonl");
            System.IO.File.WriteAllText(tracePath, "");
            if (combo)
            {
                var deck = new MDPro3.Duel.YGOSharp.Deck(testDeck);
                string hand = Environment.GetEnvironmentVariable("STORY_AI_TEST_HAND");
                if (!string.IsNullOrEmpty(hand))
                {
                    var opening = hand.Split(',').Select(int.Parse).ToList();
                    Check(opening.Count == 5, "fixed opening contains five cards");
                    foreach (int code in opening) Check(deck.Main.Remove(code), "opening card belongs to actual deck");
                    deck.Main.InsertRange(0, opening); FixedOpening = true;
                }
                Check(owner.SaveDeck(character, 7, new StoryDeck { main = deck.Main, extra = deck.Extra, side = deck.Side }), "combo deck configured");
            }
            Check(StoryModelHooks.Installed(), "model IL hook installed");
            beforeDuels = owner.Store.Current.completedDuels; beforeDp = owner.Store.Current.dp;
            owner.Challenge(character, 7); session = owner.ModelSession;
            Check(session != null, "session created"); phase = 0;
        }

        internal static void Observe(byte[] packet)
        {
            if (!Active || packet.Length < 2 || packet[0] != 1) return;
            // The human test seat passes. Opponent moves still use the live engine,
            // compiled WindBot hook and child Node service.
            if (packet[1] == 11)
            {
                idleWindows++;
                TcpHelper.CtosMessage_Response(BitConverter.GetBytes(7));
            }
            else if (packet[1] == 15 && packet.Length > 6)
            {
                int min = packet[4], count = packet[6];
                if (count > min && min > 0)
                    TcpHelper.CtosMessage_Response(new[] { (byte)min }.Concat(Enumerable.Range(0, min).Select(i => (byte)i)).ToArray());
            }
            else if (packet[1] == 16 && packet.Length >= 13)
            {
                int count = packet[3];
                if (count > 0 && packet.Length == 13 + count * 14
                    && Enumerable.Range(0, count).All(i => packet[14 + i * 14] == 0))
                    TcpHelper.CtosMessage_Response(BitConverter.GetBytes(-1));
            }
        }

        internal static bool Tick()
        {
            if (Time.unscaledTime >= reportAt)
            {
                reportAt = Time.unscaledTime + 15;
                Debug.Log("[StoryModelSelfTest] phase=" + phase + " model=" + session.ModelChoices + " automatic=" + session.AutomaticChoices
                    + " fallback=" + session.Fallbacks + " humanTurns=" + idleWindows + " status=" + session.Status);
            }
            if (Time.unscaledTime < next) return false;
            var popup = PluginGame.CurrentPopup;
            if (popup != null && popup != lastPopup)
            {
                if (popup is PopupRockPaperScissors)
                {
                    StoryModeSelfTest.PrepareTestHand();
                    var paper = popup.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "PaperButton");
                    if (paper != null) { lastPopup = popup; paper.onClick.Invoke(); }
                }
                else if (popup is PopupYesOrNo yes)
                { lastPopup = popup; yes.cancelAction?.Invoke(); yes.Hide(); }
                else if (popup.GetType().Name == "PopupConfirm") { lastPopup = popup; popup.Hide(); }
            }
            if (phase == 0)
            {
                if (session.Stopped) throw new Exception("Model service stopped: " + session.Status);
                if (combo && session.Fallbacks > 0) throw new Exception("Combo used fallback: " + failureReason);
                if (session.ProcessId > 0 && !checkedState) { CheckHiddenState(); checkedState = true; }
                if (session.ModelChoices < 4 || idleWindows < 1 || (!real && !combo && session.Fallbacks < 1)) return false;
                if (combo && !comboTurnComplete) return false;
                if (combo)
                {
                    Check(extraSummoned, "combo develops an extra-deck monster before ending its first turn");
                    Check(session.Fallbacks == 0, "combo completes with validated model choices, not local fallback");
                    if (FixedOpening) Check(interactionBoard, "fixed Agent opening leaves a live interaction monster");
                }
                if (!captured)
                {
                    Check(feature.ModelChat.MessageCount >= 4, "model summaries appear in native chat");
                    Check(feature.ModelChat.StatusText.Contains("请求 " + session.ModelCalls), "chat shows actual API attempts");
                    Check(GameObject.Find("StoryModelStatus") == null, "bottom model HUD removed");
                    Check(PluginGame.UI.chatPanel.showing, "chat opened for model duel");
                    Check(!PluginGame.UI.chatPanel.input.isFocused, "model chat does not focus input");
                    var labels = PluginGame.UI.chatPanel.GetComponentsInChildren<Text>(true)
                        .Where(t => t.transform.parent.name.StartsWith("StoryModelChat")).ToArray();
                    Check(labels.Length > 0 && labels.All(t => !t.supportRichText), "model text cannot inject UI markup");
                    captured = true;
                    var screenshot = new GameObject("StoryModelScreenshot").AddComponent<StoryScreenshot>();
                    screenshot.StartCoroutine(screenshot.Capture(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(PluginConfig.LoadedPath),
                        "screenshots/model-live.png")));
                    next = Time.unscaledTime + 1; return false;
                }
                if (!chatHidden) { PluginGame.UI.chatPanel.Hide(); chatHidden = true; next = Time.unscaledTime + 1; return false; }
                Check(!PluginGame.UI.chatPanel.showing, "new model messages respect closed chat");
                Debug.Log("[StoryModelSelfTest] model=" + session.ModelChoices + " automatic=" + session.AutomaticChoices
                    + " fallback=" + session.Fallbacks + " humanTurns=" + idleWindows);
                TcpHelper.CtosMessage_Surrender(); phase = 1; next = Time.unscaledTime + 3; return false;
            }
            if (phase == 1)
            {
                if (feature.Store.Current.completedDuels == beforeDuels) return false;
                Check(feature.Store.Current.completedDuels == beforeDuels + 1, "authoritative result settled exactly once");
                Check(feature.Store.Current.dp == beforeDp, "model cannot award DP on a loss");
                Program.instance.ocgcore.OnDuelResultConfirmed(true);
                phase = 2; next = Time.unscaledTime + 3; return false;
            }
            if (phase == 2)
            {
                if (feature.ModelSession != null) return false;
                Check(session.Stopped && ProcessGone(session.ProcessId), "normal exit cleans up service process");
                Check(feature.ModelChat.MessageCount == 0 && GameObject.Find("StoryModelChatStatus") == null, "exit removes model chat rows");
                feature.Show(); feature.Challenge(character, 7); session = feature.ModelSession;
                Check(session != null, "second challenge owns a new session");
                phase = 3; lastPopup = null; next = Time.unscaledTime + 1; return false;
            }
            if (phase == 3)
            {
                if (session.ProcessId == 0) return false;
                feature.CancelChallenge("Model test cancellation");
                phase = 4; next = Time.unscaledTime + 2; return false;
            }
            Check(session.Stopped && ProcessGone(session.ProcessId), "cancellation cleans up pending service");
            Check(feature.Store.Current.completedDuels == beforeDuels + 1, "cancel does not settle an unfinished duel");
            Active = false; return true;
        }

        private static bool ProcessGone(int pid)
        {
            if (pid <= 0) return false;
            try { using (var process = System.Diagnostics.Process.GetProcessById(pid)) return process.HasExited; }
            catch (ArgumentException) { return true; }
        }

        internal static void CaptureDecision(string root, byte[] packet, Newtonsoft.Json.Linq.JObject state, int hint,
            Newtonsoft.Json.Linq.JArray cards, Newtonsoft.Json.Linq.JArray descriptions)
        {
            if (!Active || capturedDecision || packet[0] != 11) return;
            capturedDecision = true;
            var fixture = new Newtonsoft.Json.Linq.JObject { ["packet"] = Convert.ToBase64String(packet),
                ["state"] = state.DeepClone(), ["hint"] = hint, ["cards"] = cards.DeepClone(), ["descriptions"] = descriptions.DeepClone() };
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "model-benchmark.json"), fixture.ToString());
        }

        internal static void RecordDecision(byte[] packet, Newtonsoft.Json.Linq.JObject state, int hint, Newtonsoft.Json.Linq.JObject result)
        {
            if (!Active || phase != 0) return;
            var record = new Newtonsoft.Json.Linq.JObject { ["packet"] = Convert.ToBase64String(packet),
                ["state"] = state.DeepClone(), ["hint"] = hint, ["result"] = result.DeepClone() };
            System.IO.File.AppendAllText(tracePath, record.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            if (!combo) return;
            if ((string)result["status"] != "ok") failureReason = (string)result["reason"];
            var monsters = state["players"]?[0]?["monsters"] as Newtonsoft.Json.Linq.JArray;
            if (monsters != null && monsters.Any(c => c.Type == Newtonsoft.Json.Linq.JTokenType.Object
                && (((int?)c["type"] ?? 0) & (0x40 | 0x2000 | 0x800000 | 0x4000000)) != 0)) extraSummoned = true;
            if ((string)result["status"] == "ok" && (int?)state["turnPlayer"] == 0 && packet[0] == 11)
            {
                byte[] response = Convert.FromBase64String((string)result["response"]);
                if (response.Length == 4 && BitConverter.ToInt32(response, 0) == 7)
                {
                    comboTurnComplete = true;
                    int[] interaction = { 4280258, 63101468, 84815190, 59509952, 88581108 };
                    interactionBoard = monsters != null && monsters.Any(c => c.Type == Newtonsoft.Json.Linq.JTokenType.Object
                        && interaction.Contains((int?)c["code"] ?? 0) && ((int?)c["disabled"] ?? 0) == 0
                        && ((int?)c["position"] & 5) != 0
                        && ((int?)c["code"] != 4280258 || (int?)c["attack"] >= 800)
                        && ((int?)c["code"] != 88581108 || c["overlays"].Count() > 0));
                    Debug.Log("[StoryModelSelfTest] combo end board=" + monsters?.ToString(Newtonsoft.Json.Formatting.None));
                }
            }
        }

        private static void CheckHiddenState()
        {
            var duel = new WindBot.Game.Duel(); duel.Fields[0].Init(40, 0); duel.Fields[1].Init(40, 0);
            duel.Fields[0].Hand.Add(new ClientCard(123, CardLocation.Hand, 0));
            duel.Fields[1].Hand.Add(new ClientCard(456, CardLocation.Hand, 0));
            duel.Fields[1].MonsterZone[3] = new ClientCard(789, CardLocation.MonsterZone, 3, 8);
            duel.Fields[1].Deck[0].SetId(999);
            duel.Fields[1].ExtraDeck.Add(new ClientCard(111, CardLocation.Extra, 0, 1));
            duel.Fields[1].ExtraDeck.Add(new ClientCard(222, CardLocation.Extra, 1, 8));
            var deck = new WindBot.Game.Deck();
            deck.Cards.Add(YGOSharp.OCGWrapper.NamedCard.Get(91188343));
            deck.Cards.Add(YGOSharp.OCGWrapper.NamedCard.Get(39552864));
            deck.Cards.Add(YGOSharp.OCGWrapper.NamedCard.Get(39552864));
            deck.ExtraCards.Add(YGOSharp.OCGWrapper.NamedCard.Get(4280258));
            var snapshot = StoryModelHooks.Snapshot(duel, deck);
            Check((int)snapshot["ownMainComposition"][0]["code"] == 39552864
                && (int)snapshot["ownMainComposition"][0]["count"] == 2
                && snapshot["ownMainComposition"].Count() == 2, "main composition is sorted and aggregated");
            Check((int)snapshot["ownExtraComposition"][0]["code"] == 4280258
                && snapshot["ownExtraComposition"].Count() == 1, "extra composition is separate from main deck");
            Check((int)snapshot["players"][0]["hand"][0]["code"] == 123, "own hand visible");
            Check(snapshot["players"][1]["hand"][0]["code"] == null, "opponent hand concealed even when cached");
            Check(snapshot["players"][1]["monsters"][3]["code"] == null, "opponent set identity concealed");
            Check(snapshot["players"][1]["monsters"].Count() == 7, "field holes retain sequence");
            Check(!snapshot.ToString().Contains("999"), "deck order never exported");
            Check((int)snapshot["players"][1]["extra"][0]["code"] == 111, "face-up opponent extra is public");
            Check(snapshot["players"][1]["extra"][1]["code"] == null, "face-down opponent extra stays hidden");
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
