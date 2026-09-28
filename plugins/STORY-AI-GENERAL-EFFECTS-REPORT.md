# 故事模式通用 AI：效果展开通用化（2026-09-28）

本轮把此前为具体终端服务的改星、素材手续、检索、苏生、费用和终场干扰判断合并到通用规划。候选效果来自已安装 Lua 脚本的保守结构提取；卡号在生成表里标识实际效果，不决定出牌顺序。通常召唤、效果发动、额外召唤共用有限步搜索，按实际手牌、墓地、卡组、额外卡组、素材去向和次数重新比较。银河光子龙与其他同类型改星卡走相同判断；不再有银河专用改星分支。只修改 `plugins`。

## 本轮修正

- 主动组合效果允许较小但真实的净收益（最低增益门槛由 100 调为 50），避免“能用一张手牌做出有效干扰，但差 86 分而停手”。实际代价、额外限制和后续搜索仍参与比较。
- 新获得的对方回合干扰对额外召唤和苏生使用相同估值；直接苏生的快速判断也使用这套估值，避免为做同一终端白白消耗己方素材。
- 对方持续抽牌压力按每次 1600 计入已建模的展开路径；多次抽牌压力下不把额外召唤视作免费收益。
- 召唤手续、效果目标、次数、费用与落点按性质解析；无法安全识别的复合效果保守跳过，不凭卡面关键词虚构可用资源。

这不是对任意卡片的完整 Lua 解释。旧本体移植的单卡 AI 顺序、手坑专项识别仍保留；上述新增的做场决策没有角色卡组或银河卡号特判。

## 原生核心验证

先用相同输入、核心、卡库、脚本和测试夹具成对比较 `general-effects-*-v1` 与 `general-effects-*-final`，只更换 AI 程序集；随后移除夹具中未启用的诊断日志分支，使用 `general-effects-*-clean` 重新执行最终版，终态与 `final` 完全相同。完成 176 对真实 10 级卡组首回合（快斗 128、同调／超量／融合／链接各 12）及 51 对构造局面；两版共 454 次成对执行。清理后的源码另外重跑了 9 个银河局面。全部正常结束，未出现核心拒绝响应、Lua 错误、提示循环或超时。本地 WindBot／织入入口 **4461 项检查通过**；Lua 提取器测试为 17 + 14 + 7 项通过。

| 指标 | v1 | 当前 | 含义 |
| --- | ---: | ---: | --- |
| 快斗起手出现 No.38 | 20 / 128 | 22 / 128 | 8 阶干扰终端 |
| 快斗起手出现未来龙皇 | 8 / 128 | 12 / 128 | 可用超量干扰 |
| 超量主轴出现未来龙皇 | 11 / 12 | 10 / 12 | 有一例退步 |
| 超量主轴出现蚀之双子 | 6 / 12 | 7 / 12 | 与未来龙皇组合须逐例判断 |
| 融合主轴出现舞狮子神姬 | 0 / 12 | 1 / 12 | 高阶融合终端 |
| 融合主轴出现 S:P | 10 / 12 | 10 / 12 | 该指标持平 |
| 链接主轴出现神弓 | 3 / 12 | 7 / 12 | 对方回合干扰增多 |
| 链接主轴出现女男爵 | 4 / 12 | 3 / 12 | 终端取舍，不等于净胜率 |

176 个真实起手中，28 个终场怪兽卡号组合改变，另有 4 个只改变了手牌、素材或其他资源；51 个构造局面中只有 9 个银河局面改变。三个“需要 8 阶”的银河局面均先发动银河战士、正确改星并做出 No.38；三个保留 5 星及三个 4 星局面没有为低收益路线浪费高价值手牌。其余 42 个通用改星局面保持原先的指定终场。

差异不是全都改善。`xyz-0401-0` 当前转为未来皇、蚀之双子和一只下级，失去旧版的未来龙皇；`xyz-0401-7` 保留未来龙皇并改为蚀之双子，但少一只怪兽。`link-0013-7` 从许珀里翁＋访问码语者转为神弓，减少了攻击力，增加了首回合可用干扰。融合与链接的其他变化也包含手牌和素材交换，不能只按场上数量判断。逐场终态和动作记录在 `.selfcheck/story/core-ai-general-effects-*-clean`，结构化差异与哈希见同目录 [JSON](STORY-AI-GENERAL-EFFECTS-REPORT.json)。

## 复验与边界

```powershell
python -B -X utf8 plugins/tools/tests/test_story_general_effects.py
python -B -X utf8 plugins/tools/tests/test_story_effect_safety.py
python -B -X utf8 plugins/tools/tests/test_story_level_effects.py
./plugins/tools/story-local-ai-test.ps1 -SkipCompile -Label generic-effects-clean
./plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label general-effects-galaxy-clean -Cases plugins/.selfcheck/story/general-effects-galaxy.tsv
./plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label general-effects-fixtures-clean -Cases plugins/.selfcheck/story/general-level-fixtures.tsv
./plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label general-effects-openings-clean -Cases plugins/.selfcheck/story/general-level-openings.tsv
```

`-SkipCompile` 复验依赖已经编译的 `.selfcheck/story/Assembly-CSharp.story.dll`；重新构建时先运行 `story-local-ai-test.ps1` 不带该选项。测试只覆盖无干扰首回合与指定公开局面，不代表整局胜率，也不能保证所有卡组都得到数学最优终场。未解析的复杂 Lua 分支、主题限制和对方实际干扰仍可能影响展开；每一步由原生核心判定合法性并重新规划。尚未同步到正常游戏目录，需要按原项目插件同步与重建流程生效。
