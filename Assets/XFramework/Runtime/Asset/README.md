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
├── AssetInitOptions.cs            # 初始化配置（包名 / 运行模式 / 低内存回收）
├── IAssetRemoteServices.cs        # 远端资源地址服务接口
├── AssetHandle.cs                 # 资源句柄（只读结构体，委托 YooAsset.AssetHandle）
├── AssetDownloaderHandle.cs       # 下载器句柄（事件/控制/等待）
├── SubAssetsHandle.cs             # 子资源句柄（图集/多 Sprite）
├── RawFileHandle.cs               # 原始文件句柄（txt/json/二进制）
└── InstanceTracker.cs             # 实例引用追踪组件（内部）
```

### 分层调用链

```
调用方
  → AssetManager        静态门面，全局唯一入口（所有静态方法都委托给实例）
  → IAssetManager       公共接口，可替换
  → AssetManagerImpl    默认实现：对象池 + 实例引用计数管理
  → YooAssetManagerImpl YooAsset 适配器：多包管理 + 热更链路
  → YooAsset            底层资源管线
```

- `AssetManager` 与 `IAssetManager` 是对外唯一公开层；`AssetManagerImpl` / `YooAssetManagerImpl` 为 internal，第三方不直接使用
- 替换底层实现：实现 `IAssetManager` 后通过 `AssetManager.SetInstance(...)` 注入（也用于单元测试）

## 快速使用

### 1. 初始化

```csharp
using XFramework.XAsset;
using XFramework.XPipeline;

// 方式一：通过节点树自动初始化（推荐）
// ServiceInitializerNode 内包含 AssetBootstrapNode，自动处理初始化

// 方式二：手动初始化
var progress = new LoadProgress();
await AssetManager.InitializeAsync(progress);

// 方式三：注入自定义实现
AssetManager.SetInstance(myAssetManager);
```

> 初始化配置：`AssetInitOptions.AutoReclaimOnLowMemory`（默认 true）监听 `Application.lowMemory`，自动释放对象池闲置实例并卸载未使用资源；需自管回收策略的项目可置为 false。

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

// 带进度回调：按完成数量聚合 0~1，全部完成后补发 1f
await AssetManager.PreloadAllAsync(locations, p => Debug.Log($"预加载进度: {p * 100}%"));
```

### 7. 热更链路（Host 模式）

Host 模式下资源分内置与远端两份，加载远端资源前需先下载。典型链路：

```csharp
// 1. 初始化时声明 Host 模式与远端服务
var options = new AssetInitOptions
{
    PlayMode = AssetPlayMode.Host,
    RemoteServices = new MyRemoteServices(), // 实现 IAssetRemoteServices
};
await AssetManager.InitializeAsync(progress, options);

// 2. 请求最新版本号
string version = await AssetManager.RequestPackageVersionAsync();

// 3. 预检新版本清单（可选，不激活）
await AssetManager.PreDownloadContentAsync(version);

// 4a. 一键下载全部更新（带进度）
bool success = await AssetManager.DownloadAssetsAsync(progress: p => Debug.Log($"{p * 100}%"));

// 4b. 或使用下载器句柄精细控制（暂停/恢复/取消/事件）
using (var downloader = AssetManager.CreateDownloader(new[] { "hot" }))
{
    downloader.ProgressChanged += p => Debug.Log($"{p * 100}%");
    downloader.Completed += ok => Debug.Log($"下载完成: {ok}");
    downloader.DownloadError += (file, err) => Debug.LogError($"下载失败 {file}: {err}");

    downloader.Begin();
    downloader.Pause();   // 暂停
    downloader.Resume();  // 恢复
    bool ok = await downloader.WaitAsync();
}

// 5. 激活新版本清单（之后 LoadAsync 加载新版本资源）
await AssetManager.UpdatePackageManifestAsync(version);
```

> 版本号可随时通过 `AssetManager.GetPackageVersion()` 读取。
> 下载器句柄的 `Dispose()` 仅解除事件订阅，不中止下载；需要中止时调用 `Cancel()`。

### 8. 卸载与查询

```csharp
// 卸载包中所有未使用资源（内存告警 / 关卡切换后回收）
await AssetManager.UnloadUnusedAssetsAsync();

// 尝试卸载单个未使用资源（仍被引用时无效果）
AssetManager.TryUnloadUnusedAsset("characters/old_hero");

// 查询资源定位路径是否合法（可被 LoadAsync 加载）
bool valid = AssetManager.CheckLocationValid("characters/player");

// 查询资源是否来自远端（Host 模式下预判断是否需要先下载）
bool needDownload = AssetManager.IsNeedDownloadFromRemote("characters/player");
```

