# Cinemachine 后端 —— 维护向记录

> **收件人**：框架作者（升级 Cinemachine / 审计相机后端时）。**不是**使用文档。
> 使用说明在 `Assets/XFramework/Runtime/Camera/Cinemachine/README.md` 与 `Runtime/Camera/README.md`；本文件不重复。
> **本文件不随包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 本文回答：**后端为什么并入核心包、钉哪个版本、3.x 的哪些 API 是实测过的、升级时重看哪里**。

## 一、结论与边界

- **已并入核心包**：`CinemachineCameraBackend` 位于 `Assets/XFramework/Runtime/Camera/Cinemachine/`，命名空间
  `XFramework.XCamera.Cinemachine`，程序集 `Venusy609.Xframework`；原独立扩展包
  `com.venusy609.xframework.cinemachine` 已删除（2026-10-22，用户裁定）。类型在 2026-10-23 由
  `CinemachineCameraManager` 重命名为 `CinemachineCameraBackend`。
- **适配器，不是行为库**：跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline 由 Cinemachine
  提供并在 Cinemachine 里配置；本后端只负责注册、优先级 / Brain 混合、事件、薄绑定与状态查询。**行为本身仍归 Cinemachine**。
- **混合边界**：gameplay 面（切 / 压 / 弹 / 绑定 / 查询 / 订阅）只用 id 与中立类型；Cinemachine 类型只出现在
  `Initialize(CinemachineBrain)`、`Register(..., vcam)`、`TryGetCamera(..., out vcam)`、`ActiveCamera` 四个入口。
  CM 类型允许出现在 `Runtime/Camera/`，其它模块不得引用；由
  `ModuleBoundaryTests.CinemachineTypes_StayInsideTheCameraModule` 守卫。**中立文件的代码与字符串字面量也不得出现 `Cinemachine` 字样**。
- **只支持 Cinemachine 3.x**（按 **3.1.7** 编写）：2.x 的类型名、字段名与命名空间都不同；2.x 项目需要自己的
  `ICameraBackend` 实现。
- **身份**：唯一身份是 `string` id；动态 vcam 经 `CameraManager.Register(vcam)` 得到 `@auto/N`。
  `CinemachineCameraRegistration` 负责预制体实例的自动注册 / 注销。
- **Timeline / 过场不接管**：Timeline 用 `SetCameraOverride` 压过优先级；过场 vcam 不注册；Brain 的活动相机未注册时
  `ActiveCameraId` 为 `null`，切换会告警「可能与过场冲突」。本后端在外部接管时**保持**栈顶 boost，
  过场释放后 Brain 重选即可回到游戏相机。
- **依赖真值（三处）**：`Editor/XFrameworkDependencyInstaller.cs` 的 `RequiredUPMDependencies` = 核心 `README.md`
  的「第三方依赖」表 / JSON 块 = 开发工程 `Packages/manifest.json`，均为 `"com.unity.cinemachine": "3.1.7"`；
  由 `Tests/Editor/Architecture/DependencyDeclarationTests` 守卫。核心 `package.json` 的 `dependencies` 仍留空
  （与 Input System 同型）。

## 二、为什么并入核心包

原决策（2026-10-17）把 Cinemachine 支持放独立可选扩展包，理由是「2.x / 3.x API 不兼容，核心包一旦引用就必须锁一个
主版本，把 2D / 2.x / 自定义后端挡在外面」。2026-10-22 复核后推翻：

1. **Input System 先例**：核心 asmdef 已经硬引用一个可选后端包；版本锁只影响安装，不泄漏到公开面。
2. **最低版本已抬高**：核心包要求 Unity `6000.3`，Cinemachine 3.x 是 Unity 6 默认，2.x 项目不是现实受众。
3. **自研相机后端非真实受众**：相机编排事实上归 Cinemachine；`ICameraBackend` 仅作 internal 测试缝 / 实现缝，
   公开面不再承诺可替换后端。
4. **并包消除三类长期成本**：独立安装顺序与 tag 错位（R3-CIN-004 出过事）、双份版本 / CHANGELOG / README、
   扩展包不在核心门禁射程（R3-CIN-008）。

## 三、文件结构

