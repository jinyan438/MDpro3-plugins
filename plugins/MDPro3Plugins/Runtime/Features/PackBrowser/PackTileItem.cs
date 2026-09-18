using MDPro3.Duel.YGOSharp;
using MDPro3.UI;
using MDPro3.Utility;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.PackBrowser
{
    /// <summary>What a tile shows: the card artwork (pack covers) or the whole card (pack content).</summary>
    public enum PackTileContent
    {
        /// <summary>The square artwork crop (Data/Picture/Art), used for the pack covers.</summary>
        Art,

        /// <summary>The complete card picture, used for the cards a pack contains.</summary>
        Card,
    }

    /// <summary>
    /// One tile of the pack browser grid.
    ///
    /// Reuses the game's deck tile input and sound. Pack covers get a tall foil wrapper;
    /// pack contents retain the original complete card picture and deck tile decorations.
    /// </summary>
    public sealed class PackTileItem : SelectionToggle_ScrollRectItem
    {
        /// <summary>Tile size. The browser uses it as the grid pitch.</summary>
        public const float TileWidth = 240f;
        public const float TileHeight = 276f;
        public const float PackTileWidth = 224f;
        public const float PackTileHeight = 482f;

        private const float PictureHeight = 228f;
        private const float PictureTopInset = 6f;
        private const float CardTitleBottomInset = 8f;

        private const string LabelTitle = "TextDeckName";

        /// <summary>Artwork inside the foil wrapper, loaded from the original cover card.</summary>
        public const string ArtObjectName = "PackCoverArt";

        /// <summary>Card shaped slot, used for the cards of a pack.</summary>
        public const string CardObjectName = "PackCardImage";

        /// <summary>Deck specific visuals of the template, the browser draws its own pictures.</summary>
        private static readonly string[] HiddenLabels =
        {
            "DeckImage",
            "DeckCaseIcon",
            "IconAddDeck",
            "SelectedStateToggle",
            "IconOn",
            "CardPos0",
            "CardPos1",
            "CardPos2",
        };

        /// <summary>Decoration of the tile that has to stay above the pictures.</summary>
        private static readonly string[] AboveArtLabels = { "Hover", "Corner", "SelectCursor" };

        private static readonly string[] SoundLabelFields =
        {
            "SoundLabelClick",
            "SoundLabelClickInactive",
            "SoundLabelPointerEnter",
            "SoundLabelSelectedGamePad",
        };

        // filled once from the prefab instance before the deck behaviour is removed, then applied
        // by every tile that is cloned from it
        private static readonly string[] inheritedSounds = new string[4];
        private static bool soundsInherited;

        private PackBrowserOverlay owner;
        private PackWrapperVisual wrapper;
        private RectTransform cardRect;
        private ArtRawImageHandler art;
        private CardRawImageHandler cardPicture;
        private TextMeshProUGUI title;
        private PackTileContent shownContent = PackTileContent.Art;
        private Transform deckBody;
        private TextMeshProUGUI packTitle;

        /// <summary>Index in the list that is currently printed by the browser.</summary>
        public int EntryIndex { get; private set; }

        #region Setup

        /// <summary>
        /// Called once per prefab instance by the browser, before the game behaviour is removed.
        /// </summary>
        public static void InheritSounds(SelectionToggle source)
        {
            if (source == null || soundsInherited)
                return;

            for (int i = 0; i < SoundLabelFields.Length; i++)
            {
                var field = FindField(source.GetType(), SoundLabelFields[i]);
                if (field != null && field.GetValue(source) is string value)
                    inheritedSounds[i] = value;
            }

            soundsInherited = true;
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
            EnsureEvents();
            base.Awake();

            exclusiveToggle = true;
            canToggleOffSelf = false;
            selectedWhenHover = true;

            if (transform is RectTransform rect)
                rect.sizeDelta = new Vector2(TileWidth, TileHeight);

            EnsureEvents();
            ApplyInheritedSounds();
        }

        private void EnsureEvents()
        {
            clickEvent ??= new SelectionButtonClickEvent();
            clickEvent.onLeftClick ??= new UnityEvent();
            clickEvent.onMiddleClick ??= new UnityEvent();
            clickEvent.onRightClick ??= new UnityEvent();

            hoverEvent ??= new SelectionButtonHoverEvent();
            hoverEvent.onHoverOn ??= new UnityEvent();
            hoverEvent.onHoverOff ??= new UnityEvent();

            selectEvent ??= new SelectionButtonSelectEvent();
            selectEvent.onSelect ??= new UnityEvent();
            selectEvent.onDeselect ??= new UnityEvent();

            navigationEvent ??= new SelectionButtonNavigationEvent();
            navigationEvent.onLeftNavigation ??= new UnityEvent();
            navigationEvent.onRightNavigation ??= new UnityEvent();
            navigationEvent.onUpNavigation ??= new UnityEvent();
            navigationEvent.onDownNavigation ??= new UnityEvent();

            toggleEvent ??= new SelectionToggleEvent();
            toggleEvent.onToggleOn ??= new UnityEvent();
            toggleEvent.onToggleOff ??= new UnityEvent();

            submitEvent ??= new SelectionSubmitEvent();
            submitEvent.onSubmit ??= new UnityEvent();
        }

        private void ApplyInheritedSounds()
        {
            if (string.IsNullOrEmpty(SoundLabelClick))
                SoundLabelClick = Or(inheritedSounds[0], "SE_MENU_SELECT_01");
            if (string.IsNullOrEmpty(SoundLabelClickInactive))
                SoundLabelClickInactive = Or(inheritedSounds[1], "SE_MENU_SELECT_01");
            if (string.IsNullOrEmpty(SoundLabelPointerEnter))
                SoundLabelPointerEnter = Or(inheritedSounds[2], "SE_DECK_CARD_SELECT");
            if (string.IsNullOrEmpty(SoundLabelSelectedGamePad))
                SoundLabelSelectedGamePad = Or(inheritedSounds[3], "SE_MENU_OVERLAP_02");
            if (string.IsNullOrEmpty(SoundLabelClickOn))
                SoundLabelClickOn = "SE_MENU_S_DECIDE_01";
            if (string.IsNullOrEmpty(SoundLabelClickOff))
                SoundLabelClickOff = "SE_MENU_S_DECIDE_02";
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        /// <summary>Binds a recycled tile to the browser. Called for every print of the list.</summary>
        public void Bind(PackBrowserOverlay overlay)
        {
            owner = overlay;
            EnsureEvents();
            ApplyInheritedSounds();
        }

        #endregion

        #region Toggle visuals

        // The deck prefab has no scroll-item "Offset". Keep its input state, without the slide.
        public override void ToggleOnNow()
        {
            isOn = true;
            wrapper?.SetHighlighted(true, true);
        }

        public override void ToggleOffNow()
        {
            isOn = false;
            // SuperScrollView calls this before rebinding a recycled tile.
            ResetVisualState(true);
            wrapper?.SetHighlighted(false, true);
        }

        protected override void ToggleOn()
        {
            wrapper?.SetHighlighted(true);
        }

        protected override void ToggleOff()
        {
            wrapper?.SetHighlighted(hoverd);
        }

        protected override void HoverOn()
        {
            base.HoverOn();
            wrapper?.SetHighlighted(true);
        }

        protected override void HoverOff(bool force = false)
        {
            base.HoverOff(force);
            wrapper?.SetHighlighted(isOn);
        }

        #endregion

        #region Content

        /// <summary>Shows a picture plus a caption on the tile.</summary>
        public void Show(int entryIndex, string caption, int code, PackTileContent content)
        {
            EntryIndex = entryIndex;

            EnsurePictureSlots();

            if (content == PackTileContent.Card)
            {
                // hand the artwork back before the same tile shows a whole card
                if (shownContent == PackTileContent.Art)
                    art.SetArt(0);

                cardPicture.SetCard(code);
                cardRect.gameObject.SetActive(true);
                wrapper.gameObject.SetActive(false);
            }
            else
            {
                if (shownContent == PackTileContent.Card)
                    cardPicture.SetCard((Card)null);

                art.SetArt(code);
                wrapper.SetColor(entryIndex);
                wrapper.gameObject.SetActive(true);
                cardRect.gameObject.SetActive(false);
            }

            shownContent = content;

            bool isPack = content == PackTileContent.Art;
            ((RectTransform)transform).sizeDelta = isPack
                ? new Vector2(PackTileWidth, PackTileHeight)
                : new Vector2(TileWidth, TileHeight);
            if (deckBody != null)
                deckBody.gameObject.SetActive(!isPack);
            packTitle.gameObject.SetActive(isPack);
            packTitle.text = caption;
            wrapper.SetHighlighted(isOn, true);

            if (title != null)
                title.text = Trim(caption, 14);
        }

        /// <summary>
        /// Creates the two picture slots of this tile and hides the deck specific visuals. The
        /// slots are built from code so they never depend on the state of the game's own slots.
        /// </summary>
        private void EnsurePictureSlots()
        {
            if (wrapper == null)
            {
                wrapper = PackWrapperVisual.Create(transform);
                art = wrapper.Art;
            }

            if (cardRect == null)
            {
                // a whole card is card shaped
                cardRect = NewPicture(CardObjectName, PictureHeight * CardImageLoader.DEFAULT_ASPECT, PictureHeight);
                cardPicture = cardRect.gameObject.AddComponent<CardRawImageHandler>();
            }

            if (title == null)
                title = Manager.GetElement<TextMeshProUGUI>(LabelTitle);
            if (title != null)
            {
                var titleRect = title.rectTransform;
                titleRect.anchoredPosition = new Vector2(titleRect.anchoredPosition.x, CardTitleBottomInset);
            }

            if (deckBody == null)
                deckBody = transform.Find("Body");

            if (packTitle == null)
            {
                // Separate caption so the prefab's responsive layout and color animations
                // cannot move or recolor the pack label when its deck visuals are hidden.
                var host = new GameObject("PackCaption", typeof(RectTransform));
                var rect = (RectTransform)host.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(PackTileWidth, 32f);
                rect.anchoredPosition = new Vector2(0f, -450f);
                packTitle = host.AddComponent<TextMeshProUGUI>();
                if (title != null)
                    packTitle.font = title.font;
                packTitle.fontSize = 22f;
                packTitle.alignment = TextAlignmentOptions.Center;
                packTitle.color = new Color(0.89f, 0.88f, 0.80f);
                packTitle.textWrappingMode = TextWrappingModes.NoWrap;
                packTitle.overflowMode = TextOverflowModes.Ellipsis;
                packTitle.raycastTarget = false;
            }

            foreach (string label in HiddenLabels)
            {
                var element = Manager.GetElement(label);
                if (element != null && element.activeSelf)
                    element.SetActive(false);
            }
        }

        private RectTransform NewPicture(string name, float width, float height)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)host.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, -PictureTopInset);

            var raw = host.GetComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = Color.white;

            // above the plate, below the decorations of the tile
            rect.SetAsLastSibling();
            foreach (string label in AboveArtLabels)
            {
                var decoration = Manager.GetElement(label);
                if (decoration != null)
                    decoration.transform.SetAsLastSibling();
            }

            return rect;
        }

        private static string Trim(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength - 1) + "\u2026";
        }

        #endregion

        #region Interaction

        protected override void OnClick()
        {
            var selectable = GetSelectable();
            bool interactable = selectable == null || selectable.interactable;
            AudioManager.PlaySE(interactable ? SoundLabelClick : SoundLabelClickInactive);

            RememberAsResponser();
            SetToggleOn();

            if (owner != null)
                owner.OnTileSubmit(this);
        }

        protected override void OnSubmit()
        {
            OnClick();
        }

        protected override void CallToggleOnEvent()
        {
            base.CallToggleOnEvent();
            if (owner != null)
                owner.OnTileFocus(this);
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

        #endregion
    }
}
