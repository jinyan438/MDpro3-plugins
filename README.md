# MDpro3-plugins

MDPro3 的启动脚本 + 插件包（游戏源码不改动，插件编进 `Assembly-CSharp`）。

## 用法

```bat
Run_MDPro3.bat
```

首次或插件改动后会自动重建游戏（1–3 分钟），之后启动很快。
参数：`--check` 只看状态 · `--rebuild` 强制重建 · `--no-build` 跳过重建 · `--diagnose` 游戏内自检 · `--plugin-off` 临时关闭插件 · `--help`。

## 目录

| 路径 | 说明 |
| --- | --- |
| `*.bat` `*.ps1` | 原工程的启动 / 构建 / 更新脚本 |
| `plugins/` | 插件源码与工具，说明见 [plugins/README.md](plugins/README.md) |
| `plugins/config.json` | 单功能开关，改完重启游戏即生效，**不用重建** |

## 当前功能

编辑卡组界面 → **排序** → 新增一行「发布时间」：▲ 老卡在前 / ▼ 新卡在前。
日期取自 `Data\pack\pack.db`（即卡牌详情里的收录日期），无日期的卡排在最后。