```
Assets/XFramework/Runtime/Camera/
├── CameraManager.cs / ICameraBackend.cs / CameraDirector.cs   # 门面（4 个 CM 入口）/ internal 契约 / internal 导演层
├── Messages/                                                  # 三种中立消息
├── README.md                                                  # 模块使用说明
└── Cinemachine/
    ├── CinemachineCameraBackend.cs        # internal sealed : ICameraBackend, IDisposable
    ├── CinemachineCameraRegistration.cs   # public sealed MonoBehaviour：预制体自注册
    ├── CinemachineImpulse.cs              # internal：震屏辅助
    └── README.md                          # 后端使用向：接线、行为、限制、取舍

Assets/XFramework/Tests/Runtime/Camera/
├── CinemachineCameraBackendTests.cs       # PlayMode：注册 / 优先级 / 混合 / 绑定 / 震屏
├── CinemachineCameraRegistrationTests.cs  # PlayMode：自注册 / 池化 / 未初始化时序
├── CinemachineCameraEventTests.cs         # PlayMode：消息顺序 / 外部 null / 过滤 / 静默
├── CinemachineCameraAsyncTests.cs         # PlayMode：完成 / 取代 / 取消 / 错误结果
└── ...（CameraManagerTests / CameraDirectorTests / CameraDiagnosticsTests / FakeCameraBackend）

Assets/XFramework/Samples~/Cinemachine/    # CameraSample.cs + .sample.json（不参与 Unity 编译）
```

## 四、Cinemachine 3.1.7 实测事实（一手读包内源码 + 运行时测试）

| 事实 | 结论 |
|---|---|
| 命名空间 / 程序集 | `Unity.Cinemachine`（2.x 是 `Cinemachine`）；asmdef 引用名 `Unity.Cinemachine` |
| vcam 类型 | `CinemachineCamera : CinemachineVirtualCameraBase`；ClearShot / MixingCamera 派生自 `CinemachineCameraManagerBase` |
| 目标设置 | `CinemachineVirtualCameraBase` 声明**抽象** `Follow` / `LookAt`；`CinemachineCamera` 写 `Target.TrackingTarget` / `LookAtTarget`（设 `LookAt` 置 `CustomLookAtTarget`）；分组相机同样可设 |
| Lens | `CinemachineCamera.Lens` 是公开 `LensSettings` 字段；只有 `CinemachineCamera` 有（分组相机没有） |
| `Priority` | `CinemachineVirtualCameraBase.Priority` 是 `PrioritySettings`（`Enabled` + `Value`，隐式 int 赋值会置 `Enabled = true`），不在 `ICinemachineCamera` 上；`Value` 在 `Enabled == false` 时返回 0 |
| 队列刷新 | 改 `Priority` 后必须调用 `Prioritize()`；优先级并列时取最新 `ActivationId` |
| 混合配置 | `CinemachineBrain.DefaultBlend` 与 `CustomBlends`；样式枚举 `CinemachineBlendDefinition.Styles`；`CinemachineBlenderSettings.LookupBlend` 先 `CustomBlends`，最后调 `CinemachineCore.GetBlendOverride` |
| `GetBlendOverride` | `(ICinemachineCamera from, ICinemachineCamera to, CinemachineBlendDefinition defaultBlend, UnityEngine.Object owner)`；**全局单委托**；本后端只在 `owner == brain` 且 `to == 请求目标` 时生效 |
| 状态查询 | `CinemachineBrain.ActiveVirtualCamera`（混合中为 incoming）/ `IsBlending` / `ActiveBlend` / `OutputCamera` / `ControlledObject`；`ActiveBlend = null` 是官方支持的强制完成入口 |
| 静态事件 | `CameraActivatedEvent`（含 `Origin` / `OutgoingCamera` / `IncomingCamera` / `IsCut`）、`CameraDeactivatedEvent`、`BlendFinishedEvent`、`CameraUpdatedEvent`；均为静态，必须显式退订 |
| cut 完成 | `BlendFinishedEvent` **不对 cut 触发**；cut 必须在激活事件补发零时长完成 |
| 反激活时序 | `CameraDeactivatedEvent` 在混合结束后才触发 |
| 外部 incoming 的 outgoing | 混合开始帧的 `OutgoingCamera` 可能是 `NestedBlendSource` 而不是原 vcam；本后端用「最近一次 incoming id」兜底消息里的 previous id |
| 分组解析 | `CinemachineVirtualCameraBase.ParentCamera` 返回父 mixer；子相机 `OnEnable` 时缓存，运行时创建必须**先建父、再建子** |
| Timeline | Timeline 用 `SetCameraOverride` 压过 vcam 优先级；`BlendCreatedEvent` 不对 Timeline 混合发送 |
| Impulse | `CinemachineImpulseSource.GenerateImpulseWithForce(float)`；`ImpulseDefinition.ImpulseDuration` 可写；活动相机需带 `CinemachineImpulseListener` 才生效；source 挂在 `Brain.ControlledObject` 上 |
| 手动驱动 | `CinemachineBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate` + `ManualUpdate(int currentFrame, float deltaTime)`；测试用它做确定性推进 |
| 空 pipeline | `CinemachineCamera` 不要求 body / aim 组件；空 pipeline 是合法的被动相机 |

## 五、决策台账

