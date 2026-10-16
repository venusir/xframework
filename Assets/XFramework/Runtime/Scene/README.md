# Scene —— 场景加载、配对卸载与激活：只管原语，不管编排

## 概述

场景管理：**加载、配对卸载与激活**。它只管「一个场景资源怎么加载、怎么卸载」，不管「场景之间怎么编排」。

- **入口**：静态门面 `SceneManager`（`XFramework.XScene`）
- **后端**：`XAsset` 的可选能力接口 `IAssetSceneLoader`（默认实现走 YooAsset）
- **最小承诺**：加载返回一个**可卸载的句柄**，卸载走 `UnloadAsync` / `Dispose`

**这里没有什么**（都是有意的，理由见「设计取舍」）：没有场景栈、没有「返回上一场景」、没有加载界面、
没有场景间传参通道、没有场景生命周期回调。

### 与 `UnityEngine.SceneManagement.SceneManager` 同名

两者同名，同时 `using` 会 **CS0104 二义**。按需二选一：

```csharp
using XFramework.XScene;
using UnityScene = UnityEngine.SceneManagement;                  // 用 Scene / LoadSceneMode 时

// 需要显式提到 Unity 那个 SceneManager 时才起别名：
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
```

本模块的源码一律写别名、绝不裸写 `SceneManager`——因为**裸写不会报错**：本命名空间的自有类型会静默遮蔽
Unity 那个（C# 判的是类型名），于是「想调 Unity 的」变成「调了自己」。

## 快速使用

```csharp
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XAsset;   // AssetSceneHandle
using XFramework.XScene;

// 单场景加载
var main = await SceneManager.LoadSceneAsync("scenes/main");
if (main.IsDefault)
{
    // 失败：拿到的是无效句柄，本模块不抛异常
    return;
}

// 叠加加载 + 进度
var extra = await SceneManager.LoadSceneAsync("scenes/battle",
    new SceneLoadOptions(LoadSceneMode.Additive, activateAfterLoad: true),
    new Progress<SceneLoadProgress>(p => Debug.Log($"{p.Location} {p.Progress:P0}")));

// 配对卸载
await extra.UnloadAsync();   // 拿得到结果
main.Dispose();              // 即发即忘：不等结果、不抛异常
```

## 模块结构

```text
Runtime/Scene/
├── SceneManager.cs              # 静态门面：加载编排（校验 → 转发 → 激活 / 回收后处理）+ SetActiveScene / ActiveScene
├── SceneLoadOptions.cs          # 加载选项（readonly struct；全零 = Single / 不激活 / 不回收，有意义）
├── SceneLoadProgress.cs         # 进度载荷（readonly struct）
└── SceneLoadProgressRelay.cs    # internal：Action<float> → IProgress<SceneLoadProgress>（每加载一实例，无闭包无装箱）
```

四个文件里只有一个是 `internal`（进度中继），其余三个是使用方直接接触的公开面。

## API 参考

| 成员 | 说明 |
| --- | --- |
| `LoadSceneAsync(string location, SceneLoadOptions options = default, IProgress<SceneLoadProgress> progress = null, ct = default)` | 加载场景，返回 `AssetSceneHandle`。失败返回无效句柄（不抛）；`location` 为空抛 `ArgumentException`；未初始化抛 `InvalidOperationException`（消息带 `[SceneManager]` 前缀与修复提示）；取消抛 `OperationCanceledException` |
| `SetActiveScene(Scene scene)` | 设为激活场景；场景无效 / 未加载 / 引擎拒绝时返回 `false` |
| `ActiveScene` | 当前激活场景（转自 Unity，不构成场景栈） |

