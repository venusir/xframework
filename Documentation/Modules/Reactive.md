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
├── IReactiveDictionary.cs        # 响应式字典接口(只读视图 IReadOnlyDictionary<TKey,TValue> + 变更流订阅)
├── ReactiveDictionary.cs         # 响应式字典(upsert + 键比较器注入 + 单流 struct 载荷 + Reset 兜底)
├── ReactiveDictionaryChange.cs   # 字典变更载荷 readonly struct(Kind/Key/Value/OldValue)
└── ReactiveCollectionChangeKind.cs  # 集合共享词表(Add/Remove/Replace/Reset)
```

## 沿革与已否决形状

- **事件流引擎原先物理上住在 `XMessage.Internal` 里**，本模块直接 `using` 它取 `EventStream<T>`——合计 5 个跨模块文件引用同一处内部命名空间，而全框架共用一个 asmdef、`internal` 不构成编译边界，既没有编译器约束、也没有成文约定可依。2026-09-27 把引擎下沉为**独立模块** `XFramework.XEvent`（公开接口 + 静态工厂 + internal 实现，照 Pipeline 先例），这条依赖随之变成**公开、单向、可自查**——`Tests/Editor/Architecture/ModuleBoundaryTests` 会拦住任何模块对别的模块 `Internal` 的新引用。
- **订阅泄漏是本仓历史上反复出现的一类**（UI / Reactive / Settings 的 README 都专设排查节）；若这类事故再出现，值得单独立项补一个编辑器窗口——即 R3 `ObservableTracker` 那种「列出未释放订阅**及其创建调用栈**」的工具。
  > **2026-10-02 更新**：原先的参照物 `UIStateWindow` 已并入诊断窗口删除；窗口壳现在有了（`Tools/XFramework/Diagnostics`，见 [Modules/Diagnostics](Diagnostics.md)），但**它不改变本项目标**——`ObservableTracker` 要的是「订阅 → 创建调用栈」的登记，那是投递热路径上的记账，与本窗口的拉取式页签是两回事。真要做得单独立项，届时它可以是诊断窗口的一个页签。
- **集合型响应式从「拟已否决」翻案为立项**（2026-10-11）：一次提问（`ReactiveProperty<T>` 的 `T` 是 `List` / 数组会怎样）引出论证，初稿按「已否决」记录——理由是「这类原语的最大用场（列表 UI 逐项刷新 / 条目复用）已在 UI 侧被否，剩下的 gameplay 场景里集合语义是 GamePlay 资产、使用方自建更贴」。该理由被一条反例推翻：怪物 / 弹幕 / 单位这类「一组东西变了，告诉我」**没有**堆叠 / 容量 / 主键 / 排序的领域语义，正是通用原语的形状；且本仓对它有手写先例（`UIManager.Panels` + `PanelOpened/Closed/AllPanelsClosed` 三条消息，即同一个轮子被每个模块各写一遍）。立项边界（六条）与「两处与立项时不同」记在 `Documentation/Roadmap.md` §2.4；v1 已建成（`ReactiveList<T>` + 接口 + 载荷 struct + 词表，4 个文件 / 34 条用例）。
- **`ReactiveDictionary` 同日从延后项重开并建成**（2026-10-11）：用户提出「按键寻址在游戏开发中很常见」并给出四类场景（实体 id → 状态 / 槽位 → 内容 / 叠加状态 → 层数 / 资源 id → 计数），立项边界见 `Roadmap.md` §2.5。**与 ReactiveList 那轮的关键差别（下一轮别再套错）**：本轮的立项依据**不是**「回填既有设施」——逐条核对后仓内 5 处「键 → 值 + 通知」的手写设施 **0 处可被替换**（`LockManager._locks` 是**键 → 集合**的三级结构且带聚合边沿与销毁绑定；`SettingRefRegistry<T>` 根本不是键值表——封闭泛型静态类 + `List`；UI 的公开读面是栈序列表；Localization / Config 是整表级 / 类型级广播 + LRU / 异步），依据是前瞻性使用方场景。形状三处与列表不同：**无索引哨兵**（`Reset` 的 `Key` 是 `default(TKey)`）、**有键比较器**（构造注入 + `Comparer` 属性，依据是 Lock 审计里那类「引用类型键走 `Equals` 静默合并」的事故）、**键比索引稳定但条目会被移除**（重入后果按此重写）。**两处刻意与第三方分歧**（对照 UniRx `ReactiveDictionary` 与 Cysharp `ObservableDictionary`——源码一手读取、**仓内不可复验**，按转述标注）：值相等 upsert 静默（换来「有通知 ⇔ 状态确实变了」，回答审计 F 的双观测面判据）、空表 `Clear` 静默。词表同批改名为共享的 `ReactiveCollectionChangeKind`（39 处 / 6 文件，未发布成本最低）。
- **效率线的一次重估：稠密键值容器被门槛否决、集合遍历改为零装箱**（2026-10-11，与上一轮同日）：用户澄清「综合 list 遍历与 dict 寻址」的**原始动机是效率**（Actor 管理器：全量遍历 + 按键寻址 + N 大），**不是顺序**——这推翻了前一轮按顺序语义做的两轮评估的前提。本轮按本仓纪律**先探针、门槛事先写死**，实测三条门槛**两条明确不过、一条不过比值线**（数字与复验方法见「实测记录」节）→ 稠密容器**不做**。同一次探针确认了两个集合类型**每次 `foreach` 都装箱**（`GetEnumerator()` 返回接口，反射可验的充要原因），该问题已修：公开 BCL 结构体枚举器，10 万次直接 `foreach` 的堆增量 512000 B → 0 B（`ReactiveEnumerationAllocationTests` 用结构断言 + 粗尺子双重锁住）。**探针还发现本环境有两把分配尺子是坏的**——见「实测记录」节末条。

## 已评估未采纳与未决

- **相等比较器不可注入**（未决，2026-10-02 全面清点时记下）——**2026-10-11 标注为已落地（不删除，保留沿革）**：记下时属实，同日 `69e0190` 就加了构造重载 `(T initialValue, IEqualityComparer<T> comparer)` 与 `Select` 的 `comparer` 参数（`CHANGELOG.md` 同日 Added 条目），本条目没跟着更新。教训：清点记下的「未决」可能当天就被落地——引用前先对一遍代码与 CHANGELOG。
- **ReactiveList 的延后项**（未决，2026-10-11 立项时定下）——**其中 `ReactiveDictionary` 同日即标注为重开并建成**（见沿革；条目原文保留在下）：「`ReactiveDictionary` / `ReactiveSet` / 有序集延后——Set 还卡在 `IReadOnlySet<T>` 不在 .NET Standard 2.1 API 面内，形状定不了就不定；批量作用域与 `AddRange` / `RemoveRange` / `RemoveAll` 延后（批量装载的正解是构造：静默、不发通知）；集合的 `Select` 派生、写能力接口（没有可替换的后端，承 Timer 的裁定）、派发期快照一律不做」。**其余各项仍照原样有效**，另 `ReactiveDictionary` 侧新增两条同族延后项：**有序键值**、按值查找。**有序键值一条于 2026-10-11 二次评估后改写理由**——原文「有序键值（**要新增重排词条**并给遍历顺序承诺，是另一个容器）」**是错的**：词典载荷根本没有位置字段，四个词条本就够用；顺序由容器结构决定，消费方重读。**正确表述**：顺序是**语义**需求、不是效率需求，保序删除 O(n) 是它的价（仓内先例：只有顺序是公开承诺时才付这个价——`UpdateScheduler.InsertSorted` 的派发次序、UI 的栈序）；两个候选形态（按 key 排序的 `ReactiveSortedDictionary`、插入序的 `ReactiveOrderedDictionary`）**已设计未实施**，**重开判据是语义判据**——出现「按键寻址 + 顺序为语义要求」的使用方（结算序 / 生成序 / 按 id 排序展示），**不是性能判据**。**两处 v1.1 候选**（列表侧）：`Remove(T)` 的相等比较器目前写死 `EqualityComparer<T>.Default`，可照 `ReactiveProperty<T>` 先例加注入；`Version` 计数器在实现前被砍（本仓无先例的新契约面，且主线程同步下「订阅后立即读」已是原子一致点，轮询是比订阅更差的模式）。**字典侧一条 v1.1 候选**：键的生命周期自动移除（销毁令牌绑定）——两条硬阻断：`IDestroyCancellationToken` 在 Message 模块（会新增 `Reactive → Message` 依赖）、令牌回调跑在**取消者线程**（与全族主线程专用冲突）；重开先回答「令牌回调线程 / 该接口迁往哪个中立模块 / 值被替换后旧令牌如何解除」三问。
- **稠密键值容器不做**（未决，2026-10-11 实测后裁定）：形状 = `List<KeyValuePair>` + `Dictionary<TKey,int>`（键→下标）+ 交换删除（末尾补位，O(1)），卖点是「稠密遍历 + 按键寻址」。三条**事先写死**的门槛实测**均不过**：遍历端到端 1.22–1.28×（要求 ≥1.3×）、内存约 1.54×（要求 ≤1.35×）、查找退化 1.45–1.61×（要求 ≤1.15×）；写路径另慢 2–3×。数字与复验方法见「实测记录」节。**重开判据**：出现「每帧全量遍历是主路径且 N 远超 4096」的真实使用方，且届时重跑同一探针过线。**正解写在使用方 README**（「本模块没有的」）：需要「稠密遍历 + 按键寻址」请自建 `List<T>` + `Dictionary<TKey,int>` 索引——本模块的 `ReactiveDictionary` 在 N=4096 时遍历约 0.12 ms/帧、占帧预算 0.7%，多数场景并不需要它。

## 实测记录

> **事实分级**：**实测** = 本仓跑出来的数字（方法在下方，可复验）；**推导** = 算式或 BCL 设计层事实，未在本仓实测。环境：Unity 6000.4.5f1 / Editor(batch, Mono) / WindowsEditor——**真机 IL2CPP 未测**，数字不可跨运行时外推。

### 2026-10-11 稠密键值容器 vs BCL 两种形状（判定：不做）

**方法（可复验）**：一次性探针（不入仓，跑完连同 `.meta` 删除）。N∈{64,512,4096}；计时每项预热 64 次、外层 7 次取中位数、内层 20 趟；遍历循环体为「键异或进累加器」防死代码消除；「画像」= 一帧全表遍历 + 1 次按键查找 + 每 100 帧 1 次增删，报每帧耗时与占 16.7 ms 帧预算的百分比。

| 维度 | `Dictionary<K,V>` | 稠密原型 | 比值 | 门槛 | 判定 |
|---|---|---|---|---|---|
| 遍历 ns/元素（N=4096 / 512 / 64） | 29.03 / 29.45 / 30.00 | 23.24 / 23.56 / 26.56 | 1.25× / 1.25× / 1.13× | ≥1.3× | ✗ |
| 画像：每帧耗时（N=4096） | 0.116 ms（0.70% 帧预算） | 0.091 ms（0.54%） | 1.28× | 比值 ≥1.3× | ✗ |
| 内存 B/元素（N=200000，`int→int`） | 22.6 | 34.8 | **1.54×** | ≤1.35× | ✗ |
| 查找 ns/次（`string` 键，随机命中，N=4096） | 95.6 | 153.8 | **1.61× 退化** | ≤1.15× | ✗ |
| 插入 / 删除+复原 ns/元素（N=4096） | 44 / 104 | 84 / 290 | 约 2–3× 慢 | — | 记录 |

**结构事实（反射，稳定）**：BCL `Dictionary<int,int>`（N=1000）的 `_buckets` 与 `_entries` 长度均 1931（素数容量）、`_count=1000`、`_freeCount=0`——「删除才留空洞、枚举按容量走」在本运行时成立。

### 2026-10-11 本环境有两把分配尺子是坏的

- **`GC.GetAllocatedBytesForCurrentThread()` 恒返回 0**：分配 1 MB 数组后增量仍是 **0 B**；`new object()` ×1000、结构体枚举器转接口 ×100 同样 0。
- **`Profiler.GetTotalAllocatedMemoryLong()` 与 `Profiler.GetMonoHeapSizeLong()` 同样恒 0**。
- 可用的是 **`Profiler.GetMonoUsedSizeLong()`**（同一次 1 MB 分配测得 1003520 B）与 `GC.GetTotalMemory(false)`；前者粒度约 4–8 KB，单次装箱（约 32–40 B）看不见，必须放大到 10 万次量级。
- **因此本仓既有的 `TimerAllocationTests` / `LogAllocationTests`（断言 `AreEqual(0L, …)`）在本环境下恒真**——它们锁的结论**未被证实**（未必为假，但没被验证）。修订方案待定；同条已记入 `Documentation/Workflow.md`。

### 2026-10-11 集合遍历零装箱（改造前后，同一探针同一尺子）

| 10 万次直接 `foreach` 的堆增量 | 改造前 | 改造后 |
|---|---|---|
| BCL `List<int>` / `Dictionary<int,int>`（对照） | 0 B | 0 B |
| `ReactiveList<int>` | 512000 B | **0 B** |
| `ReactiveDictionary<int,int>` | 507904 B | **0 B** |
| 经接口 `foreach`（两个类型） | 512000 B | 512000 B（接口本身的要求，无法避免） |

装箱的**充要原因**（反射，确定性、不依赖尺子）：BCL 两类型的 `GetEnumerator()` 返回结构体 `Enumerator`；改造前我们返回 `IEnumerator<T>`。该原因已由 `ReactiveEnumerationAllocationTests` 的结构断言锁住，粗尺子测量只作第二道防线。
