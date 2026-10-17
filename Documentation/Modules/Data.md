# Data —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Data/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Data/
├── IDataBlock.cs             # 数据块接口定义
├── IDataManager.cs           # 服务接口
├── DataManager.cs            # 静态门面
├── DataManagerImpl.cs        # 内部实现
├── DataBootstrapStage.cs     # 引导阶段：初始化门面（Phase 3）
├── DataException.cs          # 异常类型
├── DataSnapshot.cs           # 存档快照数据结构
└── README.md
```

## 迁移指南

v1（Table 模型）→ v2（Block 模型）。**0.1.0 从未对外发布**，这批 API 也没进 `CHANGELOG` 的破坏性变更，本表是唯一的升级线索。

| v1 API                                     | v2 API                              |
| ------------------------------------------ | ----------------------------------- |
| `IDataRow<TKey>`                           | 废弃，Block 内部自由管理键值        |
| `DataManager.GetOrCreateTable<T>()`        | `DataManager.GetOrCreateBlock<T>()` |
| `DataTable<T>.Get() / Upsert() / Remove()` | Block 内部自行定义方法              |
| `table.Upsert(row)`                        | `block.Items.Add(item)` 等          |

迁移步骤：
1. 将原有的 `[Serializable] class X : IDataRow<TKey>` 重构为 `[Serializable] class XData : IDataBlock`
2. 在 Block 内部实现 `OnSave / OnLoad / OnClear` 回调，并补 `DataVersion`（未接入版本控制时返回 0）与 `OnMigrate`（恒等返回入参）
3. 将 `DataManager.GetOrCreateTable<X>()` 替换为 `DataManager.GetOrCreateBlock<XData>()`

## 决策台账

- 已做：**数据层的身份/隔离四处修正**（2026-10-18 · `DataRobustnessTests`，DATA-004/005/007/008/010）——脏集合改引用同一比较器；名称索引只摘属于自己的项、BlockName 前置校验；`CreateSnapshot` 先取快照再遍历；`ApplySnapshot` 清空阶段逐块隔离、null 条目计数。

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过。**一条高危已修**——两个块取同一个 `BlockName` 时静默丢数据；其余是文档与测试。逐条理由的展开见使用方 README 的相关章节，此处只留「评估过并否决」这一层。已修项见 `CHANGELOG`。2026-10-17 追加 Bootstrap 审计轮的所有权结论。）

- 已做：**引导阶段的 `Shutdown` 只注销自己初始化的那份**（2026-10-17 · Bootstrap 审计轮）——`DataBootstrapStage` 新增 `_initializedByThisStage`：执行前已初始化（使用方注入 / 上一轮运行留下）时不再在 `Shutdown` 里 `DataManager.Shutdown()`。回归用例 `BootstrapStageTests.DataBootstrapStage_AlreadyInitialized_ShutdownDoesNotShutdown` / `..._InitializedByStage_ShutdownShutsDown`（前者改前必红：`Expected: True But was: False`）。

- 不做：**不给 Data 加线程守卫**（2026-09-30 · 审计轮）——与 Update 那轮同判：契约（所有 API 必须在主线程）写在 README 里，跨线程改 `Dictionary` 的失败是响亮的（抛 `InvalidOperationException` 或数据错乱），不为它加机械。无重开判据。
- 不做：**不给 `ForEachBlock` 加「遍历期间禁止增删」的断言**（2026-09-30 · 审计轮）——它已改为先取快照再遍历（与 `DispatchListPool`、Lock 的订阅快照同向），回调里的增删是**安全**的，无需禁止。无重开判据。
- 不做：**不给 `CreateSnapshot` 加 `bool clearDirty` 参数**（2026-10-14 · 裁定此形状不采纳）——它**不闭合**：`ApplySnapshot` 在恢复任何块之前无条件清脏，而它只有快照这一个信息通道，旋钮拿不到「加载前脏了谁」；「双旋钮」的第二个于是退化成死重量（要么 no-op，要么只是「要不要用已有信息」）。**承重的是信息，不是旋钮**：采纳的是「脏集合随快照携带、应用时还原」（`DataSnapshot.dirtyBlockNames`，internal 字段、不进存档），**零公开 API 变更**。同类还有两条被否的形状：Save 自持 `GetDirtyBlocks()` + 事后重标（修错地方，README 把「留回滚点」作为官方配方教给第三方）、`ApplySnapshot` 单纯不再清脏（翻掉「恢复即干净」契约与 pin 用例，加载成功后脏标记变陈旧）。无重开判据。
- 不做：**不改 `Shutdown` 让它触发 `OnClear`**（2026-09-30 · 审计轮）——`OnClear` 的 XML 列举的调用时机不含 Shutdown，且 `Shutdown` 的语义是「注销实现」而非「清空数据」；已写进 README 的「已知限制」。无重开判据。
- 已做：**回滚能恢复脏标记集合**（2026-10-14 · 回归锁 `DirtyTrackingTests` 三条 + `SaveManagerImplTests` 一条端到端）——原条目所记「`CreateSnapshot` 清脏标记与回滚之间的语义缺口」已闭合：脏集合随快照携带（`DataSnapshot.dirtyBlockNames`，进程内元数据不进存档）、由 `ApplySnapshot` 还原，回滚后「加载前的未保存改动仍是未保存」。沿革与三条被否形状见上方「`CreateSnapshot` 加 `bool clearDirty` 参数」条。
- 未做：**`DataBlockSnapshot.format` 写入端恒为 null**（2026-09-30 · 审计轮）——保留字段是为了将来「按块指定格式」，但目前没有任何写入路径：要么补上（改存档格式）、要么删掉（牵扯老存档兼容性）。本轮只在 XML 与 README 注明「回退是常规路径」。
- 未做：**`DataSnapshot.Factory` 是可变的公开静态委托**（2026-09-30 · 审计轮）——第三方可替换快照子类，但没有任何「谁替换过、何时替换」的记录；Save 侧靠它取类型（`DataSnapshot.Factory().GetType()`）。目前只有测试在替换。
- 待裁：**`DataManager._impl` 是否纳入会话复位族**（2026-10-17 · 第二轮审计）——Save/Data 不在复位族内，默认启动器路径自愈，手动初始化且无 Shutdown 钩子时可能继承上一会话实例（DATA-003）。重开判据：关闭域重载两遍 PlayMode 复现门面陈旧。
- 待裁：**写入端格式契约（`Serializer.Default` vs `defaultFormat`）**（2026-10-17 · 第二轮审计）——写入恒用 `Serializer.Default`，读取按 `defaultFormat`；经 `Factory` 子类把 `defaultFormat` 设为非 `"json"` 会让整份存档读不回（DATA-006）。重开判据：出现第二个需要非默认写入格式的使用方。
- 待裁：**脏集合载体是否提升为公开面**（2026-10-17 · 第二轮审计）——`DataSnapshot.dirtyBlockNames` 是 internal 字段，第三方 `IDataManager` 实现无法兑现接口的脏集合还原契约（DATA-009）。重开判据：出现真实的第三方 `IDataManager` 实现。
- 待裁：**块版本过新经 Save 变成整份 `Corrupt`**（2026-10-17 · 第二轮审计）——Data 跳过未来版本块，Save 把失败计数当作整份失败并回滚报 `Corrupt`（DATA-011）。重开判据：出现代码回滚/多版本并存的实际场景。

