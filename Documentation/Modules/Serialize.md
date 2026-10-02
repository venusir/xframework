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

## 已评估未采纳与未决

- **内置注册的静默覆盖**（未决，2026-10-02 全面清点时记下）：`Initialize` 幂等但会把内置两个序列化器注册进字典，于是「使用方在 `Initialize`（或 AutoInit）之前自行注册 `"json"`」会被**静默覆盖**——`Register` 的同名覆盖也是静默的。两种改法（覆盖时告警 / 内置注册可关）都是一行级，本轮未做，可再拾。注意覆盖 `"json"` 会同时改变 Save 与 Data 的落盘格式（两者都走 `Serializer.Default`）。
