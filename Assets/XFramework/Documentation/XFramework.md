# XFramework 文档

## 概述

XFramework 是一个基于**静态服务 + 节点树**双轨架构的 Unity 组合式框架，完全基于纯 C# 实现，不依赖 MonoBehaviour 继承。引入插件后即可直接编写 GamePlay 逻辑，无需额外的框架配置。

### 架构双轨

| 路径                   | 定位                            | 典型模块                                                               |
| ---------------------- | ------------------------------- | ---------------------------------------------------------------------- |
| **静态服务（无状态）** | 全局 Manager 入口，按需初始化   | File / Input / Settings / UI / Lock / Reactive / Localization / Update |
| **节点树（有状态）**   | GamePlay 层级组织，生命周期管理 | Core（RootNode / EntityNode / DictionaryNode）/ Asset / Loader         |

### 解决问题

| 痛点               | XFramework 方案                                             |
| ------------------ | ----------------------------------------------------------- |
| MonoBehaviour 耦合 | 纯 C# 节点树 + 静态服务，可脱离 GameObject 运行             |
| 生命周期混乱       | 树形有序生命周期：Awake → Start → Update → Destroy          |
| 频繁 GC 分配       | 节点级对象池 + 静态服务零分配设计，自动回收复用             |
| 更新调度粗放       | LOD 分级调度，统一管理节点树与静态服务的 Update             |
| 资源管理分散       | 统一资源服务：加载 / 对象池 / 引用计数 / 延迟卸载           |
| 跨模块耦合         | Provider 模式：接口 + 内部实现 + 扩展方法，可注入自定义实现 |

### 核心概念

| 概念              | 说明                                                              |
| ----------------- | ----------------------------------------------------------------- |
| **节点树**        | 层级化树形结构，每个节点有 Depth 属性                             |
| **组件模式**      | EntityNode 按类型缓存子节点（类 GetComponent）                    |
| **对象池**        | 节点销毁后自动回池，减少 GC                                       |
| **LOD 更新**      | 节点返回 UpdateLOD，自动调整更新频率                              |
| **Phase 调度**    | 启动管线按 Phase 分组，同 Phase 并行、不同 Phase 串行             |
| **静态服务**      | 非节点模块通过静态 Manager 类提供全局入口                         |
| **Provider 模式** | 接口定义契约 + 内部默认实现 + 扩展方法，外部可注入自定义实现      |
| **自动初始化**    | 纯静态服务通过 `[RuntimeInitializeOnLoadMethod]` 或懒加载自动就绪 |

---

## 模块索引

每个模块的详细文档、API 和代码示例见对应 README：

| 模块             | 命名空间                   | 文档                                        | 职责                                                          |
| ---------------- | -------------------------- | ------------------------------------------- | ------------------------------------------------------------- |
| **Core**         | `XFramework.XCore`         | [README](../Runtime/Core/README.md)         | 节点树核心：生命周期、EntityNode、DictionaryNode、对象池      |
| **Loader**       | `XFramework.XLoader`       | [README](../Runtime/Loader/README.md)       | 启动管线：Phase 分组调度、一键启动、进度广播                  |
| **Asset**        | `XFramework.XAsset`        | [README](../Runtime/Asset/README.md)        | 资源管理：异步加载、实例化、对象池、场景加载（基于 YooAsset） |
| **Update**       | `XFramework.XUpdate`       | [README](../Runtime/Update/README.md)       | 统一更新调度：节点树 & 静态服务、LOD 时间切片                 |
| **Reactive**     | `XFramework.XReactive`     | [README](../Runtime/Reactive/README.md)     | 响应式：消息总线、响应式属性、信号（基于 R3）                 |
| **Localization** | `XFramework.XLocalization` | [README](../Runtime/Localization/README.md) | 本地化：多语言文本、语言切换、UI 自动绑定                     |
| **File**         | `XFramework.XFile`         | [README](../Runtime/File/README.md)         | 跨平台文件系统：路径域抽象、自动选平台 Provider               |
| **Input**        | `XFramework.XInput`        | [README](../Runtime/Input/README.md)        | 输入抽象层：纯字符串 API、多设备检测、零 GC                   |
| **Settings**     | `XFramework.XSettings`     | [README](../Runtime/Settings/README.md)     | 强类型游戏设置：JSON 持久化、响应式通知、重置                 |
| **UI**           | `XFramework.XUI`           | [README](../Runtime/UI/README.md)           | UI 面板管理 / MVVM 绑定 / 导航堆栈 / HUD / Tip                |
| **Lock**         | `XFramework.XLock`         | [README](../Runtime/Lock/README.md)         | 逻辑锁：多类型锁叠加、全局锁、using 自动释放                  |

---

## 目录结构

