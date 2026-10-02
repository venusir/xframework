# 模块技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> 使用方文档在 `Assets/XFramework/Runtime/<模块>/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本目录**不复述、不复制**。
> **本目录不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 规则见 `CLAUDE.md` → 文档分层；审计结论的落点表见 `ModuleAudit.md` §五。

## 这里放什么

节名固定，顺序固定；**没有内容的节不建**（按需创建，不搞空壳）。

| 节 | 内容 | 先例 |
|---|---|---|
| 文件结构 | 模块的源文件清单与职责。只此一份，README 不再保留副本 | Asset / UI / File |
| 模块版本记录 | 模块独立成包时代的本地版本号——与包 `CHANGELOG.md` 的语义化版本**不是同一套** | Pool / Input / Config |
| 沿革与已否决形状 | 「旧实现…」「原先…」：当时形态 → 现状，带日期与提交号 | Reactive / Update |
| 迁移指南 | 破坏性 API 变更的升级路径 | Data（v1 Table → v2 Block） |
| 已完成功能与未做（roadmap） | 建了什么、还差什么 | UI |
| 已评估未采纳与未决 | 逐轮追加，**不覆盖**。下一轮审计只需读本节 | Lock / Localization |
| 审计轮次 | 过程性记录：扫了哪些判据、实测 vs 推理、本轮产出 | Lock / Localization |

**判据**：答的是「下一轮我要不要重新论证这件事」，而不是「我该怎么用」。把一段内容删掉，使用方会不会写错代码或误判行为？会 → 它属于 README，不属于这里。

## 文件

| | | |
|---|---|---|
| [Asset](Asset.md) | [Audio](Audio.md) | [Bootstrap](Bootstrap.md) |
| [Config](Config.md) | [Data](Data.md) | [Event](Event.md) |
| [File](File.md) | [Input](Input.md) | [Localization](Localization.md) |
| [Lock](Lock.md) | [Log](Log.md) | [Message](Message.md) |
| [Pipeline](Pipeline.md) | [Pool](Pool.md) | [Reactive](Reactive.md) |
| [Save](Save.md) | [Serialize](Serialize.md) | [Settings](Settings.md) |
| [Timer](Timer.md) | [UI](UI.md) | [Update](Update.md) |
| [Diagnostics](Diagnostics.md) | | |

## 模板

````markdown
# <模块> —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/<模块>/README.md`——已知限制、设计取舍、
> 接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

## 模块版本记录

## 沿革与已否决形状

## 迁移指南

## 已完成功能与未做（roadmap）

## 已评估未采纳与未决

## 审计轮次
````
