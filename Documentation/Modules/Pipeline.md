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
├── StageExecution.cs             # 阶段执行共享包装(契约兜底/取消/异常捕获)
├── ParallelStage.cs              # 并行阶段(组内并行、事件驱动组内聚合,public)
├── SequenceStage.cs              # 串行阶段(组内串行子段,public)
├── StageAggregator.cs            # 容器子阶段共享聚合器(门铃 + 加权聚合,internal)
├── ContextBell.cs                # 上下文写入门铃(重入折叠/迟写防护,internal,管线与容器共用)
└── ContextAggregation.cs         # 加权扫描共享助手(加权扫描/阈值节流/状态快照,internal)
```
