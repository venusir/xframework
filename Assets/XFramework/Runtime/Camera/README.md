# Camera —— 相机激活 / 反激活 / 过渡的统一入口（后端中立）

## 概述

`XCamera` 提供「切到哪台相机 / 何时过渡 / 当前输出相机是谁」的统一入口，游戏代码不必直接依赖
Cinemachine 的 vcam 与 Brain 类型。**它只做状态与过渡**：跟随、瞄准、震屏、缩放、碰撞、约束、Dolly、
Target Group、Timeline 仍归 Cinemachine；投影、Near-Far、Culling Mask、RenderTexture、Viewport 仍归
`UnityEngine.Camera`（经 `OutputCamera` 直接使用）。

**命名空间**：`XFramework.XCamera`。

**默认实现** `UnityCameraManager` 直接驱动 `UnityEngine.Camera`：一次一台活动相机，过渡插值位置 / 旋转 /
FOV（正交为 `orthographicSize`）。它假设相机是静态的且投影模式一致；移动 / 跟随 / 震屏请用 Cinemachine。
Cinemachine 的一等实现由**独立可选扩展包** `com.venusy609.xframework.cinemachine` 提供（单独安装，
不随核心包发布）。

## 快速使用

```csharp
using System.Collections.Generic;
using UnityEngine;
using XFramework.XCamera;

// 1. 注册相机（一次）
var manager = new UnityCameraManager(new Dictionary<string, Camera>
{
    ["main"] = mainCamera,
    ["battle"] = battleCamera,
});

// 2. 注入门面（门面取得所有权）
CameraManager.Initialize(manager);

// 3. 切相机
CameraManager.Activate("main");                                  // 立即切换
CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut); // 过渡

// 4. 直接用输出相机做世界↔屏幕 / 射线 / FOV
var camera = CameraManager.OutputCamera;
var screen = camera.WorldToScreenPoint(target.position);
```

Cinemachine 3.x 项目：安装独立扩展包后，把上面的 `UnityCameraManager` 换成扩展包里的
`CinemachineCameraManager`，其余调用不变。安装与接线见扩展包自己的 README。

## 模块结构

```text
Runtime/Camera/
├── ICameraManager.cs      # 公开：后端契约（激活 / 反激活 / 过渡 / 查询）
├── CameraManager.cs       # 公开：静态门面（转发 + 生命周期 + 会话复位）
├── UnityCameraManager.cs  # 公开：默认实现（UnityEngine.Camera 注册表 + 位姿/FOV 插值）
├── CameraBlendStyle.cs    # 公开：过渡样式枚举
└── README.md
```

## 行为契约

- **仅主线程**；注册表与活动状态不是线程安全的。
- `ActiveCameraId` 在过渡期间返回**目标**；没有活动相机返回 `null`。`OutputCamera` 同步返回目标的输出相机。
- `CameraIds` 是**实时视图**（`Dictionary` 的键集合）：枚举期间不要注册 / 注销。
- 未知 id：`Activate` / `Deactivate` / `TransitionTo` 告警并 no-op，**不抛**；`null` id 抛 `ArgumentNullException`
  （`IsActive(null)` 返回 `false`）。
- `Activate` = cut，并取消在途过渡；`TransitionTo` 期间再次调用从**当前视觉状态**重新开始；
  `duration <= 0` 或 `CameraBlendStyle.Cut` 等价于 cut。
- `Deactivate` 是否回退由后端定义：`UnityCameraManager` **不回退**（活动 id 变 `null`）；Cinemachine 由
  Brain 的优先级队列决定回退。
- `UnityCameraManager`：构造时不改变相机位置 / FOV，但会把所有已注册相机置为关闭；需要调用 `Activate`
  或 `TransitionTo` 确立活动相机。`Activate` / `TransitionTo` 会关闭其它已注册相机；未注册相机不受影响。
- `UnityCameraManager` 的过渡：起点是源相机（或在途过渡的当前视觉状态），终点是目标相机的**原始**位姿 /
  FOV；只插值位置 / 旋转 / FOV（正交为 `orthographicSize`），**不混合** culling mask / background /
  RenderTexture。源与目标投影模式不一致时直接 cut。
