using System;
using System.Collections.Generic;
using System.Linq;
using MDPro3.Duel.YGOSharp;
using MDPro3.Servant;
using MDPro3.UI;
using MDPro3.UI.ServantUI;
using MDPro3.Utility;
using TMPro;
using UnityEngine;

namespace MDPro3.Plugins.Features.StoryMode
{
    // A session in the real DeckEditor prefab. No ordinary deck files or database changes.
    // The plugin IL postprocessor inserts narrowly scoped calls to the public hooks below.
    internal sealed class StoryDeckEditor
    {
        internal static StoryDeckEditor Active { get; private set; }
        internal readonly StoryModeFeature Owner;
        internal readonly string Character;
        internal readonly int Level;
        internal readonly Banlist Banlist = new Banlist { Name = "故事模式" };
        internal DeckEditorUI UI;
        internal bool HandTestStarted;
        internal readonly StoryRarityEditor Rarities;
        private readonly Deck previousDeck;
        private readonly string previousName, previousOnlineId, previousPack;
        private readonly bool previousLocal;
        private readonly DeckEditor.Condition previousCondition;
        private readonly List<int> previousHistory;
        private readonly List<long> previousFilters;
        private readonly CardCollectionView.SortOrder previousSort;
        private readonly Servant.Servant previousReturn;
        private readonly Action previousReturnAction;
        private readonly bool previousFromHand, previousFromSolo, previousFromHost;
        private bool entered;
        private bool initialized;

        internal StoryDeckEditor(StoryModeFeature owner, string character, int level)
        {
            Owner = owner; Character = character; Level = level;
            Rarities = new StoryRarityEditor(this);
            previousDeck = DeckEditor.Deck; previousName = DeckEditor.DeckName;
            previousLocal = DeckEditor.DeckIsFromLocal; previousCondition = DeckEditor.condition;
            previousHistory = DeckEditor.historyCards; previousOnlineId = DeckEditor.onlineDeckID;
            previousFilters = CardCollectionView.filters; previousPack = CardCollectionView.packName;
            previousSort = CardCollectionView._SortOrder;
            previousReturn = Program.instance.deckEditor.returnServant;
            previousReturnAction = Program.instance.deckEditor.returnAction;
            previousFromHand = RoomServant.FromHandTest; previousFromSolo = RoomServant.FromSolo;
            previousFromHost = RoomServant.FromLocalHost;
        }

        internal void Open()
        {
            if (Active != null) throw new InvalidOperationException("已有故事卡组正在编辑。");
            if (!StoryDeckEditorHooks.Installed())
                throw new InvalidOperationException("故事模式编辑器接入尚未编译，请重建插件后再试。");
            var draft = Character == null ? Owner.Store.Current.player
                : Owner.Store.Current.TryGetOpponent(Character, Level, out var saved) ? saved : new StoryDeck();
            Active = this;
            DeckEditor.condition = DeckEditor.Condition.EditDeck;
            DeckEditor.Deck = ToGame(draft);
            DeckEditor.DeckName = Character == null ? "故事模式 · 我的卡组"
                : "故事模式 · " + CharacterSelector.characters.GetName(Character) + " · " + Level + "级";
            DeckEditor.DeckIsFromLocal = true;
            DeckEditor.onlineDeckID = null;
            DeckEditor.historyCards = new List<int>();
            CardCollectionView.filters = new List<long>(); CardCollectionView.packName = string.Empty;
            Program.instance.deckEditor.returnServant = Program.instance.menu;
            Program.instance.deckEditor.returnAction = null;
            Program.instance.ShiftToServant(Program.instance.deckEditor);
        }

        internal void Tick()
        {
            if (PluginGame.CurrentServant == Program.instance.deckEditor)
            {
                entered = true;
                if (!initialized && UI != null && UI.DeckView.deckLoaded)
                {
                    initialized = true;
                    UI.CardCollectionView.PrintSearchCards();
                    if (UI.DeckView.cards.Count > 0) UI.ShowDetail(UI.DeckView.cards[0].Card);
                }
                return;
            }
            if (!entered) return;
            // The native hand test retains its editor and returns to it with the unsaved draft.
            if (RoomServant.FromHandTest && (PluginGame.CurrentServant == Program.instance.room
                || PluginGame.CurrentServant == Program.instance.ocgcore)) return;
            // Native UI disposal clears static filters; restore only after that has finished.
            if (Program.instance.deckEditor.inTransition || Program.instance.deckEditor.servantUI != null) return;
            Restore();
            Owner.ReturnFromEditor();
        }

