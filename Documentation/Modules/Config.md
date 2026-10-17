# Config —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Config/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 模块版本记录

模块内部的时间线（按年月记，非版本号）。~~**这些条目在包 `CHANGELOG.md` 里没有对应记录**——Config 模块在 CHANGELOG 里一条都没有~~，保留本表是为了不丢失引入时间线；包级变更见 CHANGELOG。

> **订正（2026-10-13）**：删除线那句按字面**写下时就不成立**——当时 CHANGELOG 已有 13 行提到 `Config`（都是别的条目顺带提及）；它想说的「**没有一条以本模块为主体的记录**」在写下之前成立，但写下它的同一提交（`5f3b6fa`，2026-09-30）就添了第一条，归并轮（`632755b`，0.3.0）又补齐了 `#### Config` 的**模块轴**（现两条，分属 `### Fixed` 与 `### Documentation`）。**仍然成立的一半**：本模块的提交主体至今不在 CHANGELOG——那条补记自己声明「其余的变更不回填」，**本表仍是引入时间线的唯一完整来源**。

| 版本 | 说明 |
| --- | --- |
| 2026-08 | `ConfigTable<T>` 构造期提取并缓存主键类型（经 `ConfigTypeHelper` 每表仅一次反射），查询键类型不匹配时错误信息报真实键类型；注入字典与行类型声明矛盾时构造即抛 `ConfigException`（早失败） |
| 2026-08 | `ConfigTable<T>.GetRows` 新增 `Comparison<T>` 排序重载（缓冲版/便捷版，参照 GameFramework `GetDataRows(Predicate, Comparison)` 形态） |
| 2026-08 | `ConfigTable<T>` 新增谓词条件查询：`TryGet(predicate, out)` 单匹配、`GetRows(predicate[, List<T>])` 多匹配（缓冲版零 GC）、`Exists(predicate)` 存在性判断 |
| 2026-08 | 取消语义澄清（取消仅中断调用方等待，底层加载仍会完成并注册）、CsvLoader 按列名匹配成员、文档对齐实现（示例修正为真实签名、移除未实现的「后处理钩子」描述） |
| 2026-10-12 | **补 PlayMode 侧覆盖**（`Tests/Runtime/Config/ConfigPreloadFrameTests`，此前 65 条用例全在 `Tests/Editor/`）：三条都要求「加载真的跨了几帧」——① 挂起期可观测（`InFlightLoadCount`）；② 并发共享的是**跨帧**的那一个在途任务（此前 Editor 用例验的是同步共享）；③ 上表 2026-08 那条「取消仅中断调用方等待」**第一次被真帧验证**。配套 Runtime 侧 `FakeConfigLoader`（与 Editor 树同名那份互不可见，各存一份是既有先例）。 |
| 2026-05 | 新增 CSV 格式、非主键索引（`ConfigIndexView`）、批量加载（`ConfigManifest` + `PreloadGroupAsync` / `PreloadAllAsync`）、自定义 `IConfigLoader` 注入、`ConfigTable<T>` 包装器（TKey 由实参自动推断）、`IConfigManager` 接口抽象、`Get` / `TryGet` 便捷查询 |

详细变更见 git log；包级变更自 0.3.0 起另见 `CHANGELOG` 的 `#### Config` 小节。

## 文件结构

```
Runtime/Config/
├── ConfigManager.cs          # 静态外观（全局入口）
├── IConfigManager.cs         # 可替换的后端契约
├── ConfigManagerImpl.cs      # 默认实现：五个字典（表 / 全局 / 包装器 / 路径 / 在途）+ 事件派发
├── IConfigRow.cs             # 行契约（TKey Id，含复合主键用法）
├── IConfigTable.cs           # 表契约的非泛型面（零反射注入路径用）
├── ConfigTable.cs            # 表包装器（构造接收 IDictionary，含条件查询与 BuildIndex）
├── ConfigIndexView.cs        # 非主键索引视图（Get / TryGet）
├── IConfigLoader.cs          # 自定义加载器接口（策略对象，框架不持有引用）
├── JsonLoader.cs             # 内置：JSON（JsonUtility）
├── CsvLoader.cs              # 内置：CSV（Split(',')，方言边界见 README 已知限制）
├── ScriptableObjectLoader.cs # 内置：ScriptableObject（经 AssetManager）
├── ConfigLoadHelper.cs       # 加载调度助手（并发共享同一任务）
├── ConfigTextLoader.cs       # 文本类格式的公共读入段
├── ConfigTypeHelper.cs       # 主键类型推导（reflection 边界，结果缓存）
├── ConfigFormat.cs           # 内置格式枚举（Json / ScriptableObject / Csv）
├── ConfigLoadKind.cs         # 已加载条目种类（Table / Global）
├── ConfigLoadedInfo.cs       # 诊断回读载荷：CopyLoaded 的列表元素
├── ConfigManifest.cs         # 声明式批量清单（AddTable / AddGlobal + 分组）
└── ConfigException.cs        # 本模块统一异常（包装底层异常）
```

