# SettingsManager — 强类型游戏设置管理器

## 概述

`XFramework.XSettings` 提供以**强类型 POCO + 文件持久化 + 字段级响应式**为核心的游戏设置方案。

设计取向是**存储模型与响应式视图分离**：

- 设置类保持纯 POCO（`public float MasterVolume = 1f;`），落盘 JSON 不含任何框架类型，随时可搬走
- 需要响应式的字段另行为它声明一个**字段句柄**（`SettingRef`），句柄实现 `IReactiveProperty<T>`，
  因此 UI 模块现成的 `BindToSlider` / `BindToText` 等绑定扩展方法可直接使用
- 两级订阅分工：**字段值变化**归句柄，**设置对象被整体替换**归 `Observe`

## 设计理念

| 原则           | 说明                                                                     |
| -------------- | ------------------------------------------------------------------------ |
| **强类型**     | 编译期类型安全，IDE 智能提示，告别 `GetFloat("key")` 的魔法字符串        |
| **纯 POCO**    | 设置类不依赖框架类型；JSON 可读可调试，字段名即业务名下划线之外无额外包装 |
| **字段级通知** | 经 `SettingRef` 句柄订阅单个字段，相同值不通知；句柄自动跟随实例替换      |
| **可替换后端** | `ISettingsStore` 允许替换为加密存储、PlayerPrefs 或远程云存档            |
| **多类型共存** | 内部按 `Type` 索引，支持同时管理 `GameSettings`、`EditorSettings` 等      |
| **显式为主**   | 默认不自动保存、不写盘，行为可预期；需要时按需开启去抖自动保存            |

## 快速开始

### 1. 定义设置类（纯 POCO）

```csharp
using System;

[Serializable]
public class AudioSettings
{
    public float masterVolume = 1f;
    public float musicVolume = 0.8f;
}

[Serializable]
public class GameSettings
{
    public AudioSettings audio = new();
    public bool fullscreen = true;
}
```

### 2. 声明字段句柄

句柄**必须调用一次并缓存**（典型做法是 `static readonly` 字段）：`Ref` 要解析并编译表达式，
不可放入每帧路径。

```csharp
using XFramework.XSettings;

public static class Settings
{
    public static readonly SettingRef<GameSettings, float> MasterVolume =
        SettingsManager.Ref<GameSettings, float>(s => s.audio.masterVolume);

    public static readonly SettingRef<GameSettings, bool> Fullscreen =
        SettingsManager.Ref<GameSettings, bool>(s => s.fullscreen);
}
```

选择器**必须以字段结尾**——属性不会被 `JsonUtility` 序列化，用它做设置项会「改了但没存」，
`Ref` 会直接拒绝并说明原因。

### 3. 初始化

```csharp
using XFramework.XSettings;
using UnityEngine;

SettingsManager.Initialize<GameSettings>(Application.persistentDataPath + "/settings.json");
```

### 4. 读写

```csharp
// 读：直接读 POCO 字段，零开销
float v = SettingsManager.Settings<GameSettings>().audio.masterVolume;

// 写：经句柄 —— 回写 POCO + 通知订阅者 + 置脏
Settings.MasterVolume.Value = 0.5f;

// 持久化
SettingsManager.Save<GameSettings>();
```

> **写入契约（重要）**：直接改 POCO 字段（`settings.audio.masterVolume = 0.5f`）**不会**通知订阅者、
> 也**不会**置脏。要通知必须经句柄写入。直改字段后若需落盘，请调用
> `SettingsManager.MarkDirty<GameSettings>()` 再 `Save`。

### 5. 字段订阅与 UI 绑定

句柄实现 `IReactiveProperty<T>`，订阅时立即回调当前值，相同值不通知。

```csharp
// UI 绑定：直接复用 UI 模块现成的扩展方法（属性 → UI）
Settings.MasterVolume.BindToSlider(masterSlider);

// 或手动订阅
Settings.MasterVolume.Subscribe(v => audioMixer.SetFloat("Master", Mathf.Lerp(-80f, 0f, v)));

// 派生只读视图
Settings.MasterVolume.Select(v => $"{v:P0}").BindToText(volumeLabel);
```

### 6. 订阅「设置对象被整体替换」