```
Assets/XFramework/
├── Runtime/                      # 运行时代码
│   ├── Core/                     # 节点树核心（BaseNode / EntityNode / 对象池）
│   ├── Loader/                   # 启动加载管线
│   ├── Asset/                    # 资源管理（基于 YooAsset）
│   ├── Update/                   # 统一更新调度
│   ├── Reactive/                 # 响应式（消息/R3）
│   ├── Localization/             # 本地化
│   ├── File/                     # 跨平台文件系统
│   ├── Input/                    # 输入抽象
│   ├── Settings/                 # 游戏设置
│   ├── UI/                       # UI 面板 / HUD / Tip
│   ├── Lock/                     # 逻辑锁
│   └── GameLauncher.cs           # Unity ↔ 节点树生命周期桥接
├── Documentation/
│   └── XFramework.md             # 本文档
├── Tests/                        # 单元测试
├── Editor/                       # 编辑器扩展
├── package.json                  # UPM 包配置
├── README.md
└── CHANGELOG.md
```

---

## 节点体系速览

```
BaseNode (抽象基类)
  ├── LeafNode          ← 叶子节点，无子节点
  └── ParentNode        ← 可包含子节点
        ├── ContainerNode     ← 公开 Add/Remove
        ├── EntityNode        ← 按类型缓存（组件模式）
        │     └── RootNode    ← 节点树入口
        └── DictionaryNode<TKey> ← 按键缓存
```

生命周期：`Awake → Start → (Update) → Destroy → 自动回池`

---

## 启动流程

```
GameLauncher.Start()
  ├── UpdateManager.Bind(root)      # 绑定更新调度
  └── root.StartupAsync()
        ├── 装载：收集所有 ILoadable
        ├── 加载：按 Phase 分组调度（并行+串行）
        ├── 启动：递归 OnStart
        └── 回收：清理加载器
```

---

## 快速参考

### 节点创建与操作

| 操作                   | 代码                             |
| ---------------------- | -------------------------------- |
| 创建根节点             | `RootNode.Create()`              |
| 从池获取               | `NodeFactory.GetNode<T>()`       |
| 获取子节点（自动创建） | `entity.GetNode<T>()`            |
| 获取子节点（不创建）   | `entity.GetNode<T>(false)`       |
| 添加子节点             | `entity.AddNode<T>()`            |
| 异步添加               | `await entity.AddNodeAsync<T>()` |
| 移除子节点             | `entity.RemoveNode<T>()`         |
| 沿父链查找服务         | `this.Get<IAssetManager>()`      |
| 销毁（自动回池）       | `node.Destroy()`                 |
| 预热池                 | `NodeFactory.Prewarm<T>(10)`     |

### 资源操作

| 操作       | 代码                                                    |
| ---------- | ------------------------------------------------------- |
| 加载资源   | `await AssetManager.LoadAsync<T>(location)`             |
| 实例化     | `await AssetManager.InstantiateAsync(location, parent)` |
| 回收实例   | `AssetManager.DestroyInstance(go)`                      |
| 预加载     | `await AssetManager.PreloadAllAsync(locations)`         |
| 加载场景   | `await AssetManager.LoadSceneAsync(location)`           |
| 设置池大小 | `AssetManager.SetPoolMaxSize(location, 10)`             |

### 消息操作

| 操作        | 代码                                                 |
| ----------- | ---------------------------------------------------- |
| 发布        | `MessageManager.Publish(msg)`                        |
| 订阅        | `MessageManager.Subscribe<T>(handler)`               |
| 带 Key 发布 | `MessageManager.Publish(key, msg)`                   |
| 异步订阅    | `MessageManager.SubscribeAsync<T>(handler)`          |
| 缓冲订阅    | `MessageManager.SubscribeBuffered<T>(handler)`       |
| 请求-响应   | `await MessageManager.RequestAsync<TReq, TRes>(req)` |

### 本地化操作

| 操作     | 代码                                                  |
| -------- | ----------------------------------------------------- |
| 获取文本 | `LocalizationManager.GetText(key)`                    |
| 切换语言 | `await LocalizationManager.SetLanguageAsync("en-US")` |
| 当前语言 | `LocalizationManager.CurrentLanguage`                 |
| 检查 Key | `LocalizationManager.HasKey(key)`                     |

### 文件操作

| 操作           | 代码                                                                 |
| -------------- | -------------------------------------------------------------------- |
| 读取文本       | `FileManager.ReadAllText(FileDomain.SaveFile, "a.json")`             |
| 写入文本       | `FileManager.WriteAllText(FileDomain.SaveFile, "a.json", data)`      |
| 读取字节       | `FileManager.ReadAllBytes(FileDomain.StreamingAssets, "config.dat")` |
| 写入字节       | `FileManager.WriteAllBytes(FileDomain.SaveFile, "save.dat", bytes)`  |
| 文件是否存在   | `FileManager.Exists(FileDomain.SaveFile, "a.json")`                  |
| 删除文件       | `FileManager.Delete(FileDomain.SaveFile, "a.json")`                  |
| 注入自定义实现 | `FileManager.Initialize(new MyFileProvider())`                       |

### 输入操作

