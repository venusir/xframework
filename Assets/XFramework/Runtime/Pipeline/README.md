# XFramework / Pipeline 模块

## 概述

XFramework 管线模块提供**通用异步阶段编排**能力(附**相位分组编排**这一声明式装配形态):

- **通用阶段编排**:阶段按添加顺序串行执行、进度加权聚合广播、失败/取消传播。实例即用即弃(非全局单例),第三方可独立使用,不依赖节点树。
- **相位分组编排**:实现 `IPhaseStage` 声明相位号,同相位阶段并行执行、相位升序串行;经 `Pipeline.BuildPhaseGroups` 一键装配为「每相位一个并行阶段」。节点树 `StartupAsync` 的预置管线(收集 → 相位分组执行 → 启动)即由此装配而成;框架引导模块(Asset/Data/Save/Localization)均以相位阶段形态参与。

**命名空间**: `XFramework.XPipeline`

**核心理念**: 阶段声明「我要做什么」(实现 `IPipelineStage`),管线负责「按什么顺序、报多少进度、失败怎么办」;并行归 `ParallelStage`、串行子段归 `SequenceStage`,编排级归管线,容器可任意嵌套表达任意串并行拓扑。

## 架构设计

```
Runtime/Pipeline/
├── IPipeline.cs                  # 管线接口(调度入口)
├── IPipelineStage.cs             # 阶段接口(含 PipelineStageState 枚举)
├── IPhaseStage.cs                # 相位阶段接口(IPipelineStage + Phase 声明)
├── Pipeline.cs                   # 静态门面:创建实例 + 相位分组装配助手 BuildPhaseGroups
├── PipelineStageContext.cs       # 阶段执行上下文(阶段写面 + 全局读面)
├── PipelineProgress.cs           # 全局进度快照(事件载荷)
├── StageExecution.cs             # 阶段执行共享包装(契约兜底/取消/异常捕获)
├── ParallelStage.cs              # 并行阶段(组内并行、事件驱动组内聚合,public)
├── SequenceStage.cs              # 串行阶段(组内串行子段,public)
└── StageAggregator.cs            # 容器子阶段共享聚合器(门铃 + 加权聚合,internal)
```

> 管线不依赖 Node,依赖方向单向:Node → Pipeline(StartupAsync 装配在 Node 侧,复用本模块的相位分组助手)。

## 核心概念

### 阶段(IPipelineStage)

阶段是管线的执行单元,声明 `Name`(进度描述)与 `Weight`(进度权重):

```csharp
public interface IPipelineStage
{
    string Name { get; }
    float Weight { get; }
    UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken);
}
```

阶段经 `PipelineStageContext` 主动写入进度/状态/描述——**写入点同步触发管线级聚合与广播(事件驱动,非轮询)**。

### 执行模型

- **串行执行**: 阶段按添加顺序逐 await,前一阶段返回后才启动下一阶段;`RunAsync` 返回时无在途阶段任务
- **失败即停**: 阶段抛异常 → 置 Failed 并经 `OnFailed` 报告,后续阶段不再执行
- **取消**: `RunAsync(CancellationToken)` 取消后当前阶段收到已取消的 token,尚未开始的阶段不再执行,触发 `OnCancelled`,**不触发** `OnCompleted` 与 `OnFailed`;阶段自行抛 `OperationCanceledException` 同样视为取消
- **契约兜底**: 阶段正常返回但未写终态(未调用 `SetState`)时自动视为完成(进度 1f),不会阻塞调度
- **阶段超时**: `AddStage(stage, timeoutSeconds)` 为单个阶段设置超时(0/负值/NaN 不启用);超时触发 → 取消当前阶段运行并置 `Failed`(描述含超时信息)经 `OnFailed` 报告,后续阶段不再执行;不响应取消的挂起阶段不阻塞管线(在途任务被放弃,其后续上下文写入被忽略)
- **阶段日志**: 顶层阶段开始/结束经 `Debug.Log` 记录耗时(`[Pipeline] Stage 'X' start` / `'X' completed|failed|cancelled|timed out in Nms`),便于诊断执行时间分布;容器(并行/串行)组内子阶段不输出 start/end 日志(组级失败已有 `[Pipeline] Parallel/Sequence stage failed: ...` 诊断日志兜底)
- **重入守卫**: 运行中重复调用 `RunAsync` 打 `[Pipeline]` 警告忽略;空阶段列表打警告并直接触发完成

### 容器组合

管线顶层固定串行(隐式串行根容器,不提供顶层并行 API);组内并行经 `ParallelStage`、组内串行经 `SequenceStage` 表达,两容器可任意嵌套——任何阶段集合的串并行组合都是一棵「串行容器 / 并行容器」树:

