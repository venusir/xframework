# XFramework

XFramework 是一个为 Unity 设计的**模块化基础设施框架**。它提供彼此正交的运行时服务，以及两套编排原语——通用异步管线与显式启动引导注册表。它**不预设 GamePlay 架构**：实体模型、生命周期组织与时间模型都由使用方自己决定。

## 设计哲学

- **单项职责的服务** — 每个模块只做一件事，彼此正交，可单独取用
- **静态外观 + 接口 + 内部实现** — 静态类统一入口 + 接口定义契约 + 内部类实现，外部可注入自定义实现
- **零配置或显式初始化** — 无参服务（LockManager、MessageManager、UpdateManager 等）经 `[RuntimeInitializeOnLoadMethod]` 自就绪；需要配置的服务由调用方显式 `Initialize`，或实现 `IBootstrapStage` 交给启动引导
- **通用编排 + 相位分组** — `Pipeline` 提供串行/并行阶段编排、加权进度、失败即停与取消传播；实现 `IPhaseStage` 声明相位号（同相位并行、相位升序串行），`Pipeline.BuildPhaseGroups` 一键装配
- **更新按需降级** — `IUpdateable.OnUpdate` 返回 `UpdateTier` 等级，调度器自动调整其更新频率；档位按**时长**分档，与帧率无关
- **不预设 GamePlay 架构** — 框架不决定实体模型、生命周期树与时间模型

## 核心架构

框架分两层：**彼此正交的基础设施服务**，与**把它们按顺序装配起来的编排原语**。

### 基础设施服务

| 服务                    | 初始化方式                                          | 说明                                                       |
| ----------------------- | --------------------------------------------------- | ---------------------------------------------------------- |
| **LockManager**         | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置                                                     |
| **MessageManager**      | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置                                                     |
| **UpdateManager**       | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 零配置，含 PlayerLoop 自注入——场景里不需要任何 MonoBehaviour |
| **Serializer**          | `[RuntimeInitializeOnLoadMethod]` 自动就绪          | 注册内置序列化器（json / json-utility）                    |
| **FileManager**         | 首次调用时懒加载（自动选平台 Provider）             | 零配置；也可显式 `Initialize()` 注入自定义 Provider        |
| **AssetManager**        | `AssetManager.InitializeAsync()`                    | 或实现 `AssetBootstrapStage` 交由启动引导                  |
| **DataManager**         | `DataManager.Initialize(impl)`                      | 同上（`DataBootstrapStage`）                               |
| **SaveManager**         | `SaveManager.Initialize(options)`                   | 同上（`SaveBootstrapStage`）                               |
| **LocalizationManager** | `LocalizationManager.Initialize(lang, data)`        | 同上（`LocalizationBootstrapStage`）                       |
| **UIManager**           | `UIManager.Initialize(canvasTransform)`             | 需传入 Canvas 根节点                                       |
| **SettingsManager**     | `SettingsManager.Initialize<T>(path)`               | 设置类型由业务定义，每类独立初始化                         |

> **关键设计决策：** 服务不依赖统一入口。无需参数的服务自动就绪；需要参数的服务由调用方显式初始化，或实现 `IBootstrapStage` 登记进启动引导。

### 启动引导

需要按顺序初始化的服务实现 `IBootstrapStage`，然后显式登记：

```csharp
Bootstrap.RegisterDefaults();                                          // Asset(0) → Data(3) → Save(4)
Bootstrap.Register(new LocalizationBootstrapStage("zh_Hans", myTable)); // Phase 90
await Bootstrap.RunAsync(progress: myProgress);
// ...
Bootstrap.Shutdown();                                                  // 逆登记顺序清理
```

`RunAsync` 在失败与取消时**抛出**——启动失败是致命的，不该只留一条日志。整块是**可选**的：零配置项目与自建启动流程的项目都可以不用它。

> 📖 详见 **[Runtime/Bootstrap/README.md](Runtime/Bootstrap/README.md)**

### 通用管线

与启动引导正交。任何「若干异步阶段按序/并行执行 + 加权进度 + 失败即停」的场合都可直接用：

```csharp
var pipeline = Pipeline.Create();
pipeline.AddStage(new MyStage());
await pipeline.RunAsync(cancellationToken);
pipeline.Destroy();
```

实现 `IPhaseStage` 声明相位号，`Pipeline.BuildPhaseGroups` 即自动分组（同相位并行、相位升序串行）。

> 📖 详见 **[Runtime/Pipeline/README.md](Runtime/Pipeline/README.md)**

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

