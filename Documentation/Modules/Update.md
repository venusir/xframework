# Update —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Update/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Update/
├── IUpdateable.cs                # 契约：IUpdateLifecycle / IUpdateable / ILateUpdateable /
│                                 #       IFixedUpdateable / UpdateTier
├── UpdateClock.cs                # 时间基：UpdateClock（time + unscaledTime + isPaused）/ UpdateTimeMode
├── UpdateScheduler.cs            # 纯调度逻辑（档位分桶 + 时间切片 + 双时间轴），internal
└── UpdateManager.cs              # 静态门面（含 PlayerLoop 注入驱动）
```

## 沿革与已否决形状

- **「组」为什么不做成框架 API**：组一旦进入框架，紧接着就要回答「组的生命周期」「组内嵌套」「组之间的顺序」——那就是生命周期树，正是本框架在 2026-09-18 整块删除的节点系统所承担的东西（见 `CHANGELOG.md` 的「删除节点系统」，其中「一套 GamePlay 架构」「一个服务定位器」都被判定为不该由基础框架管）。
