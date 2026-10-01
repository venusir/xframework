# XFramework / Audio 模块

## 概述

**命名空间**：`XFramework.XAudio`

音频播放的静态服务：通道分类、三档音量、播放句柄、暂停与循环。默认实现自持一个隐藏宿主并池化
`AudioSource`，**使用方不接触任何 `AudioSource`**。

三条贯穿全模块的约定，先记住它们能省掉大半疑问：

1. **资源一律以 location 字符串标识**，经 `XAsset` 异步加载。**没有**接受 `AudioClip` 的重载——
   因此本模块的公开面不含任何 Unity 内置音频类型。
2. **通道是开放的字符串域**，不是枚举。`AudioChannels` 给的是推荐常量，项目可自建任意通道名。
3. **零配置即可用**：`Initialize()` 之后就能播，播放源惰性创建，从不播放的项目一个 GameObject 都不建。

## 典型场景

### A. 一次性音效

```csharp
AudioManager.Play("audio/sfx_hit", new AudioPlayOptions(AudioChannels.Se, volumeScale: 0.8f));
```

fire-and-forget，不关心它什么时候播完。异常已由框架收口，**不要**再包 `.Forget()`。

### B. 背景音乐

```csharp
_bgm = await AudioManager.PlayAsync("audio/bgm_main",
    new AudioPlayOptions(AudioChannels.Bgm, loop: true));

// 换场景或退出时：
_bgm.Stop();
```

**循环播放永不被自动回收**，必须显式 `Stop`。

### C. 定点 3D 音效

```csharp
AudioManager.Play("audio/sfx_explosion", new AudioPlayOptions(
    AudioChannels.Se, spatialBlend: 1f, position: hitPoint));
```

`spatialBlend > 0` 时位置才写进播放源。

### D. 设置面板

```csharp
AudioManager.MasterVolume = 0.8f;
AudioManager.SetChannelVolume(AudioChannels.Bgm, 0.6f);
AudioManager.SetChannelMuted(AudioChannels.Voice, true);
```

变更**立刻**作用到正在播放的声音上。静音只是听不见，播放不会中断。

### E. 暂停菜单

```csharp
AudioManager.Pause();    // 全部音频暂停
AudioManager.Resume();
```

暂停期间新起的声音也会立刻进入暂停（见「设计取舍」）。

## 快速使用

```csharp
// 1. 初始化（零配置）
AudioManager.Initialize();

// 2. 播一个音效
AudioManager.Play("audio/sfx_ui_click");

// 3. 起一段 BGM 并保留句柄
var bgm = await AudioManager.PlayAsync("audio/bgm_main",
    new AudioPlayOptions(AudioChannels.Bgm, volumeScale: 0.6f, loop: true));

// 4. 音量
AudioManager.SetChannelVolume(AudioChannels.Se, 0.5f);

// 5. 停止
bgm.Stop();
```

初始化选项（全部可选）：

```csharp
AudioManager.Initialize(new AudioInitOptions
{
    MaxVoices = 32,      // 同时播放上限（= 播放源池容量）
    MasterVolume = 1f,
    MasterMuted = false,
    PrewarmVoices = false,   // true 则在初始化时一次性建满播放源
});
```

## 完整 API 参考

### 播放

| 成员 | 说明 |
| ---- | ---- |
| `AudioHandle Play(string location, AudioPlayOptions options = default)` | fire-and-forget。**句柄在预占播放源时即返回**，加载失败会让它失效（用 `IsPlaying` 查询） |
| `UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options = default, CancellationToken ct = default)` | 等待加载完成。**句柄返回时必定已起播**；加载失败返回 `default(AudioHandle)` |

两条路径的失败契约不同，这是最容易踩的一处：

| | `Play` | `PlayAsync` |
| --- | --- | --- |
| 拿不到播放源 | 返回 `default(AudioHandle)` + 限流告警 | 同左 |
| 加载失败 | 返回**预占句柄**，随后它失效 | 返回 `default(AudioHandle)` |
| 参数非法（location 为 null/空白） | 抛 `ArgumentException` | 抛 `ArgumentException` |
| 取消 | ——（内部令牌随 `Destroy` 取消，静默） | 抛 `OperationCanceledException`，不泄漏槽位与资源 |

