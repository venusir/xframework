# Camera —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Camera/README.md`——已知限制、设计取舍、
> 接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Camera/
├── ICameraManager.cs      # 公开：后端契约（OutputCamera / CameraIds / ActiveCameraId / IsTransitioning / 四操作）
├── CameraManager.cs       # 公开：静态门面（Initialize / Destroy / ResetForNewSession + 同名转发）
├── UnityCameraManager.cs  # 公开：默认实现（UnityEngine.Camera 注册表 + 位姿/FOV 插值 + 懒注册 LateUpdate ticker）
├── CameraBlendStyle.cs    # 公开：Cut / EaseInOut / EaseIn / EaseOut / Linear
└── README.md              # 使用说明

Tests/Runtime/Camera/      # PlayMode：FakeCameraManager / CameraManagerTests / CameraFacadeCompletenessTests / UnityCameraManagerTests
Tests/Editor/Architecture/ModuleBoundaryTests.cs  # 新增 CoreRuntime_DoesNotReferenceCinemachine（核心包零 Cinemachine 依赖）
Runtime/Log/LogCategories.cs                     # 新增 CameraManager 分类
```

**Cinemachine 不在本模块**：`com.venusy609.xframework.cinemachine` 是独立可选扩展包（`Assets/XFramework.Cinemachine/`，单独 asmdef，依赖 Cinemachine 3.1.7）。核心包与它的唯一联系是 `ICameraManager` 契约；核心 `Runtime/` 不得出现任何 Cinemachine 类型，由 `ModuleBoundaryTests.CoreRuntime_DoesNotReferenceCinemachine` 机械守卫。

**模块无自动初始化**：没有 `[RuntimeInitializeOnLoadMethod]` 自初始化；只有会话复位钩子（`SubsystemRegistration`），已登记进 `AutoInitTests` 族清单。

## 决策台账

- 已做：**v1 功能面建成**（2026-10-17）——`ICameraManager` 八成员（`OutputCamera` / `CameraIds` / `ActiveCameraId` / `IsTransitioning` / `Activate` / `Deactivate` / `TransitionTo` / `IsActive`）；`CameraManager` 门面（生命周期 / 同名转发 / 会话复位 / 门面完备性测试）；`UnityCameraManager` 默认实现（一次一台活动相机、位姿 / FOV 插值、懒注册 LateUpdate ticker、取消 / 重开语义）；使用方 README 与包 README 四表同步。逐条证据在 `CHANGELOG`。
- 已做：**Cinemachine 支持以独立可选扩展包提供**（2026-10-17）——`com.venusy609.xframework.cinemachine`（单独 asmdef，依赖 Cinemachine 3.1.7），实现同一 `ICameraManager`；核心包零 Cinemachine 依赖。理由与 3.x API 实测见 `Documentation/Cinemachine.md`。
- 不做：**核心包硬依赖 Cinemachine**（2026-10-17 · 立项裁定）——Cinemachine 2.x / 3.x 的 API 不兼容，核心包一旦引用就必须锁一个主版本，把 2D / 2.x / 自定义后端项目挡在外面。替代是独立扩展包。无重开判据。
- 不做：**相机栈 / push-pop / 返回上一相机**（2026-10-17 · 立项裁定）——「谁在栈顶 / 返回上一台」= 生命周期树，与 Update 的「组」API 同一条推理链。无重开判据。
- 不做：**跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline**（2026-10-17 · 立项裁定）——Cinemachine 的行为编排；框架内建只会与它竞争（与 Tween 同一理由）。无重开判据。
- 不做：**UI 相机绑定（ScreenSpaceCamera / WorldSpace 画布）**（2026-10-17 · 立项裁定）——UI 根强制 `ScreenSpaceOverlay`；相机类型 / RenderTexture / 是否挡输入是项目结构（与 UI 不内建特效层同一判据）。无重开判据。
- 暂不做：**Diagnostics 面板**（2026-10-17 · v1.1 候选）——模块状态简单（id 列表 / 活动 id / 过渡中），诊断窗口当前没有消费者。重开判据：出现「排查相机状态需要面板」的真实使用方。
- 暂不做：**`TransitionToAsync`**（2026-10-17 · v1.1 候选）——v1 用 `IsTransitioning` + `UniTask.WaitUntil` 已能等待；异步入口要重新定义后端如何上报完成（Cinemachine Brain 没有公开的完成回调）。重开判据：真实使用方需要 await 且 `IsTransitioning` 轮询不够。
- 暂不做：**`IBootstrapStage` / `DefaultGameLauncher` 相机字段**（2026-10-17 · v1.1 候选）——实现由调用方在组合根注入，且 `DefaultGameLauncher` 没有可序列化的相机注册表（句柄类型由后端决定）。重开判据：出现「启动引导必须负责相机初始化」的真实使用方。

## 沿革与已否决形状

### 2026-10-13：不做

`Ledger.md` 原条目「不做：相机管理」——Cinemachine 已覆盖相机编排，框架内建只会与它竞争；UI 两处 `Camera.main` 是缓存过的，不值得为它开模块。**「编排归 Cinemachine」这一半今天仍然成立**。

### 2026-10-17：改为「统一入口」并立项

用户给出真实场景「用 Cinemachine，但要统一入口」，并追问「激活 / 反激活 / 相机过渡等不需要封装？」。一手核对 Cinemachine 3.1.7 源码后确认：

- 激活 / 反激活：`CinemachineVirtualCameraBase.OnEnable/OnDisable` 管理优先级队列；`Priority` 是 `PrioritySettings`（隐式 int 赋值会同时置 `Enabled`）；停用 GameObject 即 Disabled。
- 过渡：`CinemachineBrain.DefaultBlend` / `CustomBlends`；`CinemachineBlendDefinition.Styles` = Cut / EaseInOut / EaseIn / EaseOut / HardIn / HardOut / Linear / Custom。
- 3.x 与 2.x 不兼容：`CinemachineVirtualCamera → CinemachineCamera`、`ICinemachineCamera` 不再暴露 `Priority`、`m_DefaultBlend → DefaultBlend`、`UpdateMethod → UpdateMethods`。

结论：**编排仍归 Cinemachine**；框架补的是「状态 + 过渡 + 查询」的统一入口。核心包不锁 Cinemachine 版本（接口 + 默认 `UnityCameraManager`），Cinemachine 3.1.7 的实现放独立可选扩展包。采用率没有公开统计；Cinemachine 是 3D 事实标准但非全量，核心包因此不做硬依赖。

### 已否决形状

- **核心包直接引用 Cinemachine**（2026-10-17 评估中一度考虑）：会让所有消费者被迫安装并锁一个主版本；改为扩展包。
- **只给示例适配器、不进包的 Cinemachine 实现**（2026-10-17 早期计划）：用户明确要求「Cinemachine 直接实现，不要只是示例」；改为独立扩展包里的 Runtime 实现。
- **相机栈 / 状态机 / 跟随策略**：生命周期树与 GamePlay 编排，按项目定位否决。

## 审计记忆

- **`Camera` 与 `UnityEngine.Camera` 同名风险**：本模块没有 `Camera` 类型（门面是 `CameraManager`），因此没有 `SceneManager` 那种同名遮蔽问题；`OutputCamera` 返回的是 `UnityEngine.Camera`。
- **懒注册 ticker 的代价**：过渡起步必须是下一帧；若改成常驻 Tier7，档位桶会让起步延迟最多 2.1 秒（已写进使用方 README 的「内部机制」）。
- **扩展包不在核心门禁射程**：`check-docs` / `ReadmeTableTests` / 公开 API 基线只覆盖核心；扩展包用自己的测试守依赖 / 示例 / 边界。下一轮改扩展包时不要指望核心门禁报红。
