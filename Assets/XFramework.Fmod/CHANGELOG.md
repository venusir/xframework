# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-07

### Added

- **Phase 2 真实集成**：条件集成程序集 `Venusy609.Xframework.Fmod.Unity`
  （`Runtime/Integration/`，`versionDefines` = `com.fmod.fmod-for-unity.v204` / `2.4.0`，
  `defineConstraints` = `XFRAMEWORK_FMOD`）、真实 `FmodEventBackend`（事件/参数/bank/bus/snapshot/3D/
  Poll）、`FmodInstaller`（FMOD-free 自动安装逻辑）与 `FmodAutoInstall`（`BeforeSceneLoad` 入口，
  `XFW_DISABLE_FMOD_AUTOINSTALL` 逃生口）。
- **FMOD-free provider 层**：`FmodAudioSettings`（public 配置资产，`Enabled` / `EventPathPrefix` /
  `Banks` / `LoadBankSamples` / `OwnsBanks` / `MaxVoices` / `MasterVolume` / `MasterMuted`）、
  `FmodProviderOptions`、`IFmodEventBackend`（internal 缝）、`FmodEventSlotTable`（index + generation）
  与 `FmodAudioProvider`（`IAudioProvider` 的播放原语）。
- **provider 能力接口**：实现 core internal 的 `IAudioParameterProvider` / `IAudioBankProvider` /
  `IAudioBusProvider` / `IAudioSnapshotProvider` / `IAudioSpatialProvider`。`AudioManager` 的逐次参数、
  全局参数、bank、bus、snapshot、3D 位置控制面可经 fake backend 端到端验证。
- **Phase 2 测试/守卫**：`FmodInstallerTests`、条件测试程序集
  `Venusy609.Xframework.Fmod.Unity.Tests`、Integration asmdef 条件门控/内部可见性/AutoInit 源码守卫。
- **首轮 settings 约束**：`FMODStudioSettings.asset` 不存在时自动安装记 Error 并跳过（不自动创建默认
  settings、不半初始化）；消费方需先运行 FMOD Setup Wizard。
- **`OwnsBanks`**：只卸载 provider 自己成功加载、且加载前未加载的 bank；项目预加载的 bank 永不卸载。
- **包内守卫**：README 结构/文件树、`.meta` 完整性、包边界、包契约、依赖声明、AutoInit 族、
  公开 API 基线（`FmodAudioSettings` 及其构造函数）；`run-tests.ps1` 的 `-UpdateFmodApiBaseline`。

### Not verified

- 真实 bank/event 冒烟需要 FMOD Studio 2.04 工程 + 构建好的 bank；无 `XFW_FMOD_EVENT` /
  `XFW_FMOD_BANK` 时跳过。Phase 1 的其余 FMOD-free 限制仍在 README「已知限制」。

## [0.1.0] - 2026-10-26

### Added

- 包骨架与 FMOD-free 层的首个版本。当前不可实际播放 FMOD。
