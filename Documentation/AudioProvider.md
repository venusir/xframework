# AudioProvider —— `IAudioProvider` 播放引擎缝（核心、FMOD-free 扩展与 Phase 2 真实集成已实现；真实 bank/event 冒烟已实测）

> **收件人**：框架维护者（实现或审计 Audio 播放引擎缝时）。**不是**使用文档。
> **状态**：核心 provider 缝于 2026-10-25 实现并完成复审修复；2026-10-26 新增 **provider 中立控制面**
> （参数/RTPC、bank、bus、snapshot、逐次 3D）与 **FMOD-free 扩展包**（`Assets/XFramework.Fmod/`）；
> 2026-11-07 **Phase 2 真实 FMOD 集成已实现并用本地 UPM 包完成探针**；2026-11-12 真实 bank/event
> 冒烟已实测通过。Wwise 适配器仍未实现。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[本仓核对]` | 直接读本仓现有 `Runtime/Audio/` 与测试得出，可当场复现 |
| `[代理逐行核对]` | 2026-10-25 的 Plan 代理按现有代码逐行核对出的缺口与归属 |
| `[已实现]` | 核心/扩展已落地并跑过测试；以实际代码与公开 API 基线为准 |
| `[设计稿]` | Wwise 扩展的候选形状；FMOD Phase 2 已实现并有探针 |
| `[待裁]` | 需要用户拍板的取舍；本文件给推荐但不冒充已定 |

**核对基准**：2026-10-26 的 `Assets/XFramework/Runtime/Audio/`、`Assets/XFramework.Fmod/` 与对应测试。
核心 Audio 定向与扩展 Fmod 定向全绿；Phase 2 真实 FMOD 集成已实现；Wwise 仍未实现。

---

## 一、结论：B 的边界

用户 2026-10-25 裁定：**核心新增公开 `IAudioProvider` 播放引擎缝（方案 B）**，而不是让每个
中间件适配器整体实现 `IAudioManager`。

方案 B 的边界：

- **核心保留全部音频语义**：字符串通道、三档音量相乘与钳制、管理器级暂停闸门、活跃计数、
  参数归一化、保留名、核心句柄 `AudioHandle` 的分配/代际/校验、诊断回读 `CopyChannels`。
- **provider 只做播放原语**：起播、停止、释放、暂停/恢复、写音量、报告播放状态。
- **FMOD/Wwise 适配器只实现 `IAudioProvider`**，不再重写通道/音量/暂停/计数。
- **`IAudioManager` / `AudioManager.SetInstance` 保留**：整体替换管理器的逃生口，不是推荐路径。
- **核心已实现**：公开 API 为源兼容新增；内置 `AudioSourceProvider` 承接原播放池；现有 Audio 行为不变。
- **2026-10-26 扩展**：核心新增 provider 中立控制面（参数/RTPC、bank、bus、snapshot、逐次 3D），
  由核心 `AudioManager` 对外；provider 侧能力接口为 core internal，经 `InternalsVisibleTo` 给官方
  FMOD 扩展。旧的 `FmodAudio.Install` 公开门面方案已废弃。
- **FMOD 扩展**：`Assets/XFramework.Fmod/` 的 FMOD-free 层已实现（provider + 配置资产 + fake backend
  测试 + 守卫）；Phase 2 真实集成程序集已实现（2026-11-07 用本地 FMOD 2.04 UPM 包探针验证）。
- **Wwise 扩展**：仍 `暂不做`，重开判据是真实 Wwise 项目 + SDK 可编译验证。

为什么不选 A（整体替换 `IAudioManager`）的要点，见 `Documentation/FMOD.md` §三：
A 要求每个适配器重写约 200–300 行框架语义；`CopyChannels` 的类型探测与 `AudioChannelInfo` 的
internal 构造还会让诊断面在中间件路径上空白。B 的主要代价是**核心公开 API 变更 + 一次行为不变的拆分**。

---

## 二、公开 API `[已实现]`

2026-10-25 已按下列签名落地；公开 API 基线 +33（全部源兼容新增，0 破坏性）。

```csharp
namespace XFramework.XAudio
{
    public interface IAudioProvider : IDisposable
    {
        UniTask<AudioProviderHandle> PlayAsync(
            string location,
            AudioPlaybackRequest request,
            CancellationToken cancellationToken = default);

