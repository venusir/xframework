# UI 制作（面向策划）—— 评估与方案（维护侧）

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> 使用方文档在 `Assets/XFramework/Runtime/UI/README.md`——用法、已知限制、设计取舍一律留在那里，本文件不复述。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> **状态**：评估已落盘；2026-11-08 完成全面审计并得到用户裁定（接受单根、只重开预制体模板、HUD/Dialog 设计纳入），实施待排（见 §十一）。模块台账条目在 `Documentation/Modules/UI.md` 的「决策台账」节。

---

## 〇、核对状态

| 标记 | 含义 |
|---|---|
| `[源码核对]` | 读当前工作树源码确认，路径与行号可作为证据 |
| `[推理]` | 基于源码结构与既有文档推导，**未实测** |
| `[未实测]` | 需要探针，见 §十 |

本轮的 `[源码核对]` 覆盖：面板生命周期与工厂、打开链路与去重/登记、约定绑定与精确绑定、预制体校验器九条规则及其 HUD/Tip 射程、Editor 程序集与公开 API 基线、Editor 内部可见性、扩展包先例（Luban / FMOD）与门禁射程、Config 与 Settings 的定位、`PreloadAsync` 与消息/控制器的载荷、HUD/Tip/Popup/Dialog 的完整制作链路、`CloseAllAsync` 的连带范围。第三方框架对照见 §6.6，来源标注核对状态。

**一个前提**：本文所有行号取自 2026-11-08 的工作树（HEAD `9ec131b6`）；2026-11-08 全面审计已逐条复核，源码改动后**先重新核对再引用**。

---

## 一、结论摘要

**缺口一句话**：策划今天能按规范搭预制体，但做不出「能跑的面板」——每个面板必须有一个 `UIPanelBase` 子类挂在预制体根节点上，打开地址还要程序手写。HUD 与对话框同样要程序写 `UIHudItem` / `UIDialogPanel<TResult>` 子类；Tip 是唯一已经「策划做预制体 + 程序一次调用」的形态。

**已有的一半**：约定绑定（`txt_/img_/btn_/sld_/tgl_`）与编辑期预制体校验器（`UI-PREFAB-001`–`009`）已经覆盖「预制体拼装与命名」；缺的是**面板/HUD/对话框脚本**与**打开调用**。校验器射程并不齐：面板/Popup/Dialog 跑 `001`–`008`，HUD 只跑 `001`/`008`，Tip 跑 `001`/`008`/`009`（§1.5.4）。

**硬约束**：`UIManagerImpl` 按**具体类型**登记面板，同类型再注册会记 Error 并回滚旧实例（§2.3）。因此单一通用面板类型**同一时刻只能开一个**——即使改用 `OpenAsync<UIPanelBase>` 也绕不过 `RegisterPanel` 的同类型回滚。HUD 是不同的基类（`UIViewBase`），不受这条面板登记约束；声明式对话框是另一个具体类型，可与面板同时开。

**推荐路线**：新建独立可选扩展包 `com.venusy609.xframework.ui.authoring`，核心包零改动，分阶段：

1. **阶段 1**：文档 + 受限预制体模板（面板/HUD/Dialog 三种 Prefab Variant）；需重开 2026-11-08「暂不做：预制体模板」一项。
2. **阶段 2a**：`UIDeclarativePanel` + `UIDeclarativeDialogPanel`（`string` 结果）+ 封闭的 UI 级动作集 + 自定义动作事件；覆盖静态展示页、简单交互与确认/取消对话框。
3. **阶段 2b**：`UIDeclarativeHudItem`（静态文本/动作）；动态数据留阶段 3。
4. **阶段 3**：数据源接缝（程序只写一个 provider）+ 面板/HUD 数据绑定 + 编辑器校验完善。
5. **阶段 4（可选）**：声明式 Popup 独立类型，解决「主面板 + 弹窗并存」的单根冲突。

**明确不做（用户本轮确认维持）**：面板脚本/地址常量代码生成、可视化 UI 设计器、保存/构建门禁、校验器自动修复。原因与代价见 §五——不能只写「不做」两个字。

**已裁定（2026-11-08 用户裁定）**：接受单根限制；只重开「预制体模板」一项；HUD/Dialog 声明式设计纳入本方案；向导、面板脚本与地址常量生成、可视化设计器、保存/构建门禁维持不做（见 §十一）。定义资产形态、自定义动作出口、动作模型细节留待实施前确认。

---

## 一·五、本轮全面审计发现（2026-11-08）

> 审计基准：HEAD `9ec131b6`。级别：**P1** = 会误导实现/决策，或按文档改会直接变红；**P2** = 引用、措辞或边界不精确。所有结论均回源码核对；行号是当前工作树。

### 1.5.1 事实与引用错误

| # | 原文 | 事实 | 级别 |
|---|---|---|---|
| F1 | §2.3-3「多个地址会互相覆盖」 | `PreloadAsync<T>` 第二次同类型 `_assetCache.ContainsKey(type)` 直接 return，新地址被**静默忽略**；只有 `ForgetPreload<T>()` 后才换地址（`UIManagerImpl.cs:1428-1446`）。 | P2 |
| F2 | §7.6「`Push` 声明式面板会把当前那个回滚」 | `PushAsync` 命中同身份先 `RefocusOpenPanel(existing)` 并返回（`:923-928`），不进 `RegisterPanel`、不回滚。回滚只发生在 `RegisterPanel` 的并发兜底（`:2000-2007`）。 | P1 |
| F3 | §4.2/§九 引用 `Modules/UI.md:130-136` | 三条裁定在 `:132`、`:134`、`:136`；`:130` 是「已做：编辑期 UI 预制体校验器」。且前两条证据是「用户裁定」、第三条是「本计划裁量」，不能统称。 | P2 |
| F4 | §三 引用 `UI.md:157,183,185`、`Ledger.md:95,105` | `UI.md:157` 空行、`:183` 零分配断言、`:185` 列表虚拟化、`:187` 不内建 UI 特效层；`Ledger.md:95` Addressables、`:105` 崩溃上报，新手引导 `:96`、红点 `:106`。拖拽/复杂手势无独立裁定，只有 `Documentation/FairyGUI.md:227` 的评估。 | P2 |
| F5 | §4.4/§7.1「Luban 不在核心六道门禁射程（核心门禁只扫 `Assets/XFramework`）」 | 前五项只扫核心；**第六项示例编译显式包含 `Assets/XFramework.Luban/Samples~`**（`Tools/check-docs.ps1:513-534`，样例根 `:516-519`）。Luban README 自己写明只有第六项覆盖它（`Assets/XFramework.Luban/README.md:341-343`）。新扩展包不登记 `Samples~` 就没有编译通道。 | P1 |
| F6 | §4.5「扩展包 README 不得出现 `Documentation/`（`PackageBoundaryTests` 守卫）」 | 核心 `PackageBoundaryTests` 的包根是 `Assets/XFramework`（`SourceScanner.cs:26`），只扫核心包；Luban 用包内 `LubanPackageBoundaryTests.cs:15-49` 自己守。新扩展包必须自带镜像守卫。 | P1 |
| F7 | §2.2「扫描器是唯一用 `AssetDatabase` 的普通代码」 | 窗口 `UIPrefabValidatorWindow.cs:144,230,235` 与 `YooAssetUIPrefabAddressProvider.cs:28,41,46,47` 也直接用 `AssetDatabase`。 | P2 |
| F8 | §7.6「`Time.timeScale = 0` 冻结 Tier0 更新」 | README 已知限制：冻结**整条 UI 每帧通路**（面板 `OnUpdate`、HUD 跟随、Tip 动画）（`Runtime/UI/README.md:1356-1357`）；「手动 `UIManager.Update` 只驱动 Tier0 与 HUD」是另一条，且源码还驱动 Tip provider（`UIManagerImpl.cs:1651-1662`）。 | P2 |
| F9 | §5「不做」三条 | 漏了 `Modules/UI.md:130` 同条已明确的「**不做自动修复**」；补上，否则下一轮会当新需求重开。 | P2 |
| F10 | §九 拟改台账文本 `- 已做/未做：…` | `已做/未做` 不是合法状态词（`check-docs.ps1:264` 只认六个）；仓库也没有独立的「暂不做：预制体模板」条目（模板只是 `UI.md:132` 合并条目的一部分）；`已推翻` 必须保留原文、删除线，并写「被 `<日期>`『`<新结论>`』取代」。按原稿改会红。 | P1 |
| F11 | §6.5「UI Toolkit 仓内从未评估（全仓零命中）」 | 本文落盘后自身就是唯一提及；应改为「本文落盘前全仓零命中；本文首次提及，仍未评估」。 | P2 |

### 1.5.2 动作集缺口（P1）

`Open` / `Push` 只给 `assetPath` + `layer`，没有写清「不知道具体类型时怎么打开 C# 面板」（`Replace` 只用于声明式→声明式，类型固定为 `UIDeclarativePanel`，不受此影响）。核心唯一的打开入口是泛型 `OpenAsync<T>` / `PushAsync<T>`（`IUIManager.cs:45,103`），没有 `Type` 重载；但可以用 `OpenAsync<UIPanelBase>` / `PushAsync<UIPanelBase>`——工厂对根 `GetComponent<UIPanelBase>()` 能取到任意面板子类（`AssetPanelFactory.cs:32`），登记仍按运行期具体类型（`UIManagerImpl.cs:698`）；核心 README 已把这条当既有用法（`Runtime/UI/README.md:651`）。三条边界：

1. 不同具体类型的 C# 面板：正常打开、登记、入栈；
2. 同具体类型、不同地址：按地址找不到 → 继续打开 → `RegisterPanel` 命中旧实例 → **Error + 回滚旧面板**（`:2000-2007`），不是重聚焦；
3. 声明式目标：已有声明式面板开着时同样走回滚路径，所以「`Open`/`Push` 目标不能是声明式面板」是必须的校验规则，需要按地址解析预制体。

推荐 A：保留 `Open`/`Push`，实现固定为基类型打开；`Replace` 只用于声明式→声明式；声明式目标由扩展校验器拒绝；动作没有 `userData` 通道，需要传数据的 C# 面板导航走 `Custom`。B（加 `panelType` + 核心非泛型入口）会动公开 API；C（删掉 `Open`/`Push`）会让跨面板导航退化。

