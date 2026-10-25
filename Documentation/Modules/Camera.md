# Camera —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Camera/README.md`——已知限制、设计取舍、
> 接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Camera/
├── CameraManager.cs              # 公开：静态门面（生命周期 / 注册 / 栈 / 异步 / 绑定 / 查询 / 事件）
├── ICameraBackend.cs             # internal：后端契约、CameraBackendActivation、ICameraBackendSessionReset
├── CameraDirector.cs             # internal：id 生成 / 栈 / pending / 消息发布 / 快照组装 / 外部接管判定
├── CameraBlendStyle.cs           # 公开：Cut / EaseInOut / EaseIn / EaseOut / Linear / HardIn / HardOut
├── CameraTransitionResult.cs     # 公开：Completed / Superseded / CameraNotFound / ManagerDisposed
├── CameraInfo.cs                 # 公开：逐相机诊断项（id / 生成 / 栈 / 活动 / 过渡目标 / 存活）
├── CameraStateSnapshot.cs        # 公开：模块级诊断快照
├── Messages/
│   ├── CameraActivatedMessage.cs      # 公开：incoming 切换（外部 id 为 null）
│   ├── CameraDeactivatedMessage.cs    # 公开：相机不再 live
│   └── CameraBlendCompletedMessage.cs # 公开：blend 完成 / cut 零时长完成
├── Cinemachine/
│   ├── CinemachineCameraBackend.cs        # internal：唯一内置后端
│   ├── CinemachineCameraRegistration.cs   # 公开：预制体 / 动态相机自注册组件
│   ├── CinemachineImpulse.cs              # internal：震屏辅助（惰性 source / listener 探测）
│   └── README.md                          # 使用向：接线、行为、已知限制、设计取舍
└── README.md                              # 使用说明

Tests/Runtime/Camera/      # PlayMode：CameraManagerTests / CameraDirectorTests /
                           # CinemachineCameraBackendTests / CinemachineCameraRegistrationTests /
                           # CinemachineCameraEventTests / CinemachineCameraAsyncTests /
                           # CameraDiagnosticsTests / FakeCameraBackend
