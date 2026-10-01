# Log —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Log/README.md`——已知限制、设计取舍、
> 接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Assets/XFramework/Runtime/Log/
├── LogLevel.cs              公开：六档 + Off（仅作阈值）
├── LogCategory.cs           公开：interned readonly struct；default 合法（呈现 Unregistered）
├── LogCategories.cs         公开：33 个框架内置分类（集中声明，名字 = 迁移前的 [标签] 逐字）
├── LogEntry.cs              公开：一次日志的只读快照（sink 的输入形态）
├── LogSource.cs             公开：Framework / Unity
├── ILogSink.cs              公开：第三方扩展点（任意线程、不得抛、可选 IDisposable）
├── LogOptions.cs            公开：配置（档位 / 三个开关 / 目录 / 切分 / 保留数 / flush 策略 / 抓栈）
├── ILogManager.cs           公开：门面全量转发契约（30+ 成员）
├── LogManagerImpl.cs        internal：过滤、格式化、分发、摘除、捕获入口、帧号缓存
├── LogManager.cs            公开：静态门面 + AutoInit（SubsystemRegistration）
├── README.md                使用方文档
└── Internal/                命名空间 XFramework.XLog.Internal
    ├── LogCategoryState.cs  分类的可变状态（名字 + 档位覆盖；档位存这里，句柄复制后仍生效）
    ├── LogRegistry.cs       分类驻留表（copy-on-write 字典，读路径无锁）
    ├── LogFormatter.cs      模板格式化（走 string.Format(CurrentCulture, …)，失败退回原文）
    ├── LogTokens.cs         JSONL 的 lvl / src token
    ├── JsonLineWriter.cs    转义 + 时间戳 + 线程本地行缓冲
    ├── LogSession.cs        会话 id + 环境快照（由 AutoInit 在主线程复位）
    ├── ConsoleLogSink.cs    控制台输出端 + 回显深度（[ThreadStatic]）
    ├── LogFileSink.cs       JSONL 文件输出端（切分 / 轮转 / flush 策略 / 失败即静默）
    └── UnityLogCapture.cs   logMessageReceivedThreaded 的回调与去重

Tests/Runtime/Log/：LogTestKit、LogLevelFilterTests、LogRenderingFidelityTests、LogAllocationTests、
                    LogFileSinkTests、LogCaptureTests、LogConcurrencyTests、LogLifecycleTests
