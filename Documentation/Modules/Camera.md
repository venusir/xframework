# Camera —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Camera/README.md`——已知限制、设计取舍、
> 接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Camera/
├── ICameraManager.cs      # 公开：后端契约（统一句柄注册 / 激活 / 反激活 / 过渡 / 查询）
├── CameraHandle.cs        # 公开：相机句柄（Id + Generation + NamedId；Generation=0 是 default 保留值）
├── CameraManager.cs       # 公开：静态门面（Initialize / Destroy / ResetForNewSession + 同名转发 + 字符串便捷重载 + 探测型读宽容）
├── CameraBlendStyle.cs    # 公开：Cut / EaseInOut / EaseIn / EaseOut / Linear
├── CameraTransitionResult.cs # 公开：Completed / Superseded / TargetUnregistered / ManagerDisposed
├── CameraInfo.cs          # 公开：逐相机诊断项（句柄 / 活动 / 过渡目标 / 存活 / 命名）
├── CameraStateSnapshot.cs # 公开：模块级诊断快照（计数 / 活动 / 过渡 / Brain 混合 / 输出相机）
├── Messages/
│   ├── CameraActivatedMessage.cs      # 公开：incoming 相机激活
│   ├── CameraDeactivatedMessage.cs    # 公开：已注册相机不再 live
│   └── CameraBlendCompletedMessage.cs # 公开：blend 完成（cut 视为零时长完成）
├── Cinemachine/
│   ├── CinemachineCameraManager.cs  # 公开：唯一内置后端（槽位表 + 动态 boost/恢复 + GetBlendOverride + 事件）
│   └── README.md                    # 使用向：接线、行为契约、已知限制、设计取舍
└── README.md              # 使用说明

