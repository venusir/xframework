# Camera —— 游戏相机统一入口（string id + 相机栈）

## 概述

`XFramework.XCamera` 提供「切到哪台相机 / 临时覆盖 / 返回 / 跟随谁 / 震一下 / 当前输出相机是谁」的统一入口。
gameplay 动词（切 / 压 / 弹 / 跟随 / 震屏 / 查询 / 订阅）只使用 string id 与中立类型，不必在满代码里
`using Unity.Cinemachine`；Cinemachine 类型只出现在初始化、注册与高级访问。默认后端是 Cinemachine 3.x
（`XFramework.XCamera.Cinemachine`），且是唯一内置后端。

命名空间：`XFramework.XCamera`（消息在 `XFramework.XCamera.Messages`，内置后端在 `XFramework.XCamera.Cinemachine`）。

它**只做协调**：注册表、相机栈、过渡请求、异步等待、事件与薄绑定。跟随 / 瞄准的计算、Lens、Noise / Impulse、
Confiner、Dolly、ClearShot 选择、Timeline 仍归 Cinemachine；投影、世界↔屏幕、射线、Culling、RenderTexture 仍归
`UnityEngine.Camera`。容易混淆的是「相机栈」：这里指**逻辑覆盖栈**（基础相机 + 临时覆盖），不是 URP 的渲染相机栈。

## 快速使用

```csharp
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera;
using XFramework.XCamera.Cinemachine;

public sealed class CameraBootstrap : MonoBehaviour
{
    [SerializeField] private CinemachineBrain _brain;
    [SerializeField] private CinemachineCamera _gameplay;
    [SerializeField] private CinemachineCamera _aim;
    [SerializeField] private Transform _player;

    private void Awake()
    {
        CameraManager.Initialize(_brain);

        CameraManager.Register("gameplay", _gameplay);
        CameraManager.Register("aim", _aim);

        CameraManager.SwitchTo("gameplay");
        CameraManager.SetFollow("gameplay", _player);
    }

    private void OnDestroy() => CameraManager.Destroy();

    public void AimPressed() => CameraManager.Push("aim", 0.2f);
    public void AimReleased() => CameraManager.Pop(0.25f);
    public void Hit() => CameraManager.Shake(0.4f);
}
```

要点：

- 显式 id 由调用方给出；动态相机用 `CameraManager.Register(vcam)` 拿一个运行时生成的 `@auto/N` id。
- `SwitchTo` 设定基础相机；`Push` 压入临时覆盖；`Pop` / `PopAll` 返回。
- `Initialize` 取后端所有权，`Destroy` 释放；关闭域重载时会话复位只丢引用、不 Dispose。
- 需要直接配置 Lens / Aim / Confiner 时用
  `CameraManager.TryGetCamera("gameplay", out CinemachineVirtualCameraBase cam)` 或 `CameraManager.ActiveCamera`；
  这些是高级访问，gameplay 代码通常不需要。

## 模块结构

```
Runtime/Camera/
├── CameraManager.cs              # 公开：静态门面（生命周期 / 注册 / 栈 / 异步 / 绑定 / 查询 / 事件）
├── ICameraBackend.cs             # internal：后端契约 + CameraBackendActivation + 会话复位能力接口
├── CameraDirector.cs             # internal：id 生成 / 栈 / pending / 消息发布 / 快照组装
├── CameraBlendStyle.cs           # 公开：Cut / EaseInOut / EaseIn / EaseOut / Linear
├── CameraTransitionResult.cs     # 公开：Completed / Superseded / CameraNotFound / ManagerDisposed
├── CameraInfo.cs                 # 公开：逐相机诊断项
├── CameraStateSnapshot.cs        # 公开：模块级诊断快照
├── Messages/
│   ├── CameraActivatedMessage.cs
│   ├── CameraDeactivatedMessage.cs
│   └── CameraBlendCompletedMessage.cs
├── Cinemachine/
│   ├── CinemachineCameraBackend.cs        # internal：内置 Cinemachine 3.x 后端
│   ├── CinemachineCameraRegistration.cs   # 公开：预制体 / 动态相机自注册组件
│   ├── CinemachineImpulse.cs              # internal：震屏辅助
│   └── README.md
└── README.md
```

