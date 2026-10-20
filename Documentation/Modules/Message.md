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

## 决策台账

- 已做：**投递期间禁止通道回收**（2026-10-17 · `c3e16c9`，MSG-001）——`MessageChannel` 加投递深度闩锁，`IsReclaimable` 在派发期间恒 false；四条发布路径共用 `EmitSyncAndBuffer`。首条 `Publish` 的同步订阅清零不再让通道在缓冲写入前被摘除。

> 本节此前不存在（文件结构之外无待办）；2026-10-11 框架级评估产生了第一条，故按需建立。

- 已做：**`MessageStatsTests` 的零分配断言从恒真尺子改为「结构断言 + 放大粗尺子」**（2026-10-12 · 用例 `CopyTypeStats_StructuralEvidence_NoBoxing`；10 万次反复复制，实测堆增量 **0 B**）——原尺子 `GC.GetAllocatedBytesForCurrentThread()` 在本环境恒返回 0。结构证据：`MessageTypeStats` 是值类型且除 `System.Type`（指向既有类型对象）外无引用类型字段；`MessageStatsAccumulator` 是值类型且 `IMessageChannel.Accumulate` 按 `ref` 收它；`MessageBroker._channels` / `_keyedChannels` **声明为具体泛型字典而不是接口**。红证意外地互为印证：把 `_channels` 临时改回 `IDictionary<,>` 声明，结构断言立刻红，**且尺子同时读到 2,334,720 B（≈23 B/迭代的装箱枚举器）**——这条结构约束不是洁癖，它守的是真实存在的分配；注入式（`new object()`）红证另读到 1,007,616 B。校准与改造模板见 `Documentation/Workflow.md` §六（该处置是跨模块的，真相在那里）。
- 已做：**`IDestroyCancellationToken` 实现范例在销毁后抛 `ObjectDisposedException`**（2026-10-17 · 第二轮审计；自 `Localization.md` 迁入，MSG-002）——XML 示例 `=> _cts.Token` 配 `Dispose()` 里 `_cts.Dispose()`，照抄会在对象销毁后再订阅时抛 ODE；2026-10-17 `4e8645b` 修 ODE 半边（构造时缓存令牌），2026-10-19 `04b2f33` 补 `IMessageSubscriber` 半边（原示例照抄 CS1929，R3-MSG-003）。

- 待裁：**是否向 Event 要「只清缓存、保留订阅」的原语**（2026-10-19 · R3-MSG-002，由 MSG-003 的 README 指针落账）——当前 `EvictBuffered*` 直接 `Dispose` 整条缓冲流；「只清重放缓存、保留活订阅」需要 Event 侧新原语。是否值得扩 Event 面未定。重开判据：用户裁定要扩 Event 面，或出现需要「清空重放缓存、保留活订阅」的真实使用方需求。
- 已做：**`TryBindToDestroy` 拆分 None 令牌**（2026-10-20 · R3-MSG-004，本轮修复）——`!token.CanBeCanceled` 与 `IsCancellationRequested` 并成一枝，`IDestroyCancellationToken` 返回 `CancellationToken.None` 时订阅被立即释放、处理器永远收不到消息。修法：None 视为「无法绑定」并返回原句柄；回归 `DestroyTokenBindingTests.Subscribe_NoneDestroyToken_StillReceivesMessages`（改前 ReceivedCount 0）。