（本节 2026-10-16 补建——此前是 23 份里唯二没有文件结构的一份，另一份是 Bootstrap，同日一并补。）

## 决策台账

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过。**一条高危已修**——CSV 的值类型行静默产出默认值；另有一条发现被复现**证伪**（见下）。已修项见 `CHANGELOG`。本节的否决条目逐条理由见使用方 README 的相关章节，此处只留「评估过并否决」这一层——**例外**：Luban 那条的理由是维护向的，本文件自带。）

- 不做：**不给 Config 加线程守卫**（2026-09-30 · 审计轮）——与 Update / Data 同判（契约写进 README 的「线程契约」、失败响亮），不为它加机械。无重开判据。
- 不做：**不改 `_inFlightLoads` 的 `Type` 键**（2026-09-30 · 审计轮）——触发它需要「同一类型既作 Table 行类型又作 Global 类型」，不现实；拆成两个字典会让 Table/Global 的在途概念分叉。无重开判据。
- 不做：**不给 `Unload` 加「取消在途任务」**（2026-09-30 · 审计轮）——文档已写明「卸载可能不生效」；取消它要动加载管线的取消语义。无重开判据。
- 不做：**不给 `IConfigManager` 加 `CancellationToken`**（2026-09-30 · 审计轮）——门面的取消是「只中断调用方等待」（文档已明写、实测与代码一致）；把 token 下推到接口会把「底层仍会完成」这条承诺推翻。无重开判据。
- 不做：**不给 CSV 加引号 / 转义支持**（2026-09-30 · 审计轮）——那是「CSV 方言到哪为止」的取舍，已写进 README 的「已知限制」。无重开判据。
- 不做：**不改 `BuildIndex` 的缓存语义**（2026-09-30 · 审计轮）——同名换类型 / 换 selector 属调用方错误，已写进「已知限制」。无重开判据。
- 暂不做：**不重建内建 Luban 适配层**（2026-10-10 · 复评）——该形状曾存在，于 `a25f6d8`（2026-05-25）「移除内建 Luban 支持」整体删除（-497 行）：Runtime 树内第二个 asmdef `Venusy609.Xframework.Luban` + `Runtime/Config/Luban/LubanLoader.cs`（220 行，反射遍历 `Tb*` 属性、从 `DataList` 取行、主键写死 `Dictionary<int, rowType>`）+ `Editor/LubanTemplates/*.cs.txt` + `ConfigFormat.Luban` 枚举值。**那次删除只记了「移除 Bright.Serialization 程序集依赖」一条动机，「这个形状为什么不好」从未落过纸**——照 `Ledger.md` §一 的纪律（已否决项要带理由 + 出处，否则下一轮会被当新问题重新论证），三条否决点在此补记：
  ① **硬绑类型名字符串** `"Bright.Serialization.ByteBuf"`（`Type.GetType` + `Activator.CreateInstance`）——Luban 侧包名或类型名一变即静默失配；而 Luban 现有多套运行时（`cs-simple-json` / `cs-bin` / `cs-newtonsoft-json` / `cs-dotnet-json`），绑任一套都是赌。
  ② **绑死 `TbXxx.DataList` 反射约定**——该 API 随版本变（5.1.0 里它仍在，但同族还有 `DataMap` / `Get` / 索引器，多键表与单行表的形态又各不相同）。
  ③ **主键写死 `Dictionary<int, rowType>`**（当时 `IConfigRow.Id` 就是固定的 `int`——删除当天稍后的 `9a8b547` 才把它泛型化为 `TKey`）。今天是任何非 int 主键都走不通；更硬的一条是：`LubanLoader` 还实现着**单泛型**的 `IConfigLoader.LoadTableAsync<T>`，而该接口今天是双泛型的 `LoadTableAsync<T, TKey>`——**那段代码按今天的接口编不过**（CS0535）。
  三条是同一个结构性事实的三种表现：**适配层必须猜 Luban 的私有形状，而该形状既不承诺稳定、也不由本框架控制**。另有一条今日才成立的规则约束：Runtime 全部代码属于单一 asmdef（`CLAUDE.md` → 项目定位），而旧形态正是「为可选依赖在 Runtime 树里开第二个 asmdef」。
  **替代形状（现状，即默认不再重开）**：框架只留 `RegisterTable` / `RegisterGlobal` 注入面——`IConfigRow<TKey>` 与 `ConfigTable<T>(IDictionary)`，二者都**不要求 `new()`**。适配责任与 Luban 版本绑定在**使用方**一侧：使用方契约在 `Runtime/Config/README.md` 的「方式三」，完整配方（接缝写法、已核对事实、版本漂移点）在 [`Documentation/Luban.md`](../Luban.md)。
  **重开判据**（须同时满足 ①②，并带一次真实导表的实测）：① Luban 侧出现**稳定的公开契约**（官方承诺行类实现某接口，或提供版本无关的抽象），使适配层不再需要猜类型名与表的内部形状；② 形状满足包内既有约束——不新增第三方程序集依赖、不在 Runtime 树内新增 asmdef、不让不使用 Luban 的项目为它背负成本；③ 有实测证明注入面**表达不了** Luban 数据（当前无此证据：`TKey` 支持任意类型与 `ValueTuple` 复合键，`RegisterTable` 有非泛型重载，`RegisterGlobal` 覆盖单行表；纯列表表本就没有主键，接不进来是应当的）。
  **不算重开理由**：「Luban 发新版本了」（那恰是否决点本身）、「使用方想要开箱即用」（那是文档与配方该回答的事）、「接入还要写几行 partial 接缝文件」（那是使用方一次性的工具链配置，不是框架的运行时代价）。
