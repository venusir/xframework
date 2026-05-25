# XFramework / Update 模块

## 概述

Update 模块提供统一的 Update 调度服务，同时管理**节点树**与**静态服务**的更新需求。通过静态外观 `UpdateManager` 提供全局入口，基于 LOD 时间切片算法将更新负载均匀分布到各帧，避免帧消耗集中。

**命名空间**: `XFramework.XUpdate`

## 架构设计

```
Runtime/Update/
├── IUpdateable.cs                # 可更新接口（节点/静态服务实现此接口）
├── IUpdateNode.cs                # 更新节点接口
├── UpdateScheduler.cs            # 纯调度逻辑（LOD 分桶 + 时间切片）
├── UpdateManager.cs              # 静态外观（全局入口）
└── UpdateManagerExtensions.cs    # BaseNode 扩展方法
Core/Update/
└── UpdateNode.cs                 # 节点树桥梁（自动注册/注销 IUpdateable 节点）
```

## 快速使用

### 1. 节点树节点（自动注册）

实现 `IUpdateable` 的节点树节点会被 `UpdateNode` 自动注册到 `UpdateManager`，**无需手动操作**：

```csharp
using XFramework.XNode;
using XFramework.XUpdate;

public class MyNode : EntityNode, IUpdateable
{
    public UpdateLOD OnUpdate(float deltaTime, float time)
    {
        // 返回当前帧的 LOD 等级，调度器自动调整
        return UpdateLOD.Frame1;
    }
}
```

### 2. 静态服务注册

非节点树的静态服务需要手动注册：

```csharp
using XFramework.XUpdate;

public static class MyStaticService : IUpdateable
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        UpdateManager.Register(instance, depth: 0, initialLOD: UpdateLOD.Frame1);
    }

    public UpdateLOD OnUpdate(float deltaTime, float time) { ... }
}
```

## 机制说明

### LOD 分级调度

| LOD           | 更新频率 | 适用场景         |
| ------------- | -------- | ---------------- |
| `Frame1` (0)  | 每帧     | 输入、移动、物理 |
| `Frame2` (1)  | 每 2 帧  | AI 决策、寻路    |
| `Frame4` (2)  | 每 4 帧  | 动画状态机       |
| `Frame8` (3)  | 每 8 帧  | 视野检测         |
| `Frame16` (4) | 每 16 帧 | UI 刷新          |
| `Frame30` (5) | 每 30 帧 | 后台数据同步     |

`OnUpdate` 的返回值会动态调整下一次的 LOD 等级，实现自适应降频。

## 设计原则

- **静态服务独立于节点树** — `UpdateManager` 是纯静态服务，不依赖节点树生命周期
- **节点树自动注册** — 通过 `UpdateNode` 桥梁自动监听节点树事件，注册/注销 `IUpdateable` 节点
- **LOD 自适应降频** — 节点根据负载返回 LOD 等级，调度器自动调整更新频率
- **避免 GC** — 内部使用 `List<Entry>`（struct）和缓存变量，避免装箱和内存分配

## 依赖

- `XFramework.XNode` — 节点系统依赖（`IUpdateable` / `BaseNode` / `UpdateNode`）