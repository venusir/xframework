# UI 制作（面向策划）—— 评估与方案（维护侧）

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> 使用方文档在 `Assets/XFramework/Runtime/UI/README.md`——用法、已知限制、设计取舍一律留在那里，本文件不复述。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> **状态**：评估已落盘，实施待裁（见 §十一）。模块台账条目在 `Documentation/Modules/UI.md` 的「决策台账」节。

---

## 〇、核对状态

| 标记 | 含义 |
|---|---|
| `[源码核对]` | 读当前工作树源码确认，路径与行号可作为证据 |
| `[推理]` | 基于源码结构与既有文档推导，**未实测** |
| `[未实测]` | 需要探针，见 §十 |

本轮的 `[源码核对]` 覆盖：面板生命周期与工厂、打开链路与去重/登记、约定绑定与精确绑定、预制体校验器九条规则、Editor 程序集与公开 API 基线、Editor 内部可见性、扩展包先例（Luban / FMOD）、Config 与 Settings 的定位、`PreloadAsync` 与消息/控制器的载荷。

**一个前提**：本文所有行号取自 2026-11-08 的工作树；源码改动后**先重新核对再引用**。

---

## 一、结论摘要

**缺口一句话**：策划今天能按规范搭预制体，但做不出「能跑的面板」——每个面板必须有一个 `UIPanelBase` 子类挂在预制体根节点上，打开地址还要程序手写。

**已有的一半**：约定绑定（`txt_/img_/btn_/sld_/tgl_`）与编辑期预制体校验器（`UI-PREFAB-001`–`009`）已经覆盖「预制体拼装与命名」；缺的是**面板脚本**与**打开调用**。

**硬约束**：`UIManagerImpl` 按**具体类型**登记面板，同类型再注册会记 Error 并回滚旧实例（§2.3）。因此单一通用面板类型**同一时刻只能开一个**——即使改用 `OpenAsync<UIPanelBase>` 也绕不过 `RegisterPanel` 的同类型回滚。

**推荐路线**：新建独立可选扩展包 `com.venusy609.xframework.ui.authoring`，核心包零改动，分三阶段：

1. **阶段 1**：文档 + 受限预制体模板（Prefab Variant）；需重开 2026-11-08「暂不做：预制体模板」一项。
2. **阶段 2**：`UIDeclarativePanel` 声明式面板 + 封闭的 UI 级动作集 + 自定义动作事件；覆盖静态展示页与简单交互。
3. **阶段 3**：数据源接缝（程序只写一个 provider）+ 编辑器校验完善；覆盖动态数据绑定。

**明确不做（用户本轮确认维持）**：面板脚本/地址常量代码生成、可视化 UI 设计器、保存/构建门禁。原因与代价见 §五——不能只写「不做」两个字。

**待裁定**：§十一 的四个开放问题（单根 vs 核心身份键、是否重开模板、定义资产形态、自定义动作出口）。

---

## 二、现状与证据

### 2.1 一个新面板的六道工序 `[源码核对]`

| # | 工序 | 今天谁做 | 已有支撑 | 违反后的表现 |
|---|---|---|---|---|
| 1 | 写 `class XxxPanel : UIPanelBase`，实现 `OnOpen` / `OnClose` | 程序 | 无 | 编译期 |
| 2 | 在 Unity 里搭预制体，把面板脚本挂在**根节点** | 策划/程序 | 校验器 `UI-PREFAB-001`（根组件）、`002`（Binding 位置） | 工厂对根 `GetComponent<T>()` 失败：记 Error、销毁实例、打开返回 null（`AssetPanelFactory.cs:19-42`） |
| 3 | 设置根 `RectTransform`（全屏拉伸 / 弹窗尺寸） | 策划 | 校验器 `006`（默认 100×100）、`007`（非单位变换） | 布局错位；池化复用后外观不一致（根变换被重置） |
| 4 | 子节点按 `txt_/img_/btn_/sld_/tgl_` 命名 | 策划 | 校验器 `003`（深度）、`004`（类型）、`005`（重名） | 约定绑定静默失效，只在编辑器告警（`UIPanelBinding.cs:153-218`） |
| 5 | 预制体进 YooAsset **主资源采集** | 策划/程序 | 校验器 `008` | 运行时按地址加载不到 |
| 6 | 调用点写 `UIManager.OpenAsync<T>("address", layer)` | 程序 | **无** | 地址拼错只在打开时暴露；加载失败/打开错面板 |

**结论**：第 1 道与第 6 道是策划无法独立跨越的门槛；第 2–5 道已经有工具，但工具是**手动菜单**，不会自动触发（`Runtime/UI/README.md:344`）。

### 2.2 已有的一半：能力边界 `[源码核对]`

**约定绑定（`UIPanelBinding`）**

- 子节点前缀 → 组件：`txt_` → UGUI `Text`、`img_` → `Image`、`btn_` → `Button`、`sld_` → `Slider`、`tgl_` → `Toggle`；最多 5 层深度；同名只保留第一个（`UIPanelBinding.cs:254-318`）。
- **不是自动全绑**：每个属性必须显式调一次 `Binding.BindByConvention("属性名", vm.属性)` 或 `Binding.BindClick("名字", 回调)`；`BindViewModel` 只调 `OnBound()` 并缓存组件（`Runtime/UI/README.md:197-208`）。
- `txt_` **只认 UGUI `Text`，不认 `TMP_Text`**；要 TMP 或格式化走 `UIBinder` 精确绑定（README 3.4 节）。
- 类型不匹配等于没绑：`img_` 只认 `Sprite` / `Color` / `float`，`sld_` 只认 `float`，`tgl_` 只认 `bool`（`UIPanelBinding.cs:153-218`）。

**精确绑定（`UIBinder`）**

- 支持 TMP、本地化、双向：`BindToText`（`TMP_Text`）、`BindToLocalizedText`（`UIBinder.cs:107-133`，订阅自动登记到最近 `UIViewBase.Track`）、`BindTwoWay`（`Slider`/`Toggle`，`:156-172`）、`BindToSlider` / `BindToFillAmount` / `BindToSprite` / `BindToToggle` / `BindToActive` / `BindToClick`。
- 本地化绑定**只有 TMP_Text 重载**；UGUI `Text` 需要项目自己用公开的 `LocalizationManager.Get/Subscribe`（`LocalizationManager.cs:269,349`）。

**预制体校验器**

- 菜单 `Tools/XFramework/UI/Validate UI Prefabs`（`Editor/UI/UIPrefabValidatorWindow.cs:36-42`）；规则引擎对内存 `GameObject` 只读检查，纯规则 `UIPrefabValidator.cs`，扫描器 `UIPrefabScanner.cs` 是唯一用 `AssetDatabase` 的普通代码。
- 九条规则：根组件、Binding 位置、约定深度/类型/重名、根 RectTransform、池化变换、YooAsset 采集、Tip 文本（`UIPrefabValidationIssue.cs:22-50`）。
- 边界：只读、不修改预制体；不在保存或构建时自动触发；`UIBinder` 手工绑定与代码里的地址字符串**不在检查范围**（README:344）。测试 36 例（`Tests/Editor/UI/`）。