```csharp
public readonly struct SceneLoadOptions
{
    public readonly LoadSceneMode Mode;         // 缺省 Single
    public readonly bool ActivateAfterLoad;     // 仅 Additive 有意义；缺省 false
    public readonly bool UnloadUnusedAfterLoad; // 缺省 false
    public SceneLoadOptions(LoadSceneMode mode = LoadSceneMode.Single,
        bool activateAfterLoad = false, bool unloadUnusedAfterLoad = false);
    public static SceneLoadOptions Single { get; }
    public static SceneLoadOptions Additive { get; }
}

public readonly struct SceneLoadProgress
{
    public readonly float Progress;   // 0~1；成功时以 1f 收尾，失败路径不补发
    public readonly string Location;
}
```

句柄类型是 `XAsset` 的 `AssetSceneHandle`（场景的存活就是 YooAsset `SceneHandle` 的引用计数，
句柄只能由持有后端的一侧定义）：`IsDefault` / `IsValid` / `UnloadAsync(ct)` / `Dispose()`。

## 典型场景

### A. 切换关卡（单场景 + 加载界面 + 回收）

```csharp
loadingScreen.Show();
try
{
    var handle = await SceneManager.LoadSceneAsync("scenes/battle",
        new SceneLoadOptions(LoadSceneMode.Single, unloadUnusedAfterLoad: true),
        new Progress<SceneLoadProgress>(p => loadingScreen.SetProgress(p.Progress)),
        destroyCancellationToken);
    if (handle.IsDefault) { /* 加载失败：留在当前场景，提示玩家 */ }
}
finally
{
    loadingScreen.Hide();
}
```

### B. 叠加加载一个子系统场景（并配对卸载）

```csharp
var ui = await SceneManager.LoadSceneAsync("scenes/ui_overlay",
    SceneLoadOptions.Additive);          // 不激活：主场景仍是激活场景

// ... 用一段时间 ...

await ui.UnloadAsync();                  // 配对卸载；它是当前唯一场景时会被拒绝并告警
```

### C. 叠加加载之后要**激活**它

```csharp
var level = await SceneManager.LoadSceneAsync("scenes/level_02",
    new SceneLoadOptions(LoadSceneMode.Additive, activateAfterLoad: true));
```

**不激活是真实陷阱**：叠加加载不会改变激活场景，此后 `new GameObject()` 会挂到**旧**场景下，
并随旧场景一起被销毁。`SetActiveScene` 也可以事后手动调。

### D. 切场景后回收内存

`AssetManager.UnloadUnusedAssetsAsync()` 只回收引用计数为 0 的资源——**没释放的句柄与存活实例引用的
资源它一个都不动**。所以顺序是：先卸场景（或让 Single 模式替你卸掉旧场景），再回收。
嫌麻烦就用 `SceneLoadOptions.UnloadUnusedAfterLoad`（框架替你在加载成功后调一次）。

### E. 与 UI 共存

UI 模块已定「**切场景框架不做自动检测**」（`Runtime/UI/README.md` 的「设计取舍」）：它不反射扫跨场景
引用、不替你关面板。本模块同样不碰 UI。两条正解都在 UI 侧：

- **UIRoot 常驻**：把 UI 根放进常驻场景（或自行 `DontDestroyOnLoad`），离开前用 `UIManager.CloseAllAsync()` 收口；
- **每场景一根**：`UIRootNode` 的成对生命周期自动接管——旧场景卸载时它销毁整个管理器，新场景的节点再 `Initialize()`。

### F. 场景间传参

**框架不提供通道**——参数的形状是游戏逻辑，`Data` / 常驻服务对象已够用，多一个全局通道就多一份纪律。

配方（**时序是重点**）：

```csharp
// 发起方：必须在调用加载【之前】把参数就位
BattleEntry.SelectedLevelId = 7;
await SceneManager.LoadSceneAsync("scenes/battle");

// 接收方（新场景的 MonoBehaviour）：Awake / OnEnable / Start 里读
void Awake() => _levelId = BattleEntry.SelectedLevelId;
```

**为什么必须「加载前就位」**：`Single` 模式下新场景的 `Awake` / `OnEnable` 在加载过程中就跑完了——
`await SceneManager.LoadSceneAsync(...)` 返回时它们**早已执行**，此时再写参数已经晚了。

