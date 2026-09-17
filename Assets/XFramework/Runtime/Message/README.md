# XFramework / Message 模块

## 概述

Message 模块提供**全局消息总线**与支撑它的**事件流引擎**。基于**自研轻量事件引擎**（`XFramework.XMessage.Internal`，零外部依赖），通过静态外观 `MessageManager` 提供全局消息发布/订阅能力。

消息总线支持:类型化消息、带键值通道、缓冲订阅(新订阅者立即收到最近一条)、异步处理器订阅、订阅级过滤条件、全局过滤器管道、请求-响应模式。

**命名空间**: `XFramework.XMessage`;引擎 `XFramework.XMessage.Internal`

## 架构设计

```
Runtime/Message/
├── IMessageBroker.cs             # IMessagePublisher/IMessageSubscriber(公开)+ IMessageBroker(internal)
├── MessageBroker.cs              # 消息代理内部实现(订阅直落事件流)
├── MessageManager.cs             # 静态外观(全局入口) + 标记接口扩展方法
├── IMessageFilter.cs             # 消息过滤器接口
├── IDestroyCancellationToken.cs  # 销毁令牌契约(订阅自动退订的绑定对象)
└── Internal/                     # 自研事件流引擎(零外部依赖,internal)
    ├── EventStream.cs            # 事件流(可投递/订阅/完成/退订)+ 订阅节点池
    ├── BufferedEventStream.cs    # 缓冲 1 条的事件流(订阅即重放最近一条)
    ├── MessageChannel.cs         # 通道对象(聚合同步流与缓冲流)+ 键值通道存储
    └── ActionDisposable.cs       # 委托式 IDisposable(幂等)
```

## 快速使用

### 发布消息

```csharp
using XFramework.XMessage;

// 定义消息类型
public struct CoinChangedMessage { public int NewAmount; }
public struct PlayerDiedMessage { public string PlayerName; }
public struct GameStateChangedMessage { public GameState NewState; }

// 发布消息
MessageManager.Publish(new CoinChangedMessage { NewAmount = 100 });
MessageManager.Publish(new PlayerDiedMessage { PlayerName = "Hero" });
MessageManager.Publish(new GameStateChangedMessage { NewState = GameState.Playing });
```

### 订阅消息

```csharp
// 普通订阅
var subscription = MessageManager.Subscribe<CoinChangedMessage>(msg =>
{
    Debug.Log($"金币变化: {msg.NewAmount}");
});

// 带过滤条件的订阅
MessageManager.Subscribe<CoinChangedMessage>(
    filter: msg => msg.NewAmount > 50,
    handler: msg => Debug.Log($"大额金币变化: {msg.NewAmount}")
);

// 带缓冲的订阅(新订阅者立即收到最近一次发布的消息)
MessageManager.SubscribeBuffered<GameStateChangedMessage>(msg =>
{
    Debug.Log($"游戏状态: {msg.NewState}");
});

// 异步处理器订阅:ct 是订阅自身的令牌,退订即取消在途 await
MessageManager.SubscribeAsync<PlayerDiedMessage>(async (msg, ct) =>
{
    Debug.Log($"{msg.PlayerName} 死亡,开始复活倒计时...");
    await UniTask.Delay(TimeSpan.FromSeconds(3), cancellationToken: ct);
    Debug.Log($"{msg.PlayerName} 已复活");
});

// 取消订阅
subscription.Dispose();
```

**异步订阅的令牌语义**：处理器收到的 `ct` 就是订阅自身的令牌，退订即取消它，故 `await` 会随退订提前结束；同时 `SubscribeAsync` 的 `cancellationToken` 参数**与订阅生命周期绑定——令牌取消即自动退订**，传入已取消的令牌则不会登记。

异步处理器独立登记在通道的异步列表中，不占同步订阅链：同步 `Publish` 以 fire-and-forget 触发它，`PublishAsync` 则会等待。

### 异步发布

`PublishAsync` 先把消息同步投递给同步订阅者并写入缓冲通道，再启动异步处理器并等待其完成：

```csharp
// 并行(默认):全部处理器先启动,再统一等待
await MessageManager.PublishAsync(new GameStateChangedMessage { NewState = GameState.Playing });

// 顺序:逐个 await,适合响应方之间有顺序依赖
await MessageManager.PublishAsync(msg, MessagePublishStrategy.Sequential);

// 只控制本次等待的取消:不会中断已启动的处理器
await MessageManager.PublishAsync(msg, MessagePublishStrategy.Parallel, cts.Token);

// 键值异步发布:策略必须显式传入,不设默认值
await MessageManager.PublishAsync("Score", msg, MessagePublishStrategy.Parallel);
```

> 键值重载之所以不给 `strategy` 默认值：否则两参调用 `PublishAsync(x, y)` 会同时匹配 `(TMessage, MessagePublishStrategy)` 与 `(TKey, TMessage)`，两个签名同构而无法裁决。

