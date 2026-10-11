# Reactive —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Reactive/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Reactive/
├── IReactiveProperty.cs          # 响应式属性接口(Value 只读 + Subscribe,面向接口编程)
├── IReactivePropertyWriter.cs    # 可写能力接口(继承前者 + TryWriteValue),双向绑定按需索取
├── ReactiveProperty.cs           # 响应式属性(可写值 + 自动通知 + 去重)
├── ReadOnlyReactiveProperty.cs   # 只读派生属性 + Select 映射扩展
├── IReactiveList.cs              # 响应式列表接口(只读视图 IReadOnlyList<T> + 变更流订阅,不立即回调)
├── ReactiveList.cs               # 响应式列表(增删改 + 单流 struct 载荷通知 + Reset 兜底)
├── ReactiveListChange.cs         # 变更载荷 readonly struct(Kind/Index/Item/OldItem)
└── ReactiveCollectionChangeKind.cs     # 变更词表(Add/Remove/Replace/Reset)
```

## 沿革与已否决形状

- **事件流引擎原先物理上住在 `XMessage.Internal` 里**，本模块直接 `using` 它取 `EventStream<T>`——合计 5 个跨模块文件引用同一处内部命名空间，而全框架共用一个 asmdef、`internal` 不构成编译边界，既没有编译器约束、也没有成文约定可依。2026-09-27 把引擎下沉为**独立模块** `XFramework.XEvent`（公开接口 + 静态工厂 + internal 实现，照 Pipeline 先例），这条依赖随之变成**公开、单向、可自查**——`Tests/Editor/Architecture/ModuleBoundaryTests` 会拦住任何模块对别的模块 `Internal` 的新引用。
- **订阅泄漏是本仓历史上反复出现的一类**（UI / Reactive / Settings 的 README 都专设排查节）；若这类事故再出现，值得单独立项补一个编辑器窗口——即 R3 `ObservableTracker` 那种「列出未释放订阅**及其创建调用栈**」的工具。
  > **2026-10-02 更新**：原先的参照物 `UIStateWindow` 已并入诊断窗口删除；窗口壳现在有了（`Tools/XFramework/Diagnostics`，见 [Modules/Diagnostics](Diagnostics.md)），但**它不改变本项目标**——`ObservableTracker` 要的是「订阅 → 创建调用栈」的登记，那是投递热路径上的记账，与本窗口的拉取式页签是两回事。真要做得单独立项，届时它可以是诊断窗口的一个页签。
- **集合型响应式从「拟已否决」翻案为立项**（2026-10-11）：一次提问（`ReactiveProperty<T>` 的 `T` 是 `List` / 数组会怎样）引出论证，初稿按「已否决」记录——理由是「这类原语的最大用场（列表 UI 逐项刷新 / 条目复用）已在 UI 侧被否，剩下的 gameplay 场景里集合语义是 GamePlay 资产、使用方自建更贴」。该理由被一条反例推翻：怪物 / 弹幕 / 单位这类「一组东西变了，告诉我」**没有**堆叠 / 容量 / 主键 / 排序的领域语义，正是通用原语的形状；且本仓对它有手写先例（`UIManager.Panels` + `PanelOpened/Closed/AllPanelsClosed` 三条消息，即同一个轮子被每个模块各写一遍）。立项边界（六条）与「两处与立项时不同」记在 `Documentation/Roadmap.md` §2.4；v1 已建成（`ReactiveList<T>` + 接口 + 载荷 struct + 词表，4 个文件 / 34 条用例）。

## 已评估未采纳与未决

- **相等比较器不可注入**（未决，2026-10-02 全面清点时记下）——**2026-10-11 标注为已落地（不删除，保留沿革）**：记下时属实，同日 `69e0190` 就加了构造重载 `(T initialValue, IEqualityComparer<T> comparer)` 与 `Select` 的 `comparer` 参数（`CHANGELOG.md` 同日 Added 条目），本条目没跟着更新。教训：清点记下的「未决」可能当天就被落地——引用前先对一遍代码与 CHANGELOG。
- **ReactiveList 的延后项**（未决，2026-10-11 立项时定下）——**其中 `ReactiveDictionary` 同日即标注为重开**：用户给出真实使用方场景（实体 id → 状态 / 槽位 → 内容 / 叠加状态 → 层数 / 资源 id → 计数），「重开任何一项都需要真实使用方的形状」这条判据当场满足，另立 `Documentation/Roadmap.md` §2.5。**其余各项仍照原样有效**：`ReactiveSet` / 有序集延后——Set 还卡在 `IReadOnlySet<T>` 不在 .NET Standard 2.1 API 面内，形状定不了就不定；批量作用域与 `AddRange` / `RemoveRange` / `RemoveAll` 延后（批量装载的正解是构造：静默、不发通知）；集合的 `Select` 派生、写能力接口（没有可替换的后端，承 Timer 的裁定）、派发期快照一律不做。**两处 v1.1 候选**：`Remove(T)` 的相等比较器目前写死 `EqualityComparer<T>.Default`，可照 `ReactiveProperty<T>` 先例加注入；`Version` 计数器在实现前被砍（本仓无先例的新契约面，且主线程同步下「订阅后立即读」已是原子一致点，轮询是比订阅更差的模式）。
