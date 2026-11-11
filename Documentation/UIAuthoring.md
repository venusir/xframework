# UI 制作（面向策划）—— 评估与方案（维护侧）

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> 使用方文档在 `Assets/XFramework/Runtime/UI/README.md`（核心）与 `Assets/XFramework.UIAuthoring/README.md`（扩展包）——用法、已知限制、设计取舍一律留在那里，本文件不复述。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> **状态**：评估已落盘；2026-11-08 完成全面审计并得到用户裁定（接受单根、只重开预制体模板、HUD/Dialog 设计纳入）；**阶段 2a/2b/3/4 已实施并交付**（2a：`275f0168`→`1d2b5ce5`；2b：`e0e2679d`→`c2161bd0`；3：`fe1ae657`→`b25203cb`；4：`c231bc23`→`2e721767`；扩展包 `Assets/XFramework.UIAuthoring/`）；**一次性验收 P2/P3/P5 已于 2026-11-10 跑通**（§10.3）。模块台账条目在 `Documentation/Modules/UI.md` 的「决策台账」节。

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

1. **阶段 1**：文档 + P2/P3 探针（决定定义资产与模板能否落地）；面板/对话框模板随阶段 2a、HUD 模板随 2b；需重开 2026-11-08「暂不做：预制体模板」一项。
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
3. 声明式/对话框目标：已有同具体类型开着时同样走回滚路径；把对话框当普通面板打开还会绕过遮罩与结果通道。所以「`Open`/`Push` 目标根必须是具体 `UIPanelBase` 子类，且不得是 `UIDeclarativePanel` / 任何 `UIDialogPanel<>`」是必须的校验规则，需要按地址解析预制体。

推荐 A：保留 `Open`/`Push`，实现固定为基类型打开；`Replace` 只用于声明式→声明式；声明式/对话框目标由扩展校验器拒绝；动作模型目前没有 `userData` 字段（`OpenAsync<T>` 本身有 `object userData`，A 可以透传，只是动作模型没暴露；需要传数据的导航走 `Custom`）。B（加 `panelType` + 核心非泛型入口）会动公开 API；C（删掉 `Open`/`Push`）会让跨面板导航退化。

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
- `UIAuthoringActionMessage` 原设计持有 `UIDeclarativePanel`。审计发现 `MessageManager` 的 keyed 发布会把最后一条写进重放缓存，带 `View` 会持有已关闭/回池的面板引用；最终推荐 A'：消息不带 `View`，只带 `ActionId`/`Payload`；若需要面板上下文，改用 `EventStream<T>` 注册表（§11.5 D3 C）。
- 实际门面是 `SetLayerVisibility`，不是 `SetLayerVisible`（`UIManager.cs:513`）。
- `ShowDialogAsync` / `ShowPopupAsync` / `TipAssetPath` 只在门面，不在 `IUIManager`；`ShowTipAsync` 在 `IUIManager` 上（`IUIManager.cs:231`）。扩展的 fake `IUIManager` 只能拦到 `ShowTipAsync`，其余要包一层。
- 新扩展包 `package.json` 要带 `samples` 数组与 `LICENSE.md`（Luban 先例 `package.json:22-28`）；维护向记录留在本文件，不新建 `Documentation/Modules/XFramework.UIAuthoring.md`（先例 `Documentation/Modules/README.md:83-84`）。
- 核心 `Assets/XFramework` 下 `*.prefab` 零命中；`Samples~/Example/UISample.cs` 明确「不附带预制体」。阶段 2a/2b 的模板要从零建，P3 探针不是可选项。
- 「只有模板、没有 Runtime 代码」的模板不可用：预制体根必须挂 `UIPanelBase` 子类；没有可挂载脚本就是 Missing Script。面板/对话框模板与阶段 2a 的 `UIDeclarativePanel`/`UIDeclarativeDialogPanel` 一起发，HUD 模板与 2b 的 `UIDeclarativeHudItem` 一起发；若坚持阶段 1 只发模板，只能随包发一个最小 `UIEmptyPanel : UIPanelBase` 占位。
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
| **A. 只搭预制体** | 按规范搭预制体、命名节点、进采集 | 写面板类 + 打开调用 | 模板与交接规范；校验器手动跑 | 阶段 1（文档 + 探针）；模板随 2a/2b；阶段 2a 起可换成声明式组件 |
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

- 策划必须在 Unity 里工作；`RectTransform` 锚点对非程序确实难学（FairyGUI.md 也承认这一点）。缓解手段只能是受限模板 + 命名约定 + 校验器——即本方案阶段 2a/2b 的模板与校验器。
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
- **结论**：阶段 2a/2b 的内容，不是完整答案。

### 6.3 方案 2：声明式面板扩展包（推荐）

- **形态**：独立扩展包内提供三个声明式类型——`UIDeclarativePanel : UIPanelBase`、`UIDeclarativeHudItem : UIHudItem`、`UIDeclarativeDialogPanel : UIDialogPanel<string>`——共用 `UIPanelDefinition`（ScriptableObject）+ 封闭 UI 级动作集 + 数据源接缝。策划在预制体上挂组件、在定义资产里配文本/按钮/结果/数据键；程序只写一个 provider（阶段 3）或一次打开调用。
- **覆盖**：A（模板 + 组件）、B（文本 + CloseSelf）、C（动作 + Custom + ShowDialog）、D（数据源）；HUD（2b/3）与 Dialog（2a）单列。
- **代价**：新包与长期维护；面板单根限制（§2.3）；动作词汇表的边界要守住；YooAsset 定义资产采集需探针（P2）；三个类型共用一套绑定/动作实现，差异只在基类与动作集。
- **与裁定冲突**：不冲突于「向导/脚本生成」；**需重开「预制体模板」一项**（阶段 2a/2b 发模板）。「不做可视化设计器」不冲突——它不是编辑器，是运行时面板。
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
| **FairyGUI** | 策划脱离 Unity 的可视化编辑；组件/关系/控制器；本仓已有结构性评估 | 第二套 UI 栈；`IUIManager` 的加载/排序/遮罩/栈/HUD 原语按 UGUI 对象实现，没有可替换后端；`.fui` 二进制包与 Force Text/diff 评审冲突 | 只借 §2.4 的「受限模板」思路（阶段 2a/2b） |
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
| 与 2026-11-08 裁定冲突 | 无 | 需重开模板 | 需重开模板（阶段 2a/2b） | 需重开生成 | 需重开设计器 |
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
- `_definition` 为空时仍可打开：预制体本身就是静态内容，不绑任何东西（覆盖「只搭预制体」）。但「SO 没被采集」与「故意静态」在运行时同为 null；扩展校验器在根组件是 `UIDeclarativePanel` 且 `_definition` 为空时给 Warning，提示检查采集，而不是强制要求定义非空。
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
| `Open` | `assetPath`, `layer` | Panel | 打开一个 **C# 面板** | 实现用 `OpenAsync<UIPanelBase>`；目标不能是声明式面板/对话框（同具体类型会回滚旧实例）；动作模型无 `userData` 字段，需要数据的走 `Custom` |
| `Push` | `assetPath`, `layer` | Panel | 压栈一个 **C# 面板** | 实现用 `PushAsync<UIPanelBase>`；边界同 `Open` |
| `Pop` | — | Panel | 退栈 | 栈空时无操作 |
| `ShowTip` | `text`, `tipConfig` | 全部 | `UIManager.ShowTipAsync(text, ToTipConfig(...))` | `tipConfig` 是扩展 DTO，不能直接序列化 `TipConfig`（含 `Vector3?`） |
| `ShowDialog` | `assetPath`, `actionId` | Panel | `await ShowDialogAsync<UIDeclarativeDialogPanel, string>`，把结果作为 `UIAuthoringDialogResultMessage`（keyed by `actionId`）发布 | 单飞：已有声明式对话框打开时拒绝 + Warning，避免 `BeginDialogCycle` 假取消；结果带 Selected/Canceled/OpenFailed/ParentClosed 状态；面板保持打开 |
| `CloseWithResult` | `resultId` | Dialog | `SetResult(resultId)` → `CloseSelfAsync()` | 必须最后一步；`resultId` 不能为空；同一对话框内不重复 |
| `HideSelf` | — | Hud | `UIManager.HideHud(FollowTarget)` | 必须最后一步；HUD 没有 `CloseSelf` 语义。v1 2b 不暴露（`Raycaster` 关闭，按钮点不到）；需程序触发/`interactive` 开关后再启用 |
| `SetLayerVisibility` | `layer`, `visible` | Panel | 整层显隐 | 与门面同名（`UIManager.cs:513`）；全局副作用，编辑器警告 |
| `SetLayerInteractive` | `layer`, `interactive` | Panel | 整层交互 | 同上 |
| `SetActive` | `nodeName`, `bool` | 全部 | 子树显隐 | 页签的最小原语 |
| `SelectTab` | `groupName`, `index` | 全部 | 一组节点只显示一个 | 比 `SetActive` 更不易配错 |
| `Custom` | `actionId`, `payload` | 全部 | 发 keyed 消息 | 见下 |

**执行规则**

- 按列表顺序串行；异步动作 `await`；一个动作抛异常 → 记 Error、停止当前链，面板保持打开（不半拆）。
- `CloseSelf` / `CloseWithResult` / `HideSelf` 必须最后（校验器 `UI-AUTH-*` 报错）；`CloseAll` 同理建议最后（它会连带关掉 HUD/Tip）。
- `Replace` 内部先等待当前面板关闭，再打开目标；它单独成链（同一按钮上不再挂其它动作），避免与「`CloseSelf` 必须最后」的规则冲突。
- `Open` / `Push` 的实现固定为 `OpenAsync<UIPanelBase>` / `PushAsync<UIPanelBase>`（§1.5.2）：不同具体类型的 C# 面板正常；同具体类型、不同地址会 `RegisterPanel` Error + 回滚旧面板；声明式/对话框目标会被校验器拒绝。动作模型没有 `userData` 字段，需要传数据的 C# 面板导航走 `Custom`（`OpenAsync<T>` 本身支持 `userData`）。
- `ShowDialog` 会 `await` 到用户选择或对话框关闭；面板保持打开。已有声明式对话框打开时动作拒绝 + Warning，不启动第二个等待（否则 `BeginDialogCycle` 会把上一轮等待者以 `default` 唤醒）。面板在等待期间被关闭时 `UIActionSession` 失效，续体不得再触碰面板。
- 每次打开新建 `UIActionSession`；关闭/回池即失效，异步动作续体在失效后不再触碰面板（ABA 防护，参照核心 `UIManagerImpl` 的在途/回滚思路）。
- 所有动作在主线程执行；动作链只由按钮点击触发，不在每帧路径上。
- HUD 的 `Raycaster` 由 `UIHudItem` 默认关闭，HUD 上挂按钮通常不可点；`HideSelf` 主要给「点击 HUD 外部/倒计时结束」这类程序触发留出口，策划不应假设 HUD 可交互。

