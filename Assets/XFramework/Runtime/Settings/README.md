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
| **字段级通知** | 经 `SettingRef` 句柄订阅单个字段，赋值相同值不通知；实例被替换时重放一次 |
| **可替换后端** | `ISettingsStore` 允许替换为加密存储、PlayerPrefs 或远程云存档            |
| **替换点在后端** | 可替换的是 `ISettingsStore` / `ISettingsMigrator<T>` / `ISettingsValidator<T>`；**本模块不提供 `SetInstance`**——管理器的通知、脏标记与保存时机是**模块语义**而非实现细节，换它等于换模块（同 UI 的排序分层、Update 的调度模型） |
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

零配置起步可用默认路径重载：`SettingsManager.Initialize<GameSettings>()` 落到
`{persistentDataPath}/GameSettings.json`。**注意**默认路径取类型短名，不同命名空间下的同名类型
会算出同一个文件——框架会拦下这种情况并抛 `InvalidOperationException`（而非静默共用），
届时请改用显式路径重载。

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

句柄实现 `IReactiveProperty<T>`，订阅时立即回调当前值；经句柄**赋值**时，值确实改变才通知。

> **回调有两种来源，语义不同**：① 用户经句柄写入且值改变；② `Apply` / `Load` / `Reset` 换掉
> 实例（**无条件重放**，见下节）。故回调应理解为「当前值是这个」，而不是「用户刚改了它」。
> 非幂等的响应（如「画质变更 → 重建渲染管线」）会在实例替换时被多做一次；需要精确区分时，
> 用 `SettingsManager.Observe<T>` 订阅「实例被替换」这一事件本身。

```csharp
// UI 绑定：直接复用 UI 模块现成的扩展方法（属性 → UI）
Settings.MasterVolume.BindToSlider(masterSlider);

// 或手动订阅
Settings.MasterVolume.Subscribe(v => audioMixer.SetFloat("Master", Mathf.Lerp(-80f, 0f, v)));
```

**派生只读视图要自己持有并释放**：`Select` 在构造时就会订阅源，而句柄活到进程结束、**刻意不实现 `IDisposable`**，所以把返回值就地丢弃等于让这个派生值**永久订阅下去**——此后每次设置变更（含 `Load`/`Reset`/`Apply` 的无条件重放）它都会再跑一遍 format。别写成 `Settings.MasterVolume.Select(...).BindToText(label);`：

```csharp
// 持有派生值，在视图/面板关闭时释放它（BindToText 返回的是绑定方的句柄，释放它并不能释放派生值）
private IDisposable _volumeLabelProp;

_volumeLabelProp = Settings.MasterVolume.Select(v => $"{v:P0}");
_volumeLabelProp.BindToText(volumeLabel);
// 关闭时：_volumeLabelProp?.Dispose(); _volumeLabelProp = null;

// 在 ViewModel 里更省事：CreateReadOnlyProperty = 创建 + 归口，随 VM 一起释放
// HpRatio = CreateReadOnlyProperty(Settings.MasterVolume, v => $"{v:P0}");
```

### 6. 订阅「设置对象被整体替换」

`Apply` / `Load` / `Reset` 会换掉整个设置实例。句柄会自动跟随新实例**并把它推给订阅者**，
所以 UI 无需重新绑定、也不会停留在旧值——玩家点「恢复默认」时滑条会自己回到默认位置。

本订阅用于另一类需求：**你需要拿到新实例本身**（例如有代码缓存了实例引用，或要按新实例
重算一整块派生状态）：

```csharp
SettingsManager.Observe<GameSettings>(s =>
{
    // 参数是新的当前对象；订阅时也会立即回调一次
    RefreshSummary(s);
});
```

