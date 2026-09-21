# MDPro3 插件包（plugins）

给 MDPro3 增加功能的插件。插件代码只写在这里，`Run_MDPro3.bat` 会在启动时把插件同步进 Unity 工程、
编译进游戏主程序集（`Assembly-CSharp`），需要时自动重建 `Build\MDPro3\MDPro3.exe`，然后照常启动游戏。

**除了本目录和根目录的 `Run_MDPro3.bat` 之外，工程里其它文件都没有被改动。**

---

## 1. 目录结构

```text
plugins/
├─ README.md                     本说明
├─ config.json                   ★ 单功能开关（改了不用重建，重启游戏即可）
├─ MDPro3Plugins/                插件源码（唯一真源，只改这里）
│  ├─ link.xml                   保留 WindBot JSON 反序列化依赖的运行时类型
│  ├─ Runtime/
│  │  ├─ PluginInfo.cs           插件标识 / 版本
│  │  ├─ PluginLog.cs            统一日志前缀 [MDPro3Plugins]
│  │  ├─ PluginFeature.cs        ★ 功能接口 IPluginFeature + 基类 PluginFeature
│  │  ├─ PluginRegistry.cs       ★ 功能注册表（新增功能在这里加一行）
│  │  ├─ PluginEvents.cs         ★ 事件中心（功能之间与游戏状态变化的广播）
│  │  ├─ PluginGame.cs           对游戏单例的空安全只读访问
│  │  ├─ PluginConfig.cs         读取 config.json
│  │  ├─ PluginHost.cs           ★ 唯一常驻宿主：观察环境 → 分发事件 → tick 功能
│  │  ├─ Features/               一个功能一个目录
│  │  │  ├─ ReleaseDateSort/     编辑卡组界面按卡片发布时间排序
│  │  │  │  ├─ ReleaseDateSortFeature.cs   功能本体（订阅事件、维护排序）
│  │  │  │  ├─ ReleaseDateSortToggle.cs    排序弹窗里的新条目行为
│  │  │  │  ├─ SearchOrderRowInjector.cs   把新条目注入排序弹窗
│  │  │  │  ├─ CardReleaseDate.cs          取卡包首发日期
│  │  │  │  └─ ReleaseDateSortLabels.cs    多语言标签
│  │  │  ├─ PackBrowser/         主界面卡包浏览
│  │  │  │  ├─ PackBrowserFeature.cs       功能本体 + 主界面「卡包」按钮注入
│  │  │  │  ├─ PackBrowserOverlay.cs       全屏卡包界面（网格 + 卡表 + 输入）
│  │  │  │  ├─ PackTileItem.cs             格子行为（基于游戏自带卡组格子）
│  │  │  │  ├─ PackWrapperVisual.cs        长条金色包装 + 封面原画 + 选中光框
│  │  │  │  ├─ PackCatalog.cs              从游戏数据组装卡包与封面
│  │  │  │  ├─ PackCategory.cs             卡包商品分类规则
│  │  │  │  ├─ PackCoverTable.g.cs         生成的封面表（904 个卡包）
│  │  │  │  └─ PackBrowserLabels.cs        多语言标签
│  │  │  └─ RpsVisualFix/        猜拳图片缺失时提供内置图标
│  │  │     ├─ RpsVisualFixFeature.cs       弹窗修复 + 运行时图标生成
│  │  │     ├─ RpsResultPacketObserver.cs   只读捕获双方猜拳结果
│  │  │     └─ RpsResultOverlay.cs          双方结果图片展示层
│  │  └─ Diagnostics/
│  │     └─ PluginSelfTest.cs    游戏内自检（--diagnose 使用，不属于功能）
│  ├─ Editor/
│  │  └─ PluginSelfCheck.cs      编辑器自检（只读导出 UI 结构与插件设置报告）
│  └─ Resources/MDPro3Plugins/PackBrowser/  八色包装与共用金色光框纹理（含导入设置）
├─ tools/
│  ├─ plugin-state.ps1           同步 / 签名 / 构建状态 / 文件占用检测 / 读取 config.json
│  ├─ plugin-diagnose.ps1        在构建好的游戏里跑自检
│  ├─ pack_covers_rekowiki.json  爬取到的卡包一览（封面卡数据源）
│  ├─ build_pack_cover_table.py  由上面两者生成 PackCoverTable.g.cs
│  ├─ build_pack_wrapper.py      从参考图印刷细节生成包装与光框（需要 Pillow）
│  └─ pack_wrapper_source/       从用户参考图提取的金箔和底部标识源图
└─ .state/                       自动生成的状态与日志（可随时删除）
```

