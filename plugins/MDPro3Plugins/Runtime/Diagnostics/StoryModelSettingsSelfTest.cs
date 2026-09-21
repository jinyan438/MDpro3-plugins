using System;
using System.IO;
using System.Linq;
using MDPro3.Plugins.Features.StoryMode;
using MDPro3.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Toggle = UnityEngine.UI.Toggle;

namespace MDPro3.Plugins.Diagnostics
{
    internal static class StoryModelSettingsSelfTest
    {
        private static int phase;
        private static float next;
        private static string root, endpoint;
        private static StoryModelSettings view;
        private static bool screenshot;

        internal static void Begin()
        {
            root = Path.GetDirectoryName(PluginConfig.LoadedPath);
            endpoint = Environment.GetEnvironmentVariable("STORY_SETTINGS_TEST_URL");
            Check(!string.IsNullOrEmpty(endpoint), "settings test endpoint configured");
            ClickHub();
        }

        internal static bool Tick()
        {
            if (Time.unscaledTime < next) return false;
            switch (phase)
            {
                case 0:
                    Check(UIManager.InputBlocker == view, "settings owns modal input");
                    Check(Field("ModelApiKey").contentType == TMP_InputField.ContentType.Password, "key initially masked");
                    Check(Field("ModelId").text == "deepseek-flash", "configured model loaded");
                    Field("ModelId").text = "unsaved-model";
                    Click("CancelModelSettings"); ClickHub();
                    Check(Field("ModelId").text == "deepseek-flash", "cancel discards edits");
                    Field("ModelApiAddress").text = "http://remote.invalid/v1";
                    Click("FetchModels"); Check(Status().Contains("HTTPS"), "unsafe endpoint shown as validation error");
                    Field("ModelApiAddress").text = endpoint + "/v1/chat/completions";
                    Field("ModelApiKey").text = "settings-test-key";
                    Click("FetchModels"); phase++; return false;
                case 1:
                    if (Status() == "正在拉取模型…") return false;
                    Check(Status().Contains("已获取 3 个模型"), "models GET parsed and deduplicated");
                    Check(Field("ModelId").text == "deepseek-flash", "fetch does not silently replace current model");
                    Check(Options().Length == 3, "model options populated");
                    Field("ModelSearch").text = "reasoner";
                    Check(Options().Length == 1, "model search filters options");
                    Options()[0].onClick.Invoke();
                    Check(Field("ModelId").text == "deepseek-reasoner", "click selects exact model ID");
                    Field("ModelSearch").text = "";
                    Find<Toggle>("RevealModelKey").isOn = true;
                    Check(Field("ModelApiKey").contentType == TMP_InputField.ContentType.Standard, "show key control works");
                    Find<Toggle>("RevealModelKey").isOn = false;
                    Find<Toggle>("ModelEnabled").isOn = true;
                    phase++; next = Time.unscaledTime + .5f; return false;
                case 2:
                    if (!screenshot)
                    {
                        screenshot = true;
                        var capture = new GameObject("SettingsScreenshot").AddComponent<StoryScreenshot>();
                        string directory = Path.Combine(root, "screenshots"); Directory.CreateDirectory(directory);
                        capture.StartCoroutine(capture.Capture(Path.Combine(directory, "model-settings.png")));
                        next = Time.unscaledTime + 1; return false;
                    }
                    Check(File.Exists(Path.Combine(root, "screenshots/model-settings.png")), "settings screenshot captured");
                    Check(view.GetComponentsInChildren<TextMeshProUGUI>().All(t => !t.richText), "remote IDs cannot inject rich text");
                    Click("SaveModelSettings");
                    var config = StoryModelConfig.Load(root);
                    Check((string)config["model"] == "deepseek-reasoner" && (string)config["baseUrl"] == endpoint + "/v1", "save persists endpoint and selection");
                    Check((int)config["maxModelCalls"] == 137, "save preserves request budget");
                    Check((string)config["thinkingApi"] == "llamacpp" && (string)config["thinkingMode"] == "disabled", "model list detects llama.cpp thinking protocol");
                    Check(StoryModelConfig.ResolveKey(root, config, _ => "old-environment-key") == "settings-test-key", "saved key used by duel resolver");
                    Check(!File.ReadAllText(Path.Combine(root, StoryModelConfig.FileName)).Contains("settings-test-key"), "key absent from JSON config");
                    Check(UIManager.InputBlocker is StoryOverlay, "save returns input to story hub");
                    ClickHub();
                    Check(Field("ModelApiKey").text == "settings-test-key" && Field("ModelApiKey").contentType == TMP_InputField.ContentType.Password, "reopen loads key masked");
                    Click("FetchModels"); phase++; return false;
                case 3:
                    if (Status() == "正在拉取模型…") return false;
                    Check(Status() == "密钥无效或已过期。", "HTTP error is actionable and sanitized");
                    Check(Field("ModelId").text == "deepseek-reasoner", "failed fetch retains model");
                    Click("FetchModels"); phase++; next = Time.unscaledTime + .4f; return false;
                case 4:
                    Check(Status() == "正在拉取模型…", "slow request remains asynchronous");
                    Click("CancelModelSettings"); ClickHub();
                    Check(Status() == "", "closing aborts request without leaking stale status");
                    Find<Toggle>("ModelEnabled").isOn = false;
                    Field("ModelId").text = "manual-model-id";
                    Click("SaveModelSettings"); ClickHub();
                    Check(!Find<Toggle>("ModelEnabled").isOn && Field("ModelId").text == "manual-model-id", "manual ID and disabled state persist");
                    Click("CloseModelSettings");
                    Check(UIManager.InputBlocker is StoryOverlay, "close restores hub input");
                    return true;
            }
            return false;
        }

        private static void ClickHub()
        {
            var overlay = UnityEngine.Object.FindFirstObjectByType<StoryOverlay>();
            overlay.GetComponentsInChildren<Button>().First(b => b.name == "OpenModelSettings").onClick.Invoke();
            view = overlay.GetComponentsInChildren<StoryModelSettings>().Single();
        }
        private static T Find<T>(string name) where T : Component => view.GetComponentsInChildren<T>().Single(c => c.name == name);
        private static TMP_InputField Field(string name) => Find<TMP_InputField>(name);
        private static void Click(string name) => Find<Button>(name).onClick.Invoke();
        private static string Status() => Find<TextMeshProUGUI>("ModelSettingsStatus").text;
        private static Button[] Options() => view.GetComponentsInChildren<Button>().Where(b => b.name == "ModelOption").ToArray();
        private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    }
}
