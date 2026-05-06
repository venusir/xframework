# XFramework 文档

## 概述

XFramework 是一个基于树形结构的 Unity 节点系统框架，旨在提供一种**组合优于继承**的架构模式。它借鉴了 Unity GameObject/Component 的设计思想，但完全基于纯 C# 实现，不依赖 MonoBehaviour 继承。

### 解决的问题

- **MonoBehaviour 耦合** — 游戏逻辑分散在多个 MonoBehaviour 中，难以复用和测试
- **生命周期混乱** — Awake/Start/Update 的执行顺序依赖 Unity 的加载顺序
- **对象管理繁琐** — 频繁 Instantiate/Destroy 导致 GC 压力
- **更新调度粗放** — 所有 Update 每帧执行，无法按需降频
- **资源管理分散** — 资源加载、引用计数、对象池各模块各自为政

### 核心概念

| 概念         | 说明                                                        |
| ------------ | ----------------------------------------------------------- |
| **节点树**   | 层级化的树形结构，每个节点有深度（Depth）属性               |
| **组件模式** | EntityNode 按类型缓存子节点，类似 GetComponent/AddComponent |
| **对象池**   | 节点销毁后自动回池，减少 GC 分配                            |
| **LOD 更新** | 节点返回 UpdateLOD 等级，自动调整更新频率                   |
| **异步加载** | 节点装载加载任务，StartupAsync 统一调度                     |
| **资源服务** | 统一资源加载、实例化、对象池、引用计数、延迟卸载            |

---

## 目录结构

```
Assets/XFramework/
│
├── Runtime/                          # 运行时代码
│   │
│   ├── Core/                         # ── 节点树核心 ──
│   │   ├── BaseNode.cs               # 抽象基类：生命周期、深度、池事件
│   │   ├── LeafNode.cs               # 叶子节点：无子节点，树末端
│   │   ├── ParentNode.cs             # 可包含子节点的基类
│   │   ├── ContainerNode.cs          # 公开 AddNode/RemoveNode 的容器节点
│   │   ├── CompositeNode.cs          # [已废弃] 用 ContainerNode 替代
│   │   ├── EntityNode.cs             # 按类型(Type)缓存子节点（组件模式）
│   │   ├── DictionaryNode.cs         # 按 Key 缓存子节点（键值对模式）
│   │   ├── RootNode.cs               # 根节点（节点树入口）
│   │   ├── NodeFactory.cs            # 节点工厂：对象池管理、创建/回收
│   │   ├── NodePool.cs               # 泛型节点池实现
│   │   └── NodeUtility.cs            # 节点工具（已合并到扩展方法）
│   │
│   ├── LoadService/                  # ── 加载管线 ──
│   │   ├── ILoadable.cs              # ILoadable 接口 + LoadState 枚举
│   │   ├── LoadableTask.cs           # 加载任务基类（纯 C#）
│   │   ├── ILoadCoordinator.cs       # 加载协调器接口
│   │   ├── LoadCoordinator.cs        # 加载协调器实现
│   │   ├── LoadProgressSnapshot.cs   # 进度快照结构体
│   │   └── StartupExtensions.cs      # StartupAsync 扩展方法
│   │
│   ├── UpdateService/                # ── 更新调度 ──
│   │   ├── IUpdateable.cs            # IUpdateable 接口 + UpdateLOD 枚举
│   │   ├── UpdateScheduler.cs        # LOD 分级调度器（时间切片算法）
│   │   └── UpdateBinder.cs           # 自动绑定节点树事件的更新桥接
│   │
│   ├── AssetService/                 # ── 资源服务 ──
│   │   ├── IAssetService.cs          # 资源服务接口
│   │   ├── AssetServiceNode.cs       # 资源服务节点（AssetService 实现）
│   │   ├── InstanceTracker.cs        # 实例追踪器（自动防泄漏）
│   │   └── YooAsset/                 # YooAsset 底层实现
│   │       ├── YooAssetServiceImpl.cs    # 资源加载/引用计数/延迟卸载
│   │       └── YooAssetInitTask.cs       # YooAsset 初始化加载任务
│   │
│   ├── Reactive/                     # ── 响应式模块 ──
│   │   ├── ISignal.cs                # 只读/完整信号接口
│   │   ├── IReactiveProperty.cs      # 响应式属性接口
│   │   ├── IMessageBroker.cs         # 发布/订阅接口（含Async/Buffered）
│   │   ├── IEventNode.cs             # 事件节点接口
│   │   ├── IReactiveLifecycle.cs     # 响应式生命周期接口
│   │   ├── IMessageFilter.cs         # 消息过滤器接口
│   │   ├── IRequestNode.cs           # 请求-响应节点接口
│   │   ├── Signal.cs                 # R3 实现 (internal)
│   │   ├── MessageBroker.cs          # R3 实现 (internal)
│   │   ├── EventNode.cs              # 事件节点
│   │   ├── ReactiveLifecycle.cs      # 生命周期扩展
│   │   ├── ReactiveProperty.cs       # 响应式属性节点（节点树末端）
│   │   ├── RequestNode.cs            # 请求-响应节点
│   │   ├── ReactiveExtensions.cs     # AddTo 等扩展
│   │   └── Venusy609.Xframework.Reactive.asmdef
│   │
│   ├── GameLauncher.cs               # Unity ↔ 节点树的生命周期桥接
│   └── Venusy609.Xframework.asmdef   # 程序集定义
│
├── Documentation/
│   └── XFramework.md                 # 本文档
│
├── Samples/
│   └── Example/                      # 示例代码
│       └── SampleExample.cs
│
├── Tests/                            # 单元测试
│   ├── Editor/
│   └── Runtime/
│
├── Editor/                           # 编辑器扩展
│
├── package.json                      # UPM 包配置
├── README.md
├── CHANGELOG.md
└── Third Party Notices.md
```