- 过渡完成时目标相机恢复到它自己的原始位姿 / FOV；中断时被驱动的相机会恢复原始位姿，避免下次激活停在
  插值途中。
- `Initialize` 取得实现所有权；`Destroy` 在实现是 `IDisposable` 时调用 `Dispose`；
  `ResetForNewSession` 只丢引用、不 Dispose。

## 架构设计

三层：静态门面 `CameraManager` → 后端契约 `ICameraManager` → 具体实现。

- **门面**：生命周期（`Initialize` / `Destroy` / `IsInitialized`）+ 同名转发 + 会话复位；扁平，不分组。
- **契约**：只统一**运行期操作**；相机注册（`Camera`、Cinemachine vcam 或自定义句柄）是后端相关的，
  由实现类自己提供。
- **默认实现**：`UnityCameraManager`，直接操作 `UnityEngine.Camera`，过渡用 `UpdateManager` 的
  LateUpdate 时机懒驱动。
- **Cinemachine**：独立扩展包实现同一契约，不进入核心包。

## API 参考

| 入口 | 行为 |
|---|---|
| `Initialize(ICameraManager)` | 注入实现；重复调用告警后忽略；`null` 抛 `ArgumentNullException` |
| `Destroy()` | 清空门面；实现是 `IDisposable` 时释放它 |
| `IsInitialized` | 是否有实现 |
| `OutputCamera` | 当前输出相机；无则 `null` |
| `CameraIds` | 已注册 id 的实时视图 |
| `ActiveCameraId` | 活动 / 过渡中的目标 id；无则 `null` |
| `IsTransitioning` | 是否有过渡在途 |
| `Activate(string)` | 立即切换（cut），取消在途过渡 |
| `Deactivate(string)` | 反激活指定相机；回退语义由后端定义 |
| `TransitionTo(string, float, CameraBlendStyle)` | 过渡；`duration <= 0` / `Cut` 等价于 cut |
| `IsActive(string)` | 是否为当前活动（过渡目标也算） |

`UnityCameraManager` 额外公开：构造（`IReadOnlyDictionary<string, Camera>`）、`Register(string, Camera)`、
`Unregister(string)`、`Dispose()`。注册是后端相关的，门面不转发它们。

## 典型场景

### 1. 无 Cinemachine 的简单项目

用 `UnityCameraManager` + 静态相机；切场景或进战斗时调 `Activate` / `TransitionTo`。过渡走
`UpdateManager` 的 LateUpdate，暂停（`timeScale <= 0`）会冻结过渡。

### 2. Cinemachine 3.x 项目

安装独立扩展包 `com.venusy609.xframework.cinemachine`，用 `CinemachineCameraManager` 注入门面。
场景仍按 Cinemachine 的方式配置 Brain 与 vcam；游戏代码只调门面。

### 3. 等待一次过渡完成

```csharp
using Cysharp.Threading.Tasks;

await UniTask.WaitUntil(() => !CameraManager.IsTransitioning);
```

### 4. 场景加载后切相机

```csharp
await SceneManager.LoadSceneAsync("scenes/battle");
CameraManager.TransitionTo("battle", 0.5f);
```

## 自定义实现

实现 `ICameraManager` 即可接入任意后端（Cinemachine、Timeline、自研相机系统）：

- **注册**：在实现类自己的构造 / 注册方法里完成（句柄类型由后端决定）；门面只统一运行期操作。
- **义务**：仅主线程；未知 id 告警并 no-op，不得抛；`null` id 抛 `ArgumentNullException`；
  `ActiveCameraId` / `IsTransitioning` / `OutputCamera` 在过渡期间与目标保持一致；`Activate` 取消在途过渡；
  不得在每帧路径分配。
- **所有权**：实现若持有调度 / 场景引用，实现 `IDisposable`；门面的 `Destroy` 会调用它。
- 线程、异常与限制的真相在「线程契约」「已知限制」。

### 推荐第三方实现