| 操作             | 代码                                 |
| ---------------- | ------------------------------------ |
| 查询按键按下     | `InputManager.GetButtonDown("Jump")` |
| 查询按键抬起     | `InputManager.GetButtonUp("Jump")`   |
| 查询持续按住     | `InputManager.GetButton("Fire")`     |
| 获取轴值         | `InputManager.GetAxis("Move")`       |
| 获取当前设备类型 | `InputManager.CurrentDeviceType`     |
| 获取手柄类型     | `InputManager.GamepadType`           |

### 设置操作

| 操作     | 代码                                                                 |
| -------- | -------------------------------------------------------------------- |
| 加载设置 | `await SettingsManager.LoadAsync<MySettings>()`                      |
| 保存设置 | `await SettingsManager.SaveAsync<MySettings>()`                      |
| 获取值   | `SettingsManager.Get<MySettings>().MasterVolume`                     |
| 重置默认 | `SettingsManager.ResetToDefaults<MySettings>()`                      |
| 订阅变更 | `SettingsManager.Get<MySettings>().MasterVolume.Subscribe(v => ...)` |
| 应用设置 | `await SettingsManager.ApplyAsync<MySettings>()`                     |

### UI 操作

| 操作         | 代码                                                                 |
| ------------ | -------------------------------------------------------------------- |
| 打开面板     | `await UIManager.OpenAsync<MainMenuPanel>("PF_MainMenu")`            |
| 关闭面板     | `await UIManager.CloseAsync<MainMenuPanel>()`                        |
| 压入导航堆栈 | `await UIManager.PushAsync<SettingsPanel>("PF_Settings")`            |
| 弹出导航堆栈 | `await UIManager.PopAsync()`                                         |
| 显示模态遮罩 | `UIManager.ShowMask(maskLayer: 500, alpha: 0.5f)`                    |
| 隐藏模态遮罩 | `UIManager.HideMask()`                                               |
| 显示临时提示 | `UIManager.ShowTip(new TipConfig { Text = "+100", WorldPos = pos })` |

### 更新操作

| 操作           | 代码                                               |
| -------------- | -------------------------------------------------- |
| 注册到更新调度 | `UpdateManager.Register(this)`                     |
| 注销更新       | `UpdateManager.Unregister(this)`                   |
| 节点树自动绑定 | `UpdateManager.Bind(rootNode)`                     |
| 实现 LOD 降级  | `UpdateLOD IUpdateable.OnUpdate(float dt) { ... }` |

### 锁操作

| 操作           | 代码                                                 |
| -------------- | ---------------------------------------------------- |
| 加锁           | `LockManager.AddLock(subject, lockType, obj)`        |
| 解锁           | `LockManager.RemoveLock(subject, lockType, obj)`     |
| 查询           | `LockManager.IsLocked(subject, lockType)`            |
| using 自动释放 | `using var h = LockManager.AddLock(...);`            |
| 全局锁         | `LockManager.AddLock(LockManager.Global, type, obj)` |

---

## 依赖

### 第三方插件

| 包名                  | 简述                                                                                                                                                                          | 部署方法                                      | 在本插件的作用                                                                |
| --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- | ----------------------------------------------------------------------------- |
| **UniTask**           | Cysharp 出品的零 GC 高性能异步库                                                                                                                                              | UPM：添加 Git URL 到 `Packages/manifest.json` | 所有异步/await 操作基础：Asset 加载、UI 打开/关闭动画、启动管线、本地化切换等 |
| **YooAsset**          | 资源管理系统（加载/打包/热更）                                                                                                                                                | UPM：添加 Git URL 到 `Packages/manifest.json` | Asset 模块底层：AssetBundle 加载、实例化、对象池、场景加载                    |
| **NuGetForUnity**     | 在 Unity 中安装 NuGet 包的工具                                                                                                                                                | UPM：添加 Git URL 到 `Packages/manifest.json` | 用于安装 R3 及其传递依赖                                                      |
| **R3** (v1.2.9)       | Cysharp 的下一代响应式编程库                                                                                                                                                  | NuGet：通过 NuGetForUnity 安装                | Reactive 模块底层：消息总线、ReactiveProperty、信号、缓冲/请求-响应消息       |
| **R3 传递依赖** (4个) | `Microsoft.Bcl.AsyncInterfaces` / `Microsoft.Bcl.TimeProvider` / `System.ComponentModel.Annotations` / `System.Runtime.CompilerServices.Unsafe` / `System.Threading.Channels` | 随 R3 由 NuGetForUnity 自动安装               | R3 运行时依赖（IAsyncEnumerable、通道、注解等）                               |

### 安装流程

> ⚠️ R3 通过 NuGetForUnity 安装，请在添加 XFramework **之前**按顺序操作，避免编译报错。

**第一步：配置 UPM 依赖** — 在 `Packages/manifest.json` 的 `dependencies` 中添加：

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset",
    "com.github-glitchenzo.nugetforunity": "https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity"
  }
}
```

**第二步：配置 NuGet 依赖** — 在 `Assets/packages.config` 中写入：

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

**第三步：Restore** — 打开 Unity，菜单栏 `NuGet → Restore` 下载 R3。

**第四步：添加 XFramework** — 此时所有依赖就绪，可通过 Git URL 添加 XFramework。