**不要**把参数挂在场景对象上（`GameObject` 引用的静态字段）：场景卸载后那个引用就是悬空的。
跨场景要用的东西请放在常驻对象（`DontDestroyOnLoad` 的服务对象）或 `Data` 的块里。

### G. 加载界面

**框架不内建 UI**，只给进度契约（`IProgress<SceneLoadProgress>`）与「调用方自己的 `await` 窗口」——
显示什么、什么时候显示、长什么样都是表现策略，属使用方。两条常用配方：

- **调用方自己包**：`show → await LoadSceneAsync(progress) → hide`（见「典型场景 A」），最简单，也是推荐做法；
- **常驻遮罩**：把加载遮罩放在常驻 UIRoot 里。UI 侧已为它预留排序带（`UISorting.SystemOrder = 32000`，
  注释原文就是「新手引导挖洞层、全局加载遮罩」）。

## 内部机制

- **句柄**：`AssetSceneHandle` = 实现方发号 + 场景对象。`Id == 0` 保留给无效句柄（加载失败）。
- **进度口径**：`IProgress<SceneLoadProgress>` 每帧轮询上报，**只在成功路径补发 `1f`**——失败时先报 100%
  再给一个无效句柄，会让调用方的进度条停在满格而场景根本不存在。
- **前置校验**：`LoadSceneAsync` 先检查 `AssetManager.IsInitialized`，未初始化直接抛带修复提示的异常，
  而不是返回一个「不知道为什么无效」的句柄。

## 生命周期与清理

```
LoadSceneAsync → AssetSceneHandle（场景已在场）
                  ├→ UnloadAsync(ct)  拿得到结果（false = 没卸成，见「已知限制」）
                  └→ Dispose()        即发即忘，不等结果
```

**没有「引用计数」语义**：场景不像资源那样可以有多个持有者——句柄只是一次加载的凭据。重复卸载是安全的
（第二次返回 `false`，不重放）。

## 线程契约

**全部成员限主线程**。Unity 的场景 API 与 YooAsset 的加载都是主线程 API；本模块不做线程切换，也不加断言
（越过 Asset 侧已有的主线程守卫没有意义）。

## 已知限制

- **取消不中止加载**：YooAsset 的场景加载没有中止入口，取消只让 `await` 提前抛出。此后 `XAsset` 会接力善后
  （叠加加载卸掉；单场景加载保留并告警），所以**取消之后的一小段时间里场景状态仍会变**。
- **卸载不了「当前唯一的场景」**：`UnloadAsync` 返回 `false` 并打一条告警（卸载它会让工程没有任何场景，
  Unity 与 YooAsset 都会失败）。句柄在拒绝后仍然有效。
- **`Dispose` 是即发即忘**：不等卸载完成。要确认结果请用 `UnloadAsync`。
- **叠加加载不会改变激活场景**（与 Unity 一致）：要改就用 `activateAfterLoad` 或 `SetActiveScene`。
- **`SceneLoadOptions.ActivateAfterLoad` 在 `Single` 模式下无效果**：Unity 自己会激活新场景。
- **场景地址只有 `location` 字符串一种形式**（没有 GUID / `AssetReference` 入口）——与 `XAsset` 的寻址一致。
- **本模块不认识「场景被谁卸载了」**：句柄的 `IsValid` 只是 `Id != 0 && Scene.IsValid() && Scene.isLoaded`，
  场景被 Unity 直接卸掉后它就变无效，但本模块不会主动通知你。

## 设计取舍

### 为什么不做场景栈

「谁在栈顶」「返回上一场景」= 历史 + 顺序 + 归属，那就是**生命周期树**（与 Update 否决
「组」API 是同一条推理链：组 → 组的生命周期 → 组内嵌套 → 组间顺序）——框架不预设使用方的生命周期形状。
查询只保留 `ActiveScene`——那是 Unity
本就有的事实，不是本模块的账本。要「返回上一场景」的流程，请使用方自己记住目标或自己维护那几行。

### 为什么不内建加载界面