**结论**：预制体拼装已经「有尺子」，但尺子是手动量；面板脚本与调用点仍完全靠人。

### 2.3 「类型即身份」——推荐方案必须正面处理的硬约束 `[源码核对]`

核心把面板身份绑定在**具体类型**上，而不是「类型 + 实例标识」：

1. **登记**：`RegisterPanel(panel.GetType(), panel)` 写入 `_activePanels`（`UIManagerImpl.cs:698, 1996-2013`）。同一具体类型已存在时，`RegisterPanel` 记一条 **Error** 并 `RollbackPanel(oldPanel)`——这是「同类型单实例」不变量被破坏时的兜底，不是正常去重路径。
2. **查找**：`FindOpenPanelForRequest(requestedType, assetPath)` 先查 `_activePanels[requestedType]` 精确命中，再回落到按地址查找（`UIManagerImpl.cs:1966-1972`）。对**具体类型** `UIDeclarativePanel`，第一次命中后直接 `RefocusOpenPanel` 并返回——**打开第二个不同地址会静默返回第一个**（`OpenAsync` 的早退在 `:616-621`）。`PushAsync` 在 `:923-928` 先走同一去重，因此**压栈第二个声明式地址同样会静默重聚焦第一个**，不会打开新面板。
3. **预加载**：`PreloadAsync<T>` 按 `typeof(T)` 记账，同类型第二次直接早退（`UIManagerImpl.cs:1428-1440`）；一份 `UIDeclarativePanel` 只能预热一个地址，多个地址会互相覆盖。
4. **消息**：`PanelOpenedMessage` / `PanelClosedMessage` 只携带 `PanelType`（`PanelOpenedMessage.cs:10-25`、`PanelClosedMessage.cs:10-25`）；订阅方无法区分两个不同地址的声明式面板。
5. **控制器**：`OnAfterCloseAsync(Type, CancellationToken)` 只给类型（`IUIController.cs:65`）；`OnBeforeCloseAsync` 有面板实例，可以读 `AssetPath` 自己记（`IUIController.cs:56-57`）。

**后果**：单一通用面板类型不能同时打开两个不同预制体；`Push`/`Open` 第二个地址都只会重聚焦现有的那个。这不是扩展包能绕开的，只能三选一：

- **A. 接受单根 + 预制体内子页**（推荐）：声明式面板同一时刻只有一个；多页用 `SetActive` / `SelectTab` 在同一个预制体里切换；需要并存的界面继续用 C# 面板。
- **B. 核心加可选身份键**：`(Type, identity)` 登记，未实现身份接口的面板行为不变；需改核心行为与公开 API 基线，属「重开核心零改动」。
- **C. 每个预制体一个具体类型**：回到代码生成或手写空壳脚本——正是本方案想消掉的第 1 道工序。

### 2.4 数据与配置能力 `[源码核对]`

- **Config 能存「UI 配置」，不能解释行为**：`IConfigRow<TKey>` + `ConfigTable<T>` + `ScriptableObjectTable<TRow,TKey>` + `ConfigManifest` 足以承载「一行 = 一条 UI 规则」；但仓内**没有任何模块消费 `XFramework.XConfig`**（`Runtime/` 排除 Config 自身后零引用），也没有「一行配置 → 一个行为」的执行器。
- **Settings 不是策划配置**：纯 POCO + `SettingRef<T,TField>` 句柄，没有 ScriptableObject、没有 `SetInstance`；可借鉴的 Inspector 资产先例是 `InputSystemOptionsAsset`（`Runtime/Input/Default/InputSystemOptionsAsset.cs:12-23`）与 FMOD 的 `FmodAudioSettings`。

**结论**：如果声明式面板需要一个「定义资产」，ScriptableObject 是仓内已有先例的形态；Config 表更适合未来「从表里批量生成定义」，而不是第一版的运行时载体。

---

## 三、四档需求矩阵

| 档位 | 策划做什么 | 程序做什么 | 今天缺什么 | 本方案覆盖 |
|---|---|---|---|---|
| **A. 只搭预制体** | 按规范搭预制体、命名节点、进采集 | 写面板类 + 打开调用 | 模板与交接规范；校验器手动跑 | 阶段 1（模板 + 文档）；阶段 2 起可换成声明式组件 |
| **B. 静态展示页** | 标题/文本/图片/按钮关闭；不需要 C# | 一次打开调用（可由别的 C# 流程代劳） | **没有「不含逻辑的面板类」**；关闭按钮也要写代码 | 阶段 2：`UIDeclarativePanel` + 文本绑定 + `CloseSelf` 动作 |
| **C. 简单交互** | 按钮跳转、开关、页签、发消息/弹 Tip | 最多写一个全局动作分发（若用 `Custom`） | 没有动作表达层；每个按钮都要代码回调 | 阶段 2：内置 UI 级动作 + `Custom(id)` 事件出口 |
| **D. 动态数据绑定** | 在定义里配「节点 ← 数据键」 | 写**一个** `IUIDataProvider` 并注册 | 没有「按数据键绑定」的声明式通道；每页一个 ViewModel | 阶段 3：`IUIDataProvider` + 绑定表 + 双向 |

**明确不在射程内的**（与核心 README 的既有裁定一致）：列表虚拟化 / 循环列表、完整的新手引导、红点树、拖拽与复杂手势、UI 特效层——这些是领域模型或第三方组件，框架不内建（`Documentation/Modules/UI.md:157,183,185`；`Ledger.md:95,105`）。

---

## 四、约束与既有裁定

### 4.1 框架原则

- **不预设 GamePlay 架构**：框架只提供正交基础设施与编排原语，不为使用方决定实体模型、生命周期树、时间模型或状态管理（`CLAUDE.md`「项目定位」）。
- **框架代码供第三方运行时使用**：控制 GC、不得每帧分配；公开 API 必须带 CancellationToken 的异步约定；模块边界与包边界有机械守卫。

### 4.2 2026-11-08 的三条裁定 `[源码核对]`

原文见 `Documentation/Modules/UI.md:130-136`：

- **暂不做**：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成——「本轮只做校验器 + 使用方文档；制作仍由项目在 Unity 内完成，避免框架预设 UI 结构、模板与命名/地址生成策略」。重开判据：出现多个项目重复手写同一套面板模板且差异可参数化；或地址漂移成为实际事故源。
- **不做**：可视化 UI 设计器——「等价于第二套 UI 栈；第三方 UI 方案评估已给出结构性理由（`IUIManager` 的编排原语按 UGUI 对象实现，没有可替换后端的接缝），不重开」。
- **暂不做**：保存即校验 / 构建前门禁 / 源码地址字符串扫描——「保存即校验会在每次导入时加载全量预制体并可能触发导入循环；构建门禁替不使用 UI 的项目决定构建策略；源码地址多来自变量与配置，静态扫描误报率高」。重开判据：团队要求「无效预制体不得入库/出包」且已有稳定 CI 或提交钩子；或地址常量生成落地使地址可静态解析。

