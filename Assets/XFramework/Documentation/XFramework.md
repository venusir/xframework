# XFramework 文档

## 概述

XFramework 是一个基于树形结构的 Unity 节点系统框架，旨在提供一种**组合优于继承**的架构模式。它借鉴了 Unity GameObject/Component 的设计思想，但完全基于纯 C# 实现，不依赖 MonoBehaviour 继承。

### 解决的问题

- **MonoBehaviour 耦合** — 游戏逻辑分散在多个 MonoBehaviour 中，难以复用和测试
- **生命周期混乱** — Awake/Start/Update 的执行顺序依赖 Unity 的加载顺序
- **对象管理繁琐** — 频繁 Instantiate/Destroy 导致 GC 压力
- **更新调度粗放** — 所有 Update 每帧执行，无法按需降频

### 核心概念

| 概念         | 说明                                                        |
| ------------ | ----------------------------------------------------------- |
| **节点树**   | 层级化的树形结构，每个节点有深度（Depth）属性               |
| **组件模式** | EntityNode 按类型缓存子节点，类似 GetComponent/AddComponent |
| **对象池**   | 节点销毁后自动回池，减少 GC 分配                            |
| **LOD 更新** | 节点返回 UpdateLOD 等级，自动调整更新频率                   |
| **异步加载** | 节点装载加载任务，StartupAsync 统一调度                     |

---

## 节点体系

### 节点类型层级

```
BaseNode (抽象基类)
    │
    ├── LeafNode ─── 叶子节点，不包含子节点
    │
    └── ParentNode ─── 可包含子节点的抽象基类
            │
            ├── CompositeNode ─── 公开 AddNode/RemoveNode 方法
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

### 向上查找

`LeafNode` 提供 `GetNodeInParent<T>()` 沿父链向上查找：

```csharp
public class DamageNode : LeafNode
{
    void ApplyDamage(int damage)
    {
        // 向上查找父 EntityNode 上的 HealthComponent
        var health = GetNodeInParent<HealthComponent>();
        if (health != null) health.TakeDamage(damage);
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

## 更新调度

XFramework 提供了一套 LOD 分级的更新调度系统，节点实现 `IUpdateable` 接口后自动注册到 `Updater`。

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

`Updater` 对 LOD=1~5 的节点使用时间切片算法：

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
// 在节点内部
_nodeUpdater.RequestImmediateUpdate(this, deltaTime, Time.time);
```

### 自动注册

`NodeUpdater` 自动管理节点的注册和注销：

- 节点 `Start` 完成后自动注册到 `Updater`
- 节点从树中移除时自动注销
- 新添加的子节点自动注册

---

## 异步启动

`NodeUtility.StartupAsync()` 扩展方法提供了完整的异步启动管线：

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
    _nodeUpdater.Bind(_root);
    await _root.StartupAsync();
}
```

---

## 加载管线

### 定义加载任务

继承 `LoadableBase` 实现具体的加载逻辑：

```csharp
public class ConfigLoadable : LoadableBase
{
    public ConfigLoadable()
    {
        Weight = 2f; // 权重，影响进度占比
    }

    protected override async UniTask LoadAsync()
    {
        SetDescription("Loading config...");
        SetProgress(0f);

        // 模拟加载
        await UniTask.Delay(100);
        SetProgress(0.5f);

        await UniTask.Delay(100);
        SetProgress(1f);
    }
}
```

### 节点提供加载任务

节点实现 `ILoadableProvider` 接口：

```csharp
public class LoadingSceneNode : EntityNode, ILoadableProvider
{
    public void MountLoadables(ILoadableLoader loader)
    {
        loader.AddLoadable(new ConfigLoadable());
        loader.AddLoadable(new AssetBundleLoadable());
    }
}
```

### 加载进度监听

```csharp
var loader = new LoadingManager();
loader.OnProgressUpdate += (progress, desc) =>
{
    Debug.Log($"{desc}: {progress:P0}");
};
loader.OnLoadCompleted += () => Debug.Log("All done!");
loader.OnLoadFailed += (reason) => Debug.LogError($"Failed: {reason}");
```

---

## GameLauncher

`GameLauncher` 是 Unity 与节点树之间的生命周期桥接。在场景中挂载此脚本即可启动节点树。

```csharp
public class GameLauncher : MonoBehaviour
{
    void Awake()
    {
        _nodeUpdater = new NodeUpdater();
        _root = RootNode.Create();
        DontDestroyOnLoad(gameObject);
    }

    async void Start()
    {
        _nodeUpdater.Bind(_root);
        await _root.StartupAsync();
    }

    void Update()
    {
        _nodeUpdater.Tick(Time.time);
    }

    void OnDestroy()
    {
        _root?.Destroy();
        _nodeUpdater.Dispose();
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
        // 在启动前添加子节点
        _root.AddNode<UIManagerNode>();
        _root.AddNode<SceneManagerNode>();

        await _root.StartupAsync();
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

Runtime 程序集 `Venusy609.Xframework.asmdef` 依赖 `UniTask`。

---

## 示例

完整示例见 `Samples/Example/SampleExample.cs`。
