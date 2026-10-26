# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **FMOD-free provider 层**：`FmodAudioSettings`（public 配置资产，`Enabled` / `EventPathPrefix` /
  `Banks` / `LoadBankSamples` / `OwnsBanks` / `MaxVoices` / `MasterVolume` / `MasterMuted`）、
  `FmodProviderOptions`、`IFmodEventBackend`（internal 缝）、`FmodEventSlotTable`（index + generation）
  与 `FmodAudioProvider`（`IAudioProvider` 的播放原语）。
- **测试**：fake backend、provider 播放/失败/容量/代际/Dispose 用例、settings 默认值与归一化。

### Not verified

- 真实 FMOD 集成程序集、`FMODUnity` 后端、自动安装、`AudioManager` 控制面到真实 FMOD 的端到端
  冒烟均未实现；需要 FMOD 2.04 Asset Store UPM 与 SDK 探针。

## [0.1.0] - 2026-10-26

### Added

- 包骨架与 FMOD-free 层的首个版本。当前不可实际播放 FMOD。