另：直接 `UIManager.OpenAsync<UIDeclarativePanel>` 无法阻止程序打开第二个地址；扩展必须提供 `UIAuthoringNavigation.OpenAsync(address, ...)` 之类的防护入口，先查 `UIManager.IsOpen<UIDeclarativePanel>()`，并在 README 把直接调核心入口标为不支持。§8.2 写了「扩展入口有防护」，但没有定义它。

### 1.5.3 覆盖缺口（P1）

原文的六道工序、四档矩阵、§7.7 验收全部只围绕 `UIPanelBase`。核心实际有五种 UI 形态，制作链路不同；详见 §2.5。要点：

- `UIHudItem : UIViewBase`（不是 `UIPanelBase`），`ShowHudAsync<T>`，无 `userData`，不入栈、不参与焦点/消息/控制器/语言切换，固定 `Layer_HUD`/30000，池化复用；
- `UITipItem : MonoBehaviour`，`ShowTipAsync`，要求 `CanvasGroup` + 根 `RectTransform` + 根/后代 `TMP_Text`，默认地址 `PF_UITipText`，无队列/条数上限/单条关闭/层参数/本地化；
- `ShowPopupAsync<T> where T : UIPanelBase`，需锚点，`closeOnClickOutside` 只在内置 `UIManagerImpl` 生效，只摆一次、要求 `ScreenSpaceOverlay`；
- `ShowDialogAsync<TPanel,TResult>` 要求 `UIDialogPanel<TResult>`，遮罩层 = 面板实际 `Layer - 1`，`CloseAllAsync` 不碰遮罩。

`CloseAllAsync` 会 `SafeDetachAll(_hudProvider)` + `SafeDetachAll(_tipProvider)` 再关面板（`UIManagerImpl.cs:872-908`），原文只写「全局副作用」。

### 1.5.4 校验器射程（P2）

候选预制体 = 层级内任意位置有 `UIViewBase` 派生组件或 `UITipItem`（`UIPrefabCandidateDetector.cs:20-40`），所以 HUD/Tip 也在射程；但 `002`–`007` 只在根上有 `UIPanelBase` 时执行（`UIPrefabValidator.cs:104-108`）。实际覆盖：

- 面板/Popup/Dialog：`001`–`008`；
- HUD：只有 `001`（根组件）与 `008`（YooAsset 采集）；
- Tip：只有 `001`、`008`、`009`（缺 TMP 文本）。

原文只把校验器写成「面板预制体」校验器；应改为「面板 / HUD / Tip 候选预制体」，并写明 HUD 的约定命名/深度/重名/根 RectTransform 没有核心校验。要覆盖 HUD 的绑定规范，需扩展包自定义规则（或改核心规则，属核心改动）。

### 1.5.5 其他 P2

- `TipConfig` 含 `Vector3? WorldPos`，Unity 不序列化可空值类型；`ShowTip` 动作应定义扩展自己的 DTO，运行时拼 `TipConfig`。
- `UIAuthoringActionMessage` 原设计持有 `UIDeclarativePanel`；三形态共用后改为 `UIViewBase View`（面板/HUD/对话框的公共基类）。订阅方仍不得跨回调保存引用（关闭/回池后失效）。
- 实际门面是 `SetLayerVisibility`，不是 `SetLayerVisible`（`UIManager.cs:513`）。
- `ShowTipAsync` / `ShowDialogAsync` / `ShowPopupAsync` / `TipAssetPath` 只在门面，不在 `IUIManager`；扩展的 fake `IUIManager` 拦不到，测试要包一层。
- 新扩展包 `package.json` 要带 `samples` 数组与 `LICENSE.md`（Luban 先例 `package.json:22-28`）；维护向记录留在本文件，不新建 `Documentation/Modules/XFramework.UIAuthoring.md`（先例 `Documentation/Modules/README.md:83-84`）。
- 核心 `Assets/XFramework` 下 `*.prefab` 零命中；`Samples~/Example/UISample.cs` 明确「不附带预制体」。阶段 1 模板要从零建，P3 探针不是可选项。
- 「只有模板、没有 Runtime 代码」的模板不可用：预制体根必须挂 `UIPanelBase` 子类；没有可挂载脚本就是 Missing Script。推荐模板与阶段 2a 的 `UIDeclarativePanel` 一起发，或随模板发一个最小 `UIEmptyPanel : UIPanelBase` 占位。
- 两处核心 UI README 与源码不符：`:1358` 说手动 `UIManager.Update` 只驱动 Tier0 与 HUD，实际还驱动 Tip；`:1372` 说 `SetInstance` 不重接驱动器，实际在 `UIManager.cs:155-171` 调用 `EnsureFrameDriverRegistered()`。本轮一并修。
- 重聚焦不发消息：`OpenAsync`/`PushAsync` 命中已打开面板只 `RefocusOpenPanel`，不调用 Controller、不发布 `PanelOpenedMessage`（`:613-621`）。

### 1.5.6 第三方 UI 制作框架对照结论

见 §6.6。一句话：真正解决「策划制作面」的只有 FairyGUI 与 UI Toolkit，而两者都是第二套栈；UGF/Loxodon/Unity Weld 是代码优先的运行时/绑定框架，不解决策划零代码。本项目的路线只能是「UGUI 内的受限模板 + 声明式定义 + 封闭动作集」，不能靠引入第三方框架一步到位。

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

**结论**：第 1 道与第 6 道是策划无法独立跨越的门槛；第 2–5 道已经有工具，但工具是**手动菜单**，不会自动触发（`Runtime/UI/README.md:344`）。这张表只描述普通面板；HUD / Tip / Popup / Dialog 的制作链路见 §2.5。

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

- 菜单 `Tools/XFramework/UI/Validate UI Prefabs`（`Editor/UI/UIPrefabValidatorWindow.cs:36-42`）；规则引擎对内存 `GameObject` 只读检查，纯规则 `UIPrefabValidator.cs`；扫描器 `UIPrefabScanner.cs` 负责目录扫描，窗口与 `YooAssetUIPrefabAddressProvider.cs` 也直接用 `AssetDatabase`。
- 九条规则：根组件、Binding 位置、约定深度/类型/重名、根 RectTransform、池化变换、YooAsset 采集、Tip 文本（`UIPrefabValidationIssue.cs:22-50`）。**射程不齐**：`002`–`007` 只在根上有 `UIPanelBase` 时执行；HUD 只跑 `001`/`008`，Tip 跑 `001`/`008`/`009`（`UIPrefabValidator.cs:104-108`、`UIPrefabCandidateDetector.cs:20-40`）。
- 边界：只读、不修改预制体（**不做自动修复**）；不在保存或构建时自动触发；`UIBinder` 手工绑定与代码里的地址字符串**不在检查范围**（README:344）。测试 36 例（`Tests/Editor/UI/`）。

**结论**：预制体拼装已经「有尺子」，但尺子是手动量；面板脚本与调用点仍完全靠人。

### 2.3 「类型即身份」——推荐方案必须正面处理的硬约束 `[源码核对]`

核心把面板身份绑定在**具体类型**上，而不是「类型 + 实例标识」：

1. **登记**：`RegisterPanel(panel.GetType(), panel)` 写入 `_activePanels`（`UIManagerImpl.cs:698, 1996-2013`）。同一具体类型已存在时，`RegisterPanel` 记一条 **Error** 并 `RollbackPanel(oldPanel)`——这是「同类型单实例」不变量被破坏时的兜底，不是正常去重路径。
2. **查找**：`FindOpenPanelForRequest(requestedType, assetPath)` 先查 `_activePanels[requestedType]` 精确命中，再回落到按地址查找（`UIManagerImpl.cs:1966-1972`）。对**具体类型** `UIDeclarativePanel`，第一次命中后直接 `RefocusOpenPanel` 并返回——**打开第二个不同地址会静默返回第一个**（`OpenAsync` 的早退在 `:616-621`）。`PushAsync` 在 `:923-928` 先走同一去重，因此**压栈第二个声明式地址同样会静默重聚焦第一个**，不会打开新面板。
3. **预加载**：`PreloadAsync<T>` 按 `typeof(T)` 记账，同类型第二次直接早退（`UIManagerImpl.cs:1428-1440`）；一份 `UIDeclarativePanel` 只能预热一个地址，第二次同类型预加载会**静默忽略新地址**，只有先 `ForgetPreload<T>()` 才能换地址。
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

### 2.5 五种 UI 形态与制作链路（审计补全） `[源码核对]`

| 形态 | 基类 / 入口 | 策划要做什么 | 程序要做什么 | 校验器覆盖 |
|---|---|---|---|---|
| 普通面板 | `UIPanelBase` / `OpenAsync<T>` | 预制体 + 根脚本 + 约定命名 | 面板类 + 打开调用 | 001–008 |
| HUD | `UIHudItem : UIViewBase` / `ShowHudAsync<T>` | 预制体 + `UIHudItem` 子类 + `CanvasGroup` + 命名 | HUD 类 + 对目标调用；无 `userData` | 001、008 |
| Tip | `UITipItem : MonoBehaviour` / `ShowTipAsync` | Tip 预制体 + `CanvasGroup` + 根 `RectTransform` + `TMP_Text` | 设 `TipAssetPath` + 调用 | 001、008、009 |
| 锚定弹窗 | `UIPanelBase` / `ShowPopupAsync<T>` | 同普通面板 + 根尺寸/pivot | 锚点 + 调用 | 001–008 |
| 模态对话框 | `UIDialogPanel<TResult>` / `ShowDialogAsync<TPanel,TResult>` | 预制体 + 面板类 | 结果类型 + 调用 | 001–008 |