`Apply` / `Load` / `Reset` 会换掉整个设置实例。句柄会自动跟随新实例，无需重新绑定；
但若你有缓存了实例引用的代码，需要在这里改读。

```csharp
SettingsManager.Observe<GameSettings>(s =>
{
    // 参数是新的当前对象；订阅时也会立即回调一次
    RefreshSummary(s);
});
```

### 7. 全局消息订阅

```csharp
using XFramework.XMessage;

MessageManager.Subscribe<SettingsChangedMessage>(msg =>
{
    if (msg.SettingsType == typeof(GameSettings))
        Debug.Log("GameSettings 已变更");
});
```

## API 参考

### SettingsManager（静态外观）

| 方法 | 说明 |
| ---- | ---- |
| `Initialize<T>(string filePath, Func<T> defaultFactory?, SettingsOptions?)` | 用 JSON 文件路径初始化 |
| `Initialize<T>(ISettingsStore store, Func<T> defaultFactory?, SettingsOptions?)` | 用自定义存储后端初始化 |
| `Destroy()` | 释放所有管理器；之后可重新 `Initialize` |
| `Settings<T>()` | 获取当前设置对象引用 |
| `Ref<T, TField>(Expression<Func<T,TField>>)` | **创建字段句柄，调用一次并缓存** |
| `Apply<T>(T settings)` | 替换整个设置对象并通知 |
| `Save<T>()` / `SaveAsync<T>(ct)` | 保存到持久层 |
| `Load<T>()` / `LoadAsync<T>(ct)` | 从持久层重新加载 |
| `Reset<T>()` | 重置为默认值（走 `defaultFactory`）并删除文件 |
| `IsDirty<T>()` / `MarkDirty<T>()` | 查询 / 手动标记「有未提交改动」 |
| `Observe<T>(Action<T>)` | 订阅**对象被整体替换**（订阅时立即回调） |
| `GetStore<T>()` / `SetStore<T>(store)` | 获取 / 替换存储后端 |

### SettingRef\<T, TField\>（字段句柄）

| 成员 | 说明 |
| ---- | ---- |
| `Value { get; set; }` | 读穿透当前设置实例；写入回写 POCO、通知订阅者并置脏 |
| `Subscribe(Action<TField>)` | 订阅值变化，订阅时立即回调当前值 |

- 实现 `IReactiveProperty<TField>`，可直接用于 UI 绑定扩展方法
- **每次读写都解析当前设置实例**，因此 `Load` / `Reset` / `Apply` 换实例后句柄自动跟随，无需重新绑定
- **刻意不实现 `IDisposable`**：它通常声明为静态字段并活到进程结束，若带释放语义，
  「面板关闭时 Dispose 掉 ViewModel」这类正常操作会连带废掉它。取消订阅请释放 `Subscribe` 的返回值
- 读写须在主线程

### ISettingsStore（存储后端接口）

| 方法 | 说明 |
| ---- | ---- |
| `T Load<T>()` | 从持久层加载；无数据应返回 `new T()` |
| `void Save<T>(T settings)` | 保存到持久层 |
| `bool Exists()` | 是否有已保存数据 |
| `void Delete()` | 删除持久化数据 |

内置实现：`JsonFileStore(string filePath)`

落盘布局：正式文件 `settings.json`、写入中的临时文件 `.tmp`、一代备份 `.bak`。
写入走 `FilePathUtility.ReplaceFileAtomically`，保证任意时刻正式文件与备份至少有一个完整存在。

**失败语义**：读取或解析失败一律 LogWarning 并回退默认值，不向调用方抛异常——设置文件损坏
不应让游戏启动失败。主文件存在但损坏时会尝试 `.bak` 回退；**主文件不存在时不会**，
否则 `Reset`（删文件）之后的下一次 `Load` 会把刚重置掉的旧数据从备份里复活。

### IAsyncSettingsStore（可选能力接口）

继承 `ISettingsStore`，额外提供 `ExistsAsync` / `LoadAsync<T>` / `SaveAsync<T>`。

未实现该接口的存储后端**不必改动**：管理器会做能力探测，把同步调用整体挪到线程池，
对调用方而言 `SaveAsync` / `LoadAsync` 同样是非阻塞的。

