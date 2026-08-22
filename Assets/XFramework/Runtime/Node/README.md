# XFramework / Node 模块

## 概述

XFramework 核心模块提供了一套轻量级、纯 C# 的**树节点系统**，是框架所有子模块的基础。它不依赖 `MonoBehaviour`，具有完整的生命周期管理、组件式缓存、对象池复用和自动取消订阅等特性。

**命名空间**: `XFramework.XNode`

## 架构设计

```
Runtime/Node/
├── IBaseNode / BaseNode          # 节点基类：生命周期、父子关系、销毁令牌
├── ParentNode / ContainerNode    # 含子节点的节点
├── EntityNode                     # 按类型缓存子节点（类似 GetComponent）
├── DictionaryNode<TKey>          # 按键缓存子节点
├── LeafNode                       # 叶子节点（无子节点）
├── RootNode                       # 根节点入口
├── NodeFactory                    # 节点工厂 + 对象池
├── NodePool<T>                    # 泛型对象池（内部实现）
├── NodeExtensions                 # AddTo 生命周期绑定扩展
├── AssetExtensions                # 节点资源加载扩展（委托 AssetManager 门面）
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

| 类型                   | 继承自       | 用途                   | 特点                                                   |
| ---------------------- | ------------ | ---------------------- | ------------------------------------------------------ |
| `BaseNode`             | -            | 所有节点的抽象基类     | 生命周期、父子关系、DestroyCancellationToken、标签系统 |
| `LeafNode`             | `BaseNode`   | 末端节点，不包含子节点 | 最轻量                                                 |
| `ParentNode`           | `BaseNode`   | 可包含子节点的抽象基类 | `IParentNode`，子节点管理、事件冒泡                    |
| `ContainerNode`        | `ParentNode` | 对外暴露添加/移除 API  | `IContainerNode`                                       |
| `EntityNode`           | `ParentNode` | 按类型缓存子节点       | 类似 Unity `GetComponent`/`AddComponent`               |
| `DictionaryNode<TKey>` | `ParentNode` | 按键缓存子节点         | 键值对式访问                                           |
| `RootNode`             | `EntityNode` | 树根节点               | 静态 `Create()` 工厂方法                               |

## 生命周期

### 核心状态标志位

节点有四个核心 bool 标志位，共同定义节点在生命周期中所处的阶段：

| 字段         | 访问                        | 含义                                                                    | 初始值（Awake 后） | 设置时机                                                       |
| ------------ | --------------------------- | ----------------------------------------------------------------------- | ------------------ | -------------------------------------------------------------- |
| `_destroyed` | `Destroyed`（internal get） | 节点是否已销毁                                                          | `false`            | `DestroyInternal()` 入口立即置 `true`                          |
| `_started`   | `Started`（internal get）   | 是否已完成 Start                                                        | `false`            | `StartInternal()` 中置 `true`，不可逆                          |
| `_enabled`   | `Enabled`（public get/set） | 本地启用意图 — 节点自身是否希望处于活跃状态                             | `true`             | `AwakeInternal()` 重置为 `true`；`SetEnabled()` 修改           |
| `_active`    | `Active`（public get）      | 级联活跃状态 — 考虑祖先链后的有效状态（= `_enabled && parent._active`） | `true`（瞬态）     | `AwakeInternal()` 重置；`SetParent()` / `RefreshActive()` 推导 |

> **分离设计**：`_enabled` 是**本地意图**，`_active` 是**级联现实**。  
> 当父节点禁用再恢复时，`_enabled` 保存了每个子节点的独立意图，只有 `_enabled = true` 的节点恢复活跃。

### 合法状态组合

| 状态阶段                  | `_destroyed` | `_started` | `_enabled` | `_active`        |
| ------------------------- | ------------ | ---------- | ---------- | ---------------- |
| Awake 后 / 挂树前（瞬态） | F            | F          | T          | T                |
| 挂树后 / Start 前         | F            | F          | T          | 父节点 `_active` |
| 正常运行                  | F            | T          | T          | T                |
| 自身禁用                  | F            | T          | F          | F                |
| 祖先禁用（被动）          | F            | T          | T          | F                |
| 自身禁用 + 祖先禁用       | F            | T          | F          | F                |
| 已销毁（终态）            | T            | —          | —          | —                |

### 完整生命周期状态机

```
                        NodeFactory.GetNode<T>()
  ┌──────────────────────────────────────────────────┐
  │  1. Init(arg)                                    │
  │     └─ OnInit(arg)  ← 参数初始化                  │
  │                                                  │
  │  2. Awake()                                      │
  │     └─ AwakeInternal()                           │
  │        ├─ 重置 _depth / _parent / _destroyed     │
  │        │  / _started / _enabled / _active        │
  │        ├─ 创建 _destroyCts                       │
  │        └─ OnAwake()                              │
  │                                                 │
  │        ┌──────────────────────┐                 │
  │        │ _destroyed = false  │                 │
  │        │ _started   = false  │                 │
  │        │ _enabled   = true   │                 │
  │        │ _active    = true   │ (瞬态，挂树后修正)│
  │        └──────────────────────┘                 │
  └──────────────┬───────────────────────────────────┘
                 │  AddChild(node) 或 手动挂入
                 ▼
  ┌──────────────────────────────────────────────────┐
  │  3. SetParent(parent)                            │
  │     └─ RefreshActive()                           │
  │        _active = _enabled && parent._active      │
  │                                                 │
  │        ┌──────────────────────┐                 │
  │        │ _active  = parent    │ ← 实际生效       │
  │        │ 其余标志位不变        │                 │
  │        └──────────────────────┘                 │
  └──────────────┬───────────────────────────────────┘
                 │  父节点 Start 时自动传播，或手动调用
                 ▼
  ┌──────────────────────────────────────────────────┐
   │  4. Start() / StartInternal()                    │
   │     ├─ _started = true                           │
   │     ├─ OnStart()                                 │
   │     ├─ 触发 OnNodeStarted 事件                   │
   │     └─ 若 _active = true → OnEnable()           │
   │        （补调首次 OnEnable，与 Unity 行为一致）    │
   │                                                 │
   │        ┌──────────────────────┐                 │
   │        │ _started   = true   │ ← 里程碑          │
   │        │ 其余不变             │                 │
   │        └──────────────────────┘                 │
  └──────────────┬───────────────────────────────────┘
                 │
        ┌────────┴────────┐
        ▼                 ▼
  ┌──────────────┐  ┌──────────────────────────┐
  │ 自身 Enabled  │  │ 祖先 Enabled 变化          │
  │ 变化          │  │ (RefreshActive 递归传播)   │
  └──────────────┘  └──────────────────────────┘

  自身或祖先禁用:                           自身或祖先恢复:
  ┌──────────────────────────┐        ┌──────────────────────────────┐
  │ RefreshActive()          │        │ RefreshActive()              │
  │ ├─ _active = false       │        │ ├─ _active = _enabled         │
  │ └─ OnDisable()           │        │ │           && parent._active │
  │                          │        │ └─ 若新 _active = true        │
  │ 注意: _enabled 不变!     │        │    → OnEnable()              │
  └──────────────────────────┘        │ 若新 _active = false          │
                                       │    → 无变化                  │
                                       └──────────────────────────────┘
                 │
                 ▼
  ┌──────────────────────────────────────────────────┐
  │  5. Destroy() / DestroyInternal()                 │
  │                                                  │
  │  Phase 1: 标记 + 清理订阅                         │
  │   ├─ _destroyed = true  ← 第一步，阻止重入         │
  │   ├─ _destroyCts.Cancel() + Dispose()             │
  │   ├─ Dispose _autoDisposables[]                   │
  │   └─ 触发 OnNodeDestroy 事件                      │
  │                                                  │
  │  Phase 2: 用户自定义清理                           │
  │   └─ OnDestroy()  ← 此时树引用仍有效              │
  │                                                  │
  │  Phase 3: 清理内部引用 + 回池通知                  │
  │   ├─ 清理 _tags                                   │
  │   ├─ _depth = 0, _parent = null                   │
  │   └─ 触发 OnReturnToPool（通知对象池回收）          │
  │                                                  │
  │        ┌──────────────────────┐                  │
  │        │ _destroyed = true   │ ← 终态            │
  │        │ 其余字段无意义        │                  │
  │        └──────────────────────┘                  │
  └──────────────────────────────────────────────────┘
