# Serialize —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Serialize/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
XSerialize/
├── ISerializer.cs          # 序列化器接口（同步，字节数组输出）
├── Serializer.cs           # 静态门面，管理已注册的序列化器（字典索引）
├── JsonSerializer.cs       # 内置默认实现（封装 Unity JsonUtility）
├── NewtonsoftSerializer.cs # 内置实现（封装 Newtonsoft.Json，支持 Dictionary / 多态）
└── README.md               # 本文件
```

## 决策台账

- 已做：**内置注册不再静默覆盖使用方的同名序列化器**（2026-10-13 · 回归用例 `SerializerTests` 3 条）——`Initialize` 现经 `RegisterBuiltin` 注册内置项，**同名已存在时保留使用方的**并打一条 `[Serialize]` 告警（`Register` 的同名覆盖不变，那是显式动作；另半条提醒仍有效：覆盖 `"json"` 会同时改变 Save 与 Data 的落盘格式，两者都走 `Serializer.Default`）。该行为在 2026-10-02 全面清点时被记为未决、随后落地，属「清点记下的未决被随后落地、条目没跟」的又一例（同款教训见 `Modules/Reactive.md` 的相等比较器条目）。
- 不做：**不为 Serialize 补 PlayMode 侧用例**（2026-10-13 · 覆盖失衡盘点的处置）——全同步纯 CPU（`ISerializer` 就是同步字节数组契约），无帧边界、无 MonoBehaviour、无跨会话静态态；唯一带运行时特性的 `AutoInit` 已由 `AutoInitTests` 族守卫锁死（特性 + load type）。再写一条「跑起来后注册表非空」反而**受用例顺序污染**（任何先行 fixture 调过 `Shutdown()` 就红），不是有效证据。无重开判据。