- **HUD 不是面板**：`UIHudItem` 继承 `UIViewBase`（`View/UIHudItem.cs:15`），有跟随目标、`CanvasGroup`、每帧 `OnUpdate`、目标丢失回收；`ShowHudAsync` 没有 `userData`，示例在 `await` 后自行绑定（`Samples~/Example/UISample.cs:140-143`）；HUD 不入显示栈、不参与焦点/消息/控制器/语言切换，固定 `Layer_HUD`/30000，池化复用；同一 target 只能绑一个（`IUIHudProvider.cs:21-31`）。失败语义：target/地址/实例化/缺组件返回 null，`OnOpenImpl` 抛异常会向上抛（`UIHudManager.cs:221-233`）。
- **Tip 已是「策划做预制体 + 程序一次调用」**：`UITipItem` 是普通 `MonoBehaviour`（`Tip/UITipItem.cs:16`），要求 `CanvasGroup`（`:15`）、根 `RectTransform`（`:62-69`）、根/后代 `TMP_Text`（`:54`）；默认地址 `PF_UITipText`（`UITipManager.cs:24`），程序可改 `TipAssetPath`（只在门面，不在 `IUIManager`）；无队列/条数上限/单条关闭/层参数/本地化，多条可同时播；`ShowTipAsync` 创建并开始播放即完成，不等播完。
- **Popup**：`ShowPopupAsync<T> where T : UIPanelBase`（`UIManager.cs:932-940`），需 `RectTransform` 锚点；`closeOnClickOutside` 只在内置 `UIManagerImpl` 生效；只摆一次、不跟随锚点；要求画布 `ScreenSpaceOverlay`；入口只在门面。
- **Dialog**：`ShowDialogAsync<TPanel,TResult>` 要求 `TPanel : UIDialogPanel<TResult>`；`SetResult` 先到者胜、幂等；外部关闭返回 `default`；遮罩层 = 面板实际 `Layer - 1`，默认 Popup(200) → 遮罩 199/排序 6399，对话框从 6401 起；`CloseAllAsync` 不碰遮罩。

### 2.6 `CloseAllAsync` 的连带范围 `[源码核对]`

`CloseAllAsync` 先把 `_hudProvider` 与 `_tipProvider` 全部 `DetachAll`，再逐个关闭面板（`UIManagerImpl.cs:872-908`）。所以动作 `CloseAll` 的语义是「面板 + HUD + 在播 Tip」；遮罩不在其列（引用计数句柄，要显式 `HideMask()`）。原文只写「全局副作用」，不够。

---

## 三、四档需求矩阵

| 档位 | 策划做什么 | 程序做什么 | 今天缺什么 | 本方案覆盖 |
|---|---|---|---|---|
| **A. 只搭预制体** | 按规范搭预制体、命名节点、进采集 | 写面板类 + 打开调用 | 模板与交接规范；校验器手动跑 | 阶段 1（模板 + 文档）；阶段 2a 起可换成声明式组件 |
| **B. 静态展示页** | 标题/文本/图片/按钮关闭；不需要 C# | 一次打开调用（可由别的 C# 流程代劳） | **没有「不含逻辑的面板类」**；关闭按钮也要写代码 | 阶段 2a：`UIDeclarativePanel` + 文本绑定 + `CloseSelf` 动作 |
| **C. 简单交互** | 按钮跳转、开关、页签、发消息/弹 Tip | 最多写一个全局动作分发（若用 `Custom`） | 没有动作表达层；每个按钮都要代码回调 | 阶段 2a：内置 UI 级动作 + `Custom(id)` 事件出口；`ShowDialog` 覆盖对话框 |
| **D. 动态数据绑定** | 在定义里配「节点 ← 数据键」 | 写**一个** `IUIDataProvider` 并注册 | 没有「按数据键绑定」的声明式通道；每页一个 ViewModel | 阶段 3：`IUIDataProvider` + 绑定表 + 双向 |

**这四档只描述面板**。HUD / Tip / Popup / Dialog 是不同形态（§2.5），本方案的覆盖结论单列：

| 形态 | 本方案覆盖 | 说明 |
|---|---|---|
| HUD | 设计纳入，实施阶段 2b/3 | `UIDeclarativeHudItem : UIHudItem`；无 `userData`，动态数据依赖阶段 3 的 `IUIDataProvider` |
| Tip | 已可独立完成 | Tip 预制体规范 + 核心校验 `001/008/009`；不需要声明式面板 |
| Popup | 复用声明式面板 | 受单根限制；「主面板 + 弹窗」并存需要阶段 4 的独立类型 |
| Dialog | 设计纳入，实施阶段 2a | `UIDeclarativeDialogPanel : UIDialogPanel<string>`；自定义 `TResult` 仍走 C# |

**明确不在射程内的**（与核心 README 的既有裁定一致）：列表虚拟化 / 循环列表、完整的新手引导、红点树、拖拽与复杂手势、UI 特效层——这些是领域模型或第三方组件，框架不内建（`Documentation/Modules/UI.md:185,187`；`Ledger.md:96,106`；拖拽/复杂手势只有 `Documentation/FairyGUI.md:227` 的评估，无独立裁定）。

---

## 四、约束与既有裁定

### 4.1 框架原则

- **不预设 GamePlay 架构**：框架只提供正交基础设施与编排原语，不为使用方决定实体模型、生命周期树、时间模型或状态管理（`CLAUDE.md`「项目定位」）。
- **框架代码供第三方运行时使用**：控制 GC、不得每帧分配；公开 API 必须带 CancellationToken 的异步约定；模块边界与包边界有机械守卫。

### 4.2 2026-11-08 的三条裁定 `[源码核对]`

原文见 `Documentation/Modules/UI.md:132`、`:134`、`:136`（前两条证据是「用户裁定」，第三条是「本计划裁量」；`:130` 是「已做：编辑期 UI 预制体校验器」，不属于这三条）：

- **暂不做**：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成——「本轮只做校验器 + 使用方文档；制作仍由项目在 Unity 内完成，避免框架预设 UI 结构、模板与命名/地址生成策略」。重开判据：出现多个项目重复手写同一套面板模板，且模板差异可用参数表达；或地址漂移成为实际事故源。
- **不做**：可视化 UI 设计器——「等价于第二套 UI 栈；第三方 UI 方案评估已给出结构性理由（`IUIManager` 的编排原语按 UGUI 对象实现，没有可替换后端的接缝），不重开」。
- **暂不做**：保存即校验 / 构建前门禁 / 源码地址字符串扫描——「保存即校验会在每次导入时加载全量预制体并可能触发导入循环；构建门禁替不使用 UI 的项目决定构建策略；源码地址多来自变量与配置，静态扫描误报率高」。重开判据：团队要求「无效预制体不得入库/出包」且已有稳定 CI 或提交钩子；或地址常量生成落地使地址可静态解析。

另：`UI.md:130` 的校验器条目还明确「不做自动修复」；§5.3 已补上这一项，避免下一轮把它当新需求重开。

**历史状态**：上述第一条中的「预制体模板」一项已由 2026-11-08 用户裁定重开（§九），本节保留原文作为历史；向导、面板脚本、地址常量生成仍维持暂不做。

### 4.3 FairyGUI 评估的结论与「受限模板」建议

`Documentation/FairyGUI.md` 的总结论：**不接成 `IUIManager` 的后端，也不做平级管理层包**。理由不是工作量：

- 「策划脱离 Unity 编辑 UI」只有 FairyGUI 编辑器给得了；「策划脱离游戏数据逻辑」与 FairyGUI 无关，本仓 `UIBinder` + `BindByConvention` 现在就有（§2.4）。
- §2.4 明确提出替代解法：**「给策划受限的预制体模板（Prefab Variant 固定结构 + 只暴露该改的字段）」**。
- §五 的重开判据两条（上游输入分支 + Unity 6 实测通过；出现真实使用方明确要用 FairyGUI 作其 UI 栈）——与本方案不冲突。

### 4.4 扩展包先例与门禁射程

- `Assets/XFramework.Luban/`：`package.json` 名 `com.venusy609.xframework.luban`，Runtime asmdef 引用 `Venusy609.Xframework` + `UniTask`，Editor asmdef 只引用扩展 Runtime，`Tests/Runtime` 与 `Tests/Editor` 各有 asmdef；自带 README/CHANGELOG/守卫；**核心六道门禁中前五项只扫 `Assets/XFramework`，第六项示例编译显式包含 `Assets/XFramework.Luban/Samples~`**（`Tools/check-docs.ps1:513-534`）。新扩展包的 `Samples~` 不登记到该列表就没有编译通道；其余测试由全量 `run-tests.ps1` 覆盖。
- `Assets/XFramework.Fmod/` 同型（条件集成程序集用 `versionDefines` + `defineConstraints`）。
- **扩展包访问核心 `internal` 需要核心开 `InternalsVisibleTo`（属核心改动）**：Runtime 当前对 `Venusy609.Xframework.Tests`、`Venusy609.Xframework.Editor.Tests`、`Venusy609.Xframework.Fmod`、`Venusy609.Xframework.Fmod.Tests` 开放（`Runtime/AssemblyInfo.cs:3-9`）；Editor 只对 `Venusy609.Xframework.Editor.Tests` 开放（`Editor/AssemblyInfo.cs:5`）。新扩展包若想复用核心 Editor 校验器，必须改核心 `AssemblyInfo.cs`，与「核心零改动」冲突——这是 §7.5 选择「重复一小份规则引擎」的直接原因。

### 4.5 文档分层与包边界

- 本文件是维护向评估，**不随包发布**；包内全部文本（`README`/`.md`/源码注释）都不得出现 `Documentation/` 路径。**核心 `PackageBoundaryTests` 只扫 `Assets/XFramework`**；扩展包必须自带镜像守卫（Luban 的 `LubanPackageBoundaryTests.cs:15-49` 是先例）。
- 模块台账的唯一格式在 `Documentation/Modules/README.md`；状态词只有 `已做/未做/待裁/暂不做/不做/已推翻`。扩展包的维护向记录留在本文件，不新建 `Documentation/Modules/XFramework.UIAuthoring.md`（先例 `Documentation/Modules/README.md:83-84`）。

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

### 5.3 保存即校验 / 构建前门禁 / 源码地址扫描 / 自动修复（`暂不做`）

**是什么**：保存或导入时自动跑校验、构建前拦截无效预制体、静态扫描代码里的地址字符串、校验器自动修复问题。

**不做的原因**：

1. **保存即校验**：每次导入要加载全量预制体，大工程每次保存都变慢；校验器读写 `AssetDatabase` 还可能触发导入循环。
2. **构建门禁**：替不使用 UI 的项目决定构建策略；框架包不该往第三方构建管线里加钩子。仓库至今零 `IPreprocessBuildWithReport`。
3. **源码地址扫描**：地址来自变量、配置、序列化字段、拼接，静态扫描误报率高；误报会让团队直接关掉检查，反而更糟。
4. **门禁是流程强制，不是制作能力**：它不解决「策划做不出面板」，只解决「坏预制体别入库」——收件人不同。
5. **自动修复**：校验器当前只读、不修改预制体（`Modules/UI.md:130` 已裁定不做自动修复）。自动修复要处理 Undo、序列化与资产引用，且会把「制作规范被破坏」这件事掩盖成「工具已修好」；报告问题、不替人改，边界更清楚。

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

