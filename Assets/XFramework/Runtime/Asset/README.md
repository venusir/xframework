# Asset —— 资源管理（YooAsset 后端）：异步加载 / 实例化 / 对象池 / 场景 / 热更

## 概述

**命名空间**: `XFramework.XAsset`

XFramework 资源管理模块提供异步资源加载、实例化、场景切换和对象池功能。基于 **YooAsset** 底层实现，通过 `IAssetManager` 接口解耦，统一使用 `UniTask` 异步风格。

## 快速使用

### 1. 初始化

```csharp
using XFramework.XAsset;

// 方式一：交给 Bootstrap 引导管线（推荐）
// Bootstrap.RegisterDefaults() 会登记 AssetBootstrapStage（Phase 0）自动处理初始化；
// 也可单独登记：Bootstrap.Register(new AssetBootstrapStage());

// 方式二：手动初始化
await AssetManager.InitializeAsync();

// 带步骤进度（可选）：AssetInitReport 为模块自持的中性载荷（Progress + Description）
await AssetManager.InitializeAsync(options: null,
    progress: new Progress<AssetInitReport>(r => Debug.Log($"{r.Progress:P0} {r.Description}")));

// 方式三：注入自定义实现
AssetManager.SetInstance(myAssetManager);
```

> 初始化配置：`AssetInitOptions.AutoReclaimOnLowMemory`（默认 true）监听 `Application.lowMemory`，自动释放对象池闲置实例并卸载未使用资源；需自管回收策略的项目可置为 false。
>
> **加密资源**：`AssetInitOptions.DecryptionServices`（实现 `IAssetDecryptionServices`，与打包侧的加密方式对应）——
> Offline 与 Host 两种模式都会接线，编辑器 / 单机模式同样能读加密资源；不传即不加密（默认）。

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
    TextAsset asset = handle.Asset;  // 资源本体，加载失败时为 null
    string loc = handle.Location;  // 资源定位路径
    bool valid = handle.IsValid;  // 句柄是否有效
    bool done = handle.IsDone;    // 加载是否已完成
    float prog = handle.Progress; // 加载进度 0~1
    string err = handle.LastError;// 错误信息
    AssetOperationStatus st = handle.Status; // 状态：None / Processing / Succeed / Failed
} // 离开 using 块时自动 Release
```

> **`Status` 的类型是框架自己的 `AssetOperationStatus`**（不是 YooAsset 的 `EOperationStatus`）——
> 四个句柄（`AssetHandle<T>` / `RawFileHandle` / `SubAssetsHandle` / `AssetDownloaderHandle`）同型，
> 四个成员与上游一一对应，语义与取值时机不变。

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
// 单场景加载：返回可卸载的句柄
var main = await AssetManager.LoadSceneAsync("scenes/main");
if (main.IsDefault)
{
    // 失败时拿到的是无效句柄（本模块不抛异常）
    return;
}

// 叠加式加载 + 进度回调（需要 using UnityEngine.SceneManagement; 取 LoadSceneMode）
var extra = await AssetManager.LoadSceneAsync("scenes/battle", LoadSceneMode.Additive, p =>
{
    Debug.Log($"场景加载进度: {p * 100}%");
});

// 配对卸载
await extra.UnloadAsync();   // 拿得到结果
main.Dispose();              // 即发即忘：不等结果、不抛异常
```

**本成员是后端能力转发**（同 `ClearPool`）：它只管「加载 / 卸载一个场景资源」。场景之间的编排、
加载界面、场景间传参都不在这里——那些属使用方的游戏流程，框架不预设。能力缺失（第三方实现未实现
`IAssetSceneLoader`）时返回无效句柄并告警一次。