---

## 节点体系 (Core)

### 节点类型层级

```
BaseNode (抽象基类)
    │
    ├── LeafNode ─── 叶子节点，不包含子节点
    │
    └── ParentNode ─── 可包含子节点的抽象基类
            │
            ├── ContainerNode ─── 公开 AddNode/RemoveNode 方法
            │
            ├── EntityNode ─── 按类型缓存子节点（组件模式）
            │       └── RootNode ─── 节点树入口
            │
            └── DictionaryNode<TKey> ─── 按 Key 缓存子节点（键值对模式）
```

### 生命周期

```
NodeFactory.GetNode<T>()          // 从池获取节点（已销毁状态）
       │
       ├── node.Init(arg)         // 参数注入（可选），触发 OnInit
       │
ParentNode.AddChild(node)         // 挂接到父节点
       │
       ├── node.Awake()           // 初始化，重置状态
       │     └── OnAwake()        // 子类可重写
       │
       ├── node.Start()           // 启动（父节点已 Start 时自动调用）
       │     └── OnStart()        // 子类可重写
       │
       ├── ... 使用中 ...
       │
       └── node.Destroy()         // 销毁
             ├── RemoveChild()    // 从父节点脱离，触发 OnNodeRemoved 事件
             └── DestroyInternal()
                   ├── OnDestroy()          // 子类可重写
                   └── OnReturnToPool       // 自动回池
```

**关键规则：**
- `Awake` 在 `AddChild` 时自动调用，无需手动触发
- `Start` 在父节点已 Start 时自动传播给新子节点
- `Destroy` 会自动从父节点脱离并触发事件
- 节点销毁后自动回池，下次 `GetNode` 时复用

### 创建节点树

```csharp
// 方式1：简单创建（GameLauncher 自动处理）
var root = RootNode.Create();

// 方式2：使用 GameLauncher（推荐）
public class MyGameLauncher : GameLauncher
{
    async void Start()
    {
        // 在启动前添加子节点
        _root.AddNode<UIManagerNode>();
        _root.AddNode<SceneManagerNode>();

        // 启动节点树（装载 → 加载 → 启动 → 回收）
        await _root.StartupAsync();
    }
}
```

### 自定义节点

```csharp
// 叶子节点（树末端）
public class DamageNode : LeafNode
{
    protected override void OnStart()
    {
        // 获取祖先节点上的服务
        var audio = Get<IAudioService>();
    }

    protected override void OnDestroy()
    {
        // 清理资源
    }
}

// 实体节点（可包含子节点）
public class PlayerEntity : EntityNode
{
    protected override void OnAwake()
    {
        // 自动添加子组件
        AddNode<HealthComponent>(100);
        AddNode<MovementComponent>();
    }
}

// 参数注入
public class HealthComponent : LeafNode
{
    int _maxHP;

    protected override void OnInit(object arg)
    {
        if (arg is int hp) _maxHP = hp;
    }
}
```

### 参数注入

节点创建时可传入初始化参数，通过 `OnInit` 接收：

```csharp
public class HealthNode : LeafNode
{
    int _maxHP;

    protected override void OnInit(object arg)
    {
        if (arg is int hp) _maxHP = hp;
    }
}

// 创建时传参
var health = NodeFactory.GetNode<HealthNode>(100);
```

---

## EntityNode 组件模式

`EntityNode` 按类型（Type）缓存子节点，提供类似 Unity 的 GetComponent/AddComponent 体验。

### 获取子节点

```csharp
public class PlayerEntity : EntityNode { }

var player = root.AddNode<PlayerEntity>();

// 获取子节点，不存在时自动创建（autoCreate 默认为 true）
var health = player.GetNode<HealthComponent>();

// 不自动创建
var movement = player.GetNode<MovementComponent>(autoCreate: false);

// 通过接口类型获取（不会自动创建）
var updateable = player.GetNode<IUpdateable>(autoCreate: false);
```

### 添加子节点

```csharp
// 添加指定类型的子节点（已存在则直接返回）
player.AddNode<HealthComponent>(100);
player.AddNode<MovementComponent>();

// 通过运行时类型添加
player.AddNode(typeof(HealthComponent));

// 异步添加（先挂入树，再执行异步加载，完成后自动 Start）
await player.AddNodeAsync<LoadingSceneNode>();
```

### 移除子节点

```csharp
// 按类型移除
player.RemoveNode<HealthComponent>();

// 按实例移除
player.RemoveNode(healthComponent);

// 通过运行时类型移除
player.RemoveNode(typeof(HealthComponent));
```

