# XFramework

Unity 的模块化基础设施框架：一组彼此正交、可单独取用的服务，外加通用异步管线与显式启动引导。**不预设 GamePlay 架构。**

## 先判断要不要用

**适合**：

- 已经有一局游戏，想把资源 / UI / 存档 / 配置 / 消息 / 更新调度这些**基础设施**换成一套有契约、有测试、有文档的实现；
- 团队自己决定实体模型、生命周期与状态管理，不需要框架规定游戏怎么组织；
- 只想用其中一两个模块（例如只用 Message 或只用 Update），不必整体引入。

**不适合**：

- 想要一局能跑的游戏模板——本仓没有 Procedure / Entity / FSM / 网络 / 热更编排，这些是刻意不做的边界；
- 想要全家桶（GameFramework、QFramework、ET、TEngine 那一类把玩法脚手架一起给你）——它们更合适；
- 想要 DI 容器或编辑器全家桶——本仓的注入形态刻意是静态门面 + 接口 + `Initialize`，不追求容器统一。

一句话：**它只做地基，并把边界写进每个模块的 README**（「已知限制」与「设计取舍」两节）。如果你要的是「下载下来就是一款游戏」，它不是最短路径。

> **包本体在 [`Assets/XFramework/`](Assets/XFramework/)。** Cinemachine 3.x 的相机后端已内置在 Camera 模块
> （`Runtime/Camera/Cinemachine/`）；另有一个**可选**的 Luban 配置扩展包在
> [`Assets/XFramework.Luban/`](Assets/XFramework.Luban/)，核心包不依赖它、不用 Luban 的项目无需安装。
> 仓库根是一个**完整的 Unity 开发工程**（`ProjectSettings/`、场景、测试与构建工具都在这里），UPM 包只是它的一个子目录——
> 这就是下面引入方式必须带 `?path=` 的原因。

## 用这个包

- **[包 README](Assets/XFramework/README.md)** —— 设计哲学、快速开始、模块索引、可替换的实现、可配置项，以及各模块的已知限制
- **[CHANGELOG](Assets/XFramework/CHANGELOG.md)** —— 变更流水；升级前先看破坏性变更迁移表
- [MIT 许可](LICENSE)；包内另有一份副本，供只能看到包目录的读者

安装（Package Manager → Add package from git URL）：

```
https://github.com/venusir/xframework.git?path=Assets/XFramework#<发布 tag>
```

**`?path=Assets/XFramework` 是必需的**；`#` 后请钉一个发布 tag（见仓库的 tag 列表或 CHANGELOG 顶部），不写修订会跟随 `main`。
需要改框架代码的场景还有「git 子模块 + `file:`」这一种用法，两种方式与各自的坑都写在包 README 的「安装 XFramework」里。

Camera 模块的 Cinemachine 后端随核心包发布；`com.unity.cinemachine` 3.1.7 是核心 asmdef 的硬依赖，
按[包 README 的第三方依赖](Assets/XFramework/README.md#第三方依赖)安装（与 Input System 同型，核心包的
`package.json` 仍不声明依赖）。

### 可选：Luban 配置扩展

安装顺序：核心包 → Luban 官方 Unity 运行时 → 本扩展包。

```
https://github.com/venusir/xframework.git?path=Assets/XFramework.Luban#<发布 tag>
```

Luban 运行时 `com.code-philosophy.luban` 用 Git URL 安装并钉 `v1.2.0`：

```
https://github.com/focus-creative-games/luban_unity.git#v1.2.0
```

扩展包提供 Editor 一键导表、生成式 `IConfigRow<TKey>` 行接口实现与强类型适配器，并把 map / 单行 / 带索引表
注册进 `ConfigManager`；细节见[扩展包 README](Assets/XFramework.Luban/README.md)。核心包仍不内建、不依赖 Luban。

## 开发这个仓库

规则见 [`CLAUDE.md`](CLAUDE.md)，流程、提交节奏与这三条门禁的纪律见 [`Documentation/Workflow.md`](Documentation/Workflow.md)。

```powershell
pwsh -File Tools/run-tests.ps1              # 全量测试（PlayMode + EditMode，0 失败）
pwsh -File Tools/check-docs.ps1 -Enforce    # 文档门禁
```