### 4.3 FairyGUI 评估的结论与「受限模板」建议

`Documentation/FairyGUI.md` 的总结论：**不接成 `IUIManager` 的后端，也不做平级管理层包**。理由不是工作量：

- 「策划脱离 Unity 编辑 UI」只有 FairyGUI 编辑器给得了；「策划脱离游戏数据逻辑」与 FairyGUI 无关，本仓 `UIBinder` + `BindByConvention` 现在就有（§2.4）。
- §2.4 明确提出替代解法：**「给策划受限的预制体模板（Prefab Variant 固定结构 + 只暴露该改的字段）」**。
- §五 的重开判据两条（上游输入分支 + Unity 6 实测通过；出现真实使用方明确要用 FairyGUI 作其 UI 栈）——与本方案不冲突。

### 4.4 扩展包先例与门禁射程

- `Assets/XFramework.Luban/`：`package.json` 名 `com.venusy609.xframework.luban`，Runtime asmdef 引用 `Venusy609.Xframework` + `UniTask`，Editor asmdef 只引用扩展 Runtime，`Tests/Runtime` 与 `Tests/Editor` 各有 asmdef；自带 README/CHANGELOG/守卫；**不在核心六道门禁射程**（核心门禁只扫 `Assets/XFramework`），由全量 `run-tests.ps1` 覆盖测试。
- `Assets/XFramework.Fmod/` 同型（条件集成程序集用 `versionDefines` + `defineConstraints`）。
- **扩展包访问核心 `internal` 需要核心开 `InternalsVisibleTo`（属核心改动）**：Runtime 当前对 `Venusy609.Xframework.Tests`、`Venusy609.Xframework.Editor.Tests`、`Venusy609.Xframework.Fmod`、`Venusy609.Xframework.Fmod.Tests` 开放（`Runtime/AssemblyInfo.cs:3-9`）；Editor 只对 `Venusy609.Xframework.Editor.Tests` 开放（`Editor/AssemblyInfo.cs:5`）。新扩展包若想复用核心 Editor 校验器，必须改核心 `AssemblyInfo.cs`，与「核心零改动」冲突——这是 §7.5 选择「重复一小份规则引擎」的直接原因。

### 4.5 文档分层与包边界

- 本文件是维护向评估，**不随包发布**；扩展包 README 里不得出现 `Documentation/` 路径（包内不指向包外，`PackageBoundaryTests` 守卫）。
- 模块台账的唯一格式在 `Documentation/Modules/README.md`；状态词只有 `已做/未做/待裁/暂不做/不做/已推翻`。

---

## 五、明确不做的三条：原因与代价

> 三条均已在 2026-11-08 落盘；本轮用户确认**维持现状**。本节补的是原因、代价与重开判据，供下一轮审计直接引用。

### 5.1 面板脚本 / 地址常量代码生成（`暂不做`）

**是什么**：编辑器菜单从预制体生成 `XxxPanel.cs` 骨架；把 YooAsset 地址生成为常量类，调用点不再写裸字符串。

**不做的原因**：

1. **它优化的是程序，不是策划**。生成的仍是需要程序填 `OnOpen/OnClose`、需要程序维护的 C# 类——对「策划零代码做 UI」没有推进。这是与本议题最根本的错位。
2. **结构由项目定**。生成器必然固定命名、目录、命名空间、程序集；而裁定的原话就是「避免框架预设 UI 结构、模板与命名/地址生成策略」。
3. **生成器本身是长期负债**。Luban 扩展包已经演示过完整代价：auto-generated 头、manifest、备份、暂存→提交→回滚、拒绝覆盖用户文件、改名后的陈旧文件清理——写盘协议比生成逻辑本身还长。UI 生成器还要处理预制体改名/移动/删除。
4. **地址常量的漂移没有解**。地址的真相在 YooAsset 采集配置里；常量文件过期后编译照样通过、运行时才炸。要可靠就得有保存/构建钩子或采集配置监听——而 §5.3 也裁定了不做。
5. **声明式面板让「脚本生成」失去大部分意义**：没有 per-panel 脚本可生成，只剩地址常量一小块。

**不做的代价**：

- 每个项目重复手写同一套面板骨架与地址字符串；地址拼错只在打开时暴露（校验器不扫代码字符串，README:344 明确写了）。
- 地址改名靠全局搜索替换，漏改处编译通过、运行时加载失败。
- 没有编译期保证「这个地址真的在采集包里」。
- 面板多了以后人工成本线性增长——**这正是台账里的重开判据**：「多个项目重复手写同一套面板模板且差异可参数化」或「地址漂移成为实际事故源」。

### 5.2 可视化 UI 设计器（`不做`，结构性）

**是什么**：WYSIWYG 的 UI 编辑器，包含「策划脱离 Unity 制作 UI」这一类能力。

**不做的原因**：

1. **它不是「多一个工具」，而是第二套 UI 栈**。`IUIManager` 的每个编排原语（加载面板、排序、遮罩、栈、HUD、绑定）都是按 UGUI 对象实现的，没有可替换后端接缝（FairyGUI.md §2.1–2.2）。可视化设计器要么生成 UGUI 预制体（等于重写 Unity 编辑器的一大块），要么自带运行时表示（第二套栈，重复实现加载/池化/排序/输入/本地化）。
2. **它是一个完整产品**：撤销重做、多选、锚点布局、预制体嵌套、资源引用、本地化预览、版本控制 diff——每一项都是产品级工作量，且随 Unity 序列化格式升级持续维护。
3. **与仓库的 review 形态冲突**：本仓 Force Text、靠 diff 评审；自定义格式会丢掉 Unity 工具链、Prefab Variant、Undo 与多人协作基建（FairyGUI.md §2.4 也把 `.fui` 二进制包列为纯负债）。
4. **职责边界**：框架提供编排原语，不提供制作产品；这与「不预设 GamePlay 架构」是同一条边界。

**不做的代价**：

- 策划必须在 Unity 里工作；`RectTransform` 锚点对非程序确实难学（FairyGUI.md 也承认这一点）。缓解手段只能是受限模板 + 命名约定 + 校验器——即本方案阶段 1。
- 策划无法脱离 Unity 迭代；美术/策划的改动必须走 Unity 工程。
- 真正需要「脱离 Unity」的项目只能自选 FairyGUI 等方案，代价是失去 `UIManager` 的栈/遮罩/排序体系（FairyGUI.md §3.2 的两条硬约束）。
- **没有重开判据**（结构性不做）；要重开必须提供新的结构性论据，例如出现真实项目明确要求脱离 Unity 且愿意承担第二套栈。

### 5.3 保存即校验 / 构建前门禁 / 源码地址扫描（`暂不做`）

**是什么**：保存或导入时自动跑校验、构建前拦截无效预制体、静态扫描代码里的地址字符串。

**不做的原因**：

