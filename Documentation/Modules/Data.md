# Data —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Data/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
XData/
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

## 已评估未采纳与未决

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过。**一条高危已修**——两个块取同一个 `BlockName` 时静默丢数据；其余是文档与测试。已修项见 `CHANGELOG`。）

**已评估未采纳**（逐条理由见使用方 README 的相关章节，此处只留「评估过并否决」这一层）：

- **不给 Data 加线程守卫**：与 Update 那轮同判——契约（所有 API 必须在主线程）写在 README 里，跨线程改 `Dictionary` 的失败是响亮的（抛 `InvalidOperationException` 或数据错乱），不为它加机械。
- **不给 `ForEachBlock` 加「遍历期间禁止增删」的断言**：它已改为先取快照再遍历（与 `DispatchListPool`、Lock 的订阅快照同向），回调里的增删是**安全**的，无需禁止。
- **不给 `CreateSnapshot` 加 `bool clearDirty` 参数**：Save 的回滚因此恢复不了脏标记集合（`SaveManagerImpl` 的注释已记根因）——**跨模块公开 API 变更**，该单独走计划，本轮只归档。
- **不改 `Shutdown` 让它触发 `OnClear`**：`OnClear` 的 XML 列举的调用时机不含 Shutdown，且 `Shutdown` 的语义是「注销实现」而非「清空数据」；已写进 README 的「已知限制」。

**未决**：

- **`CreateSnapshot` 清脏标记与回滚之间的语义缺口**：见上。根除要给 Data 加参数（跨模块 API 变更）。
- **`DataBlockSnapshot.format` 写入端恒为 null**：保留字段是为了将来「按块指定格式」，但目前没有任何写入路径——要么补上（改存档格式）、要么删掉（牵扯老存档兼容性）。本轮只在 XML 与 README 注明「回退是常规路径」。
- **`DataSnapshot.Factory` 是可变的公开静态委托**：第三方可替换快照子类，但没有任何「谁替换过、何时替换」的记录；Save 侧靠它取类型（`DataSnapshot.Factory().GetType()`）。目前只有测试在替换。
