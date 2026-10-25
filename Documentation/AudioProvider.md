# AudioProvider —— `IAudioProvider` 播放引擎缝（核心已实现；FMOD/Wwise 扩展后续）

> **收件人**：框架维护者（实现或审计 Audio 播放引擎缝时）。**不是**使用文档。
> **状态**：**核心于 2026-10-25 实现并完成复审修复**（`IAudioProvider` / 内置 `AudioSourceProvider` /
> `AudioManager.SetProvider` / `TryGetProviderHandle`）；**FMOD 扩展包、FMOD SDK 接入方式与
> Wwise 适配器仍未实现**，后续单独讨论。本文件保留设计取舍与扩展蓝图。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[本仓核对]` | 直接读本仓现有 `Runtime/Audio/` 与测试得出，可当场复现 |
| `[代理逐行核对]` | 2026-10-25 的 Plan 代理按现有代码逐行核对出的缺口与归属 |
| `[已实现]` | 核心已于 2026-10-25 实现并跑过 Audio 测试；以实际代码与公开 API 基线为准 |
| `[设计稿]` | FMOD/Wwise 扩展的候选形状，**尚未编译、尚未跑测试** |
| `[待裁]` | 需要用户拍板的取舍；本文件给推荐但不冒充已定 |

**核对基准**：2026-10-25 实现并复审修复后的 `Assets/XFramework/Runtime/Audio/` 与
`Assets/XFramework/Tests/Runtime/Audio/`。核心 Audio 定向测试全绿（含 `AudioProviderSeamTests`），
全量双平台 0 失败（见 §七）；FMOD/Wwise 扩展仍未实现。

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
- **FMOD/Wwise 扩展后续**：FMOD 分包、SDK 接入方式（官方 `.unitypackage` / 标记包 / UPM）与 Wwise
  适配器均未实现，后续单独讨论。

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

## 六、FMOD 分包的建议形状（未实现；SDK 接入方式待讨论）

详见 `Documentation/FMOD.md`。**FMOD SDK 接入方式（官方 `.unitypackage` 导入命令 / 本地标记包 /
UPM）尚未决定，后续单独讨论**；下列是程序集拆分建议，不代表已实现。

- **拆成两个程序集**：
  - `Venusy609.Xframework.Fmod`：FMOD-free 的 provider 逻辑（`FmodAudioProvider` internal、
    `FmodAudioOptions` public、`IFmodEventBackend` internal）+ fake backend 测试；**始终编译**，
    干净克隆也能测 provider 逻辑；
  - `Venusy609.Xframework.Fmod.Unity`：条件程序集，引用 `FMODUnity`，放 `FmodEventBackend`
    与 `FmodAudio.Install`；用 `versionDefines`（`com.firelight.fmod-for-unity`，**仅当最终选择该
    UPM 路径时**，包名以实际安装的 SDK 为准）+ `defineConstraints`（`XFRAMEWORK_FMOD`）条件编译。
- **两个程序集各自带 `versionDefines`/`defineConstraints`**（Unity 的 versionDefines 符号按程序集）。
- **守卫放 PlayMode 的 `Tests/Runtime/Architecture`**，不引用条件程序集/FMOD；本仓 `-Filter`
  未指定平台时只跑 PlayMode，Editor 守卫会被静默跳过。
- **`FmodAudioProvider` internal**，用户只用 `FmodAudio.Install`；`FmodAudioOptions` public。
- `Install(FmodAudioOptions fmodOptions, AudioInitOptions audioOptions = null)` 内部构造
  real backend + provider，再 `AudioManager.SetProvider`；不要留半初始化门面。

---

## 七、测试与门禁（核心已执行；扩展待后续）

核心（已执行）：

- `Tools/run-tests.ps1 -Filter Audio`：Audio 定向全绿（含 `AudioProviderSeamTests`）；
- `-UpdateApiBaseline`：Runtime +33（源兼容，0 破坏性）；
- `Tools/check-docs.ps1 -Enforce` 通过；全量双平台（PlayMode / EditMode）0 失败。

扩展包：

- provider asmdef + fake backend 测试：干净克隆（无 FMOD）也编译、也跑；
- 条件 asmdef 探针：无 FMOD 时 `defineConstraints` 排除后不得报「missing assembly」；
  有 FMOD 时 `XFRAMEWORK_FMOD` 自动定义、集成程序集编译；
- 真实 bank/event 冒烟：`XFW_FMOD_BANK` / `XFW_FMOD_EVENT` 环境变量驱动，缺省跳过；
- 包内 PlayMode 守卫：README 树、`.meta`、包边界、包名一致性、条件 asmdef 一致性；
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

以下 5–10 属于 FMOD/Wwise 扩展，**尚未实现、待后续讨论**；保留推荐供下一轮参考：

5. **扩展公开 API 基线 / `-UpdateFmodApiBaseline` 开关**：v1 **推荐只做 provider 程序集基线**
   （始终可跑）+ 集成程序集契约测试；不新增 `run-tests` 开关。若将来加开关，FMOD 缺失时必须
   显式失败，不能 0 用例假绿。
6. **Wwise 范围**：本次只设计 provider 缝与 Wwise 映射；Wwise 适配器仍 `暂不做`。重开判据：
   真实 Wwise 项目 + SDK 可编译验证。
7. **FMOD 本地包与 `packages-lock.json`**：**推荐** FMOD 包放 `Packages/com.firelight.fmod-for-unity/`
   （**仅当最终选择该本地包路径时**）并 gitignore；若 Unity 把它写进 `packages-lock.json`，**不提交**
   lock/manifest 的本地改动；干净克隆验证 `run-tests` 不因缺包失败。若 lock 无法避免，改用仓外
   `file:` 依赖（同样不提交）。
8. **`FmodAudioProvider` 可见性**：**推荐 internal**，用户经 `FmodAudio.Install`；公开面只留
   `FmodAudio` + `FmodAudioOptions`。
9. **守卫位置**：**推荐 PlayMode 的 `Tests/Runtime/Architecture`**（理由见 §六），不做纯 Editor 守卫。
10. **`AudioPlaybackRequest` 是否增加 `Channel`/`UserData` 承载中间件专属参数**：**推荐不加**。
    中间件专属参数走扩展自己的 API + `TryGetProviderHandle`，核心 request 保持中立。

---

## 九、与既有结论的关系（下一轮别重提）

- **`Play(AudioClip)` 仍不提供**：`Documentation/Modules/Audio.md` 的「不做：不提供
  `Play(AudioClip, ...)` 重载」条目已否决（后端中立性 + 资源生命周期单语义），无重开判据。
- **框架音量 → 项目 AudioMixer 路由仍不内建**：R3-AUD-001 已实测 `outputAudioMixerGroup` 恒 null；
  B 之后如果项目要路由，只能实现自定义 provider（内置 provider 不提供路由扩展点）。
- **`IAudioProvider` 已实现**：2026-10-25 用户裁定 B，核心已落地（公开 API 源兼容新增）；
  FMOD/Wwise 扩展仍未实现。台账见 `Documentation/Modules/Audio.md`。
- **`IAudioManager` / `SetInstance` 不删除**：B 是推荐的中间件路径，不是唯一替换路径。
