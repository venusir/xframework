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
└── ReadOnlyReactiveProperty.cs   # 只读派生属性 + Select 映射扩展
```

## 沿革与已否决形状

- **事件流引擎原先物理上住在 `XMessage.Internal` 里**，本模块直接 `using` 它取 `EventStream<T>`——合计 5 个跨模块文件引用同一处内部命名空间，而全框架共用一个 asmdef、`internal` 不构成编译边界，既没有编译器约束、也没有成文约定可依。2026-09-27 把引擎下沉为**独立模块** `XFramework.XEvent`（公开接口 + 静态工厂 + internal 实现，照 Pipeline 先例），这条依赖随之变成**公开、单向、可自查**——`Tests/Editor/Architecture/ModuleBoundaryTests` 会拦住任何模块对别的模块 `Internal` 的新引用。
- **订阅泄漏是本仓历史上反复出现的一类**（UI / Reactive / Settings 的 README 都专设排查节）；若这类事故再出现，值得单独立项补一个与 `UIStateWindow` 同级的编辑器窗口——即 R3 `ObservableTracker` 那种「列出未释放订阅**及其创建调用栈**」的工具。

## 已评估未采纳与未决

- **相等比较器不可注入**（未决，2026-10-02 全面清点时记下）：`ReactiveProperty<T>` 的去重写死 `EqualityComparer<T>.Default`，没有 `IEqualityComparer<T>` 构造重载——浮点容差、大小写不敏感等自定义相等语义做不到（`ReadOnlyReactiveProperty` 的 `Select` 派生同理）。属「一次注入」级别的低成本出口，本轮未做，可再拾。
