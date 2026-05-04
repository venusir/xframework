# XFramework

XFramework 是一个基于树形结构的 Unity 节点系统框架。它提供了层级化的节点结构、对象池管理、组件式访问（EntityNode）、键值对访问（DictionaryNode）、LOD 分级的更新调度以及异步加载管线。

## 核心架构

```
GameLauncher (MonoBehaviour)
    │
    ├── RootNode ─── 节点树入口
    │       │
    │       ├── EntityNode ─── 组件模式（按类型缓存子节点）
    │       │       ├── LeafNode (行为/数据)
    │       │       └── CompositeNode (公开 Add/Remove)
    │       │
    │       └── DictionaryNode<TKey> ─── 键值对模式
    │
    ├── NodeUpdater ─── 自动注册/注销 IUpdateable 节点
    │       └── Updater ─── LOD 时间切片调度
    │
    └── NodeFactory ─── 统一创建/回收，自动对象池
```

## 设计哲学

- **组合优于继承** — EntityNode 按类型缓存子节点，类似 Unity 的 GetComponent/AddComponent
- **对象池内置** — 所有节点通过 `NodeFactory` 创建，`Destroy()` 后自动回池
- **更新按需降级** — `IUpdateable.OnUpdate` 返回 `UpdateLOD` 等级，自动调整更新频率
- **异步加载管线** — 节点实现 `ILoadableProvider` 装载加载任务，`StartupAsync` 统一调度

## 快速开始

### 1. 挂载 GameLauncher

在场景中创建一个 GameObject，挂载 `GameLauncher` 脚本。

### 2. 定义节点

```csharp
using XFramework;

public class PlayerNode : LeafNode
{
    int _hp;

    protected override void OnInit(object arg)
    {
        if (arg is int hp) _hp = hp;
    }

    protected override void OnAwake()
    {
        UnityEngine.Debug.Log($"PlayerNode Awake, HP: {_hp}");
    }
}
```

### 3. 在 GameLauncher 中构建节点树

```csharp
public class MyGameLauncher : GameLauncher
{
    async void Start()
    {
        // 添加子节点
        var player = _root.AddNode<PlayerNode>(100);
        await _root.StartupAsync();
    }
}
```

### 4. 实现更新

```csharp
public class MovementNode : LeafNode, IUpdateable
{
    public UpdateLOD OnUpdate(float deltaTime, float time)
    {
        // 每帧更新，返回 EveryFrame 保持每帧更新
        return UpdateLOD.EveryFrame;
    }
}
```

## 主要功能

| 功能           | 说明                                                           |
| -------------- | -------------------------------------------------------------- |
| **节点树**     | 层级化节点结构，支持深度排序、递归遍历                         |
| **对象池**     | `NodeFactory` + `NodePool<T>`，自动回池复用                    |
| **组件模式**   | `EntityNode.GetNode<T>()` / `AddNode<T>()` / `RemoveNode<T>()` |
| **键值对模式** | `DictionaryNode<TKey>` 按 Key 缓存子节点                       |
| **更新调度**   | `IUpdateable` + `UpdateLOD` 时间切片，自动 LOD 迁移            |
| **异步加载**   | `ILoadableProvider` + `LoadableBase` + `LoadingManager`        |
| **生命周期**   | Init → Awake → Start → Destroy，与 Unity 语义一致              |

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