Tests/Runtime/Camera/      # PlayMode：FakeCameraManager / CameraManagerTests / CameraFacadeCompletenessTests / CinemachineCameraManagerTests / CinemachineCameraEventTests / CinemachineCameraAsyncTests / CameraDiagnosticsTests
Tests/Editor/Camera/       # EditMode：CameraHandleTests（default / 相等性 / 便捷方法短路 / 未初始化宽容）
Tests/Editor/Architecture/ModuleBoundaryTests.cs  # CinemachineTypes_StayInsideTheCinemachineBackend（类型只允许出现在 Camera/Cinemachine/）
Runtime/Log/LogCategories.cs                     # CameraManager 分类
```

**Cinemachine 后端在本模块的子目录**：`Runtime/Camera/Cinemachine/` 随核心包发布，核心 asmdef 硬引用 `Unity.Cinemachine` 3.1.7（与 Input System 同型）。门面、`ICameraManager` 契约与其它模块保持后端中立，不得出现 Cinemachine 类型；Cinemachine 类型只允许出现在该子目录，由 `ModuleBoundaryTests.CinemachineTypes_StayInsideTheCinemachineBackend` 机械守卫。原独立扩展包 `com.venusy609.xframework.cinemachine` 已于 2026-10-22 删除；手写 `UnityCameraManager` 同日删除，Cinemachine 后端成为唯一内置实现。

**模块无自动初始化**：没有 `[RuntimeInitializeOnLoadMethod]` 自初始化；只有会话复位钩子（`SubsystemRegistration`），已登记进 `AutoInitTests` 族清单。

## 决策台账

- 已做：**v1 功能面建成**（2026-10-17）——`ICameraManager` 初始成员（`OutputCamera` / `CameraIds` / `ActiveCameraId` / `IsTransitioning` / `Activate` / `Deactivate` / `TransitionTo` / `IsActive`）；`CameraManager` 门面（生命周期 / 同名转发 / 会话复位 / 门面完备性测试）；`UnityCameraManager` 默认实现（一次一台活动相机、位姿 / FOV 插值、懒注册 LateUpdate ticker、取消 / 重开语义）；使用方 README 与包 README 四表同步。逐条证据在 `CHANGELOG`。
- 已做：**相机模块全面重整——Cinemachine 唯一内置后端、统一句柄、请求式过渡**（2026-10-22 · 用户裁定）——删除 `UnityCameraManager`；命名 / 动态相机统一经 `Register` 返回 `CameraHandle`，接口只收句柄；`CinemachineCameraManager` 用动态 boost + 作者状态恢复 + `CinemachineCore.GetBlendOverride` 请求 duration / style（**不改 `DefaultBlend`**），槽位表线性扫描取代 `_reverse`；门面探测型读宽容（`OutputCamera` / `ActiveHandle` / `ActiveCameraId` / `IsTransitioning` / `CameraCount` / `TryGetHandle` / `IsActive`），变更命令仍抛；`ICameraManager` 删除 string 操作与 `CameraIds`，新增 `CameraCount` / `TryGetHandle` / 命名 `Register`。证据：相机重整提交与 `PublicApiBaseline` diff。重开判据：无（用户明确要求全面重整）。
- 已推翻：**~~`UnityCameraManager` 作为核心默认实现~~**（2026-10-17）——~~核心包提供后端中立统一入口 + Unity 默认实现（手写位姿 / FOV 插值），Cinemachine 3.1.7 放独立扩展包~~。被 2026-10-22「Cinemachine 唯一内置后端」取代：Cinemachine 已是核心 asmdef 硬依赖，手写插值与 Brain 混合重复，且制造 `Deactivate` / `Unregister` / `IsTransitioning` 语义分叉；无 Cinemachine 项目直接用 Unity Camera API 或实现 `ICameraManager`。
- 已做：**相机状态事件与会话退订**（2026-10-22）——新增 `CameraActivatedMessage` / `CameraDeactivatedMessage` / `CameraBlendCompletedMessage`；Cinemachine 后端订阅 `CameraActivatedEvent` / `CameraDeactivatedEvent` / `BlendFinishedEvent` / `CameraUpdatedEvent`，按 `Origin` / mixer 过滤本 Brain 后经 `MessageManager` 发布；门面提供 `Subscribe`；内部 `ICameraManagerSessionReset` 让 `ResetForNewSession` 退订静态事件并恢复 `GetBlendOverride`，不碰场景对象。证据：`CinemachineCameraEventTests`（cut/blend 顺序、未注册 incoming、其他 Brain、Dispose/Unbind 后静默）与 `CameraManagerTests.ResetForNewSession_*`。
- 已做：**`TransitionToAsync` 与完成结果**（2026-10-22）——新增 `CameraTransitionResult`（`Completed` / `Superseded` / `TargetUnregistered` / `ManagerDisposed`）；接口 / 门面 / `CameraHandle` 提供异步入口；Cinemachine 后端用共享 `UniTaskCompletionSource` 的 pending，cut 在 `CameraActivatedEvent` 完成、blend 在 `BlendFinishedEvent` 完成，`CameraUpdatedEvent` 兜底目标销毁；取消只停止等待，不中止过渡。证据：`CinemachineCameraAsyncTests` 十二例。
- 已推翻：**~~`TransitionToAsync` 暂不做~~**（2026-10-17 · v1.1 候选）——~~v1 用 `IsTransitioning` + `UniTask.WaitUntil` 已能等待；异步入口要重新定义后端如何上报完成~~。被 2026-10-22 `TransitionToAsync` 实现取代：Cinemachine 的 `CameraActivatedEvent` / `BlendFinishedEvent` 足以给出完成信号，结果枚举区分完成 / 被取代 / 目标注销 / 管理器销毁。
- 已做：**诊断快照**（2026-10-22）——新增 `CameraInfo` / `CameraStateSnapshot` 与 `CopyCameras` / `GetState`；计数含已销毁未注销槽位（`IsAlive` 可辨）；`IsTransitioning` / `TransitionTarget` / `TransitionProgress` 只描述管理器自有过渡，`IsBrainBlending` 暴露 Brain 真实混合；`OverviewDiagnosticPanel` 的 Camera 行改读 `GetState`。**不新增独立 Camera 页签**（旧「暂不做：Diagnostics 面板」仍成立）。证据：`CameraDiagnosticsTests` 六例。
- 已做：**Cinemachine 3.1.7 后端硬合并进核心包**（2026-10-22 · 用户裁定）——`CinemachineCameraManager` 移入 `Runtime/Camera/Cinemachine/`，命名空间不变、程序集变为 `Venusy609.Xframework`；核心 asmdef 硬引用 `Unity.Cinemachine`；扩展包 / 独立 asmdef / `CinemachineDependencyTests` 删除。理由：Input System 先例、Unity 6000.3 最低版本 + Cinemachine 3 默认、自研相机后端非真实受众；门面 / 契约仍中立，由位置守卫守。**公开 API 基线 +1 类型（18 个成员），经用户确认**。证据与 3.x API 实测见 `Documentation/Cinemachine.md`。
- 已推翻：**~~Cinemachine 支持以独立可选扩展包提供~~**（2026-10-17）——~~`com.venusy609.xframework.cinemachine`（单独 asmdef，依赖 Cinemachine 3.1.7），实现同一 `ICameraManager`；核心包零 Cinemachine 依赖。~~ 被 2026-10-22「Cinemachine 3.1.7 后端硬合并进核心包」取代（用户裁定）；原理由「避免 2.x / 2D / 自定义后端被版本锁排除」在 Unity 6000.3 + Cinemachine 3 默认下不再成立，且 Input System 早已是核心硬依赖。
- 已做：**句柄化动态相机注册 / 激活**（2026-10-17）——`CameraHandle`（槽位 + 代际；`AudioHandle` 同形，`Generation = 0` 无效）+ `ICameraManager` 的 `Register<T>` / `Unregister(handle)` / `ActiveHandle` / 句柄版 `Activate` / `Deactivate` / `TransitionTo` / `IsActive` + 门面转发 + 两个实现的槽位表。命名 string id 保留；动态相机随预制 `Register` / `Dispose`，不需要 authoring id。**主接口直扩**（用户裁定，非可选能力接口）；模块未发布、仓外无 `ICameraManager` 实现，扩展包同轮更新。
- 已做：**合并 `ActiveCameraId` / `ActiveHandle`**（2026-10-17 · 用户裁定）——`ActiveHandle` 是接口 / 实现上唯一的活动查询；`CameraHandle` 新增 `NamedId`（命名 id；动态 / `default` 为 null），`CameraManager.ActiveCameraId` 直接读 `ActiveHandle.NamedId`（**O(1)、零遍历**）；`ICameraManager` / `UnityCameraManager` / `CinemachineCameraManager` 移除 `ActiveCameraId`。理由：两属性并行；命名 id 随句柄携带即可，接口不再维护第二份。对接口 / 类实现者是源码破坏（未发布、无仓外实现）。
- 不做：**Timeline / 过场相机进本模块**（2026-10-17 · 用户场景裁定）——过场由 Timeline 的 Cinemachine Track 接管 Brain；管理器改优先级 / 混合会与过场冲突；`CameraManager.ActiveCameraId` / `ActiveHandle` 对未注册的过场 vcam 返回 null / default，`OutputCamera` / `IsTransitioning` 仍可用；Cinemachine 扩展包对未注册活动相机告警。无重开判据。
- 不做：**string id 的动态注册**（2026-10-17 · 用户质疑）——随预制实例创建 / 销毁的相机没有稳定 authoring id；用句柄（槽位 + 代际）而不是让调用方生成 / 配对 id。无重开判据。
- 已推翻：**~~核心包硬依赖 Cinemachine~~**（2026-10-17 · 立项裁定）——~~Cinemachine 2.x / 3.x 的 API 不兼容，核心包一旦引用就必须锁一个主版本，把 2D / 2.x / 自定义后端项目挡在外面。替代是独立扩展包。无重开判据。~~ 被 2026-10-22「后端硬合并」取代（用户裁定）：Input System 先例、Unity 6000.3 + Cinemachine 3 默认、自研相机后端非真实受众；门面 / 契约的中立由 `CinemachineTypes_StayInsideTheCinemachineBackend` 守住。
- 不做：**相机栈 / push-pop / 返回上一相机**（2026-10-17 · 立项裁定）——「谁在栈顶 / 返回上一台」= 生命周期树，与 Update 的「组」API 同一条推理链。无重开判据。
- 不做：**跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline**（2026-10-17 · 立项裁定）——Cinemachine 的行为编排；框架内建只会与它竞争（与 Tween 同一理由）。**需要时的推荐路径**见模块 README 的「已知限制」三条：Cinemachine 级需求 → 用核心包内置的 `CinemachineCameraManager`（适配器，行为仍由 Cinemachine 提供）；简单需求 → 项目侧脚本；别的后端 → 实现 `ICameraManager`。无重开判据。
- 不做：**UI 相机绑定（ScreenSpaceCamera / WorldSpace 画布）**（2026-10-17 · 立项裁定）——UI 根强制 `ScreenSpaceOverlay`；相机类型 / RenderTexture / 是否挡输入是项目结构（与 UI 不内建特效层同一判据）。无重开判据。
- 暂不做：**Diagnostics 面板**（2026-10-17 · v1.1 候选）——模块状态简单（id 列表 / 活动 id / 过渡中），诊断窗口当前没有消费者。重开判据：出现「排查相机状态需要面板」的真实使用方。
- 暂不做：**`TransitionToAsync`**（2026-10-17 · v1.1 候选）——v1 用 `IsTransitioning` + `UniTask.WaitUntil` 已能等待；异步入口要重新定义后端如何上报完成（Cinemachine Brain 没有公开的完成回调）。重开判据：真实使用方需要 await 且 `IsTransitioning` 轮询不够。
- 暂不做：**`IBootstrapStage` / `DefaultGameLauncher` 相机字段**（2026-10-17 · v1.1 候选）——实现由调用方在组合根注入，且 `DefaultGameLauncher` 没有可序列化的相机注册表（句柄类型由后端决定）。重开判据：出现「启动引导必须负责相机初始化」的真实使用方。

- 已做：**`OutputCamera` null 语义与 `Unregister` 活动状态**（2026-10-18 · `deee388`）——核心只窄化措辞：`OutputCamera` 允许返回 null、`Unregister` 不保证 Cinemachine Brain 停用；Cinemachine 后端的实际行为随并入统一记入本模块（CAM-005 已关闭）。
- 已做：**外部销毁相机的变更面校验**（2026-10-20 · R3-CAM-002，本轮修复）——查询面已按 null 报告，但 `Activate` / `TransitionTo` 仍把假 null 的槽位当有效：`ActivateSlot` 关掉其它活相机却不启用目标，静默「成功」且没有任何相机输出。修法：四处变更面（id/handle × `Activate` / `TransitionTo`）追加 `IsUsable` 校验，不可用即 Warning 并整体忽略；回归 `UnityCameraManagerTests` 三例（改前红：活相机被关掉）。
- 已做：**`OutputCamera` 契约措辞改为「可能为 null、不保证等于目标」**（2026-10-19 · R3-CAM-003，文档订正）——Brain 对象无 Camera 时输出相机为 null，过渡期间也可能不是目标；`ICameraManager` XML 与模块 README 同步。
- 已做：**Cinemachine 后端的过渡时长有限性校验**（2026-10-20 · R3-CIN-002/CAM-001，Cinemachine 后端）——`CinemachineCameraManager` 把 NaN / ±∞ 直接交给 `CinemachineBlendDefinition`，与核心实现「抛 `ArgumentOutOfRangeException`」相反；补同形校验与消息，回归 `TransitionTo_NonFiniteDuration_Throws`（改前不抛）。

## 沿革与已否决形状

### 2026-10-13：不做

`Ledger.md` 原条目「不做：相机管理」——Cinemachine 已覆盖相机编排，框架内建只会与它竞争；UI 两处 `Camera.main` 是缓存过的，不值得为它开模块。**「编排归 Cinemachine」这一半今天仍然成立**。

### 2026-10-17：改为「统一入口」并立项

用户给出真实场景「用 Cinemachine，但要统一入口」，并追问「激活 / 反激活 / 相机过渡等不需要封装？」。一手核对 Cinemachine 3.1.7 源码后确认：

- 激活 / 反激活：`CinemachineVirtualCameraBase.OnEnable/OnDisable` 管理优先级队列；`Priority` 是 `PrioritySettings`（隐式 int 赋值会同时置 `Enabled`）；停用 GameObject 即 Disabled。
- 过渡：`CinemachineBrain.DefaultBlend` / `CustomBlends`；`CinemachineBlendDefinition.Styles` = Cut / EaseInOut / EaseIn / EaseOut / HardIn / HardOut / Linear / Custom。
- 3.x 与 2.x 不兼容：`CinemachineVirtualCamera → CinemachineCamera`、`ICinemachineCamera` 不再暴露 `Priority`、`m_DefaultBlend → DefaultBlend`、`UpdateMethod → UpdateMethods`。

结论：**编排仍归 Cinemachine**；框架补的是「状态 + 过渡 + 查询」的统一入口。核心包不锁 Cinemachine 版本（接口 + 默认 `UnityCameraManager`），Cinemachine 3.1.7 的实现放独立可选扩展包。采用率没有公开统计；Cinemachine 是 3D 事实标准但非全量，核心包因此不做硬依赖。**2026-10-22 已改为硬合并，见上方台账：后两条前提（采用率非全量 / 自研后端受众）复核后不成立。**

### 2026-10-17（第二问）：动态相机句柄，主接口直扩

用户追问「门面的动态相机」，场景分两类：

1. **Timeline / 过场相机**（随 CG 预制创建 / 销毁，Timeline 接管 Brain）——**不经过本模块**；管理器改优先级 / 混合会与过场冲突。Cinemachine 后端对「Brain 的活动相机未注册」告警；`CameraManager.ActiveCameraId` / `ActiveHandle` 对未注册活动相机返回 null / default。
2. **非 Timeline、随预制创建 / 销毁的动态相机**——需要管理器接管，但 string id 不适合：实例没有稳定 authoring id，还要生成 / 配对注销。

用户裁定**直接扩主接口**（不用可选能力接口）：

- `CameraHandle`（`readonly struct`，`Id` + `Generation`；`Generation = 0` 保留给 `default`；`AudioHandle` 同形；便捷方法 `Activate` / `Deactivate` / `TransitionTo` / `IsActive` / `Unregister` / `Dispose`）。
- `ICameraManager` 增加 `Register<T>(T)` / `Unregister(handle)` / `ActiveHandle` / 句柄版 `Activate` / `Deactivate` / `TransitionTo` / `IsActive`；命名 string 操作保留，形成**双键**。
- 两个实现同构：槽位表（`Slot`: 相机 / 可选命名 id / `Generation` / `Used`），槽位下标 = `CameraHandle.Id`；`Unregister` 清槽位、代际递增，旧句柄按代际失配失效。
- 动态活动时 `ActiveHandle.NamedId` / `CameraManager.ActiveCameraId` 为 null，用 `ActiveHandle` / `IsActive(handle)`；`CameraIds` 只列命名 id。
- 守卫：`CameraHandleTests`（default / 相等性 / 短路）、`CameraFacadeCompletenessTests`（接口每成员在门面同名）、`UnityCameraManagerTests` / `CinemachineCameraManagerTests` 的动态端到端与 stale/default/未注册活动相机用例、`PublicApiBaseline` 更新。

**随后（同日）用户指出 `ActiveCameraId` / `ActiveHandle` 并行、且门面遍历推导不可取**：`CameraHandle` 增加
`NamedId`（命名 id；动态 / `default` 为 null），接口 / 实现移除 `ActiveCameraId`，`CameraManager.ActiveCameraId`
直接读 `ActiveHandle.NamedId`（O(1)、零遍历）。`CameraHandleTests` 补 `NamedId` 用例；基线 / 文档 / CHANGELOG 同步。

### 2026-10-22：全面重整（Cinemachine 唯一后端）

用户认为 `CameraManager.cs` 与整个模块实现不好，要求结合第三方实现重整。第三方对照结论：主流相机管理器
（Unity Open Project Chop Chop、CineMachine-Manager 等）几乎清一色是「Cinemachine 薄适配 + 命名 / 优先级
切换」，不自己实现 blend。已核对 Cinemachine 3.1.7 源码：

- `CinemachineBlenderSettings.LookupBlend` 先查 `CustomBlends`，最后调用 `CinemachineCore.GetBlendOverride`；
  管理器可用后者让本次请求优先，而不必粘滞修改 `DefaultBlend`。
- `GetBlendOverrideDelegate` 签名 `(ICinemachineCamera from, ICinemachineCamera to,
  CinemachineBlendDefinition defaultBlend, UnityEngine.Object owner)`；它是**全局单委托**。
- `CameraActivatedEvent` / `CameraDeactivatedEvent` / `BlendFinishedEvent` / `CameraUpdatedEvent` 都是
  UnityEvent；`CameraUpdatedEvent` 在 Brain 更新末尾触发，可用来清理 pending（完整状态事件在后续提交补）。
- `PrioritySettings.Value` 在 `Enabled == false` 时返回 0；`CinemachineCore.GetVirtualCamera(i)` 返回
  `CinemachineVirtualCameraBase`，可直接读 `Priority`。

裁定：删除 `UnityCameraManager`；命名 / 动态统一句柄；目标 boost 到「场景内同通道最大 + 1」并恢复作者状态；
`GetBlendOverride` 请求 duration / style；`IsTransitioning` 只统计管理器自有的非 cut 过渡；门面探测型读宽容。
本轮同时补上状态事件与会话退订；`TransitionToAsync` 与诊断快照在后续提交。

### 已否决形状

- **核心包直接引用 Cinemachine**（2026-10-17 评估中一度考虑）：当时以「会让所有消费者被迫安装并锁一个主版本」为由改为扩展包；**2026-10-22 已推翻**——Input System 先例、Unity 6000.3 + Cinemachine 3 默认，硬合并落地（门面 / 契约仍中立）。
- **只给示例适配器、不进包的 Cinemachine 实现**（2026-10-17 早期计划）：用户明确要求「Cinemachine 直接实现，不要只是示例」；改为独立扩展包里的 Runtime 实现。**2026-10-22 扩展包已并入核心包**。
- **可选能力接口 `ICameraRegistry`**（2026-10-17）：作为「不扩主接口」的替代方案评估；用户选择**主接口直扩**——动态注册 / 激活是管理器的核心能力，不是后端可有可无的能力。
- **用 string id 注册动态相机**（2026-10-17）：随预制实例创建 / 销毁的相机没有稳定 authoring id；句柄（槽位 + 代际）才是答案。
- **让 CameraManager 接管 Timeline 过场相机**（2026-10-17）：过场系统已经接管 Brain；管理器插一手会冲突。
- **相机栈 / 状态机 / 跟随策略**：生命周期树与 GamePlay 编排，按项目定位否决。

## 审计记忆

- **`Camera` 与 `UnityEngine.Camera` 同名风险**：本模块没有 `Camera` 类型（门面是 `CameraManager`），因此没有 `SceneManager` 那种同名遮蔽问题；`OutputCamera` 返回的是 `UnityEngine.Camera`。
- **`GetBlendOverride` 是全局单委托**：管理器构造时保存前值并安装自己，`Dispose` 时仅当当前委托仍是自己才恢复前值；项目若也使用它，以最后安装者为准。一个进程建议只创建一个相机管理器；测试 fixture 必须在 `SetUp` / `TearDown` 把 `CinemachineCore.GetBlendOverride` 置回 `null`。
- **静态事件必须退订**：`CameraUpdatedEvent`（以及后续的 `CameraActivatedEvent` / `CameraDeactivatedEvent` / `BlendFinishedEvent`）是静态 UnityEvent；`Dispose` 与会话复位都必须 `RemoveListener`，否则关闭域重载时旧管理器被钉住。`CinemachineCore.ResetStaticsOnLoad` 并不重置全部事件。
- **槽位线性扫描是有意取舍**：相机数量通常个位数，O(n) 换取 `_reverse` 在替换 / 注销 / 外部销毁时的全部陈旧键分支；不要为「O(1)」重新引入反查表而丢掉这些分支的测试。
- **Cinemachine 后端在核心门禁射程内**（2026-10-22 起）：`check-docs` / `PublicApiBaseline` / `ReadmeTableTests` / 命名与边界扫描都覆盖 `Runtime/Camera/Cinemachine/`；原扩展包门禁缺口（R3-CIN-008）由并入结构性关闭。改动该子目录时按核心同一条门禁跑。

## 实测记录

- **2026-10-22 事件帧序**（`CinemachineCameraEventTests`，`ManualUpdate` 确定性）：cut A→B 为
  `Deactivated(A)` → `Activated(B, IsCut=true)` → `Completed(B)`；blend A→B 为开始帧 `Activated(B)`、
  完成帧 `Deactivated(A)` → `Completed(B)`。未注册 incoming 不发 `Activated`，旧注册相机仍发 `Deactivated`。
- **2026-10-22 `CameraUpdatedEvent` 兜底**（`CinemachineCameraAsyncTests.TargetDestroyedBeforeActivation...`）：
  目标在激活前被销毁时，`OnCameraActivated` 的 incoming 不再是 pending 目标；按 `_pending.Camera == null`
  报 `TargetUnregistered`（改前误报 `Superseded`）。
- **2026-10-22 `GetBlendOverride` 链**（`CinemachineCameraManagerTests.GetBlendOverride_ChainsPrevious...`）：
  无 pending 时调用前值并采用其返回值；`Dispose` 时当前委托仍是自己则恢复前值。
- **2026-10-22 异步取消**（`CinemachineCameraAsyncTests.Cancellation...`）：取消只停止等待，过渡继续并完成；
  预取消不启动。
- **仍未实测 / 需真实环境**：Timeline 混合是否经过 `GetBlendOverride`（当前只承诺“不受管理器控制”）；
  `PrioritySettings.Enabled == false` 对动态 boost 的影响（源码显示 `Value` 返回 0）；Brain 禁用时
  `CameraUpdatedEvent` 是否触发；关闭域重载连跑两遍（脚本表达不了，按惯例手工验收）。
