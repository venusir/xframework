# Cinemachine 后端

`XFramework.XCamera.Cinemachine` 是 `CameraManager` 的 Cinemachine 3.x 内部适配器（`CinemachineCameraBackend`
是 internal 实现）。它**只做适配**：用 vcam 优先级做激活、用 Brain 的混合做过渡；跟随 / 瞄准 / Noise / Confiner /
Dolly / Timeline 仍由 Cinemachine 执行。

只支持 Cinemachine 3.x（按 3.1.7 的 API 编写）；2.x 不在支持范围，也没有公开的后端契约可替换。

## 快速接线

```csharp
using Unity.Cinemachine;
using XFramework.XCamera;
using XFramework.XCamera.Cinemachine;

CameraManager.Initialize(brain);

CameraManager.Register("gameplay", gameplayCam);
CameraManager.Register("aim", aimCam);
CameraManager.SwitchTo("gameplay");
```

- 一个进程建议只创建一个后端 / 一个 `CameraManager`：`CinemachineCore.GetBlendOverride` 是全局单委托。
- 构造后端会订阅四个静态 `CinemachineCore` 事件并安装 override；`Dispose` / 会话复位负责退订与恢复。
- 重复 `CameraManager.Initialize(brain)` 告警并保留第一个；不会创建第二个后端。

## 注册与 id

- 显式 id 由调用方给出；`CameraManager.Register(vcam)` 生成 `@auto/N`，同一预制体多实例互不冲突。
- 内置后端接受 `CinemachineVirtualCameraBase`（包括 `CinemachineCamera`、ClearShot、MixingCamera）。
- 同一 id + 同一相机幂等；同 id 不同相机、同一相机不同 id 抛 `ArgumentException`。
- ClearShot / MixingCamera 只注册**父相机**；子相机不注册，`ActiveCameraId` 会沿 `ParentCamera` 向上解析到父 id。
- 已销毁相机仍占槽位直到 `Unregister` 或下一次 `Register` 的陈旧槽清理。

## 激活与优先级

- `Activate` 把目标提升到「同通道场景最大优先级 + 1」：同通道的已注册相机按作者优先级计入
  （异通道不计入），未注册 vcam 只读取、不改写。
- 只改目标；切换 / 弹出 / 注销 / Dispose 时恢复目标**首次 boost 前**的 `PrioritySettings` 与
  GameObject / 组件启用状态（注册之后作者再改，按最新值恢复）。boost 期间管理器持有这三个字段，
  作者在持有期的改动会在切走时被恢复覆盖。
- 优先级并列时 Cinemachine 取最新 `ActivationId`；`PrioritySettings.Enabled == false` 时 `Value` 为 0。
- `ClearActive` 恢复 boost 并让 Brain 重选；**不保证** Brain 停止输出原相机（作者优先级仍最高时可能继续）。

## 过渡

- `duration <= 0.0001` 或 `CameraBlendStyle.Cut` 走 cut；NaN / ±∞ 抛 `ArgumentOutOfRangeException`。
- 非 cut 通过 `GetBlendOverride` 返回本次的 `CinemachineBlendDefinition`；**不修改** `DefaultBlend`。
- override 只在 `owner == brain` 且 `to == 请求目标` 时生效；无请求时链回构造时保存的前一个委托。
- cut 用 `Brain.ActiveBlend = null` 强制完成在途混合（上游支持的强制完成入口）。
- `Unregister` 只在被注销的正是当前请求目标时才强制完成在途混合；注销无关相机不影响别人的过渡。
- 项目 `CustomBlends` 先于 `GetBlendOverride` 求值，因此本次请求优先；Timeline 混合不经过该入口，不承诺控制。
- ClearShot 等分组相机的**内部子混合**通常以分组为 owner，本后端的请求不保证作用于它。

## 状态事件与请求结算

| 事件 | 本后端行为 |
|---|---|
| `CameraActivatedEvent` | 发布 `Activated`（incoming / outgoing id，未注册端为 `null`）；cut 同时发布 `BlendCompleted` 并结算请求 |
| `CameraDeactivatedEvent` | 发布 `Deactivated`（未注册相机为 `null`）；混合结束后才由 Cinemachine 触发 |
| `BlendFinishedEvent` | 发布 `BlendCompleted`；请求目标匹配时结算 `Completed` |
| `CameraUpdatedEvent` | 兜底：目标销毁 → `CameraNotFound`；已是活动相机的 cut → `Completed` / `Superseded` |
| `RequestSettled` | 每个请求恰好一次；导演层的异步等待只依赖它 |

cut 没有 `BlendFinishedEvent`，所以零时长完成必须在激活时补发；这是消息与异步都依赖的时序。

## 绑定

- `SetFollow` / `SetLookAt` 写 `CinemachineVirtualCameraBase.Follow/LookAt`；基类是抽象属性，ClearShot / MixingCamera
  同样有效。设 `LookAt` 会置 `CustomLookAtTarget`；`null` 直接复位该标志并清空 `LookAtTarget`
  （回退到 `TrackingTarget` / 分组默认目标），**不走 setter**——setter 会留下一个空的本地覆盖。
  分组相机的 `DefaultTarget.Enabled` 保持不变；`SetFollow(null)` 在分组相机上直接写 `TrackingTarget`，同样不启用默认目标。