**自定义动作出口**

```csharp
public readonly struct UIAuthoringActionMessage
{
    public readonly string ActionId;
    public readonly string Payload;
    // 不带 View：keyed 发布的重放缓存会持有消息；带面板引用会留陈旧引用
}
// 发布：MessageManager.Publish<string, UIAuthoringActionMessage>(actionId, msg)

public readonly struct UIAuthoringDialogResultMessage
{
    public readonly string ActionId;   // ShowDialog 动作的 actionId
    public readonly UIAuthoringDialogStatus Status; // Selected / Canceled / OpenFailed / ParentClosed
    public readonly string ResultId;   // Selected 时的按钮结果 ID；其余为 null
}
// 发布：MessageManager.Publish<string, UIAuthoringDialogResultMessage>(actionId, msg)
```

- 项目按 `actionId` 订阅（keyed 消息，`MessageManager.cs:49` 有该重载）；框架不做通配符、不做反射分发、不自建第二事件系统。
- **动作消息不带 `View`**：`MessageManager` 的 keyed 发布会把最后一条写进重放缓存；带 `UIViewBase` 会持有已关闭/回池的面板引用。需要面板上下文时，项目用 `UIManager.GetTopPanel()` / 自己持有的引用；要强上下文就改用 `EventStream<T>` 薄注册表（§11.5 D3 C）。
- **禁止 `SubscribeBuffered`**：缓冲订阅会把最后一条动作重放给新订阅者；动作消息是一次性事件，只能用 `Subscribe`。
- 订阅方必须自行 `Dispose` 句柄（除非用 `MonoBehaviour`/`IDestroyCancellationToken` 的自动绑定）。
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
  - `Open` / `Push` 目标根必须是具体 `UIPanelBase` 子类，且不得是 `UIDeclarativePanel` / 任何 `UIDialogPanel<>`（含 `UIDeclarativeDialogPanel`）——需要把 `assetPath` 解析到预制体（YooAsset 地址 provider）；解析不到时只能 Warning，不能静默通过；
  - `SetActive` / `SelectTab` 目标不存在；页签索引越界；
  - `Custom` 的 `actionId` 为空；
  - 全局动作（`CloseAll` / `SetLayerVisibility` / `SetLayerInteractive`）警告；
  - HUD：`CloseSelf` 报错（应用 `HideSelf`）；根 RectTransform、节点约定、`HideSelf` 末尾；
  - Dialog：`CloseWithResult` 的 `resultId` 非空且同一定义内不重复；外部关闭返回 `null` 的提示；
  - 数据键为空 / 双向 kind 不匹配 / 定义资产未被 YooAsset 采集（阶段 2a，若 D2 选 SO）。
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
| HUD | 2b/3 | `UIDeclarativeHudItem` + 文本/本地化 + 数据键（2b 不暴露动作） | 策划做 HUD 预制体 + 定义；程序只写 `ShowHudAsync`；静态文本无 C#，动态值走 provider；关闭用 `HideHud(target)` |
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

**状态（2026-11-08）**：已按本节实施（阶段 2b），提交 `e0e2679d`→`c2161bd0`；只做文本/本地化，未暴露 HUD 动作。

- 基类 `UIHudItem : UIViewBase`；预制体根挂 `UIDeclarativeHudItem`，继承 `[RequireComponent(CanvasGroup)]`；根需要 `RectTransform`。
- `OnOpenImpl`：先 `base.OnOpenImpl(userData)`（缓存相机/CanvasGroup、关 Raycaster），再按定义绑定文本/动作；`OnCloseImpl`：先解绑再 `base.OnCloseImpl(immediate)`；`OnPoolRecycle` 走基类释放 `Track`。
- 不重写 `OnUpdate`；跟随、屏幕后隐藏、目标丢失回收全由 `UIHudItem` 负责。
- 2b 只做文本/本地化 + 数据键（3）；**不暴露 HUD 动作**：`Raycaster` 默认关闭，动作链只由按钮触发，`HideSelf` / `SetActive` / `SelectTab` / `ShowTip` / `Custom` 在默认 HUD 上不可触发。关闭 HUD 用核心 `HideHud(target)`（程序）。若后续确有需要，再加 `TryExecuteAction(actionId)` 程序入口或 `interactive` 开关（v1 不做）。
- 无 `userData`；动态数据走阶段 3 provider；静态文本可直接写定义。
- 校验：核心只覆盖 `001/008`；扩展补 HUD 的节点存在/重名/深度、根 RectTransform、定义与动作规则（`UI-AUTH-HUD-*`）。
- 模板 `Samples~/Templates/Hud`；示例 `Samples~/DeclarativeDemo` 增加跟随目标的 HUD。

### 7.10 `UIDeclarativeDialogPanel` 设计

- `public sealed class UIDeclarativeDialogPanel : UIDialogPanel<string>`；结果类型固定 `string`（按钮结果 ID）。Unity 不能把开放泛型 `MonoBehaviour` 挂到预制体上，单一具体类型才能零代码。
- 按钮动作 `CloseWithResult(resultId)`：`SetResult(resultId)` → `CloseSelfAsync()`；必须最后一步；`resultId` 非空；同一对话框内不重复。
- 外部关闭（`CloseAsync`/`CloseAllAsync`/`Dispose`）返回 `null`；但 `null` 也覆盖「面板打开失败 / 控制器拦截」，所以 `UIDeclarativeDialogPanel` 要记录 `WasOpened`，`ShowDialog` 动作把它与 `ResultId` 一起归类为 `UIAuthoringDialogStatus`（Selected / Canceled / OpenFailed / ParentClosed），不能只报 `null`。
- `ShowDialog` 动作单飞：已有声明式对话框打开时拒绝 + Warning，不启动第二个等待（否则 `BeginDialogCycle` 会把上一轮等待者以 `default` 唤醒，造成假取消）。
- `ShowDialogAsync<UIDeclarativeDialogPanel, string>(address, ...)` 满足 `where TPanel : UIDialogPanel<string>`；遮罩、结果通道、回池兜底全部复用核心。
- 独立具体类型：与 `UIDeclarativePanel` 可以同时打开；同一时刻只能有一个声明式对话框；核心的重复 `ShowDialogAsync` 会复用现有实例并重置结果通道，不同地址的第二个不会被加载；扩展的 `ShowDialog` 动作在此之上单飞（拒绝 + Warning）。
- 自定义 `TResult`（枚举/结构体/业务对象）仍写 C# `UIDialogPanel<TResult>` 子类。
- 面板按钮打开对话框：动作 `ShowDialog(assetPath, actionId)`，结果发布 `UIAuthoringDialogResultMessage`；面板保持打开，`UIActionSession` 失效后不得再触碰面板。
- 模板 `Samples~/Templates/Dialog`；示例：确认/取消、三选一、带 `ShowTip` 的复合链。

---

## 八、分阶段实施

### 8.1 阶段 1：文档 + P2/P3 探针（模板移到 2a/2b）

**已裁定**：Q2 批准重开「预制体模板」；D7 把面板/对话框模板放到阶段 2a、HUD 模板放到 2b（HUD 类型在 2b 才有）。阶段 1 不单独发 Prefab——没有根组件就是 Missing Script。

**交付物**

- 扩展包 README 的模板节（固定根结构、允许修改的字段/子节点、禁止修改项、与 `UI-PREFAB-001..009` 的对应）；本文件只记录设计结论与证据。
- P2 探针报告：同包/依赖/跨包 + SO 未采集负向 + 开合/回池/`UnloadPanelAssetAsync` 后 SO 生命周期；结论决定 D2 SO/内联。
- P3 探针报告：面板/对话框模板、HUD 模板分别导入裸消费方项目，脚本引用是否保持。
- 使用方文档：模板是起点，不是运行时强制结构；`Samples~` 是导入时复制，包升级不覆盖。
- 包骨架、模板资产、`Tools/check-docs.ps1` 登记移到阶段 2a/2b。

**验收**

- P2/P3 有明确结论（通过 / 失败 / 回退内联 / 改模板配方），不是「未跑」。
- 模板配方与根结构可被策划照做；阶段 1 不承诺可打开的 Prefab。

**测试**

- 两个探针各一次；不引入日常单测。

### 8.2 阶段 2a：声明式面板 + 声明式对话框 + 动作

**状态（2026-11-08）**：已实施并交付（提交 `275f0168`→`1d2b5ce5`）。扩展包 `Assets/XFramework.UIAuthoring/` 含 Runtime/Editor/Tests/Samples 全部产物；核心 `Assets/XFramework/**` 零改动。验证：PlayMode 19 例 / EditMode 17 例全绿；模板经核心预制体校验器扫描 0 Error；`check-docs.ps1 -Enforce`（含新包示例编译）通过。**一次性验收未跑**：P2 的运行时半场（真实构建 / PlayMode 同包与依赖加载）与 P3 的实际模板裸消费方导入，见 §十。

**前置裁定**：Q1 已裁定接受单根；Q3 定义资产形态推荐 SO（实施前用 P2 探针确认）。

**交付物**

