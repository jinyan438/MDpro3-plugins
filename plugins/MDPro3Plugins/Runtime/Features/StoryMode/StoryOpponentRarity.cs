using System.IO;
using System.Linq;
using MDPro3.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed class StoryOpponentRarity : MonoBehaviour
    {
        private const string IconPath = "MDPro3Plugins/StoryMode/Icon_Rarity_SR";
        private static Sprite icon;
        private UIWidgetCardBase widget;
        private SelectionToggle_Rarity[] toggles;

        internal static void Attach(UIWidgetCardBase widget)
        {
            var picker = widget.GetComponent<StoryOpponentRarity>();
            if (picker != null) { picker.Refresh(); return; }
            var existing = widget.GetComponentsInChildren<SelectionToggle_Rarity>(true);
            var source = existing.FirstOrDefault(t => t.rarity == CardRarity.Rarity.Royal);
            if (source == null) return;
            picker = widget.gameObject.AddComponent<StoryOpponentRarity>();
            picker.widget = widget;
            var sr = Instantiate(source, source.transform.parent);
            sr.name = "ToggleRaritySR"; sr.rarity = (CardRarity.Rarity)(int)StoryRarity.SR;
            sr.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
            sr.SetToggleOff(false);
            sr.transform.Find("Icon").GetComponent<Image>().sprite = LoadIcon();
            picker.toggles = existing.Concat(new[] { sr }).OrderBy(t => t.transform.GetSiblingIndex()).ToArray();
            // Five equal native buttons fit in the same row without shrinking their circular artwork.
            var row = source.transform.parent.GetComponent<HorizontalLayoutGroup>();
            if (row != null) { row.spacing = 0; row.childForceExpandWidth = true; row.childControlWidth = true; }
            for (int i = 0; i < picker.toggles.Length; i++)
            {
                var toggle = picker.toggles[i];
                var layout = toggle.GetComponent<LayoutElement>();
                if (layout != null) { layout.minWidth = 0; layout.preferredWidth = 0; layout.flexibleWidth = 1; }
                var selectable = toggle.GetComponent<Selectable>();
                var navigation = selectable.navigation;
                navigation.selectOnLeft = i > 0 ? picker.toggles[i - 1].GetComponent<Selectable>() : null;
                navigation.selectOnRight = i + 1 < picker.toggles.Length ? picker.toggles[i + 1].GetComponent<Selectable>() : null;
                selectable.navigation = navigation;
            }
            picker.Refresh();
        }

        private static Sprite LoadIcon()
        {
            if (icon != null) return icon;
            icon = Resources.Load<Sprite>(IconPath);
            // Also support the isolated development player before its resource bundle is rebuilt.
            if (icon == null && PluginConfig.LoadedPath != null)
            {
                string path = Path.Combine(Path.GetDirectoryName(PluginConfig.LoadedPath), "MDPro3Plugins/Resources", IconPath + ".png");
                if (File.Exists(path))
                {
                    var texture = new Texture2D(2, 2);
                    texture.LoadImage(File.ReadAllBytes(path));
                    icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
                    icon.name = "Icon_Rarity_SR";
                }
            }
            return icon;
        }

        internal void Refresh()
        {
            var session = StoryDeckEditor.Active;
            if (session == null || session.Character == null || widget.Card == null || toggles == null) return;
            var rarity = session.Rarities.Selected(widget.Card);
            foreach (var toggle in toggles)
            {
                bool selected = (int)toggle.rarity == (int)rarity;
                if (selected && !toggle.isOn) toggle.SetToggleOn(false);
                else if (!selected && toggle.isOn) toggle.SetToggleOff(false);
            }
        }
    }
}
