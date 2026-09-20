using System;
using System.IO;
using System.Linq;
using MDPro3.Plugins.Features.PackBrowser;
using MDPro3.Plugins.Features.StoryMode;
using MDPro3.UI;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MDPro3.Plugins.Diagnostics
{
    // Invoked only by the existing isolated-player self-test, never in a regular game.
    internal static class StoryOpeningSelfTest
    {
        private static int step, skipPhase;
        private static float next;
        private static StoryPackOpening opening;
        private static PackBrowserOverlay shop;
        private static string committed;

        internal static bool Tick(StoryModeFeature feature, string output)
        {
            if (Time.unscaledTime < next) return false;
            switch (step)
            {
                case 0:
                    shop = UnityEngine.Object.FindFirstObjectByType<PackBrowserOverlay>();
                    opening = UnityEngine.Object.FindFirstObjectByType<StoryPackOpening>();
                    Check(opening != null && opening.CurrentPhase == StoryPackOpening.Phase.Sealed, "sealed pack is ready");
                    Check(UIManager.InputBlocker == opening, "opening owns input");
                    Check(!shop.GetComponent<CanvasGroup>().interactable, "shop cannot receive navigation during opening");
                    Check(CanClick(opening.GetComponentsInChildren<Button>().First(b => b.name == "OpeningContinue")), "opening button receives pointer through its modal group");
                    committed = JsonConvert.SerializeObject(feature.Store.Current);
                    BuyButton().onClick.Invoke();
                    Check(committed == JsonConvert.SerializeObject(feature.Store.Current), "double purchase is ignored");
                    var finishes = opening.GetComponentsInChildren<StoryCardFinish>(true);
                    Check(finishes.Length == 3 && finishes.All(f => StoryProgress.ValidRarity(f.Rarity)
                        && feature.Store.Current.Owned(f.GetComponent<CardRawImageHandler>().card.Id, f.Rarity) > 0),
                        "all revealed finishes correspond to owned native rarity versions");
                    Capture(output, "05a-sealed");
                    Time.timeScale = 0;
                    Next(1, .7f); break;
                case 1:
                    opening.Advance(); Next(2, .92f); break;
                case 2:
                    Check(opening.CurrentPhase == StoryPackOpening.Phase.Opening, "seal animates while timescale is zero");
                    Capture(output, "05b-seal-cut"); Next(3, 1.9f); break;
                case 3:
                    Check(opening.CurrentPhase == StoryPackOpening.Phase.Reveal, "cards fan out");
                    Capture(output, "05c-reveal"); Next(4, 3.5f); break;
                case 4:
                    Check(opening.CurrentPhase == StoryPackOpening.Phase.Complete && opening.RevealedCount == 3, "three cards fully revealed");
                    Check(committed == JsonConvert.SerializeObject(feature.Store.Current), "normal animation leaves committed rewards unchanged");
                    var foils = opening.GetComponentsInChildren<StoryCardFoil>();
                    Check(foils.Length == 3 && foils.All(f => f.HasRenderedFoil), "acquired SR cards reveal with animated full-face foil");
                    Capture(output, "05-opening");
                    Time.timeScale = 1;
                    Next(5, .7f); break;
                case 5:
                    opening.transform.Find("Stage/ObtainedCard0").GetComponent<Button>().onClick.Invoke();
                    Next(6, 1.2f); break;
                case 6:
                    Check(UIManager.InputBlocker is CardInfoDetail, "result opens native card detail");
                    Check(UIManager.InputBlocker.GetComponentInChildren<StoryCardFoil>()?.HasRenderedFoil == true,
                        "card detail retains the acquired SR full-face foil");
                    Capture(output, "05d-sr-detail");
                    Next(15, .3f); break;
                case 15:
                    ((CardInfoDetail)UIManager.InputBlocker).Hide(); Next(7, .8f); break;
                case 7:
                    Check(UIManager.InputBlocker == opening, "card detail returns input to the result");
                    opening.Advance(); Next(8, .3f); break;
                case 8:
                    Check(UIManager.InputBlocker == shop && shop.GetComponent<CanvasGroup>().interactable, "continue restores shop input");
                    Check(BuyButton().gameObject.activeInHierarchy, "continue returns to the same purchasable pack");
                    // Fund only the isolated profile for skip and interrupted-opening regressions.
                    var save = feature.Store.Current.Copy(); save.dp = 1000; feature.Store.Commit(save);
                    Next(9, .1f); break;
                case 9:
                    int before = feature.Store.Current.owned.Values.Sum(), dp = feature.Store.Current.dp;
                    BuyButton().onClick.Invoke();
                    opening = UnityEngine.Object.FindFirstObjectByType<StoryPackOpening>();
                    Check(opening != null, "repeat purchase creates a fresh opening");
                    Check(feature.Store.Current.owned.Values.Sum() == before + 3 && feature.Store.Current.dp == dp - feature.Rules.packPrice,
                        "repeat purchase commits once");
                    committed = JsonConvert.SerializeObject(feature.Store.Current);
                    if (skipPhase == 0) { opening.Skip(); Next(12, .1f); }
                    else Next(10, 1.2f);
                    break;
                case 10:
                    if (skipPhase == 1) { opening.Skip(); Next(12, .1f); }
                    else { opening.Advance(); Next(11, skipPhase == 2 ? .5f : 2.2f); }
                    break;
                case 11:
                    Check(opening.CurrentPhase == (skipPhase == 2 ? StoryPackOpening.Phase.Opening : StoryPackOpening.Phase.Reveal), "skip exercises the expected phase");
                    opening.Skip(); Next(12, .1f); break;
                case 12:
                    opening.Skip();
                    Check(opening.CurrentPhase == StoryPackOpening.Phase.Complete && opening.RevealedCount == 3, "skip shows all three cards, including repeated skip");
                    Check(committed == JsonConvert.SerializeObject(feature.Store.Current), "skip preserves rewards and DP");
                    opening.Advance();
                    skipPhase++;
                    Next(skipPhase < 4 ? 9 : 13, .3f); break;
                case 13:
                    BuyButton().onClick.Invoke();
                    committed = JsonConvert.SerializeObject(feature.Store.Current);
                    shop.Close(); Next(14, .4f); break;
                case 14:
                    Check(UnityEngine.Object.FindFirstObjectByType<StoryPackOpening>() == null, "closing shop tears down its opening");
                    Check(UIManager.InputBlocker is StoryOverlay, "closing shop returns input to story mode");
                    Check(committed == JsonConvert.SerializeObject(feature.Store.Current), "interrupted opening retains the purchase");
                    var loaded = new StoryStore(Path.Combine(Path.GetDirectoryName(PluginConfig.LoadedPath), "StoryMode"));
                    loaded.Load(StoryCatalog.Starter);
                    Check(JsonConvert.SerializeObject(loaded.Current) == committed, "awarded cards survive a save reload");
                    Debug.Log("[StoryOpeningSelfTest] PASS normal reveal, paused time, native detail, repeat purchase, skip in all phases, teardown and persisted rewards");
                    return true;
            }
            return false;
        }

        private static Button BuyButton() => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .First(b => b.name == "购买并拆包" && b.gameObject.activeInHierarchy);
        private static bool CanClick(Button button)
        {
            var pointer = new PointerEventData(EventSystem.current);
            pointer.position = RectTransformUtility.WorldToScreenPoint(Program.instance.camera_.cameraUI, button.transform.position);
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform) && button.IsInteractable();
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Opening: " + message);
            Debug.Log("[StoryOpeningSelfTest] " + message);
        }
        private static void Next(int value, float delay) { step = value; next = Time.unscaledTime + delay; }
        private static void Capture(string directory, string name)
        {
            var capture = new GameObject("OpeningScreenshot").AddComponent<StoryScreenshot>();
            capture.StartCoroutine(capture.Capture(Path.Combine(directory, name + ".png")));
        }
    }
}
