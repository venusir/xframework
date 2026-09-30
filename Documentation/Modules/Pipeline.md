# Pipeline —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Pipeline/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

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