## 行为契约

### 身份与注册

- 唯一身份是 `string` id，大小写敏感。显式 id 不能为空、全空白或以 `@auto/` 开头（保留给动态 id）。
- 同一 id + 同一相机重复注册幂等返回同一 id；同 id 不同相机、同一相机不同 id、已销毁 Unity 对象、
  后端不接受的对象类型抛 `ArgumentException`；字面 `null` 抛 `ArgumentNullException`。
- 注册不激活、不改变场景状态。动态 id 是 `@auto/N`，**不透明**，不要解析或持久化。
- 已销毁但尚未注销的相机仍占槽位；`CameraCount` / `CopyCameras` 含它，`CameraInfo.IsAlive` 可辨。
  下一次 `Register` 会先释放已销毁的槽位；`Unregister` 用于显式清理；查询本身不会隐式删除槽位。

### 相机栈

- `SwitchTo(id)`：把栈重置为 `[id]`（清空全部覆盖），并激活。
- `Push(id)`：追加一层覆盖；目标已是请求栈顶且没有在途混合时 no-op（外部接管时会重新激活）。
- `Pop()`：弹出栈顶覆盖并回到上一层；只有基础相机时告警 no-op。
- `PopAll()`：清空全部覆盖，回到基础相机。
- `Clear()`：清空栈并释放本管理器的激活请求；输出驱动自行重选，**不保证**停止输出某台相机。
- `Unregister(id)` 会把 id 从栈中移除；若移除的是栈顶，切到新栈顶（无则清空）。中间层移除不改变当前激活。
- 后端报告某台相机 `Deactivated` 而它已销毁时，导演层会剪除死栈项，并在需要时切到新栈顶。

### 过渡

- `blendDuration <= 0.0001` 或 `style == Cut` 视为 cut；NaN / ±∞ 抛 `ArgumentOutOfRangeException`。
- 非 cut 经后端请求本次 duration / style；内置后端用 `CinemachineCore.GetBlendOverride`，**不修改** Brain 的
  `DefaultBlend`，且本次请求优先于项目的 `CustomBlends`。
- 目标已是请求栈顶时不再叠加请求，也不承诺重启已在进行的混合。
- cut 在内置后端用 `Brain.ActiveBlend = null` 强制完成在途混合。

### 异步

- 四个动词：`SwitchToAsync` / `PushAsync` / `PopAsync` / `PopAllAsync`，返回
  `UniTask<CameraTransitionResult>`。
- 同一请求（目标 id + cut 性 + duration + style）共享同一 pending；不同请求 / 同步命令 / 外部接管 →
  `Superseded`；目标注销或销毁 → `CameraNotFound`；`Destroy` / 会话复位 → `ManagerDisposed`。
- 取消只停止等待，**不中止**相机过渡；预取消不启动请求。
- 完成由后端的过渡事件驱动；输出驱动被禁用或不再推进时等待不会完成，请用取消 / 调用方超时兜底。

### 事件

- `CameraManager.Subscribe` 三种消息：激活 / 反激活 / 过渡完成。订阅经 `MessageManager`，
  **不依赖相机门面是否初始化**，返回的句柄释放即退订。
- 外部 / 未注册相机的 `CameraId` 为 `null`（`IsExternal == true`）。
- 时序：cut = `Deactivated → Activated(IsCut=true) → BlendCompleted`；blend = 开始帧 `Activated`、
  结束帧 `Deactivated → BlendCompleted`。cut 的完成消息与旧行为一致（零时长语义）。
- 订阅是全局通道；多 Brain / 多管理器场景请按 `CameraId` 过滤。

### 探测型读与命令

