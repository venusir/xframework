# Camera —— 相机激活 / 反激活 / 过渡的统一入口（后端中立）

## 概述

`XCamera` 提供「切到哪台相机 / 何时过渡 / 当前输出相机是谁」的统一入口，游戏代码不必直接依赖
Cinemachine 的 vcam 与 Brain 类型。**它只做状态与过渡**：跟随、瞄准、震屏、缩放、碰撞、约束、Dolly、
Target Group、Timeline 仍归 Cinemachine；投影、Near-Far、Culling Mask、RenderTexture、Viewport 仍归
`UnityEngine.Camera`（经 `OutputCamera` 直接使用）。

**命名空间**：`XFramework.XCamera`。

**唯一内置后端** `CinemachineCameraManager`（`Cinemachine/` 子目录）把 Cinemachine 3.x 的 vcam 优先级与
`CinemachineBrain` 混合接进本模块。`ICameraManager` 仍保持后端中立，第三方可以为自己的相机系统实现它。
不再提供手写的 `UnityCameraManager`：Cinemachine 已是核心 asmdef 的硬依赖，手写位姿 / FOV 插值只会与
Brain 的混合逻辑重复。

**统一句柄**：命名相机与随预制创建 / 销毁的动态相机都先注册、拿到 `CameraHandle`，之后所有操作只收句柄；
字符串入口只是门面的便捷重载。Timeline / 过场相机**不经过本模块**——由过场系统接管 Brain。

## 快速使用

```csharp
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera;
using XFramework.XCamera.Cinemachine;

// 1. 构造后端并注入门面（门面取得所有权）
var manager = new CinemachineCameraManager(brain);
CameraManager.Initialize(manager);

// 2. 注册命名相机（不激活、不改场景）
CameraManager.Register("main", mainVcam);
CameraManager.Register("battle", battleVcam);

// 3. 切相机
CameraManager.Activate("main");                                  // 立即切换（cut）
CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut); // 过渡

// 4. 直接用输出相机做世界↔屏幕 / 射线 / FOV
var camera = CameraManager.OutputCamera;
var screen = camera.WorldToScreenPoint(target.position);

// 5. 随预制创建 / 销毁的动态相机：注册拿句柄，不生成 string id
var handle = CameraManager.Register(prefabVcam);
CameraManager.Activate(handle);
// 预制销毁时
CameraManager.Unregister(handle); // 或 handle.Dispose()
```

接线与 Cinemachine 侧边界见 [`Cinemachine/README.md`](Cinemachine/README.md)。

## 模块结构

```text
Runtime/Camera/
├── ICameraManager.cs      # 公开：后端契约（统一句柄注册 / 激活 / 反激活 / 过渡 / 查询）
├── CameraHandle.cs        # 公开：相机句柄（Id + Generation + NamedId；default 无效）
├── CameraManager.cs       # 公开：静态门面（转发 + 生命周期 + 会话复位 + 字符串便捷重载）
├── CameraBlendStyle.cs    # 公开：过渡样式枚举（后端中立）
├── CameraTransitionResult.cs # 公开：Completed / Superseded / TargetUnregistered / ManagerDisposed
├── Messages/
│   ├── CameraActivatedMessage.cs      # 公开：incoming 相机激活
│   ├── CameraDeactivatedMessage.cs    # 公开：已注册相机不再 live
│   └── CameraBlendCompletedMessage.cs # 公开：blend 完成（cut 视为零时长完成）
├── Cinemachine/
│   ├── CinemachineCameraManager.cs  # 公开：ICameraManager 的 Cinemachine 3.x 实现
│   └── README.md                    # Cinemachine 后端的用法与边界
└── README.md
```

## 行为契约

- **仅主线程**；注册表与活动状态不是线程安全的。
- **统一句柄**：命名相机与动态相机都经 `Register` 返回 `CameraHandle`；命名 id 在 `CameraHandle.NamedId`
  里（动态为 `null`）。`CameraManager.ActiveCameraId` = `ActiveHandle.NamedId`（O(1)、零遍历）。
- `ActiveHandle` 读 **Brain 的真实活动相机**，可能比请求晚一次 Brain 更新；过渡期间是 incoming。无活动 /
  活动相机未注册时为 `default`。