> **使用方请走 `XFramework.XScene`**（`Runtime/Scene/README.md`）：`SceneManager.LoadSceneAsync` 在这条
> 能力之上补了选项（模式 / 叠加后激活 / 切完回收）、进度载荷口径与前置校验，并承载「加载界面、场景间
> 传参、与 UI 共存」三条配方。本节保留的是**资源层**视角。

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
// 1. 初始化时声明 Host 模式与远端服务（注意形参顺序：options 在前、progress 在后）
var options = new AssetInitOptions
{
    PlayMode = AssetPlayMode.Host,
    RemoteServices = new MyRemoteServices(), // 实现 IAssetRemoteServices
};
await AssetManager.InitializeAsync(options,
    progress: new Progress<AssetInitReport>(r => Debug.Log($"{r.Progress:P0} {r.Description}")));

// 2. 请求最新版本号
string version = await AssetManager.RequestPackageVersionAsync();

// 3. 预检该版本清单能不能加载（可选；**预检只做可用性检查**——不激活清单，
//    也不改变下载器基于哪份清单）
await AssetManager.PreDownloadContentAsync(version);

// 4. 激活新版本清单——**必须在下载之前**：下载器按「当前激活清单」算待下内容，
//    晚于下载调用等于按旧清单算，会得到一份空荡的待下列表
await AssetManager.UpdatePackageManifestAsync(version);

// 5a. 一键下载全部更新（带进度）
bool success = await AssetManager.DownloadAssetsAsync(progress: p => Debug.Log($"{p * 100}%"));

// 5b. 或使用下载器句柄精细控制（暂停/恢复/取消/事件）
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

