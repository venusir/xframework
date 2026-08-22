# XFramework / Asset 模块

## 概述

XFramework 资源管理模块提供异步资源加载、实例化、场景切换和对象池功能。基于 **YooAsset** 底层实现，通过 `IAssetManager` 接口解耦，统一使用 `UniTask` 异步风格。

**命名空间**: `XFramework.XAsset`

## 架构设计

```
Runtime/Asset/
├── IAssetManager.cs               # 资源管理器公共接口
├── AssetManager.cs                # 静态外观（全局入口）
├── AssetManagerImpl.cs            # 默认实现（对象池 + 生命周期管理）
├── YooAssetManagerImpl.cs         # YooAsset 底层适配器（多包管理）
├── AssetInitOptions.cs            # 初始化配置（包名 / 运行模式）
├── IAssetRemoteServices.cs        # 远端资源地址服务接口
├── AssetHandle.cs                 # 资源句柄（只读结构体，委托 YooAsset.AssetHandle）
├── InstanceTracker.cs             # 实例引用追踪组件（内部）
└── AssetExtensions.cs             # 节点扩展方法
```

## 快速使用

### 1. 初始化

```csharp
using XFramework.XAsset;
using XFramework.XLoader;

// 方式一：通过 Bootstrap 自动初始化（推荐）
// BootstrapNode 内包含 AssetBootstrapNode，自动处理初始化

// 方式二：手动初始化
var progress = new LoadProgress();
await AssetManager.InitializeAsync(progress);

// 方式三：注入自定义实现
AssetManager.SetInstance(myAssetManager);
```

### 2. 加载资源

`LoadAsync<T>` 返回 `AssetHandle<T>`，需通过 `using` 语句管理生命周期，块结束时自动释放引用计数：

```csharp
// 加载资源（返回 AssetHandle<T>）
using (var handle = await AssetManager.LoadAsync<GameObject>("characters/player"))
{
    var prefab = handle.Asset;
}

// 加载 TextAsset（优先级用法类似）
var handle = await AssetManager.LoadAsync<TextAsset>("configs/game_settings");
using (handle)
{
    var text = handle.Asset.text;
}
```

### 3. AssetHandle\<T\>

`AssetHandle<T>` 是一个只读结构体，直接委托 YooAsset 的 `AssetHandle`。主要属性和用法：

```csharp
using (var handle = await AssetManager.LoadAsync<TextAsset>(location))
{
    T asset = handle.Asset;     // 资源本体，加载失败时为 null
    string loc = handle.Location; // 资源定位路径
    bool valid = handle.IsValid;  // 句柄是否有效
    bool done = handle.IsDone;    // 加载是否已完成
    float prog = handle.Progress; // 加载进度 0~1
    string err = handle.LastError;// 错误信息
} // 离开 using 块时自动 Release
```

### 4. 加载并实例化

```csharp
// 加载预制体并实例化（自动走对象池）
var go = await AssetManager.InstantiateAsync("characters/player", parent: transform);

// 指定位置和旋转
var go = await AssetManager.InstantiateAsync("characters/enemy", position, rotation, parent);

// 直接获取组件
var healthBar = await AssetManager.InstantiateAsync<HealthBar>("ui/health_bar", parent: uiRoot);
```

### 5. 场景加载

```csharp
// 单场景加载
var scene = await AssetManager.LoadSceneAsync("scenes/main");

// 叠加式加载 + 进度回调
var scene = await AssetManager.LoadSceneAsync("scenes/main", additive: true, p =>
{
    Debug.Log($"场景加载进度: {p * 100}%");
});

// 注意：加载失败时返回无效的 default(Scene)，调用方需用 scene.IsValid 校验
```

### 6. 批量预加载

```csharp
var locations = new[]
{
    "characters/hero",
    "characters/enemy_soldier",
    "effects/explosion",
    "ui/loading_screen"
};
await AssetManager.PreloadAllAsync(locations);
```

### 7. 释放与回收

```csharp
// 回收实例（自动走对象池，满则销毁；回池实例保留资源引用，真正销毁时才释放）
AssetManager.DestroyInstance(gameObject);
AssetManager.DestroyInstance(component);
```

> **注意**：用户直接调用 `Object.Destroy(instance)` 的实例**不会回池**（OnDestroy 阶段操作对象池在 Unity 语义下不可靠），但会经 `InstanceTracker.OnDestroy` 自动释放资源引用，不会泄漏。

### 8. 对象池配置

```csharp
// 设置指定预制体的对象池最大容量（默认 5）
AssetManager.SetPoolMaxSize("characters/bullet", maxSize: 50);

// 查看对象池状态（调试用）
var (pooled, active, max) = AssetManager.GetPoolStatus("characters/bullet");
Debug.Log($"池中: {pooled}, 上限: {max}");
```

## 节点扩展方法

通过 `AssetExtensions`，节点树中的任意节点（实现 `IBaseNode`）可直接调用便捷方法：

```csharp
public class MyNode : EntityNode
{
    protected override async void OnStart()
    {
        base.OnStart();

        // 加载资源
        var prefab = await this.LoadAssetAsync<GameObject>("characters/player");

        // 加载并实例化
        var go = await this.InstantiateAssetAsync("characters/player", parent: transform);

        // 回收
        this.DestroyAssetInstance(go);

        // 对象池配置
        this.SetAssetPoolMaxSize("characters/bullet", 100);
    }
}
```

## 内部机制

### InstanceTracker

每次 `InstantiateAsync` 创建的实例上会自动挂载 `InstanceTracker` 组件。该组件持有 `AssetHandle<GameObject>`，确保实例存活期间底层资源引用计数 > 0。用户无需感知此组件：

- 调用 `DestroyInstance` 时，优先回池（`SetActive(false)`）；**回池不释放句柄**（资源保活，实例可随时取出复用）
- 实例真正销毁时（池满、`Dispose()` 清理、用户直接 `Object.Destroy`）才释放句柄；句柄释放幂等，多条销毁路径不会重复 Release
- 用户直接调用 `Object.Destroy` 的实例不回池，`OnDestroy` 自动释放句柄

### 对象池

默认每种预制体最多保留 5 个闲置实例，池满时新回池的实例直接销毁。可通过 `SetPoolMaxSize` 调整上限。

### 取消支持

所有公开异步 API（`InitializeAsync`、`LoadAsync`、`InstantiateAsync`、`LoadSceneAsync`、`PreloadAllAsync`）均支持 `CancellationToken` 参数，取消时抛出 `OperationCanceledException`。

## 设计原则

- **接口可替换** — 通过 `IAssetManager` 接口，可替换底层实现（当前基于 YooAsset）
- **句柄生命周期** — `AssetHandle<T>` 实现 `IDisposable`，推荐 `using` 语句管理
- **对象池** — `InstantiateAsync` 优先从池获取，减少 `Instantiate`/`Destroy` 开销
- **引用计数** — `InstanceTracker` 自动管理资源生命周期，防止过早释放
- **静态外观** — `AssetManager` 提供全局入口，任意位置可调用

## 依赖

- **YooAsset** — Git URL 依赖。由于 Unity Package Manager 不支持在 package.json 中直接引用 Git URL 作为传递依赖，第三方接入时需手动安装：
  ```
  https://github.com/tuyoogame/YooAsset.git
  ```
  在 Unity 中通过 Package Manager → "Add package from git URL..." 添加。
- `UniTask`（框架层已提供）
- `XFramework.XNode` — 节点扩展依赖 Core 模块