1. **保存即校验**：每次导入要加载全量预制体，大工程每次保存都变慢；校验器读写 `AssetDatabase` 还可能触发导入循环。
2. **构建门禁**：替不使用 UI 的项目决定构建策略；框架包不该往第三方构建管线里加钩子。仓库至今零 `IPreprocessBuildWithReport`。
3. **源码地址扫描**：地址来自变量、配置、序列化字段、拼接，静态扫描误报率高；误报会让团队直接关掉检查，反而更糟。
4. **门禁是流程强制，不是制作能力**：它不解决「策划做不出面板」，只解决「坏预制体别入库」——收件人不同。

**不做的代价**：

- 坏预制体可以入库：根组件缺失 → 运行时打开失败；绑定不在根 → 静默失效；地址拼错 → 运行时加载失败。校验器是**手动菜单**，靠自觉。
- **无法在 CI 里硬门禁**。当前校验器是 `internal`，第三方拿不到机器可读的结果 API，只能执行菜单（窗口是给人看的）——想在 CI 里卡「坏预制体」目前**没有官方出路**。这是三条里最实在的能力缺口；未来若补公开校验 API，属公开 API 变更，需单独立项评审。
- 问题暴露晚，策划/程序来回成本高。

**重开判据**（台账原文）：团队要求「无效预制体不得入库/出包」且已有稳定 CI 或提交钩子；或地址常量生成落地使地址可静态解析。

### 5.4 三条的共同点

它们都不是「制作能力」，而是**强制 / 生成 / 第二产品**类能力；而框架定位是正交基础设施。与「面向策划」目标的关系也不同：

| 条目 | 解决什么 | 与策划自主性的关系 |
|---|---|---|
| 代码生成 | 程序样板量 / 地址漂移 | **不解决** |
| 可视化设计器 | 制作体验 / 脱离 Unity | **真解决**，但代价是第二套产品/栈 |
| 保存/构建门禁 | 质量强制 | **不解决**，只减少坏件 |

---

## 六、候选方案对比

### 6.1 方案 0：维持现状 + 文档流程

- **形态**：在核心 UI README 增加「策划协作流程」：角色分工、预制体规范（复用校验器）、节点命名即绑定键、交接自检清单。不新增代码。
- **覆盖**：只覆盖档位 A。
- **代价**：零；不重开任何裁定。
- **结论**：必要但不充分——它解决「怎么交接」，不解决「策划做不出面板」。

### 6.2 方案 1：受限预制体模板（Prefab Variant）

- **形态**：固定根结构（`Canvas + GraphicRaycaster + 面板脚本`）的基础 Prefab，策划创建 Variant，只改子页与命名节点；模板随扩展包 `Samples~/Templates` 分发。
- **覆盖**：档位 A，并降低 B 的拼装门槛；**不覆盖** B 的「无 C#」与 C/D。
- **代价**：模板资产 + 文档；需重开「暂不做：预制体模板」。`Samples~` 不参与编译，模板中的脚本引用在跨项目导入时需要探针（P3）。
- **结论**：阶段 1 的内容，不是完整答案。

### 6.3 方案 2：声明式面板扩展包（推荐）

- **形态**：独立扩展包内提供单一 `UIDeclarativePanel : UIPanelBase` + `UIPanelDefinition`（ScriptableObject）+ 封闭 UI 级动作集 + 数据源接缝。策划在预制体上挂组件、在定义资产里配文本/按钮/数据键；程序只写一个 provider（阶段 3）或一次打开调用。
- **覆盖**：A（模板 + 组件）、B（文本 + CloseSelf）、C（动作 + Custom）、D（数据源）。
- **代价**：新包与长期维护；单根限制（§2.3）；动作词汇表的边界要守住；YooAsset 定义资产采集需探针（P2）。
- **与裁定冲突**：不冲突于「向导/脚本生成」；**需重开「预制体模板」一项**（阶段 1 发模板）。「不做可视化设计器」不冲突——它不是编辑器，是运行时面板。
- **结论**：唯一能以核心零改动覆盖四档的选项。

### 6.4 方案 3：创建向导 + 代码生成

- **形态**：菜单从预制体生成面板脚本、地址常量、目录/命名空间骨架。
- **覆盖**：档位 A（程序更快），**不覆盖** B/C/D——生成物仍是需要程序维护的 C# 面板。
- **代价**：重开「暂不做」；生成器写盘协议（Luban 先例）；命名/目录策略；陈旧文件清理。
- **结论**：它优化的是程序，不是策划。可作为方案 2 的**可选补充**（只生成地址常量或创建声明式面板模板），但不作为主方案。

### 6.5 方案 4：换栈（FairyGUI / UI Toolkit）

- **FairyGUI**：已在 `Documentation/FairyGUI.md` 结构性否决（不接后端 / 不做平级包）；重开判据两条，与本方案不冲突。
- **UI Toolkit**：仓内**从未评估**（全仓零命中）。它是 Unity 原生、有 UI Builder，但仍是与 UGUI 并行的渲染/文档栈；`IUIManager` 的编排原语按 UGUI 对象实现，接进来同样没有后端接缝。**本轮不建议开**；若用户认真考虑换栈，先跑一个 30–60 分钟探针（P4），再决定是否立项。
- **结论**：不推荐；不把它写成「已否决」——它是「未评估」，不是「已证明不行」。

### 6.6 对比表

| 维度 | 0 现状+文档 | 1 受限模板 | 2 声明式面板扩展包 | 3 向导+代码生成 | 4 换栈 |
|---|---|---|---|---|---|
| 覆盖档位 A | ✅ | ✅ | ✅ | ✅ | ✅ |
| 覆盖档位 B | ❌ | 部分（仍需 C#） | ✅ | ❌ | 需另立评估 |
| 覆盖档位 C | ❌ | ❌ | ✅ | ❌ | 需另立评估 |
| 覆盖档位 D | ❌ | ❌ | ✅（阶段 3） | ❌ | 需另立评估 |
| 核心包改动 | 无 | 无 | 无 | 无（但需扩展工具） | 结构性 |
| 与 2026-11-08 裁定冲突 | 无 | 需重开模板 | 需重开模板（阶段 1） | 需重开生成 | 需重开设计器 |
| 维护成本 | 极低 | 低 | 中（新包 + 测试 + 守卫） | 中高（写盘协议） | 极高（第二套栈） |
| 运行时开销 | 无 | 无 | 开面板时一次性绑定；无每帧 | 无 | 不可控 |
| 测试难度 | 无 | 低（校验器扫描） | 中（EditMode 纯逻辑 + PlayMode 生命周期） | 中高（生成物 golden） | 极高 |
| 公开 API 影响 | 无 | 无 | 扩展包自有基线 | 扩展包自有基线 | 核心 API 大改 |
| 包边界 | 不新增 | 扩展包资产 | 新扩展包 | 新扩展包 | 新栈 |

---

## 七、推荐方案详设

### 7.1 包形状（遵循 Luban 先例）

