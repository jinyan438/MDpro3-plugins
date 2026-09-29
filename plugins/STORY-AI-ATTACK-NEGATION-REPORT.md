# 故事通用 AI：攻击无效通用触发入口修复（2026-09-29）

本轮在实际原生对战核心中复现了「No.39 希望皇 霍普」消耗素材、无效己方直接攻击的问题。修复及所有测试产物都在 `plugins` 内。

## 根因

安装的 Lua 脚本为攻击无效注册了精确效果编号，但核心的 `SelectEffectYn` 提示实际发送通用编号 **221**，客户端将其标准化为 **-1**。旧效果档案只查精确编号，多段卡文兜底也无法判断这个提示，因而没有进入已有的攻击收益检查。此前测试直接传精确编号，漏掉了真实协议入口。

另外，核心可以在 `ChainEnd` 之后才发送 `AttackDisabled`。旧测试统计依赖“当前正在处理的连锁”，此时已清空，因此记录到的己方攻击误无效数量可能偏低。本轮新增独立的 `ATTACK-STOP attacker=...` 事件记录，以实际被取消的攻击归属核对；它本身不假定取消攻击的是哪一方效果。

## 通用修复

- 从 Lua 脚本提取唯一可确认的可选攻击宣言触发及发动区域，共 **18 个**档案。运行时通用触发提示可据此进入统一的攻击无效收益判断，没有新增按霍普卡名／卡号特判。
- 唯一性检查包含未分类的其他可选触发；存在歧义、区域未知或效果编号未知时不猜测。强制效果、起动效果、快速效果分别处理。
- 保留有实际保护收益的攻击无效；己方正常直接攻击、能获胜的攻击不会被这个入口无效。覆盖异画卡、无正在进行的攻击、怪兽及魔法陷阱来源。

## 验证结果

- 修改前新增的通用提示反例 **4 个全部失败**，修改后通过。
- 最终本地 AI **4598 项检查通过**，其中效果安全 **772 项**；效果脚本提取器 **20 项测试通过**。
- 原生核心成对比较包含四组实际 **0004 角色 10 级卡组**起手，以及两组明确标注的霍普素材构造场景。构造场景从两只通常怪兽开始，由核心真实超量召唤霍普并附加素材；其卡组用于隔离问题，不代表真实角色卡组。
- 同样输入、六回合上限：两组构造场景合计无收益攻击无效 **4 → 0 次**，每组保留的超量素材 **0 → 2 张**，对敌方造成的伤害 **0 → 5000**。日志确认四次 `description=221` 提示都被正确拒绝。
- 最终另跑相同六组输入的压力对手版本，合计完成 **12 个原生场景**，无非法响应、Lua 错误、提示循环或超时。压力版本用于执行回归，不作为霍普必须发动保护效果的证明；保护行为另有本地反例验证。以上不是完整对局胜率测试。

成对比较的输入、核心、卡库、脚本及测试程序哈希相同，程序集不同。证据保存在：

- `.selfcheck/story/utopia-final/results.log`
- `.selfcheck/story/core-ai-utopia-direct-before/{manifest.json,trace.log,endstates.tsv,interactions.tsv}`
- `.selfcheck/story/core-ai-utopia-direct-after/{manifest.json,trace.log,endstates.tsv,interactions.tsv}`
- `.selfcheck/story/core-ai-utopia-pressure-after/{manifest.json,trace.log,results.tsv}`
- 冻结输入 `.selfcheck/story/utopia-cases.tsv`

在项目根目录复现最终验证：

```powershell
python plugins/tools/tests/test_story_effect_safety.py
plugins/tools/story-local-ai-test.ps1 -Label utopia-final
plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label utopia-direct-after -Cases plugins/.selfcheck/story/utopia-cases.tsv -Turns 6 -Interruption none
plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label utopia-pressure-after -Cases plugins/.selfcheck/story/utopia-cases.tsv -Turns 6 -Interruption pressure
```

未执行会写入 `plugins` 外的工程同步或游戏重建。通过项目根目录 `Run_MDPro3.bat` 正常启动并成功完成重建后，游戏才加载本轮修复。