// 6. 下载完成后再加载新版本资源（激活后、下载完成前加载，未下到的 bundle 会走按需下载）
```

> 版本号可随时通过 `AssetManager.GetPackageVersion()` 读取。
> 下载器句柄的 `Dispose()` 仅解除事件订阅，不中止下载；需要中止时调用 `Cancel()`。
> **顺序不是风格问题是正确性问题**：见下方「热更链路状态流转」与已知限制第 12 条。

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

## 模块结构

```text
Runtime/Asset/
├── AssetManager.cs                # 静态外观（全局入口）
├── IAssetManager.cs               # 资源管理器公共接口
├── AssetManagerImpl.cs            # 默认实现（对象池 + 生命周期管理）
├── YooAssetManagerImpl.cs         # YooAsset 底层适配器（多包管理 + 热更链路 + 远端 / 解密适配）
├── IAssetRemoteServices.cs        # 远端资源地址服务接口（主站 / 备站）
├── IAssetDecryptionServices.cs    # 资源解密服务接口（镜像 YooAsset 的 IDecryptionServices）
├── IAssetPoolController.cs        # 资源实例池的受控清理能力（可选能力接口）
├── IAssetSceneLoader.cs           # 场景加载与卸载能力（可选能力接口；2026-10-13 从 IAssetManager 移出）
├── AssetSceneHandle.cs            # 场景句柄（只读结构体；公开构造，Dispose = 即发即忘卸载）
├── ISceneLoadOperation.cs         # internal：场景加载操作的最小面（测试缝）
├── ISceneLoadSource.cs            # internal：场景加载来源缝（测试缝）
├── YooAssetSceneLoadSource.cs     # internal：缝的默认实现，包住 YooAsset.SceneHandle
├── AssetInitOptions.cs            # 初始化配置（包名 / 运行模式 / 低内存回收 / 远端地址服务 / 解密服务）
├── AssetInitReport.cs             # 初始化进度载荷（readonly struct，中性，不依赖管线）
├── AssetInitProgressRelay.cs      # 进度直写桥：AssetInitReport → PipelineStageContext
├── AssetBootstrapStage.cs         # 引导阶段：资源管理器初始化（Phase 0，最早）
├── AssetOperationStatus.cs        # 资源操作状态（四句柄共用）+ 与 YooAsset.EOperationStatus 的唯一换算
├── AssetHandle.cs                 # 资源句柄（只读结构体，委托 YooAsset.AssetHandle）
├── AssetDownloaderHandle.cs       # 下载器句柄（事件 / 控制 / 等待）
├── SubAssetsHandle.cs             # 子资源句柄（图集 / 多 Sprite）
├── RawFileHandle.cs               # 原始文件句柄（txt / json / 二进制）
└── InstanceTracker.cs             # internal：实例引用追踪组件（挂在实例上保活）
```

二十二个文件：标 `internal` 的四个是测试缝与内部组件（`InstanceTracker` 由 `AddComponent` 挂到实例上，使用方不直接接触），其余是公开面。

## 行为契约

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

## 架构设计

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

## API 参考

全部方法均位于 `AssetManager` 静态门面（`XFramework.XAsset`），以下为速查。`ct` 表示 `CancellationToken`。

### 初始化

| 方法 | 说明 |
| --- | --- |
| `InitializeAsync(AssetInitOptions options = null, IProgress<AssetInitReport> progress = null, ct)` | 初始化默认包。`options` 为 null 时使用默认配置（默认包 + 离线模式）。重复调用 LogWarning 忽略；并发调用共享同一初始化任务 |
| `InitializePackageAsync(AssetInitOptions options, IProgress<AssetInitReport> progress = null, ct)` | 追加初始化额外资源包（多包场景），需先完成默认包初始化 |
| `SetInstance(IAssetManager manager)` | 注入自定义实现（替换底层 / 单元测试） |
| `Destroy()` | 销毁全局管理器，释放全部资源 |
| `IsInitialized` | 是否已初始化 |
| `AssetInitOptions.AutoReclaimOnLowMemory = true` | 初始化选项字段：低内存自动回收开关（监听 `Application.lowMemory`，默认开启） |
| `AssetInitOptions.DecryptionServices` | 初始化选项字段：解密服务（`IAssetDecryptionServices`，镜像 YooAsset 的 `IDecryptionServices`）；`null` = 不加密。Offline / Host 都接线 |

### 异步加载

| 方法 | 说明 |
| --- | --- |
| `LoadAsync<T>(string location, ct)` | 异步加载资源，返回 `AssetHandle<T>`，用 `using` 管理生命周期 |
| `LoadAllAsync<T>(IReadOnlyList<string> locations, ct)` | 批量加载同类型资源，按序返回句柄数组（每项可单独 `using`）；单项失败为 default 句柄，不整体失败；取消时已加载句柄自动释放 |
| `LoadAsync<T>(string location, int priority, ct)` | 带优先级加载（数值越大越先加载） |
| `InstantiateAsync(string location, Transform parent = null, ct)` | 加载并实例化，自动走对象池，引用生命周期自动管理 |
| `InstantiateAsync(string location, Vector3 pos, Quaternion rot, Transform parent = null, ct)` | 指定位置/旋转实例化 |
| `InstantiateAsync<T>(...（同上两重载）)` | 实例化并直接取组件；预制体缺该组件时销毁实例并返回 null |
| `LoadSceneAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, Action<float> progress = null, ct)` | 加载场景（单/叠加），返回**可卸载的句柄** `AssetSceneHandle`；失败返回无效句柄（用 `IsDefault` 校验）。能力接口 `IAssetSceneLoader` 的后端转发（形态同 `ClearPool`） |
| `UnloadSceneAsync(AssetSceneHandle handle, ct)` | 卸载句柄对应的场景；失败（句柄失效 / 场景已不在场 / 它是当前唯一场景 / 能力缺失 / 门面未初始化）返回 `false` **而不抛** |

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

## 典型场景

### 加载模式选择

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
| 场景切换 | `LoadSceneAsync` | `LoadSceneMode.Additive` 叠加加载；返回的句柄配 `UnloadAsync`/`Dispose` 成对使用 |
| 内存告警 / 切关卡 | `UnloadUnusedAssetsAsync` | 只回收引用计数为 0 的资源 |
| 远端资源更新 | Host 模式热更链 | 版本 → 预检 → 下载 → 激活（见「快速使用」7） |
| 预判断资源状态 | `CheckLocationValid` / `IsNeedDownloadFromRemote` | 纯查询，失败不抛异常 |

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

- 包由内部字典管理，默认包名为 `DefaultPackage`；**主包名可用 `InitializeAsync` 的 `AssetInitOptions.PackageName` 改**（null/空白时用默认名，须与 YooAsset 构建侧的包名一致）；`InitializePackageAsync(options)` 可追加任意数量的额外包（各包独立版本与清单）——**它不改主包**
- **包复用语义**：包已初始化成功时跳过 `InitializeAsync`，直接刷新版本与清单——因此 `Destroy()` 后重新初始化不会失败（早期版本对已初始化包再次初始化会抛异常）
- **加载族固定作用于主包**（`LoadAsync` / `InstantiateAsync` / `PreloadAllAsync` / `LoadSceneAsync`）；包级操作（热更 / 卸载 / 查询 / 下载）通过 `packageName = null` 参数显式指定。「主包」= `InitializeAsync` 那次初始化出来的包（见上一条）
- **刻意不提供「包切换」API**：对象池与活跃计数以 location 为唯一键，状态化切换包会串资源

### 热更链路状态流转

```
RequestPackageVersionAsync → 检查远端新版本号
  → PreDownloadContentAsync → 预检该版本清单能不能加载（不激活，可选）
  → UpdatePackageManifestAsync → 激活新版本（**下载器的基准在这里切换**）
  → CreateDownloader / DownloadAssetsAsync → 下载（Pause/Resume/Cancel 可控制）
  → 此后 LoadAsync 加载新版本资源