| 项 | 建议 |
|---|---|
| 目录 | `Assets/XFramework.UIAuthoring/` |
| `package.json` | `"name": "com.venusy609.xframework.ui.authoring"`；`displayName`: `XFramework UI Authoring`；`version`: `0.1.0`；`unity`: `6000.3`；`dependencies`: `{}`（UPM 不支持 Git URL；对核心包的依赖写进扩展包 README 的安装顺序） |
| Runtime 程序集 | `Venusy609.Xframework.UI.Authoring`；引用 `Venusy609.Xframework`、`UniTask`；命名空间 `XFramework.XUI.Authoring` |
| Editor 程序集 | `Venusy609.Xframework.UI.Authoring.Editor`；`includePlatforms: [Editor]`；引用扩展 Runtime（**不引用核心 Editor**） |
| 测试程序集 | `Venusy609.Xframework.UI.Authoring.Tests`（Runtime 测试）/ `.Editor.Tests`；各自 asmdef |
| 示例 | `Samples~/Templates`（阶段 1）、`Samples~/DeclarativeDemo`（阶段 2） |
| 文档 | 扩展包 `README.md` + `CHANGELOG.md`；**不指向 `Documentation/`** |
| 守卫 | 镜像 Luban：README/文件树、`.meta` 完整性、包边界、AutoInit 族清单、Runtime/Editor 公开 API 基线、依赖声明；不在核心六道门禁射程，全量 `run-tests.ps1` 覆盖测试 |

**核心包零改动**：不改 `Assets/XFramework/**`、不改核心公开 API 基线、不给核心 asmdef 加 `InternalsVisibleTo`。

### 7.2 运行时模型

**组件**

```csharp
public sealed class UIDeclarativePanel : UIPanelBase
{
    [SerializeField] private UIPanelDefinition _definition;
    // OnOpen：按定义绑定文本/动作/数据；OnClose：Track 统一释放；OnPoolRecycle：base 优先
}
```

- 面板类型只有一个，预制体可以有任意多个；每个预制体根节点挂它。
- `_definition` 为空时仍可打开：预制体本身就是静态内容，不绑任何东西（覆盖「只搭预制体」）。
- 节点查找：按名字递归、**深度上限 5**（与 `UIPanelBinding.CacheComponents` 对齐）；名字在子树内必须唯一；找不到 → 编辑器告警、运行期跳过该条，不抛异常。
- 生命周期：`OnOpen` 建绑定并登记到 `Track`；`OnClose` / `OnPoolRecycle` 由基类统一释放；`OnPoolRecycle` 必须 `base.OnPoolRecycle()`（核心契约，`UIPanelBase.cs:198-214`）。
- 每帧无工作：不做 `OnUpdate`；动作与绑定只在打开时发生。

**定义资产（`UIPanelDefinition : ScriptableObject`）**

```
Version: int                       // 序列化值只在末尾追加；动作/目标枚举显式编号
TextBindings: List<UITextBinding>  // nodeName, literal?, localizationKey?, useLocalization
ActionBindings: List<UIActionBinding> // buttonNodeName, List<UIAction>
TabGroups: List<UITabGroup>        // groupName, List<nodeName>
DataBindings: List<UIDataBinding>  // nodeName, kind, dataKey, twoWay, format?（阶段 3）
```

**SO vs 内联字段**：

| 维度 | ScriptableObject（推荐） | 组件内联 |
|---|---|---|
| 复用 | 多预制体共享一份定义 | 每预制体一份 |
| 校验 | 可独立打开、独立校验 | 必须连同预制体加载 |
| 预制体 diff | 干净（只引用资产） | 每次改配置都改预制体 YAML |
| 依赖 | 需要 YooAsset 采集/依赖加载（P2） | 随预制体走，无额外依赖 |
| 回退 | 探针失败时降级为内联字段 | — |

**文本**：`literal` 直接写；`localizationKey` 对 `TMP_Text` 走 `UIBinder.BindToLocalizedText`（自动 `Track`）；对 UGUI `Text` 用公开的 `LocalizationManager.Get/Subscribe` 自行订阅并 `Track`（`LocalizationManager.cs:269,349`）。TMP Essentials 未导入时的行为见 P5。

**图片**：静态图片直接烘焙在预制体里；运行期换图走阶段 3 的数据绑定（`Sprite`），不在文本定义里做地址加载。

### 7.3 动作模型

**内置动作（封闭集合，只含 UI 级动词）**

| 动作 | 参数 | 语义 | 备注 |
|---|---|---|---|
| `CloseSelf` | — | 关闭当前面板 | 必须是动作链最后一步；否则校验报错 |
| `CloseAll` | — | `UIManager.CloseAllAsync` | 全局副作用，编辑器警告 |
| `Replace` | `assetPath`, `layer` | 先关闭当前面板，再打开目标（可声明式或 C#） | **声明式→声明式唯一支持的导航**；单独成链，不与 `CloseSelf` 混用 |
| `Open` | `assetPath`, `layer` | 打开一个 **C# 面板** | 目标是声明式面板时校验报错（单根限制），改用 `Replace` |
| `Push` | `assetPath`, `layer` | 压栈一个 **C# 面板** | 同上 |
| `Pop` | — | 退栈 | 栈空时无操作 |
| `ShowTip` | `text` / `config` | `UIManager.ShowTipAsync` | 可与其它动作串联 |
| `SetLayerVisible` | `layer`, `visible` | 整层显隐 | 全局副作用，编辑器警告 |
| `SetLayerInteractive` | `layer`, `interactive` | 整层交互 | 同上 |
| `SetActive` | `nodeName`, `bool` | 子树显隐 | 页签的最小原语 |
| `SelectTab` | `groupName`, `index` | 一组节点只显示一个 | 比 `SetActive` 更不易配错 |
| `Custom` | `actionId`, `payload` | 发 keyed 消息 | 见下 |

**执行规则**

- 按列表顺序串行；异步动作 `await`；一个动作抛异常 → 记 Error、停止当前链，面板保持打开（不半拆）。
- `CloseSelf` 必须最后（校验器 `UI-AUTH-*` 报错）；`CloseAll` 同理建议最后。
- `Replace` 内部先等待当前面板关闭，再打开目标；它单独成链（同一按钮上不再挂其它动作），避免与「`CloseSelf` 必须最后」的规则冲突。
- 每次打开新建 `UIActionSession`；关闭/回池即失效，异步动作续体在失效后不再触碰面板（ABA 防护，参照核心 `UIManagerImpl` 的在途/回滚思路）。
- 所有动作在主线程执行；动作链只由按钮点击触发，不在每帧路径上。

**自定义动作出口**

```csharp
public readonly struct UIAuthoringActionMessage
{
    public readonly string ActionId;
    public readonly string Payload;
    public readonly UIDeclarativePanel Panel;
}
// 发布：MessageManager.Publish<string, UIAuthoringActionMessage>(actionId, msg)
```

- 项目按 `actionId` 订阅（keyed 消息，`MessageManager.cs:49` 有该重载）；框架不做通配符、不做反射分发、不自建第二事件系统。
- 「发消息」由 `Custom` 承载，不另立内置动作——避免把领域动词塞进动作词汇表。

