# Asset —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Asset/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Asset/
├── IAssetManager.cs               # 资源管理器公共接口
├── AssetManager.cs                # 静态外观（全局入口）
├── AssetManagerImpl.cs            # 默认实现（对象池 + 生命周期管理）
├── YooAssetManagerImpl.cs         # YooAsset 底层适配器（多包管理）
├── IAssetRemoteServices.cs        # 远端资源地址服务接口
├── IAssetPoolController.cs        # 资源实例池的受控清理能力（可选能力接口）
├── AssetInitOptions.cs            # 初始化配置（包名 / 运行模式 / 低内存回收）
├── AssetInitReport.cs             # 初始化进度载荷（readonly struct，中性，不依赖管线）
├── AssetInitProgressRelay.cs      # 进度直写桥：AssetInitReport → PipelineStageContext
├── AssetBootstrapStage.cs         # 引导阶段：资源管理器初始化（Phase 0，最早）
├── AssetHandle.cs                 # 资源句柄（只读结构体，委托 YooAsset.AssetHandle）
├── AssetDownloaderHandle.cs       # 下载器句柄（事件/控制/等待）
├── SubAssetsHandle.cs             # 子资源句柄（图集/多 Sprite）
├── RawFileHandle.cs               # 原始文件句柄（txt/json/二进制）
└── InstanceTracker.cs             # 实例引用追踪组件（内部）
```

## 模块版本记录

模块内部的时间线（按年月记，非版本号）。部分条目在包 `CHANGELOG.md` 里有对应记录、部分没有，保留本表是为了不丢失引入时间线；包级变更见 CHANGELOG。

| 版本 | 说明 |
| --- | --- |
| 2026-08 | 并发初始化修复（共享任务 + 代际号）、低内存自动回收（AutoReclaimOnLowMemory）、LoadAllAsync 批量持有句柄、未初始化消息统一为 [模块] 前缀中文文案 |
| 2026-08 | 接口扩展：多包架构与初始化配置、卸载控制与存在性查询、热更链路（下载器句柄 + 一键下载）、预加载进度回调、同步加载 / 子资源 / RawFile |
| 2026-08 | 池语义修正（方案 A）：回池保活——回池保留句柄、真正销毁才释放；补齐取消支持与池状态统计 |

详细变更见 git log。