- 未初始化时：`ActiveCameraId` / `BaseCameraId` / `StackDepth` / `HasActiveCamera` / `OutputCamera` /
  `IsBlending` / `IsExternallyControlled` / `CameraCount` / `GetState` / `CopyCameras` / `TryGetCamera`
  返回默认值；`Unregister` 返回 `false`。
- 注册与变更命令（`Register` / `SwitchTo` / `Push` / `Pop` / `PopAll` / `Clear` / 绑定 / `Shake`）在未初始化时抛
  `InvalidOperationException`，消息带 `[CameraManager]` 前缀与修复提示。
- 未知 id / 已销毁相机的同步命令告警并 no-op；异步返回 `CameraNotFound`。

### 外部接管与优先级

- 内置后端把目标提升到「同通道场景最大优先级 + 1」并恢复上一个目标被改过的作者状态。
- 外部系统接管时**保持**栈顶的 boost：过场释放后 Brain 重选，游戏相机自然恢复；`Clear` / `Unregister` /
  `Dispose` 才恢复作者状态。
- `IsExternallyControlled`：输出驱动有活动相机、它不是请求栈顶、且没有在途请求。
- Timeline 用 `SetCameraOverride` 压过 vcam 优先级，本模块的 boost 不会挡住它；过场请使用**未注册**的专用 vcam。

## 架构设计

```
CameraManager（静态门面：生命周期 / 参数校验 / 宽容读 / Subscribe）
   └─ CameraDirector（internal：id 生成 / 栈 / pending / 消息 / 快照 / 外部接管判定）
        └─ ICameraBackend（internal 原语：注册 / 激活 / 清空 / 绑定 / 查询 / 事件）
             └─ CinemachineCameraBackend（internal：槽位 / 优先级 boost+restore / GetBlendOverride / 静态事件）
```

- 注册表真相在后端；导演层只持有 id 栈与异步 pending。
- 优先级与混合请求在后端；导演层只决定「目标 id」。
- 消息由导演层转发到 `MessageManager`；外部 incoming 的 id 为 `null`。
- gameplay 面只通过 id / 中立类型交互；Cinemachine 类型只出现在初始化、注册与高级访问。

## API 参考

| 分组 | 入口 | 说明 |
|---|---|---|
| 生命周期 | `Initialize(CinemachineBrain)` / `Destroy()` / `IsInitialized` | 门面创建并拥有内部后端；重复注入告警并保留第一个 |
| 注册 | `Register(string, CinemachineVirtualCameraBase)` / `Register(CinemachineVirtualCameraBase)` / `Unregister(string)` | 显式 id 或生成 `@auto/N`；注册不激活 |
| 注册并激活 | `RegisterAndSwitchTo(CinemachineVirtualCameraBase, ...)` / `RegisterAndPush(...)` | 裸 vcam：生成 `@auto/N`、注册并立即切基座 / 压栈；返回 id |
| 高级访问 | `TryGetCamera(string, out CinemachineVirtualCameraBase)` / `ActiveCamera` | 取回 vcam 以配置 Lens / Aim / Confiner；gameplay 代码通常不需要 |
| 播放 | `SwitchTo` / `Push` / `Pop` / `PopAll` / `Clear` | 基础相机 + 覆盖栈；参数为 `blendDuration` 与 `CameraBlendStyle` |
| 异步 | `SwitchToAsync` / `PushAsync` / `PopAsync` / `PopAllAsync` | 返回 `UniTask<CameraTransitionResult>`；取消只停等待 |
| 绑定 | `SetFollow` / `SetLookAt` / `SetFieldOfView` / `Shake` | 薄绑定；行为仍由后端执行 |
| 查询 | `ActiveCameraId` / `BaseCameraId` / `StackDepth` / `HasActiveCamera` / `OutputCamera` / `IsBlending` / `IsExternallyControlled` / `GetState()` / `CopyCameras(List<CameraInfo>)` | 探测型读；`GetState` 单次、无额外分配 |
| 事件 | `Subscribe(Action<CameraActivatedMessage>)` 等三种 | 经 `MessageManager`；不依赖初始化 |

