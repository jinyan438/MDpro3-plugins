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
│  │  │  └─ ReleaseDateSort/     编辑卡组界面按卡片发布时间排序
│  │  │     ├─ ReleaseDateSortFeature.cs   功能本体（订阅事件、维护排序）
│  │  │     ├─ ReleaseDateSortToggle.cs    排序弹窗里的新条目行为
│  │  │     ├─ SearchOrderRowInjector.cs   把新条目注入排序弹窗
│  │  │     ├─ CardReleaseDate.cs          取卡包首发日期
│  │  │     └─ ReleaseDateSortLabels.cs    多语言标签
│  │  └─ Diagnostics/
│  │     └─ PluginSelfTest.cs    游戏内自检（--diagnose 使用，不属于功能）
│  └─ Editor/
│     └─ PluginSelfCheck.cs      编辑器自检（只读导出 UI 结构与插件设置报告）
├─ tools/
│  ├─ plugin-state.ps1           同步 / 签名 / 构建状态 / 文件占用检测 / 读取 config.json
│  └─ plugin-diagnose.ps1        在构建好的游戏里跑自检
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

### 编辑卡组界面：按「卡片发布时间」排序

- 位置：编辑卡组界面 → **排序** 按钮 → 弹出的排序窗口最后一行新增：

  | 第一列（条件名） | 第二列（↑） | 第三列（↓） |
  | --- | --- | --- |
  | `发布时间` | 老卡在前（升序） | 新卡在前（降序） |

  和现有的「种类 / 攻击力 / 守备力 / 稀有度 / Genesys分数」完全同排布，图标沿用游戏自带的升序 / 降序箭头，
  视觉效果与原版一致，移动端布局同样生效；手柄方向键链接也按新位置重新接好。
- 时间来源：`Data\pack\pack.db`（MDPro3 用 `PacksManager` 把卡包首发日期写进 `Card.year/month/day`），
  也就是卡牌详情里显示的那个收录日期，排序结果和详情页一致。
- 没有卡包日期的卡（当前约 800 多张）一律排最后；日期相同的卡保持游戏原本（种类）排序的相对顺序，结果稳定。
- 选中后卡表立即按发布时间重排，并且**之后每次搜索 / 切收藏 / 切历史 / 游戏重新打印卡表都会保持**这个顺序；
  排序按钮图标同步变成对应方向。再点任意一个游戏自带排序即恢复原版；关闭编辑卡组界面时也会自动恢复
  （和游戏自带排序一样，不会跨界面保留）。

---

## 3. config.json：单功能开关

```json
{
  "features": [
    { "id": "releaseDateSort", "enabled": true, "note": "按卡片发布时间排序" }
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
