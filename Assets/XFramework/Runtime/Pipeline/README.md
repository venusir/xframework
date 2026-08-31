# XFramework / Pipeline 模块

## 概述

XFramework 管线模块提供**通用阶段编排**与**加载应用**两部分能力:

- **通用阶段编排**:阶段按添加顺序串行执行、进度加权聚合广播、失败/取消传播。它是对「启动管线」中编排能力的泛化——`StartupAsync` 的预置管线(装载→加载→启动)即由本模块装配而成。
- **加载应用(Loadable 子系统)**:`ILoadable` 契约 + `LoadProgress` 进度数据结构 + `LoadableStage` 单任务适配 + `ParallelStage` 并行阶段。节点实现 `ILoadable` 声明加载任务,经单任务适配后以「并行阶段」形态接入管线——**加载是管线的一种应用**(原 Loader 模块并入,`ILoader`/`Loader` 已删除)。两层物理化:通用编排位于根目录,加载应用位于 `Loading/` 子目录。

**命名空间**: `XFramework.XPipeline`

**核心理念**: 阶段声明「我要做什么」(实现 `IPipelineStage`),管线负责「按什么顺序、报多少进度、失败怎么办」;加载任务同样以阶段形态接入,任务级调度归 `LoadableStage` 单任务适配,并行编排归 `ParallelStage`,串行子段归 `SequenceStage`,编排级归管线。

## 架构设计

```
Runtime/Pipeline/
├── IPipelineStage.cs            # 通用编排:阶段接口(含 PipelineStageState 枚举)
├── IPipeline.cs                 # 通用编排:管线接口(调度入口)
├── Pipeline.cs                  # 通用编排:静态工厂 Pipeline.Create() + internal PipelineImpl 实现
├── PipelineStageContext.cs      # 通用编排:阶段执行上下文(阶段写面 + 全局读面)
├── PipelineProgress.cs          # 通用编排:全局进度快照(事件载荷)
├── StageExecution.cs            # 通用编排:阶段执行共享包装(契约兜底/取消/异常捕获)
├── ParallelStage.cs             # 通用编排:并行阶段(组内并行、事件驱动组内聚合,public)
├── SequenceStage.cs             # 通用编排:串行阶段(组内串行子段,public)
├── StageAggregator.cs           # 通用编排:容器子阶段共享聚合器(门铃 + 加权聚合,internal)
└── Loading/                     # 加载应用层(加载是管线的一种应用)
    ├── ILoadable.cs             # 可加载接口(含 LoadState 枚举)
    ├── LoadProgress.cs          # 加载进度数据结构(写后通知)
    └── LoadableStage.cs         # 单任务适配阶段(internal,ILoadable → 管线阶段)
```

> 管线不依赖 Node,依赖方向单向:Node → Pipeline(加载应用同属本模块)。

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

### 加载应用(Loadable)

#### ILoadable 契约

节点实现 `ILoadable` 声明加载任务,由加载应用阶段(`LoadableStage` 单任务适配 + `ParallelStage` 并行阶段)统一调度执行:

```csharp
public interface ILoadable
{
    int Phase { get; }
    UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken);
}
```

- `Phase` 决定调度顺序;**相同 Phase 的任务并行执行,不同 Phase 按值从小到大串行执行**
- 加载过程中通过 `progress` 更新进度/描述/状态;通过 `cancellationToken` 响应取消
- 契约兜底:正常返回但未写终态时自动补置为 `Completed`(进度视为 1f);抛异常置为 `Failed` 并经聚合报告;抛 `OperationCanceledException` 视为取消

#### Phase 分组调度

```
Phase 0: [AssetBootstrapNode]──────────────┐
Phase 3: [GameDataNode]────────────────────┤  组间串行(等上一组完成)
Phase 4: [SaveBootstrapNode]───────────────┤
Phase 90: [LocalizationBootstrapNode]──────┘
```

每个 Phase 装配一个 `ParallelStage`(组内并行、组间串行由管线编排,阶段权重 = 组内任务数),组内子阶段为 `LoadableStage` 单任务适配;装配期同步收集全部 `ILoadable` 节点(运行前快照,树在装配后变更不收录)。

#### LoadProgress

任务级进度数据结构,由 `LoadableStage` 在装载时创建并注入:

- 节点写入: `SetWeight`(首行同步调用,默认 1f,下限 0.01 防除零)/ `SetProgress` / `SetDescription` / `SetState`(Pending → Loading → Completed / Failed)
- **写后通知(门铃)**: 每次写入同步触发 `OnChanged`,调度者无需轮询即可感知任务进度变化(事件驱动,与管线一致)
- **全局级字段**(`OverallProgress` / `CurrentTaskName` 等):由调度者聚合时填充,供 UI 读取当前加载状态的全部信息;独立使用场景(如 `AssetManager.InitializeAsync` 的进度参数)不注入回调,写入仅落字段,行为与无通知时代完全一致