> 两种通知的先后顺序是固定的：**先重放字段句柄，再回调 `Observe`**。因此 `Observe` 的回调
> 运行时，字段视图已经指向新实例。

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
| `Initialize<T>(Func<T> defaultFactory?, SettingsOptions?)` | 用**默认路径**初始化（零配置） |
| `Destroy()` | 释放所有管理器；之后可重新 `Initialize` |
| `Settings<T>()` | 获取当前设置对象引用；未初始化时抛异常 |
| `TrySettings<T>(out T)` / `IsRegistered<T>()` | 探测入口，未初始化时不抛异常 |
| `Ref<T, TField>(Expression<Func<T,TField>>)` | **创建字段句柄，调用一次并缓存** |
| `Apply<T>(T settings)` | 替换整个设置对象并通知 |
| `Save<T>()` / `SaveAsync<T>(ct)` | 保存到持久层 |
| `Load<T>()` / `LoadAsync<T>(ct)` | 从持久层重新加载 |
| `Reset<T>()` | 重置为默认值（走 `defaultFactory`）并删除文件 |
| `IsDirty<T>()` / `MarkDirty<T>()` | 查询 / 手动标记「有未提交改动」 |
| `SaveAllDirty()` | 把所有脏了的类型一次写盘（显式时机用；不看 `SaveOnPause`） |
| `Observe<T>(Action<T>)` | 订阅**对象被整体替换**（订阅时立即回调） |
| `GetStore<T>()` / `SetStore<T>(store)` | 获取 / 替换存储后端 |
| `GetMigrator<T>()` / `SetMigrator<T>(migrator)` | 获取 / 注册格式迁移钩子 |
| `GetValidator<T>()` / `SetValidator<T>(validator)` | 获取 / 注册载荷校验钩子 |

### SettingRef\<T, TField\>（字段句柄）

| 成员 | 说明 |
| ---- | ---- |
| `Value { get; set; }` | 读穿透当前设置实例；写入回写 POCO、通知订阅者并置脏 |
| `Subscribe(Action<TField>)` | 订阅值变化，订阅时立即回调当前值 |
| `TryWriteValue(TField, out TField)` | 尝试写入并回传写入后的值；设置类型不可用（未 `Initialize` 或已 `Destroy`）时返回 `false` 且不抛异常 |

- 实现 `IReactiveProperty<TField>`，可直接用于 UI 绑定扩展方法
- 也实现 `IReactivePropertyWriter<TField>`，故可直接交给 `UIBinder.BindTwoWay` 做双向绑定——写值经 `TryWriteValue`，目标失效时不抛异常
- 订阅时的立即回调属于**注册期**同步代码，它抛出的异常原样上抛（订阅已自动清理，不会泄漏）；之后投递中的异常记 Error 日志后继续
- **每次读写都解析当前设置实例**，因此 `Load` / `Reset` / `Apply` 换实例后句柄自动跟随，无需重新绑定
- **实例替换时无条件重放**：换实例即向订阅者推一次当前值，即使数值恰好未变。
  这是刻意取舍——可比较的只有「上一实例的值」，而需要知道的是「订阅者上次收到什么」，
  二者在「直改 POCO 不通知」这个缺口上分叉，比较会漏掉陈旧的订阅者而让它永久停留在旧值
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

内置实现：

| 实现 | 说明 |
| ---- | ---- |
| `JsonFileStore(string filePath)` | JSON 文件，原子写 + 一代备份 |
| `EncryptedSettingsStore(inner, cryptoProvider)` | 加解密装饰器，可叠在任意后端之上 |

> **为什么 `JsonFileStore` 直接用 `System.IO`、不经 `FileManager`？** 三条理由，按硬度排：
>
> 1. **门面路径把崩溃防护从不变式降级为能力探测。** `JsonFileStore` 的原子写与一代备份是
>    无条件自持的；而 `FileManager.WriteAllBytesAtomic` 在 Provider 未实现 `IAtomicFileProvider`
>    （Console、WebGL 自定义实现）时**降级为普通写并告警**——崩溃防护与备份在那些平台上静默
>    失效。设置属于「写了就不能丢」的数据，不适合建在会降级的能力探测上。
> 2. **全局 `SetCryptoProvider` 会静默破坏设置文件。** 不传 domain 时它对**所有域**生效
>    （`File/README.md` 的示例正是这么写的）。设置一旦走门面，任何无关代码一次全局加密就把
>    设置文件变成密文；此后 `SetCryptoProvider(null)`（文档标注为「禁用加密」）会让设置读成
>    垃圾 → 解析失败 → 回退默认值，玩家表现为**设置静默丢失**。本模块的加密走显式、作用域
>    自持的 `EncryptedSettingsStore` 装饰器，对此免疫。
> 3. **公开 API 形状。** `JsonFileStore(string filePath)` 收绝对路径，允许把设置放在任意位置；
>    门面是 `FileDomain + 相对路径`，且带沙箱（拒盘符 / UNC / `..` 段）。
>
> 权衡下来：设置是几百字节的本地 JSON，上述三条比域管理更值钱。需要平台存档时请自行实现
> `ISettingsStore`；只是要加密则用上面的装饰器，不必自己写。

