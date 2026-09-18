using MDPro3.Servant;
using MDPro3.UI;
using MDPro3.UI.Popup;
using MDPro3.UI.ServantUI;
using System;
using GameServant = MDPro3.Servant.Servant;

namespace MDPro3.Plugins
{
    /// <summary>
    /// Null safe access to the few game singletons the plugin uses. Everything here is a cheap
    /// field read, so it can be called every frame.
    /// </summary>
    public static class PluginGame
    {
        public static bool IsReady => Program.instance != null;

        public static Program ProgramInstance => Program.instance;

        public static UIManager UI => Program.instance != null ? Program.instance.ui_ : null;

        public static GameServant CurrentServant => Program.instance != null ? Program.instance.currentServant : null;

        public static DeckEditor DeckEditor => Program.instance != null ? Program.instance.deckEditor : null;

        public static bool DeckEditorShown
        {
            get
            {
                var editor = DeckEditor;
                return editor != null && editor.showing;
            }
        }

        /// <summary>The popup of the new UI system that is currently open, or null.</summary>
        public static Popup CurrentPopup => UI != null ? UI.currentPopupB : null;

        /// <summary>The deck editor UI, or null while it is not loaded yet.</summary>
        public static DeckEditorUI DeckEditorUI
        {
            get
            {
                var editor = DeckEditor;
                if (editor == null || !editor.showing)
                    return null;

                try
                {
                    return editor.GetUI<DeckEditorUI>();
                }
                catch (Exception)
                {
                    // GetUI returns the loaded UI, it can be null while Addressables is still loading.
                    return null;
                }
            }
        }

        /// <summary>The card collection of the deck editor, or null when it is not shown.</summary>
        public static CardCollectionView CardCollectionView
        {
            get
            {
                var ui = DeckEditorUI;
                if (ui == null)
                    return null;

                var view = ui.CardCollectionView;
                if (view == null || !view.gameObject.activeInHierarchy)
                    return null;

                return view;
            }
        }
    }
}
