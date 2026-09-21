using System.Collections.Generic;
using System.Reflection;
using MDPro3.Servant;
using MDPro3.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Uses native chat rows locally, without sending network chat, replay records
    // or OcgCore's on-board speech bubbles. All access is on Unity's main thread.
    internal sealed class StoryModelChat
    {
        private static readonly FieldInfo Template = Field("chatItemSystem");
        private static readonly FieldInfo Scroll = Field("scrollRect");
        private static readonly FieldInfo Items = Field("chatItems");
        private readonly List<GameObject> owned = new List<GameObject>();
        private ChatPanel panel;
        private ScrollRect scroll;
        private List<GameObject> items;
        private Text status;
        private GameObject statusRow;
        private bool opened;
        internal int MessageCount { get; private set; }
        internal string StatusText => status == null ? "" : status.text;
        private static FieldInfo Field(string name) => typeof(ChatPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        internal void Tick(StoryModelSession session)
        {
            if (session?.ShowChat != true || Program.instance == null
                || PluginGame.CurrentServant != Program.instance.ocgcore || RoomServant.CoreShowing != 2) return;
            panel = PluginGame.UI?.chatPanel;
            if (panel == null || Template?.GetValue(panel) is not GameObject template) return;
            scroll = Scroll?.GetValue(panel) as ScrollRect;
            items = Items?.GetValue(panel) as List<GameObject>;
            if (scroll == null || items == null) return;
            bool atBottom = scroll.verticalNormalizedPosition <= .03f || scroll.content.rect.height <= scroll.viewport.rect.height;
            bool added = false;
            if (status == null)
            {
                status = Add(template, ""); statusRow = status.transform.parent.gameObject;
                statusRow.name = "StoryModelChatStatus"; added = true;
            }
            while (session.TryReadChat(out var message))
            {
                Add(template, message); MessageCount++; added = true;
            }
            status.text = session.Status + "\n请求 " + session.ModelCalls + " 次 · 自动 " + session.AutomaticChoices
                + " 次 · 接管 " + session.Fallbacks + " 次\n最近耗时 " + (session.LastElapsedMs / 1000f).ToString("0.0") + " 秒";
            if (added)
            {
                // Keep one live status row after the completed decisions.
                items.Remove(statusRow); items.Add(statusRow); statusRow.transform.SetAsLastSibling();
                while (owned.Count > 129)
                {
                    var oldest = owned[1]; owned.RemoveAt(1); items.Remove(oldest); Object.Destroy(oldest);
                }
                Layout();
                if (atBottom) scroll.verticalNormalizedPosition = 0;
            }
            if (!opened && PluginGame.CurrentPopup == null)
            {
                // Calling the base overload avoids ChatPanel.Show(bool)'s input.Select().
                ((SidePanel)panel).Show(); opened = panel.showing;
                scroll.verticalNormalizedPosition = 0;
            }
        }

        private Text Add(GameObject template, string content)
        {
            var row = Object.Instantiate(template, scroll.content, false);
            row.name = "StoryModelChatMessage";
            foreach (var text in row.GetComponentsInChildren<Text>()) { text.text = ""; text.supportRichText = false; text.raycastTarget = false; }
            var label = row.transform.GetChild(2).GetComponent<Text>();
            label.text = content; label.alignment = TextAnchor.UpperLeft;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 18; label.resizeTextMaxSize = 26;
            row.GetComponent<Image>().raycastTarget = false;
            items.Add(row); owned.Add(row);
            return label;
        }

        private void Layout()
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null) items[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -i * 150);
            scroll.content.sizeDelta = new Vector2(0, items.Count * 150);
        }

        internal void Clear()
        {
            foreach (var row in owned) { items?.Remove(row); if (row != null) Object.Destroy(row); }
            if (scroll != null && items != null) Layout();
            if (opened && panel != null) panel.Hide();
            owned.Clear(); status = null; statusRow = null; panel = null; scroll = null; items = null;
            opened = false; MessageCount = 0;
        }
    }
}