启动后的数据流：

```text
plugins\MDPro3Plugins\  ──复制──▶  MDPro3\Assets\MDPro3Plugins\  ──Unity 编译──▶  Assembly-CSharp  ──Build──▶  Build\MDPro3\MDPro3.exe
plugins\config.json     ──游戏启动时读取（随时改，不用重建）──▶  决定哪些功能启用
```

`MDPro3\Assets\MDPro3Plugins` 是**自动生成**的（脚本会清理源里已删除的文件、空目录和孤立 `.meta`），
不要在那边改代码；`Run_MDPro3.bat --plugin-off` 可以把它整个移除。

---

## 2. 已实现的功能

### 主界面：故事模式

主菜单新增 **故事模式**。在独立的存档里，用初始卡组挑战本体角色，胜利获得 DP，购买逐步解锁的卡包，扩充自己的构筑卡池。

1. **第一次进入**会获得一套 40 张初始卡组：24 张早期通常怪兽、10 张通用魔法、6 张通用陷阱。初始 DP 为 1000。
   初始卡组的每一张卡都计入持有数量；以后重新进入不会再次赠送。
2. **角色挑战**读取本体 `Characters` 资源，包含 DM、GX、5D’s、DSOD、ZEXAL、ARC-V、VRAINS、SEVENS、Duel Links、GO RUSH!!。
   名称、头像、立绘和简介均来自游戏资源，跳过本体标为未就绪的角色。
3. 先选角色与 1–10 级难度，点击 **编辑 N级卡组 · 全卡库**，直接进入**本体卡组编辑器**。每个角色的每一级分别保存卡组，无需配置全部十级。
   新角色不预设卡组；可从右上角菜单选择 **使用初始构筑**，也可从空卡组开始。玩家可任选一个已经配置的等级挑战，未配置的等级不能开战。
4. **我的卡组**同样复用本体编辑器：左侧卡片详情和加减按钮、中间主／额外／副卡组、右侧卡片列表、收藏、历史、搜索筛选、排序、拖拽以及手柄操作均沿用本体。
   玩家卡池只显示初始卡组和故事卡包获得的卡片，收藏、历史、关联卡片和 YDKE 导入也不能绕过限制。
   主卡组、额外卡组、副卡组合计不能超过持有张数，同名卡／异画卡合计最多 3 张；角色构筑使用本体全卡库。
   玩家使用 **N／R／SR／UR／GR／MR** 六档独立库存，详情区的「持有版本」显示每档可用／持有张数；选择持有版本后使用 +1 或拖拽加入。不同版本可混用，但同名卡跨版本、跨三个卡组区域合计仍最多 3 张。玩家的自由罕贵度按钮已替换，不会改写本体全局罕贵度设置。
   角色编辑器左下角使用 **R／SR／UR／GR／MR** 五个原生样式按钮，SR 为绿色徽章；再次点击当前档位切回 N。选中卡组中的卡片后，切换只修改这一张实体卡，同名的其它卡片保留各自版本；在卡库中切换只决定随后加入的新卡版本。罕贵度逐张随角色和等级的卡组独立保存，不改写全局罕贵度设置。
   卡组逐张保存罕贵度，打乱、移入副卡组、重新打开均保留。YDKE 格式不包含罕贵度，导入时按 N→R→SR→UR→GR→MR 从实际持有数量分配版本；不足时拒绝导入。
   顶部 **保存** 和返回时的保存都写入故事存档，离开前可保存、放弃修改或关闭提示继续编辑；返回后保留所选角色。
   原生 **手牌测试**使用当前构筑，结束回到同一编辑器，不发 DP，也不增加解锁进度。YDKE 可导入／导出，导入草稿可不足 40 张，保存和开战时仍须满足完整构筑规则。
   故事卡组按玩家／角色／难度固定归档，不提供普通卡组的重命名、复制、云同步和外观入口；故事模式的禁限表按钮显示构筑规则。
5. **开始挑战**使用本体的本地决斗服务器与故事模式自己的 WindBot 通用策略，载入玩家为该角色保存的实际卡组。
   故事 AI 当前完整复刻人机模式「P2-自选卡组」的 `LuckyExecutor` 行为，插件内的独立实现位于 `Runtime/Features/StoryMode/StoryLuckyExecutor.cs`，后续可单独调整而无需修改本体。
   规则为 8000 LP、起手 5 张、每回合抽 1 张、单局、无禁限表；两方仍须满足主卡 40–60、额外／副卡各 0–15、同名卡合计最多 3 张。