判断一次播放是否有效，用 `AudioManager.IsPlaying(handle)`，不要判 `handle.IsDefault`——那只能识别
「池满/加载失败返回的 default」，识别不了「预占成功但加载失败而失效的句柄」。

### 控制

| 成员 | 说明 |
| ---- | ---- |
| `bool Stop(AudioHandle handle)` | 真的停掉了一个在播的声音才返回 `true`。**探测型**：对默认/过期/未知句柄返回 `false`，不抛 |
| `int StopAll(string channel = null)` | `channel` 为 `null` 即全部通道。返回实际停止数 |
| `void Pause()` / `void Resume()` / `bool IsPaused` | 见「典型场景 E」与「设计取舍」 |

### 音量

实际音量 = **主音量 × 通道音量 × 单次播放的 VolumeScale**，任一档静音即为 `0`，最后钳到 `[0, 1]`。

| 成员 | 说明 |
| ---- | ---- |
| `float MasterVolume { get; set; }` | 钳到 `[0, 1]` |
| `bool MasterMuted { get; set; }` | 主静音 |
| `void SetChannelVolume(string channel, float volume)` | 钳到 `[0, 1]`；通道不存在时惰性建状态 |
| `float GetChannelVolume(string channel)` | **查询不建状态**；未知通道返回 `1` |
| `void SetChannelMuted(string channel, bool muted)` | 通道不存在时惰性建状态 |
| `bool IsChannelMuted(string channel)` | **查询不建状态**；未知通道返回 `false`。读的是通道自己的静音，不含主静音 |
| `void RegisterChannel(string channel, AudioChannelConfig config)` | 声明通道初始值。配置**调用时快照**，之后改实例无效 |

### 查询

| 成员 | 说明 |
| ---- | ---- |
| `bool IsPlaying(AudioHandle handle)` | **探测型**。加载中的不算「在播」，**暂停中的算** |
| `int ActiveVoiceCount { get; }` | 活跃播放数，**含加载中与已暂停的**（它们都实打实占着播放源） |
| `int GetActiveVoiceCount(string channel)` | 按通道计数 |

### 值类型

| 类型 | 说明 |
| ---- | ---- |
| `AudioHandle` | 播放句柄。`readonly struct`、零 GC、可自由复制。`IsDefault` / `Stop()` / `Dispose()` |
| `AudioPlayOptions` | 播放参数：`Channel` / `VolumeScale` / `Loop` / `Pitch` / `SpatialBlend` / `Position` |
| `AudioChannels` | 推荐通道常量：`Default`("default") / `Bgm`("bgm") / `Se`("se") / `Voice`("voice") / `Ui`("ui") |
| `AudioChannelConfig` | `RegisterChannel` 的配置：`Volume`（默认 `1`，线性值、由 `SetChannelVolume` 钳到 `[0,1]`）/ `Muted`（默认 `false`）。**调用时读取一次并快照**，之后改这个实例不影响已注册的通道；**通道也可以不声明**——未声明的通道首次使用时按同样的默认值惰性建状态 |
| `AudioInitOptions` | 初始化选项：`MaxVoices` / `MasterVolume` / `MasterMuted` / `PrewarmVoices` |

### 参数归一化

为了让「省略实参」安全，实现会把未设置的值归一化：

| 字段 | 归一化规则 |
| ---- | ---- |
| `Channel` | `null` / 空 → `AudioChannels.Default` |
| `VolumeScale` | `<= 0` → `1` |
| `Pitch` | `<= 0` → `1` |
| `SpatialBlend` | 钳到 `[0, 1]` |

**这条归一化是必需的**：省略 `options` 实参传入的是 `default(AudioPlayOptions)`，它的字段全是零——
不归一化的话，忘写 options 会得到一个完全静音的、音高被拉平的声音，比抛异常更难查。