### 方式二：带启动引导

```csharp
// 场景里挂一个 GameLauncher 即可；或在自己的启动流程里显式调用：
Bootstrap.RegisterDefaults();
await Bootstrap.RunAsync();
```

## 主要功能

| 功能             | 说明                                                                              |
| ---------------- | --------------------------------------------------------------------------------- |
| **更新调度**     | `UpdateManager` + `UpdateTier` 时间切片，自动档位迁移；档位按**时长**分档，与帧率无关 |
| **通用管线**     | `Pipeline` 阶段编排（串行/并行/加权进度/失败即停/取消传播）；`IPhaseStage` 相位分组一键装配 |
| **启动引导**     | `Bootstrap` 显式登记 + 相位装配 + 逆序清理；内置 Asset/Data/Save 三件，Localization 可选 |
| **对象池**       | `PoolManager` + `CollectionPool`（List / HashSet / Dictionary / StringBuilder）    |
| **UI 面板管理**  | `UIManager.OpenAsync<T>()` 异步打开/关闭面板，支持栈式导航、模态遮罩              |
| **Tip 临时提示** | 扣血提示、浮动文字等临时 UI，支持世界坐标定位、渐隐动画、对象池复用               |
| **配置管理**     | `ConfigManager` 内置 Json / CSV / ScriptableObject 格式，支持自定义 Loader 与 Register 注入，一行代码加载与查询 |
| **消息总线**     | `MessageManager` 类型化发布/订阅、带 Key 通道、缓冲重放、异步发布 `PublishAsync`、请求-响应、全局过滤器、缓冲淘汰与运行统计 |
| **响应式属性**   | `ReactiveProperty<T>` 状态同步：订阅即回调当前值、相同值去重，支持 `Select` 派生与 UI 绑定 |

## UI 系统

XFramework 提供一套完整的 UI 管理方案，包括面板生命周期管理和临时提示（Tip）。UI 基于 Canvas + UGUI 渲染，通过 `UIRootNode` 挂载在场景中作为 UI 根节点。

### 初始化

```csharp
// 在场景中挂载 UIRootNode，然后初始化
var uiRoot = FindFirstObjectByType<UIRootNode>();
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

用于显示无需交互的浮动提示文字，如扣血数字、暴击提示、获得物品等。通过 `UIManager.ShowTipAsync()` 一行代码即可使用。

> 📖 详细文档请参阅 **[Runtime/UI/README.md - Tip 临时提示](Runtime/UI/README.md#tip-临时提示扣血提示--浮动文字)**，包含 `TipConfig` 参数说明、预制体要求和架构详解。

## 依赖

- Unity 6000.3 或更新版本

### 第三方依赖

XFramework 依赖以下第三方包。由于 Unity 包管理器的限制，这些依赖需要在**项目根目录的 `Packages/manifest.json`** 中声明，而非在 XFramework 的 `package.json` 中。

| 包名                                              | 版本/URL                                                                         | 说明         | 安装方式 |
| ------------------------------------------------- | -------------------------------------------------------------------------------- | ------------ | -------- |
| [UniTask](https://github.com/Cysharp/UniTask)     | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` | 异步操作库   | UPM      |
| [YooAsset](https://github.com/tuyoogame/YooAsset) | `https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset`                 | 资源管理系统 | UPM      |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | `com.unity.nuget.newtonsoft-json`（3.2.1）                           | JSON 序列化库 | UPM      |

### 安装依赖

> **重要：** 由于 Unity 包管理器的限制，UPM 包的 `package.json` 中 `dependencies` 字段只支持语义化版本号，不支持 Git URL。因此 XFramework 不在自身 `package.json` 中声明第三方依赖，而是需要您在**项目根目录的 `Packages/manifest.json`** 中手动添加。

在项目 `Packages/manifest.json` 的 `dependencies` 中添加以下三个包：

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset",
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

配置完成后，再通过 Git URL 或本地路径添加 XFramework。消息总线（XMessage）与响应式属性（XReactive）均为框架自研实现，无需额外依赖。

---

## 配置管理

`ConfigManager` 提供统一的配置加载与查询接口，内置 Json / CSV / ScriptableObject 三种格式，自定义格式通过实现 `IConfigLoader` 扩展，Luban 等外部反序列化结果经 `RegisterTable` / `RegisterGlobal` 注入，一行代码加载与查询。

> 📖 详细文档请参阅 **[Runtime/Config/README.md](Runtime/Config/README.md)**，包含格式对比、自定义 Loader 和 Luban 集成指南。

