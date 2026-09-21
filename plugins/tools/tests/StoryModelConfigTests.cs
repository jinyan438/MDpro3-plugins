using System;
using System.IO;
using System.Linq;
using MDPro3.Plugins.Features.StoryMode;
using Newtonsoft.Json.Linq;

internal static class StoryModelConfigTests
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    private static void Reject(Action action, string label)
    { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, label); }

    internal static void Run(string directory)
    {
        string root = Path.Combine(directory, "model-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var original = StoryModelConfig.Load(root);
        Check((string)original["model"] == "deepseek-flash" && (bool)original["enabled"] == false, "model defaults");
        foreach (string address in new[] { "https://api.deepseek.com", "https://api.deepseek.com/", "https://api.deepseek.com/chat/completions", "https://api.deepseek.com/models" })
            Check(StoryModelConfig.Endpoint(address, true).AbsoluteUri == "https://api.deepseek.com/models", "models endpoint normalized");
        Check(StoryModelConfig.Endpoint("https://example.com/v1/chat/completions", true).AbsoluteUri == "https://example.com/v1/models", "version path preserved");
        foreach (string invalid in new[] { "file:///tmp", "http://remote.example/v1", "https://user:pass@host", "https://host?key=secret", "https://host/#frag", "not-a-url" })
            Reject(() => StoryModelConfig.Endpoint(invalid, true), "unsafe endpoint rejected");
        Check(StoryModelConfig.Local(StoryModelConfig.Endpoint("http://127.0.0.1:9000/v1", false)), "local HTTP accepted");
        Check(StoryModelConfig.Local(StoryModelConfig.Endpoint("http://[::1]:9000/v1", false)), "IPv6 loopback accepted");
        Reject(() => StoryModelConfig.ValidateKey("header\ninjection", new Uri("https://host"), true), "key control chars rejected");
        Reject(() => StoryModelConfig.ValidateKey("", new Uri("https://host"), true), "remote key required");
        var ids = StoryModelConfig.ParseModels("{\"data\":[{\"id\":\"b\"},{\"id\":\"a\"},{\"id\":\"b\"},{\"id\":3},{\"id\":\"\"},{\"id\":\"bad\\nname\"}]}");
        Check(ids.SequenceEqual(new[] { "a", "b" }), "model IDs filtered, distinct and sorted");
        Reject(() => StoryModelConfig.ParseModels("{\"error\":\"secret detail\"}"), "provider error is not model data");
        Reject(() => StoryModelConfig.ParseModels(new string('a', 1048577)), "oversized model list rejected");
        original["thinkingMode"] = "adaptive"; original["customProviderOption"] = 123;
        var local = StoryModelConfig.ProviderSettings(original, "http://127.0.0.1:8080/v1", "llamacpp");
        Check((string)local["thinkingApi"] == "llamacpp" && (string)local["thinkingMode"] == "disabled", "detected llama.cpp uses template thinking control");
        Check((string)original["thinkingMode"] == "adaptive" && original["thinkingApi"] == null, "provider detection does not mutate saved config");
        local["baseUrl"] = "http://127.0.0.1:8080/v1"; local["thinkingMode"] = "enabled";
        var same = StoryModelConfig.ProviderSettings(local, "http://127.0.0.1:8080/v1/chat/completions", "llamacpp");
        Check((string)same["thinkingMode"] == "enabled", "same provider retains explicit thinking preference");
        var deepseek = StoryModelConfig.ProviderSettings(local, "https://api.deepseek.com", null);
        Check((string)deepseek["thinkingApi"] == "deepseek", "switching to DeepSeek restores its request format");
        var generic = StoryModelConfig.ProviderSettings(local, "https://example.com/v1", null);
        Check(generic["thinkingApi"] == null && (string)generic["thinkingMode"] == "provider", "generic service does not inherit incompatible parameters");
        string firstKey = "test-local-key-one";
        var saved = StoryModelConfig.Save(root, original, true, "https://example.com/v1/chat/completions", "model-one", firstKey, "");
        Check((string)saved["baseUrl"] == "https://example.com/v1", "saved root fits duel client");
        Check((string)saved["thinkingMode"] == "adaptive" && (int)saved["customProviderOption"] == 123, "advanced config preserved");
        Check(!File.ReadAllText(Path.Combine(root, StoryModelConfig.FileName)).Contains(firstKey), "key excluded from JSON config");
        Check(StoryModelConfig.ResolveKey(root, saved, _ => "old-environment-key") == firstKey, "UI key overrides existing environment");
        Check((string)original["model"] == "deepseek-flash", "input config remains unchanged");
        var environmentConfig = (JObject)saved.DeepClone(); environmentConfig.Remove("apiKeySource");
        Check(StoryModelConfig.ResolveKey(root, environmentConfig, _ => "environment-key") == "environment-key", "legacy environment precedence preserved");
        string before = File.ReadAllText(Path.Combine(root, StoryModelConfig.FileName));
        Reject(() => StoryModelConfig.Save(root, saved, true, "https://host", "", "second-key", firstKey), "invalid model cannot overwrite key");
        Check(StoryModelConfig.ResolveKey(root, saved, _ => "") == firstKey && File.ReadAllText(Path.Combine(root, StoryModelConfig.FileName)) == before, "validation failure retains both files");
        using (File.Open(Path.Combine(root, StoryModelConfig.FileName), FileMode.Open, FileAccess.Read, FileShare.None))
            Reject(() => StoryModelConfig.Save(root, saved, true, "https://host", "new-model", "second-key", firstKey), "failed config replacement reported");
        Check(StoryModelConfig.ResolveKey(root, saved, _ => "") == firstKey, "failed config write restores previous secret");
        Check(Directory.GetFiles(root, "*.tmp").Length == 0, "no temporary credential files remain");
        saved = StoryModelConfig.Save(root, saved, false, "https://host", "", "", firstKey);
        Check(!((bool)saved["enabled"]) && StoryModelConfig.ResolveKey(root, saved, _ => "environment-key") == "", "disabled configuration can explicitly clear key");
        saved = StoryModelConfig.Save(root, saved, true, "http://localhost:9000/v1", "local-model", "", "");
        Check((bool)saved["allowAnonymousLocal"], "local model accepts empty key");
        Check((string)StoryModelConfig.Load(root)["model"] == "local-model", "saved configuration reloads");
        Console.WriteLine("StoryModelConfig tests: PASS (" + checks + " checks)");
    }
}
