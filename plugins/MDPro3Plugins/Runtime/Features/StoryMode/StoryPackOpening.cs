using System;
using System.Collections.Generic;
using MDPro3.Duel.YGOSharp;
using MDPro3.Plugins.Features.PackBrowser;
using MDPro3.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Presentation only: Buy has already committed the exact cards before this is created.
    // Skipping, a lost scene or destruction must never draw cards or write the story save.
    internal sealed class StoryPackOpening : MonoBehaviour
    {
        internal enum Phase { Arrival, Sealed, Opening, Reveal, Complete }
        private static readonly Color Cyan = new Color(.25f, .85f, 1f);
        private static readonly Color Gold = new Color(1f, .77f, .32f);
        private static readonly Color Muted = new Color(.54f, .65f, .76f);
        private readonly List<CardView> views = new List<CardView>();
        private List<int> cards;
        private List<StoryCard> acquiredCards;
        private MonoBehaviour previousBlocker;
        private CanvasGroup browserGroup;
        private bool oldInteractable, oldRaycasts, restored, closed;
        private GameObject previousSelection;
        private Action finished;
        private RectTransform stage, pack, lid, body, cut;
        private CanvasGroup packAlpha, actionAlpha;
        private TextMeshProUGUI heading, subtitle, hint, result, actionText;
        private Button action, skip;
        private StoryOpeningGraphic halo, orbit, rays, dust, burst, packShine;
        private Image flash;
        private float elapsed, clock;
        private int lastSound = -1;
        internal Phase CurrentPhase { get; private set; }
        internal int RevealedCount { get; private set; }

        private sealed class CardView
        {
            internal RectTransform Root, Face, Back;
            internal CanvasGroup Alpha, Label;
            internal StoryOpeningGraphic Halo, Shine;
            internal Button Button;
            internal Color Color;
        }

        internal static StoryPackOpening Open(Transform parent, PackEntry entry, List<StoryCard> drawn,
            StorySave previouslyOwned, int wrapperColor, Action onFinished)
        {
            if (drawn == null || drawn.Count != 3) throw new ArgumentException("Opening requires the three purchased cards.");
            var root = StoryUI.Box("StoryPackOpening", parent, 0, 0, 1, 1, new Color(.006f, .012f, .028f));
            var opening = root.gameObject.AddComponent<StoryPackOpening>();
            try
            {
                opening.cards = new List<int>();
                foreach (var card in drawn) opening.cards.Add(card.id);
                opening.acquiredCards = drawn;
                opening.finished = onFinished;
                opening.previousBlocker = UIManager.InputBlocker;
                opening.browserGroup = parent.GetComponent<CanvasGroup>() ?? parent.gameObject.AddComponent<CanvasGroup>();
                opening.oldInteractable = opening.browserGroup.interactable;
                opening.oldRaycasts = opening.browserGroup.blocksRaycasts;
                opening.browserGroup.interactable = false;
                opening.browserGroup.blocksRaycasts = false;
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.ignoreParentGroups = true;
                if (EventSystem.current != null)
                {
                    opening.previousSelection = EventSystem.current.currentSelectedGameObject;
                    EventSystem.current.SetSelectedGameObject(null);
                }
                opening.Build(entry, drawn, previouslyOwned, wrapperColor);
                UIManager.InputBlocker = opening;
                opening.SetPhase(Phase.Arrival);
                return opening;
            }
            catch
            {
                opening.RestoreInput();
                root.gameObject.SetActive(false);
                Destroy(root.gameObject);
                throw;
            }
        }

        private void Build(PackEntry entry, List<StoryCard> drawn, StorySave owned, int wrapperColor)
        {
            stage = Fixed("Stage", transform, 0, 0, 1600, 900);
            halo = Effect("Atmosphere", stage, StoryOpeningGraphic.Shape.Halo, 0, 40, 1450, 1450, new Color(.04f, .26f, .39f, .7f));
            rays = Effect("LightRays", stage, StoryOpeningGraphic.Shape.Rays, 0, 40, 1350, 1350, Cyan);
            orbit = Effect("SummoningRings", stage, StoryOpeningGraphic.Shape.Orbit, 0, 40, 740, 740, new Color(.21f, .7f, .8f, .38f));
            dust = Effect("Stardust", stage, StoryOpeningGraphic.Shape.Dust, 0, 0, 1600, 900, new Color(.54f, .86f, 1, .8f));
            Line(stage, -676, 392, 80, 2, Cyan);
            Label(stage, "STORY  /  CARD ACQUISITION", -425, 392, 400, 24, 16, Muted, TextAlignmentOptions.MidlineLeft);
            heading = Label(stage, "开启新的可能", 0, 328, 750, 64, 42, Color.white);
            subtitle = Label(stage, entry.Name, 0, 278, 1000, 38, 23, Muted);
            Label(stage, "03  CARDS", 635, 392, 180, 24, 17, Muted, TextAlignmentOptions.MidlineRight);

            // Two clipped copies of the shop's actual foil wrapper separate along the seal.
            pack = Fixed("SealedPack", stage, 0, 25, 248, 520);
            packAlpha = pack.gameObject.AddComponent<CanvasGroup>();
            body = WrapperSlice(pack, "WrapperBody", 0, -.5f, 1, .9f, entry, wrapperColor);
            lid = WrapperSlice(pack, "WrapperSeal", 0, .4f, 1, .1f, entry, wrapperColor);
            var shineClip = StoryUI.Rect("FoilShineMask", pack, 0, 0, 1, 1);
            shineClip.gameObject.AddComponent<RectMask2D>();
            packShine = Effect("FoilShine", shineClip, StoryOpeningGraphic.Shape.Shine, 0, 0, 248, 520, Color.white);
            cut = Line(pack, 0, 208, 300, 3, Color.white);
            cut.gameObject.SetActive(false);

            burst = Effect("OpeningBurst", stage, StoryOpeningGraphic.Shape.Halo, 0, 150, 1100, 1100, Color.clear);
            // Load the full card art during the sealed-pack shot, before it is revealed.
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < cards.Count; i++)
            {
                int code = cards[i];
                string key = code + ":" + drawn[i].rarity;
                int count = counts.TryGetValue(key, out int seen) ? seen : owned.Owned(code, drawn[i].rarity);
                counts[key] = count + 1;
                views.Add(BuildCard(i, drawn[i], count == 0, count + 1));
            }

            var footer = Fixed("OpeningFooter", stage, 0, -289, 1300, 66);
            hint = Label(footer, "点击下方按钮，开启卡包", 0, 5, 1100, 35, 23, new Color(.8f, .89f, .94f));
            result = Label(footer, "", 0, -27, 1100, 27, 18, Muted);
            action = CreateButton(stage, "OpeningContinue", "开启卡包", 0, -368, 280, 62, Advance, true);
            actionAlpha = action.gameObject.AddComponent<CanvasGroup>();
            actionText = action.GetComponentInChildren<TextMeshProUGUI>();
            skip = CreateButton(stage, "OpeningSkip", "跳过动画  ›", 651, -371, 190, 46, Skip, false);
            Line(stage, -640, -371, 65, 1, Muted);
            Label(stage, "STORY MODE", -509, -371, 180, 24, 15, Muted, TextAlignmentOptions.MidlineLeft);
            var flashRect = StoryUI.Box("SoftFlash", stage, 0, 0, 1, 1, Color.clear);
            flash = flashRect.GetComponent<Image>(); flash.raycastTarget = false;
            Resize();
        }

        private RectTransform WrapperSlice(RectTransform parent, string name, float x, float y, float w, float h,
            PackEntry entry, int wrapperColor)
        {
            var slice = Fixed(name, parent, x, (y + h * .5f) * 520, w * 248, h * 520);
            // Stencil clipping follows the floating/tearing rotation, unlike RectMask2D.
            slice.gameObject.AddComponent<Image>().raycastTarget = false;
            slice.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var wrapper = PackWrapperVisual.Create(slice);
            var rect = (RectTransform)wrapper.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, (1 - (y + h + .5f)) * 520);
            rect.localScale = new Vector3(248 / PackWrapperVisual.Width, 520 / PackWrapperVisual.Height, 1);
            wrapper.SetColor(wrapperColor);
            wrapper.Art.SetArt(entry.CoverCard);
            return slice;
        }

        private CardView BuildCard(int index, StoryCard acquired, bool isNew, int count)
        {
            int code = acquired.id;
            var data = CardsManager.GetCardRaw(code);
            Color tint = StoryCardFinish.Tint(acquired.rarity);
            var root = Fixed("ObtainedCard" + index, stage, 0, 25, 240, 350);
            var view = new CardView { Root = root, Color = tint };
            view.Alpha = root.gameObject.AddComponent<CanvasGroup>();
            view.Alpha.alpha = 0;
            view.Halo = Effect("CardAura", root, StoryOpeningGraphic.Shape.Halo, 0, 0, 640, 640, new Color(tint.r, tint.g, tint.b, .45f));
            view.Halo.Rainbow = acquired.rarity == StoryRarity.UR || acquired.rarity == StoryRarity.MR;
            var border = StoryUI.Box("CardEdge", root, -.014f, -.01f, 1.028f, 1.02f, tint);
            border.GetComponent<Image>().raycastTarget = false;
            view.Back = StoryUI.Box("CardBack", root, 0, 0, 1, 1, new Color(.035f, .055f, .09f));
            CardImage(view.Back, 0);
            view.Face = StoryUI.Box("CardFace", root, 0, 0, 1, 1, new Color(.04f, .08f, .12f));
            CardImage(view.Face, code);
            StoryCardFinish.Apply(view.Face.GetComponentInChildren<CardRawImageHandler>(), acquired.rarity);
            // A clipped foil sweep crosses the face once when this card turns over.
            var shineMask = StoryUI.Rect("CardShineMask", view.Face, 0, 0, 1, 1);
            shineMask.gameObject.AddComponent<RectMask2D>();
            view.Shine = Effect("CardShine", shineMask, StoryOpeningGraphic.Shape.Shine, 0, 0, 240, 350, Color.white);
            view.Face.gameObject.SetActive(false);
            var label = Fixed("CardCaption", root, 0, -220, 330, 70);
            view.Label = label.gameObject.AddComponent<CanvasGroup>(); view.Label.alpha = 0;
            Label(label, data?.Name ?? code.ToString(), 0, 5, 326, 37, 24, Color.white);
            Label(label, isNew ? "NEW  ·  首次获得" : "持有 ×" + count,
                0, -32, 320, 26, 17, isNew ? Gold : Muted);
            // The result is inspectable with the native card-detail popup.
            view.Button = root.gameObject.AddComponent<Button>();
            view.Button.targetGraphic = border.GetComponent<Image>();
            border.GetComponent<Image>().raycastTarget = true;
            view.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            view.Button.interactable = false;
            view.Button.onClick.AddListener(() =>
            {
                if (CurrentPhase == Phase.Complete) StoryCardFinish.ShowDetail(acquiredCards[index]);
            });
            return view;
        }

        private static void CardImage(Transform parent, int code)
        {
            var rect = StoryUI.Rect("Art", parent, 0, 0, 1, 1);
            rect.gameObject.AddComponent<RawImage>().raycastTarget = false;
            rect.gameObject.AddComponent<CardRawImageHandler>().SetCard(code);
        }

        private void Update()
        {
            if (closed) return;
            if (!PluginGame.IsReady || !(PluginGame.CurrentServant is MDPro3.Servant.MainMenu)) { Close(false); return; }
            Resize();
            // Native card-detail popups retain exclusive input until they are dismissed.
            if (PluginGame.CurrentPopup != null || UIManager.InputBlocker != this) return;
            clock += Time.unscaledDeltaTime;
            elapsed += Time.unscaledDeltaTime;
            if (UserInput.WasCancelPressed || UserInput.MouseRightDown)
            {
                if (CurrentPhase == Phase.Complete) Close(true); else Skip();
                return;
            }
            // No selected uGUI button also receives this submit; pointer navigation is disabled.
            if (UserInput.WasSubmitPressed) Advance();
            TickVisuals();
        }

        private void TickVisuals()
        {
            dust.Animate(clock);
            orbit.Animate(clock);
            rays.Animate(clock, CurrentPhase == Phase.Opening ? 2 : .55f);
            switch (CurrentPhase)
            {
                case Phase.Arrival:
                    float entrance = Ease(elapsed / 1.05f);
                    pack.anchoredPosition = new Vector2(0, Mathf.Lerp(-160, -15, entrance));
                    pack.localScale = Vector3.one * Mathf.Lerp(.65f, .92f, entrance);
                    pack.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-12, -3, entrance));
                    packAlpha.alpha = entrance;
                    if (elapsed >= 1.05f) SetPhase(Phase.Sealed);
                    break;
                case Phase.Sealed:
                    pack.anchoredPosition = new Vector2(0, -15 + Mathf.Sin(clock * 1.6f) * 7);
                    pack.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(clock * 1.2f) * 2);
                    packShine.Animate(clock * .28f);
                    break;
                case Phase.Opening:
                    float charge = Mathf.Clamp01(elapsed / .85f);
                    pack.localRotation = Quaternion.Euler(0, 0, 0);
                    pack.localScale = Vector3.one * (.92f + Mathf.Sin(charge * Mathf.PI) * .035f);
                    orbit.transform.localScale = Vector3.one * Mathf.Lerp(1, .82f, charge);
                    halo.color = Color.Lerp(new Color(.04f, .26f, .39f, .7f), new Color(.15f, .5f, .65f, .95f), charge);
                    packShine.Animate(elapsed * .7f);
                    cut.gameObject.SetActive(elapsed > .55f && elapsed < 1.28f);
                    cut.localScale = new Vector3(Mathf.Clamp01((elapsed - .55f) / .28f), 1, 1);
                    if (elapsed > .85f)
                    {
                        if (lastSound < 0) { lastSound = 0; AudioManager.PlaySE("SE_CARD_MOVE_01"); }
                        float tear = Ease((elapsed - .85f) / .75f);
                        lid.anchoredPosition = new Vector2(tear * 190, 234 + tear * 160);
                        lid.localRotation = Quaternion.Euler(0, 0, -tear * 28);
                        body.anchoredPosition = new Vector2(-tear * 35, -26 - tear * 240);
                        packAlpha.alpha = 1 - tear;
                        burst.color = new Color(.55f, .91f, 1, Mathf.Sin(tear * Mathf.PI) * .85f);
                        burst.transform.localScale = Vector3.one * (.4f + tear);
                        flash.color = new Color(.7f, .9f, 1, Mathf.Max(0, 1 - (elapsed - .85f) / .4f) * .22f);
                    }
                    if (elapsed >= 1.65f) SetPhase(Phase.Reveal);
                    break;
                case Phase.Reveal:
                    for (int i = 0; i < views.Count; i++)
                    {
                        var view = views[i];
                        float spread = Ease((elapsed - i * .09f) / .7f);
                        view.Alpha.alpha = spread;
                        view.Root.anchoredPosition = new Vector2((i - 1) * 360 * spread, Mathf.Lerp(-55, 35, spread));
                        float flipTime = elapsed - 1f - i * .85f;
                        float flip = Mathf.Clamp01(flipTime / .6f);
                        bool front = flip >= .5f;
                        view.Face.gameObject.SetActive(front); view.Back.gameObject.SetActive(!front);
                        view.Root.localScale = new Vector3(Mathf.Max(.012f, Mathf.Abs(Mathf.Cos(flip * Mathf.PI))), 1, 1)
                            * (1 + .055f * Mathf.Sin(flip * Mathf.PI));
                        view.Label.alpha = Mathf.Clamp01((flipTime - .3f) / .4f);
                        view.Halo.color = new Color(view.Color.r, view.Color.g, view.Color.b, front ? .52f : .16f);
                        view.Halo.Animate(clock);
                        view.Shine.Animate(Mathf.Clamp01((flipTime - .3f) / .8f) * .999f);
                        if (front && lastSound < i)
                        {
                            lastSound = i; RevealedCount = i + 1;
                            AudioManager.PlaySE(i == 2 ? "SE_CARDEXPAND_DISPLAY" : "SE_CARD_MOVE_02", .8f);
                        }
                    }
                    if (elapsed >= 3.7f) SetPhase(Phase.Complete);
                    break;
                case Phase.Complete:
                    foreach (var view in views) view.Halo.Animate(clock);
                    break;
            }
        }

        private void SetPhase(Phase phase)
        {
            CurrentPhase = phase; elapsed = 0;
            action.interactable = phase == Phase.Sealed || phase == Phase.Complete;
            actionAlpha.alpha = action.interactable ? 1 : 0;
            if (phase == Phase.Arrival) { packAlpha.alpha = 0; hint.text = ""; }
            if (phase == Phase.Sealed) hint.text = "轻触开启，揭晓你的三张卡片";
            if (phase == Phase.Opening)
            {
                hint.text = "";
                AudioManager.PlaySE("SE_CARDEXPAND_ZOOMIN", .7f);
            }
            if (phase == Phase.Reveal)
            {
                lastSound = -1;
                pack.gameObject.SetActive(false); burst.color = Color.clear; flash.color = Color.clear;
                heading.text = "命运，即将揭晓";
                hint.text = "";
                orbit.color = new Color(.2f, .55f, .7f, .13f);
                halo.color = new Color(.04f, .26f, .39f, .7f);
            }
            if (phase == Phase.Complete)
            {
                pack.gameObject.SetActive(false); burst.color = Color.clear; flash.color = Color.clear;
                heading.text = "获得卡片";
                hint.text = "3 张卡片已加入你的收藏";
                result.text = "已自动保存   ·   点击卡片查看详情";
                actionText.text = "继续购买";
                skip.gameObject.SetActive(false);
                orbit.color = new Color(.2f, .55f, .7f, .13f);
                halo.color = new Color(.04f, .26f, .39f, .7f);
                RevealedCount = cards.Count;
                for (int i = 0; i < views.Count; i++)
                {
                    var view = views[i];
                    view.Root.anchoredPosition = new Vector2((i - 1) * 360, 35);
                    view.Root.localScale = Vector3.one;
                    view.Alpha.alpha = view.Label.alpha = 1;
                    view.Face.gameObject.SetActive(true); view.Back.gameObject.SetActive(false);
                    view.Shine.gameObject.SetActive(false);
                    view.Halo.color = new Color(view.Color.r, view.Color.g, view.Color.b, .45f);
                    view.Button.interactable = true;
                }
            }
        }

        internal void Advance()
        {
            if (closed || PluginGame.CurrentPopup != null) return;
            if (CurrentPhase == Phase.Sealed) SetPhase(Phase.Opening);
            else if (CurrentPhase == Phase.Complete) Close(true);
        }
        internal void Skip()
        {
            if (closed || CurrentPhase == Phase.Complete || PluginGame.CurrentPopup != null) return;
            SetPhase(Phase.Complete);
        }
        internal void Abort() { Close(false); }

        private void Close(bool notify)
        {
            if (closed) return;
            closed = true;
            RestoreInput();
            gameObject.SetActive(false); Destroy(gameObject);
            var callback = finished; finished = null;
            if (notify) callback?.Invoke();
        }

        private void RestoreInput()
        {
            if (restored) return;
            restored = true;
            if (browserGroup != null)
            {
                browserGroup.interactable = oldInteractable;
                browserGroup.blocksRaycasts = oldRaycasts;
            }
            if (ReferenceEquals(UIManager.InputBlocker, this))
                UIManager.InputBlocker = previousBlocker != null && previousBlocker.isActiveAndEnabled ? previousBlocker : null;
            if (EventSystem.current != null && previousSelection != null && previousSelection.activeInHierarchy
                && previousBlocker != null && previousBlocker.isActiveAndEnabled)
                EventSystem.current.SetSelectedGameObject(previousSelection);
        }
        private void OnDisable() { RestoreInput(); }
        private void OnDestroy() { RestoreInput(); }
        private void Resize()
        {
            if (stage == null) return;
            var size = ((RectTransform)transform).rect.size;
            stage.localScale = Vector3.one * Mathf.Max(.01f, Mathf.Min(size.x / 1600, size.y / 900));
        }
        private static float Ease(float t) { t = Mathf.Clamp01(t); return 1 - Mathf.Pow(1 - t, 3); }
        private static RectTransform Fixed(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = StoryUI.Rect(name, parent, .5f, .5f, 0, 0);
            rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y); return rect;
        }
        private static RectTransform Line(Transform parent, float x, float y, float width, float height, Color color)
        {
            var rect = Fixed("LightLine", parent, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }
        private static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float width, float height,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var host = Fixed("Label", parent, x, y, width, height);
            var label = StoryUI.Text(host, text, 0, 0, 1, 1, size, alignment); label.color = color; return label;
        }
        private static StoryOpeningGraphic Effect(string name, Transform parent, StoryOpeningGraphic.Shape shape,
            float x, float y, float width, float height, Color color)
        {
            var rect = Fixed(name, parent, x, y, width, height);
            var graphic = rect.gameObject.AddComponent<StoryOpeningGraphic>();
            graphic.Form = shape; graphic.color = color; graphic.raycastTarget = false; return graphic;
        }
        private static Button CreateButton(Transform parent, string name, string caption, float x, float y, float w, float h, Action click, bool primary)
        {
            var host = Fixed(name, parent, x, y, w, h);
            var button = StoryUI.Button(host, caption, 0, 0, 1, 1, click, primary);
            button.gameObject.name = name;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.targetGraphic.color = primary ? new Color(.75f, .91f, 1) : new Color(.04f, .08f, .12f);
            return button;
        }
    }
}
