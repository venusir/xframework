# Event —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Event/README.md`——语义契约、线程、性能、与 C# `event` / Rx 的取舍一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Event/
├── IEventStream.cs                # 公开面:Subscribe / Emit / Complete / Dispose + SubscriptionCount
├── IBufferedEventStream.cs        # 公开面:缓冲变体(+ HasCachedValue)
├── EventStream.cs                # 静态工厂:Create / CreateBuffered
├── DisposableBag.cs              # 句柄容器 + AddTo 链式登记
├── SubscriptionTracker.cs        # 仅 Editor 的全局存活订阅计数(跨模块共享设施,刻意不在 Internal/)
└── Internal/
    ├── EventStreamImpl.cs        # 锁 + 快照的默认实现、订阅节点池、异常隔离投递
    ├── BufferedEventStreamImpl.cs# 缓冲变体(重放先于实时;与基类双锁永不嵌套)
    ├── DispatchListPool.cs       # 派发快照 List 的静态池
    └── ActionDisposable.cs       # 委托式句柄(含共享 Empty)
```

## 命名沿革（下一轮别再重新论证）

**类型名**：`Subject`（生于 `Runtime/Reactive/Internal/Subject.cs`，`1699e7f`）→ `EventStream` / `BufferedEventStream`（`725422c`，2026-09-09「去 Rx 命名」，五分钟后随 `127a634` 迁入 `XMessage`）→ 2026-09-27 下沉为独立模块 `XFramework.XEvent`（`2520c44`，照 Pipeline 先例：公开接口 + 静态工厂 + internal 实现）。

**成员名**：`725422c` 那轮只改了类型名，提交信息写明「成员方法名**保守保留**（Subscribe/OnNext/OnCompleted）」——动机是收窄当时那轮的改动面；**该子句只活在提交信息里**，CHANGELOG 对应条目把成员名那半句丢了（考古时的第一现场）。2026-10-01 补做：`OnNext` → `Emit`、`OnCompleted` → `Complete`、订阅参数 `onNext` → `handler`。当时的窗口条件：公开面**从未发布**（0.2.0 里引擎仍是 internal 的 `Subject`），越过后即成为对第三方的破坏性变更。

**为什么是这三个名字**：

1. `On` 前缀在本仓**有方向含义**——Update / Data / Pool / UI 等十余处都是「框架回调使用方」（`OnUpdate` / `OnRent` / `OnBound`），而 `OnNext` / `OnCompleted` 是持有者的**推入**面，方向相反。
2. `OnCompleted` 还与 Pipeline 的同名**事件**（`IPipeline.OnCompleted`，`+=` 订阅）同名不同形。
3. `Emit` / `Complete` 与 `Subscribe` / `Dispose` 组成全直白成员面，且不与 Message 的 `Publish` 争词。

**已评估与否决的替代名**：

| 候选 | 否决理由 |
|---|---|
| `Publish` | 与 Message 的总线动词同词不同契约（总线带寻址 / 过滤 / 异步登记），会糊掉 README 的「流 vs 总线」二分 |
| `Subject` | Rx 术语，带回整套 `IObservable` 契约期望；本仓自 0.2.0 起刻意去 R3/Rx 依赖 |
| `Send` | MediatR 把 `Send` 定为「请求发给唯一处理器」（扇出才叫 `Publish`），略带有目的地的味道 |
| `Invoke` | 与 Unity `MonoBehaviour.Invoke(string, float)`（按名延迟调用）撞名；且它描述机制（调用订阅者）而非动作（发出事件） |
| `Broadcast` | 本仓文档里「广播」正是**总线**的词（`Runtime/Message/README.md:340`）；另撞 `MonoBehaviour.BroadcastMessage` |

改名波及的其它公开面：`IReactiveProperty<T>` / `ReactiveProperty<T>` / `ReadOnlyReactiveProperty<T>` / `SettingRef.Subscribe` 的参数名。其中 `ReactiveProperty<T>.Subscribe` 的参数名属 **0.2.0 已发布面**（源兼容，仅具名实参调用受影响，仓内为零）；其余为未发布面。

## 已评估未采纳与未决

**已评估未采纳**：

- **不拆「观察面 / 投递面」两个接口**（`IPublisher` / `ISubscriber` 分离那种）：`Emit` 公开、靠「谁持有谁才能投递」约束是本模块的有意取舍（README「与 C# `event` 的关系」已记：**封装反而是 `event` 更好**，本模块拿封装换了「流可作为值持有」。拆面会让类型翻倍，并把持有关系变成类型关系。**如实记下**：拆面本身没有专门的评估记录，本轮只是未采纳——若第三方误用 `Emit` 成为实际问题，这是可以重开的方向。
- **不合并两份 internal 实现**（`DispatchListPool` / `ActionDisposable` 在 Event 与 Message 各留一份）：十行适配器不值得跨模块绑定，先例是 UI 的 `UIBinder` 内联；理由也写在两处注释里（见 CHANGELOG `事件流引擎下沉` 条目）。

**未决**：

- **Event 层没有主线程断言**：Message 在发布 / 订阅入口有 `MainThreadGuard`（仅 Editor、Release 零开销），而本模块的直接调用不经任何断言——README「线程」节已如实标注这条边界。是否要在 `Emit` / `Subscribe` 上加同类断言未定：本模块处在每帧热路径上，加检查要论证它值那一次分支（Message 的入口断言成本之所以可接受，是因为它不在每帧路径上）。