- **Cinemachine 3.x → `ICameraManager`**：官方独立扩展包 `com.venusy609.xframework.cinemachine`
  （单独安装，不随核心包发布）。已知代价：只支持 Cinemachine 3.x；项目在 Brain 上配置的 custom blends
  会覆盖扩展包请求的 style / duration；未注册 vcam 的优先级不受管理。

## 内部机制

`UnityCameraManager` 维护 `id → Camera` 字典、活动 id 与过渡状态（起点 / 终点位姿、时长、样式、已用时长）。

- `TransitionTo` 首次调用时把自身注册到 `UpdateManager.RegisterLate(..., Tier0, Scaled)`；过渡完成或取消时
  在 `OnLateUpdate` / 取消路径里注销。空闲时零调度成本——**不要**常驻 Tier7，档位桶会让过渡起步最多延迟
  2.1 秒。
- 每帧按 `t = elapsed / duration` 与样式对应的缓动函数（Linear / smoothstep / t² / 1-(1-t)²）插值，
  零分配。
- 过渡完成时把目标精确恢复到原始位姿；取消时同样恢复，避免残留中间态。

## 生命周期与清理

- `CameraManager.Initialize(impl)` 后门面拥有实现；`Destroy` 释放它（若 `IDisposable`）。
- `UnityCameraManager.Dispose` 注销调度、清空注册表；**不销毁**相机对象（相机归场景 / 调用方）。
- `ResetForNewSession` 在会话开始时只丢引用、不 Dispose（关闭域重载时旧实现可能握着已销毁的场景对象，
  在会话开头 Dispose 只会制造异常与噪音）。
- 相机在场景 / 调用方手里创建与销毁；管理器只持有引用。

## 线程契约

**全部 API 仅在主线程调用。** `OnLateUpdate` 由 `UpdateManager` 在主线程派发；注册表、活动状态与过渡状态
都不是线程安全的。

## 性能代价

- `Activate` / `TransitionTo` / `Deactivate` 遍历注册表（相机数量通常个位数），O(n)，无分配。
- 过渡每帧 O(1)，无闭包、无 LINQ、无字符串拼接；id 比较用序号比较。
- 空闲时管理器不在调度器里（懒注册），零每帧开销。
- `CameraIds` 返回字典键视图，不复制。

## 已知限制

- **一次只有一台活动相机**（默认实现）：分屏、小地图、RenderTexture 多路渲染不在这里；请直接用 Unity
  的多相机 / `Camera.rect` / `targetTexture`，或写自己的 `ICameraManager`。
- **默认实现只插值位置 / 旋转 / FOV（正交为 `orthographicSize`）**：不混合 culling mask、background、
  RenderTexture 等渲染设置；相机必须是静态的，移动 / 跟随 / 瞄准 / 震屏请用 Cinemachine。
- **源与目标投影模式不一致时直接 cut**，不做投影混用。
- **没有相机栈 / 历史**：不提供 push / pop / 返回上一相机；那会引出生命周期树，按项目自己决定。
- **没有 UI 相机绑定**：UI 模块的根画布固定为 `ScreenSpaceOverlay`，不使用相机；需要 `ScreenSpaceCamera`
  的项目要自己换 UI 根，不在本模块射程。
- **Cinemachine 支持是可选扩展包**：核心包不依赖它；扩展包只支持 Cinemachine 3.x，2.x 项目需要自己的
  `ICameraManager` 实现。
- 未知 id 只告警不抛；被销毁的相机在注册表里表现为 `null`，`OutputCamera` 返回 `null`，但 `CameraIds`
  仍可能列出该 id，直到调用方 `Unregister`。

## 设计取舍

- **为什么后端中立、核心包不依赖 Cinemachine**：Cinemachine 2.x / 3.x 的 API 不兼容，核心包一旦引用就
  必须锁一个主版本，会把 2D / 2.x / 自定义后端的项目挡在外面；接口 + 独立扩展包让版本锁只落在扩展包。
- **为什么注册留在实现类**：Unity `Camera` 与 Cinemachine vcam 的句柄类型不同，强行统一成 `object` 会让
  接口失去类型安全；门面只统一运行期操作，注册在组合根完成。