落盘布局：正式文件 `settings.json`、写入中的临时文件 `.tmp`、一代备份 `.bak`。
写入走 `FilePathUtility.ReplaceFileAtomically`，保证任意时刻正式文件与备份至少有一个完整存在。

**线程**：`JsonFileStore` 的成员可被任意线程调用——同一实例上的读写由内部锁串行化
（同步 `Save` 与在飞的 `SaveAsync` 会同时出现）。**残余限制**：两个 `JsonFileStore` 实例
指向同一路径不在覆盖范围内；覆盖它需要一张进程级的路径表，代价与收益不成比例。

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

### 需要平台存档（云同步 / 账号绑定 / Console）时

框架**不内置**平台存档后端——各平台 SDK 受 NDA 保护，且同步/异步契约差异很大。正路是自己实现
`IAsyncSettingsStore`，照这三条写：

1. **实现 `IAsyncSettingsStore` 而不是纯 `ISettingsStore`**。后者会让管理器走能力降级路径、把整段
   同步读挪到线程池——平台 SDK 的读通常不能那样跑。
2. **同步侧只读本地镜像**（就是 `JsonFileStore` 落在 `AppData` 的那份），真正的平台拉取留给异步侧。
   这正是上面「实现者注意」要求的形态：构造函数不会阻塞等平台 SDK。
3. **异步侧用 `FileDomain.SaveData`**——桌面/移动上它等同 `AppData`，Console 上由第三方
   `ConsoleFileProvider` 映射到 XGameSave / sceSaveData / nn::fs（见 File 模块 README 的「接入 Console 平台」）。

```csharp
// 示意代码：本框架不提供此类，需要时照此自行实现
public sealed class PlatformSaveSettingsStore : IAsyncSettingsStore
{
    private const string RemotePath = "settings.json";

    private readonly JsonFileStore _mirror; // AppData 里的本地镜像，服务同步侧

    public PlatformSaveSettingsStore(string mirrorFilePath) => _mirror = new JsonFileStore(mirrorFilePath);

    // —— 同步侧：只碰镜像，绝不阻塞等平台 ——
    public bool Exists() => _mirror.Exists();
    public T Load<T>() where T : class, new() => _mirror.Load<T>();
    public void Save<T>(T settings) where T : class, new() => _mirror.Save(settings);

    public void Delete()
    {
        _mirror.Delete();
        FileManager.Delete(FileDomain.SaveData, RemotePath);
    }

    // —— 异步侧：平台为准，镜像兜底 ——

    // 两个来源都算「有数据」：管理器见 false 会直接走 defaultFactory，连 LoadAsync 都不会调
    public async UniTask<bool> ExistsAsync(CancellationToken cancellationToken = default)
        => _mirror.Exists() || await FileManager.ExistsAsync(FileDomain.SaveData, RemotePath, cancellationToken);

    public async UniTask<T> LoadAsync<T>(CancellationToken cancellationToken = default) where T : class, new()
    {
        var bytes = await FileManager.ReadAllBytesAsync(FileDomain.SaveData, RemotePath, cancellationToken);

        // JsonUtility 是 Unity 原生 API，解析前切回主线程（与 SettingsManagerImpl 的取舍一致）
        await UniTask.SwitchToMainThread(cancellationToken);

        if (bytes == null || bytes.Length == 0)
            return _mirror.Load<T>(); // 平台无数据——新设备首次启动即此列

        var loaded = JsonUtility.FromJson<T>(Encoding.UTF8.GetString(bytes));
        _mirror.Save(loaded); // 回填镜像，供下次同步构造使用
        return loaded ?? new T();
    }

    public async UniTask SaveAsync<T>(T settings, CancellationToken cancellationToken = default) where T : class, new()
    {
        // 先序列化再 await：把 Unity API 留在调用线程（同上）
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(settings));

        // 平台优先。平台写失败会抛出、镜像保持旧值、脏标记未清，故下次自动保存会重试；
        // 反过来先写镜像的话，失败后平台仍是旧值，下次 LoadAsync 会把旧值读回来盖掉新改动
        await FileManager.WriteAllBytesAtomicAsync(FileDomain.SaveData, RemotePath, bytes, cancellationToken);
        _mirror.Save(settings);
    }
}
```

接线：

```csharp
SettingsManager.Initialize<GameSettings>(
    new PlatformSaveSettingsStore(Application.persistentDataPath + "/GameSettings.json"));
```

**四点必须知道的行为**（骨架不替你解决，取决于你怎么用它）：

