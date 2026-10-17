# Pipeline —— 通用异步阶段编排（附相位分组编排这一声明式装配形态）

## 概述

命名空间 `XFramework.XPipeline`。

XFramework 管线模块提供**通用异步阶段编排**能力(附**相位分组编排**这一声明式装配形态):

- **通用阶段编排**:阶段按添加顺序串行执行、进度加权聚合广播、失败/取消传播。实例即用即弃(非全局单例),第三方可独立使用,无框架内模块依赖。
- **相位分组编排**:实现 `IPhaseStage` 声明相位号,同相位阶段并行执行、相位升序串行;经 `Pipeline.BuildPhaseGroups` 一键装配为「每相位一个并行阶段」。启动引导模块 `Bootstrap.RunAsync` 的管线即由此装配而成;框架引导模块(Asset / Input / UI / Data / Save / Localization)均以相位阶段形态参与。

**核心理念**: 阶段声明「我要做什么」(实现 `IPipelineStage`),管线负责「按什么顺序、报多少进度、失败怎么办」;并行归 `ParallelStage`、串行子段归 `SequenceStage`,编排级归管线,容器可任意嵌套表达任意串并行拓扑。

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

// 终局读拉取面(失败/取消时 RunAsync 也正常返回,返回值分不出结果)
if (pipeline.Status != PipelineStatus.Completed)
    Debug.LogError($"管线未完成: {pipeline.Status} {pipeline.FailureReason}");

pipeline.Destroy();
```

> 启动引导模块 `Bootstrap.RunAsync` 即内部装配并运行这套管线,详见 [Bootstrap 模块](../Bootstrap/README.md)。

### 定义相位阶段

业务引导/初始化模块实现 `IPhaseStage`(Phase + 普通阶段三成员),经 `Pipeline.BuildPhaseGroups` 分组装配:

```csharp
using XFramework.XPipeline;
using Cysharp.Threading.Tasks;
using System.Threading;

