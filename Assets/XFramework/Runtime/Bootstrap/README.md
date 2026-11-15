# Bootstrap —— 显式登记的启动引导：按相位装配启动管线，退出时逆序清理

## 概述

命名空间 `XFramework.XBootstrap`。框架启动引导的**登记表与运行入口**。让需要异步初始化的服务把自己的初始化与反向清理交给一个统一的、有序的启动流程。

框架自己**不预设该初始化什么**。它提供的只有两样东西：

1. 一个**显式登记表**（`Bootstrap.Register`）——零反射，顺序可控
2. 一个**按相位装配并运行**的入口（`Bootstrap.RunAsync`）

至于登记哪些模块、按什么顺序，由使用方决定——内置阶段直接 `new` 各模块自己的阶段类，自定义阶段同样直接 `new`。框架不提供预设组合，也不提供中央构造目录。

## 快速使用

```csharp
// 登记什么由你定：内置阶段用目录构造，自己的阶段直接 new
Bootstrap.Register(new AssetBootstrapStage());
Bootstrap.Register(new DataBootstrapStage());
Bootstrap.Register(new SaveBootstrapStage());
await Bootstrap.RunAsync(progress: myProgress);

// 需要自己掌控的（第二个参数是子阶段超时；0/负值/NaN = 不启用）
Bootstrap.Register(new MyServiceBootstrapStage(), 10f);
Bootstrap.Register(new LocalizationBootstrapStage("en", myLanguageTable));
await Bootstrap.RunAsync();

// 业务侧等「启动就绪」：可取消；失败/取消时重抛本次运行的异常
await Bootstrap.WaitUntilReadyAsync(destroyCancellationToken);

// 启动画面也可以直接轮询状态与最近一帧进度
if (Bootstrap.State == BootstrapState.Completed) { /* 收起遮罩 */ }
float progress = Bootstrap.LastProgress?.OverallProgress ?? 0f;
```

退出时反向清理：

```csharp
Bootstrap.Shutdown();   // 先中止在途 RunAsync，再按执行序的逆序（相位降序）逐个调 stage.Shutdown()
```

登记表本身可以随时查看与清空（`Stages` **是实时视图而非快照**，后续登记会反映出来——只读用途，勿缓存后假定其不变）：

```csharp
foreach (var stage in Bootstrap.Stages)      // 按登记顺序
    Debug.Log($"{stage.Name} (Phase {stage.Phase})");

Bootstrap.Clear();                            // 清空登记表（不影响已运行的管线）
```

## 模块结构

```text
Runtime/Bootstrap/
├── Bootstrap.cs            # 静态门面：登记表 + RunAsync / Shutdown / State / WaitUntilReadyAsync / LastProgress
├── BootstrapState.cs       # 启动状态枚举（Idle / Running / Completed / Failed / Cancelled）
├── IBootstrapStage.cs      # 阶段契约：IPhaseStage + 一个同步 Shutdown
├── BootstrapStage.cs       # 委托阶段工厂：BootstrapStage.Create
├── BootstrapPhases.cs      # 框架内置阶段的相位号常量（0–89 由框架保留）
└── GameLauncher.cs         # 启动器底座（MonoBehaviour）：不含序列化字段，ConfigureStages 为抽象，暴露 StartupTask
```

这些文件都是公开面，本模块没有 `internal` 类型。

## 行为契约

### 阶段义务

- **`Shutdown` 幂等**：同一次会话里它可能被调用多次（多个销毁路径），重复调用应为空操作。
- **`Shutdown` 可能在 `ExecuteAsync` 从未执行过时被调用**：启动在它之前失败/取消、它在运行中才登记、
  或启动被 `Shutdown` 中止，都会让它被扫到。实现要能在「本阶段什么都没做」时安全空转。
