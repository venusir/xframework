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

**命名 / 动态双键**：常驻相机用 string id（构造 map）；随预制创建 / 销毁的动态相机用 `CameraHandle`
（`Register<T>` 返回），不需要生成 id。Timeline / 过场相机**不经过本模块**——由过场系统接管 Brain。

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

// 5. 随预制创建 / 销毁的动态相机：用句柄，不生成 string id
var dynamicCamera = prefab.GetComponentInChildren<Camera>();
var handle = CameraManager.Register(dynamicCamera);
CameraManager.Activate(handle);
// 预制销毁时
CameraManager.Unregister(handle); // 或 handle.Dispose()
```

Cinemachine 3.x 项目：安装独立扩展包后，把上面的 `UnityCameraManager` 换成扩展包里的
`CinemachineCameraManager`，其余调用不变。安装与接线见扩展包自己的 README。

## 模块结构

```text
Runtime/Camera/
├── ICameraManager.cs      # 公开：后端契约（命名 id / 动态句柄的激活 / 反激活 / 过渡 / 查询）
├── CameraHandle.cs        # 公开：动态相机句柄（Id + Generation；default 无效）
├── CameraManager.cs       # 公开：静态门面（转发 + 生命周期 + 会话复位）
├── UnityCameraManager.cs  # 公开：默认实现（槽位表 + 命名 / 动态双键 + 位姿/FOV 插值）
├── CameraBlendStyle.cs    # 公开：过渡样式枚举
└── README.md
```

## 行为契约

- **仅主线程**；注册表与活动状态不是线程安全的。
- `CameraManager.ActiveCameraId`（= `ActiveHandle.NamedId`）在过渡期间返回**目标**；动态相机活动或无活动返回
  `null`。`OutputCamera` 同步返回目标的输出相机。
- `CameraIds` 是**实时视图**（`Dictionary` 的键集合）：枚举期间不要注册 / 注销；它**只列命名 id**，
  动态句柄不在其中。
- **命名 / 动态双键**：常驻相机用 string id；动态相机用 `CameraHandle`。`ActiveHandle` 是唯一的活动查询
  （命名与动态都有；过渡期间是目标）；命名 id 在 `ActiveHandle.NamedId` 里，门面便利
  `CameraManager.ActiveCameraId` 直接读它（O(1)）；动态相机活动时两者都为 `null`，请用
  `ActiveHandle` / `IsActive(handle)`。
- `Register<T>(handle)` 注册动态相机并返回句柄；注册**不激活**，新相机初始关闭。`T` 由后端解释
  （`UnityCameraManager` 要 `Camera`，Cinemachine 扩展要它的 vcam 基类）；类型不符抛 `ArgumentException`，
  `null` 抛 `ArgumentNullException`。
- 句柄无效 / 已注销 / `default`：`Activate` / `Deactivate` / `TransitionTo` 告警并 no-op；`IsActive` 返回
  `false`；`Unregister` 返回 `false`。`CameraHandle.Generation == 0` 是保留给 `default` 的无效值。
- `Unregister(handle)` 移除槽位（若在活动 / 过渡目标上会清掉活动状态，**不回退**）；旧句柄按「槽位 + 代际」
  失配自然失效。同一台相机不要同时注册成命名与动态——一次只有一台活动相机，重复槽位会互相关闭。
- 未知 id：`Activate` / `Deactivate` / `TransitionTo` 告警并 no-op，**不抛**；`null` id 抛 `ArgumentNullException`
  （`IsActive(null)` 返回 `false`）。
- **Timeline / 过场**：随 CG 预制创建、由 Timeline / Cinemachine 接管 Brain 的过场相机**不经过本模块**。
  过渡期间不要调用 `Activate` / `TransitionTo`；Cinemachine 扩展包会对「Brain 的活动相机未注册」告警；
  `CameraManager.ActiveCameraId` / `ActiveHandle` 对未注册的过场 vcam 返回 `null` / `default`，
  `OutputCamera` / `IsTransitioning` 仍可用。
- `Activate` = cut，并取消在途过渡；`TransitionTo` 期间再次调用从**当前视觉状态**重新开始；
  `duration <= 0.0001`（内置实现的 Epsilon）或 `CameraBlendStyle.Cut` 等价于 cut；NaN / ±∞ 抛 `ArgumentOutOfRangeException`。
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
| `CameraIds` | 已注册**命名** id 的实时视图；动态句柄不在其中 |
| `ActiveCameraId`（门面便利） | = `ActiveHandle.NamedId`；命名 / 过渡目标返回 id，动态活动或无活动返回 `null`（O(1)） |
| `ActiveHandle` | 唯一的活动查询；活动 / 过渡目标的句柄，`NamedId` 为命名 id；无活动为 `default` |
| `IsTransitioning` | 是否有过渡在途 |
| `Register<T>(T)` | 注册动态相机，返回 `CameraHandle`；不激活 |
| `Unregister(CameraHandle)` | 注销动态相机；无效句柄返回 `false` |
| `Activate(string)` / `Activate(CameraHandle)` | 立即切换（cut），取消在途过渡 |
| `Deactivate(string)` / `Deactivate(CameraHandle)` | 反激活指定相机；回退语义由后端定义 |
| `TransitionTo(string \| CameraHandle, float, CameraBlendStyle)` | 过渡；`duration <= 0.0001` / `Cut` 等价于 cut；NaN / ±∞ 抛异常 |
| `IsActive(string)` / `IsActive(CameraHandle)` | 是否为当前活动（过渡目标也算） |

`CameraHandle` 自身提供便捷方法：`Activate()` / `Deactivate()` / `TransitionTo(...)` / `IsActive()` /
`Unregister()` / `Dispose()`（= 注销，供 `using`）。`default` 句柄全部短路；`Unregister` / `Dispose` 在门面
未初始化 / 已销毁时安全返回 `false`（预制 `OnDestroy` 可能晚于管理器 `Destroy`）。

`UnityCameraManager` 额外公开：构造（`IReadOnlyDictionary<string, Camera>`）、`Register(string, Camera)`
（命名）、`Unregister(string)`、`Dispose()`。命名注册在具体实现；动态注册走接口的 `Register<T>`（门面已转发）。

## 典型场景

### 1. 无 Cinemachine 的简单项目

用 `UnityCameraManager` + 静态相机；切场景或进战斗时调 `Activate` / `TransitionTo`。过渡走
`UpdateManager` 的 LateUpdate，暂停（`timeScale <= 0`）会冻结过渡。跟随 / 震屏等行为由项目侧脚本实现，
配方见「已知限制」的三条路径。

### 2. Cinemachine 3.x 项目

安装独立扩展包 `com.venusy609.xframework.cinemachine`，用 `CinemachineCameraManager` 注入门面。
场景仍按 Cinemachine 的方式配置 Brain 与 vcam；游戏代码只调门面。**扩展包是适配器，不是行为库**——
跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline 仍在 Cinemachine 里配置。

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

### 5. 随预制创建 / 销毁的动态相机

预制在 `Awake` / `OnEnable` 里注册，在 `OnDestroy` 里注销：

```csharp
public sealed class DynamicCameraOwner : MonoBehaviour
{
    private CameraHandle _handle;