- **形态**：独立扩展包内提供三个声明式类型——`UIDeclarativePanel : UIPanelBase`、`UIDeclarativeHudItem : UIHudItem`、`UIDeclarativeDialogPanel : UIDialogPanel<string>`——共用 `UIPanelDefinition`（ScriptableObject）+ 封闭 UI 级动作集 + 数据源接缝。策划在预制体上挂组件、在定义资产里配文本/按钮/结果/数据键；程序只写一个 provider（阶段 3）或一次打开调用。
- **覆盖**：A（模板 + 组件）、B（文本 + CloseSelf）、C（动作 + Custom + ShowDialog）、D（数据源）；HUD（2b/3）与 Dialog（2a）单列。
- **代价**：新包与长期维护；面板单根限制（§2.3）；动作词汇表的边界要守住；YooAsset 定义资产采集需探针（P2）；三个类型共用一套绑定/动作实现，差异只在基类与动作集。
- **与裁定冲突**：不冲突于「向导/脚本生成」；**需重开「预制体模板」一项**（阶段 1 发模板）。「不做可视化设计器」不冲突——它不是编辑器，是运行时面板。
- **结论**：唯一能以核心零改动覆盖面板四档的选项；HUD/Dialog 是本轮新增的设计范围。

### 6.4 方案 3：创建向导 + 代码生成

- **形态**：菜单从预制体生成面板脚本、地址常量、目录/命名空间骨架。
- **覆盖**：档位 A（程序更快），**不覆盖** B/C/D——生成物仍是需要程序维护的 C# 面板。
- **代价**：重开「暂不做」；生成器写盘协议（Luban 先例）；命名/目录策略；陈旧文件清理。
- **结论**：它优化的是程序，不是策划。可作为方案 2 的**可选补充**（只生成地址常量或创建声明式面板模板），但不作为主方案。

### 6.5 方案 4：换栈（FairyGUI / UI Toolkit）

- **FairyGUI**：已在 `Documentation/FairyGUI.md` 结构性否决（不接后端 / 不做平级包）；重开判据两条，与本方案不冲突。
- **UI Toolkit**：本文落盘前**全仓零命中**；本文首次提及，仍未评估。它是 Unity 原生、有 UI Builder 与 Runtime Binding（2023.2 起：数据源资产、Inspector 配 `data-source-path`、`ToTarget/ToSource/TwoWay`、Converter Group），但仍是与 UGUI 并行的渲染/文档栈；`IUIManager` 的编排原语按 UGUI 对象实现，接进来同样没有后端接缝。**本轮不建议开**；若用户认真考虑换栈，先跑一个 30–60 分钟探针（P4），再决定是否立项。
- **结论**：不推荐；不把它写成「已否决」——它是「未评估」，不是「已证明不行」。

### 6.6 第三方 UI 制作框架对照（借模式，不引第二套栈）

用户本轮追问「由第三方 UI 制作框架借鉴吗？」。结论：**可以借鉴，但只借模式，不引依赖**。真正解决「策划制作面」的只有 FairyGUI 与 UI Toolkit，而两者都是第二套栈；UGF/Loxodon/Unity Weld 是代码优先的运行时/绑定框架，不解决策划零代码。

| 参照 | 能给什么 | 不能给什么 / 为什么不引 | 本项目怎么用 |
|---|---|---|---|
| **FairyGUI** | 策划脱离 Unity 的可视化编辑；组件/关系/控制器；本仓已有结构性评估 | 第二套 UI 栈；`IUIManager` 的加载/排序/遮罩/栈/HUD 原语按 UGUI 对象实现，没有可替换后端；`.fui` 二进制包与 Force Text/diff 评审冲突 | 只借 §2.4 的「受限模板」思路（阶段 1/2） |
| **UnityGameFramework（UGF）** | `UIForm`/`UIGroup` 的栈、组深度、`OnInit/OnOpen/OnClose/OnRecycle/OnPause/OnResume/OnCover/OnReveal/OnRefocus` 生命周期、延迟回收对象池 | 仍是**代码优先**：每个 UIForm 要写 `UIFormLogic` 子类，不解决策划零代码；引入它就是第二套 UI 管理 | 只作生命周期词汇与栈语义的对照；不引入 |
| **Loxodon Framework** | MVVM 绑定、`ObservableProperty`/`ObservableList`、零 GC 绑定、UGUI/UI Toolkit/FairyGUI 多栈 | 完整 MVVM 栈；绑定是 C# 代码 + 表达式绑定，不是策划配置；与本仓 `UIBinder`/`XReactive` 重叠 | 阶段 3 `IUIDataProvider` 的接口形状可对照；不引依赖 |
| **Unity Weld** | MVVM 式双向绑定、`BindingSet` | 已停更（v0.8.0/2018，最后推送 2022）；C# 绑定集；反射/委托开销与本仓零 GC/AOT 规则不一致 | 不采用；只作为「代码绑定」对照 |
| **UI Toolkit / UI Builder** | 官方可视化编辑 + Runtime Binding：数据源资产、Inspector 配绑定、`ToTarget/ToSource/TwoWay`、Converter Group | 与 UGUI 并行的渲染/文档栈；`IUIManager` 编排原语没有后端接缝；本文落盘前仓内从未评估 | 只借「数据源资产 + 绑定表 + 双向/转换器」到阶段 3；换栈需单独立项探针 |
| **Excel/JSON 配置驱动 UI**（行业常见做法） | 把 UIName/层/预制体路径/行为外置为表，策划配逻辑 | 需要自建解释器与工具链；本仓 `XConfig` 目前零消费，「一行配置 → 一个行为」的执行器不存在 | 未来「从表批量生成 `UIPanelDefinition`」的方向，不进 v1 |

来源（保留 URL 与核对状态）：UGF `https://deepwiki.com/EllanJiang/UnityGameFramework/5.1-ui-system`（网页/源码摘要；本仓未读 UGF UI 源码）；Loxodon `https://github.com/vovgou/loxodon-framework`（README）；Unity Weld `https://github.com/Real-Serious-Games/Unity-Weld`（README）；UI Toolkit Runtime Binding `https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-get-started-runtime-binding.html`；FairyGUI 见本仓 `Documentation/FairyGUI.md`。

### 6.7 对比表

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

**第三方借鉴结论**：不引入第二套栈；只借受限模板（FairyGUI）、生命周期/分组词汇（UGF）、数据绑定与数据源形状（Loxodon/UI Toolkit）。FairyGUI 与 UI Toolkit 是唯一真正面向制作面的方案，但都是第二套栈——这正是本方案选择「UGUI 内的受限模板 + 声明式定义 + 封闭动作集」的理由。

---

## 七、推荐方案详设

### 7.1 包形状（遵循 Luban 先例）

| 项 | 建议 |
|---|---|
| 目录 | `Assets/XFramework.UIAuthoring/` |
| `package.json` | `"name": "com.venusy609.xframework.ui.authoring"`；`displayName`: `XFramework UI Authoring`；`version`: `0.1.0`；`unity`: `6000.3`；`dependencies`: `{}`（UPM 不支持 Git URL；对核心包的依赖写进扩展包 README 的安装顺序）；**`samples` 数组**（Luban 先例 `package.json:22-28`）与 `license`/`licensesUrl` |
| Runtime 程序集 | `Venusy609.Xframework.UI.Authoring`；引用 `Venusy609.Xframework`、`UniTask`；命名空间 `XFramework.XUI.Authoring` |
| Editor 程序集 | `Venusy609.Xframework.UI.Authoring.Editor`；`includePlatforms: [Editor]`；引用扩展 Runtime（**不引用核心 Editor**） |
| 测试程序集 | `Venusy609.Xframework.UI.Authoring.Tests`（Runtime 测试）/ `.Editor.Tests`；各自 asmdef |
| 示例 | `Samples~/Templates`（面板/HUD/Dialog 三种受限 Variant）、`Samples~/DeclarativeDemo`（阶段 2a/2b）；两个目录都要登记到 `Tools/check-docs.ps1` 的样例根列表 |
| 文档 | 扩展包 `README.md` + `CHANGELOG.md` + `LICENSE.md`；**不指向 `Documentation/`**；维护向记录留在本文件，不新建 `Documentation/Modules/XFramework.UIAuthoring.md` |
| 守卫 | 镜像 Luban：README/文件树、`.meta` 完整性、包边界（扩展包自带，不能依赖核心 `PackageBoundaryTests`）、AutoInit 族清单、Runtime/Editor 公开 API 基线、依赖声明；核心六道门禁前五项不扫扩展包，第六项示例编译需显式登记；全量 `run-tests.ps1` 覆盖测试 |

**核心包零改动**：不改 `Assets/XFramework/**` 代码、不改核心公开 API 基线、不给核心 asmdef 加 `InternalsVisibleTo`。唯一会碰到的仓库文件是 `Tools/check-docs.ps1` 的样例根列表（`samples` 登记），它不是核心包代码。

### 7.2 运行时模型

**组件**

```csharp
public sealed class UIDeclarativePanel : UIPanelBase
{
    [SerializeField] private UIPanelDefinition _definition;
    // OnOpen：按定义绑定文本/动作/数据；OnClose：Track 统一释放；OnPoolRecycle：base 优先
}

public sealed class UIDeclarativeHudItem : UIHudItem
{
    [SerializeField] private UIPanelDefinition _definition;
    // OnOpenImpl：base.OnOpenImpl(userData) → 绑定；OnCloseImpl：解绑 → base.OnCloseImpl
    // 不重写 OnUpdate；HideSelf 动作调 UIManager.HideHud(FollowTarget)
}

public sealed class UIDeclarativeDialogPanel : UIDialogPanel<string>
{
    [SerializeField] private UIPanelDefinition _definition;
    // OnOpen：绑定；按钮动作 CloseWithResult(resultId) → SetResult → CloseSelfAsync
}
```