- `IsTransitioning` **只表示本管理器发起且尚未完成的非 cut 过渡**；Timeline / 外部系统发起的混合不计入。
- `OutputCamera` = `brain.OutputCamera`，可能为 `null`、不保证等于过渡目标；**不能**用
  `OutputCamera != null` 判断「是否有已注册的活动相机」。
- `CameraCount` 统计已注册槽位，包含「已被外部销毁但尚未注销」的槽位；外部销毁的相机仍占槽位，直到
  `Unregister` 或下一次 `Register`（后者会先清理陈旧槽位）。
- **注册唯一性**：同一 id 同一实例重复注册幂等返回同一句柄；id 已被另一实例占用、或实例已注册为另一 id 时
  抛 `ArgumentException`；注册已销毁的 Unity 对象抛 `ArgumentException`；字面 `null` 抛
  `ArgumentNullException`。注册**不激活**、不改变场景状态。
- 句柄无效 / 已注销 / `default`：`Activate` / `Deactivate` / `TransitionTo` 告警并 no-op；`IsActive` 返回
  `false`；`Unregister` 返回 `false`。`CameraHandle.Generation == 0` 是保留给 `default` 的无效值。
- 未知 id：门面的字符串 `Activate` / `Deactivate` / `TransitionTo` 告警并 no-op，**不抛**；`null` id 抛
  `ArgumentNullException`（`IsActive(null)` / `Unregister(null)` 的规则见 API 参考）。
- **激活 / 优先级**：`Activate` / 非 cut 过渡把目标提升到「场景内同通道最大优先级 + 1」（包含未注册 vcam），
  只改目标；切换时恢复上一个目标由管理器改过的优先级 / 启用状态。`Unregister` / `Dispose` 恢复注册时捕获的
  作者优先级与启用状态。
- `Deactivate` 抑制该相机的优先级（`int.MinValue`）并让 Brain 重选；可能选中未注册相机。没有其它候选时
  Brain 可能继续输出它——要强制停用请注销或直接 `SetActive(false)`。
- **过渡**：`TransitionTo` 通过 `CinemachineCore.GetBlendOverride` 请求本次的 duration / style；
  **不修改** `Brain.DefaultBlend`。`duration <= 0.0001` 或 `CameraBlendStyle.Cut` 等价于 cut；NaN / ±∞ 抛
  `ArgumentOutOfRangeException`。
- `GetBlendOverride` 是**全局单委托**：管理器构造时保存前值并安装自己，`Dispose` 时仅当当前委托仍是自己才
  恢复前值。一个进程建议只创建一个相机管理器；项目若也使用该委托，以最后安装者为准。
- 项目 `CustomBlends` 在 `LookupBlend` 中先于 `GetBlendOverride` 求值，因此**本次管理器请求优先于
  `CustomBlends`**；Timeline 混合不经过该入口，管理器不承诺控制 Timeline。
- `Unregister` 活动相机：恢复作者优先级 / 启用状态，必要时强制完成在途混合；**不销毁相机对象**。若作者
  优先级仍最高，Brain 可能继续输出它，此时 `ActiveHandle` 为 `default`，下一次 `Activate` 会以
  「场景最大 + 1」压过它。
- **Timeline / 过场**：过场相机不注册；若 Brain 的活动相机未注册，激活 / 过渡会告警「可能与过场冲突」；
  `ActiveHandle` / `ActiveCameraId` 对这种活动相机返回 `default` / `null`，`OutputCamera` 仍可读。
- `Initialize` 取得实现所有权；重复注入告警并保留第一个，**被拒绝的第二个实例仍归调用方所有**，需自行释放
  （Cinemachine 后端在构造时订阅静态事件）。`Destroy` 先清实例再 Dispose，Dispose 异常继续传播；
  `ResetForNewSession` 只丢引用、不 Dispose。

## 架构设计

三层：静态门面 `CameraManager` → 后端契约 `ICameraManager` → 具体实现（内置 Cinemachine 后端 / 第三方后端）。