**为什么不违反「不预设 GamePlay 架构」**：

1. 动作集**封闭且只含 UI 级动词**：关闭/打开/压退栈/显隐/页签/提示。任何领域语义（购买、装备、任务）都走 `Custom` → 项目代码。
2. 没有条件、变量、循环、脚本、反射——它不是行为树，也不是可视化编程。
3. 扩展包 **opt-in**，核心零改动；不使用它的项目完全不受影响。
4. 它不强制预制体结构：只按名字引用节点，节点怎么摆由项目决定；模板是可选的起点，不是运行时约束。

如果评审认为「封闭动作集」仍然越界，退路是只保留 `Custom` + `CloseSelf`/`SetActive`（最小集），把导航也交给项目——但会显著降低「无需 C#」的兑现度。

### 7.4 数据源接缝（阶段 3）

**候选接口形状**

| 形状 | 优点 | 缺点 |
|---|---|---|
| `bool TryGet<T>(string key, out IReactiveProperty<T> source)`（推荐） | 与 XReactive 同构；订阅是强类型、值变化零装箱；实现简单 | provider 需按预期类型提供；文本只支持 `string`（或由 provider 提供 `IReactiveProperty<string>` 的适配） |
| `IUIDataSink.Add<T>(key, Action<T>)` | 由 provider 主动填充，类型安全 | 多一层 sink 抽象；谁来枚举 key 需要约定 |
| `IDisposable Subscribe(string key, Action<object>)` | 最灵活 | 每次变化装箱；异常与释放责任在 provider |

**推荐**：

```csharp
public interface IUIDataProvider
{
    bool TryGet<T>(string key, out IReactiveProperty<T> source);
}

public static class UIDataProviderRegistry
{
    public static void Register(IUIDataProvider provider);
    public static void Unregister(IUIDataProvider provider);
}
```

**绑定种类与期望类型**

| 定义里的 kind | 目标组件 | 期望类型 | 双向 |
|---|---|---|---|
| `Text` | `TMP_Text` / UGUI `Text` | `IReactiveProperty<string>` | — |
| `Sprite` | `Image` | `IReactiveProperty<Sprite>` | — |
| `Color` | `Image` | `IReactiveProperty<Color>` | — |
| `FillAmount` | `Image` | `IReactiveProperty<float>` | — |
| `Slider` | `Slider` | `IReactiveProperty<float>` | ✅（源实现 `IReactivePropertyWriter<float>`） |
| `Toggle` | `Toggle` | `IReactiveProperty<bool>` | ✅（源实现 `IReactivePropertyWriter<bool>`） |
| `Active` | `GameObject` | `IReactiveProperty<bool>` | — |

- 双向复用核心 `UIBinder.BindTwoWay`（`UIBinder.cs:156-172`）；`ReactiveProperty<T>` 同时实现 `IReactiveProperty<T>` 与 `IReactivePropertyWriter<T>`（`ReactiveProperty.cs:23`）。源只读时降级为单向并告警。
- 缺 provider / 缺 key / 类型不匹配 → **只告警、不抛**；静态绑定照常生效。
- 注册表是静态状态：必须提供会话级复位（`SubsystemRegistration`），测试 fixture 必须复位（仓库既有纪律）。
- 所有权：provider 归项目，`Unregister` 不 Dispose；面板只持有订阅句柄，回池释放。
- GC：绑定在打开时一次性建立；值变化走强类型回调，无每帧分配；定义列表遍历一次，不缓存反射。

### 7.5 编辑器工具

- **定义 Inspector**：列表编辑、节点名候选（从当前预制体收集）、动作参数校验；阶段 3 增加数据键候选与 `twoWay` 资格提示。
- **校验器**：纯规则引擎 + IMGUI 窗口/菜单 `Tools/XFramework/UI Authoring/Validate Declarative Panels`；规则号 `UI-AUTH-001...`，与核心 `UI-PREFAB-*` 分开。
- **不能复用核心校验器**：它在 `Venusy609.Xframework.Editor` 里全部 `internal`，`AssemblyInfo.cs` 只对 `Venusy609.Xframework.Editor.Tests` 开放。可选路：① 扩展侧重复一小份规则引擎（推荐，规则只看定义与预制体）；② 核心开公开接缝（公开 API 变更，单独立项）；③ 不做（不建议）。
- 规则草案：
  - 定义缺失 / 预制体引用为空；
  - 节点不存在 / 重名 / 深度超 5；
  - 文本目标没有 `TMP_Text` 或 `Text`；
  - 动作按钮节点不存在 / 没有 `Button`；
  - `CloseSelf` 不在末尾；`Open/Push` 目标是声明式面板；
  - `SetActive` / `SelectTab` 目标不存在；页签索引越界；
  - `Custom` 的 `actionId` 为空；
  - 全局动作（`CloseAll` / `SetLayerVisible` / `SetLayerInteractive`）警告；
  - 数据键为空 / 双向 kind 不匹配 / 定义资产未被 YooAsset 采集（阶段 3）。
- 使用方文档（扩展包 README）要求：**两个校验器都跑**——核心校验预制体结构，扩展校验声明式定义。

### 7.6 与核心 API 的契合与使用限制

| 限制 | 证据 | 影响 | 缓解 |
|---|---|---|---|
| 单类型单实例 | `UIManagerImpl.cs:698,1996-2013` | 同时只能开一个声明式面板；`Push` 声明式面板会把当前那个回滚 | 单根 + 子页；需要并存用 C# 面板；校验器禁止声明式→声明式 `Open/Push` |
| `IsOpen<T>` / `GetPanel<T>` 歧义 | 同一类型 | 只能判断「是否有声明式面板开着」，不能区分地址 | 用 `OpenAsync` 返回的实例；关闭用 `CloseAsync(panel)` |
| 预加载按类型 | `UIManagerImpl.cs:1428-1440` | 只能预热一个声明式地址 | 不依赖 `PreloadAsync<UIDeclarativePanel>`；需要预热时用 `AssetManager.PreloadAllAsync` |
| 面板消息只带类型 | `PanelOpenedMessage.cs:10-25` | 订阅方无法区分地址 | 用扩展自己的 `UIAuthoringActionMessage` / 面板实例 |
| 控制器关闭后无地址 | `IUIController.cs:65` | `OnAfterClose` 拿不到是哪个面板 | 在 `OnBeforeClose` 记录 `panel.AssetPath` |
| `Dispose` 不补跑 `OnClose` | 核心 README 已知限制 | 清理必须放 `OnPoolRecycle` | 扩展面板遵守；测试 `Destroy` 路径 |
| 弹窗/对话框 | `ShowPopupAsync` 读面板 Rect；`ShowDialogAsync<TPanel,TResult>` 要求 `UIDialogPanel<TResult>` | 声明式面板可做锚定弹窗，但计入单根；**不能直接做对话框** | v1 不做声明式对话框；需要对话框时用 C# `UIDialogPanel` 或阶段 3 再议 |
| `Time.timeScale = 0` | 核心 README 已知限制 | 冻结 Tier0 更新 | 声明式面板无每帧逻辑，不受影响 |

