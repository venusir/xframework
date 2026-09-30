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