6. 胜利获得 `难度等级 × winDP`：默认 1 级 100 DP、2 级 200 DP，依此类推，10 级 1000 DP。胜、负、平局均计入已完成挑战数。主动投降以服务器返回的真实胜负计数；启动失败、取消连接、直接断线、录像或普通决斗均不发奖励、不增加故事进度。
   每场挑战只结算一次；结算后返回故事界面，普通单人模式的“重试”入口不会在故事对战中启动其它卡组。
7. **DP 卡包商店**复用卡包浏览的金属包装、封面图、分类、虚拟滚动网格及原生卡片详情。
   商品按发售日期从早到晚排列，未解锁商品可以预览，显示还需完成多少场挑战。购买按钮显示 DP 价格，余额不足或尚未解锁时不可购买。
   每次购买立即扣费并开出 **恰好 3 张卡**，结果显示完整卡图并自动存档；三次等概率独立抽取，允许重复，重复卡逐张累加持有数量。
   每张卡独立抽取实际罕贵度，默认 N 55%、R 32.5%、SR 7.5%、UR 2.5%、GR 1.5%、MR 1%。SR 使用实卡全卡面面闪样式：动态彩虹反光与细颗粒箔面连续覆盖卡名、卡图、效果文字区和外框，保留文字可读性，反光在卡牌圆角边缘裁切；其余版本沿用本体材质。购买成功后进入独立开包演出：封装卡包悬浮入场，点击「开启卡包」后聚光切开封口，三张卡展开并依次翻面。不叠加文字角标；卡面效果和保存的版本完全一致，首次获得与持有数量按版本统计。
   「跳过动画」或 Esc／右键直接展示全部结果；结果页点击卡图查看详情，「继续购买」回到当前卡包。奖励在演出前已经保存，跳过或中途退出不会丢卡，也不会重复扣费。动画使用未缩放时间，并适配不同窗口比例。

默认进度规则在 `plugins/story-mode.json`，重启游戏生效，无需重新编译：

| 配置 | 默认值 | 含义 |
| --- | --- | --- |
| `initialPacks` | 5 | 初始开放最早的 5 个可购买商品卡包 |
| `duelsPerUnlock` | 3 | 每完成 3 场挑战解锁下一批 |
| `packsPerUnlock` | 1 | 每批解锁 1 个卡包 |
| `winDP` | 100 | 每一级难度的胜利基础 DP；实际奖励为此值乘难度等级 |
| `packPrice` | 100 | 每包价格 |
| `rarityWeights` | `[550,325,75,25,15,10]` | N、R、SR、UR、GR、MR 六档非负整数权重，按总和归一化；总和必须大于零 |

商店使用与卡包浏览一致的本体卡包数据，排除没有可用卡片、没有发售日期的商品和虚拟先行卡包。
卡号仍在包内等概率抽取，罕贵度另行独立抽取；每包三张是固定规则，不随配置变化。角色构筑可以使用本体已加载的先行卡，实际能否决斗还取决于本体是否具备该卡脚本。

**数据位置**：`plugins/StoryMode/progress.json` 保存 DP、胜场／对战数、按罕贵度区分的持有卡、逐张记录版本的玩家卡组与各角色各难度卡组。存档版本为 3；旧卡片因未记录实际版本，自动保留为 N，数量和进度不变。版本 1 的原角色卡组迁移为 1 级，版本 2 已配置的难度卡组原样保留。
保存采用临时文件和原子替换，上一份为 `progress.json.bak`；主存档损坏时保留损坏文件并恢复备份，两份都损坏时停止载入并报错，不会悄悄清空进度。
`opponent.ydk` 是当前挑战导出给 WindBot 的临时卡组。这个目录已从 Git 排除，不修改本体的 `Deck`、`Data` 或卡片数据库。
角色外观设置仅在故事决斗期间临时切换，结束或退出游戏时恢复。

**AI 能力边界**：角色共用与「P2-自选卡组」相同的自由构筑通用策略，并由插件维护独立副本。它能召唤、发动效果和战斗，并包含原策略对常见通用卡的处理，但不具备每个主题专用 AI 的组合规划能力。
故事构筑使用本体的 `DeckEditorUI`／移动端 prefab，离开后恢复普通编辑器的卡池、筛选与保存行为。
`Editor/StoryMode.CodeGen` 通过 Unity IL 后处理给编译产物接入故事会话的卡池和保存规则，无需修改本体源文件或 prefab，也不引入运行时补丁库。
钩子只在故事编辑会话生效；未完成接入的构建拒绝打开故事编辑器，本体接口不兼容时编译报错，避免静默丢失卡池限制。

