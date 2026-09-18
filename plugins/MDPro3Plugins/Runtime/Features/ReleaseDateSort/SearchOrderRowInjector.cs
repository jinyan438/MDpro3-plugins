using MDPro3.UI;
using MDPro3.UI.Popup;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using YgomSystem.ElementSystem;

namespace MDPro3.Plugins.Features.ReleaseDateSort
{
    /// <summary>
    /// Adds the "release date" row to the deck editor sort popup (PopupSearchOrder).
    ///
    /// The popup keeps one grid row per sort criteria: [text label][ascending][descending].
    /// This injector clones the text label of the first criteria plus the last ascending and
    /// descending toggles, replaces their behaviour and appends them to the grid, so the new row
    /// is laid out by the popup itself.
    /// </summary>
    public static class SearchOrderRowInjector
    {
        public const string GridElementLabel = "Middle";
        public const string TextObjectName = "Text_ReleaseDate";
        public const string AscRowName = "Toggle ReleaseDate Up";
        public const string DescRowName = "Toggle ReleaseDate Down";

        private static bool reportedMissingTemplates;

        /// <summary>Adds the row. Returns true when the popup has just been modified.</summary>
        public static bool Inject(ReleaseDateSortFeature feature, PopupSearchOrder popup)
        {
            if (feature == null || popup == null)
                return false;

            var manager = popup.GetComponent<ElementObjectManager>();
            var grid = manager != null ? manager.GetElement(GridElementLabel) : null;
            if (grid == null)
            {
                ReportMissingTemplates("the sort popup has no '" + GridElementLabel + "' element");
                return false;
            }

            var gridTransform = grid.transform;
            if (gridTransform.Find(TextObjectName) != null)
                return false;

            GameObject textTemplate = null;
            SelectionToggle ascendingTemplate = null;
            SelectionToggle descendingTemplate = null;
            FindTemplates(gridTransform, ref textTemplate, ref ascendingTemplate, ref descendingTemplate);

            if (textTemplate == null || ascendingTemplate == null || descendingTemplate == null)
            {
                ReportMissingTemplates("the sort popup layout is not the expected one");
                return false;
            }

            var labelObject = Object.Instantiate(textTemplate, gridTransform, false);
            labelObject.name = TextObjectName;
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            if (label != null)
                label.text = ReleaseDateSortLabels.Label;

            var ascending = CreateRow(feature, ascendingTemplate, gridTransform, ReleaseDateSortFeature.Direction.OldestFirst, AscRowName);
            var descending = CreateRow(feature, descendingTemplate, gridTransform, ReleaseDateSortFeature.Direction.NewestFirst, DescRowName);
            LinkNavigation(ascendingTemplate, descendingTemplate, ascending, descending);

            return true;
        }

        private static void FindTemplates(
            Transform grid,
            ref GameObject textTemplate,
            ref SelectionToggle ascendingTemplate,
            ref SelectionToggle descendingTemplate)
        {
            var toggles = new List<SelectionToggle>();

            for (int i = 0; i < grid.childCount; i++)
            {
                var child = grid.GetChild(i);

                if (textTemplate == null
                    && child.GetComponent<TextMeshProUGUI>() != null
                    && child.GetComponent<SelectionToggle>() == null)
                    textTemplate = child.gameObject;

                var toggle = child.GetComponent<SelectionToggle_SearchOrder>();
                if (toggle != null)
                    toggles.Add(toggle);
            }

            // The criteria pairs are always "ascending, descending" next to each other and the new
            // row is appended below the last criteria, so clone the last pair: the copies keep
            // sensible left/right navigation and the correct "down" neighbour (the buttons below
            // the grid).
            if (toggles.Count >= 2)
            {
                ascendingTemplate = toggles[toggles.Count - 2];
                descendingTemplate = toggles[toggles.Count - 1];
                if (ascendingTemplate.GetIconSprite() != descendingTemplate.GetIconSprite())
                    return;
            }

            for (int i = 0; i + 1 < toggles.Count; i++)
            {
                if (toggles[i].GetIconSprite() == toggles[i + 1].GetIconSprite())
                    continue;

                ascendingTemplate = toggles[i];
                descendingTemplate = toggles[i + 1];
                return;
            }

            ascendingTemplate = null;
            descendingTemplate = null;
        }