- **门面**：生命周期（`Initialize` / `Destroy` / `IsInitialized`）+ 同名转发 + 字符串便捷重载 + 会话复位；
  扁平，不分组。
- **契约**：统一句柄的注册 / 激活 / 反激活 / 过渡 / 查询；不出现 Cinemachine 类型。
- **内置实现**：`CinemachineCameraManager`，用 vcam 优先级与 Brain 混合；门面与契约不出现 Cinemachine 类型，
  由 `ModuleBoundaryTests.CinemachineTypes_StayInsideTheCinemachineBackend` 守卫。

## API 参考

| 入口 | 行为 |
|---|---|
| `Initialize(ICameraManager)` | 注入实现；重复调用告警后忽略；`null` 抛 `ArgumentNullException` |
| `Destroy()` | 清空门面；实现是 `IDisposable` 时释放它；Dispose 异常继续传播 |
| `IsInitialized` | 是否有实现 |
| `OutputCamera` | 当前输出相机；未初始化或无输出时为 `null` |
| `ActiveHandle` | Brain 活动相机的句柄；无活动 / 未注册为 `default` |
| `ActiveCameraId`（门面便利） | = `ActiveHandle.NamedId`；动态活动或无活动为 `null` |
| `IsTransitioning` | 本管理器发起的非 cut 过渡是否在途 |
| `CameraCount` | 已注册槽位数量（含已销毁未注销） |
| `TryGetHandle(string, out CameraHandle)` | 按命名 id 查句柄；未知 / 未初始化返回 `false` |
| `Register<T>(string, T)` / `Register<T>(T)` | 命名 / 动态注册，返回句柄；不激活 |
| `Unregister(CameraHandle)` / `Unregister(string)` | 注销；无效 / 未知返回 `false` |
| `Activate(string)` / `Activate(CameraHandle)` | 立即切换（cut） |
| `Deactivate(string)` / `Deactivate(CameraHandle)` | 反激活；回退由后端定义 |
| `TransitionTo(string \| CameraHandle, float, CameraBlendStyle)` | 过渡；`duration <= 0.0001` / `Cut` 等价于 cut |
| `TransitionToAsync(string \| CameraHandle, float, CameraBlendStyle, CancellationToken)` | 过渡并等待完成；返回 `CameraTransitionResult`；取消只停止等待 |
| `IsActive(string)` / `IsActive(CameraHandle)` | 是否为 Brain 当前活动（过渡 incoming 也算） |
| `Subscribe(Action<CameraActivatedMessage / CameraDeactivatedMessage / CameraBlendCompletedMessage>)` | 订阅状态消息；不依赖门面初始化，返回退订句柄 |

探测型读（`OutputCamera` / `ActiveHandle` / `ActiveCameraId` / `IsTransitioning` / `CameraCount` /
`TryGetHandle` / `IsActive`）在门面未初始化时返回默认值；`Register` / `Activate` / `Deactivate` /
`TransitionTo` 仍抛 `InvalidOperationException`。

`CameraHandle` 自身提供便捷方法：`Activate()` / `Deactivate()` / `TransitionTo(...)` / `IsActive()` /
`Unregister()` / `Dispose()`（= 注销，供 `using`）。`default` 句柄全部短路；非 default 命令在门面未初始化时
仍抛，`IsActive()` 返回 `false`，`Unregister()` / `Dispose()` 返回 `false`（预制 `OnDestroy` 可能晚于管理器
`Destroy`）。

## 相机事件

需要知道「Brain 真的把哪台相机变成了 live / 过渡何时完成」时，订阅消息而不是轮询：

```csharp
using XFramework.XCamera.Messages;

using var sub = CameraManager.Subscribe((CameraActivatedMessage m) =>
{
    // m.Handle / m.PreviousHandle / m.IsCut
});
```

| 消息 | 发布时机 | 条件 |
|---|---|---|
| `CameraActivatedMessage` | `CinemachineCore.CameraActivatedEvent`（混合开始帧） | incoming 能解析到已注册相机 |
| `CameraDeactivatedMessage` | `CinemachineCore.CameraDeactivatedEvent` | 被停 live 的相机仍能解析到已注册槽位 |
| `CameraBlendCompletedMessage` | `BlendFinishedEvent`；cut 在激活帧发布 | 完成的相机能解析到已注册槽位 |
| `CameraActivatedMessage` + `IsCut` | 同帧再发 `CameraBlendCompletedMessage` | 同上；cut 视为零时长完成 |

