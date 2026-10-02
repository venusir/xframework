# XFramework

XFramework 是一个为 Unity 设计的**模块化基础设施框架**。它提供彼此正交的运行时服务，以及两套编排原语——通用异步管线与显式启动引导注册表。它**不预设 GamePlay 架构**：实体模型、生命周期组织与时间模型都由使用方自己决定。

## 设计哲学

- **单项职责的服务** — 每个模块只做一件事，彼此正交，可单独取用
- **静态外观 + 接口 + 内部实现** — 静态类统一入口 + 接口定义契约 + 内部类实现，外部可注入自定义实现
- **零配置或显式初始化** — 无参服务（LockManager、MessageManager、UpdateManager 等）经 `[RuntimeInitializeOnLoadMethod]` 自就绪；需要配置的服务由调用方显式 `Initialize`，或实现 `IBootstrapStage` 交给启动引导。**「零配置」指默认可用、按需覆盖**——每个模块都有合理的默认值或无参路径，需要时才覆盖；全部可配置项见「可配置项一览」
- **通用编排 + 相位分组** — `Pipeline` 提供串行/并行阶段编排、加权进度、失败即停与取消传播；实现 `IPhaseStage` 声明相位号（同相位并行、相位升序串行），`Pipeline.BuildPhaseGroups` 一键装配
- **更新按需降级** — `IUpdateable.OnUpdate` 返回 `UpdateTier` 等级，调度器自动调整其更新频率；档位按**时长**分档，与帧率无关
- **不预设 GamePlay 架构** — 框架不决定实体模型、生命周期树与时间模型

## 核心架构

框架分两层：**彼此正交的基础设施服务**，与**把它们按顺序装配起来的编排原语**。

### 基础设施服务

| 服务                    | 初始化方式                                          | 说明                                                       |
| ----------------------- | --------------------------------------------------- | ---------------------------------------------------------- |
| **LockManager**         | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置                                                     |
| **MessageManager**      | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置                                                     |
| **UpdateManager**       | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置，含 PlayerLoop 自注入——场景里不需要任何 MonoBehaviour |
| **Serializer**          | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 注册内置序列化器（json / json-utility）                    |
| **FileManager**         | 首次调用时懒加载（自动选平台 Provider）             | 零配置；也可显式 `Initialize()` 注入自定义 Provider        |
| **AssetManager**        | `AssetManager.InitializeAsync()`                    | 或实现 `AssetBootstrapStage` 交由启动引导                  |
| **DataManager**         | `DataManager.Initialize(impl)`                      | 同上（`DataBootstrapStage`）                               |
| **SaveManager**         | `SaveManager.Initialize(options)`                   | 同上（`SaveBootstrapStage`）                               |
| **LocalizationManager** | `LocalizationManager.Initialize(lang, data)`        | 同上（`LocalizationBootstrapStage`）                       |
| **UIManager**           | `UIManager.Initialize(canvasTransform)`             | 需传入 Canvas 根节点                                       |
| **SettingsManager**     | `SettingsManager.Initialize<T>(path)`               | 设置类型由业务定义，每类独立初始化                         |
| **AudioManager**        | `AudioManager.Initialize()`                         | 零配置；播放源惰性创建，从不播放就不建任何对象             |
| **TimerManager**        | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置；没有正在计时的定时器时驱动器退出调度               |
| **LogManager**          | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置；分级 + 分类过滤；Editor/Development 下自动写 JSONL |
| **PoolManager**         | 首次 `Get<T>()` 时自动建池                          | 零配置；`Configure<T>` 只在建池前生效                     |
| **ConfigManager**       | `ConfigManager.Initialize()`（无参）                | 零配置；数据由 `Register` / 批量加载注入，路径语义由调用方定 |

> **关键设计决策：** 服务不依赖统一入口。无需参数的服务自动就绪；需要参数的服务由调用方显式初始化，或实现 `IBootstrapStage` 登记进启动引导。

### 启动引导

需要按顺序初始化的服务实现 `IBootstrapStage`，然后显式登记：

```csharp
Bootstrap.RegisterDefaults();                                          // Asset(0) → Data(3) → Save(4)
Bootstrap.Register(new LocalizationBootstrapStage("zh_Hans", myTable)); // Phase 90
await Bootstrap.RunAsync(progress: myProgress);
// ...
Bootstrap.Shutdown();                                                  // 按执行序逆序清理（相位降序）
```

