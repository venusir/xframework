# Bootstrap —— 显式登记的启动引导：按相位装配启动管线，退出时逆序清理

## 概述

命名空间 `XFramework.XBootstrap`。框架启动引导的**登记表与运行入口**。让需要异步初始化的服务把自己的初始化与反向清理交给一个统一的、有序的启动流程。

框架自己**不预设该初始化什么**。它提供的只有两样东西：

1. 一个**显式登记表**（`Bootstrap.Register`）——零反射，顺序可控
2. 一个**按相位装配并运行**的入口（`Bootstrap.RunAsync`）

至于登记哪些模块、按什么顺序，由使用方决定。`Bootstrap.RegisterDefaults()` 只是把最常用的三件（Asset / Data / Save）打包好，用不用随你。

## 快速使用

```csharp
// 用框架内置的默认组合
Bootstrap.RegisterDefaults();
await Bootstrap.RunAsync(progress: myProgress);

// 或者自己挑
Bootstrap.Register(new MyServiceBootstrapStage());
Bootstrap.Register(new LocalizationBootstrapStage("en", myLanguageTable));
await Bootstrap.RunAsync();
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
├── Bootstrap.cs            # 静态门面：登记表（Register / Unregister / Clear / Stages）+ RunAsync / Shutdown
├── IBootstrapStage.cs      # 阶段契约：IPhaseStage + 一个同步 Shutdown
├── BootstrapPhases.cs      # 框架内置阶段的相位号常量（0–89 由框架保留）
├── GameLauncher.cs         # 启动器底座（MonoBehaviour）：不含任何配置字段，ConfigureStages 为抽象
└── DefaultGameLauncher.cs  # 开箱默认启动器：底座 + 八组 Inspector 声明式字段
```

五个文件都是公开面，本模块没有 `internal` 类型。

## 典型场景

### 用 DefaultGameLauncher 启动时怎么带配置

`DefaultGameLauncher` 在 Inspector 上提供一组**声明式配置字段**——挂上它、填字段，`Awake` 里自动组装成各模块的 options 注入，`Start` 里跑管线。**全为默认/留空时，登记表与行为与零配置完全一致。**

| 分组 | 字段 | 落到哪 |
| ---- | ---- | ------ |
| Asset | 主包名 / 运行模式 / 低内存自动回收 | 重建 `AssetBootstrapStage` 并把字段装进 `AssetInitOptions` |
| Save | 存档格式版本上限 | 重建 `SaveBootstrapStage` 并把字段装进 `SaveOptions` |
| UI | UI 根节点（场景里的 Canvas 根） | 登记 `UIBootstrapStage`（**留空则不登记**——用 `UIRootNode` 自动初始化或自行 `Initialize`） |
| Input | 输入配置资产（`InputSystemOptionsAsset`：输入资产 + 初始 ActionMap 名） | 登记 `InputBootstrapStage`（**资产留空则不登记**）。注①：字段类型是框架类型而非 `InputActionAsset`——Unity 输入类型按模块边界只允许住在 `XInput.Default` 内；注②：**换后端（Rewired 等）请保持留空**，改为覆写里 `Register(new InputBootstrapStage(myProvider))`（服务实例走代码注入） |
| Localization | 默认语言 / 语言表（扁平 JSON 的 `TextAsset`） | 登记 `LocalizationBootstrapStage`（**语言留空则不登记**；表可空——此时该阶段打一条警告后跳过） |

复制的字段与各模块 options 一一对应，权威定义仍在各模块 README（`已知限制` / options 的 XML）。

**进阶一：覆写 `ConfigureStages()`** —— 服务实例（`IAssetRemoteServices` / `ICryptoProvider` / `IAssetDecryptionServices`）与自定义阶段从这里进；调 `base` 之后追加或替换，不调 `base` 则完全自建：

```csharp
public sealed class MyLauncher : DefaultGameLauncher
{
    [SerializeField] private MyRemoteServicesAsset _remoteServices;   // ScriptableObject 实现服务接口

    protected override void ConfigureStages()
    {
        base.ConfigureStages();                       // 字段面照常生效
        Bootstrap.Unregister<AssetBootstrapStage>();  // 再换掉 Asset，把服务实例装进去
        Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions
        {
            PlayMode = AssetPlayMode.Host,
            RemoteServices = _remoteServices,
        }));
    }
}
```