- cut A→B：`Deactivated(A)` → `Activated(B, IsCut=true)` → `Completed(B)`。
- blend A→B：混合开始帧 `Activated(B)`；完成帧 `Deactivated(A)` → `Completed(B)`。混合期间 A 仍参与混合，
  所以 `Deactivated(A)` 会晚于 `IsActive(A)` 变 `false`。
- 未注册的 incoming（Timeline / 外来 vcam）**不发布** `Activated`；旧的已注册相机失去 live 时仍发布
  `Deactivated`，消费方以 `ActiveHandle == default` 识别。
- 事件经 `MessageManager` 发布，**不依赖相机门面是否初始化**；订阅生命周期由调用方管理（返回值 `Dispose`
  或绑定 `destroyCancellationToken`）。
- `CinemachineCore` 的事件是静态 UnityEvent；管理器在 `Dispose` 与 `ResetForNewSession` 都会退订。第三方
  后端若想提供同等能力，应按上述消息类型直接 `MessageManager.Publish`（不发布不影响同步成员）。

## 典型场景

### 1. 进入战斗

```csharp
CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut);
```

### 2. 立即切镜（演出 / 传送）

```csharp
CameraManager.Activate("cutscene");
```

### 3. 等待一次过渡完成

```csharp
using Cysharp.Threading.Tasks;

var result = await CameraManager.TransitionToAsync(
    "battle", 0.8f, CameraBlendStyle.EaseInOut);
if (result != CameraTransitionResult.Completed)
{
    // Superseded / TargetUnregistered / ManagerDisposed
}
```

