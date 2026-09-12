# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Asset 低内存自动回收**：`AssetInitOptions.AutoReclaimOnLowMemory`（默认 true）监听 `Application.lowMemory`，自动清池并卸载全部包中未使用资源
- **Asset 批量加载**：`AssetManager.LoadAllAsync<T>` 按序返回句柄数组，单项失败为 default 句柄，取消时自动释放已完成项
- **异步发布 `PublishAsync`**：同步投递先行的基础上等待全部异步处理器完成，`MessagePublishStrategy` 可选 Parallel（默认）/ Sequential；`Publish` 仍以 fire-and-forget 触发异步订阅
- **缓冲通道淘汰**：`EvictBufferedChannel`（类型级 / 按键）、`EvictBufferedChannels`、`TrimEmptyChannels`；订阅清零的通道自动回收，缓冲通道需显式淘汰（保留「订阅前发布可重放」语义）
- **按 Key 跨消息类型淘汰**：`EvictBufferedChannels<TKey>(key)` 一次淘汰该 Key 在**所有**消息类型下的缓冲通道。此前淘汰只能按消息类型进行，实体销毁处要为其参与的每种带 Key 消息各写一次调用，且每新增一种消息都得回去补一处——漏掉的那处即「复用同 Id 的新实体重放到旧值」的静默事故。新 API 与实体的生命周期同形，README「内存管理」的推荐写法已同步改为按 Key 淘汰
- **运行统计**：`MessageManager.GetStats()` / `GetChannelStats<T>()` 返回 `readonly struct` 快照（通道数、订阅数、缓冲通道数、发布/请求次数等），零分配，用于排查订阅泄漏与缓冲内存驻留
- **过滤器管理**：`RemoveFilter` / `ClearFilters`
- **请求处理器注销**：`Unregister<TRequest, TResponse>`
- **请求-响应补全**：`HasHandler<TRequest>()` 探测响应方是否已注册；`TryRequestAsync<TRequest, TResponse>` 在一次调用内完成「查 + 发」，未注册处理器时返回 `(false, default)` 而不抛异常。模块定位是跨模块解耦，请求方本就不该假定响应方已就绪，此前只能靠 try/catch 接 `InvalidOperationException`——既是用异常做控制流，也无法与处理器内部抛出的同类型异常区分。`false` 只表示未注册，处理器自身的异常照常上抛
- **节点订阅扩展**：`NodeExtensions.SubscribeAsync(this BaseNode, ...)`；`Subscribe` 去掉 `where TMessage : class` 约束，框架内置 struct 消息现可用
- **节点缓冲订阅**：`NodeExtensions.SubscribeBuffered<TMessage>(this BaseNode, handler)`，订阅即收到最近一条并自动绑定节点生命周期。刻意只提供这一个缓冲重载——带 Key 与带过滤条件的变体一旦同时存在，`(TKey, Action<TMessage>)` 会捕获本意为过滤器的委托实参，使该调用退化为 CS0121 或被静默解析为「把谓词当 Key」

### Changed

- **Asset 并发初始化修复**：`InitializeAsync` 并发调用共享同一初始化任务（门面 + 实现层），`Destroy()`/`SetInstance()` 使在途初始化结果作废
- **未初始化异常消息统一**：各模块 guard 消息改为中文 + `[模块]` 前缀 + 修复提示；`DataException` 改为继承 `InvalidOperationException`
- **Message 异步订阅签名变更（破坏性）**：`SubscribeAsync` 处理器由 `Func<TMessage, UniTask>` 改为 `Func<TMessage, CancellationToken, UniTask>`；处理器收到的令牌即订阅自身令牌，退订会取消在途 await
- **Message 请求处理器签名变更（破坏性）**：`Register` 处理器由 `Func<TRequest, UniTask<TResponse>>` 改为 `Func<TRequest, CancellationToken, UniTask<TResponse>>`；`RequestAsync` 补可选 `CancellationToken`（令牌语义见下条）
- **Message `RequestAsync` 取消语义变更（破坏性）**：调用方令牌此前只原样转发给处理器、**不**中断本次等待——处理器若不响应令牌，调用方将永久挂起。现对齐 `PublishAsync`：令牌一方面仍原样转发给处理器，另一方面用于取消本次等待，取消时抛 `OperationCanceledException` 但不中断已启动的处理器。破坏性在于「传入已取消的令牌仍能正常拿到响应」这一写法不再成立，对应的既有测试已改写为用令牌同一性验证转发，并新增「在途取消」用例锁定新语义
- **Message 异步处理器执行时机变更**：异步订阅不再与同步订阅者同链交错，改为排在同步投递之后独立派发
- **Message 缓冲通道结构**：`MessageBroker` 的 6 个业务字典合并为「通道对象」两表（`MessageChannel<TMessage>` + `KeyedChannelStore<TKey,TMessage>`），键值键改由 `(消息类型, Key 类型)` 复合而成
- **Message 淘汰即回收**：`EvictBufferedChannel` / `EvictBufferedChannels` 丢弃重放缓存后顺带回收因此变空的通道与存储表项，不再残留「已可回收但仍在表中」的空壳；`TrimEmptyChannels` 退居兜底与诊断手段，常规路径下返回 0。返回值语义不变（只计淘汰数，不含回收数）。同时补文档：带 Key 的淘汰是语义要求而非可选优化——漏淘汰会把上一个同 Id 实体的旧值重放给新订阅者
- **节点订阅解析变更**：`BaseNode` 实现 `IMessagePublisher`/`IMessageSubscriber`，`NodeExtensions.Subscribe` 改以 `BaseNode` 为接收者以消除与 `MessageManager.Subscribe(this IMessageSubscriber, ...)` 的重载二义
- **Message 接口可见性收敛**：`IMessageBroker` 由 `public` 改为 `internal`——它只有一个 `Clear()` 成员，实现类 `MessageBroker` 与实例替换点 `MessageManager.SetInstance` 均为 internal，对外既无法实现也无处注入，公开它只会得到一个不可用的契约。`IMessagePublisher`/`IMessageSubscriber` 保持公开（它们是扩展方法的接收者类型，`BaseNode` 实现了二者）。外部代码本就无法取得 `IMessageBroker` 实例，实际无可观察影响
- **Reactive 消息总线内部去 R3 化结构**：订阅直落自研事件流，移除内部 ObservableSignal 包装层与信号缓存；公共 API 与行为语义不变（投递顺序、缓冲保留、completed 语义保持）
- **Reactive 清理**：裁撤 Signal/ISignal/IReadonlySignal 死代码（公开类型名中 Signal 术语退场），ReactiveProperty 接入自足 `IReactiveProperty<T>` 接口链；注释与文档同步去 R3 叙述
- **模块拆分**：消息总线与事件流引擎迁入新 `Runtime/Message/`（命名空间 `XFramework.XMessage`），Reactive 仅保留响应式属性；公共类型名不变，使用方仅需改 using（编译期破坏性变更）
- **引擎去 Rx 命名**：Subject→EventStream、ReplaySubject→BufferedEventStream、AnonymousDisposable→ActionDisposable；Unit 删除（InputManager 帧脉冲改发 `Time.frameCount`）；事件引擎日志前缀定稿 `[Message]`