- 扩展包完整骨架：package.json（含 samples/license）、README、CHANGELOG、LICENSE.md、Runtime/Editor/Tests asmdefs、`AssemblyInfo.cs`。
- `UIDeclarativePanel`、`UIDeclarativeDialogPanel`、`UIPanelDefinition`（含 `Kind`）、`UIActionKind` / `UIAction` / `UITextBinding` / `UITabGroup`、`UIActionSession`、`UIAuthoringActionMessage`、`UIAuthoringDialogResultMessage`、`UIAuthoringDialogStatus`。
- 动作集：`CloseSelf` / `CloseAll` / `Replace` / `Open` / `Push` / `Pop` / `ShowTip` / `ShowDialog` / `SetLayerVisibility` / `SetLayerInteractive` / `SetActive` / `SelectTab` / `Custom`；Dialog 专用 `CloseWithResult`。
- 定义 Inspector + `UIAuthoringValidator`（`UI-AUTH-*` / `UI-AUTH-DIALOG-*`）+ 校验窗口。
- `Samples~/Templates/Panel` + `Samples~/Templates/Dialog`（随 2a）；`Samples~/DeclarativeDemo/`：静态页、按钮关闭/跳转、开关、`SetActive` 页签、`ShowTip`、`ShowDialog`、`Custom` 消息。
- 使用方 README 的「使用限制」：单根、`IsOpen` 歧义、预加载、弹窗、对话框结果限 `string` + 状态字段、动作消息禁止缓冲订阅。

**验收**

- 策划在一个预制体内完成：静态展示、按钮关闭/跳转/发消息、开关切换、页签切换；该页不需要任何专属 C#。
- 策划在对话框定义里用 `CloseWithResult` 表达确认/取消/三选一；`ShowDialogAsync<UIDeclarativeDialogPanel,string>` 返回结果 ID，`ShowDialog` 动作同时发布 `Selected/Canceled/OpenFailed/ParentClosed` 状态。
- 打开该页只需一次 C# 调用（或由另一个 C# 流程打开）；声明式面板之间不直接 `Push`。
- 直接调用 `UIManager.OpenAsync<UIDeclarativePanel>(第二个地址)` 的行为被 README 明确标为不支持；扩展入口 `UIAuthoringNavigation` 拒绝 + Warning。
- 核心包 `git diff -- Assets/XFramework` 为空；核心测试全绿。
- 无每帧分配；重复开合 100 次订阅数不增长；对话框结果通道每轮重置；`ShowDialog` 单飞不产生假取消。
- 扩展校验器对缺失定义、缺失按钮/节点、非法 `SetActive`、`CloseSelf`/`CloseWithResult` 非末尾、`Push` 到声明式/对话框目标、`resultId` 为空/重复等报错。

**测试**

- EditMode：定义/动作计划/校验规则/节点目录的纯逻辑；fake `IUIActionRuntime`；无需 YooAsset。
- PlayMode：`UIDeclarativePanel` 的打开/关闭/回池、按钮/开关动作、`UIActionSession` 的 ABA（打开中关闭再重开）、`UIDeclarativeDialogPanel` 的结果通道/状态/单飞、`ShowDialog` 动作、provider 尚未接入时的静态绑定；用 fake `IUIManager` 测导航。
- `ShowDialogAsync` 只在门面、不在 `IUIManager`；`ShowTipAsync` 在 `IUIManager` 上（`IUIManager.cs:231`）。扩展测试要包一层或走门面；真实核心集成（`UIManager.OpenAsync` / `ShowDialogAsync` + YooAsset）用一次性探针，不做日常单测。
- 包内自设守卫全部落地；`run-tests` 全量覆盖。

### 8.3 阶段 2b：声明式 HUD（文本）

**状态（2026-11-08）**：已实施并交付（提交 `e0e2679d`→`c2161bd0`）。`UIDeclarativeHudItem`、HUD 校验规则 `UI-AUTH-HUD-*`、扫描器候选、`Samples~/Templates/Hud.prefab` 与 DemoHud 示例、包 README/CHANGELOG 与 Runtime API 基线全部落地；核心 `Assets/XFramework/**` 零改动。验证：UIAuthoring PlayMode 44 例 / EditMode 22 例全绿；模板经核心与扩展校验器 0 Error；示例编译通过。HUD 模板的裸消费方导入验收未跑（见 §十 P3）。

**交付物**

- `UIDeclarativeHudItem`、HUD 定义规则（`UI-AUTH-HUD-*`）；**2b 不暴露 HUD 动作**（`HideSelf`/`SetActive`/`SelectTab`/`ShowTip`/`Custom` 在 `Raycaster` 关闭的默认 HUD 上不可触发）。
- 模板 `Samples~/Templates/Hud`；示例 `Samples~/DeclarativeDemo` 增加跟随目标的 HUD。
- 使用方 README：HUD 无 `userData`、不入栈、固定层、池化、`CloseAllAsync` 连带回收；关闭用核心 `HideHud(target)`。

**验收**

- 策划做 HUD 预制体 + 定义；程序只写 `ShowHudAsync<UIDeclarativeHudItem>`。
- 静态文本/本地化由定义表达；关闭由程序 `HideHud(target)`；动态数据留阶段 3。
- 核心校验器对 HUD 只跑 001/008；扩展校验器补节点/深度/重名/根 RectTransform。

**测试**

- PlayMode：HUD 打开/关闭/回池、目标丢失、`CloseAllAsync`、池化后订阅不增长。
- 不引入真实 YooAsset；用 fake provider / `UIHudManagerImpl` 的既有测试缝。

### 8.4 阶段 3：数据源接缝 + 三形态数据绑定

**状态（2026-11-10）**：已实施并交付（提交 `fe1ae657`→`b25203cb`）。`IUIDataProvider` / `UIDataProviderRegistry`、`UIDataBinding` / `UIDataBindingKind`、Panel/HUD/Dialog 的 `DataBindings`、`Slider`/`Toggle` 双向、编辑器规则 `UI-AUTH-070..075` 与 `UI-AUTH-HUD-005`、示例 provider 与 Demo 预制体/定义全部落地；核心 `Assets/XFramework/**` 零改动。验证：新增 PlayMode 22 例（21 数据绑定 + 1 语言切换）与 EditMode 14 例（13 校验器 + 1 定义/枚举）全绿；示例预制体经临时导入与扩展校验器 0 Error；`check-docs.ps1 -Enforce`（含示例编译）通过。

**与原文的差异**：

- **不做「定义资产 YooAsset 采集」检查**：P2 构建图探针（§10.1）已证明被预制体引用的 SO 必进构建图，该检查会误报。
- **不做「共享定义的引用扫描」**：扫描窗口按预制体逐个校验，共享定义自然会被覆盖；单定义反查引用属编辑器 UX 增强，不属本轮。
- **TMP 本地化路径不做自动化**：TMP Essentials 未导入的项目行为不同，保留 P5 一次性手工探针；自动化只测 UGUI `Text` 的语言切换。
- **顺带修复扫描器缺陷**（提交 `9981bf5c`）：`SerializedObject(GameObject)` 读不到组件上的 `_definition`，定义校验曾被整体静默跳过；改为对声明式根组件建 `SerializedObject`。
- **一次性验收已跑（2026-11-10）**：P2 运行时（真实构建 + `OfflinePlayMode`，同包与 `DependAssetCollector`）、P3 实际模板导入、P5 TMP 绑定全部通过（§10.3）；P6 IL2CPP/AOT 仍待。

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

**状态（2026-11-10）**：用户明确要求开项并已交付（提交 `c231bc23`→`2e721767`）。

**交付物**

- `UIDeclarativePopupPanel : UIPanelBase`（`Kind=Popup`，与面板/对话框可同时打开）。
- `UIActionKind.ShowPopup` + `UIAction.AnchorNodeName` / `Direction` / `Align` / `CloseOnClickOutside`；`UIAuthoringNavigation.ShowPopupAsync`（同地址重摆、异地址拒绝）。
- 编辑器规则 `UI-AUTH-080`（锚点缺失/非 RectTransform）；`ShowPopup` 仅允许 `Kind=Panel`，Dialog/Popup 定义报 `ActionNotAllowed`。
- `Samples~/Templates/Popup.prefab` 与 DemoPopup 示例（面板 `btn_popup` 经 `ShowPopup` 打开）。

**v1 边界**：同一时刻只能开一个声明式 Popup；弹窗内不能再开声明式 Popup；层固定 `UILayers.Popup`；`closeOnClickOutside` 只在核心内置 `UIManagerImpl` 生效；打开后不跟随锚点移动。嵌套子菜单需要 C# 面板或未来的多个具体类型，不在 v1。

**验证**：新增 PlayMode 9 例（4 Popup + 2 执行器 + 3 导航）与 EditMode 8 例（7 校验 + 1 扫描）全绿；示例预制体经临时导入与扩展校验器 0 Error；`check-docs -Enforce` 通过。

### 8.6 阶段 4 之后的边界

声明式 UI 制作线（阶段 1–4）到此完整：面板、对话框、HUD、Popup 四形态均可用定义资产 + 封闭动作集 + 数据绑定表达，程序只保留一次打开调用与一个数据源。后续若出现新需求，按 `Documentation/Modules/UI.md` 的台账与本文 §五「明确不做」的重开判据处理；不自动扩展动作词汇表或新增第五种形态。

### 8.6 裁定点与门禁