- **只销毁自己初始化的那份**：执行时发现服务已初始化（使用方手动 `Initialize` / `SetInstance`，
  或上一次运行留下的实例）就早退，并在 `Shutdown` 里跳过销毁——否则会把使用方的管理器一起拆掉。
  内置的 `LocalizationBootstrapStage` / `InputBootstrapStage` / `UIBootstrapStage` 是范例；
  Asset / Data / Save 三个阶段同样按此口径（`_initializedByThisStage`）。
- **`ExecuteAsync` 可能被重复执行**：失败后重试、或 `Shutdown` 后再次 `RunAsync`，都会重跑同一批阶段
  实例。要么让 `ExecuteAsync` 可重入（已初始化即早退，像内置阶段那样），要么在重建前 `Clear()` 并
  重新构造阶段。
- **相位 / 名称 / 权重**：`Phase` 在登记后应保持稳定（`Shutdown` 在入口**快照一次**并按该值计算清理顺序）；
  getter 抛异常会记一条 `[Bootstrap]` Error，该阶段按 `int.MinValue` 兜底最后清理，其余阶段照常清理。
  `Name` 会进入日志与失败原因，给它一个可辨认的名字（通常 `GetType().Name`）；`Weight` 须为有限非负数，
  `0` 表示该阶段不占进度。
- **执行契约**（来自 Pipeline）：
  - 不要吞 `OperationCanceledException`——让它冒泡走取消终局；
  - 失败直接抛——`StageExecution` 会代你把描述写成异常消息并置 `Failed`；
  - 从后台线程回来必须先 `await UniTask.SwitchToMainThread(cancellationToken)` 再写 context；
  - 收尾写 `SetProgress(1f)` + `SetState(Completed)`，或干脆不写（管线契约会兜底补完成）。

### 会话与重跑

**会话开始时登记表会被清空。** 关闭域重载时，`ResetForNewSession` 在 `SubsystemRegistration` 档清空
登记表；登记必须发生在它之后。推荐在场景 `Awake`（`GameLauncher.ConfigureStages` 即如此）或
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 里登记；不要用同一个 `SubsystemRegistration` 档
自行登记——同档回调之间的执行顺序 Unity 不保证。

**`Shutdown` 不修改登记表。** 它只调各阶段的 `Shutdown`；阶段实例仍留在表里。同一会话内再 `RunAsync`
会重跑同一批实例；启动失败后重试同样从头重跑。需要「重建」时先 `Bootstrap.Clear()` 再重新构造、登记。

### 就绪信号与重入

`Bootstrap` 暴露三个拉取/等待面（**都在主线程使用**）：

| 成员 | 语义 |
| ---- | ---- |
| `State` | 最近一次运行的终局：`Idle` / `Running` / `Completed` / `Failed` / `Cancelled`。会话复位回 `Idle`。 |
| `WaitUntilReadyAsync(ct)` | `Completed` 立即完成；`Failed` / `Cancelled` **重抛本次运行的异常**；`Running` 等本次运行；`Idle` 等下一次运行开始并结束（可取消，会话复位也会取消）。 |
| `LastProgress` | 最近一帧 `PipelineProgress` 快照；运行开始时新建、每次进度广播全量覆盖；会话复位清空。启动画面轮询它即可。 |

**重入是 join，不是忽略。** `RunAsync` 在途时，第二次调用**返回第一次运行的任务**（同一个完成源，
可多路 await）；第二次调用的令牌**不参与本次运行**。旧行为「警告后立即返回」已删除——那会让第二个
调用方误以为启动已完成。

**`GameLauncher.StartupTask`** 是这个启动器实例的启动任务：成功完成，失败时与 `RunAsync` 同样抛出，
组件在启动途中销毁时以 `OperationCanceledException` 收场。在 `Start` 之前访问也有效。

**同一会话只应有一个 `GameLauncher`。** `GameLauncher` 会 `DontDestroyOnLoad`——**只对根 GameObject 生效**，
请把启动器挂在根对象上；挂在子对象上时切场景会随父对象销毁并触发 `Shutdown`，拆掉全部全局服务。每个场景各挂
一个会叠加登记表，任一实例销毁时的 `Shutdown` 会拆掉全局服务。