### 7.7 四档需求逐条兑现

| 档位 | 阶段 | 机制 | 验收 |
|---|---|---|---|
| A 只搭预制体 | 1 | 模板 + 校验器 + 交接清单 | 模板扫描 0 Error；策划按模板加子页不改根结构 |
| B 静态展示页 | 2 | `UIDeclarativePanel` + 文本绑定 + `CloseSelf` | 预制体 + 定义即可打开/关闭，无专属 C# |
| C 简单交互 | 2 | 内置动作 + `Custom` 消息 | 按钮关闭/跳转/页签/Tip 全部由定义表达 |
| D 动态数据 | 3 | `IUIDataProvider` + 数据绑定表 | 程序只写一个 provider；开关/滑条双向 |

---

## 八、分阶段实施

### 8.1 阶段 1：文档 + 受限模板

**前置裁定**：Q2——是否重开「暂不做：预制体模板」。
**若不重开**：只写配方（在扩展包 README 或使用方文档里给出模板结构说明），不发预制体，不建包。

**交付物**

- 扩展包 README 的模板节（固定根结构、允许修改的字段/子节点、禁止修改项、与 `UI-PREFAB-001..009` 的对应）；本文件只记录设计结论与证据。
- 若批准：`Assets/XFramework.UIAuthoring/Samples~/Templates/` 下的基础 Prefab + Prefab Variant；`package.json` / README / CHANGELOG 最小骨架（只有模板，没有 Runtime 代码）。
- 使用方文档：模板是起点，不是运行时强制结构。

**验收**

- 核心菜单 `Tools/XFramework/UI/Validate UI Prefabs` 扫描模板：0 Error；Warning 逐条有解释。
- 策划按模板创建 Variant、添加子页、命名节点，不需要改根结构。
- 程序仅写面板类 + 一次打开调用（阶段 1 仍保留 C# 壳，这是与阶段 2 的分界）。

**测试**

- 核心校验器既有 36 例继续作为规则基线；扩展包若建骨架，加 EditMode 结构测试（对模板预制体的根组件/命名/深度做自有断言，不复用核心 internal）。
- 不引入 YooAsset 真加载；模板验收以编辑器扫描 + 手工打开为主。

### 8.2 阶段 2：声明式静态页 + 动作

**前置裁定**：Q1——接受单根限制（推荐）还是核心身份键；Q3——定义资产形态（推荐 SO）。

**交付物**

- 扩展包完整骨架：package.json、README、CHANGELOG、Runtime/Editor/Tests asmdefs、`AssemblyInfo.cs`。
- `UIDeclarativePanel`、`UIPanelDefinition`、`UIActionKind` / `UIAction` / `UITextBinding` / `UITabGroup`、`UIActionSession`、`UIAuthoringActionMessage`。
- 定义 Inspector + `UIAuthoringValidator`（`UI-AUTH-*`）+ 校验窗口。
- `Samples~/DeclarativeDemo/`：静态页、按钮关闭/跳转、开关、`SetActive` 页签、`ShowTip`、`Custom` 消息。
- 使用方 README 的「使用限制」：单根、`IsOpen` 歧义、预加载、弹窗/对话框。

**验收**

- 策划在一个预制体内完成：静态展示、按钮关闭/跳转/发消息、开关切换、页签切换；该页**不需要任何专属 C#**。
- 打开该页只需一次 C# 调用（或由另一个 C# 流程打开）；声明式面板之间不直接 `Push`。
- 直接调用 `UIManager.OpenAsync<UIDeclarativePanel>(第二个地址)` 的行为被 README 明确标为不支持；扩展入口有防护。
- 核心包 `git diff -- Assets/XFramework` 为空；核心测试全绿。
- 无每帧分配；重复开合 100 次订阅数不增长。
- 扩展校验器对缺失定义、缺失按钮/节点、非法 `SetActive`、`CloseSelf` 非末尾、`Push` 到声明式目标等报错。

**测试**

- EditMode：定义/动作计划/校验规则/节点目录的纯逻辑；fake `IUIActionRuntime`；无需 YooAsset。
- PlayMode：`UIDeclarativePanel` 的打开/关闭/回池、按钮/开关动作、`UIActionSession` 的 ABA（打开中关闭再重开）、provider 尚未接入时的静态绑定；用 fake `IUIManager` 测导航。
- 真实核心集成（`UIManager.OpenAsync` + YooAsset）用一次性探针，不做日常单测；文档登记为「覆盖缺口」。
- 包内自设守卫全部落地；`run-tests` 全量覆盖。

### 8.3 阶段 3：数据源接缝 + 编辑器校验完善

**交付物**

- `IUIDataProvider` / `UIDataProviderRegistry` / 绑定适配（Text/TMP/Image/Slider/Toggle/Active；双向 Slider/Toggle）。
- 本地化静态绑定（TMP + UGUI Text）与语言切换测试。
- 示例 provider：一个 `ReactiveProperty<T>` 集合，演示文本、图片、滑条、开关、双向写回。
- 编辑器校验完善：数据键缺失运行期 Warning、类型不匹配 Warning、`TwoWay` 限制、定义资产 YooAsset 采集、共享定义的引用扫描。
- 文档：provider 编写指南、注册/注销/会话复位、所有权与 Dispose 顺序、类型擦除与 GC 说明。
- 真实 YooAsset EditorSimulate 探针：预制体引用定义资产，验证同包/跨包两种采集方式。

**验收**

- 程序只写一个 provider + 一次注册；策划在定义资产里配绑定表；页面无 C#。
- 开合 100 次后订阅与 provider 实例不增长；`Dispose` 顺序正确。
- 缺失 key/类型不匹配只 Warning、不崩；provider 回调抛异常被隔离。
- 定义资产在真实 YooAsset 加载路径下不为 null；若失败，用户在「内联字段 fallback」与「定义资产必须同包采集」之间裁定。

### 8.4 裁定点与门禁

| 时点 | 触发的条目 | 原因 |
|---|---|---|
| 阶段 1 开工前 | 「暂不做：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成」中的**预制体模板** | 随包发受限 Prefab Variant 模板 |
| 阶段 2 开工前（若用户不接受单根限制） | 「核心包零改动」方向 + 核心面板身份键 | 需要核心行为/API 变更 |
| 阶段 3 开工前（仅当探针失败） | 定义资产形态（SO vs 内联） | 实现形态裁定，不涉及旧台账条目 |
| 任何阶段，若用户要求条件/变量/游戏动词/向导/代码生成 | 「不做：可视化 UI 设计器」或「暂不做：向导/脚本/地址生成」 | 超出本方案边界，需重新论证 |

---

## 九、与既有裁定的关系

