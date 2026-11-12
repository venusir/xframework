# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- 端到端门控冒烟 `FmodEndToEndTests`（`XFW_FMOD_E2E=1` + `XFW_FMOD_EVENT` / `XFW_FMOD_BANK` /
  `XFW_FMOD_GLOBAL_PARAM`）：验证 `FmodAutoInstall` → 真实 `FmodEventBackend` → `AudioManager`
  公开面的播放 / bank / 全局参数全链路；缺省 `Assert.Ignore`。
- 2026-11-12 真实 bank/event 冒烟实测记录：三条既有门控冒烟 8/8、端到端 1/1、有 SDK 的 `Fmod`
  过滤 95 例（86 FMOD-free + 9 Integration）91 通过 / 4 跳过 / 0 失败。

### Fixed

- `SetGlobalParameter` 不再在 `RuntimeManager` 未初始化时提前返回 `false`：`StudioSystem` 会懒初始化，
  「先设全局参数再起播」的第一个调用现在能成功（行为冒烟 `XFW_FMOD_GLOBAL_PARAM` 已于 2026-11-12
  实测通过）。
- 自动安装失败清理只卸载本次安装成功加载的 bank：失败点之后出现的项目预加载 bank 不再被误卸载；
  重复 bank 条目不再导致回滚漏卸；外层失败清理与成功路径统一使用安装开始时的配置快照。
- `TryInstall` 的 `backendFactory == null` 检查提前到所有前置早退之前，与 XML 契约一致。
- `PlayAsync` 对 null/空白 location 返回 default 而非抛 `ArgumentNullException`；占槽后的路径解析
  移入异常隔离区，异常时必定归还槽位。
- 真实 backend 的 `SetBusVolume` / `SetBusMuted` / `SetBusPaused` 在 Dispose 后返回 false；
  `SafeStopAndRelease` 的 `isValid()` 纳入异常隔离。

### Changed

- 包内守卫射程加宽：公开类型正则覆盖修饰符 / record / delegate；asmdef 绑定
  `versionDefines.define` 与 `defineConstraints` 同值并覆盖 Tests asmdef；README 检查精确包名/版本；
  Resources 资产检查覆盖所有 Resources 目录；README 文件树按相对路径比较；`.meta` 根检查仅在
  `Assets/` 布局执行；AutoInit 改用属性正则；包边界扫描全部文本文件。
- 文档明确两条边界：显式 `UnloadBank` 不受 `OwnsBanks` 限制；`SetPaused` / `SetVolume` 的 false
  语义比核心预期更宽，当前只记录不改行为。

### Removed

- 删除未使用的 internal `FmodProviderOptions.Default`。

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
