# XFramework UI Authoring

## 概述

本包是 XFramework 的可选扩展包，目标是让策划在 **UGUI 预制体 + 定义资产** 里完成静态展示、按钮关闭/跳转、页签、Tip、确认对话框与自定义消息，程序只保留一次打开调用。

- 核心包零改动：面板/对话框仍继承 `UIPanelBase` / `UIDialogPanel<string>`，加载、排序、遮罩、栈、池化全部复用核心。
- 单根限制：`UIDeclarativePanel` 同一时刻只能打开一个；多页用同一预制体内的子页/页签。
- `UIDeclarativeDialogPanel` 是独立具体类型，可与声明式面板同时打开；结果类型固定 `string`。
- 阶段 2a 覆盖面板与对话框，阶段 2b 覆盖 HUD 静态文本；数据源、声明式 Popup 不在本版本。

## 快速使用

### 1. 安装顺序

1. 先安装核心包 `com.venusy609.xframework` 及其依赖（UniTask、YooAsset、TextMeshPro、Input System、Cinemachine；见核心包 README 的「第三方依赖」表）。
2. 再安装本包：`https://github.com/venusir/xframework.git?path=Assets/XFramework.UIAuthoring`（可自行在 `#` 后钉提交或 tag）。
3. `package.json` 的 `dependencies` 为空（UPM 不支持 Git URL 传递依赖），所以顺序必须手动保证。

### 2. 创建定义资产

`Create → XFramework/UI Authoring/Panel Definition`，设置：

- `Kind`：`Panel` / `Dialog` / `Hud`（HUD 只支持静态文本，不能配动作/页签）。
- `TextBindings`：`NodeName` + 字面量或本地化 key。
- `ActionBindings`：按钮节点名 + 动作列表。
- `TabGroups`：组名 + 一组节点名。

### 3. 搭建预制体

- 面板：根节点挂 `UIDeclarativePanel`；`Canvas` / `GraphicRaycaster` 由 `RequireComponent` 自动补；根 `RectTransform` 自己设。
- 对话框：根节点挂 `UIDeclarativeDialogPanel`。
- 子节点按 `txt_` / `img_` / `btn_` / `sld_` / `tgl_` 命名（最多 5 层，同名只保留第一个）；定义里的节点名与预制体一致。
- 把定义资产拖到面板组件的 `Definition` 槽位。
- 预制体进 YooAsset **主资源采集**；定义资产与预制体同包采集（或同包 `DependAssetCollector`）。

### 4. 打开与关闭

```csharp
using XFramework.XUI.Authoring;

var panel = await UIAuthoringNavigation.OpenAsync("ui/panels/shop");
if (panel == null) return; // 资源加载失败/被控制器拦下
```

- 已有声明式面板时，同地址会重聚焦；异地址会被拒绝并 Warning。
- 直接调用 `UIManager.OpenAsync<UIDeclarativePanel>(第二个地址)` **不支持**：核心会按具体类型去重，静默返回第一个面板；请统一走 `UIAuthoringNavigation`。
- 对话框：

```csharp
var status = await UIAuthoringNavigation.ShowDialogAsync("ui/dialogs/confirm", "shop.buy");
```

`status` 为 `Selected` / `Canceled` / `OpenFailed`；`Selected` 时结果 ID 从 `UIAuthoringDialogResultMessage` 读取。

### 5. 自定义动作

在定义里配置 `Custom(actionId, payload)`，项目按 `actionId` 订阅：

```csharp
MessageManager.Subscribe<string, UIAuthoringActionMessage>("shop.buy", msg =>
{
    // msg.Payload
});
```

动作消息**不带面板引用**，不要在回调外保存 `msg` 里的上下文；**不要用 `SubscribeBuffered`**（会重放最后一条动作）。

### 6. HUD（静态文本）

```csharp
var hud = await UIManager.ShowHudAsync<UIDeclarativeHudItem>(target.transform, "ui/huds/npc");
```

- HUD 定义 `Kind = Hud`，只读 `TextBindings`；`ActionBindings` / `TabGroups` 会被校验器拒绝/告警，运行时不绑定。
- 没有 `userData`；动态数据留阶段 3。同一目标同时只能挂一个 HUD，重复调用会替换。
- 关闭用 `UIManager.HideHud(target.transform)`；`CloseAllAsync` 会连带回收 HUD。
- 跟随目标、屏幕后隐藏、目标丢失回收全部由核心 `UIHudItem` 完成；不要重写 `OnUpdate`。

### 7. 运行两个校验器

- 核心：`Tools/XFramework/UI/Validate UI Prefabs`（预制体结构、命名、根 RectTransform、YooAsset 采集等）。
- 本包：`Tools/XFramework/UI Authoring/Validate Declarative Panels`（定义与节点、动作参数/顺序、目标根类型、地址解析）。

