# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **兼容声明与门禁射程订正（R3-CIN-004/007/008）**：「兼容核心 0.3.0」失实——该 tag 早于 Camera 模块、不含
  `ICameraManager`；改为「核心包必须包含 Camera 模块」。`OutputCamera` 不再承诺「与过渡目标一致」，
  改为「可能为 null、不保证等于目标」。补记核心包门禁不覆盖本包的射程缺口。
- **`Activate` 在混合中途不能强制完成（R3-CIN-001）**：旧实现用短暂失活 / 激活 GameObject 试图重新入队，
  但同步 off/on 后 Brain 仍视同一目标为活动相机，原过渡按原时长继续。修法：改用上游文档明说的
  `CinemachineBrain.ActiveBlend = null`；补 PlayMode 用例（改前 `IsBlending` 恒 true）。
- **槽位复用 ABA 无用例（R3-CIN-005）**：补 `DynamicHandle_AfterSlotReuse_OldHandleIsRejected`，
  锁定「旧句柄按代际失配失效、不得操作到复用槽位的新相机」。

## [0.1.0] - 2026-10-17

### Added

- **`CinemachineCameraManager : ICameraManager`**（按 Cinemachine **3.1.7** 编写）：用 vcam 优先级做激活 /
  反激活，用 `CinemachineBrain` 的 `DefaultBlend` 做过渡；`OutputCamera` / `CameraIds` / `ActiveHandle` /
  `IsTransitioning` 查询；命名 `Register(string, vcam)` 与动态 `Register<T>(vcam)` 维护注册表。
- **动态 vcam 句柄**：`Register<T>` 返回核心包的 `CameraHandle`，按句柄激活 / 过渡 / 注销；动态 vcam 活动时
  `ActiveHandle.NamedId` / `CameraManager.ActiveCameraId` 为 `null`，用 `ActiveHandle` / `IsActive(handle)`。
- **测试**：`CinemachineCameraManagerTests`（PlayMode，`UpdateMethods.ManualUpdate` 确定性推进；含动态 vcam 与
  未注册活动相机告警）与 `CinemachineDependencyTests`（依赖三处 / 示例路径 / 包内不指向包外）。
- **示例**：`Samples~/Cinemachine` 演示 Brain + 两个 vcam 的接线与切换。
- 依赖 `com.unity.cinemachine` **3.1.7**；核心包 `com.venusy609.xframework` 需先安装（UPM 不支持 Git URL 依赖，
  由 README 的安装顺序兜底）。

### Changed

- **未注册活动相机告警**：Brain 的活动相机未在管理器注册（Timeline / 过场 / 外来 vcam）时，
  `Activate` / `TransitionTo`（非 cut）告警「可能与过场冲突」，避免静默与过场打架。
