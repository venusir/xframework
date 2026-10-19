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

## 决策台账

（2026-09-30 第二轮审计。首轮 2026-09-27 自述零高危，**且此后 Save 的代码零变更**——最后一次 `.cs` 改动是 2026-09-19 的 File 侧修复，Save 一行未改，此后的提交全是文档与测试。本轮的重点因此是**复审**：接缝、漂移、首轮未覆盖的面。2026-10-17 追加 Bootstrap 审计轮的所有权结论。）

- 已做：**引导阶段的 `Shutdown` 只注销自己初始化的那份**（2026-10-17 · Bootstrap 审计轮）——`SaveBootstrapStage` 新增 `_initializedByThisStage`：执行前已初始化（使用方注入 / 上一轮运行留下）时不再在 `Shutdown` 里 `SaveManager.Shutdown()`；恢复扫描行为不变。回归用例 `BootstrapStageTests.SaveBootstrapStage_AlreadyInitialized_ShutdownDoesNotShutdown` / `..._InitializedByStage_ShutdownShutsDown`（前者改前必红：`Expected: True But was: False`）。

- 已做：**恢复扫描清 `.meta.tmp`**（2026-10-19 · R3-SAVE-005，本轮修复）——载荷存在时只清载荷 `.tmp`，侧车自己的 `.tmp` 永久残留；同分支一并清；回归 `RecoverAsync_PayloadPresent_ClearsMetaTemp`。
- 已做：**缺失+备份损坏报 Corrupt、元数据查询异常隔离**（2026-10-19 · R3-SAVE-002/003，本轮修复）——主文件缺失且备份损坏时按 `Missing` 回报；`GetSlotMetaAsync` 异常直抛。修法：`Missing` 仅在无备份时成立；查询面异常记 Error 并按损坏条目回报；回归 `TryLoadAsync_MissingPayloadAndCorruptBackup_ReportsCorrupt`。
- 已做：**复制/移动认备份**（2026-10-19 · R3-SAVE-001，本轮修复）——`TransferSlotAsync` 源读取只走载荷、目标占用只查载荷；仅备份可恢复的槽位在查询面算存在、复制/移动却不可用。修法：源载荷缺失时回退备份、目标占用也认 `.bak`；回归两例。
- 已推翻：**~~不改 `RecoverAsync` 让它真的「全程不切回主线程」~~**（2026-09-30 · 第二轮审计）——~~本轮只订正文档：`WriteSidecarAsync` 被 `SaveAsync` 与恢复扫描共用，对前者而言那次 `ReturnToMainThread` 是必需的（公开异步 API 的契约就是「返回前切回主线程」）；要拆就得引入「不切回的写入路径」，在刚做过多次手术的线程路径上再动一刀，收益只是让一条文档声明成立。~~ 被 2026-10-19「R3-ADJ-08：只订正文档、双路径改造记 `未做`」取代：文档已订正为「每次读侧车都切回主线程」，改造本身作为开放条目跟踪，不再记作 `不做`。
- 不做：**不给 `SaveManager.Shutdown` 加「取消在途操作」**（2026-09-30 · 第二轮审计）——`ISaveManager` 不是 `IDisposable`，实现也不持非托管资源；`Shutdown` 的语义是「注销门面持有的实现」，在途操作跑完即可。无重开判据。
- 不做：**不给 `ISaveManager` 加忙碌守卫**（2026-09-30 · 第二轮审计）——三道忙时守卫在**门面层**（`SetCurrentVersion` / `SetCurrentPlayer` / `ClearCurrentPlayer`），接口与实现都没有——刻意的：第三方自己 `new` 出来的实现由它自己负责守卫，门面只对经它注入的那一份负责。无重开判据。
- 未做：**`RecoverAsync` 的设计属性与实现不一致（双路径线程改造）**（2026-09-30 第二轮审计；2026-10-15 追加判据；2026-10-19 R3-ADJ-08 裁定本轮只订正文档）——那条「全程不切回主线程」曾被用来论证 `File` 侧的域根必须在主线程预热；要让它真正成立，得把侧车写入拆成「切回」与「不切回」两条路径——属线程路径改造，该单独走计划。追加判据：恢复扫描里剩下的 **5 处同步 `Delete` 与 2 处同步 `Exists` 不随「File 补异步删除原语」一起迁移**——那条路径的线程契约正是本条目要裁定的东西（`SaveManager.cs` / `ISaveManager.cs` / `SaveManagerImpl.cs` 三处表述互相冲突），在未定论的契约上叠加新行为必然写出第四份矛盾表述；**做它的前置＝本条闭合，两者同批做**。同理，`GetSlotMetaAsync` / `LoadCoreAsync` / `TransferSlotAsync` 三处**单次**同步探测也留在这里（判据是「是否成批」而非「是否同步」）。2026-10-19 进展：文档层已订正（每次读侧车都切回主线程，`SaveManager.cs` / `SaveManagerImpl.cs` / `SaveBootstrapStage.cs` / `Save/README.md` 与 File 侧四处残留），双路径改造仍未做。重开判据：出现必须在恢复扫描期间保证「不切回主线程」的真实使用方，且接受线程路径改造。
- 已做：**回滚能恢复脏标记集合**（2026-10-14 · 端到端回归锁 `TryLoadAsync_BlockRestoreFails_RollsBackDirtyFlagsToo`）——原条目所记 `CreateSnapshot` 清脏标记 → 本模块的回滚恢复不了脏标记集合的缺口已闭合：脏集合随快照携带并由 `ApplySnapshot` 还原（Data 侧 `DataSnapshot.dirtyBlockNames`），本模块**零代码改动**。沿革与被否形状见 `Modules/Data.md`。
- 已做：**删除 / 移动不再被迫在主线程做同步 IO**（2026-10-15 · `FileManager.DeleteAsync` 落地后本模块 5 处迁移 `a57d06a`、复活隐患修复 `1d8ea18`）——`DeleteSlotFiles` → `DeleteSlotFilesAsync`，`DeleteSlotAsync` / `TransferSlotAsync` / `DeleteAllSlotsAsync` / `DeletePlayerAsync` 每处删除后补 `ReturnToMainThread`（循环里的进度回调线程不变），删除全部在池线程执行。**同轮顺带修掉一个既有隐患**：删除顺序改为「先清 `.bak`」——此前中断（取消 / 崩溃）会让恢复扫描把槽位**复活成上一代内容**（改前实测：中断后 `LoadAsync` 拿回 `Gold=1` 而最新存档是 `Gold=2`）。沿革、被否形状与行为变更见 `CHANGELOG` 与 `Modules/File.md`。
- 暂不做：**写盘失败窗口（脏标记先丢）不修**（2026-10-14 登记 · 未修）——`SaveAsync` 在写盘**之前**就经 `CreateSnapshot` 清了脏（「导出即视为已保存」的固有代价），写失败时脏标记已丢；届时修法已现成：让 `SaveAsync` 自留一个回滚点再 `ApplySnapshot` 回去（脏集合现已随快照还原）。重开判据：出现「写失败后必须重存」的真实需求。
- 不做：**`SaveManagerFactory` 的真实替换路径按「仓内测不了」登记、不按「以后补」**（2026-09-30 · 第二轮审计）——它是公开扩展点，本轮补了「经 factory 注入自定义实现」的用例，但**跨平台/跨进程的真实后端替换**（Console SDK、云存档）在仓内无路径可测——与 File 的移动端 Streaming 同属「如实登记的覆盖空白」。重开判据：仓内出现可测的真实后端替换通道（Console SDK / 云存档）。
- 不做：**不为 Save 补 PlayMode 侧用例**（2026-10-13 · 覆盖失衡盘点的处置）——88 例已在 `Tests/Runtime/`（**PlayMode 平台**）跑真临时 FS + 真线程端到端；门面无帧依赖、无 MonoBehaviour 生命周期面；README 三条已知限制均为流程内 / EditMode 可验的语义事实。真实后端替换的空白（上条）不因 PlayMode 而改变。无重开判据。
- 已做：**Save 纳入会话复位族**（2026-10-18 · `753b501`）——新增 `ResetForNewSession`（`SubsystemRegistration`）只丢 `_impl`、不 Dispose（玩家上下文/忙标志/版本号都在实例上）；同时修复「File 复位后第二会话加密接线丢失」——引导阶段重新初始化时会重接 `SaveOptions.CryptoProvider`（SAVE-001）。


- 不做：**不为 `GetSlotMetasAsync(default)` 补显式重载/文档提示**（2026-10-18 · 独立工程 dotnet build 探针）——单实参 `default` 字面量正确绑定到 `(CancellationToken)` 重载（可选参数的 `(IProgress<SaveReport>, CancellationToken)` 候选被排除），未产生 CS0121；审计推断不成立。无重开判据。