> **实现者注意**：管理器的**构造函数必然走同步路径**（构造无法 await），`Exists()` 为 true 时
> 会调用同步 `Load<T>()`。因此同步成员不能是「昂贵且阻塞」的实现——底层若是网络或平台 SDK，
> 同步 `Load` 必须在无数据时快速返回，真正的拉取留给异步 API，否则初始化会卡住主线程。

### SettingsOptions

| 字段 | 默认 | 说明 |
| ---- | ---- | ---- |
| `CurrentVersion` | `0` | 持久化格式版本。`0` 表示不启用版本化（JSON 最干净）；`>= 1` 启用版本信封 |
| `AutoSave` | `false` | 是否启用去抖自动保存 |
| `AutoSaveDelay` | `0.5` | 自动保存的去抖窗口（秒） |
| `SaveOnQuit` | `false` | 应用退出时若有未提交改动则写盘（兜底） |

### ISettingsMigrator\<T\>

```csharp
public class GameSettingsMigrator : ISettingsMigrator<GameSettings>
{
    public void Migrate(int fromVersion, int toVersion, GameSettings settings)
    {
        // 就地修改 settings
    }
}

SettingsManager.Initialize<GameSettings>(store,
    options: new SettingsOptions { CurrentVersion = 2 }).Migrator = new GameSettingsMigrator();
```

注册方式：`Initialize` 返回的 `ISettingsManager<T>` 上设置 `Migrator` 属性。
它是可写属性而非构造参数，避免「先 Initialize 还是先注册迁移器」的顺序问题。

### SettingsChangedMessage

`readonly struct` 消息，通过 `MessageManager` 发布。包含 `SettingsType`（发生变更的设置类型）。

## 高级用法

### 自定义默认值

```csharp
SettingsManager.Initialize<GameSettings>(
    Application.persistentDataPath + "/settings.json",
    () =>
    {
        // 根据设备性能动态决定默认画质
        int defaultQuality = SystemInfo.graphicsMemorySize > 4096 ? 3 : 1;
        return new GameSettings { qualityLevel = defaultQuality };
    });
```

默认值工厂在三条路径上一致生效：首次初始化、`Load<T>()` 遇到无持久化数据、以及 `Reset<T>()`。
因此玩家点「恢复默认」得到的是同一个设备自适应结果，而不是设置类的字段初始值。

### 格式版本与迁移

`CurrentVersion` 默认 `0` 表示不启用版本化，落盘内容就是设置对象本身：

```json
{ "audio": { "masterVolume": 0.5 } }
```

设为 `>= 1` 后落盘内容变为版本信封：

```json
{ "Version": 1, "Data": { "audio": { "masterVolume": 0.5 } } }
```

加载时按版本分流：**高于**本版本则整份拒绝并回退默认值（数据可能由更新版游戏写入，
按旧结构解析只会得到静默错位的设置）；**低于**本版本则调用 `Migrator`，未注册迁移器时
同样回退默认值（宁可回默认值，也不要错位数据）。迁移在订阅者被通知之前执行，
故迁移过程中写值不会产生多余通知。

> **陷阱**：启用版本化会改变落盘格式，此前按无版本格式写下的文件将无法被识别
> （解析出的信封载荷为空），会回退默认值。切换前后需自行处理存量数据。

### 自动保存

```csharp
SettingsManager.Initialize<GameSettings>(store, null, new SettingsOptions
{
    AutoSave = true,
    AutoSaveDelay = 0.5f,
    SaveOnQuit = true,
});
```

**去抖而非节流**：等待窗口从**最后一次改动**起算。玩家拖动滑条期间一次都不写盘，
松手静默 0.5 秒后写一次。若做成节流，一次三秒的拖动会写六次。

关闭时（默认）不注册任何帧回调，零开销。开启后经 `UpdateManager` 注册一个帧驱动器，
LOD 自适应：无待提交改动时用粗粒度，窗口内用细粒度。

> **`SaveOnQuit` 的两处局限**：Unity 的 `Application.quitting` **在编辑器中不触发**，
> 该行为只能在构建产物中确认；它也不覆盖移动端切后台后被系统杀死的场景
> （那需要 `OnApplicationPause`，静态服务收不到该回调，须业务自行在暂停时 `Save`）。

