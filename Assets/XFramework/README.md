# XFramework

XFramework 是一个为 Unity 设计的**组合式框架插件**。目标：**引入插件后即可直接编写 GamePlay 逻辑**，无需额外的框架配置。

## 设计哲学

- **组合优于继承** — EntityNode 按类型缓存子节点，类似 Unity 的 GetComponent/AddComponent
- **对象池内置** — 所有节点通过 `NodeFactory` 创建，`Destroy()` 后自动回池
- **更新按需降级** — `IUpdateable.OnUpdate` 返回 `UpdateLOD` 等级，自动调整更新频率
- **异步加载管线** — 节点实现 `ILoadable` 装载加载任务，`StartupAsync` 统一调度
- **零配置初始化** — 纯静态服务（LockManager、MessageManager 等）通过 `[RuntimeInitializeOnLoadMethod]` 在游戏启动时自动就绪，无需手动初始化
- **静态外观 + 接口 + 内部实现** — 非节点服务采用静态类统一入口 + 接口定义契约 + 内部类实现，外部可注入自定义实现

## 核心架构

XFramework 服务分为两条路径：

### 路径 A：节点树（有状态 / 可组合服务）

```
GameLauncher (MonoBehaviour)
    │
    ├── RootNode ─── 节点树入口
    │       │
    │       ├── ServiceInitializerNode ─── 按需挂载的初始化节点（如 AssetBootstrapNode）
    │       │
    │       ├── EntityNode ─── 组件模式（按类型缓存子节点）
    │       │       ├── LeafNode (行为/数据)
    │       │       └── CompositeNode (公开 Add/Remove)
    │       │
    │       └── DictionaryNode<TKey> ─── 键值对模式
    │
    └── UpdateNode ─── 自动注册/注销 IUpdateable 节点 LOD 时间切片调度
```

### 路径 B：静态外观（无状态 / 全局服务）

各服务独立管理自身生命周期，无需统一的中心化入口：

| 服务                    | 初始化方式                                   | 说明                                                |
| ----------------------- | -------------------------------------------- | --------------------------------------------------- |
| **LockManager**         | `[RuntimeInitializeOnLoadMethod]` 自动就绪   | 零配置                                              |
| **MessageManager**      | `[RuntimeInitializeOnLoadMethod]` 自动就绪   | 零配置                                              |
| **FileManager**         | 首次调用时懒加载（自动选平台 Provider）      | 零配置；也可显式 `Initialize()` 注入自定义 Provider |
| **UIManager**           | `UIManager.Initialize(canvasTransform)`      | 需传入 Canvas 根节点                                |
| **UIHudManager**        | 随 UIManager 自动就绪                        | —                                                   |
| **UITipManager**        | 随 UIManager 自动就绪                        | —                                                   |
| **LocalizationManager** | `LocalizationManager.Initialize(lang, data)` | 需传入语言数据                                      |

> **关键设计决策：** 纯静态服务不依赖节点树生命周期。无需参数的服务通过 `[RuntimeInitializeOnLoadMethod]` 或懒加载自动就绪；需要参数的服务由调用方显式 `Initialize()`。不存在统一的中心化入口。节点树仅承载需要生命周期插件的服务（如 AssetManager）。

## 快速开始

### 方式一：零配置使用

```csharp
// 无需手动初始化任何服务！LockManager、MessageManager 已自动就绪。

// 直接使用消息系统
MessageManager.Subscribe<PlayerDiedMessage>(msg =>
{
    Debug.Log($"Player {msg.PlayerId} died!");
});

// 直接使用锁系统
using (LockManager.AddLock(player, LockType.InputBlock, this))
{
    // 此时玩家输入被锁
}
```

### 方式二：完整节点树

```csharp
// 在场景中挂载 GameLauncher 组件

public class MyGameLauncher : GameLauncher
{
    async void Start()
    {
        var player = _root.AddNode<PlayerNode>(100);
        await _root.StartupAsync();
    }
}
```

## 主要功能

| 功能             | 说明                                                                              |
| ---------------- | --------------------------------------------------------------------------------- |
| **节点树**       | 层级化节点结构，支持深度排序、递归遍历                                            |
| **对象池**       | `NodeFactory` + `NodePool<T>`，自动回池复用                                       |
| **组件模式**     | `EntityNode.GetNode<T>()` / `AddNode<T>()` / `RemoveNode<T>()`                    |
| **键值对模式**   | `DictionaryNode<TKey>` 按 Key 缓存子节点                                          |
| **更新调度**     | `IUpdateable` + `UpdateLOD` 时间切片，自动 LOD 迁移                               |
| **异步加载**     | `ILoadableProvider` + `LoadableBase` + `LoadingManager`                           |
| **生命周期**     | Init → Awake → Start → Destroy，与 Unity 语义一致                                 |
| **UI 面板管理**  | `UIManager.OpenAsync<T>()` 异步打开/关闭面板，支持栈式导航、模态遮罩              |
| **Tip 临时提示** | 扣血提示、浮动文字等临时 UI，支持世界坐标定位、渐隐动画、对象池复用               |
| **配置管理**     | `ConfigManager` 支持 JSON / ScriptableObject / Luban 三种格式，一行代码加载与查询 |

