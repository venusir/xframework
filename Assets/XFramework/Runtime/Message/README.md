# XFramework / Message 模块

## 概述

Message 模块提供**全局消息总线**与支撑它的**事件流引擎**。基于**自研轻量事件引擎**（`XFramework.XMessage.Internal`，零外部依赖），通过静态外观 `MessageManager` 提供全局消息发布/订阅能力。

消息总线支持:类型化消息、带键值通道、缓冲订阅(新订阅者立即收到最近一条)、异步处理器订阅、订阅级过滤条件、全局过滤器管道、请求-响应模式。

**命名空间**: `XFramework.XMessage`;引擎 `XFramework.XMessage.Internal`

## 架构设计

```
Runtime/Message/
├── IMessageBroker.cs             # IMessagePublisher/IMessageSubscriber/IMessageBroker
├── MessageBroker.cs              # 消息代理内部实现(订阅直落事件流)
├── MessageManager.cs             # 静态外观(全局入口) + 节点扩展方法
├── IMessageFilter.cs             # 消息过滤器接口
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

**异步订阅的令牌语义**：处理器收到的 `ct` 就是订阅自身的令牌，退订即取消它，故 `await` 会随退订提前结束；同时 `SubscribeAsync` 的 `cancellationToken` 参数**与订阅生命周期绑定——令牌取消即自动退订**（与 `AddTo` 一致），传入已取消的令牌则不会登记。

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

// 发送请求(令牌原样转发给处理器;取消是否响应由处理器决定)
var cts = new CancellationTokenSource();
var response = await MessageManager.RequestAsync<GetPlayerScoreRequest, GetPlayerScoreResponse>(
    new GetPlayerScoreRequest { PlayerId = "player_1" },
    cts.Token
);
Debug.Log($"玩家分数: {response.Score}");

// 注销处理器:重复注册前需先注销
MessageManager.Unregister<GetPlayerScoreRequest, GetPlayerScoreResponse>();
```

> **迁移提示**:异步处理器形参由 `request =>` 变为 `(request, ct) =>`。旧写法会因 lambda 元数不符而**编译期报错**,不会静默错绑。

### 全局过滤器

注册 `IMessageFilter<TMessage>` 可统一拦截/处理某类型消息,过滤器通过「不调用 next」拦截消息(类似 ASP.NET Core Middleware 形态):

```csharp
public sealed class BlockNegativeFilter : IMessageFilter<TestMessage>
{
    public void Invoke(TestMessage msg, Action<TestMessage> next)
    {
        if (msg.Value >= 0) next(msg);
    }
}

MessageManager.AddFilter<TestMessage>(new BlockNegativeFilter());

// 移除过滤器（同一实例重复注册时移除首个匹配项）
MessageManager.RemoveFilter<TestMessage>(filter);

// 移除该类型的全部过滤器，返回移除数量
var removedCount = MessageManager.ClearFilters<TestMessage>();
```

过滤器自身抛异常时由 broker 兜底：记 `[Message] Global filter threw exception` 的 Error 日志、**该条消息被拦截**、过滤器保留，不影响后续消息。

## 内存管理

通道的回收遵循两条不同规则——这是「订阅前发布的消息可重放」这一语义的必然代价：

| 通道 | 订阅清零时 | 回收方式 |
|---|---|---|
| 普通通道 | 链表空 | **自动回收**（事件流回调持有者摘除空通道） |
| 缓冲通道 | 链表空但**仍持有重放缓存** | 只能经 `EvictBufferedChannel` 系列**显式淘汰** |

带 Key 的高频发布（如按实体 Id 发布）会为每个 Key 保留一条消息，实体的生命周期结束时应当显式淘汰：

```csharp
// 按实体发布：每个 Key 各持有一条重放缓存
MessageManager.Publish(entityId, new HealthChangedMessage { Value = 100 });

// 实体销毁时淘汰其通道，否则该 Key 的重放缓存将一直保留
MessageManager.EvictBufferedChannel<int, HealthChangedMessage>(entityId);
```

```csharp
MessageManager.EvictBufferedChannel<TestMessage>();                  // 淘汰类型级缓冲通道，返回是否存在并已淘汰
MessageManager.EvictBufferedChannel<string, TestMessage>("Score");   // 淘汰指定 Key 的缓冲通道
MessageManager.EvictBufferedChannels<TestMessage>();                 // 淘汰该类型全部缓冲通道（类型级 + 所有 Key），返回淘汰数量
MessageManager.TrimEmptyChannels();                                  // 兜底：回收所有无订阅者且无重放缓存的空通道
```

淘汰后再次发布会重建空缓冲通道，重放缓存从下一条消息重新建立。

## 节点扩展方法

实现了 `IMessagePublisher` / `IMessageSubscriber` 的节点可以直接使用便捷的扩展方法:

```csharp
public class MyNode : EntityNode, IMessagePublisher, IMessageSubscriber
{
    protected override void OnStart()
    {
        base.OnStart();

        // 发布消息
        this.Publish(new CoinChangedMessage { NewAmount = 200 });

        // 订阅消息(自动绑定节点生命周期,节点销毁时自动取消订阅)
        this.Subscribe<PlayerDiedMessage>(msg =>
        {
            Debug.Log($"{msg.PlayerName} 死了");
        });

        // 带 Key 的发布
        this.Publish("Score", 500);
    }
}
```

## 设计原则

- **自研引擎驱动** — 基于零依赖的轻量事件引擎(锁 + 快照线程模型、订阅节点池),性能优异且内存安全
- **生命周期绑定** — 节点的消息订阅自动绑定到节点生命周期,节点销毁时自动取消
- **类型安全** — 消息通过泛型类型标识,编译期安全
- **双模式访问** — 同时支持静态 API(非节点类)和节点扩展方法
- **请求-响应支持** — 提供异步请求-响应模式,适合服务定位场景
- **全局过滤器** — 支持注册全局消息过滤器,统一拦截和处理
- **异常隔离** — 订阅回调/过滤条件抛异常记 Error 日志后继续,不影响其他订阅者
- **自动回收** — 订阅清零的通道由事件流回调自动摘除;缓冲通道因需保留重放缓存,提供显式淘汰 API 主动释放

## 依赖

- 无外部依赖(事件流引擎自研,零第三方依赖)
