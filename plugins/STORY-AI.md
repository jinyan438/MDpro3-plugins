# 故事模式大模型对手

故事模式可将当前 WindBot 的决策交给 `ygo-ai` 中的 OpenAI 兼容服务。普通人机模式不受影响。未配置、禁用或服务故障时，仍使用插件内独立的 `StoryLuckyExecutor`。

## 本机使用

1. 安装 Node.js 22 或更新版本，保留 `plugins` 与 `ygo-ai` 两个目录。
2. 使用原来的 `Run_MDPro3.bat` 重建并启动，进入故事模式，点击右上方的「模型设置」。
3. 打开启用开关，填写 API 地址和密钥，点击「拉取模型」，从列表选定模型并保存。也可以直接填写模型 ID；默认是 `https://api.deepseek.com`、`deepseek-flash`。
4. 模型配置在每次开始挑战时读取。设置保存后，下次挑战生效，无需再次重建。
5. 在故事模式保存角色对应难度的卡组并挑战。右侧原生聊天框显示模型的简短行动说明、实际 API 请求次数、自动选择和本地接管次数、最近请求耗时。底部不再显示模型状态。聊天框只自动打开一次，手动关闭后仍会保存本局消息。

设置支持带 `/v1` 的 API 根地址，以及完整 `/chat/completions` 地址，拉取模型时对应请求同一前缀下的 `/models`。列表支持筛选，拉取不会自动替换当前模型；不支持模型列表的服务可以手动填写 ID。远程接口要求 HTTPS，本机接口允许 HTTP 和空密钥。拉取限时 12 秒、限制响应大小且不跟随重定向；关闭弹窗或修改地址/密钥会取消未完成请求。取消关闭不会保存输入。

密钥默认遮蔽显示，可通过「显示」开关临时查看。在界面修改密钥后，它会存入被 Git 忽略的本机 `story-ai.secret.local`，不会写进普通配置 JSON。设置页面和对战进程使用同一密钥来源；界面保存的新密钥优先于原有环境变量。原有超时、思考模式、Node 路径等高级配置会保留。

`story-ai.local.json`、`story-ai.secret.local` 已被 Git 忽略。手动配置仍可复制 `story-ai.example.json` 并设置 `enabled: true`，通过 `STORY_AI_API_KEY` 环境变量或 `apiKeyFile` 提供密钥。默认优先环境变量；`apiKeySource: "file"` 时只读取文件（界面更改密钥后自动设置）。服务子进程继承选定密钥，模型提示、游戏日志和命令行参数不包含密钥。配置中的路径相对于 `plugins`；`nodePath` 也可直接填写 Node 可执行文件的绝对路径。

| 配置 | 用途 |
| --- | --- |
| `enabled` | 本局是否启用模型，默认示例为关闭 |
| `ygoAiRoot` | ygo-ai 项目目录，默认 `../ygo-ai` |
| `nodePath` | Node 可执行文件，默认 `node` |
| `baseUrl` | API 根地址，支持含 `/v1` 或完整 `/chat/completions` 地址 |
| `model` | 服务实际提供的模型 ID |
| `apiKeyEnv` | 密钥环境变量名，默认 `STORY_AI_API_KEY` |
| `apiKeyFile` | 环境变量未设置时使用的本地密钥文件；可留空 |
| `apiKeySource` | 默认环境变量优先；`file` 仅使用指定本机文件，界面保存新密钥时设置 |
| `allowAnonymousLocal` | 仅允许本机 HTTP 服务免密钥；远端要求 HTTPS 和密钥 |
| `jsonMode` | 是否发送 `response_format: json_object`，依服务兼容性开启 |
| `thinkingMode` | `adaptive` 每回合首次自由行动限时规划、后续快速选择；`disabled` 关闭思考；`enabled` 一直开启；`provider` 不发送此参数。DeepSeek 官方地址默认 `adaptive`，其他地址默认 `provider` |
| `timeoutMs` | 每个模型决策的 HTTP 总期限，规划、快速请求和一次修复共享，默认 12000 ms，范围 1000–300000 ms |
| `allowLocalFallback` | 模型失败时是否允许本地 AI 接管，默认 `true`；设为 `false` 时会结束本局，确保不会由本地 AI 继续操作 |
| `maxTokens` | 快速选择的输出上限，默认 768 |
| `strategyTimeoutMs` | `adaptive` 单次规划期限，默认 6000 ms；到期取消该请求，在总期限内转为快速选择 |
| `strategyTokens` | `adaptive` 规划的输出上限，默认 1536，包含服务计算的思考 token |
| `maxModelCalls` | 每局请求次数上限，修复重试也计数；不是金额预算 |
| `maxFailures` / `cooldownMs` | 连续失败后暂时停用接口的阈值与等待时间 |
| `maxPromptChars` | 提示文本字符上限；超出时按 `allowLocalFallback` 决定接管或结束本局，不截断候选项 |