### 替换内置阶段

**唯一与 `Awake` 顺序无关的路径是启动器的 `ConfigureStages` 覆写。** 框架不提供默认启动器，
登记代码写在你的 `GameLauncher` 子类里；替换也在那里做：先 `Unregister<T>()` 摘掉，再
`Register(...)` 自己的实例。另一个组件在自己 `Awake` 里的登记与启动器的先后由 Unity 决定——
分开写会形成同相位双份。把登记集中到 `ConfigureStages`，才是与顺序无关的写法。

## 典型场景

### 用启动器启动时怎么带配置

框架只提供抽象底座 `GameLauncher`——**字段面与装配代码都由你写**。最小形状是两个字段（一个配置资产 + 场景 UI 根）加一段 `ConfigureStages`：

```csharp
public sealed class MyLauncher : GameLauncher
{
    [SerializeField] private MyBootstrapProfile _profile;   // 你自己的 ScriptableObject
    [SerializeField] private Transform _uiRoot;

    protected override void ConfigureStages()
    {
        if (_profile == null)
            return;

        Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions
        {
            PackageName = string.IsNullOrEmpty(_profile.AssetPackageName) ? null : _profile.AssetPackageName,
            PlayMode = _profile.PlayMode,
            AutoReclaimOnLowMemory = _profile.AutoReclaimOnLowMemory,
        }));
        Bootstrap.Register(new DataBootstrapStage());
        Bootstrap.Register(new SaveBootstrapStage(new SaveOptions { CurrentVersion = _profile.SaveVersion }));

        if (_uiRoot != null)
            Bootstrap.Register(new UIBootstrapStage(_uiRoot));
        if (_profile.InputOptions != null)
            Bootstrap.Register(new InputBootstrapStage(_profile.InputOptions.ToOptions()));
        if (!string.IsNullOrEmpty(_profile.DefaultLanguage))
            Bootstrap.Register(new LocalizationBootstrapStage(_profile.DefaultLanguage, _profile.LanguageTable));
    }
}
```

完整可运行版本见 Integration 示例的 `IntegrationLauncher` / `IntegrationBootstrapProfile`。

**Profile 只是示例的一种形状**（下表是示例字段到构造的映射）。字段怎么取舍由你定：只用 Asset / Data / Save 的项目可以删掉其余字段；换输入后端（Rewired 等）把 `_inputOptions` 换成自己的 provider 资产，或删掉该字段、改在 `ConfigureStages` 里 `Register(new InputBootstrapStage(myProvider))`。

| 示例字段（`IntegrationBootstrapProfile`） | 落到哪 |
| ---- | ------ |
| Asset 包名 / 运行模式 / 低内存自动回收 | `AssetInitOptions` → `new AssetBootstrapStage(...)` |
| Save 存档格式版本上限 | `SaveOptions` → `new SaveBootstrapStage(...)` |
| Input 配置资产（`InputSystemOptionsAsset`：输入资产 + 初始 ActionMap 名） | `new InputBootstrapStage(asset.ToOptions())`（**资产留空则不登记**）。字段类型是框架类型而非 `InputActionAsset`——Unity 输入类型按模块边界只允许住在 `XInput.Default` 内 |
| 默认语言 / 语言表（扁平 JSON 的 `TextAsset`） | `new LocalizationBootstrapStage(...)`（**语言留空则不登记**；表可空——此时该阶段打一条警告后跳过） |
| （不在 Profile 里——`Transform` 是场景引用） | `_uiRoot` 非空时 `new UIBootstrapStage(...)`（留空则不登记——用 `UIRootNode` 自动初始化或自行 `Initialize`） |

各模块 options 的权威定义仍在各模块 README（`已知限制` / options 的 XML）。
**服务实例装不进资产**：`RemoteServices` / `CryptoProvider` / 自定义 provider 由你直接在 `ConfigureStages` 里构造并传给对应阶段。

