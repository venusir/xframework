# XFramework / Core 模块

## 概述

XFramework 核心模块提供了一套轻量级、纯 C# 的**树节点系统**，是框架所有子模块的基础。它不依赖 `MonoBehaviour`，具有完整的生命周期管理、组件式缓存、对象池复用和自动取消订阅等特性。

**命名空间**: `XFramework.XCore`

## 架构设计

```
Runtime/Core/
├── IBaseNode / BaseNode          # 节点基类：生命周期、父子关系、销毁令牌
├── ParentNode / ContainerNode    # 含子节点的节点
├── EntityNode                     # 按类型缓存子节点（类似 GetComponent）
├── DictionaryNode<TKey>          # 按键缓存子节点
├── LeafNode                       # 叶子节点（无子节点）
├── RootNode                       # 根节点入口
├── NodeFactory                    # 节点工厂 + 对象池
├── NodePool<T>                    # 泛型对象池（内部实现）
├── NodeExtensions                 # AddTo 生命周期绑定扩展
├── Bootstrap/                     # 启动引导节点
│   ├── BootstrapNode              #   统一管理非节点树模块的启动
│   ├── AssetBootstrapNode         #   异步初始化 AssetManager（实现 ILoadable）
│   ├── LockBootstrapNode          #   LockManager 销毁清理
│   └── MessageBootstrapNode       #   MessageManager 销毁清理
└── Update/                        # 更新系统
    ├── IUpdateable                #   可更新接口
    ├── IUpdateNode                #   更新服务接口
    ├── UpdateNode                 #   更新节点
    └── UpdateScheduler            #   更新调度器
```

## 节点类型速览

| 类型                   | 继承自       | 用途                   | 特点                                         |
| ---------------------- | ------------ | ---------------------- | -------------------------------------------- |
| `BaseNode`             | -            | 所有节点的抽象基类     | 生命周期、父子关系、DestroyCancellationToken |
| `LeafNode`             | `BaseNode`   | 末端节点，不包含子节点 | 最轻量                                       |
| `ParentNode`           | `BaseNode`   | 可包含子节点的抽象基类 | `IParentNode`，子节点管理、事件冒泡          |
| `ContainerNode`        | `ParentNode` | 对外暴露添加/移除 API  | `IContainerNode`                             |
| `EntityNode`           | `ParentNode` | 按类型缓存子节点       | 类似 Unity `GetComponent`/`AddComponent`     |
| `DictionaryNode<TKey>` | `ParentNode` | 按键缓存子节点         | 键值对式访问                                 |
| `RootNode`             | `EntityNode` | 树根节点               | 静态 `Create()` 工厂方法                     |

## 生命周期

```
创建 → Init(arg) → Awake() → 挂入树（SetParent） → Start() → ... → Destroy()
        │              │           │                    │               │
        ▼              ▼           ▼                    ▼               ▼
     OnInit()     AwakeInternal()  添加到父节点     StartInternal()  DestroyInternal()
                  → OnAwake()                      → OnStart()     → OnDestroy()
                                                                   → Cancel(Cts)
                                                                   → 回池
```

### 关键方法

| 方法        | 访问级别   | 说明                                                |
| ----------- | ---------- | --------------------------------------------------- |
| `Awake()`   | `internal` | 初始化节点，由 `NodeFactory` 或 `AddChild` 自动调用 |
| `Start()`   | `internal` | 启动节点（父节点 Start 时自动传播给子节点）         |
| `Destroy()` | `public`   | 销毁节点，自动从父节点脱离并回池                    |
| `Dispose()` | `public`   | 等同于 `Destroy()`，支持 `using` 语法               |

### 可重写回调

| 回调                 | 说明                           |
| -------------------- | ------------------------------ |
| `OnInit(object arg)` | 参数初始化（在 Awake 之前）    |
| `OnAwake()`          | 初始化完成                     |
| `OnStart()`          | 启动完成（所有子节点已 Start） |
| `OnDestroy()`        | 销毁时                         |

## 快速使用

### 1. 创建节点树

```csharp
using XFramework.XCore;

// 创建根节点
var root = RootNode.Create();

// 添加子节点（EntityNode 模式 — 按类型缓存）
var player = root.AddNode<PlayerNode>();       // 自动从池中获取、Awake、挂入树
var health = root.AddNode<HealthNode>();

// 启动节点树（递归启动所有节点）
root.Start();

// 销毁节点树（递归销毁所有节点，自动回池）
root.Destroy();
```

### 2. 自定义节点