```

- **激活必须在下载之前**：`CreateDownloader` 按「当前**激活**清单」算待下内容（`ActiveManifest`），
  而预检**不激活**任何东西。把激活放到下载之后，待下列表就是按旧清单算的——预下载**静默空转**，
  要等激活后才按需下载。
- 断点续传由 YooAsset 缓存机制保证；下载失败的资源不会破坏缓存
- 下载器 `Progress` 在无待下载内容（`TotalDownloadBytes == 0`）时特判返回 1f，避免除零 NaN

### 取消支持

所有公开异步 API 均支持 `CancellationToken`，取消时抛出 `OperationCanceledException`：

- `DownloadAssetsAsync` / `AssetDownloaderHandle.WaitAsync`：取消时自动调用 `Cancel()` 中止下载后抛出
- 其余加载类 API：取消后返回的句柄状态可能半途，需自行 `Dispose` 收尾

## 线程契约

**本模块按主线程使用设计。** `AssetManager` 门面的全部成员（含 `InitializeAsync` / `SetInstance` / `Destroy` /
`IsInitialized`）、底层 `IAssetManager` 实现与 `InstanceTracker` 的活跃计数都必须在 Unity 主线程访问。

**为什么不能跨线程**：门面的初始化协调是一段**无锁的「检查-再赋值」序列**（创建者选举、等待者登记、代际号递增），
没有任何一处能容忍两个线程同时进入——并发发起会造出两个实例（前一个不被释放），后台线程调用则可能在任意时刻
撕裂协议状态。Editor 下三个入口有断言（越线程记一条 `[AssetManager]` 前缀的 Error，每个会话只报一次）；
Release 下断言整段不编译，**没有任何运行期保护**，请按契约使用。

**回调落在主线程**：下载器事件（`ProgressChanged` / `Completed` / `DownloadError`）由 YooAsset 的
`YooAssetsDriver.Update` 驱动；所有 `await` 的续体经 UniTask 的 PlayerLoop 推进——两者都在主线程。

需要从后台线程加载资源时，请先 `await UniTask.SwitchToMainThread(cancellationToken)` 再进入本模块。

## 性能代价

- 句柄均为 `readonly struct`（`AssetHandle<T>` / `SubAssetsHandle` / `RawFileHandle`），传递零分配
- 对象池复用实例，减少 `Instantiate`/`Destroy` 开销；回池实例取出无需重新加载
- `GetPoolStatus` 的活跃计数由 `OnEnable`/`OnDisable` 统计，无每帧轮询
- 加载族路径无每帧闭包分配（`PreloadAllAsync` 的进度聚合闭包仅在启动期一次性调用）
- **同步 API 的代价**：`LoadSync`/`InstantiateSync` 阻塞调用线程；远端未下载时可能长时间卡住，运行时高频路径禁用

## 已知限制

1. **并发初始化只有首个调用者的参数生效**：`InitializeAsync` 被并发调用时共享同一次初始化，
   `options`、`progress` 与取消令牌**一律取首个调用者**，其余调用者的这三项被忽略且不告警
   （取消令牌与参数的这一约定写在 XML 文档里；若各调用方需要不同的包配置，请自行串行化）。
2. **`Destroy()` / `SetInstance()` 会作废在途初始化**：在途初始化的创建者与加入者都会收到
   `InvalidOperationException`（消息含修复提示）。这是刻意的——`await` 正常返回必须等价于「已初始化」。
3. **额外包并发初始化不受保护**：`InitializePackageAsync` 建议在默认包初始化完成后**顺序**调用，
   并发初始化多个额外包没有任何保护。
4. **句柄重复 `Dispose` 是安全的**（YooAsset 对已释放句柄提前返回），代价是它会打一条
   **不带 `[AssetManager]` 前缀**的 YooLogger 警告——日志里出现那条约等于「有人重复释放了句柄」。
5. **`WaitAsync` 在 `Begin()` 之前会抛异常**：YooAsset 的下载器在启动前既不完成也不失败，
   本方法没有超时，拒绝比永久挂起好排查。
6. **`UnloadUnusedAssetsAsync` 不报告失败**（YooAsset 的卸载操作没有失败路径）；
   它只回收引用计数为 0 的资源，未 `Dispose` 的句柄与存活实例引用的资源不受影响。
7. **文件系统根目录（`packageRoot`）没有出口**：它在 YooAsset 里是**每个文件系统各一个**
   （内置包根 / 缓存根独立），不在 `AssetInitOptions` 里。要改包根、换用自定义文件系统类或接管
   其它 YooAsset 参数，只能整体替换 `IAssetManager`。加载寻址同样只有 `location` 字符串一种形式
   （没有 GUID / `AssetReference` 入口）。
8. **取消不中止场景加载**：YooAsset 的场景加载没有对外可用的中止入口，取消只让 `await` 提前抛出，
   底层照跑完、场景最终仍会进来。框架会接力到后台收干净——**Additive 卸掉**（世界回到调用前）、
   **Single 保留并告警**（卸载它会让工程一个场景都没有，比留着更糟）。调用方不必自己收拾，
   但要知道「取消之后的一小段时间里场景状态还会变」。
9. **卸载不了「当前唯一的场景」**：`UnloadSceneAsync` 会提前挡掉并告警——卸载它会让工程没有任何场景，
   Unity 与 YooAsset 都会失败，提前拒绝给的是能看懂的提示。句柄在拒绝后仍然有效。
10. **`AssetSceneHandle.Dispose` 是即发即忘**：不等卸载完成、不抛异常（异常只记一条日志）。
    要确认卸载结果请用 `UnloadAsync`。
11. **`IAssetSceneLoader` 是可选能力**：第三方替换 `IAssetManager` 时若未实现它，场景加载返回无效句柄、
    卸载返回 `false`，两者各告警一次。句柄是后端发的凭据，**不要自己伪造**——实现方会校验句柄与
    自己登记的加载是否对得上，对不上的一律拒绝。
12. **预检（`PreDownloadContentAsync`）只回答「该版本清单能不能加载」**：它**不激活**清单，
    **也不改变 `CreateDownloader` 的基准**（下载器永远按当前激活清单算）。所以「先预检、后下载」
    拿不到新版本的内容，得**先激活再下载**；也**拿不到**「要下多少 MB / 多少个文件」——
    框架没有把预检产出的待下清单透出来。

## 接口承诺到哪为止

1. **加载失败不抛异常**：`LoadAsync` / `LoadSubAssetsAsync` / `LoadRawFileAsync` 失败返回 `default` 句柄
   （`IsValid == false`、`Asset == null`，原因在 `LastError`）；`InstantiateAsync` 失败返回 `null`；
   `LoadSceneAsync` 失败返回无效的 `default(AssetSceneHandle)`（用 `IsDefault` 校验），`UnloadSceneAsync`
   失败返回 `false`——**卸载路径刻意不抛**，因为它会被 `AssetSceneHandle.Dispose` 调到，而 `Dispose`
   里的异常没人接得住。这是本模块的刻意约定
   ——对照 Addressables 的「失败抛异常」，选它是因为 Unity 加载失败多为可预期的（地址写错、资源未下载）。
   **例外**：初始化与热更链路（`RequestPackageVersionAsync` / `UpdatePackageManifestAsync` /
   `PreDownloadContentAsync`）失败**抛异常**；`DownloadAssetsAsync` 失败返回 `false`、取消抛 `OperationCanceledException`。
2. **句柄的生命周期就是 YooAsset 的引用计数**：`Dispose` 即 `Release`，`using` 之外没有别的魔法；
   池化的实例在回池时**保留**句柄（资源保活），真正销毁时才释放。
3. **`Destroy()` 不卸载 YooAsset 侧的包与已加载资源**：它释放托管实例与包引用表，YooAsset 的
   包注册与 bundle 缓存**保持存活**——这正是「`Destroy()` 后可重新初始化复用」的前提。
   要真正回收内存请用 `UnloadUnusedAssetsAsync`（配合 `ClearPool` 清掉闲置实例）。
4. **`PreloadAllAsync` 不占引用计数**，只把内容拉进缓存；需要持有请用 `LoadAllAsync`（其句柄必须逐项释放）。

## 与相邻模块的关系

- **XFramework.XBootstrap / XFramework.XPipeline**（启动引导与相位分组编排）：`AssetBootstrapStage`（`IBootstrapStage`，Phase 0）负责自动初始化；初始化步骤进度经 `AssetInitReport`（本模块自持中性载荷）上报，由引导阶段经 `AssetInitProgressRelay` 直写管线阶段上下文
- **XFramework.XPool**：本模块内置的**实例对象池**（按 location 键、容量上限）只服务 `InstantiateAsync`；`PoolManager` 是通用对象池（任意类型池化），职责不同，两者不混用

## 设计原则

- **接口可替换** — 通过 `IAssetManager` 接口，可替换底层实现（当前基于 YooAsset）
- **句柄生命周期** — `AssetHandle<T>` 实现 `IDisposable`，推荐 `using` 语句管理
- **对象池** — `InstantiateAsync` 优先从池获取，减少 `Instantiate`/`Destroy` 开销
- **引用计数** — `InstanceTracker` 自动管理资源生命周期，防止过早释放
- **静态外观** — `AssetManager` 提供全局入口，任意位置可调用
- **多包隔离** — 加载族固定默认包，包级操作显式指定 `packageName`，杜绝状态化切换串资源

## 依赖

- **YooAsset** — Git URL 依赖。由于 Unity Package Manager 不支持在 package.json 中直接引用 Git URL 作为传递依赖，第三方接入时需手动安装：
  ```
  https://github.com/tuyoogame/YooAsset.git
  ```
  在 Unity 中通过 Package Manager → "Add package from git URL..." 添加。
- `XFramework.XBootstrap` / `XFramework.XPipeline` — `AssetBootstrapStage`（Phase 0）实现引导阶段契约与相位分组编排
- `UniTask`（框架层已提供）