**进阶二：完全自控（Inspector 里一个字段都不要）** —— 继承抽象底座 `GameLauncher` 并实现 `ConfigureStages`：
本组件被拆成两层正是为了这一档。Unity **无法隐藏继承来的序列化字段**，所以「Inspector 干净」只能靠类型层次保证——
底座 `GameLauncher` 不带字段（`ConfigureStages` 是抽象方法，编译器强制你实现），`DefaultGameLauncher` 才带那八个字段。

```csharp
public sealed class MyLauncher : GameLauncher   // 抽象底座：Inspector 里不会有任何配置字段
{
    protected override void ConfigureStages()
    {
        Bootstrap.Register(new MyAssetBootstrapStage(myOptions));   // 登记什么全由你定
        // 要默认组合就调 Bootstrap.RegisterDefaults()；换输入后端则 Register(new InputBootstrapStage(myProvider))
    }
}
```

**进阶三：干脆不用它** —— 在自己的启动流程里调 `Bootstrap.Register` / `RunAsync`。

⚠ **`DefaultGameLauncher`（以及它的抽象底座 `GameLauncher`）覆盖不到的两样**：① **`LogOptions`**——日志配置要在**任何场景加载之前**生效（`SubsystemRegistration` 就立默认实现，正是为了不丢启动期日志），场景组件结构性地晚一步，需要时用 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 自己调 `LogManager.Configure`；② **服务实例**（同上，它们本就是代码对象而非数据）。

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

写 `ExecuteAsync` 时遵守四条 Pipeline 既有契约：

- **不要吞 `OperationCanceledException`** —— 让它冒泡走取消终局
- **失败直接抛** —— `StageExecution` 会代你把描述写成异常消息并置 `Failed`
- **后台线程完成后必须 `await UniTask.SwitchToMainThread(cancellationToken)` 再写 context** —— `PipelineStageContext` 有编辑器越线程写入断言
- **收尾写 `SetProgress(1f)` + `SetState(Completed)`**，或干脆不写（管线契约会兜底补完成）

## 内置相位约定

相位号已公开为 `BootstrapPhases` 常量，IntelliSense 可见，不必照抄字面量：

| 常量 | Phase | 模块 | 说明 |
| ---- | ----- | ---- | ---- |
| `BootstrapPhases.Asset` | 0 | Asset | 资源管理器初始化（最早——本地化等模块的数据要经 YooAsset 地址加载） |
| `BootstrapPhases.Input` | 1 | Input | 输入模块初始化（不在默认组合内，需要资产） |
| `BootstrapPhases.UI` | 2 | UI | UI 模块初始化（不在默认组合内，需要场景里的 Canvas 根） |
| `BootstrapPhases.Data` | 3 | Data | 数据管理器初始化 |
| `BootstrapPhases.Save` | 4 | Save | 存档管理器初始化。**硬性晚于 Data**：本模块运行期经 `DataManager` 收集/应用数据块快照（阶段自身只用 `FileManager` 跑恢复扫描，不碰 Data） |
| `BootstrapPhases.Localization` | 90 | Localization | 本地化数据加载（不在默认登记组合内） |
| `BootstrapPhases.UserStart` | 90 | 用户自定义 | 业务阶段的建议起点（与 Localization 同相位并行——依赖本地化的阶段请取更大的值） |

**0–89 由框架保留**：框架新增模块可能占用其中任意值（5–89 目前空闲，但只是现状、不是保留承诺）。需要精确插进框架阶段之间（例如「Asset 之后、Data 之前」）时，别赌空闲值——用 `Unregister` 换掉内置阶段，那一段顺序自己编排。

**同相位 = 并行**，彼此不可有依赖；有依赖就必须分属不同相位。

## 线程契约

**登记表没有任何同步原语**（模块内既无锁、也无主线程断言）：`Register` / `Unregister` / `Clear` / `Stages` 直接读写同一个静态列表，**登记与查询都应在主线程发生**——从别的线程并发访问即数据竞争，且不会有任何提示。

