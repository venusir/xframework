# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `UIDeclarativeHudItem : UIHudItem`：声明式 HUD（阶段 2b），`Kind=Hud` 的定义只绑定静态文本；不绑定动作，关闭用核心 `UIManager.HideHud(target)`。
- 校验器新增 `UI-AUTH-HUD-*`（根 RectTransform、动作/页签不支持、文本节点超深），扫描器候选加入 HUD。
- `Samples~/Templates/Hud.prefab`、`Samples~/DeclarativeDemo` 的 DemoHud 预制体/定义与演示脚本 HUD 跟随。

### Not verified

- HUD 模板的裸消费方导入验收（P3 机制已过，实际模板未跑）。

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
