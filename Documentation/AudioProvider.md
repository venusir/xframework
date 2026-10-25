# AudioProvider —— `IAudioProvider` 播放引擎缝设计（设计稿，未实现）

> **收件人**：框架维护者（实现或审计 Audio 播放引擎缝时）。**不是**使用文档，也**不是**已实现功能的说明。
> **状态**：**设计稿，未写代码**。本文件里的类型、签名、状态机都是候选设计；落地前必须按
> `Documentation/Modules/Audio.md` 的台账流程重新确认，并以实际代码与公开 API 基线为准。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[本仓核对]` | 直接读本仓现有 `Runtime/Audio/` 与测试得出，可当场复现 |
| `[代理逐行核对]` | 2026-10-25 的 Plan 代理按现有代码逐行核对出的缺口与归属 |
| `[设计稿]` | 本文件提出的候选形状，**尚未编译、尚未跑测试** |
| `[待裁]` | 需要用户拍板的取舍；本文件给推荐但不冒充已定 |

**核对基准**：仓库提交 `8f59032f`（2026-10-25）的 `Assets/XFramework/Runtime/Audio/` 与
`Assets/XFramework/Tests/Runtime/Audio/`。**没有新增代码，也没有跑实现测试。**

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
- **本次只出设计文档**：不新增 `.cs`、不改公开 API、不建扩展包；实现待文档评审与 FMOD UPM 包就绪后另开。

为什么不选 A（整体替换 `IAudioManager`）的要点，见 `Documentation/FMOD.md` §三：
A 要求每个适配器重写约 200–300 行框架语义；`CopyChannels` 的类型探测与 `AudioChannelInfo` 的
internal 构造还会让诊断面在中间件路径上空白。B 的主要代价是**核心公开 API 变更 + 一次行为不变的拆分**。

---

## 二、公开 API 草案 `[设计稿]`

以下全部是**候选签名**，不是已存在的 API。最终落地时要按 `CLAUDE.md` 的公开 API 基线流程确认。

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
        internal AudioPlaybackRequest(...);   // 核心构造；构造可见性见 §八 待裁
    }
}
```

门面新增：

```csharp
public static class AudioManager
{
    public static void SetProvider(IAudioProvider provider, AudioInitOptions options = null);

    // 建议纳入；见 §八 待裁
    public static bool TryGetProviderHandle(
        AudioHandle handle, out AudioProviderHandle providerHandle);
}
```

### 接口契约（设计稿）

- **线程**：全部成员限主线程。
- **`request.Volume` 是最终值**：provider **不得**再乘 `VolumeScale` / 通道音量 / 主音量；
  三档相乘与 `[0,1]` 钳制由核心完成。
- **`PlayAsync` 失败返回 `default(AudioProviderHandle)`**，由 provider 自行记日志（不抛）；
  调用方 `cancellationToken` 取消抛 `OperationCanceledException`，且不得泄漏已加载资源/实例。
- **`Stop` 只停播**；**`Release` 负责释放**（停播、释放实例/租约）且幂等。核心在用户 `Stop` 时
  调 `Stop + Release`；ticker 回收与「加载完成但核心槽位已失效」只调 `Release`。
- **`SetPaused` / `SetVolume` 返回 false 表示声音已消失**（播放源被外部销毁、句柄失效），
  核心据此回收槽位；返回 true 表示已应用。
- **`Poll` 是纯查询**：不推进超时、不修改状态；只由核心 ticker 每拍对活跃 voice 调用一次。
  状态含义见 §四。
- **`Dispose` 幂等**：取消在途 `PlayAsync`、释放全部实例与租约；核心在 `AudioManagerImpl.Dispose`
  里调用并隔离异常。

### `SetProvider` 的语义（设计稿）

- 先 `AudioManager.Destroy()` 旧实例（含其 provider），再构造 `new AudioManagerImpl(provider, options)`
  并置为门面实例。**与 `SetInstance` 的「不 Dispose 旧实例」不同**——provider 由核心持有，
  不 Dispose 会泄漏；XML 与 README 必须写明这条差异。
- `provider` 为 null 抛 `ArgumentNullException`。
- `options` 只给核心语义层（`MaxVoices` / `MasterVolume` / `MasterMuted`）；`PrewarmVoices`
  对自定义 provider 无意义，文档要写明。
- `Initialize(options)` 仍构造内置 `AudioSourceProvider`；`SetProvider` 之后 `Initialize`
  按现有规则告警并忽略。