> **多包场景**：以上方法均支持 `packageName` 参数指定资源包，为 null 时作用于默认包。
> 卸载只会回收引用计数为 0 的资源——`AssetHandle<T>` 未 Dispose 的资源保持存活。

### 9. 释放与回收

```csharp
// 回收实例（自动走对象池，满则销毁；回池实例保留资源引用，真正销毁时才释放）
AssetManager.DestroyInstance(gameObject);
AssetManager.DestroyInstance(component);
```

> **注意**：用户直接调用 `Object.Destroy(instance)` 的实例**不会回池**（OnDestroy 阶段操作对象池在 Unity 语义下不可靠），但会经 `InstanceTracker.OnDestroy` 自动释放资源引用，不会泄漏。

### 10. 对象池配置

```csharp
// 设置指定预制体的对象池最大容量（默认 5）
AssetManager.SetPoolMaxSize("characters/bullet", maxSize: 50);

// 查看对象池状态（调试用）
var (pooled, active, max) = AssetManager.GetPoolStatus("characters/bullet");
Debug.Log($"池中: {pooled}, 上限: {max}");
```

### 11. 同步加载

`LoadSync<T>` / `InstantiateSync` 阻塞当前线程直至加载完成，仅建议在启动画面、静态初始化等不阻塞 UI 的场景使用：

```csharp
// 同步加载（返回 AssetHandle<T>，同样用 using 管理生命周期）
using (var handle = AssetManager.LoadSync<TextAsset>("configs/game_settings"))
{
    var text = handle.Asset.text;
}

// 同步实例化（自动走对象池，与异步路径共用）
var go = AssetManager.InstantiateSync("characters/player", parent: transform);
```

> **风险**：同步加载会卡住调用线程；若资源需从远端下载，可能长时间阻塞。运行时高频路径请使用异步 API。

### 12. 子资源与 RawFile

```csharp
// 子资源：图集、多 Sprite 贴图等（加载整个主资源及其子资源）
using (var handle = await AssetManager.LoadSubAssetsAsync("ui/icon_atlas"))
{
    var icons = handle.GetSubAssets<Sprite>();
    var red = handle.GetSubAsset<Sprite>("icon_red");
}

// RawFile：txt/json/二进制等原始文件（不经过 Unity 资源管线）
using (var handle = await AssetManager.LoadRawFileAsync("configs/server_list"))
{
    string text = handle.GetRawFileText();
    byte[] data = handle.GetRawFileData();
    string path = handle.GetRawFilePath();
}
```

> 子资源与 RawFile 均提供 `LoadXxxSync` 同步版本（同上节风险说明）。

## 完整 API 参考

全部方法均位于 `AssetManager` 静态门面（`XFramework.XAsset`），以下为速查。`ct` 表示 `CancellationToken`。

### 初始化

| 方法 | 说明 |
| --- | --- |
| `InitializeAsync(LoadProgress progress, AssetInitOptions options = null, ct)` | 初始化默认包。`options` 为 null 时使用默认配置（默认包 + 离线模式）。重复调用 LogWarning 忽略；并发调用共享同一初始化任务 |
| `InitializePackageAsync(AssetInitOptions options, LoadProgress progress, ct)` | 追加初始化额外资源包（多包场景），需先完成默认包初始化 |
| `SetInstance(IAssetManager manager)` | 注入自定义实现（替换底层 / 单元测试） |
| `Destroy()` | 销毁全局管理器，释放全部资源 |
| `IsInitialized` | 是否已初始化 |
| `AssetInitOptions.AutoReclaimOnLowMemory = true` | 初始化选项字段：低内存自动回收开关（监听 `Application.lowMemory`，默认开启） |

### 异步加载

