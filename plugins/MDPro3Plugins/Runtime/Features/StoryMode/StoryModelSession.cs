using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Created by the story owner, captured by exactly one bot, disposed on every exit path.
    // All waits/HTTP work run on WindBot's background thread, never Unity's main thread.
    internal sealed class StoryModelSession : IDisposable
    {
        private readonly object lifecycle = new object();
        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> chatMessages = new ConcurrentQueue<string>();
        private readonly object received = new object();
        private readonly string id, root;
        private readonly JObject config;
        private Process process;
        private volatile bool stopped, exited;
        private bool started, modelDisabled;
        private int step, modelChoices, automaticChoices, fallbacks, modelCalls, lastElapsedMs, terminationRequested;
        private volatile string status;
        internal string Status => status;
        internal int ModelChoices => modelChoices;
        internal int Fallbacks => fallbacks;
        internal bool Stopped => stopped;
        internal int AutomaticChoices => automaticChoices;
        internal int ModelCalls => modelCalls;
        internal int LastElapsedMs => lastElapsedMs;
        internal bool ShowChat => Enabled;
        internal bool TryReadChat(out string message) => chatMessages.TryDequeue(out message);
        internal int ProcessId { get; private set; }
        internal bool CanAttempt => Enabled && !modelDisabled && !stopped;
        internal bool AllowLocalFallback => StoryModelConfig.AllowLocalFallback(config);
        // A disabled model must preserve the normal story AI path. Model-only
        // termination applies only after the user enabled the model integration.
        internal bool TryRequestModelOnlyTermination() => Enabled && !AllowLocalFallback
            && Interlocked.Exchange(ref terminationRequested, 1) == 0;

        internal StoryModelSession(string pluginRoot, string sessionId)
        {
            root = Path.GetFullPath(pluginRoot); id = sessionId;
            try
            {
                string file = Path.Combine(root, "story-ai.local.json");
                if (!File.Exists(file)) { status = "故事 AI：本地策略（尚未配置大模型）"; return; }
                config = StoryModelConfig.Load(root);
                status = Enabled ? "故事 AI：大模型准备中" : "故事 AI：本地策略";
                if (Enabled && !StoryModelHooks.Installed()) { status = "故事 AI：接入钩子未安装，使用本地策略"; modelDisabled = true; }
            }
            catch { status = "故事 AI：配置格式有误，使用本地策略"; }
        }

        private bool Enabled => config?["enabled"]?.Type == JTokenType.Boolean && (bool)config["enabled"];
        private int Timeout => StoryModelConfig.TimeoutMs(config);
        private static string Quote(string path) => "\"" + path.Replace("\"", "") + "\"";

        private bool Start()
        {
            if (!CanAttempt) return false;
            if (started) return process != null && !exited;
            started = true;
            try
            {
                var repo = (string)config["ygoAiRoot"] ?? "../ygo-ai";
                repo = Path.GetFullPath(Path.Combine(root, Environment.ExpandEnvironmentVariables(repo)));
                var script = Path.Combine(repo, "skill/backend/mdpro3/service.mjs");
                if (!File.Exists(script)) { Unavailable("找不到 ygo-ai 服务"); return false; }
                var node = Environment.ExpandEnvironmentVariables((string)config["nodePath"] ?? "node");
                if (node.IndexOfAny(new[] { '/', '\\' }) >= 0) node = Path.GetFullPath(Path.Combine(root, node));
                var start = new ProcessStartInfo { FileName = node, Arguments = Quote(script), WorkingDirectory = repo,
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                string keyEnv = StoryModelConfig.KeyEnvironment(config);
                start.EnvironmentVariables[keyEnv] = StoryModelConfig.ResolveKey(root, config);
                lock (lifecycle)
                {
                    if (stopped) return false;
                    process = new Process { StartInfo = start, EnableRaisingEvents = true };
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data == null) { exited = true; Wake(); return; }
                        if (e.Data.Length > 2 * 1024 * 1024 || incoming.Count > 8) { Dispose(); return; }
                        incoming.Enqueue(e.Data); Wake();
                    };
                    // Drain stderr but never copy provider output or credentials to Unity logs.
                    process.ErrorDataReceived += (_, e) => { };
                    process.Exited += (_, e) => { exited = true; Wake(); };
                    process.Start(); ProcessId = process.Id;
                    process.StandardInput.AutoFlush = true;
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                }
                Send(new JObject { ["type"] = "start", ["version"] = 1, ["session"] = id, ["config"] = config.DeepClone() });
                var ready = Read(Stopwatch.StartNew(), 10000);
                if ((string)ready?["type"] != "ready" || (string)ready["session"] != id || (int?)ready["version"] != 1
                    || (string)ready["status"] != "ok")
                {
                    Unavailable((string)ready?["reason"] == "missing_api_key" ? "未读取到密钥" : "服务配置不可用");
                    Dispose(); return false;
                }
                status = "故事 AI：大模型已连接"; return true;
            }
            catch { Unavailable("服务启动失败"); Dispose(); return false; }
        }

        internal byte[] Decide(byte[] packet, JObject state, int hint)
        {
            if (!Start()) return null;
            int current = ++step;
            try
            {
                status = "故事 AI：正在决策";
                SendDecision(current, packet, state, hint);
                var timer = Stopwatch.StartNew(); bool cardsSent = false;
                while (!stopped)
                {
                    var message = Read(timer, Timeout + 6000);
                    if (message == null || (int?)message["version"] != 1 || (string)message["session"] != id
                        || (int?)message["step"] != current) throw new InvalidDataException();
                    if ((string)message["type"] == "progress")
                    {
                        modelCalls = Metric(message, "modelCalls");
                        status = "故事 AI：正在决策（请求 " + modelCalls + "）";
                        continue;
                    }
                    if ((string)message["type"] == "cards" && !cardsSent)
                    {
                        cardsSent = true;
                        var ids = message["ids"] as JArray;
                        if (ids == null || ids.Count > 2048) throw new InvalidDataException();
                        var cards = new JArray();
                        foreach (var token in ids)
                        {
                            if (token.Type != JTokenType.Integer) throw new InvalidDataException();
                            int code = (int)token;
                            var card = MDPro3.Duel.YGOSharp.CardsManager.GetCardRaw(code);
                            if (card != null) cards.Add(new JObject { ["code"] = code, ["name"] = card.Name,
                                ["text"] = card.Desc, ["strings"] = JArray.FromObject(card.Str ?? new string[0]),
                                ["type"] = card.Type, ["level"] = card.Level, ["attack"] = card.Attack, ["defense"] = card.Defense,
                                ["race"] = card.Race, ["attribute"] = card.Attribute });
                        }
                        var descriptions = message["descriptions"] as JArray ?? new JArray();
                        if (descriptions.Count > 2048) throw new InvalidDataException();
                        var texts = new JArray();
                        foreach (var token in descriptions)
                        {
                            if (token.Type != JTokenType.Integer) throw new InvalidDataException();
                            int description = (int)token;
                            texts.Add(new JObject { ["descriptionId"] = description, ["text"] = StringHelper.Get(description) });
                        }
                        Send(new JObject { ["type"] = "cards_result", ["version"] = 1, ["session"] = id,
                            ["step"] = current, ["cards"] = cards, ["descriptions"] = texts });
                        Diagnostics.StoryModelSelfTest.CaptureDecision(root, packet, state, hint, cards, texts);
                        continue;
                    }
                    if ((string)message["type"] != "result") throw new InvalidDataException();
                    Diagnostics.StoryModelSelfTest.RecordDecision(packet, state, hint, message);
                    var metrics = message["metrics"] as JObject;
                    modelCalls = Metric(metrics, "modelCalls");
                    if (Metric(metrics, "apiCalls") > 0) lastElapsedMs = Metric(metrics, "elapsedMs");
                    if ((string)message["status"] != "ok")
                    {
                        string reason = (string)message["reason"];
                        if ((string)message["status"] == "retry" && reason == "invalid_model_json")
                        {
                            status = "故事 AI：模型未返回有效 JSON，正在重试（请求 " + modelCalls + "）";
                            Publish(status);
                            current = ++step; cardsSent = false; timer.Restart();
                            SendDecision(current, packet, state, hint);
                            continue;
                        }
                        if (reason == "http_401" || reason == "http_402" || reason == "http_403") modelDisabled = true;
                        if (AllowLocalFallback)
                        {
                            Interlocked.Increment(ref fallbacks);
                            status = "故事 AI：" + (modelDisabled ? "本局" : "本次") + "由本地策略接管（" + Reason(reason) + "）";
                        }
                        else
                        {
                            status = "故事 AI：" + Reason(reason) + "，未启用本地 AI 接管，本局结束";
                            Dispose();
                        }
                        Publish(status);
                        if (Diagnostics.StoryModelSelfTest.Active) UnityEngine.Debug.Log("[StoryModelSelfTest] fallback message="
                            + packet[0] + " reason=" + Reason((string)message["reason"]));
                        return null;
                    }
                    var response = Convert.FromBase64String((string)message["response"] ?? "");
                    if (response.Length < 1 || response.Length > 512) throw new InvalidDataException();
                    if ((string)message["source"] == "model")
                    {
                        Interlocked.Increment(ref modelChoices);
                        string summary = (string)message["summary"] ?? "已选择本次行动。";
                        summary = new string(summary.Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(80).ToArray());
                        Publish("故事 AI · 请求 " + modelCalls + " · " + (lastElapsedMs / 1000f).ToString("0.0") + " 秒\n" + summary);
                        if (Diagnostics.StoryModelSelfTest.Active) UnityEngine.Debug.Log("[StoryModelSelfTest] decision metrics=" + metrics?.ToString(Formatting.None));
                    }
                    else Interlocked.Increment(ref automaticChoices);
                    status = "故事 AI：等待下一步";
                    return stopped ? null : response;
                }
            }
            catch
            {
                if (AllowLocalFallback)
                {
                    status = "故事 AI：连接中断，本局由本地策略接管";
                    Interlocked.Increment(ref fallbacks);
                }
                else status = "故事 AI：连接中断，未启用本地 AI 接管，本局结束";
                Publish(status); Dispose();
            }
            return null;
        }

        private void SendDecision(int current, byte[] packet, JObject state, int hint)
        {
            Send(new JObject { ["type"] = "decision", ["version"] = 1, ["session"] = id, ["step"] = current,
                ["packet"] = Convert.ToBase64String(packet), ["state"] = state, ["hint"] = hint });
        }

        private void Unavailable(string reason)
        {
            status = AllowLocalFallback ? "故事 AI：" + reason + "，使用本地策略"
                : "故事 AI：" + reason + "，未启用本地 AI 接管，本局结束";
            if (!AllowLocalFallback) Publish(status);
        }

        private static string Reason(string reason)
        {
            switch (reason)
            {
                case "model_timeout": return "模型超时";
                case "connection_refused": return "连接被拒绝，请检查 API 端口和服务是否启动";
                case "host_not_found": return "找不到 API 主机，请检查地址";
                case "connection_failed": return "无法连接 API，请检查网络或证书";
                case "http_400": return "接口拒绝请求参数，请检查模型和接口兼容性";
                case "http_404": return "接口或模型不存在，请检查地址路径和模型 ID";
                case "http_429": return "接口限流，请稍后重试";
                case "http_503": return "模型服务尚未就绪或暂不可用";
                case "invalid_model_json": return "模型未返回有效 JSON";
                case "invalid_model_choice": return "模型选择不符合当前候选";
                case "missing_plan": return "模型未提供有效展开计划";
                case "output_truncated": return "模型输出被截断，请调整输出上限或思考模式";
                case "http_401": return "接口密钥无效";
                case "http_402": return "接口余额不足或付费受限";
                case "http_403": return "接口拒绝访问";
                case "call_budget": return "达到本局调用上限";
                case "context_budget": return "场面超过上下文上限";
                case "circuit_open": return "服务暂不可用，稍后重试";
                case "unsupported_or_invalid_packet": return "特殊协议选择";
                default: return "模型响应不可用";
            }
        }

        private static int Metric(JObject metrics, string name)
        {
            var token = metrics?[name];
            return token?.Type == JTokenType.Integer ? (int)Math.Max(0, Math.Min(100000000, (long)token)) : 0;
        }

        private void Publish(string message)
        {
            while (chatMessages.Count >= 128) chatMessages.TryDequeue(out _);
            chatMessages.Enqueue(message);
        }

        internal void RejectedByEngine()
        {
            if (AllowLocalFallback)
            {
                Interlocked.Increment(ref fallbacks);
                status = "故事 AI：引擎拒绝模型选择，本局由本地策略接管";
            }
            else status = "故事 AI：引擎拒绝模型选择，未启用本地 AI 接管，本局结束";
            Publish(status);
            Dispose();
        }

        internal void IntegrationFailure()
        {
            if (AllowLocalFallback)
            {
                Interlocked.Increment(ref fallbacks);
                status = "故事 AI：接入异常，本局由本地策略接管";
            }
            else status = "故事 AI：接入异常，未启用本地 AI 接管，本局结束";
            Publish(status);
            Dispose();
        }

        private void Send(JObject value)
        {
            string line = value.ToString(Formatting.None);
            if (line.Length > 2 * 1024 * 1024) throw new InvalidDataException();
            if (stopped || process == null || exited) throw new IOException();
            if (!process.StandardInput.WriteLineAsync(line).Wait(5000)) throw new IOException();
        }

        private JObject Read(Stopwatch timer, int timeout)
        {
            while (!stopped)
            {
                if (incoming.TryDequeue(out var line)) return JObject.Parse(line);
                int remaining = timeout - (int)timer.ElapsedMilliseconds;
                if (exited || remaining <= 0) return null;
                lock (received) { if (incoming.IsEmpty && !stopped && !exited) Monitor.Wait(received, Math.Min(remaining, 100)); }
            }
            return null;
        }

        public void Dispose()
        {
            lock (lifecycle)
            {
                if (stopped) return;
                stopped = true; Wake();
                if (process == null) return;
                // Kill first: closing a StreamWriter can flush a blocked pipe on the UI thread.
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
                process = null;
            }
        }

        private void Wake() { lock (received) Monitor.PulseAll(received); }
    }
}
