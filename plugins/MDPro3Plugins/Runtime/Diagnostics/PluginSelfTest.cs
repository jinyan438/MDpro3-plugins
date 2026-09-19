using MDPro3.Duel.YGOSharp;
using MDPro3.Plugins.Features.PackBrowser;
using MDPro3.Plugins.Features.ReleaseDateSort;
using MDPro3.Plugins.Features.RpsVisualFix;
using MDPro3.Servant;
using MDPro3.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace MDPro3.Plugins.Diagnostics
{
    /// <summary>
    /// Built in diagnostics of the plugin package. It is not a feature, it always runs when the
    /// player was started with "-mdpro3-plugin-selftest" (Run_MDPro3.bat --diagnose), checks the
    /// plugin plumbing (config, registry, events) and the release date sort with the real game
    /// data, then quits the game with exit code 0 (ok) or 1 (failed).
    ///
    /// A feature that is switched off in config.json is reported and its checks are skipped, the
    /// result is still PASS in that case (the plugin is meant to run without it then).
    /// </summary>
    public static class PluginSelfTest
    {
        public const string ResultMarker = "MDPro3Plugins self test:";
        private const string SelfTestArgument = "-mdpro3-plugin-selftest";
        private const string PopupAddress = "Popup/PopupSearchOrder.prefab";
        private const float InitializeTimeout = 120f;
        private const float LoadTimeout = 60f;
        private const float UiTimeout = 30f;

        private const int StepWaitForGame = 0;
        private const int StepCheckEnvironment = 1;
        private const int StepLoadPopup = 2;
        private const int StepWaitForPopup = 3;
        private const int StepOpenDeckEditor = 4;
        private const int StepWaitForDeckEditor = 5;
        private const int StepActivateSort = 6;
        private const int StepCheckSortOrder = 7;
        private const int StepCheckOrderAfterPrint = 8;
        private const int StepCheckRowInDeckEditor = 9;
        private const int StepCheckPackData = 10;
        private const int StepOpenPackBrowser = 11;
        private const int StepWaitForPackBrowser = 12;
        private const int StepCheckPackBrowser = 13;
        private const int StepReport = 14;
        private const int StepQuit = 15;

        private static bool started;
        private static bool requested;
        private static bool finished;
        private static int step;
        private static int stepFrames;
        private static float timer;
        private static readonly List<string> failures = new List<string>();
        private static readonly List<string> notes = new List<string>();
        private static AsyncOperationHandle<GameObject> popupHandle;
        private static GameObject popupInstance;
        private static MDPro3.UI.Popup.Popup previousPopup;
        private static ReleaseDateSortFeature feature;
        private static bool sortChecksSkipped;

        private static void Log(string message)
        {
            Debug.Log("[MDPro3PluginsSelfTest] " + message);
        }

        public static void Tick()
        {
            if (finished)
                return;

            if (!started)
            {
                started = true;
                requested = HasArgument(SelfTestArgument);
                timer = 0f;
                if (!requested)
                {
                    finished = true;
                    return;
                }
                Log("requested by command line");
            }

            timer += Time.unscaledDeltaTime;

            switch (step)
            {
                case StepWaitForGame:
                    if (!GameReady())
                    {
                        if (timer > InitializeTimeout)
                            Fail("game data was not initialized within " + InitializeTimeout + "s");
                        return;
                    }
                    GoTo(StepCheckEnvironment);
                    return;

                case StepCheckEnvironment:
                    CheckEnvironment();
                    if (sortChecksSkipped)
                    {
                        GoTo(StepReport);
                        return;
                    }
                    CheckCardData();
                    GoTo(StepLoadPopup);
                    return;

                case StepLoadPopup:
                    StartPopupLoad();
                    GoTo(StepWaitForPopup);
                    return;

                case StepWaitForPopup:
                    if (!popupHandle.IsValid())
                    {
                        Fail("the sort popup could not be loaded (" + PopupAddress + ")");
                        GoTo(StepReport);
                        return;
                    }

                    if (!popupHandle.IsDone)
                    {
                        if (timer > LoadTimeout)
                            Fail("loading the sort popup timed out");
                        return;
                    }

                    popupInstance = popupHandle.Result;
                    CheckPopupIntegration(popupInstance);
                    GoTo(StepOpenDeckEditor);
                    return;

                case StepOpenDeckEditor:
                    ReleasePopup();
                    StartDeckEditorCheck();
                    GoTo(StepWaitForDeckEditor);
                    return;

                case StepWaitForDeckEditor:
                    if (!TickDeckEditorOpen())
                        return;
                    GoTo(StepActivateSort);
                    return;

                case StepActivateSort:
                    if (!TickSortActivation())
                        return;
                    GoTo(StepCheckSortOrder);
                    return;

                case StepCheckSortOrder:
                    if (!TickSortAfterActivation())
                        return;
                    GoTo(StepCheckOrderAfterPrint);
                    return;

                case StepCheckOrderAfterPrint:
                    if (!TickSortAfterGamePrint())
                        return;
                    GoTo(StepCheckRowInDeckEditor);
                    return;

                case StepCheckRowInDeckEditor:
                    if (!TickSortRowInDeckEditor())
                        return;
                    GoTo(StepCheckPackData);
                    return;

                case StepCheckPackData:
                    CheckPackData();
                    GoTo(StepOpenPackBrowser);
                    return;

                case StepOpenPackBrowser:
                    StartPackBrowserCheck();
                    GoTo(StepWaitForPackBrowser);
                    return;

                case StepWaitForPackBrowser:
                    if (!TickPackBrowserOpen())
                        return;
                    GoTo(StepCheckPackBrowser);
                    return;

                case StepCheckPackBrowser:
                    CheckPackBrowserContent();
                    GoTo(StepReport);
                    return;

                case StepReport:
                    Report();
                    GoTo(StepQuit);
                    return;

                default:
                    if (timer > 1f)
                    {
                        finished = true;
                        Application.Quit(failures.Count == 0 ? 0 : 1);
                    }
                    return;
            }
        }

        private static void GoTo(int nextStep)
        {
            step = nextStep;
            stepFrames = 0;
            timer = 0f;
        }

        private static bool GameReady()
        {
            if (Program.instance == null)
                return false;
            if (CardsManager._cards == null || CardsManager._cards.Count == 0)
                return false;
            if (PacksManager.packs == null || PacksManager.packs.Count == 0)
                return false;
            return true;
        }

        #region Environment (plugin plumbing)

        private static void CheckEnvironment()
        {
            notes.Add("plugin version: " + PluginInfo.Version);
            notes.Add("config: " + (PluginConfig.Found
                ? PluginConfig.LoadedPath
                : PluginInfo.ConfigFolderName + "\\" + PluginInfo.ConfigFileName + " not found, defaults are used"));

            CheckWindBotSerializationRuntime();
            CheckRpsResultRuntime();

            foreach (var entry in PluginRegistry.Features)
                notes.Add("feature " + entry.Id + " = "
                    + PluginConfig.DescribeFeature(entry.Id)
                    + (PluginRegistry.IsRunning(entry.Id) ? ", running" : ", not running"));

            feature = PluginRegistry.Get<ReleaseDateSortFeature>(ReleaseDateSortFeature.FeatureId);
            if (feature == null || !PluginRegistry.IsRunning(ReleaseDateSortFeature.FeatureId))
            {
                notes.Add("the release date sort feature is not running (see config.json), its checks are skipped");
                sortChecksSkipped = true;
                return;
            }

            notes.Add("release date sort is running, checks follow");
        }

        private static void CheckWindBotSerializationRuntime()
        {
            const string assemblyName = "System.Runtime.Serialization";
            const string groupName = "System.Runtime.Serialization.Configuration.SerializationSectionGroup";
            const string sectionName = "System.Runtime.Serialization.Configuration.DataContractSerializerSection";

            var groupType = Type.GetType(groupName + ", " + assemblyName, false);
            var sectionType = Type.GetType(sectionName + ", " + assemblyName, false);
            if (groupType == null)
            {
                Fail("WindBot cannot start: Unity stripped " + groupName);
                return;
            }
            if (sectionType == null)
            {
                Fail("WindBot cannot start: Unity stripped " + sectionName);
                return;
            }
            if (groupType.GetConstructor(Type.EmptyTypes) == null
                || sectionType.GetConstructor(Type.EmptyTypes) == null)
            {
                Fail("WindBot cannot start: Unity stripped a data contract configuration constructor");
                return;
            }

            notes.Add("WindBot serialization runtime: available");
        }

        private static void CheckRpsResultRuntime()
        {
            var resultPacket = new byte[] { 0x05, 2, 1 };
            if (!RpsResultPacketObserver.TryDecode(resultPacket, out int myHand, out int opponentHand)
                || myHand != 2 || opponentHand != 1)
            {
                Fail("the rock-paper-scissors result packet decoder rejected a valid packet");
            }
            if (RpsResultPacketObserver.TryDecode(new byte[] { 0x04, 2, 1 }, out _, out _)
                || RpsResultPacketObserver.TryDecode(new byte[] { 0x05, 2 }, out _, out _)
                || RpsResultPacketObserver.TryDecode(new byte[] { 0x05, 0, 1 }, out _, out _))
            {
                Fail("the rock-paper-scissors result packet decoder accepted an invalid packet");
            }

            if (RpsResultOverlay.GetOutcome(1, 3) != RpsResultOverlay.Outcome.Win
                || RpsResultOverlay.GetOutcome(2, 1) != RpsResultOverlay.Outcome.Win
                || RpsResultOverlay.GetOutcome(3, 2) != RpsResultOverlay.Outcome.Win
                || RpsResultOverlay.GetOutcome(1, 2) != RpsResultOverlay.Outcome.Lose
                || RpsResultOverlay.GetOutcome(2, 3) != RpsResultOverlay.Outcome.Lose
                || RpsResultOverlay.GetOutcome(3, 1) != RpsResultOverlay.Outcome.Lose
                || RpsResultOverlay.GetOutcome(1, 1) != RpsResultOverlay.Outcome.Draw)
            {
                Fail("the rock-paper-scissors win/loss mapping is incorrect");
            }

            if (PluginRegistry.IsRunning(RpsVisualFixFeature.FeatureId))
            {
                var observer = Program.instance.GetComponent<RpsResultPacketObserver>();
                if (observer == null)
                    Fail("the rock-paper-scissors result packet observer is not attached");
                else
                    notes.Add("rock-paper-scissors result packet decoder and observer: available");
            }
            else
            {
                notes.Add("the rock-paper-scissors visual feature is not running; live observer check skipped");
            }
        }

        #endregion

        #region Release date sort checks

        private static void CheckCardData()
        {
            if (sortChecksSkipped || feature == null)
                return;

            notes.Add("cards in database: " + CardsManager._cards.Count);
            notes.Add("packs: " + PacksManager.packs.Count);

            int dated = 0;
            int unknown = 0;
            int prerelease = 0;
            long newest = 0L;
            long oldest = long.MaxValue;
            int newestCode = 0;
            int oldestCode = 0;

            foreach (var pair in CardsManager._cards)
            {
                if (CardReleaseDate.IsPrerelease(pair.Value))
                {
                    prerelease++;
                    continue;
                }

                long key = CardReleaseDate.GetKey(pair.Key);
                if (!CardReleaseDate.HasDate(key))
                {
                    unknown++;
                    continue;
                }

                dated++;
                if (key > newest)
                {
                    newest = key;
                    newestCode = pair.Key;
                }
                if (key < oldest)
                {
                    oldest = key;
                    oldestCode = pair.Key;
                }
            }

            notes.Add("cards with release date: " + dated + " (without: " + unknown + ")");
            notes.Add("prerelease cards sorted ahead of dated cards: " + prerelease);
            if (dated > 0)
            {
                notes.Add("oldest: " + oldestCode + " " + CardReleaseDate.FormatKey(oldest));
                notes.Add("newest: " + newestCode + " " + CardReleaseDate.FormatKey(newest));
            }

            if (dated == 0)
                Fail("no card has release date data, Data/pack/pack.db is not loaded");

            // Build a list with three different dates plus one card without date data and check
            // both sort directions.
            int oldCode = oldestCode;
            int newCode = newestCode;
            int middleCode = FindCodeWithKeyBetween(oldest, newest);
            if (middleCode == 0)
                notes.Add("no third date available for the order check");

            int unknownCode = FindCodeWithoutDate();
            if (unknownCode == 0)
                notes.Add("no card without release date found, unknown date handling not checked");

            int prereleaseCode = FindPrereleaseCode();
            if (prereleaseCode == 0)
                notes.Add("no prerelease card found, prerelease date handling not checked");

            var input = new List<int> { middleCode, unknownCode, oldCode, prereleaseCode, newCode };
            var newestFirst = feature.BuildOrder(input, ReleaseDateSortFeature.Direction.NewestFirst);
            var oldestFirst = feature.BuildOrder(input, ReleaseDateSortFeature.Direction.OldestFirst);

            notes.Add("newest first: " + Describe(newestFirst));
            notes.Add("oldest first: " + Describe(oldestFirst));

            int expectedNewest = prereleaseCode != 0 ? prereleaseCode : newCode;
            if (newestFirst.Count == 0 || newestFirst[0] != expectedNewest)
                Fail("prerelease/newest card is not the first entry of the descending sort");

            if (oldestFirst.Count == 0 || oldestFirst[0] != oldCode)
                Fail("oldest card is not the first entry of the ascending sort");

            if (unknownCode != 0 && (newestFirst[newestFirst.Count - 1] != unknownCode
                || oldestFirst[oldestFirst.Count - 1] != unknownCode))
                Fail("a card without release date is not put at the end");

            if (prereleaseCode != 0)
            {
                if (newestFirst.IndexOf(prereleaseCode) > newestFirst.IndexOf(newCode))
                    Fail("a prerelease card is not ahead of dated cards in the descending sort");
                if (oldestFirst.IndexOf(prereleaseCode) < oldestFirst.IndexOf(newCode)
                    || (unknownCode != 0 && oldestFirst.IndexOf(prereleaseCode) > oldestFirst.IndexOf(unknownCode)))
                    Fail("a prerelease card is not between dated and unknown cards in the ascending sort");
            }

            if (newestFirst.Count != input.Count || oldestFirst.Count != input.Count)
                Fail("the sort dropped entries");

            if (middleCode != 0 && newestFirst.IndexOf(middleCode) > newestFirst.IndexOf(oldCode))
                Fail("the descending sort is not ordered by date");
        }

        private static int FindCodeWithKeyBetween(long oldest, long newest)
        {
            if (newest - oldest < 1000)
                return 0;

            long target = oldest + (newest - oldest) / 2;
            int bestCode = 0;
            long bestDistance = long.MaxValue;

            foreach (var pair in CardsManager._cards)
            {
                long key = CardReleaseDate.GetKey(pair.Key);
                if (!CardReleaseDate.HasDate(key) || key == oldest || key == newest)
                    continue;

                long distance = Math.Abs(key - target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestCode = pair.Key;
                }
            }

            return bestCode;
        }

        private static int FindCodeWithoutDate()
        {
            foreach (var pair in CardsManager._cards)
                if (!CardReleaseDate.IsPrerelease(pair.Value)
                    && !CardReleaseDate.HasDate(CardReleaseDate.GetKey(pair.Value)))
                    return pair.Key;

            return 0;
        }

        private static int FindPrereleaseCode()
        {
            foreach (var pair in CardsManager._cards)
                if (CardReleaseDate.IsPrerelease(pair.Value))
                    return pair.Key;

            return 0;
        }

        private static string Describe(List<int> codes)
        {
            var sb = new StringBuilder();
            foreach (int code in codes)
            {
                sb.Append(code);
                sb.Append('(');
                sb.Append(CardReleaseDate.IsPrerelease(code)
                    ? "prerelease"
                    : CardReleaseDate.FormatKey(CardReleaseDate.GetKey(code)));
                sb.Append(") ");
            }
            return sb.ToString().TrimEnd();
        }

        private static void StartPopupLoad()
        {
            try
            {
                popupHandle = Addressables.InstantiateAsync(PopupAddress);
            }
            catch (Exception e)
            {
                Fail("instantiating the sort popup failed: " + e.Message);
            }
        }

        private static void CheckPopupIntegration(GameObject instance)
        {
            if (instance == null)
            {
                Fail("the sort popup instance is null");
                return;
            }

            var popup = instance.GetComponent<MDPro3.UI.Popup.PopupSearchOrder>();
            if (popup == null)
            {
                Fail("the loaded prefab is not a sort popup");
                return;
            }

            // The popup is instantiated directly here, Popup.Show() is not called, so register it
            // the same way Show() does. Without that the game rows hit a null reference in their
            // Start() when they select themselves (`currentPopupB` would be null).
            var ui = PluginGame.UI;
            if (ui != null)
            {
                previousPopup = ui.currentPopupB;
                ui.currentPopupB = popup;
            }

            bool injected = SearchOrderRowInjector.Inject(feature, popup);
            var ascending = FindRow(popup, SearchOrderRowInjector.AscRowName);
            var descending = FindRow(popup, SearchOrderRowInjector.DescRowName);
            var label = instance.transform.Find(
                "Content/" + SearchOrderRowInjector.GridElementLabel + "/" + SearchOrderRowInjector.TextObjectName);

            notes.Add("sort popup row injected: " + injected);
            notes.Add("ascending row: " + (ascending != null) + ", descending row: " + (descending != null));
            notes.Add("text label: " + (label != null ? label.name : "missing"));

            if (!injected)
                Fail("the sort popup row could not be injected");

            if (ascending == null || descending == null)
            {
                Fail("the release date sort rows are missing");
                return;
            }

            if (ascending.Direction != ReleaseDateSortFeature.Direction.OldestFirst
                || descending.Direction != ReleaseDateSortFeature.Direction.NewestFirst)
                Fail("the release date sort rows have the wrong direction");

            if (ascending.GetIconSprite() == null || descending.GetIconSprite() == null)
                Fail("the release date sort rows have no icon");

            if (ascending.GetIconSprite() == descending.GetIconSprite())
                Fail("both release date rows show the same direction icon");

            if (ascending.GetComponent<SelectionToggle_SearchOrder>() != null
                || descending.GetComponent<SelectionToggle_SearchOrder>() != null)
                Fail("a release date row still runs the game sort behaviour");

            // A second injection must be refused, otherwise the row would be added twice.
            if (SearchOrderRowInjector.Inject(feature, popup))
                Fail("the row was injected twice");
        }

        private static ReleaseDateSortToggle FindRow(MDPro3.UI.Popup.PopupSearchOrder popup, string name)
        {
            var manager = popup.GetComponent<YgomSystem.ElementSystem.ElementObjectManager>();
            var grid = manager != null ? manager.GetElement(SearchOrderRowInjector.GridElementLabel) : null;
            if (grid == null)
                return null;

            var row = grid.transform.Find(name);
            return row != null ? row.GetComponent<ReleaseDateSortToggle>() : null;
        }

        private static void ReleasePopup()
        {
            if (popupInstance != null)
            {
                UnityEngine.Object.Destroy(popupInstance);
                popupInstance = null;
            }

            if (popupHandle.IsValid())
            {
                Addressables.ReleaseInstance(popupHandle);
                popupHandle = default;
            }

            var ui = PluginGame.UI;
            if (ui != null)
            {
                ui.currentPopupB = previousPopup;
                previousPopup = null;
            }
        }

        private static void StartDeckEditorCheck()
        {
            notes.Add("deck editor check: opening the deck editor to verify the real usage flow");
            try
            {
                var editor = Program.instance.deckEditor;
                if (editor == null)
                {
                    notes.Add("deck editor servant is not available, UI check skipped");
                    sortChecksSkipped = true;
                    return;
                }

                editor.SwitchCondition(DeckEditor.Condition.EditDeck);
                editor.Show(0);
            }
            catch (Exception e)
            {
                notes.Add("opening the deck editor failed, UI check skipped: " + e.Message);
                sortChecksSkipped = true;
            }
        }

        private static bool TickDeckEditorOpen()
        {
            if (sortChecksSkipped)
                return true;

            if (PluginGame.CardCollectionView == null)
            {
                if (timer > UiTimeout)
                {
                    notes.Add("the deck editor UI did not show up within " + UiTimeout + "s, UI check skipped");
                    sortChecksSkipped = true;
                    return true;
                }
                return false;
            }

            notes.Add("deck editor UI loaded, the card collection view is available");
            return true;
        }

        private static bool TickSortActivation()
        {
            if (sortChecksSkipped)
                return true;

            var view = PluginGame.CardCollectionView;
            if (view == null)
            {
                notes.Add("the card collection view disappeared, sort check skipped");
                sortChecksSkipped = true;
                return true;
            }

            if (stepFrames == 0)
            {
                view.PrintSearchCards();
                notes.Add("the game printed " + (view.printedCards == null ? 0 : view.printedCards.Count)
                    + " cards with its own sort");
                feature.Activate(ReleaseDateSortFeature.Direction.NewestFirst, null);
            }

            stepFrames++;
            return stepFrames >= 4;
        }

        private static bool TickSortAfterActivation()
        {
            if (sortChecksSkipped)
                return true;

            var view = PluginGame.CardCollectionView;
            if (view == null)
            {
                Fail("the card collection view disappeared while the release date sort was active");
                return true;
            }

            if (stepFrames == 0)
            {
                if (CardCollectionView._SortOrder != ReleaseDateSortFeature.InvalidSortOrder)
                    Fail("the plugin sort order sentinel is not applied while the plugin sort is active");

                string problem = DescribeOrderProblem(view.printedCards);
                if (problem != null)
                    Fail("the card collection was not ordered by release date: " + problem);
                else
                    notes.Add("card collection ordered by release date (" + view.printedCards.Count + " cards)");

                // Print again with the game sort, the plugin has to re-order the new list.
                view.PrintSearchCards();
            }

            stepFrames++;
            return stepFrames >= 4;
        }

        private static bool TickSortAfterGamePrint()
        {
            if (sortChecksSkipped)
                return true;

            var view = PluginGame.CardCollectionView;
            if (view == null)
            {
                Fail("the card collection view disappeared while checking the reprint");
                return true;
            }

            if (stepFrames == 0)
            {
                string problem = DescribeOrderProblem(view.printedCards);
                if (problem != null)
                    Fail("a new game print was not re-ordered by release date: " + problem);
                else
                    notes.Add("the release date order is kept after a new game print");

                // the first frame work is done, the frames below wait for the popup
                stepFrames = 1;
                view.ShowSortOrder();
            }

            var popup = PluginGame.CurrentPopup as MDPro3.UI.Popup.PopupSearchOrder;
            if (popup == null)
            {
                if (timer > UiTimeout)
                {
                    notes.Add("the sort popup did not open within " + UiTimeout + "s, popup check skipped");
                    sortChecksSkipped = true;
                    return true;
                }
                return false;
            }

            stepFrames++;
            return stepFrames >= 6;
        }

        private static bool TickSortRowInDeckEditor()
        {
            if (sortChecksSkipped)
                return true;

            if (stepFrames == 0)
            {
                var popup = PluginGame.CurrentPopup as MDPro3.UI.Popup.PopupSearchOrder;
                if (popup == null)
                {
                    notes.Add("the sort popup is closed again, row check skipped");
                    sortChecksSkipped = true;
                    return true;
                }

                var row = SearchOrderRowInjector.FindRow(popup);
                if (row == null)
                {
                    Fail("the release date row was not injected into the sort popup of the deck editor");
                }
                else
                {
                    notes.Add("the release date row is part of the deck editor sort popup");
                    if (row.Direction != ReleaseDateSortFeature.Direction.NewestFirst)
                        Fail("the active release date row has the wrong direction");
                    if (!row.isOn)
                        Fail("the release date row is not marked as the active sort while the plugin sort is active");

                    foreach (var toggle in popup.GetComponentsInChildren<SelectionToggle>(true))
                    {
                        if (toggle == null || toggle == row || !toggle.isOn)
                            continue;
                        Fail("the game sort row '" + toggle.name + "' is still marked as active next to the release date row");
                        break;
                    }
                }

                try
                {
                    if (popup != null)
                        popup.Hide();
                }
                catch (Exception)
                {
                    // the popup can already be gone
                }

                feature.Deactivate();
            }

            stepFrames++;
            return stepFrames >= 4;
        }

        /// <summary>Returns null when the list is ordered newest first, otherwise a description.</summary>
        private static string DescribeOrderProblem(List<int> codes)
        {
            if (codes == null || codes.Count == 0)
                return "the printed card list is empty";

            bool datedSeen = false;
            bool unknownSeen = false;
            long previousKey = long.MaxValue;
            for (int i = 0; i < codes.Count; i++)
            {
                if (CardReleaseDate.IsPrerelease(codes[i]))
                {
                    if (datedSeen || unknownSeen)
                        return "prerelease card " + codes[i]
                            + " is placed after a dated or unknown card (index " + i + ")";
                    continue;
                }

                long key = CardReleaseDate.GetKey(codes[i]);
                if (!CardReleaseDate.HasDate(key))
                {
                    unknownSeen = true;
                    continue;
                }

                if (unknownSeen)
                    return "card " + codes[i] + " with date " + CardReleaseDate.FormatKey(key)
                        + " is placed after a card without date (index " + i + ")";

                datedSeen = true;

                if (key > previousKey)
                    return "card " + codes[i] + " with date " + CardReleaseDate.FormatKey(key)
                        + " is placed after " + CardReleaseDate.FormatKey(previousKey) + " (index " + i + ")";

                previousKey = key;
            }

            return null;
        }

        #endregion

        #region Pack browser checks

        private static PackBrowserFeature packFeature;
        private static bool packChecksSkipped;
        private static bool wallCaptured;
        private static PackEntry shotPack;

        private static void CheckPackData()
        {
            packFeature = PluginRegistry.Get<PackBrowserFeature>(PackBrowserFeature.FeatureId);
            if (packFeature == null || !PluginRegistry.IsRunning(PackBrowserFeature.FeatureId))
            {
                notes.Add("the pack browser feature is not running (see config.json), its checks are skipped");
                packChecksSkipped = true;
                return;
            }

            notes.Add("generated pack cover table entries: " + PackCoverTable.Count);

            var catalog = PackCatalog.All;
            notes.Add("packs from the game data: " + catalog.Count
                + " (PacksManager: " + PacksManager.packs.Count + ")");

            if (PackCoverTable.Count == 0)
                Fail("the generated pack cover table is empty");

            if (catalog.Count != PacksManager.packs.Count + 1)
                Fail("the pack catalog does not contain exactly one virtual pack in addition to PacksManager");
            if (catalog.Count == 0 || !catalog[0].IsPrerelease)
                Fail("the prerelease pack is not the first entry in All packs");

            var kinds = new int[7];
            int withoutCover = 0;
            int coverOutsidePack = 0;
            int withCards = 0;
            int prereleasePacks = 0;
            int expectedPrereleaseCards = 0;
            PackEntry sample = null;

            foreach (var pair in CardsManager._cards)
                if (pair.Value.isPre)
                    expectedPrereleaseCards++;

            foreach (var entry in catalog)
            {
                if (entry.IsPrerelease)
                {
                    prereleasePacks++;
                    if (entry.Category != PackCategory.All)
                        Fail("the prerelease pack is visible outside the All category");
                    if (entry.Count != expectedPrereleaseCards)
                        Fail("the prerelease pack has " + entry.Count + " cards instead of " + expectedPrereleaseCards);
                    foreach (int code in entry.Cards)
                    {
                        var card = CardsManager.GetCardRaw(code);
                        if (card == null || !card.isPre)
                        {
                            Fail("the prerelease pack contains a non-prerelease card: " + code);
                            break;
                        }
                    }
                    if (entry.Count == 0 && entry.CoverCard != 0)
                        Fail("the empty prerelease pack has a cover card");
                    if (entry.Count > 0)
                    {
                        if (entry.CoverCard != entry.Cards[0])
                            Fail("the prerelease cover is not the first card");
                        int coverRank = PackCatalog.PrereleaseRarityRank(entry.CoverCard);
                        foreach (int code in entry.Cards)
                            if (PackCatalog.PrereleaseRarityRank(code) > coverRank)
                            {
                                Fail("the prerelease cover does not have the highest available rarity");
                                break;
                            }
                    }
                }

                if (entry.Count > 0)
                    withCards++;

                if (entry.CoverCard == 0)
                {
                    if (!entry.IsPrerelease || entry.Count > 0)
                        withoutCover++;
                    continue;
                }

                if (entry.Count > 0 && !entry.Cards.Contains(entry.CoverCard))
                    coverOutsidePack++;

                if (entry.CoverKind > 0 && entry.CoverKind < kinds.Length)
                    kinds[entry.CoverKind]++;

                if (sample == null && entry.Count > 5)
                    sample = entry;
            }

            notes.Add("packs with card data: " + withCards + ", without: " + (catalog.Count - withCards));
            notes.Add("packs without a cover card: " + withoutCover);
            notes.Add("MC prerelease cards: " + expectedPrereleaseCards);

            if (prereleasePacks != 1)
                Fail("the catalog contains " + prereleasePacks + " prerelease packs instead of one");

            for (int kind = 1; kind < kinds.Length; kind++)
                if (kinds[kind] > 0)
                    notes.Add("cover from " + PackCoverTable.DescribeKind(kind) + ": " + kinds[kind]);

            if (withoutCover > 0)
                Fail(withoutCover + " packs have no cover card");

            if (coverOutsidePack > 0)
                Fail(coverOutsidePack + " cover cards are not part of their own pack");

            if (sample != null)
                notes.Add("sample pack: " + sample.Code + " " + sample.Name + " -> " + sample.Count
                    + " cards, cover " + sample.CoverCard + " (" + sample.CoverSourceText + ")");
        }

        private static void StartPackBrowserCheck()
        {
            if (packChecksSkipped || packFeature == null)
                return;

            // the browser lives on the main menu, so go back there first
            var menu = Program.instance.menu;
            if (menu == null)
            {
                notes.Add("the main menu servant is not available, pack browser check skipped");
                packChecksSkipped = true;
                return;
            }

            Program.instance.ShiftToServant(menu);
        }

        private static bool TickPackBrowserOpen()
        {
            if (packChecksSkipped || packFeature == null)
                return true;

            if (!(PluginGame.CurrentServant is MainMenu))
            {
                if (timer > UiTimeout)
                {
                    notes.Add("the main menu did not come back within " + UiTimeout + "s, pack browser check skipped");
                    packChecksSkipped = true;
                }

                return false;
            }

            if (packFeature.Overlay == null)
            {
                packFeature.ShowBrowser();
                if (packFeature.Overlay == null)
                {
                    notes.Add("the pack browser could not be opened, UI check skipped");
                    packChecksSkipped = true;
                    return true;
                }
            }

            if (packFeature.Overlay.IsReady)
            {
                // give the picture loaders a few frames so the checks below see real textures
                stepFrames++;

                if (stepFrames < 40)
                    return false;

                if (stepFrames == 40 && !wallCaptured)
                {
                    wallCaptured = true;
                    string path = ScreenshotPath("pack-wall.png");
                    ScreenCapture.CaptureScreenshot(path);
                    notes.Add("pack wall screenshot: " + path);
                }

                // open a pack so the card picture slot is exercised and can be captured too
                if (stepFrames == 45)
                {
                    shotPack = FindPackWithCards();
                    if (shotPack != null)
                        packFeature.Overlay.ShowCards(shotPack);
                }

                if (stepFrames == 90)
                {
                    notes.Add("pack card slot: " + packFeature.Overlay.DescribeFirstTile());
                    string path = ScreenshotPath("pack-cards.png");
                    ScreenCapture.CaptureScreenshot(path);
                    notes.Add("pack cards screenshot: " + path);
                }

                if (stepFrames == 95)
                {
                    if (shotPack != null)
                        packFeature.Overlay.Back();

                    return true;
                }

                return false;
            }

            if (timer > UiTimeout)
            {
                notes.Add("the pack browser grid did not become ready within " + UiTimeout + "s, UI check skipped");
                packChecksSkipped = true;
            }

            return false;
        }

        private static PackEntry FindPackWithCards()
        {
            foreach (var entry in PackCatalog.All)
                if (entry.Count > 3)
                    return entry;

            return null;
        }

        /// <summary>Writes the screenshot into the plugin state folder next to this repository.</summary>
        private static string ScreenshotPath(string fileName)
        {
            try
            {
                var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
                for (int level = 0; level < 6 && directory != null; level++)
                {
                    string candidate = Path.Combine(directory.FullName, "plugins", ".state");
                    if (Directory.Exists(candidate))
                        return Path.Combine(candidate, fileName);

                    directory = directory.Parent;
                }
            }
            catch (Exception)
            {
                // fall back to the working directory below
            }

            return Path.Combine(Directory.GetCurrentDirectory(), fileName);
        }

        private static void CheckPackBrowserContent()
        {
            if (packChecksSkipped || packFeature == null)
                return;

            var overlay = packFeature.Overlay;
            if (overlay == null)
            {
                Fail("the pack browser closed unexpectedly");
                return;
            }

            var catalog = PackCatalog.All;
            notes.Add("pack browser ready, list entries: " + overlay.TileCount
                + ", live tiles: " + overlay.LiveTileCount + ", packs: " + overlay.PackCount
                + ", viewport: " + overlay.ViewportSize);
            notes.Add("cover art slot: " + overlay.DescribeFirstTile());

            if (!overlay.IsReady)
                Fail("the pack browser grid was not built");

            if (!overlay.BackButtonReady)
                Fail("the pack browser's game-style back button is not ready");

            if (overlay.TileCount != catalog.Count)
                Fail("the pack wall lists " + overlay.TileCount + " entries instead of " + catalog.Count);

            if (overlay.LiveTileCount <= 0 || overlay.LiveTileCount > overlay.TileCount)
                Fail("the pack wall has " + overlay.LiveTileCount + " live tiles for " + overlay.TileCount + " entries");

            CheckPackCategories(overlay, catalog);

            PackEntry first = null;
            foreach (var entry in catalog)
            {
                if (entry.Count > 3)
                {
                    first = entry;
                    break;
                }
            }

            if (first == null)
            {
                notes.Add("no pack with cards found, opening a pack is not checked");
            }
            else
            {
                overlay.ShowCards(first);
                notes.Add("opened the pack " + first.Code + " with " + overlay.TileCount
                    + " listed cards (pack has " + first.Count + ")");

                if (overlay.CardCount != first.Count)
                    Fail("the opened pack reports " + overlay.CardCount + " cards instead of " + first.Count);

                if (overlay.TileCount != first.Count)
                    Fail("the opened pack lists " + overlay.TileCount + " cards instead of " + first.Count);

                overlay.Back();
                if (overlay.CardCount != catalog.Count)
                    Fail("Esc did not return to the pack wall");
                else
                    notes.Add("Esc returned to the pack wall");
            }

            // the browser must also open from the main menu entry the way a player uses it, and the
            // cloned button must not run the game action of the entry it was copied from
            packFeature.CloseBrowser();
            CheckMainMenuEntry();

            packFeature.CloseBrowser();
            notes.Add("pack browser closed again, open browsers: " + (packFeature.Overlay != null));
        }

        private static void CheckPackCategories(PackBrowserOverlay overlay, List<PackEntry> catalog)
        {
            int total = 0;
            for (int i = 1; i < PackCategories.Count; i++)
            {
                var category = (PackCategory)i;
                int expected = 0;
                PackEntry sample = null;
                foreach (var entry in catalog)
                {
                    if (entry.Category != category)
                        continue;
                    expected++;
                    if (sample == null && entry.Count > 0)
                        sample = entry;
                }

                total += expected;
                overlay.SelectCategory(category);
                if (overlay.PackCount != expected || overlay.TileCount != expected)
                    Fail("pack category " + category + " has an incorrect filtered count");
                if (sample != null)
                {
                    overlay.ShowCards(sample);
                    if (overlay.CardCount != sample.Count || overlay.TileCount != sample.Count)
                        Fail("pack category " + category + " did not open its card list");
                    overlay.Back();
                    if (overlay.SelectedCategory != category || overlay.TileCount != expected)
                        Fail("returning from cards lost the pack category " + category);
                }
                notes.Add("pack category " + category + ": " + expected);
            }

            int allOnly = 0;
            foreach (var entry in catalog)
                if (entry.Category == PackCategory.All)
                    allOnly++;
            if (allOnly != 1 || total + allOnly != catalog.Count)
                Fail("product categories plus the All-only prerelease pack do not cover the catalog exactly once");
            overlay.SelectCategory(PackCategory.All);
            if (overlay.PackCount != catalog.Count || overlay.TileCount != catalog.Count)
                Fail("selecting all packs did not restore the catalog");
        }

        private static void CheckMainMenuEntry()
        {
            var menu = PluginGame.CurrentServant as MainMenu;
            var entry = MainMenuPackEntry.FindInjected(menu);
            if (entry == null)
            {
                Fail("the main menu has no card pack entry");
                return;
            }

            string expectedLabel = PackBrowserLabels.MenuEntry;
            string[] labelNames = { "Text", "TextOver", "TextShadow" };
            foreach (string labelName in labelNames)
            {
                var label = MainMenuPackEntry.FindText(entry, labelName);
                if (label == null)
                {
                    Fail("the card pack entry has no " + labelName + " label");
                }
                else if (!string.Equals(label.text, expectedLabel, StringComparison.Ordinal))
                {
                    Fail("the card pack entry " + labelName + " label is '" + label.text
                        + "' instead of '" + expectedLabel + "'");
                }
            }

            var normalLabel = MainMenuPackEntry.FindText(entry, "Text");
            notes.Add("main menu entry: " + entry.name + " -> "
                + (normalLabel != null ? normalLabel.text : "no label"));

            var button = entry.GetSelectable() as Button;
            if (button == null)
            {
                Fail("the card pack entry has no button");
                return;
            }

            int leftover = 0;
            for (int index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                if (button.onClick.GetPersistentListenerState(index) != UnityEventCallState.Off)
                    leftover++;

            if (leftover > 0)
                Fail("the card pack entry still runs " + leftover + " game menu action(s)");

            button.onClick.Invoke();

            if (packFeature.Overlay == null)
                Fail("clicking the card pack entry did not open the browser");
            else if (!(PluginGame.CurrentServant is MainMenu))
                Fail("clicking the card pack entry left the main menu");
            else
                notes.Add("clicking the card pack entry opened the browser, remaining menu actions: " + leftover);
        }

        #endregion

        #region Result

        private static void Report()
        {
            foreach (string note in notes)
                Log(note);

            if (failures.Count == 0)
            {
                Debug.Log("[MDPro3PluginsSelfTest] " + ResultMarker + " PASS");
                return;
            }

            foreach (string failure in failures)
                Debug.LogError("[MDPro3PluginsSelfTest] " + failure);

            Debug.LogError("[MDPro3PluginsSelfTest] " + ResultMarker + " FAIL (" + failures.Count + ")");
        }

        private static void Fail(string reason)
        {
            failures.Add(reason);
        }

        private static bool HasArgument(string name)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        #endregion
    }
}
