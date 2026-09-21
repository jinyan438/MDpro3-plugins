using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal static class StoryModelConfig
    {
        internal const string FileName = "story-ai.local.json";
        internal const string SecretFile = "story-ai.secret.local";
        internal const int MinTimeoutMs = 1000;
        internal const int MaxTimeoutMs = 300000;

        internal static JObject Load(string root)
        {
            string path = Path.Combine(root, FileName);
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 32768) throw new InvalidDataException();
                return JObject.Parse(File.ReadAllText(path));
            }
            return new JObject { ["enabled"] = false, ["baseUrl"] = "https://api.deepseek.com",
                ["model"] = "deepseek-flash", ["ygoAiRoot"] = "../ygo-ai", ["nodePath"] = "node",
                ["apiKeyEnv"] = "STORY_AI_API_KEY", ["apiKeyFile"] = SecretFile,
                ["timeoutMs"] = 12000, ["allowLocalFallback"] = true,
                ["maxTokens"] = 768, ["strategyTimeoutMs"] = 6000, ["strategyTokens"] = 1536 };
        }

        internal static int TimeoutMs(JObject config)
        {
            int value = (int?)config?["timeoutMs"] ?? 12000;
            return Math.Max(MinTimeoutMs, Math.Min(MaxTimeoutMs, value));
        }

        internal static bool AllowLocalFallback(JObject config) =>
            config?["allowLocalFallback"]?.Type != JTokenType.Boolean || (bool)config["allowLocalFallback"];

        internal static string KeyEnvironment(JObject config)
        {
            string name = (string)config["apiKeyEnv"] ?? "STORY_AI_API_KEY";
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidDataException();
            return name;
        }

        internal static string ResolveKey(string root, JObject config, Func<string, string> environment = null)
        {
            string name = KeyEnvironment(config);
            if ((string)config["apiKeySource"] != "file")
            {
                string value = (environment ?? Environment.GetEnvironmentVariable)(name);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            string file = (string)config["apiKeyFile"];
            if (string.IsNullOrEmpty(file)) return "";
            string path = Path.GetFullPath(Path.Combine(root, file));
            if (!File.Exists(path)) return "";
            if (new FileInfo(path).Length > 8192) throw new InvalidDataException();
            return File.ReadAllText(path).Trim();
        }

        internal static Uri Endpoint(string address, bool models)
        {
            if (string.IsNullOrWhiteSpace(address) || address.Length > 2048 || address.Any(char.IsControl)
                || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != "https" && uri.Scheme != "http")
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || (uri.Scheme == "http" && !Local(uri)))
                throw new ArgumentException("API 地址无效；远程接口须使用 HTTPS，本机接口可使用 HTTP。");
            var builder = new UriBuilder(uri);
            string path = uri.AbsolutePath.TrimEnd('/');
            foreach (string suffix in new[] { "/chat/completions", "/models" })
                if (path.EndsWith(suffix, StringComparison.Ordinal)) { path = path.Substring(0, path.Length - suffix.Length); break; }
            builder.Path = path + (models ? "/models" : "");
            return builder.Uri;
        }

        internal static bool Local(Uri uri) => uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "[::1]" || uri.Host == "::1";

        internal static JObject ProviderSettings(JObject original, string address, string detectedApi)
        {
            var next = (JObject)original.DeepClone();
            var endpoint = Endpoint(address, false);
            bool sameEndpoint = false;
            try { sameEndpoint = endpoint == Endpoint((string)original["baseUrl"], false); }
            catch (ArgumentException) { }
            if (endpoint.Host == "api.deepseek.com") next["thinkingApi"] = "deepseek";
            else if (detectedApi == "llamacpp")
            {
                if (!sameEndpoint || (string)original["thinkingApi"] != "llamacpp") next["thinkingMode"] = "disabled";
                next["thinkingApi"] = "llamacpp";
            }
            else if (!sameEndpoint)
            {
                next.Remove("thinkingApi");
                next["thinkingMode"] = "provider";
            }
            return next;
        }

        internal static void ValidateKey(string key, Uri endpoint, bool required)
        {
            if (key.Length > 8192 || key.Any(c => c < 33 || c > 126)) throw new ArgumentException("密钥格式无效。");
            if (required && !Local(endpoint) && string.IsNullOrWhiteSpace(key)) throw new ArgumentException("请填写 API 密钥。");
        }

        internal static string[] ParseModels(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1048576) throw new InvalidDataException();
            var data = JObject.Parse(json)["data"] as JArray;
            if (data == null || data.Count > 2000) throw new InvalidDataException();
            return data.OfType<JObject>().Select(item => item["id"])
                .Where(id => id?.Type == JTokenType.String).Select(id => ((string)id).Trim())
                .Where(id => id.Length > 0 && id.Length <= 200 && !id.Any(char.IsControl))
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        internal static JObject Save(string root, JObject original, bool enabled, string address, string model, string key, string previousKey)
            => Save(root, original, enabled, address, model, key, previousKey, TimeoutMs(original), AllowLocalFallback(original));

        internal static JObject Save(string root, JObject original, bool enabled, string address, string model, string key,
            string previousKey, int timeoutMs, bool allowLocalFallback)
        {
            var endpoint = Endpoint(address, false);
            key = key.Trim(); model = model.Trim();
            ValidateKey(key, endpoint, enabled);
            if (model.Length > 200 || model.Any(char.IsControl) || (enabled && model.Length == 0))
                throw new ArgumentException("请填写有效的模型 ID。");
            if (timeoutMs < MinTimeoutMs || timeoutMs > MaxTimeoutMs)
                throw new ArgumentException("请求超时必须在 1–300 秒之间。");
            var next = (JObject)original.DeepClone();
            next["enabled"] = enabled; next["baseUrl"] = endpoint.AbsoluteUri.TrimEnd('/'); next["model"] = model;
            next["timeoutMs"] = timeoutMs; next["allowLocalFallback"] = allowLocalFallback;
            next["allowAnonymousLocal"] = Local(endpoint) && key.Length == 0;
            bool changedKey = key != previousKey;
            if (changedKey)
            {
                next["apiKeySource"] = "file"; next["apiKeyFile"] = SecretFile;
            }
            KeyEnvironment(next);
            string json = next.ToString(Formatting.Indented);
            if (Encoding.UTF8.GetByteCount(json) > 32768) throw new InvalidDataException();
            string secret = Path.Combine(root, SecretFile);
            byte[] previous = changedKey && File.Exists(secret) ? File.ReadAllBytes(secret) : null;
            try
            {
                if (changedKey) AtomicWrite(secret, Encoding.UTF8.GetBytes(key));
                AtomicWrite(Path.Combine(root, FileName), new UTF8Encoding(false).GetBytes(json));
            }
            catch
            {
                if (changedKey)
                {
                    if (previous != null) AtomicWrite(secret, previous);
                    else if (File.Exists(secret)) File.Delete(secret);
                }
                throw;
            }
            return next;
        }

        private static void AtomicWrite(string path, byte[] data)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