## 接入方式

- `StoryModelHooks.cs`：通过现有 IL 后处理器接入 WindBot 数据包入口，构造以 AI 为视角的场面快照。游戏本体源码无需修改。
- `StoryModelSession.cs`：每局管理一个 Node 子进程，通过私有标准输入/输出传输带版本、会话和步骤编号的 NDJSON。网络等待发生在 WindBot 后台线程。
- `StoryModelChat.cs`：主线程读取后台消息队列，复用右侧聊天框的原生消息行；不发送网络聊天、不生成场内气泡、不写入录像。统计只更新一条消息，最近 128 条决策记录在退出本局时清理。
- `ygo-ai/skill/backend/mdpro3/decisions.mjs`：复用项目已有的协议解析库，适配 MDPro3 的旧版字节计数协议，生成候选和校验选择。模型只能选择索引，不能提交任意协议字节。
- `policy.mjs`：构造提示、调用兼容接口、校验 JSON、控制限时规划和快速选择、限制请求与上下文。
- `strategy.mjs`：维护整回合目标与路线，跟踪效果来源和素材窗口，补充卡片当前等级、真实效果描述与代行者卡组参考。
- `service.mjs`：单局服务入口，不开放监听端口，也不向模型提供工具执行或文件访问权限。

模型可看到自己的手牌与卡组构成、双方公开场面、LP、阶段、连锁、候选卡片原文和效果字符串。对手手牌与盖卡身份被遮蔽，牌库只提供数量，自己的构成按卡号聚合而非抽卡顺序。卡片文本来自当前客户端卡库，避免另用一份过期卡库。

正常更新包仍由原 WindBot 更新场面。模型返回可用选择时，由插件发送引擎响应；失败则恢复当前数据包读取位置，交给原故事 AI 处理同一个选择。核心若返回 `MSG_RETRY`，会用原始选择窗口尝试本地策略，并停用本局模型。胜负与 DP 结算仍只依赖原生服务器的 WIN 数据包。

结束挑战、取消、禁用故事功能和退出游戏都会清理模型进程。子进程也会在父进程管道关闭后退出。断线后不会把旧步骤结果用于新对局。

## 覆盖与限制

支持通常/特殊召唤、发动效果、战斗、连锁、是/否、选项、选卡、选择/取消选择、祭品、合计素材、指示物、区域、表示形式、卡片排序、数字/种族/属性宣告和局内猜拳。多卡选择采用索引及约束，避免枚举所有排列造成卡顿。连接素材的交互窗口每次只提交一个动作，明确提供已选素材和引擎是否允许完成。只有一个动作时直接执行，不请求模型。

### 展开策略

首次主动行动要求提供结构化目标和路线，卡号必须属于实际卡组；这是格式及资源校验，不是对整条路线的规则证明。位置、表示形式和连锁让过不会清空计划。每一步仍用最新候选重新选择，回合变化、效果被无效或本地接管后重新规划。

代行者卡组提供厄斯通常召唤检索、尼普顿特召维纳斯、球体转连接素材等参考，以及神巫、升级转变、克里斯提亚的关键限制。提前结束展开、空放增殖的G、把已有干扰用于中间连接怪兽时会要求模型检查一次。自己回合没有对方连锁或召唤、且唯一可选连锁是增殖的G时，直接保留手坑。其他实际选择仍由模型决定，警告不保证模型一定选择最优路线。

聊天中的动作由实际提交的合法候选生成，标明检索、送墓、特召或素材选择。模型自己的简短说明仅作为 `intention` 返回，不在界面显示成已经发生的效果，也不显示或保存服务的 `reasoning_content`。

### 响应速度

