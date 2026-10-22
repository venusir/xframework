# Cinemachine 后端 —— 维护向记录

> **收件人**：框架作者（升级 Cinemachine / 审计相机后端时）。**不是**使用文档。
> 使用说明在 `Assets/XFramework/Runtime/Camera/Cinemachine/README.md` 与 `Runtime/Camera/README.md`；本文件不重复。
> **本文件不随包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 本文回答：**后端为什么并入核心包、钉哪个版本、3.x 的哪些 API 是实测过的、升级时重看哪里**。

## 一、结论与边界

- **已并入核心包**：`CinemachineCameraManager` 位于 `Assets/XFramework/Runtime/Camera/Cinemachine/`，命名空间 `XFramework.XCamera.Cinemachine`，程序集 `Venusy609.Xframework`；原独立扩展包 `com.venusy609.xframework.cinemachine` 已删除（2026-10-22，用户裁定）。
- **适配器，不是行为库**：跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline 由 Cinemachine 提供并在 Cinemachine 里配置；本后端只负责注册、优先级 / Brain 混合与状态查询。**行为本身仍归 Cinemachine**。
- **后端中立承诺不变**：门面 `CameraManager`、契约 `ICameraManager` 与其它模块不得出现 Cinemachine 类型，由 `ModuleBoundaryTests.CinemachineTypes_StayInsideTheCinemachineBackend` 守卫；Cinemachine 类型只允许出现在 `Runtime/Camera/Cinemachine/`。2026-10-22 起 `CinemachineCameraManager` 是**唯一内置后端**，手写 `UnityCameraManager` 已删除。
- **只支持 Cinemachine 3.x**（按 **3.1.7** 编写）：2.x 的类型名、字段名与命名空间都不同；2.x 项目需要自己的 `ICameraManager` 实现。
- **统一句柄**：命名 vcam 与动态 vcam 都经 `Register` 返回 `CameraHandle`；命名 id 在 `NamedId` 里（动态为 `null`），动态活动时 `CameraManager.ActiveCameraId` 为 `null`。槽位表（`Slot`: vcam / 可选命名 id / `Generation` / `Used` / 注册时捕获的作者状态）；槽位下标 = `CameraHandle.Id`。
- **Timeline / 过场不接管**：过场 vcam 不注册；Brain 的活动相机未注册（Timeline / 外来 vcam）时，`Activate` / `TransitionTo` 告警「可能与过场冲突」；`CameraManager.ActiveCameraId` / `ActiveHandle` 返回 null / default，`OutputCamera` 仍可读；`IsTransitioning` 只反映管理器自有过渡（此时通常为 false）。
- **依赖真值（三处）**：`Editor/XFrameworkDependencyInstaller.cs` 的 `RequiredUPMDependencies` = 核心 `README.md` 的「第三方依赖」表 / JSON 块 = 开发工程 `Packages/manifest.json`，均为 `"com.unity.cinemachine": "3.1.7"`；由 `Tests/Editor/Architecture/DependencyDeclarationTests` 守卫。核心 `package.json` 的 `dependencies` 仍留空（与 Input System 同型）。
- **为什么可以硬依赖**：核心 asmdef 早已无条件引用 `Unity.InputSystem` 并靠安装器 / README 兜底（R3-INP-001）；「零可选依赖」不是本仓的真实原则，「门面 / 契约后端中立」才是。Unity 6000.3 最低版本 + Cinemachine 3 默认，使版本锁的排除面小于原判断。

## 二、为什么并入核心包

原决策（2026-10-17）把 Cinemachine 支持放独立可选扩展包，理由是「2.x / 3.x API 不兼容，核心包一旦引用就必须锁一个主版本，把 2D / 2.x / 自定义后端挡在外面」。2026-10-22 复核后推翻：

1. **Input System 先例**：核心 asmdef 已经硬引用一个可选后端包；版本锁只影响安装，不泄漏到公开面。
2. **最低版本已抬高**：核心包要求 Unity `6000.3`，Cinemachine 3.x 是 Unity 6 默认，2.x 项目不是现实受众。
3. **自研相机后端非真实受众**：相机编排事实上归 Cinemachine；`ICameraManager` 的价值是让游戏代码不绑 CM 类型（可测试、可替换），不依赖「有人自研后端」这一假设。
4. **并包消除三类长期成本**：独立安装顺序与 tag 错位（R3-CIN-004 出过事）、双份版本 / CHANGELOG / README、扩展包不在核心门禁射程（R3-CIN-008）。