- **三个独立具体类型**：`UIDeclarativePanel`（面板）、`UIDeclarativeHudItem`（HUD，基类 `UIViewBase`）、`UIDeclarativeDialogPanel`（对话框，结果类型固定 `string`）。面板与对话框可以同时打开；同一时刻各自只能有一个；HUD 可有多个（每个 target 一个）。
- 面板类型只有一个，预制体可以有任意多个；每个预制体根节点挂它。HUD/Dialog 同理，各自一个类型、多个预制体。
- `_definition` 为空时仍可打开：预制体本身就是静态内容，不绑任何东西（覆盖「只搭预制体」）。
- 节点查找：按名字递归、**深度上限 5**（与 `UIPanelBinding.CacheComponents` 对齐）；名字在子树内必须唯一；找不到 → 编辑器告警、运行期跳过该条，不抛异常。
- 生命周期：`OnOpen` 建绑定并登记到 `Track`；`OnClose` / `OnPoolRecycle` 由基类统一释放；`OnPoolRecycle` 必须 `base.OnPoolRecycle()`（核心契约，`UIPanelBase.cs:198-214`）。
- 每帧无工作：不做 `OnUpdate`；动作与绑定只在打开时发生。

**定义资产（`UIPanelDefinition : ScriptableObject`）**

```
Kind: Panel | Hud | Dialog     // 显式编号；校验器按 Kind 限制动作集
Version: int                       // 序列化值只在末尾追加；动作/目标枚举显式编号
TextBindings: List<UITextBinding>  // nodeName, literal?, localizationKey?, useLocalization
ActionBindings: List<UIActionBinding> // buttonNodeName, List<UIAction>
TabGroups: List<UITabGroup>        // groupName, List<nodeName>
DataBindings: List<UIDataBinding>  // nodeName, kind, dataKey, twoWay, format?（阶段 3）
```

定义资产可复用，但 `Kind` 必须匹配；同一 Kind 的定义可被多个预制体引用，校验器逐个预制体检查节点存在性。三类型共用节点查找、文本绑定、`Track`、动作链与数据绑定实现，差异只在基类、动作集和结果通道。

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

| 动作 | 参数 | 适用 Kind | 语义 | 备注 |
|---|---|---|---|---|
| `CloseSelf` | — | Panel / Dialog | 关闭当前面板 | 必须是动作链最后一步；否则校验报错。Dialog 上允许但校验器 Warning（结果为 `null`） |
| `CloseAll` | — | Panel | `UIManager.CloseAllAsync` | 连带关闭 HUD 与在播 Tip（§2.6）；不碰遮罩；全局副作用，编辑器警告 |
| `Replace` | `assetPath`, `layer` | Panel | 先关闭当前声明式面板，再打开目标 | **声明式→声明式导航**；单独成链，不与 `CloseSelf` 混用 |
| `Open` | `assetPath`, `layer` | Panel | 打开一个 **C# 面板** | 实现用 `OpenAsync<UIPanelBase>`；目标不能是声明式面板（同具体类型会回滚旧实例）；**无 `userData`** |
| `Push` | `assetPath`, `layer` | Panel | 压栈一个 **C# 面板** | 实现用 `PushAsync<UIPanelBase>`；边界同 `Open` |
| `Pop` | — | Panel | 退栈 | 栈空时无操作 |
| `ShowTip` | `text`, `tipConfig` | 全部 | `UIManager.ShowTipAsync(text, ToTipConfig(...))` | `tipConfig` 是扩展 DTO，不能直接序列化 `TipConfig`（含 `Vector3?`） |
| `ShowDialog` | `assetPath`, `actionId` | Panel | `await ShowDialogAsync<UIDeclarativeDialogPanel, string>`，把结果作为 `UIAuthoringDialogResultMessage`（keyed by `actionId`）发布 | 面板保持打开；面板关闭后 `UIActionSession` 失效，续体不得再触碰面板 |
| `CloseWithResult` | `resultId` | Dialog | `SetResult(resultId)` → `CloseSelfAsync()` | 必须最后一步；`resultId` 不能为空；同一对话框内不重复 |
| `HideSelf` | — | Hud | `UIManager.HideHud(FollowTarget)` | 必须最后一步；HUD 没有 `CloseSelf` 语义 |
| `SetLayerVisibility` | `layer`, `visible` | Panel | 整层显隐 | 与门面同名（`UIManager.cs:513`）；全局副作用，编辑器警告 |
| `SetLayerInteractive` | `layer`, `interactive` | Panel | 整层交互 | 同上 |
| `SetActive` | `nodeName`, `bool` | 全部 | 子树显隐 | 页签的最小原语 |
| `SelectTab` | `groupName`, `index` | 全部 | 一组节点只显示一个 | 比 `SetActive` 更不易配错 |
| `Custom` | `actionId`, `payload` | 全部 | 发 keyed 消息 | 见下 |

**执行规则**

- 按列表顺序串行；异步动作 `await`；一个动作抛异常 → 记 Error、停止当前链，面板保持打开（不半拆）。
- `CloseSelf` / `CloseWithResult` / `HideSelf` 必须最后（校验器 `UI-AUTH-*` 报错）；`CloseAll` 同理建议最后（它会连带关掉 HUD/Tip）。
- `Replace` 内部先等待当前面板关闭，再打开目标；它单独成链（同一按钮上不再挂其它动作），避免与「`CloseSelf` 必须最后」的规则冲突。
- `Open` / `Push` 的实现固定为 `OpenAsync<UIPanelBase>` / `PushAsync<UIPanelBase>`（§1.5.2）：不同具体类型的 C# 面板正常；同具体类型、不同地址会 `RegisterPanel` Error + 回滚旧面板；声明式目标会被校验器拒绝。动作不带 `userData`，需要传数据的 C# 面板导航走 `Custom`。
- `ShowDialog` 会 `await` 到用户选择或对话框关闭；面板保持打开。面板在等待期间被关闭时 `UIActionSession` 失效，续体不得再触碰面板。
- 每次打开新建 `UIActionSession`；关闭/回池即失效，异步动作续体在失效后不再触碰面板（ABA 防护，参照核心 `UIManagerImpl` 的在途/回滚思路）。
- 所有动作在主线程执行；动作链只由按钮点击触发，不在每帧路径上。
- HUD 的 `Raycaster` 由 `UIHudItem` 默认关闭，HUD 上挂按钮通常不可点；`HideSelf` 主要给「点击 HUD 外部/倒计时结束」这类程序触发留出口，策划不应假设 HUD 可交互。

**自定义动作出口**

```csharp
public readonly struct UIAuthoringActionMessage
{
    public readonly string ActionId;
    public readonly string Payload;
    public readonly UIViewBase View;   // 面板 / HUD / 对话框的公共基类
}
// 发布：MessageManager.Publish<string, UIAuthoringActionMessage>(actionId, msg)

public readonly struct UIAuthoringDialogResultMessage
{
    public readonly string ActionId;   // ShowDialog 动作的 actionId
    public readonly string ResultId;   // 声明式对话框的按钮结果 ID；null = 未选择
}
// 发布：MessageManager.Publish<string, UIAuthoringDialogResultMessage>(actionId, msg)
```

- 项目按 `actionId` 订阅（keyed 消息，`MessageManager.cs:49` 有该重载）；框架不做通配符、不做反射分发、不自建第二事件系统。
- **订阅方不得跨回调保存 `View` / 对话框引用**：面板/HUD/对话框关闭或回池后引用失效；消息只在处理函数内使用。
- 「发消息」由 `Custom` 承载，不另立内置动作——避免把领域动词塞进动作词汇表。`ShowDialog` 的结果用独立的 `UIAuthoringDialogResultMessage`，不与 `Custom` 混用。

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

| 定义里的绑定种类 | 目标组件 | 期望类型 | 双向 |
|---|---|---|---|
| `Text` | `TMP_Text` / UGUI `Text` | `IReactiveProperty<string>` | — |
| `Sprite` | `Image` | `IReactiveProperty<Sprite>` | — |
| `Color` | `Image` | `IReactiveProperty<Color>` | — |
| `FillAmount` | `Image` | `IReactiveProperty<float>` | — |
| `Slider` | `Slider` | `IReactiveProperty<float>` | ✅（源实现 `IReactivePropertyWriter<float>`） |
| `Toggle` | `Toggle` | `IReactiveProperty<bool>` | ✅（源实现 `IReactivePropertyWriter<bool>`） |
| `Active` | `GameObject` | `IReactiveProperty<bool>` | — |

- 双向复用核心 `UIBinder.BindTwoWay`（`UIBinder.cs:156-172`）；`ReactiveProperty<T>` 同时实现 `IReactiveProperty<T>` 与 `IReactivePropertyWriter<T>`（`ReactiveProperty.cs:23`）。源只读时降级为单向并告警。
- 同一套 provider 服务于 Panel / Hud / Dialog：三者都是 `UIViewBase` 派生，绑定适配相同；Dialog 的按钮结果走 `CloseWithResult`，不走数据绑定。
- 缺 provider / 缺 key / 类型不匹配 → **只告警、不抛**；静态绑定照常生效。
- 注册表是静态状态：必须提供会话级复位（`SubsystemRegistration`），测试 fixture 必须复位（仓库既有纪律）。
- 所有权：provider 归项目，`Unregister` 不 Dispose；面板只持有订阅句柄，回池释放。
- GC：绑定在打开时一次性建立；值变化走强类型回调，无每帧分配；定义列表遍历一次，不缓存反射。

### 7.5 编辑器工具