顺序是硬保证：**全局过滤器 → 同步订阅者（与 `Publish` 同序）→ 缓冲通道写入 → 异步处理器**。

> **注意**：同步 `Publish` 同样会触发异步处理器（fire-and-forget），所以同一调用点混用 `Publish` 与 `PublishAsync` 会让处理器被触发两次。

> **线程**：本模块不做线程调度。从非主线程调用 `PublishAsync` 时，同步订阅者与处理器的同步前段会在该线程上执行，而 Unity API 多数非线程安全，需要自行切回主线程。

### 带 Key 的消息

```csharp
// 按 Key 发布(相同 Key 的消息在同一通道传递)
MessageManager.Publish("PlayerHealth", 75);
MessageManager.Publish("EnemyHealth", 50);

// 按 Key 订阅
MessageManager.Subscribe<string, int>("PlayerHealth", health =>
{
    // 仅响应 PlayerHealth 通道的消息
    hpBar.Value = health;
});
```

### 请求-响应模式

```csharp
// 定义请求/响应类型
public class GetPlayerScoreRequest { public string PlayerId; }
public class GetPlayerScoreResponse { public int Score; }

// 注册处理器(全局唯一;表的键只取请求类型,换响应类型仍是重复注册)
MessageManager.Register<GetPlayerScoreRequest, GetPlayerScoreResponse>(async (request, ct) =>
{
    // 异步获取分数:ct 即 RequestAsync 调用方传入的令牌,直接透传给下游
    var score = await database.GetScoreAsync(request.PlayerId, ct);
    return new GetPlayerScoreResponse { Score = score };
});

// 发送请求(令牌既原样透传给处理器,也用于取消本次等待)
var cts = new CancellationTokenSource();
var response = await MessageManager.RequestAsync<GetPlayerScoreRequest, GetPlayerScoreResponse>(
    new GetPlayerScoreRequest { PlayerId = "player_1" },
    cts.Token
);
Debug.Log($"玩家分数: {response.Score}");

// 注销处理器:重复注册前需先注销
MessageManager.Unregister<GetPlayerScoreRequest, GetPlayerScoreResponse>();
```

响应方可能尚未就绪——这是跨模块解耦下的常态,不该靠捕获异常来判断:

```csharp
// 发请求前探测
if (MessageManager.HasHandler<GetPlayerScoreRequest>())
{
    // ... 走上面的 RequestAsync
}

// 或在一次调用内完成「查 + 发」:未注册处理器时返回 (false, default),不抛异常
var (ok, response) = await MessageManager.TryRequestAsync<GetPlayerScoreRequest, GetPlayerScoreResponse>(
    new GetPlayerScoreRequest { PlayerId = "player_1" });
```

> `TryRequestAsync` 返回的 `false` **只**表示「未注册处理器」;处理器自身抛出的异常照常向上传播,不会被折算成失败——需要区分二者时,失败后可用 `HasHandler<TRequest>()` 复核。

> **令牌的两个作用**:`RequestAsync` 的令牌一方面**原样转发**给处理器(处理器据此把取消传递到下游),另一方面用于**取消本次等待**——取消会抛 `OperationCanceledException`,但不会中断已启动的处理器。取舍与 `PublishAsync` 一致:处理器是否响应取消由它自己决定,但调用方不会因为处理器忽略令牌而无法脱身。

> **迁移提示**:异步处理器形参由 `request =>` 变为 `(request, ct) =>`。旧写法会因 lambda 元数不符而**编译期报错**,不会静默错绑。

### 全局过滤器

注册 `IMessageFilter<TMessage>` 可统一拦截/处理某类型消息,过滤器通过「不调用 next」拦截消息(类似 ASP.NET Core Middleware 形态):

```csharp
public sealed class BlockNegativeFilter : IMessageFilter<HealthChangedMessage>
{
    public void Invoke(HealthChangedMessage msg, Action<HealthChangedMessage> next)
    {
        if (msg.Value >= 0) next(msg);
    }
}

MessageManager.AddFilter<HealthChangedMessage>(new BlockNegativeFilter());

// 移除过滤器（同一实例重复注册时移除首个匹配项）
MessageManager.RemoveFilter<HealthChangedMessage>(filter);

// 移除该类型的全部过滤器，返回移除数量
var removedCount = MessageManager.ClearFilters<HealthChangedMessage>();
```

过滤器自身抛异常时由 broker 兜底：记 `[Message] Global filter threw exception` 的 Error 日志、**该条消息被拦截**、过滤器保留，不影响后续消息。

两条容易踩的语义，都是刻意行为而非缺陷：