`RunAsync` 在失败与取消时**抛出**——启动失败是致命的，不该只留一条日志。整块是**可选**的：零配置项目与自建启动流程的项目都可以不用它。

挂 `DefaultGameLauncher`（场景组件）是零配置起步；要带配置就填它的 Inspector 字段，或继承它覆写 `ConfigureStages()`。要完全自控（Inspector 里一个字段都不要）则继承抽象底座 `GameLauncher` 并实现 `ConfigureStages()`。

> 📖 详见 **[Runtime/Bootstrap/README.md](Runtime/Bootstrap/README.md)**

### 通用管线

与启动引导正交。任何「若干异步阶段按序/并行执行 + 加权进度 + 失败即停」的场合都可直接用：

```csharp
var pipeline = Pipeline.Create();
pipeline.AddStage(new MyStage());
await pipeline.RunAsync(cancellationToken);
pipeline.Destroy();
```

实现 `IPhaseStage` 声明相位号，`Pipeline.BuildPhaseGroups` 即自动分组（同相位并行、相位升序串行）。

> 📖 详见 **[Runtime/Pipeline/README.md](Runtime/Pipeline/README.md)**

## 快速开始

### 方式一：零配置使用

```csharp
// 无需手动初始化任何服务！LockManager、MessageManager 已自动就绪。

// 直接使用消息系统
MessageManager.Subscribe<PlayerDiedMessage>(msg =>
{
    Debug.Log($"Player {msg.PlayerId} died!");
});

// 直接使用锁系统
using (LockManager.AddLock(player, LockType.InputBlock, this))
{
    // 此时玩家输入被锁
}
```

> 锁的用法要点：`player` 需实现 `ILockable`（**主体** = 锁谁）；`LockType` 是使用方自建的常量类
> （**类型** = 哪一类事，全框架共享命名空间，跨模块要先约定不撞号）；`this` 是**持有者身份**（谁锁的）。
> 三者构成一把锁，多来源叠加时各自只解自己那把——详见 [Runtime/Lock/README.md](Runtime/Lock/README.md)。

```csharp
// 锁类型由使用方定义（完整写法与理由见 Lock 模块 README §1）
public static class LockType
{
    public const int InputBlock = 1;
}
```

### 方式二：带启动引导

```csharp
// 场景里挂一个 DefaultGameLauncher 即可——它带一组 Inspector 配置字段（Asset 包名/模式、存档版本、
// UI 根、输入资产、默认语言），填了就注入对应模块，留空则那个模块不初始化：
// 或在自己的启动流程里显式调用：
Bootstrap.RegisterDefaults();
await Bootstrap.RunAsync();
```

## 主要功能

| 功能             | 说明                                                                              |
| ---------------- | --------------------------------------------------------------------------------- |
| **更新调度**     | `UpdateManager` + `UpdateTier` 时间切片，自动档位迁移；档位按**时长**分档，与帧率无关 |
| **通用管线**     | `Pipeline` 阶段编排（串行/并行/加权进度/失败即停/取消传播）；`IPhaseStage` 相位分组一键装配 |
| **启动引导**     | `Bootstrap` 显式登记 + 相位装配 + 逆序清理；内置 Asset/Data/Save 三件，Localization 可选 |
| **对象池**       | `PoolManager` + `CollectionPool`（List / HashSet / Dictionary / StringBuilder）    |
| **UI 面板管理**  | `UIManager.OpenAsync<T>()` 异步打开/关闭面板，支持栈式导航、模态遮罩              |
| **Tip 临时提示** | 扣血提示、浮动文字等临时 UI，支持世界坐标定位、渐隐动画、对象池复用               |
| **配置管理**     | `ConfigManager` 内置 Json / CSV / ScriptableObject 格式，支持自定义 Loader 与 Register 注入，一行代码加载与查询 |
| **消息总线**     | `MessageManager` 类型化发布/订阅、带 Key 通道、缓冲重放、异步发布 `PublishAsync`、请求-响应、全局过滤器、缓冲淘汰与运行统计 |
| **逻辑锁**       | `LockManager` 多来源叠加的门禁（主体 × 类型 × 持有者）：全局锁、聚合状态事件 `OnLockStateChanged`、销毁自动释放、`DumpState` 排查 |
| **响应式属性**   | `ReactiveProperty<T>` 状态同步：订阅即回调当前值、相同值去重，支持 `Select` 派生与 UI 绑定 |
| **事件流**       | `EventStream<T>` 订阅句柄 + 异常隔离 + 重放缓存；是 Message 与 Reactive 的共同底层，也可单独取用 |
| **输入抽象**     | `InputManager` 纯字符串 API（按下/按住/长按/轴值）+ 多设备检测 + 零 GC 热路径；默认提供者基于 Unity Input System，可整体替换（如 Rewired），资源加载方式自选 |
| **音频播放**     | `AudioManager` 字符串通道 + 三档音量相乘、location 异步加载、代际安全句柄、池化播放源与自动回收；公开面不含 Unity 音频类型，可整体替换实现以接 Wwise / FMOD |
| **定时器**       | `TimerManager` 一次性延时与固定间隔，返回可查询剩余量、可重开、零分配的句柄；不漂移、跳拍不补发；按最近的截止自动升降 Update 档位（60 秒的定时器每 2.1 秒才被扫一次）；暂停与时间缩放完全转接自 Update 的双时间轴 |
| **日志**         | `LogManager` 六档分级 + 分类过滤（`[模块]` 前缀由分类渲染，不再手抄）+ 模板化调用（未启用不格式化、零分配）；控制台文本与迁移前逐字一致；每条日志一行 JSONL 落盘，并把引擎 / 第三方 / 未捕获异常收进同一条时间线供 AI 分析 |

