# Message —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Message/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Message/
├── IMessageBroker.cs             # IMessagePublisher/IMessageSubscriber(公开)+ IMessageBroker(internal)
├── MessageBroker.cs              # 消息代理内部实现(订阅直落事件流)
├── MessageManager.cs             # 静态外观(全局入口) + 标记接口扩展方法
├── IMessageFilter.cs             # 消息过滤器接口
├── IDestroyCancellationToken.cs  # 销毁令牌契约(订阅自动退订的绑定对象)
├── MessagePublishStrategy.cs     # PublishAsync 等待异步处理器时的调度策略
├── MessageBusStats.cs            # 总线只读统计快照(readonly struct,诊断订阅泄漏/缓冲驻留)
├── MessageChannelStats.cs        # 单通道只读统计快照(通道不存在时各字段为 0 / false)
├── MessageTypeStats.cs           # 单类型通道统计(类型级通道 + 全部键值通道的合计)
└── Internal/                     # 总线内部实现(internal)
    ├── MessageChannel.cs         # 通道对象(聚合同步流与缓冲流)+ 键值通道存储
    ├── AsyncSubscription.cs      # 异步订阅登记项(退订句柄 + 令牌生命周期)
    ├── DispatchListPool.cs       # 派发快照 List 池(与 XEvent 的同名池是两份,不合并)
    ├── MainThreadGuard.cs        # 发布/订阅入口的主线程断言(仅 Editor)
    └── ActionDisposable.cs       # 委托式 IDisposable(幂等)
```

## 已评估未采纳与未决

> 本模块此前没有这一节（文件结构之外无待办）。2026-10-11 框架级评估产生了第一条，故按需建立。

**测试侧的处置（跨模块，真相在别处）**：`MessageStatsTests` 的零分配断言此前用的是**恒真**的尺子
（`GC.GetAllocatedBytesForCurrentThread()` 在本环境恒返回 0）。**2026-10-12 已处置**：

- **结构证据**（`CopyTypeStats_StructuralEvidence_NoBoxing`）：`MessageTypeStats` 是值类型且除
  `System.Type`（指向既有类型对象）外无引用类型字段；`MessageStatsAccumulator` 是值类型且
  `IMessageChannel.Accumulate` 按 `ref` 收它；`MessageBroker._channels` / `_keyedChannels`
  **声明为具体泛型字典而不是接口**。
- **放大粗尺子**：10 万次反复复制，实测堆增量 **0 B**。
- **红证意外地互为印证**：把 `_channels` 临时改回 `IDictionary<,>` 声明，结构断言立刻红，
  **且尺子同时读到 2,334,720 B（≈23 B/迭代的装箱枚举器）**——这条结构约束不是洁癖，
  它守的是真实存在的分配。注入式（`new object()`）红证另读到 1,007,616 B。

校准与改造模板见 `Documentation/Workflow.md` §六。
