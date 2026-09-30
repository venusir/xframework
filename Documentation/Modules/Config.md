# Config —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Config/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 模块版本记录

模块内部的时间线（按年月记，非版本号）。**这些条目在包 `CHANGELOG.md` 里没有对应记录**——Config 模块在 CHANGELOG 里一条都没有，保留本表是为了不丢失引入时间线；包级变更见 CHANGELOG。

| 版本 | 说明 |
| --- | --- |
| 2026-08 | `ConfigTable<T>` 构造期提取并缓存主键类型（经 `ConfigTypeHelper` 每表仅一次反射），查询键类型不匹配时错误信息报真实键类型；注入字典与行类型声明矛盾时构造即抛 `ConfigException`（早失败） |
| 2026-08 | `ConfigTable<T>.GetRows` 新增 `Comparison<T>` 排序重载（缓冲版/便捷版，参照 GameFramework `GetDataRows(Predicate, Comparison)` 形态） |
| 2026-08 | `ConfigTable<T>` 新增谓词条件查询：`TryGet(predicate, out)` 单匹配、`GetRows(predicate[, List<T>])` 多匹配（缓冲版零 GC）、`Exists(predicate)` 存在性判断 |
| 2026-08 | 取消语义澄清（取消仅中断调用方等待，底层加载仍会完成并注册）、CsvLoader 按列名匹配成员、文档对齐实现（示例修正为真实签名、移除未实现的「后处理钩子」描述） |
| 2026-05 | 新增 CSV 格式、非主键索引（`ConfigIndexView`）、批量加载（`ConfigManifest` + `PreloadGroupAsync` / `PreloadAllAsync`）、自定义 `IConfigLoader` 注入、`ConfigTable<T>` 包装器（TKey 由实参自动推断）、`IConfigManager` 接口抽象、`Get` / `TryGet` 便捷查询 |

详细变更见 git log。