```

### 关键方法

| 方法        | 访问级别   | 说明                                                      |
| ----------- | ---------- | --------------------------------------------------------- |
| `Awake()`   | `internal` | 初始化节点，由 `NodeFactory` 或 `AddChild` 自动调用       |
| `Start()`   | `internal` | 启动节点（父节点 Start 时自动传播给子节点），只会执行一次 |
| `Destroy()` | `public`   | 销毁节点，自动从父节点脱离并回池                          |
| `Dispose()` | `public`   | 等同于 `Destroy()`，支持 `using` 语法                     |

### 全部可重写回调

| 回调                 | 触发时机                                          | 前置条件                  |
| -------------------- | ------------------------------------------------- | ------------------------- |
| `OnInit(object arg)` | 参数初始化，在 Awake 之前                         | —                         |
| `OnAwake()`          | 初始化完成                                        | —                         |
| `OnStart()`          | 启动完成（所有子节点已 Start）                    | —                         |
| `OnEnable()`         | 级联活跃状态 <see cref="Active"/> 从 false → true | `_started && !_destroyed` |
| `OnDisable()`        | 级联活跃状态 <see cref="Active"/> 从 true → false | `_started && !_destroyed` |
| `OnDestroy()`        | 节点销毁，树引用仍有效                            | —（Phase 2 调用）         |

> `OnEnable/OnDisable` 响应**级联活跃状态**（`Active`）变化，语义与 Unity MonoBehaviour.OnEnable/OnDisable 一致。  
> 无论状态变化由自身 `Enabled` 改变还是祖先节点启用/禁用引起，回调行为统一，无需区分。

### 级联活跃机制 (`_enabled` vs `_active`)

**公式**：`_active = _enabled && (parent == null || parent._active)`

Update 系统及其他外部调用方只需检查 `Active` 即可 O(1) 判断节点是否应接收 Tick：

```csharp
// UpdateScheduler 伪代码
foreach (var node in allUpdatables)
{
    if (node.Active) node.Tick();
}
```

**典型场景**：

| 操作                                         | `_enabled` | `_active` | 效果                                                             |
| -------------------------------------------- | ---------- | --------- | ---------------------------------------------------------------- |
| 关闭 UI 面板 `panel.Enabled = false`         | F          | F         | 面板及所有子孙停止 Update，`OnDisable` 递归触发                  |
| 重新打开面板 `panel.Enabled = true`          | T          | T         | 面板恢复，但之前被单独禁用的子按钮 `_enabled=false` **不会**恢复 |
| 暂停整棵树（游戏暂停）`root.Enabled = false` | T          | F         | 所有节点暂停 Tick，但保留各自的 `_enabled` 意图                  |
| 恢复游戏 `root.Enabled = true`               | T          | T         | 只有 `_enabled=true` 的节点恢复 Tick                             |

## 快速使用

### 1. 创建节点树

```csharp
using XFramework.XNode;

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