### 服务查找

`BaseNode` 提供 `Get<T>()` 沿父链向上遍历，在所有祖先 EntityNode 中查找第一个匹配指定接口类型的节点。
通常用于获取挂载在 RootNode 下的全局服务。所有节点类型（LeafNode、EntityNode 等）均可调用。

```csharp
public class DamageLeaf : LeafNode
{
    IAudioService _audio;

    protected override void OnStart()
    {
        // 在 Start 中缓存服务引用，避免每帧遍历
        _audio = Get<IAudioService>();
    }

    void ApplyDamage(int damage)
    {
        _audio?.PlaySFX("hit");
    }
}
```

---

## DictionaryNode 键值对模式

`DictionaryNode<TKey>` 按自定义键（Key）缓存子节点，提供高效的键值对式访问。

```csharp
public class InventoryNode : DictionaryNode<string> { }

var inventory = root.AddNode<InventoryNode>();

// 添加
inventory.AddNode("sword", new ItemNode());
inventory.AddNode("shield", new ItemNode());

// 设置（键已存在时先移除旧的）
inventory.SetNode("sword", new BetterSwordNode());

// 获取
var item = inventory.GetNode<ItemNode>("sword");

// 尝试获取
if (inventory.TryGetNode("shield", out ItemNode shield))
{
    // ...
}

// 移除
inventory.RemoveNode("shield");

// 清空
inventory.ClearNodes();

// 遍历
foreach (var key in inventory.Keys)
{
    var node = inventory.GetNode<BaseNode>(key);
}
```

---

## 对象池

所有节点通过 `NodeFactory` 创建时自动使用对象池。节点调用 `Destroy()` 后自动回池。

### NodeFactory API

```csharp
// 从池获取（或创建新节点）
var node = NodeFactory.GetNode<MyNode>();

// 带参数初始化
var node = NodeFactory.GetNode<MyNode>(initData);

// 通过运行时类型获取
var node = NodeFactory.GetNode(typeof(MyNode));

// 预热池
NodeFactory.Prewarm<MyNode>(10);

// 清空池
NodeFactory.ClearPool<MyNode>();

// 清空所有池
NodeFactory.ClearAllPools();
```

### 自动回池机制

1. 节点创建时，`NodePool.Get()` 订阅节点的 `OnReturnToPool` 事件
2. 节点调用 `Destroy()` → `DestroyInternal()` 触发 `OnReturnToPool`
3. 池收到事件后将节点压入栈中，取消事件订阅
4. 下次 `GetNode` 时从栈中弹出复用

### 手动回收

通常不需要手动回收，但 `NodeFactory.ReturnNode()` 也提供了手动回收接口：

```csharp
NodeFactory.ReturnNode(node);
```

---

## 更新调度 (UpdateService)

XFramework 提供了一套 LOD 分级的更新调度系统，节点实现 `IUpdateable` 接口后自动注册到 `UpdateScheduler`。

### UpdateLOD 等级

| 等级 | 枚举值          | 更新频率         |
| ---- | --------------- | ---------------- |
| 0    | `EveryFrame`    | 每帧更新         |
| 1    | `Every2Frames`  | 每 2 帧更新一次  |
| 2    | `Every4Frames`  | 每 4 帧更新一次  |
| 3    | `Every8Frames`  | 每 8 帧更新一次  |
| 4    | `Every16Frames` | 每 16 帧更新一次 |
| 5    | `Every32Frames` | 每 32 帧更新一次 |

### 实现 IUpdateable

```csharp
public class AINode : LeafNode, IUpdateable
{
    public UpdateLOD OnUpdate(float deltaTime, float time)
    {
        // 执行 AI 逻辑
        UpdateAI(deltaTime);

        // 如果距离玩家很远，降低更新频率
        if (IsFarFromPlayer)
            return UpdateLOD.Every8Frames;

        return UpdateLOD.EveryFrame;
    }
}
```

### 时间切片算法

`UpdateScheduler` 对 LOD=1~5 的节点使用时间切片算法：

- LOD=1：每帧处理 1/2 的节点
- LOD=2：每帧处理 1/4 的节点
- LOD=3：每帧处理 1/8 的节点
- LOD=4：每帧处理 1/16 的节点
- LOD=5：每帧处理 1/32 的节点

每帧轮换一个切片，确保所有节点在 N 帧内都能被更新一次。

### 深度排序

同一 LOD 内的节点按深度升序排列，确保父节点先于子节点更新。

### 立即更新

当外部逻辑变化时需要立即响应，可调用 `RequestImmediateUpdate`：

```csharp
// 通过 UpdateBinder 调用（推荐）
_updateBinder.RequestImmediateUpdate(this, deltaTime, Time.time);
```

### UpdateBinder 自动绑定

`UpdateBinder` 自动管理节点的注册和注销，无需手动处理：

```csharp
// GameLauncher 中的典型用法
public class GameLauncher : MonoBehaviour
{
    UpdateBinder _updateBinder;

    void Awake()
    {
        _updateBinder = new UpdateBinder();
        _root = RootNode.Create();
    }

    async void Start()
    {
        _updateBinder.Bind(_root);   // 自动遍历树，注册所有 IUpdateable
        await _root.StartupAsync();   // 加载完成后自动启动节点
    }

    void Update()
    {
        _updateBinder.Tick(Time.time);
    }

    void OnDestroy()
    {
        _updateBinder.Dispose();
    }
}
```

