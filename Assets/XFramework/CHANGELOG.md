# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Asset 低内存自动回收**：`AssetInitOptions.AutoReclaimOnLowMemory`（默认 true）监听 `Application.lowMemory`，自动清池并卸载全部包中未使用资源
- **Asset 批量加载**：`AssetManager.LoadAllAsync<T>` 按序返回句柄数组，单项失败为 default 句柄，取消时自动释放已完成项

### Changed

- **Asset 并发初始化修复**：`InitializeAsync` 并发调用共享同一初始化任务（门面 + 实现层），`Destroy()`/`SetInstance()` 使在途初始化结果作废
- **未初始化异常消息统一**：各模块 guard 消息改为中文 + `[模块]` 前缀 + 修复提示；`DataException` 改为继承 `InvalidOperationException`
- **Reactive 消息总线内部去 R3 化结构**：订阅直落自研事件流，移除内部 ObservableSignal 包装层与信号缓存；公共 API 与行为语义不变（投递顺序、缓冲保留、completed 语义保持）
- **Reactive 清理**：裁撤 Signal/ISignal/IReadonlySignal 死代码（公开类型名中 Signal 术语退场），ReactiveProperty 接入自足 `IReactiveProperty<T>` 接口链；注释与文档同步去 R3 叙述
- **模块拆分**：消息总线与事件流引擎迁入新 `Runtime/Message/`（命名空间 `XFramework.XMessage`），Reactive 仅保留响应式属性；公共类型名不变，使用方仅需改 using（编译期破坏性变更）
- **引擎去 Rx 命名**：Subject→EventStream、ReplaySubject→BufferedEventStream、AnonymousDisposable→ActionDisposable；Unit 删除（InputManager 帧脉冲改发 `Time.frameCount`）；事件引擎日志前缀定稿 `[Message]`

### Fixed

- **Reactive 带过滤订阅的过滤条件抛异常不再击穿派发**：记 Error 日志、该订阅本条不收、订阅保留、其余订阅者照常收到（含缓冲重放路径）

## [0.2.0] - 2026-08-20

### 移除 R3 依赖（重大变更）

- **响应式引擎自研化**：移除 R3 NuGet 依赖与 NuGetForUnity，新增零依赖自研响应式引擎（`XFramework.XReactive.Internal`：Subject/ReplaySubject/AnonymousDisposable/Unit）
- **公共 API 不变**：MessageManager、ReactiveProperty、ReadOnlyReactiveProperty、ISignal、InputManager.ObserveXxx、SettingsManager.Observe/ObserveField、UIBinder 签名与行为语义保持不变（订阅立即回调、相同值去重、异常隔离）
- **修复既有缺陷**：MessageBroker 缓冲通道订阅前发布的消息不再丢失；消息过滤器拦截现在真正生效
- **清理**：删除 packages.config、NuGet DLL、残留 R3 csproj、探针测试；XFrameworkDependencyInstaller 仅保留 UPM 依赖安装
- **第三方集成简化**：安装依赖仅需 UniTask + YooAsset 两个 UPM 包

## [0.1.0] - 2026-01-13

### This is the first release of *\<XFramework\>*.

*Short description of this release*
