using MDPro3.Duel.YGOSharp;
using MDPro3.Servant;
using MDPro3.UI;
using MDPro3.Utility;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.PackBrowser
{
    internal enum BrowserMode
    {
        Packs,
        Cards,
    }

    /// <summary>
    /// The full screen pack browser.
    ///
    /// It is built at runtime on top of the game UI canvas and reuses the game components where it
    /// matters: SuperScrollView for the recycling grid, the deck grid tile (UI/ItemDeck.prefab) for
    /// the cells and ArtRawImageHandler for the card art. A shared foil wrapper surrounds each
    /// pack's original cover artwork.
    ///
    /// While it is open it registers itself as UIManager.InputBlocker, which stops the main menu
    /// from reacting to Esc / right click at the same time.
    /// </summary>
    public sealed class PackBrowserOverlay : MonoBehaviour
    {
        private const string TileAddress = "UI/ItemDeck.prefab";

        // the tile size lives in PackTileItem, the pitch adds the gap of the grid
        private const float GridTopPadding = 10f;
        private const float GridBottomPadding = 30f;

        private readonly List<PackEntry> packs = new List<PackEntry>();
        private readonly List<int> cards = new List<int>();

        private BrowserMode mode = BrowserMode.Packs;
        private PackEntry current;
        private int focused;

        private ScrollRect scrollRect;
        private RectTransform viewportRect;
        private SuperScrollView scrollView;
        private BrowserMode gridMode;
        private AsyncOperationHandle<GameObject> tileHandle;
        private bool templateReady;
        private bool pendingPrint;
        private bool closing;
        private GameObject template;

        private TextMeshProUGUI titleText;
        private TextMeshProUGUI infoText;
        private TextMeshProUGUI hintText;

        public int PackCount => packs.Count;

        public int CardCount => mode == BrowserMode.Packs ? packs.Count : cards.Count;

        /// <summary>true once the tile template is loaded and the grid was built.</summary>
        public bool IsReady => templateReady && scrollView != null;

        /// <summary>Number of entries of the current list (the grid recycles its tiles).</summary>
        public int TileCount => scrollView != null ? scrollView.items.Count : 0;

        /// <summary>Number of tiles that currently exist as game objects.</summary>
        public int LiveTileCount => scrollView != null ? scrollView.gameObjects.Count : 0;

        /// <summary>Size of the scrolling viewport, used to check the runtime layout.</summary>
        public Vector2 ViewportSize => viewportRect != null ? viewportRect.rect.size : Vector2.zero;

        /// <summary>Diagnostic: state of the picture slots of the first live tile.</summary>
        public string DescribeFirstTile()
        {
            if (scrollView == null || scrollView.gameObjects.Count == 0)
                return "no live tile";

            var tile = scrollView.gameObjects[0];
            if (tile == null)
                return "first tile is gone";

            return DescribeSlot(tile.transform, PackWrapperVisual.ObjectName + "/Foil/" + PackTileItem.ArtObjectName, "cover")
                + " | "
                + DescribeSlot(tile.transform, PackTileItem.CardObjectName, "card");
        }

        private static string DescribeSlot(Transform tile, string objectName, string label)
        {
            var slot = tile.Find(objectName);
            if (slot == null)
                return label + ": no slot";

            var raw = slot.GetComponent<RawImage>();
            if (raw == null)
                return label + ": slot without RawImage";

            string size = slot is RectTransform rect ? rect.sizeDelta.ToString() : "?";
            string texture = raw.texture != null
                ? raw.texture.width + "x" + raw.texture.height
                : "none";

            return label + ": " + size + ", texture " + texture
                + ", active " + slot.gameObject.activeInHierarchy;
        }

        #region Life cycle

        public static PackBrowserOverlay Open()
        {
            var ui = PluginGame.UI;
            if (ui == null)
                return null;

            Transform parent = ui.popup != null ? ui.popup : ui.transform;
            var host = new GameObject("PackBrowser", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            host.transform.SetAsLastSibling();

            return host.AddComponent<PackBrowserOverlay>();
        }

        private void Awake()
        {
            BuildLayout();

            packs.AddRange(PackCatalog.All);

            UIManager.InputBlocker = this;

            LoadTiles();
            ShowPacks();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(UIManager.InputBlocker, this))
                UIManager.InputBlocker = null;

            if (template != null)
            {
                Destroy(template);
                template = null;
            }

            if (tileHandle.IsValid())
            {
                Addressables.ReleaseInstance(tileHandle);
                tileHandle = default;
            }

            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(false);
        }

        private void LoadTiles()
        {
            try
            {
                tileHandle = Addressables.InstantiateAsync(TileAddress);
                tileHandle.Completed += OnTilesLoaded;
            }
            catch (Exception e)
            {
                PluginLog.Error("pack browser: loading " + TileAddress + " failed: " + e.Message);
            }
        }

        private void OnTilesLoaded(AsyncOperationHandle<GameObject> handle)
        {
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                PluginLog.Error("pack browser: " + TileAddress + " is not available");
                return;
            }

            template = handle.Result;
            template.SetActive(false);
            template.transform.SetParent(transform, false);

            var deckBehaviour = template.GetComponent<SelectionToggle_Deck>();
            if (deckBehaviour != null)
            {
                PackTileItem.InheritSounds(deckBehaviour);
                DestroyImmediate(deckBehaviour);
            }

            template.AddComponent<PackTileItem>();
            templateReady = true;

            // the grid could not be printed while the tile template was still loading
            Print();
        }

        #endregion

        #region Layout

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var backdrop = NewImage("Backdrop", root, new Color(0f, 0f, 0f, 0.88f));
            Stretch(backdrop.rectTransform, 0f, 0f, 0f, 0f);

            var font = PickFont();

            // header
            titleText = NewText("Title", root, font, 46f, TextAlignmentOptions.TopLeft,
                new Color(1f, 1f, 1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(60f, -92f), new Vector2(1200f, -34f));

            infoText = NewText("Info", root, font, 30f, TextAlignmentOptions.TopLeft,
                new Color(0.86f, 0.9f, 1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(60f, -146f), new Vector2(1500f, -98f));

            hintText = NewText("Hint", root, font, 26f, TextAlignmentOptions.TopRight,
                new Color(0.72f, 0.76f, 0.86f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-900f, -92f), new Vector2(-60f, -44f));

            // grid
            var area = NewRect("Grid", root, Vector2.zero, Vector2.one,
                new Vector2(60f, 44f), new Vector2(-60f, -170f));

            var scrollObject = new GameObject("ScrollRect", typeof(RectTransform));
            var scrollRectTransform = (RectTransform)scrollObject.transform;
            scrollRectTransform.SetParent(area, false);
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = new Vector2(-30f, 0f);

            scrollRect = scrollObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 60f;

            var viewport = NewImage("Viewport", scrollRectTransform, new Color(0f, 0f, 0f, 0.001f));
            Stretch(viewport.rectTransform, 0f, 0f, 0f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewportRect = viewport.rectTransform;

            var content = new GameObject("Content", typeof(RectTransform));
            var contentTransform = (RectTransform)content.transform;
            contentTransform.SetParent(viewport.rectTransform, false);
            contentTransform.anchorMin = new Vector2(0f, 1f);
            contentTransform.anchorMax = new Vector2(1f, 1f);
            contentTransform.pivot = new Vector2(0.5f, 1f);
            contentTransform.anchoredPosition = Vector2.zero;
            contentTransform.sizeDelta = Vector2.zero;

            scrollRect.viewport = viewport.rectTransform;
            scrollRect.content = contentTransform;

            var bar = NewScrollbar(scrollRectTransform);
            scrollRect.verticalScrollbar = bar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private Scrollbar NewScrollbar(RectTransform parent)
        {
            var barObject = new GameObject("Scrollbar", typeof(RectTransform));
            var barTransform = (RectTransform)barObject.transform;
            barTransform.SetParent(parent, false);
            barTransform.anchorMin = new Vector2(1f, 0f);
            barTransform.anchorMax = new Vector2(1f, 1f);
            barTransform.pivot = new Vector2(0f, 0.5f);
            barTransform.anchoredPosition = new Vector2(12f, 0f);
            barTransform.sizeDelta = new Vector2(14f, 0f);

            var background = NewImage("Background", barTransform, new Color(1f, 1f, 1f, 0.12f));
            Stretch(background.rectTransform, 0f, 0f, 0f, 0f);

            var sliding = NewRect("Sliding Area", barTransform, Vector2.zero, Vector2.one,
                new Vector2(3f, 3f), new Vector2(-3f, -3f));

            var handle = NewImage("Handle", sliding, new Color(1f, 1f, 1f, 0.55f));
            Stretch(handle.rectTransform, 0f, 0f, 0f, 0f);

            var bar = barObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            return bar;
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font,
            float size, TextAlignmentOptions alignment, Color color, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static TMP_FontAsset PickFont()
        {
            var ui = PluginGame.UI;
            if (ui == null)
                return null;

            switch (Language.GetConfig())
            {
                case Language.SimplifiedChinese:
                    return ui.cnMenuTmpFont != null ? ui.cnMenuTmpFont : ui.tmpFont;
                case Language.Japanese:
                case Language.English:
                    return ui.jpMenuTmpFont != null ? ui.jpMenuTmpFont : ui.tmpFont;
                default:
                    return ui.tmpFont;
            }
        }

        #endregion

        #region Content

        /// <summary>Shows the pack wall.</summary>
        public void ShowPacks()
        {
            mode = BrowserMode.Packs;
            current = null;
            focused = -1;
            Print();
        }

        /// <summary>Shows the cards of one pack.</summary>
        internal void ShowCards(PackEntry entry)
        {
            if (entry == null)
                return;

            mode = BrowserMode.Cards;
            current = entry;
            focused = -1;
            cards.Clear();
            cards.AddRange(entry.Cards);
            Print();
        }

        private void Print()
        {
            UpdateHeader();

            // the grid needs the tile template and a laid out viewport, both arrive a frame later
            pendingPrint = true;
            if (!templateReady)
                return;

            var tasks = new List<string[]>();
            int count = mode == BrowserMode.Packs ? packs.Count : cards.Count;
            for (int i = 0; i < count; i++)
                tasks.Add(new[] { ((int)mode).ToString(), i.ToString() });

            if (!templateReady)
                return;

            if (scrollView == null || gridMode != mode)
            {
                scrollView?.Clear();
                // SuperScrollView adds one listener per instance, this browser builds exactly one
                // for its own scrollbar, so clear it first to stay idempotent.
                if (scrollRect.verticalScrollbar != null)
                    scrollRect.verticalScrollbar.onValueChanged.RemoveAllListeners();

                bool isPack = mode == BrowserMode.Packs;
                float pitchX = (isPack ? PackTileItem.PackTileWidth : PackTileItem.TileWidth) + 26f;
                float pitchY = (isPack ? PackTileItem.PackTileHeight : PackTileItem.TileHeight) + 28f;
                scrollView = new SuperScrollView(0, pitchX, pitchY, isPack ? 18f : GridTopPadding,
                    GridBottomPadding, template, OnTileRefresh, scrollRect);
                gridMode = mode;
            }

            scrollView.selected = -1;
            scrollView.Print(tasks);
            pendingPrint = false;
        }

        private void OnTileRefresh(string[] args, GameObject item)
        {
            if (item == null || args == null || args.Length < 2)
                return;

            var tile = item.GetComponent<PackTileItem>();
            if (tile == null)
                return;

            tile.Bind(this);
            if (!int.TryParse(args[1], out int index))
                return;

            if (args[0] == ((int)BrowserMode.Packs).ToString())
            {
                if (index < 0 || index >= packs.Count)
                    return;

                var entry = packs[index];
                tile.Show(index, Caption(entry), entry.CoverCard, PackTileContent.Art);
            }
            else
            {
                if (index < 0 || index >= cards.Count)
                    return;

                int code = cards[index];
                var card = CardsManager.GetCardRaw(code);
                tile.Show(index, card != null ? card.Name : code.ToString(), code, PackTileContent.Card);
            }
        }

        private static string Caption(PackEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.Code))
                return entry.Code;

            return entry.Name;
        }

        private void UpdateHeader()
        {
            if (titleText != null)
                titleText.text = mode == BrowserMode.Packs
                    ? PackBrowserLabels.Title
                    : (current != null ? current.Name : PackBrowserLabels.Title);

            if (hintText != null)
                hintText.text = mode == BrowserMode.Packs ? PackBrowserLabels.Hint : PackBrowserLabels.Back;

            if (infoText == null)
                return;

            if (mode == BrowserMode.Packs)
            {
                infoText.text = packs.Count + " " + PackBrowserLabels.Title;
                return;
            }

            if (current == null)
            {
                infoText.text = string.Empty;
                return;
            }

            infoText.text = current.DateText
                + "  \u00b7  " + current.Count + " " + PackBrowserLabels.Cards
                + "  \u00b7  " + PackBrowserLabels.CoverSource + ": " + current.CoverSourceText;
        }

        /// <summary>Called by a tile when it is selected or hovered.</summary>
        public void OnTileFocus(PackTileItem tile)
        {
            if (tile == null || infoText == null)
                return;

            focused = tile.EntryIndex;
            if (mode != BrowserMode.Packs || focused < 0 || focused >= packs.Count)
                return;

            var entry = packs[focused];
            infoText.text = entry.Name
                + "  \u00b7  " + entry.DateText
                + "  \u00b7  " + entry.Count + " " + PackBrowserLabels.Cards
                + "  \u00b7  " + PackBrowserLabels.CoverSource + ": " + entry.CoverSourceText;
        }

        /// <summary>Called by a tile when it is clicked or confirmed.</summary>
        public void OnTileSubmit(PackTileItem tile)
        {
            if (tile == null)
                return;

            int index = tile.EntryIndex;

            if (mode == BrowserMode.Packs)
            {
                if (index < 0 || index >= packs.Count)
                    return;

                var entry = packs[index];
                if (entry.Count == 0)
                {
                    if (infoText != null)
                        infoText.text = entry.Name + "  \u00b7  " + PackBrowserLabels.Empty;
                    return;
                }

                AudioManager.PlaySE("SE_MENU_DECIDE");
                ShowCards(entry);
                return;
            }

            if (index < 0 || index >= cards.Count)
                return;

            UIManager.ShowCardInfoDetail(cards, index);
        }

        #endregion

        #region Input

        private void Update()
        {
            if (closing)
                return;

            if (!PluginGame.IsReady)
            {
                Close();
                return;
            }

            // the first print waits for the tile template and for the viewport layout
            if (pendingPrint)
            {
                Print();
                return;
            }

            // a game popup (card detail) owns the input while it is open
            if (PluginGame.CurrentPopup != null || UIManager.InputBlocker != this)
                return;

            if (!(PluginGame.CurrentServant is MainMenu))
            {
                Close();
                return;
            }

            if (UserInput.WasCancelPressed || UserInput.MouseRightDown)
            {
                AudioManager.PlaySE("SE_MENU_CANCEL");
                Back();
                return;
            }

            if (scrollRect != null)
            {
                float wheel = UserInput.RightScrollWheel.y + UserInput.LeftScrollWheel.y;
                if (Mathf.Abs(wheel) > 0.01f)
                    scrollRect.verticalNormalizedPosition =
                        Mathf.Clamp01(scrollRect.verticalNormalizedPosition + wheel * 0.08f);
            }
        }

        /// <summary>Esc: from the card list back to the pack wall, from the wall it closes.</summary>
        public void Back()
        {
            if (mode == BrowserMode.Cards)
                ShowPacks();
            else
                Close();
        }

        public void Close()
        {
            if (closing)
                return;

            closing = true;

            if (ReferenceEquals(UIManager.InputBlocker, this))
                UIManager.InputBlocker = null;

            Destroy(gameObject);
        }

        #endregion
    }
}