### Fixed

- **Reactive 带过滤订阅的过滤条件抛异常不再击穿派发**：记 Error 日志、该订阅本条不收、订阅保留、其余订阅者照常收到（含缓冲重放路径）
- **BufferedEventStream 方法隐藏改为 override**：此前经 `IDisposable` 接口释放会落到基类槽位，缓存不清空，已释放的流仍向新订阅者重放陈旧值；同时修复「已完成（`OnCompleted`）后再 `OnNext` 会写回缓存并重放」的 completed 语义违背
- **键值通道的 Key 类型隔离**：同一消息类型配不同 Key 类型（如 `Publish("k", 1)` 与 `Publish(1, 1)`）此前会强转到先创建者并抛 `InvalidCastException`
- **缓冲通道内存无界增长**：新增显式淘汰 API；`EvictBufferedChannels<T>` 按消息类型跨全部 Key 类型淘汰
- **订阅回池的重复归还**：淘汰缓冲流后陈旧句柄的 `Dispose` 会二次回池，同一节点被发放两次将形成 `node.Next` 自环并令派发快照死循环
- **`SubscribeAsync` 已取消令牌留下空通道**：四个重载此前先建通道再判令牌（实参先于被调方法求值），令牌已取消时通道（键值版连整条存储表项）被建出却没有订阅者来触发回收，只剩 `TrimEmptyChannels` 兜底；现将令牌守卫置于建通道之前，判空仍先于判令牌（保证 `null` 处理器照常抛 `ArgumentNullException`）
- **异步订阅令牌竞态下通道永久不可回收**：`AsyncSubscription` 构造期间向已取消令牌 `Register` 会同步内联触发退订，而该项尚未入表，退订落空、不回调空通知；此时若仍入表，登记表永远非空使 `IsReclaimable` 恒为 false，自动回收与 `TrimEmptyChannels` 共用该谓词而双双失效，只能等 `Clear()`。现改为已退订的登记项不入表（该窗口仅在跨线程 `Cancel` 时可达，属防御性加固）
- **节点消息订阅补全**：`NodeExtensions.Subscribe` 的 `where TMessage : class` 约束使框架内置 struct 消息全部编译不过，且与 `MessageManager.Subscribe` 同形导致重载二义；`BaseNode` 实现消息标记接口后二者同时解决
- **订阅与过滤器 API 判空**：此前 `null` 过滤器要到派发时才 NRE 并被 `try/catch` 吞成日志（静默失效且订阅保留）
- **异步与键值重载的二义**：键值 `PublishAsync` 的 `strategy` 取消默认值，避免两参调用 `(消息, 策略)` 与 `(Key, 消息)` 无法裁决
- **文档修正**：UI README 引用了不存在的 `MessageManager.Unsubscribe`；`IMessageSubscriber` 扩展文档夸大了生命周期绑定范围（实际仅 `MonoBehaviour`）；`Documentation/XFramework.md` 的 Node README 链接漂移

## [0.2.0] - 2026-08-20

### 移除 R3 依赖（重大变更）

- **响应式引擎自研化**：移除 R3 NuGet 依赖与 NuGetForUnity，新增零依赖自研响应式引擎（`XFramework.XReactive.Internal`：Subject/ReplaySubject/AnonymousDisposable/Unit）
- **公共 API 不变**：MessageManager、ReactiveProperty、ReadOnlyReactiveProperty、ISignal、InputManager.ObserveXxx、SettingsManager.Observe/ObserveField、UIBinder 签名与行为语义保持不变（订阅立即回调、相同值去重、异常隔离）
- **修复既有缺陷**：MessageBroker 缓冲通道订阅前发布的消息不再丢失；消息过滤器拦截现在真正生效
- **清理**：删除 packages.config、NuGet DLL、残留 R3 csproj、探针测试；XFrameworkDependencyInstaller 仅保留 UPM 依赖安装
- **第三方集成简化**：安装依赖仅需 UniTask + YooAsset 两个 UPM 包

## [0.1.0] - 2026-01-13

### This is the first release of *\<XFramework\>*.

*Short description of this release*