### 自定义存储后端

```csharp
// 加密存储示例（示意，需自行实现加密逻辑）
public class EncryptedFileStore : ISettingsStore { /* ... */ }

SettingsManager.Initialize<GameSettings>(new EncryptedFileStore(path, encryptionKey));
```

### 运行时切换存储后端

```csharp
SettingsManager.SetStore<GameSettings>(new PlayerPrefsStore());
SettingsManager.Load<GameSettings>(); // 读取新后端的已有数据
SettingsManager.Save<GameSettings>();
```

> **替换只换后端、不迁移数据**：内存中的设置仍是旧后端加载的内容，下一次 `Save` 会把它们
> 写入新后端。框架刻意不做隐式重新加载——那会静默丢掉内存中尚未保存的修改。故替换时会打
> LogWarning 提醒；如需读取新后端已有数据，请随后调用 `Load`。

## 已知限制

- **写入契约**：直接改 POCO 字段不通知、不置脏。框架无法感知对普通字段的赋值，
  这是保留的已知限制——要通知/置脏必须经句柄写入，直改后手动 `MarkDirty`
- **`Ref` 必须调用一次并缓存**。它编译表达式，且每次调用都会新建句柄与事件流。
  IL2CPP 下 `Expression.Compile()` 走解释器（可运行但慢），故不可放入每帧路径
- **容器内的字段不跟踪**：`List<ReactiveProperty<T>>` 之类不在句柄体系内；
  `SettingRef` 的路径也必须是对设置对象自身成员的连续访问（不支持方法调用、索引器、闭包捕获）
- **句柄读写须在主线程**
- **`IsDirty` 的语义边界**：它表示「内存改动是否已**提交给存储后端**」，而**不**保证已成功落盘
  ——`ISettingsStore.Save<T>` 返回 `void`，`JsonFileStore` 对 IO 失败只告警不抛（避免磁盘满时崩游戏），
  框架无从得知。因此磁盘写失败时 `IsDirty` 也会被清除，自动保存不会重试
- **`JsonUtility` 的固有限制**：不支持 `Dictionary`、多态、属性、顶层数组；
  `null` 反序列化为默认实例。这些是序列化器层面的约束，换后端才能绕开

## 文件结构

```
Runtime/Settings/
├── ISettingsStore.cs              # 存储后端接口
├── IAsyncSettingsStore.cs         # 可选：异步存储能力
├── JsonFileStore.cs               # 默认 JSON 文件存储（原子写 + 一代备份）
├── ISettingsManager.cs            # 管理器接口
├── SettingsManagerImpl.cs         # 默认实现（internal sealed）
├── SettingsManager.cs             # 全局静态外观
├── SettingRef.cs                  # 字段句柄
├── SettingsOptions.cs             # 选项
├── SettingsEnvelope.cs            # 版本信封（internal）
├── ISettingsMigrator.cs           # 迁移钩子
├── SettingsAutoSaveTicker.cs      # 自动保存帧驱动器（internal）
├── Messages/
│   └── SettingsChangedMessage.cs  # 变更消息
└── README.md
```

## 避免 GC

- `SettingsChangedMessage` 使用 `readonly struct`，避免堆分配
- `SettingRef` 的值读写零分配；去重基准是 POCO 的实时值（不缓存，故无陈旧锚点）
- 句柄与内部事件流在 `Ref` 调用时一次性分配（故须调用一次并缓存），不在热路径
- 关闭自动保存时不注册任何帧回调

## 与项目其他模块的对比

| 特性       | LocalizationManager      | InputManager                | SettingsManager              |
| ---------- | ------------------------ | --------------------------- | ---------------------------- |
| 模式       | 静态外观 + 接口 + 实现   | 静态外观 + 接口 + 实现      | ✅ 一致                       |
| 初始化     | `Initialize(data)`       | `Initialize()`              | `Initialize<T>(path, …)`     |
| 响应式订阅 | N/A                      | `ObserveXxx`                | `SettingRef` / `Observe`     |
| 消息通知   | `LanguageChangedMessage` | `DeviceConnectedMessage` 等 | `SettingsChangedMessage`     |
