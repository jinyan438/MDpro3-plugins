using MDPro3.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.RpsVisualFix
{
    /// <summary>Short, input-blocking comparison of the two hands returned by the server.</summary>
    internal sealed class RpsResultOverlay : MonoBehaviour
    {
        private const float FadeInDuration = 0.14f;
        private const float HoldDuration = 1.8f;
        private const float FadeOutDuration = 0.22f;

        private RpsVisualFixFeature owner;
        private CanvasGroup canvasGroup;
        private float age;

        internal static RpsResultOverlay Show(RpsVisualFixFeature feature, int myHand, int opponentHand)
        {
            var ui = PluginGame.UI;
            if (ui == null || ui.popup == null)
            {
                PluginLog.Warn("rpsVisualFix could not show the result because the UI root is unavailable");
                return null;
            }

            var root = new GameObject("MDPro3Plugins_RpsResult", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasGroup), typeof(GraphicRaycaster));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(ui.popup, false);
            Stretch(rootRect);
            rootRect.SetAsLastSibling();

            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;

            var overlay = root.AddComponent<RpsResultOverlay>();
            overlay.owner = feature;
            overlay.canvasGroup = root.GetComponent<CanvasGroup>();
            overlay.canvasGroup.alpha = 0f;
            overlay.canvasGroup.blocksRaycasts = true;
            overlay.canvasGroup.interactable = true;
            overlay.Build(myHand, opponentHand);
            return overlay;
        }

        private void Build(int myHand, int opponentHand)
        {
            Outcome outcome = GetOutcome(myHand, opponentHand);
            Color accent = OutcomeColor(outcome);
            TMP_FontAsset font = PickFont();

            var backdrop = NewImage("Backdrop", transform, new Color(0f, 0.025f, 0.035f, 0.78f));
            Stretch(backdrop.rectTransform);

            var panel = NewImage("ResultPanel", transform, new Color(0.025f, 0.085f, 0.105f, 0.98f));
            SetRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(940f, 540f), Vector2.zero);

            var topLine = NewImage("OutcomeLine", panel.transform, accent);
            topLine.raycastTarget = false;
            topLine.rectTransform.anchorMin = new Vector2(0f, 1f);
            topLine.rectTransform.anchorMax = Vector2.one;
            topLine.rectTransform.offsetMin = new Vector2(0f, -8f);
            topLine.rectTransform.offsetMax = Vector2.zero;

            NewText("Outcome", panel.transform, font, 52f, TextAlignmentOptions.Center, accent,
                new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.96f), RpsResultLabels.Outcome(outcome));

            BuildHand(panel.transform, font, texture: owner.GetTexture(myHand),
                x: -225f, sideLabel: RpsResultLabels.You, handLabel: RpsResultLabels.Hand(myHand));
            BuildHand(panel.transform, font, texture: owner.GetTexture(opponentHand),
                x: 225f, sideLabel: RpsResultLabels.Opponent, handLabel: RpsResultLabels.Hand(opponentHand));

            NewText("Versus", panel.transform, font, 42f, TextAlignmentOptions.Center,
                new Color(0.82f, 0.88f, 0.9f, 0.9f), new Vector2(0.43f, 0.34f),
                new Vector2(0.57f, 0.62f), "VS");
        }

        private static void BuildHand(Transform parent, TMP_FontAsset font, Texture texture, float x,
            string sideLabel, string handLabel)
        {
            var side = NewText("Side", parent, font, 30f, TextAlignmentOptions.Center,
                new Color(0.78f, 0.86f, 0.89f, 1f), Vector2.zero, Vector2.zero, sideLabel);
            SetRect(side.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300f, 50f), new Vector2(x, 140f));

            var icon = NewRawImage("Hand", parent, texture);
            SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(240f, 240f), new Vector2(x, 0f));

            var hand = NewText("HandName", parent, font, 34f, TextAlignmentOptions.Center,
                Color.white, Vector2.zero, Vector2.zero, handLabel);
            SetRect(hand.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300f, 56f), new Vector2(x, -155f));
        }

        private void Update()
        {
            age += Time.unscaledDeltaTime;
            if (age < FadeInDuration)
            {
                canvasGroup.alpha = age / FadeInDuration;
                return;
            }

            float fadeOutStart = FadeInDuration + HoldDuration;
            if (age < fadeOutStart)
            {
                canvasGroup.alpha = 1f;
                return;
            }

            canvasGroup.alpha = 1f - Mathf.Clamp01((age - fadeOutStart) / FadeOutDuration);
            if (age >= fadeOutStart + FadeOutDuration)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (owner != null)
                owner.ResultOverlayClosed(this);
        }

        internal static Outcome GetOutcome(int myHand, int opponentHand)
        {
            if (myHand == opponentHand)
                return Outcome.Draw;
            if ((myHand == 1 && opponentHand == 2)
                || (myHand == 2 && opponentHand == 3)
                || (myHand == 3 && opponentHand == 1))
                return Outcome.Lose;
            return Outcome.Win;
        }

        private static Color OutcomeColor(Outcome outcome)
        {
            switch (outcome)
            {
                case Outcome.Win: return new Color(0.54f, 1f, 0.24f, 1f);
                case Outcome.Lose: return new Color(1f, 0.34f, 0.42f, 1f);
                default: return new Color(1f, 0.78f, 0.25f, 1f);
            }
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

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static RawImage NewRawImage(string name, Transform parent, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font,
            float size, TextAlignmentOptions alignment, Color color, Vector2 anchorMin,
            Vector2 anchorMax, string value)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        internal enum Outcome
        {
            Win,
            Lose,
            Draw
        }
    }

    internal static class RpsResultLabels
    {
        internal static string You => Pick("You", "Tu", "Voce", "Vous", "Du", "Tu",
            "\u3042\u306a\u305f", "\ub098", "\u6211\u65b9", "\u6211\u65b9");

        internal static string Opponent => Pick("Opponent", "Rival", "Adversario", "Adversaire",
            "Gegner", "Avversario", "\u76f8\u624b", "\uc0c1\ub300", "\u5c0d\u65b9", "\u5bf9\u65b9");

        internal static string Outcome(RpsResultOverlay.Outcome outcome)
        {
            switch (outcome)
            {
                case RpsResultOverlay.Outcome.Win:
                    return Pick("YOU WIN", "VICTORIA", "VITORIA", "VICTOIRE", "SIEG", "VITTORIA",
                        "\u52dd\u5229", "\uc2b9\ub9ac", "\u52dd\u5229", "\u80dc\u5229");
                case RpsResultOverlay.Outcome.Lose:
                    return Pick("YOU LOSE", "DERROTA", "DERROTA", "DEFAITE", "NIEDERLAGE", "SCONFITTA",
                        "\u6557\u5317", "\ud328\ubc30", "\u843d\u6557", "\u843d\u8d25");
                default:
                    return Pick("DRAW", "EMPATE", "EMPATE", "EGALITE", "UNENTSCHIEDEN", "PAREGGIO",
                        "\u5f15\u304d\u5206\u3051", "\ubb34\uc2b9\ubd80", "\u5e73\u5c40", "\u5e73\u5c40");
            }
        }

        internal static string Hand(int hand)
        {
            switch (hand)
            {
                case 1:
                    return Pick("Scissors", "Tijeras", "Tesoura", "Ciseaux", "Schere", "Forbici",
                        "\u30c1\u30e7\u30ad", "\uac00\uc704", "\u526a\u5200", "\u526a\u5200");
                case 2:
                    return Pick("Rock", "Piedra", "Pedra", "Pierre", "Stein", "Sasso",
                        "\u30b0\u30fc", "\ubc14\uc704", "\u77f3\u982d", "\u77f3\u5934");
                default:
                    return Pick("Paper", "Papel", "Papel", "Papier", "Papier", "Carta",
                        "\u30d1\u30fc", "\ubcf4", "\u5e03", "\u5e03");
            }
        }

        private static string Pick(string en, string es, string pt, string fr, string de, string it,
            string ja, string ko, string zh, string zhHans)
        {
            switch (Language.GetConfig())
            {
                case Language.English: return en;
                case Language.Spanish: return es;
                case Language.Portuguese: return pt;
                case Language.French: return fr;
                case Language.German: return de;
                case Language.Italian: return it;
                case Language.Japanese: return ja;
                case Language.Korean: return ko;
                case Language.SimplifiedChinese: return zhHans;
                case Language.TraditionalChinese: return zh;
                default: return zhHans;
            }
        }
    }
}
