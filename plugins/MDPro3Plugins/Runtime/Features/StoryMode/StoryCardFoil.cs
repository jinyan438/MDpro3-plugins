using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // A continuous foil sheet over the card face, clipped only at its rounded outer edge.
    internal sealed class StoryCardFoil : MaskableGraphic
    {
        private const int Grid = 24;
        private const int TextureSize = 512;
        private static Texture2D foil;
        internal bool HasRenderedFoil { get; private set; }
        public override Texture mainTexture => foil != null ? foil : Texture2D.whiteTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (foil == null) foil = CreateTexture();
            SetMaterialDirty(); SetVerticesDirty();
        }

        private static Texture2D CreateTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)
            { name = "Story SR full-face foil", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color32[TextureSize * TextureSize];
            var random = new System.Random(713);
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    int index = y * TextureSize + x;
                    float u = (x + .5f) / TextureSize, v = (y + .5f) / TextureSize;
                    // Measure the corner radius in card-width units, accounting for the 59:86 aspect ratio.
                    const float aspect = 86f / 59, radius = .03f, inset = .002f;
                    float dx = Mathf.Abs(u - .5f) - (.5f - radius - inset);
                    float dy = Mathf.Abs(v - .5f) * aspect - (.5f * aspect - radius - inset);
                    float distance = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude
                        + Mathf.Min(Mathf.Max(dx, dy), 0) - radius;
                    float edge = Mathf.Clamp01(-distance * TextureSize);
                    // Stable micro-grain, with larger foil irregularities underneath the moving reflection.
                    float grain = (float)random.NextDouble();
                    float brushed = Mathf.PerlinNoise(x * .09f, y * .035f);
                    float alpha = edge * (.2f + .55f * grain + .25f * brushed);
                    pixels[index] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            HasRenderedFoil = false;
            if (foil == null) return;
            var rect = GetPixelAdjustedRect();
            float tilt = Mathf.Sin(Time.unscaledTime * .62f);
            for (int y = 0; y <= Grid; y++)
                for (int x = 0; x <= Grid; x++)
                {
                    float u = x / (float)Grid, v = y / (float)Grid;
                    float diagonal = u + v * .42f + .035f * Mathf.Sin(v * 19 + u * 7);
                    float distance = diagonal - (.7f + tilt * .5f);
                    float reflection = Mathf.Exp(-distance * distance * 9);
                    float glint = Mathf.Exp(-distance * distance * 160);
                    float hue = Mathf.Repeat(diagonal * 1.25f - tilt * .32f + .12f, 1);
                    var tint = new Color(Spectrum(hue), Spectrum(hue + 2f / 3), Spectrum(hue + 1f / 3));
                    tint = Color.Lerp(tint, Color.white, glint * .5f);
                    tint.a = color.a * (.075f + .42f * reflection + .15f * glint);
                    mesh.AddVert(new Vector2(rect.xMin + u * rect.width, rect.yMin + v * rect.height), tint, new Vector2(u, v));
                }
            for (int y = 0; y < Grid; y++)
                for (int x = 0; x < Grid; x++)
                {
                    int a = y * (Grid + 1) + x, b = a + Grid + 1;
                    mesh.AddTriangle(a, b, a + 1);
                    mesh.AddTriangle(a + 1, b, b + 1);
                }
            HasRenderedFoil = true;
        }

        private static float Spectrum(float hue) => .37f + .63f * Mathf.Clamp01(Mathf.Abs(Mathf.Repeat(hue, 1) * 6 - 3) - 1);

        private void LateUpdate()
        {
            if (!canvasRenderer.cull && color.a > 0) SetVerticesDirty();
        }
    }
}