- **同步 `Save` 只写镜像**——游戏若全程只用同步 API，数据永远到不了平台。要平台存档就必须走 `SaveAsync`。
- **`Exists()` 与 `ExistsAsync()` 会不一致**：前者只查镜像，后者连平台一起查。「新设备 + 云端已有数据」
  时构造函数拿到的是默认值，得由调用方在一次 `LoadAsync` 之后才切到平台数据。这是「同步侧只读镜像」
  的固有代价，也正是它换来「构造函数不阻塞」的原因。
- **平台侧在 Console 上不是原子写**：`ConsoleFileProvider` 未实现 `IAtomicFileProvider`，
  `WriteAllBytesAtomicAsync` 会**降级为普通写并告警**，崩溃防护失效。要保住它需自行实现该接口。
- **可与 `EncryptedSettingsStore` 叠加**：装饰器只管编解码，不关心内层是镜像还是平台，叠上去即两者都是密文。

> 上面这段代码已逐个核对签名、并整体编译通过；但**本框架不含此类、也没有配套测试**——它是写法
> 示意，不是可直接投产的组件。

### SettingsOptions

| 字段 | 默认 | 说明 |
| ---- | ---- | ---- |
| `CurrentVersion` | `0` | 持久化格式版本。`0` 表示不启用版本化（JSON 最干净）；`>= 1` 启用版本信封 |
| `AutoSave` | `false` | 是否启用去抖自动保存 |
| `AutoSaveDelay` | `0.5` | 自动保存的去抖窗口（秒） |
| `SaveOnQuit` | `false` | 应用退出时若有未提交改动则写盘（兜底） |
| `SaveOnPause` | `false` | 应用切到后台时若有未提交改动则写盘（兜底） |

> **选项在 `Initialize` 时读取一次，之后修改不再生效。** `SettingsOptions` 保持可变只是为了
> 对象初始化器语法好用，但管理器只取值并快照、**不保留引用**。初始化后再改这些字段不会有任何
> 效果——不是「部分生效」那种难查的状态。之所以要这么严：`SaveOnQuit` 中途翻转会让释放逻辑
> 按与订阅时不同的判据决定是否退订（订阅就此永远留在 `Application.wantsToQuit` 上），
> `CurrentVersion` 中途改会让上下半场写出的落盘格式不同。

### ISettingsMigrator\<T\>

```csharp
public class GameSettingsMigrator : ISettingsMigrator<GameSettings>
{
    public void Migrate(int fromVersion, int toVersion, GameSettings settings)
    {
        // 就地修改 settings
    }
}

SettingsManager.Initialize<GameSettings>(store, options: new SettingsOptions { CurrentVersion = 2 });
SettingsManager.SetMigrator<GameSettings>(new GameSettingsMigrator());
```

注册方式有两条：经门面 `SettingsManager.SetMigrator<T>(migrator)`，或在 `Initialize` 返回的
`ISettingsManager<T>` 上设置 `Migrator` 属性。二者等价，门面那条是在**初始化发生在别处**时
（引导阶段、另一个程序集）唯一够得着的入口——不必一路传递 `Initialize` 的返回值。

它是可写属性而非构造参数，避免「先 Initialize 还是先注册迁移器」的顺序问题；
`SetMigrator` 可在任意时刻调用，包括 `Load` 之前。

### ISettingsValidator\<T\>

把不可信的数据校正回合法范围。持久层里的值不能当可信：玩家手改过 JSON、磁盘位翻转、
迁移实现写错了字段、`JsonUtility` 给新增字段填了类型默认值（新加的 `int range = 3`
在旧存档上会变成 `0`）。

```csharp
public class GameSettingsValidator : ISettingsValidator<GameSettings>
{
    public void Validate(GameSettings settings)
    {
        settings.audio.masterVolume = Mathf.Clamp01(settings.audio.masterVolume);
        settings.qualityLevel = Mathf.Clamp(settings.qualityLevel, 0, QualitySettings.names.Length - 1);
    }
}

SettingsManager.SetValidator<GameSettings>(new GameSettingsValidator());
SettingsManager.Load<GameSettings>();   // 让校验器也作用于启动时读到的那份数据，见下
```

在 `Load` / `LoadAsync` / `Reset` / `Apply` 上被调用，位于**迁移之后**、订阅者被通知之前。

