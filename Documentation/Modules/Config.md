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

## 已评估未采纳与未决

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过。**一条高危已修**——CSV 的值类型行静默产出默认值；另有一条发现被复现**证伪**（见下）。已修项见 `CHANGELOG`。）

**已评估未采纳**（逐条理由见使用方 README 的相关章节，此处只留「评估过并否决」这一层）：

- **不给 Config 加线程守卫**：与 Update / Data 同判（契约写进 README 的「线程契约」、失败响亮），不为它加机械。
- **不改 `_inFlightLoads` 的 `Type` 键**：触发它需要「同一类型既作 Table 行类型又作 Global 类型」，不现实；拆成两个字典会让 Table/Global 的在途概念分叉。
- **不给 `Unload` 加「取消在途任务」**：文档已写明「卸载可能不生效」；取消它要动加载管线的取消语义。
- **不给 `IConfigManager` 加 `CancellationToken`**：门面的取消是「只中断调用方等待」（文档已明写、实测与代码一致）；把 token 下推到接口会把「底层仍会完成」这条承诺推翻。
- **不给 CSV 加引号/转义支持**：那是「CSV 方言到哪为止」的取舍，已写进 README 的「已知限制」。
- **不改 `BuildIndex` 的缓存语义**：同名换类型/换 selector 属调用方错误，已写进「已知限制」。

**未决**：

- **同一类型既作 Table 行类型又作 Global 类型时的错乱**（F8）：`PreloadGlobalAsync` 会加入 Table 的在途任务并直接 return（不注册 Global），随后 `GetGlobal<T>()` 仍抛「未加载」。触发条件不现实，本轮只归档。
- **`ConfigTypeHelper` 的失败路径不缓存**（F15）：类型未实现 `IConfigRow<>` 时每次调用都重新反射，且抛的是**未被包装**的 `ConfigException`，文案层级与其它失败路径不一致。
- **会话级复位同族**（D5）：CHANGELOG 把 Config 列为「重复 `Initialize()` 变 no-op、使用方无法自救」的同根因模块——它与「关闭域重载时的会话复位」那一族同根因（该族的归档在 `Runtime/Input/README.md` 与 `Runtime/UI/README.md` 的已知限制里），修法待设计。

**一条被证伪的发现（如实留档）**：审计取证曾报「同一个实例连调两次 `SetInstance` 会留下两份订阅、事件派发两次」，并给出「裸 `+=` / `-=` 只移除一次」的推理。**复现已把它证伪**——漏看的是 `SetInstance` 里先 `UnsubscribeImplEvents(oldImpl)` 那两行，它对同一个实例同样成立（先退订再订阅，净一份）。生产代码因此一行未改，只留下一条钉住该行为的用例。

**为什么这个模块的缺陷史在审计手册里是空白**：Config 有 31 条提交（含 `fix(...)` 与两次破坏性重构），而 CHANGELOG 里它的出现次数是 **0**——手册的 A–F 判据是对全仓 CHANGELOG 的 `### Fixed` 段归纳出来的，所以**它从本模块学到的是零**。变更史目前只在 git 与技术文档的「模块版本记录」里。