    private void Awake()
    {
        _handle = CameraManager.Register(GetComponentInChildren<Camera>());
        _handle.Activate();
    }

    private void OnDestroy()
    {
        _handle.Dispose(); // 门面已销毁也安全
    }
}
```

同一台相机不要同时注册成命名与动态；管理器**不销毁**相机对象，预制销毁由场景负责。

### 6. Timeline / 过场

过场相机**不注册**到 `CameraManager`：Timeline 会接管 `CinemachineBrain`。过场期间不要调用 `Activate` /
`TransitionTo`；过场结束后由过场系统 / 调用方恢复玩法相机。Cinemachine 扩展包会对「Brain 的活动相机未注册」
告警。

## 自定义实现

实现 `ICameraManager` 即可接入任意后端（Cinemachine、自研相机系统）：

- **命名注册**在实现类自己的构造 / 命名 `Register` 方法里完成；**动态注册**走接口的 `Register<T>(T)`：
  实现把后端相机对象转成槽位并返回 `CameraHandle`（`Id` = 槽位下标、`Generation` 从 1 起，`default` 无效）。
- **义务**：仅主线程；未知 id / 无效 / 已注销 / `default` 句柄告警并 no-op，不得抛；`null` id / 句柄抛
  `ArgumentNullException`；`Register<T>` 的类型不符抛 `ArgumentException`。`ActiveHandle`（命名槽位的
  `NamedId` 必须填对应 id，动态槽位填 `null`；门面 `CameraManager.ActiveCameraId` 直接读它）/ `IsTransitioning` /
  `OutputCamera` 在过渡期间与目标保持一致；`Activate` 取消在途过渡；
  `Unregister` 按「槽位 + 代际」双校验，旧句柄失配；不得在每帧路径分配。
- **所有权**：实现若持有调度 / 场景引用，实现 `IDisposable`；门面的 `Destroy` 会调用它。
- `Register` 不激活相机；`Unregister` 若在活动 / 过渡目标上清活动状态、**不回退**。
- 线程、异常与限制的真相在「线程契约」「已知限制」。

### 推荐第三方实现

- **Cinemachine 3.x → `ICameraManager`**：官方独立扩展包 `com.venusy609.xframework.cinemachine`
  （单独安装，不随核心包发布）。已知代价：只支持 Cinemachine 3.x；项目在 Brain 上配置的 custom blends
  会覆盖扩展包请求的 style / duration；未注册 vcam 的优先级不受管理。

## 内部机制

`UnityCameraManager` 维护一个槽位表（`Slot`: `Camera`、可选命名 id、`Generation`、`Used`）、命名 id → 槽位映射、
活动 / 过渡槽位与过渡状态（起点 / 终点位姿、时长、样式、已用时长）。

- 槽位下标即 `CameraHandle.Id`；`Unregister` 清槽位、保留代际，复用时代际递增，旧句柄按代际失配失效。
- 命名与动态槽位走同一套激活 / 过渡 / 关闭路径；`ActiveSlot` 在过渡期间是目标。
- 动态注册默认关闭；`Activate` 时启用目标、关闭其它已注册相机。
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
- **动态相机的 `CameraManager.ActiveCameraId`（= `ActiveHandle.NamedId`）为 `null`**：动态槽位没有命名 id，
  请用 `ActiveHandle` / `IsActive(handle)`；`CameraIds` 只列命名 id。
- **Timeline / 过场相机不经过本模块**：由过场系统接管 Brain；`CameraManager.ActiveCameraId` /
  `ActiveHandle` 对未注册的过场 vcam 返回 `null` / `default`（`OutputCamera` / `IsTransitioning` 仍可用）；
  过场期间不要调用
  `Activate` / `TransitionTo`。Cinemachine 扩展包会对「Brain 的活动相机未注册」告警。
- **同一台相机不要同时注册成命名与动态**：两个槽位会互相关闭（一次只有一台活动相机）。
- **管理器不销毁相机对象**：动态相机随预制 / 场景销毁；`Unregister` 只移除槽位并关闭相机。
- **默认实现只插值位置 / 旋转 / FOV（正交为 `orthographicSize`）**：不混合 culling mask、background、
  RenderTexture 等渲染设置；相机必须是静态的，移动 / 跟随 / 瞄准 / 震屏请用 Cinemachine。
- **源与目标投影模式不一致时直接 cut**，不做投影混用。
- **没有相机栈 / 历史**：不提供 push / pop / 返回上一相机；那会引出生命周期树，按项目自己决定。
- **没有 UI 相机绑定**：UI 模块的根画布固定为 `ScreenSpaceOverlay`，不使用相机；需要 `ScreenSpaceCamera`
  的项目要自己换 UI 根，不在本模块射程。
- **Cinemachine 支持是可选扩展包**：核心包不依赖它；扩展包只支持 Cinemachine 3.x，2.x 项目需要自己的
  `ICameraManager` 实现。
- **需要跟随 / 震屏 / 缩放等行为时**：核心本身不提供这些（见「接口承诺到哪为止」的能力归属）。三条路径：
  - **Cinemachine 级需求**（3D 跟随 / 瞄准 / 震屏 / 碰撞 / 约束 / Dolly / Target Group / Timeline）→ 安装
    `com.venusy609.xframework.cinemachine` 扩展包，并在 Cinemachine 里配置 vcam / Brain。**扩展包只把
    Cinemachine 接进 `CameraManager`，行为本身由 Cinemachine 提供**。
  - **简单需求**（2D 跟随、基础震屏、FOV 缩放）→ 项目侧十几行脚本即可，不必装 Cinemachine：跟随用
    `UpdateManager.RegisterLate`（LateUpdate）+ `Vector3.SmoothDamp`；震屏 / 缩放计时用 `TimerManager`；
    相机属性直接经 `OutputCamera`（`UnityEngine.Camera`）读写。
  - **别的相机系统** → 实现 `ICameraManager`；核心包不锁后端。
- **未知 id 只告警不抛；被销毁的相机在注册表里表现为 `null`，`OutputCamera` 返回 `null`，但 `CameraIds`
  仍可能列出该 id，直到调用方 `Unregister`。
- **`OutputCamera` 不能当作「有活动相机」的判据**（CAM-005）：核心接口只承诺「没有输出相机时可为 null」；
  Cinemachine 适配器即使活动 vcam 未注册也返回 Brain 的输出相机（恒非空）。判断活动状态请用
  `ActiveHandle` / `IsActive`。
- **`Unregister` 只清管理器视图**（CAM-005）：Cinemachine 适配器下 Brain 仍可能继续使用该 vcam；
  需要真正停用请另行降优先级 / 停用 vcam。

## 设计取舍

- **为什么后端中立、核心包不依赖 Cinemachine**：Cinemachine 2.x / 3.x 的 API 不兼容，核心包一旦引用就
  必须锁一个主版本，会把 2D / 2.x / 自定义后端的项目挡在外面；接口 + 独立扩展包让版本锁只落在扩展包。
- **为什么命名注册留在实现类、动态注册进主接口**：命名相机是组合根 / 构造 map 的静态配置；动态相机随
  预制实例创建 / 销毁，必须经门面按句柄注册。句柄用 `CameraHandle`（槽位 + 代际）而不是 string id——
  实例没有稳定的 authoring id；`Register<T>` 的泛型只保留调用点的类型推断，后端仍做类型校验。
- **为什么 `CameraHandle` 字段公开**：第三方 `ICameraManager` 实现必须能造句柄、读回自己编码进去的值
  （与 `AudioHandle` 同一条理由）。
- **为什么 `TransitionTo` 是 `void` + `IsTransitioning`**：不把完成语义绑到一个 await 上；后端可以自由
  决定怎么驱动过渡。需要等待时用 `UniTask.WaitUntil`（典型场景 3）。`TransitionToAsync` 列为 v1.1 候选。
- **为什么不做相机栈 / follow / shake**：栈是生命周期树（与 Update 的「组」API 同一条推理链）；跟随 /
  震屏 / 约束是 Cinemachine 的行为编排，框架重复实现只会与它竞争。需要这些行为时的三条路径见「已知限制」。
- **为什么默认实现只做静态相机的位姿 / FOV 插值**：它要为零依赖项目提供一个可用、可测、行为明确的兜底；
  不追求覆盖 Cinemachine 的能力面。

## 接口承诺到哪为止

| 常见相机能力 | 归属 | 本模块 |
|---|---|---|
| 激活 / 反激活 / 切换 | Camera | 是 |
| 过渡 / cut | Camera | 是 |
| 当前活动相机 / 输出相机 | Camera | 是（`ActiveHandle` / `OutputCamera`；门面 `ActiveCameraId` 是便利） |
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
| Timeline / 过场相机 | 过场系统（Cinemachine Track） | 否（不注册；过场期间不要激活 / 过渡） |
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

### 与 Timeline / 过场的边界

过场相机由 Timeline 的 Cinemachine Track 驱动，会接管 `CinemachineBrain`；它们**不经过本模块**。过场期间
`CameraManager` 的 `Activate` / `TransitionTo` 会与过场冲突，不要调用；`CameraManager.ActiveCameraId` /
`ActiveHandle` 对未注册的过场 vcam 返回 `null` / `default`，`OutputCamera` / `IsTransitioning` 仍可读。过场结束后由调用方
恢复玩法相机。

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