两个都跑：核心管预制体，本包管声明式定义；本包不复制核心的九条预制体规则。

## 模块结构

```text
Assets/XFramework.UIAuthoring/
├── package.json / README.md / CHANGELOG.md / LICENSE.md
├── Runtime/
│   ├── Venusy609.Xframework.UI.Authoring.asmdef
│   ├── AssemblyInfo.cs
│   ├── UIPanelDefinition.cs / UIPanelDefinitionKind.cs
│   ├── UITextBinding.cs / UIActionBinding.cs / UITabGroup.cs
│   ├── IUIDataProvider.cs / UIDataProviderRegistry.cs
│   ├── UIDataBinding.cs / UIDataBindingKind.cs
│   ├── UIAction.cs / UIActionKind.cs / UITipActionConfig.cs
│   ├── UIDeclarativePanel.cs / UIDeclarativeDialogPanel.cs / UIDeclarativeHudItem.cs
│   ├── UIDeclarativeNodeFinder.cs / UIDeclarativeBinder.cs / UIDeclarativeDataBinder.cs
│   ├── UIActionSession.cs / UIActionExecutor.cs
│   ├── IUIActionRuntime.cs / UIManagerActionRuntime.cs
│   ├── UIAuthoringNavigation.cs
│   ├── UIAuthoringActionMessage.cs
│   ├── UIAuthoringDialogResult.cs / UIAuthoringDialogResultMessage.cs / UIAuthoringDialogStatus.cs
│   └── UIAuthoringLog.cs
├── Editor/
│   ├── Venusy609.Xframework.UI.Authoring.Editor.asmdef
│   ├── AssemblyInfo.cs
│   ├── UIAuthoringValidator.cs / UIAuthoringValidationIssue.cs
│   ├── UIAuthoringScanner.cs / UIAuthoringValidatorWindow.cs
│   ├── UIAuthoringAddressProvider.cs
│   └── UIPanelDefinitionEditor.cs
├── Tests/
│   ├── Runtime/
│   │   ├── Venusy609.Xframework.UI.Authoring.Tests.asmdef
│   │   ├── UIDeclarativePanelTests.cs / UIDeclarativeDialogPanelTests.cs / UIDeclarativeHudItemTests.cs
│   │   ├── UIActionExecutorTests.cs / UIAuthoringNavigationTests.cs
│   │   ├── UIAuthoringActionMessageTests.cs / UIAuthoringTestPaths.cs
│   │   └── Architecture/（README/meta/边界/AutoInit/依赖/API 基线守卫）
│   └── Editor/
│       ├── Venusy609.Xframework.UI.Authoring.Editor.Tests.asmdef
│       ├── UIAuthoringValidatorTests.cs / UIAuthoringScannerTests.cs
│       ├── UIPanelDefinitionTests.cs
│       ├── UIAuthoringAddressProviderTests.cs
│       └── UIPanelDefinitionEditorTests.cs
└── Samples~/
    ├── Templates/（Panel.prefab、Dialog.prefab、Hud.prefab、.sample.json）
    └── DeclarativeDemo/（面板/对话框/HUD 预制体 + 定义资产 + UIAuthoringDemo.cs + .sample.json）
```

## 模板与示例

通过 Package Manager 的 Samples 页签导入：

- **声明式面板/对话框/HUD 模板**（`Samples~/Templates`）：`Panel.prefab` / `Dialog.prefab` / `Hud.prefab`。根节点已带 `Canvas` / `GraphicRaycaster` 与声明式根组件，子节点按 `txt_` / `btn_` 命名；复制一份作为你的起点，再加子页与节点。
- **声明式 UI 示例**（`Samples~/DeclarativeDemo`）：`DemoPanel.prefab` / `DemoDialog.prefab` / `DemoHud.prefab` + 三个定义资产 + `UIAuthoringDemo.cs`。导入后需要：
  1. 把三个预制体加入消费方 YooAsset **主资源采集**，地址分别设为 `ui/panels/demo` / `ui/dialogs/demo` / `ui/huds/demo`（与定义里的 `ShowDialog.AssetPath` 和演示脚本的 `hudAddress` 一致）；
  2. 三个定义资产与预制体**同包采集**（或同包 `DependAssetCollector`）；
  3. 场景里放一个 `UIRootNode`，把 `UIAuthoringDemo` 挂到任意物体上按 Play；可选地把一个 3D 目标拖到 `hudTarget` 看 HUD 跟随。

模板是**导入时复制**：包升级不会覆盖你改过的模板；卸载本包后模板根组件会变成 Missing Script。模板只是起点，不是运行时强制结构。

## 行为契约