#### LoadableStage + ParallelStage

单任务适配阶段 `LoadableStage`(`internal sealed`,Name = 任务类型名,Weight = 1)把 `ILoadable` 桥接为管线阶段,由并行阶段 `ParallelStage`(public,Weight = Σ子阶段权重)组内并行执行:

- **镜像语义**: 任务写 `LoadProgress`(门铃)→ 全字段镜像到子上下文并单次显式通知——一次任务写入恰好触发 1 次组级聚合(加权 `Σ(w·p)/Σ(w)` + 阈值节流 ≥1% 或描述/状态变化);任务权重经镜像影响组内聚合,不泄漏到管线级
- **取消**: 任务收到已取消的 token,沉降后显式上抛 `OperationCanceledException` 使管线走取消路径——防共享包装的契约兜底把已取消任务误补为 Completed
- **失败即停**: 任意任务失败 → 立即取消兄弟任务,沉降(WhenAll)后置阶段 Failed;日志 `[Pipeline] Parallel stage failed: {任务名} ({耗时}s): {描述}`(组内失败标识,与 `[Pipeline] Pipeline failed` 编排级失败区分)
- **诊断优先**: 失败时描述/任务名强制取失败任务的值,避免被兄弟任务描述覆盖
- **并行能力通用化**: 第三方阶段同样可塞入 `ParallelStage` 并行执行(见「快速使用」)
- **串行子段**: 同 Phase 内存在先后依赖时,可将任务组包一层 `SequenceStage`(组内依次执行)再放入并行组

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
pipeline.AddStage(new LoadStage());          // 如加载应用的并行阶段 ParallelStage
pipeline.AddStage(new PostStage { Weight = 0f }); // 瞬时阶段,不占进度
pipeline.AddStage(new InitStage(), 30f);     // 30 秒超时:超时置 Failed 并停止后续阶段

// 并行执行两个阶段(组内并行、组间串行由管线承担;阶段权重 = Σ子阶段权重)
pipeline.AddStage(new ParallelStage(new IPipelineStage[] { new InitStage(), new LoadStage() }, "Parallel-Init"));

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

### 定义可加载节点

```csharp
using XFramework.XPipeline;
using XFramework.XNode;
using Cysharp.Threading.Tasks;
using System.Threading;

public class ConfigBootstrapNode : EntityNode, ILoadable
{
    // 加载阶段号(越小越先执行)
    public int Phase => 10;

    public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
    {
        // 权重与描述须在首次 await 前同步设置(组内按权重加权聚合进度)
        progress.SetWeight(2f);
        progress.SetState(LoadState.Loading);
        progress.SetDescription("加载配置表...");

        // 执行加载逻辑
        await LoadConfigFilesAsync(cancellationToken);

        progress.SetState(LoadState.Completed);
        progress.SetDescription("配置表加载完成");
    }
}
```

### 一键启动节点树

```csharp
using XFramework.XPipeline;
using XFramework.XNode;

// 构建节点树
var root = RootNode.Create();
root.AddNode<ServiceInitializerNode>();
root.AddNode<ConfigBootstrapNode>();
root.AddNode<GameplayNode>();

// 一键启动(自动执行装载→加载→启动)
await root.StartupAsync();

// 带进度回调
await root.StartupAsync(new Progress<LoadProgress>(p =>
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
- **事件驱动进度** — 阶段/任务写入即聚合,管线不轮询、无每帧开销
- **失败/取消即停** — 异常→Failed、OCE→取消,后续阶段不再执行,终局三路互斥
- **阈值节流广播** — 变化 ≥1% 或状态/描述变化才广播,避免垃圾推送
- **实例即用即弃** — `Pipeline.Create()` 工厂创建,非全局单例
- **声明式加载** — 节点实现 `ILoadable` 声明加载需求,无需关心调度逻辑
- **Phase 分组调度** — 相同 Phase 并行,不同 Phase 串行,兼顾性能与依赖顺序
- **契约兜底** — `LoadAsync` 正常返回但未写终态时自动视为完成(进度 1f),不会阻塞调度
- **加权聚合** — 组内按 `Weight` 加权聚合 `Σ(w·p)/Σ(w)`;失败任务不计入进度(进度略回退属预期)
- **串并行嵌套组合** — 顶层固定串行,组内并行/串行经 `ParallelStage`/`SequenceStage` 容器嵌套表达任意调度拓扑

## 依赖

- `UniTask`(框架层已提供)