| 时点 | 触发的条目 | 原因 |
|---|---|---|
| 阶段 1 开工前 | 「暂不做：面板创建向导 / 预制体模板 / 面板脚本与地址常量生成」中的**预制体模板** | 已由 2026-11-08 用户裁定重开 |
| 阶段 1 | P2（SO 依赖）与 P3（模板导入）探针 | P2 决定 D2 SO/内联；P3 决定模板能否随包；失败则停下确认 |
| 阶段 2a 开工前（Q3） | 定义资产形态（SO vs 内联） | P2 通过 → SO；失败 → 停下确认后回退内联 |
| 阶段 2a 开工前（Q4/Q8） | 动作出口与动作模型 A | 推荐 keyed message + 基类型打开；不涉及核心 API |
| 阶段 2b 开工前（D7/D8） | HUD 模板与 HUD 动作策略 | HUD 模板随 2b；2b 只做文本，动作暂不暴露 |
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
| P2 YooAsset 定义资产采集 | 预制体引用的 `UIPanelDefinition` 是否随包/依赖加载 | **构建图实测**（2026-11-08 · YooAsset 2.3.18 `BeginCollect(simulateBuild:false)`）：引用 SO 必进构建图；same/dep 显式、cross/missing 隐式依赖；null 只在资产缺失时出现。**运行时实测（2026-11-10）**：真实构建 + `OfflinePlayMode` 下同包与 `DependAssetCollector` 两种配置的 `_definition` 均非 null（§10.3） | 已跑 |
| P3 Prefab Variant 模板分发 | `Samples~/Templates` 中的基础 Prefab + Variant 能否在消费方项目导入并保持脚本引用 | **机制实测**（2026-11-08 · 本地 UPM 包 + 裸消费方）：包程序集产出、样例 Prefab / Variant / 实例的脚本引用全部保持；移除包后变 Missing Script。**实际模板实测（2026-11-10）**：Panel / Dialog / Hud 三个模板导入后脚本与预期子节点全部解析、扩展校验器 0 Error（§10.3） | 实际模板已验；裸消费方机制沿用 §10.2（本轮未重复搭裸工程） |
| P4 UI Toolkit | 是否值得另立换栈评估 | `[未实测]`，本文落盘前从未评估 | 仅当用户认真考虑换栈时做 30–60 分钟探针（UIDocument、Input System only、运行时绑定、TMP、GC、与 `UIManager` 共存） |
| P5 TMP 绑定 | TMP Essentials 未导入时能否测 | **实测（2026-11-10）**：临时导入 TMP Essentials 后，TMP 本地化语言切换与数据绑定均通过（§10.3） | 日常自动化仍只测 UGUI `Text`；TMP 资源不随包提交 |
| P6 IL2CPP/AOT | `IUIDataProvider.TryGet<T>` 的泛型实例化、无反射 | `[未实测]` | 阶段 3 至少一次 IL2CPP 构建；必要时 `link.xml` |
| P7 真实核心集成 | 扩展测试无法注入核心 `UIManager.PanelFactoryFactory`（internal） | 已知边界 | 用 fake runtime 测逻辑；真实 `UIManager.OpenAsync` 用一次性 EditorSimulate 探针 |
| P8 HUD 校验射程 | 核心校验器对 HUD 实际只跑 `001/008`；扩展 HUD 规则是否覆盖节点/深度/重名/根 RectTransform | **源码已坐实**（§1.5.4），扩展规则未实现 | 阶段 2b 前用一条 EditMode 用例固化；面板与 HUD 规则分开 |
| P9 Tip 本地化 | Tip 只有原始字符串，没有本地化重载 | **源码已坐实**（`TipConfig` 无 key） | 阶段 3 手工探针：调用方 `LocalizationManager.Get` 后显示；不纳入自动化 |
| P10 声明式 Popup | 复用 `UIDeclarativePanel` 时与主面板抢单根；遮罩/捕获层行为 | **源码已坐实**（§2.3/§2.5），未跑 Unity | 阶段 4 已实现独立类型 `UIDeclarativePopupPanel`（§8.5），单根冲突消除 |
| P11 扩展包 Samples~ 登记 | `check-docs.ps1` 样例根是硬编码列表；新包不登记就没有编译通道 | **源码已坐实**（`Tools/check-docs.ps1:513-518`） | 阶段 2a 建包时同步改 `Tools/check-docs.ps1`；跑一次 `check-docs -Enforce` 验证 |

### 10.1 P2 构建图探针结果（2026-11-08）

**方法**：临时 Unity 工程（不碰仓库），YooAsset 2.3.18；创建 SO + `ProbeRef` prefab，按四组 collector 调 `AssetBundleCollectorSettingData.Setting.BeginCollect("ProbePackage", simulateBuild: false, useAssetDependencyDB: false)`，看 prefab 的 `CollectAssetInfo`、`DependAssets` 与 SO 的 `BundleName`。

| case | prefab bundle | SO 显式 | SO 依赖 | SO bundle | 结论 |
|---|---|---|---|---|---|
| same | `probepackage_assets_probe_runtime.bundle` | true | true | 同一 bundle | 同包安全 |
| dep | `...runtime.bundle` | true | true | `...shared.bundle` | 依赖采集安全 |
| cross | `...runtime.bundle` | false | true | 未显式（隐式依赖） | 仍会进构建图；若 SharedPackage 也构建则可能重复 |
| missing | `...runtime.bundle` | false | true | 未显式（隐式依赖） | 仍会进构建图；不会 null |

**结论**：

1. **SO 被 prefab 引用就一定会进构建图**：同包是显式；`DependAssetCollector` 是独立依赖 bundle；未采集/跨包是隐式依赖。`null` 只会在 SO 资产被删除/引用丢失时出现，不会因为「没被主资源采集」出现。
2. **v1 定义资产应与 prefab 同包采集，或放在同包的 `DependAssetCollector`**：避免跨包显式采集导致同一 SO 被两个包各带一份；跨包共享留到有真实需求时再评估。
3. **运行时加载仍需补测**：本探针是构建图（`BeginCollect`），不是运行时。`EditorSimulate` 的 `SimulateBuild` 会设置 `ECollectFlags.IgnoreGetDependencies / IgnoreStaticCollector / IgnoreDependCollector`（`CollectCommand.cs:40-44`），因此不能拿 EditorSimulate 证明依赖 bundle 加载；阶段 2a 用真实构建 / PlayMode 跑一次同包与依赖两种配置。

**对 D2 的影响**：SO 的「未采集 → null」风险被排除；D2 仍推荐 SO，但采集规则写死为「同包或同包 `DependAssetCollector`」。P2 的运行时半场保留为 2a 验收项。

### 10.2 P3 模板机制探针结果（2026-11-08）

**方法**：本地 UPM 包 `com.xfw.uiauthoring.probe`（`Runtime/ProbePanel.cs` + asmdef + `Samples~/Templates/Probe.prefab`），裸消费方工程通过 `file:` 引用；把样例 Prefab 复制进 `Assets`（模拟 Package Manager 的 Sample 导入结果），用 `PrefabUtility.SaveAsPrefabAsset` 从实例创建 Prefab Variant，再实例化 Variant。

| 配置 | 包程序集 | 样例 Prefab | Prefab 组件 | Variant 组件 | 实例化组件 |
|---|---|---|---|---|---|
| 有包 | `Xfw.UIAuthoring.Probe.dll` ✅ | ✅ | ✅ | ✅ | ✅ |
| 移除包 | ❌ | ✅（Prefab 仍在） | ❌ Missing Script | ❌ | ❌ |

**结论**：

1. **包内脚本 → 样例 Prefab → Prefab Variant → 消费方实例**这条链是通的：脚本 GUID 在包 `Runtime`，样例 Prefab 在 `Samples~/Templates`，复制到消费方后 GUID 仍解析；Variant 保留脚本引用；实例化后组件可 `GetComponent`。
2. **移除包后模板会变 Missing Script**：模板不是自包含资产；README 必须写明「模板导入后归项目所有，卸载扩展包会断脚本引用」。
3. **范围**：本探针用的是 `ProbePanel : MonoBehaviour` 替身，且样例是「复制进 Assets」模拟 Package Manager 导入；实际 `UIDeclarativePanel` / `UIDeclarativeDialogPanel` / `UIDeclarativeHudItem` 模板（含 core 依赖、真实 Sample 导入入口）在 2a/2b 各补一次。

**对 D7 的影响**：模板机制可用；D7 的「面板/对话框随 2a、HUD 随 2b」不变；模板维护成本（导入后不随包升级）是已知边界。

### 10.3 P2 运行时 / P3 实际模板 / P5 TMP 探针结果（2026-11-10）

**方法**：在仓库测试壳内搭临时探针程序集（用完即删，不产生提交）：

- **P2 运行时**：用 YooAsset 2.3.18 `ScriptableBuildPipeline` 真实构建两个包——`UIAuthoringProbeSame`（预制体 + 定义资产同一 `MainAssetCollector`）与 `UIAuthoringProbeDepend`（预制体 Main + 定义资产 `DependAssetCollector`，`UniqueBundleName=true`）；构建产物复制到 `StreamingAssets/yoo`，PlayMode 用 `OfflinePlayModeParameters` 加载预制体并实例化，反射读取根组件 `_definition`。
- **P3 实际模板**：把 `Samples~/Templates/{Panel,Dialog,Hud}.prefab` 连同 `.meta` 临时复制进 `Assets`（模拟 Package Manager 的 Sample 导入结果），用 EditMode 用例加载并运行扩展校验器。
- **P5 TMP**：从 `com.unity.ugui` 的 `TMP Essential Resources.unitypackage` 临时解包到 `Assets/TextMesh Pro`，PlayMode 用例验证 TMP_Text 的本地化语言切换与数据绑定。

| 探针 | 结果 |
|---|---|
| P2 同包 | 构建成功；`OfflinePlayMode` 加载 `ProbePanel` 成功；`_definition` 非 null，`DataBindings` 数量 1 |
| P2 DependAssetCollector | 构建成功；同上；定义资产随依赖 bundle 加载，`_definition` 非 null |
| P3 实际模板 | Panel / Dialog / Hud 三个预制体导入后根脚本与预期子节点全部解析；扩展校验器 0 Error（定义为空只报 `UI-AUTH-002` Warning） |
| P5 TMP | 本地化 TMP 文本：初始「标题」→ `SetLanguage("en")` 后「Title」；TMP 数据绑定：初始「TMP」→ 源更新后「Next」 |

**结论与边界**：

1. P2 的运行时半场**通过**：真实构建 + `OfflinePlayMode` 下，同包与同包 `DependAssetCollector` 两种采集都能让预制体引用的 SO 非 null；§10.1 的构建图结论在运行时成立。
2. P3 验证的是**实际模板资产**的 GUID/脚本引用；裸消费方的包安装机制（UPM 包 → Sample 复制 → Variant → 实例）沿用 §10.2 的既有结论，本轮未重复搭裸工程。模板仍是「导入时复制」，卸载包会断脚本引用（已知边界）。
3. P5 证明 TMP_Text 路径可用；自动化测试仍只覆盖 UGUI `Text`（TMP Essentials 不进仓、不随包提交）。探针资源已删除。
4. 探针只验证「`_definition` 是否随包/依赖加载」与「TMP 文本通道」；数据绑定的行为断言由包内 21 例 PlayMode 测试覆盖，未在探针中重复。

