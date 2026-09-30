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