## 模块索引

| 模块 | 命名空间 | 职责 | 文档 |
|---|---|---|---|
| **Bootstrap** | `XFramework.XBootstrap` | 启动引导：显式登记引导阶段、按相位装配运行启动管线、退出时反向清理 | [README](Runtime/Bootstrap/README.md) |
| **Pipeline** | `XFramework.XPipeline` | 通用编排：阶段串行/并行/容器嵌套、加权进度聚合、失败与取消传播 | [README](Runtime/Pipeline/README.md) |
| **Asset** | `XFramework.XAsset` | 资源管理：异步加载、实例化、对象池、场景加载（基于 YooAsset） | [README](Runtime/Asset/README.md) |
| **Update** | `XFramework.XUpdate` | 统一更新调度：三个派发时机、双时间轴、档位时间切片、PlayerLoop 自驱动 | [README](Runtime/Update/README.md) |
| **Event** | `XFramework.XEvent` | 事件流引擎：订阅句柄、异常隔离、重放缓存（Message / Reactive 的共同底层） | [README](Runtime/Event/README.md) |
| **Message** | `XFramework.XMessage` | 消息总线：按类型 / Key 治理通道、过滤器管道、异步订阅、请求-响应 | [README](Runtime/Message/README.md) |
| **Reactive** | `XFramework.XReactive` | 响应式属性（基于 Event 事件流） | [README](Runtime/Reactive/README.md) |
| **Localization** | `XFramework.XLocalization` | 本地化：多语言文本、语言切换、UI 自动绑定 | [README](Runtime/Localization/README.md) |
| **File** | `XFramework.XFileManager` | 跨平台文件系统：路径域抽象、平台 Provider、原子写与一代备份、按域加密 | [README](Runtime/File/README.md) |
| **Data** | `XFramework.XData` | 数据块管理：快照收集/应用、逐块版本迁移链、脏标记 | [README](Runtime/Data/README.md) |
| **Serialize** | `XFramework.XSerialize` | 序列化注册表：按格式名取用（json / json-utility），供 Data / Save / Config 复用 | [README](Runtime/Serialize/README.md) |
| **Save** | `XFramework.XSave` | 存档：原子写与备份恢复、元数据侧车、版本门禁、玩家隔离、槽位复制移动 | [README](Runtime/Save/README.md) |
| **Config** | `XFramework.XConfig` | 配置表：Json / CSV / ScriptableObject 三种格式、自定义 Loader、声明式批量加载 | [README](Runtime/Config/README.md) |
| **Pool** | `XFramework.XPool` | 对象池与集合池（List / HashSet / Dictionary / StringBuilder） | [README](Runtime/Pool/README.md) |
| **Input** | `XFramework.XInput` | 输入抽象层：纯字符串 API、多设备检测、零 GC | [README](Runtime/Input/README.md) |
| **Audio** | `XFramework.XAudio` | 音频：字符串通道、三档音量相乘、location 加载、池化播放源 | [README](Runtime/Audio/README.md) |
| **Timer** | `XFramework.XTimer` | 定时器：一次性延时与固定间隔、可查询句柄、与 Update 档位联动 | [README](Runtime/Timer/README.md) |
| **Log** | `XFramework.XLog` | 日志：六档分级 + 分类过滤、模板化调用、JSONL 落盘与全量捕获 | [README](Runtime/Log/README.md) |
| **Settings** | `XFramework.XSettings` | 强类型游戏设置：纯 POCO 持久化、字段句柄、版本迁移 | [README](Runtime/Settings/README.md) |
| **UI** | `XFramework.XUI` | UI 面板管理 / MVVM 绑定 / 导航堆栈 / HUD / Tip | [README](Runtime/UI/README.md) |
| **Lock** | `XFramework.XLock` | 逻辑锁：多类型锁叠加、全局锁、`using` 自动释放 | [README](Runtime/Lock/README.md) |
| **Diagnostics** | `XFramework.XDiagnostics` | 诊断契约：页签注册 + 报告采集（渲染在 Editor 侧，菜单 `Tools/XFramework/Diagnostics`） | [README](Runtime/Diagnostics/README.md) |