- **过滤器拦截会一并阻止缓冲缓存写入**——被拦下的消息不会进入重放缓存，因此缓冲订阅重放的是「上一次通过过滤器的消息」。
- **过滤器管道在没有任何订阅者时也会执行**——有副作用的过滤器（日志、埋点）会为无人消费的消息触发。之所以不改成「先查订阅者再过滤」，是因为那会破坏 `PublishAsync` 与缓冲语义的一致性。

## 内存管理

通道的回收遵循两条不同规则——这是「订阅前发布的消息可重放」这一语义的必然代价：

| 通道 | 订阅清零时 | 回收方式 |
|---|---|---|
| 普通通道 | 链表空 | **自动回收**（事件流回调持有者摘除空通道） |
| 缓冲通道 | 链表空但**仍持有重放缓存** | 须经 `EvictBufferedChannel` 系列**显式淘汰**；淘汰会顺带回收因此变空的通道与存储 |

### 带 Key 的淘汰是语义要求，不只是省内存

带 Key 的高频发布（如按实体 Id 发布）会为每个 Key 保留一条消息。**实体生命周期结束时必须淘汰**，否则后果不只是内存不回落：

```csharp
// 按实体发布：每个 Key 各持有一条重放缓存
MessageManager.Publish(entityId, new HealthChangedMessage { Value = 100 });

// 实体销毁时淘汰该实体的全部通道（跨消息类型，一次调用）
MessageManager.EvictBufferedChannels(entityId);
```

漏掉这一步的失败是静默的：实体 42 死亡后其通道仍持有死亡那一刻的血量，若新生成的实体复用同一个 Id，新实体的血条订阅者会**立刻重放到上一个实体的死亡血量**——表现为满血的新实体血条一亮起来就是空的。这类 bug 不报错、不抛异常，只能靠淘汰纪律避免。

类型级通道没有这个问题（每个消息类型至多一条缓存，且有界）；键值通道的缓存则必须与实体生命周期绑定，建议把淘汰调用写在实体销毁的同一处。

**按 Key 淘汰优于按消息类型淘汰**：一个实体往往参与多种带 Key 的消息（血量、位置、状态……），按消息类型淘汰就得在实体销毁处写 N 次调用，且每新增一种消息都要回去补一处——漏掉的那一处正是上面那类静默 bug。`EvictBufferedChannels<TKey>(key)` 按「这个实体」而不是「这个消息类型」来清理，与实体的生命周期同形。

### 淘汰 API

```csharp
MessageManager.EvictBufferedChannel<HealthChangedMessage>();               // 类型级，O(1)；返回是否存在并已淘汰
MessageManager.EvictBufferedChannel<int, HealthChangedMessage>(entityId);  // 指定 Key 与该消息类型，O(1)；返回是否存在并已淘汰
MessageManager.EvictBufferedChannels<HealthChangedMessage>();              // 该消息类型全部（类型级 + 所有 Key 类型的所有 Key）；返回淘汰数量
MessageManager.EvictBufferedChannels(entityId);                            // 该 Key 跨全部消息类型（实体销毁处用这个）；返回淘汰数量
MessageManager.TrimEmptyChannels();                                        // 兜底：回收无订阅者且无重放缓存的空通道，常规路径下返回 0
```

- 类型级单数版的行为已完全包含在复数版中，保留它只是因为它是 O(1) 快路径（复数版需按消息类型扫全表）。
- `EvictBufferedChannels<TKey>(key)` 按 Key 类型扫全表，与 `EvictBufferedChannel<TKey, TMessage>(key)` 的 O(1) 快路径不同——前者是实体销毁处的一次性调用，不在热路径上。
- 淘汰的返回值只计**淘汰**的缓冲通道数，不含顺带回收的通道数与存储表项数。
- `TrimEmptyChannels` 的返回值同样只计**通道**数：键值存储整体清空而被一并摘除时不计入——结构清理一律不计入返回值。
- 淘汰只丢重放缓存，不会回收仍有活订阅者的通道。

淘汰后再次发布会重建空缓冲通道，重放缓存从下一条消息重新建立。

## 运行统计

订阅泄漏（订阅数只增不减）与缓冲内存驻留是事件总线最常见的事故，可用统计 API 定位：

```csharp
var stats = MessageManager.GetStats();
Debug.Log(stats);   // MessageBusStats(通道表 3, 通道 5, 同步订阅 12, ... 缓冲通道 4, 发布 187, ...)

// 下钻到单个类型或单个 Key
var byType = MessageManager.GetChannelStats<HealthChangedMessage>();
var byKey  = MessageManager.GetChannelStats<int, HealthChangedMessage>(entityId);
```

`BufferedChannelCount` 是排查缓冲内存驻留的主要指标——它等于「各持有一条消息的通道数」。统计为 O(通道数) 遍历、零分配，属诊断接口，不适合每帧调用。