**验证命令**（所有产物留在 `plugins/.selfcheck/story`）：

```powershell
# 规则、卡池限制、购买、结算及存档异常回归测试
./plugins/tools/story-test.ps1
# 使用真实源码与 Unity 引用编译，验证编辑器 IL 后处理，不同步或修改本体
./plugins/tools/story-compile.ps1
# 验证原生编辑器/卡池/导入/保存/手牌测试/购买/真实 AI 对战并截图（需约 12 GB）
./plugins/tools/story-runtime-test.ps1
# 已有隔离副本时可跳过再次复制
./plugins/tools/story-runtime-test.ps1 -ReusePlayer
# 仅验证开包演出、卡片详情、各阶段跳过、重复购买与关闭后存档，并截图
./plugins/tools/story-runtime-test.ps1 -ReusePlayer -OpeningOnly
# 对比 N/R/SR/UR 卡面并截取 SR 面闪在两个动画时刻的画面
./plugins/tools/story-runtime-test.ps1 -ReusePlayer -FoilOnly
# 验证小窗口的六档选择、保存、筛选和开包布局
./plugins/tools/story-runtime-test.ps1 -ReusePlayer -VisualOnly -Width 1024 -Height 576
# 在不含本体的 Git 工作树中，复用已有的本体和隔离客户端
./plugins/tools/story-runtime-test.ps1 -ReusePlayer -GameProject E:/AI/MDPro3/MDPro3 -PlayerRoot E:/AI/MDPro3/plugins/.selfcheck/story/player
```

运行时自检只接受工具创建的隔离客户端，不会改动正常故事存档。源码编译依赖本地 Unity 6000.0.24f1 和本体已有的构建响应文件；
工作树中的纯规则测试可用 `story-test.ps1 -GameBuild <本体客户端目录>` 指定只读的依赖位置。
正式使用时通过根目录原有的 `Run_MDPro3.bat` 自动同步、重建即可。本次故事功能的所有开发改动均位于 `plugins`。

---

### 编辑卡组界面：按「卡片发布时间」排序

- 位置：编辑卡组界面 → **排序** 按钮 → 弹出的排序窗口最后一行新增：

  | 第一列（条件名） | 第二列（↑） | 第三列（↓） |
  | --- | --- | --- |
  | `发布时间` | 老卡在前（升序） | 新卡在前（降序） |

  和现有的「种类 / 攻击力 / 守备力 / 稀有度 / Genesys分数」完全同排布，图标沿用游戏自带的升序 / 降序箭头，
  视觉效果与原版一致，移动端布局同样生效；手柄方向键链接也按新位置重新接好。
- 时间来源：`Data\pack\pack.db`（MDPro3 用 `PacksManager` 把卡包首发日期写进 `Card.year/month/day`），
  也就是卡牌详情里显示的那个收录日期，排序结果和详情页一致。
- MC 超先行卡虽然还没有正式卡包日期，但本体会标记为 `isPre`：新卡在前时排在全部有日期卡之前，老卡在前时排在
  全部有日期卡之后。其他没有卡包日期的卡一律排最后；日期相同或同为先行卡时保持游戏原本排序的相对顺序，结果稳定。
- 选中后卡表立即按发布时间重排，并且**之后每次搜索 / 切收藏 / 切历史 / 游戏重新打印卡表都会保持**这个顺序；
  排序按钮图标同步变成对应方向。再点任意一个游戏自带排序即恢复原版；关闭编辑卡组界面时也会自动恢复
  （和游戏自带排序一样，不会跨界面保留）。

---

### 主界面：卡包浏览

- 位置：主界面左侧菜单底部（「退出」上方）新增一个按钮 **`卡包`**。它由游戏自己的主菜单按钮克隆而来
  （同一套底板、悬停动画、选中光标和音效），只替换了点击行为。
- 打开后是**铺满屏幕的卡包墙**：`Data\pack\pack.db` 里的**全部 904 个商品卡包**，最新的在最前；
  「全部卡包」最前方另有一个动态的**先行卡包**。