- 已做：**从零重设计为 `CinemachineCameraBackend` + 中立主面 + 逃生舱**（2026-10-23 · 用户裁定；`58a9b62` /
  `ebe10d7` / `849f199` / `6f99584` / `04661ea`）——删除 `CameraHandle` / `ICameraManager` / `CinemachineCameraManager`；
  vcam 以 string id 注册，动态 vcam 用 `@auto/N` + `CinemachineCameraRegistration`；后端暴露 `Activated` /
  `Deactivated` / `BlendCompleted` / `RequestSettled` 与 `SetFollow` / `SetLookAt` / `SetFieldOfView` / `Shake`；
  外部接管保持 boost。证据：`CinemachineCameraBackendTests` / `...RegistrationTests` / `...EventTests` / `...AsyncTests`。
  该条的公开 `ICameraBackend` / `CinemachineCameraBackend` 已在同日第二轮收为 internal（见下条）。
- 已做：**组合根 / 高级访问原生化 + 后端 internal**（2026-10-23 · `e6d56db`）——`Initialize(CinemachineBrain)`；
  `Register` / `TryGetCamera` / `ActiveCamera` 使用 `CinemachineVirtualCameraBase`；`ICameraBackend` /
  `CameraBackendActivation` / `ICameraBackendSessionReset` / `CinemachineCameraBackend` 收为 internal；
  gameplay 面保持 string id + `CameraBlendStyle` + id 消息；`ModuleBoundaryTests` 射程放宽为 `Runtime/Camera/`。
- 已推翻：**~~统一句柄 + 槽位代际~~**（2026-10-22 · `4e34616`）——~~命名 / 动态 vcam 统一 `CameraHandle`，槽位下标 =
  Id、代际防旧句柄~~。被 2026-10-23「string id + `@auto/N`」取代：动态 id 已按实例唯一；句柄代际只在槽位复用时有用，
  而注销 / 重注册窗口由单 pending 覆盖。
- 已推翻：**~~不做相机栈 / 跟随 / 震屏~~**（2026-10-17）——~~Cinemachine 的行为编排；框架内建只会与它竞争~~。
  被 2026-10-23「栈 + 薄绑定」取代：模块只协调，行为仍由 Cinemachine 执行。
- 已推翻：**~~事件只对已注册相机发布 / 外部 incoming 不发布 Activated~~**（2026-10-22）——被 2026-10-23
  「外部 id 为 null」取代：HUD / 音频 / 诊断需要看到外部接管。
- 已推翻：**~~命名 / 动态双键（string id + CameraHandle 两套操作）~~**（2026-10-17）——被 2026-10-22「统一句柄」
  取代，再被 2026-10-23「string id 唯一身份」取代。
- 已推翻：**~~`Activate` 在「正在混合且目标已是活动相机」时短暂失活 / 激活目标 GameObject~~**（2026-10-17）——
  前提失实；`ActiveBlend = null` 是官方的强制完成入口（R3-CIN-001）。
- 已推翻：**~~Cinemachine 3.1.7 一等实现以独立可选扩展包提供~~**（2026-10-17）——被 2026-10-22「硬合并进核心包」取代。
- 已做：**Cinemachine 3.1.7 后端硬合并进核心包**（2026-10-22 · 用户裁定）——核心 asmdef 硬引用 `Unity.Cinemachine`；
  扩展包 / 独立 asmdef / `CinemachineDependencyTests` 删除；版本三处由 `DependencyDeclarationTests` 守。
- 已做：**外部接管保持 boost**（2026-10-23 · `04661ea`）——过场释放后回到游戏相机；恢复作者优先级会让栈顶输掉竞争。
- 已做：**ClearShot 子相机向上解析父 id**（2026-10-23 · `04661ea`）——`ParentCamera` 链；测试用 `SetCameraOverride`
  把子相机设为活动并断言父 id。
- 已做：**震屏 source 惰性创建 + Listener 告警一次**（2026-10-23 · `6f99584`）——不自动添加 Listener。
- 不做：**自动添加 `CinemachineImpulseListener`**（2026-10-23）——不静默改场景。重开判据：真实项目反馈震屏静默失效且无法通过文档解决。
- 不做：**支持 Cinemachine 2.x**（2026-10-17）——类型名 / 命名空间 / 混合字段全不同；2.x 不在支持范围，也没有公开后端契约。无重开判据。
- 不做：**清空 `brain.CustomBlends`**（2026-10-17）——项目配置优先；本次请求经 `GetBlendOverride` 覆盖，不清空资产。无重开判据。
- 不做：**接管 Timeline 过场相机**（2026-10-17）——Timeline 已接管 Brain；过场用未注册专用 vcam。无重开判据。
- 已做：**`OutputCamera` null 语义与 `Unregister` 活动状态**（2026-10-18 · CAM-005；2026-10-19 R3-CAM-003 结案）
  ——核心契约窄化为「可能为 null、不保证等于目标」；后端直接转发 Brain 输出相机，`Unregister` 不改场景状态。