特性：
- 节点 `Start` 完成后自动注册到 `UpdateScheduler`
- 节点从树中移除时自动注销
- 新添加的子节点自动注册
- 通过 `BaseNode.OnStarted` 事件确保**加载中的节点不会收到 Update 调用**

---

## 加载管线 (LoadService)

`StartupExtensions.StartupAsync()` 扩展方法提供了完整的异步启动管线：

```
StartupAsync()
    │
    ├── 1. 装载：扫描节点树，收集 ILoadableProvider 的加载任务
    │
    ├── 2. 加载：等待所有加载任务完成（并行执行）
    │
    ├── 3. 启动：递归启动所有节点的 OnStart
    │
    └── 4. 回收：销毁加载器，清理资源
```

### 使用方式

```csharp
// 在 GameLauncher 中
async void Start()
{
    _updateBinder.Bind(_root);
    await _root.StartupAsync();
}
```

### 定义加载任务

继承 `LoadableTask` 实现具体的加载逻辑：

```csharp
public class ConfigLoadable : LoadableTask
{
    public ConfigLoadable()
    {
        Weight = 2f; // 权重，影响进度占比
    }

    protected override async UniTask LoadAsync(CancellationToken cancellationToken)
    {
        SetDescription("Loading config...");
        SetProgress(0f);

        // 模拟加载，支持取消
        await UniTask.Delay(100, cancellationToken: cancellationToken);
        SetProgress(0.5f);

        await UniTask.Delay(100, cancellationToken: cancellationToken);
        SetProgress(1f);
    }
}
```

### 节点提供加载任务

节点实现 `ILoadableProvider` 接口：

```csharp
public class LoadingSceneNode : EntityNode, ILoadableProvider
{
    public void MountLoadables(ILoadCollector collector)
    {
        collector.AddLoadable(new ConfigLoadable());
        collector.AddLoadable(new AssetBundleLoadable());
    }
}
```

`ILoadableProvider` 由 `MountLoadables` 扩展方法自动扫描、递归注册。

### 加载进度监听

`ILoadCoordinator` 每帧轮询时通过 `OnProgressUpdate` 事件广播 `LoadProgressSnapshot` 快照：

```csharp
var loader = new LoadCoordinator();
loader.OnProgressUpdate += snapshot =>
{
    Debug.Log($"[{snapshot.CurrentTaskName}] {snapshot.Description}: {snapshot.OverallProgress:P0}");
    Debug.Log($"  Tasks: {snapshot.CompletedCount}/{snapshot.TotalTaskCount}");
};
loader.OnLoadCompleted += () => Debug.Log("All done!");
loader.OnLoadFailed += (reason) => Debug.LogError($"Failed: {reason}");
```

### LoadProgressSnapshot 字段

| 字段              | 类型   | 说明                   |
| ----------------- | ------ | ---------------------- |
| `OverallProgress` | float  | 总体进度 0~1           |
| `Description`     | string | 当前描述文字           |
| `CurrentTaskName` | string | 当前正在执行的任务名称 |
| `TotalTaskCount`  | int    | 总任务数               |
| `CompletedCount`  | int    | 已完成数               |
| `FailedCount`     | int    | 失败数                 |

### 启动阶段报告

`StartupAsync` 支持传入 `IProgress<LoadProgressSnapshot>` 接收启动各阶段的进度：

```csharp
await _root.StartupAsync(new System.Progress<LoadProgressSnapshot>(snapshot =>
{
    Debug.Log($"{snapshot.Description}: {snapshot.OverallProgress:P0}");
}));
```

启动管线分为四个阶段，每个阶段都会通过 progress 报告：

| 阶段    | Description            | 说明                       |
| ------- | ---------------------- | -------------------------- |
| 1. 装载 | "Scanning nodes..."    | 扫描节点树，收集加载任务   |
| 2. 加载 | 由各 LoadableTask 提供 | 执行所有加载任务           |
| 3. 启动 | "Starting nodes..."    | 递归启动所有节点的 OnStart |
| 4. 回收 | —                      | 销毁加载器，清理资源       |

---

## 资源服务 (AssetService)

资源服务是 XFramework 的核心服务模块，基于 YooAsset 提供统一的资源加载、实例化与生命周期管理。通过 `AssetServiceNode` 挂载到节点树中，其他节点通过 `Get<IAssetService>()` 访问。

### 架构设计

```
IAssetService (接口)           ← 外部统一访问入口
    ↑
AssetServiceNode (LeafNode)    ← 节点树服务，自动管理：
    │                              引用计数、对象池、映射表
    │                              InstanceTracker 自动防泄漏
    │
    └── YooAssetServiceImpl    ← 纯底层实现：
                                   资源加载/卸载、引用计数
                                   延迟卸载（5s）、预加载
```

### 注册 AssetServiceNode

```csharp
// 在 GameLauncher 或自定义启动器中
_root.AddNode<AssetServiceNode>();
```