### `TryGetProviderHandle`（建议纳入，待裁）

- 目的：让中间件扩展的**逐次播放能力**（FMOD 事件参数/RTPC、Wwise RTPC/switch）有用户可达入口；
  同时让测试不再依赖内部反射。
- 行为：未初始化/已销毁/`default`/过期/Loading 句柄 → 返回 false 不抛；Active 句柄 → 输出 provider handle。
- 对 `SetInstance` 注入的自定义 `IAudioManager` → 返回 false（它不是 `AudioManagerImpl`）。
- 不加它的后果：扩展只能做「核心语义 + 中间件播放」，做不了逐次事件控制；测试只能用内部构造函数。

---

## 三、为什么核心保留语义（与 A 的边界）

`[本仓核对]` 现有 `AudioManagerImpl` 的职责可以逐条数出来：

| 现有职责 | 拆分后归属 | 依据 |
| --- | --- | --- |
| 通道状态表、序数比较、`"master"` 保留名、查询不建状态 | 核心 | `AudioManagerImpl.cs:52-53,114-127,470-525` |
| 三档音量相乘、钳制、静音 | 核心 | `AudioManagerImpl.cs:637-683` |
| 管理器级暂停闸门 | 核心 | `AudioManagerImpl.cs:374-435` |
| 活跃计数、按通道计数、`CopyChannels` | 核心 | `AudioManagerImpl.cs:547-587` |
| 核心句柄 `AudioHandle` 的槽位/代际/校验 | 核心 | `AudioSourcePool.cs:92-161`（要迁到核心槽位表） |
| `MaxVoices` 与耗尽告警、`Play` 的同步预占 | **核心** | `AudioSourcePool.cs:30-35,92-143,366-377` |
| 加载、`AudioSource` 配置、播完回收、`Starting` 超时 | 内置 provider + 核心 ticker | `AudioManagerImpl.cs:193-316`、`AudioSourcePool.cs:204-289` |
| `AudioListener.pause`、死源检测 | 内置 provider | `AudioSourcePool.cs:218-284` |
| `PrewarmVoices`、宿主/槽位 GameObject | 内置 provider | `AudioSourcePool.cs:74-82,319-364` |

A 方案会让每个中间件适配器重写第一、二、三、四、五行；B 只把最后三行下沉。这就是选 B 的理由。

---

## 四、核心状态机与 ticker `[代理逐行核对]`

### 4.1 核心槽位记录

核心新增 `AudioManagedVoice`（internal）：

- `Generation`
- `State`：`Free` / `Loading` / `Starting` / `Playing` / `Paused`
- `AudioProviderHandle ProviderHandle`（Loading 时是 default）
- `Channel` / `Location` / `VolumeScale`
- `StartingTicks`（仅 `Starting` 用，60 拍超时）

**核心状态机不再与 provider 槽位状态共用同一个枚举**。provider 侧的 `AudioVoice` 只保留
provider 需要的字段（`Source` / `SlotObject` / `Generation` / `ClipLease` / `Location` / `Loop` /
是否已 `start()`）；`AudioVoiceState` 不再同时服务两边。

### 4.2 ticker 每拍的处置

对每个非 `Free` 槽位：

| 核心状态 | 动作 |
| --- | --- |
| `Loading` | 不 poll；保留 ticker（加载续体负责收尾） |
| `Paused` | 不 poll；保留（管理器暂停期间不回收） |
| `Starting` | `Poll`：`Invalid`/`Stopped` → `Release` 回收；`Playing` → 转 `Playing`；`Paused` → 保留、**不计超时**；`Starting` → `++StartingTicks`，超过 60 拍记「起播超时」并 `Release` |
| `Playing` | `Poll`：`Invalid`/`Stopped` → `Release`；`Starting`/`Playing`/`Paused` → 保留（`Loop` 由 provider 报告为 `Playing`） |

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
- 内置 provider 的池只按同一 `MaxVoices` 建 GameObject；provider 容量失败（理论上不会发生）
  按「播放失败」处理，核心回收预占。
- 这样 `Play_WhenPoolExhausted_ReturnsDefaultHandleAndWarns` 与「池满时一次 IO 都不浪费」保持。

### 4.4 加载中 Stop 与取消（P0）

两条路线，推荐第一条：

