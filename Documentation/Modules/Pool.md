# Pool —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Pool/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Pool/
├── CollectionPool/
│   ├── ListPool.cs                # List<T> 池，Return 自动 Clear()
│   ├── HashSetPool.cs             # HashSet<T> 池，Return 自动 Clear()
│   ├── DictionaryPool.cs          # Dictionary<K,V> 池，Return 自动 Clear()
│   ├── StringBuilderPool.cs       # StringBuilder 池，Return 自动 Clear()
│   └── CollectionPoolManager.cs   # 集合池统一管理器，一键 ClearAll()
├── IPoolable.cs                   # 生命周期回调接口（OnRent / OnReturn）
├── IPoolDiscardable.cs            # 可选能力接口：实例被池丢弃时释放资源（OnDiscard）
├── PoolConfig.cs                  # 配置结构体（值类型，零装箱）
├── PooledObject.cs                # using 包装器（struct，零 GC）
├── IPool.cs                       # 池操作接口（用于 DI / 测试）
├── Pool.cs                        # 泛型池实现（核心）
├── PoolManager.cs                 # 全局静态管理器
├── PoolStats.cs                   # 诊断快照：CopyPoolStats 的列表元素（2026-10-16 补录——它一直是公开面，此前漏在本树之外）
├── Internal/
│   └── PoolLogCategories.cs       # 本模块的日志分类定义（internal；2026-10-16 补录——同上）
└── README.md
```

## 模块版本记录

本表是**模块独立成包时代的本地版本号**（1.0.0–1.3.0），与包 `CHANGELOG.md` 的语义化版本（`0.1.0` / `0.2.0`）**不是同一套**，不要互相映射。1.3.0 与 1.2.0 的部分内容在 CHANGELOG 里有对应条目、1.1.0 / 1.0.0 没有；保留本表是为了不丢失本模块的时间线。

| 版本  | 说明                                                                |
| ----- | ------------------------------------------------------------------- |
| 1.3.0 | 补 `CountActive`；新增池满丢弃回调（`onDestroy` / `IPoolDiscardable`）；归还按运行时类型回落；预热与栈容量钳到 `MaxSize`；集合池 Configure 守卫改用真实活跃计数；重复归还检测改用引用相等比较器；`PooledObject.Dispose` 幂等。**破坏性**：`IPool<T>` / `Pool<T>` / `PooledObject<T>` 加 `where T : class` 约束、删除 `PoolManagerExtensions` |
| 1.2.0 | 修复 ClearAll 改为清闲置语义（原实现会使 Return 永久失效）、Pool.Clear 保留 Editor 活跃追踪；新增 EditMode 单元测试 |
| 1.1.0 | 新增 CollectionPool 集合池（List/HashSet/Dictionary/StringBuilder） |
| 1.0.0 | 初始版本                                                            |

**2026-10-12 补 PlayMode 侧覆盖**（`Tests/Runtime/Pool/PoolFrameLifecycleTests`，3 例；此前 66 条用例全在
`Tests/Editor/`）：本模块此前的入池-归还用例都是**同一时间片内**完成的，而真实用法里实例常常跨帧被持有
（`using` 块的 `Dispose` 落在某一帧末尾、复用发生在再下一帧）。三条分别验：① 跨帧持有期间 `CountActive`
为 1、`Dispose` 归还后计数翻转、再取回**引用同一**（复用的定义）；② `IPoolable.OnRent` / `OnReturn`
此前只验过**次数**，现在验**帧号**——租出在第一帧、归还落在后续帧且两帧不同；③ `ClearAll` 跨帧后
池仍注册可用（清的是闲置实例，不是注册表）。

## 决策台账

（按 `Modules/README.md` 的约定**逐轮追加、不覆盖**。下面是从既有设计决定与变更记录（模块 README / CHANGELOG / 当时的 `Roadmap.md`，今 `Ledger.md`）里归集的第一批，出处逐条标注；整轮审计（`../ModuleAudit.md` 的 A–F）的结论会在做过后追加到本节。）

- 不做：**不加公开的非泛型 `IPool` 基接口**（2026-10-02 · 诊断模块那一轮，`CHANGELOG` 的「Pool / Config / Settings 三组诊断回读」）——会让用显式实现 `IPool<T>` 的第三方编译不过（CS0535）；诊断回读与非泛型入口改走**内部缝** `IUntypedPool`（`Pool<T>` 的三个已有公开属性隐式实现它，池本身零改动）。**本条原先记在 `Diagnostics.md` 的「已评估未采纳」里**——按落点表「决策归模块」迁到此处，那边留指针。无重开判据。
- 不做：**不给 `IPoolable` 加成员**（2026-09-27 · 池满丢弃回调那一轮，`6fac9fd`）——池满丢弃回调另立可选能力接口 `IPoolDiscardable`；`IPoolable` 是设计给第三方数据类实现的，给它加方法即**源码破坏**——新能力一律走可选能力接口（`A2` 的同一条形状）。无重开判据。
- 不做：**不把静态性接口化 / 配置化**（2026-10-02 · 同批清点，判据见 `Ledger.md` 的「把模块内部策略接口化」条）——`PoolManager` 与四个集合池的静态门面：这不是「配置」而是**模块不变量**——换掉它，池的全局唯一性与「`Configure` 只消费一次」等保证都不再成立。同批结论（Timer / Lock 同判）见该条。无重开判据。
- 不做：**不支持值类型池**（`where T : class`）（2026-09-27 · `0840a21`，1.3.0 的破坏性标签）——实例复用依赖**引用同一性**：Editor 的重复归还检测与 `CountActive` 的取还配对都会因此失真；1.3.0 取齐 `UnityEngine.Pool` / GameFramework / `Microsoft.Extensions.ObjectPool` 的既有约束。无重开判据。
- 不做：**不把 `PooledObject<T>` 做成「防得住副本」的形态**（2026-09-27 · `99fce31`）——副本各自持有一份 `_disposed`，结构体复制天然无法共享标志——要防住只能让包装器不再是值类型，而本模块的 `using` 形态正是靠值类型做到零 GC 的。故只保护**同一个存储位置**，边界如实写进 XML doc 与使用方 README。无重开判据。
- 暂不做：**集合池没有 `RemovePool` 这类移除入口**（2026-10-17 · 第二轮审计改判）——`PoolManager` 侧有，四个集合池只有 `Configure` / `GetPool` / `ClearAll`；`Configure` 在无活跃实例时重建，已覆盖「必须重建池」的主场景。重开判据：出现需要丢弃集合池注册的使用方，或 POOL-002 的会话复位落地需要重置入口。
- 已做：**池的静态状态补 `ResetForNewSession`**（2026-10-17 · `e6166c1`，POOL-002）——`SubsystemRegistration` 清空 `_pools`/`_configs`（只丢引用不 Dispose），`CollectionPoolManager` 重建四个集合池的内部池；已登记 `AutoInitTests` 族清单。用户裁定：框架静态态复位，不保留跨会话池。
- 不做：**`CollectionPoolManager.Register` 不收窄为 internal**（2026-10-18 · 裁定）——保持公开（集合池静态构造要用），XML/README 明示「仅供集合池静态构造、不要手动使用」（POOL-010）。无重开判据。