**保留名**：`"master"` 不是通道。把它传给任何接受通道的成员（含 `Play` / `StopAll`）都会抛
`ArgumentException`，异常消息指出该用 `MasterVolume`。不拦的话，调用方会静默建出一个没人收听的通道，
而以为自己控住了总音量。

## 与音频中间件的边界

本模块的公开面**不含任何 Unity 内置音频类型**（没有 `AudioClip`、没有 `AudioMixerGroup`、没有
`AudioSource.priority`），因此除了默认实现，还可以：

### 整体替换 `IAudioManager`

使用 Wwise / FMOD 等音频中间件的项目，实现 `IAudioManager` 后注入即可——中间件实现可以把
`location` 直接解释成自己的事件名：

```csharp
AudioManager.SetInstance(new MyWwiseAudioManager());
```

这与 `InputManager` 换 Rewired 是同一形态（整套底层引擎被替换，而非按平台/能力切分的分层 Provider）。
`AudioHandle` 的两个字段是公开的且对门面不透明，实现方可以按自己的需要编码（有效句柄的
`Generation` 必须非 `0`，`default(AudioHandle)` 必须被视为无效）。

**注意**：`SetInstance` **不会** Dispose 先前持有的实例。若之前已 `Initialize`，请先 `Destroy`。

### 用项目自己的 AudioMixer

框架只写自己池里播放源的 `volume`，**不碰** `AudioMixer`。要让声音进入项目自己的总线/效果链，
把混音交给 Mixer：让框架的通道音量留在 `1.0`，项目自己用 `AudioMixer.SetFloat` 控制总线音量。
两者是相乘关系，不会互相覆盖。

### 关于「跟随移动物体的 3D 发射器」

本模块的播放源是**定点**的：`spatialBlend > 0` 时只写一次位置，不跟随。跟随需要一个每帧写
Transform 的宿主对象，那是项目侧的 MonoBehaviour——框架不去追 Transform。

## 与相邻模块的关系

| 模块 | 关系 |
| ---- | ---- |
| **Asset** | 唯一的资源来源。所有音频资源必须注册进 YooAsset 的 location；启动画面可用 `AssetManager.PreloadAllAsync` 预热音效，之后 `Play` 走缓存 |
| **Settings** | **无硬依赖**（Settings 是可选模块）。要把音量做进设置里，项目侧接三行——见「常见配方」 |
| **Localization** | **无硬依赖**。切语言时重播语音由项目自己订阅 `LanguageChangedMessage` 决定（见「设计取舍」） |
| **Update** | 内部用 `UpdateManager` 驱动播完回收，档位 `Tier1`、时间轴 `Unscaled`。**使用方无需做任何事** |
| **Pool** | **不使用**。`PoolManager` 只服务纯 C# 对象，不涉及 GameObject 与资源引用；播放源池是模块自持的第三套 |

### 常见配方：音量接进 Settings

```csharp
private static readonly SettingRef<GameSettings, float> BgmVolume =
    SettingsManager.Ref<GameSettings, float>(s => s.Audio.BgmVolume);   // 调用一次并缓存

// 初始化时接一次即可
BgmVolume.Subscribe(v => AudioManager.SetChannelVolume(AudioChannels.Bgm, v));
```

`SettingRef.Subscribe` **订阅时立即同步回调一次当前值**，正好补上「启动时把已存档音量推给音频」这一步。

## 内部机制

### 播放源池与隐藏宿主

首次播放时惰性创建宿主 `[AudioManager] Audio Host`（`DontDestroyOnLoad`，BGM 因此能跨场景连续），
每个播放占用一个子 GameObject + `AudioSource`。播放源的 `AudioSource` 与 `Transform` 在创建时缓存，
运行期没有任何 `GetComponent`。

`MaxVoices` 是**唯一的并发策略**：取不到空闲槽位时返回 `default(AudioHandle)` 并记一条限流告警
（同一段耗尽期只告警一次，释放任一槽位后重新允许告警）。**框架不做优先级抢占**——抢占必须先回答
「抢谁」，那是替使用方决定混音策略。