## 三、文件结构

```
Assets/XFramework/Runtime/Camera/Cinemachine/
├── CinemachineCameraManager.cs  # public sealed : ICameraManager, IDisposable；槽位表 + 动态 boost + GetBlendOverride
└── README.md                    # 使用向：接线、行为契约、已知限制、设计取舍

Assets/XFramework/Tests/Runtime/Camera/CinemachineCameraManagerTests.cs  # PlayMode（ManualUpdate 确定性）
Assets/XFramework/Samples~/Cinemachine/                                   # CameraSample.cs + .sample.json（不参与 Unity 编译）
```

核心包根另有：`Runtime/Camera/README.md` 的模块结构列出该子目录；`package.json` 的 `samples` 登记示例。

## 四、Cinemachine 3.1.7 实测事实（一手读包内源码 + 运行时测试）

| 事实 | 结论 |
|---|---|
| 命名空间 | **`Unity.Cinemachine`**（2.x 是 `Cinemachine`） |
| 程序集 | `Unity.Cinemachine`（asmdef 引用名） |
| vcam 类型 | `CinemachineCamera : CinemachineVirtualCameraBase`（2.x 是 `CinemachineVirtualCamera`） |
| `Priority` | `CinemachineVirtualCameraBase.Priority` 是 `PrioritySettings`（`Enabled` + `Value`，隐式 int 赋值会置 `Enabled = true`），**不在 `ICinemachineCamera` 上**；`Value` 在 `Enabled == false` 时返回 0 |
| 队列刷新 | 改 `Priority` 后必须调用 `CinemachineVirtualCameraBase.Prioritize()`；否则要等该 vcam 下一帧 `Update`（它比较 `Priority.Value != m_QueuePriority`）。`MoveToTopOfPrioritySubqueue()` 是过时别名 |
| 混合配置 | `CinemachineBrain.DefaultBlend`（原 `m_DefaultBlend`）与 `CustomBlends`（原 `m_CustomBlends`）；样式枚举 `CinemachineBlendDefinition.Styles`（原 `Style`）。`CinemachineBlenderSettings.LookupBlend` 先 `CustomBlends`，最后调 `CinemachineCore.GetBlendOverride` |
| `GetBlendOverride` | `CinemachineCore.GetBlendOverrideDelegate (ICinemachineCamera from, ICinemachineCamera to, CinemachineBlendDefinition defaultBlend, UnityEngine.Object owner)`；**全局单委托**。管理器用它让本次请求优先于 `CustomBlends`，不改 `DefaultBlend`；Timeline 混合不经过它 |
| 状态查询 | `CinemachineBrain.ActiveVirtualCamera` / `IsBlending` / `ActiveBlend` / `OutputCamera` |
| 静态事件 | `CinemachineCore.CameraActivatedEvent`（`UnityEvent<ActivationEventParams>`，含 `Origin` / `OutgoingCamera` / `IncomingCamera` / `IsCut`）、`CameraDeactivatedEvent` / `BlendFinishedEvent`（`UnityEvent<ICinemachineMixer, ICinemachineCamera>`）、`CameraUpdatedEvent`（`UnityEvent<CinemachineBrain>`）；均为静态，必须显式退订 |
| `GetVirtualCamera` | `CinemachineCore.GetVirtualCamera(int)` 返回 `CinemachineVirtualCameraBase`（可直接读 `Priority`）；`VirtualCameraCount` 给数量；`CinemachineBrain.IsValidChannel(CinemachineVirtualCameraBase)` 筛同通道 |
| 手动驱动 | `CinemachineBrain.UpdateMethod`（类型 `CinemachineBrain.UpdateMethods`，**嵌套枚举**）+ `ManualUpdate(int currentFrame, float deltaTime)`；测试用它做确定性推进 |
| 空 pipeline | `CinemachineCamera` 不要求 body / aim 组件；空 pipeline 是合法的被动相机（按 GameObject transform 驱动） |
| 过场集成 | Timeline 的 Cinemachine Track 会把过场 vcam 设为 Brain 的活动相机（**推理**：本轮未接真实 `PlayableDirector`；「未注册活动相机 → `ActiveHandle.NamedId` null + 激活/过渡告警」已由 PlayMode 用例实测） |

## 五、决策台账

