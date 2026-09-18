using MDPro3.Servant;
using MDPro3.UI;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.PackBrowser
{
    /// <summary>
    /// Main menu: browse every card pack of Data/pack/pack.db as a wall of pack covers.
    ///
    /// The feature adds one entry to the main menu (a clone of an existing menu button, so the look
    /// is the game's own) and opens the browser on top of the menu. Nothing else of the game is
    /// touched: the browser registers itself as UIManager.InputBlocker while it is open, which is
    /// the same mechanism the game uses to stop a servant from responding to input.
    /// </summary>
    public sealed class PackBrowserFeature : PluginFeature
    {
        public const string FeatureId = "packBrowser";

        public override string Id => FeatureId;

        public override string DisplayName => "Card pack browser in the main menu";

        private MainMenu injectedMenu;
        private bool waitingForMenu;
        private PackBrowserOverlay overlay;

        /// <summary>The browser of this session, or null while it is closed.</summary>
        public PackBrowserOverlay Overlay => overlay;

        #region Feature life cycle

        public override void Enable()
        {
            PluginEvents.ServantChanged += OnServantChanged;
            waitingForMenu = true;
            Log("enabled");
        }

        public override void Disable()
        {
            PluginEvents.ServantChanged -= OnServantChanged;
            CloseBrowser();
            injectedMenu = null;
            waitingForMenu = false;
        }

        /// <summary>
        /// The menu entry can only be added once the main menu UI prefab is loaded, that happens a
        /// few frames after the servant changed, so the injection is retried until it worked.
        /// </summary>
        public override void Tick()
        {
            if (waitingForMenu)
                TryInjectMenuEntry();
        }

        #endregion

        #region Main menu entry

        private void OnServantChanged(Servant.Servant servant)
        {
            if (servant is MainMenu)
            {
                waitingForMenu = true;
                return;
            }

            // left the main menu, the browser cannot stay open
            injectedMenu = null;
            waitingForMenu = false;
            CloseBrowser();
        }

        private void TryInjectMenuEntry()
        {
            var menu = PluginGame.CurrentServant as MainMenu;
            if (menu == null || menu.servantUI == null)
                return;

            if (ReferenceEquals(menu, injectedMenu))
            {
                waitingForMenu = false;
                return;
            }

            if (!MainMenuPackEntry.Inject(menu, ToggleBrowser))
                return;

            injectedMenu = menu;
            waitingForMenu = false;
            Log("main menu entry added");
        }

        #endregion

        #region Browser

        /// <summary>Opens the browser, or closes it when it is already open.</summary>
        public void ToggleBrowser()
        {
            if (overlay != null)
            {
                CloseBrowser();
                return;
            }

            overlay = PackBrowserOverlay.Open();
            if (overlay == null)
                PluginLog.Error("pack browser: could not be opened");
        }

        /// <summary>Opens the browser when it is closed. Used by the self test.</summary>
        public void ShowBrowser()
        {
            if (overlay == null)
                overlay = PackBrowserOverlay.Open();
        }

        public void CloseBrowser()
        {
            if (overlay == null)
                return;

            overlay.Close();
            overlay = null;
        }

        #endregion

        #region Helpers for the self test

        public static bool IsAvailable
        {
            get
            {
                var feature = PluginRegistry.Get<PackBrowserFeature>(FeatureId);
                return feature != null && PluginRegistry.IsRunning(FeatureId);
            }
        }

        #endregion
    }

    /// <summary>
    /// Adds the "card packs" button to the main menu by cloning one of the game menu buttons.
    ///
    /// The clone keeps the plate, the hover animation, the cursor and the sound, and only the
    /// click action is replaced (SelectionButton.SetClickEvent does exactly that, it clears the
    /// persistent call of the cloned button first). The new button is inserted above the exit
    /// button and the explicit gamepad navigation of its neighbours is re-linked.
    /// </summary>
    internal static class MainMenuPackEntry
    {
        public const string ObjectName = "ButtonPacks";

        private const string LabelText = "Text";
        private const string LabelTextOver = "TextOver";
        private const string LabelTextShadow = "TextShadow";

        private static bool reportedTemplateProblem;

        public static bool Inject(MainMenu menu, Action onOpen)
        {
            if (menu == null)
                return false;

            var buttons = menu.GetComponentsInChildren<SelectionButton_MainMenu>(true);
            if (buttons == null || buttons.Length == 0)
            {
                Report("the main menu has no buttons");
                return false;
            }

            var template = FindTemplate(buttons);
            var parent = template != null ? template.transform.parent : null;
            if (template == null || parent == null)
            {
                Report("the main menu button layout is not the expected one");
                return false;
            }

            if (parent.Find(ObjectName) != null)
                return false;

            var clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            clone.name = ObjectName;

            var button = clone.GetComponent<SelectionButton_MainMenu>();
            if (button == null)
            {
                UnityEngine.Object.Destroy(clone);
                Report("the cloned menu button has no behaviour");
                return false;
            }

            SetLabel(button, PackBrowserLabels.MenuEntry);
            button.SetClickEvent(() => onOpen());
            DisableClonedActions(button.GetSelectable() as Button);

            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex());
            Relink(template, button);
            EnsureWidth(buttons, button);

            return true;
        }

        /// <summary>
        /// The clone keeps the persistent click of the menu entry it was copied from (in this
        /// layout the exit button). SelectionButton.SetClickEvent only clears the runtime
        /// listeners, so the copied persistent calls have to be switched off here, otherwise
        /// clicking the new entry would also run the action of the original one.
        /// </summary>
        private static void DisableClonedActions(Button button)
        {
            if (button == null)
                return;

            int count = button.onClick.GetPersistentEventCount();
            for (int index = 0; index < count; index++)
                button.onClick.SetPersistentListenerState(index, UnityEventCallState.Off);
        }

        /// <summary>The injected menu entry of a main menu, or null.</summary>
        public static SelectionButton_MainMenu FindInjected(MainMenu menu)
        {
            if (menu == null)
                return null;

            var buttons = menu.GetComponentsInChildren<SelectionButton_MainMenu>(true);
            foreach (var candidate in buttons)
                if (candidate != null && candidate.name == ObjectName)
                    return candidate;

            return null;
        }

        /// <summary>The exit button is always the last entry, the new one goes right above it.</summary>
        private static SelectionButton_MainMenu FindTemplate(SelectionButton_MainMenu[] buttons)
        {
            SelectionButton_MainMenu last = null;
            int bestIndex = -1;

            foreach (var candidate in buttons)
            {
                if (candidate == null)
                    continue;

                int index = candidate.transform.GetSiblingIndex();
                if (index >= bestIndex)
                {
                    bestIndex = index;
                    last = candidate;
                }
            }

            return last;
        }

        private static void SetLabel(SelectionButton button, string text)
        {
            SetText(button, LabelText, text);
            SetText(button, LabelTextOver, text);
            SetText(button, LabelTextShadow, text);
        }

        private static void SetText(SelectionButton button, string label, string text)
        {
            var element = button.GetElement<TextMeshProUGUI>(label);
            if (element != null)
                element.text = text;
        }

        /// <summary>
        /// The main menu uses explicit navigation targets, so the new entry has to be linked with
        /// the entry above it and with the exit button below it.
        /// </summary>
        private static void Relink(SelectionButton_MainMenu below, SelectionButton_MainMenu inserted)
        {
            var insertedSelectable = inserted.GetSelectable();
            var belowSelectable = below.GetSelectable();
            if (insertedSelectable == null || belowSelectable == null)
                return;

            var aboveSelectable = belowSelectable.navigation.selectOnUp;

            var insertedNavigation = insertedSelectable.navigation;
            insertedNavigation.mode = Navigation.Mode.Explicit;
            insertedNavigation.selectOnUp = aboveSelectable;
            insertedNavigation.selectOnDown = belowSelectable;
            insertedSelectable.navigation = insertedNavigation;

            var belowNavigation = belowSelectable.navigation;
            belowNavigation.mode = Navigation.Mode.Explicit;
            belowNavigation.selectOnUp = insertedSelectable;
            belowSelectable.navigation = belowNavigation;

            if (aboveSelectable == null)
                return;

            var aboveNavigation = aboveSelectable.navigation;
            aboveNavigation.mode = Navigation.Mode.Explicit;
            aboveNavigation.selectOnDown = insertedSelectable;
            aboveSelectable.navigation = aboveNavigation;
        }

        /// <summary>The game uses one width for every menu entry, keep the new one in that row.</summary>
        private static void EnsureWidth(SelectionButton_MainMenu[] buttons, SelectionButton_MainMenu inserted)
        {
            float width = inserted.GetPreferredWidth();

            foreach (var candidate in buttons)
            {
                if (candidate == null)
                    continue;

                float preferred = candidate.GetPreferredWidth();
                if (preferred > width)
                    width = preferred;
            }

            inserted.SetWidth(width);

            foreach (var candidate in buttons)
                if (candidate != null && candidate != inserted)
                    candidate.SetWidth(width);
        }

        private static void Report(string reason)
        {
            if (reportedTemplateProblem)
                return;

            reportedTemplateProblem = true;
            PluginLog.Warn("pack browser is unavailable: " + reason);
        }
    }
}