### 加载资源

```csharp
public class PlayerEntity : EntityNode
{
    IAssetService _assetService;

    protected override void OnStart()
    {
        _assetService = Get<IAssetService>();
    }

    async UniTaskVoid LoadPlayerIcon()
    {
        // 异步加载（引用计数 +1）
        Texture2D icon = await _assetService.LoadAsync<Texture2D>("ui_player_icon");
        // 使用 icon...
        // 不再需要时释放
        _assetService.Release(icon);
    }
}
```

### 实例化预制体（自动对象池）

```csharp
// 简单的实例化
GameObject bullet = await _assetService.InstantiateAsync("prefabs_bullet");

// 带位置旋转
GameObject enemy = await _assetService.InstantiateAsync(
    "prefabs_enemy",
    new Vector3(0, 0, 10),
    Quaternion.identity);

// 挂载到指定父节点
GameObject ui = await _assetService.InstantiateAsync("prefabs_button", canvasTransform);

// 直接获取组件
EnemyController ctrl = await _assetService.InstantiateAsync<EnemyController>("prefabs_enemy");

// 销毁/回池
_assetService.DestroyInstance(bullet);  // 自动回池
_assetService.DestroyInstance(enemy);   // 引用计数归零时自动释放资源
```

### 加载优先级

```csharp
// 带优先级的加载（值越大优先级越高）
var highPriority = await _assetService.LoadAsync<Texture2D>("important_tex", priority: 10);
var lowPriority  = await _assetService.LoadAsync<Texture2D>("background_tex", priority: 1);
```

### 取消加载

```csharp
var cts = new CancellationTokenSource();
var texTask = _assetService.LoadAsync<Texture2D>("big_texture", cancellationToken: cts.Token);
cts.CancelAfter(5000); // 5秒超时取消
```

### 批量预加载

```csharp
// 预加载一批资源到缓存（引用计数不增加）
// 后续 LoadAsync 直接命中缓存
string[] preloadList = new[]
{
    "prefabs_player",
    "prefabs_enemy",
    "prefabs_bullet",
};
await _assetService.PreloadAllAsync(preloadList);
```

### 场景加载

```csharp
// 单场景加载（带进度回调）
await _assetService.LoadSceneAsync("scene_level1");

// 叠加场景加载
await _assetService.LoadSceneAsync("scene_ui_layer", additive: true,
    progress: p => loadingSlider.value = p);
```

### 对象池配置

```csharp
// 设置某个预制体的池最大容量
_assetService.SetPoolMaxSize("prefabs_bullet", 20);  // 池中最多保留20个闲置子弹

// 获取池状态（调试用）
var (pooled, active, maxSize) = _assetService.GetPoolStatus("prefabs_bullet");
Debug.Log($"闲置:{pooled} 活跃:{active} 池上限:{maxSize}");
```

### 回调版本 API（传统风格）

```csharp
// 加载资源（回调版本）
_assetService.LoadAsync<Texture2D>("ui_icon",
    onCompleted: tex => image.sprite = tex.ToSprite(),
    onError: error => Debug.LogError(error));

// 实例化（回调版本）
_assetService.InstantiateAsync("prefabs_bullet",
    onCompleted: go => go.transform.position = spawnPos,
    onError: error => Debug.LogError(error));
```

### 资源生命周期管理

所有由 `AssetServiceNode` 实例化的 GameObject **自动挂载 InstanceTracker**，实现自动防泄漏：

```csharp
// ✅ 通过 DestroyInstance 回池（推荐）
_assetService.DestroyInstance(bullet);
// bullet 回池，引用计数 -1，count 归零时开始5秒延迟卸载

// ✅ 直接 Destroy 也安全（自动释放引用）
Destroy(bullet);
// InstanceTracker.OnDestroy → AssetServiceNode.OnInstanceDestroyed → 引用计数 -1

// ❌ 错误做法：单独 Release 资源对象
// 正常流程由 DestroyInstance 自动完成
```

### 资源引用计数

资源服务的引用计数在两层各自独立管理：

| 层级                | 位置              | 计数粒度    | 说明                               |
| ------------------- | ----------------- | ----------- | ---------------------------------- |
| AssetServiceNode    | `_locationCounts` | 按 location | 追踪每个预制体有多少活跃实例       |
| YooAssetServiceImpl | `_refCounts`      | 按 location | 追踪每个资源的 YooAsset 句柄引用数 |

实例化流程：`InstantiateAsync → LoadAsync（_refCounts+1）→ Instance（_locationCounts+1）`

销毁流程：`DestroyInstance → _locationCounts-1 → 归零时 Release(_refCounts-1) → _refCounts归零时5秒延迟卸载`

---

## IAssetService 完整接口一览

