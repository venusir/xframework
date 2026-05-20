# XFramework 文档

## 概述

XFramework 是一个基于**节点树**的 Unity 组合式架构框架，完全基于纯 C# 实现，不依赖 MonoBehaviour 继承。通过将游戏系统组织为层级节点树，实现高内聚、低耦合的模块化开发。

### 解决问题

| 痛点               | XFramework 方案                                    |
| ------------------ | -------------------------------------------------- |
| MonoBehaviour 耦合 | 纯 C# 节点树，可脱离 GameObject 运行               |
| 生命周期混乱       | 树形有序生命周期：Awake → Start → Update → Destroy |
| 频繁 GC 分配       | 节点级对象池，自动回收复用                         |
| 更新调度粗放       | LOD 分级调度，按需降频                             |
| 资源管理分散       | 统一资源服务：加载 / 对象池 / 引用计数 / 延迟卸载  |

### 核心概念

| 概念           | 说明                                                  |
| -------------- | ----------------------------------------------------- |
| **节点树**     | 层级化树形结构，每个节点有 Depth 属性                 |
| **组件模式**   | EntityNode 按类型缓存子节点（类 GetComponent）        |
| **对象池**     | 节点销毁后自动回池，减少 GC                           |
| **LOD 更新**   | 节点返回 UpdateLOD，自动调整更新频率                  |
| **Phase 调度** | 启动管线按 Phase 分组，同 Phase 并行、不同 Phase 串行 |
| **静态外观**   | 各服务模块通过静态 Manager 类提供全局入口             |

---

## 模块索引

每个模块的详细文档、API 和代码示例见对应 README：

| 模块             | 命名空间                   | 文档                                        | 职责                                                          |
| ---------------- | -------------------------- | ------------------------------------------- | ------------------------------------------------------------- |
| **Core**         | `XFramework.XCore`         | [README](../Runtime/Core/README.md)         | 节点树核心：生命周期、EntityNode、DictionaryNode、对象池      |
| **Loader**       | `XFramework.XLoader`       | [README](../Runtime/Loader/README.md)       | 启动管线：Phase 分组调度、一键启动、进度广播                  |
| **Asset**        | `XFramework.XAsset`        | [README](../Runtime/Asset/README.md)        | 资源管理：异步加载、实例化、对象池、场景加载（基于 YooAsset） |
| **Localization** | `XFramework.XLocalization` | [README](../Runtime/Localization/README.md) | 本地化：多语言文本、语言切换、UI 自动绑定                     |
| **Reactive**     | `XFramework.XReactive`     | [README](../Runtime/Reactive/README.md)     | 响应式：消息总线、响应式属性、信号（基于 R3）                 |
| **Lock**         | `XFramework.XLock`         | [README](../Runtime/Lock/README.md)         | 逻辑锁：多类型锁叠加、全局锁、using 自动释放                  |

---

## 目录结构

```
Assets/XFramework/
├── Runtime/                      # 运行时代码
│   ├── Core/                     # 节点树核心
│   ├── Loader/                   # 启动加载管线
│   ├── Asset/                    # 资源管理
│   ├── Localization/             # 本地化
│   ├── Reactive/                 # 响应式（消息/R3）
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
  ├── UpdateBinder.Bind(root)      # 绑定更新调度
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

| 包                    | 用途                                                  |
| --------------------- | ----------------------------------------------------- |
| `com.cysharp.unitask` | 异步操作（UniTask）                                   |
| YooAsset              | 资源管理底层（需手动安装，见各模块 README）           |
| R3                    | 响应式编程底层（需手动安装，见 Reactive 模块 README） |