Tests/Editor/Log/： LogFormatterTests、LogCategoryRegistryTests、LogFacadeCompletenessTests、LogJsonEscapingTests
```

不开 `Internal/` 之外的子目录；测试 asmdef 沿用两个既有测试根，未新建。

## 迁移指南（18 个模块的回填）

**配方**（逐字保真：控制台文本与迁移前完全一致，30+ 处 `LogAssert` 零改动）：

```
Debug.LogWarning($"[Save] 跳过空存档文件: {path}")
→ LogManager.Warning(LogCategories.Save, "跳过空存档文件: {0}", path);
```

1. 级别映射固定：`Log→Info`、`Warning→Warning`、`Error→Error`（Release 默认档是 `Info`，可见性不变）。
2. 模板去掉 `[前缀] `——由分类渲染层补回。
3. `+` 拼接合并为一个模板，插值洞按序重编号为 `{0}`、`{1}`…；`{{` / `}}` 原样保留
   （`string.Format` 的转义规则与插值一致）。
4. **无插值洞的消息走「现成字符串」重载**——这同时是花括号安全线：带字面花括号又没有洞的消息
   永远不会被当模板解析（实测 193 处非插值串里含 `{`/`}` 的为 0 处，唯一带洞又带字面花括号的是
   `SettingsManagerImpl` 的 `{{Version, Data}}`，保持写法即可）。
5. 格式说明符照抄（`{x:F2}` → `{0:F2}`）；culture 由 `LogFormatter` 的 `CurrentCulture` 保证与插值同源。
6. 洞数 ≥ 4 的（实测仅 2 处，Config 与 Data 的长消息）先本地拼好再传现成字符串重载。

**分类映射**（分类名 = 现有标签逐字，`LogCategories` 字段同名）：

| 模块 | 标签 → 分类 | 备注 |
|---|---|---|
| Pipeline | `Pipeline` | |
| Bootstrap | `Bootstrap`、`GameLauncher` | 两个标签两个分类 |
| Config | `Config`、`ConfigManager` | 实现用 `Config`、门面用 `ConfigManager`，**不要合并** |
| Data | `Data` | |
| Save | `Save` | |
| Asset | `AssetManager`、`YooAssetManager` | 适配层与门面分开是有意的（信息量更大） |
| Settings | `SettingsManager` | 模块内统一 |
| UI | `UIManager`、`UIHudManager`、`UIDefaultController`、`UIPanelBase`、`UIPanelBinding`、`UITipManager`、`UITipItem` | 七个标签七个分类 |
| Input | `Input` | |
| Audio | `Audio` | |
| Timer | `Timer` | |
| Message | `Message` | |
| Lock | `Lock` | |
| Update | `UpdateScheduler`、`Update` | |
| Pool | `PoolManager`、`StringBuilderPool` + **泛型缓存** | `Pool<T>` / `ListPool<T>` / `HashSetPool<T>` / `DictionaryPool<K,V>` 的名字是运行时算的，用模块内 `PoolLogCategory<T>` 之类的泛型静态缓存注册（渲染逐字一致） |
| Localization | `LocalizationManager`、`LocalizationBootstrapStage` | `LanguageAssetLoader` **不在其中**：那三处是 `throw new InvalidOperationException($"[LanguageAssetLoader] …")` 的**异常消息**，不是日志 |
| File | `FileManager` | |
| Event | `Event` | |
| Serialize | — | **无需回填**：`[XSerialize]` 的两处是 `KeyNotFoundException` / `InvalidOperationException` 的消息前缀 |
| Reactive | — | **无需回填**：`[Reactive]` 的两处是 `ObjectDisposedException` 的消息前缀 |
| *(Editor)* | `XFramework` | `XFrameworkDependencyInstaller.cs` 的 6 处；其中 2 处原本**没有前缀**（全仓唯二违例），见「未决」 |

> **两类误收要记住**（回填时逐一核实过）：机械提取 `$"[标签]` 会把**异常消息**和**非日志字符串**
> 一起收进来——`[AudioManager]` 是 GameObject 名、`[Slot:…]` 是 `SaveMeta.ToString()`、
> `[XSerialize]` / `[Reactive]` / `[LanguageAssetLoader]` 是异常消息前缀。判定一个标签是否属于
> 日志，必须看**调用点**（`Debug.Log*` 还是 `throw new …`），不能看字符串。分类表最终 31 个。

**边界**：其它模块 README 里的 `Debug.Log` 代码片段**不改**——判据是 README 自己的判据
（「删掉它使用方会不会写错代码」），那些片段是示例业务代码在打日志，不是框架契约；教新 API 的地方
只有包 README 与 Log 模块 README。

## 已完成功能与未做（roadmap）

### 已完成（v1，2026-10-01）

六档分级 + 分类过滤（全局档 + 分类覆盖）· 模板 + 泛型参数（arity 0..3，未启用零分配）·
控制台输出端（逐字兼容迁移前文本与 LogType）· JSONL 文件输出端（每会话一文件 / 切分 / 轮转 /
flush 策略 / 失败即静默）· 全量捕获（引擎 / 第三方 / 未捕获异常，回显去重）· 门面永不抛 ·
AutoInit 自初始化（SubsystemRegistration，载入期即装入默认实现）· 门面完备性守卫 · 31 个内置分类。

**回填也已完成**（同日，20 个提交）：18 个模块 + Editor 工具 + Samples，约 190 处调用点，
按模块拆分、**控制台文案与 LogType 逐字不变**（30+ 处既有 `LogAssert` 一条未改）。
`Reactive` 与 `Serialize` 无需回填（无日志）。至此 Runtime 下除控制台输出端自己那三行外，
**不再有任何 `Debug.Log*` 调用**——框架里只有一条日志通路。