        internal void Restore()
        {
            if (Active != this) return;
            Active = null;
            DeckEditor.Deck = previousDeck; DeckEditor.DeckName = previousName;
            DeckEditor.DeckIsFromLocal = previousLocal; DeckEditor.condition = previousCondition;
            DeckEditor.historyCards = previousHistory; DeckEditor.onlineDeckID = previousOnlineId;
            CardCollectionView.filters = previousFilters; CardCollectionView.packName = previousPack;
            CardCollectionView._SortOrder = previousSort;
            Program.instance.deckEditor.returnServant = previousReturn;
            Program.instance.deckEditor.returnAction = previousReturnAction;
            RoomServant.FromHandTest = previousFromHand; RoomServant.FromSolo = previousFromSolo;
            RoomServant.FromLocalHost = previousFromHost;
        }

        internal bool Owns(Component component)
        {
            var ui = component == null ? null : component.GetComponentInParent<DeckEditorUI>();
            return ui != null && (ui == UI || (UI == null && PluginGame.CurrentServant == Program.instance.deckEditor));
        }

        internal bool Save(DeckView view)
        {
            if (!view.deckLoaded) return false;
            var deck = FromGame(view.FromObjectDeckToCodedDeck());
            if (!Owner.SaveDeck(Character, Level, deck)) return false;
            // Separate objects: native YDKE import mutates DeckView.Deck before printing.
            view.Deck = ToGame(deck); DeckEditor.Deck = ToGame(deck);
            view.SetDirty(false);
            MessageManager.Toast(Owner.Notice);
            return true;
        }

        internal bool AllowedDraft(Deck deck, bool complete)
        {
            var draft = Rarities.Import(deck);
            string invalid = StoryProgress.ValidateDeck(draft, Character == null ? Owner.Store.Current.owned : null,
                StoryCatalog.Playable, StoryCatalog.IsExtra, StoryCatalog.Identity, complete, Character == null ? Owner.Store.Current : null);
            if (invalid == null) Rarities.Remember(deck, draft);
            if (invalid == null) return true;
            MessageManager.Toast(invalid); return false;
        }

        internal static StoryDeck FromGame(Deck deck) => deck != null
            && Active != null && Active.Rarities.Read(deck, out var saved) ? saved.Copy() : PlainDeck(deck);
        internal static StoryDeck PlainDeck(Deck deck) => deck == null ? null : new StoryDeck
        { main = deck.Main == null ? null : new List<int>(deck.Main), extra = deck.Extra == null ? null : new List<int>(deck.Extra),
            side = deck.Side == null ? null : new List<int>(deck.Side) };
        internal static Deck ToGame(StoryDeck deck)
        {
            var game = new Deck { Main = new List<int>(deck.main), Extra = new List<int>(deck.extra), Side = new List<int>(deck.side) };
            if (Active != null) Active.Rarities.Remember(game, deck);
            return game;
        }
    }

    public static class StoryDeckEditorHooks
    {
        // Replaced with true by the IL postprocessor, so an unprocessed build fails closed.
        public static bool Installed() => false;
        public static bool Active() => StoryDeckEditor.Active != null;
        public static Banlist GetBanlist() => StoryDeckEditor.Active.Banlist;
        public static bool UsesView(DeckView view) => StoryDeckEditor.Active?.Owns(view) == true;
        public static bool UsesPlayerView(DeckView view) => UsesView(view) && StoryDeckEditor.Active.Character == null;
        public static bool BlockFreeRarity(DeckEditorUI ui) => StoryDeckEditor.Active?.Owns(ui) == true && StoryDeckEditor.Active.Character == null;
        public static bool ChangeRarity(DeckEditorUI ui, CardRarity.Rarity rarity)
        {
            var session = StoryDeckEditor.Active;
            if (session?.Owns(ui) != true) return false;
            if (!BlockFreeRarity(ui))
            {
                var card = ui._ResponseRegion == DeckEditorUI.ResponseRegion.Action ? ui.CardActionMenu.Card : ui.CardDetailView?.Card;
                if (card != null) session.Rarities.ChangeOpponent(card.Id, (StoryRarity)(int)rarity);
            }
            return true;
        }