### 句柄的代际

槽位被回收后再被复用时会递增代际号，因此：

- 旧句柄**不会**误停新播放；
- 旧句柄**不会**把新播放误报成在播。

这让「随机有个音效被掐断」这类难以回溯的问题从根上不成立。

### 一次播放的完整生命周期

```
预占槽位 → 加载（location → AudioClip） → 校验代际 → 写参数 → Play()
   → 播放中（槽位持有资源租约：既是释放凭据也是保活凭据）
   → 停止 / 播完 / 销毁 → 归还槽位 + 归还租约
```

资源租约的释放点是**恰好一次**：`Stop` 时若仍在加载，只作废槽位，由加载续体负责归还已经拿到的租约。

### 播完回收

内部 ticker 挂在 `UpdateManager` 上，`Tier1`（约 33ms）、时间轴 `Unscaled`。**只在确实有活跃播放时注册**，
最后一个播放结束时注销——闲置时零帧开销。

回收判据是**框架自己的状态机**，不是只看 `AudioSource.isPlaying`：因为 `Pause()` 之后 `isPlaying` 就是
`false`，只看它会**把暂停中的播放当场回收掉**。

## 生命周期与清理

```csharp
AudioManager.Destroy();   // 停止全部播放、归还全部资源、销毁宿主
```

- `Destroy` 会被 `Application.quitting` 自动调用，**无需手动挂**。
- 销毁后需重新 `Initialize` 或 `SetInstance`。
- **销毁后旧句柄仍然安全**：`Stop` 与 `IsPlaying` 返回 `false` 而不抛——句柄的生命周期可以长于管理器。

未初始化时：管理器范围的成员抛 `InvalidOperationException`（消息带 `[Audio]` 前缀与修复提示），
**探测型成员**（`Stop` / `IsPlaying`）返回 `false` 不抛。

| 成员 | 未初始化 / 已销毁时 |
| ---- | ---- |
| `Stop` / `IsPlaying` | 安全返回 `false`（探测型豁免） |
| 其余全部（`Play` / `PlayAsync` / `StopAll` / 音量 / 查询计数） | 抛 `InvalidOperationException` |

## 线程契约

**全部成员限主线程调用。** 本模块不提供线程安全保证，也不做线程断言（Release 下无开销，但也就没有检测）。

回调（无）与事件（无）：本模块没有可订阅的公开事件，因此不存在「回调在哪个线程」的问题。

## 设计原则

- **不预设游戏架构**：通道是开放字符串域、音量模型是线性的三档相乘、播放源归框架所有——只提供
  正交的播放能力，不替使用方决定混音策略、不决定「什么声音该在什么时候响」。
- **单一资源来源**：只接受 location，资源生命周期只有一套语义。
- **失败形态确定**：加载失败不抛（返回可安全探测的句柄）、参数非法抛、取消抛且不泄漏。
- **探测型成员宽松**：句柄可以长于管理器，对它的查询与停止永远安全。

## 依赖

| 依赖 | 用途 | 必需 |
| ---- | ---- | ---- |
| **UniTask** | 异步播放与加载 | 是 |
| **YooAsset**（经 `XAsset`） | 资源加载 | 是（除非整体替换 `IAudioManager`） |
| `com.unity.modules.audio` | Unity 内置音频 | 是（Unity 自带模块，无需安装） |

**不依赖** Settings / Localization / Config / UI 中任何一个。

## 已知限制

- **所有音频资源必须注册进 YooAsset 的 location。** `[SerializeField] AudioClip hitSfx;` 那种 Inspector
  直接引用的用法用不了——不过这是本框架的一贯立场（UI 的面板同样必须走 location）。建议在加载画面用
  `AssetManager.PreloadAllAsync` 预热常用音效。