「加载期间显示什么」是表现策略。框架内建只会与项目的美术与节奏打架，而 UI 模块已经为「全局加载遮罩」
预留了排序带——那一半答案是现成的。框架给的是**进度契约**与**一个可靠的 `await` 窗口**。

### 为什么不给场景间传参通道

参数形状是游戏逻辑。多一个带生命周期的全局通道，就多一份「什么时候清、谁把它清了」的纪律，而
`Data` / 常驻服务已经覆盖了这件事。README 给的是配方与时序，不是新 API。

### 为什么没有 `ISceneManager` 接口

本仓「有接口」的分界线是**是否存在可替换的后端**：`IAudioManager` 对应 FMOD/Wwise，`IInputProvider`
对应 Rewired。本模块的后端就是 `XAsset` 的能力接口 `IAssetSceneLoader`——再套一层只会多一份必须长期同步的
转发契约，外加第 12 处门面完备性守卫（同 Timer 的取舍）。

### 为什么 `SetActiveScene` 在门面上而不在句柄上

激活是「多个场景之间的关系」，不是「这一个场景的属性」——句柄上放它，会让人以为一个句柄能决定全局。
`handle.Scene` 交给 `SetActiveScene` 即可。

### 为什么不暴露 `suspendLoad` / `priority` / `physicsMode`

YooAsset 的 `ResourcePackage.LoadSceneAsync` 有这三个参数，本模块一律不转发。`suspendLoad`（加载到 90% 挂起、
需显式解除）尤其危险：它引入「谁负责激活」的时序，正是「不做场景栈」要躲的那类东西。等出现真实需求
（例如「必须撤销一次 `Single` 切换」）再评估。

## 与相邻模块的关系

- **`XFramework.XAsset`**：本模块的**全部**实际工作都在这里——场景的加载、句柄与卸载都由
  `IAssetSceneLoader` 能力接口承担。本模块加的是选项、进度载荷口径、激活与查询，以及「使用方该从哪进」这条边界。
  也就是说：**场景是资源，但资源系统的门面不是场景的入口**。
- **`XFramework.XUI`**：互不依赖。切场景时 UI 的两条正解见「典型场景 E」；框架不做自动检测这条不因本模块改变。
- **`XFramework.XBootstrap` / `XPipeline`**：无依赖。场景加载不需要引导阶段（门面无状态）；
  若要「启动时加载首个场景」，把它写进你自己的 `ConfigureStages` 或流程里。

### 与 `UnityEngine.SceneManagement` 的边界

| 本模块 | Unity 对应物 | 差异 |
| --- | --- | --- |
| `SceneManager.LoadSceneAsync` | `SceneManager.LoadSceneAsync` | 返回**可卸载的句柄**；失败不抛；走资源系统（YooAsset）所以有进度与资源回收 |
| `AssetSceneHandle.UnloadAsync` | `SceneManager.UnloadSceneAsync` | 一并释放底层资源句柄；拒绝卸载「当前唯一的场景」 |
| `SceneManager.SetActiveScene` / `ActiveScene` | 同名成员 | 纯转发 |
| `Scene` / `LoadSceneMode` | 同名类型 | 直接用 Unity 的，本模块不另立一套 |

## 设计原则

- **只管原语，不管编排** —— 「谁在栈顶」「返回上一场景」是生命周期树，不在本框架的射程内
- **失败不抛** —— 加载失败返回无效句柄，与 `XAsset` 同口径；只有「参数错 / 未初始化 / 取消」才抛
- **配对句柄** —— 加载与卸载是一对，`Dispose` 是即发即忘的那一半
- **零静态状态** —— 门面无状态，故无初始化、无会话复位、无诊断面板

## 依赖

- `XFramework.XAsset`（`IAssetSceneLoader` 能力接口 + `AssetSceneHandle`）
- `XFramework.XLog`（间接：日志由 `XAsset` 侧打）
- `UniTask`（框架层已提供）、`UnityEngine.SceneManagement`（Unity 核心，非可选包）
