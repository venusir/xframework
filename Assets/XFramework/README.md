# XFramework

XFramework 是一个基于树形结构的 Unity 节点系统框架。它提供了层级化的节点结构、对象池管理、组件式访问（EntityNode）、键值对访问（DictionaryNode）、LOD 分级的更新调度以及异步加载管线。

## 核心架构

```
GameLauncher (MonoBehaviour)
    │
    ├── RootNode ─── 节点树入口
    │       │
    │       ├── EntityNode ─── 组件模式（按类型缓存子节点）
    │       │       ├── LeafNode (行为/数据)
    │       │       └── CompositeNode (公开 Add/Remove)
    │       │
    │       └── DictionaryNode<TKey> ─── 键值对模式
    │
    ├── NodeUpdater ─── 自动注册/注销 IUpdateable 节点
    │       └── Updater ─── LOD 时间切片调度
    │
    └── NodeFactory ─── 统一创建/回收，自动对象池
```

## 设计哲学

- **组合优于继承** — EntityNode 按类型缓存子节点，类似 Unity 的 GetComponent/AddComponent
- **对象池内置** — 所有节点通过 `NodeFactory` 创建，`Destroy()` 后自动回池
- **更新按需降级** — `IUpdateable.OnUpdate` 返回 `UpdateLOD` 等级，自动调整更新频率
- **异步加载管线** — 节点实现 `ILoadableProvider` 装载加载任务，`StartupAsync` 统一调度

## 快速开始

### 1. 挂载 GameLauncher

在场景中创建一个 GameObject，挂载 `GameLauncher` 脚本。

### 2. 定义节点

```csharp
using XFramework;

public class PlayerNode : LeafNode
{
    int _hp;

    protected override void OnInit(object arg)
    {
        if (arg is int hp) _hp = hp;
    }

    protected override void OnAwake()
    {
        UnityEngine.Debug.Log($"PlayerNode Awake, HP: {_hp}");
    }
}
```

### 3. 在 GameLauncher 中构建节点树

```csharp
public class MyGameLauncher : GameLauncher
{
    async void Start()
    {
        // 添加子节点
        var player = _root.AddNode<PlayerNode>(100);
        await _root.StartupAsync();
    }
}
```

### 4. 实现更新

```csharp
public class MovementNode : LeafNode, IUpdateable
{
    public UpdateLOD OnUpdate(float deltaTime, float time)
    {
        // 每帧更新，返回 EveryFrame 保持每帧更新
        return UpdateLOD.EveryFrame;
    }
}
```

## 主要功能

| 功能           | 说明                                                           |
| -------------- | -------------------------------------------------------------- |
| **节点树**     | 层级化节点结构，支持深度排序、递归遍历                         |
| **对象池**     | `NodeFactory` + `NodePool<T>`，自动回池复用                    |
| **组件模式**   | `EntityNode.GetNode<T>()` / `AddNode<T>()` / `RemoveNode<T>()` |
| **键值对模式** | `DictionaryNode<TKey>` 按 Key 缓存子节点                       |
| **更新调度**   | `IUpdateable` + `UpdateLOD` 时间切片，自动 LOD 迁移            |
| **异步加载**   | `ILoadableProvider` + `LoadableBase` + `LoadingManager`        |
| **生命周期**   | Init → Awake → Start → Destroy，与 Unity 语义一致              |

## 依赖

- Unity 6000.3 或更新版本
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10+