| 既有条目 | 现状 | 本方案建议 | 需要用户裁定 |
|---|---|---|---|
| 暂不做：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成（2026-11-08） | 一条合并条目 | **拆开**：模板一项建议重开（阶段 1）；向导、脚本/地址生成维持暂不做 | 是（Q2） |
| 不做：可视化 UI 设计器（2026-11-08） | 结构性不做 | 维持；本方案不是编辑器 | 否 |
| 暂不做：保存即校验 / 构建前门禁 / 源码地址扫描（2026-11-08） | 暂不做 | 维持；代价与缺口见 §5.3 | 否（用户本轮确认） |
| 核心包零改动 | 本轮方向 | 维持；Q1 选 B 才需要重开 | 是（Q1） |
| FairyGUI：不接后端 / 不做平级包 | 结构性结论 | 维持；不冲突 | 否 |

**若 Q2 批准重开模板，台账的拟改文本**（按 `Documentation/Modules/README.md` 格式）：

- 原条目保留结论并标注拆分：`- 暂不做：**面板创建向导 / 面板脚本与地址常量生成**（2026-11-08 · 用户裁定）——…（模板一项移出，见下条）…`
- 新增条目：`- 已推翻：**~~暂不做：预制体模板~~**（原判 2026-11-08）——…被 2026-xx-xx「受限预制体模板随扩展包分发」取代。`
- 新增条目：`- 已做/未做：**受限预制体模板**（2026-xx-xx · <提交号>）——…`

（具体提交号在实施后回填；本条只有在用户裁定后才落盘。）

---

## 十、未验证事实与探针清单

| 探针 | 问题 | 状态 | 何时跑 / 影响 |
|---|---|---|---|
| P1 单类型多地址 | `OpenAsync<UIDeclarativePanel>` 开第二个地址的实际行为 | **源码已坐实**（§2.3），未跑 Unity | 阶段 2 前用一条 PlayMode 用例固化；README 写明「不支持」 |
| P2 YooAsset 定义资产采集 | 预制体引用的 `UIPanelDefinition` 是否随包/依赖加载 | `[未实测]` | 阶段 3 必跑：同包、跨包、依赖采集三种配置 |
| P3 Prefab Variant 模板分发 | `Samples~/Templates` 中的基础 Prefab + Variant 能否在消费方项目导入并保持脚本引用 | `[未实测]` | 阶段 1 若发模板，跑一次裸消费方导入探针 |
| P4 UI Toolkit | 是否值得另立换栈评估 | `[未实测]`，仓内从未评估 | 仅当用户认真考虑换栈时做 30–60 分钟探针（UIDocument、Input System only、运行时绑定、TMP、GC、与 `UIManager` 共存） |
| P5 TMP 绑定 | TMP Essentials 未导入时能否测 | 核心已有覆盖缺口 | 阶段 3 手工探针；自动化只测非 TMP 路径 |
| P6 IL2CPP/AOT | `IUIDataProvider.TryGet<T>` 的泛型实例化、无反射 | `[未实测]` | 阶段 3 至少一次 IL2CPP 构建；必要时 `link.xml` |
| P7 真实核心集成 | 扩展测试无法注入核心 `UIManager.PanelFactoryFactory`（internal） | 已知边界 | 用 fake runtime 测逻辑；真实 `UIManager.OpenAsync` 用一次性 EditorSimulate 探针 |

---

## 十一、开放裁定点

### Q1（最高）：核心零改动 vs 面板身份键

- **A. 接受单根限制（推荐）**：同一时刻只有一个声明式根面板；多页用预制体内子页 + `SetActive`/`SelectTab`；需要并存的界面用 C# 面板。零核心改动。
- **B. 核心加可选身份键**：`(Type, identity)` 记账，未实现身份接口的面板行为不变；支持多声明式面板并存与正确栈语义。需要核心行为/公开 API 变更，并重开「核心零改动」；身份接口是 public 还是 internal + `InternalsVisibleTo` 也要定。

### Q2：是否重开「暂不做：预制体模板」

- **A. 不重开**：阶段 1 只写配方，不随包发 Prefab Variant；项目自建模板。
- **B. 仅重开模板一项（推荐）**：扩展包 `Samples~/Templates` 随附受限 Prefab Variant；不做向导、不生成脚本/地址常量。

### Q3：定义资产形态

- **A. ScriptableObject `UIPanelDefinition`（推荐）**：可复用、可独立校验、预制体 diff 干净；需 YooAsset 依赖采集（P2）。
- **B. 组件内联序列化字段**：随预制体走、无额外资产；预制体 diff 大、不可复用。
- 若选 A，接受 P2 探针失败时回退 B 的可能。

### Q4：自定义动作出口

- **A. keyed `UIAuthoringActionMessage`（推荐）**：经 `MessageManager.Publish<string, UIAuthoringActionMessage>(actionId, msg)`；无反射、无第二事件系统；项目按 id 订阅，无通配符。
- **B. 静态 handler 注册表**：`Register(actionId, handler)`；更直接，但要自管生命周期、会话复位与异常隔离。
- 另需确认：「发消息」由 `Custom` 承载（推荐），不另立内置动作。

---

## 附录：源码证据索引

| 文件 | 看什么 |
|---|---|
| `Assets/XFramework/Runtime/UI/View/UIPanelBase.cs` | `OnOpen`/`OnClose` 抽象、sealed 包装、`OnPoolRecycle` 清理顺序 |
| `Assets/XFramework/Runtime/UI/View/UIViewBase.cs` | `RequireComponent`、`Track`、生命周期状态机、`Layer`/`AssetPath` |
| `Assets/XFramework/Runtime/UI/AssetPanelFactory.cs` | 根节点 `GetComponent<T>()`、失败销毁 |
| `Assets/XFramework/Runtime/UI/UIManagerImpl.cs` | 打开链路、去重、`RegisterPanel`、`FindOpenPanelForRequest`、预加载记账 |
| `Assets/XFramework/Runtime/UI/Data/UIPanelBinding.cs` | 约定绑定与 5 层深度、同名规则 |
| `Assets/XFramework/Runtime/UI/Data/UIBinder.cs` | 精确绑定、本地化、双向 |
| `Assets/XFramework/Runtime/UI/Data/PanelOpenedMessage.cs` | 消息载荷只有 `PanelType` |
| `Assets/XFramework/Runtime/UI/Controller/IUIController.cs` | 打开/关闭回调的可用参数 |
| `Assets/XFramework/Editor/UI/` | 九条预制体规则、扫描器、窗口、地址 provider |
| `Assets/XFramework/Editor/AssemblyInfo.cs` | `InternalsVisibleTo` 只对 Editor.Tests |
| `Assets/XFramework/Tests/Runtime/Architecture/EditorPublicApiBaselineTests.cs` | Editor 公开面基线 |
| `Assets/XFramework.Luban/` | 扩展包形状、测试/守卫、生成式工具的写盘协议 |
| `Documentation/Modules/UI.md:130-136` | 2026-11-08 三条裁定的原文 |
| `Documentation/FairyGUI.md` | 第二套栈的结构性理由与 §2.4 受限模板建议 |