**追加与替换** —— 服务实例（`IAssetRemoteServices` / `ICryptoProvider` / `IAssetDecryptionServices`）与自定义阶段都写在同一个 `ConfigureStages` 里，顺序确定：

```csharp
protected override void ConfigureStages()
{
    // 直接登记自己的 Asset 阶段（不必先登记内置那份）
    Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions
    {
        PlayMode = AssetPlayMode.Host,
        RemoteServices = _remoteServices,
    }));
    Bootstrap.Register(new DataBootstrapStage());
    Bootstrap.Register(new SaveBootstrapStage());

    Bootstrap.Register(new MyServiceBootstrapStage());
}
```

已经在别处登记过内置阶段时，用 `Bootstrap.Unregister<AssetBootstrapStage>()` 摘掉再登记自己的。

**干脆不用它** —— 在自己的启动流程里调 `Bootstrap.Register` / `RunAsync`（见「快速使用」）。

> ⚠ **同一次会话只走一条登记路径。** 登记表是全局静态的：启动器的 `ConfigureStages` 与另一个
> `Awake` 里各登记一遍同类型阶段，会形成同相位双份（第二个的 options 被幂等守卫挡下），而两者的
> 先后由 Unity 决定。把登记集中到 `ConfigureStages`；外部组件只读不登记。

### 编辑器校验

菜单 **`Tools/XFramework/Bootstrap/Validate Bootstrap`** 做一次只读体检：Build Settings 的启动场景索引 0、
当前打开场景里的启动器数量、登记表里重复登记的内置阶段、Asset 阶段配 Host 时是否缺 `RemoteServices`。
错误 / 警告 / 提示输出到 Console，可点击定位对象。

**边界**：启动器字段与 Profile 归使用方——校验器不读它们，只读框架拥有的面。Build Settings 的场景只检查
索引与启用状态（不打开场景扫描）；登记表检查只在 PlayMode（或使用方手动登记后）有效——编辑模式下
`Awake` 不会跑。校验器不修改场景、资产或登记表。

⚠ **启动器覆盖不到的两样**：① **`LogOptions`**——日志配置要在**任何场景加载之前**生效（`SubsystemRegistration` 就立默认实现，正是为了不丢启动期日志），场景组件结构性地晚一步，需要时用 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 自己调 `LogManager.Configure`；② **服务实例**（同上，它们本就是代码对象而非数据）。

## 自定义实现

本模块的实现方入口：写一个启动阶段并登记。

- **实现哪个契约**：`IBootstrapStage`（= `IPhaseStage` + 同步 `Shutdown`）；Phase / Name / Weight 的约定见「内置相位约定」与「行为契约」。
- **怎么接线**：`Bootstrap.Register(stage)`；内置阶段直接 `new` 各模块的阶段类；替换已登记的内置阶段用 `Unregister<T>()` 再 `Register`（按精确类型匹配，见「设计取舍」）。
- **要守什么**：阶段义务（幂等清理、所有权、可重入、执行契约）见「行为契约」；线程要求见「线程契约」；超时 / 从未执行 / 协作式取消等边界见「已知限制」。

### 定义一个阶段

实现 `IBootstrapStage`——它就是 Pipeline 的 `IPhaseStage` 加一个 `Shutdown`：

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

public sealed class MyServiceBootstrapStage : IBootstrapStage
{
    public int Phase => BootstrapPhases.UserStart;   // 0–89 由框架保留；同相位并行，相位升序串行
    public string Name => GetType().Name;
    public float Weight => 1f;               // 0 = 不占进度

    public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
    {
        context.SetDescription("Initializing MyService...");
        await MyService.InitializeAsync(cancellationToken);
        context.SetProgress(1f);
        context.SetState(PipelineStageState.Completed);
    }