## UI 系统

XFramework 提供一套完整的 UI 管理方案，包括面板生命周期管理和临时提示（Tip）。UI 基于 Canvas + UGUI 渲染，通过 `UIRootNode` 挂载在场景中作为 UI 根节点。

### 初始化

```csharp
// 在场景中挂载 UIRootNode，然后初始化
var uiRoot = FindObjectOfType<UIRootNode>();
if (uiRoot != null)
{
    UIManager.Initialize(uiRoot.transform);
}
```

### 面板管理

面板继承 `UIPanelBase`，通过 `UIManager` 静态方法管理生命周期：

```csharp
// 打开面板
var panel = await UIManager.OpenAsync<MainMenuPanel>("PF_MainMenu", layer: 100);

// 关闭面板
await UIManager.CloseAsync<MainMenuPanel>();

// 栈式导航
var settings = await UIManager.PushAsync<SettingsPanel>("PF_Settings", layer: 200);
await UIManager.PopAsync();  // 返回上一个面板

// 模态遮罩
UIManager.ShowMask(maskLayer: 500, alpha: 0.5f);
UIManager.HideMask();
```

### 临时提示（Tip / 扣血提示）

用于显示无需交互的浮动提示文字，如扣血数字、暴击提示、获得物品等。通过 `UIManager.ShowTip()` 一行代码即可使用。

> 📖 详细文档请参阅 **[Runtime/UI/README.md - Tip 临时提示](Runtime/UI/README.md#tip-临时提示扣血提示--浮动文字)**，包含 `TipConfig` 参数说明、预制体要求和架构详解。

## 依赖

- Unity 6000.3 或更新版本

### 第三方依赖

XFramework 依赖以下第三方包。由于 Unity 包管理器的限制，这些依赖需要在**项目根目录的 `Packages/manifest.json`** 中声明，而非在 XFramework 的 `package.json` 中。

| 包名                                                         | 版本/URL                                                                         | 说明         | 安装方式 |
| ------------------------------------------------------------ | -------------------------------------------------------------------------------- | ------------ | -------- |
| [UniTask](https://github.com/Cysharp/UniTask)                | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` | 异步操作库   | UPM      |
| [YooAsset](https://github.com/tuyoogame/YooAsset)            | `https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset`                 | 资源管理系统 | UPM      |
| [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) | `https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity`         | NuGet 包管理 | UPM      |
| [R3](https://github.com/Cysharp/R3)                          | `1.2.9`（NuGet 包）                                                              | 响应式编程库 | NuGet    |

### 安装依赖

> **重要：** 由于 Unity 包管理器的限制，UPM 包的 `package.json` 中 `dependencies` 字段只支持语义化版本号，不支持 Git URL。因此 XFramework 不在自身 `package.json` 中声明第三方依赖，而是需要您在**项目根目录的 `Packages/manifest.json`** 中手动添加。

**⚠️ 重要：请按以下顺序操作，避免编译报错导致死锁。**

XFramework 的 asmdef 引用了 R3，如果先添加 XFramework 再装 R3，会因编译报错导致 Editor 脚本无法运行，从而无法通过菜单安装依赖。因此请**在添加 XFramework 之前**，先手动配置好所有依赖。

#### 第一步：配置 UPM 依赖

在项目 `Packages/manifest.json` 的 `dependencies` 中添加以下三个包：

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset",
    "com.github-glitchenzo.nugetforunity": "https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity"
  }
}
```

#### 第二步：配置 NuGet 依赖（R3）

R3 通过 NuGetForUnity 安装，需要在项目 `Assets/packages.config` 中声明。如果文件不存在则创建，写入以下内容：

```xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="R3" version="1.2.9" manuallyInstalled="true" />
  <package id="Microsoft.Bcl.AsyncInterfaces" version="6.0.0" />
  <package id="Microsoft.Bcl.TimeProvider" version="8.0.0" />
  <package id="System.ComponentModel.Annotations" version="5.0.0" />
  <package id="System.Runtime.CompilerServices.Unsafe" version="6.0.0" />
  <package id="System.Threading.Channels" version="8.0.0" />
</packages>
```

#### 第三步：打开 Unity 并 Restore NuGet 包

打开 Unity Editor，等待 NuGetForUnity 自动检测到 `packages.config` 中的变更，然后点击菜单栏 `NuGet -> Restore` 下载 R3 及其依赖。

#### 第四步：添加 XFramework

完成以上步骤后，再通过 Git URL 或本地路径添加 XFramework。此时所有依赖已就绪，不会出现编译报错。

---

**如果已经先添加了 XFramework 导致编译报错：**

关闭 Unity Editor，手动编辑 `Packages/manifest.json` 和 `Assets/packages.config`（按上述第一、二步配置），然后重新打开 Unity。编译通过后，即可正常使用。

---

## 配置管理

`ConfigManager` 提供统一的配置加载与查询接口，支持 JSON / ScriptableObject / Luban 三种格式，一行代码加载与查询。

> 📖 详细文档请参阅 **[Runtime/Config/README.md](Runtime/Config/README.md)**，包含格式对比、自定义格式和 Luban 集成指南。

