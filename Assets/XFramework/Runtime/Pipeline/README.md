# XFramework / Pipeline 模块

## 概述

XFramework 管线模块提供通用阶段编排抽象:阶段按添加顺序串行执行、进度加权聚合广播、失败/取消传播。它是对「启动管线」中编排能力的泛化——`StartupAsync` 的四阶段(装载→加载→启动→回收)即由本模块装配而成,加载阶段的调度由 [Loader 模块](../Loader/README.md) 承担。

**命名空间**: `XFramework.XPipeline`

**核心理念**: 阶段声明「我要做什么」(实现 `IPipelineStage`),管线负责「按什么顺序、报多少进度、失败怎么办」。

## 架构设计

```
Runtime/Pipeline/
├── IPipelineStage.cs            # 阶段接口(含 PipelineStageState 枚举)
├── IPipeline.cs                 # 管线接口(调度入口)
├── Pipeline.cs                  # 静态工厂 Pipeline.Create() + internal PipelineImpl 实现
├── PipelineStageContext.cs      # 阶段执行上下文(阶段写面 + 全局读面)
└── PipelineProgress.cs          # 全局进度快照(事件载荷)
```

> 管线不依赖 Loader 与 Node,依赖方向单向:Node → Loader → Pipeline。

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
- **取消**: `RunAsync(CancellationToken)` 取消后当前阶段收到已取消的 token,尚未开始的阶段不再执行,触发 `OnFailed("Pipeline cancelled.")`,**不触发** `OnCompleted`;阶段自行抛 `OperationCanceledException` 同样视为取消
- **契约兜底**: 阶段正常返回但未写终态(未调用 `SetState`)时自动视为完成(进度 1f),不会阻塞调度
- **重入守卫**: 运行中重复调用 `RunAsync` 打 `[Pipeline]` 警告忽略;空阶段列表打警告并直接触发完成

### 进度模型

- **加权聚合**: 全局进度 = `Σ(w·p) / Σ(w)`——已完成阶段记 w、执行中记 w·p;失败阶段权重移出;`Weight = 0` 的阶段不占进度(如瞬时阶段)
- **阈值节流**: 全局进度变化 ≥1% 或任一阶段状态/描述变化才广播;终局必广播
- **阶段切换时进度回落属预期**(新阶段从 0 开始);装配 `Weight = 0` 的瞬时阶段可平滑过渡

## 快速使用

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
pipeline.AddStage(new LoadStage());          // 如 Loader 的加载阶段
pipeline.AddStage(new PostStage { Weight = 0f }); // 瞬时阶段,不占进度

pipeline.OnProgressUpdate += p => Debug.Log($"进度: {p.OverallProgress:P1} {p.Description}");
pipeline.OnCompleted += () => Debug.Log("管线完成");
pipeline.OnFailed += reason => Debug.LogError($"管线失败: {reason}");

await pipeline.RunAsync();
pipeline.Destroy();
```

> 节点树一键启动 `StartupAsync` 即内部装配并运行预置管线,详见 [Node 模块](../Node/README.md)。

## 设计原则

- **声明式阶段** — 阶段实现 `IPipelineStage` 声明执行内容,无需关心调度逻辑
- **事件驱动进度** — 阶段写入即聚合,管线不轮询、无每帧开销
- **失败/取消即停** — 异常→Failed、OCE→取消,后续阶段不再执行,终局三路互斥
- **阈值节流广播** — 变化 ≥1% 或状态/描述变化才广播,避免垃圾推送
- **实例即用即弃** — `Pipeline.Create()` 工厂创建,非全局单例

## 依赖

- `UniTask`(框架层已提供)

> 与 [Loader 模块](../Loader/README.md) 的分工:管线负责**阶段编排**(串行、进度、失败取消);加载阶段(Loader)负责**任务调度**(Phase 分组、任务级进度、轮询)。
