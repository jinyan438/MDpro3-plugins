using System;
using System.Collections;
using System.IO;
using System.Linq;
using MDPro3.UI;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Toggle = UnityEngine.UI.Toggle;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed class StoryModelSettings : MonoBehaviour
    {
        private StoryOverlay owner;
        private string root, savedKey;
        private JObject config;
        private TMP_InputField address, key, model, search, timeout;
        private Toggle modelEnabled, modelOnly, reveal;
        private Button fetch, save;
        private TextMeshProUGUI status, modelCount;
        private RectTransform list;
        private string[] models = Array.Empty<string>();
        private UnityWebRequest request;
        private Coroutine loading;
        private bool closing;
        private string detectedThinkingApi;

        internal static void Open(StoryOverlay overlay)
        {
            var rect = StoryUI.Box("StoryModelSettings", overlay.transform, 0, 0, 1, 1, new Color(0, 0, 0, .8f));
            var view = rect.gameObject.AddComponent<StoryModelSettings>();
            view.owner = overlay;
            view.root = PluginConfig.Found ? Path.GetDirectoryName(PluginConfig.LoadedPath) : Path.Combine(Directory.GetCurrentDirectory(), "plugins");
            view.Build();
            UIManager.InputBlocker = view;
        }

        private void Build()
        {
            var panel = StoryUI.Box("SettingsPanel", transform, .19f, .09f, .62f, .82f, new Color(.025f, .06f, .085f));
            StoryUI.Text(panel, "对战模型设置", .04f, .9f, .72f, .065f, 34);
            StoryUI.Button(panel, "×", .91f, .91f, .05f, .05f, Close).name = "CloseModelSettings";
            StoryUI.Box("Accent", panel, .04f, .88f, .92f, .003f, StoryUI.Accent);
            modelEnabled = CheckBox(panel, "启用大模型对手", .04f, .805f, .44f, .06f);
            modelEnabled.name = "ModelEnabled";
            StoryUI.Text(panel, "请求超时(秒)", .53f, .805f, .18f, .065f, 21);
            timeout = Field(panel, "ModelTimeoutSeconds", "12", .72f, .805f, .22f, 6);
            modelOnly = CheckBox(panel, "仅允许模型操作（失败时结束本局）", .04f, .735f, .7f, .06f);
            modelOnly.name = "ModelOnly";
            StoryUI.Text(panel, "API 地址", .04f, .665f, .17f, .065f, 24);
            address = Field(panel, "ModelApiAddress", "https://api.deepseek.com", .23f, .665f, .73f, 2048);
            StoryUI.Text(panel, "API 密钥", .04f, .575f, .17f, .065f, 24);
            key = Field(panel, "ModelApiKey", "API Key", .23f, .575f, .53f, 8192);
            key.contentType = TMP_InputField.ContentType.Password;
            reveal = CheckBox(panel, "显示", .79f, .575f, .17f, .065f);
            reveal.name = "RevealModelKey";
            reveal.onValueChanged.AddListener(value => { key.contentType = value ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password; key.ForceLabelUpdate(); });
            StoryUI.Text(panel, "模型 ID", .04f, .485f, .17f, .065f, 24);
            model = Field(panel, "ModelId", "deepseek-flash", .23f, .485f, .51f, 200);
            fetch = StoryUI.Button(panel, "拉取模型", .77f, .485f, .19f, .065f, FetchModels);
            fetch.name = "FetchModels";
            modelCount = StoryUI.Text(panel, "可用模型", .23f, .417f, .31f, .05f, 21);
            search = Field(panel, "ModelSearch", "筛选模型", .57f, .411f, .39f, 200, .05f);
            search.onValueChanged.AddListener(_ => RenderModels());
            list = StoryUI.Scroll(panel, .23f, .145f, .73f, .244f);
            status = StoryUI.Text(panel, "", .04f, .075f, .92f, .055f, 20);
            status.name = "ModelSettingsStatus";
            StoryUI.Button(panel, "取消", .55f, .027f, .19f, .065f, Close).name = "CancelModelSettings";
            save = StoryUI.Button(panel, "保存", .77f, .027f, .19f, .065f, Save, true);
            save.name = "SaveModelSettings";
            try
            {
                config = StoryModelConfig.Load(root); savedKey = StoryModelConfig.ResolveKey(root, config);
                modelEnabled.isOn = (bool?)config["enabled"] == true;
                modelOnly.isOn = !StoryModelConfig.AllowLocalFallback(config);
                timeout.text = (StoryModelConfig.TimeoutMs(config) / 1000f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                address.text = (string)config["baseUrl"] ?? "https://api.deepseek.com";
                model.text = (string)config["model"] ?? "deepseek-flash";
                key.text = savedKey;
            }
            catch { SetStatus("无法读取模型配置或密钥文件，请检查本机配置。", true); fetch.interactable = save.interactable = false; }
            address.onValueChanged.AddListener(_ => InvalidateModels());
            key.onValueChanged.AddListener(_ => InvalidateModels());
            RenderModels();
        }

        private static TMP_InputField Field(Transform parent, string name, string placeholder, float x, float y, float w, int limit, float height = .065f)
        {
            var field = StoryUI.Input(parent, placeholder, x, y, w, height);
            field.name = name; field.characterLimit = limit;
            field.textComponent.enableAutoSizing = false; field.textComponent.fontSize = 24;
            field.textComponent.overflowMode = TextOverflowModes.Overflow;
            field.richText = false;
            return field;
        }

        private static Toggle CheckBox(Transform parent, string caption, float x, float y, float w, float h)
        {
            var row = StoryUI.Rect(caption, parent, x, y, w, h);
            var hit = row.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var box = StoryUI.Box("Box", row, 0, .14f, 0, .72f, StoryUI.Panel);
            box.sizeDelta = new Vector2(30, 0);
            var mark = StoryUI.Box("Check", box, .2f, .2f, .6f, .6f, StoryUI.Accent).GetComponent<Image>();
            var toggle = row.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = hit; toggle.graphic = mark;
            var label = StoryUI.Text(row, caption, 0, 0, 1, 1, 24);
            label.rectTransform.offsetMin = new Vector2(40, 0);
            toggle.isOn = false;
            return toggle;
        }

        private void InvalidateModels()
        {
            CancelFetch(); detectedThinkingApi = null; models = Array.Empty<string>(); RenderModels(); status.text = "";
        }

        private void RenderModels()
        {
            StoryUI.Clear(list);
            var matches = models.Where(id => id.IndexOf(search.text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            modelCount.text = "可用模型" + (models.Length > 0 ? " · " + matches.Length + "/" + models.Length : "");
            list.sizeDelta = new Vector2(0, Math.Max(48, matches.Length * 44));
            list.anchoredPosition = Vector2.zero;
            if (matches.Length == 0) StoryUI.Text(list, models.Length == 0 ? "尚未获取模型列表" : "没有匹配的模型", .02f, 0, .96f, 1, 21);
            for (int i = 0; i < matches.Length; i++)
            {
                string id = matches[i];
                var button = StoryUI.Button(list, id, 0, 1f - (i + 1f) / matches.Length, 1, 1f / matches.Length,
                    () => { model.text = id; RenderModels(); }, id == model.text);
                button.name = "ModelOption";
                var text = button.GetComponentInChildren<TextMeshProUGUI>();
                text.alignment = TextAlignmentOptions.MidlineLeft; text.fontSizeMax = 23;
            }
        }

        private void FetchModels()
        {
            if (request != null) { CancelFetch(); SetStatus("已取消拉取。"); return; }
            try
            {
                var endpoint = StoryModelConfig.Endpoint(address.text, true);
                string credential = key.text.Trim(); StoryModelConfig.ValidateKey(credential, endpoint, true);
                request = UnityWebRequest.Get(endpoint.AbsoluteUri);
                request.timeout = 12; request.redirectLimit = 0;
                request.SetRequestHeader("Accept", "application/json");
                if (credential.Length > 0) request.SetRequestHeader("Authorization", "Bearer " + credential);
                SetStatus("正在拉取模型…"); fetch.GetComponentInChildren<TextMeshProUGUI>().text = "取消拉取";
                loading = StartCoroutine(ReadModels());
            }
            catch (ArgumentException ex) { CancelFetch(); SetStatus(ex.Message, true); }
            catch { CancelFetch(); SetStatus("无法发起模型请求，请检查 API 地址。", true); }
        }

        private IEnumerator ReadModels()
        {
            var active = request;
            var operation = active.SendWebRequest();
            while (!operation.isDone)
            {
                if (active.downloadedBytes > 1048576) { active.Abort(); break; }
                yield return null;
            }
            if (request != active || closing) yield break;
            try
            {
                if (active.result != UnityWebRequest.Result.Success)
                {
                    string message = active.responseCode == 401 ? "密钥无效或已过期。"
                        : active.responseCode == 402 ? "接口余额不足或付费受限。"
                        : active.responseCode == 403 ? "没有访问模型列表的权限。"
                        : active.responseCode == 404 ? "此地址没有模型列表接口，可手动填写模型 ID。"
                        : active.responseCode == 429 ? "请求过于频繁，请稍后重试。"
                        : active.responseCode >= 300 && active.responseCode < 400 ? "接口发生重定向，请填写最终 API 地址。"
                        : "拉取失败，请检查网络、API 地址或稍后重试。";
                    SetStatus(message, true);
                }
                else
                {
                    if (active.downloadedBytes > 1048576) throw new InvalidDataException();
                    models = StoryModelConfig.ParseModels(active.downloadHandler.text);
                    detectedThinkingApi = (active.GetResponseHeader("Server") ?? "").IndexOf("llama.cpp", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "llamacpp" : null;
                    search.SetTextWithoutNotify(""); RenderModels();
                    SetStatus(models.Length > 0 ? "已获取 " + models.Length + " 个模型。" : "接口未返回可用模型，可手动填写模型 ID。");
                }
            }
            catch { SetStatus("模型列表格式无效，可手动填写模型 ID。", true); }
            finally { active.Dispose(); request = null; loading = null; fetch.GetComponentInChildren<TextMeshProUGUI>().text = "拉取模型"; }
        }

        private void Save()
        {
            try
            {
                if (!float.TryParse(timeout.text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                    || seconds < StoryModelConfig.MinTimeoutMs / 1000f || seconds > StoryModelConfig.MaxTimeoutMs / 1000f)
                    throw new ArgumentException("请求超时必须在 1–300 秒之间。");
                int timeoutMs = (int)Math.Round(seconds * 1000f);
                var draft = StoryModelConfig.ProviderSettings(config, address.text, detectedThinkingApi);
                config = StoryModelConfig.Save(root, draft, modelEnabled.isOn, address.text, model.text, key.text, savedKey,
                    timeoutMs, !modelOnly.isOn);
                savedKey = key.text.Trim(); CancelFetch();
                owner.ModelSettingsSaved(); Close();
            }
            catch (ArgumentException ex) { SetStatus(ex.Message, true); }
            catch { SetStatus("保存失败，请检查配置文件和目录写入权限。", true); }
        }

        private void SetStatus(string text, bool error = false)
        { status.text = text; status.color = error ? new Color(1, .55f, .45f) : Color.white; }

        private void CancelFetch()
        {
            if (loading != null) { StopCoroutine(loading); loading = null; }
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (fetch != null) fetch.GetComponentInChildren<TextMeshProUGUI>().text = "拉取模型";
        }

        private void Update()
        {
            if (!closing && UIManager.InputBlocker == this && PluginGame.CurrentPopup == null
                && (UserInput.WasCancelPressed || UserInput.MouseRightDown)) Close();
        }

        private void Close()
        {
            if (closing) return;
            closing = true; CancelFetch();
            key.SetTextWithoutNotify(""); savedKey = null;
            if (UIManager.InputBlocker == this) UIManager.InputBlocker = owner != null && owner.isActiveAndEnabled ? owner : null;
            gameObject.SetActive(false); Destroy(gameObject);
        }

        private void OnDisable() { CancelFetch(); }
        private void OnDestroy()
        {
            if (UIManager.InputBlocker == this) UIManager.InputBlocker = owner != null && owner.isActiveAndEnabled ? owner : null;
            savedKey = null;
        }

    }
}