---

## 十一、剩余决策简报（2026-11-08）

> 来源区分：Q1/Q2/Q5/Q6 已由用户裁定；Q3/Q4/Q7/Q8 是原开放裁定点；D1/D6/D7/D8/D10 是本轮审计后新增的实施选择/边界确认；D9 是推荐默认。每条给出「控制什么 / 选项 / 优劣 / 推荐 / 何时必须定」。

### 11.1 剩余决策总表

| 编号 | 决策 | 现在必须定？ | 阻塞什么 | 推荐 |
|---|---|---|---|---|
| **D1** | 实施范围与顺序 | 是（决定下一步做什么） | 全部实施 | 阶段 1（P2/P3 探针）→ 2a → 2b → 3 → 4；**2a/2b/3/4 已完成（2026-11-10）**，无后续阶段 |
| **D2**（Q3） | 定义资产形态：SO / 内联 / 混合 | 是（阶段 2a 代码前） | `UIPanelDefinition` 形状、YooAsset 采集 | SO（同包或同包依赖采集）；P2 构建图已过，运行时 2a 补测 |
| **D3**（Q4） | 自定义动作出口 | 阶段 2a 动作实现前 | `Custom` 与 `ShowDialog` 结果 | keyed `MessageManager`，消息不带 `View`，禁用缓冲订阅 |
| **D4**（Q8） | C# 面板导航动作 | 阶段 2a 动作实现前 | `Open` / `Push` 动作 | A：`OpenAsync<UIPanelBase>` |
| **D5**（Q7） | 声明式 Popup | 可延迟到阶段 4 | Popup 的零代码 | v1 不做；阶段 4 独立类型 |
| **D6** | 声明式对话框结果失败语义（Q6 已定 `string`） | 是（阶段 2a API 前） | `UIDeclarativeDialogPanel` | `string` + 状态字段（Selected/Canceled/OpenFailed/ParentClosed） |
| **D7** | 模板交付 | 是（阶段 1/2a 前） | 模板的可用性 | 面板/对话框随 2a；HUD 随 2b |
| **D8** | HUD 实施阶段与交互性 | 可延迟到阶段 2b 前 | HUD 的零代码 | 2b 文本 only + 3 数据；v1 不做交互/动作 |
| **D10** | 边界约束（核心零改动 / Tools / 许可 / 全局动作） | 是（实施前） | 实施边界 | 见 11.12 表 |

### 11.2 已裁定（Q1 / Q2 / Q5 / Q6）

- **Q1 接受单根**：同一时刻只有一个声明式 `UIDeclarativePanel`；多页用同一预制体内的子页 + `SetActive`/`SelectTab`；需要并存的界面用 C# 面板。核心零改动。
- **Q2 只重开「预制体模板」**：`Samples~/Templates` 随附受限 Prefab Variant；向导、面板脚本、地址常量生成维持暂不做。
- **Q5 HUD 设计纳入**：`UIDeclarativeHudItem : UIHudItem`，阶段 2b/3 实施。
- **Q6 Dialog 设计纳入**：`UIDeclarativeDialogPanel : UIDialogPanel<string>`，阶段 2a 实施；自定义 `TResult` 仍走 C#。

### 11.3 D1 实施范围与顺序

**控制什么**：这轮之后是继续只做文档，还是开始写扩展包；先做哪一段。

| 选项 | 内容 | 优 | 劣 |
|---|---|---|---|
| **A. 只做文档** | 保持现状 | 零风险；先确认设计 | 策划仍拿不到可跑的面板/HUD/对话框；文档可能继续漂移 |
| **A+. 只跑 P2/P3 探针** | 不建包，先跑两个探针，结果回来后再确认 B | 零返工；把两个未知先钉死；用户保留「是否开始」的决定权 | 推迟实施 |
| **B. 阶段 1 探针 + 2a** | 先验证 YooAsset 依赖与模板导入；再做模板 + `UIDeclarativePanel` + `UIDeclarativeDialogPanel` + 动作 + 校验器 | 最快拿到「面板/对话框零代码」；探针挡住最大的两个未知 | 仍不含 HUD/数据源；包骨架与测试量大 |
| **C. B + 阶段 2b** | 加静态 HUD | 覆盖 HUD 的静态文本 | HUD 的按钮默认不可点（`Raycaster` 关），静态 HUD 价值有限 |
| **D. B + 2b + 3** | 加 `IUIDataProvider` 与 HUD/面板数据绑定 | 覆盖「动态数据」这一档；HUD 才真正有用 | 最大范围；P2/P6/P9 探针都要过 |
| **E. 全部 + 阶段 4** | 再加声明式 Popup | 最完整 | Popup 是窄需求；不建议先做 |

**推荐 B**：先跑 P2（预制体引用的 SO 是否随 YooAsset 加载）与 P3（模板跨项目导入后脚本引用是否保持）（阶段 1），通过后直接进阶段 2a。理由：面板与确认对话框是「策划零代码」的最高价值切片；HUD 的价值主要在动态数据，适合与阶段 3 一起；Popup 是窄需求，留到阶段 4。阶段 2b/3/4 仍保留在排期中，不是砍掉。如果只想先看探针结果、暂不承诺建包，选 **A+**——B 等于 A+ + 2a。

### 11.4 D2（Q3）定义资产形态：ScriptableObject vs 内联 vs 混合

**控制什么**：策划在哪配置文本/动作/页签/数据键；预制体与定义的依赖关系；校验器怎么读。

| 选项 | 做法 | 优 | 劣 |
|---|---|---|---|
| **A. ScriptableObject `UIPanelDefinition`** | 预制体引用一个 SO；`Kind` 区分 Panel/Hud/Dialog | 可复用（多预制体共享）；可独立打开/校验；预制体 diff 干净；定义资产单独 diff/评审 | 多一个资产；预制体→SO 的 YooAsset 依赖加载未实测（P2）；SO 未被采集时定义可能为 null，绑定静默为空 |
| **B. 组件内联序列化字段** | `UIDeclarativePanel` 上挂一个 `[Serializable]` 定义类 | 随预制体走，零额外依赖；没有 P2 风险；分发最简单 | 不可复用；预制体 YAML diff 大；定义 Inspector 挤在组件上；变体复制定义 |
| **C. 混合** | 组件同时有 `_definition`（SO）与 `_inline`，二选一 | 无 SO 也能用；可共享 | 两条真相；校验/优先级/迁移复杂；v1 不值得 |

**推荐 A（同包采集）**：`UIPanelDefinition` 与预制体放同一 YooAsset 采集包/同一目录，或放在同包的 `DependAssetCollector`；先跑 P2。**P2 构建图已跑（§10.1）**：引用 SO 必进构建图；同包/依赖采集安全；未采集/跨包是隐式依赖；`null` 只在资产缺失时出现。因此 v1 不存在「SO 没被主资源采集就为 null」的风险，D2 可以直接选 SO；运行时加载仍需阶段 2a 补测。跨包共享定义会带来重复打包，v1 不做。

**P2 的已知事实**（`[源码核对·YooAsset 2.3.18]` + `[构建图实测 2026-11-08]` + `[运行时未实测]`）：YooAsset 的 `LoadAssetAsync` 没有依赖参数，依赖加载发生在 `ProviderOperation` 内部：清单里的 `DependBundleIDs` 会被自动加载；未被显式采集的依赖会作为隐式依赖并入引用方 bundle。构建图探针（§10.1）已验证：prefab 引用的 SO 在 same/dep 是显式、在 cross/missing 是隐式依赖，四种配置都进构建图。**运行时**加载仍要阶段 2a 用真实构建 / PlayMode 补测——`EditorSimulate` 的 `SimulateBuild` 会忽略依赖收集，不能拿它证明依赖 bundle 加载。

**补充权衡**：

- **SO 作为预制体子资产**（`AssetDatabase.AddObjectToAsset`）：有 SO 的 Inspector/独立校验，又随预制体走、无 YooAsset 依赖；代价是每预制体一份、需要编辑器工具维护子资产。它是「既不想内联、又怕 P2」的折中；v1 不推荐（工具成本），但值得记下。
- **同包采集与「可复用」有张力**：同包只允许同一采集包内的预制体共享定义；跨包共享必须走依赖采集，正是 P2 的另一配置。v1 的复用边界写死为「同包」，跨包留到 P2 通过后。
- **扩展校验器必须自建 YooAsset 地址目录**：核心 `YooAssetUIPrefabAddressProvider` 是 `internal sealed`，扩展无法复用；`Open`/`Push` 目标解析与「定义资产是否被采集」都要重复实现。这是 2a 的真实工作量。
- **空定义策略**：`_definition == null` 既可能是「故意静态预制体」，也可能是「SO 资产缺失/引用丢失」；「没被主资源采集」已由 P2 构建图排除（§10.1）。推荐：null = 静态预制体（允许打开），但校验器在预制体根有 `UIDeclarativePanel` 且 `_definition` 为空时给 Warning（提示检查引用/采集），不要求定义非空。

**如果选 B**：实现最快、分发最稳，但每个预制体一份定义，页面多了以后重复与 diff 噪音会线性增长；后续想共享需要迁移。

### 11.5 D3（Q4）自定义动作出口：keyed 消息 / 静态注册表 / `EventStream` 注册表

**控制什么**：`Custom(actionId, payload)` 与 `ShowDialog` 的结果怎么交给项目代码。

**关键事实**：`MessageManager` 的**每次 keyed 发布都会写入该 Key 的重放缓存**（即使没有 `SubscribeBuffered` 订阅者），持有重放缓存的通道不会被自动回收，直到 `EvictBufferedChannel(s)` 或 `Clear()`（`MessageBroker.cs:138-164`、`MessageChannel.cs:119-123`）。因此：

