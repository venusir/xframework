# XFramework 文档

## 概述

XFramework 是一个以**静态服务**为核心、以 **Pipeline 编排 + 启动引导**为骨架的 Unity 组合式框架，完全基于纯 C# 实现，不依赖 MonoBehaviour 继承。引入插件后即可直接编写 GamePlay 逻辑，无需额外的框架配置。

### 架构分层

| 路径                   | 定位                            | 典型模块                                                               |
| ---------------------- | ------------------------------- | ---------------------------------------------------------------------- |
| **静态服务（无状态）** | 全局 Manager 入口，按需初始化   | File / Input / Settings / UI / Lock / Reactive / Localization / Update |
| **管线（通用编排）** | 阶段串行/并行编排、加权进度聚合、失败传播，工厂创建实例即用即弃；相位分组编排（IPhaseStage）声明同相位并行 / 相位升序串行 | Pipeline                                  |
| **引导（启动编排）** | 显式登记各模块的引导阶段，按相位装配运行启动管线，退出时反向清理 | Bootstrap                                 |

### 解决问题

| 痛点               | XFramework 方案                                             |
| ------------------ | ----------------------------------------------------------- |
| MonoBehaviour 耦合 | 纯 C# 静态服务，可脱离 GameObject 运行                      |
| 生命周期混乱       | 引导阶段统一初始化与反向清理；纯静态服务按需初始化          |
| 频繁 GC 分配       | 对象池 / 集合池 + 静态服务零分配设计，自动回收复用          |
| 更新调度粗放       | 档位分级调度，统一管理所有对象的 Update                     |
| 资源管理分散       | 统一资源服务：加载 / 对象池 / 引用计数 / 延迟卸载           |
| 跨模块耦合         | Provider 模式：接口 + 内部实现 + 扩展方法，可注入自定义实现 |

### 核心概念

| 概念              | 说明                                                              |
| ----------------- | ----------------------------------------------------------------- |
| **对象池**        | 频繁创建销毁的对象经 `PoolManager` / 集合池复用，减少 GC          |
| **档位更新**      | 对象返回 UpdateTier，自动调整更新频率                             |
| **Phase 分组调度**| 实现 `IPhaseStage` 声明相位号，同相位并行、不同相位串行（数值含义为使用方约定） |
| **管线编排**      | 通用管线抽象：阶段串行执行、加权进度聚合（事件驱动）、失败/取消传播 |
| **启动引导**      | 引导阶段经 `Bootstrap.Register` 显式登记，`RunAsync` 按相位装配运行 |
| **静态服务**      | 各模块通过静态 Manager 类提供全局入口                             |
| **Provider 模式** | 接口定义契约 + 内部默认实现 + 扩展方法，外部可注入自定义实现      |
| **自动初始化**    | 纯静态服务通过 `[RuntimeInitializeOnLoadMethod]` 或懒加载自动就绪 |

---

## 模块索引

每个模块的详细文档、API 和代码示例见对应 README：

| 模块             | 命名空间                   | 文档                                        | 职责                                                          |
| ---------------- | -------------------------- | ------------------------------------------- | ------------------------------------------------------------- |
| **Bootstrap**    | `XFramework.XBootstrap`    | [README](../Runtime/Bootstrap/README.md)    | 启动引导：显式登记引导阶段、按相位装配运行启动管线、退出时反向清理 |
| **Pipeline**     | `XFramework.XPipeline`     | [README](../Runtime/Pipeline/README.md)     | 通用编排：阶段编排（串行/并行/容器嵌套）、加权进度聚合、失败/取消传播；相位分组编排（IPhaseStage） |
| **Asset**        | `XFramework.XAsset`        | [README](../Runtime/Asset/README.md)        | 资源管理：异步加载、实例化、对象池、场景加载（基于 YooAsset） |
| **Update**       | `XFramework.XUpdate`       | [README](../Runtime/Update/README.md)       | 统一更新调度：三个派发时机（Update/LateUpdate/FixedUpdate）、双时间轴（含暂停）、档位时间切片、PlayerLoop 自驱动 |
| **Message**      | `XFramework.XMessage`      | [README](../Runtime/Message/README.md)       | 消息总线、事件流引擎                                |
| **Reactive**     | `XFramework.XReactive`     | [README](../Runtime/Reactive/README.md)     | 响应式属性（基于 Message 事件流）                  |
| **Localization** | `XFramework.XLocalization` | [README](../Runtime/Localization/README.md) | 本地化：多语言文本、语言切换、UI 自动绑定                     |
| **File**         | `XFramework.XFileManager`  | [README](../Runtime/File/README.md)         | 跨平台文件系统：路径域抽象、自动选平台 Provider、原子写与一代备份、按域加密 |
| **Data**         | `XFramework.XData`         | [README](../Runtime/Data/README.md)         | 数据块管理：快照收集/应用、逐块版本迁移链、脏标记             |
| **Serialize**    | `XFramework.XSerialize`    | [README](../Runtime/Serialize/README.md)    | 序列化注册表：按格式名取用（JSON 等），供 Data / Save 复用    |
| **Save**         | `XFramework.XSave`         | [README](../Runtime/Save/README.md)         | 存档：原子写与备份恢复、元数据侧车、版本门禁、玩家隔离、槽位复制移动 |
| **Pool**         | `XFramework.XPool`         | [README](../Runtime/Pool/README.md)         | 对象池与集合池（List/HashSet/Dictionary/StringBuilder）       |
| **Input**        | `XFramework.XInput`        | [README](../Runtime/Input/README.md)        | 输入抽象层：纯字符串 API、多设备检测、零 GC                   |
| **Settings**     | `XFramework.XSettings`     | [README](../Runtime/Settings/README.md)     | 强类型游戏设置：纯 POCO 持久化、字段句柄、版本迁移           |
| **UI**           | `XFramework.XUI`           | [README](../Runtime/UI/README.md)           | UI 面板管理 / MVVM 绑定 / 导航堆栈 / HUD / Tip                |
| **Lock**         | `XFramework.XLock`         | [README](../Runtime/Lock/README.md)         | 逻辑锁：多类型锁叠加、全局锁、using 自动释放                  |

