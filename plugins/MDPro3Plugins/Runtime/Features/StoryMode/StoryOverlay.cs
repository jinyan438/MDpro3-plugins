using System;
using System.Linq;
using MDPro3.Servant;
using MDPro3.UI;
using TMPro;
using UnityEngine;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed class StoryOverlay : MonoBehaviour
    {
        private StoryModeFeature owner;
        private RectTransform page;
        private TextMeshProUGUI stats, status;
        private string selected;
        private int series, characterPage, level;
        private bool closing;
        private static readonly string[] Series = { "DM", "GX", "5D’s", "DSOD", "ZEXAL", "ARC-V", "VRAINS", "SEVENS", "Duel Links", "GO RUSH!!" };

        internal static StoryOverlay Open(StoryModeFeature feature)
        {
            var ui = PluginGame.UI;
            var root = StoryUI.Box("StoryMode", ui.popup != null ? ui.popup : ui.transform,
                0, 0, 1, 1, new Color(.015f, .035f, .06f));
            var overlay = root.gameObject.AddComponent<StoryOverlay>();
            overlay.owner = feature;
            overlay.Build();
            UIManager.InputBlocker = overlay;
            return overlay;
        }

        private void Build()
        {
            selected = owner.SelectedCharacter; series = owner.SelectedSeries; characterPage = owner.SelectedPage;
            level = Mathf.Clamp(owner.SelectedLevel, StoryProgress.MinLevel, StoryProgress.MaxLevel);
            StoryUI.Text(transform, "故事模式", .025f, .914f, .30f, .065f, 44);
            stats = StoryUI.Text(transform, "", .34f, .922f, .52f, .05f, 28, TextAlignmentOptions.MidlineRight);
            StoryUI.Button(transform, "返回", .89f, .922f, .085f, .05f, Back);
            StoryUI.Box("Accent", transform, .025f, .9f, .95f, .003f, StoryUI.Accent);
            StoryUI.Button(transform, "角色挑战", .025f, .825f, .19f, .06f, () => { if (!owner.Launching) ShowCharacters(); });
            StoryUI.Button(transform, "我的卡组", .225f, .825f, .19f, .06f, () => owner.EditDeck(null));
            StoryUI.Button(transform, "DP 卡包商店", .425f, .825f, .22f, .06f, () => { if (!owner.Launching) owner.OpenShop(); });
            StoryUI.Button(transform, "模型设置", .755f, .825f, .22f, .06f,
                () => { if (!owner.Launching) StoryModelSettings.Open(this); }).name = "OpenModelSettings";
            page = StoryUI.Rect("Page", transform, .025f, .068f, .95f, .73f);
            status = StoryUI.Text(transform, owner.Notice, .025f, .012f, .95f, .04f, 23);
            RefreshStats();
            ShowCharacters();
        }

        internal void RefreshStats()
        {
            var save = owner.Store.Current;
            int unlocked = owner.Packs.Count(owner.Unlocked);
            stats.text = save.dp + " DP   |   " + save.completedDuels + " 战 / " + save.wins + " 胜   |   卡包 " + unlocked + "/" + owner.Packs.Count;
        }

        internal void ModelSettingsSaved() { status.text = "模型设置已保存，下次挑战生效。"; }

        private void ShowCharacters()
        {
            StoryUI.Clear(page);
            for (int i = 0; i < Series.Length; i++)
            {
                int value = i;
                StoryUI.Button(page, Series[i], 0, .914f - i * .096f, .13f, .083f,
                    () => { series = value; characterPage = 0; selected = null; ShowCharacters(); }, i == series);
            }
            var characters = CharacterSelector.characters.GetSeriesCharacters(series.ToString("D2"))
                .Where(c => !c.notReady && !string.IsNullOrEmpty(c.id)).ToList();
            int pages = Math.Max(1, (characters.Count + 19) / 20);
            characterPage = Mathf.Clamp(characterPage, 0, pages - 1);
            if (selected == null && characters.Count > 0) selected = characters[characterPage * 20].id;
            StoryUI.Text(page, "选择对手 · " + characters.Count + " 位角色", .15f, .93f, .55f, .06f, 29);
            for (int i = 0; i < 20 && i + characterPage * 20 < characters.Count; i++)
            {
                var character = characters[i + characterPage * 20];
                string id = character.id;
                float x = .15f + (i % 5) * .112f, y = .72f - (i / 5) * .214f;
                var button = StoryUI.Button(page, "", x, y, .105f, .20f, () => { selected = id; ShowCharacters(); }, id == selected);
                StoryUI.CharacterPicture(button.transform, "sn" + id, .03f, .21f, .94f, .77f);
                var label = StoryUI.Text(button.transform, CharacterSelector.characters.GetName(id), .02f, .01f, .96f, .19f, 21, TextAlignmentOptions.Center);
                if (id == selected) label.color = Color.black;
            }
            StoryUI.Button(page, "上一页", .15f, 0, .14f, .064f, () => { characterPage--; ShowCharacters(); }).interactable = characterPage > 0;
            StoryUI.Text(page, (characterPage + 1) + " / " + pages, .30f, 0, .23f, .064f, 23, TextAlignmentOptions.Center);
            StoryUI.Button(page, "下一页", .55f, 0, .14f, .064f, () => { characterPage++; ShowCharacters(); }).interactable = characterPage < pages - 1;
            if (selected == null) return;
            var detail = StoryUI.Box("CharacterDetail", page, .735f, 0, .265f, 1, StoryUI.Panel);
            string name = CharacterSelector.characters.GetName(selected);
            StoryUI.Text(detail, name, .04f, .914f, .92f, .07f, 34, TextAlignmentOptions.Center);
            StoryUI.CharacterPicture(detail, "sn" + selected + "_2", .03f, .40f, .94f, .51f);
            string profile = Cid2Ydk.ReplaceWithCardName(CharacterSelector.characters.GetProfile(selected));
            StoryUI.Text(detail, profile.Replace("\n", ""), .06f, .235f, .88f, .16f, 22, TextAlignmentOptions.TopLeft);
            bool ready = owner.Store.Current.TryGetOpponent(selected, level, out var enemy);
            int configured = owner.Store.Current.opponents.TryGetValue(selected, out var levels) && levels != null ? levels.Count : 0;
            int wins = owner.Store.Current.characterWins.TryGetValue(selected, out int valueWins) ? valueWins : 0;
            StoryUI.Text(detail, "难度", .04f, .18f, .105f, .05f, 20, TextAlignmentOptions.Center);
            for (int i = StoryProgress.MinLevel; i <= StoryProgress.MaxLevel; i++)
            {
                int difficulty = i;
                bool hasDeck = owner.Store.Current.TryGetOpponent(selected, difficulty, out _);
                var button = StoryUI.Button(detail, difficulty.ToString(), .15f + (difficulty - 1) * .081f,
                    .18f, .077f, .05f, () => { level = difficulty; ShowCharacters(); }, difficulty == level);
                button.gameObject.name = "Difficulty" + difficulty;
                if (hasDeck && difficulty != level) button.targetGraphic.color = StoryUI.Configured;
            }
            StoryUI.Text(detail, (ready ? level + "级卡组：" + enemy.main.Count + " 张主卡" : level + "级卡组：尚未配置")
                + "  |  已配置 " + configured + "/10  |  战胜 " + wins + " 次",
                .04f, .125f, .92f, .047f, 20, TextAlignmentOptions.Center);
            StoryUI.Button(detail, "编辑 " + level + "级卡组 · 全卡库", .06f, .065f, .88f, .052f,
                () => owner.EditDeck(selected, level));
            StoryUI.Button(detail, ready ? "挑战 " + level + "级 · 胜利 +" + owner.Rules.WinReward(level) + " DP"
                    : "请先配置 " + level + "级卡组",
                .06f, .006f, .88f, .052f, () => owner.Challenge(selected, level), true).interactable = ready;
        }

        internal void ShowConnecting(string name, int level)
        {
            StoryUI.Clear(page);
            StoryUI.Text(page, "正在准备与 " + name + " 的 " + level + "级决斗…", .1f, .48f, .8f, .13f, 40, TextAlignmentOptions.Center);
            StoryUI.Text(page, "使用已保存的玩家卡组与该等级角色卡组。请在弹出的窗口中完成猜拳。", .1f, .34f, .8f, .1f, 25, TextAlignmentOptions.Center);
            StoryUI.Button(page, "取消挑战", .39f, .19f, .22f, .08f, () => owner.CancelChallenge());
        }

        private void Back()
        {
            if (owner.Launching) { owner.CancelChallenge(); return; }
            Close();
        }

        private void Update()
        {
            if (closing) return;
            if (PluginGame.CurrentPopup != null || UIManager.InputBlocker != this) return;
            if (UserInput.WasCancelPressed || UserInput.MouseRightDown) Back();
        }

        internal void Close()
        {
            if (closing) return;
            owner.SelectedCharacter = selected; owner.SelectedSeries = series; owner.SelectedPage = characterPage;
            owner.SelectedLevel = level;
            closing = true;
            if (ReferenceEquals(UIManager.InputBlocker, this)) UIManager.InputBlocker = null;
            gameObject.SetActive(false); Destroy(gameObject);
        }
        private void OnDestroy() { if (ReferenceEquals(UIManager.InputBlocker, this)) UIManager.InputBlocker = null; }
    }
}