`RunAsync` 不改变阶段的线程规则：阶段在哪个线程执行，由阶段自己与它的 await 决定（Pipeline 的既有契约：从后台线程回来必须先 `await UniTask.SwitchToMainThread(cancellationToken)` 再写 `PipelineStageContext`，见「典型场景」的四条）。`Shutdown` 是同步方法，`DefaultGameLauncher` 在 `OnDestroy` 里调它——都在主线程。

## 已知限制

**没有超时手段。** `IPipeline.AddStage(stage, timeoutSeconds)` 的超时只作用在**阶段**粒度，而本模块每相位装配为一个 `ParallelStage`——从 Bootstrap 侧无处传入超时。一个不响应取消、又永不返回的阶段会挂死整个启动流程（乐观超时只在直接使用 Pipeline 时可用，见 Pipeline README）。

**`Shutdown` 可能在阶段从未执行过时被调用。** 启动在它之前失败/取消、它是在 `RunAsync` 运行中才登记的，或启动在途被 `Shutdown` 中止——`Bootstrap.Shutdown` 都会扫到它。清理实现要能在「本阶段什么都没做」时安全空转——内置的 `LocalizationBootstrapStage` 就是范例：只在确实由自己完成初始化时才销毁门面。

**运行中修改登记表只告警、不阻止**，两条反直觉后果与理由见上面「设计取舍」的末两段：运行中登记的阶段本轮不执行、运行中注销的阶段拿不到 `Shutdown`。

**中止在途启动是协作式的，有两条覆盖边界（2026-10-13 实测并修复后如实记录）。** `Shutdown` 会先中止在途运行、`GameLauncher.Start` 也把 `destroyCancellationToken` 传给了 `RunAsync`——「启动途中销毁 `GameLauncher`」这条默认入口路径已关闭（原缺陷实测会留下三条 Error，`GameLauncherFrameTests` 用例 3 留作端到端证据）。但：① **不响应取消的阶段仍会跑完**——它不观察令牌，谁也停不下它，整轮在它的沉降点才收场；期间真实模块若撞上「已被销毁」（如 `AssetManager.Destroy` 作废初始化代际），照旧留下失败链（用例 3 的三条 Error 原样保留，正是这条边界的证据）；② `Shutdown` 返回时在途运行只是「已收到取消信号」，**同帧再调 `RunAsync` 仍会被重入守卫忽略**——要等它收敛，请持有第一次调用的任务。

**`RunAsync` 的 `OperationCanceledException` 携带本次运行的链接令牌。** `Shutdown` 触发的中止里调用方令牌并未取消，携带它会让 `catch when (ex.CancellationToken == myToken)` 这类过滤与语义都对不上。判据用 `OperationCanceledException.CancellationToken.IsCancellationRequested`，**不要比对令牌身份**。另注意一个既有的边界形态：阶段自抛 `OperationCanceledException` 时，链接令牌本身可能尚未取消（`CanBeCanceled == true` 而 `IsCancellationRequested == false`）——异常的类型才是终局判据。

**模块有两处静态态，写测试时要复位。** 登记表与在途运行的取消源都跨用例存活：`Clear()` 清登记表；用例中途 `Shutdown` 过在途启动的，请等那一次运行收敛（持有它的任务）再开始下一个用例——否则在途运行会经重入守卫**静默吞掉**后续的 `RunAsync`，表现是「第二个用例什么都没发生」。

## 设计取舍

**为什么是显式登记而不是反射发现？** 零反射、顺序可控、可测试，且使用方一眼能看出到底有哪些东西会在这个启动流程里跑。

**登记表按实例去重，不按类型。** 同一个实例重复登记会被忽略，但**同类型的多个实例可以共存**——登记表是「初始化步骤列表」，不是「每类型一个的容器」，参数化的阶段用同一类型登记多次是合法的。唯一例外是 `RegisterDefaults()`：它按类型跳过已存在的内置阶段，因此可重复调用而不叠加。