## 可替换的实现一览

**「换后端」与「配参数」是两条不同的路**：实现类一律经接口注入（服务实例是代码），参数一律经 options（数据）。下表是**全部可替换点**；写「无」的模块是**刻意不提供**，理由就写在那一格（多与「它是不是纯静态服务」有关）。

| 模块 | 替换契约 | 接线入口 |
|---|---|---|
| **Log** | `ILogManager`（整体）+ `ILogSink`（追加输出端） | `Initialize(ILogManager)` / `AddSink` / `RemoveSink` |
| **Diagnostics** | 无——注册表是纯静态服务，没有后端可换；要换的是**页签**（`IDiagnosticPanel` 登记）与**渲染**（`IDiagnosticPanelView` 自绘） | `DiagnosticsManager.Register` / 实现 `IDiagnosticPanelView` |
| **Asset** | `IAssetManager`（能力接口 `IAssetPoolController` 由门面探测） | `SetInstance(IAssetManager)` |
| **Audio** | `IAudioManager` | `SetInstance(IAudioManager)` |
| **Save** | `ISaveManager`（工厂委托 `SaveManagerFactory`） | `Initialize(factory, options)` |
| **File** | `IFileProvider`（可选能力 `IAtomicFileProvider` / `IDirectoryProvider`）；`ICryptoProvider` 按域接线 | `Initialize(provider)`——**一次性**，换它必须先 `Destroy()`；`SetCryptoProvider` 可随时调 |
| **Settings** | `ISettingsStore` / `ISettingsMigrator<T>` / `ISettingsValidator<T>`（**按类型**注入；`IAsyncSettingsStore` 是 store 上的能力探测，不是独立注入点） | 三个 `Initialize<T>` 重载 / `SetStore<T>` / `SetMigrator<T>` / `SetValidator<T>` |
| **Localization** | `ILocalizationManager` | `SetInstance(ILocalizationManager)` |
| **UI** | `IUIManager` + 三个 provider：`IUIController` / `IUITipProvider` / `IUiHudProvider` | `SetInstance` / `SetController` / `SetTipProvider` / `SetHudProvider` |
| **Input** | `IInputProvider`（28 成员） | `Initialize(IInputProvider)`（**生产路径**，自动注册帧驱动）；`SetProvider` 不注册帧驱动，是测试注入路径 |
| **Config** | `IConfigManager`；`IConfigLoader` 逐次传入（**无法全局注册新格式**） | `SetInstance(IConfigManager)` |
| **Data** | `IDataManager` | `Initialize(IDataManager)`——传 `null` 等价于 `Shutdown()` |
| **Serialize** | `ISerializer`（按 `Format` 字符串注册） | `Register` / `Unregister` |
| **Message** | **无** | `IMessageBroker` 是 internal，XML 原话：「刻意收敛，不是遗漏」——零配置纯静态服务 |
| **Update** | **无** | 自管理的静态服务（`AutoInit` + PlayerLoop 注入） |
| **Timer** | **无** | 代码原话：「纯静态服务，没有 `ITimerManager` 这类后端替换点」 |
| **Lock** | **无** | 静态服务；`ILockable` 是「谁被锁」的主体契约，不是后端 |
| **Event** | **无** | 「公开接口 + 静态工厂 + internal 实现」——接口可自行实现，但框架内没有接受第三方实现的入口 |
| **Reactive** | **无** | 接口公开（可面向接口编程、可自定义实现），但没有注入句柄 |
| **Pool** | **部分** | 管理器级无注入点；单个池可用公开构造自建（`Pool<T>` + `IPool<T>`），`GetPool<T>()` 做依赖反转 |
| **Bootstrap / Pipeline** | 不是「换后端」，是**装配入口** | `Bootstrap.Register` / `Unregister<T>`；`Pipeline.Create()` + `AddStage` / `BuildPhaseGroups` |