- 已推翻：**~~Cinemachine 3.1.7 一等实现以独立可选扩展包提供~~**（2026-10-17）——~~`CinemachineCameraManager` 用优先级做激活 / 反激活、`DefaultBlend` 做过渡；核心包保持零依赖。理由：2.x / 3.x API 不兼容，核心包一旦引用就必须锁一个主版本；采用率没有公开统计，不能假设所有消费者都用 Cinemachine。~~ 被 2026-10-22「Cinemachine 3.1.7 后端硬合并进核心包」取代（用户裁定）：Input System 先例、Unity 6000.3 + Cinemachine 3 默认、自研相机后端非真实受众；门面 / 契约的中立改由位置守卫守住。
- 已做：**Cinemachine 3.1.7 后端硬合并进核心包**（2026-10-22 · 用户裁定）——源码移入 `Runtime/Camera/Cinemachine/`，命名空间不变、程序集变为 `Venusy609.Xframework`；核心 asmdef 硬引用 `Unity.Cinemachine`；扩展包 / 独立 asmdef / `CinemachineDependencyTests` 删除；`package.json` `dependencies` 仍留空，版本真值按 Input System 先例落安装器 + 核心 README + 开发 manifest 三处。**公开 API 基线 +1 类型（18 个成员），经用户确认**。
- 已做：**相机模块全面重整（Cinemachine 唯一后端 / 统一句柄 / GetBlendOverride）**（2026-10-22 · 用户裁定）——删除 `UnityCameraManager`；命名 / 动态 vcam 统一经 `Register` 返回 `CameraHandle`，接口只收句柄；目标动态 boost 到「场景内同通道最大 + 1」并恢复注册时捕获的作者状态；过渡走 `CinemachineCore.GetBlendOverride` 请求 duration / style（**不改 `DefaultBlend`**）；`IsTransitioning` 只统计管理器自有非 cut 过渡；槽位表线性扫描取代 `_reverse`。证据：`CinemachineCameraManagerTests` 与 `PublicApiBaseline` diff。
- 已推翻：**~~命名 / 动态双键（string id + CameraHandle 两套操作）~~**（2026-10-17）——~~命名 vcam 用 string id，动态 vcam 用句柄；接口 / 门面 / 实现各背两套 `Activate` / `Deactivate` / `TransitionTo` / `IsActive`~~。被 2026-10-22「统一句柄」取代：接口只收句柄，字符串只是门面糖，命名注册也进接口。
- 已做：**相机状态事件与会话退订**（2026-10-22）——订阅 `CameraActivatedEvent` / `CameraDeactivatedEvent` / `BlendFinishedEvent` / `CameraUpdatedEvent`，按 `Origin` / mixer 过滤本 Brain，发布 `CameraActivatedMessage` / `CameraDeactivatedMessage` / `CameraBlendCompletedMessage`；`Dispose` 与内部 `ICameraManagerSessionReset.UnbindSessionEvents` 退订并恢复 `GetBlendOverride` 前值。cut 顺序为 `Deactivated → Activated(IsCut) → Completed`，blend 为 `Activated → Deactivated → Completed`；未注册 incoming 不发布 Activated。证据：`CinemachineCameraEventTests`。
- 已推翻：**~~`Activate` 在「正在混合且目标已是活动相机」时短暂失活 / 激活目标 GameObject~~**（2026-10-17 · 原称「测试锁定」）——~~Brain 的公开面没有「强制完成 / 重开混合」的入口；副作用是该 vcam 的 `PreviousStateIsValid` 重置~~ 该前提失实：`CinemachineBrain.ActiveBlend = null` 是上游文档明说的「强制完成混合」入口；旧 hack 同步 off/on 后下一帧仍是同一目标，Brain 不会新建混合，原过渡按原时长继续，且当时没有对应用例。被 2026-10-19「R3-CIN-001：改用 `ActiveBlend = null` + 补改前必红用例」取代。
- 已做：**改优先级后立即 `Prioritize()`**（2026-10-17 · `CinemachineCameraManagerTests` 改前必红：不调用时 `TransitionTo_BlendsAndCompletes` / `TransitionTo_ZeroDuration_Cuts` 失败，Brain 仍停在旧活动相机）。
- 已做：**动态 vcam 句柄（`Register<T>` / `ActiveHandle` / 槽位表）**（2026-10-17 · 用户裁定主接口直扩）——命名 vcam 保持 string id；动态 vcam 随预制 `Register` / `Dispose`，不需要 authoring id。槽位下标 = `CameraHandle.Id`，代际递增防旧句柄误用；动态活动时 `ActiveHandle.NamedId` 为 `null`，用 `ActiveHandle`。
- 已做：**合并 `ActiveCameraId` / `ActiveHandle`**（2026-10-17 · 用户裁定）——`CameraHandle` 增加 `NamedId`；`CinemachineCameraManager` 移除 `ActiveCameraId`；`CameraManager.ActiveCameraId` 直接读 `ActiveHandle.NamedId`（O(1)、零遍历）。命名槽位的 `NamedId` 即 vcam 的命名 id，动态槽位 null。
- 已做：**未注册活动相机告警**（2026-10-17）——Brain 的活动相机未在管理器注册（Timeline / 外来 vcam）时，`Activate` / `TransitionTo`（非 cut）告警「可能与过场冲突」；`CameraManager.ActiveCameraId` / `ActiveHandle` 返回 `null` / `default`。`Activate_WhenBrainActiveIsUnregistered_LogsWarning` 锁定。
- 不做：**让 CameraManager 接管 Timeline 过场相机**（2026-10-17 · 用户场景裁定）——Timeline 已经接管 Brain；管理器改优先级 / 混合会冲突。过场相机由过场系统管理。无重开判据。
- 不做：**在 Cinemachine 后端里清空 `brain.CustomBlends`**（2026-10-17）——项目配置优先；清空会悄悄改写项目行为。无重开判据。
- 不做：**支持 Cinemachine 2.x**（2026-10-17）——2.x 与 3.x 的类型名、命名空间、混合字段全不同，一份代码无法同时编译；2.x 项目写自己的 `ICameraManager`。无重开判据。
- 暂不做：**`TransitionToAsync` / 过渡完成回调**（2026-10-17 · v1.1 候选；2026-10-19 订正措辞）——Brain 没有 **await 形态**的完成回调，需要轮询 `IsBlending`（`ActiveBlend` / `CameraActivated` 等可观测，只是没有 Task/UniTask 形态）；核心接口当前用 `IsTransitioning` 覆盖。重开判据：真实使用方需要 await 且轮询不够。
- 已做：**过渡时长有限性校验与核心同形**（2026-10-20 · R3-CIN-002/CAM-001）——NaN / ±∞ 直接交给 `CinemachineBlendDefinition`，而核心 `UnityCameraManager` 对同一参数抛 `ArgumentOutOfRangeException`；同一接口两种实现行为相反。修法：`TransitionToSlot` 补同形校验与消息；回归 `TransitionTo_NonFiniteDuration_Throws`（改前不抛）。
- 已做：**外部销毁 vcam 的变更面统一告警 no-op**（2026-10-20 · R3-CIN-003）——`Deactivate` 访问已销毁组件的 `gameObject` 抛 `MissingReferenceException`，`Activate` / `TransitionTo` 静默 no-op，反查表残留陈旧键（被销毁对象作字典键已不可寻址）。修法：六处变更面（id/handle × 三操作）统一假 null 检测 → Warning 后忽略，并按值定位重建反查表；`Register` 替换与 `ReleaseSlot` 两条清理路径同步补齐；回归五个用例（改前必红）。
- 已做：**兼容声明与门禁射程订正**（2026-10-19 · R3-CIN-004/007/008）——`OutputCamera` 不再承诺「与过渡目标一致」，改为「可能为 null、不保证等于目标」（已随 2026-10-22 并入落定）。**R3-CIN-008 的门禁缺口由并入结构性关闭**：源码 / `.meta` / README 文件树 / XML 文档 / 公开 API 基线 / 示例编译自此都在核心 `check-docs -Enforce` 与测试射程内。
- 已做：**「测试锁定」声称改为指向真实用例**（2026-10-19 · R3-CIN-005）——强制 cut 与槽位 ABA 的两处声称原先没有对应用例；本轮补 `Activate_DuringBlend_…`（改前 `IsBlending` 恒 true）与 `DynamicHandle_AfterSlotReuse_OldHandleIsRejected`，文档改为指向它们。
- 已做：**完成回调措辞订正为「没有 await 形态」**（2026-10-19 · R3-CIN-006，文档订正）——`IsBlending` / `ActiveBlend` / `CameraActivated` 均可观测，原句「Brain 无公开完成回调」失实；见上方 `暂不做` 条已订正。