---

## 目录结构

```
Assets/XFramework/
├── Runtime/                      # 运行时代码
│   ├── Bootstrap/                # 启动引导（引导阶段登记表 + 运行入口 + 可选 GameLauncher）
│   ├── Pipeline/                 # 通用编排（阶段编排/进度/失败取消）+ 相位分组编排（IPhaseStage）
│   ├── Asset/                    # 资源管理（基于 YooAsset）
│   ├── Update/                   # 统一更新调度
│   ├── Message/                  # 消息总线 + 事件流引擎
│   ├── Reactive/                 # 响应式属性（基于 Message 事件流）
│   ├── Localization/             # 本地化
│   ├── File/                     # 跨平台文件系统
│   ├── Input/                    # 输入抽象
│   ├── Settings/                 # 游戏设置
│   ├── UI/                       # UI 面板 / HUD / Tip
│   └── Lock/                     # 逻辑锁
├── Documentation/
│   └── XFramework.md             # 本文档
├── Tests/                        # 单元测试
├── Editor/                       # 编辑器扩展
├── package.json                  # UPM 包配置
├── README.md
└── CHANGELOG.md
```

---

## 启动流程

```
Bootstrap.RegisterDefaults()        # 登记框架内置的 Asset / Data / Save 三个阶段（Phase 0/3/4）
Bootstrap.Register(new MyStage())   # 业务引导阶段自行登记（Phase 建议晚于框架内置相位）

Bootstrap.RunAsync()                # 按相位装配管线并运行
      ├── 装配：每相位一个并行阶段 ParallelStage（组内并行 / 相位升序串行）
      └── 运行：逐相位串行执行；失败与取消抛出（启动失败是致命的）

Bootstrap.Shutdown()                # 按登记顺序的逆序清理，退出时调用

每帧驱动（与引导流程无关）
  └── UpdateManager 注入的 PlayerLoop 驱动系统 → 三个时机的调度器
```

`GameLauncher`（可选 MonoBehaviour，位于 `Runtime/Bootstrap/`）只是把上面三步接到 Unity 生命周期上：`Awake` 调 `RegisterDefaults`，`Start` 调 `RunAsync`，`OnDestroy` 调 `Shutdown`。不用它的话，在自己的启动流程里手动调这三个方法即可。

---

## 快速参考

### 资源操作

| 操作         | 代码                                                          |
| ------------ | ------------------------------------------------------------- |
| 加载资源     | `await AssetManager.LoadAsync<T>(location)`                   |
| 实例化       | `await AssetManager.InstantiateAsync(location, parent)`       |
| 回收实例     | `AssetManager.DestroyInstance(go)`                            |
| 预加载       | `await AssetManager.PreloadAllAsync(locations, p => ...)`     |
| 批量加载     | `await AssetManager.LoadAllAsync<T>(locations)`               |
| 加载场景     | `await AssetManager.LoadSceneAsync(location)`                 |
| 设置池大小   | `AssetManager.SetPoolMaxSize(location, 10)`                   |

### 消息操作

| 操作          | 代码                                                                     |
| ------------- | ------------------------------------------------------------------------ |
| 发布          | `MessageManager.Publish(msg)`                                            |
| 订阅          | `MessageManager.Subscribe<T>(handler)`                                   |
| 带 Key 发布   | `MessageManager.Publish(key, msg)`                                       |
| 异步订阅      | `MessageManager.SubscribeAsync<T>((msg, ct) => ...)`                     |
| 异步发布      | `await MessageManager.PublishAsync(msg, MessagePublishStrategy.Parallel)` |
| 缓冲订阅      | `MessageManager.SubscribeBuffered<T>(handler)`                           |
| 请求-响应     | `await MessageManager.RequestAsync<TReq, TRes>(req, ct)`                  |
| 淘汰缓冲通道  | `MessageManager.EvictBufferedChannel<T>()`                               |
| 运行统计      | `MessageManager.GetStats()`                                              |

### 本地化操作