| 方法 | 说明 |
| --- | --- |
| `LoadAsync<T>(string location, ct)` | 异步加载资源，返回 `AssetHandle<T>`，用 `using` 管理生命周期 |
| `LoadAllAsync<T>(IReadOnlyList<string> locations, ct)` | 批量加载同类型资源，按序返回句柄数组（每项可单独 `using`）；单项失败为 default 句柄，不整体失败；取消时已加载句柄自动释放 |
| `LoadAsync<T>(string location, int priority, ct)` | 带优先级加载（数值越大越先加载） |
| `InstantiateAsync(string location, Transform parent = null, ct)` | 加载并实例化，自动走对象池，引用生命周期自动管理 |
| `InstantiateAsync(string location, Vector3 pos, Quaternion rot, Transform parent = null, ct)` | 指定位置/旋转实例化 |
| `InstantiateAsync<T>(...（同上两重载）)` | 实例化并直接取组件；预制体缺该组件时销毁实例并返回 null |
| `LoadSceneAsync(string location, bool additive = false, Action<float> progress = null, ct)` | 加载场景（单/叠加），失败返回无效 `Scene`（用 `IsValid` 校验） |

### 同步加载（会阻塞调用线程）

| 方法 | 说明 |
| --- | --- |
| `LoadSync<T>(string location)` | 同步加载资源，返回 `AssetHandle<T>`（同样用 `using` 管理） |
| `InstantiateSync(string location, Transform parent = null)` | 同步实例化（与异步路径共用对象池） |
| `InstantiateSync(string location, Vector3 pos, Quaternion rot, Transform parent = null)` | 指定位置/旋转同步实例化 |

### 子资源与 RawFile

| 方法 | 说明 |
| --- | --- |
| `LoadSubAssetsAsync(string location, ct)` / `LoadSubAssetsSync(string location)` | 加载图集、多 Sprite 贴图等子资源集合，返回 `SubAssetsHandle` |
| `LoadRawFileAsync(string location, ct)` / `LoadRawFileSync(string location)` | 加载 txt/json/二进制原始文件（不经 Unity 资源管线），返回 `RawFileHandle` |

### 预加载

| 方法 | 说明 |
| --- | --- |
| `PreloadAllAsync(IEnumerable<string> locations, Action<float> progress = null, ct)` | 批量预热资源缓存，不增加引用计数；进度按完成数量聚合 0~1，完成补发 1f。需持有句柄的批量加载用 `LoadAllAsync`（见「异步加载」表） |

### 热更（Host 模式，均支持 `packageName = null` 默认包）

| 方法 | 说明 |
| --- | --- |
| `RequestPackageVersionAsync(string packageName = null, ct)` | 请求远端最新版本号 |
| `PreDownloadContentAsync(string version, string packageName = null, ct)` | 预检版本清单（不激活） |
| `CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8, int failedRetryCount = 3, string packageName = null)` | 创建下载器句柄，`Begin()` 启动，支持 Pause/Resume/Cancel |
| `DownloadAssetsAsync(string[] tags = null, Action<float> progress = null, string packageName = null, ct)` | 一键下载（创建 + 启动 + 聚合进度），返回是否全部成功 |
| `UpdatePackageManifestAsync(string version, string packageName = null, ct)` | 激活新版本清单（之后 `LoadAsync` 加载新版本） |
| `GetPackageVersion(string packageName = null)` | 读取当前激活版本号 |

### 卸载与查询（均支持 `packageName = null` 默认包）

| 方法 | 说明 |
| --- | --- |
| `UnloadUnusedAssetsAsync(string packageName = null, ct)` | 卸载全部未使用资源（引用计数为 0） |
| `TryUnloadUnusedAsset(string location, string packageName = null)` | 尝试卸载单个未使用资源，仍被引用时无效果 |
| `CheckLocationValid(string location, string packageName = null)` | 资源定位路径是否合法（可被 `LoadAsync` 加载） |
| `IsNeedDownloadFromRemote(string location, string packageName = null)` | 资源是否来自远端（Host 模式预判断） |

### 对象池与实例管理

| 方法 | 说明 |
| --- | --- |
| `SetPoolMaxSize(string location, int maxSize)` | 设置对象池容量（下限 1，默认 5） |
| `GetPoolStatus(string location)` | 返回 `(pooledCount, activeCount, maxPoolSize)` 三元组（调试用） |
| `DestroyInstance(GameObject instance)` / `DestroyInstance<T>(T component)` | 回池（池未满）或销毁实例，引用自动释放 |

### 节点扩展（`AssetExtensions`，`XFramework.XNode`）

