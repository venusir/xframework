# XFramework

Unity 的模块化基础设施框架：一组彼此正交、可单独取用的服务，外加通用异步管线与显式启动引导。**不预设 GamePlay 架构。**

> **包本体在 [`Assets/XFramework/`](Assets/XFramework/)。** 可选扩展包在
> [`Assets/XFramework.Cinemachine/`](Assets/XFramework.Cinemachine/)（Cinemachine 3.x 的相机后端，单独安装）。
> 仓库根是一个**完整的 Unity 开发工程**（`ProjectSettings/`、场景、测试与构建工具都在这里），UPM 包只是它的一个子目录——
> 这就是下面引入方式必须带 `?path=` 的原因。

## 用这个包

- **[包 README](Assets/XFramework/README.md)** —— 设计哲学、快速开始、模块索引、可替换的实现、可配置项，以及各模块的已知限制
- **[Cinemachine 扩展包](Assets/XFramework.Cinemachine/README.md)**（可选）—— 把 Cinemachine 3.x 的激活 / 过渡接进 Camera 模块；单独安装，核心包不依赖它
- **[CHANGELOG](Assets/XFramework/CHANGELOG.md)** —— 变更流水；升级前先看破坏性变更迁移表
- [MIT 许可](LICENSE)；包内另有一份副本，供只能看到包目录的读者

安装（Package Manager → Add package from git URL）：

```
https://github.com/venusir/xframework.git?path=Assets/XFramework#<发布 tag>
```

**`?path=Assets/XFramework` 是必需的**；`#` 后请钉一个发布 tag（见仓库的 tag 列表或 CHANGELOG 顶部），不写修订会跟随 `main`。
需要改框架代码的场景还有「git 子模块 + `file:`」这一种用法，两种方式与各自的坑都写在包 README 的「安装 XFramework」里。

Cinemachine 扩展包**先装核心包、再装它**（两个 URL 用同一个 tag）：

```
https://github.com/venusir/xframework.git?path=Assets/XFramework.Cinemachine#<发布 tag>
```

它会在 `package.json` 里声明 `com.unity.cinemachine` 3.1.7，UPM 自动解析；安装顺序、`file:` 用法与已知限制见
[扩展包 README](Assets/XFramework.Cinemachine/README.md)。

## 开发这个仓库

规则见 [`CLAUDE.md`](CLAUDE.md)，流程、提交节奏与这三条门禁的纪律见 [`Documentation/Workflow.md`](Documentation/Workflow.md)。

```powershell
pwsh -File Tools/run-tests.ps1              # 全量测试（PlayMode + EditMode，0 失败）
pwsh -File Tools/check-docs.ps1 -Enforce    # 文档门禁
```