## 可配置项一览

**「零配置」指的是「不配置也能跑」，不是「没有可配置项」**——每个模块都有可用的默认值或无参路径，需要时才覆盖。下表是**全部配置面**的索引（每模块一行，细节在各自的 README）。

> **收录判据**：只有**能持久改变框架行为的选择**才进表——`Clear()` / `Destroy()` / `RunAsync()` 这类纯操作不列（否则会退化成 API 清单）。路径与目录类见下一节。

| 模块 | 你能配置什么（入口） | 何时生效 |
|---|---|---|
| **Log** | `LogOptions` 11 字段（`LogManager.Configure`——**会丢弃已加的自定义 sink 并复位分类档位**）；`ILogSink` 追加 / 移除；`ILogManager` 整体替换（**注入非内置实现会停用 Unity 全量捕获**，替换前会有一条 `[LogManager]` 提醒）；`MinimumLevel` / `SetCategoryLevel` / `ResetCategoryLevel`；自定义分类 `LogCategory.Get` | options 在 `Configure` 时快照；档位与输出端实时 |
| **Asset** | `AssetInitOptions` 5 字段（主包名 / PlayMode / 远端服务 / 解密服务 / 低内存回收；**`DefaultGameLauncher` 字段面覆盖第 1、2、4 项**，两个服务走覆写）；`IAssetManager` 整体替换；运行时 `SetPoolMaxSize` / `CreateDownloader` 参数 | 初始化时；运行时项实时 |
| **Audio** | `AudioInitOptions` 4 字段；`IAudioManager` 整体替换；运行时 `MasterVolume` / `MasterMuted` / `SetChannelVolume` / `SetChannelMuted` / `RegisterChannel` / `Pause` / `Resume`；每次播放 `AudioPlayOptions` 6 字段、`RegisterChannel` 用 `AudioChannelConfig` 2 字段；`AudioChannels` 推荐通道名（`"master"` 是保留名） | 初始化时快照；运行时项立即扫活跃播放源 |
| **Settings** | `SettingsOptions` 5 字段；**按类型**注入 `ISettingsStore` / `ISettingsMigrator<T>` / `ISettingsValidator<T>`（均可运行时替换；`IAsyncSettingsStore` 是 store 上的能力探测，不是独立注入点）；`defaultFactory` 决定持久层无数据时的默认值 | options 初始化时快照；注入点实时（换 Store 后需自行 `Load`） |
| **UI** | `Initialize(Transform uiRoot, IUIController = null)`（或登记 `UIBootstrapStage` / `DefaultGameLauncher` 字段面填 UI 根）；`IUIManager` 与三个 provider（`IUIController` / `IUITipProvider` / `IUiHudProvider`）均可整体替换；`TipAssetPath`（**静态属性**，注入自定义 `IUIManager` 或 `IUITipProvider` 后失效）；每次 Tip 用 `TipConfig`、遮罩用 `UIMaskStyle`；`UILayers` 是**建议值**、`UISorting` 是**推导源（不可配）**；`OpenAsync` / `PushAsync` 的 `layer` 与 `ShowMask` 的 `alpha` 默认值；实例参数（`UpdateTier` / `FollowTarget` / `ScreenOffset`）；`UIRootNode.applySafeArea` | provider 换后即时；`TipAssetPath` 下次显示生效；实例参数每帧读 |
| **Input** | 自己加载 actions 资产后 `Initialize(new InputSystemOptions { Asset = …, InitialActionMap = … })`（或登记 `InputBootstrapStage` / `DefaultGameLauncher` 字段面指向一份 `InputSystemOptionsAsset`）；**换后端**：`Initialize(IInputProvider)` 或 `InputBootstrapStage(IInputProvider)`（`SetProvider` 不注册帧驱动）——无参 `Initialize()` 走 `Resources` 默认 + `"Player"` map | 初始化时 |
| **Save** | `SaveOptions` 2 字段（版本号 / 加密 Provider，后者接线到 `FileDomain.SaveData`）；`SaveManagerFactory` 整体替换；运行时 `SetCurrentVersion` / `SetCurrentPlayer` / `ClearCurrentPlayer` | 初始化时；运行时项实时（**写操作进行中会抛** `InvalidOperationException`） |
| **File** | `IFileProvider` 整体替换（**一次性**：换它必须先 `Destroy()`，重复 `Initialize` 只告警忽略）；`ICryptoProvider` 按域接线（`SetCryptoProvider`，可随时调）；可选能力接口 `IAtomicFileProvider` / `IDirectoryProvider`（装饰器须一并实现）；**四个域根不可配**（换根 = 换 provider） | provider 在首次 `Initialize` 时定；加密与运行时项实时 |
| **Pool** | `PoolConfig` 3 字段；`PoolManager.Configure<T>`（**池已存在则忽略**，需先 `RemovePool<T>`）与四个集合池各自的 `Configure`（**无活跃实例时**生效，会重建池）；`Pool<T>` 公开构造可自带 `onRent` / `onReturn` / `onDestroy` 与 `IPoolable` / `IPoolDiscardable` 钩子 | 见左列两种口径；集合池在「无活跃实例」那一刻重配 |
| **Config** | `IConfigManager` 整体替换；`IConfigLoader` 自定义格式（**逐次传入，无法全局注册**）；`RegisterTable` / `RegisterGlobal` 注入已反序列化数据；`ConfigManifest` 声明式批量清单（`ConfigFormat`：Json / ScriptableObject / Csv）——**无 options** | 注册即生效；Loader 随调用 |
| **Message** | 全局过滤器 `AddFilter<T>` / `RemoveFilter` / `ClearFilters<T>`；请求-响应 `Register<TReq,TRes>` / `Unregister`；缓冲淘汰 `EvictBufferedChannel(s)` / `TrimEmptyChannels`；`PublishAsync` 的调度策略 `MessagePublishStrategy`——**统计没有开关**（常开） | 实时 |
| **Data** | `IDataManager` 整体替换（`DataManager.Initialize(impl)`，**传 `null` 等价于 `Shutdown()`**）——**无 options** | 初始化时 |
| **Localization** | `ILocalizationManager` 整体替换；`Initialize(lang, data)` / `SetLanguageData` 注入数据；`LanguageAssetPath`（语言表地址模板）/ `FallbackLanguage`；`LocalizationBootstrapStage`（含 `TextAsset` 重载）——**换实现换不掉 `SwitchLanguageAsync` 的加载路径**（它走门面内部的 loader，见模块 README） | 运行时实时（下次切换 / 下次回退读取） |
| **Serialize** | `ISerializer` 按格式名 `Register` / `Unregister`（公开 `Register` 同名覆盖；**`Initialize` 遇同名注册保留使用方的并告警**）；`Get` / `TryGet` / `Default` 取用 | 实时 |
| **Bootstrap** | 登记表：`Register` / `Unregister` / `Unregister<T>` / `RegisterDefaults` / `Clear`（`Stages` 是实时只读视图）——**无 options**；相位号常量见 `BootstrapPhases`；`DefaultGameLauncher` 的八个 Inspector 字段 + `ConfigureStages()` 覆写 | 实时（须在 `RunAsync` 之前） |
| **Update** | 注册参数（`order` / `initialTier` / `timeMode`——**`RegisterFixed` 没有 `timeMode`**）；`AutoDriveEnabled`（关掉即停止 PlayerLoop 自动派发，改为自行 `Tick`）；运行时 `Pause` / `Resume` / `Clear` / `Enable` / `Disable`——**无 options**；`timeMode` 注册时读一次 | 注册时 / 实时 |
| **Lock** | `AutoReleaseOnDestroy`（运行时可变；关掉后**不再新建销毁绑定**，且对**已销毁主体**的操作不再被拒）；`LockType` 是**使用方自建**的常量类——框架不预设锁类型 | 实时 |
| **Event** | **无配置点**（订阅期钩子 `EventStream.Create(onEmpty)` 除外） | — |
| **Reactive** | 构造期：`ReactiveProperty<T>(T initialValue, IEqualityComparer<T> comparer)`——自定义「相同值不通知」的相等语义（浮点容差等）；`Select(..., comparer)` 对派生值同理 | 构造时 |
| **Timer** | **无配置点**（每次调用的 `UpdateTimeMode` 与取消令牌） | — |
| **Pipeline** | 装配期：`AddStage(stage, timeoutSeconds)` / `IPhaseStage.Phase` / `IPipelineStage.Name`·`Weight` / `ParallelStage`·`SequenceStage`（可传 `name`）/ `BuildPhaseGroups(..., nameFormat)` / 四个终局与进度事件——**无 options** | 装配时 |