**异常入 JSONL**（同日后续）：`ILogManager.Exception` 增加级别可指定的重载，框架里 47 处
「异常只有文本 / 异常对象没进结构化字段」的站点全部改为把**异常对象**交给日志——17 处只传
`ex.Message` 的（含 Save 元数据解析的 3 个入口）、30 处「对象已作为格式实参传入、`ToString()`
混在 `msg` 里」的、以及 2 条把异常转成状态/文本后丢弃的主线（Pipeline 的用户阶段异常经
`PipelineStageContext.FailureException` 一路带到编排级终局日志；Save 的 `TryDeserializeSnapshot`
加 `out Exception`）。至此 `exc` 成为稳定 schema：`jq 'select(.exc)'` 可靠地取出
「类型 + 异常链 + 抛出点堆栈」，`msg` 恢复成「人读的一句话」。既有断言改了 **10 条**、落在
2 个文件（`UpdateSchedulerTests` 5 + `PipelineSubscriberIsolationTests` 5）——**都因为正则依赖
`": "` 这个已被换行取代的分隔符**。教训记在这里：评估「文案变化会不会弄红断言」时，
**不锚定的头匹配不敏感，但带尾部冒号或内嵌异常文本的会**，两类必须分开数
（本次首轮评估只数了前者，被 10 条里剩下的 7 条打回来）。

### 未做

| 项 | 为什么不做 |
|---|---|
| 会话级复位（与其它模块的静态门面同族问题） | 属框架级未决项，不在本模块范围内（见 `Documentation/Modules/Input.md` 的实测配方）。本模块的 `AutoInit` 已把「丢弃上一会话的实现 + 复位会话 id + 重挂退出订阅」做齐，可作为将来那件事的参照 |

## 已评估未采纳与未决

**静默 catch 的口径**（下一轮审计直接引用本节，不必重新普查）：

判据是一句话：**这个 catch 若被命中，有没有别的出口能让人看见**？据此，全仓「不记日志的 catch」
只有两类、七个位置，全部是有意为之：

| 位置 | 为什么不记 |
|---|---|
| `Bootstrap/GameLauncher.cs:37`（OCE） | 启动取消是正常路径（应用退出） |
| `File/FilePathUtility.cs:142` + `:146`（同一方法的两个子句） | 平台不支持原子写/`File.Replace` → 走降级路径，语义是「换一条路」不是「出错了」 |
| `Audio/AudioManagerImpl.cs:280`（OCE） | 只有 `Destroy` 会取消内部令牌，此时无调用方可通知 |
| `Message/MessageBroker.cs:954`（OCE，带 `when` 过滤） | 订阅已退订，属预期 |
| `Log/Internal/*` 的全部 catch | 模块承诺「永不抛、永不记日志」（见 README 的设计取舍） |
| `Pipeline/Pipeline.cs:353`（OCE 防御分支） | 语义上转为 Cancelled 并派发 `OnCancelled`，非静默 |
| `Pipeline/StageExecution.cs:34`（OCE） | 取消不是失败；**异常分支不在此列**——它现在会经 `FailureException` 落盘 |

其余所有 catch 要么 `throw;` 重抛（异常对象保留），要么记日志。

**已评估未采纳**（逐条列出，避免下一轮重新论证）：

