# XFramework / Asset 模块

## 概述

XFramework 资源管理模块提供异步资源加载、实例化、场景切换和对象池功能。基于 **YooAsset** 底层实现，通过 `IAssetManager` 接口解耦，支持回调式和 `UniTask` 两种调用方式。

**命名空间**: `XFramework.XAsset`

## 架构设计

```
Runtime/Asset/
├── IAssetManager.cs               # 资源管理器公共接口
├── AssetManager.cs                # 静态外观（全局入口）
├── AssetManagerImpl.cs            # 默认实现（封装 YooAsset）
├── YooAssetManagerImpl.cs         # YooAsset 底层适配器
├── InstanceTracker.cs             # 实例引用追踪（内部）
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

### 2. 加载资源（UniTask）

```csharp
// 加载资源本体（引用计数 +1）
var prefab = await AssetManager.LoadAsync<GameObject>("characters/player");
var config = await AssetManager.LoadAsync<TextAsset>("configs/game_settings");
var sprite = await AssetManager.LoadAsync<Sprite>("ui/icons/coin");

// 带优先级加载
var prefab = await AssetManager.LoadAsync<GameObject>("characters/player", priority: 10);
```

### 3. 加载并实例化

```csharp
// 加载预制体并实例化
var go = await AssetManager.InstantiateAsync("characters/player", parent: transform);

// 指定位置和旋转
var go = await AssetManager.InstantiateAsync("characters/enemy", position, rotation, parent);

// 直接获取组件
var healthBar = await AssetManager.InstantiateAsync<HealthBar>("ui/health_bar", parent: uiRoot);
```

### 4. 场景加载

```csharp
// 加载场景
var scene = await AssetManager.LoadSceneAsync("scenes/main");

// 叠加式加载
var scene = await AssetManager.LoadSceneAsync("scenes/main", additive: true);

// 带进度回调
var scene = await AssetManager.LoadSceneAsync("scenes/main", additive: false, p => 
{
    Debug.Log($"场景加载进度: {p * 100}%");
});
```

### 5. 批量预加载

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

### 6. 释放与回收

```csharp
// 释放资源（引用计数 -1）
AssetManager.Release(loadedAsset);

// 回收实例（自动走对象池或销毁）
AssetManager.DestroyInstance(gameObject);
AssetManager.DestroyInstance(component);
```

### 7. 对象池配置

```csharp
// 设置指定预制体的对象池最大容量
AssetManager.SetPoolMaxSize("characters/bullet", maxSize: 50);

// 查看对象池状态（调试用）
var (pooled, active, max) = AssetManager.GetPoolStatus("characters/bullet");
Debug.Log($"池中: {pooled}, 活跃: {active}, 上限: {max}");
```

## 节点扩展方法

通过 `AssetExtensions`，节点树中的任意节点可以直接使用 `this.LoadAssetAsync()` 等便捷语法：

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

## 设计原则

- **接口可替换** — 通过 `IAssetManager` 接口，可替换底层实现（当前基于 YooAsset）
- **双 API 风格** — 同时支持 `UniTask` 异步和回调式
- **对象池** — `InstantiateAsync` 自动走对象池，减少 GC
- **引用计数** — 自动管理资源生命周期，防止过早释放
- **静态外观** — `AssetManager` 提供全局入口，任意位置可调用

## 依赖

- `YooAsset` — Git URL 依赖，需在 README 中引导用户手动安装
- `UniTask`（框架层已提供）
- `XFramework.XCore` — 节点扩展依赖 Core 模块
- `XFramework.XLoader` — 启动加载流程依赖 Loader 模块