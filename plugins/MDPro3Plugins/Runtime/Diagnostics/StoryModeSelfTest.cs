using System;
using System.IO;
using System.Linq;
using System.Collections;
using MDPro3.Plugins.Features.PackBrowser;
using MDPro3.Plugins.Features.StoryMode;
using MDPro3.Servant;
using MDPro3.UI;
using MDPro3.UI.Popup;
using MDPro3.UI.ServantUI;
using MDPro3.Duel.YGOSharp;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using System.Reflection;

namespace MDPro3.Plugins.Diagnostics
{
    // Opt-in only. The staging tool gives this player its own config, profile and copied resources.
    internal static class StoryModeSelfTest
    {
        private static bool checkedArgs, active, done;
        private static int step;
        private static float next, deadline;
        private static StoryModeFeature feature;
        private static string output, character;
        private static int beforeCards, beforeDp, beforeDuels;
        private static Popup lastPopup;
        private static bool surrendered;
        private static string captured;
        private static WindBot.Game.GameClient testBot;
        private static bool testingWin;
        private static int unowned;
        private static string nativeFilesBefore, savedPlayer;

        internal static void AttachBot(WindBot.Game.GameClient bot)
        {
            if (active && !done) testBot = bot;
        }

        internal static void PrepareTestHand()
        {
            if (!active || done || testBot == null) return;
            // Avoid random repeated ties exhausting the room's opening timer.
            var behavior = typeof(WindBot.Game.GameClient).GetField("_behavior", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(testBot);
            if (behavior != null) typeof(WindBot.Game.GameBehavior).GetField("_hand", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(behavior, 2);
        }

        internal static void Tick()
        {
            if (!checkedArgs)
            {
                checkedArgs = true;
                active = Environment.GetCommandLineArgs().Contains("-mdpro3-story-selftest");
                deadline = Time.unscaledTime + 240f;
            }
            if (!active || done) return;
            try { Run(); }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        private static void Run()
        {
            if (Time.unscaledTime > deadline) throw new Exception("Timed out at step " + step);
            if (Time.unscaledTime < next) return;
            // The base client may show release notes after its asynchronous version check.
            // Dismiss that informational modal before inspecting the story canvas.
            if ((step < 8 || step == 35 || step == 36 || step == 51) && PluginGame.CurrentPopup != null)
            {
                PluginGame.CurrentPopup.Hide();
                next = Time.unscaledTime + .8f;
                return;
            }
            switch (step)
            {
                case 51:
                    if (StoryModelSettingsSelfTest.Tick()) Finish(true, "Story model settings: edit, fetch, select, save, masking, errors and cancellation passed.");
                    break;
                case 50:
                    if (StoryModelSelfTest.Tick()) Finish(true, "Story model: private state, live decisions, authoritative settlement, fallback and cancellation passed.");
                    break;
                case 0:
                    if (!(PluginGame.CurrentServant is MainMenu menu) || menu.servantUI == null || CharacterSelector.characters == null
                        || CardsManager._cards.Count == 0 || PacksManager.packs.Count == 0) return;
                    feature = PluginRegistry.Get<StoryModeFeature>(StoryModeFeature.FeatureId);
                    // Refuse to run destructive test setup against a normal installation/profile.
                    if (PluginConfig.LoadedPath == null || !PluginConfig.LoadedPath.Replace('\\', '/').Contains("/.selfcheck/story/player/"))
                        throw new Exception("Story self-test requires the isolated player from story-stage.ps1.");
                    output = Path.Combine(Path.GetDirectoryName(PluginConfig.LoadedPath), "screenshots");
                    Directory.CreateDirectory(output);
                    feature.Show();
                    Check(feature.Store != null, "story data initialized");
                    if (Environment.GetCommandLineArgs().Contains("-story-model-settings-only"))
                    { StoryModelSettingsSelfTest.Begin(); Advance(51, 1); break; }
                    if (Environment.GetCommandLineArgs().Contains("-story-foil-only")
                        || Environment.GetCommandLineArgs().Contains("-story-silver-only"))
                    { StoryRaritySelfTest.BeginFoilPreview(); Advance(35, 3); break; }
                    Check(feature.Store.Current.player.main.Count >= 40, "starter deck loaded");
                    Check(StoryCatalog.Validate(feature.Store.Current.player, feature.Store.Current) == null, "live starter cards legal");
                    Check(feature.Packs.Count > 5, "live chronological pack catalog");
                    for (int i = 1; i < feature.Packs.Count; i++)
                        Check(string.CompareOrdinal(feature.Packs[i - 1].DateText, feature.Packs[i].DateText) <= 0, "pack chronology");
                    character = CharacterSelector.characters.dm.First(c => !c.notReady).id;
                    Check(feature.SaveDeck(character, 1, StoryCatalog.Starter()), "level one opponent deck configured");
                    Check(feature.SaveDeck(character, 7, StoryCatalog.Starter()), "level seven opponent deck configured");
                    Check(!feature.Store.Current.TryGetOpponent(character, 2, out _), "unconfigured level remains absent");
                    feature.Challenge(character, 2);
                    Check(!feature.Launching, "unconfigured level cannot start a challenge");
                    var setup = feature.Store.Current.Copy(); setup.dp = 400; setup.completedDuels = 0; setup.wins = 0;
                    // Keep the undersized-deck regression reproducible when reusing an isolated player.
                    setup.player = StoryCatalog.Starter();
                    feature.Store.Commit(setup);
                    if (Environment.GetCommandLineArgs().Contains("-story-model-only"))
                    { StoryModelSelfTest.Begin(feature, character); Advance(50, 1); break; }
                    nativeFilesBefore = DeckFiles();
                    UnityEngine.Object.FindFirstObjectByType<StoryOverlay>().RefreshStats();
                    if (Environment.GetCommandLineArgs().Contains("-story-opening-only"))
                    { feature.OpenShop(); Advance(4, 3); break; }
                    Advance(1, 3); break;
                case 1:
                    for (int difficulty = StoryProgress.MinLevel; difficulty <= StoryProgress.MaxLevel; difficulty++)
                    {
                        string buttonName = "Difficulty" + difficulty;
                        Check(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                            .Any(b => b.gameObject.activeInHierarchy && b.name == buttonName), "difficulty button available: " + difficulty);
                    }
                    UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                        .First(b => b.gameObject.activeInHierarchy && b.name == "Difficulty7").onClick.Invoke();
                    Check(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                        .Any(b => b.gameObject.activeInHierarchy && b.name == "挑战 7级 · 胜利 +700 DP"), "configured level is selectable");
                    if (!Capture("01-characters")) return;
                    Click("我的卡组"); Advance(2, 3); break;
                case 35:
                    if (!StoryRaritySelfTest.FoilPreviewReady()) return;
                    StoryRaritySelfTest.CheckFoilPreview();
                    Time.timeScale = 0;
                    if (!Capture("12-foil-finish-a")) return;
                    Advance(36, 1.1f); break;
                case 36:
                    if (!Capture("12-foil-finish-b")) return;
                    Time.timeScale = 1;
                    Finish(true, "Full-face foil preview and paused-time animation captured."); break;
                case 2:
                    if (!EditorReady()) return;
                    if (!Capture("02-player-deck")) return;
                    CheckPlayerEditor();
                    StoryRaritySelfTest.Prepare(feature);
                    Advance(32, 3); break;
                case 32:
                    if (!EditorReady()) return;
                    Program.instance.deckEditor.GetUI<DeckEditorUI>().DeckView.PrintDeck(StoryDeckEditor.ToGame(feature.Store.Current.player), DeckEditor.DeckName, DeckView.Condition.Editable);
                    Advance(33, 3); break;
                case 33:
                    if (!EditorReady()) return;
                    StoryRaritySelfTest.CheckReload(feature);
                    savedPlayer = feature.Store.Current.player.ToYdk();
                    Advance(34, 1); break;
                case 34:
                    if (!Capture("02b-rarity-deck")) return;
                    Program.instance.deckEditor.GetUI<DeckEditorUI>().CardCollectionView.ShowFilters();
                    Advance(37, 1.5f); break;
                case 37:
                    if (!(PluginGame.CurrentPopup is PopupSearchFilter rarityFilter)) return;
                    StoryRaritySelfTest.CheckFilter(rarityFilter);
                    Advance(38, 1); break;
                case 38:
                    Program.instance.deckEditor.GetUI<DeckEditorUI>().CardCollectionView.ResetFilters();
                    if (Environment.GetCommandLineArgs().Contains("-story-visual-only"))
                    { Program.instance.deckEditor.OnReturn(); Advance(20, 2); }
                    else
                    {
                        beforeDp = feature.Store.Current.dp; beforeDuels = feature.Store.Current.completedDuels;
                        Program.instance.deckEditor.GetUI<DeckEditorUI>().OnHandTest(); Advance(25, 3);
                    }
                    break;
                case 25:
                    if (PluginGame.CurrentServant != Program.instance.ocgcore || OcgCore.duelEnded) return;
                    Check(StoryDeckEditor.Active != null, "native hand test retains story session");
                    Advance(31, 6); break;
                case 31:
                    // Use the same exit/surrender action as the native hand-test UI.
                    Program.instance.ocgcore.OnDuelResultConfirmed(true); Advance(26, 1); break;
                case 26:
                    if (PluginGame.CurrentPopup is MDPro3.UI.Popup.PopupYesOrNo surrender)
                    { surrender.decideAction.Invoke(); surrender.Hide(); }
                    Check(feature.Store.Current.dp == beforeDp && feature.Store.Current.completedDuels == beforeDuels, "native hand test grants no story rewards");
                    Advance(27, 3); break;
                case 27:
                    if (!EditorReady())
                    {
                        if (Time.unscaledTime - next > 15) throw new Exception("Hand test return: current=" + PluginGame.CurrentServant?.GetType().Name
                            + ", editor showing=" + Program.instance.deckEditor.showing + ", transition=" + Program.instance.deckEditor.inTransition
                            + ", ui=" + (Program.instance.deckEditor.servantUI != null) + ", hand=" + RoomServant.FromHandTest);
                        return;
                    }
                    Check(StoryDeckEditorHooks.UsesView(Program.instance.deckEditor.GetUI<DeckEditorUI>().DeckView), "hand test returns to same native story editor");
                    Check(Program.instance.deckEditor.GetUI<DeckEditorUI>().CardCollectionView.printedCards.All(feature.Store.Current.owned.ContainsKey), "hand test return retains owned pool");
                    var returnedPlayer = StoryDeckEditor.FromGame(Program.instance.deckEditor.GetUI<DeckEditorUI>().DeckView.FromObjectDeckToCodedDeck());
                    Check(returnedPlayer.Copies().Select(c => c.id + ":" + c.rarity).SequenceEqual(
                        feature.Store.Current.player.Copies().Select(c => c.id + ":" + c.rarity)), "hand test return retains per-copy rarities");
                    Program.instance.deckEditor.OnReturn(); Advance(20, 2); break;
                case 20:
                    Check(PluginGame.CurrentServant is MainMenu && StoryDeckEditor.Active == null, "native editor returns to story and restores context");
                    Check(DeckFiles() == nativeFilesBefore, "story save never writes ordinary deck files");
                    feature.EditDeck(character, 1); Advance(3, 3); break;
                case 3:
                    if (!EditorReady()) return;
                    if (!Capture("03-character-deck")) return;
                    StoryRaritySelfTest.CheckOpponent(Program.instance.deckEditor.GetUI<DeckEditorUI>(), unowned);
                    Advance(39, 1); break;
                case 39:
                    if (!Capture("03b-character-sr")) return;
                    var enemyEditor = Program.instance.deckEditor.GetUI<DeckEditorUI>();
                    Check(enemyEditor.CardCollectionView.printedCards.Contains(unowned), "opponent native collection includes unowned cards");
                    Check(enemyEditor.DeckView.AddCard(CardsManager.Get(unowned), false, false), "opponent may add full-pool card");
                    enemyEditor.OnSave();
                    Check(feature.Store.Current.TryGetOpponent(character, 1, out var savedLevelOne)
                        && savedLevelOne.All().Contains(unowned), "native save targets selected character level");
                    Check(savedLevelOne.Copies().Any(c => c.id == unowned && c.rarity == StoryRarity.SR), "opponent save records selected SR");
                    Check(feature.Store.Current.TryGetOpponent(character, 7, out var savedLevelSeven)
                        && !savedLevelSeven.All().Contains(unowned), "editing level one preserves level seven");
                    Check(feature.Store.Current.player.ToYdk() == savedPlayer, "opponent editing preserves player deck");
                    enemyEditor.DeckView.AddCard(CardsManager.Get(unowned), false, false);
                    Program.instance.deckEditor.OnReturn(); Advance(28, 1); break;
                case 28:
                    var confirm = PluginGame.CurrentPopup as MDPro3.UI.Popup.PopupYesOrNo;
                    Check(confirm != null, "native dirty editor offers save on return");
                    confirm.decideAction.Invoke(); confirm.Hide(); Advance(29, 2); break;
                case 29:
                    Check(feature.Store.Current.TryGetOpponent(character, 1, out var returnedLevelOne)
                        && returnedLevelOne.Count(unowned) == 2, "native save and return commits selected level");
                    feature.EditDeck(character, 1); Advance(30, 3); break;
                case 30:
                    if (!EditorReady()) return;
                    StoryRaritySelfTest.CheckOpponentReload(Program.instance.deckEditor.GetUI<DeckEditorUI>(), unowned);
                    Program.instance.deckEditor.GetUI<DeckEditorUI>().DeckView.PrintDeck(
                        StoryDeckEditor.ToGame(StoryDeckEditor.FromGame(Program.instance.deckEditor.GetUI<DeckEditorUI>().DeckView.FromObjectDeckToCodedDeck())),
                        DeckEditor.DeckName, DeckView.Condition.Editable);
                    Advance(40, 3); break;
                case 40:
                    if (!EditorReady()) return;
                    StoryRaritySelfTest.CheckOpponentMixedReload(Program.instance.deckEditor.GetUI<DeckEditorUI>(), unowned);
                    if (!Capture("03c-character-mixed-copies")) return;
                    Program.instance.deckEditor.GetUI<DeckEditorUI>().DeckView.AddCard(CardsManager.Get(unowned), false, false);
                    Program.instance.deckEditor.OnReturn(); Advance(21, 1); break;
                case 21:
                    var discard = PluginGame.CurrentPopup as MDPro3.UI.Popup.PopupYesOrNo;
                    Check(discard != null, "native dirty return asks before discarding");
                    discard.cancelAction.Invoke(); discard.Hide(); Advance(22, 2); break;
                case 22:
                    Check(feature.Store.Current.TryGetOpponent(character, 1, out var discardedLevelOne)
                        && discardedLevelOne.Count(unowned) == 2, "discard preserves last saved opponent level");
                    Check(feature.Store.Current.TryGetOpponent(character, 7, out var untouchedLevelSeven)
                        && !untouchedLevelSeven.All().Contains(unowned), "other opponent level remains unchanged");
                    // Ordinary native editor must regain its full card collection and original behavior.
                    UnityEngine.Object.FindFirstObjectByType<StoryOverlay>().Close();
                    DeckEditor.condition = DeckEditor.Condition.EditDeck;
                    DeckEditor.Deck = StoryDeckEditor.ToGame(StoryCatalog.Starter()); DeckEditor.DeckName = "Native read-only regression";
                    DeckEditor.DeckIsFromLocal = true; DeckEditor.historyCards = new System.Collections.Generic.List<int>();
                    Program.instance.deckEditor.returnServant = Program.instance.menu;
                    Program.instance.ShiftToServant(Program.instance.deckEditor); Advance(23, 3); break;
                case 23:
                    if (!EditorReady()) return;
                    Program.instance.deckEditor.GetUI<DeckEditorUI>().CardCollectionView.PrintSearchCards();
                    Check(Program.instance.deckEditor.GetUI<DeckEditorUI>().CardCollectionView.printedCards.Contains(unowned), "ordinary editor retains full pool");
                    Check(!StoryDeckEditorHooks.UsesView(Program.instance.deckEditor.GetUI<DeckEditorUI>().DeckView), "ordinary editor is outside story hooks");
                    Check(!StoryDeckEditorHooks.BlockFreeRarity(Program.instance.deckEditor.GetUI<DeckEditorUI>()), "ordinary editor retains rarity selection");
                    Check(Program.instance.deckEditor.GetUI<DeckEditorUI>().GetComponentInChildren<StoryOpponentRarity>(true) == null,
                        "opponent SR toggle does not leak into ordinary editor");
                    Program.instance.deckEditor.OnReturn(); Advance(24, 2); break;
                case 24:
                    Check(DeckFiles() == nativeFilesBefore, "all editor checks preserve ordinary deck files");
                    feature.Show(); feature.OpenShop(); Advance(4, 3); break;
                case 4:
                    if (!Capture("04-shop")) return;
                    var shop = UnityEngine.Object.FindFirstObjectByType<PackBrowserOverlay>();
                    Check(shop != null && shop.IsReady, "shop grid ready");
                    Check(feature.Unlocked(feature.Packs[0]) && !feature.Unlocked(feature.Packs[5]), "live locked packs");
                    beforeCards = feature.Store.Current.owned.Values.Sum(); beforeDp = feature.Store.Current.dp;
                    shop.ShowCards(feature.Packs[0]); Advance(5, 2); break;
                case 5:
                    var weights = feature.Rules.rarityWeights;
                    try { feature.Rules.rarityWeights = new[] { 0, 0, 1, 0, 0, 0 }; Click("购买并拆包"); }
                    finally { feature.Rules.rarityWeights = weights; }
                    Check(feature.Store.Current.owned.Values.Sum() == beforeCards + 3, "purchase awards three cards");
                    Check(feature.Store.Current.dp == beforeDp - feature.Rules.packPrice, "purchase debits DP");
                    Advance(6, 3); break;
                case 6:
                    if (!StoryOpeningSelfTest.Tick(feature, output)) return;
                    Advance(7, 1); break;
                case 7:
                    if (Environment.GetCommandLineArgs().Contains("-story-visual-only")
                        || Environment.GetCommandLineArgs().Contains("-story-opening-only"))
                    { Finish(true, "UI, collection and shop visual checks passed (duels skipped)."); return; }
                    beforeDuels = feature.Store.Current.completedDuels; beforeDp = feature.Store.Current.dp;
                    feature.Challenge(character, testingWin ? 7 : 1); Advance(8, 1); break;
                case 8:
                    if (PluginGame.CurrentServant == Program.instance.room)
                        Check(!Program.instance.ui_.chatPanel.showing, "story room does not auto-open chat panel");
                    var popup = PluginGame.CurrentPopup;
                    if (popup != null && popup != lastPopup)
                    {
                        if (popup is MDPro3.UI.Popup.PopupRockPaperScissors)
                        {
                            PrepareTestHand();
                            var paper = popup.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "PaperButton");
                            if (paper != null) { lastPopup = popup; paper.onClick.Invoke(); }
                        }
                        else if (popup is MDPro3.UI.Popup.PopupYesOrNo yes)
                        { lastPopup = popup; yes.decideAction?.Invoke(); yes.Hide(); }
                    }
                    if (PluginGame.CurrentServant == Program.instance.ocgcore && OcgCore.duelEnded == false)
                    {
                        Advance(9, 6);
                        Debug.Log("[StorySelfTest] live duel started");
                    }
                    break;
                case 9:
                    Check(!Program.instance.ui_.chatPanel.showing, "story duel keeps chat panel closed");
                    if (!Capture(testingWin ? "09-second-duel" : "06-duel")) return;
                    if (!surrendered)
                    {
                        if (testingWin)
                        {
                            Check(testBot?.Connection != null, "test opponent connected");
                            testBot.Connection.Send(YGOSharp.Network.Enums.CtosMessage.Surrender);
                        }
                        else TcpHelper.CtosMessage_Surrender();
                        surrendered = true;
                    }
                    Advance(10, 4); break;
                case 10:
                    if (feature.Store.Current.completedDuels == beforeDuels) return;
                    Check(feature.Store.Current.completedDuels == beforeDuels + 1, "real challenge increments duel count exactly once");
                    Check(feature.Store.Current.dp == beforeDp + (testingWin ? feature.Rules.WinReward(7) : 0),
                        "level seven victory grants scaled DP");
                    if (!Capture(testingWin ? "10-victory" : "07-result")) return;
                    Program.instance.ocgcore.OnDuelResultConfirmed(true);
                    Advance(11, 4); break;
                case 11:
                    Check(PluginGame.CurrentServant is MainMenu, "returns to main menu");
                    Check(UnityEngine.Object.FindFirstObjectByType<StoryOverlay>() != null, "story mode reopened");
                    if (!Capture(testingWin ? "11-final-return" : "08-return")) return;
                    Advance(12, 1); break;
                case 12:
                    if (!testingWin)
                    {
                        testingWin = true; surrendered = false; testBot = null; lastPopup = null;
                        Advance(7, 1);
                    }
                    else Finish(true, "UI, optional difficulty decks, scaled DP, closed story chat panel, collection, purchase, live AI challenges and return passed.");
                    break;
            }
        }
        private static bool EditorReady() => PluginGame.CurrentServant == Program.instance.deckEditor
            && Program.instance.deckEditor.GetUI<DeckEditorUI>()?.DeckView.deckLoaded == true;
        private static string DeckFiles() => string.Join("|", Directory.GetFiles(Program.PATH_DECK, "*", SearchOption.AllDirectories)
            .OrderBy(p => p).Select(p => p + ":" + new FileInfo(p).Length + ":" + File.GetLastWriteTimeUtc(p).Ticks));
        private static void CheckPlayerEditor()
        {
            var ui = Program.instance.deckEditor.GetUI<DeckEditorUI>();
            var view = ui.DeckView; var collection = ui.CardCollectionView;
            Check(StoryDeckEditorHooks.Installed() && StoryDeckEditorHooks.UsesView(view), "real native editor hooks active");
            Check(collection.printedCards.Count > 0 && collection.printedCards.All(feature.Store.Current.owned.ContainsKey), "player native collection contains owned cards only");
            unowned = CardsManager._cards.Values.First(c => c.Alias == 0 && c.year > 0 && StoryCatalog.Playable(c.Id)
                && !c.IsExtraCard() && !feature.Store.Current.owned.ContainsKey(c.Id)).Id;
            int count = view.cards.Count;
            Check(!view.AddCard(CardsManager.Get(unowned), false, false) && view.cards.Count == count, "native add rejects unowned card");
            var full = view.cards.First(c => view.cards.Count(v => v.Card.Id == c.Card.Id) == feature.Store.Current.owned[c.Card.Id]);
            Check(!view.CanAddCard(full.Card.Id, false), "native drag and +1 check owned quantity");
            Check(!view.CanAddCardToLocation(full.Card, DeckView.DeckLocation.SideDeck, false), "native side-deck drag shares owned quantity");
            collection.InputSearch.InputField.text = unowned.ToString(); collection.PrintSearchCards();
            Check(collection.printedCards.Count == 0, "native ID search cannot expose unowned cards");
            collection.historyCards.Add(unowned); collection.PrintHistoryCards();
            Check(!collection.printedCards.Contains(unowned), "native history respects collection");
            collection.PrintBookmarkCards(); Check(collection.printedCards.All(feature.Store.Current.owned.ContainsKey), "native bookmark respects collection");
            collection.ShowRelatedCard(CardsManager.Get(unowned)); Check(collection.printedCards.All(feature.Store.Current.owned.ContainsKey), "related cards respect collection");
            collection.HideRelatedCard(); collection.InputSearch.InputField.text = ""; collection.PrintSearchCards();
            var imported = StoryDeckEditor.ToGame(feature.Store.Current.player); imported.Main.Add(unowned);
            Check(!view.ImportCardLists(imported) && view.cards.Count == count, "native YDKE import cannot bypass collection");
            savedPlayer = feature.Store.Current.player.ToYdk();
            var removed = view.cards[0]; var data = removed.Card;
            Check(view.RemoveCard(removed, false, false, false), "native remove works");
            ui.OnSave(); Check(view.GetDirty() && feature.Store.Current.player.ToYdk() == savedPlayer, "invalid native save preserves story profile");
            Check(view.AddCard(data, false, false), "native add restores owned card");
            ui.Manager.GetNestedElement<SelectionButton>("Header/ButtonSave").GetComponent<Button>().onClick.Invoke();
            Check(!view.GetDirty(), "native save button commits valid story deck");
            savedPlayer = feature.Store.Current.player.ToYdk();
            Check(DeckFiles() == nativeFilesBefore, "native story save is isolated");
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Click(string name)
        {
            var target = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.gameObject.activeInHierarchy && b.name == name && b.interactable);
            Check(target != null, "button available: " + name); target.onClick.Invoke();
        }
        private static bool Capture(string name)
        {
            if (captured == name) return true;
            captured = name;
            var host = new GameObject("StoryScreenshot").AddComponent<StoryScreenshot>();
            host.StartCoroutine(host.Capture(Path.Combine(output, name + ".png")));
            next = Time.unscaledTime + .6f;
            return false;
        }
        private static void Advance(int value, float delay) { step = value; next = Time.unscaledTime + delay; Debug.Log("[StorySelfTest] step " + step); }
        private static void Finish(bool pass, string message)
        {
            done = true;
            string line = "StoryMode runtime test: " + (pass ? "PASS " : "FAIL ") + message;
            Debug.Log(line);
            if (output != null) File.WriteAllText(Path.Combine(output, "result.txt"), line);
            Application.Quit(pass ? 0 : 1);
        }
    }

    internal sealed class StoryScreenshot : MonoBehaviour
    {
        internal IEnumerator Capture(string path)
        {
            // Render the real base-camera stack to our own target. Hidden Windows players may
            // have no readable system framebuffer, but camera render targets remain available.
            var cameras = Program.instance.camera_;
            var camera = cameras.cameraMain.gameObject.activeInHierarchy ? cameras.cameraMain : cameras.camera2D;
            var oldTarget = camera.targetTexture;
            var target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            yield return null;
            yield return null;
            camera.Render();
            // Camera.Render renders a single camera, not its URP overlay stack. Draw the UI
            // overlay explicitly into the same target for screenshots of the actual canvas.
            var uiCamera = cameras.cameraUI;
            var uiData = uiCamera.GetComponent<UniversalAdditionalCameraData>();
            var typeField = typeof(UniversalAdditionalCameraData).GetField("m_CameraType", BindingFlags.Instance | BindingFlags.NonPublic);
            var oldType = typeField.GetValue(uiData);
            var oldUiTarget = uiCamera.targetTexture;
            var oldFlags = uiCamera.clearFlags;
            try
            {
                typeField.SetValue(uiData, CameraRenderType.Base);
                uiCamera.targetTexture = target;
                uiCamera.clearFlags = CameraClearFlags.Depth;
                uiCamera.Render();
            }
            finally
            {
                typeField.SetValue(uiData, oldType);
                uiCamera.targetTexture = oldUiTarget;
                uiCamera.clearFlags = oldFlags;
            }
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = oldTarget;
            if (texture != null) { File.WriteAllBytes(path, texture.EncodeToPNG()); Destroy(texture); }
            target.Release(); Destroy(target);
            Destroy(gameObject);
        }
    }
}