- 若消息里带 `UIViewBase View`，这个引用会被缓存持有到淘汰/清空；面板关闭/回池后就是陈旧引用。
- 动作消息是**一次性事件**，不是「当前值」；keyed 缓冲的语义本来就不适合它。
- 用 `Subscribe`（非缓冲）不会重放，但缓存仍会驻留；内存量由 actionId 数量决定，通常有界。

| 选项 | 做法 | 优 | 劣 |
|---|---|---|---|
| **A. keyed `MessageManager`（带 `View`）** | `Publish<string, UIAuthoringActionMessage>(actionId, msg)`；消息含 `View` | 复用现有消息系统：异常隔离（同步处理器异常由 `EventStreamImpl` 捕获记 `[Event]`、异步记 `[Message]`，不向发布方上抛）、订阅句柄、退订、Editor 主线程断言、`Clear` 退出清理 | **缓存持有 `View`**：面板/HUD 关闭后陈旧引用驻留；`SubscribeBuffered` 会重放陈旧面板；键值通道无通配符；消息类型是扩展包公开面 |
| **A'. keyed `MessageManager`（无 `View`）** | 同上，但消息只带 `actionId` + `payload`（+ 可选实例 ID） | 保留 A 的异常隔离/生命周期；缓存只持有字符串，内存有界；不会保留面板引用 | 订阅方拿不到面板实例；`SubscribeBuffered` 仍会重放最后一条动作（必须文档禁止）；键值通道缓存仍驻留 |
| **B. 静态 `Register(actionId, handler)`** | 扩展包维护 `Dictionary<string, Action<...>>`，自己 `Dispatch` | 无消息缓存；一次性事件语义正确；可强制单 handler；可携带 `View` | 要自管退订、异常隔离、会话复位、线程；又一个静态门面；漏退订即泄漏；本质是第二套事件系统 |
| **C. `EventStream<T>` 薄注册表** | 用 `XFramework.XEvent.EventStream.Create<UIAuthoringActionMessage>()` 按 actionId 建流；项目 `Subscribe` | 无缓冲缓存；复用 EventStream 的异常隔离/派发池；可携带 `View`；`onEmpty` 可自动摘流 | 仍是静态注册表；需 `ResetForNewSession`/`AutoInitTests` 登记；没有 `MessageManager` 的全局过滤器/统计/主线程断言；比 A 多一层 |
| **D. UnityEvent 补充** | 按钮/预制体上挂 UnityEvent | 策划在 Inspector 里拖方法，最直观 | 只能引用场景/预制体上的方法；无法跨模块；预制体 diff 大；不适合作为主出口 |

**推荐 A'（v1）**：keyed `MessageManager`，但**消息不带 `View`**（只带 `actionId` + `payload`；`ShowDialog` 结果用独立的 `UIAuthoringDialogResultMessage`，同样不带面板引用），并用 `Subscribe` 而不是 `SubscribeBuffered`；README 明确「动作消息是一次性事件，禁止缓冲订阅」。理由：复用仓内消息系统的异常隔离与生命周期，改动最小；缓存只保留字符串，内存有界。**若项目确实需要 action 携带面板/HUD 实例**，选 C（`EventStream<T>` 薄注册表）——它没有缓存、可以安全携带 `View`，代价是多一个静态注册表与复位。B 只在「不想依赖 MessageManager」时选。

**如果选 B/C**：必须同时提供 `ResetForNewSession`（清空注册表/流）、异常隔离、`AutoInitTests` 族登记与测试 fixture 复位；C 可以直接复用 `EventStream` 的异常隔离，B 要自己写。

**其他边界**：

- **非 keyed 全局消息**（`Publish<UIAuthoringActionMessage>` + 按 payload/actionId 过滤）也可用，但所有动作共用一个通道，缓存只保留最后一条，项目要自己过滤；不如 keyed 直接。
- **无订阅者时 `Publish` 静默成功**，不会告警，并会创建/保留该 actionId 的通道与缓存；这是 `MessageManager` 的既有语义，不是扩展能改的。
- **`Payload` 只有 `string`**；复杂数据由项目自己编码（JSON/ID），扩展不做对象序列化。
- **订阅方必须自行 `Dispose` 句柄**；除非订阅者是 `MonoBehaviour` / 实现 `IDestroyCancellationToken` 并用扩展方法绑定销毁令牌。

### 11.6 D4（Q8）C# 面板导航动作：A 基类型 / B 核心新 API / C 删除

**控制什么**：声明式面板上的按钮能不能打开一个 C# 面板（`Open` / `Push`），以及怎么知道目标类型。

| 选项 | 做法 | 优 | 劣 |
|---|---|---|---|
| **A. 基类型打开（推荐）** | `OpenAsync<UIPanelBase>` / `PushAsync<UIPanelBase>`；动作只存 `assetPath` + `layer` | 核心零改动；不同具体类型的 C# 面板正常打开/入栈；实现最小 | 同具体类型、不同地址会 `RegisterPanel` Error + 回滚旧面板；动作模型无 `userData` 字段（可加 payload 或走 `Custom`）；声明式/对话框目标必须由校验器拒绝；校验器需要把地址解析到预制体 |
| **B. 加 `panelType` + 核心非泛型 API** | 动作存类型（`MonoScript`/类型名）；核心加 `OpenAsync(Type, ...)` | 目标类型显式；可校验；不再有基类型的回滚歧义 | 公开 API 变更（`IUIManager` 新成员，第三方实现者编译破裂）；门面/Impl/Factory/Fake/基线/CHANGELOG 全要跟；`Type` 在 SO 里没有原生序列化，`MonoScript` 有编辑器/运行时/AOT 细节；`userData` 仍不能由 SO 传 |
| **C. 删除 `Open`/`Push`** | 跨面板导航交给 `Custom`，由项目代码 `OpenAsync<T>` | 动作集最小；零核心改动；无歧义 | 声明式按钮不能直接打开 C# 面板；「无需 C#」只在纯声明式页面内成立 |
| **D. 反射调泛型** | 类型名 → `MakeGenericMethod` | 不改核心 | 反射/AOT/IL2CPP 风险；与仓内零反射纪律冲突；不推荐 |

**推荐 A**：保留 `Open`/`Push`，实现固定为基类型打开；文档写清三条边界（不同具体类型正常；同具体类型不同地址回滚；声明式目标校验器拒绝）。

**修正一个容易写错的点**：`OpenAsync<T>` 本身有 `object userData` 参数（`IUIManager.cs:45-46`），所以 A **不是不能传数据**，只是当前动作模型没有这个字段。若项目需要给 C# 面板传数据：① 动作增加一个可序列化的 `payload`（字符串/基础类型）并映射到 `userData`；② 走 `Custom`，由项目代码调用 `OpenAsync<T>`。v1 推荐 ②，不把任意对象塞进 SO。

**校验器必须检查目标根类型**：`Open`/`Push` 的目标根必须是具体 `UIPanelBase` 子类，且**不得是任何 `UIDialogPanel<>`（包括 `UIDeclarativeDialogPanel`）或 `UIDeclarativePanel`**——否则会把对话框当普通面板打开，绕过遮罩与结果通道。解析不到目标预制体时 Warning，不静默通过。`Replace` 是「先关当前、再开目标」，目标打开失败时当前面板已经没了；动作文档必须写明这个失败代价，或改成「先打开目标成功后再关当前」（但 `OpenAsync` 会入栈，语义不同）。

**B 的成本（源码核对）**：`IUIManager` 新增成员会同时要求 `UIManager` 门面转发、`UIManagerImpl` 实现、`FakeUIManager` 补成员、`PublicApiBaseline.txt` 更新与 `CHANGELOG` 记录；`UIFacadeCompletenessTests` 只查成员**名字**，不会发现缺了「非泛型重载」，所以这条只能靠人工与基线。接口新增成员对第三方实现者是源破裂（先例 `CHANGELOG` 的 `IUIManager` 层级可见性回读，标为「破坏性变更，仅对第三方实现者成立」）。另外 `InstantiatePanelAsync<T>` 与 `IUIPanelFactory.CreateAsync<T>` 仍是泛型，B 需要补一条非泛型实例化路径；底层 `AssetManager.InstantiateAsync` 本身已返回 `GameObject`，不是缺口。

**B 的另一种形态**：核心新增一个可选能力接口 + 门面 `is` 探测，不改 `IUIManager`；它避免了第三方接口破裂，但仍新增核心公开 API 与基线，**仍违反「核心零改动」**，要单独立项。

**如果选 B**：需要一次公开 API 变更评审，并同步 `FakeUIManager`/`PublicApiBaseline`/`CHANGELOG`；`Type` 的序列化建议用 `MonoScript` + 编辑器侧转 `Type`，运行时不得反射。

### 11.7 D5（Q7）声明式 Popup：复用 / 独立类型 / 不做

> **状态（2026-11-10）**：用户明确要求开阶段 4，已按选项 B 交付（`UIDeclarativePopupPanel` + `ShowPopup`，见 §8.5）。本节保留原决策过程。

**控制什么**：主面板开着时，能不能再弹一个声明式弹窗。

| 选项 | 做法 | 优 | 劣 |
|---|---|---|---|
| **A. 复用 `UIDeclarativePanel`** | `ShowPopupAsync<UIDeclarativePanel>` | 零新类型 | **单根冲突**：主面板已开时，`OpenAsync<UIDeclarativePanel>` 会重聚焦并重摆主面板，不是弹窗；只有「当前没有声明式面板」时才可用 |
| **B. 独立 `UIDeclarativePopupPanel`** | 再加一个 `UIPanelBase` 具体类型；`Kind=Popup`；`ShowPopup` 动作带锚点节点 | 可与主面板/对话框并存；复用定义/动作/绑定 | 多一个类型、动作、模板、校验规则与测试；阶段 4 |
| **C. 不做声明式 Popup** | 继续用 C# `UIPanelBase` 子类 | 零额外面；现有 `ShowPopupAsync<T>` 可用 | Popup 仍需程序写类；不是零代码 |