- **先行卡包**只收录本体从萌卡（MC）`ygopro-super-pre.ypk` 的 `test-release*.cdb` 载入并标为
  `isPre` 的超先行服先行卡，不写死卡号。每次打开卡包浏览都会重新读取当前卡库，因此本体更新后新增、删除或替换的
  先行卡会在重启游戏并载入新数据后自动同步。这个虚拟包只出现在「全部卡包」，不会混入九个商品分类；封面取包内按
  可用罕贵度从高到低、卡号从小到大排列后的第一张卡（先用卡包罕贵度，先行数据未提供时用本体卡片稀有度），包内也
  保持这一顺序。没有载入超先行数据时卡包保留为空包。
  网格沿用游戏自带的 `SuperScrollView`，只实例化可视区域和少量缓冲行中的格子，滚动时回收复用。
- 顶部按顺序提供 **全部卡包、基本卡包、预组卡包、主题构筑卡包、主题强化卡包、动画卡包、漫画卡包、海外卡包、特别卡包、活动卡包**，
  每项显示卡包数量；切换后从列表顶部显示，仍按发售日期由新到旧排列。进入卡包后返回，会保留原分类。
  简体、繁体和游戏支持的其他语言均有对应标签，无卡包的分类显示空状态。

  分类参考 [Reko Wiki 系列卡包一览](https://rekowiki.org/wiki/遊☆戯☆王/系列卡包一覽)（2026-09-18 阅读），
  按商品系列而非卡片效果或封面角色划分。该页面只收录主要系列；下表对本地数据库中的早期商品、书籍、赠卡等作了补充约定。

  | 分类 | 商品系列 / 归属规则 |
  | --- | --- |
  | 基本卡包 | Vol.1～7、Booster 1～7、第二／三期通常补充包、SOD 起的通常补充包（包含页面已列出的 IMPH） |
  | 预组卡包 | SD、SR、STARTER、EX、角色预组、Duelist Set／Entry Deck、TACTICAL-TRY DECK、THE CHRONICLES DECK |
  | 主题构筑卡包 | Booster SP、Deck Build Pack、DUEL TERMINAL |
  | 主题强化卡包 | Duelist Pack、TERMINAL WORLD、TACTICAL-TRY PACK、REVOLUTION BOOSTER、LINK VRAINS PACK／DUELIST SET、SELECTION |
  | 动画卡包 | Collection／Collectors Pack、Animation Chronicle、MOVIE PACK、LIMITED PACK GX |
  | 漫画卡包 | PREMIUM PACK、漫画单行本附卡、Jump 杂志附卡／订阅特典、LIMITED EDITION、V JUMP EDITION、VP 应募包 |
  | 海外卡包 | EXTRA PACK、WORLD PREMIERE PACK 等海外先行卡引进系列；全球发售的通常补充包仍属于基本卡包 |
  | 特别卡包 | 周年纪念、收藏／复刻、礼盒、官方图鉴／攻略书、游戏／周边同捆商品，以及尚未识别的新商品 |
  | 活动卡包 | 大会奖品、TP／AT、Jump Festa／嘉年华、促销／联动／配布、电影入场／预售赠卡 |

  每个商品卡包只归入一个具体分类，「全部卡包」汇总所有商品分类并额外显示先行卡包。识别优先采用完整系列编号，
  并用商品名称处理早期无编号卡包；`DBLE` 等非构筑商品不会因为 `DB` 前缀被误分。
  分类在插件内完成，不修改 `pack.db`，运行时无需联网。新增常规系列编号时可更新 `PackCategory.cs`；
  无法识别的商品保留在「特别卡包」，不会从列表中消失。
- 基本卡包格子使用 Wiki 的数字序列显示（例如 SOD 显示为 `401`、DANE 显示为 `1008`）；
  第一期没有这类官方数字序列的 `Vol.1`、`Booster 1` 等保留原名。其他分类仍显示商品编号。
- **格子的样子**：卡包采用 Master Duel 风格的 200×420 长条包装。底部的金箔压纹、深蓝 V 形条纹、
  游戏字标和晶体三角标识从用户参考图提取，保留原版印刷细节。包装按卡包顺序循环使用
  **金、绿、红、橙、蓝、紫、黑、银**八种金属色，选中光框始终保持金色。顶部原卡图保持矩形直边和不透明背景，
  不使用透明立绘、圆弧裁剪或顶部遮罩。悬停/选中时显示白金内圈和金色辉光，包名位于光框下方。
  卡包格子为 224×482，进入卡包后切回 240×276 的完整卡片格子；输入和音效继续复用 `UI/ItemDeck.prefab`。

  | 用途 | 图槽 | 加载组件 | 效果 |
  | --- | --- | --- | --- |
  | **卡包封面** | 包装内约 198×405 | 游戏自带 `ArtRawImageHandler` | 原封面卡的**原画和背景**，按比例居中裁切到长条区域，不拉伸 |
  | **卡包内的卡片** | 卡片比例 156.4×228 | 游戏自带 `CardRawImageHandler` | **完整卡图**（723×1054 整张卡，和编辑卡组界面的卡表同一个组件） |

  封面卡的选取保持原规则，只在原画上叠加插件自带的包装。八色包装纹理按索引循环复用，金色光框由所有格子共用，
  通过 Resources 随游戏打包，运行时无需额外联网。同步脚本会复制源码中自带的 `.meta` 导入设置，
  同时保留 Unity 为其它文件自动生成的 `.meta`。
  用 `python plugins/tools/build_pack_wrapper.py` 可从插件内保留的印刷源图重建资源，无需原始截图。
- 封面的选取优先级（构建时已写死在 `PackCoverTable.g.cs`，运行时无需联网 / 无需读 json）：

  | 来源 | 数量 | 说明 |
  | --- | --- | --- |
  | 爬取的卡包一览 | 46 | 标题卡名与该包内某张卡能对上（相似度 ≥ 0.90） |
  | 包内唯一 HR 卡 | 26 | OCG 把 HR（全息闪）只印在封面卡上 |
  | 包内唯一单标签 QCSE | 6 | 25 周年那批 `-JP000` 封面卡 |
  | 包内唯一 UL 卡 | 28 | 2004～2007 年补充包的封面卡 |
  | 爬取数据（宽松匹配） | 31 | 卡名近似（≥ 0.55） |
  | 代表卡 | 767 | 其余卡包没有公开封面信息，取包内最高罕贵度（优先怪兽）的一张 |

  生成脚本：`plugins\tools\build_pack_cover_table.py`（读同目录的 `pack_covers_rekowiki.json` +
  `Data\pack\pack.db` + `locales/zh-TW/cards.cdb`，写出 `PackCoverTable.g.cs`）。
  **卡包数变化后重跑一次脚本即可**，不用改插件代码。
- 交互：

  | 操作 | 结果 |
  | --- | --- |
  | 点击右上角本体样式的后退按钮 | 卡片网格 → 回卡包墙；卡包墙 → 关闭 |
  | 点击卡包 | 换成该卡包的**卡片网格**（封面卡在最前，其后按罕贵度、卡号排序） |
  | 点击卡片 | 打开游戏原生的卡牌详情，可以用左右键在**整包卡片**里翻页 |
  | `Esc` / 鼠标右键 | 卡片网格 → 回卡包墙；卡包墙 → 关闭 |
  | 鼠标滚轮 | 滚动网格 |

- 界面打开期间用游戏自己的 `UIManager.InputBlocker` 阻断主菜单输入，所以按 Esc 不会同时触发菜单返回；
  打开卡牌详情时浏览器自动让出输入。离开主界面（例如进决斗）浏览器会自动关闭。

---

### 决斗开始：猜拳图标缺失修复

- 原弹窗从运行目录下的 `Picture\DIY\Rock.png`、`Paper.png`、`Scissors.png` 读取三个选项；这些文件缺失时，
  原版按钮仍可点击，但图片保持完全透明。
- 插件在猜拳弹窗出现时检查三个按钮；仍不可见的按钮会使用插件运行时生成的石头、布、剪刀图标，
  不依赖发布包外部文件。若原版 PNG 存在且成功加载，原版图片仍会正常覆盖兜底图标。
- 服务器返回结果后，插件会短暂显示我方与对方各自出的手势图片，并明确标出胜利、落败或平局；
  手动选择和“自动猜拳”都使用服务器的真实结果，不靠本地推断。

---

## 3. config.json：单功能开关

```json
{
  "features": [
    { "id": "releaseDateSort", "enabled": true, "note": "按卡片发布时间排序" },
    { "id": "packBrowser", "enabled": true, "note": "主界面卡包浏览" },
    { "id": "rpsVisualFix", "enabled": true, "note": "修复猜拳选项并显示双方结果图片" },
    { "id": "storyMode", "enabled": true, "note": "故事模式：角色挑战、DP 与收集构筑" }
  ],
  "logFeatureTicks": false,
  "logEvents": false
}
```

| 键 | 作用 |
| --- | --- |
| `features[].id` | 功能 id（= 功能类的 `Id`，注册表里能看到） |
| `features[].enabled` | `false` 就关掉这个功能（代码仍在游戏里，只是不启动） |
| `features[].note` | 仅供人看，插件忽略 |
| `logFeatureTicks` | `true` 时每 5 秒在日志里打印各功能 tick 的耗时（看性能用） |
| `logEvents` | `true` 时打印事件分发（看事件是否按预期触发） |

- **改完直接重启游戏即可生效，不需要重建**（JSON 是启动时读的）。
- 没有出现在列表里的功能按“默认开启”处理，所以以后新增功能不用先改配置。
- 写错 id 会在日志里收到 `config.json has an entry for an unknown feature: xxx` 警告。
- 文件找不到（例如把构建产物拷到了别的机器）就用默认值：全部开启；日志里会写明用的是默认值。
- `Run_MDPro3.bat --check` 会打印当前开关状态与配置文件路径。

---

## 4. 功能注册表 + 事件分发

### 4.1 宿主每帧做什么

`PluginHost` 是插件包唯一的常驻 MonoBehaviour，每帧只做两件廉价的事：

1. **观察环境（O(1) 引用比较）**：当前弹窗、当前 Servant、编辑卡组界面是否显示、卡表是否变化
   （卡表用「列表实例 + 数量 + 第一个元素」三个读操作判断，见 `PluginHost.WatchEnvironment`），
   **只有发生变化才广播事件**；
2. **tick 需要每帧工作的功能**：纯事件驱动的功能 `Tick()` 是空的，等于不花钱。

所以插件数量增加不会让每帧变慢：新增功能默认是“零每帧开销”的，除非它自己要求 tick。

### 4.2 事件列表（PluginEvents）

| 事件 | 何时触发 |
| --- | --- |
| `DeckEditorVisibilityChanged(bool)` | 编辑卡组界面打开 / 关闭 |
| `PopupChanged(Popup)` | 新 UI 体系的弹窗变化（null = 没有弹窗） |
| `ServantChanged(Servant)` | 主区域 Servant 变化（回到主菜单、进决斗等） |
| `CardCollectionChanged(CardCollectionView)` | 卡表视图新建、打印内容变化、编辑卡组关闭（参数为 null） |

需要新信号时：在 `PluginEvents` 里加一个事件 + 在 `PluginHost.WatchEnvironment` 里加一段比较与广播（各一两行）。
事件处理里抛异常只会记录一条错误，不会影响其它功能（`PluginEvents.Invoke` 逐个调用并兜底）。

### 4.3 新增一个功能（三步）

1. 新建目录 `Runtime/Features/你的功能/`，写一个继承 `PluginFeature` 的类：

```csharp
using MDPro3.UI;

namespace MDPro3.Plugins.Features.MyFeature
{
    public sealed class MyFeature : PluginFeature
    {
        public const string FeatureId = "myFeature";
        public override string Id => FeatureId;
        public override string DisplayName => "My feature";

        public override void Enable()
        {
            PluginEvents.CardCollectionChanged += OnCardCollectionChanged;
            Log("enabled");
        }

        public override void Disable()
        {
            PluginEvents.CardCollectionChanged -= OnCardCollectionChanged;
        }

        private void OnCardCollectionChanged(CardCollectionView view)
        {
            if (view == null)
                return;
            Log("card collection changed: " + (view.printedCards == null ? 0 : view.printedCards.Count) + " cards");
        }
    }
}
```

1. 在 `PluginRegistry.CreateAll()` 里加一行：

```csharp
yield return new Features.MyFeature.MyFeature();
```

1. （可选）在 `plugins/config.json` 的 `features` 里加一条，方便随时开关：

```json
{ "id": "myFeature", "enabled": true, "note": "说明" }
```

注意：注册是**显式**的，不做反射扫描 —— 启动更快、顺序确定，而且类被引用后 IL2CPP 构建（安卓）不会把它裁掉。
如果功能需要每帧工作，就重写 `Tick()`，但请保持廉价（先判断再干活，不要在里面 `Find`/`GetComponent`/分配内存），
用 `config.json` 的 `logFeatureTicks: true` 可以看到每个功能 tick 的实测耗时。

---

## 5. 启动参数

| 参数 | 作用 |
| --- | --- |
| 无参数 | 同步插件 →（插件变化时）重建 → 启动游戏（并打印功能开关状态） |
| `--check` | 只输出诊断信息：Unity / 构建产物 / 插件状态 / config.json 路径与功能开关 |
| `--rebuild` | 即使插件没变也强制重建 |
| `--no-build` | 跳过自动重建，直接启动现有游戏 |
| `--plugin-off` | 把插件从 Unity 工程移除并重建成原版行为 |
| `--diagnose` | 启动构建好的游戏跑插件自检，打印结果后自动退出 |
| `--help` | 帮助 |

> 插件改动后的首次启动会多花 1–3 分钟自动重建；重建时请先关掉正在运行的游戏（脚本会等 30 秒并提示）。
> Unity 编辑器正开着工程时无法批处理构建，此时在编辑器里点 Play 同样会加载插件。

---

## 6. 自检 / 诊断

```bat
Run_MDPro3.bat --diagnose
```

会启动构建好的游戏（`-mdpro3-plugin-selftest`）跑一遍自检再自动退出，检查：

- 插件是否被编译进玩家、版本号、`config.json` 实际读取路径；
- WindBot 读取对话 JSON 所需的 `System.Runtime.Serialization.Configuration` 类型和构造函数是否未被 Unity 裁剪；
- 猜拳结果包解析、胜负映射以及运行时只读观察器是否正常；
- **注册表与开关**：每个功能 id、状态（on/off）、是否正在运行；
- 真实卡库数据：卡数、有日期的卡数、最早 / 最新的卡与日期；
- 升序 / 降序是否有序，含“无日期卡放最后”；
- 排序弹窗注入：新行、文本、升/降图标、游戏行为是否已移除、是否重复注入；
- 真实流程：打开编辑卡组界面 → 游戏先打印全部卡表 → 切到插件排序 → 校验整表有序 → 让游戏再打印一次 →
  校验插件重新排好 → 打开排序窗口 → 校验新行存在、处于选中态、且没有游戏行被同时点亮。

在 `config.json` 里把 `releaseDateSort` 设为 `false` 时，自检会报告“该功能未运行、相关检查跳过”，结果仍是 PASS
（这时的期望就是插件不介入）。结果行固定为 `MDPro3Plugins self test: PASS / FAIL`，退出码 0 / 1；
日志：`plugins\.state\player-selftest.log`。

编辑器里还可以导出结构与插件设置报告（只读）：

```bat
"C:\Program Files\Unity\Hub\Editor\6000.0.24f1\Editor\Unity.exe" -batchmode -nographics -quit ^
  -projectPath "E:\AI\MDPro3\MDPro3" ^
  -executeMethod MDPro3.Plugins.EditorTools.PluginSelfCheck.DumpPrefabs ^
  -dumpPath   "E:\AI\MDPro3\plugins\.selfcheck\prefab-dump.txt"
```

---

## 7. 实现要点（为什么性能放心）

1. **编译期注入**：插件源码位于 `Assets\MDPro3Plugins`，Unity 直接编进 `Assembly-CSharp`，
   与游戏同程序集，可以直接用游戏内部 API，**无需**修改任何游戏源码，也没有额外的程序集加载 / 反射转发。
2. **一个常驻宿主**：所有功能共用一个 `PluginHost`，避免 N 个 MonoBehaviour 各自的 `Update` 开销。
3. **事件优先**：宿主用几个引用比较换事件广播，功能平时不轮询；只有确实需要连续帧工作的功能才 `Tick()`。
4. **重活只在必要时做**：卡表重排只发生在“打印内容真的变了”时（列表实例 / 数量 / 首元素三者之一变化），
   且重排后会把结果记下来，避免自己触发的重复计算。
5. **不污染游戏数据**：不写 `translation.conf`、不写 `Data`、不动卡组文件；界面文字由插件自带多语言表提供。
6. **按功能可关**：`config.json` 单个开关，改完重启即可，不需要重建。

---

## 8. 已知限制

- 插件是“编译进游戏”的方式：改插件后必须重建才能在大厅客户端看到（`Run_MDPro3.bat` 已自动处理）；
  改 `config.json` 不需要重建。
- 若游戏版本更新了排序弹窗结构（`PopupSearchOrder` 的 `Middle` 网格），注入会失败并在日志里给出一条
  `release date sort is unavailable: ...` 警告，插件其余部分不受影响；用上面的编辑器报告可以核对结构。
- 卡包日期取自 `Data\pack\pack.db`；没有该数据的卡（自制卡、部分先行卡）排在最后。
- 排序选择不写入配置：关闭编辑卡组界面即恢复游戏默认排序（与游戏自带排序行为一致）。
- `plugins` 目录被删除时，`Run_MDPro3.bat` 会打印一行提示并照常启动游戏（相当于没有插件）。
