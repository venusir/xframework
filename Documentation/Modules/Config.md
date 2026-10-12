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
| 2026-10-12 | **补 PlayMode 侧覆盖**（`Tests/Runtime/Config/ConfigPreloadFrameTests`，此前 65 条用例全在 `Tests/Editor/`）：三条都要求「加载真的跨了几帧」——① 挂起期可观测（`InFlightLoadCount`）；② 并发共享的是**跨帧**的那一个在途任务（此前 Editor 用例验的是同步共享）；③ 上表 2026-08 那条「取消仅中断调用方等待」**第一次被真帧验证**。配套 Runtime 侧 `FakeConfigLoader`（与 Editor 树同名那份互不可见，各存一份是既有先例）。 |
| 2026-05 | 新增 CSV 格式、非主键索引（`ConfigIndexView`）、批量加载（`ConfigManifest` + `PreloadGroupAsync` / `PreloadAllAsync`）、自定义 `IConfigLoader` 注入、`ConfigTable<T>` 包装器（TKey 由实参自动推断）、`IConfigManager` 接口抽象、`Get` / `TryGet` 便捷查询 |

详细变更见 git log。

## 已评估未采纳与未决

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过。**一条高危已修**——CSV 的值类型行静默产出默认值；另有一条发现被复现**证伪**（见下）。已修项见 `CHANGELOG`。）

**已评估未采纳**（逐条理由见使用方 README 的相关章节，此处只留「评估过并否决」这一层。**例外**：最后一条的理由是维护向的，本文件自带）：

- **不给 Config 加线程守卫**：与 Update / Data 同判（契约写进 README 的「线程契约」、失败响亮），不为它加机械。
- **不改 `_inFlightLoads` 的 `Type` 键**：触发它需要「同一类型既作 Table 行类型又作 Global 类型」，不现实；拆成两个字典会让 Table/Global 的在途概念分叉。
- **不给 `Unload` 加「取消在途任务」**：文档已写明「卸载可能不生效」；取消它要动加载管线的取消语义。
- **不给 `IConfigManager` 加 `CancellationToken`**：门面的取消是「只中断调用方等待」（文档已明写、实测与代码一致）；把 token 下推到接口会把「底层仍会完成」这条承诺推翻。
- **不给 CSV 加引号/转义支持**：那是「CSV 方言到哪为止」的取舍，已写进 README 的「已知限制」。
- **不改 `BuildIndex` 的缓存语义**：同名换类型/换 selector 属调用方错误，已写进「已知限制」。
- **不重建内建 Luban 适配层**（2026-10-10 复评）。该形状曾存在，于 `a25f6d8`（2026-05-25）「移除内建 Luban 支持」整体删除（-497 行）：Runtime 树内第二个 asmdef `Venusy609.Xframework.Luban` + `Runtime/Config/Luban/LubanLoader.cs`（220 行，反射遍历 `Tb*` 属性、从 `DataList` 取行、主键写死 `Dictionary<int, rowType>`）+ `Editor/LubanTemplates/*.cs.txt` + `ConfigFormat.Luban` 枚举值。**那次删除只记了「移除 Bright.Serialization 程序集依赖」一条动机，「这个形状为什么不好」从未落过纸**——照 `Roadmap.md` 的纪律（已否决项要带理由 + 出处，否则下一轮会被当新问题重新论证），三条否决点在此补记：
  - ① **硬绑类型名字符串** `"Bright.Serialization.ByteBuf"`（`Type.GetType` + `Activator.CreateInstance`）——Luban 侧包名或类型名一变即静默失配；而 Luban 现有多套运行时（`cs-simple-json` / `cs-bin` / `cs-newtonsoft-json` / `cs-dotnet-json`），绑任一套都是赌。
  - ② **绑死 `TbXxx.DataList` 反射约定**——该 API 随版本变（5.1.0 里它仍在，但同族还有 `DataMap` / `Get` / 索引器，多键表与单行表的形态又各不相同）。
  - ③ **主键写死 `Dictionary<int, rowType>`**（当时 `IConfigRow.Id` 就是固定的 `int`——删除当天稍后的 `9a8b547` 才把它泛型化为 `TKey`）。今天是任何非 int 主键都走不通；更硬的一条是：`LubanLoader` 还实现着**单泛型**的 `IConfigLoader.LoadTableAsync<T>`，而该接口今天是双泛型的 `LoadTableAsync<T, TKey>`——**那段代码按今天的接口编不过**（CS0535）。
  三条是同一个结构性事实的三种表现：**适配层必须猜 Luban 的私有形状，而该形状既不承诺稳定、也不由本框架控制**。另有一条今日才成立的规则约束：Runtime 全部代码属于单一 asmdef（`CLAUDE.md` → 项目定位），而旧形态正是「为可选依赖在 Runtime 树里开第二个 asmdef」。
  **替代形状（现状，即默认不再重开）**：框架只留 `RegisterTable` / `RegisterGlobal` 注入面——`IConfigRow<TKey>` 与 `ConfigTable<T>(IDictionary)`，二者都**不要求 `new()`**。适配责任与 Luban 版本绑定在**使用方**一侧：使用方契约在 `Runtime/Config/README.md` 的「方式三」，完整配方（接缝写法、已核对事实、版本漂移点）在 [`Documentation/Luban.md`](../Luban.md)。
  **重开判据**（须同时满足 ①②，并带一次真实导表的实测）：
  - ① Luban 侧出现**稳定的公开契约**（官方承诺行类实现某接口，或提供版本无关的抽象），使适配层不再需要猜类型名与表的内部形状；
  - ② 形状满足包内既有约束——不新增第三方程序集依赖、不在 Runtime 树内新增 asmdef、不让不使用 Luban 的项目为它背负成本；
  - ③ 有实测证明注入面**表达不了** Luban 数据（当前无此证据：`TKey` 支持任意类型与 `ValueTuple` 复合键，`RegisterTable` 有非泛型重载，`RegisterGlobal` 覆盖单行表；纯列表表本就没有主键，接不进来是应当的）。
  **不算重开理由**：「Luban 发新版本了」（那恰是否决点本身）、「使用方想要开箱即用」（那是文档与配方该回答的事）、「接入还要写几行 partial 接缝文件」（那是使用方一次性的工具链配置，不是框架的运行时代价）。

