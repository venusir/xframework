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

// 异步处理器订阅
MessageManager.SubscribeAsync<PlayerDiedMessage>(async msg =>
{
    Debug.Log($"{msg.PlayerName} 死亡,开始复活倒计时...");
    await UniTask.Delay(TimeSpan.FromSeconds(3));
    Debug.Log($"{msg.PlayerName} 已复活");
});

// 取消订阅
subscription.Dispose();
```

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

// 注册处理器(全局唯一)
MessageManager.Register<GetPlayerScoreRequest, GetPlayerScoreResponse>(async request =>
{
    // 异步获取分数
    var score = await database.GetScoreAsync(request.PlayerId);
    return new GetPlayerScoreResponse { Score = score };
});

// 发送请求
var response = await MessageManager.RequestAsync<GetPlayerScoreRequest, GetPlayerScoreResponse>(
    new GetPlayerScoreRequest { PlayerId = "player_1" }
);
Debug.Log($"玩家分数: {response.Score}");
```

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
```

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

## 依赖

- 无外部依赖(事件流引擎自研,零第三方依赖)
