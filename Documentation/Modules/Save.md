# Save —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Save/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Save/
├── ISaveManager.cs        # 接口 + 工厂委托
├── SaveManager.cs         # 静态门面
├── SaveManagerImpl.cs     # 默认实现
├── SaveMeta.cs            # 存档元数据（含侧车字段与校验和）
├── SaveOptions.cs         # 初始化选项（格式版本、加密）
├── SaveReport.cs          # 进度载荷
├── SaveLoadResult.cs      # 加载状态与结果
├── SavePathUtility.cs     # 路径构造、槽位解析与标识校验
├── SaveIntegrity.cs       # 载荷校验和（FNV-1a 64）
├── SaveBootstrapStage.cs  # 引导阶段：初始化门面 + 跑一次启动恢复扫描
└── README.md
```

## 沿革与已否决形状

- **「文件原语可从任意线程调用」这条前提曾经不成立**：Provider 在池线程上解析域根会撞 Unity 的主线程限定，恢复扫描因此必崩；修复后域根改在主线程解析并缓存（见 `File/README.md` 的「线程契约」）。

## 已评估未采纳与未决

（2026-09-30 第二轮审计。首轮 2026-09-27 自述零高危，**且此后 Save 的代码零变更**——最后一次 `.cs` 改动是 2026-09-19 的 File 侧修复，Save 一行未改，此后的提交全是文档与测试。本轮的重点因此是**复审**：接缝、漂移、首轮未覆盖的面。）

**已评估未采纳**：

- **不改 `RecoverAsync` 让它真的「全程不切回主线程」**：本轮只订正文档。`WriteSidecarAsync` 被 `SaveAsync` 与恢复扫描**共用**——对前者而言那次 `ReturnToMainThread` 是**必需的**（公开异步 API 的契约就是「返回前切回主线程」）。要拆就得引入「不切回的写入路径」，在刚做过多次手术的线程路径上再动一刀，收益只是让一条文档声明成立。见「未决」。
- **不给 `SaveManager.Shutdown` 加「取消在途操作」**：`ISaveManager` 不是 `IDisposable`，实现也不持非托管资源；`Shutdown` 的语义是「注销门面持有的实现」，在途操作跑完即可。
- **不给 `ISaveManager` 加忙碌守卫**：三道忙时守卫在**门面层**（`SetCurrentVersion` / `SetCurrentPlayer` / `ClearCurrentPlayer`），接口与实现都没有——刻意的：第三方自己 `new` 出来的实现由它自己负责守卫，门面只对经它注入的那一份负责。

**未决**：

- **`RecoverAsync` 的设计属性与实现不一致**：那条「全程不切回主线程」曾被用来论证 `File` 侧的域根必须在主线程预热。要让它真正成立，得把侧车写入拆成「切回」与「不切回」两条路径——属线程路径改造，该单独走计划。
- **`CreateSnapshot` 清脏标记 → 本模块的回滚恢复不了脏标记集合**：根除要给 Data 加 `CreateSnapshot(bool clearDirty)`，属**跨模块公开 API 变更**（Data 侧的技术文档同样记着这条）。
- **`SaveManagerFactory` 的真实替换路径仍无覆盖**：它是公开扩展点。本轮补了「经 factory 注入自定义实现」的用例，但**跨平台/跨进程的真实后端替换**（Console SDK、云存档）在仓内无路径可测——与 File 的移动端 Streaming 同属「如实登记的覆盖空白」。