- **定义 Inspector**：列表编辑、节点名候选（从当前预制体收集）、动作参数校验；阶段 3 增加数据键候选与 `twoWay` 资格提示。
- **校验器**：纯规则引擎 + IMGUI 窗口/菜单 `Tools/XFramework/UI Authoring/Validate Declarative Panels`；规则号 `UI-AUTH-*`（面板）/ `UI-AUTH-HUD-*` / `UI-AUTH-DIALOG-*`，与核心 `UI-PREFAB-*` 分开。
- **不能复用核心校验器**：它在 `Venusy609.Xframework.Editor` 里全部 `internal`，`AssemblyInfo.cs` 只对 `Venusy609.Xframework.Editor.Tests` 开放。可选路：① 扩展侧重复一小份规则引擎（推荐，规则只看定义与预制体）；② 核心开公开接缝（公开 API 变更，单独立项）；③ 不做（不建议）。
- **核心校验器的射程不同**：面板/Popup/Dialog 跑 `001`–`008`；HUD 只跑 `001`/`008`；Tip 跑 `001`/`008`/`009`（§1.5.4）。扩展校验器要补 HUD 的约定命名/深度/重名/根 RectTransform 规则，不能假设核心已经查过。
- 规则草案：
  - 定义缺失 / 预制体引用为空；`Kind` 与根组件不匹配；
  - 节点不存在 / 重名 / 深度超 5；
  - 文本目标没有 `TMP_Text` 或 `Text`；
  - 动作按钮节点不存在 / 没有 `Button`；
  - `CloseSelf` / `CloseWithResult` / `HideSelf` 不在末尾；
  - `Open` / `Push` 目标是声明式面板——需要把 `assetPath` 解析到预制体（YooAsset 地址 provider）；解析不到时只能 Warning，不能静默通过；
  - `SetActive` / `SelectTab` 目标不存在；页签索引越界；
  - `Custom` 的 `actionId` 为空；
  - 全局动作（`CloseAll` / `SetLayerVisibility` / `SetLayerInteractive`）警告；
  - HUD：`CloseSelf` 报错（应用 `HideSelf`）；根 RectTransform、节点约定、`HideSelf` 末尾；
  - Dialog：`CloseWithResult` 的 `resultId` 非空且同一定义内不重复；外部关闭返回 `null` 的提示；
  - 数据键为空 / 双向 kind 不匹配 / 定义资产未被 YooAsset 采集（阶段 3）。
- 使用方文档（扩展包 README）要求：**两个校验器都跑**——核心校验预制体结构，扩展校验声明式定义；HUD 的约定命名核心不查，必须由扩展校验器覆盖。
- **扩展包 `Samples~` 登记**：把 `Assets/XFramework.UIAuthoring/Samples~` 加进 `Tools/check-docs.ps1` 的样例根列表（当前 `:513-518` 只有核心与 Luban），否则模板/示例代码没有编译通道。

### 7.6 与核心 API 的契合与使用限制

| 限制 | 证据 | 影响 | 缓解 |
|---|---|---|---|
| 单类型单实例 | `UIManagerImpl.cs:698,1996-2013` | 同时只能开一个声明式面板；同具体类型、不同地址的并发打开会 Error + 回滚旧实例 | 单根 + 子页；需要并存用 C# 面板；校验器禁止声明式→声明式 `Open/Push` |
| `Push` 同身份 | `UIManagerImpl.cs:923-928` | `Push` 命中同地址只重聚焦返回现有实例，**不回滚**；`OpenAsync<UIPanelBase>` 命中同具体类型不同地址才走回滚 | 文档分开写「重聚焦」与「回滚」；不要用 `OpenAsync<UIPanelBase>` 打开第二个声明式地址 |
| `IsOpen<T>` / `GetPanel<T>` 歧义 | 同一类型 | 只能判断「是否有声明式面板开着」，不能区分地址 | 用 `OpenAsync` 返回的实例；关闭用 `CloseAsync(panel)` |
| 预加载按类型 | `UIManagerImpl.cs:1428-1446` | 只能预热一个声明式地址；第二次同类型静默忽略新地址 | 不依赖 `PreloadAsync<UIDeclarativePanel>`；需要换地址先 `ForgetPreload<T>()`；或 `AssetManager.PreloadAllAsync` |
| 面板消息只带类型 | `PanelOpenedMessage.cs:10-25` | 订阅方无法区分地址；重聚焦不发消息 | 用扩展自己的 `UIAuthoringActionMessage` / 面板实例；不要依赖消息做导航记账 |
| 控制器关闭后无地址 | `IUIController.cs:65` | `OnAfterClose` 拿不到是哪个面板 | 在 `OnBeforeClose` 记录 `panel.AssetPath` |
| `Dispose` 不补跑 `OnClose` | 核心 README 已知限制 | 清理必须放 `OnPoolRecycle` | 扩展面板遵守；测试 `Destroy` 路径 |
| 弹窗 | `ShowPopupAsync` 读面板 Rect；`closeOnClickOutside` 只在内置 `UIManagerImpl` 生效 | 声明式面板可做锚定弹窗，但计入单根；弹窗与主面板不能同时都是声明式 | 需要并存时用 C# 面板，或阶段 4 的 `UIDeclarativePopupPanel` |
| 对话框 | `ShowDialogAsync<TPanel,TResult>` 要求 `UIDialogPanel<TResult>` | `UIDeclarativeDialogPanel` 固定 `string` 结果；自定义 `TResult` 仍要 C# | 声明式只覆盖字符串结果；自定义结果写 C# `UIDialogPanel<TResult>` |
| HUD 无 `userData` / 不入面板字典 | `ShowHudAsync<T>` 签名；`UIHudManager.cs:202-224` | 打开时拿不到数据；`IsOpen<T>`/`Panels` 不统计 HUD；`CloseAllAsync` 会 detach 全部 HUD | 动态数据走阶段 3 provider；HUD 用 `HideHud(target)` 关闭；校验器补 HUD 规则 |
| Tip 无本地化/队列/层参数 | `IUITipProvider.cs:21-33`；`UITipConfig.cs:9-47` | Tip 文本是原始字符串；多条同时播；固定 `Layer_Tip`/31000 | 调用方先 `LocalizationManager.Get(key)`；需要队列/单条关闭时换自定义 `IUITipProvider` |
| `Time.timeScale = 0` | 核心 README 已知限制 | 冻结整条 UI 每帧通路（面板 `OnUpdate`、HUD 跟随、Tip 动画） | 声明式面板无每帧逻辑；HUD/Tip 会停；需要时手动 `UIManager.Update`（会与驱动器叠加） |

### 7.7 四档需求逐条兑现

| 档位 | 阶段 | 机制 | 验收 |
|---|---|---|---|
| A 只搭预制体 | 1 | 模板 + 校验器 + 交接清单 | 模板扫描 0 Error；策划按模板加子页不改根结构 |
| B 静态展示页 | 2a | `UIDeclarativePanel` + 文本绑定 + `CloseSelf` | 预制体 + 定义即可打开/关闭，无专属 C# |
| C 简单交互 | 2a | 内置动作 + `Custom` 消息 | 按钮关闭/跳转/页签/Tip 全部由定义表达 |
| D 动态数据 | 3 | `IUIDataProvider` + 数据绑定表 | 程序只写一个 provider；开关/滑条双向 |

**面板之外的三形态**：

| 形态 | 阶段 | 机制 | 验收 |
|---|---|---|---|
| HUD | 2b/3 | `UIDeclarativeHudItem` + `HideSelf` / 文本 / 数据键 | 策划做 HUD 预制体 + 定义；程序只写 `ShowHudAsync`；静态文本无 C#，动态值走 provider |
| Dialog | 2a | `UIDeclarativeDialogPanel : UIDialogPanel<string>` + `CloseWithResult` | 确认/取消/三选一由定义表达；自定义 `TResult` 仍写 C# |
| Popup | 4（可选） | 复用面板或 `UIDeclarativePopupPanel` | 单根限制下主面板 + 弹窗并存需独立类型 |

### 7.8 HUD / Tip / Popup / Dialog 的现状与目标态

| 形态 | 现状（不装扩展包） | 目标态 | 阶段 |
|---|---|---|---|
| HUD | 程序写 `XxxHudItem : UIHudItem`；策划搭预制体 | `UIDeclarativeHudItem` + 定义 | 2b/3 |
| Tip | 策划做 Tip 预制体；程序 `ShowTipAsync` | 不变（已可独立完成） | 现在 |
| Popup | 程序写 `UIPanelBase` 子类；`ShowPopupAsync` | 复用 `UIDeclarativePanel` 或阶段 4 独立类型 | 2a/4 |
| Dialog | 程序写 `UIDialogPanel<TResult>` 子类 | `UIDeclarativeDialogPanel : UIDialogPanel<string>` | 2a |

### 7.9 `UIDeclarativeHudItem` 设计

- 基类 `UIHudItem : UIViewBase`；预制体根挂 `UIDeclarativeHudItem`，继承 `[RequireComponent(CanvasGroup)]`；根需要 `RectTransform`。
- `OnOpenImpl`：先 `base.OnOpenImpl(userData)`（缓存相机/CanvasGroup、关 Raycaster），再按定义绑定文本/动作；`OnCloseImpl`：先解绑再 `base.OnCloseImpl(immediate)`；`OnPoolRecycle` 走基类释放 `Track`。
- 不重写 `OnUpdate`；跟随、屏幕后隐藏、目标丢失回收全由 `UIHudItem` 负责。
- 动作：`HideSelf`（`UIManager.HideHud(FollowTarget)`）、`SetActive`、`SelectTab`、`ShowTip`、`Custom`；`CloseSelf` 报错。`Raycaster` 默认关闭，HUD 按钮通常不可点。
- 无 `userData`；动态数据走阶段 3 provider；静态文本可直接写定义。
- 校验：核心只覆盖 `001/008`；扩展补 HUD 的节点存在/重名/深度、根 RectTransform、定义与动作规则（`UI-AUTH-HUD-*`）。
- 模板 `Samples~/Templates/Hud`；示例 `Samples~/DeclarativeDemo` 增加跟随目标的 HUD。

### 7.10 `UIDeclarativeDialogPanel` 设计

- `public sealed class UIDeclarativeDialogPanel : UIDialogPanel<string>`；结果类型固定 `string`（按钮结果 ID）。Unity 不能把开放泛型 `MonoBehaviour` 挂到预制体上，单一具体类型才能零代码。
- 按钮动作 `CloseWithResult(resultId)`：`SetResult(resultId)` → `CloseSelfAsync()`；必须最后一步；`resultId` 非空；同一对话框内不重复。
- 外部关闭（`CloseAsync`/`CloseAllAsync`/`Dispose`）返回 `null`，项目把 `null` 当「未选择/取消」。
- `ShowDialogAsync<UIDeclarativeDialogPanel, string>(address, ...)` 满足 `where TPanel : UIDialogPanel<string>`；遮罩、结果通道、回池兜底全部复用核心。
- 独立具体类型：与 `UIDeclarativePanel` 可以同时打开；同一时刻只能有一个声明式对话框；重复 `ShowDialogAsync` 复用现有实例并重置结果通道，不同地址的第二个不会被加载。
- 自定义 `TResult`（枚举/结构体/业务对象）仍写 C# `UIDialogPanel<TResult>` 子类。
- 面板按钮打开对话框：动作 `ShowDialog(assetPath, actionId)`，结果发布 `UIAuthoringDialogResultMessage`；面板保持打开，`UIActionSession` 失效后不得再触碰面板。
- 模板 `Samples~/Templates/Dialog`；示例：确认/取消、三选一、带 `ShowTip` 的复合链。