### 3. 标签系统（Tags）

每个节点支持多个字符串标签，可动态添加/移除，用于语义分组和灵活筛选。

```csharp
// 添加/移除标签
player.AddTag("Player");
player.AddTag("TeamA");
player.RemoveTag("TeamA");

// 检查标签
bool isPlayer = player.HasTag("Player");
bool isSpecial = player.HasTags(new[] { "Player", "Elite" });  // 拥有全部标签（AND）
```

**标签查询与类型查询的组合使用** — 从 `IParentNode` 沿树查询：

```csharp
// 查找第一个带有指定标签的节点
var player = root.GetNodeByTag("Player");

// 查找第一个带有指定标签且匹配类型的节点
var hero = root.GetNodeByTag<PlayerNode>("Hero");

// 查找所有带有指定标签的节点
List<BaseNode> teamA = root.GetNodesByTag("TeamA", recursive: true);

// 查找所有拥有全部指定标签的节点（AND 逻辑）
List<BaseNode> elites = root.GetNodesByTags(new[] { "Player", "Elite" }, recursive: true);

// 查找所有拥有全部指定标签且匹配类型的节点
List<EnemyNode> enemyElites = root.GetNodesByTags<EnemyNode>(new[] { "Enemy", "Elite" }, recursive: true);
```