        private static ReleaseDateSortToggle CreateRow(
            ReleaseDateSortFeature feature,
            SelectionToggle template,
            Transform grid,
            ReleaseDateSortFeature.Direction direction,
            string name)
        {
            var row = Object.Instantiate(template.gameObject, grid, false);
            row.name = name;

            var gameBehaviour = row.GetComponent<SelectionToggle_SearchOrder>();
            if (gameBehaviour != null)
                Object.DestroyImmediate(gameBehaviour);

            var toggle = row.AddComponent<ReleaseDateSortToggle>();
            toggle.Initialize(feature, direction, template);
            return toggle;
        }

        /// <summary>
        /// The popup uses explicit gamepad navigation. The cloned rows carry the navigation of the
        /// rows they came from, so reconnect the new row with the row above it and with the buttons
        /// below the grid.
        /// </summary>
        private static void LinkNavigation(
            SelectionToggle ascendSource,
            SelectionToggle descendSource,
            SelectionToggle ascend,
            SelectionToggle descend)
        {
            var ascendSourceSelectable = ascendSource != null ? ascendSource.GetSelectable() : null;
            var descendSourceSelectable = descendSource != null ? descendSource.GetSelectable() : null;
            var ascendSelectable = ascend != null ? ascend.GetSelectable() : null;
            var descendSelectable = descend != null ? descend.GetSelectable() : null;
            if (ascendSourceSelectable == null || descendSourceSelectable == null
                || ascendSelectable == null || descendSelectable == null)
                return;

            var ascendSourceNavigation = ascendSourceSelectable.navigation;
            var descendSourceNavigation = descendSourceSelectable.navigation;

            var ascendDown = ascendSourceNavigation.selectOnDown;
            var descendDown = descendSourceNavigation.selectOnDown;

            // the row above leads into the new row
            ascendSourceNavigation.selectOnDown = ascendSelectable;
            descendSourceNavigation.selectOnDown = descendSelectable;
            ascendSourceSelectable.navigation = ascendSourceNavigation;
            descendSourceSelectable.navigation = descendSourceNavigation;

            var ascendNavigation = ascendSelectable.navigation;
            ascendNavigation.selectOnUp = ascendSourceSelectable;
            ascendNavigation.selectOnDown = ascendDown;
            ascendNavigation.selectOnRight = descendSelectable;
            ascendSelectable.navigation = ascendNavigation;

            var descendNavigation = descendSelectable.navigation;
            descendNavigation.selectOnUp = descendSourceSelectable;
            descendNavigation.selectOnDown = descendDown;
            descendNavigation.selectOnLeft = ascendSelectable;
            descendSelectable.navigation = descendNavigation;
        }

        public static ReleaseDateSortToggle FindRow(PopupSearchOrder popup)
        {
            if (popup == null)
                return null;

            var manager = popup.GetComponent<ElementObjectManager>();
            var grid = manager != null ? manager.GetElement(GridElementLabel) : null;
            if (grid == null)
                return null;

            var row = grid.transform.Find(DescRowName);
            return row != null ? row.GetComponent<ReleaseDateSortToggle>() : null;
        }

        /// <summary>
        /// Keeps the visual state of the row in sync with the feature state. Called for a few
        /// frames after the popup was opened and after the popup changed, because the game rows
        /// toggle themselves on in their Start() when their sort order equals the (static) current
        /// sort order.
        /// </summary>
        public static void SyncState(ReleaseDateSortFeature feature, PopupSearchOrder popup)
        {
            if (feature == null)
                return;

            var row = FindRow(popup);
            if (row == null)
                return;

            if (feature.IsActive)
            {
                CardCollectionView._SortOrder = ReleaseDateSortFeature.InvalidSortOrder;
                TurnOffOtherRows(popup, row);
                if (!row.isOn)
                    row.SetToggleOn(false);
            }
            else if (row.isOn)
            {
                row.SetToggleOff(false);
            }
        }

        private static void TurnOffOtherRows(PopupSearchOrder popup, SelectionToggle keepOn)
        {
            var rows = popup.GetComponentsInChildren<SelectionToggle>(true);
            foreach (var candidate in rows)
            {
                if (candidate == null || candidate == keepOn || !candidate.isOn)
                    continue;
                candidate.SetToggleOff(false);
            }
        }

        private static void ReportMissingTemplates(string reason)
        {
            if (reportedMissingTemplates)
                return;

            reportedMissingTemplates = true;
            PluginLog.Warn("release date sort is unavailable: " + reason);
        }
    }
}
