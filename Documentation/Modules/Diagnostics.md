# Diagnostics 维护记录

> 面向维护者与下一轮审计者。使用说明在 `Assets/XFramework/Runtime/Diagnostics/README.md`，本文件不重复。

## 文件结构

```
Runtime/Diagnostics/          契约与注册表（随包发布，零 GUI 依赖）
├── IDiagnosticPanel.cs       页签契约：Title / Order / Collect
├── IDiagnosticReport.cs      采集器（写侧）契约
├── DiagnosticReport.cs       采集器默认实现（可 Clear 复用）
├── DiagnosticItem.cs         报告条目 readonly struct
├── DiagnosticItemKind.cs     条目种类枚举（2026-10-16 补录——从 DiagnosticItem.cs 拆出后漏在本树之外）
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
└── Panels/                   框架页签（每个面板一个文件）

Tests/Runtime/Diagnostics/    注册表、报告、文本渲染
Tests/Editor/Diagnostics/     列宽分配纯函数、窗口内核
```

## 沿革与已否决形状

**2026-10-02：诊断窗口立项并建成**（用户裁定四项：Editor 窗口 + Runtime 数据契约 / 开放第三方注册 / 补齐关键缺口 / 合并删除旧 `UIStateWindow`；后续追加两条：回读走门面专属成员而非主接口、渲染模块化三层）。

- **先出契约、后出渲染与页签**：契约（Runtime 侧纯数据）必须能被文本渲染、编辑器渲染与将来的运行时覆盖层同时消费，因此 IMGUI 一律留在 Editor 程序集。
- **旧 `UIStateWindow` 合并删除**（菜单 `Tools/XFramework/UI State` 一并移除）：UI 页签的信息 ⊇ 旧窗口（面板列表 / 状态快照 / `DumpState` 全文 + 层级表），保留两个窗口看同一份数据违反「一处只写一份真相」。
- **唯一破坏性变更**：`IUIManager` +2（`IsLayerVisible` / `IsLayerInteractive`）——`Documentation/Modules/UI.md` 里早已记为「将来若要公开走完整的接口 + 转发 + 完备性测试三步」，本轮按这三步走完。
- **渲染层是公开面**：第三方既能整份复用 `DiagnosticReportView`，也能用单项原语混搭自绘，或实现 `IDiagnosticPanelView` 整页自绘（可选能力接口 + `is` 探测）。

## 决策台账

- 不做：**自定义条目类型 + 渲染器注册表**（2026-10-02 · 建成轮）——自绘接口配公开原语已覆盖自定义视觉的绝大多数需求；给纯数据契约加 `object` 载荷会让文本渲染与运行时覆盖层对这类条目只能降级。无重开判据。
- 不做：**公开非泛型 `IPool` 基接口**（2026-10-02 · 建成轮）——会让显式实现 `IPool<T>` 的第三方编译不过（CS0535）；`IUntypedPool` 内部缝已够用。**本条归口已迁至 [`Pool.md`](Pool.md) 的「决策台账」**（按落点表「决策归模块」，此处只留指针）。无重开判据。
- 不做：**给 `IConfigManager` / `ISettingsManager<T>` / `ILogManager` / `ILocalizationManager` / `IAudioManager` 加回读成员**（2026-10-02 · 建成轮）——门面专属成员 + 内置实现探测可完全替代，且第三方实现零影响；代价是注入自定义实现时回读为空（各模块 README 已写明）。无重开判据。
- 不做：**`LogManager.CategoryCount`**（2026-10-02 · 建成轮）——`LogCategory.RegisteredCount` 已公开提供同一个数，两个名字指向同一个值就是第二份真相。无重开判据。
- 不做：**注册表的自动初始化成员——不要补**（2026-10-02 · 建成轮）——注册表不订阅 Unity 事件、不做会话复位、不注入 PlayerLoop，因此 `AutoInitTests` 族清单零改动。这是设计结论，不是遗漏。无重开判据。
- 已做：**主线程断言维持三份内联副本、不抽公共防线；诊断模块不加第四份**（2026-10-15 · `Ledger.md`「主线程断言三份副本——不抽公共防线」条）——本条原记诊断模块**不加第四份内联副本**的立场，随框架级裁定一并结案（它是诊断设施，不是断言宿主）。
- 未做：**`UpdateNodeInfo.TimeMode` 在 `FixedUpdate` 下无意义**（2026-10-02 · 建成轮）——当前保留字段并文档说明「判断以 `Timing` 为准」。
- 未做：**窗口焦点/跨会话状态**（2026-10-02 · 建成轮）——本仓零 `EditorPrefs` 使用，选中页签与筛选不跨会话记忆。
- 未做：**Event / Reactive 订阅注册表、Asset 句柄表、Input 启用 ActionMap 列表、Localization 缺失项计数、Log 最近日志环形缓冲、Save 槽位列表、Message 键值通道 key 枚举、运行时覆盖层本体**（2026-10-02 · 建成轮）——本轮不做，各有理由，见 CHANGELOG 与 `../Ledger.md`。
- 已做：**文档改为「按实例幂等」，去重语义不变**（2026-10-17 · `eebbfed`，DIAG-001）——管理器 XML、模块 README 与随包示例统一为「静态只读实例登记（或退出时 `Unregister`）」；用户裁定不改按标题去重的行为。
- 已做：**`Register` 的标题读取异常隔离**（2026-10-20 · R3-DIAG-001，本轮修复）——坏 getter 会让登记整体抛出，幂等判定与冲突扫描一并失效，与渲染侧 `SafeTitle` 不对称。修法：登记侧新增私有 `SafeTitle`（异常记 Error、以「(类型名)」兜底），两处读取都经它；回归 `DiagnosticsRegistryTests` 两例（改前 getter 异常外泄）。
- 已做：**`Clear()` 抹框架页签且包外无恢复入口已成文**（2026-10-19 · R3-DIAG-002，文档订正）——`Clear` 的 XML 写明「包外宿主无法恢复框架页签，需要时自行保存并重登记或不要调用」；**不新增公开恢复 API**（无使用方，公开面新增须单独确认）。
- 已做：**`IDiagnosticPanel` 错误提示契约句与 README 计数订正**（2026-10-19 · R3-DIAG-003，文档订正）——「只显示一条错误提示」实际写提示 + 堆栈两条；README 的「十个文件」固定计数已删。

