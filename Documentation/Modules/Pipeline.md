# Pipeline —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Pipeline/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## PlayMode 侧覆盖（2026-10-12）

本模块此前的 89 条用例全在 `Tests/Editor/`。新增 `Tests/Runtime/Pipeline/PipelineFrameTests`
（3 例）+ `FrameStage`（逐帧 `await UniTask.Yield()` 的假阶段，也可以选择永不完成），补的正是
**EditMode 结构上测不到**的两件事：

- **真跨帧**：阶段内的 `await UniTask.Yield()` 在 EditMode 里不让出到下一帧，「跨帧挂起」这个形态不存在。
  覆盖「跨帧期间 `IsRunning` 保持为真 → 完成后 Completed」「跨帧期间外部取消 → Cancelled 且后续阶段零执行」。
- **生产的那条真实墙钟超时**：超时默认走 `UniTask.Delay(…, DelayType.Realtime)`，而 EditMode 用例**必须**
  注入 `TimeoutTaskFactory` 假计时器（其注释自述「真实延时由 PlayerLoop 泵送，永不触发」）——
  于是「生产路径本身」此前从未被跑到。新用例不注入工厂，直接断言 0.05s 后 `Status == Failed`、
  `FailureReason` 含 `timed out after 0.05s`、后续阶段零执行。
  失败的假阶段**故意不挂令牌**，为的是顺带钉住「不响应取消的挂死阶段不得阻塞管线（在途任务被放弃）」。

## 异常承载（2026-10-01）

阶段抛出的异常对象沿 `StageExecution` → `PipelineStageContext.FailureException`（internal）→
`ContextAggregation.ScanResult.FailException`（与描述/任务名在**同一首失败闩锁**里成对捕获）→
`StageAggregator` → 容器拷进自己的 `_stageCtx` → 管线终局日志（`LogManager.Exception`）这条链走到
JSONL 的 `exc` 字段。三条设计约束值得记住：**只在编排级终局行挂**（容器那行与它同源，两处都挂会让
同一个堆栈出现两遍）；**没有异常的失败路径保持原样**（超时、阶段主动 `SetState(Failed)`；
`Exception(cat, null, msg)` 是「什么都不做」，不能用它兜）；**赋值顺序是「异常 → 描述 → 状态」**
（`SetState` 同步触发聚合，聚合要读到这两样）。

**失败原因里的阶段名**（2026-10-02 补）：`FailureReason` 的格式为 `Failed: {名字}: {描述}`，名字取
`ctx.CurrentTaskName ?? ctx.Name`——与容器日志同一「最具体的名字」口径（容器是聚合器写入的首失败子阶段名，
直接挂到管线的阶段没上报过任务名则回落自身 `Name`）。**日志行文案没有跟着改**（名字已在上一行
`Stage 'X' failed`），两处形状不一致是有意的：改日志会牵动 `PipelineFailureTests` 的精确正则，
而缺名字的是**拉取面**（`Bootstrap` 据它抛出异常）。

## 文件结构

```
Runtime/Pipeline/
├── IPipeline.cs                  # 管线接口(调度入口)
├── IPipelineStage.cs             # 阶段接口(含 PipelineStageState 枚举)
├── IPhaseStage.cs                # 相位阶段接口(IPipelineStage + Phase 声明)
├── Pipeline.cs                   # 静态门面:创建实例 + 相位分组装配助手 BuildPhaseGroups
├── PipelineStageContext.cs       # 阶段执行上下文(阶段写面;读取面在 PipelineProgress / IPipeline.Status)
├── PipelineProgress.cs           # 全局进度快照(事件载荷)
├── StageExecution.cs             # 阶段执行共享包装(契约兜底/取消/异常捕获,internal)
├── ParallelStage.cs              # 并行阶段(组内并行、事件驱动组内聚合,public)
├── SequenceStage.cs              # 串行阶段(组内串行子段,public)
├── StageAggregator.cs            # 容器子阶段共享聚合器(门铃 + 加权聚合,internal)
├── ContextBell.cs                # 上下文写入门铃(重入折叠/迟写防护,internal,管线与容器共用)
└── ContextAggregation.cs         # 加权扫描共享助手(加权扫描/阈值节流/状态快照,internal)
```

## 决策台账

