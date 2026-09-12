# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **File 原子替换与一代备份**：改用 `File.Replace` 一步完成「替换正式文件 + 保留一代 `.bak`」，平台不支持时降级为三步移动。原实现以「删正式文件 → Move」实现替换，两步之间进程被杀即为「旧档已删、新档还在 `.tmp`」——从上层看是存档凭空消失且无从恢复。两条路径都维持「任意时刻至少一份完整副本」的不变式
- **File 目录枚举可选能力**：新增 `IDirectoryProvider.GetDirectoriesAsync` 与 `FileManager.GetDirectoriesAsync`。`IFileProvider` 只有非递归的 `GetFilesAsync`，上层无法发现「存在但当前没有文件的目录」；按 `IAtomicFileProvider` 的既有模式做成可选能力，不给 `IFileProvider` 增加成员，第三方 Provider 实现零影响。装饰器 `CryptoFileProvider` 同步透传——装饰器漏实现可选能力会把底层 Provider 的能力遮蔽掉
- **File 加密按域限定**：`SetCryptoProvider(provider, FileDomain?)`。原实现把整个 Provider 包成加密装饰器、即加密全部域，从存档侧接线会静默连带加密 AppData / Cache
- **Data 快照应用上报失败块数（破坏性）**：`IDataManager.ApplySnapshot` 由 `void` 改为返回未能恢复的数据块数量。此前单块恢复失败只记 warning 后吞掉、方法正常返回，调用方无从判断本次加载是否**完整**——「部分块未恢复」等价于内存里少了一半数据
- **Save 元数据侧车与校验和**：槽位旁新增 `slot_N.save.meta`（元数据 + FNV-1a 64 校验和），使枚举不必为拿元数据而读入并反序列化整个载荷。侧车缺失或不可解析时回退全量解析并重建；**校验和不符时刻意不重建**——那是「载荷可能已损坏」的信号，重建等于把它抹掉，应留给加载侧走备份回退。定位写明：只发现损坏，不提供防篡改
- **Save 加载结果枚举与备份回退**：新增 `SaveLoadStatus` / `SaveLoadResult` / `TryLoadAsync`，把「槽位不存在」「文件损坏」这类预期内失败以状态回报而非抛异常——存档界面需要区分这些情况，做成异常会迫使调用方用 try 表达正常分支。主文件不可用时自动回退到一代备份并回报 `LoadedFromBackup`
- **Save 快照版本门禁**：`SaveOptions.CurrentVersion` / `SetCurrentVersion`。保存时写入快照，加载时高于当前客户端则整份拒绝（`VersionTooNew`）、低于则正常加载并回报 `Migrated`。此前只有 Data 侧的按块门禁，而它是「跳过该块」——结果是新格式存档被半加载（一半新数据、一半空着），比干净地拒绝危险得多
- **Save 启动恢复扫描**：`SaveBootstrapNode` 初始化后扫描存档目录，用一代备份还原丢失的载荷、清理崩溃残留、重建缺失侧车
- **Save 槽位复制与移动**：`CopySlotAsync` / `MoveSlotAsync`，基于 provider 读写原语实现，所有存储后端与加密层都成立；目标侧车按目标槽位重建（照抄源侧车会让元数据把目标报成源槽位）
- **Save 进度上报**：多步操作（枚举槽位、批量删除）支持 `IProgress<SaveReport>`，按框架惯例以重载而非改签名新增
- **Save 加密接线**：`SaveOptions.CryptoProvider` 非空时只对 `FileDomain.SaveData` 域接线加密
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

