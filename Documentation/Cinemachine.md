# Cinemachine 扩展包 —— 维护向记录

> **收件人**：框架作者（升级 Cinemachine / 审计扩展包时）。**不是**使用文档。
> 使用说明在 `Assets/XFramework.Cinemachine/README.md` 与它的 `Runtime/README.md`；本文件不重复。
> **本文件不随包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 本文回答：**扩展包为什么是独立包、钉哪个版本、3.x 的哪些 API 是实测过的、升级时重看哪里**。

## 一、结论与边界

- **独立可选扩展包**：`com.venusy609.xframework.cinemachine`（`Assets/XFramework.Cinemachine/`），版本 `0.1.0`，单独 asmdef，不随核心包发布。
- **核心包零 Cinemachine 依赖**：由 `ModuleBoundaryTests.CoreRuntime_DoesNotReferenceCinemachine` 守卫。
- **只支持 Cinemachine 3.x**（按 **3.1.7** 编写）：2.x 的类型名、字段名与命名空间都不同；2.x 项目需要自己的 `ICameraManager` 实现。
- **依赖真值**：扩展 `package.json` 的 `"com.unity.cinemachine": "3.1.7"` = 扩展 README 的同一条 = 开发工程 `Packages/manifest.json` 的同一条；由 `CinemachineDependencyTests` 守卫。**核心包的 README / 安装器不含 Cinemachine**；开发 manifest 里的 3.1.7 是扩展包编译 / 测试用的（核心 `DependencyDeclarationTests` 只核核心清单的三落点，manifest 的额外项不被当作核心依赖）。
- **核心包依赖不写进扩展 `package.json`**：UPM 不支持 Git URL 依赖；由扩展 README 的安装顺序 + asmdef 引用兜底。扩展 README 声明兼容的核心版本，`CinemachineDependencyTests` 与核心 `package.json` 比对。

## 二、文件结构

```
Assets/XFramework.Cinemachine/
├── package.json                     # com.venusy609.xframework.cinemachine 0.1.0；依赖 Cinemachine 3.1.7；samples
├── README.md                        # 包级：安装顺序 / Git URL / file: / 示例 / 已知限制
├── CHANGELOG.md                     # 0.1.0
├── Runtime/
│   ├── CinemachineCameraManager.cs  # public sealed : ICameraManager, IDisposable
│   ├── Venusy609.Xframework.Cinemachine.asmdef
│   └── README.md                    # 18 节骨架；5 项必答；依赖 / 已知限制 / 设计取舍
├── Tests/
│   ├── Runtime/CinemachineCameraManagerTests.cs   # PlayMode 9 例（ManualUpdate 确定性）
│   └── Editor/CinemachineDependencyTests.cs       # 依赖三处 / 核心版本 / 五节必答 / 示例路径 / 不指向包外
└── Samples~/Cinemachine/            # CameraSample.cs + .sample.json（不参与编译，手工 .cs.meta）
```

## 三、Cinemachine 3.1.7 实测事实（一手读包内源码 + 运行时测试）

| 事实 | 结论 |
|---|---|
| 命名空间 | **`Unity.Cinemachine`**（2.x 是 `Cinemachine`） |
| 程序集 | `Unity.Cinemachine`（asmdef 引用名） |
| vcam 类型 | `CinemachineCamera : CinemachineVirtualCameraBase`（2.x 是 `CinemachineVirtualCamera`） |
| `Priority` | `CinemachineVirtualCameraBase.Priority` 是 `PrioritySettings`（`Enabled` + `Value`，隐式 int 赋值会置 `Enabled = true`），**不在 `ICinemachineCamera` 上** |
| 队列刷新 | 改 `Priority` 后必须调用 `CinemachineVirtualCameraBase.Prioritize()`；否则要等该 vcam 下一帧 `Update`（它比较 `Priority.Value != m_QueuePriority`）。`MoveToTopOfPrioritySubqueue()` 是过时别名 |
| 混合配置 | `CinemachineBrain.DefaultBlend`（原 `m_DefaultBlend`）与 `CustomBlends`（原 `m_CustomBlends`）；样式枚举 `CinemachineBlendDefinition.Styles`（原 `Style`） |
| 状态查询 | `CinemachineBrain.ActiveVirtualCamera` / `IsBlending` / `ActiveBlend` / `OutputCamera` |
| 手动驱动 | `CinemachineBrain.UpdateMethod`（类型 `CinemachineBrain.UpdateMethods`，**嵌套枚举**）+ `ManualUpdate(int currentFrame, float deltaTime)`；测试用它做确定性推进 |
| 空 pipeline | `CinemachineCamera` 不要求 body / aim 组件；空 pipeline 是合法的被动相机（按 GameObject transform 驱动） |

