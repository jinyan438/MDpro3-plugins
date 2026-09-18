using MDPro3.UI;
using MDPro3.UI.Popup;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace MDPro3.Plugins.Features.ReleaseDateSort
{
    /// <summary>
    /// One entry of the deck editor sort popup ("sort by card release date").
    ///
    /// It is created by cloning one of the game rows (so the look, sizing and the mobile overrides
    /// stay identical) and replacing the game behaviour with this component. Because the component
    /// is added from code, the serialized UnityEvent containers have to be created here as well.
    /// </summary>
    public sealed class ReleaseDateSortToggle : SelectionToggle
    {
        private static readonly string[] SoundLabelFields =
        {
            "SoundLabelClick",
            "SoundLabelClickInactive",
            "SoundLabelPointerEnter",
            "SoundLabelSelectedGamePad",
            "SoundLabelClickOn",
            "SoundLabelClickOff"
        };

        private ReleaseDateSortFeature feature;
        private ReleaseDateSortFeature.Direction direction;

        public ReleaseDateSortFeature.Direction Direction => direction;

        public void Initialize(
            ReleaseDateSortFeature feature,
            ReleaseDateSortFeature.Direction direction,
            SelectionToggle source)
        {
            this.feature = feature;
            this.direction = direction;

            exclusiveToggle = true;
            canToggleOffSelf = false;

            EnsureEvents();
            CopySoundLabels(source);
            EnsureSoundLabels();
        }

        private void EnsureEvents()
        {
            if (clickEvent == null)
                clickEvent = new SelectionButtonClickEvent();
            clickEvent.onLeftClick ??= new UnityEvent();
            clickEvent.onMiddleClick ??= new UnityEvent();
            clickEvent.onRightClick ??= new UnityEvent();

            if (hoverEvent == null)
                hoverEvent = new SelectionButtonHoverEvent();
            hoverEvent.onHoverOn ??= new UnityEvent();
            hoverEvent.onHoverOff ??= new UnityEvent();

            if (selectEvent == null)
                selectEvent = new SelectionButtonSelectEvent();
            selectEvent.onSelect ??= new UnityEvent();
            selectEvent.onDeselect ??= new UnityEvent();

            if (navigationEvent == null)
                navigationEvent = new SelectionButtonNavigationEvent();
            navigationEvent.onLeftNavigation ??= new UnityEvent();
            navigationEvent.onRightNavigation ??= new UnityEvent();
            navigationEvent.onUpNavigation ??= new UnityEvent();
            navigationEvent.onDownNavigation ??= new UnityEvent();

            if (toggleEvent == null)
                toggleEvent = new SelectionToggleEvent();
            toggleEvent.onToggleOn ??= new UnityEvent();
            toggleEvent.onToggleOff ??= new UnityEvent();

            if (submitEvent == null)
                submitEvent = new SelectionSubmitEvent();
            submitEvent.onSubmit ??= new UnityEvent();
        }

        private void CopySoundLabels(SelectionToggle source)
        {
            if (source == null)
                return;

            foreach (string name in SoundLabelFields)
            {
                var field = FindField(source.GetType(), name);
                if (field == null || field.FieldType != typeof(string))
                    continue;

                if (field.GetValue(source) is string value && !string.IsNullOrEmpty(value))
                    field.SetValue(this, value);
            }
        }

        private void EnsureSoundLabels()
        {
            if (string.IsNullOrEmpty(SoundLabelClick))
                SoundLabelClick = "SE_MENU_SELECT_01";
            if (string.IsNullOrEmpty(SoundLabelClickOn))
                SoundLabelClickOn = "SE_MENU_S_DECIDE_01";
            if (string.IsNullOrEmpty(SoundLabelClickOff))
                SoundLabelClickOff = "SE_MENU_S_DECIDE_02";
            if (string.IsNullOrEmpty(SoundLabelSelectedGamePad))
                SoundLabelSelectedGamePad = "SE_MENU_OVERLAP_02";
        }

        private static FieldInfo FindField(System.Type type, string name)
        {
            while (type != null && type != typeof(MonoBehaviour))
            {
                var field = type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;

                type = type.BaseType;
            }

            return null;
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureEvents();
        }

        protected override void OnClick()
        {
            var selectable = GetSelectable();
            AudioManager.PlaySE(selectable != null && selectable.interactable ? SoundLabelClickOn : SoundLabelClickInactive);

            RememberAsResponser();
            SetToggleOn();
            ApplySort();
        }

        protected override void OnSubmit()
        {
            OnClick();
        }

        private void RememberAsResponser()
        {
            if (!SetToResponser)
                return;

            var selectable = GetSelectable();
            if (selectable == null)
                return;

            var ui = PluginGame.UI;
            if (ui != null && ui.currentPopupB != null)
            {
                ui.currentPopupB.lastSelectable = selectable;
                return;
            }

            var servant = PluginGame.CurrentServant;
            if (servant != null)
                servant.lastSelectable = selectable;
        }

        private void ApplySort()
        {
            if (feature == null)
            {
                PluginLog.Error("release date sort row has no feature instance");
                return;
            }

            feature.Activate(direction, GetIconSprite());

            // Close the popup this row belongs to, the same way the game rows do.
            var owner = GetComponentInParent<Popup>();
            if (owner != null && ReferenceEquals(PluginGame.CurrentPopup, owner))
                owner.Hide();
        }
    }
}