---

## 八、分阶段实施

### 8.1 阶段 1：文档 + 受限模板

**已裁定**：Q2 批准重开「预制体模板」；向导、面板脚本与地址常量生成维持暂不做。

**模板与根组件的配对（本轮审计新增）**：预制体根节点必须挂 `UIPanelBase` 子类；「只有模板、没有 Runtime 代码」会留下 Missing Script。推荐模板与阶段 2a 的 `UIDeclarativePanel` 一起发；若坚持阶段 1 只发模板，则随包发一个最小 `UIEmptyPanel : UIPanelBase` 占位，或只写结构配方、不随包发预制体。

**交付物**

- 扩展包 README 的模板节（固定根结构、允许修改的字段/子节点、禁止修改项、与 `UI-PREFAB-001..009` 的对应）；本文件只记录设计结论与证据。
- `Assets/XFramework.UIAuthoring/Samples~/Templates/` 下的基础 Prefab + Prefab Variant：面板/HUD/Dialog 三种；配套最小运行时骨架（或与阶段 2a 一起发）。
- `package.json`（含 `samples` 数组与 `license`）、README、CHANGELOG、`LICENSE.md` 最小骨架。
- 把 `Assets/XFramework.UIAuthoring/Samples~` 登记到 `Tools/check-docs.ps1` 的样例根列表。
- 使用方文档：模板是起点，不是运行时强制结构。

**验收**

- 核心菜单 `Tools/XFramework/UI/Validate UI Prefabs` 扫描模板：0 Error；Warning 逐条有解释。
- 策划按模板创建 Variant、添加子页、命名节点，不需要改根结构。
- 程序仅写面板类 + 一次打开调用（阶段 1 若仍保留 C# 壳，这是与阶段 2a 的分界）。

**测试**

- 核心校验器既有 36 例继续作为规则基线；扩展包若建骨架，加 EditMode 结构测试（对模板预制体的根组件/命名/深度做自有断言，不复用核心 internal）。
- 不引入 YooAsset 真加载；模板验收以编辑器扫描 + 手工打开为主。

### 8.2 阶段 2a：声明式面板 + 声明式对话框 + 动作

**前置裁定**：Q1 已裁定接受单根；Q3 定义资产形态推荐 SO（实施前用 P2 探针确认）。

**交付物**

- 扩展包完整骨架：package.json（含 samples/license）、README、CHANGELOG、LICENSE.md、Runtime/Editor/Tests asmdefs、`AssemblyInfo.cs`。
- `UIDeclarativePanel`、`UIDeclarativeDialogPanel`、`UIPanelDefinition`（含 `Kind`）、`UIActionKind` / `UIAction` / `UITextBinding` / `UITabGroup`、`UIActionSession`、`UIAuthoringActionMessage`、`UIAuthoringDialogResultMessage`。
- 动作集：`CloseSelf` / `CloseAll` / `Replace` / `Open` / `Push` / `Pop` / `ShowTip` / `ShowDialog` / `SetLayerVisibility` / `SetLayerInteractive` / `SetActive` / `SelectTab` / `Custom`；Dialog 专用 `CloseWithResult`。
- 定义 Inspector + `UIAuthoringValidator`（`UI-AUTH-*` / `UI-AUTH-DIALOG-*`）+ 校验窗口。
- `Samples~/DeclarativeDemo/`：静态页、按钮关闭/跳转、开关、`SetActive` 页签、`ShowTip`、`ShowDialog`、`Custom` 消息。
- 使用方 README 的「使用限制」：单根、`IsOpen` 歧义、预加载、弹窗、对话框结果限 `string`。

**验收**

- 策划在一个预制体内完成：静态展示、按钮关闭/跳转/发消息、开关切换、页签切换；该页不需要任何专属 C#。
- 策划在对话框定义里用 `CloseWithResult` 表达确认/取消/三选一；`ShowDialogAsync<UIDeclarativeDialogPanel,string>` 返回结果 ID。
- 打开该页只需一次 C# 调用（或由另一个 C# 流程打开）；声明式面板之间不直接 `Push`。
- 直接调用 `UIManager.OpenAsync<UIDeclarativePanel>(第二个地址)` 的行为被 README 明确标为不支持；扩展入口 `UIAuthoringNavigation` 有防护。
- 核心包 `git diff -- Assets/XFramework` 为空；核心测试全绿。
- 无每帧分配；重复开合 100 次订阅数不增长；对话框结果通道每轮重置。
- 扩展校验器对缺失定义、缺失按钮/节点、非法 `SetActive`、`CloseSelf`/`CloseWithResult` 非末尾、`Push` 到声明式目标、`resultId` 为空/重复等报错。

**测试**

- EditMode：定义/动作计划/校验规则/节点目录的纯逻辑；fake `IUIActionRuntime`；无需 YooAsset。
- PlayMode：`UIDeclarativePanel` 的打开/关闭/回池、按钮/开关动作、`UIActionSession` 的 ABA（打开中关闭再重开）、`UIDeclarativeDialogPanel` 的结果通道与 `ShowDialog` 动作、provider 尚未接入时的静态绑定；用 fake `IUIManager` 测导航。
- 真实核心集成（`UIManager.OpenAsync` / `ShowDialogAsync` + YooAsset）用一次性探针，不做日常单测；文档登记为「覆盖缺口」。
- 包内自设守卫全部落地；`run-tests` 全量覆盖。

### 8.3 阶段 2b：声明式 HUD（静态）

**交付物**

- `UIDeclarativeHudItem`、HUD 动作 `HideSelf`、HUD 定义规则（`UI-AUTH-HUD-*`）。
- 模板 `Samples~/Templates/Hud`；示例 `Samples~/DeclarativeDemo` 增加跟随目标的 HUD。
- 使用方 README：HUD 无 `userData`、不入栈、固定层、池化、`CloseAllAsync` 连带回收。

**验收**

- 策划做 HUD 预制体 + 定义；程序只写 `ShowHudAsync<UIDeclarativeHudItem>`。
- 静态文本/页签/`HideSelf` 由定义表达；动态数据留阶段 3。
- 核心校验器对 HUD 只跑 001/008；扩展校验器补节点/深度/重名/根 RectTransform。

**测试**

- PlayMode：HUD 打开/关闭/回池、`HideSelf`、目标丢失、`CloseAllAsync`、池化后订阅不增长。
- 不引入真实 YooAsset；用 fake provider / `UIHudManagerImpl` 的既有测试缝。

### 8.4 阶段 3：数据源接缝 + 三形态数据绑定

**交付物**

- `IUIDataProvider` / `UIDataProviderRegistry` / 绑定适配（Text/TMP/Image/Slider/Toggle/Active；双向 Slider/Toggle）。
- Panel / HUD / Dialog 的 `DataBindings`（Dialog 只用于文本/图片等显示，结果仍走 `CloseWithResult`）。
- 本地化静态绑定（TMP + UGUI Text）与语言切换测试。
- 示例 provider：一个 `ReactiveProperty<T>` 集合，演示文本、图片、滑条、开关、双向写回；HUD 名字/血条示例。
- 编辑器校验完善：数据键缺失运行期 Warning、类型不匹配 Warning、`TwoWay` 限制、定义资产 YooAsset 采集、共享定义的引用扫描。
- 文档：provider 编写指南、注册/注销/会话复位、所有权与 Dispose 顺序、类型擦除与 GC 说明。
- 真实 YooAsset EditorSimulate 探针：预制体引用定义资产，验证同包/跨包两种采集方式。

**验收**

- 程序只写一个 provider + 一次注册；策划在定义资产里配绑定表；页面/HUD 无 C#。
- 开合 100 次后订阅与 provider 实例不增长；`Dispose` 顺序正确。
- 缺失 key/类型不匹配只 Warning、不崩；provider 回调抛异常被隔离。
- 定义资产在真实 YooAsset 加载路径下不为 null；若失败，用户在「内联字段 fallback」与「定义资产必须同包采集」之间裁定。

### 8.5 阶段 4（可选）：声明式 Popup 独立类型

- `UIDeclarativePopupPanel : UIPanelBase` + `ShowPopup` 动作（锚点节点名、address、direction/align、closeOnClickOutside）。
- 只有出现「主面板 + 弹窗并存」的真实需求才开；否则复用 `UIDeclarativePanel` 并接受单根限制。

### 8.6 裁定点与门禁

| 时点 | 触发的条目 | 原因 |
|---|---|---|
| 阶段 1 开工前 | 「暂不做：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成」中的**预制体模板** | 已由 2026-11-08 用户裁定重开 |
| 阶段 2a 开工前（Q3） | 定义资产形态（SO vs 内联） | 用 P2 探针确认；SO 失败回退内联 |
| 阶段 2a 开工前（Q4/Q8） | 动作出口与动作模型 A | 推荐 keyed message + 基类型打开；不涉及核心 API |
| 阶段 3 开工前（仅当 P2 失败） | 定义资产形态回退 | 实现形态裁定 |
| 任何阶段，若用户要求条件/变量/游戏动词/向导/代码生成 | 「不做：可视化 UI 设计器」或「暂不做：向导/脚本/地址生成」 | 超出本方案边界，需重新论证 |

---

## 九、与既有裁定的关系

| 既有条目 | 现状 | 本方案处理 | 用户裁定 |
|---|---|---|---|
| 暂不做：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成（2026-11-08） | 一条合并条目 | **只重开模板**；向导、脚本/地址生成维持暂不做 | 已裁定（Q2） |
| 不做：可视化 UI 设计器（2026-11-08） | 结构性不做 | 维持；本方案不是编辑器 | 否 |
| 暂不做：保存即校验 / 构建前门禁 / 源码地址扫描 / 自动修复（2026-11-08） | 暂不做 | 维持；代价与缺口见 §5.3 | 否（用户本轮确认） |
| 核心包零改动 | 本轮方向 | 维持；Q1 已裁定接受单根，不引入身份键 | 已裁定（Q1） |
| FairyGUI：不接后端 / 不做平级包 | 结构性结论 | 维持；不冲突 | 否 |