> 所有 GetNodesBy* 方法均提供两个重载版本：返回值列表版本和填充已存在列表版本（减少 GC 分配）。

### 4. 获取节点（类型查找）

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

### 5. 服务解析（沿父链查找）

```csharp
// 从任意节点获取挂载在祖先 EntityNode 上的服务
var updateService = this.Get<IUpdateNode>();      // 沿父链向上查找
```

### 6. 生命周期绑定（自动取消订阅）

```csharp
protected override void OnStart()
{
    base.OnStart();

    // 订阅外部事件，节点销毁时自动取消
    externalEvent.Subscribe(OnEvent)
        .AddTo(this.DestroyCancellationToken);    // 或 .AddTo(this)
}
```

### 7. 对象池

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

## 资源加载（AssetExtensions）

节点树可以依赖并启动静态服务（见「Bootstrap 启动引导」）。`AssetExtensions` 为实现了 `IBaseNode` 的任意节点提供资源加载便捷糖，全部方法委托 `AssetManager` 静态门面（`XFramework.XAsset`）。

**前置条件**：需先经 `AssetBootstrapNode` 完成 `AssetManager.InitializeAsync`（`BootstrapNode` 默认注册，见上节）。

```csharp
using Cysharp.Threading.Tasks;
using XFramework.XNode;

public class MyNode : EntityNode
{
    protected override void OnStart()
    {
        base.OnStart();

        // OnStart 是同步生命周期回调：异步加载放入私有 async UniTask 方法，用 Forget() 启动
        LoadResourcesAsync().Forget();
    }

    private async UniTask LoadResourcesAsync()
    {
        // 加载资源（句柄用 using 管理，离开块自动释放引用计数，不释放会泄漏）
        using (var handle = await this.LoadAssetAsync<GameObject>("characters/player"))
        {
            var prefab = handle.Asset;
            // ... 使用 prefab ...
        }

        // 加载并实例化（自动走对象池，实例销毁时经 InstanceTracker 自动释放引用）
        var go = await this.InstantiateAssetAsync("characters/enemy");
        this.DestroyAssetInstance(go);
    }
}
```

> `EntityNode` 是纯 C# 节点，没有 `transform` 成员；需要指定挂载父物体时，把外部 `Transform` 传给 `InstantiateAssetAsync` 的 `parent` 参数。
> 加载过程可传入 `this.DestroyCancellationToken`，节点销毁时自动中止。
> 完整 API 见 [Asset 模块 README](../Asset/README.md)。

## 设计原则

- **纯 C#** — 不依赖 `MonoBehaviour`，可独立于 Unity 运行时测试
- **组合优先** — 通过 `EntityNode` 的组件模式，避免深层继承
- **对象池** — `NodeFactory` 内置缓存池，减少 GC
- **自动清理** — `DestroyCancellationToken` + `AddTo` 模式，节点销毁时自动取消所有订阅
- **零侵入** — 不使用本模块的项目零负担

## 依赖

- `UniTask`（`Cysharp.Threading.Tasks`，框架层已提供）
- `XFramework.XAsset` — `AssetExtensions` 委托 `AssetManager` 静态门面（节点树依赖静态服务，方向单向）