        bool Stop(AudioProviderHandle handle);
        void Release(AudioProviderHandle handle);
        bool SetPaused(AudioProviderHandle handle, bool paused);
        bool SetVolume(AudioProviderHandle handle, float volume);
        AudioProviderPlaybackState Poll(AudioProviderHandle handle);
    }

    public enum AudioProviderPlaybackState
    {
        Invalid = 0,   // 句柄失效 / 播放源被外部销毁 / 未知句柄
        Starting,      // 已起播，但尚未观察到真正在播
        Playing,
        Paused,        // 引擎侧暂停（内置 provider 的 AudioListener.pause）
        Stopped,       // 自然结束
    }

    public readonly struct AudioProviderHandle : IEquatable<AudioProviderHandle>
    {
        public readonly int Id;
        public readonly int Generation;
        public AudioProviderHandle(int id, int generation);   // provider 构造
        public bool IsDefault => Generation == 0;
    }

    public readonly struct AudioPlaybackRequest
    {
        public readonly bool Loop;
        public readonly float Pitch;          // 核心已归一化
        public readonly float SpatialBlend;   // 核心已钳到 [0,1]
        public readonly Vector3 Position;
        public readonly float Volume;         // 核心算好的最终音量（主 × 通道 × 单次，已钳制）
        public readonly bool Paused;          // 发起时的管理器暂停快照
        public readonly CancellationToken ValidityToken; // 核心槽位有效性令牌；start() 前检查
        public readonly AudioParameter[] InitialParameters; // 核心校验并复制的快照（provider 只读）
        internal AudioPlaybackRequest(...);   // 核心构造（已定为 internal）
    }
}
```

门面新增：

```csharp
public static class AudioManager
{
    public static void SetProvider(IAudioProvider provider, AudioInitOptions options = null);

    public static bool TryGetProviderHandle(
        AudioHandle handle, out AudioProviderHandle providerHandle);
}
```

### 接口契约（已实现）

- **线程**：全部成员限主线程。
- **`request.Volume` 是最终值**：provider **不得**再乘 `VolumeScale` / 通道音量 / 主音量；
  三档相乘与 `[0,1]` 钳制由核心完成。
- **`PlayAsync` 失败返回 `default(AudioProviderHandle)`**，由 provider 自行记日志（不抛）；
  调用方 `cancellationToken` 取消抛 `OperationCanceledException`，且不得泄漏已加载资源/实例。
- **两个取消来源分开**：`cancellationToken` 是调用方/生命周期取消（OCE）；
  `request.ValidityToken` 是核心槽位有效性令牌（`Stop`/`StopAll` 作废时取消）。provider 必须在
  `start()` 前检查后者；已取消则释放并返回 default，**不要抛 OCE**。**取消时必须立即归还实现自己
  持有的预占槽位（不能等加载完成）**，否则 `Stop` 后立即重播会撞上 provider 的容量窗口（P1-1）。
- **`Stop` 只停播**；**`Release` 负责释放**（停播、释放实例/租约）且幂等。核心在用户 `Stop` 时
  调 `Stop + Release`；ticker 回收与「加载完成但核心槽位已失效」只调 `Release`。
- **`SetPaused` / `SetVolume` 返回 false 表示声音已消失**（播放源被外部销毁、句柄失效），
  核心据此回收槽位；返回 true 表示已应用。
- **`Poll` 是纯查询**：不推进超时、不修改状态；由核心 ticker 每拍调用——`Starting` / `Playing` 在
  管理器未暂停时 poll，`Paused` 槽位始终 poll（回收外部销毁的播放源）；`Loading` 还没有 provider
  句柄，不 poll。
  状态含义见 §四。
- **`Dispose` 幂等**：取消在途 `PlayAsync`、释放全部实例与租约；核心在 `AudioManagerImpl.Dispose`
  里调用并隔离异常。

### `SetProvider` 的语义（已实现）

- 先 `AudioManager.Destroy()` 旧实例（含其 provider），再构造 `new AudioManagerImpl(provider, options)`
  并置为门面实例。**与 `SetInstance` 的「不 Dispose 旧实例」不同**——provider 由核心持有，
  不 Dispose 会泄漏；XML 与 README 必须写明这条差异。
- `provider` 为 null 抛 `ArgumentNullException`。
- `options` 只给核心语义层（`MaxVoices` / `MasterVolume` / `MasterMuted`）；`PrewarmVoices`
  对自定义 provider 无意义，文档要写明。
- `Initialize(options)` 仍构造内置 `AudioSourceProvider`；`SetProvider` 之后 `Initialize`
  按现有规则告警并忽略。

### `TryGetProviderHandle`（已实现公开）

- 目的：让中间件扩展的**逐次播放能力**（FMOD 事件参数/RTPC、Wwise RTPC/switch）有用户可达入口；
  同时让测试不再依赖内部反射。
- 行为：未初始化/已销毁/`default`/过期/Loading 句柄 → 返回 false 不抛；Active 句柄 → 输出 provider handle。
- 对 `SetInstance` 注入的自定义 `IAudioManager` → 返回 false（它不是 `AudioManagerImpl`）。
- 不加它的后果：扩展只能做「核心语义 + 中间件播放」，做不了逐次事件控制；测试只能用内部构造函数。

### 中立控制面（2026-10-26 已实现）

公开面：

```csharp
public readonly struct AudioParameter
{
    public readonly string Name;
    public readonly float Value;   // Label 构造时为 0
    public readonly string Label;  // 数值构造时为 null
    public bool IsLabel { get; }
    public AudioParameter(string name, float value);
    public AudioParameter(string name, string label);
}