- **Save 契约重整（破坏性）**：`ISaveManager` 全面收敛——异步成员统一 `Async` 后缀（此前同一文件里两种读法，`SaveAsync` 保留而 `GetSlotMetas`/`DeleteAllSlots` 丢弃）；玩家隔离 API（`SetCurrentPlayer` / `ClearCurrentPlayer` / `CurrentPlayerId` / `GetAllPlayerIdsAsync` / `DeletePlayerAsync`）由实现类内部成员提升到接口，消除门面里靠 `is` 降级访问、自定义实现静默空返回的问题；去掉同步成员（`DeleteSlot` / `SlotExists`）——同步 IO 在主线程执行，而 Console 等平台的 Provider 可能是数百毫秒的平台 SDK 调用
- **Save 存档 IO 后切回主线程**：Provider 的 IO 用 `RunOnThreadPool(configureAwait: false)` 完成时停留在子线程，此前 `SaveManagerImpl` 中所有 IO `await` 之后的代码——含 `DataManager.ApplySnapshot` 与第三方 `IDataBlock` 回调——都在线程池上执行，既是「Unity API 只能在主线程调用」违规，也是与主线程读写共享状态的真实竞态。代价：这些方法依赖 PlayerLoop 泵，**禁止在主线程同步阻塞等待**（`.GetAwaiter().GetResult()` 会死锁）
- **Save 序列化移出主线程**：载荷的序列化与反序列化改在池线程上执行（`CreateSnapshot` / `ApplySnapshot` 必须留主线程，它们要遍历并回调第三方数据块）。固有代价：目标类型的字段初始化器与构造器会在后台线程执行，第三方数据类若在其中访问 UnityEngine 对象会抛异常
- **Save 校验前移**：槽位号与 playerId 的校验集中到 `SavePathUtility` 并在入口快速失败。此前校验只在构造路径时生效——`SetCurrentPlayer("../../outside")` 会先污染玩家上下文、直到下一次保存才失败（那时快照已生成、脏标记已清空）；`SetCurrentPlayer(".")` 更能穿过路径沙箱把存档写进域根、绕开玩家隔离，而 `DeletePlayer(".")` 会删掉域根下全部存档
- **Save 写操作门禁**：`IsBusy` 改为 `Interlocked` 原子占位并覆盖全部写操作（保存/加载/删除/复制/移动）。此前删除类操作不受约束，可在「已写 `.tmp`、尚未替换」的窗口里把目标文件抽走；且普通 `bool` 字段跨线程无可见性保证。读操作刻意不进保护（替换是原子的，读只会看到完整的旧文件或新文件），该取舍已写入注释
- **Save 枚举结果确定化**：`GetSlotMetasAsync` 结果按槽位号升序排序（此前是文件系统顺序，跨平台不确定）；损坏槽位带 `SaveMeta.isCorrupted` 标记保留在列表中，不再从列表消失——消失会与 `SlotExistsAsync` 返回 true 自相矛盾，界面既看不到也删不掉它
- **Asset 并发初始化修复**：`InitializeAsync` 并发调用共享同一初始化任务（门面 + 实现层），`Destroy()`/`SetInstance()` 使在途初始化结果作废
- **未初始化异常消息统一**：各模块 guard 消息改为中文 + `[模块]` 前缀 + 修复提示；`DataException` 改为继承 `InvalidOperationException`
- **Message 异步订阅签名变更（破坏性）**：`SubscribeAsync` 处理器由 `Func<TMessage, UniTask>` 改为 `Func<TMessage, CancellationToken, UniTask>`；处理器收到的令牌即订阅自身令牌，退订会取消在途 await
- **Message 请求处理器签名变更（破坏性）**：`Register` 处理器由 `Func<TRequest, UniTask<TResponse>>` 改为 `Func<TRequest, CancellationToken, UniTask<TResponse>>`；`RequestAsync` 补可选 `CancellationToken`（令牌语义见下条）
- **Message `RequestAsync` 取消语义变更（破坏性）**：调用方令牌此前只原样转发给处理器、**不**中断本次等待——处理器若不响应令牌，调用方将永久挂起。现对齐 `PublishAsync`：令牌一方面仍原样转发给处理器，另一方面用于取消本次等待，取消时抛 `OperationCanceledException` 但不中断已启动的处理器。破坏性在于「传入已取消的令牌仍能正常拿到响应」这一写法不再成立，对应的既有测试已改写为用令牌同一性验证转发，并新增「在途取消」用例锁定新语义
- **Message 异步处理器执行时机变更**：异步订阅不再与同步订阅者同链交错，改为排在同步投递之后独立派发
- **Message 缓冲通道结构**：`MessageBroker` 的 6 个业务字典合并为「通道对象」两表（`MessageChannel<TMessage>` + `KeyedChannelStore<TKey,TMessage>`），键值键改由 `(消息类型, Key 类型)` 复合而成
- **Message 淘汰即回收**：`EvictBufferedChannel` / `EvictBufferedChannels` 丢弃重放缓存后顺带回收因此变空的通道与存储表项，不再残留「已可回收但仍在表中」的空壳；`TrimEmptyChannels` 退居兜底与诊断手段，常规路径下返回 0。返回值语义不变（只计淘汰数，不含回收数）。同时补文档：带 Key 的淘汰是语义要求而非可选优化——漏淘汰会把上一个同 Id 实体的旧值重放给新订阅者
- **节点订阅解析变更**：`BaseNode` 实现 `IMessagePublisher`/`IMessageSubscriber`，`NodeExtensions.Subscribe` 改以 `BaseNode` 为接收者以消除与 `MessageManager.Subscribe(this IMessageSubscriber, ...)` 的重载二义
- **Message 统计字段更名（破坏性）**：`MessageBusStats.MessageTypeCount` 改为 `ChannelStoreCount`。它的值本就是「类型通道表项数 + 键值通道表项数」，同一消息类型既有类型通道又配了键值通道（或配多种 Key 类型）会各占一项而重复计数——原名在说谎，而统计接口的用途正是排查泄漏，误导性命名会直接浪费排查时间。`ToString()` 的标签也由「类型」改为「通道表」。无行为变化，仅更名
- **Message 接口可见性收敛**：`IMessageBroker` 由 `public` 改为 `internal`——它只有一个 `Clear()` 成员，实现类 `MessageBroker` 与实例替换点 `MessageManager.SetInstance` 均为 internal，对外既无法实现也无处注入，公开它只会得到一个不可用的契约。`IMessagePublisher`/`IMessageSubscriber` 保持公开（它们是扩展方法的接收者类型，`BaseNode` 实现了二者）。外部代码本就无法取得 `IMessageBroker` 实例，实际无可观察影响
- **Reactive 消息总线内部去 R3 化结构**：订阅直落自研事件流，移除内部 ObservableSignal 包装层与信号缓存；公共 API 与行为语义不变（投递顺序、缓冲保留、completed 语义保持）
- **Reactive 清理**：裁撤 Signal/ISignal/IReadonlySignal 死代码（公开类型名中 Signal 术语退场），ReactiveProperty 接入自足 `IReactiveProperty<T>` 接口链；注释与文档同步去 R3 叙述
- **模块拆分**：消息总线与事件流引擎迁入新 `Runtime/Message/`（命名空间 `XFramework.XMessage`），Reactive 仅保留响应式属性；公共类型名不变，使用方仅需改 using（编译期破坏性变更）
- **引擎去 Rx 命名**：Subject→EventStream、ReplaySubject→BufferedEventStream、AnonymousDisposable→ActionDisposable；Unit 删除（InputManager 帧脉冲改发 `Time.frameCount`）；事件引擎日志前缀定稿 `[Message]`

