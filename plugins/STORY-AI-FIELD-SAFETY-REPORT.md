# 通用场上干扰防误伤修复（2026-09-28）

修复范围是故事模式本地通用 AI，所有源码、测试和运行产物位于 `plugins`，不改角色卡组或故事存档。

## 已确认的根因

截图中的混沌侠使用 Lua 的描述编号 `23204029 * 16`，对应场上卡效果无效。原效果表未覆盖这类 `CATEGORY_DISABLE` 目标效果，且整段卡文含多个效果，旧卡文识别跳过了它。

原生核心重放又发现一个独立问题：对方通常怪兽「基因狼人」的背景描述包含“破坏”，被收益评分误当作有可无效效果的怪兽。AI 据此发动，核心却正确排除了这张通常怪兽，实际候选只剩己方卡。仅有“己方空场／对方空场”单元测试不能发现这个问题。

## 通用规则变更

- Lua 提取器识别目标效果无效和 `aux.NegateAnyFilter`，新增 43 个精确无效效果档案。运行时按效果描述编号识别，不按卡名分支修补。
- 无效目标先检查当前／原始效果类型，避免通常怪兽背景描述产生虚假目标；怪兽、魔法、陷阱的目标无效共用这一判断。
- 对可明确识别的多段卡文，补充场上无效／除外识别；区分必选数量与“最多”，拒绝缺少敌方目标、需要用己方卡补齐的发动。弃牌成本仍参与判断。
- 卡文兜底不借用其他明确发动段落的除外句来否决抽牌等效果。脚本档案优先，复杂费用、苏生后无效及已有临时除外救场规划保持各自语义。
- 保留混合破坏的成对规划：己方需要具体破坏收益，另一张需要有收益的敌方对象，连续选卡提示沿用同一计划。
- 原生测试记录无效／破坏／除外选卡时的归属和区域。仅统计 `CHAIN_DISABLED` 会漏掉没有正在发动效果的己方怪兽被无效。

## 验证结果

- 真实游戏源码编译、IL 注入及幂等检查通过。
- 本地 AI **4504 项检查通过**，其中效果安全 **726 项**；Python 效果安全／通用效果／改星提取器分别 **15／17／7 项**通过。
- 对新增数量、成本和串段反例，修复前有 4 个场景失败；对通常怪兽描述反例，怪兽／魔法／陷阱来源均能复现，修复后通过。
- 最终原生核心完成 **24 个场景**：五副实际 10 级卡组各 4 个起手（同调、超量、月光、链接、十代英雄），另有 4 个明确标注的英雄构造场面。使用固定压力对手，最多执行四回合；没有非法响应、Lua 错误、提示循环或超时。
- 英雄的 8 个相同输入场景中，补类型检查前后，混沌侠无收益无效己方卡 **2 → 0**；对敌方泡影／雷击的 **6 次无效均保留**。这里的前版是本轮发现问题时冻结的候选版本，已经包含 Lua 无效识别，并非最初生产版本。
- 最终 24 个场景记录到的己方无效目标、己方连锁无效、己方攻击无效和 S:P 无收益永久除外己方目标均为 **0**。苏生、融合素材、回收和临时除外产生的己方移动另行记录，不一概视为误伤。

这些是效果安全及有限回合执行验证，不是完整对局胜率或任意复杂效果的最优决策证明。

## 产物与复现

结构化数据及程序集／核心／卡库／脚本／输入哈希：[STORY-AI-FIELD-SAFETY-REPORT.json](STORY-AI-FIELD-SAFETY-REPORT.json)。英雄对照两次运行的输入、核心、卡库、脚本及测试程序哈希相同。

在项目根目录运行：

```powershell
plugins/tools/story-local-ai-test.ps1 -Label field-safety-final
python plugins/tools/tests/test_story_effect_safety.py
python plugins/tools/tests/test_story_general_effects.py
python plugins/tools/tests/test_story_level_effects.py
plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label field-safety-hero-fixed -Cases plugins/.selfcheck/story/field-safety-hero-cases.tsv -Turns 4 -Interruption pressure
plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label field-safety-level10-fixed -Cases plugins/.selfcheck/story/core-ai-field-safety-level10/cases.tsv -Turns 4 -Interruption pressure
```

完整原生选卡日志在 `.selfcheck/story/core-ai-field-safety-hero-fixed/trace.log` 和 `.selfcheck/story/core-ai-field-safety-level10-fixed/trace.log`。

此次未运行会写入 `plugins` 外部的工程同步／游戏重建。下次通过项目根目录 `Run_MDPro3.bat` 正常同步重建后，游戏才加载本轮修改。