        public static SelectionButton_CardInDeck StampCard(SelectionButton_CardInDeck card, DeckView view)
        {
            if (UsesView(view)) StoryDeckEditor.Active.Rarities.Stamp(view, card);
            return card;
        }
        public static Card PrepareCard(DeckView view, Card card) => UsesView(view) ? card.Clone() : card;
        public static void SelectWidgetVersion(UIWidgetCardBase widget, Card card)
        {
            var session = StoryDeckEditor.Active;
            if (session?.Owns(widget) == true) session.Rarities.SelectCopy(card);
        }
        public static Deck ExportRarities(Deck deck, DeckView view)
        {
            if (UsesView(view)) StoryDeckEditor.Active.Rarities.Export(view, deck);
            return deck;
        }
        public static SelectionButton_CardInDeck FindVersion(DeckView view, Card card)
        {
            var rarity = StoryDeckEditor.Active.Rarities.Selected(card.Id);
            return view.cards.FirstOrDefault(c => c.Card.Id == card.Id && StoryRarityEditor.Finish(c) == rarity);
        }
        public static void SelectDeckVersion(SelectionButton_CardInDeck card)
        {
            if (UsesView(card.deckView)) StoryDeckEditor.Active.Rarities.Select(card.Card.Id, StoryRarityEditor.Finish(card));
        }
        public static void ConfigureCardWidget(UIWidgetCardBase widget)
        {
            var session = StoryDeckEditor.Active;
            if (session?.Owns(widget) != true) return;
            if (session.Character == null) StoryRarityPicker.Attach(widget);
            else StoryOpponentRarity.Attach(widget);
        }
        public static void StyleCard(CardRawImageHandler image)
        {
            var session = StoryDeckEditor.Active;
            if (session == null || image.card == null
                || PluginGame.CurrentServant != Program.instance.deckEditor) return;
            var deckCard = image.GetComponentInParent<SelectionButton_CardInDeck>();
            var copy = deckCard?.GetComponent<StoryDeckCardVersion>() ?? image.GetComponent<StoryCardFinish>()?.DeckCopy;
            StoryCardFinish.Apply(image, copy != null ? copy.Rarity : session.Rarities.Selected(image.card.Id));
        }
        public static CardRarity.Rarity SearchRarity(int code)
        {
            var session = StoryDeckEditor.Active;
            if (session == null || session.Character != null || PluginGame.CurrentServant != Program.instance.deckEditor)
                return CardRarity.GetRarity(code);
            int mask = 0;
            foreach (var rarity in StoryProgress.Rarities)
                if (session.Owner.Store.Current.Owned(code, rarity) > 0) mask |= (int)rarity;
            return (CardRarity.Rarity)mask;
        }

        public static int SortRarity(int code)
        {
            var session = StoryDeckEditor.Active;
            if (session == null || session.Character != null || PluginGame.CurrentServant != Program.instance.deckEditor)
                return (int)CardRarity.GetRarity(code);
            for (int i = StoryProgress.Rarities.Length - 1; i >= 0; i--)
                if (session.Owner.Store.Current.Owned(code, StoryProgress.Rarities[i]) > 0) return i + 1;
            return 0;
        }

        public static bool MatchesRarity(Card card, List<long> filters)
        {
            var session = StoryDeckEditor.Active;
            if (session == null || session.Character != null || PluginGame.CurrentServant != Program.instance.deckEditor
                || filters == null || filters.Count <= 8 || filters[8] == 0) return true;
            // The native legacy shortcut treats mask 7 as all tiers, which excludes story SR/GR/MR.
            return (filters[8] & (long)SearchRarity(card.Id)) != 0;
        }