- **单根**：`UIDeclarativePanel` 是单一具体类型，核心按具体类型登记；同一时刻只能打开一个声明式面板。
- **对话框**：`UIDeclarativeDialogPanel : UIDialogPanel<string>`；`CloseWithResult(resultId)` 设置结果并关闭；外部关闭返回 `null`。
- **动作顺序**：动作链串行；`CloseSelf` / `CloseWithResult` 必须最后；同一 session 同时只跑一条链，重复点击忽略并 Warning。
- **`Replace`**：先关闭当前声明式面板，再打开目标；目标打开失败时当前面板已经关闭（已知代价）。
- **`Open` / `Push`**：只用于 C# 面板，内部用 `OpenAsync<UIPanelBase>` / `PushAsync<UIPanelBase>`；目标不能是声明式面板或任何 `UIDialogPanel<>`；动作不带 `userData`，需要数据的导航走 `Custom`。
- **`ShowDialog`**：单飞；已有声明式对话框打开时拒绝并 Warning，不启动第二个等待。
- **`Custom`**：keyed 消息；无通配符、无反射、无第二事件系统。
- **HUD**：`UIDeclarativeHudItem : UIHudItem`，只绑定文本；`Kind=Hud`；无 `userData`、不入栈、不进面板字典；同一目标只挂一个；关闭用 `HideHud(target)`，`CloseAllAsync` 连带回收。
- **定义资产**：可为空（静态预制体）；`Kind` 必须与根组件一致。

## 线程契约

全部在主线程调用：创建/打开/关闭面板与 HUD、执行动作、发布/订阅消息、编辑器校验。本包不做线程调度，也不提供跨线程入口。

## 性能代价

- 文本绑定与节点查找只在 `OnOpen` 发生；本包不新增每帧 `OnUpdate`，没有每帧分配。
- 按钮动作只在点击时执行；`Custom` 发布的 keyed 消息由 `MessageManager` 处理，正常路径零分配。
- HUD 的每帧跟随由核心 `UIHudItem` 执行，代价与 C# HUD 相同；声明式面板/对话框无每帧逻辑，`UpdateTier` 保持默认即可。

## 已知限制

- **单根**：不能同时打开两个 `UIDeclarativePanel`；需要并存请用 C# 面板或阶段 4 的独立 Popup 类型。
- **对话框结果**：固定 `string`；`ParentClosed` 目前归入 `Canceled`；自定义 `TResult` 请写 C# `UIDialogPanel<TResult>`。
- **HUD 只做静态文本**：`UIDeclarativeHudItem` 不支持动作/页签/动态数据；无 `userData`、不入面板字典、同一目标只挂一个，关闭用 `HideHud(target)`。
- **数据源 / Popup**：本版本未实现，数据绑定留阶段 3、声明式 Popup 留阶段 4。
- **`Open`/`Push` 无 `userData`**：需要给 C# 面板传数据的导航走 `Custom`。
- **Tip 本地化**：核心 `TipConfig` 只接收原始字符串；需要本地化时调用方先 `LocalizationManager.Get(key)`。
- **UGUI Text 本地化**：核心只给 TMP 提供 `BindToLocalizedText`；UGUI `Text` 由本包手动 `Get/Subscribe`。
- **校验器是手动的**：保存/构建不会自动触发；CI 没有机器可读结果 API。
- **模板是导入时复制**：包升级不会自动更新消费方模板；卸载本包后模板根脚本会变 Missing Script。
- **定义资产采集**：推荐与预制体同包采集，或同包 `DependAssetCollector`；跨包显式采集会重复打包。
- **动作消息禁止缓冲订阅**：`SubscribeBuffered` 会重放最后一条动作，使用 `Subscribe`。

## 设计取舍

- **扩展包而非核心**：声明式面板属于可选的制作层，不进入核心包的「不预设 GamePlay 架构」边界。
- **SO 定义资产而非内联**：独立校验、干净 diff、可复用；同包采集已验证定义会随预制体进构建图。
- **keyed 消息而非第二事件系统**：复用 `MessageManager` 的订阅/异常隔离；消息不带面板引用，避免 keyed 重放缓存持有已回池对象。
- **动作集封闭且只含 UI 级动词**：领域语义一律走 `Custom`。
- **不复制核心九条预制体规则**：核心校验器管预制体，本包只校验声明式定义。

## 依赖

- 核心包 `com.venusy609.xframework`（`Assets/XFramework/`）。
- UniTask、YooAsset、TextMeshPro、Input System、Cinemachine（核心包的第三方依赖；安装顺序见核心包 README）。
- 本包 `package.json` 的 `dependencies` 为空；UPM 不支持 Git URL 传递依赖，必须手动按顺序安装。