    public void Shutdown() => MyService.Destroy();
}
```

写 `ExecuteAsync` 时遵守的四条 Pipeline 契约见「行为契约 → 阶段义务」。

### 用工厂少写样板

**只调一次初始化**的阶段不必单独建类：

```csharp
Bootstrap.Register(BootstrapStage.Create(
    BootstrapPhases.UserStart,
    "MyService",
    ct => MyService.InitializeAsync(ct),
    shutdown: MyService.Destroy));          // 所有权归你：工厂只负责调用
```

**内置阶段**直接 `new` 各模块的阶段类（构造入口随阶段走，没有中央目录）：

```csharp
Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions { PlayMode = AssetPlayMode.Host }));
Bootstrap.Register(new DataBootstrapStage());
Bootstrap.Register(new SaveBootstrapStage(new SaveOptions { CurrentVersion = 2 }));
Bootstrap.Register(new UIBootstrapStage(uiRoot, controller));
Bootstrap.Register(new InputBootstrapStage(inputOptionsAsset.ToOptions()));
Bootstrap.Register(new LocalizationBootstrapStage("en", languageTable));
```

示例启动器的 `ConfigureStages` 就是直接这样装配。
委托阶段不提供 context 写入接口——需要自己报告描述/进度的阶段，仍应实现 `IBootstrapStage`。

## 内置相位约定

相位号已公开为 `BootstrapPhases` 常量，IntelliSense 可见，不必照抄字面量：

| 常量 | Phase | 模块 | 说明 |
| ---- | ----- | ---- | ---- |
| `BootstrapPhases.Asset` | 0 | Asset | 资源管理器初始化（最早——本地化等模块的数据要经 YooAsset 地址加载） |
| `BootstrapPhases.Input` | 1 | Input | 输入模块初始化（需要资产，不属于任何预设） |
| `BootstrapPhases.UI` | 2 | UI | UI 模块初始化（需要场景里的 Canvas 根，不属于任何预设） |
| `BootstrapPhases.Data` | 3 | Data | 数据管理器初始化 |
| `BootstrapPhases.Save` | 4 | Save | 存档管理器初始化。**硬性晚于 Data**：本模块运行期经 `DataManager` 收集/应用数据块快照（阶段自身只用 `FileManager` 跑恢复扫描，不碰 Data） |
| `BootstrapPhases.Localization` | 90 | Localization | 本地化数据加载（需要语言数据，不属于任何预设） |
| `BootstrapPhases.UserStart` | 90 | 用户自定义 | 业务阶段的建议起点（与 Localization 同相位并行——依赖本地化的阶段请取更大的值） |

**0–89 由框架保留**：框架新增模块可能占用其中任意值（5–89 目前空闲，但只是现状、不是保留承诺）。需要精确插进框架阶段之间（例如「Asset 之后、Data 之前」）时，别赌空闲值——用 `Unregister` 换掉内置阶段，那一段顺序自己编排。

**同相位 = 并行**，彼此不可有依赖；有依赖就必须分属不同相位。

## 线程契约

**登记表没有任何同步原语**（模块内既无锁、也无主线程断言）：`Register` / `Unregister` / `Clear` / `Stages` 直接读写同一个静态列表，**登记与查询都应在主线程发生**——从别的线程并发访问即数据竞争，且不会有任何提示。

**`RunAsync` 与 `Shutdown` 也应在主线程调用**：Pipeline 在调度线程上写阶段上下文并同步聚合（见 Pipeline 的线程契约），`GameLauncher` 的 `Start` / `OnDestroy` 天然满足。阶段在哪个线程执行由阶段自己与它的 await 决定；从后台线程回来必须先 `await UniTask.SwitchToMainThread(cancellationToken)` 再写 `PipelineStageContext`（见「行为契约 → 阶段义务」）。

## 已知限制

**框架不提供开箱启动器、Profile、默认组合或中央构造目录。** `GameLauncher` 只有生命周期接线与 `StartupTask`；字段面（配置资产、UI 根）与登记代码都由使用方写；内置阶段由各模块自己的公开构造函数构造。`Samples~/Integration` 是可复制的模板，不是框架契约——字段形状可以按项目需要增删。

**超时是「报告失败」，不是「真正中止」。** `Bootstrap.Register(stage, timeoutSeconds)` 把超时下沉到该引导阶段（0 / 负值 / NaN / ±∞ = 不启用）：触发时置该子阶段 `Failed` 并取消其令牌，整轮启动以失败收场。但**不响应取消的阶段仍会跑完**——它在自己的沉降点收场，启动不再等它（与 `IPipeline.AddStage(stage, timeoutSeconds)` 同一条乐观超时边界，见 Pipeline README）。组失败后管线照常失败并停止后续相位。

**`Shutdown` 可能在阶段从未执行过时被调用。** 启动在它之前失败/取消、它是在 `RunAsync` 运行中才登记的，或启动在途被 `Shutdown` 中止——`Bootstrap.Shutdown` 都会扫到它。清理实现要能在「本阶段什么都没做」时安全空转——六个内置阶段都按此口径（例如 `LocalizationBootstrapStage` 只在确实由自己完成初始化时才销毁门面）。

**运行中修改登记表只告警、不阻止**（`Register` / `Unregister` / `Clear`），后果与理由见上面「设计取舍」的「登记表请在 `RunAsync` 之前定稿」段：运行中新增的阶段本轮不执行（但会被 `Shutdown` 扫到）；运行中注销 / 清掉的阶段本轮仍执行但拿不到 `Shutdown`。

**中止在途启动是协作式的，有两条覆盖边界（2026-10-13 实测并修复后如实记录）。** `Shutdown` 会先中止在途运行、`GameLauncher.Start` 也把 `destroyCancellationToken` 传给了 `RunAsync`——「启动途中销毁 `GameLauncher`」这条默认入口路径已关闭（原缺陷实测会留下三条 Error，`GameLauncherFrameTests` 用例 3 留作端到端证据）。但：① **不响应取消的阶段仍会跑完**——它不观察令牌，谁也停不下它，整轮在它的沉降点才收场；期间真实模块若撞上「已被销毁」（如 `AssetManager.Destroy` 作废初始化代际），照旧留下失败链（用例 3 的三条 Error 原样保留，正是这条边界的证据）；② `Shutdown` 返回时在途运行只是「已收到取消信号」，**同帧再调 `RunAsync` 会 join 那次在途运行**（而不是开新的一轮）——要等它收敛，持有第一次调用的任务或 `WaitUntilReadyAsync`。

**`RunAsync` 的 `OperationCanceledException` 携带本次运行的链接令牌。** `Shutdown` 触发的中止里调用方令牌并未取消，携带它会让 `catch when (ex.CancellationToken == myToken)` 这类过滤与语义都对不上。判据用 `OperationCanceledException.CancellationToken.IsCancellationRequested`，**不要比对令牌身份**。另注意一个既有的边界形态：阶段自抛 `OperationCanceledException` 时，链接令牌本身可能尚未取消（`CanBeCanceled == true` 而 `IsCancellationRequested == false`）——异常的类型才是终局判据。

**模块有四处静态态，写测试时要复位。** 登记表、在途运行的取消源、就绪状态（`State` / `LastProgress` / 等待者）都跨用例存活：`Clear()` 清登记表；会话复位（`SubsystemRegistration` 自动触发，测试可手动调）清就绪面并取消 Idle 等待者；用例中途 `Shutdown` 过在途启动的，请等那一次运行收敛（持有它的任务）再开始下一个用例——在途期间再调 `RunAsync` 会 **join 第一次运行**（不再是静默吞掉），若第一个用例的闸门没放行，第二个用例会跟着挂住。

**没有「早于 Asset」的保留相位。** `BootstrapPhases` 从 0（Asset）开始；自定义阶段从 90 起。需要在 Asset 之前完成的初始化，请在调用 `RunAsync` 之前自行完成（例如一个独立的 `Pipeline` 或直接 `await`），不要依赖负相位号——`Pipeline` 只按数值升序排序，负值当前会最先执行，但这不是保留承诺。

## 设计取舍

**为什么是显式登记而不是反射发现？** 零反射、顺序可控、可测试，且使用方一眼能看出到底有哪些东西会在这个启动流程里跑。

**为什么没有中央构造目录？** 每个内置阶段都有公开构造函数，`new AssetBootstrapStage(options)` 已经是它自己模块里的构造入口；再加一层 `BootstrapStages.Asset(options)` 只是转发，却会让 Bootstrap 模块引用全部六个模块。构造随阶段走，模块边界保持单向（阶段依赖本模块的 `IBootstrapStage`，本模块不依赖任何具体模块）。

**登记表按实例去重，不按类型。** 同一个实例重复登记会被忽略，但**同类型的多个实例可以共存**——登记表是「初始化步骤列表」，不是「每类型一个的容器」，参数化的阶段用同一类型登记多次是合法的。没有按类型跳过的预设：重复登记同类型阶段由校验器（BST-VAL-004）在编辑器里报出。

**替换内置阶段用 `Unregister`：它消掉「同类型阶段是另一个步骤还是替换品」的歧义。**

> ⚠ **「顺序无关」只在同一段登记代码内成立。**
>
> ```csharp
> Bootstrap.Register(new AssetBootstrapStage());          // 内置那份
> Bootstrap.Unregister<AssetBootstrapStage>();            // 摘掉内置的那个
> Bootstrap.Register(new AssetBootstrapStage(myOptions)); // 换成自己的
> ```
>
> 这三步之间插入别的登记不会改变结果。但**不要把它和另一个组件的 `Awake` 混在一起**：
> Unity 不保证两个组件 `Awake` 的先后。对方在你之后登记时两边各留一份；在你之前时，你 `Unregister`
> 摘掉的是对方的实例。**唯一与 `Awake` 顺序无关的替换路径是启动器的 `ConfigureStages()`**——
> 那里是确定调用栈，替换就写在那。
>
> 反过来写（先 `Register(new AssetBootstrapStage())` 再 `Register(new AssetBootstrapStage(custom))`）
> **两者都会被登记**，同一个门面被初始化两次，第二次会被门面自身的幂等守卫挡下并打警告，而
> **你的 options 被静默忽略**。框架无从区分「另一个同类型阶段」与「同一个阶段的替换品」——
> `Unregister` 就是为消掉这个歧义而存在的。
>
> 另注意 `Unregister<T>()` 按**精确类型**匹配。六个内置阶段都是 `sealed`，不能派生；精确类型语义
> 对**你自己的**阶段基类 / 派生类仍然成立（`GetType() == typeof(T)`，传接口类型不会命中任何阶段）。

**为什么 `RunAsync` 在失败/取消时抛异常？** `PipelineImpl.RunAsync` 在这两种情况下都「正常返回」，单看返回值分不出成功与失败。启动失败是致命的，静默吞掉会让故障表现成「服务莫名其妙没就绪」。所以本模块在 `RunAsync` 返回后读管线的终局拉取面（`IPipeline.Status` / `FailureReason`）并据此抛出——早期版本是订阅 `OnFailed`/`OnCancelled` 再用两个局部变量记账，管线补上拉取面后那套记账已删除。

**为什么 `Shutdown` 是同步的？** 框架内置六个阶段的清理入口都是同步 `void`，且调用点通常是 `OnDestroy`（没有 await 机会）。将来若某个模块确实需要异步清理，再为它单独扩展接口。

**为什么 `Shutdown` 要先中止在途的 `RunAsync`？** 被清理过的阶段（含本轮从未执行的）不该再被在途管线捡起来执行——否则真实模块会在「已被销毁」与「仍在初始化」之间相撞，落成一串错误日志（启动途中销毁 `GameLauncher` 曾实测复现三条 Error）。所以 `Shutdown` 第一步取消在途运行的**链接令牌**（`RunAsync` 创建它时把调用方令牌链接进来），管线在下一个阶段边界停止，在途运行以 `OperationCanceledException` 收场。两点如实记：① **中止是协作式的**——不观察令牌的阶段会先跑完自己的沉降点，`Shutdown` 返回时在途运行只是「已收到信号」；② 该异常携带的是本次运行的链接令牌，判据用 `IsCancellationRequested`，不要比对令牌身份（详见已知限制）。

**为什么未登记任何阶段不抛异常？** 零配置使用静态服务的项目本就不需要引导流程，此时 `RunAsync` 打一条警告后直接返回。这与门面模板里 `EnsureInitialized` 抛 `InvalidOperationException` 的取向不同，是有意为之。

**为什么 `RunAsync` 重入返回第一次运行的任务？** 每次调用都会装配一条**新**管线（管线自身的重入守卫是实例级的），并发两次会让同一批阶段跑两遍。共享第一次运行的任务既阻止了双跑，又让第二个调用方**真的等到了启动结束**；第二次调用的令牌不参与本次运行（只认第一次调用方的令牌），这是 join 语义里唯一需要文档写死的细节。完成源用 `UniTaskCompletionSource` 而不是把 `async` 方法的 `UniTask` 存起来——状态机 `UniTask` 只允许 await 一次，多路调用方会撞上「Token version is not matched」。

**登记表请在 `RunAsync` 之前定稿。** 运行中的修改（`Register` / `Unregister` / `Clear`）照常生效但都打警告，因为后果都反直觉：运行中新增的阶段**本轮不执行**（装配期已快照）但会被 `Shutdown` 扫到；运行中注销 / 清掉的阶段**本轮仍执行**但拿不到 `Shutdown`（资源泄漏的候选）。

**为什么引导流程不暂停 Update（也不提供「就绪」门控）？** 每帧派发由 Update 模块自注入 PlayerLoop 独立驱动，与引导流程无关——`RunAsync` 期间（Asset 初始化可能持续数秒）派发照常进行。这不构成缺口：依赖已初始化服务的回调，正确接法是 `await RunAsync()` 完成之后再注册，或直接写在自家引导阶段里。框架不自动暂停——那等于替使用方决定「加载期算不算可派发的时间」，而且失败/中断路径会让整个应用背上「忘了恢复即静默冻结」的风险。确需加载期安静时自行接管（只冻逻辑轴，`Unscaled` 节点照常）：

```csharp
UpdateManager.Pause();
try { await Bootstrap.RunAsync(); }
finally { UpdateManager.Resume(); }
```

## 与相邻模块的关系

### 与 Pipeline 的边界

本模块**不含任何编排逻辑**。相位分组、同相位并行、加权进度聚合、失败即停、取消传播全部由 Pipeline 模块提供，本模块只做两件事：

- 用 `Pipeline.BuildPhaseGroups` 把登记表装配成「每相位一个 `ParallelStage`」
- 补上 Pipeline 没有的那一半——**反向清理**

## 依赖

- **XFramework.XPipeline** —— 阶段契约与相位分组装配
- **UniTask** —— 异步
- **`GameLauncher`** 另需 XLog（启动失败日志）
- **模块核心不引用任何具体模块**：本模块只依赖 XPipeline + UniTask，`GameLauncher` 另需 XLog。内置阶段（`AssetBootstrapStage` / `DataBootstrapStage` / `SaveBootstrapStage` / `UIBootstrapStage` / `InputBootstrapStage` / `LocalizationBootstrapStage`）各自住在所属模块里，由使用方直接构造；Integration 示例的启动器用到上述全部六个。
- **反向的单向契约依赖**：各模块的引导阶段适配器（`*BootstrapStage.cs`）引用本模块的 `IBootstrapStage` / `BootstrapPhases`；**各模块的管理器不引用本模块**。方向是「模块适配器 → Bootstrap 契约」，不存在循环——`BootstrapPhases` 是引导顺序的单一代码真相，适配器只是声明自己在这张表里的位置。