定义于 Node 模块（命名空间 `XFramework.XNode`，文件 `Runtime/Node/AssetExtensions.cs`）。节点内便捷糖，全部委托门面：`LoadAssetAsync<T>`（2 重载）、`InstantiateAssetAsync`（4 重载）、`LoadSceneAssetAsync`、`PreloadAssetsAsync`、`SetAssetPoolMaxSize`、`GetAssetPoolStatus`、`DestroyAssetInstance`（2 重载）。仅限节点内使用（`this` 必须是 `IBaseNode`）；非节点代码直接用门面。

## 加载模式选择

按场景选择 API 的决策清单：

| 场景 | 推荐 API | 说明 |
| --- | --- | --- |
| 运行时按需加载 | `LoadAsync<T>` + `using` 句柄 | 主流路径，退出 using 自动释放引用 |
| 预制体实例化 | `InstantiateAsync` | 自动对象池，实例销毁自动回收引用 |
| 启动预热常用资源 | `PreloadAllAsync` | 只加热缓存，不占引用计数，进度可反馈 UI |
| 批量加载持有句柄 | `LoadAllAsync<T>` | 按序返回句柄数组（逐项 using），单项失败为 default，取消自动释放已完成项 |
| 静态初始化 / 启动画面 | `LoadSync` / `InstantiateSync` | 阻塞调用线程，注意远端未下载时的卡顿风险 |
| 图集 / 多 Sprite | `LoadSubAssetsAsync` | 主资源 + 子资源整体加载 |
| txt / json / 二进制 | `LoadRawFileAsync` | 不经过 Unity 资源管线 |
| 场景切换 | `LoadSceneAsync` | `additive: true` 叠加加载 |
| 内存告警 / 切关卡 | `UnloadUnusedAssetsAsync` | 只回收引用计数为 0 的资源 |
| 远端资源更新 | Host 模式热更链 | 版本 → 预检 → 下载 → 激活（见快速使用 7） |
| 预判断资源状态 | `CheckLocationValid` / `IsNeedDownloadFromRemote` | 纯查询，失败不抛异常 |

## 节点扩展方法

`AssetExtensions`（`XFramework.XNode`，定义于 Node 模块）为节点树中的任意节点（实现 `IBaseNode`）提供便捷方法，全部委托 `AssetManager` 门面：

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

> **注意**：`OnStart` 是同步生命周期回调，不能写成 `async void`（节点不是 Unity 生命周期入口，异常无兜底）。异步操作放入私有 `async UniTask` 方法后用 `Forget()` 启动。
> `EntityNode` 是纯 C# 节点，没有 `transform` 成员；需要指定挂载父物体时，把外部 `Transform` 传给 `InstantiateAssetAsync` 的 `parent` 参数。
> 加载过程可传入 `this.DestroyCancellationToken`，节点销毁时自动中止。

## 内部机制

### 生命周期与引用计数模型

资源的存活由引用计数控制，计数增减的**唯一**来源是句柄：

```
LoadAsync<T> → 返回 AssetHandle<T>（引用计数 +1）
                 └→ using 块退出 / 调用 Dispose()（引用计数 -1）
```

计数为 0 且 bundle 无其他引用时，资源可被 `UnloadUnusedAssetsAsync` 回收。因此**句柄必须用 `using` 管理或显式 `Dispose`**，否则资源永不释放。

`InstantiateAsync` 的引用计数流转（实例场景）：

```
LoadAsync<GameObject> → 句柄（+1）
  └→ Instantiate → 挂载 InstanceTracker.SetHandle(句柄)
        ├→ DestroyInstance → 回池成功：SetActive(false)，句柄保留（资源保活）
        │     └→ 再次取出：SetActive(true)，直接复用，无需重新加载
        └→ 真正销毁（池满 / Dispose() 清理 / 用户直接 Object.Destroy）
              └→ DisposeHandle：句柄释放（-1，幂等）
```

`InstanceTracker` 是挂在实例上的内部组件，持有 `AssetHandle<GameObject>`，确保实例存活期间引用计数 > 0。其计数一致性由四条路径保证，互不重复：

| 路径 | 动作 |
| --- | --- |
| `SetHandle`（实例化时挂载） | 补记实例活跃计数 |
| 回池（`SetActive(false)`） | 活跃计数 -1（资源引用保留） |
| 取出复用（`SetActive(true)`） | 活跃计数 +1 |
| 真正销毁 | 释放句柄；幂等，多条销毁路径不重复 Release |

用户直接调用 `Object.Destroy(instance)` 的实例**不会回池**（OnDestroy 阶段操作对象池在 Unity 语义下不可靠），但 `OnDestroy` 会自动释放句柄，不会泄漏。