public sealed class ConfigBootstrapStage : IPhaseStage
{
    public int Phase => 10;              // 相位号(内置约定见「内置相位约定」;越小越先执行)
    public string Name => GetType().Name;
    public float Weight => 1f;           // 组内等权(组权重 = 组内阶段数)

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

> 阶段契约兜底保证:未写终态的正常返回自动视为完成;不写 `SetState(Executing)` 也不影响进度聚合(容器会预置执行中状态)。需要「初始化 + 反向清理」成对时实现 `IBootstrapStage`(`IPhaseStage` + `Shutdown`),装配模板详见 [Bootstrap 模块](../Bootstrap/README.md)。

### 一键启动引导流程

把相位阶段登记进引导模块,`Bootstrap` 会按相位装配本模块的管线并运行:

```csharp
using System;
using XFramework.XBootstrap;
using XFramework.XPipeline;

Bootstrap.Register(new ConfigBootstrapStage());
Bootstrap.RegisterDefaults();   // 可选:登记框架内置的 Asset / Data / Save 三个阶段

// 一键启动(按相位分组装配 → 运行)
await Bootstrap.RunAsync();

// 带进度回调(接收管线级快照)
await Bootstrap.RunAsync(new Progress<PipelineProgress>(p =>
{
    Debug.Log($"启动进度: {p.OverallProgress * 100}% - {p.Description}");
}));
```

## 模块结构

```text
Runtime/Pipeline/
├── IPipeline.cs                  # 管线接口（调度入口 + Status / FailureReason 拉取面）
├── IPipelineStage.cs             # 阶段接口（含 PipelineStageState 枚举）
├── IPhaseStage.cs                # 相位阶段接口（IPipelineStage + Phase 声明）
├── Pipeline.cs                   # 静态门面：Create 工厂 + BuildPhaseGroups 分组助手
├── PipelineStageContext.cs       # 阶段执行上下文（阶段写面；读取面在 PipelineProgress / IPipeline.Status）
├── PipelineProgress.cs           # 全局进度快照（事件载荷）
├── StageExecution.cs             # internal：阶段执行共享包装（契约兜底 / 取消 / 异常捕获）
├── ParallelStage.cs              # 并行阶段（组内并行、事件驱动组内聚合）
├── SequenceStage.cs              # 串行阶段（组内串行子段）
├── StageAggregator.cs            # internal：容器子阶段共享聚合器（门铃 + 加权聚合）
├── ContextBell.cs                # internal：上下文写入门铃（重入折叠 / 迟写防护）
└── ContextAggregation.cs         # internal：加权扫描共享助手（阈值节流 / 状态快照）
```

标 `internal` 的四个是编排内部件，使用方不直接接触；其余八个是公开面。

## 架构设计

本模块的公开面与内部面：

| 面 | 类型 |
|---|---|
| **公开** | `IPipeline`、`IPipelineStage`(含 `PipelineStageState`)、`IPhaseStage`、`Pipeline`(静态门面)、`PipelineStageContext`、`PipelineProgress`、`ParallelStage`、`SequenceStage` |
| **internal** | `StageExecution`、`StageAggregator`、`ContextBell`、`ContextAggregation` |

> 管线不依赖上层模块,依赖方向单向:Bootstrap → Pipeline(装配在引导模块侧,复用本模块的相位分组助手)。

### 阶段（IPipelineStage）

阶段是管线的执行单元,声明 `Name`(进度描述)与 `Weight`(进度权重):

```csharp
public interface IPipelineStage
{
    string Name { get; }
    float Weight { get; }
    UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken);
}
```

阶段经 `PipelineStageContext` 主动写入进度/状态/描述/任务名——**写入点同步触发管线级聚合与广播(事件驱动,非轮询)**。任务名用 `SetCurrentTaskName` 上报(如「正在下载 xxx」),它单独变化也会广播,UI 据此显示比阶段名更细的当前子任务。

### 相位分组编排（IPhaseStage）

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
Phase 0:  [AssetBootstrapStage]───────────────┐
Phase 1:  [InputBootstrapStage]───────────────┤
Phase 2:  [UIBootstrapStage]──────────────────┤  组间串行(等上一相位完成)
Phase 3:  [DataBootstrapStage]────────────────┤
Phase 4:  [SaveBootstrapStage]────────────────┤
Phase 90: [LocalizationBootstrapStage]────────┘
```

装配结果直接逐个 `AddStage` 加入管线;组权重 = Σ 组内子阶段声明权重(引导阶段统一声明 1f,故数值 = 组内阶段数)。同相位内若存在先后依赖,可用 `SequenceStage` 包一层再放入该相位组。

> **内置相位约定**见「内置相位约定」节;数值含义属模块间约定,由各使用方自行声明,引擎不解释。

## 行为契约

### 执行模型

- **串行执行**: 阶段按添加顺序逐 await,前一阶段返回后才启动下一阶段;`RunAsync` 返回时无在途阶段任务
- **失败即停**: 阶段抛异常 → 置 Failed 并经 `OnFailed` 报告,后续阶段不再执行。**编排级失败日志 `Pipeline failed: …` 会带上抛出异常的完整对象**(JSONL 的 `exc` 字段,含类型与抛出点堆栈;容器内子阶段抛出的同样传到底),容器级的 `Parallel/Sequence stage failed` 行保持纯文本——两处都挂会让同一个堆栈出现两遍
- **取消**: `RunAsync(CancellationToken)` 取消后当前阶段收到已取消的 token,尚未开始的阶段不再执行,触发 `OnCancelled`,**不触发** `OnCompleted` 与 `OnFailed`;阶段自行抛 `OperationCanceledException` 同样视为取消。阶段可以对取消无感并正常返回,但**阶段沉降后 token 已取消即判取消**——否则最后一个阶段那样收场会直接落进完成终局,取消被静默吞掉(容器与顶层同一判据)
- **契约兜底**: 阶段正常返回但未写终态(未调用 `SetState`)时自动视为完成(进度 1f),不会阻塞调度
- **阶段超时**: `AddStage(stage, timeoutSeconds)` 为单个阶段设置超时(0/负值/NaN 不启用);超时触发 → 取消当前阶段运行并置 `Failed`(描述含超时信息)经 `OnFailed` 报告,后续阶段不再执行;不响应取消的挂起阶段不阻塞管线(在途任务被放弃,其后续上下文写入被忽略)
- **阶段日志**: 顶层阶段开始/结束经 `Debug.Log` 记录耗时(`[Pipeline] Stage 'X' start` / `'X' completed|failed|cancelled|timed out in Nms`),便于诊断执行时间分布;容器(并行/串行)组内子阶段不输出 start/end 日志(组级失败已有 `[Pipeline] Parallel/Sequence stage failed: ...` 诊断日志兜底)
- **重入守卫**: 运行中重复调用 `RunAsync` 打 `[Pipeline]` 警告忽略;空阶段列表打警告并直接触发完成
- **运行期装配防护**: 运行中 `AddStage` 打 `[Pipeline] AddStage: already running` 警告并忽略(阶段列表运行期只读,运行中上下文已按启动时刻快照,入列会错位)
- **写入线程契约**: 阶段经 `PipelineStageContext` 写入须与 `RunAsync` 调度同一上下文(Unity 主线程)——写入同步触发聚合与订阅者回调,整条链非线程安全;Editor 下越线程写入打 LogError 提示(Release 构建零开销)
- **订阅者异常隔离**: 四个事件的订阅者抛出的异常记 `[Pipeline] {事件名} subscriber threw:` 后继续,不改变阶段状态与终局——订阅方(如 UI)的 bug 不会把无辜阶段打成 Failed,不会把完成/取消改报成失败,也不会让 `RunAsync` 抛出
- **重入折叠与终局关闸**: 订阅者在广播里回写上下文(如 UI 反向驱动)不会递归——一次外部写入最多补一次聚合,**递归深度恒为 2**;终局广播与终局事件期间关闸,写入不再触发聚合,故**终局事件之后不会再收到进度广播**。折叠刻意不写成 while 排空:订阅者若在每次广播都回写,while 会死循环而不管它会栈溢出,宁可少广播一次
- **终局从拉取面读**: `RunAsync` 在失败/取消时也「正常返回」,分不出结果——`IPipeline.Status`(`Idle`/`Running`/`Completed`/`Failed`/`Cancelled`)与 `FailureReason` 在运行结束后**保持终局值**(仅 `Destroy` 回落 `Idle`),且**落位先于终局事件**(订阅者在回调里即可读到)。`Bootstrap.RunAsync` 即据此把失败/取消转成异常。`FailureReason` 的格式为 `Failed: {最具体的名字}: {描述}`——容器取**首失败子阶段**的名字,直接挂到管线的阶段取自身名字(它上报过任务名时用任务名),因此调用方从拉取面或异常即可定位到失败阶段

### 容器组合

管线顶层固定串行(隐式串行根容器,不提供顶层并行 API);组内并行经 `ParallelStage`、组内串行经 `SequenceStage` 表达,两容器可任意嵌套——任何阶段集合的串并行组合都是一棵「串行容器 / 并行容器」树:

- **顶层并行** = 将并行段包为 `ParallelStage` 作为单阶段添加(见「快速使用」)
- **组内串行子段** = `SequenceStage` 包一层,如 `ParallelStage(SequenceStage(A, B), C)` 表达「组内先 A 后 B、与 C 并行」
- **子段切换进度回落属预期**: 未开始子阶段(Pending)不占进度,与管线「阶段切换回落」一致(等权子段完成瞬间 1.0 → 下一子段启动 0.5)

### 进度模型

- **加权聚合**: 全局进度 = `Σ(w·p) / Σ(w)`——已完成阶段记 w、执行中记 w·p;失败阶段权重移出;`Weight = 0` 的阶段不占进度(如瞬时阶段)。阶段进度经 `SetProgress` 写入时自动 clamp 0~1,**NaN 归一为 0**;`Weight` 须为有限非负数——非有限值一旦进入加权和,全局进度会变 NaN 或静默钉死,且脏判定的差值比较对 NaN 恒为 false,会把阈值节流一并废掉
- **阈值节流**: 全局进度变化 ≥1%,或任一阶段的**描述 / 任务名** / 状态变化才广播;终局必广播
- **阶段切换时进度回落属预期**(新阶段从 0 开始);装配 `Weight = 0` 的瞬时阶段可平滑过渡
- **描述占位**: 运行中未写描述时广播/转发的 `Description` 为空串占位(无描述即无文案,不伪造终局文案);完成终局恒为 `"Completed"`;失败/取消终局可能为空串(诊断经 `OnFailed` 原因与失败阶段上下文)

> **为什么广播载荷是类,而不是框架惯例里的 readonly struct?** `Broadcast` 每次广播新分配一个 `PipelineProgress`,看似违背「进度载荷用 readonly struct」的惯例(`AssetInitReport` / `SaveReport`)——那两者是模块在热点路径上**自建自用**的上报结构,而 `PipelineProgress` 是**交出去**的事件载荷:订阅者可以留存它(`Bootstrap` 就直接转交给调用方的 `IProgress<PipelineProgress>`),池化需要一个并不存在的归还协议;改成 struct 则经接口传递要装箱,还会改掉 `Report` 的就地覆盖语义与 `Bootstrap.RunAsync` 的公开签名。分配量本身也受阈值节流约束(≥1% 或描述/任务名/状态变化才广播),不在每帧路径上。此条写给下一次复审,免得再被当成漏网的 GC 问题。

## 自定义实现

本模块的实现方入口：实现阶段并装配进管线。

- **实现哪个契约**：`IPipelineStage`（`Name` / `Weight` / `ExecuteAsync`）；需要按相位分组时实现 `IPhaseStage`（多一个 `Phase`）。
- **怎么接线**：`Pipeline.Create()` + `AddStage`（或 `BuildPhaseGroups` 装配相位组）；容器（`ParallelStage` / `SequenceStage`）可嵌套。
- **要守什么**：执行模型（串行逐 await、失败 / 取消即停、三路互斥终局）见「行为契约」；取消须经 `OperationCanceledException` 传播、不得吞；从后台线程回来必须先 `SwitchToMainThread` 再写 `PipelineStageContext`（见「线程契约」）；自定义相位号请避开框架保留的 0–89（见「内置相位约定」与「已知限制」）。

## 内部机制

### 并行/串行容器语义（组内细节）

- **并行组内事件驱动聚合**: 子阶段写入即触发组内加权聚合(门铃 + 阈值节流 ≥1% 或描述/任务名/状态变化),收敛后转发组主上下文(组内均未写描述时转发空串占位,不伪造终局文案)——**一次子阶段写入恰好 1 次组级聚合**。任务名由组内「最近一次写入的子阶段」产出(未上报则回落该子阶段名),故组内**子阶段交替上报时切换本身即脏**——那正是「当前在跑谁」变了
- **取消**: 子阶段收到已取消的 token;组沉降后若 token 已取消或任一子阶段以取消结束,组上抛 `OperationCanceledException` 走管线取消路径(契约兜底不会把已取消的组误补为 Completed)
- **失败即停**: 组内任一子阶段失败(抛异常或主动 `SetState(Failed)`)→ 立即取消其余兄弟,沉降(WhenAll)后组置 Failed;日志 `[Pipeline] Parallel stage failed: {任务名} ({耗时}s): {描述}`(组级失败标识,与 `[Pipeline] Pipeline failed` 编排级失败区分)
- **诊断优先**: 失败时描述/任务名成对取组内**首个失败**的子阶段值,避免被兄弟描述覆盖(首失败未写描述时描述为空,但任务名仍是它——两者同源,不各取一处)
- **取消语义由阶段负责**: 需真正可中断的阶段必须把 `CancellationToken` 传入内层异步服务并禁止吞掉 `OperationCanceledException`;同步瞬时阶段无取消窗口属正常

## 线程契约

**写入必须与 `RunAsync` 调度同一上下文（Unity 主线程）。** 阶段经 `PipelineStageContext` 写入进度 / 描述 / 状态时，会**同步**触发聚合与订阅者回调，整条链非线程安全——跨线程写入不是「晚一点生效」，而是与派发竞争。Editor 下越线程写入打 `[Pipeline]` 前缀的 LogError 提示，**Release 构建整段不编译、零开销**（因此 Release 里没有任何保护）。

从后台线程完成工作后要写上下文时，先 `await UniTask.SwitchToMainThread(cancellationToken)` 再写——这也是 `IBootstrapStage` 实现者的既有契约（见 [Bootstrap 模块](../Bootstrap/README.md) 的四条）。

## 内置相位约定

`Phase` 的取值由使用方自行约定，Pipeline 本身不预设任何值。框架**引导阶段**（XBootstrap 模块）的相位号已公开为 `BootstrapPhases` 常量（Asset=0 / Input=1 / UI=2 / Data=3 / Save=4 / Localization=90 / UserStart=90），其中 **0–89 由框架保留**；约定细节与「怎么插进框架阶段之间」见 Bootstrap 模块 README——那里是这份约定的唯一真相。

## 设计原则

- **声明式阶段** — 阶段实现 `IPipelineStage` 声明执行内容,无需关心调度逻辑
- **事件驱动进度** — 阶段写入即聚合,管线不轮询、无每帧开销
- **失败/取消即停** — 异常→Failed、OCE→取消,后续阶段不再执行,终局三路互斥
- **阈值节流广播** — 变化 ≥1% 或状态/描述变化才广播,避免垃圾推送
- **实例即用即弃** — `Pipeline.Create()` 工厂创建,非全局单例
- **单一执行契约** — 阶段是唯一执行单元,无第二套任务/状态机(状态/进度写面统一在 `PipelineStageContext`)
- **声明式相位** — 阶段实现 `IPhaseStage` 声明相位号,无需关心分组调度逻辑
- **相位分组调度** — 相同相位并行、不同相位串行,兼顾性能与依赖顺序
- **契约兜底** — 阶段正常返回但未写终态时自动视为完成(进度 1f),不会阻塞调度
- **加权聚合** — 组内按 `Weight` 加权聚合 `Σ(w·p)/Σ(w)`;失败阶段不计入进度(进度略回退属预期)
- **串并行嵌套组合** — 顶层固定串行,组内并行/串行经 `ParallelStage`/`SequenceStage` 容器嵌套表达任意调度拓扑

## 已知限制

知道边界比以为没有边界安全。以下都是刻意的取舍或跨模块的能力缺口，**不是待修的缺陷**：

- **`RunAsync` 在失败 / 取消时也「正常返回」。** 返回值分不出结果，必须读拉取面 `Status` / `FailureReason`（它们在运行结束后保持终局值）——只 `await RunAsync` 会把失败当成功，这是本模块的头号坑。`Bootstrap.RunAsync` 正是据此把失败与取消转成异常的。
- **契约兜底会把「忘了写终态」判成完成。** 阶段正常返回但没调 `SetState` 时自动视为完成（进度 1f），静默、不告警。
- **顶层只有串行。** 没有顶层并行 API：要并行必须把并行段包成 `ParallelStage` 当作单阶段添加，容器可任意嵌套表达拓扑。
- **运行期装配被静默忽略。** 运行中重复 `RunAsync`、或运行中 `AddStage`，都只打一条 `[Pipeline]` 警告后忽略——阶段列表在运行期只读，启动时刻已快照，入列会错位。
- **超时放弃的挂起阶段，其后续上下文写入被忽略。** 不响应取消的阶段不会阻塞管线，但它**没有被真正中止**：副作用可能仍在继续，只是写入不再进来。超时中止的是编排，不是那个阶段。
- **进度广播受阈值节流，终局之后不再有广播。** 全局进度变化 ≥1%，或描述 / 任务名 / 状态变化才广播；阶段切换时进度回落属预期；终局事件之后写入不再触发聚合（那是重入折叠的刻意取舍，不是漏广播）。
- **`Weight` 必须是有限非负数。** 非有限值一旦进入加权和，全局进度会变 NaN 或静默钉死，还会把阈值节流一并废掉（脏判定的差值比较对 NaN 恒为 false）。
- **`PipelineProgress` 是类，每次广播新分配。** 这是刻意的——它是**交出去**的事件载荷（订阅者可留存、`Bootstrap` 直接转交给调用方），理由见「进度模型」下的注；但它确实是本模块唯一的分配点，分配量受阈值节流约束。
- **相位号 0–89 由框架保留。** 自定义相位阶段的 `Phase` 请避开这一段（框架引导阶段已占 0 / 1 / 2 / 3 / 4 / 90），否则组间顺序会与预期不符。约定见「内置相位约定」。
- **取消的生效范围由阶段决定。** 同步瞬时阶段没有取消窗口；要真正可中断，阶段必须把 `CancellationToken` 传进内层异步服务，且不得吞掉 `OperationCanceledException`。
- **写入须与 `RunAsync` 同线程。** 阶段经 `PipelineStageContext` 写入会同步触发聚合与订阅者回调，整条链非线程安全；Editor 下越线程写入打 LogError（Release 零开销）。

## 依赖

- `UniTask`(框架层已提供)
