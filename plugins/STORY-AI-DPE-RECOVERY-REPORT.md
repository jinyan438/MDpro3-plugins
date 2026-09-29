# 故事通用 AI：混合破坏与破坏后复活修复（2026-09-29）

修复「命运英雄 毁灭凤凰人」无法发动效果的回归。运行时按效果类型处理，没有新增按卡名或卡号放行的分支。本轮源码、测试和测试产物均在 `plugins` 内。

## 根因与修改

- 之前要求破坏己方卡本身就产生正收益，没有计算敌方卡被破坏的收益。凤凰人还被既有威胁表评为高价值怪兽，因此合理的交换也被否决。现在共同评估己方损失、可用的破坏收益和敌方移除收益，仍须有敌方目标且净收益超过门槛。
- 破坏后墓地苏生按打折后的场面价值计算恢复收益；未完成正规召唤、次元吸引者等送墓替代生效时，不虚构该恢复收益。
- 整张卡文中的混合破坏句曾误覆盖墓地复活触发。现在限制场上效果的识别范围，并利用已有脚本效果编号／触发信息分开处理。
- 不取对象破坏不再错误检查取对象抗性。连续选卡按对应连锁保留己方／敌方组合，中间发动另一张己方卡也不会覆盖原计划。

修改涉及 `StoryAiEffects.cs`、`StoryAiEffectReading.cs`、`StoryAiResources.cs`，回归位于 `tools/tests/StoryLocalAiEffectSafetyTests.cs`。此前防误伤报告中“己方单独需要正收益”的旧规则由本轮共同收益评估替代。

## 验证

- 真实游戏源码编译与 IL 注入检查通过。本地 AI **4570 项检查全部通过**，其中效果安全 **744 项**。
- 新增检查覆盖：有收益的混合破坏、不取对象破坏、空场复活触发、敌方空场不自毁、高价值己方卡不换无价值衍生物、连锁中间插入其他己方效果、匿名卡号的同类效果、无法苏生及送墓替代。
- 原生核心使用实际 0101 角色的 10 级英雄全卡组，冻结 8 组输入：4 组洗牌起手、4 组明确标注的融合命运起手。种子为 929400–929403，压力对手，每组最多四回合。凤凰人经核心实际融合召唤，不以预置场面伪造苏生资格。

| 相同输入的原生核心记录 | 修复前 | 最终修复 |
| --- | ---: | ---: |
| 凤凰人破坏效果发动 | 0 | 4 |
| 凤凰人破坏后复活触发发动 | 0 | 9 |
| 完成场景 | 8 | 8 |

最终四次破坏均选择己方凤凰人和敌方卡。日志确认“破坏双方卡 → 复活触发 → 下回合准备阶段苏生”的完整流程；复活触发次数不等于实际苏生次数。两版均无非法响应、Lua 错误、提示循环或超时。这是有限场景的效果回归，不是完整对局胜率测试。

对照运行的输入、核心、卡库、脚本和测试程序 SHA-256 全部相同，程序集不同。原始证据：

- `.selfcheck/story/dpe-final/results.log`
- `.selfcheck/story/core-ai-dpe-core-before/{manifest.json,trace.log,results.tsv}`
- `.selfcheck/story/core-ai-dpe-core-final/{manifest.json,trace.log,results.tsv}`
- 冻结输入 `.selfcheck/story/dpe-cases.tsv`，SHA-256：`F1E5A8982EC2EBDF5031DE944838E766465B490432A43B67C5C67C3836C889A3`。

在项目根目录复现最终检查：

```powershell
plugins/tools/story-local-ai-test.ps1 -Label dpe-final
plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label dpe-core-final -Cases plugins/.selfcheck/story/dpe-cases.tsv -Turns 4 -Interruption pressure
```

为遵守只修改 `plugins` 的限制，本轮没有运行工程同步或游戏重建。通过项目根目录 `Run_MDPro3.bat` 正常启动并成功完成重建后，游戏才加载本轮修复。
