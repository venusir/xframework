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
Bootstrap.Shutdown();   // 按登记顺序的逆序，逐个调 stage.Shutdown()
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
    public int Phase => 10;                  // 同相位并行，相位升序串行
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

| Phase | 模块 | 说明 |
| ----- | ---- | ---- |
| 0 | Asset | 资源管理器初始化（最早——本地化等模块的数据要经 YooAsset 地址加载） |
| 3 | Data | 数据管理器初始化 |
| 4 | Save | 存档管理器初始化。**硬性晚于 Data(3)**：恢复扫描要用 `DataManager.CreateSnapshot` 回滚数据块 |
| 90 | Localization | 本地化数据加载（不在默认登记组合内） |
| 90+ | 用户自定义 | 建议业务模块从此区间开始 |

1 与 2 曾是 Lock / Message 的相位，二者已改为 `[RuntimeInitializeOnLoadMethod]` 自管理生命周期而被移出，**这两个值现为空闲**。

**同相位 = 并行**，彼此不可有依赖；有依赖就必须分属不同相位。

## 与 Pipeline 的关系

本模块**不含任何编排逻辑**。相位分组、同相位并行、加权进度聚合、失败即停、取消传播全部由 Pipeline 模块提供，本模块只做两件事：

- 用 `Pipeline.BuildPhaseGroups` 把登记表装配成「每相位一个 `ParallelStage`」
- 补上 Pipeline 没有的那一半——**反向清理**（旧实现里这一步散落在各引导节点的 `OnDestroy` 中）

## 设计取舍

**为什么是显式登记而不是反射发现？** 零反射、顺序可控、可测试，且使用方一眼能看出到底有哪些东西会在这个启动流程里跑。

**登记表按实例去重，不按类型。** 同一个实例重复登记会被忽略，但**同类型的多个实例可以共存**——登记表是「初始化步骤列表」，不是「每类型一个的容器」，参数化的阶段用同一类型登记多次是合法的。唯一例外是 `RegisterDefaults()`：它按类型跳过已存在的内置阶段，因此可重复调用而不叠加。

**为什么 `RunAsync` 在失败/取消时抛异常？** `PipelineImpl.RunAsync` 在这两种情况下都「正常返回」，单看返回值分不出成功与失败。启动失败是致命的，静默吞掉会让故障表现成「服务莫名其妙没就绪」。所以本模块订阅 `OnFailed`/`OnCancelled` 并在事后抛出。

**为什么 `Shutdown` 是同步的？** 框架内置四个模块的清理入口都是同步 `void`，且调用点通常是 `OnDestroy`（没有 await 机会）。将来若某个模块确实需要异步清理，再为它单独扩展接口。

**为什么未登记任何阶段不抛异常？** 零配置使用静态服务的项目本就不需要引导流程，此时 `RunAsync` 打一条警告后直接返回。这与门面模板里 `EnsureInitialized` 抛 `InvalidOperationException` 的取向不同，是有意为之。

## 依赖

- **XFramework.XPipeline** —— 阶段契约与相位分组装配
- **UniTask** —— 异步
- 使用 `RegisterDefaults()` 时另需 XAsset / XData / XSave

## 测试注意事项

`Bootstrap` 的登记表是**静态**的。测试须在 `SetUp`/`TearDown` 里调 `Bootstrap.Clear()` 复位，否则用例间互相污染。