## 四、决策台账

- 已做：**Cinemachine 3.1.7 一等实现以独立可选扩展包提供**（2026-10-17）——`CinemachineCameraManager` 用优先级做激活 / 反激活、`DefaultBlend` 做过渡；核心包保持零依赖。理由：2.x / 3.x API 不兼容，核心包一旦引用就必须锁一个主版本；采用率没有公开统计，不能假设所有消费者都用 Cinemachine。
- 已做：**`Activate` 在「正在混合且目标已是活动相机」时短暂失活 / 激活目标 GameObject**（2026-10-17 · 测试锁定）——Brain 的公开面没有「强制完成 / 重开混合」的入口；副作用是该 vcam 的 `PreviousStateIsValid` 重置，写进使用方 README。
- 已做：**改优先级后立即 `Prioritize()`**（2026-10-17 · `CinemachineCameraManagerTests` 改前必红：不调用时 `TransitionTo_BlendsAndCompletes` / `TransitionTo_ZeroDuration_Cuts` 失败，Brain 仍停在旧活动相机）。
- 不做：**核心包硬依赖 Cinemachine**（2026-10-17）——版本锁会排除 2.x / 2D / 自定义后端项目。无重开判据。
- 不做：**在扩展包里清空 `brain.CustomBlends`**（2026-10-17）——项目配置优先；清空会悄悄改写项目行为。无重开判据。
- 不做：**支持 Cinemachine 2.x**（2026-10-17）——2.x 与 3.x 的类型名、命名空间、混合字段全不同，一份代码无法同时编译；2.x 项目写自己的 `ICameraManager`。无重开判据。
- 暂不做：**`TransitionToAsync` / 过渡完成回调**（2026-10-17 · v1.1 候选）——Brain 的公开面没有完成回调，需要轮询 `IsBlending`；核心接口当前用 `IsTransitioning` 覆盖。重开判据：真实使用方需要 await 且轮询不够。

## 五、依赖与安装

- **顺序**：先核心包，后扩展包（扩展 asmdef 引用核心程序集；`package.json` 不能声明 Git URL 依赖）。
- **Git URL**：两个 URL 用同一个 `?path=`；同一仓库、同一 tag；`?path=` 在 `#` 之前。
- **本地**：`file:` 指向 `Submodules/XFramework/Assets/XFramework` 与 `.../Assets/XFramework.Cinemachine`。
- **Cinemachine 3.1.7**：扩展 `package.json` 声明，UPM 自动解析；若工程已装 3.1.x 的其他补丁版，以 manifest 为准（扩展按 3.1.7 API 编写）。
- **开发工程**：`Packages/manifest.json` 加 `3.1.7`；`packages-lock.json` 同步（含 Splines / Mathematics 等上游依赖）。
- **示例**：`Samples~/Cinemachine` 不参与编译；`.cs.meta` 手工维护（`Samples~` 被 Unity 完全忽略，测试壳不会回写）。

## 六、升级 Cinemachine 3.x 时重看哪里

1. `CinemachineVirtualCameraBase.Priority` / `Prioritize()`：优先级队列的刷新方式是本实现的核心依赖。
2. `CinemachineBrain.DefaultBlend` / `ActiveVirtualCamera` / `IsBlending` / `OutputCamera` / `UpdateMethods.ManualUpdate`：管理器与测试都直接用它们。
3. `CinemachineBlendDefinition.Styles`：`CameraBlendStyle` 的映射表。
4. 升级后先跑 `run-tests.ps1 -Filter Cinemachine -Platform All`；`CinemachineDependencyTests` 会因版本三处不一致而红，按扩展 `package.json` / 扩展 README / 开发 manifest 三处同步。

## 七、测试射程

- `CinemachineCameraManagerTests`（PlayMode）：激活 / 反激活 / 未知 id / cut / 过渡完成 / 注册表 / Dispose / null id；用 `CinemachineBrain.UpdateMethods.ManualUpdate` + `ManualUpdate(frame, delta)`，不依赖真实帧时长。
- `CinemachineDependencyTests`（EditMode）：Cinemachine 版本三处一致、核心版本一致、`Runtime/README.md` 五节必答、示例路径、扩展包内 `.cs` / `.md` 不指向包外的维护向文档。
- **不在射程**：扩展包的公开 API 未纳入核心的 `PublicApiBaselineTests`（基线只扫核心程序集）；扩展包的命名 / 文件树 / 台账不受核心 `check-docs` 覆盖。改动扩展包时按核心的同名约定人工核对。
