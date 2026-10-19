# XFramework Cinemachine —— 可选的 Cinemachine 3.x 相机后端

本包是 **XFramework 的可选扩展**：把 Cinemachine 的 vcam 激活 / 反激活与 Brain 过渡接进核心包的
`ICameraManager`，让游戏代码只调 `CameraManager`，不直接依赖 `CinemachineBrain` / `CinemachineCamera` 类型。

**它不是框架必需件**：不用 Cinemachine 的项目只用核心包 + `UnityCameraManager` 即可；本包只服务
Cinemachine 3.x 项目。核心包**零 Cinemachine 依赖**，版本锁只落在本包。

**它是适配器，不是相机行为库**：跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline
由 Cinemachine 提供并在 Cinemachine 里配置；本包只负责注册、优先级 / Brain 混合与状态查询。

- 包名：`com.venusy609.xframework.cinemachine`
- 版本：`0.1.0`
- 依赖：`com.unity.cinemachine` **3.1.7**（由本包 `package.json` 自动解析）；核心包 `com.venusy609.xframework`（需先安装）
- **核心包版本基线**：必须包含 `CameraManager` / `ICameraManager` 的版本；仓库 tag `0.3.0` **早于 Camera 模块**，不含本包所需的核心类型（安装时不要钉 `0.3.0`，跟随 `main` 或用包含 Camera 的 tag）
- 命名空间：`XFramework.XCamera.Cinemachine`

## 安装

**顺序：先核心包，后本包。** 本包 `package.json` 不能声明核心依赖（UPM 不支持 Git URL 依赖），
靠 asmdef 引用在编译期兜底；装反了会报程序集缺失。

### 方式一：Git URL（只读）

在工程 `Packages/manifest.json` 的 `dependencies` 中同时加两个：

```json
{
  "dependencies": {
    "com.venusy609.xframework": "https://github.com/venusir/xframework.git?path=Assets/XFramework",
    "com.venusy609.xframework.cinemachine": "https://github.com/venusir/xframework.git?path=Assets/XFramework.Cinemachine"
  }
}
```

要钉版本就在两个 URL 末尾追加**同一个** `#<tag>`（`?path=` 必须写在 `#` 之前）。不写 tag 跟随 `main`。
Cinemachine 3.1.7 由本包 `package.json` 自动解析；若自动解析失败，手动加
`"com.unity.cinemachine": "3.1.7"`。

### 方式二：git 子模块 + 本地路径（需要改代码）

```json
{
  "dependencies": {
    "com.venusy609.xframework": "file:../Submodules/XFramework/Assets/XFramework",
    "com.venusy609.xframework.cinemachine": "file:../Submodules/XFramework/Assets/XFramework.Cinemachine"
  }
}
```

### Package Manager UI

`+ → Add package from git URL...`，先核心 URL，再本包 URL。示例：`Window → Package Manager →
XFramework Cinemachine → Samples → Import`。

## 快速使用

```csharp
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera;
using XFramework.XCamera.Cinemachine;

// 1. 场景里已有 CinemachineBrain 与 vcam
var manager = new CinemachineCameraManager(brain, new Dictionary<string, CinemachineVirtualCameraBase>
{
    ["main"] = mainVcam,
    ["battle"] = battleVcam,
});

// 2. 注入核心门面
CameraManager.Initialize(manager);

// 3. 之后只调门面
CameraManager.Activate("main");
CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut);
var camera = CameraManager.OutputCamera; // brain.OutputCamera
```

## 本包给第三方什么

- **开箱即用的适配器**：不用自己写优先级切换、Brain 混合与状态反查。
- **已测试**：激活 / 反激活 / 过渡 / 状态查询有 PlayMode 用例；游戏代码面向 `ICameraManager`，测试时用核心的
  `FakeCameraManager`，不必搭 Cinemachine 场景。
- **游戏代码与 Cinemachine 类型解耦**：调用点不出现 `CinemachineBrain` / `CinemachineCamera`；换版本或换后端时
  只改适配器。
- **可选择性**：核心包不装 Cinemachine；只有本包需要它。
- **版本对齐**：本包钉住 Cinemachine 3.1.7，升级与 API 漂移由本包维护。

**局限**：适配器本身不大，项目完全可以自己写；它不隐藏 Cinemachine 的概念（Brain / vcam / 优先级 / custom
blends 仍要在场景里配）；行为有主张（一次一台活动相机、`Activate` 强制 cut、`CustomBlends` 会覆盖请求的
style / duration）；多一个包要装。

## 已知限制

- **只支持 Cinemachine 3.x**（按 3.1.7 编写）；2.x 的类型名与字段名不同，需要自己的 `ICameraManager` 实现。
- **动态 vcam 用 `CameraHandle`**（`Register<T>`）：预制 `OnDestroy` 里 `Unregister` / `Dispose`；动态 vcam
  活动时 `ActiveHandle.NamedId` / `CameraManager.ActiveCameraId` 为 `null`，用 `ActiveHandle`。
- **Timeline / 过场不接管**：过场 vcam 不注册；Brain 的活动相机未注册时激活 / 过渡会告警「可能与过场冲突」；
  细节见 Runtime README。
- **项目在 Brain 上配置的 `CustomBlends` 会覆盖** `TransitionTo` 请求的 style / duration；本包不强行清空项目配置。
- **未注册 vcam 的优先级不受管理**：请把本包要控制的 vcam 都注册进来，不要让它们用高于管理器的优先级。
- **`Activate` 在「正在混合且目标已是活动相机」时会短暂失活 / 激活目标 GameObject** 来强制 cut；副作用是该
  vcam 的 `PreviousStateIsValid` 重置。
- **`Deactivate` 的回退**由 Brain 的优先级队列决定；当前 `DefaultBlend` 会用于回退混合。
- `Register` / `Unregister` 只更新注册表与反查表，不改变场景；替换 / 注销活动相机前请先 `Deactivate`。

## 版本与维护

- 本包版本 `0.1.0`；**核心包必须包含 Camera 模块**（`0.3.0` tag 早于 Camera，不含本包所需类型）。
- 升级 Cinemachine 3.x 时只改本包；核心包的 `ICameraManager` 不变。
- **门禁射程**：核心包的 `check-docs` / `PublicApiBaseline` / README 表格与章节守卫**不覆盖本包**；本包只有
  自己的 `CinemachineDependencyTests`（依赖三处 / 示例路径 / 包内不指向包外）。这条缺口已登记在仓库级决策台账（R3-CIN-008）。
- 维护向记录（3.1.7 API 实测、决策与升级说明）在仓库根的维护文档里，不随本包发布。
