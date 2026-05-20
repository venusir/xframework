# XFramework / Loader 模块

## 概述

XFramework 加载器模块提供节点树的启动加载管线。通过 `ILoadable` 接口标记需要加载的节点，由 `ILoader` 调度器按 Phase 分组调度执行。模块通过 `StartupExtensions` 提供一键启动方法，封装了「装载 → 加载 → 启动 → 回收」完整流程。

**命名空间**: `XFramework.XLoader`

**核心理念**：节点声明「我需要加载什么」（实现 `ILoadable`），加载器负责「按什么顺序加载」（Phase 分组调度）。

## 架构设计

```
Runtime/Loader/
├── ILoadable.cs                  # 可加载接口（节点实现此接口声明加载任务）
├── ILoader.cs                    # 加载器接口（调度入口）
├── Loader.cs                     # 加载器内部实现（internal，按 Phase 分组调度）
├── LoadProgress.cs               # 加载进度数据结构
└── StartupExtensions.cs          # 启动扩展方法（一键启动 + 节点树遍历）
```

## 核心概念

### Phase 调度机制

加载器按 `ILoadable.Phase` 值对任务进行分组：

- **相同 Phase 的任务并行执行**
- **不同 Phase 的任务按值从小到大串行执行**

```
Phase 0: [AssetBootstrapNode]──────────────┐
Phase 10: [ConfigNode, AudioNode]──────────┤  并行
Phase 50: [NetworkNode]────────────────────┤  串行（等 Phase 10 完成后开始）
Phase 100: [GameplayNode]──────────────────┤  串行（等 Phase 50 完成后开始）
```

### 生命周期流程

```
StartupAsync(rootNode)
  ├── 1. 装载 (CollectLoadables)
  │      └── 递归扫描节点树，收集所有 ILoadable
  ├── 2. 加载 (LoadAsync)
  │      └── 按 Phase 分组调度，输出进度事件
  ├── 3. 启动 (OnStart)
  │      └── 递归调用所有节点的 OnStart
  └── 4. 回收 (Destroy)
         └── 销毁加载器，清理资源
```

## 快速使用

### 1. 定义可加载节点

```csharp
using XFramework.XLoader;
using XFramework.XCore;
using Cysharp.Threading.Tasks;
using System.Threading;

public class ConfigBootstrapNode : EntityNode, ILoadable
{
    // 加载阶段号（越小越先执行）
    public int Phase => 10;

    public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
    {
        progress.SetState(LoadState.Loading);
        progress.Description = "加载配置表...";

        // 执行加载逻辑
        await LoadConfigFilesAsync(cancellationToken);

        progress.SetState(LoadState.Completed);
        progress.Description = "配置表加载完成";
    }
}
```

### 2. 一键启动节点树

```csharp
using XFramework.XLoader;
using XFramework.XCore;

// 构建节点树
var root = RootNode.Create();
root.AddNode<BootstrapNode>();
root.AddNode<ConfigBootstrapNode>();
root.AddNode<GameplayNode>();

// 一键启动（自动执行装载→加载→启动→回收）
await root.StartupAsync();

// 带进度回调
await root.StartupAsync(new Progress<LoadProgress>(p =>
{
    Debug.Log($"启动进度: {p.OverallProgress * 100}% - {p.Description}");
}));
```

### 3. 手动管理加载流程

```csharp
using XFramework.XLoader;

// 创建加载器
ILoader loader = new Loader();

// 手动注册可加载节点（通常由 CollectLoadables 自动完成）
loader.AddLoadable(new AssetBootstrapNode());
loader.AddLoadable(new ConfigBootstrapNode());

// 监听进度
loader.OnProgressUpdate += p =>
{
    Debug.Log($"进度: {p.OverallProgress:P1} {p.Description}");
};

// 监听完成
loader.OnLoadCompleted += () =>
{
    Debug.Log("所有加载任务完成！");
};

// 监听失败
loader.OnLoadFailed += error =>
{
    Debug.LogError($"加载失败: {error}");
};

// 执行加载
await loader.LoadAsync();

// 清理
loader.Destroy();
```

### 4. 进度数据结构

```csharp
public class LoadProgress
{
    public LoadState State;          // 当前状态: Pending / Loading / Completed / Failed
    public float OverallProgress;    // 总体进度 0~1
    public float Progress;           // 当前任务进度 0~1
    public string Description;       // 当前描述文本
    public string Name;              // 当前任务名称（取自 ILoadable 的类型名）
    public string CurrentTaskName;   // 当前正在执行的任务名称
    public int TotalTaskCount;       // 总任务数
    public int CompletedCount;       // 已完成任务数
    public int FailedCount;          // 失败任务数
}
```

## 内置 Phase 约定

为保持一致性，框架内置模块使用以下 Phase 值：

| Phase | 模块         | 说明                     |
| ----- | ------------ | ------------------------ |
| 0     | Asset        | 资源管理器初始化（最早） |
| 10    | Localization | 本地化数据加载           |
| 20    | Config       | 游戏配置表加载           |
| 30    | Message      | 消息总线初始化           |
| 40    | Lock         | 锁服务初始化             |
| 90+   | 用户自定义   | 建议业务模块从此范围开始 |

## 设计原则

- **声明式加载** — 节点实现 `ILoadable` 声明加载需求，无需关心调度逻辑
- **Phase 分组调度** — 相同 Phase 并行，不同 Phase 串行，兼顾性能与依赖顺序
- **失败即停** — 任意任务失败后取消所有同 Phase 及后续 Phase 的任务
- **一次性调度** — 每个 `ILoader` 实例仅执行一次加载，用完即销毁
- **进度广播** — 每帧通过 `OnProgressUpdate` 推送加载进度，方便 UI 展示

## 依赖

- `XFramework.XCore` — 节点系统（`IParentNode` 遍历、`BaseNode` 生命周期）
- `UniTask`（框架层已提供）