## 路径与目录

框架**不在业务资源上发明路径**（面板 / HUD / 音频 / 配置表一律以 YooAsset `location` 字符串由你传入），但**框架自己**的落点与命名如下。「能否改」一列是硬事实：写「不能」的都只能整体替换对应的实现（见上一节）。

| 路径 / 命名 | 默认值 | 能否改 | 改的入口 |
|---|---|---|---|
| 日志落盘目录 | `{persistentDataPath}/XLog`（取不到回退 `{临时目录}/XLog`） | **能** | `LogOptions.FileDirectory` + `LogManager.Configure` |
| 日志文件命名 | `xlog-{时间}-{会话}-p{n}.jsonl` | **不能** | 内置 sink 的私有常量——要换命名只能自行实现 `ILogSink` / `ILogManager` |
| 资源默认包名 | `DefaultPackage` | **能** | `AssetInitOptions.PackageName`（`DefaultGameLauncher` 字段面同名项）；额外包走 `InitializePackageAsync`，**它不改主包** |
| YooAsset 文件系统根（`packageRoot`）/ 文件系统类 | YooAsset 工厂的默认值 | **不能** | 只能整体替换 `IAssetManager` |
| 存档域与槽位布局 | `FileDomain.SaveData` + `{playerId}/slot_{N}.save`（`.meta` 侧车；无 playerId 时直接落域根） | **不能** | 只能整体替换 `ISaveManager`（`SaveManagerFactory`） |
| File 四个域根（AppData / SaveData / Streaming / Cache） | 由 Unity 的 `persistentDataPath` / `streamingAssetsPath` / `temporaryCachePath` 决定 | **不能** | 换根 = 换 `IFileProvider`。（原子写的 `.tmp` / `.bak` 后缀是 `FilePathUtility` 上的 **`public const`**——可读、不可改） |
| Settings 零配置落点 | `{persistentDataPath}/{类型短名}.json`（伴生 `.tmp` / `.bak`） | **能** | 显式路径重载 `Initialize<T>(filePath, …)`，或注入自定义 `ISettingsStore` |
| 语言表地址模板 | `localization/lang_{0}` | **能** | `LocalizationManager.LanguageAssetPath`（初始化后仍可改） |
| Tip 预制体地址 | `PF_UITipText` | **能** | `UIManager.TipAssetPath`（下次显示生效；注入自定义 `IUIManager` 或 `IUITipProvider` 后失效） |
| UI 层容器命名 | `Layer_{层号}` / `Layer_HUD` / `Layer_Tip` | **不能** | 内部字面量（层号是 `int`——见 UI README 的已知限制） |
| 音频宿主与播放源命名 | `[AudioManager] Audio Host` / `Voice {index}` | **不能** | 内部字面量（Audio README 已登记） |
| 输入默认资产 | `Resources/InputSystem_Actions` | **不能只改名** | `Resources` 是零配置默认；换来源用 `InputSystemOptions` 自己加载，换后端实现 `IInputProvider` |
| PlayerLoop 三个驱动系统名 | `ScriptRunBehaviourUpdate` 等三个 | **不能** | 私有常量；注入失败会告警并提示手动 `Tick` |