**未决**：

- **同一类型既作 Table 行类型又作 Global 类型时的错乱**（F8）：`PreloadGlobalAsync` 会加入 Table 的在途任务并直接 return（不注册 Global），随后 `GetGlobal<T>()` 仍抛「未加载」。触发条件不现实，本轮只归档。
- **`ConfigTypeHelper` 的失败路径不缓存**（F15）：类型未实现 `IConfigRow<>` 时每次调用都重新反射，且抛的是**未被包装**的 `ConfigException`，文案层级与其它失败路径不一致。
- ~~会话级复位同族~~（D5）—— **2026-10-12 已处置**：Config 被列为「重复 `Initialize()` 变 no-op、使用方无法自救」的同根因模块，本轮按同一形态修（门面加 `ResetForNewSession()`，挂
  `RuntimeInitializeOnLoadMethod` 的 `SubsystemRegistration` 档，只清 `_instance` / `_instanceInitialized`，
  **不动 `ConfigChanged` 的外部订阅者**——那是调用方持有的句柄）。回归用例在
  `Tests/Runtime/Architecture/SessionResetTests`（已做改前必红），族的收口记录见
  `Runtime/Input/README.md` 已知限制。

**一条被证伪的发现（如实留档）**：审计取证曾报「同一个实例连调两次 `SetInstance` 会留下两份订阅、事件派发两次」，并给出「裸 `+=` / `-=` 只移除一次」的推理。**复现已把它证伪**——漏看的是 `SetInstance` 里先 `UnsubscribeImplEvents(oldImpl)` 那两行，它对同一个实例同样成立（先退订再订阅，净一份）。生产代码因此一行未改，只留下一条钉住该行为的用例。

**为什么这个模块的缺陷史在审计手册里是空白**：Config 有 31 条提交（含 `fix(...)` 与两次破坏性重构），而 CHANGELOG 里它的出现次数是 **0**——手册的 A–F 判据是对全仓 CHANGELOG 的 `### Fixed` 段归纳出来的，所以**它从本模块学到的是零**。变更史目前只在 git 与技术文档的「模块版本记录」里。