- 已做：**外部销毁 vcam 的变更面统一告警 no-op**（2026-10-20 · R3-CIN-003）——假 null 检测 → Warning 后忽略。
- 已做：**过渡时长有限性校验与核心同形**（2026-10-20 · R3-CIN-002/CAM-001）——NaN / ±∞ 抛 `ArgumentOutOfRangeException`。

## 六、依赖与安装

- **Cinemachine 3.1.7 是核心 asmdef 的硬依赖**（与 Input System 同型）：必须写进安装器清单、核心 `README.md` 的
  依赖表 / JSON 块与开发 `Packages/manifest.json` 三处并逐字一致，由 `DependencyDeclarationTests` 守卫。
- **安装顺序不再存在**：核心包与 Cinemachine 后端同属一个 UPM 包；按包 README 装好第三方依赖即可。
- **`package.json` 的 `dependencies` 仍留空**：UPM 不支持 Git URL 依赖，本仓所有依赖都走 manifest + 安装器。
- **示例**：`Samples~/Cinemachine` 不参与 Unity 编译；`.cs.meta` 手工维护，由 `check-samples.ps1` 编译校验。

## 七、升级 Cinemachine 3.x 时重看哪里

1. `CinemachineVirtualCameraBase.Priority` / `Prioritize()` / `PrioritySettings.Enabled`：boost 与作者状态恢复。
2. `CinemachineCore.GetBlendOverride` 的委托签名与 `LookupBlend` 顺序；`owner` 过滤。
3. `CinemachineBrain.ActiveVirtualCamera` / `IsBlending` / `ActiveBlend` / `OutputCamera` / `ControlledObject` / `ManualUpdate`。
4. `CinemachineCore` 四个静态事件的触发时机；尤其 cut 没有 `BlendFinishedEvent`、反激活在混合结束。
5. `CinemachineVirtualCameraBase.Follow` / `LookAt` 抽象属性与 `CinemachineCamera.Lens`。
6. `CinemachineImpulseSource` / `CinemachineImpulseListener` / `ImpulseDefinition.ImpulseDuration`。
7. `CinemachineBrain.SetCameraOverride` / `ReleaseCameraOverride`（Timeline 与测试）。
8. 升级后跑 `run-tests.ps1 -Filter Cinemachine -Platform All`；`DependencyDeclarationTests` 会因版本三处不一致而红。

## 八、测试射程

- `CinemachineCameraBackendTests`（PlayMode）：注册冲突 / 幂等 / 错误类型 / 已销毁；boost 高于未注册同通道相机；
  作者状态恢复；cut 强制完成；duration / style 经 override；`DefaultBlend` 不变；外部接管保持 boost 并恢复；
  ClearShot 父 id 解析；Follow / LookAt / FOV / Shake；其他 Brain 过滤；Dispose / 会话复位。用
  `ManualUpdate` 确定性推进，每个 fixture 复位 `CinemachineCore.GetBlendOverride`。
- `CinemachineCameraRegistrationTests`（PlayMode）：OnEnable 注册 / 两实例不同 id / OnDestroy 栈回退 / 池化幂等 /
  未初始化告警后显式注册 / ClearShot 只注册父相机 / 管理器先销毁时安全。
- `CinemachineCameraEventTests`（PlayMode）：cut / blend 消息顺序、外部 null id、其他 Brain 过滤、Dispose / 会话复位静默。
- `CinemachineCameraAsyncTests`（PlayMode）：blend / cut 完成、同请求 join、同步命令 / 外部接管 supersede、
  注销 / 激活前销毁 `CameraNotFound`、Destroy / 会话复位 `ManagerDisposed`、取消只停等待、预取消、非有限时长。
- `CameraDiagnosticsTests`（PlayMode）：`GetState` 计数 / 活动 / 过渡 / 外部标志、`CopyCameras` 标志与清理。
- **公开 API 已入核心基线**：`CameraManager` 的 4 个 CM 入口（`Initialize` / `Register` / `TryGetCamera` /
  `ActiveCamera`）与 `CinemachineCameraRegistration` 由 `PublicApiBaselineTests` 锁定；`CinemachineCameraBackend` /
  `CameraBackendActivation` 为 internal，不在公开面。
- **模块边界守卫**：`ModuleBoundaryTests.CinemachineTypes_StayInsideTheCameraModule` 扫核心 `Runtime/**/*.cs`
  （剥注释），`Camera/` 之外出现 `Cinemachine` 即红。