> ⚠ **要替换某个内置阶段，用 `Unregister`——它与调用顺序无关。**
>
> ```csharp
> Bootstrap.RegisterDefaults();                          // Asset(0) → Data(3) → Save(4)
> Bootstrap.Unregister<SaveBootstrapStage>();             // 摘掉内置的那个
> Bootstrap.Register(new SaveBootstrapStage(myOptions));   // 换成自己的
> ```
>
> 顺序无关这一点在 `DefaultGameLauncher` 在场时尤其重要：它在 `Awake` 里登记默认组合，与使用方自己 `Awake` 的执行先后是不确定的。
>
> **旧姿势（先 `Register` 再 `RegisterDefaults()`）仍然有效**——后者按类型跳过已存在的内置阶段，于是不会重复；但它对调用顺序敏感：反过来写（先 `RegisterDefaults()` 再 `Register` 同类型的自定义实例）**两者都会被登记**，同一个门面被初始化两次，第二次会被门面自身的幂等守卫挡下并打警告，而**你的 options 被静默忽略**。框架无从区分「另一个同类型阶段」与「同一个阶段的替换品」——`Unregister` 就是为消掉这个歧义而存在的。
>
> 另注意 `RegisterDefaults()` 与 `Unregister<T>()` 都按**精确类型**匹配：`class MySaveStage : SaveBootstrapStage` 这样的派生类不会被前者识别为「已有 Save」，从而与内置那份**双份登记**——派生替换也请走 `Unregister`。

**为什么 `RunAsync` 在失败/取消时抛异常？** `PipelineImpl.RunAsync` 在这两种情况下都「正常返回」，单看返回值分不出成功与失败。启动失败是致命的，静默吞掉会让故障表现成「服务莫名其妙没就绪」。所以本模块在 `RunAsync` 返回后读管线的终局拉取面（`IPipeline.Status` / `FailureReason`）并据此抛出——早期版本是订阅 `OnFailed`/`OnCancelled` 再用两个局部变量记账，管线补上拉取面后那套记账已删除。

**为什么 `Shutdown` 是同步的？** 框架内置四个模块的清理入口都是同步 `void`，且调用点通常是 `OnDestroy`（没有 await 机会）。将来若某个模块确实需要异步清理，再为它单独扩展接口。

**为什么 `Shutdown` 要先中止在途的 `RunAsync`？** 被清理过的阶段（含本轮从未执行的）不该再被在途管线捡起来执行——否则真实模块会在「已被销毁」与「仍在初始化」之间相撞，落成一串错误日志（启动途中销毁 `GameLauncher` 曾实测复现三条 Error）。所以 `Shutdown` 第一步取消在途运行的**链接令牌**（`RunAsync` 创建它时把调用方令牌链接进来），管线在下一个阶段边界停止，在途运行以 `OperationCanceledException` 收场。两点如实记：① **中止是协作式的**——不观察令牌的阶段会先跑完自己的沉降点，`Shutdown` 返回时在途运行只是「已收到信号」；② 该异常携带的是本次运行的链接令牌，判据用 `IsCancellationRequested`，不要比对令牌身份（详见已知限制）。

**为什么未登记任何阶段不抛异常？** 零配置使用静态服务的项目本就不需要引导流程，此时 `RunAsync` 打一条警告后直接返回。这与门面模板里 `EnsureInitialized` 抛 `InvalidOperationException` 的取向不同，是有意为之。

**为什么 `RunAsync` 重入只警告并忽略？** 每次调用都会装配一条**新**管线（管线自身的重入守卫是实例级的），并发两次会让同一批阶段跑两遍。被忽略的那次**立即返回、不代表启动完成**——调用方要「等启动结束」，应共享第一次调用的任务，而不是再调一次。

**登记表请在 `RunAsync` 之前定稿。** 运行中的修改照常生效但都打警告，因为两条后果都反直觉：运行中 `Register` 的阶段**本轮不执行**（装配期已快照）但会被 `Shutdown` 扫到；运行中 `Unregister` 的阶段**本轮仍执行**但拿不到 `Shutdown`（资源泄漏的候选）。

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
- 使用 `RegisterDefaults()` 时另需 XAsset / XData / XSave
