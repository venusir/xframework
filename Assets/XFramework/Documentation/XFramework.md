# XFramework

XFramework 是一个基于树形结构的 Unity 节点系统框架。它提供了层级化的节点结构、对象池管理、组件式访问（EntityNode）、键值对访问（DictionaryNode）以及统一的工厂 API（NodeFactory）用于节点的创建和回收。

## 节点类型

| 类型                   | 说明                                                                |
| ---------------------- | ------------------------------------------------------------------- |
| `BaseNode`             | 抽象基类，定义节点生命周期（Awake → Start → Destroy）               |
| `LeafNode`             | 叶子节点，不包含子节点，通常用于存储具体数据或行为                  |
| `ParentNode`           | 可包含子节点的抽象基类，管理子节点列表                              |
| `CompositeNode`        | 继承自 ParentNode，公开 AddNode/RemoveNode 方法                     |
| `RootNode`             | 根节点，作为节点树的入口，提供便捷的 CreateNode 方法                |
| `EntityNode`           | 实体节点，按类型缓存子节点，类似 Unity 的 GetComponent/AddComponent |
| `DictionaryNode<TKey>` | 字典节点，按自定义键缓存子节点，提供高效的键值对式访问              |

## 节点生命周期

```
NodeFactory.GetNode<T>()          // 从池获取节点（已销毁状态）
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
       └── node.Destroy()         // 销毁，自动回池
             └── OnDestroy()      // 子类可重写
```

## 快速开始

### 1. 定义节点

```csharp
using XFramework;

public class PlayerNode : LeafNode
{
    int _hp;

    protected override void OnInit<TArg>(TArg arg)
    {
        if (arg is int hp)
            _hp = hp;
    }

    protected override void OnAwake()
    {
        UnityEngine.Debug.Log($"PlayerNode Awake, HP: {_hp}");
    }
}
```

### 2. 创建根节点并构建树

```csharp
var root = new RootNode();
var player = root.CreateNode<PlayerNode>(100);  // 创建并自动挂接
```

### 3. 使用 EntityNode（组件模式）

```csharp
public class EnemyEntity : EntityNode { }

var enemy = root.CreateNode<EnemyEntity>();
enemy.AddComponent<HealthComponent>(50);
enemy.AddComponent<MovementComponent>();

var health = enemy.GetComponent<HealthComponent>();
```

### 4. 使用 DictionaryNode（键值对模式）

```csharp
public class InventoryNode : DictionaryNode<string> { }

var inventory = root.CreateNode<InventoryNode>();
inventory.AddNode("sword", new ItemNode());
inventory.AddNode("shield", new ItemNode());

var item = inventory.GetNode<ItemNode>("sword");
```

### 5. 遍历子节点

```csharp
foreach (var child in root)
{
    UnityEngine.Debug.Log(child.GetType().Name);
}
```

## 对象池

所有节点通过 `NodeFactory` 创建时自动使用对象池：

```csharp
// 从池获取（或创建新节点）
var node = NodeFactory.GetNode<MyNode>();

// 带参数初始化
var node = NodeFactory.GetNode<MyNode>(initData);

// 预热池
NodeFactory.Prewarm<MyNode>(10);

// 清空池
NodeFactory.ClearPool<MyNode>();
```

节点调用 `Destroy()` 后自动回池，无需手动回收。

## 技术细节

### 依赖

- Unity 6000.3 或更新版本

### 已知限制

- 当前无已知限制