Tests/Editor/Architecture/ModuleBoundaryTests.cs  # Cinemachine 类型只允许在 Camera/Cinemachine/
Runtime/Log/LogCategories.cs                     # CameraManager 分类
```

**边界**：Cinemachine 已是核心 asmdef 的硬依赖（与 Input System 同型），类型只允许出现在 `Runtime/Camera/`
（相机模块可原生使用），其它模块仍不得引用；由 `ModuleBoundaryTests.CinemachineTypes_StayInsideTheCameraModule`
剥注释后扫描守。**gameplay 面仍只使用 string id / 中立类型**；CM 类型只在初始化（`Initialize`）、注册
（`Register`）与高级访问（`TryGetCamera` / `ActiveCamera`）出现。其它模块的代码与字符串字面量不得出现
`Cinemachine` 字样。

**模块无自动初始化**：没有 `[RuntimeInitializeOnLoadMethod]` 自初始化；只有会话复位钩子
（`SubsystemRegistration`），已登记进 `AutoInitTests` 族清单。

**相机测试全部在 PlayMode**：`Tests/Editor/Camera/` 已随 2026-10-23 重设计删除。理由与 `AutoInitTests` /
`ReadmeTableTests` 同一条——开发回路 `-Filter` 未指定平台时只跑 PlayMode，EditMode 守卫会被静默跳过。

## 决策台账

- 已做：**从零重设计公开面：string id + 相机栈 + 中立主面 + 原生逃生舱**（2026-10-23 · 用户裁定；`58a9b62` / `ebe10d7` / `849f199` / `6f99584` / `04661ea`）——删除 `CameraHandle` / `ICameraManager` / 旧消息与旧测试；门面改为 `SwitchTo` / `Push` / `Pop` / `PopAll` / `Clear` + 异步 + 绑定 + 诊断；动态 id 为 `@auto/N`；`TryGetCamera<T>` 取回后端相机。证据：`CameraManagerTests` / `CameraDirectorTests` / `CinemachineCameraBackendTests` / `CinemachineCameraRegistrationTests` / `CinemachineCameraEventTests` / `CinemachineCameraAsyncTests` / `CameraDiagnosticsTests`。重开判据：无（用户明确要求完全抛弃现有方案）。
- 已推翻：**~~统一句柄~~**（2026-10-22 · `4e34616`）——~~命名 / 动态统一 `CameraHandle`，接口只收句柄，字符串只是门面糖~~。被 2026-10-23「string id 唯一身份 + 生成 `@auto/N`」取代：双轨让每个入口都要选；动态 id 已能按实例唯一，句柄的代际语义只在槽位复用时有用，而注销 / 重注册窗口由单 pending 覆盖。
- 已推翻：**~~不做相机栈 / push-pop / 返回上一相机~~**（2026-10-17 · 立项裁定）——~~生命周期树，与 Update 的「组」API 同一条推理链~~。被 2026-10-23「相机栈成为核心」取代：开镜 / 对话 / 演出需要临时覆盖并返回；栈只持有 id，不是实体生命周期树。
- 已推翻：**~~不做跟随 / 瞄准 / 震屏 / 缩放~~**（2026-10-17 · 立项裁定）——~~Cinemachine 的行为编排；框架内建只会与它竞争~~。被 2026-10-23「薄绑定」取代：`SetFollow` / `SetLookAt` / `SetFieldOfView` / `Shake` 只转发到后端，不重写行为。
- 已推翻：**~~事件只对已注册相机发布~~**（2026-10-22）——~~未注册 incoming 不发布激活消息~~。被 2026-10-23「外部 id 为 null」取代：HUD / 音频 / 诊断需要知道外部接管；订阅方按 id 过滤。
- 已推翻：**~~不做 string id 的动态注册~~**（2026-10-17 · 立项裁定）——~~动态实例没有稳定 authoring id~~。被 2026-10-23「`@auto/N` + `CinemachineCameraRegistration`」取代：生成 id 按实例唯一，组件负责 OnEnable / OnDestroy 生命周期。
- 已推翻：**~~后端中立契约 + 内部导演 + 公开逃生口~~**（2026-10-23 · `58a9b62`）——~~`ICameraBackend` 公开、`TryGetCamera<T>` 泛型、门面不出现 Cinemachine 类型~~。被 2026-10-23「组合根 / 高级访问原生化 + 后端 internal」取代：公开可替换后端无真实受众；gameplay 面仍保持 id / 中立类型，CM 类型只出现在初始化 / 注册 / 高级访问。
- 已做：**组合根 / 高级访问原生化 + 后端 internal + gameplay 保持 id**（2026-10-23 · `e6d56db`）——`Initialize(CinemachineBrain)`；`Register` / `TryGetCamera` / `ActiveCamera` 使用 `CinemachineVirtualCameraBase`；`ICameraBackend` / `CameraBackendActivation` / `ICameraBackendSessionReset` / `CinemachineCameraBackend` 收为 internal；`CameraBlendStyle`、消息、`CameraInfo` / `CameraStateSnapshot` 保持 id；`ModuleBoundaryTests` 射程放宽为 `Runtime/Camera/`。证据：`CameraManagerTests` / `CameraDirectorTests` / `CinemachineCamera*Tests`；公开 API 基线 -65 / +5。
- 已做：**请求结算事件 `RequestSettled`**（2026-10-23 · `849f199`）——每个请求恰好一次；异步等待不依赖轮询，关闭「目标激活前销毁」「已是活动相机的 cut」导致的 await 悬挂。
- 已做：**预制体自注册组件**（2026-10-23 · `ebe10d7`）——`CinemachineCameraRegistration`：OnEnable 注册 / Start 重试 / OnDestroy 注销 / 池化幂等 / 未初始化告警后显式 `Register()`。
- 已做：**一步注册并激活（裸 vcam 与自注册组件两条入口）**（2026-10-24 · `7068534`）——`CameraManager.RegisterAndSwitchTo` / `RegisterAndPush` 为裸 vcam 生成 `@auto/N`、注册并立即切基座 / 压栈，返回 id 供 `Unregister`；`CinemachineCameraRegistration.SwitchTo` / `Push` 复用组件的 `@auto/N`，并在管理器晚于 OnEnable 初始化时补注册。门面合并方法不静默复用已注册相机，按 `Register` 契约抛 `ArgumentException`。证据：`CameraDirectorTests` / `CameraManagerTests` / `CinemachineCameraRegistrationTests`。
- 不做：**门面 `SwitchTo(CinemachineVirtualCameraBase)` / `Push(CinemachineVirtualCameraBase)` 重载**（2026-10-24）——会让 `SwitchTo(null)` 在 string / vcam 重载间二义，并把注册副作用藏进播放动词；改用显式 `RegisterAndSwitchTo` / `RegisterAndPush`。无重开判据。
- 不做：**自动添加 `CinemachineImpulseListener`**（2026-10-23 · `6f99584`）——运行时静默改场景会掩盖配置问题；缺失只告警一次。重开判据：真实项目反馈震屏静默失效且无法通过文档解决。
- 暂不做：**`ShakeAt` / 缩放过渡 / ClearShot 子相机内省**（2026-10-23）——当前无真实使用方；重开判据：出现需要指定世界坐标震源、FOV tween 或按子相机查询的真实项目。
- 不做：**接管 Timeline / 多 Brain 分屏**（2026-10-23）——Timeline 用 `SetCameraOverride` 压过优先级；多输出是独立路由模型。无重开判据。
- 不做：**Timeline / 过场相机进本模块**（2026-10-17 · 用户场景裁定）——过场由 Timeline 的 Cinemachine Track 接管 Brain；本模块的 boost 不挡 Timeline override；`ActiveCameraId` 对未注册过场 vcam 为 `null`。无重开判据。
- 不做：**UI 相机绑定（ScreenSpaceCamera / WorldSpace 画布）**（2026-10-17 · 立项裁定）——UI 根强制 `ScreenSpaceOverlay`；相机类型 / RenderTexture / 是否挡输入是项目结构。无重开判据。
- 暂不做：**独立 Camera 诊断页签**（2026-10-17 · v1.1 候选）——概览页的 Camera 行已读 `GetState()`；没有「排查相机状态需要独立页签」的真实使用方。重开判据：出现该需求。
- 暂不做：**`IBootstrapStage` / `DefaultGameLauncher` 相机字段**（2026-10-17 · v1.1 候选）——后端由组合根注入；`DefaultGameLauncher` 没有可序列化的相机注册表。重开判据：出现「启动引导必须负责相机初始化」的真实使用方。
- 已做：**Cinemachine 3.1.7 后端硬合并进核心包**（2026-10-22 · 用户裁定）——扩展包 / 独立 asmdef 删除；核心 asmdef 硬引用 `Unity.Cinemachine`；类型在 2026-10-23 重命名为 `CinemachineCameraBackend`，位置与门禁不变。证据：`Documentation/Cinemachine.md`、`DependencyDeclarationTests`、`ModuleBoundaryTests`。
- 已做：**2026-10-24 重新审计与 P1/P2 修复**（2026-10-24）——修：未初始化提示指向 internal API、作者状态快照时点、`SetLookAt(null)` 语义、强制 cut 回归、死栈顶 Pop、注销截断混合、同通道 boost 过滤、震屏早建 source；补 `HardIn` / `HardOut`（公开 API 源兼容 +2）。证据：`CinemachineCameraBackendTests` / `CameraDirectorTests` / `CinemachineCameraAsyncTests` / `CameraDiagnosticsTests`；清单见「审计记忆」与 CHANGELOG。
- 不做：**Unreal 式 Camera Modifier 栈（FOV / 后期 / 跟随的临时叠加与自动恢复）**（2026-10-24）——本模块只做薄绑定；行为叠加走 Cinemachine 组件。无重开判据。
- 暂不做：**自定义 `AnimationCurve` 混合 / 震屏通道·位置·多源 / `SetFieldOfView` 补间 / `TopCameraId` / `Clear(resetBrainState)` / 消息带 Brain token / `GetCustomBlender` 接管**（2026-10-24）——当前无真实使用方。重开判据：出现对应需求。
- 暂不做：**timeScale 策略（`CutWhenPaused` / `Unscaled` / 看门狗超时）**（2026-10-24）——先只写文档；需要新的 options / 重载，且 `Unscaled` 要临时改 `Brain.IgnoreTimeScale`（全局状态保存/恢复）。重开判据：真实项目在暂停菜单里 `await` 相机过渡。
- 待裁：**`Rebind(CinemachineBrain)` / `Initialize(brain, replaceExisting)`（跨场景 Brain 重绑）、多 Brain 检测告警、`GetBlendOverride` 被他人覆盖的检测、主线程断言第四份、`IsInitialized` 随 Brain 销毁降级（`HasOutputDriver`）、boost 期间作者改优先级的「比较-恢复」**（2026-10-24）。
- 已做：**同一目标中途 cut 的事件例外文档化**（2026-10-25 · `abb7055c`，R4-CAM-001，本轮文档化）——A→B 混合途中再 cut 到同一 B 时 CM 不发 `Activated`；README/本文件写明明细，`CinemachineCameraEventTests.SameTargetCut_DuringBlend_*` 钉住现状。不补发事件（属行为变更，留待单独裁定）。
- 已做：**外部 override 下 `SwitchTo` 接管 boost 归属**（2026-10-25 · `c70dcc17`，R4-CAM-002，本轮修复）——早退分支先 `RestorePreviousBoosted + Boost` 再结算；`CameraDirector` 的栈顶 no-op 不变。回归 `Activate_WhileExternalOverrideMakesTargetActive_TakesBoostOwnership`（改前 A 仍为 boost 值）。
- 已做：**已销毁注册相机事件 id 稳定**（2026-10-25 · `d78031b9`，R4-CAM-005，本轮修复）——`FindSlotByCamera` / `ResolveCameraId` 改用 `ReferenceEquals`，事件不再退化为 `null`（外部相机）；`IsRegistered` / `CameraCount` 的死槽计数语义不变。回归 `DestroyedRegisteredActiveCamera_DeactivatedCarriesItsId`（改前 `deactivated:null`）。

## 沿革与已否决形状

### 2026-10-17：立项「统一入口」

用户场景是「用 Cinemachine，但要统一入口」。一手核对 Cinemachine 3.1.7 源码确认激活 / 反激活 / 过渡由它实现，
框架只补统一入口；当时核心包零 Cinemachine 依赖，Cinemachine 实现放独立扩展包，并新增 `CameraHandle` 双键。

### 2026-10-22：Cinemachine 唯一后端 + 统一句柄

删除 `UnityCameraManager`，扩展包硬合并进核心；接口只收 `CameraHandle`，后端用槽位表 + 动态 boost + 作者状态恢复 +
`GetBlendOverride`；随后追加状态事件、`TransitionToAsync`、诊断快照。**该形状在 2026-10-23 被整体取代**：
`CameraHandle` / `ICameraManager` / `CinemachineCameraManager` / 旧消息全部删除，槽位表换成 id 字典 + 栈。

### 2026-10-23：从零重设计（用户裁定）

用户认为 `CameraManager` 的对外 API 仍不满意，要求**完全抛弃现有方案、按游戏开发相机需求从零设计**，并给出边界：
主 API 中立、允许通过显式逃生舱访问 Cinemachine。第三方对照（Cinemachine 3.1.7 / GameFramework / QFramework / ET /
Chop Chop / CineMachine-Manager / Nuclex）的结论：**没有一家提供相机栈 / 异步等待 / 集中式目标与震屏绑定 /
显式注册生命周期**；共同底线是「一台活动相机 + 优先级或 `Camera.main`」，id 常靠名字解析。于是新形状：

- 唯一身份 `string id`；动态 id `@auto/N`；`CinemachineCameraRegistration` 自注册。
- 游戏语言：`SwitchTo` / `Push` / `Pop` / `PopAll` / `Clear`；四个 async；`SetFollow` / `SetLookAt` /
  `SetFieldOfView` / `Shake`。
- 中立门面 + 公开 `ICameraBackend` 原语 + 内部 `CameraDirector`（栈 / pending / 消息 / 快照）。
- `RequestSettled` 单一结算源；外部 incoming 的 id 为 `null`；外部接管保持 boost。
- 删除 `CameraHandle` / `ICameraManager` / 旧消息 / `CameraFacadeCompletenessTests` / EditMode 相机测试。

### 2026-10-23（第二轮）：组合根 / 高级访问原生化

第一轮落地后继续讨论「公开可替换后端是否有意义」与「cameraId 是否还需要」：

- 公开可替换后端无真实受众（CM 硬依赖、仓外零实现）；`ICameraBackend` / `CameraBackendActivation` /
  `ICameraBackendSessionReset` / `CinemachineCameraBackend` 收为 internal，保留 `CameraDirector` + `FakeCameraBackend`
  作测试缝。
- `cameraId` 保留为 **gameplay 面的身份**：切 / 压 / 弹 / 跟随 / 震屏 / 查询 / 订阅只认 id 与中立类型，
  不必在满代码里 `using Unity.Cinemachine`。CM 类型只出现在 `Initialize(CinemachineBrain)`、`Register(..., vcam)`、
  `TryGetCamera(..., out vcam)`、`ActiveCamera`。
- 为保持 gameplay 面 CM-free，`SwitchTo` 等继续收 `CameraBlendStyle`，消息 / 快照继续只带 id；没有把
  `CinemachineBlendDefinition` 带进常用动词。
- `ModuleBoundaryTests` 射程从 `Runtime/Camera/Cinemachine/` 放宽为 `Runtime/Camera/`；其它模块仍不得引用 CM。

### 已否决形状

- **核心包直接引用 Cinemachine（2026-10-17 评估中一度考虑）**：当时以「锁主版本」为由改为扩展包；2026-10-22 硬合并。
- **只给示例适配器、不进包的 Cinemachine 实现**（2026-10-17 早期计划）：用户明确要求「Cinemachine 直接实现」。
- **可选能力接口 `ICameraRegistry`**（2026-10-17）：用户选择**主接口直扩**；2026-10-23 该主接口本身被 `ICameraBackend` 取代。
- **用 string id 注册动态相机**（2026-10-17 否决）：2026-10-23 **推翻**——生成 id 解决「没有稳定 authoring id」。
- **让 CameraManager 接管 Timeline 过场相机**（2026-10-17）：过场系统已经接管 Brain；现仍不做。
- **相机栈 / 跟随策略**（2026-10-17 否决）：2026-10-23 **推翻**——栈与薄绑定成为核心；仍不做实体生命周期树。

## 迁移指南

2026-10-23 重设计的编译器可见变更以 `Assets/XFramework/CHANGELOG.md` 的「破坏性变更迁移表」为准。要点：

- `ICameraManager` → `CameraManager.Initialize(CinemachineBrain)` + `Register(..., CinemachineVirtualCameraBase)`；
  `ICameraBackend` 不再公开（internal 实现细节）。
- `CameraHandle` 删除 → 所有 gameplay 入口改用 `string` id；`Register(CinemachineVirtualCameraBase)` 返回 `@auto/N`。
- `CinemachineCameraManager` → internal `CinemachineCameraBackend`（无公开类型）。
- `Activate` / `TransitionTo` → `SwitchTo`；`Deactivate` → `Pop` / `Clear`；`TransitionToAsync` → `SwitchToAsync`。
- `TryGetHandle` / `TryGetCamera<T>` → `TryGetCamera(..., out CinemachineVirtualCameraBase)`；新增 `ActiveCamera`；
  `IsActive` 删除（用 `ActiveCameraId` / `IsExternallyControlled`）。
- 消息载荷 `CameraHandle` → `CameraId`（外部为 `null`）；`CameraStateSnapshot` / `CameraInfo` 字段重排后保持 id。
- `TargetUnregistered` → `CameraNotFound`。
- 行为变化：同步命令 supersede pending；外部接管事件 id 为 null；已注册但被外部销毁的相机事件 id 仍解析为自己的 id（R4-CAM-005）；外部接管保持 boost；计数含已销毁未注销槽位。

## 审计记忆

- **`GetBlendOverride` 是全局单委托**：后端构造时保存前值，Dispose 仅当当前值仍是自己时恢复；一个进程建议一个后端。
- **静态事件必须退订**：四个 `CinemachineCore` 事件跨场景 / 域重载存活；Dispose 与会话复位都要 RemoveListener。
- **`ActiveVirtualCamera` 在 Brain 首次更新前为 null**：`ActiveCameraId` 可能晚一次更新；cut 完成只能靠激活事件。
- **`BlendFinishedEvent` 不对 cut 触发**：cut 的 `BlendCompleted` 必须在激活事件补发。
- **`CameraDeactivatedEvent` 在混合结束后才触发**；事件顺序测试锁死。
- **Timeline 用 `SetCameraOverride` 压过优先级**：boost 不阻挡过场；外部接管保持 boost 才能让过场结束后回到游戏相机。
- **`ControlledObject` 才是输出对象**：`OutputCamera` 可能为 null；震屏 source 挂在 `ControlledObject`。
- **ClearShot 的 `ParentCamera` 缓存**：子相机 `OnEnable` 时缓存父相机；测试 / 运行时创建必须**先建父、再建子**，
  否则 `ParentCamera` 可能为 null。
- **`PrioritySettings.Enabled == false` 时 `Value` 为 0**：boost / 恢复必须整体赋值 `PrioritySettings`。
- **CM 类型只允许在 `Runtime/Camera/`**：守卫 `CinemachineTypes_StayInsideTheCameraModule` 剥注释后扫描；其它模块
  不得出现。模块内 `CameraManager.cs` / `CameraDirector.cs` / `ICameraBackend.cs` 可用 CM；`CameraInfo` / 消息 /
  `CameraBlendStyle` 仍保持中立。
- **公开面的 CM 类型只允许在 4 个入口**：`Initialize(CinemachineBrain)`、`Register(..., vcam)`、
  `TryGetCamera(..., out vcam)`、`ActiveCamera`。gameplay 动词 / 消息 / 快照只用 id + 中立类型；新增成员时先判断
  它属于哪一面，别把 `CinemachineBlendDefinition` 之类带进常用动词。
- **`@auto/` 是保留前缀**：显式 id 以它开头直接抛；生成 id 不透明、不持久化。
- **`CameraStateSnapshot` 不叫 `CameraState`**：避免与 `Unity.Cinemachine.CameraState` 撞名。
- **没有 ticker**：模块不在 `UpdateManager` 注册任何节点；过渡由 Brain 推进。
- **作者状态快照在首次 boost 前抓**：`Register` 时抓会在作者注册后改状态时恢复旧值；`Boost` 内按
  `PriorityOwned` / `GameObjectEnabledByManager` / `ComponentEnabledByManager` 守卫，二次 boost 不得重抓。
- **`SetLookAt(null)` 不能走 CM setter**：setter 会置 `CustomLookAtTarget = true` 并留下空目标，相机从此
  不回退 `TrackingTarget`；必须直接写 `CinemachineCamera.Target` / 分组 `DefaultTarget.Target`。
  `SetFollow(null)` 对分组相机同理（setter 会置 `DefaultTarget.Enabled = true`）。
- **`ActiveBlend = null` 的归属**：cut 与本管理器放弃请求目标时才用；`Unregister` 只有目标是当前请求时
  才强制完成混合，否则会截断别人的过渡。
- **死栈顶的 `Pop` 语义**：剪除死栈顶后视为该层已经离开，只切到新栈顶；不要继续弹存活层（与
  `OnBackendDeactivated` 的回退对齐）。
- **`CinemachineBrain` 的时间**：默认 `Time.deltaTime`，`timeScale = 0` 时混合停摆、异步等待不完成；
  `IgnoreTimeScale` / `UniformDeltaTimeOverride` 是绕法。震屏的 `CinemachineImpulseManager.IgnoreTimeScale`
  是独立开关。
- **`ChannelMask` 默认全通**：异通道相机可注册但不会被本 Brain 选中，也不计入 boost 的最大值。
- **`ActiveVirtualCamera` 优先 `CinemachineCore.SoloCamera`**：Solo 期间 `ActiveCameraId` 与
  `OutputCamera` / `IsBlending` 会分叉。
- **`BaseCameraId` 与 `CameraInfo.IsBase` 语义不同**：前者是第一个存活栈项，后者是栈底下标 0（可能是死槽）。
- **`RequestSettled` 在 `Dispose` / 会话复位不通知**：导演层先自行结算 pending，避免二次结算。

## 实测记录

- **2026-10-23 事件帧序**（`CinemachineCameraEventTests`，`ManualUpdate`）：cut = `Deactivated → Activated(IsCut=true) → BlendCompleted`；
  blend = 开始帧 `Activated`、结束帧 `Deactivated → BlendCompleted`；外部 incoming 的 id 为 `null`，旧注册相机仍发 `Deactivated`。
  **例外（R4-CAM-001）**：A→B 混合途中再 cut 到同一目标 B 时 Cinemachine 不发 `CameraActivated`，只有
  `Deactivated(A) → BlendCompleted(B)`；`CinemachineCameraEventTests.SameTargetCut_DuringBlend_PublishesDeactivatedAndCompletedWithoutActivated`
  钉住现状。
- **2026-10-23 异步**（`CinemachineCameraAsyncTests`）：blend / cut 完成、同请求 join、同步命令 / 外部接管 supersede、
  注销 / 激活前销毁 → `CameraNotFound`、Destroy / 会话复位 → `ManagerDisposed`、取消只停等待、预取消不启动。
- **2026-10-23 预制体注册**（`CinemachineCameraRegistrationTests`）：OnEnable 注册；两个实例 id 不同；OnDestroy 后栈回退；
  池化禁用不注销、重新启用幂等；未初始化 Start 告警后显式 `Register()` 可恢复；ClearShot 注册父相机。
- **2026-10-23 ClearShot 解析**（`CinemachineCameraBackendTests.ActiveCameraId_ResolvesClearShotChildToRegisteredParent`）：
  用 `SetCameraOverride` 把子相机设为活动，`ActiveCameraId` 沿 `ParentCamera` 解析到已注册父 id。
- **2026-10-23 外部接管 boost**（`ExternalTakeover_KeepsManagerBoost_AndResumesAfterRelease`）：外部高优先级接管时
  栈顶 boost 保持；释放后回到栈顶。
- **2026-10-23 绑定**：`SetFieldOfView` 写 Lens 并经 Brain 输出到 Unity Camera；`SetFollow` / `SetLookAt` 往返；
  `Shake` 在 `ControlledObject` 建 source、设 duration、缺 Listener 只告警一次。
- **2026-10-24 审计回归**（PlayMode，均先跑红再加修法）：
  `SwitchAway_RestoresPriorityChangedAfterRegister` / `..._RestoresGameObjectDisabledAfterRegister` /
  `..._RestoresComponentDisabledAfterRegister`（改前三红）；`SetLookAt_Null_FallsBackToFollowTarget` /
  `SetLookAt_Null_OnGroup_FallsBackToDefaultTrackingTarget` / `SetFollow_Null_OnGroup_DoesNotEnableDefaultTarget`
  （改前三红）；`Activate_DuringBlendToSameTarget_ForcesCutImmediately`（负向注入注释掉 `ActiveBlend = null` 即红）；
  `Pop_DeadTop_DoesNotPopNextLiveLayer` / `PopAsync_DeadTop_DoesNotPopNextLiveLayer`（改前双红）；
  `Unregister_UnrelatedCamera_DoesNotCompletePushEarly`（改前红）；
  `Activate_IgnoresRegisteredCamerasOnOtherChannels`（改前 101、改后 1）；
  `Shake_WithoutActiveCamera_DoesNotCreateSource`（改前红）。
- **2026-10-24 第三方对照**（Cinemachine 3.1.7 源码 + 官方文档 + GitHub 样本）：Cinemachine 无 gameplay 相机栈
  （`ICameraOverrideStack` 面向 Timeline）；UE classic 是单 ViewTarget + CameraModifier，UE 5.5+ Gameplay Camera
  System 的实验性 Blend Stack 才有 push/自动 pop；URP 是渲染栈；Godot 每 viewport 单 current；社区未发现成熟的
  「Cinemachine 相机栈管理器」（`RolandKaechele/CameraManager` 有 Set/Push/Pop 但驱动 Unity Camera profile、
  不等待混合）。来源：Unity Cinemachine 3.1 manual（Brain / Camera control / Manager cameras / Blends /
  Multiple cameras / Impulse / Timeline）、UE `APlayerCameraManager` / `CameraModifier` / Gameplay Camera System、
  Godot `Camera2D/3D`、GitHub `RolandKaechele/CameraManager` / `TD-but/CinemachineManager` /
  `DevJamaly/CineMachine-Manager` / `ewersp/CameraShake`。
- **仍未实测 / 需真实环境**：真实 `PlayableDirector` 的 Timeline 接管与释放；关闭域重载连跑两遍（手工验收）；
  IL2CPP 构建；Cinemachine 2.x（不支持）；多 Brain / 分屏（明确不做）。