> **两处刻意的边界**
>
> - **构造期那次加载不在覆盖内**：钩子只能在拿到实例之后注册，而管理器构造时就已经读过一次盘。
>   注册后补一次 `Load` 即可（上例就是这么写的），这是推荐的启动顺序。
> - **经句柄的字段写入不在覆盖内**：那是进程内的显式赋值，可信且每帧可能发生。
>
> 两处都与 `ISettingsMigrator<T>` 的处境相同，理由也相同——它同样是「注册只能发生在构造之后」。

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
档位自适应：无待提交改动时用粗粒度，窗口内用细粒度。

**写失败会重试**：存储后端抛异常（配额、签名、平台 SDK 失败等）时，驱动器吞住异常、
按**下一个去抖窗口**重试，`IsDirty` 保持为真，且每个失败周期只告警一次——既不逐帧空转，
也不会因一次失败而永久停摆。驱动器必须吞住异常的原因很具体：`UpdateManager` 对抛异常的
节点是「LogError + 永久注销」，放任异常冒泡等于让一次可恢复的写失败变成本次会话再也不自动保存。
只告警不抛的后端（如 `JsonFileStore`）不在此列，框架无从察觉，详见「已知限制」。

> **`SaveOnQuit` 的两处局限**：它挂在 `Application.wantsToQuit`（比 `quitting` 更早触发，
> 写盘更可能在被拆掉之前跑完），但该事件**在编辑器播放模式下不触发**（返回值也被忽略），
> 且在 iOS / Android 上同样不保证触发——故只能在构建产物里确认，定位是「最后一道兜底」
> 而非可靠机制；真正可靠的是 `AutoSave` 与 `SaveOnPause`。它也不覆盖移动端切后台后被系统
> 杀死的场景——那里 `OnApplicationQuit` 根本不会触发，于是「退出时兜底」在这些设备上等于
> 不存在，请用 `SaveOnPause`（下节）。
>
> **保存失败绝不会取消退出**：处理函数恒返回 `true`。把异常翻译成 `false` 会让玩家关不掉游戏，
> 那是比丢一次设置严重得多的事故，故有用例专门钉住这一条。

### 切后台兜底

```csharp
SettingsManager.Initialize<GameSettings>(store, null, new SettingsOptions
{
    AutoSave = true,
    SaveOnQuit = true,
    SaveOnPause = true,   // 移动端必备：被系统杀掉时 OnApplicationQuit 不会触发
});
```

开启后框架自持一个隐藏的常驻宿主接收 `OnApplicationPause`——仅本选项开启时创建，
全部释放后销毁，关闭则零开销。它刻意**不**挂到 `DefaultGameLauncher` 上：后者自己的文档写明
「是可选件、不是框架的必需入口……场景里没有它也照常运转」，把落盘挂在一个可缺席的组件上
会让本选项的承诺落空。

> **只覆盖「进入后台」**：恢复前台不写盘，那里没有新的丢失风险。
>
> **不想用内置宿主？** 在你自己的 `OnApplicationPause` 里调 `SettingsManager.SaveAllDirty()` 即可——
> 它把所有脏了的类型一次写盘，不看任何开关（那是调用方的显式指令，语义同逐个 `Save`）。
> 它同样是「菜单关闭 / 场景切换 / 进入过场前先把一切存下来」的入口。

### 自定义存储后端

需要平台存档（主机 SDK）、云存档或特殊格式时，实现 `ISettingsStore` 即可：

```csharp
public class PlatformSaveStore : ISettingsStore { /* ... */ }

SettingsManager.Initialize<GameSettings>(new PlatformSaveStore(...));
```

**只是要加密则不必自己写**，用现成的装饰器：

```csharp
SettingsManager.Initialize<GameSettings>(new EncryptedSettingsStore(
    new JsonFileStore(Application.persistentDataPath + "/settings.json"),
    new XorCryptoProvider("my-secret-key")));
```

装出来的落盘形状是 `{"Data":"<Base64 密文>"}`——多一层外壳是因为 `ISettingsStore` 只有泛型的
`Load/Save`、没有字节通道，而 Base64 是任意字节序列能无损放进 JSON 字符串的编码。

> **装饰器可叠加，且不损失内层能力**：它只在「交给内层之前」与「从内层取出之后」各做一次变换，
> 原子写、一代备份、失败降级全部照旧。
>
> **换密钥等于换加密方案**：旧文件无法还原，读取时按「解密失败」回退默认值并告警。要迁移请在
> 换密钥之前把数据读出并重新保存。
>
> **用途是防篡改而非保密**：密钥随游戏分发，加密强度上限取决于注入的 `ICryptoProvider`
> （内置 `XorCryptoProvider` 只防普通用户手改）。真正敏感的数据不要放在设置里。

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
- **默认路径取类型短名**：不同命名空间下的同名类型会算出同一路径。框架用占用表拦下并抛异常
  （而非静默共用一份文件），该表**刻意不随 `Destroy` 清空**——占用关系对应的是磁盘文件，
  文件不会随 Destroy 消失；若清空则「A 初始化 → Destroy → B 初始化」会让 B 悄悄接管 A 的路径
