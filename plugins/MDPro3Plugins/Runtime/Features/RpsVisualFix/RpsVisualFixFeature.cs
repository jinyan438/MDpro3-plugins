using MDPro3.UI.Popup;
using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.RpsVisualFix
{
    /// <summary>
    /// Keeps the rock-paper-scissors choices visible when the optional Picture/DIY images are
    /// missing. The game starts all three RawImages fully transparent and only reveals them after
    /// loading those files, so a missing deployment asset otherwise leaves clickable blank space.
    /// </summary>
    public sealed class RpsVisualFixFeature : PluginFeature
    {
        public const string FeatureId = "rpsVisualFix";

        public override string Id => FeatureId;

        public override string DisplayName => "Rock-paper-scissors visuals and result";

        private Texture2D rockTexture;
        private Texture2D paperTexture;
        private Texture2D scissorsTexture;
        private RpsResultPacketObserver packetObserver;
        private RpsResultOverlay resultOverlay;

        public override void Enable()
        {
            PluginEvents.PopupChanged += OnPopupChanged;
            EnsurePacketObserver();
            Log("enabled");
        }

        public override void Disable()
        {
            PluginEvents.PopupChanged -= OnPopupChanged;
            if (packetObserver != null)
                Object.Destroy(packetObserver);
            packetObserver = null;

            if (resultOverlay != null)
                Object.Destroy(resultOverlay.gameObject);
            resultOverlay = null;

            DestroyTexture(ref rockTexture);
            DestroyTexture(ref paperTexture);
            DestroyTexture(ref scissorsTexture);
        }

        public override void Tick()
        {
            if (packetObserver == null)
                EnsurePacketObserver();
        }

        private void OnPopupChanged(Popup popup)
        {
            var rpsPopup = popup as PopupRockPaperScissors;
            if (rpsPopup == null)
                return;

            EnsureTextures();

            var images = rpsPopup.GetComponentsInChildren<RawImage>(true);
            int restored = 0;
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                if (image == null || image.color.a > 0.01f)
                    continue;

                Texture2D fallback = null;
                switch (image.gameObject.name)
                {
                    case "RockButton":
                        fallback = rockTexture;
                        break;
                    case "PaperButton":
                        fallback = paperTexture;
                        break;
                    case "ScissorsButton":
                        fallback = scissorsTexture;
                        break;
                }

                if (fallback == null)
                    continue;

                image.texture = fallback;
                image.color = Color.white;
                restored++;
            }

            if (restored > 0)
                Log("restored " + restored + " invisible choice image(s)");
        }

        internal void ShowResult(int myHand, int opponentHand)
        {
            if (!IsValidHand(myHand) || !IsValidHand(opponentHand))
            {
                PluginLog.Warn("ignored invalid rock-paper-scissors result: " + myHand + ", " + opponentHand);
                return;
            }

            EnsureTextures();
            if (resultOverlay != null)
                Object.Destroy(resultOverlay.gameObject);

            resultOverlay = RpsResultOverlay.Show(this, myHand, opponentHand);
            if (resultOverlay != null)
                Log("showing result: me=" + HandName(myHand) + ", opponent=" + HandName(opponentHand));
        }

        internal Texture2D GetTexture(int hand)
        {
            switch (hand)
            {
                case 1: return scissorsTexture;
                case 2: return rockTexture;
                case 3: return paperTexture;
                default: return null;
            }
        }

        internal void ResultOverlayClosed(RpsResultOverlay overlay)
        {
            if (ReferenceEquals(resultOverlay, overlay))
                resultOverlay = null;
        }

        private void EnsurePacketObserver()
        {
            var program = PluginGame.ProgramInstance;
            if (program == null)
                return;

            packetObserver = program.GetComponent<RpsResultPacketObserver>();
            if (packetObserver == null)
                packetObserver = program.gameObject.AddComponent<RpsResultPacketObserver>();
            packetObserver.Bind(this);
        }

        private void EnsureTextures()
        {
            if (rockTexture == null)
                rockTexture = RpsFallbackIcons.Create(RpsFallbackIcons.Choice.Rock);
            if (paperTexture == null)
                paperTexture = RpsFallbackIcons.Create(RpsFallbackIcons.Choice.Paper);
            if (scissorsTexture == null)
                scissorsTexture = RpsFallbackIcons.Create(RpsFallbackIcons.Choice.Scissors);
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
                Object.Destroy(texture);
            texture = null;
        }

        private static bool IsValidHand(int hand)
        {
            return hand >= 1 && hand <= 3;
        }

        private static string HandName(int hand)
        {
            switch (hand)
            {
                case 1: return "scissors";
                case 2: return "rock";
                case 3: return "paper";
                default: return "unknown";
            }
        }
    }

    /// <summary>Small built-in pictograms, independent of files beside the executable.</summary>
    internal static class RpsFallbackIcons
    {
        internal enum Choice
        {
            Rock,
            Paper,
            Scissors
        }

        private const int Size = 256;

        private static readonly Color32 Backdrop = new Color32(8, 29, 39, 245);
        private static readonly Color32 Ink = new Color32(4, 15, 20, 255);
        private static readonly Color32 Face = new Color32(236, 243, 245, 255);

        internal static Texture2D Create(Choice choice)
        {
            var pixels = new Color32[Size * Size];
            Color32 accent = GetAccent(choice);

            FillCircle(pixels, 128, 128, 116, accent);
            FillCircle(pixels, 128, 128, 107, Backdrop);

            switch (choice)
            {
                case Choice.Rock:
                    DrawRock(pixels, accent);
                    break;
                case Choice.Paper:
                    DrawPaper(pixels, accent);
                    break;
                default:
                    DrawScissors(pixels, accent);
                    break;
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
            {
                name = "MDPro3Plugins_RpsFallback_" + choice,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color32 GetAccent(Choice choice)
        {
            switch (choice)
            {
                case Choice.Rock:
                    return new Color32(244, 181, 62, 255);
                case Choice.Paper:
                    return new Color32(72, 205, 224, 255);
                default:
                    return new Color32(235, 92, 133, 255);
            }
        }

        private static void DrawRock(Color32[] pixels, Color32 accent)
        {
            var outline = new[]
            {
                new Vector2Int(55, 83), new Vector2Int(60, 126), new Vector2Int(79, 158),
                new Vector2Int(111, 184), new Vector2Int(151, 181), new Vector2Int(184, 153),
                new Vector2Int(199, 113), new Vector2Int(181, 76), new Vector2Int(148, 55),
                new Vector2Int(94, 58)
            };
            var rock = new[]
            {
                new Vector2Int(65, 88), new Vector2Int(70, 126), new Vector2Int(87, 151),
                new Vector2Int(115, 173), new Vector2Int(147, 170), new Vector2Int(174, 147),
                new Vector2Int(188, 114), new Vector2Int(173, 84), new Vector2Int(144, 66),
                new Vector2Int(99, 68)
            };

            FillPolygon(pixels, outline, Ink);
            FillPolygon(pixels, rock, Face);
            FillCircle(pixels, 101, 143, 10, accent);
            FillCircle(pixels, 151, 128, 8, accent);
            FillCapsule(pixels, 112, 133, 127, 111, 4, Ink);
            FillCapsule(pixels, 127, 111, 116, 88, 4, Ink);
            FillCapsule(pixels, 151, 151, 139, 127, 4, Ink);
        }

        private static void DrawPaper(Color32[] pixels, Color32 accent)
        {
            var outline = new[]
            {
                new Vector2Int(68, 50), new Vector2Int(68, 200),
                new Vector2Int(158, 200), new Vector2Int(190, 168),
                new Vector2Int(190, 50)
            };
            var sheet = new[]
            {
                new Vector2Int(78, 60), new Vector2Int(78, 190),
                new Vector2Int(153, 190), new Vector2Int(180, 163),
                new Vector2Int(180, 60)
            };
            var fold = new[]
            {
                new Vector2Int(153, 190), new Vector2Int(153, 163), new Vector2Int(180, 163)
            };

            FillPolygon(pixels, outline, Ink);
            FillPolygon(pixels, sheet, Face);
            FillPolygon(pixels, fold, accent);
            FillCapsule(pixels, 96, 139, 153, 139, 4, accent);
            FillCapsule(pixels, 96, 115, 162, 115, 4, accent);
            FillCapsule(pixels, 96, 91, 145, 91, 4, accent);
        }

        private static void DrawScissors(Color32[] pixels, Color32 accent)
        {
            FillCapsule(pixels, 98, 105, 177, 193, 11, Ink);
            FillCapsule(pixels, 98, 105, 177, 193, 6, Face);
            FillCapsule(pixels, 139, 105, 70, 194, 11, Ink);
            FillCapsule(pixels, 139, 105, 70, 194, 6, Face);

            FillCircle(pixels, 91, 76, 38, Ink);
            FillCircle(pixels, 91, 76, 29, accent);
            FillCircle(pixels, 91, 76, 17, Backdrop);
            FillCircle(pixels, 157, 76, 38, Ink);
            FillCircle(pixels, 157, 76, 29, accent);
            FillCircle(pixels, 157, 76, 17, Backdrop);

            FillCircle(pixels, 120, 119, 15, Ink);
            FillCircle(pixels, 120, 119, 8, accent);
        }

        private static void FillCircle(Color32[] pixels, int centerX, int centerY, int radius, Color32 color)
        {
            int radiusSquared = radius * radius;
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    int dx = x - centerX;
                    int dy = y - centerY;
                    if (dx * dx + dy * dy <= radiusSquared)
                        SetPixel(pixels, x, y, color);
                }
            }
        }

        private static void FillCapsule(Color32[] pixels, int x1, int y1, int x2, int y2, int radius, Color32 color)
        {
            int minX = Mathf.Min(x1, x2) - radius;
            int maxX = Mathf.Max(x1, x2) + radius;
            int minY = Mathf.Min(y1, y2) - radius;
            int maxY = Mathf.Max(y1, y2) + radius;
            float vx = x2 - x1;
            float vy = y2 - y1;
            float lengthSquared = vx * vx + vy * vy;
            float radiusSquared = radius * radius;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float t = lengthSquared <= 0f ? 0f : ((x - x1) * vx + (y - y1) * vy) / lengthSquared;
                    t = Mathf.Clamp01(t);
                    float dx = x - (x1 + t * vx);
                    float dy = y - (y1 + t * vy);
                    if (dx * dx + dy * dy <= radiusSquared)
                        SetPixel(pixels, x, y, color);
                }
            }
        }

        private static void FillPolygon(Color32[] pixels, Vector2Int[] points, Color32 color)
        {
            int minX = Size - 1;
            int maxX = 0;
            int minY = Size - 1;
            int maxY = 0;
            for (int i = 0; i < points.Length; i++)
            {
                minX = Mathf.Min(minX, points[i].x);
                maxX = Mathf.Max(maxX, points[i].x);
                minY = Mathf.Min(minY, points[i].y);
                maxY = Mathf.Max(maxY, points[i].y);
            }

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (Contains(points, x + 0.5f, y + 0.5f))
                        SetPixel(pixels, x, y, color);
        }

        private static bool Contains(Vector2Int[] points, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                var a = points[i];
                var b = points[j];
                if ((a.y > y) != (b.y > y)
                    && x < (float)(b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static void SetPixel(Color32[] pixels, int x, int y, Color32 color)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size)
                return;
            pixels[y * Size + x] = color;
        }
    }
}