`CinemachineCameraRegistration.SwitchTo` / `Push` 是预制体组件路径的一步激活（先确保注册再激活）；
门面 `RegisterAnd...` 不接收已注册相机，按 `Register` 契约抛 `ArgumentException`。

值类型：`CameraBlendStyle`、`CameraTransitionResult`、`CameraInfo`、`CameraStateSnapshot`。
`CameraStateSnapshot` 刻意不叫 `CameraState`——使用方同时 `using Unity.Cinemachine` 时会与 `Unity.Cinemachine.CameraState` 撞名。

## 典型场景

### 开镜 / 收镜

```csharp
CameraManager.Push("aim", 0.2f);   // 进入瞄准相机
// ...
CameraManager.Pop(0.25f);          // 回到基础相机
```

### Boss 出场演出并等待

```csharp
var result = await CameraManager.PushAsync("bossIntro", 1.2f,
    CameraBlendStyle.EaseInOut, destroyCancellationToken);
if (result == CameraTransitionResult.Completed)
    PlayBossIntroTimeline();

// 演出结束
CameraManager.Pop(0.8f);
```

### 预制体携带的动态相机

```csharp
var instance = Instantiate(vehiclePrefab);
var registration = instance.GetComponentInChildren<CinemachineCameraRegistration>();
registration.Push(0.5f);   // 一步：确保注册 + 压栈激活；切基座用 SwitchTo

// 销毁实例时组件自动 Unregister，栈回到上一个相机
```

`SwitchTo` / `Push` 先确保注册（幂等）；管理器晚于 `OnEnable` 初始化时也能补注册。同一预制体实例化多次
会各自得到独立的 `@auto/N`，不会互相顶替。池化对象禁用不注销、销毁才注销；若返回池前要求它离开栈，
显式 `Unregister()` 或先 `Pop()`。

### 自持有裸 vcam

不挂 `CinemachineCameraRegistration` 时，门面可以直接完成「注册 + 激活」，并返回生成的 `@auto/N`：

```csharp
var vcam = new GameObject("BossCam").AddComponent<CinemachineCamera>();
var id = CameraManager.RegisterAndPush(vcam, 0.5f); // 或 RegisterAndSwitchTo

// 结束时自行注销并销毁
CameraManager.Unregister(id);
Destroy(vcam.gameObject);
```

门面的 `RegisterAnd...` 面向尚未注册的相机；已注册的相机请用 `SwitchTo(id)` / `Push(id)`，带组件的相机
请用组件上的同名方法——门面方法不会静默复用已有 id，会按 `Register` 契约抛 `ArgumentException`。

### 重定向与震屏

```csharp
CameraManager.SetFollow("gameplay", player);
CameraManager.SetLookAt("gameplay", lockTarget); // null 清除
CameraManager.Shake(0.4f, 0.2f);
```

### HUD 射线

```csharp
var camera = CameraManager.OutputCamera;
if (camera != null)
{
    Ray ray = camera.ScreenPointToRay(Input.mousePosition);
}
```

`OutputCamera` 可能为 `null`，且过渡期间不保证等于目标；不要用它判断「是否有已注册的活动相机」。

## 内部机制

- **优先级**：内置后端在**首次 boost 前**记录目标被改写前的 `PrioritySettings` / GameObject / 组件启用状态
  （注册之后作者再改，按最新值恢复）；只 boost 目标，不把其它相机压成 0；切换 / 弹出 / 注销 / Dispose 时恢复。
  boost 期间管理器持有这三个字段，作者在持有期的改动会在切走时被恢复覆盖。
- **混合请求**：`GetBlendOverride` 只对 `owner == brain` 且 `to == 请求目标` 生效；无请求时链回前一个委托。
- **请求结算**：cut 在激活事件结算；blend 在完成事件结算；`CameraUpdatedEvent` 兜底目标销毁与「已是活动相机的 cut」。
- **外部接管**：外部 incoming 让后端结算 `Superseded`，但不恢复 boost；`IsExternallyControlled` 由导演层按栈顶判定。
- **快照**：`GetState` 直接从后端属性组装；`CopyCameras` 复用内部 id buffer，调用方 buffer 容量足够时不再分配。