## 六、依赖与安装

- **Cinemachine 3.1.7 是核心 asmdef 的硬依赖**（与 Input System 同型）：`com.unity.cinemachine` 3.1.7 必须写进安装器清单、核心 `README.md` 的依赖表 / JSON 块与开发 `Packages/manifest.json` 三处并逐字一致，由 `DependencyDeclarationTests` 守卫。
- **安装顺序不再存在**：核心包与 Cinemachine 后端同属一个 UPM 包；按包 README 装好第三方依赖即可。
- **`package.json` 的 `dependencies` 仍留空**：UPM 不支持 Git URL 依赖，本仓所有依赖都走 manifest + 安装器；Cinemachine 是注册表包，但沿用同一形态。
- **开发工程**：`Packages/manifest.json` 加 `3.1.7`；`packages-lock.json` 同步（含 Splines / Mathematics 等上游依赖）。
- **示例**：`Samples~/Cinemachine` 不参与 Unity 编译；`.cs.meta` 手工维护（`Samples~` 被 Unity 完全忽略），由 `check-samples.ps1` 编译校验。

## 七、升级 Cinemachine 3.x 时重看哪里

1. `CinemachineVirtualCameraBase.Priority` / `Prioritize()` / `PrioritySettings.Enabled`：动态 boost 与作者状态恢复都依赖它们。
2. `CinemachineCore.GetBlendOverride` 的委托签名与 `CinemachineBlenderSettings.LookupBlend` 的调用顺序（`CustomBlends` 之后）：请求式过渡的核心依赖。
3. `CinemachineBrain.ActiveVirtualCamera` / `IsBlending` / `ActiveBlend` / `OutputCamera` / `UpdateMethods.ManualUpdate`：活动解析与测试直接使用。
4. `CinemachineCore.CameraUpdatedEvent`（以及后续状态事件使用的 `CameraActivatedEvent` / `CameraDeactivatedEvent` / `BlendFinishedEvent`）：静态 UnityEvent，订阅 / 退订方式与触发时机。
5. `CinemachineBlendDefinition.Styles`：`CameraBlendStyle` 的映射表。
6. 升级后先跑 `run-tests.ps1 -Filter Cinemachine -Platform All`；`DependencyDeclarationTests` 会因版本三处不一致而红，按安装器 / 核心 README / 开发 manifest 三处同步。