## 框架约定的名字

框架刻意**不提供统一的配置文件**：参数由各模块自己的 `Initialize(options)` 接收——**参数即契约**，读签名就知道这个模块依赖什么；一个全局配置文件会把依赖变成隐式的，也会让「模块可单独取用」失效（同「服务不依赖统一入口」那条设计决策）。**路径与目录类的名字见上一节**，这里只剩非路径的名字。**权威定义在对应模块的 README**，本表只是入口。

| 名字 / 值 | 性质与能否更改 | 详情 |
|---|---|---|
| `Player`（初始 ActionMap 名） | **可改**：`InputSystemOptions.InitialActionMap`（`DefaultGameLauncher` 字段面填 `InputSystemOptionsAsset` 时是它的同名项）；传 `null` / 空串则不自动切换（保持全部 map 常开） | [Input](Runtime/Input/README.md) |
| `"master"`（音频保留通道名） | **不能当通道用**：总音量由 `MasterVolume` / `MasterMuted` 表达；把它传给通道 API 会抛 `ArgumentException` | [Audio](Runtime/Audio/README.md) |
| `AudioChannels` 推荐通道名 | **建议值**：`default` / `bgm` / `se` / `voice` / `ui`——通道域是开放字符串，自建自己的通道名即可 | [Audio](Runtime/Audio/README.md) |
| `UILayers` / `UISorting` | `UILayers` 是**建议值**（可以给自己的层号）；`UISorting` 是排序空间的**推导源（不变量）**——改常量不会让框架按新值重排 | [UI](Runtime/UI/README.md) |