## 生命周期与清理

- 组合根调用一次 `CameraManager.Initialize(brain)`；`Destroy` 释放内部后端并恢复场景状态（不销毁 Brain）。
- 关闭域重载时，`ResetForNewSession` 只丢引用、不 Dispose；后端 `UnbindSessionEvents` 退订静态事件。
- 动态相机：用 `CinemachineCameraRegistration` 或在生成 / 销毁处显式 `Register` / `Unregister`。
- 预制体只应携带 vcam；自带 `CinemachineBrain` 的相机预制体属于多 Brain / 独立输出，不在本模块射程。
- 管理器不销毁相机对象；`Unregister` 只恢复状态并移除槽位。

## 线程契约

全部 API 仅在主线程调用；注册表、栈与 pending 都不是线程安全的。

## 性能代价

- 模块**不注册 ticker**，没有每帧开销；过渡由后端的输出驱动推进。
- 注册 / 切换为 O(栈深) 与 O(注册数) 的小规模操作；动态 id 生成只分配一次字符串。
- `GetState` 单次组装；`CopyCameras` 在 buffer 容量足够时零分配（内部 id buffer 复用）。
- 异步等待每次请求一个 `UniTaskCompletionSource`；没有请求时无分配。
- `Shake` 的 `ImpulseSource` 惰性创建一次；`GetBlendOverride` 是全局单委托。

## 已知限制

- **一次只有一台活动相机**：分屏、小地图、多 RenderTexture 不在射程。
- **Timeline / 过场**：Timeline 用 `SetCameraOverride` 压过优先级；本模块不接管。过场应使用未注册的专用 vcam；
  若 Timeline 绑定的是已注册游戏相机，模块无法区分「自己切换」与「Timeline 切换」，`IsExternallyControlled` 可能误判。
- **ClearShot / 分组相机**：只注册父相机；活动子相机解析为父 id；分组**内部子混合**不受本次 duration / style 控制。
- **`OutputCamera` 可能为 `null`**，且不保证等于过渡目标；不能当作「有活动相机」的判据。
- **`ActiveCameraId` 可能晚一次输出驱动更新才可见**；请求后不要假设同步可读。
- **`IsBlending` 含外部混合**；`GetState().IsTransitioning` 只统计本管理器发起的非 cut 过渡。
- **计数含已销毁未注销槽位**；用 `CameraInfo.IsAlive` 区分。
- **外部事件 id 为 `null`**；订阅是全局通道，多 Brain 需按 id 过滤。
- **异步完成由事件驱动**：输出驱动被禁用 / 不再推进时等待不会完成；取消只停止等待。
- **`GetBlendOverride` 是全局单委托**：一个进程建议只创建一个 `CameraManager`；项目若也使用它，以最后安装者为准。
- **`Clear` / `Unregister` 不保证停止输出**：恢复作者状态后，若该相机作者优先级仍最高，输出驱动可能继续用它。
- **震屏要求活动相机带 `CinemachineImpulseListener`**：缺失时只告警一次、不自动添加；连续调用以最后一次时长为准；
  自建 `ImpulseSource` 挂在输出对象上，复用的既有 source 不销毁。
- **`SetFieldOfView` 只对 `CinemachineCamera` 有效**：分组相机没有 Lens，告警一次并 no-op。
- **动态 id 不透明**：不要解析 / 持久化 `@auto/N`；显式 id 不得以该前缀开头。
- **没有公开的后端契约**：Cinemachine 是唯一内置后端；替换后端等于替换模块。需要直接驱动
  `UnityEngine.Camera` 的项目请直接用 Unity API。
- **无 UI 相机绑定**：UI 根固定 `ScreenSpaceOverlay`；`ScreenSpaceCamera` / RenderTexture 是项目结构。
- **没有过场 / 演出编排**：Timeline、镜头序列、过场状态机不在本模块。

## 设计取舍