        public static void ConfigureRarityFilter(MDPro3.UI.Popup.PopupSearchFilter popup)
        {
            var session = StoryDeckEditor.Active;
            if (session == null || session.Character != null || PluginGame.CurrentServant != Program.instance.deckEditor) return;
            var toggles = popup.GetComponentsInChildren<SelectionToggle_SearchFilter>(true);
            if (toggles.Any(t => t.group == 8 && t.filterCode == (int)StoryRarity.SR)) return;
            var source = toggles.FirstOrDefault(t => t.group == 8 && t.filterCode == (int)StoryRarity.R);
            if (source == null) return;
            var silver = UnityEngine.Object.Instantiate(source, source.transform.parent);
            silver.name = "StorySRFilter"; silver.code = silver.subCode = 0;
            silver.filterCode = (int)StoryRarity.SR;
            silver.SetButtonText("SR"); silver.SetToggleOff();
            silver.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        }

        public static List<int> FilterCards(CardCollectionView view, List<int> cards)
        {
            var session = StoryDeckEditor.Active;
            if (session?.Owns(view) != true) return cards;
            var result = cards.Where(id => StoryCatalog.Playable(id)
                && (session.Character != null || session.Owner.Store.Current.owned.ContainsKey(id))).ToList();
            view.GetComponent<YgomSystem.ElementSystem.ElementObjectManager>()
                .GetNestedElement<SelectionButton>("FilterAndSortArea/SearchButton")?.SetButtonText(result.Count.ToString());
            return result;
        }

        public static bool CanAdd(DeckView view, int code, bool showToast)
        {
            var session = StoryDeckEditor.Active;
            string invalid = null;
            if (!StoryCatalog.Playable(code)) invalid = "这张卡不能加入卡组。";
            else if (view.cards.Count(c => StoryCatalog.Identity(c.Card.Id) == StoryCatalog.Identity(code)) >= 3)
                invalid = "同名卡在主卡组、额外卡组和副卡组合计最多 3 张。";
            else if (session.Character == null && (!session.Owner.Store.Current.owned.TryGetValue(code, out int owned)
                || view.cards.Count(c => c.Card.Id == code) >= owned))
                invalid = "已用完这张卡的持有数量，请通过故事卡包获取更多。";
            else if (session.Character == null && session.Rarities.Used(code, session.Rarities.Selected(code))
                >= session.Owner.Store.Current.Owned(code, session.Rarities.Selected(code)))
                invalid = "已用完 " + session.Rarities.Selected(code) + " 版本，请选择其他持有版本或继续开包。";
            if (invalid != null && showToast) MessageManager.Toast(invalid);
            return invalid == null;
        }

        public static bool SaveView(DeckView view) => StoryDeckEditor.Active.Save(view);
        public static bool SaveUI(DeckEditorUI ui)
        {
            if (StoryDeckEditor.Active?.Owns(ui) != true) return false;
            StoryDeckEditor.Active.Save(ui.DeckView); return true;
        }

        public static bool Return(DeckEditor editor)
        {
            var session = StoryDeckEditor.Active;
            if (session == null || editor != Program.instance.deckEditor) return false;
            var view = editor.GetUI<DeckEditorUI>()?.DeckView;
            if (view == null || !view.deckLoaded) return true;
            if (!view.GetDirty()) editor.OnExit();
            else UIManager.ShowPopupYesOrNo(new List<string> { "故事卡组未保存", "是否保存当前修改？关闭窗口可继续编辑。", "保存并返回", "放弃修改" },
                () => { if (session.Save(view)) editor.OnExit(); }, editor.OnExit);
            return true;
        }

        public static bool AllowImport(DeckView view, Deck deck)
        {
            if (!UsesView(view)) return true;
            var session = StoryDeckEditor.Active;
            if (!session.AllowedDraft(deck, false)) return false;
            // Native ImportCardLists copies the imported IDs into the existing Deck object.
            if (session.Rarities.Read(deck, out var draft)) session.Rarities.Remember(view.Deck, draft);
            return true;
        }
        public static bool AllowHandTest(DeckEditorUI ui)
        {
            var session = StoryDeckEditor.Active;
            if (session?.Owns(ui) != true) return true;
            if (!ui.DeckView.deckLoaded || !session.AllowedDraft(ui.DeckView.FromObjectDeckToCodedDeck(), true)) return false;
            session.HandTestStarted = true;
            return true;
        }

