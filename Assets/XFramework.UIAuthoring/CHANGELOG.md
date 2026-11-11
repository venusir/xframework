# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `IUIDataProvider` / `UIDataProviderRegistry`（阶段 3）：强类型 `TryGet<T>` 数据源接缝，后注册优先，provider 异常隔离，`SubsystemRegistration` 会话复位。
- `UIDeclarativePopupPanel : UIPanelBase`（阶段 4）与 `UIPanelDefinitionKind.Popup`：声明式锚定弹窗，可与声明式面板/对话框同时打开。
- `UIActionKind.ShowPopup` 与 `UIAction.AnchorNodeName` / `Direction` / `Align` / `CloseOnClickOutside`；`UIAuthoringNavigation.ShowPopupAsync`（同地址重摆、异地址拒绝）。
- 校验器新增 `UI-AUTH-080`（ShowPopup 锚点缺失/非 RectTransform）；`ShowPopup` 仅允许 Panel 定义，Dialog/Popup 报 ActionNotAllowed。
- `Samples~/Templates/Popup.prefab` 与 DemoPopup 示例（面板 `btn_popup` 经 `ShowPopup` 打开）。
- `UIDataBinding` / `UIDataBindingKind`（Text / Sprite / Color / FillAmount / Slider / Toggle / Active）与 `UIPanelDefinition.DataBindings`；Panel / HUD / Dialog 打开时绑定，订阅随回池释放。
- `Slider` / `Toggle` 双向：源实现 `IReactivePropertyWriter<T>` 时启用；只读源降级为单向并告警。
- 校验器新增 `UI-AUTH-070..075`（节点/空 key/目标组件/TwoWay/Format/同节点冲突）与 `UI-AUTH-HUD-005`（HUD 双向不可交互）。
- `UIDeclarativeHudItem`：声明式 HUD（阶段 2b），`Kind=Hud` 的定义只绑定静态文本；不绑定动作，关闭用核心 `UIManager.HideHud(target)`。
- 校验器新增 `UI-AUTH-HUD-*`（根 RectTransform、动作/页签不支持、文本节点超深），扫描器候选加入 HUD。
- `Samples~/Templates/Hud.prefab`、`Samples~/DeclarativeDemo` 的 DemoHud 预制体/定义与演示脚本 HUD 跟随。
- `Samples~/DeclarativeDemo` 增加 `UIAuthoringDemoDataProvider`、DemoPanel 的图片/滑条/开关节点与 DemoHud 血条，演示文本、颜色、滑条双向、开关双向、HUD 数据绑定。

### Fixed

- `ShowDialog` 在空 `assetPath` / `actionId` 上的失败现在与其它拒绝分支一致：返回 `OpenFailed`，`actionId` 非空时同时发布 `UIAuthoringDialogResultMessage`（`UIAuthoringNavigation` 与 `UIManagerActionRuntime` 两个入口一致）。
- 编辑器校验器新增独立规则 `UI-AUTH-013`（文本绑定启用本地化但缺 `LocalizationKey`）；此前复用 `UI-AUTH-010 TextNodeMissing`，报告里无法区分「节点不存在」与「缺 key」。
- 编辑器校验器现在拒绝把 `UIDeclarativePopupPanel` 作为 `Open` / `Push` 的目标（此前只拒绝声明式面板与 `UIDialogPanel<>`，弹窗会被当普通面板层打开，绕过锚定/层语义）。
- `ShowDialog` 的取消不再被吞成 `OpenFailed` + Error 日志：`OperationCanceledException` 原样传播给调用方，与 `OpenAsync` 的取消语义一致。
- 控制器拦下关闭（`OnBeforeCloseAsync` 返回 `false`）时，`CloseSelf` / `Replace` / `CloseWithResult` 不再使动作会话失效——面板仍开着，按钮不再变成空操作。
- `CloseWithResult` 在关闭被拦下时立即丢弃待兑现结果，并在下一次 `OnOpen` 复位；过期选择不会再被之后的 `CloseAllAsync` / 父面板关闭 / 回池误兑现。
- `UIDeclarativeBinder` 的告警 helper 直接传 `params object[]` 会被 `LogManager` 的单参泛型重载抢走，格式串原样输出；改为显式 `string.Format` 后再记日志。

### Not verified

- IL2CPP/AOT（P6）：`IUIDataProvider.TryGet<T>` 的泛型实例化未做 IL2CPP 构建验证。
- TMP 路径只做了一次性探针；日常自动化测试仍只覆盖 UGUI `Text`（TMP Essentials 不随包提交）。

## [0.1.0] - 2026-11-08

### Added

- `UIDeclarativePanel` / `UIDeclarativeDialogPanel`：声明式面板与声明式对话框（结果类型固定 `string`）。
- `UIPanelDefinition` 定义资产与 `Kind`（Panel / Hud / Dialog；2a 只实现 Panel / Dialog）。
- 封闭 UI 级动作集：关闭、跳转、压退栈、Tip、对话框、层显隐/交互、节点显隐、页签、自定义消息。
- `UIAuthoringActionMessage`（keyed，不带面板引用）与 `UIAuthoringDialogResultMessage` + `UIAuthoringDialogStatus`。
- `UIAuthoringNavigation` 单根守卫与对话框单飞入口。
- 编辑器校验器 `UI-AUTH-*`、扫描窗口与 `UIPanelDefinition` 基础 Inspector。
- `Samples~/Templates`（面板/对话框模板）与 `Samples~/DeclarativeDemo`。
- 包内 README/文件树、`.meta`、包边界、公开 API 基线等守卫。

### Not verified

- 真实 YooAsset Offline 构建下的定义资产加载（阶段 2a 一次性探针）。