- **为什么 `TransitionTo` 是 `void` + `IsTransitioning`**：不把完成语义绑到一个 await 上；后端可以自由
  决定怎么驱动过渡。需要等待时用 `UniTask.WaitUntil`（典型场景 3）。`TransitionToAsync` 列为 v1.1 候选。
- **为什么不做相机栈 / follow / shake**：栈是生命周期树（与 Update 的「组」API 同一条推理链）；跟随 /
  震屏 / 约束是 Cinemachine 的行为编排，框架重复实现只会与它竞争。
- **为什么默认实现只做静态相机的位姿 / FOV 插值**：它要为零依赖项目提供一个可用、可测、行为明确的兜底；
  不追求覆盖 Cinemachine 的能力面。

## 接口承诺到哪为止

| 常见相机能力 | 归属 | 本模块 |
|---|---|---|
| 激活 / 反激活 / 切换 | Camera | 是 |
| 过渡 / cut | Camera | 是 |
| 当前活动相机 / 输出相机 | Camera | 是（`ActiveCameraId` / `OutputCamera`） |
| 注册表查询 | Camera | 是（`CameraIds`） |
| 过渡状态 | Camera | 是（`IsTransitioning`） |
| 跟随 / 瞄准 / 轨道 | Cinemachine | 否 |
| 震屏 / 噪声 / Impulse | Cinemachine | 否 |
| 缩放 / FOV / Lens | Cinemachine Lens / Tween | 否 |
| 碰撞 / 约束 / Confiner / Dolly / Target Group / Timeline | Cinemachine | 否 |
| 世界↔屏幕 / 射线 | `UnityEngine.Camera`（经 `OutputCamera`） | 否，不重复封装 |
| 投影 / Near-Far / Culling Mask / Clear Flags | `UnityEngine.Camera` | 否 |
| RenderTexture / 分屏 / Viewport | `UnityEngine.Camera` + 项目 | 否 |
| 相机栈 / 深度排序 | 项目 / Cinemachine Brain | 否 |
| UI 相机绑定 | UI 模块（Overlay） | 否 |
| 跨场景持久化 / 存档 / 后处理 | 项目 / URP | 否 |

**判据**：只补「游戏代码需要跨系统协调、且不想直接依赖 Cinemachine 类型」的那一层；引擎已给的与
Cinemachine 已给的一律不重写。

## 与相邻模块的关系

### 与 Update 的边界

默认实现的过渡驱动使用 `UpdateManager.RegisterLate(..., Tier0, Scaled)`；相机跟随等每帧逻辑应使用
`LateUpdate`（`Update` 模块 README 的例子就是相机跟随）。本模块不另建调度器。

### 与 Scene 的边界

`Scene` 只做场景加载 / 卸载，不碰相机；场景切换后切相机由调用方显式调 `Activate` / `TransitionTo`。

### 与 UI 的边界

UI 根画布固定为 `ScreenSpaceOverlay`，不使用相机；HUD / Tip 的世界坐标换算直接用 `Camera.main`，
与本模块互不依赖。需要 `ScreenSpaceCamera` 画布的项目要自己换 UI 根。

### 与 Diagnostics / Bootstrap 的边界

v1 不提供 Diagnostics 面板与 `IBootstrapStage`：模块状态简单，且 `DefaultGameLauncher` 没有相机字段。
实现由调用方在组合根注入（或自行写一个引导阶段）。

## 设计原则

- **正交**：只做相机状态与过渡，不碰相机行为、UI 与场景编排。
- **后端中立**：核心包零 Cinemachine 依赖；Cinemachine 支持是可选扩展包。
- **不替项目定架构**：不提供相机栈、状态机、跟随策略或画面编排。

## 依赖

本模块**无第三方依赖**（仅 `UnityEngine` 与框架内的 `XLog` / `XUpdate`）。Cinemachine 3.x 支持在独立
可选扩展包 `com.venusy609.xframework.cinemachine` 中，核心包不引用它。