1. **内部取消 + 区分返回值（推荐）**：核心为每个 Loading voice 持一个停止令牌；
   `Stop`/`StopAll` 取消它，provider 在 `start()` 前检查；provider 抛 OCE 后核心按令牌来源分流：
   - 调用方 `cancellationToken` 或 `Destroy` → 抛 `OperationCanceledException`；
   - `Stop`/`StopAll` 内部取消 → 返回 `default(AudioHandle)`（与现状一致）。
2. **不取消**：`Stop` 只作废核心槽位，加载继续；续体发现槽位已失效后对 provider 返回值调
   `Stop + Release`。返回值的区分不存在，但停止后可能有一帧声音（provider 已经 `start()`）。
   这是最小行为变化路线，但「Stop 后仍出声」是可见缺陷。

### 4.5 通道创建副作用（P1）

现有 `ComputeVoiceVolume` 用 `GetOrCreateChannel`，只在加载成功后调用；加载失败的播放不会
创建通道状态，`CopyChannels` 不会多出一行。拆到 provider 后，发起时的 `request.Volume` 必须用
**不建状态的读取**（`GetChannelVolume` / `IsChannelMuted` 口径）；provider 成功后核心再
`GetOrCreateChannel` 重算并 `SetVolume`。否则一次加载失败的播放会污染 `CopyChannels`。

### 4.6 日志文案保持

以下文案与归属必须保持，现有 `LogAssert.Expect` 依赖它们：

- 核心：`播放源已耗尽（MaxVoices=…）`、`播放源起播超时（…）`；
- 内置 provider：`播放失败，clip 加载未成功：…`、`加载音频资源 '…' 时发生未预期异常`、
  `播放源已被外部销毁，…`、`播放源已被外部销毁，暂停/恢复时回收槽位：…`。

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

- 保留 internal 便捷构造 `AudioManagerImpl(AudioInitOptions options, IAudioClipLoader clipLoader = null)`，
  内部创建 `AudioSourceProvider`，减少现有 fixture 的改动；
- 新增 `AudioManagerImpl(IAudioProvider provider, AudioInitOptions options)` 供 fake provider 测试；
- `AudioTestKit` 需要「核心 handle → provider handle」映射：建议加 internal
  `AudioManagerImpl.TryGetProviderHandle(...)`（与公开的 `AudioManager.TryGetProviderHandle` 同实现），
  再加 `AudioSourceProvider.GetSource(AudioProviderHandle)`；**不要靠「核心槽位下标 == provider 槽位下标」**；
- 新增 `FakeAudioProvider` 覆盖核心语义：预占即返回、Loading 期间 Stop/StopAll/Destroy、
  加载窗口内暂停/音量变化、`Starting` 60 拍超时、provider 抛异常、`CopyChannels` 有数据；
- 现有 13 个 Audio fixture 的 setup 保存 impl（或改用 `SetProvider`），不再依赖 `AudioManager` 静态门面
  的隐式实例。

---

## 六、FMOD 分包的建议形状

详见 `Documentation/FMOD.md` 与 `Documentation/FmodUPM.md`。核心建议：

- **拆成两个程序集**：
  - `Venusy609.Xframework.Fmod`：FMOD-free 的 provider 逻辑（`FmodAudioProvider` internal、
    `FmodAudioOptions` public、`IFmodEventBackend` internal）+ fake backend 测试；**始终编译**，
    干净克隆也能测 provider 逻辑；
  - `Venusy609.Xframework.Fmod.Unity`：条件程序集，引用 `FMODUnity`，放 `FmodEventBackend`
    与 `FmodAudio.Install`；用 `versionDefines`（`com.firelight.fmod-for-unity`）+ `defineConstraints`
    （`XFRAMEWORK_FMOD`）条件编译。
- **两个程序集各自带 `versionDefines`/`defineConstraints`**（Unity 的 versionDefines 符号按程序集）。
- **守卫放 PlayMode 的 `Tests/Runtime/Architecture`**，不引用条件程序集/FMOD；本仓 `-Filter`
  未指定平台时只跑 PlayMode，Editor 守卫会被静默跳过。
- **`FmodAudioProvider` internal**，用户只用 `FmodAudio.Install`；`FmodAudioOptions` public。
- `Install(FmodAudioOptions fmodOptions, AudioInitOptions audioOptions = null)` 内部构造
  real backend + provider，再 `AudioManager.SetProvider`；不要留半初始化门面。

---

## 七、测试与门禁计划（实现时执行，本次不做）

核心：