- 未做：**同一类型既作 Table 行类型又作 Global 类型时的错乱**（F8）（2026-09-30 · 审计轮）——`PreloadGlobalAsync` 会加入 Table 的在途任务并直接 return（不注册 Global），随后 `GetGlobal<T>()` 仍抛「未加载」。触发条件不现实，本轮只归档。2026-10-17 复核：新增一面——Global 先、Table 后时 Table 侧读 `_tables[type]` 抛未包装的 `KeyNotFoundException`；修法可给 `InFlightLoad` 记录结果种类。
- 不做：**不缓存 `ConfigTypeHelper` 的失败路径**（F15）（2026-10-17 · 第二轮审计改判）——类型未实现 `IConfigRow<>` 时每次调用都会抛异常，异常成本远高于重新反射，缓存失败哨兵的收益可忽略。重开判据：出现高频失败重试路径（如热更后反复重载同一坏类型）。
- 已做：**会话级复位落成**（2026-10-12 · `b6c9e8a`，回归用例 `Tests/Runtime/Architecture/SessionResetTests`，已做改前必红）——Config 被列为「重复 `Initialize()` 变 no-op、使用方无法自救」的同根因模块，本轮按同一形态修：门面加 `ResetForNewSession()`，挂 `RuntimeInitializeOnLoadMethod` 的 `SubsystemRegistration` 档，只清 `_instance` / `_instanceInitialized`，**不动 `ConfigChanged` 的外部订阅者**——那是调用方持有的句柄。族的收口记录见 `Runtime/Input/README.md` 已知限制。
- 已推翻：**~~同一个实例连调两次 `SetInstance` 会留下两份订阅、事件派发两次~~**（2026-09-30 · 复现探针；原为审计取证的发现，附「裸 `+=` / `-=` 只移除一次」的推理）——**复现已把它证伪**：漏看的是 `SetInstance` 里先 `UnsubscribeImplEvents(oldImpl)` 那两行，它对同一个实例同样成立（先退订再订阅，净一份）。生产代码因此一行未改，只留下一条钉住该行为的用例。被 2026-09-30「复现证伪——不构成缺陷，净一份订阅」取代。

**为什么这个模块的缺陷史在审计手册里是空白**：Config 的提交主体（含 `fix(...)` 与两次破坏性重构）至今不在 CHANGELOG，而写下时按字面数是「CHANGELOG 里它的出现次数是 **0**」——手册的 A–F 判据是对全仓 CHANGELOG 的 `### Fixed` 段归纳出来的，所以**它从本模块学到的是零**。变更史目前只在 git 与技术文档的「模块版本记录」里。

> **订正（2026-10-13）**：上面那句的**前提不成立**（「出现次数是 0」是字面误述——写下时已有 13 行顺带提及），**结论则要换理由**——判据的归纳快照日期是 **2026-09-27**（`../ModuleAudit.md` 头部），而本模块第一条 `### Fixed` 记录到 **2026-09-30** 才随本轮审计的修复出现：**归纳入早于缺陷史入册**，所以「已成稿的 A–F 清单从本模块学到的是零」依然为真，与「CHANGELOG 里一条都没有」无关。另一半也仍然成立：本模块提交的主体不在 CHANGELOG（补记自己声明不回填）。**下一轮若要重做归纳，本模块的缺陷史这次会被读到**（`CHANGELOG` 的 `### Fixed` → `#### Config`）。表述的分歧与本轮回收见 `../ModuleAudit.md` §七 的「已知边界」-5。
