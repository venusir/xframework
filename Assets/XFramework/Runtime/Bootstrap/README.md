# XFramework / Bootstrap 模块

框架启动引导的**登记表与运行入口**。让需要异步初始化的服务把自己的初始化与反向清理交给一个统一的、有序的启动流程。

## 定位

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
Bootstrap.Shutdown();   // 按执行序的逆序（相位降序），逐个调 stage.Shutdown()
```

登记表本身可以随时查看与清空（`Stages` **是实时视图而非快照**，后续登记会反映出来——只读用途，勿缓存后假定其不变）：

```csharp
foreach (var stage in Bootstrap.Stages)      // 按登记顺序
    Debug.Log($"{stage.Name} (Phase {stage.Phase})");

Bootstrap.Clear();                            // 清空登记表（不影响已运行的管线）
```

## 定义一个阶段

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
| `BootstrapPhases.Data` | 3 | Data | 数据管理器初始化 |
| `BootstrapPhases.Save` | 4 | Save | 存档管理器初始化。**硬性晚于 Data**：恢复扫描要用 `DataManager.CreateSnapshot` 回滚数据块 |
| `BootstrapPhases.Localization` | 90 | Localization | 本地化数据加载（不在默认登记组合内） |
| `BootstrapPhases.UserStart` | 90 | 用户自定义 | 业务阶段的建议起点（与 Localization 同相位并行——依赖本地化的阶段请取更大的值） |

**0–89 由框架保留**：框架新增模块可能占用其中任意值（1、2、5–89 目前空闲，但只是现状、不是保留承诺）。需要精确插进框架阶段之间（例如「Asset 之后、Data 之前」）时，别赌空闲值——用 `Unregister` 换掉内置阶段，那一段顺序自己编排。

**同相位 = 并行**，彼此不可有依赖；有依赖就必须分属不同相位。

## 与 Pipeline 的关系

本模块**不含任何编排逻辑**。相位分组、同相位并行、加权进度聚合、失败即停、取消传播全部由 Pipeline 模块提供，本模块只做两件事：

- 用 `Pipeline.BuildPhaseGroups` 把登记表装配成「每相位一个 `ParallelStage`」
- 补上 Pipeline 没有的那一半——**反向清理**

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
> 顺序无关这一点在 `GameLauncher` 在场时尤其重要：它在 `Awake` 里登记默认组合，与使用方自己 `Awake` 的执行先后是不确定的。
>
> **旧姿势（先 `Register` 再 `RegisterDefaults()`）仍然有效**——后者按类型跳过已存在的内置阶段，于是不会重复；但它对调用顺序敏感：反过来写（先 `RegisterDefaults()` 再 `Register` 同类型的自定义实例）**两者都会被登记**，同一个门面被初始化两次，第二次会被门面自身的幂等守卫挡下并打警告，而**你的 options 被静默忽略**。框架无从区分「另一个同类型阶段」与「同一个阶段的替换品」——`Unregister` 就是为消掉这个歧义而存在的。
>
> 另注意 `RegisterDefaults()` 与 `Unregister<T>()` 都按**精确类型**匹配：`class MySaveStage : SaveBootstrapStage` 这样的派生类不会被前者识别为「已有 Save」，从而与内置那份**双份登记**——派生替换也请走 `Unregister`。

**为什么 `RunAsync` 在失败/取消时抛异常？** `PipelineImpl.RunAsync` 在这两种情况下都「正常返回」，单看返回值分不出成功与失败。启动失败是致命的，静默吞掉会让故障表现成「服务莫名其妙没就绪」。所以本模块在 `RunAsync` 返回后读管线的终局拉取面（`IPipeline.Status` / `FailureReason`）并据此抛出——早期版本是订阅 `OnFailed`/`OnCancelled` 再用两个局部变量记账，管线补上拉取面后那套记账已删除。

**为什么 `Shutdown` 是同步的？** 框架内置四个模块的清理入口都是同步 `void`，且调用点通常是 `OnDestroy`（没有 await 机会）。将来若某个模块确实需要异步清理，再为它单独扩展接口。

**为什么未登记任何阶段不抛异常？** 零配置使用静态服务的项目本就不需要引导流程，此时 `RunAsync` 打一条警告后直接返回。这与门面模板里 `EnsureInitialized` 抛 `InvalidOperationException` 的取向不同，是有意为之。

**为什么 `RunAsync` 重入只警告并忽略？** 每次调用都会装配一条**新**管线（管线自身的重入守卫是实例级的），并发两次会让同一批阶段跑两遍。被忽略的那次**立即返回、不代表启动完成**——调用方要「等启动结束」，应共享第一次调用的任务，而不是再调一次。

**登记表请在 `RunAsync` 之前定稿。** 运行中的修改照常生效但都打警告，因为两条后果都反直觉：运行中 `Register` 的阶段**本轮不执行**（装配期已快照）但会被 `Shutdown` 扫到；运行中 `Unregister` 的阶段**本轮仍执行**但拿不到 `Shutdown`（资源泄漏的候选）。

**为什么引导流程不暂停 Update（也不提供「就绪」门控）？** 每帧派发由 Update 模块自注入 PlayerLoop 独立驱动，与引导流程无关——`RunAsync` 期间（Asset 初始化可能持续数秒）派发照常进行。这不构成缺口：依赖已初始化服务的回调，正确接法是 `await RunAsync()` 完成之后再注册，或直接写在自家引导阶段里。框架不自动暂停——那等于替使用方决定「加载期算不算可派发的时间」，而且失败/中断路径会让整个应用背上「忘了恢复即静默冻结」的风险。确需加载期安静时自行接管（只冻逻辑轴，`Unscaled` 节点照常）：

```csharp
UpdateManager.Pause();
try { await Bootstrap.RunAsync(); }
finally { UpdateManager.Resume(); }
```

## 已知限制

**没有超时手段。** `IPipeline.AddStage(stage, timeoutSeconds)` 的超时只作用在**阶段**粒度，而本模块每相位装配为一个 `ParallelStage`——从 Bootstrap 侧无处传入超时。一个不响应取消、又永不返回的阶段会挂死整个启动流程（乐观超时只在直接使用 Pipeline 时可用，见 Pipeline README）。

**`Shutdown` 可能在阶段从未执行过时被调用。** 启动在它之前失败/取消，或它是在 `RunAsync` 运行中才登记的，`Bootstrap.Shutdown` 都会扫到它。清理实现要能在「本阶段什么都没做」时安全空转——内置的 `LocalizationBootstrapStage` 就是范例：只在确实由自己完成初始化时才销毁门面。

**运行中修改登记表只告警、不阻止**，两条反直觉后果与理由见上面「设计取舍」的末两段：运行中登记的阶段本轮不执行、运行中注销的阶段拿不到 `Shutdown`。

## 依赖

- **XFramework.XPipeline** —— 阶段契约与相位分组装配
- **UniTask** —— 异步
- 使用 `RegisterDefaults()` 时另需 XAsset / XData / XSave

## 测试注意事项

`Bootstrap` 的登记表是**静态**的。测试须在 `SetUp`/`TearDown` 里调 `Bootstrap.Clear()` 复位，否则用例间互相污染。