## 八、测试射程

- `CinemachineCameraManagerTests`（PlayMode，`Tests/Runtime/Camera/`）：统一句柄注册 / 重复注册冲突 / 激活与动态 boost / `GetBlendOverride` 不改 `DefaultBlend` / cut 与混合完成 / 注销恢复作者状态 / stale-default / 未注册活动相机告警 / Dispose / null id / 错误类型 / 非有限时长 / 外部销毁 vcam 的变更面；用 `CinemachineBrain.UpdateMethods.ManualUpdate` + `ManualUpdate(frame, delta)`，不依赖真实帧时长。每个 fixture 在 `SetUp` / `TearDown` 把 `CinemachineCore.GetBlendOverride` 置回 `null`。
- `CinemachineCameraEventTests`（PlayMode）：cut / blend 的消息顺序、未注册 incoming 不发布 Activated、其他 Brain 过滤、`Dispose` / `UnbindSessionEvents` 后不再发布；`CameraManagerTests` 覆盖会话复位调用退订与退订异常隔离。
- **原扩展包 `CinemachineDependencyTests` 已删除**，五项职责分别由核心守卫接管：版本三处 → `DependencyDeclarationTests`；五节 README → `ReadmeSectionTests`；示例路径 → `DependencyDeclarationTests.Samples_PathResolvesToAnExistingDirectory`；包内不指向包外 → `PackageBoundaryTests`。
- **公开 API 已入核心基线**：`CinemachineCameraManager` 及其公开成员由 `PublicApiBaselineTests` 锁定；命名 / 文件树 / 台账 / XML 文档 / 示例编译均随核心 `check-docs -Enforce` 一起检查。
- **门面中立守卫**：`ModuleBoundaryTests.CinemachineTypes_StayInsideTheCinemachineBackend` 扫核心 `Runtime/**/*.cs`（剥注释），`Camera/Cinemachine/` 之外出现 `Cinemachine` 即红。

## 九、台账

- 已做：**适配器对 `OutputCamera` null 语义与 `Unregister` 活动状态**（2026-10-18 · CAM-005；2026-10-19 R3-CAM-003 结案）——核心契约已窄化为「可能为 null、不保证等于目标」，适配器「直接转发 Brain 输出相机、`Unregister` 不改场景状态」即契约本身，不再存在需要对齐的差异。