### Fixed

- **Save `LoadAsync` 静默清空内存数据**：内容为合法 JSON 但不是存档的文件（如 `{"foo":1}`）同样能反序列化出快照，其数据块列表取到的是字段初始化器给的空列表而非 `null`，`ApplySnapshot` 会先清空全部数据块、再因列表为空直接返回——玩家的内存数据被零警告清空；传字面量 `null` 则先清空全部数据块再抛 NRE。现引入结构校验（非 null + 含数据块列表 + 版本号 ≥ 1）后才允许应用
- **Save `GetAllPlayerIds` 恒返回空数组**：原实现靠扫描非递归 `GetFilesAsync` 的返回路径、用分隔符切出玩家目录前缀，而根目录返回的路径永不含分隔符，该分支自诞生起从未成立，且无测试覆盖
- **Save 按玩家查询会串档**：`GetPlayerSlotMetas` 在 `await` 期间把全局玩家上下文临时改成目标玩家，且该路径不设门禁——窗口内发起的保存会写进被查询者的目录，而 `SetCurrentPlayer` 的 busy 守卫对此无效（它读的正是未被置位的 `IsBusy`）。现改为玩家上下文只作默认值、实际路径由参数决定
- **Save 数据块恢复失败被静默吞掉**：`ApplySnapshot` 会跳过恢复失败的块并正常返回，而 Save 侧据此回报 `Loaded`——内存里少了一整个块的数据却报告成功，玩家看到的是半个游戏加一行控制台 warning。现由 Data 侧上报失败块数，Save 侧据此判定加载不完整、回滚内存并回报 `Corrupt`
- **Save 加载失败无回滚**：应用快照失败后内存已被清空且不可恢复，现先留回滚点、失败时尽力恢复（回滚本身失败也必须记 Error 而不能静默，否则会留下「以为已回滚、实则是半清空」的状态）。已知局限：`CreateSnapshot` 会清空脏标记，故回滚恢复得了数据、恢复不了脏标记集合
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
