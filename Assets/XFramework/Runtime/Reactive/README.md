# XFramework / Reactive 模块

## 概述

XFramework 响应式模块提供消息总线、响应式属性和信号系统。基于 **R3** 响应式编程库构建，通过静态外观 `MessageManager` 提供全局消息发布/订阅能力，通过 `ReactiveProperty<T>` 节点提供响应式属性绑定，通过 `ISignal` 接口提供轻量级事件通知。

**命名空间**: `XFramework.XReactive`

## 架构设计

```
Runtime/Reactive/
├── IMessageBroker.cs             # 消息发布/订阅器接口
├── MessageBroker.cs              # 消息代理内部实现（基于 R3）
├── MessageManager.cs             # 静态外观（全局入口） + 节点扩展方法
├── MessageFilter.cs              # 消息过滤器接口
├── MessageBootstrapNode.cs       # 启动节点（注册到 Bootstrap 管线）
├── IReactiveProperty.cs          # 响应式属性接口
├── ReactiveProperty.cs           # 响应式属性节点                           
├── ISignal.cs                    # 信号接口
└── Signal.cs                     # 信号内部实现（基于 R3 Subject）
```

## 快速使用

### 1. 消息总线（MessageManager）

#### 发布消息

```csharp
using XFramework.XReactive;

// 定义消息类型
public struct CoinChangedMessage { public int NewAmount; }
public struct PlayerDiedMessage { public string PlayerName; }
public struct GameStateChangedMessage { public GameState NewState; }

// 发布消息
MessageManager.Publish(new CoinChangedMessage { NewAmount = 100 });
MessageManager.Publish(new PlayerDiedMessage { PlayerName = "Hero" });
MessageManager.Publish(new GameStateChangedMessage { NewState = GameState.Playing });
```

#### 订阅消息

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

// 带缓冲的订阅（新订阅者立即收到最近一次发布的消息）
MessageManager.SubscribeBuffered<GameStateChangedMessage>(msg =>
{
    Debug.Log($"游戏状态: {msg.NewState}");
});

// 异步处理器订阅
MessageManager.SubscribeAsync<PlayerDiedMessage>(async msg =>
{
    Debug.Log($"{msg.PlayerName} 死亡，开始复活倒计时...");
    await UniTask.Delay(TimeSpan.FromSeconds(3));
    Debug.Log($"{msg.PlayerName} 已复活");
});

// 取消订阅
subscription.Dispose();
```

#### 带 Key 的消息

```csharp
// 按 Key 发布（相同 Key 的消息在同一通道传递）
MessageManager.Publish("PlayerHealth", 75);
MessageManager.Publish("EnemyHealth", 50);

// 按 Key 订阅
MessageManager.Subscribe<int>("PlayerHealth", health =>
{
    // 仅响应 PlayerHealth 通道的消息
    hpBar.Value = health;
});
```

#### 请求-响应模式

```csharp
// 定义请求/响应类型
public class GetPlayerScoreRequest { public string PlayerId; }
public class GetPlayerScoreResponse { public int Score; }

// 注册处理器（全局唯一）
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

### 2. 响应式属性（ReactiveProperty）

```csharp
using XFramework.XReactive;

// ReactiveProperty 是节点，可挂载到节点树中
var healthProp = new ReactiveProperty<int>();
healthProp.Value = 100;

// 订阅值变化
var subscription = healthProp.Subscribe(newValue =>
{
    Debug.Log($"血量变化: {newValue}");
    // 更新血量条 UI
});

// 修改值（自动推送）
healthProp.Value = 80;   // 输出: 血量变化: 80
healthProp.Value = 50;   // 输出: 血量变化: 50

// 取消订阅
subscription.Dispose();
```

### 3. 信号系统（Signal）

```csharp
var signal = new Signal();

// 订阅信号
var subscription = signal.Subscribe(() =>
{
    Debug.Log("信号被触发！");
});

// 发布信号
signal.Publish();  // 输出: 信号被触发！
subscription.Dispose();

// 带参数的信号
var scoreSignal = new Signal<int>();
scoreSignal.Subscribe(score =>
{
    Debug.Log($"收到分数: {score}");
});
scoreSignal.Publish(1000);
```

## 节点扩展方法

实现了 `IMessagePublisher` / `IMessageSubscriber` 的节点可以直接使用便捷的扩展方法：

```csharp
public class MyNode : EntityNode, IMessagePublisher, IMessageSubscriber
{
    protected override void OnStart()
    {
        base.OnStart();

        // 发布消息
        this.Publish(new CoinChangedMessage { NewAmount = 200 });

        // 订阅消息（自动绑定节点生命周期，节点销毁时自动取消订阅）
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

- **R3 驱动** — 基于 R3 响应式编程库，性能优异且内存安全
- **生命周期绑定** — 节点的消息订阅自动绑定到节点生命周期，节点销毁时自动取消
- **类型安全** — 消息通过泛型类型标识，编译期安全
- **双模式访问** — 同时支持静态 API（非节点类）和节点扩展方法
- **请求-响应支持** — 提供异步请求-响应模式，适合服务定位场景
- **全局过滤器** — 支持注册全局消息过滤器，统一拦截和处理

## 依赖

- `R3` — GitHub/OpenUPM 依赖，响应式编程库
- `XFramework.XCore` — 节点系统依赖