using System;
using System.Linq;
using MDPro3.Duel.YGOSharp;
using MDPro3.Plugins.Features.StoryMode;
using MDPro3.Servant;
using MDPro3.UI;
using MDPro3.UI.ServantUI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MDPro3.Plugins.Diagnostics
{
    internal static class StoryRaritySelfTest
    {
        private static int code;
        private static CardRarity.Rarity ordinaryFinish;
        private static CardRawImageHandler[] preview;

        internal static void BeginFoilPreview()
        {
            var root = StoryUI.Box("FoilFinishPreview", PluginGame.UI.popup, 0, 0, 1, 1, new Color(.025f, .03f, .04f));
            var rarities = new[] { StoryRarity.N, StoryRarity.R, StoryRarity.SR, StoryRarity.UR };
            preview = new CardRawImageHandler[rarities.Length];
            for (int i = 0; i < rarities.Length; i++)
            {
                float x = .025f + i * .245f;
                StoryUI.Text(root, rarities[i].ToString(), x, .86f, .215f, .07f, 32, TextAlignmentOptions.Center);
                var slot = StoryUI.Rect("PreviewSlot", root, x, .1f, .215f, .74f);
                var art = StoryUI.Rect("Art", slot, 0, 0, 1, 1);
                var aspect = art.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 59f / 86;
                art.gameObject.AddComponent<RawImage>().raycastTarget = false;
                var handler = art.gameObject.AddComponent<CardRawImageHandler>();
                handler.SetCard(76524506);
                StoryCardFinish.Apply(handler, rarities[i]); preview[i] = handler;
            }
        }

        internal static bool FoilPreviewReady() => preview.All(p => p.Refreshed);
        internal static void CheckFoilPreview()
        {
            var sr = preview[2];
            Check(sr.RawImage.material.shader.name.Contains("Normal") && sr.GetComponentInChildren<StoryCardFoil>() != null,
                "SR combines ordinary printing with a separate full-face foil");
            Check(sr.GetComponentInChildren<StoryCardFoil>().HasRenderedFoil, "SR full-face foil has actually populated its canvas mesh");
            Check(preview[1].RawImage.material.shader.name.Contains("Shine")
                && preview[3].RawImage.material.shader.name.Contains("Royal"), "R and UR retain native finishes");
            Check(preview.Where(p => p != sr).All(p => p.GetComponentInChildren<StoryCardFoil>() == null),
                "full-face foil is exclusive to SR");
            Check(preview.All(p => p.RawImage.texture != null), "all finish previews loaded actual card art");
        }

        internal static void Prepare(StoryModeFeature feature)
        {
            var session = StoryDeckEditor.Active;
            var ui = session.UI; var view = ui.DeckView;
            code = view.cards.GroupBy(c => c.Card.Id).First(g => g.Count() == 2
                && g.All(c => StoryRarityEditor.Finish(c) == StoryRarity.N)).Key;
            ordinaryFinish = CardRarity.GetRarity(code);
            var save = feature.Store.Current.Copy();
            int previousDp = save.dp; save.dp = feature.Rules.packPrice;
            var rules = new StoryRules { packPrice = feature.Rules.packPrice, rarityWeights = new[] { 0, 0, 0, 1, 0, 0 } };
            var draws = StoryProgress.Buy(save, rules, 0, new[] { code }, _ => 0);
            Check(draws.All(c => c.rarity == StoryRarity.UR), "forced test pack awards actual UR copies");
            rules.rarityWeights = new[] { 0, 0, 1, 0, 0, 0 }; save.dp = rules.packPrice;
            Check(StoryProgress.Buy(save, rules, 0, new[] { code }, _ => 0).All(c => c.rarity == StoryRarity.SR),
                "forced foil pack awards distinct SR copies");
            save.dp = previousDp; feature.Store.Commit(save);
            Check(!ui.GetComponentsInChildren<SelectionToggle_Rarity>(true).Any(t => t.gameObject.activeInHierarchy), "player free rarity switches are hidden");
            ui.ChangeRarity(CardRarity.Rarity.Gold);
            Check(CardRarity.GetRarity(code) == ordinaryFinish, "player cannot change global card finish");
            session.Rarities.Select(code, StoryRarity.UR);
            Check(view.AddCard(CardsManager.Get(code), false, false), "owned UR can be added alongside N copies");
            Check(!view.CanAddCard(code, false), "three copies across finishes block a fourth");
            var ur = view.cards.Single(c => c.Card.Id == code && StoryRarityEditor.Finish(c) == StoryRarity.UR);
            var urImage = ur.GetComponentInChildren<StoryCardFinish>(true);
            Check(view.GetCardByData(ur.Card) == ur, "minus-one targets the chosen finish");
            session.Rarities.Select(code, StoryRarity.MR);
            Check(session.Rarities.Selected(code) == StoryRarity.UR, "unowned finish cannot be selected");
            var normal = view.cards.First(c => c.Card.Id == code && StoryRarityEditor.Finish(c) == StoryRarity.N);
            Check(view.RemoveCard(normal, false, false, true), "remove a normal copy without removing the UR");
            view.MoveCardToLocation(ur, DeckView.DeckLocation.SideDeck, ur.transform.position);
            Check(StoryDeckEditor.FromGame(view.FromObjectDeckToCodedDeck()).sideRarities.Contains(StoryRarity.UR), "side-deck move retains the finish");
            view.MoveCardToLocation(ur, DeckView.DeckLocation.MainDeck, ur.transform.position);
            session.Rarities.Select(code, StoryRarity.N);
            Check(urImage.Rarity == StoryRarity.UR, "switching selection during a move leaves the physical copy unchanged");
            session.Rarities.Select(code, StoryRarity.UR);
            session.Rarities.Select(code, StoryRarity.SR);
            Check(view.AddCard(CardsManager.Get(code), false, false), "owned SR joins N and UR in the same deck");
            Check(!view.CanAddCard(code, false), "N plus SR plus UR still blocks a fourth copy");
            view.Randomize();
            Check(StoryDeckEditor.FromGame(view.FromObjectDeckToCodedDeck()).Copies().Count(c => c.id == code && c.rarity == StoryRarity.UR) == 1,
                "shuffle preserves per-copy finishes");
            ui.OnSave();
            Check(!view.GetDirty() && feature.Store.Current.player.Copies().Count(c => c.id == code && c.rarity == StoryRarity.UR) == 1,
                "native save records the mixed-finish deck");
        }

        internal static void CheckReload(StoryModeFeature feature)
        {
            var ui = StoryDeckEditor.Active.UI;
            var cards = ui.DeckView.cards.Where(c => c.Card.Id == code).ToList();
            Check(cards.Count(c => StoryRarityEditor.Finish(c) == StoryRarity.UR) == 1
                && cards.Count(c => StoryRarityEditor.Finish(c) == StoryRarity.N) == 1
                && cards.Count(c => StoryRarityEditor.Finish(c) == StoryRarity.SR) == 1, "reprinting restores separate N, SR and UR copies");
            var image = cards.First(c => StoryRarityEditor.Finish(c) == StoryRarity.UR).GetComponentInChildren<CardRawImageHandler>();
            Check(image.Refreshed && image.GetComponent<StoryCardFinish>().Rarity == StoryRarity.UR
                && image.RawImage.material.shader.name.Contains("Royal"), "UR copy renders using the native royal material");
            Check(CardRarity.GetRarity(code) == ordinaryFinish, "per-copy materials do not alter global rarity");
            var sr = cards.First(c => StoryRarityEditor.Finish(c) == StoryRarity.SR).GetComponentInChildren<CardRawImageHandler>();
            Check(sr.Refreshed && sr.GetComponentInChildren<StoryCardFoil>() != null
                && sr.RawImage.material.shader.name.Contains("Normal"), "saved SR copy restores its full-face foil");
            ui.CardCollectionView.InputSearch.InputField.text = code.ToString();
            // Native filters contain 13 bitmasks followed by 12 optional range bounds.
            CardCollectionView.filters = Enumerable.Repeat(0L, 13).Concat(Enumerable.Repeat(-233L, 12)).ToList();
            CardCollectionView.filters[8] = (long)CardRarity.Rarity.Royal;
            ui.CardCollectionView.PrintSearchCards();
            Check(ui.CardCollectionView.printedCards.Contains(code), "native rarity filter finds owned UR even with global N");
            CardCollectionView.filters[8] = (long)StoryRarity.SR;
            ui.CardCollectionView.PrintSearchCards();
            Check(ui.CardCollectionView.printedCards.Contains(code), "native rarity filter finds actual SR ownership");
            int normalOnly = feature.Store.Current.owned.Keys.First(id => feature.Store.Current.ownedRarities[id].Count == 1
                && feature.Store.Current.Owned(id, StoryRarity.N) > 0);
            ui.CardCollectionView.InputSearch.InputField.text = normalOnly.ToString(); ui.CardCollectionView.PrintSearchCards();
            Check(!ui.CardCollectionView.printedCards.Contains(normalOnly), "SR filter excludes ordinary copies");
            Check(StoryDeckEditorHooks.SortRarity(code) == 4, "rarity sorting places UR above SR despite preserved save IDs");
            CardCollectionView.filters.Clear(); ui.CardCollectionView.InputSearch.InputField.text = "";
            ui.CardCollectionView.PrintSearchCards();
            var store = new StoryStore(feature.Store.DirectoryPath); store.Load(StoryCatalog.Starter);
            Check(store.Current.player.Copies().Count(c => c.id == code && c.rarity == StoryRarity.UR) == 1,
                "mixed-finish deck survives disk reload");
            Check(store.Current.player.Copies().Count(c => c.id == code && c.rarity == StoryRarity.SR) == 1,
                "SR deck copies survive disk reload");
            ui.ShowDetail(CardsManager.Get(code));
            Check(ui.GetComponentsInChildren<StoryRarityPicker>(true).Length > 0, "owned rarity picker attached");
            var pickerRoot = ui.GetComponentsInChildren<RectTransform>(true).First(r => r.name == "OwnedRarityPicker" && r.gameObject.activeInHierarchy);
            Check(pickerRoot.rect.width > 200 && pickerRoot.rect.height > 40, "owned rarity choices have a visible layout");
            var choices = pickerRoot.GetComponentsInChildren<Button>();
            Check(choices.Length == 6 && choices.All(b => ((RectTransform)b.transform).anchorMax.x <= 1),
                "all six owned version choices fit within the native rarity row");
            Debug.Log("[StoryRaritySelfTest] PASS version inventory, mixed deck, three-copy limit, side move, shuffle, save and native material");
        }

        internal static void CheckOpponent(DeckEditorUI ui, int unowned)
        {
            Check(!StoryDeckEditorHooks.BlockFreeRarity(ui), "opponent retains free rarity controls");
            Check(ui.GetComponentsInChildren<SelectionToggle_Rarity>(true).Any(t => t.gameObject.activeInHierarchy), "opponent rarity buttons remain visible");
            Check(ui.GetComponentInChildren<StoryRarityPicker>(true) == null, "player version picker does not affect opponents");
            ui._ResponseRegion = DeckEditorUI.ResponseRegion.Collection;
            ui.ShowDetail(CardsManager.Get(unowned));
            var toggles = ui.CardDetailView.GetComponentsInChildren<SelectionToggle_Rarity>(true);
            Check(toggles.Length == 5 && toggles.Select(t => (int)t.rarity).Distinct().Count() == 5, "opponent has five unique native rarity toggles");
            var row = (RectTransform)toggles[0].transform.parent;
            float buttonsWidth = 0;
            foreach (var toggle in toggles) buttonsWidth += ((RectTransform)toggle.transform).rect.width;
            Check(toggles.All(t => ((RectTransform)t.transform).rect.width >= 60)
                && buttonsWidth <= row.rect.width + 1,
                "five opponent rarity buttons fit inside the native row");
            var sr = toggles.Single(t => (int)t.rarity == (int)StoryRarity.SR);
            Check(sr.transform.Find("Icon").GetComponent<Image>().sprite != null, "SR badge asset is loaded");
            var previous = CardRarity.GetRarity(unowned);
            var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            sr.OnPointerClick(pointer);
            Check(sr.isOn && StoryDeckEditor.Active.Rarities.Selected(unowned) == StoryRarity.SR, "SR click selects an unowned version for the opponent");
            sr.OnPointerClick(pointer);
            Check(!sr.isOn && StoryDeckEditor.Active.Rarities.Selected(unowned) == StoryRarity.N, "clicking SR again restores N");
            foreach (var toggle in toggles.Where(t => t != sr))
            {
                toggle.OnPointerClick(pointer);
                Check(toggle.isOn && StoryDeckEditor.Active.Rarities.Selected(unowned) == (StoryRarity)(int)toggle.rarity,
                    "native opponent toggle works: " + toggle.rarity);
            }
            sr.OnPointerClick(pointer);
            Check(toggles.Count(t => t.isOn) == 1 && sr.isOn, "SR selection is exclusive");
            Check(CardRarity.GetRarity(unowned) == previous, "opponent SR does not write a plugin-only ID into global rarity");
        }

        internal static void CheckOpponentReload(DeckEditorUI ui, int id)
        {
            var copies = ui.DeckView.cards.Where(c => c.Card.Id == id).ToArray();
            Check(copies.Length == 2 && copies.All(c => StoryRarityEditor.Finish(c) == StoryRarity.SR), "opponent deck reload restores both SR copies");
            Check(copies.All(c => c.GetComponentInChildren<StoryCardFoil>()?.HasRenderedFoil == true), "reloaded opponent SR renders full-face foil");
            ui.ShowDetail(copies[0].Card);
            var sr = ui.CardDetailView.GetComponentsInChildren<SelectionToggle_Rarity>(true).Single(t => (int)t.rarity == (int)StoryRarity.SR);
            Check(sr.isOn, "reopened opponent card restores SR button selection");
            var store = new StoryStore(StoryDeckEditor.Active.Owner.Store.DirectoryPath); store.Load(StoryCatalog.Starter);
            Check(store.Current.TryGetOpponent(StoryDeckEditor.Active.Character, StoryDeckEditor.Active.Level, out var saved)
                && saved.Copies().Count(c => c.id == id && c.rarity == StoryRarity.SR) == 2, "opponent SR versions survive disk reload");
            ui._ResponseRegion = DeckEditorUI.ResponseRegion.Collection;
            ui.ChangeRarity(CardRarity.Rarity.Royal);
            Check(ui.DeckView.GetDirty() && copies.All(c => StoryRarityEditor.Finish(c) == StoryRarity.UR),
                "changing an existing opponent card updates its copies and marks the deck dirty");
            ui.ChangeRarity((CardRarity.Rarity)(int)StoryRarity.SR);
            Check(copies.All(c => StoryRarityEditor.Finish(c) == StoryRarity.SR), "existing opponent copies can switch back to SR");
        }

        internal static void CheckFilter(MDPro3.UI.Popup.PopupSearchFilter popup)
        {
            var silver = popup.GetComponentsInChildren<SelectionToggle_SearchFilter>(true)
                .Single(t => t.group == 8 && t.filterCode == (int)StoryRarity.SR);
            Check(silver.GetButtonText() == "SR" && ((RectTransform)silver.transform).rect.width > 100,
                "native search popup includes a laid-out SR option");
            silver.SetToggleOn();
            typeof(MDPro3.UI.Popup.PopupSearchFilter).GetMethod("OnDecide", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic).Invoke(popup, null);
            Check(CardCollectionView.filters[8] == (int)StoryRarity.SR
                && StoryDeckEditor.Active.UI.CardCollectionView.printedCards.Contains(code), "SR filter selection reaches actual collection results");
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Rarity: " + message);
            Debug.Log("[StoryRaritySelfTest] " + message);
        }
    }
}