**推荐 C（v1）/ B（阶段 4）**：v1 不做声明式 Popup；需要弹窗时用 C# 面板（现有路径）。阶段 4 若出现真实需求（主面板 + 下拉/右键菜单并存），再增加 `UIDeclarativePopupPanel` 独立类型。理由：A 的单根冲突会让「复用」变成陷阱；B 现在做没有真实消费方，违反「没有真实使用方不立项」的仓内纪律。

### 11.8 D6 声明式对话框结果类型（Q6 已定 string；本项只确认失败语义）

**已裁定**：Q6 已把结果定为 `UIDialogPanel<string>`；**不再重开** string/int/泛型子类。若现在改类型，会与 `Documentation/Modules/UI.md:144` 的落盘记录冲突，需要重开 Q6。

**本项要确认的**：`null` 到底表示什么。`ShowDialogAsync` 在「面板打开失败 / 被控制器拦下」时也返回 `default`（`UIManager.cs:846-848`、`:869-876`）；`UIDialogPanel` 在外部关闭 / 回池兜底时同样返回 `default`（`UIDialogPanel.cs:116-125`）。对 `string` 而言，以下三种情况都是 `null`：

1. 用户没有选择就关闭（取消）；
2. 面板从未成功打开（资源加载失败 / 控制器拦截）；
3. 父面板在等待期间被关闭，对话框被连带收掉。

| 选项 | 做法 | 优 | 劣 |
|---|---|---|---|
| **A. 扩展把三种情况合并为「无结果」** | `UIAuthoringDialogResultMessage.ResultId = null` | 最简单 | 项目无法区分「用户取消」与「弹窗没打开」；无法做埋点/错误提示 |
| **B. 结果消息带状态（推荐）** | `UIAuthoringDialogResultMessage` 增加 `UIAuthoringDialogStatus`：`Selected` / `Canceled` / `OpenFailed` / `ParentClosed` | 项目能区分；动作层可记录/提示 | 需要在 `ShowDialog` 动作里检测「是否真正打开过」（例如让 `UIDeclarativeDialogPanel` 在 `OnOpen` 成功后记录 `WasOpened`） |
| **C. 只保留 `string`，空串表示未选择** | 校验器禁止空串作为有效 resultId | 少一个字段 | 仍无法区分失败与取消；不推荐 |

**推荐 B**：结果消息带状态；`ResultId` 仅在 `Selected` 时有意义。实现上让 `UIDeclarativeDialogPanel` 在 `OnOpen` 成功后记录 `WasOpened`；`ShowDialog` 动作返回后按 `WasOpened` / `ResultId` 归类。父面板关闭导致对话框被收掉的情况归 `ParentClosed` 或 `Canceled`，二选一在 README 写死。

**附带确认（重入）**：已有声明式对话框打开时，再 `ShowDialogAsync` 会 `BeginDialogCycle()` 把上一轮等待者以 `default` 唤醒（`UIDialogPanel.cs:101-111`），造成「假取消」。`ShowDialog` 动作必须单飞：检测到 `IsOpen<UIDeclarativeDialogPanel>()` 时拒绝并 Warning，不启动第二个等待；或先关闭现有对话框再开（第一轮仍收到 `Canceled`，需明确）。推荐 **拒绝 + Warning**。

**其余选项（仅在用户决定重开 Q6 时考虑）**：`int` 的 `default(0)` 与未选择冲突，可用 `int?` / 可空枚举；`UIDeclarativeDialogPanel<TResult>` 不能作为开放泛型 MonoBehaviour 挂到预制体上，需要每个 TResult 一个具体子类（回到 C#）。这两条都不如 `string` + 状态字段直接。

### 11.9 D7 模板交付：面板/对话框随 2a，HUD 随 2b

**控制什么**：`Samples~/Templates` 里的 Prefab Variant 什么时候发、根节点挂什么。

**先拆开 Q2 的三种模板**：面板模板与对话框模板的根组件 `UIDeclarativePanel` / `UIDeclarativeDialogPanel` 在阶段 2a 就有；HUD 模板的根组件 `UIDeclarativeHudItem` 在阶段 2b 才有。**HUD 模板必须随 2b**，否则 2a 里是 Missing Script。

| 选项 | 做法 | 优 | 劣 |
|---|---|---|---|
| **A. 面板/对话框随 2a、HUD 随 2b（推荐）** | 模板根挂对应声明式类型；与代码同一包版本 | 模板可用（根组件存在）；不产生临时类型；P3 分类型测 | 阶段 1 不能单独交付可打开的模板；阶段 1 变成纯文档 + 探针 |
| **B. 阶段 1 发 `UIEmptyPanel` 占位** | 模板根挂一个最小 `UIEmptyPanel : UIPanelBase` | 阶段 1 即可打开/校验；模板先行 | 多一个公开/临时类型；阶段 2a/2b 后要退役或保留；API 基线/维护成本 |
| **C. 只写结构配方** | 不随包发预制体，文档给根结构与命名 | 零资产 | 与 Q2「重开模板」矛盾；策划仍从零搭 |

**推荐 A**：面板/对话框模板随阶段 2a，HUD 模板随 2b；阶段 1 只交付文档 + P2/P3 探针。理由：预制体根节点必须挂 `UIPanelBase` 子类，没有可挂载脚本就是 Missing Script；模板的价值在于固定根结构，必须有真实根组件。

**P3 机制已过（§10.2）**：包内脚本 → 样例 Prefab → Prefab Variant → 消费方实例的脚本引用全部保持；移除包后变 Missing Script，是已知边界。实际 `UIDeclarativePanel` / `UIDeclarativeDialogPanel` / `UIDeclarativeHudItem` 模板在 2a/2b 各补一次。

**补充（模板是「一次性起点」，不是长期同步资产）**：`Samples~` 是导入时复制，包更新不会自动更新消费方已经复制出去的模板；卸载扩展包后模板根脚本会变 Missing Script。README 要写明「模板导入后归项目所有，包升级不覆盖」。P3 分两类跑：面板/对话框模板、HUD 模板。

### 11.10 D8 HUD 实施阶段与交互性

**已裁定**：Q5 已把 HUD 设计纳入；本项只确认「什么时候做、做到什么程度」，**不重开「做不做」**。

**控制什么**：`UIDeclarativeHudItem` 在 2b 交付什么、3 交付什么；HUD 的动作能不能被触发。

**关键事实**：`UIHudItem.OnOpenImpl` 无条件关闭 `Raycaster`（`UIHudItem.cs:95-108`），而动作链只由按钮点击触发。所以「HUD 上的按钮 + `HideSelf`/`SetActive`/`SelectTab`/`ShowTip`/`Custom`」在默认 HUD 上**全部不可触发**；`HideSelf` 也不是程序 API。2b 如果不解决触发通道，实际只剩文本显示。

| 选项 | 做法 | 优 | 劣 |
|---|---|---|---|
| **A. 2b 只做文本（推荐）** | 2b：文本/本地化 + 数据键留 3；关闭 HUD 用核心 `HideHud(target)`（程序） | 语义诚实；不引入无效动作；最小切片 | HUD 的声明式动作在 2b 不生效 |
| **B. 2b 文本 + 程序触发入口** | `UIDeclarativeHudItem.TryExecuteAction(actionId)`，项目代码按 id 触发 | 可程序触发 `HideSelf`/`Custom`；不需要 Raycaster | 多一个公开方法；动作仍不能由玩家点击 |
| **C. 2b 文本 + `interactive` 开关** | `UIDeclarativeHudItem` 增加序列化 `interactive`，`OnOpenImpl` 里重新打开 Raycaster | 按钮可用；完整声明式 HUD | HUD 会吃输入、遮挡世界点击；需要 EventSystem/相机/CanvasGroup 射线设计 |
| **D. 并入阶段 3** | HUD 与数据源一起做 | 一次做出有用的 HUD；少一个半成品阶段 | 阶段 3 更大；HUD 生命周期问题与数据问题混在一起 |

**推荐 A（2b 文本 only）+ 阶段 3 数据**：2b 只验证 `UIDeclarativeHudItem` 生命周期与文本绑定；关闭用核心 `HideHud(target)`；**不在 2b 暴露 HUD 动作**。如果 2b 后确有「程序按 id 触发 HUD 动作」的需求，再加 B；玩家可点击 HUD 是另一个设计题（C），v1 不做。理由：HUD 的主要价值是显示动态数据（名字/血条）；在 `Raycaster` 关闭的前提下承诺 HUD 动作会落空。

**连带修改**：§7.9 的动作集从「`HideSelf`/`SetActive`/`SelectTab`/`ShowTip`/`Custom`」收紧为「2b 不暴露动作；`HideSelf`/`Custom` 仅在 B/C 落地后启用；`HideHud(target)` 是 2b 的关闭方式」。

### 11.11 D9 推荐默认（不需要单独确认，除非反对）

- **包位置与命名**：`Assets/XFramework.UIAuthoring/`，包名 `com.venusy609.xframework.ui.authoring`，与 Luban/FMOD 同型。
- **定义组织**：单一 `UIPanelDefinition` + `Kind`（Panel/Hud/Dialog），不拆三个 SO 子类；若 D2 回退内联，`Kind` 由组件类型隐含，不强制字段。
- **单根语义**：Q1 的单根只针对 `UIDeclarativePanel`；`UIDeclarativeDialogPanel` 是独立类型，可与声明式面板同时打开；HUD 可多个（每个 target 一个）。声明式 Popup 若复用面板类型，就会与主面板抢单根（D5）。
- **核心零改动是硬约束**：D4-B（核心非泛型打开）若被选中，属公开 API 变更，需单独立项评审；Q7 的独立类型在扩展包内，不涉及核心。
- **`ShowDialog` 结果通道**：独立 `UIAuthoringDialogResultMessage`，不带面板引用，不复用 `UIAuthoringActionMessage`。
- **`Tools/check-docs.ps1` 会改一处**：把 `Assets/XFramework.UIAuthoring/Samples~` 加进样例根列表；这不是核心包代码，但不登记模板/示例就没有编译通道。
- **HUD 校验**：扩展校验器补 `UI-AUTH-HUD-*`，不改核心校验器（核心零改动）。
- **包默认**：`samples` 数组、`LICENSE.md`、扩展包自带包边界/API 基线守卫、依赖声明与 README/CHANGELOG；实现时同步 `CLAUDE.md`/根 README 的扩展包说明（与 Luban/FMOD 同型）。