`ChannelStoreCount` 是两张通道表（类型通道表 + 键值通道表）的**表项数之和**，**不是消息类型的个数**——同一消息类型若既有类型通道又配了键值通道，或配了多种 Key 类型，都会各占一项。它衡量的是表的规模，用来确认「该消失的表项是否真的消失了」（例如实体的最后一个 Key 被淘汰后，键值存储表项应当一并摘除）。

## 标记接口扩展方法

实现 `IMessagePublisher` / `IMessageSubscriber` 的类型可直接用 `this.Publish()` / `this.Subscribe()` 等扩展方法，无需每次都写 `MessageManager.` 前缀:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XMessage;

public class PlayerModel : IMessagePublisher, IMessageSubscriber, IDestroyCancellationToken
{
    readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public CancellationToken DestroyCancellationToken => _cts.Token;

    public void Start()
    {
        // 发布消息
        this.Publish(new CoinChangedMessage { NewAmount = 200 });

        // 带 Key 的发布
        this.Publish("Score", 500);

        // 订阅消息(自动绑定销毁时机,对象销毁时自动取消订阅)
        this.Subscribe<PlayerDiedMessage>(msg =>
        {
            Debug.Log($"{msg.PlayerName} 死了");
        });

        // 异步订阅(同样自动绑定)
        this.SubscribeAsync<PlayerDiedMessage>(async (msg, ct) =>
        {
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
        });

        // 缓冲订阅(订阅即收到最近一条,同样自动绑定)
        this.SubscribeBuffered<GameStateChangedMessage>(msg =>
        {
            Debug.Log($"当前状态: {msg.NewState}");
        });

        // 带 Key 的订阅
        this.Subscribe<string, int>("Score", score => Debug.Log($"分数: {score}"));
    }

    public void Dispose() => _cts.Cancel();
}
```

消息类型**不限 struct/class**。类型级与带 Key 的 `Subscribe` / `SubscribeAsync` / `SubscribeBuffered` 均有对应扩展重载（含过滤条件重载），无需退回静态 API 组合。

订阅自动绑定订阅者的销毁时机：`MonoBehaviour` 用其 `destroyCancellationToken`，普通 C# 对象实现 `IDestroyCancellationToken`（定义于本模块）即可；两者皆非时不会自动绑定，需自行持有返回的 `IDisposable`。

## 适用场景与选型

框架内并存多套通信机制，选错了会造成调用链难追踪或状态不同步。判据如下：

| 你的需求 | 用什么 |
|---|---|
| 跨模块 / 跨层级的解耦广播（发布方不知道谁在听） | **本模块** `MessageManager`，类型即频道 |
| 广播后要等所有响应方处理完 | `MessageManager.PublishAsync` |
| 需要返回值 / 等一个结果 | `MessageManager.RequestAsync`；响应方可能未就绪时用 `TryRequestAsync` |
| 单个对象的属性变化，UI 需要跟随 | `ReactiveProperty<T>`（Reactive 模块） |
| 需要「当前值」语义（后来者要立刻拿到状态） | `ReactiveProperty<T>`（订阅即回调当前值、相同值去重）；一次性快照可用 `SubscribeBuffered` |
| 类内部或对象级的私有回调 | C# `event` |
| 每帧高频、性能敏感的路径 | C# `event` 或直接调用（消息总线要付通道查找与锁的开销） |

**何时不要用消息总线**：一对一、调用链本就清晰时，直接调用或接口注入更好——消息总线会切断调用链，调试时难回答「这条消息是谁发的、谁收的」；框架自身也保留了 `Pipeline` 生命周期、`AssetDownloaderHandle` 等处的 C# event 便属此类。

> 框架内确实存在功能重叠：`SettingsChangedMessage`（走消息总线）与 `ConfigManager.ConfigChanged`（走 C# event）是同一类需求的两种实现。选型以「是否跨模块」为准，而非以「哪个更先进」为准。

## 设计原则

- **自研引擎驱动** — 基于零依赖的轻量事件引擎(锁 + 快照线程模型、订阅节点池),性能优异且内存安全
- **生命周期绑定** — 订阅可自动绑定到订阅者生命周期(MonoBehaviour 或 `IDestroyCancellationToken`),对象销毁时自动取消
- **类型安全** — 消息通过泛型类型标识,编译期安全
- **双模式访问** — 同时支持静态 API 与标记接口扩展方法
- **请求-响应支持** — 提供异步请求-响应模式,适合服务定位场景
- **全局过滤器** — 支持注册全局消息过滤器,统一拦截和处理
- **异常隔离** — 订阅回调/过滤条件抛异常记 Error 日志后继续,不影响其他订阅者
- **自动回收** — 订阅清零的通道由事件流回调自动摘除;缓冲通道因需保留重放缓存,提供显式淘汰 API 主动释放

## 依赖

- 无外部依赖(事件流引擎自研,零第三方依赖)
