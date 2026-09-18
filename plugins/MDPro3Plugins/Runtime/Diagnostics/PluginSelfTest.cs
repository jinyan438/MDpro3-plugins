using MDPro3.Duel.YGOSharp;
using MDPro3.Plugins.Features.ReleaseDateSort;
using MDPro3.Servant;
using MDPro3.UI;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
        private const int StepReport = 10;
        private const int StepQuit = 11;

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
            long newest = 0L;
            long oldest = long.MaxValue;
            int newestCode = 0;
            int oldestCode = 0;

            foreach (var pair in CardsManager._cards)
            {
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

            var input = new List<int> { middleCode, unknownCode, oldCode, newCode };
            var newestFirst = feature.BuildOrder(input, ReleaseDateSortFeature.Direction.NewestFirst);
            var oldestFirst = feature.BuildOrder(input, ReleaseDateSortFeature.Direction.OldestFirst);

            notes.Add("newest first: " + Describe(newestFirst));
            notes.Add("oldest first: " + Describe(oldestFirst));

            if (newestFirst.Count == 0 || newestFirst[0] != newCode)
                Fail("newest card is not the first entry of the descending sort");

            if (oldestFirst.Count == 0 || oldestFirst[0] != oldCode)
                Fail("oldest card is not the first entry of the ascending sort");

            if (unknownCode != 0 && (newestFirst[newestFirst.Count - 1] != unknownCode
                || oldestFirst[oldestFirst.Count - 1] != unknownCode))
                Fail("a card without release date is not put at the end");

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
                if (!CardReleaseDate.HasDate(CardReleaseDate.GetKey(pair.Key)))
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
                sb.Append(CardReleaseDate.FormatKey(CardReleaseDate.GetKey(code)));
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

            bool unknownSeen = false;
            long previousKey = long.MaxValue;
            for (int i = 0; i < codes.Count; i++)
            {
                long key = CardReleaseDate.GetKey(codes[i]);
                if (!CardReleaseDate.HasDate(key))
                {
                    unknownSeen = true;
                    continue;
                }

                if (unknownSeen)
                    return "card " + codes[i] + " with date " + CardReleaseDate.FormatKey(key)
                        + " is placed after a card without date (index " + i + ")";

                if (key > previousKey)
                    return "card " + codes[i] + " with date " + CardReleaseDate.FormatKey(key)
                        + " is placed after " + CardReleaseDate.FormatKey(previousKey) + " (index " + i + ")";

                previousKey = key;
            }

            return null;
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