- **实例替换会重放**：`Apply` / `Load` / `Reset` 之后每个已创建的句柄都会向订阅者推一次当前值，
  即使数值未变。代价是非幂等的响应会被多做一次（见「字段订阅与 UI 绑定」一节的说明）；
  换来的是 UI 不会停留在旧值。这一行为刻意不做去重，理由见上
- **句柄随 `Ref` 调用一次性创建并长期留在重放注册表里**，注册表不随 `Destroy` 清空
  （与默认路径占用表同理：关联关系比 `Destroy` 活得久）。因此 `Ref` 务必调用一次并缓存，
  不要放进循环或每帧路径——那既重复编译表达式，也会让注册表无谓增长
- **容器内的字段不跟踪**：`List<ReactiveProperty<T>>` 之类不在句柄体系内；
  `SettingRef` 的路径也必须是对设置对象自身成员的连续访问（不支持方法调用、索引器、闭包捕获）
- **句柄读写须在主线程**
- **`IsDirty` 的语义边界**：它表示「内存改动是否已**提交给存储后端**」，而**不**保证已成功落盘
  ——`ISettingsStore.Save<T>` 返回 `void`，框架无从得知后端内部是否真的写成功。于是写失败分两类：
  - **只告警不抛的后端**（`JsonFileStore` 即如此，为的是磁盘满时也不崩游戏）：框架按「已提交」
    处理，`IsDirty` 被清除，自动保存**不会重试**。换后端才能改变这一取舍
  - **抛异常的后端**（配额、签名、平台 SDK 失败等）：`IsDirty` 保持为真，自动保存会在下一个
    去抖窗口重试，每个失败周期只告警一次
- **删除失败无法回报**：`Reset` 走 `ISettingsStore.Delete()`，同样返回 `void`。删除失败只告警，
  因此「恢复默认」在极端情况下（文件被占用、权限不足）可能于下次启动被旧数据覆盖，
  而玩家当场看到的确实是默认值
- **`JsonUtility` 的固有限制**：不支持 `Dictionary`、多态、属性、顶层数组；
  `null` 反序列化为默认实例。这些是序列化器层面的约束，换后端才能绕开
- **`T : class, new()` 约束有具体理由，不只是图省事**：`JsonUtility.FromJson` 只跑无参构造函数，
  类型若没有它，所有字段会落到 C# 类型默认值、**字段初始化器一律不执行**（`public int Range = 3`
  会解析成 `0`）。约束把这条陷阱变成了编译期错误。新增字段能在旧存档上取到初始化器的值，
  靠的也正是这一点
- **迁移器与校验器可能在非主线程运行**：后端只实现同步接口、而调用方用的是 `LoadAsync` 时，
  整段同步读被挪到线程池，两个钩子随之在线程池线程上执行。它们若碰 Unity API 会炸——
  需要碰就只走同步 `Load`

## 避免 GC

- `SettingsChangedMessage` 使用 `readonly struct`，避免堆分配
- `SettingRef` 的值读写零分配；去重基准是 POCO 的实时值（不缓存，故无陈旧锚点）
- 实例被替换时的重放按句柄数线性遍历，`Apply` / `Load` / `Reset` 都是低频调用，不在热路径
- 句柄与内部事件流在 `Ref` 调用时一次性分配（故须调用一次并缓存），不在热路径
- 关闭自动保存时不注册任何帧回调

## 与项目其他模块的对比

| 特性       | LocalizationManager      | InputManager                | SettingsManager              |
| ---------- | ------------------------ | --------------------------- | ---------------------------- |
| 模式       | 静态外观 + 接口 + 实现   | 静态外观 + 接口 + 实现      | ✅ 一致                       |
| 初始化     | `Initialize(data)`       | `Initialize()`              | `Initialize<T>(path, …)`     |
| 响应式订阅 | N/A                      | `ObserveXxx`                | `SettingRef` / `Observe`     |
| 消息通知   | `LanguageChangedMessage` | `DeviceConnectedMessage` 等 | `SettingsChangedMessage`     |