| 项 | 不采纳的理由 |
|---|---|
| 砍掉 `ILogManager`（照 Timer 先例做纯静态门面） | 日志的「可替换后端」形式上存在（整体换 Serilog / 自研上报）；更要紧的是**门面转发完备性只有反射能锁，而反射需要一个接口**——多 30 个转发行换一条机械保证，值 |
| 第三方日志库（ZLogger / Serilog / NLog） | ZLogger 的核心卖点是 `InterpolatedStringHandler`，**需要 C# 10**，本包 `LangVersion 9.0`；且它们不解决本包真正的问题（分类前缀约定 + 与 Unity Console / `LogAssert` 逐字兼容） |
| 编译期剥离（`[Conditional("DEBUG")]`） | 现有日志 175/193 是 Warning/Error，Release 也必须输出；且它会连**实参求值**一起删掉，与「按档位过滤」语义不同 |
| 替换 `Debug.unityLogger.logHandler` | 全局副作用大（Unity Console、`LogAssert`、第三方工具都挂在上面），且 `ILogHandler.LogFormat(…, params object[])` 自身就分配数组 |
| 自研 formatter（不走 `string.Format`） | 逐字保真的免费证明就是「与插值同源」；自研要在 `{{`、null、`IFormattable`、culture 四处逐一对齐，任何一处差一点就是 30+ 处 `LogAssert` 变红 |
| per-sink 独立档位 | 会让 `IsEnabled` 的语义变成「有没有任何一个 sink 想要」，第三方无法推理；要「文件全收、控制台安静」用分类档位即可 |
| 结构化参数（`Warning(cat, "x {path}", …)` 带字段名） | 193 个调用点每个都要给参数起名，迁移成本翻倍；AI 分析已由「单行 JSON + cat/lvl」满足。记为未决 |
| 后台写线程 / `ConcurrentQueue` 泵 | 崩溃后 AI 要读的正是最后几条，而后台队列会把它们留在内存里；还需引入 Update 依赖或 PlayerLoop 注入，与「零模块依赖」冲突 |
| 异常入口的「形态 (a)」：把实参从 `ex.Message` 换成 `ex` | 零 API 增长，但 `exc` 仍为空、堆栈只是混进 `msg`——正是要消掉的那种混法（30 处「对象已传」的站点此前就处于这个终态的变体，正是它们促使了形态 (b)） |
| 异常入口的「形态 (c)」：让 `Log(...)` 家族也收异常 | +16 个公开成员，只为错误路径的模板一致性；错误路径上的 `string.Format` 相对栈展开可忽略，不值得用公开面换 |
| 给异常入口再加泛型重载（`Exception<T1>(level, cat, ex, template, arg)`） | 同上：14 个调用点全在 catch 块里，「未启用不格式化」是**每帧路径**的承诺（`LogAllocationTests` 的执行对象），不是错误路径的 |

**未决**（需单独裁定的两项，都属「改文案 = 仅对仓内的行为变更」）：

1. **Editor 两处无前缀 `Debug.LogError`**（`XFrameworkDependencyInstaller.cs:52,74`，全仓唯二违例）：经 `LogManager` 会自动补 `[XFramework] ` 前缀 = 改控制台文案。本轮按「逐字保真」保持原样未迁。
2. **`UIDefaultController` 的 5 条 verbose 日志保持 `Info`**：降到 `Debug` 会改变 Release 可见性（`verbose: true` 的使用方会突然看不到），属行为变更。

## 与其它模块的边界（下一轮审计时先看这里）

- **零模块依赖**：只用 `System.*` 与 `UnityEngine`；写文件走 `System.IO` 直写而**不经 `FileManager`**
  ——否则「FileManager 报错要记日志」就是循环。`LogCategories` 只存字符串，不引用任何模块。
- **反向依赖是单向的**：回填之后，所有模块 → Log，Log → 无。跨模块边界测试（`ModuleBoundaryTests`）
  对 `XFramework.XLog.Internal` 的判定天然通过（没有别的模块引用它）。
- **`AutoInit` 的档位是族级约定的一部分**：`SubsystemRegistration` 是最早一档，捕获回调必须在其它模块
  的最早 AutoInit 之前就位；改档位要同步 `Tests/Runtime/Architecture/AutoInitTests` 的族清单。
- **与 `LogAssert` 的关系**：控制台输出端是 `LogAssert` 唯一的通路，所以它默认恒开；关掉它测试全盲。
  捕获与文件输出端**不产生任何 Unity 日志**，因此不干扰既有断言。
- **与第三方库的关系**：YooAsset 的 `YooLogger` 等绕过本模块的日志，现在会以 `src:"unity"` 出现在同一份
  JSONL 里（`Asset` README 提到的那条「不带 `[AssetManager]` 前缀的 YooLogger 警告」因此可被统一检索）。
- **与 `XPipeline` 的边界**：`PipelineStageContext.FailureException` 是 **internal** —— 它是框架内部把
  「阶段抛了什么」从 `StageExecution` 带到编排级终局日志的承载，不是给使用方读的公开契约
  （使用方可见的失败描述仍是公开的 `Description`）。`ModuleBoundaryTests` 只扫公开签名，故天然通过。