- `SetFieldOfView` 只对 `CinemachineCamera` 写 `Lens.FieldOfView`；分组相机没有 Lens，告警一次并 no-op。
- `Shake` 在 `Brain.ControlledObject`（默认 Brain 自身）上惰性取 / 建 `CinemachineImpulseSource`，
  写 `ImpulseDefinition.ImpulseDuration` 后 `GenerateImpulseWithForce`。
  - 活动相机（分组的活动子相机）必须带 `CinemachineImpulseListener`；缺失时只告警一次，**不自动添加**。
  - 连续调用以最后一次 duration 为准；后端自建的 source 在 `Dispose` 时销毁，复用的既有 source 不销毁。
  - 无活动相机 / Brain 已销毁时告警并 no-op；无活动相机时也不会创建 `CinemachineImpulseSource`。

## 预制体自注册组件

`CinemachineCameraRegistration`（同目录）：

- `OnEnable` 在管理器已初始化时注册；`Start` 重试一次；仍未初始化则告警，调用方可稍后显式 `Register()`。
- `OnDestroy` 自动 `Unregister`；池化禁用不注销，重新启用幂等复用同一 id。
- `SwitchTo` / `Push`：先确保注册（幂等；管理器晚于 `OnEnable` 初始化时补注册）再激活；切基座 / 压栈各一个方法。
- 裸 vcam（不挂组件）用门面的 `CameraManager.RegisterAndSwitchTo` / `RegisterAndPush`；组件相机用本组件的同名方法，避免重复注册。
- 同一预制体多实例各得 `@auto/N`；ClearShot 只给父相机挂组件。
- 组件只应挂在 vcam 上；自带 `CinemachineBrain` 的相机预制体属于多 Brain / 独立输出，不在本后端射程。

## 外部接管与过场

- Timeline 用 `SetCameraOverride` 压过 vcam 优先级，本后端的 boost 不会挡住过场。
- 外部 / 未注册 incoming 会让当前请求结算 `Superseded`；模块**保持**栈顶 boost，过场释放后 Brain 重选，
  游戏相机自然恢复。
- 过场请使用未注册的专用 vcam。若 Timeline 绑定的是已注册游戏相机，后端无法区分「自己切换」与「Timeline 切换」，
  `IsExternallyControlled` / 消息 id 可能不符合直觉。
- 未注册活动相机会在切换时告警「可能与过场冲突」。

## 已知限制

- 只支持 Cinemachine 3.x；升级 3.x 大版本时按模块维护记录里的检查清单复核 API。
- `GetBlendOverride` 是全局单委托：项目若也使用它，以最后安装者为准；一个进程建议只创建一个后端。
- `ActiveVirtualCamera` 在 Brain 首次更新前为 `null`：`ActiveCameraId` 可能晚一次更新才可见。
- `OutputCamera` 可能为 `null`；`ControlledObject` 才是输出对象。
- ClearShot 内部子混合不受本次 `duration` / `style` 控制；分组相机没有 Lens。
- 震屏需要 Listener；缺失只告警一次。
- 外部接管时保持 boost 会让栈顶在优先级上压住未注册相机；这是为「过场结束后回到游戏相机」有意选择的。
- `ClearActive` / `Unregister` 不保证 Brain 停止输出某台相机。
- 没有分屏 / 多 Brain 路由；`OutputChannel` 与 Brain 的 `ChannelMask` 仍由 Cinemachine 配置。
  异通道相机可以注册，但不会被本 Brain 选中——`Activate` 它不会产生输出。
- 无活动相机时 `Shake` 只告警，不创建 `CinemachineImpulseSource`。

## 设计取舍

- **为什么只做适配**：手写位姿 / FOV 插值会与 Brain 的混合逻辑重复，并制造 `Deactivate` / `Unregister` / `IsTransitioning`
  的语义分叉；Cinemachine 已经是核心程序集硬依赖。
- **为什么用 `GetBlendOverride` 而不是改 `DefaultBlend`**：后者会污染之后所有未命中 `CustomBlends` 的过渡。
- **为什么外部接管保持 boost**：恢复作者优先级会让过场结束后的游戏相机在与其它相机的竞争中输掉。
- **为什么不自动加 `CinemachineImpulseListener`**：运行时静默改场景会掩盖配置问题；告警一次让使用方自己决定。
- **为什么 `Shake` 用 `ControlledObject`**：`OutputCamera` 可能为 null，`ControlledObject` 才是 Brain 驱动的对象。
- **为什么一步激活有两个入口**：组件持有 `@auto/N` 与 `OnDestroy` 注销的生命周期，`SwitchTo` / `Push` 必须走组件才能复用该 id 并支持晚初始化补注册；裸 vcam 没有组件，门面 `RegisterAnd...` 负责生成 id 并返回。门面若再加 `SwitchTo(vcam)` / `Push(vcam)` 重载，`SwitchTo(null)` 会产生二义，且已注册相机会与组件的所有权冲突。