`TransitionToAsync` 返回 `CameraTransitionResult`；`CancellationToken` 只取消等待，**不中止**相机过渡。
只需要“等到当前管理器过渡结束”时仍可用 `UniTask.WaitUntil(() => !CameraManager.IsTransitioning)`。

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
        _handle = CameraManager.Register(GetComponentInChildren<CinemachineCamera>());
        _handle.Activate();
    }

    private void OnDestroy()
    {
        _handle.Dispose(); // 门面已销毁也安全
    }
}
```

管理器**不销毁**相机对象，预制销毁由场景负责；同一台相机只能注册一次（不同 id / 命名 + 动态都会抛）。

### 6. Timeline / 过场

过场相机**不注册**到 `CameraManager`：Timeline 会接管 `CinemachineBrain`。过场期间不要调用 `Activate` /
`TransitionTo`；过场结束后由过场系统 / 调用方恢复玩法相机。Cinemachine 后端会对「Brain 的活动相机未注册」
告警。

## 自定义实现

实现 `ICameraManager` 即可接入任意后端（2.x、自研相机系统）：

- **注册**：命名与动态都经 `Register` 返回 `CameraHandle`（`Id` = 槽位下标、`Generation` 从 1 起，
  `NamedId` 命名槽位填 id、动态填 `null`；`default` 无效）。注册不激活。
- **义务**：仅主线程；未知 id / 无效 / 已注销 / `default` 句柄告警并 no-op，不得抛；`null` id 抛
  `ArgumentNullException`；类型不符 / 已销毁对象抛 `ArgumentException`；重复注册按幂等 / 冲突规则处理。
  `ActiveHandle` 在过渡期间与 incoming 保持一致；`IsTransitioning` 只统计本后端发起的非 cut 过渡；
  `Unregister` 按「槽位 + 代际」双校验，旧句柄失配；不得在每帧路径分配。
- **所有权**：实现若持有调度 / 场景 / 静态事件引用，实现 `IDisposable`；门面的 `Destroy` 会调用它。
- `OutputCamera` 可能为 `null`、不保证等于过渡目标；`Unregister` 不销毁后端对象。
- 线程、异常与限制的真相在「线程契约」「已知限制」。

### 推荐第三方实现

- **Cinemachine 3.x → `ICameraManager`**：核心包内置 `CinemachineCameraManager`
  （`Runtime/Camera/Cinemachine/`）。已知代价：只支持 Cinemachine 3.x；`GetBlendOverride` 是全局单委托；
  Timeline 混合不受管理器控制。

## 内部机制

`CinemachineCameraManager` 维护单一槽位表（`Slot`: vcam、命名 id、`Generation`、`Used`、注册时捕获的
`PrioritySettings` / GameObject / 组件启用状态），槽位下标即 `CameraHandle.Id`。

- **线性扫描**：活动相机 → 句柄、相机 → 槽位、id → 槽位都按槽位顺序查找；相机数量通常个位数，O(n) 换取
  反查表在替换 / 注销 / 外部销毁时的全部陈旧键分支。
- 槽位复用时代际递增，旧句柄按代际失配失效。
- 激活 / 过渡只改目标优先级；`ComputeBoostPriority` 取已注册槽位的作者优先级与同通道未注册 vcam 的最大值
  + 1，`int.MaxValue` 封顶；切换时恢复上一个目标的拥有状态。
- 过渡请求写入 `PendingTransition`，`CinemachineCore.GetBlendOverride` 在 Brain 创建混合时返回请求定义；
  `CameraUpdatedEvent` 在混合结束后清理 pending（`IsTransitioning` 随之变 false，无需轮询）。
- cut 也走同一个 override（返回 `Styles.Cut`），因此不修改 `DefaultBlend`。
- `Dispose` 恢复所有仍存活槽位的作者状态、退订 `CameraUpdatedEvent`、恢复 `GetBlendOverride` 前值。

## 生命周期与清理

- `CameraManager.Initialize(impl)` 后门面拥有实现；`Destroy` 释放它（若 `IDisposable`）。
- `CinemachineCameraManager.Dispose` 恢复优先级 / 启用状态、退订静态事件、清空注册表；**不销毁** Brain / vcam。
- `ResetForNewSession` 在会话开始时只丢引用、不 Dispose（关闭域重载时旧实现可能握着已销毁的场景对象）。
- 相机在场景 / 调用方手里创建与销毁；管理器只持有引用。

## 线程契约

**全部 API 仅在主线程调用。** Brain 与 vcam 都是 Unity 场景对象；注册表、活动状态与过渡状态都不是线程安全的。

## 性能代价

- `Activate` / `TransitionTo` / `Deactivate` / 注册查询遍历槽位（相机数量通常个位数），O(n)，无分配。
- 管理器不在每帧路径上；Brain 自己按 `UpdateMethod` 推进混合。`CameraUpdatedEvent` 的处理在 Brain 更新末尾，
  只做指针比较与 pending 清理。
- `GetBlendOverride` 每次混合创建时调用一次，只做引用比较。
- `CameraCount` O(n)；`TryGetHandle` O(n)。

## 已知限制

- **一次只有一台活动相机**：分屏、小地图、RenderTexture 多路渲染不在这里；请直接用 Unity 的多相机 /
  `Camera.rect` / `targetTexture`，或写自己的 `ICameraManager`。
- **动态相机的 `ActiveCameraId` 为 `null`**：动态槽位没有命名 id，请用 `ActiveHandle` / `IsActive(handle)`；
  `TryGetHandle` 只查命名 id。
- **Timeline / 过场相机不经过本模块**：由过场系统接管 Brain；`ActiveCameraId` / `ActiveHandle` 对未注册的
  过场 vcam 返回 `null` / `default`（`OutputCamera` 仍可读）；过场期间不要调用 `Activate` / `TransitionTo`。
- **事件只覆盖已注册相机**：未注册的 incoming 不发布 `CameraActivatedMessage`；订阅是全局
  `MessageManager` 通道，多个管理器 / 多个 Brain 时应按 `Handle` 过滤。事件只反映 Brain 的真实状态，
  不是命令回执——命令后可能要等 Brain 下一次更新才收到消息。
- **`TransitionToAsync` 取消只停止等待**：`CancellationToken` 取消不会中止相机过渡；要中断过渡请用新的
  `Activate` / `TransitionTo` / `Deactivate`。取消抛 `OperationCanceledException`。
- **异步完成由 Brain 事件驱动**：Brain 被禁用或不再推进时等待不会完成；用 `CancellationToken` / 调用方
  超时兜底。目标在完成前被注销 / 销毁返回 `TargetUnregistered`，被新请求 / 外部激活取代返回
  `Superseded`，管理器销毁返回 `ManagerDisposed`。
- **同一台相机只能注册一次**：命名 + 动态、两个命名 id、两次不同 id 的动态注册都会抛 `ArgumentException`；
  先 `Unregister` 再换注册方式。
- **管理器不销毁相机对象**：动态相机随预制 / 场景销毁；`Unregister` 只恢复状态并移除槽位。
- **`Deactivate` 不保证停用**：它只抑制优先级并让 Brain 重选；没有其它候选时 Brain 可能继续输出该相机。
- **`Unregister` 活动相机后可能仍在输出**：恢复作者优先级后若它仍是场景最高，Brain 会继续用它；此时
  `ActiveHandle` 为 `default`，下一次 `Activate` 会压过它。
- **`GetBlendOverride` 是全局单委托**：一个进程建议只创建一个相机管理器；项目若也使用它，以最后安装者为
  准。管理器 `Dispose` 只在当前委托仍是自己时恢复前值。
- **Timeline 混合不受管理器控制**：Timeline 不经过 `GetBlendOverride`；管理器请求的 duration / style 对
  Timeline 无效。
- **没有相机栈 / 历史**：不提供 push / pop / 返回上一相机；那会引出生命周期树，按项目自己决定。
- **没有 UI 相机绑定**：UI 模块的根画布固定为 `ScreenSpaceOverlay`，不使用相机；需要 `ScreenSpaceCamera`
  的项目要自己换 UI 根，不在本模块射程。
- **Cinemachine 支持内置于核心包**：`CinemachineCameraManager` 随核心包发布（核心 asmdef 硬引用
  `Unity.Cinemachine` 3.1.7）；它只支持 Cinemachine 3.x，2.x 项目需要自己的 `ICameraManager` 实现。
- **没有内置的无 Cinemachine 后端**：需要直接驱动 `UnityEngine.Camera` 的项目请用自己的 `ICameraManager`
  实现，或直接使用 Unity 的 Camera API。
- **需要跟随 / 震屏 / 缩放等行为时**：核心本身不提供这些（见「接口承诺到哪为止」的能力归属）。三条路径：
  - **Cinemachine 级需求**（3D 跟随 / 瞄准 / 震屏 / 碰撞 / 约束 / Dolly / Target Group / Timeline）→ 直接用
    内置的 `CinemachineCameraManager`，并在 Cinemachine 里配置 vcam / Brain。**它只把 Cinemachine 接进
    `CameraManager`，行为本身由 Cinemachine 提供**。
  - **简单需求**（2D 跟随、基础震屏、FOV 缩放）→ 项目侧脚本；跟随用 `UpdateManager.RegisterLate`（LateUpdate）
    + `Vector3.SmoothDamp`；震屏 / 缩放计时用 `TimerManager`；相机属性直接经 `OutputCamera` 读写。
  - **别的相机系统** → 实现 `ICameraManager`；核心包不锁后端。
- **`OutputCamera` 不能当作「有活动相机」的判据**（CAM-005）：Cinemachine 后端即使活动 vcam 未注册也返回
  Brain 的输出相机（**可能为 null**——Brain 所在对象没有 `Camera` 时）。判断活动状态请用 `ActiveHandle` /
  `IsActive`。

## 设计取舍

- **为什么只保留 Cinemachine 后端**：Cinemachine 已是核心 asmdef 的硬依赖；手写位姿 / FOV 插值与 Brain 的
  混合逻辑重复，并制造了 `Deactivate` / `Unregister` / `IsTransitioning` 的语义分叉。无 Cinemachine 项目
  直接用 Unity Camera API 或实现 `ICameraManager`。
- **为什么统一句柄**：命名 / 动态双键会让接口、门面与每个实现各背一套重复成员；统一句柄后接口只认
  `CameraHandle`，字符串只是门面糖，命名注册也能进接口。
- **为什么用 `GetBlendOverride` 而不是粘滞改 `DefaultBlend`**：后者会污染之后所有未命中 `CustomBlends` 的
  过渡；前者把 duration / style 限定在本次请求，且不改项目配置。代价是全局单委托，见「已知限制」。
- **为什么 `IsTransitioning` 只统计管理器自有过渡**：Brain 的 `IsBlending` 包含 Timeline / 外部混合，用它等待
  会被不属于管理器的混合拖住；Brain 的真实状态属于诊断面。
- **为什么门面 / 契约保持中立、Cinemachine 只隔离在子目录**：核心 asmdef 已硬引用 Cinemachine 3.1.7，
  但游戏代码与第三方后端实现仍只面向 `ICameraManager`；`Runtime/Camera/` 根层的门面、接口不得出现
  Cinemachine 类型，由位置守卫守住。
- **为什么 `CameraHandle` 字段公开**：第三方 `ICameraManager` 实现必须能造句柄、读回自己编码进去的值
  （与 `AudioHandle` 同一条理由）。
- **为什么不做相机栈 / follow / shake**：栈是生命周期树（与 Update 的「组」API 同一条推理链）；跟随 /
  震屏 / 约束是 Cinemachine 的行为编排，框架重复实现只会与它竞争。

## 接口承诺到哪为止

| 常见相机能力 | 归属 | 本模块 |
|---|---|---|
| 激活 / 反激活 / 切换 | Camera | 是 |
| 过渡 / cut | Camera | 是（请求 duration / style） |
| 当前活动相机 / 输出相机 | Camera | 是（`ActiveHandle` / `OutputCamera`；门面 `ActiveCameraId` 是便利） |
| 注册表查询 | Camera | 是（`CameraCount` / `TryGetHandle`） |
| 过渡状态 | Camera | 是（`IsTransitioning`，只含管理器自有过渡） |
| 异步等待过渡完成 | Camera | 是（`TransitionToAsync`，返回结果枚举） |
| 状态事件（激活 / 反激活 / 过渡完成） | Camera | 是（`MessageManager`，只对已注册相机发布） |
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

本模块不再注册 ticker：过渡由 `CinemachineBrain` 的 `UpdateMethod` 驱动。相机跟随等每帧逻辑仍可使用
`UpdateManager` 的 `LateUpdate`；本模块不另建调度器。

### 与 Scene 的边界

`Scene` 只做场景加载 / 卸载，不碰相机；场景切换后切相机由调用方显式调 `Activate` / `TransitionTo`。

### 与 Timeline / 过场的边界

过场相机由 Timeline 的 Cinemachine Track 驱动，会接管 `CinemachineBrain`；它们**不经过本模块**。过场期间
`CameraManager` 的 `Activate` / `TransitionTo` 会与过场冲突，不要调用；`ActiveCameraId` / `ActiveHandle`
对未注册的过场 vcam 返回 `null` / `default`，`OutputCamera` 仍可读。过场结束后由调用方恢复玩法相机。

### 与 UI 的边界

UI 根画布固定为 `ScreenSpaceOverlay`，不使用相机；HUD / Tip 的世界坐标换算直接用 `Camera.main`，
与本模块互不依赖。需要 `ScreenSpaceCamera` 画布的项目要自己换 UI 根。

### 与 Diagnostics / Bootstrap 的边界

当前不提供独立 Diagnostics 页签与 `IBootstrapStage`：模块状态简单，诊断窗口的概览行读取活动相机 id；
实现由调用方在组合根注入（或自行写一个引导阶段）。

## 设计原则

- **正交**：只做相机状态与过渡，不碰相机行为、UI 与场景编排。
- **后端中立**：门面与契约不出现 Cinemachine 类型；Cinemachine 后端隔离在 `Cinemachine/` 子目录。
- **不替项目定架构**：不提供相机栈、状态机、跟随策略或画面编排。

## 依赖

本模块依赖 `Unity.Cinemachine` **3.1.7**（核心 asmdef 硬引用，与 Input System 同型；版本真值由安装器清单 /
包 README / 开发 manifest 三处一致守卫），其余仅 `UnityEngine` 与框架内的 `XLog` / `XMessage`（状态事件）。
