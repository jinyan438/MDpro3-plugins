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
    /// The plate, the hover animation, the selection cursor and the sound come from the game's own
    /// deck grid tile (UI/ItemDeck.prefab). Its deck specific content (deck case art and the three
    /// fanned card slots) is hidden and replaced by the tile's own picture slots, because those
    /// slots are not visible outside the deck selector's pickup state.
    ///
    /// Two picture slots are created once per tile and only one is active at a time:
    ///   * "PackCoverArt" is a square slot filled through the game's ArtRawImageHandler.
    ///   * "PackCardImage" is a card shaped slot filled through the game's CardRawImageHandler,
    ///     which renders the whole card the same way the deck editor's card grid does it.
    /// </summary>
    public sealed class PackTileItem : SelectionToggle_ScrollRectItem
    {
        /// <summary>Tile size. The browser uses it as the grid pitch.</summary>
        public const float TileWidth = 240f;
        public const float TileHeight = 276f;

        private const float PictureHeight = 228f;
        private const float PictureTopInset = 6f;

        private const string LabelTitle = "TextDeckName";

        /// <summary>Square artwork slot, used for the pack covers.</summary>
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
        private RectTransform artRect;
        private RectTransform cardRect;
        private ArtRawImageHandler art;
        private CardRawImageHandler cardPicture;
        private TextMeshProUGUI title;
        private PackTileContent shownContent = PackTileContent.Art;

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

        // UI/ItemDeck.prefab has no "Offset" element, so the slide animation of the game scroll
        // item cannot run. The game's own deck tile overrides the same four methods for that
        // reason; the selection feedback comes from Hover / SelectCursor / ColorContainerGraphic.
        public override void ToggleOnNow()
        {
        }

        public override void ToggleOffNow()
        {
        }

        protected override void ToggleOn()
        {
        }

        protected override void ToggleOff()
        {
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
                artRect.gameObject.SetActive(false);
            }
            else
            {
                if (shownContent == PackTileContent.Card)
                    cardPicture.SetCard((Card)null);

                art.SetArt(code);
                artRect.gameObject.SetActive(true);
                cardRect.gameObject.SetActive(false);
            }

            shownContent = content;

            if (title != null)
                title.text = Trim(caption, 14);
        }

        /// <summary>
        /// Creates the two picture slots of this tile and hides the deck specific visuals. The
        /// slots are built from code so they never depend on the state of the game's own slots.
        /// </summary>
        private void EnsurePictureSlots()
        {
            if (artRect == null)
            {
                // the artwork crop is square (624x624), so the cover slot is square
                artRect = NewPicture(ArtObjectName, PictureHeight, PictureHeight);
                art = artRect.gameObject.AddComponent<ArtRawImageHandler>();
            }

            if (cardRect == null)
            {
                // a whole card is card shaped
                cardRect = NewPicture(CardObjectName, PictureHeight * CardImageLoader.DEFAULT_ASPECT, PictureHeight);
                cardPicture = cardRect.gameObject.AddComponent<CardRawImageHandler>();
            }

            if (title == null)
                title = Manager.GetElement<TextMeshProUGUI>(LabelTitle);

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

        /// <summary>The square slot of a pack cover is square, not card shaped.</summary>
        public void UseSquarePicture()
        {
            EnsurePictureSlots();
            artRect.sizeDelta = new Vector2(PictureHeight, PictureHeight);
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
