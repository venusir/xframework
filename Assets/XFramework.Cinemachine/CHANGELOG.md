# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-10-17

### Added

- **`CinemachineCameraManager : ICameraManager`**（按 Cinemachine **3.1.7** 编写）：用 vcam 优先级做激活 /
  反激活，用 `CinemachineBrain` 的 `DefaultBlend` 做过渡；`OutputCamera` / `CameraIds` / `ActiveCameraId` /
  `IsTransitioning` 查询；`Register` / `Unregister` 维护注册表。
- **测试**：`CinemachineCameraManagerTests`（PlayMode，`UpdateMethods.ManualUpdate` 确定性推进）与
  `CinemachineDependencyTests`（依赖三处 / 示例路径 / 包内不指向包外）。
- **示例**：`Samples~/Cinemachine` 演示 Brain + 两个 vcam 的接线与切换。
- 依赖 `com.unity.cinemachine` **3.1.7**；核心包 `com.venusy609.xframework` 需先安装（UPM 不支持 Git URL 依赖，
  由 README 的安装顺序兜底）。
