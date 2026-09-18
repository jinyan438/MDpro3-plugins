using MDPro3.UI;
using MDPro3.UI.Popup;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MDPro3.Plugins.Features.ReleaseDateSort
{
    /// <summary>
    /// Deck editor: sort the card collection by card release date.
    ///
    /// The game sorts the collection in CardCollectionView.SortCards() with
    /// CardsManager.GetSort(CardCollectionView._SortOrder). The plugin must not change game
    /// sources, so it works like this:
    ///   * it appends its own row to the sort popup (SearchOrderRowInjector),
    ///   * while its sort is chosen it keeps CardCollectionView._SortOrder on an unused value
    ///     (so no game row claims to be the active sort, and picking a game row is detectable),
    ///   * whenever the game prints a card list (search, tab switch, bookmark, history, ...) the
    ///     plugin re-orders that list by release date and prints it again.
    ///
    /// Everything is event driven: PluginHost watches the deck editor and the printed card list,
    /// this feature only reacts on PluginEvents and needs no per frame polling of the game.
    /// </summary>
    public sealed class ReleaseDateSortFeature : PluginFeature
    {
        public const string FeatureId = "releaseDateSort";

        /// <summary>Sort order value that no game sort entry uses.</summary>
        public static readonly CardCollectionView.SortOrder InvalidSortOrder = (CardCollectionView.SortOrder)(-1);

        public enum Direction
        {
            /// <summary>Newest card first.</summary>
            NewestFirst = 0,
            /// <summary>Oldest card first.</summary>
            OldestFirst = 1
        }

        public override string Id => FeatureId;

        public override string DisplayName => "Release date sort for the deck editor";

        /// <summary>true while the plugin sort is the chosen one.</summary>
        public bool IsActive { get; private set; }

        public Direction ActiveDirection { get; private set; }

        private PopupSearchOrder openPopup;
        private int syncPopupUntilFrame = -1;
        private CardCollectionView trackedView;
        private List<int> appliedOrder;
        private Sprite activeIcon;

        #region Feature life cycle

        public override void Enable()
        {
            PluginEvents.PopupChanged += OnPopupChanged;
            PluginEvents.CardCollectionChanged += OnCardCollectionChanged;
            PluginEvents.DeckEditorVisibilityChanged += OnDeckEditorVisibilityChanged;
            Log("enabled");
        }

        public override void Disable()
        {
            PluginEvents.PopupChanged -= OnPopupChanged;
            PluginEvents.CardCollectionChanged -= OnCardCollectionChanged;
            PluginEvents.DeckEditorVisibilityChanged -= OnDeckEditorVisibilityChanged;

            Deactivate();
            openPopup = null;
            trackedView = null;
            appliedOrder = null;
        }

        /// <summary>
        /// Only the visual state of the sort popup needs a few frames after it was opened,
        /// because the game rows switch themselves on in their Start().
        /// </summary>
        public override void Tick()
        {
            if (openPopup != null && Time.frameCount <= syncPopupUntilFrame)
                SearchOrderRowInjector.SyncState(this, openPopup);
        }

        #endregion

        #region Chosen sort

        /// <summary>Called by the plugin row of the sort popup (ReleaseDateSortToggle).</summary>
        public void Activate(Direction direction, Sprite icon)
        {
            IsActive = true;
            ActiveDirection = direction;
            activeIcon = icon;

            CardCollectionView._SortOrder = InvalidSortOrder;
            appliedOrder = null;
            UpdateSortButton();
            Log("sort activated: " + direction);
        }

        /// <summary>Hands the card list back to the game sorts.</summary>
        public void Deactivate()
        {
            if (!IsActive)
                return;

            IsActive = false;
            appliedOrder = null;
            Log("sort deactivated");
        }

        #endregion

        #region Event handling

        private void OnPopupChanged(Popup popup)
        {
            var sortPopup = popup as PopupSearchOrder;
            if (sortPopup == null)
            {
                openPopup = null;
                return;
            }

            SearchOrderRowInjector.Inject(this, sortPopup);
            openPopup = sortPopup;
            syncPopupUntilFrame = Time.frameCount + 3;
        }

        private void OnDeckEditorVisibilityChanged(bool showing)
        {
            if (showing)
                return;

            // The deck editor is gone, the same way the game drops its own sort order.
            Deactivate();
            openPopup = null;
            trackedView = null;
            appliedOrder = null;
        }

        private void OnCardCollectionChanged(CardCollectionView view)
        {
            if (view == null)
            {
                trackedView = null;
                return;
            }

            if (!ReferenceEquals(view, trackedView))
            {
                // A fresh view resets the static sort order to the game default.
                trackedView = view;
                appliedOrder = null;

                if (IsActive)
                {
                    CardCollectionView._SortOrder = InvalidSortOrder;
                    UpdateSortButton();
                }

                return;
            }

            if (!IsActive)
                return;

            if (CardCollectionView._SortOrder != InvalidSortOrder)
            {
                // The player picked one of the game sort rows, let the game sort win.
                Deactivate();
                return;
            }

            EnsureOrder(view);
        }

        #endregion

        #region Ordering

        private void EnsureOrder(CardCollectionView view)
        {
            var printed = view.printedCards;
            if (printed == null || printed.Count < 2)
            {
                appliedOrder = printed;
                return;
            }

            // The list the plugin installed is already correct.
            if (ReferenceEquals(printed, appliedOrder))
                return;

            var order = BuildOrder(printed, ActiveDirection);
            if (SameOrder(printed, order))
                return;

            ApplyOrder(view, order);
        }

        private void ApplyOrder(CardCollectionView view, List<int> order)
        {
            var args = new List<string[]>(order.Count);
            for (int i = 0; i < order.Count; i++)
                args.Add(new[] { order[i].ToString() });

            view.printedCards = order;
            view.superScrollView.Print(args);
            appliedOrder = order;

            // Same as CardCollectionView.PrintCards(): printing destroys the items, the collection
            // keeps the input response when it was the active region.
            try
            {
                var deckEditor = PluginGame.DeckEditor;
                if (deckEditor != null && deckEditor.ResponseRegion == MDPro3.UI.ServantUI.DeckEditorUI.ResponseRegion.Collection)
                    view.SelectDefaultItem();
            }
            catch (Exception)
            {
                // The deck editor can be closed while the list is re-ordered.
            }
        }

        /// <summary>
        /// Orders a card code list by release date. Unknown dates are always put at the end,
        /// equal dates keep their previous relative order.
        /// </summary>
        public List<int> BuildOrder(IList<int> codes, Direction direction)
        {
            var result = new List<int>(codes == null ? 0 : codes.Count);
            if (codes == null || codes.Count == 0)
                return result;

            var entries = new List<Entry>(codes.Count);
            for (int i = 0; i < codes.Count; i++)
            {
                entries.Add(new Entry
                {
                    Code = codes[i],
                    Key = CardReleaseDate.GetKey(codes[i]),
                    Index = i
                });
            }

            entries.Sort((left, right) => Compare(left, right, direction));

            for (int i = 0; i < entries.Count; i++)
                result.Add(entries[i].Code);

            return result;
        }

        private static int Compare(Entry left, Entry right, Direction direction)
        {
            bool leftHasDate = CardReleaseDate.HasDate(left.Key);
            bool rightHasDate = CardReleaseDate.HasDate(right.Key);

            if (leftHasDate != rightHasDate)
                return leftHasDate ? -1 : 1;

            if (leftHasDate && left.Key != right.Key)
                return direction == Direction.NewestFirst
                    ? right.Key.CompareTo(left.Key)
                    : left.Key.CompareTo(right.Key);

            return left.Index.CompareTo(right.Index);
        }

        private struct Entry
        {
            public int Code;
            public long Key;
            public int Index;
        }

        private static bool SameOrder(List<int> left, List<int> right)
        {
            if (left == null || right == null || left.Count != right.Count)
                return false;

            for (int i = 0; i < left.Count; i++)
                if (left[i] != right[i])
                    return false;

            return true;
        }

        private void UpdateSortButton()
        {
            var view = PluginGame.CardCollectionView;
            if (view == null)
                return;

            view.SetSortText(ReleaseDateSortLabels.Label);
            view.SetSortIcon(activeIcon);
        }

        #endregion
    }
}
