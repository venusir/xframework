# Diagnostics 维护记录

> 面向维护者与下一轮审计者。使用说明在 `Assets/XFramework/Runtime/Diagnostics/README.md`，本文件不重复。

## 文件结构

```
Runtime/Diagnostics/          契约与注册表（随包发布，零 GUI 依赖）
├── IDiagnosticPanel.cs       页签契约：Title / Order / Collect
├── IDiagnosticReport.cs      采集器（写侧）契约
├── DiagnosticReport.cs       采集器默认实现（可 Clear 复用）
├── DiagnosticItem.cs         报告条目 readonly struct + 种类枚举
├── DiagnosticTable.cs        表格（表头固定、行只增不改）
├── DiagnosticLevel.cs        提示级别
├── DiagnosticOrders.cs       Order 建议取值带
├── DiagnosticReportFormatter.cs  报告 → 纯文本（复制按钮与单测都走它）
└── DiagnosticsManager.cs     登记表（静态门面，无接口）

Editor/Diagnostics/           窗口与渲染（不发布）
├── DiagnosticsWindow.cs      窗口壳：生命周期 / 转发 Model / GUILayout
├── DiagnosticsWindowModel.cs 可测内核：节流、选中、重载、异常隔离、自绘探测
├── FrameworkPanels.cs        框架自带页签的唯一登记点
├── Rendering/                公开渲染层：DiagnosticReportView / DiagnosticItemDrawer /
│                             IDiagnosticPanelView / DiagnosticTableLayout（internal 纯函数）
└── Panels/                   18 个框架页签

Tests/Runtime/Diagnostics/    注册表、报告、文本渲染
Tests/Editor/Diagnostics/     列宽分配纯函数、窗口内核
```

## 沿革与已否决形状

**2026-10-02：诊断窗口立项并建成**（用户裁定四项：Editor 窗口 + Runtime 数据契约 / 开放第三方注册 / 补齐关键缺口 / 合并删除旧 `UIStateWindow`；后续追加两条：回读走门面专属成员而非主接口、渲染模块化三层）。

- **先出契约、后出渲染与页签**：契约（Runtime 侧纯数据）必须能被文本渲染、编辑器渲染与将来的运行时覆盖层同时消费，因此 IMGUI 一律留在 Editor 程序集。
- **旧 `UIStateWindow` 合并删除**（菜单 `Tools/XFramework/UI State` 一并移除）：UI 页签的信息 ⊇ 旧窗口（面板列表 / 状态快照 / `DumpState` 全文 + 层级表），保留两个窗口看同一份数据违反「一处只写一份真相」。
- **唯一破坏性变更**：`IUIManager` +2（`IsLayerVisible` / `IsLayerInteractive`）——`Documentation/Modules/UI.md` 里早已记为「将来若要公开走完整的接口 + 转发 + 完备性测试三步」，本轮按这三步走完。
- **渲染层是公开面**：第三方既能整份复用 `DiagnosticReportView`，也能用单项原语混搭自绘，或实现 `IDiagnosticPanelView` 整页自绘（可选能力接口 + `is` 探测）。

## 已评估未采纳与未决

- **自定义条目类型 + 渲染器注册表**（未采纳）：自绘接口配公开原语已覆盖自定义视觉的绝大多数需求；给纯数据契约加 `object` 载荷会让文本渲染与运行时覆盖层对这类条目只能降级。
- **公开非泛型 `IPool` 基接口**（未采纳）：会让显式实现 `IPool<T>` 的第三方编译不过（CS0535）；`IUntypedPool` 内部缝已够用。
- **给 `IConfigManager` / `ISettingsManager<T>` / `ILogManager` / `ILocalizationManager` / `IAudioManager` 加回读成员**（未采纳）：门面专属成员 + 内置实现探测可完全替代，且第三方实现零影响；代价是注入自定义实现时回读为空（各模块 README 已写明）。
- **`LogManager.CategoryCount`**（未采纳）：`LogCategory.RegisteredCount` 已公开提供同一个数，两个名字指向同一个值就是第二份真相。
- **注册表的自动初始化成员**（未采纳，且**不要补**）：注册表不订阅 Unity 事件、不做会话复位、不注入 PlayerLoop，因此 `AutoInitTests` 族清单零改动。这是设计结论，不是遗漏。
- **主线程断言**（未决）：`Roadmap.md` §五 已把「主线程断言抽公共防线」记为未决；诊断模块只写文档、不加第四份内联副本。
- **`UpdateNodeInfo.TimeMode` 在 `FixedUpdate` 下无意义**（未决）：当前保留字段并文档说明「判断以 `Timing` 为准」。
- **窗口焦点/跨会话状态**（未决）：本仓零 `EditorPrefs` 使用，选中页签与筛选不跨会话记忆。
- **Event / Reactive 订阅注册表、Asset 句柄表、Input 启用 ActionMap 列表、Localization 缺失项计数、Log 最近日志环形缓冲、Save 槽位列表、Message 键值通道 key 枚举、运行时覆盖层本体**（本轮不做，各有理由，见 CHANGELOG 与 `Roadmap.md`）。