```csharp
public interface IAssetService
{
    // ── UniTask 异步 API ──

    // 基本加载（引用计数 +1）
    UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : Object;

    // 带优先级的加载
    UniTask<T> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : Object;

    // 实例化（自动对象池）
    UniTask<GameObject> InstantiateAsync(string location, Transform parent = null);
    UniTask<GameObject> InstantiateAsync(string location, Vector3 pos, Quaternion rot, Transform parent = null);
    UniTask<T> InstantiateAsync<T>(string location, Transform parent = null) where T : Component;
    UniTask<T> InstantiateAsync<T>(string location, Vector3 pos, Quaternion rot, Transform parent = null) where T : Component;

    // 场景加载
    UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null);

    // 批量预加载
    UniTask PreloadAllAsync(IEnumerable<string> locations);

    // ── 回调版本 API ──
    void LoadAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null) where T : Object;
    void InstantiateAsync(string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null);
    void InstantiateAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component;

    // ── 对象池配置 ──
    void SetPoolMaxSize(string location, int maxSize);
    (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location);

    // ── 生命周期 ──
    void Release(Object asset);                              // 释放资源（引用计数 -1）
    void DestroyInstance(GameObject instance);               // 销毁/回收实例
    void DestroyInstance<T>(T component) where T : Component;  // Component 版本
}
```

---

## Reactive 响应式模块

基于 R3 的响应式编程模块，通过节点树隔离 R3 依赖，提供简洁、安全的响应式 API。

### 设计目标

- **隔离 R3**：所有 R3 引用仅在 `Venusy609.Xframework.Reactive` 程序集内部，外部只需引用接口
- **生命周期安全**：订阅自动绑定节点生命周期，节点销毁时自动取消订阅，杜绝内存泄漏
- **树作用域**：消息在节点树中按层级传递，支持父子节点消息隔离
- **功能完整**：覆盖 MessagePipe 全部核心功能

### 架构说明

```
┌─────────────────────────────────────────────┐
│             外部代码（仅引用接口）              │
│  IReadonlySignal<T>  IReactiveProperty<T>    │
│  IMessagePublisher    IMessageSubscriber      │
│  IEventNode           IRequestNode            │
│  IReactiveLifecycle   IMessageFilter<T>       │
└──────────────────────┬──────────────────────┘
                       │ 依赖接口，不依赖 R3
┌──────────────────────▼──────────────────────┐
│     Venusy609.Xframework.Reactive 程序集      │
│  Signal<T>  ReactiveProperty<T>              │
│  MessageBroker  EventNode  RequestNode       │
│  ReactiveLifecycle  ReactiveProperty<T>      │
│  ReactiveExtensions                          │
└──────────────────────┬──────────────────────┘
                       │ 内部使用 R3
┌──────────────────────▼──────────────────────┐
│              R3 响应式库                       │
│  Subject<T>  ReactiveProperty<T>             │
│  Observable<T>  ReplaySubject<T>             │
└─────────────────────────────────────────────┘
```

### 快速开始

#### 1. 创建事件节点

```csharp
// 在根节点下挂载事件节点
var root = new ParentNode("Root");
var events = root.AddNode<EventNode>("Events");

// 在子节点中通过 Get 沿父链查找
var child = root.AddNode<BaseNode>("Child");
var eventNode = child.Get<IEventNode>(); // 沿父链向上找到 events
```

#### 2. 发布和订阅消息

```csharp
// 定义消息类型
public struct DamageEvent
{
    public int Amount;
    public string Source;
}

// 发布消息
events.Publish(new DamageEvent { Amount = 10, Source = "Enemy" });

// 订阅消息
events.Subscribe<DamageEvent>()
    .Subscribe(damage => Debug.Log($"受到 {damage.Amount} 点伤害"))
    .AddTo(this); // 绑定到节点生命周期
```

#### 3. 带过滤的订阅

```csharp
// 只订阅伤害大于 50 的事件
events.Subscribe<DamageEvent>(d => d.Amount > 50)
    .Subscribe(d => Debug.Log($"大伤害！{d.Amount}"))
    .AddTo(this);
```

#### 4. 键值消息（Keyed）

```csharp
// 按玩家 ID 发布消息
events.Publish<int, DamageEvent>(playerId, new DamageEvent { Amount = 10 });

// 按玩家 ID 订阅
events.Subscribe<int, DamageEvent>(playerId)
    .Subscribe(d => Debug.Log($"玩家 {playerId} 受到伤害"))
    .AddTo(this);
```

### 高级功能

#### 异步消息处理器

```csharp
events.SubscribeAsync<OnPlayerDied>(async evt =>
{
    await UniTask.Delay(1000);
    await ShowDeathAnimationAsync();
    ShowGameOverUI();
}).AddTo(this);

// 带过滤的异步订阅
events.SubscribeAsync<DamageEvent>(
    d => d.Amount > 50,
    async d => await PlayBigHitEffect(d)
).AddTo(this);
```

#### 缓冲消息（Buffered）

消息发布时如果还没有订阅者，新订阅者会立即收到最近一次消息。

```csharp
// 模块 A 先发布了事件（此时还没有订阅者）
events.Publish(new ConfigLoaded());

// ... 一段时间后，模块 B 才启动并订阅
// 仍能立即收到 ConfigLoaded 事件
events.SubscribeBuffered<ConfigLoaded>()
    .Subscribe(cfg => ApplyConfig(cfg))
    .AddTo(this);

// 也支持键值缓冲
events.SubscribeBuffered<int, ConfigLoaded>(playerId)
    .Subscribe(cfg => ApplyPlayerConfig(cfg))
    .AddTo(this);
```