（按 `Modules/README.md` 的约定**逐轮追加、不覆盖**。下面是从既有设计决定与变更记录（模块 README / CHANGELOG / 当时的 `Roadmap.md`，今 `Ledger.md`）里归集的第一批，出处逐条标注；整轮审计（`../ModuleAudit.md` 的 A–F）的结论会在做过后追加到本节。逐条理由的展开见使用方 README 的相关章节，此处只留结论这一层。）

- 不做：**不加顶层并行 API**（`RunParallelAsync` 式）（2026-08-31 · 模块 README「容器组合」，`3ffb5ce`）——顶层固定串行是「隐式串行根容器」，并行段包成 `ParallelStage` 作为**单个**阶段添加即可表达同一拓扑——多一个入口就多一套「阶段列表怎么排」的语义要维护，而容器嵌套已能表达任意串并行树（`3ffb5ce` 起「任何阶段集合的串并行组合均可用容器树表达」）。无重开判据。
- 不做：**`PipelineProgress` 不改成 `readonly struct`**（2026-09-27 · `5d8dd15`）——它确实违背「进度载荷用 readonly struct」的框架惯例，但它是**交出去**的事件载荷——订阅者可留存它、`Bootstrap` 直接转交给调用方的 `IProgress<PipelineProgress>`，池化需要一个并不存在的归还协议；改成 struct 则经接口传递要装箱，还会改掉 `Report` 的就地覆盖语义与 `Bootstrap.RunAsync` 的公开签名。**这条写给下一次复审**，免得被当成漏网的 GC 问题（模块 README「进度模型」节留了同样一句）。无重开判据。
- 不做：**重入折叠不写成 `while` 排空**（2026-09-27 · `15e6ea1`，模块 README「执行模型」）——订阅者若在每次广播里都回写上下文，`while` 会死循环（而不管它会栈溢出）——**宁可少广播一次**，故递归深度恒为 2。无重开判据。
- 不做：**失败原因的日志行文案不跟 `FailureReason` 一起改**（2026-10-02 · `dcee909`，见本文「异常承载」）——名字已在上一行 `Stage 'X' failed` 里，改日志会牵动 `PipelineFailureTests` 的精确正则；缺名字的是**拉取面**（`Bootstrap` 据它抛出异常）。两处形状不一致是有意的。无重开判据。
- 不做：**超时取「乐观」语义，不等待被放弃的阶段沉降**（2026-08-31 · `a03e864`）——触发即取消当前阶段并置 `Failed`，不响应取消的挂起阶段在后台继续跑、只是其后续上下文写入不再影响管线（与 Polly 的 `TimeoutStrategy.Optimistic` 同语义）。写成「等它沉降」等于让一个不响应取消的阶段把编排挂死——超时要中止的是**编排**，不是那个阶段。无重开判据。
- 已做：**主线程断言三份副本——不抽公共防线**（2026-10-15 · `ac9a9da`；判据「共享的那部分是不是有难度的那部分」，见 `Ledger.md` 的同名条）——本模块与 Message / Asset 三份**各自保留**：它们的共同部分只有三行，而断言绑在哪个语义位置、文案与闩锁寿命逐模块不同（本模块绑在「越线程**写入**」这个位置、按次运行重报）。
- 已做：**容器元素 null 校验**（2026-10-20 · R3-PIPE-001，本轮修复）——`ParallelStage`/`SequenceStage` 构造只校验列表 null/空，元素 null 在读 `Weight` 时抛裸 NRE；改为与 `AddStage(null)` 同形抛 `ArgumentNullException`。回归两例。
- 已做：**非有限超时与取消路径沉降**（2026-10-20 · R3-PIPE-002/003，本轮修复）——`+∞` 超时会让 `TimeSpan.FromSeconds` 溢出；容器取消路径 `throw OCE` 跳过 `_aggregator.Settle()`。修法：入口把 ±∞/NaN 归一为「不启用」；`Settle` 移入 `finally`。回归 `PositiveInfinityTimeout_IsTreatedAsDisabled`。
- 已做：**沉降后解除 Owner 引用**（2026-10-20 · R3-PIPE-004，本轮修复）——容器 `_stageCtx.Owner` 指向上一轮 `PipelineImpl`，长寿命容器跨管线复用时钉住管线与订阅者闭包。修法：`finally` 里把 `Owner` 置 null，上下文本身保留给诊断/测试读取；回归两容器各一例。
