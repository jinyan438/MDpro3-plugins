using System;
using MDPro3.UI;
using MDPro3.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal static class StoryUI
    {
        internal static readonly Color Panel = new Color(0.065f, 0.14f, 0.20f, 1f);
        internal static readonly Color Accent = new Color(0.75f, 0.93f, 0.08f, 1f);
        internal static TMP_FontAsset Font => Language.GetConfig() == Language.SimplifiedChinese
            ? PluginGame.UI.cnMenuTmpFont ?? PluginGame.UI.tmpFont : PluginGame.UI.tmpFont;

        internal static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h, float inset = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x, y); rect.anchorMax = new Vector2(x + w, y + h);
            rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        internal static RectTransform Box(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var rect = Rect(name, parent, x, y, w, h);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        internal static TextMeshProUGUI Text(Transform parent, string value, float x, float y, float w, float h,
            float size = 26, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var rect = Rect("Text", parent, x, y, w, h, 3);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = Font; text.text = value; text.color = Color.white;
            text.fontSize = size; text.alignment = align; text.raycastTarget = false;
            text.enableAutoSizing = true; text.fontSizeMin = Math.Min(size, 12); text.fontSizeMax = size;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            return text;
        }

        internal static Button Button(Transform parent, string label, float x, float y, float w, float h, Action action, bool accent = false)
        {
            var rect = Box(label, parent, x, y, w, h, accent ? Accent : Panel);
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.75f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
            button.colors = colors;
            button.targetGraphic = rect.GetComponent<Image>();
            var text = Text(rect, label, .03f, .03f, .94f, .94f, 25, TextAlignmentOptions.Center);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            if (accent) text.color = Color.black;
            button.onClick.AddListener(() =>
            {
                try { AudioManager.PlaySE("SE_MENU_DECIDE"); action?.Invoke(); }
                catch (Exception ex) { MessageManager.Cast("故事模式：" + ex.Message); PluginLog.Error("storyMode UI: " + ex); }
            });
            return button;
        }

        internal static TMP_InputField Input(Transform parent, string placeholder, float x, float y, float w, float h)
        {
            var rect = Box("Search", parent, x, y, w, h, Panel);
            var area = Rect("Text Area", rect, .02f, .04f, .96f, .92f);
            area.gameObject.AddComponent<RectMask2D>();
            var text = Text(area, "", 0, 0, 1, 1);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var hint = Text(area, placeholder, 0, 0, 1, 1);
            hint.color = new Color(.6f, .7f, .75f);
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area; input.textComponent = text; input.placeholder = hint;
            input.fontAsset = Font; input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 120;
            return input;
        }

        internal static RectTransform Scroll(Transform parent, float x, float y, float w, float h)
        {
            var root = Rect("Scroll", parent, x, y, w, h);
            var view = Box("Viewport", root, 0, 0, 1, 1, new Color(.02f, .05f, .08f));
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", view, 0, 1, 1, 0);
            content.pivot = new Vector2(.5f, 1);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 45; scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        internal static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false); UnityEngine.Object.Destroy(child);
            }
        }

        internal static void CardPicture(Transform parent, int id, float x, float y, float w, float h)
        {
            var rect = Rect("Card", parent, x, y, w, h);
            var fit = rect.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 723f / 1054f;
            rect.gameObject.AddComponent<RawImage>().raycastTarget = false;
            rect.gameObject.AddComponent<CardRawImageHandler>().SetCard(id);
        }

        internal static void CharacterPicture(Transform parent, string address, float x, float y, float w, float h)
        {
            var rect = Rect("Portrait", parent, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.preserveAspect = true; image.raycastTarget = false; image.color = Color.clear;
            rect.gameObject.AddComponent<StorySprite>().Load(address, image);
        }
    }

    internal sealed class StorySprite : MonoBehaviour
    {
        private AsyncOperationHandle<Sprite> handle;
        internal void Load(string address, Image image)
        {
            bool found = false;
            foreach (var locator in Addressables.ResourceLocators)
                if (locator.Locate(address, typeof(Sprite), out var locations) && locations.Count > 0) { found = true; break; }
            if (!found) return;
            handle = Addressables.LoadAssetAsync<Sprite>(address);
            handle.Completed += result =>
            {
                if (this == null || image == null || result.Status != AsyncOperationStatus.Succeeded) return;
                image.sprite = result.Result; image.color = Color.white;
            };
        }
        private void OnDestroy() { if (handle.IsValid()) Addressables.Release(handle); }
    }
}