#### 消息过滤器（Filter Pipeline）

类似 ASP.NET Core Middleware，在消息传递时执行横切逻辑。

```csharp
// 定义日志过滤器
public class LoggingFilter<T> : IMessageFilter<T>
{
    public void Invoke(T message, Action<T> next)
    {
        Debug.Log($"[Message] {typeof(T).Name}: {message}");
        next(message); // 传递给下一个过滤器或订阅者
    }
}

// 定义权限过滤器
public class AuthFilter<T> : IMessageFilter<T>
{
    public void Invoke(T message, Action<T> next)
    {
        if (CheckPermission())
            next(message);
        else
            Debug.LogWarning($"[Auth] 无权处理 {typeof(T).Name}");
    }
}

// 注册过滤器
var eventNode = root.GetNode<EventNode>("Events");
eventNode.AddFilter(new LoggingFilter<DamageEvent>());
eventNode.AddFilter(new AuthFilter<DamageEvent>());
```

#### Request/Response 模式

```csharp
// 创建请求节点
var reqNode = root.AddNode<RequestNode>("Requests");

// 注册请求处理器
reqNode.Register<HealRequest, HealResponse>(async req =>
{
    await UniTask.Delay(100); // 模拟异步处理
    return new HealResponse(req.Amount);
});

// 发送请求并等待响应
var result = await reqNode.RequestAsync<HealRequest, HealResponse>(
    new HealRequest(50)
);
Debug.Log($"治疗量: {result.HealedAmount}");
```

#### 响应式属性

```csharp
// 创建响应式属性节点
var healthProp = root.AddNode<ReactiveProperty<int>>("Health");

// 初始化值
healthProp.Init(100);

// 监听值变化（节点本身实现了 IReactiveProperty，可直接订阅）
healthProp.Subscribe(value =>
{
    UpdateHealthBar(value);
}).AddTo(this);

// 更新值（自动通知订阅者）
healthProp.Value = 80;
```

#### 响应式生命周期

```csharp
// 为节点激活生命周期信号
var lifecycle = root.AddLifecycle();

// 订阅生命周期事件
lifecycle.OnInitializedSignal.Subscribe(_ =>
    Debug.Log("节点初始化完成")).AddTo(this);

lifecycle.OnStartedSignal.Subscribe(_ =>
    Debug.Log("节点 Start 完成")).AddTo(this);

lifecycle.OnDestroyedSignal.Subscribe(_ =>
    Debug.Log("节点销毁")).AddTo(this);
```

### 接口总览

| 接口                   | 说明                            |
| ---------------------- | ------------------------------- |
| `IReadonlySignal<T>`   | 只读信号，可订阅不可推送        |
| `ISignal<T>`           | 完整信号，可订阅也可推送        |
| `IReactiveProperty<T>` | 响应式属性，值变化自动推送      |
| `IMessagePublisher`    | 消息发布器                      |
| `IMessageSubscriber`   | 消息订阅器（含 Async/Buffered） |
| `IMessageBroker`       | 消息代理（发布+订阅）           |
| `IEventNode`           | 事件节点（树作用域消息）        |
| `IReactiveLifecycle`   | 响应式生命周期                  |
| `IMessageFilter<T>`    | 消息过滤器                      |
| `IRequestNode`         | 请求-响应节点                   |

---

## GameLauncher