public static class AudioManager
{
    public static bool SetParameter(AudioHandle handle, string name, float value);
    public static bool SetParameter(AudioHandle handle, string name, string label);
    public static bool SetGlobalParameter(string name, float value);
    public static bool SetGlobalParameter(string name, string label);
    public static bool LoadBank(string bank, bool loadSamples = false);
    public static bool UnloadBank(string bank);
    public static bool IsBankLoaded(string bank);
    public static bool SetBusVolume(string bus, float volume);
    public static bool SetBusMuted(string bus, bool muted);
    public static bool SetBusPaused(string bus, bool paused);
    public static bool StartSnapshot(string snapshot);
    public static bool StopSnapshot(string snapshot);
    public static bool SetPosition(AudioHandle handle, Vector3 position);
}
```

- `AudioPlayOptions.InitialParameters` 承载起播前参数；核心在 `Play`/`PlayAsync` 预占槽位前校验并复制
  成快照（`null`/空数组归一为 `null`；非法项抛 `ArgumentException` 且不预占/不加载），provider 只读。
- 所有控制方法为探测型：未初始化/已销毁/无效句柄/provider 未实现/`SetInstance` 自定义管理器 → `false`；
  名称空白 → `ArgumentException`（参数校验先于能力探测）；provider 异常隔离并记 `[Audio]` 日志。
- `SetBusVolume` 原样透传、不做核心钳制；框架三档音量与 bus/snapshot 是两层。
- 内置 `AudioSourceProvider` 只实现 `SetPosition`；其余控制方法返回 `false`。
- provider 侧能力接口为 **core internal**：`IAudioParameterProvider` / `IAudioBankProvider` /
  `IAudioBusProvider` / `IAudioSnapshotProvider` / `IAudioSpatialProvider`。它们经
  `InternalsVisibleTo("Venusy609.Xframework.Fmod")` 给官方扩展；不是第三方可自由实现的公开契约。
  第三方 provider 仍可用 `TryGetProviderHandle` 自行处理逐次控制。
- FMOD-free 扩展 `FmodAudioProvider` 已实现五个接口；真实 `FmodEventBackend` 已在 Phase 2 实现。

---

## 三、为什么核心保留语义（与 A 的边界）

`[本仓核对]` 现有 `AudioManagerImpl` 的职责可以逐条数出来：

| 现有职责 | 拆分后归属 | 依据（实现后的类型/方法） |
| --- | --- | --- |
| 通道状态表、序数比较、`"master"` 保留名、查询不建状态 | 核心 | `AudioManagerImpl._channels` / `NormalizeChannel` / `GetOrCreateChannel` |
| 三档音量相乘、钳制、静音 | 核心 | `AudioManagerImpl.ComputeVoiceVolume` / `RefreshVolumes` |
| 管理器级暂停闸门 | 核心 | `AudioManagerImpl.Pause` / `Resume` / `_paused` |
| 活跃计数、按通道计数、`CopyChannels` | 核心 | `AudioManagerImpl.ActiveVoiceCount` / `GetActiveVoiceCount` / `CopyChannels` |
| 核心句柄 `AudioHandle` 的槽位/代际/校验 | 核心 | `AudioManagerImpl.TryReserveVoice` / `IsCurrent` / `NextGeneration` |
| `MaxVoices` 与耗尽告警、`Play` 的同步预占 | **核心** | `AudioManagerImpl.TryReserveVoice` / `WarnExhaustedOnce` |
| 加载、`AudioSource` 配置、播完回收、`Starting` 超时 | 内置 provider（加载 / 配置 / 实际播放）+ 核心 ticker（`Starting` 超时与回收判断） | `AudioSourceProvider.PlayAsync` + `AudioManagerImpl.SweepVoices` |
| `AudioListener.pause`、死源检测 | 内置 provider | `AudioSourceProvider.Poll` |
| `PrewarmVoices`、宿主/槽位 GameObject | 内置 provider | `AudioSourceProvider` 构造 + `AudioSourcePool` |

A 方案会让每个中间件适配器重写第一至六行（核心语义）；B 只把最后三行下沉。这就是选 B 的理由。

---

## 四、核心状态机与 ticker `[代理逐行核对]`

### 4.1 核心槽位记录

核心新增 `AudioManagedVoice`（internal）：

- `Generation`
- `State`：`Free` / `Loading` / `Starting` / `Playing` / `Paused`
- `AudioProviderHandle ProviderHandle`（Loading 时是 default）
- `Channel` / `Location` / `VolumeScale`
- `StartingTicks`（仅 `Starting` 用，60 拍超时）
- `ValidityCts`：核心槽位有效性令牌源；`Stop`/`StopAll` 取消它，provider 在 `start()` 前检查。
  正常结束**不取消**，下一次预占复用它（避免每次播放分配一条 CTS）；只有加载中被作废的那条会换新。

**核心状态机不再与 provider 槽位状态共用同一个枚举**。provider 侧的 `AudioVoice` 只保留
provider 需要的字段（`Source` / `SlotObject` / `Generation` / `ClipLease` / `Location` / `Loop` /
是否已 `start()`）；`AudioVoiceState` 不再同时服务两边。

### 4.2 ticker 每拍的处置

对每个非 `Free` 槽位：

| 核心状态 | 动作 |
| --- | --- |
| `Loading` | 不 poll；保留 ticker（加载续体负责收尾） |
| `Paused` | `Poll`：`Invalid`/`Stopped` → `Release` 回收（外部销毁的播放源不会自己变回来）；`Paused` → 保留 |
| `Starting` | `Poll`：`Invalid`/`Stopped` → `Release` 回收；`Playing` → 转 `Playing`；`Paused` → 保留、**不计超时**；`Starting` → `++StartingTicks`，超过 60 拍记「起播超时」并 `Release` |
| `Playing` | `Poll`：`Invalid`/`Stopped` → `Release`；`Starting` → 回退为 `Starting` 并重新累计超时（不这么做会永久停在起始态）；`Playing`/`Paused` → 保留（`Loop` 由 provider 报告为 `Playing`） |

- **未知状态兜底**：`Poll` 返回枚举外的值记一条告警并 `Release`——漏掉它会让唯一 voice 既不计入
  `active` 又不被回收，ticker 被注销、槽位永久卡死。

- **死源**：内置 provider 的 `Poll` 对 Unity 假 null 记原有告警
  「播放源已被外部销毁」并返回 `Invalid`；核心立即回收（保持 `AudioPoolTests` 的 0.2s 断言）。
- **全局 `AudioListener.pause`**：内置 provider 的 `Poll` 返回 `Paused`；核心保留、不累计超时
  （AUD-013 的原始语义）。
- **Loop**：核心**不再**对 `Loop` 做特殊保留；内置 provider 对 `Loop` 源返回 `Playing`，
  FMOD 以事件实际状态为准。这样 FMOD one-shot + `Loop=true` 播完后会被正常回收。
- **空转注销**：无 `Loading`/`Starting`/`Playing`/`Paused` 槽位时注销 ticker（保持现有闲置零开销）。
- **异常隔离**：核心对每次 provider 调用包 try/catch；provider 抛异常记 `Audio` 分类日志并回收
  该槽位，不能打崩 `UpdateManager` 调度。

### 4.3 容量所有权（P0）

**`MaxVoices`、耗尽告警、`Play` 的同步预占留在核心**：

- 核心槽位表容量 = `Math.Max(1, options.MaxVoices)`；`Play` 在发起加载前同步预占，池满立即
  `default(AudioHandle)` 并记一次限流告警（同一段耗尽期只告警一次）。
- 内置 provider 的池只按同一 `MaxVoices` 建 GameObject；provider 容量失败按「播放失败」处理，
  核心回收预占。**自定义 provider 的容量必须至少支持与核心 `MaxVoices` 同等的并发**，否则
  `Play` 会返回一个立即失效的预占句柄（`PlayAsync` 返回 default）。
- 这样 `Play_WhenPoolExhausted_ReturnsDefaultHandleAndWarns` 与「池满时一次 IO 都不浪费」保持。

### 4.4 加载中 Stop 与取消（P0，已实现）

实现采用**「不取消加载 + 有效性令牌」**：

- 核心为每个 Loading voice 持 `ValidityCts`；`Stop`/`StopAll` 取消它，但**不取消加载令牌**；
- provider 加载完成后检查 `request.ValidityToken`：已取消则归还租约、不 `start()`、返回 default；
- **validity 取消时必须立即归还实现自己持有的预占槽位（不能等加载完成）**：否则核心槽位已释放、
  provider 槽还占着，`Stop` 之后紧接着的重播会撞上 provider 容量窗口，拿到必然失效的预占句柄
  （P1-1）。内置 provider 在预占后注册 validity 取消回调，回调里 `AudioSourcePool.Invalidate`
  立即归还池槽；晚到的加载续体按代际检查，只归还自己手里的租约，不碰可能已被复用的池槽（P1-2）；
- 因此 `PlayAsync` 在 Stop 期间返回 `default(AudioHandle)`，**不抛 OCE**；调用方
  `cancellationToken` / `Destroy` 取消仍抛 `OperationCanceledException`；
- 租约本身仍随加载跑完再归还（或由晚到续体在取消分支归还）——与现有
  `Stop_WhileLoading_LeaseIsReturnedExactlyOnce` 的「租约释放点恰好一次」契约一致，且不会在
  Stop 后出声。

历史备选（未采用）是「取消加载 + 按令牌来源分流返回值」；它也能不出声，但会改变加载替身的
取消路径，收益只是提前结束 IO。

### 4.5 通道创建副作用（P1）

现有 `ComputeVoiceVolume` 用 `GetOrCreateChannel`，只在加载成功后调用；加载失败的播放不会
创建通道状态，`CopyChannels` 不会多出一行。拆到 provider 后，发起时的 `request.Volume` 必须用
**不建状态的读取**（`GetChannelVolume` / `IsChannelMuted` 口径）；provider 成功后核心再
`GetOrCreateChannel` 重算并 `SetVolume`。否则一次加载失败的播放会污染 `CopyChannels`。

### 4.6 日志文案保持

以下文案与归属必须保持，现有 `LogAssert.Expect` 依赖它们：

- 核心：`播放源已耗尽（MaxVoices=…）`、`播放源起播超时（…）`；
- 内置 provider：`播放失败，clip 加载未成功：…`、`加载音频资源 '…' 时发生未预期异常`、
  `播放源已被外部销毁，…`。
- 暂停/恢复遇到死源时由 `SetPaused` 返回 false、核心回收，不再单独记「暂停/恢复时回收槽位」。

---

## 五、内置 `AudioSourceProvider` 的拆分边界 `[本仓核对]`

迁入 provider：

- `AudioSourcePool`（宿主/槽位/池耗尽策略，去掉 `Sweep`/`CountActive`/`Channel`）、
- `AudioVoice`（provider 槽位记录）、`AudioHost`、`IAudioClipLoader` / `AudioClipLoader`、
  `AudioClipLease`；
- `ConfigureSource`、`ComputeVoiceVolume` 的写源部分、`SafeDisposeLease`、`Prewarm`。

保留在核心：

- 通道/音量/暂停/计数/归一化/校验/`CopyChannels`；
- ticker 与 `StartingTicks` 超时；
- 核心槽位表与 `AudioHandle` 代际。

测试迁移面：

- 保留 internal 便捷构造 `AudioManagerImpl(AudioInitOptions)` 与
  `AudioManagerImpl(AudioInitOptions, IAudioClipLoader)`，内部创建 `AudioSourceProvider`；
- 新增 `AudioManagerImpl(IAudioProvider provider, AudioInitOptions options)` 供 fake provider 测试；
- `AudioTestKit` 经 `AudioManager.CurrentManagerForTests` + internal `TryGetProviderHandle` 映射，
  再加 `AudioSourceProvider.GetSource(AudioProviderHandle)`；**不靠「核心槽位下标 == provider 槽位下标」**；
- `FakeAudioProvider` + `AudioProviderSeamTests` 覆盖核心语义：预占即返回、provider 失败/抛异常、
  音量/暂停、自然结束回收、`CopyChannels`、`SetProvider` 替换与 `TryGetProviderHandle`；
- 现有 Audio fixture 与新增 seam fixture 全绿；无需改 setup 的隐式实例依赖。

---

## 六、FMOD 分包（Phase 1 + Phase 2 已实现）

详见 `Documentation/FMOD.md`。

- **Phase 1 已实现**：`Assets/XFramework.Fmod/` 的 `Venusy609.Xframework.Fmod` 程序集
  （refs 核心 + UniTask，不引用 FMOD）：
  - `FmodAudioSettings`（public 唯一公开类型，配置资产）；
  - `FmodProviderOptions` / `IFmodEventBackend` / `FmodEventSlotTable` / `FmodAudioProvider` /
    `FmodInstaller`（全部 internal）；
  - provider 实现 `IAudioProvider` + 五个 core internal 能力接口；
  - fake backend + provider/设置/桥接/安装测试 + 包内守卫。
- **Phase 2 已实现**：`Venusy609.Xframework.Fmod.Unity` 条件集成程序集（`Runtime/Integration/`），引用
  `FMODUnity`，放真实 `FmodEventBackend` 与 `FmodAutoInstall`；用 `versionDefines`
  （`com.fmod.fmod-for-unity.v204` / `2.4.0`）+ `defineConstraints`（`XFRAMEWORK_FMOD`）门控。
  **两个程序集各自带 versionDefines/defineConstraints**（符号按程序集）。
- **不提供 `FmodAudio.Install` / `FmodAudioOptions`**：公开控制面只有 `AudioManager`；扩展包只公开
  `FmodAudioSettings` 配置资产。
- **自动安装（Phase 2 已实现）**：`FmodAutoInstall` 在 `BeforeSceneLoad` 检测 `Resources` 中的
  `FmodAudioSettings`；`Enabled` 且 `AudioManager` 未初始化时，`FmodInstaller` 先构造 backend/provider
  （加载 banks）成功后再 `SetProvider`；失败清理半成品、记 Error。已初始化则 Warning + 跳过；首轮
  `FMODStudioSettings` 缺失时 Error + 跳过（需先跑 Setup Wizard）。
- **条件 asmdef 悬空引用探针已通过**：无 FMOD 时 `defineConstraints` 排除后不报 missing assembly
  （§FMOD.md §九）；不需要回退到 Editor 生成源码。
- **守卫放 PlayMode 的 `Tests/Runtime/Architecture`**，不引用条件程序集/FMOD；`-Filter Fmod` 默认跑
  PlayMode，避免 Editor 守卫被静默跳过。条件测试单独放 `Tests/Runtime/Integration`。

---

## 七、测试与门禁（Phase 1 + Phase 2 已执行）

核心（已执行）：

- `Tools/run-tests.ps1 -Filter Audio`：核心 Audio 定向全绿（含控制面、内置 SetPosition、起播前参数）；
- `-UpdateApiBaseline`：控制面源兼容新增；基线更新后重跑绿灯；
- `Tools/check-docs.ps1 -Enforce` 通过。

扩展包（Phase 1 + Phase 2 已执行）：

- `Tools/run-tests.ps1 -Filter Fmod`：provider / 设置 / 桥接 / 安装 / 条件测试 / 包内守卫全绿；
  **无 FMOD（干净克隆）与有 FMOD 两种环境都跑**；
- `-UpdateFmodApiBaseline`：Phase 2 公开面未变（Integration 全 internal），无需更新；
- 守卫：README 必答节/文件树、`.meta`、包边界、包契约、依赖声明（Integration 豁免）、AutoInit
  源码守卫、Integration asmdef 条件门控/内部可见性、公开 API 基线、仓库不提交
  `FmodAudioSettings.asset`；
- 真实 bank/event 冒烟：`XFW_FMOD_BANK` / `XFW_FMOD_EVENT` / `XFW_FMOD_GLOBAL_PARAM` 驱动，
  缺省 `Assert.Ignore`；2026-11-12 用 FMOD Studio 2.04 + 官方 Examples 工程实测通过
  （三条 backend 冒烟 8/8、端到端 `FmodEndToEndTests` 1/1；配方与证据见 `Documentation/FMOD.md` §9.6）；
- 自动安装档位 `BeforeSceneLoad` 已按探针选定；`XFW_DISABLE_FMOD_AUTOINSTALL=1` 已实现；
- 干净克隆 + 有 FMOD 两种环境跑全量；`git status` 核对 `.meta` / manifest / lock 未误提交。

---

## 八、已定 / 待裁（扩展）

1. **`AudioManager.TryGetProviderHandle` 是否公开**：**已实现公开**。它是 FMOD/Wwise 逐次事件控制与
   测试映射的入口。
2. **加载中 `Stop` 是否取消**：**已实现「不取消加载 + 有效性令牌」**（§4.4）。`PlayAsync` 返回 default，
   调用方取消/销毁仍抛 OCE。
3. **`AudioPlaybackRequest` 构造可见性**：**已实现 internal**（同 `AudioChannelInfo`）。第三方 provider
   只读取 request；单测可用 fake provider 捕获核心构造的 request。
4. **容量所有权**：**已定为核心**（§4.3）。

以下 5–10 已于 2026-10-26 落地或重新裁定：

5. **扩展公开 API 基线 / `-UpdateFmodApiBaseline` 开关**：**已实现** provider 程序集基线
   （`FmodPublicApiBaselineTests` + `.txt`）；`run-tests.ps1` 增加 `-UpdateFmodApiBaseline`
   （写盘后按设计判失败，重跑才绿灯）。集成程序集契约测试已实现
   （`IntegrationAsmdefs_BindDefineToConstraints` / `IntegrationSources_AreInternalOnly`）；Integration
   全 internal，不需要公开 API 基线。
6. **Wwise 范围**：Wwise 适配器仍 `暂不做`。重开判据：真实 Wwise 项目 + SDK 可编译验证。
7. **FMOD SDK 与 `packages-lock.json`**：目标为 **FMOD 2.04 UPM**；已实测包名
   `com.fmod.fmod-for-unity.v204` / 版本 `2.4.0`。不提交 SDK、manifest 的 SDK 条目或
   `packages-lock.json` 本地改动；2.03 可手动 UPM 化/`.unitypackage` 作兜底。首轮
   `FMODStudioSettings` 缺失时自动安装跳过（FMOD.md §九）。
8. **`FmodAudioProvider` 可见性**：**已实现 internal**；扩展包公开面只有 `FmodAudioSettings` 配置资产。
   不再有 `FmodAudio` / `FmodAudioOptions`。
9. **守卫位置**：**已实现 PlayMode 的 `Tests/Runtime/Architecture`**（理由见 §六），未做纯 Editor 守卫。
10. **`AudioPlaybackRequest` 的中间件参数承载**：**已推翻**「推荐不加」；改为 `AudioParameter` +
    `AudioPlayOptions.InitialParameters`（核心校验并快照复制），运行中/全局参数走
    `AudioManager.SetParameter` / `SetGlobalParameter` 的 internal 能力接口。没有 `UserData` / `Channel` 字段。

---

## 九、与既有结论的关系（下一轮别重提）

- **`Play(AudioClip)` 仍不提供**：`Documentation/Modules/Audio.md` 的「不做：不提供
  `Play(AudioClip, ...)` 重载」条目已否决（后端中立性 + 资源生命周期单语义），无重开判据。
- **框架音量 → 项目 AudioMixer 路由仍不内建**：R3-AUD-001 已实测 `outputAudioMixerGroup` 恒 null；
  B 之后如果项目要路由，只能实现自定义 provider（内置 provider 不提供路由扩展点）。
- **`IAudioProvider` 已实现**：2026-10-25 用户裁定 B，核心已落地；2026-10-26 新增 provider 中立控制面与
  FMOD-free 扩展包；2026-11-07 Phase 2 真实集成已实现。Wwise 仍 `暂不做`。台账见 `Documentation/Modules/Audio.md`。
- **`IAudioManager` / `SetInstance` 不删除**：B 是推荐的中间件路径，不是唯一替换路径。
- **`FmodAudio.Install` / `FmodAudioOptions` 不提供**：被 2026-10-26「只经 AudioManager + 自动安装 +
  `FmodAudioSettings`」取代。