- `Tools/run-tests.ps1 -Filter Audio`（现有 13 个 fixture 回归）；
- `Tools/run-tests.ps1 -UpdateApiBaseline`（写盘后按设计失败）→ 审 diff → 重跑全量；
- `Tools/check-docs.ps1 -Enforce`（新增公开成员的 XML 文档、cref、源文件参与编译、模块 README 文件树）。

扩展包：

- provider asmdef + fake backend 测试：干净克隆（无 FMOD）也编译、也跑；
- 条件 asmdef 探针：无 FMOD 时 `defineConstraints` 排除后不得报「missing assembly」；
  有 FMOD 时 `XFRAMEWORK_FMOD` 自动定义、集成程序集编译；
- 真实 bank/event 冒烟：`XFW_FMOD_BANK` / `XFW_FMOD_EVENT` 环境变量驱动，缺省跳过；
- 包内 PlayMode 守卫：README 树、`.meta`、包边界、包名一致性、条件 asmdef 一致性；
- 干净克隆 + 有 FMOD 两种环境跑全量；`git status` 核对 `.meta` / manifest / lock 未误提交。

---

## 八、待裁（逐条给推荐，不自称已定）

1. **`AudioManager.TryGetProviderHandle` 是否公开**：**推荐公开**。不加则 FMOD 事件参数/RTPC、
   Wwise RTPC/switch 没有用户可达入口，扩展只能做「核心语义播放」。代价是公开面 +1 方法。
2. **加载中 `Stop` 是否取消**：**推荐内部取消 + 区分返回值**（§4.4 路线 1）。它保持「Stop 后不出声」
   且 `PlayAsync` 仍返回 default；代价是核心需要区分令牌来源。路线 2 更小，但有一帧声音。
3. **`AudioPlaybackRequest` 构造可见性**：**推荐 internal**（同 `AudioChannelInfo`）。第三方 provider
   只读取 request；它的单测可以用自己的 fake provider 捕获核心构造的 request，不必自己 new。
   若未来有第三方 provider 要在框架外自测，再评估 public 构造。
4. **容量所有权**：**已定为核心**（§4.3），不再作为待裁。
5. **扩展公开 API 基线 / `-UpdateFmodApiBaseline` 开关**：v1 **推荐只做 provider 程序集基线**
   （始终可跑）+ 集成程序集契约测试；不新增 `run-tests` 开关。若将来加开关，FMOD 缺失时必须
   显式失败，不能 0 用例假绿。
6. **Wwise 范围**：本次只设计 provider 缝与 Wwise 映射；Wwise 适配器仍 `暂不做`。重开判据：
   真实 Wwise 项目 + SDK 可编译验证。
7. **FMOD 本地包与 `packages-lock.json`**：**推荐** FMOD 包放 `Packages/com.firelight.fmod-for-unity/`
   并 gitignore；若 Unity 把它写进 `packages-lock.json`，**不提交** lock/manifest 的本地改动；
   干净克隆验证 `run-tests` 不因缺包失败。若 lock 无法避免，改用仓外 `file:` 依赖（同样不提交）。
8. **`FmodAudioProvider` 可见性**：**推荐 internal**，用户经 `FmodAudio.Install`；公开面只留
   `FmodAudio` + `FmodAudioOptions`。
9. **守卫位置**：**推荐 PlayMode 的 `Tests/Runtime/Architecture`**（理由见 §六），不做纯 Editor 守卫。
10. **`AudioPlaybackRequest` 是否增加 `Channel`/`UserData` 承载中间件专属参数**：**推荐不加**。
    中间件专属参数走扩展自己的 API + `TryGetProviderHandle`，核心 request 保持中立。

---

## 九、与既有结论的关系（下一轮别重提）

- **`Play(AudioClip)` 仍不提供**：`Documentation/Modules/Audio.md:59` 已否决（后端中立性 +
  资源生命周期单语义），无重开判据。
- **框架音量 → 项目 AudioMixer 路由仍不内建**：R3-AUD-001 已实测 `outputAudioMixerGroup` 恒 null；
  B 之后如果项目要路由，只能实现自定义 provider（内置 provider 不提供路由扩展点）。
- **`IAudioProvider` 不再是「暂不做」**：2026-10-25 用户裁定改为设计；实现状态是 `未做`，
  不是 `已做`。台账见 `Documentation/Modules/Audio.md`。
- **`IAudioManager` / `SetInstance` 不删除**：B 是推荐的中间件路径，不是唯一替换路径。