- **顶层并行** = 将并行段包为 `ParallelStage` 作为单阶段添加(见「快速使用」)
- **组内串行子段** = `SequenceStage` 包一层,如 `ParallelStage(SequenceStage(A, B), C)` 表达「组内先 A 后 B、与 C 并行」
- **子段切换进度回落属预期**: 未开始子阶段(Pending)不占进度,与管线「阶段切换回落」一致(等权子段完成瞬间 1.0 → 下一子段启动 0.5)

### 进度模型

- **加权聚合**: 全局进度 = `Σ(w·p) / Σ(w)`——已完成阶段记 w、执行中记 w·p;失败阶段权重移出;`Weight = 0` 的阶段不占进度(如瞬时阶段)
- **阈值节流**: 全局进度变化 ≥1% 或任一阶段状态/描述变化才广播;终局必广播
- **阶段切换时进度回落属预期**(新阶段从 0 开始);装配 `Weight = 0` 的瞬时阶段可平滑过渡

### 相位分组编排(IPhaseStage)

「同相位并行、相位升序串行」是启动/初始化类流程的常见需求,以**相位阶段 + 分组装配助手**提供声明式表达——执行面与普通阶段完全一致,不引入第二套契约:

```csharp
public interface IPhaseStage : IPipelineStage
{
    int Phase { get; }   // 同值并行执行,不同值按值升序串行执行
}
```

```csharp
// 按相位分组装配:每组一个 ParallelStage(组内并行、组内保持输入顺序),返回按相位升序的清单
public static IReadOnlyList<IPipelineStage> BuildPhaseGroups(
    IReadOnlyList<IPhaseStage> stages, string nameFormat = "Phase-{0}");
```

#### 相位分组调度示意

```
Phase 0:  [AssetBootstrapNode]────────────────┐
Phase 3:  [GameDataNode]──────────────────────┤  组间串行(等上一相位完成)
Phase 4:  [SaveBootstrapNode]─────────────────┤
Phase 90: [LocalizationBootstrapNode]─────────┘
```

装配结果直接逐个 `AddStage` 加入管线;组权重 = Σ 组内子阶段声明权重(引导节点统一声明 1f,故数值 = 组内节点数)。同相位内若存在先后依赖,可用 `SequenceStage` 包一层再放入该相位组。

> **内置相位约定**见文末表格;数值含义属模块间约定,由各使用方自行声明,引擎不解释。

### 并行/串行容器语义(组内细节)

- **并行组内事件驱动聚合**: 子阶段写入即触发组内加权聚合(门铃 + 阈值节流 ≥1% 或描述/状态变化),收敛后转发组主上下文——**一次子阶段写入恰好 1 次组级聚合**
- **取消**: 子阶段收到已取消的 token;组沉降后若 token 已取消或任一子阶段以取消结束,组上抛 `OperationCanceledException` 走管线取消路径(契约兜底不会把已取消的组误补为 Completed)
- **失败即停**: 组内任一子阶段失败(抛异常或主动 `SetState(Failed)`)→ 立即取消其余兄弟,沉降(WhenAll)后组置 Failed;日志 `[Pipeline] Parallel stage failed: {任务名} ({耗时}s): {描述}`(组级失败标识,与 `[Pipeline] Pipeline failed` 编排级失败区分)
- **诊断优先**: 失败时描述/任务名强制取失败子阶段的值,避免被兄弟描述覆盖
- **取消语义由阶段负责**: 需真正可中断的阶段必须把 `CancellationToken` 传入内层异步服务并禁止吞掉 `OperationCanceledException`;同步瞬时阶段无取消窗口属正常

## 快速使用

### 通用管线

```csharp
using XFramework.XPipeline;
using Cysharp.Threading.Tasks;
using System.Threading;

// 1. 定义阶段(实现 IPipelineStage)
public sealed class InitStage : IPipelineStage
{
    public string Name => "Init";
    public float Weight => 1f;

    public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
    {
        context.SetDescription("Initializing...");
        await DoInitAsync(cancellationToken);
        context.SetProgress(1f);
    }
}

// 2. 装配并运行
var pipeline = Pipeline.Create();
pipeline.AddStage(new InitStage());
pipeline.AddStage(new ParallelStage(new IPipelineStage[] { new InitStage(), new LoadStage() }, "Parallel-Init")); // 组内并行
pipeline.AddStage(new PostStage { Weight = 0f }); // 瞬时阶段,不占进度
pipeline.AddStage(new InitStage(), 30f);     // 30 秒超时:超时置 Failed 并停止后续阶段

// 容器组合:并行组内先 Init 后 Load(串行子段),与 PostStage 并行——嵌套树表达任意串并行组合
pipeline.AddStage(new ParallelStage(new IPipelineStage[]
{
    new SequenceStage(new IPipelineStage[] { new InitStage(), new LoadStage() }, "Seq-Bootstrap"),
    new PostStage { Weight = 0f },
}, "Bootstrap"));

pipeline.OnProgressUpdate += p => Debug.Log($"进度: {p.OverallProgress:P1} {p.Description}");
pipeline.OnCompleted += () => Debug.Log("管线完成");
pipeline.OnCancelled += () => Debug.LogWarning("管线取消");
pipeline.OnFailed += reason => Debug.LogError($"管线失败: {reason}");

await pipeline.RunAsync();
pipeline.Destroy();
```

