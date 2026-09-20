using System.Collections.Generic;
using System.Linq;
using MDPro3.Duel.YGOSharp;
using MDPro3.Servant;
using MDPro3.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed class StoryRarityEditor
    {
        private readonly StoryDeckEditor session;
        private readonly Dictionary<Deck, StoryDeck> decks = new Dictionary<Deck, StoryDeck>();
        private readonly Dictionary<int, StoryRarity> choices = new Dictionary<int, StoryRarity>();
        private readonly Dictionary<Card, StoryRarity> copies = new Dictionary<Card, StoryRarity>();
        private Card pendingDetailCopy;
        internal StoryRarityEditor(StoryDeckEditor session) { this.session = session; }
        internal void Remember(Deck game, StoryDeck story) { decks[game] = story.Copy(); }
        internal bool Read(Deck game, out StoryDeck story)
        {
            story = null;
            return game != null && decks.TryGetValue(game, out story) && game.Main != null && game.Extra != null && game.Side != null
                && game.Main.SequenceEqual(story.main) && game.Extra.SequenceEqual(story.extra) && game.Side.SequenceEqual(story.side);
        }
        internal int Used(int id, StoryRarity rarity) => session.UI?.DeckView.cards?.Count(c => c.Card.Id == id && Finish(c) == rarity) ?? 0;
        internal StoryRarity Selected(Card card) => copies.TryGetValue(card, out var rarity) ? rarity : Selected(card.Id);
        internal StoryRarity Selected(int id)
        {
            if (choices.TryGetValue(id, out var rarity)) return rarity;
            if (session.Character != null)
            {
                var copy = session.UI?.DeckView.cards.FirstOrDefault(c => c.Card.Id == id && c.GetComponent<StoryDeckCardVersion>() != null);
                return copy != null ? Finish(copy) : (StoryRarity)(int)CardRarity.GetRarity(id);
            }
            foreach (var candidate in StoryProgress.Rarities)
                if (session.Owner.Store.Current.Owned(id, candidate) > 0) return candidate;
            return StoryRarity.N;
        }
        internal void Select(int id, StoryRarity rarity)
        {
            if (!StoryProgress.ValidRarity(rarity) || (session.Character == null && session.Owner.Store.Current.Owned(id, rarity) == 0)) return;
            choices[id] = rarity;
            if (session.UI != null)
                foreach (var image in session.UI.GetComponentsInChildren<CardRawImageHandler>(true))
                    if (image.card?.Id == id && image.GetComponentInParent<SelectionButton_CardInDeck>() == null
                        && image.GetComponent<StoryCardFinish>()?.DeckCopy == null
                        && (session.Character == null || !copies.ContainsKey(image.card)))
                        StoryCardFinish.Apply(image, rarity);
        }

        internal void ChangeOpponent(Card target, StoryRarity rarity)
        {
            if (session.Character == null || target == null || !StoryProgress.ValidRarity(rarity)) return;
            var card = session.UI.DeckView.cards.FirstOrDefault(c => ReferenceEquals(c.Card, target));
            // A removed card can remain in an open widget until its native animation finishes.
            if (card == null && copies.ContainsKey(target)) return;
            Select(target.Id, rarity);
            if (card != null)
            {
                if (Finish(card) != rarity) session.UI.DeckView.SetDirty(true);
                card.GetComponent<StoryDeckCardVersion>().Rarity = rarity;
                copies[card.Card] = rarity;
            }
            foreach (var image in session.UI.GetComponentsInChildren<CardRawImageHandler>(true))
                if (ReferenceEquals(image.card, target)) StoryCardFinish.Apply(image, rarity);
            foreach (var picker in session.UI.GetComponentsInChildren<StoryOpponentRarity>(true)) picker.Refresh();
        }

        internal void SelectDeckCopy(SelectionButton_CardInDeck card)
        {
            Select(card.Card.Id, Finish(card));
            // ShowThisCard converts the selected tile to a code list before calling the detail widget.
            if (session.Character != null && session.UI?.CardDetailView != null) pendingDetailCopy = card.Card;
        }

        internal Card PrepareWidgetCard(UIWidgetCardBase widget, Card card)
        {
            if (session.Character != null && widget is CardDetailView)
            {
                if (pendingDetailCopy?.Id == card.Id) card = pendingDetailCopy;
                pendingDetailCopy = null;
            }
            SelectCopy(card);
            // Native SetCardData returns early for the same ID, but each copy needs its own reference.
            if (session.Character != null && widget.Card?.Id == card.Id && !ReferenceEquals(widget.Card, card))
                widget.Card = card;
            return card;
        }

        internal static StoryRarity Finish(SelectionButton_CardInDeck card) =>
            card.GetComponent<StoryDeckCardVersion>()?.Rarity ?? StoryRarity.N;
        internal void SelectCopy(Card card)
        {
            if (card != null && copies.TryGetValue(card, out var rarity)) Select(card.Id, rarity);
        }

        internal void Stamp(DeckView view, SelectionButton_CardInDeck card)
        {
            var rarity = Selected(card.Card.Id);
            if (!view.deckLoaded && Read(view.Deck, out var deck))
            {
                var section = card.location == DeckView.DeckLocation.MainDeck ? deck.mainRarities
                    : card.location == DeckView.DeckLocation.ExtraDeck ? deck.extraRarities : deck.sideRarities;
                int index = view.cards.Count(c => c.location == card.location) - 1;
                if (section.Count > 0 || session.Character == null) rarity = StoryDeck.At(section, index);
            }
            var version = card.GetComponent<StoryDeckCardVersion>() ?? card.gameObject.AddComponent<StoryDeckCardVersion>();
            version.Rarity = rarity;
            // The native move animation temporarily detaches the image from its card tile.
            StoryCardFinish.Apply(card.GetComponentInChildren<CardRawImageHandler>(true), rarity).DeckCopy = version;
            copies[card.Card] = rarity;
        }

        internal void Export(DeckView view, Deck game)
        {
            var story = StoryDeckEditor.PlainDeck(game);
            foreach (var card in view.cards)
            {
                var section = card.location == DeckView.DeckLocation.MainDeck ? story.mainRarities
                    : card.location == DeckView.DeckLocation.ExtraDeck ? story.extraRarities : story.sideRarities;
                section.Add(Finish(card));
            }
            Remember(game, story);
        }

        internal StoryDeck Import(Deck game)
        {
            if (game == null) return null;
            if (Read(game, out var known)) return known.Copy();
            var story = StoryDeckEditor.PlainDeck(game);
            if (story?.main == null || story.extra == null || story.side == null) return story;
            // YDK/YDKE has no finish fields. Assign actual owned copies, never manufacture a finish.
            var used = new Dictionary<string, int>();
            Assign(story.main, story.mainRarities, used);
            Assign(story.extra, story.extraRarities, used);
            Assign(story.side, story.sideRarities, used);
            return story;
        }
        private void Assign(List<int> ids, List<StoryRarity> target, Dictionary<string, int> used)
        {
            foreach (int id in ids)
            {
                if (session.Character != null) { target.Add(Selected(id)); continue; }
                var rarity = StoryRarity.N;
                foreach (var candidate in StoryProgress.Rarities)
                {
                    string key = id + ":" + candidate;
                    int n = used.TryGetValue(key, out int count) ? count : 0;
                    if (n < session.Owner.Store.Current.Owned(id, candidate))
                    { rarity = candidate; used[key] = n + 1; break; }
                }
                target.Add(rarity);
            }
        }
    }

    internal sealed class StoryDeckCardVersion : MonoBehaviour { internal StoryRarity Rarity; }

    internal sealed class StoryRarityPicker : MonoBehaviour
    {
        private UIWidgetCardBase widget;
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();

        internal static void Attach(UIWidgetCardBase widget)
        {
            if (widget.GetComponent<StoryRarityPicker>() != null) return;
            var menu = widget.GetComponent<YgomSystem.ElementSystem.ElementObjectManager>().GetElement("MenuArea");
            if (menu == null) return;
            var toggles = widget.GetComponentsInChildren<SelectionToggle_Rarity>(true);
            if (toggles.Length == 0) return;
            var picker = widget.gameObject.AddComponent<StoryRarityPicker>();
            picker.widget = widget;
            foreach (var toggle in toggles) toggle.gameObject.SetActive(false);
            // Preserve the native rarity row's layout; disabled individual toggles have zero width.
            var root = StoryUI.Rect("OwnedRarityPicker", toggles[0].transform.parent, 0, 0, 1, 1);
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var title = StoryUI.Text(root, "持有版本 · 可用 / 持有", 0, .7f, 1, .28f, 15, TextAlignmentOptions.Center);
            title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
            title.fontSizeMin = 10;
            for (int i = 0; i < StoryProgress.Rarities.Length; i++)
            {
                var rarity = StoryProgress.Rarities[i];
                float width = 1f / StoryProgress.Rarities.Length;
                var button = StoryUI.Button(root, rarity.ToString(), i * width, 0, width - .008f, .68f, () =>
                {
                    var session = StoryDeckEditor.Active;
                    if (session?.Character == null && widget.Card != null) session?.Rarities.Select(widget.Card.Id, rarity);
                });
                var label = button.GetComponentInChildren<TextMeshProUGUI>();
                label.textWrappingMode = TextWrappingModes.Normal;
                label.fontSizeMax = 19;
                picker.buttons.Add(button); picker.labels.Add(label);
            }
        }

        private void LateUpdate()
        {
            var session = StoryDeckEditor.Active;
            if (session == null || session.Character != null || widget.Card == null) return;
            int id = widget.Card.Id;
            for (int i = 0; i < buttons.Count; i++)
            {
                var rarity = StoryProgress.Rarities[i];
                int owned = session.Owner.Store.Current.Owned(id, rarity);
                int remaining = Mathf.Max(0, owned - session.Rarities.Used(id, rarity));
                buttons[i].interactable = owned > 0;
                labels[i].text = rarity + "\n" + remaining + "/" + owned;
                labels[i].color = StoryCardFinish.Tint(rarity);
                buttons[i].targetGraphic.color = session.Rarities.Selected(id) == rarity ? new Color(.12f, .25f, .32f) : StoryUI.Panel;
            }
        }
    }
}