| 操作     | 代码                                                  |
| -------- | ----------------------------------------------------- |
| 获取文本 | `LocalizationManager.GetText(key)`                    |
| 切换语言 | `await LocalizationManager.SetLanguageAsync("en-US")` |
| 当前语言 | `LocalizationManager.CurrentLanguage`                 |
| 检查 Key | `LocalizationManager.HasKey(key)`                     |

### 文件操作

| 操作           | 代码                                                                       |
| -------------- | -------------------------------------------------------------------------- |
| 读取文本       | `await FileManager.ReadAllTextAsync(FileDomain.AppData, "a.json")`         |
| 写入文本       | `await FileManager.WriteAllTextAsync(FileDomain.AppData, "a.json", data)`  |
| 读取字节       | `await FileManager.ReadAllBytesAsync(FileDomain.Streaming, "config.dat")`  |
| 写入字节       | `await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "save.dat", bytes)` |
| 文件是否存在   | `FileManager.Exists(FileDomain.SaveData, "a.json")`                        |
| 删除文件       | `FileManager.Delete(FileDomain.SaveData, "a.json")`                        |
| 注入自定义实现 | `FileManager.Initialize(new MyProvider())` // MyProvider 需实现 IFileProvider |

### 输入操作

| 操作                   | 代码                                                            |
| ---------------------- | --------------------------------------------------------------- |
| 按下事件               | `InputManager.WasPressedThisFrame("Jump")`                      |
| 抬起事件               | `InputManager.WasReleasedThisFrame("Jump")`                     |
| 持续按住               | `InputManager.IsPressed("Fire")`                                |
| 值输入(应用绑定处理器) | `InputManager.ReadFloat("MoveX")` / `ReadVector2("Move")`       |
| 原始值(绕过绑定处理器) | `InputManager.ReadFloatRaw("LookX")` / `ReadVector2Raw("Look")` |
| 长按时长               | `InputManager.GetButtonPressDuration("Jump")`                   |
| 当前设备类型           | `InputManager.LastActiveDeviceType`                             |
| 当前手柄类型           | `InputManager.ActiveGamepadType`                                |
| 多玩家读取             | `InputManager.IsPressed("Fire", playerId: 1)`                   |

### 设置操作

| 操作     | 代码                                                          |
| -------- | ------------------------------------------------------------- |
| 加载设置 | `await SettingsManager.LoadAsync<MySettings>()`               |
| 保存设置 | `await SettingsManager.SaveAsync<MySettings>()`               |
| 获取值   | `SettingsManager.Settings<MySettings>().MasterVolume`         |
| 重置默认 | `SettingsManager.Reset<MySettings>()`                         |
| 订阅字段 | `SettingsManager.Ref<MySettings, float>(s => s.MasterVolume)` |
| 订阅替换 | `SettingsManager.Observe<MySettings>(s => ...)`               |
| 应用设置 | `SettingsManager.Apply<MySettings>(settings)`                 |

> `Ref` 创建**字段句柄**，须调用一次并缓存（如 `static readonly` 字段）。句柄可 `Subscribe`、
> 可直接用 `BindToSlider` 等 UI 绑定扩展方法、写入即通知并置脏，且会自动跟随设置实例替换。
> 详见 [Settings README](../Runtime/Settings/README.md)。

### UI 操作

| 操作         | 代码                                                                 |
| ------------ | -------------------------------------------------------------------- |
| 打开面板     | `await UIManager.Panel.OpenAsync<MainMenuPanel>("PF_MainMenu")`            |
| 关闭面板     | `await UIManager.Panel.CloseAsync<MainMenuPanel>()`                        |
| 压入导航堆栈 | `await UIManager.Stack.PushAsync<SettingsPanel>("PF_Settings")`            |
| 弹出导航堆栈 | `await UIManager.Stack.PopAsync()`                                         |
| 显示模态遮罩 | `UIManager.Mask.Show(maskLayer: 500, alpha: 0.5f)`                    |
| 隐藏模态遮罩 | `UIManager.Mask.Hide()`                                               |
| 显示临时提示 | `UIManager.ShowTip(new TipConfig { Text = "+100", WorldPos = pos })` |

### 更新操作

| 操作           | 代码                                                          |
| -------------- | ------------------------------------------------------------- |
| 注册到更新调度 | `UpdateManager.Register(this, order: 0)`                      |
| 注销更新       | `UpdateManager.Unregister(this)`                              |
| 实现档位降级   | `UpdateTier IUpdateable.OnUpdate(float deltaTime, float time)` |
| 延迟更新时机   | 实现 `ILateUpdateable.OnLateUpdate(deltaTime, time)`          |
| 固定步长时机   | 实现 `IFixedUpdateable.OnFixedUpdate(deltaTime, fixedTime)`   |
| 声明墙钟时间轴 | 注册时传 `timeMode: UpdateTimeMode.Unscaled`                  |

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

### 安装流程

在 `Packages/manifest.json` 的 `dependencies` 中添加：

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset"
  }
}
```

配置完成后，再通过 Git URL 添加 XFramework。消息总线（XMessage）与响应式属性（XReactive）均为框架自研实现，零额外依赖。