**台账落盘文本**（按 `Documentation/Modules/README.md` 格式；不拆、不移动 `UI.md:132` 合并条目）：

- `UI.md:144` 的「待裁」条目改为「未做」，记录 2026-11-08 用户裁定：接受单根、只重开模板、HUD/Dialog 设计纳入、实施待排。
- 台账末尾追加：
  - `- 已推翻：**~~暂不做：预制体模板~~**（原条目「暂不做：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成」的模板一项；2026-11-08 · 用户裁定）——被 2026-11-08『未做：受限预制体模板随扩展包分发』取代；向导、面板脚本、地址常量生成维持暂不做。`
  - `- 未做：**受限预制体模板**（2026-11-08 · 用户裁定）——扩展包 `Samples~/Templates` 随附面板/HUD/Dialog 三种受限 Prefab Variant；向导、面板脚本、地址常量生成维持暂不做。`
- 状态词只用六个；`已推翻` 保留原文并写「被 `<日期>`『`<新结论>`』取代」；条目永不移动、不分小节（`Modules/README.md:29-35,45,53`）。

---

## 十、未验证事实与探针清单

| 探针 | 问题 | 状态 | 何时跑 / 影响 |
|---|---|---|---|
| P1 单类型多地址 | `OpenAsync<UIDeclarativePanel>` 开第二个地址的实际行为 | **源码已坐实**（§2.3），未跑 Unity | 阶段 2a 前用一条 PlayMode 用例固化；README 写明「不支持」 |
| P2 YooAsset 定义资产采集 | 预制体引用的 `UIPanelDefinition` 是否随包/依赖加载 | `[未实测]` | 阶段 3 必跑：同包、跨包、依赖采集三种配置 |
| P3 Prefab Variant 模板分发 | `Samples~/Templates` 中的基础 Prefab + Variant 能否在消费方项目导入并保持脚本引用 | `[未实测]` | 阶段 1 若发模板，跑一次裸消费方导入探针 |
| P4 UI Toolkit | 是否值得另立换栈评估 | `[未实测]`，本文落盘前从未评估 | 仅当用户认真考虑换栈时做 30–60 分钟探针（UIDocument、Input System only、运行时绑定、TMP、GC、与 `UIManager` 共存） |
| P5 TMP 绑定 | TMP Essentials 未导入时能否测 | 核心已有覆盖缺口 | 阶段 3 手工探针；自动化只测非 TMP 路径 |
| P6 IL2CPP/AOT | `IUIDataProvider.TryGet<T>` 的泛型实例化、无反射 | `[未实测]` | 阶段 3 至少一次 IL2CPP 构建；必要时 `link.xml` |
| P7 真实核心集成 | 扩展测试无法注入核心 `UIManager.PanelFactoryFactory`（internal） | 已知边界 | 用 fake runtime 测逻辑；真实 `UIManager.OpenAsync` 用一次性 EditorSimulate 探针 |
| P8 HUD 校验射程 | 核心校验器对 HUD 实际只跑 `001/008`；扩展 HUD 规则是否覆盖节点/深度/重名/根 RectTransform | **源码已坐实**（§1.5.4），扩展规则未实现 | 阶段 2b 前用一条 EditMode 用例固化；面板与 HUD 规则分开 |
| P9 Tip 本地化 | Tip 只有原始字符串，没有本地化重载 | **源码已坐实**（`TipConfig` 无 key） | 阶段 3 手工探针：调用方 `LocalizationManager.Get` 后显示；不纳入自动化 |
| P10 声明式 Popup | 复用 `UIDeclarativePanel` 时与主面板抢单根；遮罩/捕获层行为 | **源码已坐实**（§2.3/§2.5），未跑 Unity | 阶段 2a 用 PlayMode 用例固化「同时开面板+弹窗会怎样」；阶段 4 再决定独立类型 |
| P11 扩展包 Samples~ 登记 | `check-docs.ps1` 样例根是硬编码列表；新包不登记就没有编译通道 | **源码已坐实**（`Tools/check-docs.ps1:513-518`） | 阶段 1 建包时同步改 `Tools/check-docs.ps1`；跑一次 `check-docs -Enforce` 验证 |

---

## 十一、开放裁定点

### Q1（已裁定）：核心零改动 vs 面板身份键

- **已选 A. 接受单根限制**（2026-11-08 · 用户裁定）：同一时刻只有一个声明式根面板；多页用预制体内子页 + `SetActive`/`SelectTab`；需要并存的界面用 C# 面板。零核心改动。
- B. 核心加可选身份键：未选；`(Type, identity)` 记账需要核心行为/公开 API 变更，重开需单独评审；身份接口是 public 还是 internal + `InternalsVisibleTo` 也要定。

### Q2（已裁定）：是否重开「暂不做：预制体模板」

- **已选 B. 仅重开模板一项**（2026-11-08 · 用户裁定）：扩展包 `Samples~/Templates` 随附面板/HUD/Dialog 三种受限 Prefab Variant；不做向导、不生成脚本/地址常量。
- A. 不重开：不随包发 Prefab Variant；项目自建模板（未选）。

### Q3（待实施前确认）：定义资产形态

- **A. ScriptableObject `UIPanelDefinition`（推荐）**：可复用、可独立校验、预制体 diff 干净；需 YooAsset 依赖采集（P2）。
- **B. 组件内联序列化字段**：随预制体走、无额外资产；预制体 diff 大、不可复用。
- 若选 A，接受 P2 探针失败时回退 B 的可能。

### Q4（待实施前确认）：自定义动作出口

- **A. keyed `UIAuthoringActionMessage`（推荐）**：经 `MessageManager.Publish<string, UIAuthoringActionMessage>(actionId, msg)`；无反射、无第二事件系统；项目按 id 订阅，无通配符。
- **B. 静态 handler 注册表**：`Register(actionId, handler)`；更直接，但要自管生命周期、会话复位与异常隔离。
- 另需确认：「发消息」由 `Custom` 承载（推荐），不另立内置动作；对话框结果用 `UIAuthoringDialogResultMessage`。

### Q5（已裁定）：HUD 是否进入声明式

- **已选：本轮设计 `UIDeclarativeHudItem`**（2026-11-08 · 用户裁定）；实施按阶段 2b/3。HUD 基类 `UIHudItem : UIViewBase` 是独立类型，不与面板单根冲突。

### Q6（已裁定）：Dialog 是否声明式

- **已选：本轮设计 `UIDeclarativeDialogPanel : UIDialogPanel<string>`**（2026-11-08 · 用户裁定）；结果限 `string`；自定义 `TResult` 仍走 C#。

### Q7（可选，阶段 4）：声明式 Popup 是否独立成类型

- 复用 `UIDeclarativePanel` 会与主面板抢单根；若需要「主面板 + 弹窗」并存，增加 `UIDeclarativePopupPanel`（独立具体类型）。

### Q8（待实施前确认）：动作模型

- **A（推荐）**：`Open`/`Push` 用 `OpenAsync<UIPanelBase>` / `PushAsync<UIPanelBase>`；声明式目标由校验器拒绝；C# 面板导航不传 `userData`，需要数据的走 `Custom`。
- B：加 `panelType` + 核心非泛型入口（公开 API 变更）。
- C：删掉 `Open`/`Push`，导航交给 `Custom`。

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
| `Assets/XFramework/Runtime/UI/View/UIHudItem.cs` | HUD 基类 `UIViewBase`、`CanvasGroup`、跟随目标、`OnOpenImpl/OnCloseImpl/OnUpdate` |
| `Assets/XFramework/Runtime/UI/UIHudManager.cs` | `AttachAsync`/`Detach`/`DetachAll`、固定 `Layer_HUD`/30000、池化回收 |
| `Assets/XFramework/Runtime/UI/Tip/UITipItem.cs` | Tip 预制体要求（`CanvasGroup`、根 `RectTransform`、`TMP_Text`）、`Begin/Tick/StopImmediate` |
| `Assets/XFramework/Runtime/UI/Tip/UITipConfig.cs` | `TipConfig` 字段与默认值；`Vector3? WorldPos` |
| `Assets/XFramework/Runtime/UI/Tip/IUITipProvider.cs` | Tip provider 契约、每帧由 `UIManager.Update` 驱动 |
| `Assets/XFramework/Runtime/UI/IUIHudProvider.cs` | HUD provider 契约、同 target 单实例 |
| `Assets/XFramework/Runtime/UI/View/UIDialogPanel.cs` | `UIDialogPanel<TResult>` 结果通道、`SetResult`/`WaitResultAsync`/回池兜底 |
| `Assets/XFramework/Editor/UI/` | 九条预制体规则、扫描器、窗口、地址 provider |
| `Assets/XFramework/Editor/UI/UIPrefabCandidateDetector.cs` | 候选预制体 = `UIViewBase` 派生或 `UITipItem` |
| `Assets/XFramework/Editor/UI/UIPrefabValidator.cs` | `002`–`007` 只对 `UIPanelBase` 根执行；HUD/Tip 只跑 001/008(/009) |
| `Assets/XFramework/Editor/AssemblyInfo.cs` | `InternalsVisibleTo` 只对 Editor.Tests |
| `Assets/XFramework/Tests/Runtime/Architecture/EditorPublicApiBaselineTests.cs` | Editor 公开面基线 |
| `Assets/XFramework.Luban/` | 扩展包形状、测试/守卫、生成式工具的写盘协议；`Samples~` 登记先例 |
| `Tools/check-docs.ps1` | 六道门禁；样例根列表 `:513-518`（核心 + Luban） |
| `Documentation/Modules/README.md` | 台账条目格式与六状态词 |
| `Documentation/Modules/UI.md:132-136` | 2026-11-08 三条裁定的原文（`:130` 是校验器条目） |
| `Documentation/FairyGUI.md` | 第二套栈的结构性理由与 §2.4 受限模板建议 |

**第三方来源**（评估时保留 URL；本仓未读 UGF/Loxodon/Unity Weld 源码，均为公开 README/文档）：

- UGF：`https://deepwiki.com/EllanJiang/UnityGameFramework/5.1-ui-system`
- Loxodon：`https://github.com/vovgou/loxodon-framework`
- Unity Weld：`https://github.com/Real-Serious-Games/Unity-Weld`
- UI Toolkit Runtime Binding：`https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-get-started-runtime-binding.html`