        public static void ShiftAfterDisconnect(Program program, Servant.Servant destination)
        {
            // TcpHelper completes disconnect cleanup after OcgCore has returned to the editor.
            // Its ordinary side-deck disconnect fallback must not discard a story hand-test draft.
            var session = StoryDeckEditor.Active;
            if (session?.HandTestStarted == true && program.currentServant == program.deckEditor
                && destination == program.online) return;
            program.ShiftToServant(destination);
        }

        public static void ShowRoomChat(ChatPanel panel, bool takeOver)
        {
            var story = PluginRegistry.Get<StoryModeFeature>(StoryModeFeature.FeatureId);
            if (story?.SuppressRoomChat != true) panel.Show(takeOver);
        }
        public static bool BlockAppearance(DeckEditorUI ui) => StoryDeckEditor.Active?.Owns(ui) == true;

        public static bool Regulation(DeckEditorUI ui)
        {
            if (StoryDeckEditor.Active?.Owns(ui) != true) return false;
            UIManager.ShowPopupConfirm(new List<string> { "故事模式构筑规则",
                "主卡组 40–60 张，额外和副卡组各最多 15 张，同名卡合计最多 3 张。@n"
                + (StoryDeckEditor.Active.Character == null ? "玩家只能使用持有的 N/R/SR/UR/GR/MR 版本，各版本不能超过持有数量，所有版本合计仍最多 3 张。" : "角色卡组可使用本体全卡库。") });
            return true;
        }

        public static bool SubMenu(DeckEditorUI ui)
        {
            var session = StoryDeckEditor.Active;
            if (session?.Owns(ui) != true) return false;
            if (!ui.DeckView.deckLoaded) return true;
            UIManager.ShowSubMenu(new List<string> { "", "重置到已保存卡组", "打乱", "清空", "使用初始构筑", "YDKE", "手牌测试设置" },
                new List<Action> { null,
                    () => {
                        var original = session.Character == null ? session.Owner.Store.Current.player
                            : session.Owner.Store.Current.TryGetOpponent(session.Character, session.Level, out var saved) ? saved : new StoryDeck();
                        ui.DeckView.PrintDeck(StoryDeckEditor.ToGame(original), DeckEditor.DeckName, DeckView.Condition.Editable);
                    }, ui.OnRandom, () => ui.DeckView.ClearDeck(),
                    () => ui.DeckView.ImportCardLists(StoryDeckEditor.ToGame(StoryCatalog.Starter())),
                    () => UIManager.ShowPopupYdke(() => ui.DeckView.ImportCardLists(YdkeConverter.Ydke2Deck(GUIUtility.systemCopyBuffer)),
                        () => { GUIUtility.systemCopyBuffer = YdkeConverter.DeckToYdke(ui.DeckView.FromObjectDeckToCodedDeck()); }),
                    () => typeof(DeckEditorUI).GetMethod("OnHandTestSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null) });
            return true;
        }

        public static void Configure(DeckEditorUI ui)
        {
            var session = StoryDeckEditor.Active;
            if (session?.Owns(ui) != true) return;
            session.UI = ui;
            // Keep the native layout, making the story save destination visible and immutable.
            var name = ui.DeckView.GetComponent<YgomSystem.ElementSystem.ElementObjectManager>()
                .GetNestedElement<TMP_InputField>("HeaderArea/InputField");
            name.readOnly = true;
            ui.DeckView.ButtonDeck.gameObject.SetActive(false);
            ui.Manager.GetNestedElement("AppearanceGroup").SetActive(false);
            if (session.Character == null)
                foreach (var toggle in ui.GetComponentsInChildren<SelectionToggle_Rarity>(true)) toggle.gameObject.SetActive(false);
            StoryUI.Text(ui.transform, session.Character == null ? "故事模式 · 持有卡池"
                : "故事模式 · 角色全卡库 · " + session.Level + "级",
                .055f, .936f, .34f, .045f, 25);
        }
    }
}
