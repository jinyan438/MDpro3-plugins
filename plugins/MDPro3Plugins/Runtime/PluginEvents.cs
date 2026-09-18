using MDPro3.UI;
using MDPro3.UI.Popup;
using System;
using System.Collections.Generic;
using UnityEngine;
using GameServant = MDPro3.Servant.Servant;

namespace MDPro3.Plugins
{
    /// <summary>
    /// Central event hub of the plugin package.
    ///
    /// The host (PluginHost) watches a few game singletons once per frame with cheap reference
    /// comparisons (see PluginHost.WatchEnvironment) and dispatches an event only when something
    /// really changed. Features subscribe in Enable() and unsubscribe in Disable(), so nothing
    /// has to poll the game state per frame.
    ///
    /// Adding a new signal is two lines: a new event here plus a raise call in the host.
    /// </summary>
    public static class PluginEvents
    {
        /// <summary>true when the deck editor was opened, false when it was closed.</summary>
        public static event Action<bool> DeckEditorVisibilityChanged;

        /// <summary>The active popup of the new UI system changed (null when none is open).</summary>
        public static event Action<Popup> PopupChanged;

        /// <summary>The servant shown in the main area changed.</summary>
        public static event Action<GameServant> ServantChanged;

        /// <summary>
        /// The card collection of the deck editor changed: a new view was created, its printed
        /// card list changed, or the deck editor was closed (argument is null then).
        /// </summary>
        public static event Action<CardCollectionView> CardCollectionChanged;

        private static readonly HashSet<string> reportedHandlerErrors = new HashSet<string>();

        /// <summary>
        /// Drops every subscription. Called once when the plugin starts, because static fields can
        /// survive a play mode session when the editor keeps the domain loaded.
        /// </summary>
        internal static void Clear()
        {
            DeckEditorVisibilityChanged = null;
            PopupChanged = null;
            ServantChanged = null;
            CardCollectionChanged = null;
            reportedHandlerErrors.Clear();
        }

        internal static void RaiseDeckEditorVisibilityChanged(bool showing)
        {
            Invoke(DeckEditorVisibilityChanged, showing, "DeckEditorVisibilityChanged");
        }

        internal static void RaisePopupChanged(Popup popup)
        {
            Invoke(PopupChanged, popup, "PopupChanged");
        }

        internal static void RaiseServantChanged(GameServant servant)
        {
            Invoke(ServantChanged, servant, "ServantChanged");
        }

        internal static void RaiseCardCollectionChanged(CardCollectionView view)
        {
            Invoke(CardCollectionChanged, view, "CardCollectionChanged");
        }

        /// <summary>
        /// Calls every handler on its own so one broken feature cannot stop the others.
        /// </summary>
        private static void Invoke<T>(Action<T> handlers, T argument, string eventName)
        {
            if (handlers == null)
                return;

            foreach (Delegate candidate in handlers.GetInvocationList())
            {
                if (candidate is not Action<T> handler)
                    continue;

                try
                {
                    handler(argument);
                }
                catch (Exception e)
                {
                    string key = eventName + "|" + handler.Method.DeclaringType?.FullName + "." + handler.Method.Name;
                    if (reportedHandlerErrors.Add(key))
                        PluginLog.Error(eventName + " handler " + handler.Method.DeclaringType?.Name + "." + handler.Method.Name + " failed: " + e);
                }
            }
        }
    }
}