### 对象池

- 按 `location` 为键，每种预制体独立成池；默认容量 5，`SetPoolMaxSize` 调整（下限 1）
- 池满时新回池的实例直接销毁；回池实例**保留资源引用**（保活语义），取出即用，无需重新加载
- `GetPoolStatus` 三元组：`pooledCount` 池中闲置数 / `activeCount` 活跃数（由 InstanceTracker 按 `SetActive` 状态实时统计，零轮询）/ `maxPoolSize` 容量

### 多包架构

- 包由内部字典管理，默认包名为 `DefaultPackage`；`InitializePackageAsync(options)` 可追加任意数量的额外包（各包独立版本与清单）
- **包复用语义**：包已初始化成功时跳过 `InitializeAsync`，直接刷新版本与清单——因此 `Destroy()` 后重新初始化不会失败（早期版本对已初始化包再次初始化会抛异常）
- **加载族固定作用于默认包**（`LoadAsync` / `InstantiateAsync` / `PreloadAllAsync` / `LoadSceneAsync`）；包级操作（热更 / 卸载 / 查询 / 下载）通过 `packageName = null` 参数显式指定
- **刻意不提供「包切换」API**：对象池与活跃计数以 location 为唯一键，状态化切换包会串资源

### 热更链路状态流转

```
RequestPackageVersionAsync → 检查远端新版本号
  → PreDownloadContentAsync → 预检新版本清单（不激活，可选）
  → CreateDownloader / DownloadAssetsAsync → 下载（Pause/Resume/Cancel 可控制）
  → UpdatePackageManifestAsync → 激活新版本（此后 LoadAsync 加载新版本资源）
```

- 断点续传由 YooAsset 缓存机制保证；下载失败的资源不会破坏缓存
- 下载器 `Progress` 在无待下载内容（`TotalDownloadBytes == 0`）时特判返回 1f，避免除零 NaN

### 取消支持

所有公开异步 API 均支持 `CancellationToken`，取消时抛出 `OperationCanceledException`：

- `DownloadAssetsAsync` / `AssetDownloaderHandle.WaitAsync`：取消时自动调用 `Cancel()` 中止下载后抛出
- 其余加载类 API：取消后返回的句柄状态可能半途，需自行 `Dispose` 收尾

### GC 与性能

- 句柄均为 `readonly struct`（`AssetHandle<T>` / `SubAssetsHandle` / `RawFileHandle`），传递零分配
- 对象池复用实例，减少 `Instantiate`/`Destroy` 开销；回池实例取出无需重新加载
- `GetPoolStatus` 的活跃计数由 `OnEnable`/`OnDisable` 统计，无每帧轮询
- 加载族路径无每帧闭包分配（`PreloadAllAsync` 的进度聚合闭包仅在启动期一次性调用）
- **同步 API 的代价**：`LoadSync`/`InstantiateSync` 阻塞调用线程；远端未下载时可能长时间卡住，运行时高频路径禁用

## 注意事项与常见误区

