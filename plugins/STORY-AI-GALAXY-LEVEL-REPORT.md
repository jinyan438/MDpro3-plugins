# 银河光子龙改星修复

本轮针对天城快斗（`0407`）10 级卡组中「银河光子龙把银河战士从 5 星改为 4 星」的问题。基线为本次修复前冻结的 4025 项检查版本，保留此前通用 AI 优化。只修改 `plugins`，角色卡组、故事进度和正式游戏程序集未改写。

## 原因与处理

原策略未比较可选改星的收益；数字未预选时，WindBot 会随机返回 4／8 的选项位置。此外，核心的可选诱发询问可能使用通用描述 `221`，由客户端转换为 `-1`，只匹配精确效果编号会漏掉实战入口。

- 「不发动」与每个合法目标变成 4／8 星的后续展开使用同一套有预算的做场搜索。只有改星的预计终场比不发动更好才发动；等级本身不加分。
- 保留两只 5 星的可用路线；需要凑 8 阶或 4 阶时选择相应等级。原本 4 星只能变 8，原本 8 星只能变 4，与实际 Lua 一致。
- 目标限定本次特召事件中的己方表侧光属性、持有等级的怪兽；额外检查核心实际提供的选卡列表。目标与数字绑定到同一物理来源，结算时依据当前局面重新评估。
- 数字回调只接管当前正在结算的该改星效果，按数值查找选项位置；不向全局数字队列写入预选，不影响其他效果或非故事执行器。连锁、阶段和回合切换清理旧计划。
- 「银河天翔」支付费用时施加的光子／银河召唤限制纳入规划；效果被无效后仍保留，到下一回合清除。

每条改星分支复用原有搜索上限：7 步主动动作、40 个常规保留状态、5000 个生成状态、45000 次素材检查；没有扩大单条展开搜索预算。它仍是有限搜索，不是执行全部 Lua 的最优解求解器。

## 验证

真实源码编译及 IL 注入、符号、幂等、API 不匹配拒绝检查通过。本地 AI **4093 项检查通过**，相对基线新增 **68 项**。新增专项在冻结旧程序集上有 **7 个行为场景组失败**，新版通过，包括保留 5／8 星组合、实际通用提示编号、目标约束、数字选项换序、队列隔离、结算重规划和天翔限制。

原生核心成对运行：**128 个实际完整卡组起手＋9 个单独构造的中途局面，每版 137 场，共 274 次最终执行**。每对完整牌序、种子、核心、卡库、Lua 与测试程序哈希一致，仅替换 AI 程序集。全部完成，无核心 Retry、Lua 错误、提示循环或场景超时。中间调试运行不混入最终结果。

128 个实际起手中，改星发动次数 **19 → 1**。所有起手的终场怪兽 ID 组合相同；其中一个起手的超量素材分配不同。这个修复证明减少了无收益的改星，并不表示这 128 个起手的终场数量或完整对局胜率提高。

复现相同错误行为的 `galaxy-0407-2`：旧版在两只银河战士同场时选择 4，主阶段可见等级为 **5＋4**；新版拒绝发动，保持 **5＋5**。该起手已发动银河天翔，故本回合新星仍被召唤限制禁止，不能把这部分停场归因于等级。在 `galaxy-0407-31`，新版把银河魔导师改为 8 星，随后与银河眼光子龙叠出希望魁龙，验证有益改星没有被一律关闭。

9 个构造局面只使用实际快斗卡组中的卡片副本，缩减其他路线以检查原生效果协议；它们不是完整 40 张卡组的自然起手，不计入上述 128 个样本：

| 场景 | 旧版成功 | 新版成功 | 每版局面数 |
| --- | ---: | ---: | ---: |
| 战士改为 8，与已有 8 星叠出希望魁龙 | 2 | 3 | 3 |
| 战士改为 4，与光子跳跃者叠出光子爆龙 | 1 | 3 | 3 |
| 拒绝无收益改星，两只战士保持 5 星 | 0 | 3 | 3 |

最后一组验证等级保留，不声称 AI 必须把两只战士换成单只新星；新星本身是否值得出仍由终场收益判断。该实际卡组没有电子龙·无限。

## 文件与复现

- `MDPro3Plugins/Runtime/Features/StoryMode/StoryAiLevelPlanning.cs`：改星收益、选目标和数字结算。
- `StoryAiEffects.cs`、`StoryAiPolicy.cs`、`StoryAiDevelopment.cs`：效果识别、生命周期和天翔限制。
- `MDPro3Plugins/Editor/StoryMode.CodeGen/StoryEditorPostProcessor.cs`：接入真实数字回调。
- `tools/tests/StoryLocalAiGalaxyTests.cs` 与 `StoryLocalAiGalaxyCards.cs`：专项行为回归和实际卡库快照。
- `STORY-AI-GALAXY-LEVEL-REPORT.json`：成对哈希、逐场等级轨迹、数字响应、终场资源。
- `.selfcheck/story/galaxy-level-start/`：本次修复前快照。
- `.selfcheck/story/core-ai-galaxy-level-{before,after}/`、`core-ai-galaxy-level-fixtures-{before,after}/`：最终原始记录。

在项目根目录执行：

```powershell
& plugins/tools/story-local-ai-test.ps1 -Label galaxy-level-tests
python plugins/tools/tests/story_galaxy_level_cases.py --output-root plugins/.selfcheck/story
& plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label galaxy-level-after -Cases plugins/.selfcheck/story/galaxy-level-cases-128.tsv
& plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label galaxy-level-fixtures-after -Cases plugins/.selfcheck/story/galaxy-level-fixtures.tsv
python plugins/tools/tests/story_galaxy_level_report.py --root plugins/.selfcheck/story --output plugins/STORY-AI-GALAXY-LEVEL-REPORT.json
```

审计命令还需要保留对应的冻结基线目录；可用 `-ReuseAssembly` 重跑其中准备好的旧程序集。正式游戏需通过原有 `Run_MDPro3.bat` 同步并重建后生效，本轮未启动该流程，以遵守只修改 `plugins` 的范围。