> 节点树一键启动 `StartupAsync` 即内部装配并运行预置管线,详见 [Node 模块](../Node/README.md)。

### 定义相位阶段

业务引导/初始化模块实现 `IPhaseStage`(Phase + 普通阶段四成员),挂树后自动被收集分组:

```csharp
using XFramework.XPipeline;
using XFramework.XNode;
using Cysharp.Threading.Tasks;
using System.Threading;

public sealed class ConfigBootstrapNode : EntityNode, IPhaseStage
{
    public int Phase => 10;              // 相位号(内置约定见文末;越小越先执行)
    public string Name => GetType().Name;
    public float Weight => 1f;           // 组内等权(组权重 = 组内节点数)

    public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
    {
        context.SetDescription("加载配置表...");
        context.SetState(PipelineStageState.Executing);

        // 执行加载逻辑(取消须向内部异步服务传播 token,禁止吞 OperationCanceledException)
        await LoadConfigFilesAsync(cancellationToken);

        context.SetProgress(1f);
        context.SetState(PipelineStageState.Completed);
    }
}
```

> 阶段契约兜底保证:未写终态的正常返回自动视为完成;不写 `SetState(Executing)` 也不影响进度聚合(容器会预置执行中状态)。引导节点与节点树的装配模板详见 [Node 模块](../Node/README.md)。

### 一键启动节点树

```csharp
using XFramework.XPipeline;
using XFramework.XNode;

// 构建节点树
var root = RootNode.Create();
root.AddNode<ServiceInitializerNode>();
root.AddNode<ConfigBootstrapNode>();
root.AddNode<GameplayNode>();

// 一键启动(收集 → 按相位分组执行 → 启动)
await root.StartupAsync();

// 带进度回调(接收管线级快照)
await root.StartupAsync(new Progress<PipelineProgress>(p =>
{
    Debug.Log($"启动进度: {p.OverallProgress * 100}% - {p.Description}");
}));
```

## 内置 Phase 约定

为保持一致性，框架内置模块使用以下 Phase 值：

| Phase | 模块         | 说明                         |
| ----- | ------------ | ---------------------------- |
| 0     | Asset        | 资源管理器初始化（最早）     |
| 3     | Data         | 数据管理器初始化             |
| 4     | Save         | 存档管理器初始化             |
| 90    | Localization | 本地化数据加载               |
| 90+   | 用户自定义   | 建议业务模块从此范围开始     |

## 设计原则

- **声明式阶段** — 阶段实现 `IPipelineStage` 声明执行内容,无需关心调度逻辑
- **事件驱动进度** — 阶段写入即聚合,管线不轮询、无每帧开销
- **失败/取消即停** — 异常→Failed、OCE→取消,后续阶段不再执行,终局三路互斥
- **阈值节流广播** — 变化 ≥1% 或状态/描述变化才广播,避免垃圾推送
- **实例即用即弃** — `Pipeline.Create()` 工厂创建,非全局单例
- **单一执行契约** — 阶段是唯一执行单元,无第二套任务/状态机(状态/进度写面统一在 `PipelineStageContext`)
- **声明式相位** — 节点实现 `IPhaseStage` 声明相位号,无需关心分组调度逻辑
- **相位分组调度** — 相同相位并行、不同相位串行,兼顾性能与依赖顺序
- **契约兜底** — 阶段正常返回但未写终态时自动视为完成(进度 1f),不会阻塞调度
- **加权聚合** — 组内按 `Weight` 加权聚合 `Σ(w·p)/Σ(w)`;失败阶段不计入进度(进度略回退属预期)
- **串并行嵌套组合** — 顶层固定串行,组内并行/串行经 `ParallelStage`/`SequenceStage` 容器嵌套表达任意调度拓扑

## 依赖

- `UniTask`(框架层已提供)