1. **句柄不释放 = 资源泄漏（最常见的坑）**：`LoadAsync` 返回的句柄必须用 `using` 管理或显式 `Dispose`，否则引用计数不为 0，`UnloadUnusedAssetsAsync` 永远回收不掉，内存持续增长。
2. **加载失败不抛异常**：`LoadAsync` 失败返回 `default` 句柄（`IsValid == false`、`Asset == null`），`InstantiateAsync` 失败返回 `null`。失败原因在 `LastError`，调用方需校验返回值。
3. **`DestroyInstance` 回池不等于释放资源**：回池实例保留资源引用（保活），只有真正销毁（池满 / `Dispose()` / `Object.Destroy`）才释放。回池的资源量按 `GetPoolStatus` 的 `pooledCount` 观察。
4. **直接 `Object.Destroy` 不回池**：实例不会进对象池复用，但 `OnDestroy` 会释放资源引用，不会泄漏——代价只是少了复用。
5. **同步加载卡顿**：`LoadSync` / `InstantiateSync` 阻塞调用线程；Host 模式下资源未下载时可能长时间卡住，运行时高频路径禁用。
6. **`UnloadUnusedAssetsAsync` 只回收引用计数为 0 的资源**：未 `Dispose` 的句柄、存活实例引用的资源不受影响——它不解决句柄泄漏，只回收真正闲置的资源。
7. **下载器 `Dispose()` 不中止下载**：只解除事件订阅；需要中止调用 `Cancel()`（已下载部分保留，断点续传）。
8. **Offline 模式没有热更**：`RequestPackageVersionAsync` / `DownloadAssetsAsync` 等版本与下载类 API 仅 Host 模式有意义；Host 模式必须先 `InitializeAsync` 再走热更链。
9. **多包时加载族固定默认包**：`LoadAsync` 等不带 `packageName` 参数；包级操作（热更 / 卸载 / 查询）需显式传 `packageName`，默认包可省略。
10. **`PreloadAllAsync` 取消时进度可能不收敛**：取消抛出 `OperationCanceledException` 前，进度回调可能停在中间值不补发 1f；UI 侧应以异常处理收尾。
11. **低内存自动回收**：`AssetInitOptions.AutoReclaimOnLowMemory`（默认 true）监听 `Application.lowMemory`，触发时清空对象池闲置实例并卸载全部包中未使用资源（引用计数为 0），随后请求 `Resources.UnloadUnusedAssets`。Host 模式下卸载只释放内存，bundle 缓存文件保留，重新加载无需下载。对低内存事件敏感或自管回收策略的项目可置为 false。
12. **`LoadAllAsync` 的句柄必须逐项释放**：返回的句柄数组每个元素占引用计数（与 `LoadAsync` 相同），需逐项 `using` 或 `Dispose`；单项失败为 `default` 句柄（`IsValid == false`，释放安全）。批量预热请用 `PreloadAllAsync`（不占引用计数）。
13. **额外包初始化应顺序进行**：`InitializePackageAsync` 建议在默认包初始化完成后依次调用，并发初始化多个额外包不受保护。

## 与相关模块的关系

- **XFramework.XPipeline**（加载应用）：`AssetBootstrapNode`（Phase 0）负责自动初始化，进度经 `LoadProgress` 上报（只读 `Progress` / `OverallProgress` / `State` / `Description`，成员详情见 Pipeline 模块 README「加载应用」章节）
- **XFramework.XNode**：`AssetExtensions` 定义于 Node 模块（`XFramework.XNode` 命名空间），内部委托本模块 `AssetManager` 门面——依赖方向 Node → Asset；节点代码可直接调用，非节点代码（MonoBehaviour、纯 C# 类）直接用门面
- **XFramework.XPool**：本模块内置的**实例对象池**（按 location 键、容量上限）只服务 `InstantiateAsync`；`PoolManager` 是通用对象池（任意类型池化），职责不同，两者不混用

## 设计原则

- **接口可替换** — 通过 `IAssetManager` 接口，可替换底层实现（当前基于 YooAsset）
- **句柄生命周期** — `AssetHandle<T>` 实现 `IDisposable`，推荐 `using` 语句管理
- **对象池** — `InstantiateAsync` 优先从池获取，减少 `Instantiate`/`Destroy` 开销
- **引用计数** — `InstanceTracker` 自动管理资源生命周期，防止过早释放
- **静态外观** — `AssetManager` 提供全局入口，任意位置可调用
- **多包隔离** — 加载族固定默认包，包级操作显式指定 `packageName`，杜绝状态化切换串资源

## 版本记录

| 版本 | 说明 |
| --- | --- |
| 2026-08 | 并发初始化修复（共享任务 + 代际号）、低内存自动回收（AutoReclaimOnLowMemory）、LoadAllAsync 批量持有句柄、未初始化消息统一为 [模块] 前缀中文文案 |
| 2026-08 | 接口扩展：多包架构与初始化配置、卸载控制与存在性查询、热更链路（下载器句柄 + 一键下载）、预加载进度回调、同步加载 / 子资源 / RawFile |
| 2026-08 | 池语义修正（方案 A）：回池保活——回池保留句柄、真正销毁才释放；补齐取消支持与池状态统计 |

详细变更见 git log。

## 依赖

- **YooAsset** — Git URL 依赖。由于 Unity Package Manager 不支持在 package.json 中直接引用 Git URL 作为传递依赖，第三方接入时需手动安装：
  ```
  https://github.com/tuyoogame/YooAsset.git
  ```
  在 Unity 中通过 Package Manager → "Add package from git URL..." 添加。
- `UniTask`（框架层已提供）
- `XFramework.XNode` — `AssetExtensions` 节点扩展定义于 Node 模块，委托本模块 `AssetManager` 门面（依赖方向：Node → Asset，见「与相关模块的关系」）