DeepSeek 官方接口默认开启高强度思考（[官方参数说明](https://api-docs.deepseek.com/guides/thinking_mode)）。当前示例保留 `deepseek-flash`，采用 `adaptive`：每回合首次主要/战斗行动最多用 6 秒低强度思考规划，后续素材、效果和行动选择关闭深度思考。规划超时或被 token 上限截断，会在剩余总期限内快速重试。卡片文本、真实效果索引、完整候选和场面仍保留，空效果字符串改为稀疏索引以减少提示体积。

12 秒期限到达后由本地策略处理当前选择。`disabled` 可用于始终快速选择；`enabled` 可用于始终思考，需相应提高时间与输出预算。API 请求次数包含失败、规划超时和修复请求，自动选择不消耗请求次数。配置更新在下次挑战生效。HTTP 401/402/403 会显示密钥、余额或权限问题并在本局停止继续请求；修复账户配置后重新挑战。

卡名宣告、连锁排序及未知或不兼容的数据包保留原故事 AI 处理。大模型不是规则引擎，也不保证竞技强度或始终走最优路线；实际局面选择、服务延迟和模型上下文都会影响表现。每个决策都刷新场面，不复用旧动作索引。

当前目标为 Windows 桌面 Mono 客户端；Android、WebGL 和不允许运行 Node 子进程的平台不属于此接入的支持范围。

## 验证

在工作区根目录执行：

```powershell
node --test ygo-ai/skill/backend/mdpro3/*.test.mjs
node ygo-ai/skill/backend/mdpro3/probe.mjs plugins/story-ai.local.json
& plugins/tools/story-compile.ps1
& plugins/tools/story-test.ps1
& plugins/tools/story-model-test.ps1 -SkipCompile
& plugins/tools/story-model-settings-test.ps1 -SkipCompile
& plugins/tools/story-model-test.ps1 -SkipCompile -RealModel
& plugins/tools/story-model-test.ps1 -SkipCompile -RealModel -ComboDeck plugins/StoryMode/opponent.ydk -ComboHand '91188343,59509952,23434538,38529357,97854941'
```

`probe` 会产生一次真实 API 调用；`-RealModel` 会用本机配置运行真实对局并产生若干调用。不带 `-RealModel` 时使用本地模拟接口，主动注入一次 HTTP 503，检查同局接管与恢复。

`-ComboDeck` 使用实际卡组检查 AI 首回合必须完成额外召唤且没有本地接管；`-ComboHand` 在隔离副本中固定五张起手，并额外检查代行者终场有可用干扰。正式卡组和洗牌规则不变。使用相同参数但将 `-RealModel` 换成 `-VerifyAgentRoute`，可用本地脚本验证厄斯+尼普顿参考路线；神巫路线使用起手 `92919429,5288597,23434538,67723438,67169062`。这只证明原生引擎接受路线，不能证明模型会自行走出该路线。

2026-09-21 两条脚本路线均通过原生对局：厄斯+尼普顿留下三素材、2400 攻击力神弓和厄斯；神巫送墓三位圣统者、被其解放拉六女，六女拉希格露恩后留下两素材法王兽和六女。两局均零本地接管。真实模型的质量测试仍必须独立通过，不能以这项脚本测试代替。

运行时检查在 `plugins/.selfcheck/story/player` 的隔离客户端和存档中进行；首次准备使用 `plugins/tools/story-stage.ps1`。已有的隔离客户端可能裁剪了新代码需要的框架方法，模型测试脚本会在这个副本中补齐同版本 Unity 的 Mono 运行库。它不会改写正式游戏目录。

测试覆盖合法选择、越界/重复索引、强制连锁、加权祭品、合计素材、过期步骤、断线、限流、超时、隐私遮蔽、真实引擎决策、结算及进程退出。测试日志位于 `plugins/.selfcheck/story/player/story-runtime.log`。

模型自测会在隔离目录生成 `plugins/model-benchmark.json`，包含首个主要阶段的局面与卡片文本，不含密钥；`plugins/model-decisions.jsonl` 记录全部决策、合法选择和耗时，用于定位展开问题。可用同一个局面交替测量三轮原服务默认设置（45 秒、4096 token）和旧快速设置（12 秒、384 token）：

```powershell
node ygo-ai/skill/backend/mdpro3/benchmark.mjs plugins/story-ai.local.json plugins/.selfcheck/story/player/plugins/model-benchmark.json
```

这会产生六次或更多真实请求（修复也计数），只打印耗时、调用次数、token 用量及状态，不输出密钥和服务原始响应。对局自测还检查聊天框显示、无底部 HUD、手动关闭不被打断和退出清理。

2026-09-21 旧版快速模式实测（同一主要阶段局面，8664 字符提示，交替三轮）：默认思考耗时 10.7 / 44.2 / 44.8 秒，其中两次耗尽输出预算而接管；快速设置三次均成功，耗时 0.43 / 0.83 / 1.10 秒。另一次真实对局的 8 次模型决策耗时 0.62–1.44 秒，0 次接管。这些是旧提示和旧配置的记录，不代表当前自适应规划的延迟或展开强度。当前真实模型的最终质量回归被服务 HTTP 402 阻断，需接口恢复后重新运行固定起手测试。

后续修改策略优先编辑 `strategy.mjs` 的卡组参考和状态记忆，以及 `policy.mjs` 的提示和 `StoryPolicy`。修改原本的本地兜底策略编辑 `StoryLuckyExecutor.cs`。调整协议适配时，同时更新 `decisions.test.mjs` 并运行真实引擎测试。
