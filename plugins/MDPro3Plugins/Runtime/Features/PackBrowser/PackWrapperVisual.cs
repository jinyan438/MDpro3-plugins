using MDPro3.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.PackBrowser
{
    /// <summary>A foil booster wrapper around the existing cover card's artwork.</summary>
    public sealed class PackWrapperVisual : MonoBehaviour
    {
        public const string ObjectName = "PackWrapper";
        public const float Width = 200f;
        public const float Height = 420f;
        public const float TopInset = 8f;

        private const float ArtWidth = Width * 479f / 483f;
        private const float ArtHeight = Height * 969f / 1004f;
        private const float ArtTop = Height * 2f / 1004f;
        private const string ResourcePath = "MDPro3Plugins/PackBrowser/";
        private static Texture2D wrapperTexture;
        private static Texture2D glowTexture;
        private static Texture2D foilTexture;
        private static Texture2D glossTexture;

        private CanvasGroup glow;
        private bool highlighted;

        public ArtRawImageHandler Art { get; private set; }

        public static PackWrapperVisual Create(Transform parent)
        {
            var host = new GameObject(ObjectName, typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(Width, Height);
            rect.anchoredPosition = new Vector2(0f, -TopInset);
            var visual = host.AddComponent<PackWrapperVisual>();
            visual.Build();
            return visual;
        }

        private void Build()
        {
            if (wrapperTexture == null)
                wrapperTexture = Resources.Load<Texture2D>(ResourcePath + "Wrapper");
            if (glowTexture == null)
                glowTexture = Resources.Load<Texture2D>(ResourcePath + "Glow");
            if (foilTexture == null)
                foilTexture = Resources.Load<Texture2D>(ResourcePath + "Foil");
            if (glossTexture == null)
                glossTexture = Resources.Load<Texture2D>(ResourcePath + "Gloss");

            var halo = NewImage("GoldGlow", transform, Width + 40f, Height + 40f, -20f);
            halo.texture = glowTexture;
            glow = halo.gameObject.AddComponent<CanvasGroup>();
            glow.alpha = 0f;
            glow.blocksRaycasts = false;
            glow.interactable = false;

            var backing = NewImage("Foil", transform, Width, Height, 0f);
            backing.texture = foilTexture;
            var body = backing.rectTransform;

            var picture = NewImage(PackTileItem.ArtObjectName, body, ArtWidth, ArtHeight, ArtTop);
            // ArtRawImageHandler supplies square artwork. Crop the sides to fill the tall bag
            // without stretching the illustration; its card code and loading/cache stay intact.
            float uvWidth = ArtWidth / ArtHeight;
            picture.uvRect = new Rect((1f - uvWidth) * 0.5f, 0f, uvWidth, 1f);
            Art = picture.gameObject.AddComponent<ArtRawImageHandler>();

            var gloss = NewImage("FoilHighlights", body, Width, Height, 0f);
            gloss.texture = glossTexture;
            var frame = NewImage("PrintedWrapper", body, Width, Height, 0f);
            frame.texture = wrapperTexture;
        }

        private static RawImage NewImage(string name, Transform parent, float width, float height, float top)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(width, height);
            var image = go.GetComponent<RawImage>();
            image.raycastTarget = false;
            return image;
        }

        public void SetHighlighted(bool value, bool immediate = false)
        {
            highlighted = value;
            if (immediate && glow != null)
                glow.alpha = value ? 1f : 0f;
        }

        private void Update()
        {
            // Only instantiated, visible/recycled tiles animate. No per-frame allocation.
            if (!highlighted && glow.alpha == 0f)
                return;
            float target = highlighted ? 0.91f + 0.09f * Mathf.Sin(Time.unscaledTime * 3f) : 0f;
            glow.alpha = Mathf.MoveTowards(glow.alpha, target, Time.unscaledDeltaTime * 7f);
        }

        private void OnDisable()
        {
            SetHighlighted(false, true);
        }
    }
}