- **没有同步播放路径。** 只有异步加载；`Play` 是「立即返回 + 后台加载」，不是同步加载。
- **3D 播放源是定点的**，不跟随移动物体（见「与音频中间件的边界」）。
- **切后台不会自动暂停。** 本模块不接 `OnApplicationPause`——「是谁暂停的、谁来恢复」必须由项目掌握
  （玩家本来就在暂停菜单里切后台时，回到前台不能替他 `Resume`）。需要的话由项目在自己的生命周期回调里
  调 `Pause()` / `Resume()`。
- **`ActiveVoiceCount` 含加载中与已暂停的**，它不是「正在出声的数量」。
- **不能播一个音量为 0 的声音。** `VolumeScale <= 0` 会被归一化成 `1`（这是让省略实参安全的前提）。
  要静音请用 `SetChannelMuted` 或把通道音量调到 `0`。
- **最终音量钳在 `[0, 1]`**，不支持 `VolumeScale > 1` 的放大。
- **没有淡入淡出 / crossfade。**
- **没有优先级抢占**，只有全局的 `MaxVoices` 上限；也没有按通道的独立上限。
- **`AudioManager.Pause()` 不碰 `AudioListener.pause`**——那是项目的全局开关。项目用它暂停时，框架的
  播放会被连带暂停（这是期望行为）。

## 设计取舍

### 为什么公开面一个 Unity 音频类型都没有

`Play(AudioClip)` 是 Unity 项目里最常见的调用形状，本模块刻意不提供它。

- **可替换**：有了它，`IAudioManager` 就无法被音频中间件实现——Wwise 的事件、FMOD 的 `EventInstance`
  都没有 `AudioClip` 这个对象。把它去掉，整个接口才是后端中立的。
- **一条路径 = 一套句柄生命周期**：只走 location 之后，资源句柄的持有/释放只剩一种语义。原设计里
  「调用方在播放期间释放自己持有的资源句柄 → 声音静默中断」这个坑随之消失——那本来只能靠文档挡。
- 代价是回不到 Inspector 直引的用法（见「已知限制」第一条）。

### 为什么通道是字符串而不是枚举

枚举一旦进入公开面，「新增一个通道」就变成框架变更。通道名是使用方的领域知识（`"bgm"` / `"voice"` /
`"ambient_forest"`……），框架没有资格替它枚举完。这与 `UILayers` 只给推荐层级常量、`XInput` 只接受
字符串动作名是同一条取舍。

通道名以**序数**比较（区分大小写）：若忽略大小写，两个互不相关的作者取到同名通道时会静默串改音量
——与 Lock 踩过的「键空间与相等语义」是同一类坑。

### 为什么不自动暂停

自动暂停需要回答「是谁暂停的」：玩家本来就在暂停菜单里切后台时，回到前台框架不能替他 `Resume`。
这需要一个保存「暂停前状态」的状态机，而收益只是省掉项目侧两行代码。此外 iOS 与 Android 在切后台时
对音频的处置本来就不同，那是需要真机实测才能固化进公开面的行为。

### 为什么 `Pause` 是管理器级闸门

暂停期间新起的声音**也**立即进入暂停。另一种选择是「只冻住此刻在播的那些，新起的声音照放」，但那会让
`IsPaused` 这个状态自相矛盾：管理器说自己暂停着，新起的声音却在响，而 `Resume` 又归它管。

### 为什么暂停中的播放仍算「在播」

`IsPlaying` 对暂停中的句柄返回 `true`。暂停不是停止——`Stop` 与音量变更对它照常有效，
`ActiveVoiceCount` 也仍然算它。把暂停当成停止会让「暂停菜单打开时统计有多少声音活着」这个问题问不出答案。

### 为什么内部有一个「资源租约」类型

播放槽位持有的是 `AudioClipLease` 而不是 `AssetHandle<AudioClip>`。原因有二：一是把本模块对 `XAsset`
类型的接触收敛到两个文件；二是 `AssetHandle` 内部包着 YooAsset 的句柄，**没有活的 YooAsset 环境就构造不出
有效实例**，于是测试替身既无法表达「加载成功」，也无法观测「释放了几次」——而「资源租约释放点恰好一次」
是本模块最要紧的不变量（少一次即泄漏，多一次即重复释放）。