```csharp
public class PlayerNode : EntityNode
{
    protected override void OnAwake()
    {
        base.OnAwake();
        // 自动创建子组件
        AddNode<HealthNode>();
        AddNode<WeaponNode>();
    }

    protected override void OnStart()
    {
        base.OnStart();
        // 所有子节点已经 Start，可以安全访问
        var health = GetNode<HealthNode>();
    }

    protected override void OnDestroy()
    {
        // 清理资源
        base.OnDestroy();
    }
}
```

### 3. 获取节点（类型查找）

```csharp
// EntityNode: 按类型自动缓存
var health = entity.GetNode<HealthNode>();        // 自动创建（默认）
var health = entity.GetNode<HealthNode>(false);   // 仅查找，不创建

// 通过接口查找
var updatable = entity.GetNode<IUpdateNode>();    // 查找实现了 IUpdateNode 的节点

// ParentNode / ContainerNode: 按类型遍历查找
var child = parent.GetNode<HealthNode>();

// DictionaryNode: 按键查找
var node = dict.GetNode<PlayerNode>("player_1");
```

### 4. 服务解析（沿父链查找）

```csharp
// 从任意节点获取挂载在祖先 EntityNode 上的服务
var updateService = this.Get<IUpdateNode>();      // 沿父链向上查找
```

### 5. 生命周期绑定（自动取消订阅）

```csharp
protected override void OnStart()
{
    base.OnStart();

    // 订阅外部事件，节点销毁时自动取消
    externalEvent.Subscribe(OnEvent)
        .AddTo(this.DestroyCancellationToken);    // 或 .AddTo(this)
}
```

### 6. 对象池

```csharp
// 预热
NodeFactory.Prewarm<BulletNode>(100);

// 获取节点（优先从池中复用）
var bullet = NodeFactory.GetNode<BulletNode>();
bullet.Awake();
// ... 使用 ...
bullet.Destroy();  // 自动回池

// 手动回收
NodeFactory.ReturnNode(bullet);

// 清空池
NodeFactory.ClearPool<BulletNode>();
NodeFactory.ClearAllPools();
```

## 事件系统

| 事件                  | 来源                  | 触发时机                            |
| --------------------- | --------------------- | ----------------------------------- |
| `OnNodeAdded`         | `IParentNode`         | 直接子节点添加                      |
| `OnNodeRemoved`       | `IParentNode`         | 直接子节点移除                      |
| `OnDescendantAdded`   | `IParentNode`         | 任意子孙节点添加（递归冒泡）        |
| `OnDescendantRemoved` | `IParentNode`         | 任意子孙节点移除（递归冒泡）        |
| `OnDescendantStarted` | `IParentNode`         | 任意子孙节点 Start 完成（递归冒泡） |
| `OnNodeStarted`       | `BaseNode`            | 自身 Start 完成                     |
| `OnNodeDestroy`       | `BaseNode`            | 自身销毁                            |
| `OnReturnToPool`      | `BaseNode` (internal) | 销毁后通知缓存池回收                |

## 更新系统

**命名空间**: `XFramework.XUpdate`

```csharp
// 实现 IUpdateable 接口
public class MyUpdatable : LeafNode, IUpdateable
{
    void IUpdateable.OnUpdate(float deltaTime, float time)
    {
        // 每帧逻辑
    }

    void IUpdateable.OnEnable()  { /* 启用时 */ }
    void IUpdateable.OnDisable() { /* 禁用时 */ }
}

// 控制启用/禁用
var updateNode = this.Get<IUpdateNode>();
updateNode.Enable(myUpdatable);
updateNode.Disable(myUpdatable);
bool isEnabled = updateNode.IsEnabled(myUpdatable);
updateNode.ProcessImmediate(myUpdatable, deltaTime, time);  // 立即执行一次
```

## Bootstrap 启动引导

```csharp
// BootstrapNode 统一管理非节点树模块的启动和销毁
// 默认注册 AssetBootstrapNode、LockBootstrapNode、MessageBootstrapNode
// 可子类化并重写 OnRegisterModules 自定义

public class MyBootstrapNode : BootstrapNode
{
    protected override void OnRegisterModules()
    {
        base.OnRegisterModules();
        AddNode<LocalizationBootstrapNode>();
    }
}
```

> 配合 Loader 模块的 `StartupExtensions.StartupAsync()` 可实现完整的异步启动管线。

## 设计原则

- **纯 C#** — 不依赖 `MonoBehaviour`，可独立于 Unity 运行时测试
- **组合优先** — 通过 `EntityNode` 的组件模式，避免深层继承
- **对象池** — `NodeFactory` 内置缓存池，减少 GC
- **自动清理** — `DestroyCancellationToken` + `AddTo` 模式，节点销毁时自动取消所有订阅
- **零侵入** — 不使用本模块的项目零负担

## 依赖

- `UniTask`（`Cysharp.Threading.Tasks`，框架层已提供）