- **为什么用 string id 而不是句柄**：双键（句柄 + 字符串）让每个入口都要选；动态相机没有稳定 authoring id，
  但运行时生成的 `@auto/N` 已经唯一，句柄的代际语义只在「槽位复用」时有用，而注销 / 重注册的窗口由单 pending 覆盖。
- **为什么做相机栈**：游戏真实需求是「临时覆盖再返回」（开镜、对话、Boss 演出）；把返回逻辑留给每个项目
  重复实现，正是本模块要收敛的部分。栈只持有 id，不引入生命周期树。
- **为什么 gameplay 面用 id、组合根用 CM 类型**：id 让切 / 压 / 弹 / 跟随 / 震屏 / 订阅不碰
  `Unity.Cinemachine`；初始化 / 注册 / 高级访问（`TryGetCamera` / `ActiveCamera`）用 CM 原生类型，换取编译期安全。
  代价是公开面绑定 Cinemachine 3.x——CM 大版本改名按框架大版本升级处理。
- **为什么用 `GetBlendOverride` 而不是改 `DefaultBlend`**：后者会污染之后所有未命中 `CustomBlends` 的过渡；
  前者把 duration / style 限定在本次请求。
- **为什么外部接管保持 boost**：过场释放后要能自然回到游戏相机；恢复作者优先级会让栈顶在与其它相机的竞争中输掉。
- **为什么震屏不自动加 Listener**：组件是场景配置的一部分；运行时静默添加会让「为什么这个相机多了组件」无法解释。
- **为什么叫 `CameraStateSnapshot`**：避免与 `Unity.Cinemachine.CameraState` 撞名；同时明确它是快照。

## 接口承诺到哪为止

| 能力 | 归属 |
|---|---|
| 注册 / 注销、相机栈、切换 / 过渡、异步等待、状态消息、诊断快照、外部接管判定 | 本模块 |
| 跟随 / 瞄准 / Orbit、Lens / FOV、Noise / Impulse 信号、Confiner / Dolly / Target Group、ClearShot 选择 | Cinemachine |
| 世界↔屏幕、射线、投影、Near / Far、Culling Mask、Clear Flags、RenderTexture、Viewport | `UnityEngine.Camera`（经 `OutputCamera`） |
| 分屏 / 多 RenderTexture、相机深度排序、UI 相机绑定、存档 / 后期 | 项目 |
| 过场 / 演出相机、Timeline 轨道 | Timeline / 过场系统 |

判据：只补「游戏代码需要跨系统协调、且不想直接依赖 Cinemachine 类型」的那一层；Cinemachine 已给的行为不重写。

## 与相邻模块的关系

- **Message**：三种状态消息经 `MessageManager` 发布；订阅句柄可配合 `destroyCancellationToken` 自动退订。
- **Scene**：场景加载 / 卸载不自动调用相机；随场景销毁的相机由注册组件注销，管理器剪除死槽。
- **Diagnostics**：概览页的 Camera 行读取 `GetState()`；没有独立 Camera 页签。
- **Log**：告警统一用 `LogCategories.CameraManager`。
- **UI / Input**：本模块不绑定 UI 相机，也不处理输入；UI 根固定 `ScreenSpaceOverlay`。

## 设计原则

- 用手游 / 主机游戏的语言命名：切、压、弹、返回、跟随、震。
- 模块只协调，行为交给后端；不重复实现 Cinemachine 已有的能力。
- 显式注册优于场景扫描；id 不依赖 GameObject 名字。
- 探测型读宽容，变更命令明确失败；所有失败都有修复提示。
- 主线程、无每帧 ticker、异步由事件派生。

## 依赖

- Unity 6000.3+。
- `com.unity.cinemachine` 3.1.7：内置后端；核心程序集**无条件引用**。未安装时整个核心程序集无法编译。
- UniTask（异步返回值）、Message（事件）、Log（告警）：核心包内依赖，无需额外安装。
- 无 options / 无配置文件；注册与过渡参数都在运行时给出。