### 11.12 D10 边界约束确认（可勾选）

| 约束 | 推荐 | 说明 |
|---|---|---|
| **核心零改动** | 接受 | `Assets/XFramework/**` 的代码 / 公开 API / asmdef 不改；允许扩展包、`Tools/check-docs.ps1` 只加样例根、根 README/CLAUDE.md 加扩展包说明。D4-B 若选，需重开此约束。 |
| **校验器放扩展** | 接受 | 扩展自建规则引擎 + 地址目录；核心校验器不改。替代是核心公开校验 API（公开 API 变更，单独立项）。 |
| **包版本 / 许可** | 0.1.0 / MIT | 与核心/Luban 同型；根 README 加可选包安装节；版本/tag 策略实施时定。 |
| **全局动作保留** | 保留但警告 | `CloseAll` / `SetLayerVisibility` / `SetLayerInteractive` 是声明式按钮触发全局副作用的入口；v1 保留 + 编辑器警告，若认为风险大可以裁剪。 |
| **`UIAuthoringNavigation` 防护语义** | 拒绝 + Warning | 已有声明式面板/对话框打开时，不打开第二个；不抛异常；README 写明直接调核心入口不支持。 |
| **示例可跑** | 需消费方采集 | `Samples~/DeclarativeDemo` 的预制体/定义资产默认不在消费方 YooAsset 采集里；README 要写「把 demo 加入采集」，否则 `Open`/`ShowDialog` 演示不可用。 |

### 11.13 推荐组合与下一步

1. **P2/P3 已跑**（§10.1 / §10.2）：P2 构建图通过（SO 必进构建图；同包/依赖安全；运行时留 2a 补测）；P3 模板机制通过（实际模板留 2a/2b 补测）。
2. **阶段 2a**：面板/对话框模板（随 2a）+ `UIDeclarativePanel` + `UIDeclarativeDialogPanel`（string 结果 + 状态字段）+ 动作集（A：基类型打开；D3 A'：keyed message、消息不带 `View`、禁用缓冲订阅；`ShowDialog` 单飞）+ 两个校验器。
3. **阶段 2b**：HUD 模板（随 2b）+ `UIDeclarativeHudItem` 文本/本地化；2b 不暴露 HUD 动作，关闭用 `HideHud(target)`。
4. **阶段 3（已完成，2026-11-10）**：`IUIDataProvider` + 面板/HUD/Dialog 数据绑定；HUD 才真正有用。P2 运行时/P3 实际模板/P5 TMP 一次性验收已于 2026-11-10 跑通（§10.3）。
5. **阶段 4（已完成，2026-11-10）**：`UIDeclarativePopupPanel` + `ShowPopup` 已按独立类型交付（§8.5）；声明式 UI 制作线到此完整。

**这套组合的代价与风险**：

- **P2 构建图已过**（§10.1）：引用 SO 必进构建图；同包/依赖安全；未采集/跨包是隐式依赖。运行时加载留阶段 2a 的真实构建 / PlayMode 补测；不再保留「P2 失败自动回退内联」的分支。跨包显式采集会重复打包，v1 写死「同包或同包 `DependAssetCollector`」。
- **P3 机制已过**（§10.2）：包内脚本 → 样例 Prefab → Variant → 实例的脚本引用保持；移除包后 Missing Script 是已知边界。实际模板 2a/2b 补测。
- **扩展校验器要自建地址目录**（核心 provider 是 `internal sealed`）；`Open`/`Push` 目标解析、定义资产采集检查都重复实现，是 2a 的真实工作量。
- **`ShowDialog`/`ShowTip` 只在门面、不在 `IUIManager`**；扩展的测试与 fake 需要包一层，真实集成用探针。
- **HUD 交互是独立设计题**：2b 只做文本；按钮/动作需要 `interactive` 开关或程序触发入口，v1 不做。
- 核心零改动；不新增公开 API；不引第三方依赖。
- 阶段 2a 的实现量最大（包骨架、定义、两个类型、动作、绑定、校验器、示例、测试、守卫），需要单独的实施计划与提交拆分。

**如果用户不逐项回复**：文档按上述推荐默认落盘（D2 SO（P2 构建图已过，运行时 2a 补测）、D3 A'、D4 A、D5 v1 不做、D6 string + 状态、D7 面板/对话框随 2a、HUD 随 2b、D8 2b 文本 only、D9/D10 默认）；**实施不启动**——D1 是唯一必须明确说「开始」的项。

---

## 十二、R5 审计修复（2026-11-11）

- 已做：**控制器拦下关闭时保持动作会话与结果**（2026-11-11 · `f2117fb1`，R5-UAU-001，本轮修复）——`IUIActionRuntime.CloseAsync` / `ReplaceDeclarativeAsync` 改为返回「是否真的关闭」；`UIActionExecutor` 的 `CloseSelf` / `Replace` / `CloseWithResult` 仅在真正关闭时 `Invalidate`；`UIDeclarativeDialogPanel.CloseWithResultAsync` 在关闭被拦时立即丢弃 `_pendingResultId`，并在 `OnOpen` 复位。回归：`UIActionExecutorTests.CloseSelf_ControllerBlocksClose_KeepsSessionAndContinuesChain` / `Replace_ControllerBlocksClose_KeepsSessionValid` / `CloseWithResult_ControllerBlocksClose_ClearsPendingAndKeepsSession`、`UIDeclarativeDialogPanelTests.ReopenAfterCloseWithResult_DoesNotCarryPendingFromPreviousCycle`（改前 4 例红）。**边界**：生产实现的 `!panel.IsOpen` 判定不在包单测射程（测试钩子绕过核心生命周期），由核心控制器拦截用例 + 评审背书。
- 已做：**ShowDialog 取消不再吞成 OpenFailed**（2026-11-11 · `d887d696`，R5-UAU-002，本轮修复）——`UIManagerActionRuntime` 在通用 catch 前单独 `catch (OperationCanceledException) { throw; }`；新增 `UIManagerActionRuntimeTests`（经 `UIManager.SetInstance` 注入 `FakeUIManagerForAuthoring`，不再只靠 fake runtime 间接覆盖）与导航层取消传播用例。回归：`ShowDialog_Cancellation_PropagatesAndPublishesNothing`（改前红）、`UIAuthoringNavigationTests.ShowDialog_Cancellation_Propagates`。
- 已做：**公开/受保护成员 XML 注释补齐**（2026-11-11 · `6767f5f0`，R5-UAU-006，本轮修复）——三个面板的 `OnOpen`/`OnClose`、`UIDeclarativeHudItem.OnOpenImpl`、`UIPanelDefinitionEditor.OnInspectorGUI` 补 `<inheritdoc/>` / `<summary>`；顺带修掉两处 `CS0419`（`IUIActionRuntime` 与 `UIDeclarativePopupPanel` 的异重载 cref）。`check-docs.ps1` 对 UI.Authoring 两个 csproj 实测 0 条门禁码。
- 已做：**`Open`/`Push` 校验拒绝声明式弹窗目标**（2026-11-11 · `b3b581b9`，R5-UAU-003）——`UIDeclarativePopupPanel` 也是 `UIPanelBase`，此前会被当普通面板层打开、绕过锚定/层语义。回归：`OpenOrPushTargetPopup_Reports`（Open/Push 两态，改前红）。
- 已做：**本地化缺 key 使用独立规则号**（2026-11-11 · `4ffec487`，R5-UAU-007）——新增 `UI-AUTH-013`，不再复用 `UI-AUTH-010 TextNodeMissing`。回归：`LocalizationKeyMissing_UsesDedicatedRule`（改前红）。
- 已做：**`ShowDialog` 空参数失败也发布结果**（2026-11-11 · `dd1d8106`，R5-UAU-013）——`UIManagerActionRuntime` 与 `UIAuthoringNavigation` 两个入口在 `actionId` 非空时发布 `OpenFailed`；`actionId` 为空只告警。回归：`ShowDialog_EmptyAssetPath_PublishesOpenFailed_WhenActionIdPresent` / `ShowDialog_EmptyActionId_DoesNotPublish`（改前红）。
- 已做：**删除 `UIActionExecutor` 未使用的 `definition` 参数**（2026-11-11 · `3eff320a`，R5-UAU-011）——三个面板与测试工厂同步；无公开面变化。
- 已做：**生产 `UIManagerActionRuntime` 直接覆盖**（2026-11-11 · `949f31a7`，R5-UAU-010）——抽 `ResolveDialogStatus` 纯函数测四态；`FakeUIManagerForAuthoring` 经 `UIManager.SetInstance` 覆盖空参数、单飞拒绝、打开返回 null、取消传播、`Replace` 被拦与成功、已关闭面板的 `CloseAsync`。**边界**：`!panel.IsOpen` 的「被拦」侧与真 facade 的 `Selected`/`Canceled` 端到端仍无覆盖（测试钩子绕过核心生命周期），由核心控制器用例 + 评审背书。
- 已做：**Editor asmdef 引用纳入包守卫**（2026-11-11 · `6325451b`，R5-ENG-006）——`UIAuthoringDependencyDeclarationTests.EditorAsmdef_ReferencesOnlyDeclaredAssemblies` 逐字锁定 TMP / YooAsset / YooAsset.Editor；红证：期望集合临时加假项即红。
- 暂不做：**`UIActionKind.HideSelf` 的可达化**（2026-11-11 · R5-ADJ-01，用户裁定）——三条路径均不可达：校验器对所有 Kind 直接报错、HUD 从不绑定动作、执行器只打 Warning；`Raycaster` 默认关闭，HUD 上按钮也点不到。删除是公开 API 变更且预 1.0，补 HUD 动作入口需要新设计（`TryExecuteAction` 或 `interactive` 开关），本轮只把「保留、当前不可用」写进 XML 与包 README。重开判据：出现真实的 HUD 动作需求。

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