`GameLauncher` 是 Unity 与节点树之间的生命周期桥接。在场景中挂载此脚本即可启动节点树。

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    public class GameLauncher : MonoBehaviour
    {
        RootNode _root;
        UpdateBinder _updateBinder;

        void Awake()
        {
            _updateBinder = new UpdateBinder();
            _root = RootNode.Create();
            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            // 加载前绑定，确保加载过程中 Update 即可正常调度
            _updateBinder.Bind(_root);
            await _root.StartupAsync();
        }

        void Update()
        {
            _updateBinder.Tick(Time.time);
        }

        void OnDestroy()
        {
            _root?.Destroy();
            _updateBinder.Dispose();
        }
    }
}
```

### 自定义启动器

继承 `GameLauncher` 添加自定义逻辑：

```csharp
public class MyGameLauncher : GameLauncher
{
    async void Start()
    {
        // 添加全局服务节点
        _root.AddNode<AssetServiceNode>();       // 资源服务
        _root.AddNode<AudioServiceNode>();       // 音频服务
        _root.AddNode<UIManagerNode>();          // UI 管理

        await _root.StartupAsync();
    }
}
```

---

## 完整示例

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.Example
{
    // 1. 定义玩家实体
    public class PlayerEntity : EntityNode, ILoadableProvider
    {
        IAssetService _assets;
        HealthComponent _health;

        protected override void OnAwake()
        {
            _health = AddNode<HealthComponent>(100);
        }

        protected override void OnStart()
        {
            _assets = Get<IAssetService>();
        }

        // 2. 加载管线：提供初始化加载任务
        void ILoadableProvider.MountLoadables(ILoadCollector collector)
        {
            collector.AddLoadable(new PlayerConfigLoadable());
        }
    }

    // 3. 定义健康值组件
    public class HealthComponent : LeafNode
    {
        int _maxHP;
        int _currentHP;

        protected override void OnInit(object arg)
        {
            if (arg is int hp)
            {
                _maxHP = hp;
                _currentHP = hp;
            }
        }

        public void TakeDamage(int damage)
        {
            _currentHP = Mathf.Max(0, _currentHP - damage);
            if (_currentHP <= 0)
                Die();
        }

        void Die()
        {
            // 通知父节点销毁
            (Parent as EntityNode)?.RemoveNode(this);
            Destroy();
        }
    }

    // 4. 定义加载任务
    public class PlayerConfigLoadable : LoadableTask
    {
        public PlayerConfigLoadable()
        {
            Name = "Player Config";
            Weight = 1f;
        }

        protected override async UniTask LoadAsync(CancellationToken cancellationToken)
        {
            SetDescription("Loading player config...");
            await UniTask.Delay(100, cancellationToken: cancellationToken);
            SetProgress(0.5f);
            await UniTask.Delay(100, cancellationToken: cancellationToken);
            SetProgress(1f);
        }
    }

    // 5. 场景挂载的启动器
    public class GameRoot : GameLauncher
    {
        async void Start()
        {
            // 注册全局资源服务
            _root.AddNode<AssetServiceNode>();

            // 异步启动（装载 → 加载 → 启动）
            await _root.StartupAsync(new System.Progress<LoadProgressSnapshot>(snapshot =>
            {
                Debug.Log($"[{snapshot.CurrentTaskName}] {snapshot.Description}: {snapshot.OverallProgress:P0}");
            }));

            // 启动后创建玩家
            var player = _root.AddNode<PlayerEntity>();

            // 使用资源服务实例化预制体
            var assets = player.Get<IAssetService>();
            GameObject go = await assets.InstantiateAsync("prefabs_player_avatar");

            // 5秒后销毁实例（自动回池）
            await UniTask.Delay(5000);
            assets.DestroyInstance(go);
        }
    }
}
```

---

## 配置与依赖

### package.json

```json
{
    "name": "com.venusy609.xframework",
    "displayName": "XFramework",
    "version": "0.1.0",
    "unity": "6000.3",
    "dependencies": {
        "com.cysharp.unitask": "2.5.10"
    }
}
```

### Assembly Definition

Core 程序集 `Venusy609.Xframework.asmdef` 依赖 `UniTask`、`YooAsset`。
Reactive 程序集 `Venusy609.Xframework.Reactive.asmdef` 依赖 `R3`、`UniTask`、`Venusy609.Xframework`，提供 R3 的隔离封装。

---

## 快速参考

### 节点创建

| 方式           | 代码                          |
| -------------- | ----------------------------- |
| 创建根节点     | `RootNode.Create()`           |
| 带参创建根节点 | `RootNode.Create(arg)`        |
| 从池获取节点   | `NodeFactory.GetNode<T>()`    |
| 从池获取带参   | `NodeFactory.GetNode<T>(arg)` |
| 预热池         | `NodeFactory.Prewarm<T>(10)`  |

### EntityNode 操作

| 操作             | 代码                                         |
| ---------------- | -------------------------------------------- |
| 获取（自动创建） | `node.GetNode<T>()`                          |
| 获取（不创建）   | `node.GetNode<T>(false)`                     |
| 添加             | `node.AddNode<T>()` / `node.AddNode<T>(arg)` |
| 异步添加         | `await node.AddNodeAsync<T>()`               |
| 移除             | `node.RemoveNode<T>()`                       |
| 服务查找         | `node.Get<IAssetService>()`                  |

### 资源服务

| 操作         | 代码                                              |
| ------------ | ------------------------------------------------- |
| 加载资源     | `await assets.LoadAsync<T>(location)`             |
| 实例化预制体 | `await assets.InstantiateAsync(location)`         |
| 销毁/回池    | `assets.DestroyInstance(go)`                      |
| 释放资源     | `assets.Release(asset)`                           |
| 预加载       | `await assets.PreloadAllAsync(locations)`         |
| 加载场景     | `await assets.LoadSceneAsync(location, additive)` |
| 设置池大小   | `assets.SetPoolMaxSize(location, 10)`             |
| 取消加载     | `CancellationTokenSource`                         |
| 加载优先级   | `assets.LoadAsync<T>(loc, priority)`              |
| 对象池状态   | `assets.GetPoolStatus(location)`                  |

### 响应式操作

| 操作         | 代码                                                |
| ------------ | --------------------------------------------------- |
| 发布消息     | `events.Publish<T>(msg)`                            |
| 订阅消息     | `events.Subscribe<T>().Subscribe(cb).AddTo(this)`   |
| 键值消息     | `events.Publish<TKey,T>(key, msg)`                  |
| 异步订阅     | `events.SubscribeAsync<T>(async cb)`                |
| 缓冲订阅     | `events.SubscribeBuffered<T>()`                     |
| 注册过滤器   | `eventNode.AddFilter(new LoggingFilter<T>())`       |
| 请求-响应    | `reqNode.RequestAsync<TReq, TRes>(req)`             |
| 响应式属性   | `node.Subscribe(cb)`                                |
| 生命周期信号 | `node.AddLifecycle().OnStartedSignal.Subscribe(cb)` |
