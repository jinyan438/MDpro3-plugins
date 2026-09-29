# MDPro3 Plugins

MDPro3 的启动脚本与插件扩展。插件源码集中在 `plugins/`，启动器会将其同步到 Unity 工程并编译进游戏主程序集 `Assembly-CSharp`，无需直接修改游戏源码。

## 功能

| 功能 | 说明 |
| --- | --- |
| 卡片发布时间排序 | 在卡组编辑器的排序菜单中按收录日期升序或降序排列；没有日期的卡排在最后。 |
| 卡包浏览 | 从主界面浏览卡包、分类和包内卡片，并使用游戏卡库中的封面素材。 |
| 故事模式 | 挑战游戏角色、编辑独立卡组、赢取 DP 并购买卡包，逐步扩充故事模式卡池。 |
| 故事模式本地 AI | 默认使用插件内的确定性通用策略进行资源规划、展开搜索、效果安全判断和战斗评估；无需配置大模型。也可选配 OpenAI 兼容服务。 |
| 猜拳显示修复 | 缺少原版图标时提供内置图标，并展示服务器返回的双方猜拳结果。 |

故事模式 AI 使用有限搜索和已识别的通用效果信息，不是完整规则模拟器，也不保证任意卡组或起手都能找到最优路线。验证范围、取舍和限制见下方报告。

## 启动

在 MDPro3 工作区根目录运行：

```bat
Run_MDPro3.bat
```

首次启动或插件源码更新后，启动器会同步插件并在需要时重建游戏；首次重建通常需要 1–3 分钟。插件源码变更需要重建后才会进入游戏。只修改 `plugins/config.json` 中的功能开关时，重启游戏即可，无需重建。

常用参数：

| 参数 | 作用 |
| --- | --- |
| `--check` | 查看构建、插件和功能开关状态 |
| `--rebuild` | 强制重建 |
| `--no-build` | 跳过重建并启动现有游戏 |
| `--diagnose` | 运行游戏内插件自检 |
| `--plugin-off` | 临时移除插件并以原版行为启动 |
| `--help` | 显示帮助 |

## 文档

- [插件目录、开关和各项功能详解](plugins/README.md)
- [故事模式本地 AI 与验证记录](plugins/STORY-LOCAL-AI.md)
- [故事模式大模型接入与配置](plugins/STORY-AI.md)
- [现代召唤与资源规划报告](plugins/STORY-AI-MODERN-SUMMON-REPORT.md)
- [场上效果安全报告](plugins/STORY-AI-FIELD-SAFETY-REPORT.md)
- [攻击无效入口修复报告](plugins/STORY-AI-ATTACK-NEGATION-REPORT.md)
- [混合破坏与毁灭凤凰人恢复报告](plugins/STORY-AI-DPE-RECOVERY-REPORT.md)

## 目录

| 路径 | 内容 |
| --- | --- |
| `Run_MDPro3.bat`、`Build_*.bat`、`Update_*.bat` | 启动、构建和更新脚本 |
| `plugins/MDPro3Plugins/` | 插件源码 |
| `plugins/tools/` | 构建、诊断和回归工具 |
| `plugins/config.json` | 插件功能开关 |
