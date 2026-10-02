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

挂 `GameLauncher`（场景组件）是零配置起步；要带配置就继承它并覆写 `ConfigureStages()`，替换/追加登记项。

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
// 场景里挂一个 GameLauncher 即可——它带一组 Inspector 配置字段（Asset 包名/模式、存档版本、
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

## 可配置项一览

**「零配置」指的是「不配置也能跑」，不是「没有可配置项」**——每个模块都有可用的默认值或无参路径，需要时才覆盖。下表是**全部配置面**的索引（每模块一行，细节在各自的 README）。

| 模块 | 你能配置什么（入口） | 何时生效 |
|---|---|---|
| **Log** | `LogOptions` 11 字段（`LogManager.Configure`）；`ILogSink` 追加输出端 / `ILogManager` 整体替换；`MinimumLevel` / `SetCategoryLevel`；自定义分类 `LogCategory.Get` | options 在 `Configure` 时快照；档位与输出端实时 |
| **Asset** | `AssetInitOptions` 5 字段（主包名 / PlayMode / 远端服务 / 解密服务 / 低内存回收；前三项可在 `GameLauncher` 字段面填，服务实例走覆写）；`IAssetManager` 整体替换；运行时 `SetPoolMaxSize` / `CreateDownloader` 参数 | 初始化时；运行时项实时 |
| **Audio** | `AudioInitOptions` 4 字段；`IAudioManager` 整体替换；运行时 `MasterVolume` / `MasterMuted` / `SetChannelVolume` / `RegisterChannel`；`AudioChannels` 推荐通道名 | 初始化时快照；运行时项立即扫活跃播放源 |
| **Settings** | `SettingsOptions` 5 字段；四个注入点 `ISettingsStore` / `IAsyncSettingsStore` / `ISettingsMigrator<T>` / `ISettingsValidator<T>`（前两者按类型注入，Store / Migrator / Validator 均可运行时替换） | options 初始化时快照；注入点实时（换 Store 后需自行 `Load`） |
| **UI** | `Initialize(Transform uiRoot, IUIController controller = null)`（或登记 `UIBootstrapStage` / 在 `GameLauncher` 字段面填 UI 根）；`IUIController` / `IUITipProvider` / `IUiHudProvider` 三个可注入 provider；`UIManager.TipAssetPath`；`UILayers` / `UISorting` 常量约定；面板 / HUD 实例级参数（`UpdateTier` / `FollowTarget` / `ScreenOffset`） | provider 换后即时；`TipAssetPath` 下次显示生效；实例参数每帧读 |
| **Input** | 自己加载 actions 资产后 `Initialize(new InputSystemOptions { Asset = …, InitialActionMap = … })`（或登记 `InputBootstrapStage` / 在 `GameLauncher` 字段面指向一份 `InputSystemOptionsAsset`）；或整体替换 `IInputProvider`（28 成员）——无参 `Initialize()` 走 `Resources` 默认 + `"Player"` map | 初始化时 |
| **Save** | `SaveOptions` 2 字段（版本号 / 加密 Provider，后者接线到 `FileDomain.SaveData`）；`SaveManagerFactory` 整体替换；运行时 `SetCurrentVersion` / `SetCurrentPlayer` | 初始化时；运行时项实时（写操作进行中会拒绝） |
| **File** | `IFileProvider` 整体替换；`ICryptoProvider` 按域接线（`SetCryptoProvider`）；可选能力接口 `IAtomicFileProvider` / `IDirectoryProvider`（装饰器须一并实现） | 初始化或运行时接线 |
| **Pool** | `PoolConfig` 3 字段（`PoolManager.Configure<T>` 与四个集合池各自的 `Configure`） | **首次建池时**一次性消费；池已存在则忽略（需先 `RemovePool<T>`） |
| **Config** | `IConfigManager` 整体替换；`IConfigLoader` 自定义格式（逐次传入）；`RegisterTable` / `RegisterGlobal` 注入已反序列化数据；`ConfigManifest` 声明式批量清单 | 注册即生效；Loader 随调用 |
| **Message** | 全局过滤器 `AddFilter<T>` / `RemoveFilter`；请求-响应 `Register<TReq,TRes>`；缓冲淘汰 `EvictBufferedChannel(s)`——**统计没有开关**（常开） | 实时 |
| **Data** | `IDataManager` 整体替换（`DataManager.Initialize(impl)`）——**无 options** | 初始化时 |
| **Localization** | `ILocalizationManager` 整体替换；运行时 `LanguageAssetPath`（语言表地址模板）/ `FallbackLanguage` | 运行时实时（下次切换 / 下次回退读取） |
| **Serialize** | `ISerializer` 按格式名注册（`Register` / `Unregister`，同名覆盖） | 实时 |
| **Bootstrap** | 登记表：`Register` / `Unregister` / `RegisterDefaults` / `Clear`（`Stages` 是实时只读视图）——**无 options**；相位号常量见 `BootstrapPhases`；`GameLauncher` 经覆写 `ConfigureStages()` 定制 | 实时（须在 `RunAsync` 之前） |
| **Update** | 注册参数（`order` / `initialTier` / `timeMode`）与运行时 `Pause` / `Resume` / `Clear`——**无 options**；`timeMode` 注册时读一次 | 注册时 / 实时 |
| **Lock** | `AutoReleaseOnDestroy`（运行时可变，只影响此后新绑定）；`LockType` 是**使用方自建**的常量类——框架不预设锁类型 | 实时 |
| **Event** | **无配置点**（订阅期钩子 `EventStream.Create(onEmpty)` 除外） | — |
| **Reactive** | **无配置点**（仅构造初值） | — |
| **Timer** | **无配置点**（每次调用的 `UpdateTimeMode` 与取消令牌） | — |
| **Pipeline** | 装配期配置：`AddStage(stage, timeoutSeconds)`、`IPhaseStage.Phase`、`ParallelStage` / `SequenceStage`、`OnProgressUpdate`——**无 options** | 装配时 |