## UI 系统

XFramework 提供一套完整的 UI 管理方案，包括面板生命周期管理和临时提示（Tip）。UI 基于 Canvas + UGUI 渲染，通过 `UIRootNode` 挂载在场景中作为 UI 根节点。

### 初始化

```csharp
// 在场景中挂载 UIRootNode，然后初始化
var uiRoot = FindAnyObjectByType<UIRootNode>();
if (uiRoot != null)
{
    UIManager.Initialize(uiRoot.transform);
}
```

### 面板管理

面板继承 `UIPanelBase`，通过 `UIManager` 静态方法管理生命周期：

```csharp
// 打开面板
var panel = await UIManager.OpenAsync<MainMenuPanel>("PF_MainMenu", layer: 100);

// 关闭面板
await UIManager.CloseAsync<MainMenuPanel>();

// 栈式导航
var settings = await UIManager.PushAsync<SettingsPanel>("PF_Settings", layer: 200);
await UIManager.PopAsync();  // 返回上一个面板

// 模态遮罩
UIManager.ShowMask(maskLayer: 500, alpha: 0.5f);
UIManager.HideMask();
```

### 临时提示（Tip / 扣血提示）

用于显示无需交互的浮动提示文字，如扣血数字、暴击提示、获得物品等。通过 `UIManager.ShowTipAsync()` 一行代码即可使用。

> 📖 详细文档请参阅 **[Runtime/UI/README.md - Tip 临时提示](Runtime/UI/README.md#tip-临时提示扣血提示--浮动文字)**，包含 `TipConfig` 参数说明、预制体要求和架构详解。

## 依赖

- Unity 6000.3 或更新版本

### 第三方依赖

XFramework 依赖以下第三方包。由于 Unity 包管理器的限制，这些依赖需要在**项目根目录的 `Packages/manifest.json`** 中声明，而非在 XFramework 的 `package.json` 中。

| 包名                                              | 版本/URL                                                                         | 说明         | 安装方式 |
| ------------------------------------------------- | -------------------------------------------------------------------------------- | ------------ | -------- |
| [UniTask](https://github.com/Cysharp/UniTask)     | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` | 异步操作库   | UPM      |
| [YooAsset](https://github.com/tuyoogame/YooAsset) | `https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset`                 | 资源管理系统 | UPM      |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | `com.unity.nuget.newtonsoft-json`（3.2.1）                           | JSON 序列化库 | UPM      |

### 安装依赖

> **重要：** 由于 Unity 包管理器的限制，UPM 包的 `package.json` 中 `dependencies` 字段只支持语义化版本号，不支持 Git URL。因此 XFramework 不在自身 `package.json` 中声明第三方依赖，而是需要您在**项目根目录的 `Packages/manifest.json`** 中手动添加。

在项目 `Packages/manifest.json` 的 `dependencies` 中添加以下三个包：

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset",
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

配置完成后，再通过 Git URL 或本地路径添加 XFramework。消息总线（XMessage）与响应式属性（XReactive）均为框架自研实现，无需额外依赖。

---

## 配置管理

`ConfigManager` 提供统一的配置加载与查询接口，内置 Json / CSV / ScriptableObject 三种格式，自定义格式通过实现 `IConfigLoader` 扩展，Luban 等外部反序列化结果经 `RegisterTable` / `RegisterGlobal` 注入，一行代码加载与查询。

> 📖 详细文档请参阅 **[Runtime/Config/README.md](Runtime/Config/README.md)**，包含格式对比、自定义 Loader 和 Luban 集成指南。