## 框架约定的名字

框架刻意**不提供统一的配置文件**：参数由各模块自己的 `Initialize(options)` 接收——**参数即契约**，读签名就知道这个模块依赖什么；一个全局配置文件会把依赖变成隐式的，也会让「模块可单独取用」失效（同「服务不依赖统一入口」那条设计决策）。但框架内部确实定了一些名字——下表把它们集中登记，省得逐个模块翻。**权威定义在对应模块的 README**，本表只是入口。

| 名字 / 值 | 性质与能否更改 | 详情 |
|---|---|---|
| `InputSystem_Actions`（默认从 `Assets/Resources/` 加载） | **可改**：自己加载资产（YooAsset / Addressables / 任意方式）后走 `InputManager.Initialize(new InputSystemOptions { Asset = ... })`——`Resources` 只是零配置默认；换输入插件则实现 `IInputProvider` | [Input](Runtime/Input/README.md) |
| `Player`（初始 ActionMap 名） | **可改**：`InputSystemOptions.InitialActionMap`（`GameLauncher` 字段面填 `InputSystemOptionsAsset` 时是它的同名项）；传 `null` / 空串则不自动切换（保持全部 map 常开） | [Input](Runtime/Input/README.md) |
| `PF_UITipText`（Tip 预制体的 YooAsset 地址） | **可改**：`UIManager.TipAssetPath`（内置实现的实例属性，改后下次显示生效；注入自定义 `IUITipProvider` 时由它自己决定） | [UI](Runtime/UI/README.md) |
| `DefaultPackage`（默认主包名） | **可改**：`InitializeAsync` 的 `AssetInitOptions.PackageName`（须与 YooAsset 构建侧的包名一致）；额外包走 `InitializePackageAsync`，**它不改主包** | [Asset](Runtime/Asset/README.md) |
| 业务资源地址（面板 / HUD / 音频 / 配置表 / 语言表） | **由你决定**：一律以 YooAsset `location` 字符串传入，框架不发明路径约定 | [Asset](Runtime/Asset/README.md) · [Audio](Runtime/Audio/README.md) · [UI](Runtime/UI/README.md) |
| `localization/lang_{0}`（语言表地址模板） | **可改**：`LocalizationManager.LanguageAssetPath`（公开属性，初始化后仍可改） | [Localization](Runtime/Localization/README.md) |
| `{persistentDataPath}/XLog/xlog-*.jsonl`（日志落点与命名） | **可改**：`LogOptions.FileDirectory` 等；Release 默认不写文件 | [Log](Runtime/Log/README.md) |
| `{persistentDataPath}/{类型短名}.json`（Settings 零配置落点） | **可改**：改用显式路径重载 | [Settings](Runtime/Settings/README.md) |
| `{playerId}/slot_{N}.save`（存档布局）+ `.meta` 侧车 | **可改，但代价高**：域根由 `FileDomain` 决定；槽位布局（`slot_` / `.save` / `.meta`）是默认实现写死的——要改只能整体替换 `ISaveManager`（`SaveManagerFactory`） | [Save](Runtime/Save/README.md) · [File](Runtime/File/README.md) |
| `slot_` / `.save` / `.meta`、`xlog-` / `.jsonl`、`Layer_Tip` / `Layer_HUD`、PlayerLoop 三个系统名、`AudioChannels` 推荐通道名、File 的 `.tmp` / `.bak` | **内部实现细节，不是契约**——排查日志与磁盘现场时用得到，别写进你的代码 | 各模块 README 的「已知限制」 |

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

