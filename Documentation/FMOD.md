# FMOD —— 扩展包设计与实施状态（Phase 1 FMOD-free 已实现；Phase 2 集成待 SDK）

> **收件人**：框架维护者（实现或审计 FMOD 扩展时）。**不是**随包发布的使用文档。
> **状态**：核心 provider 中立控制面与 `Assets/XFramework.Fmod` 的 FMOD-free 层已于 2026-10-26 实现；
> 真实 FMOD 集成程序集（条件编译 + `FMODUnity` + 自动安装）**尚未实现**，需要 FMOD 2.04 Asset Store UPM
> 与 SDK 探针。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[已实现]` | 本仓已落地并有测试/门禁 |
| `[已实测]` | 有真实 SDK / 探针输出支撑 |
| `[设计稿]` | 候选形状，未编译、未跑测试 |
| `[未实测]` | 只有官方文档或推理支撑 |

**核对基准**：FMOD for Unity 2.04（Asset Store UPM；2.04 为 UPM-only，2.03 仍可手动 UPM 化）。
**本机未安装 FMOD SDK，Phase 2 的真实 API 签名、包名、程序集名与初始化档位均未实测。**

---

## 一、结论：公开控制面只有 `AudioManager`

用户 2026-10-26 裁定：

- **第三方控制音效只经 `AudioManager`**；不提供 `FmodAudio.Install` / `FmodAudioOptions` / 公开 FMOD 门面。
- 核心新增 provider 中立的控制面：参数（起播前/运行中/全局）、bank、bus、snapshot、逐次 3D 位置。
- provider 侧的可选能力接口（`IAudioParameterProvider` / `IAudioBankProvider` / `IAudioBusProvider` /
  `IAudioSnapshotProvider` / `IAudioSpatialProvider`）为 **core internal**，经 `InternalsVisibleTo` 提供给
  扩展程序集；它们是**官方扩展能力**，不是第三方可自由实现的公开契约。
- 扩展包只公开 `FmodAudioSettings` 配置资产；provider/backend/options 全部 internal。
- 扩展包在 `Resources` 中检测到 `FmodAudioSettings` 时自动安装 provider（Phase 2）；失败不留半初始化门面。
- SDK 目标为 **FMOD 2.04 Asset Store UPM**；SDK 不入仓，不提交 manifest/lock 的本地改动。

**为什么不是旧的 `FmodAudio.Install` 方案**：那是第二套公开门面，第三方要学两套 API，且内置 provider 与
FMOD provider 的控制面会分叉。现在控制语义只在 `AudioManager`，扩展只是后端。

---

## 二、公开面（`[已实现]`）

### 2.1 核心公开新增

- `AudioParameter`：`Name` / `Value`（数值）/ `Label`（标签）/ `IsLabel`；构造校验名称/标签非空白。
- `AudioPlayOptions.InitialParameters`：起播前参数；核心在 `Play`/`PlayAsync` 预占槽位前校验并复制快照，
  非法项抛 `ArgumentException`，调用方之后改原数组不影响本次播放；内置 provider 忽略。
- `AudioPlaybackRequest.InitialParameters`：核心传给 provider 的快照（provider 只读）。
- `AudioManager`：
  - `SetParameter(AudioHandle, string, float/string)`、`SetGlobalParameter(string, float/string)`
  - `LoadBank(string, bool loadSamples = false)`、`UnloadBank(string)`、`IsBankLoaded(string)`
  - `SetBusVolume(string, float)`、`SetBusMuted(string, bool)`、`SetBusPaused(string, bool)`
  - `StartSnapshot(string)`、`StopSnapshot(string)`
  - `SetPosition(AudioHandle, Vector3)`
- 所有控制方法为**探测型**：未初始化/已销毁/default/过期/Loading 句柄/provider 未实现/
  `SetInstance` 自定义管理器 → `false`，不抛、不记 Warning；名称参数 null/空白 → `ArgumentException`
  （参数校验先于能力探测）；provider 异常隔离并记 `[Audio]` 日志。
- `SetBusVolume` 原样透传、不做核心钳制（FMOD 允许 >1 的线性增益）。
- 内置 `AudioSourceProvider` 实现 `SetPosition`（写 `AudioSource.transform.position`），其余控制能力返回
  `false`；`IAudioProvider` / `IAudioManager` 不新增成员。

### 2.2 扩展包公开新增

- 仅 `FmodAudioSettings`（`ScriptableObject` 配置资产，`[CreateAssetMenu]`）。除编译器生成的 public 构造函数
  外没有 public 成员；字段用 `[SerializeField] private`，internal 属性供集成/测试读取。
- `FmodAudioProvider` / `IFmodEventBackend` / `FmodProviderOptions` / `FmodEventSlotTable` 全部 internal。

### 2.3 核心 internal 能力接口

```csharp
internal interface IAudioParameterProvider { bool TrySetParameter(AudioProviderHandle, AudioParameter); bool TrySetGlobalParameter(AudioParameter); }
internal interface IAudioBankProvider { bool TryLoadBank(string, bool); bool TryUnloadBank(string); bool IsBankLoaded(string); }
internal interface IAudioBusProvider { bool TrySetBusVolume(string, float); bool TrySetBusMuted(string, bool); bool TrySetBusPaused(string, bool); }
internal interface IAudioSnapshotProvider { bool TryStartSnapshot(string); bool TryStopSnapshot(string); }
internal interface IAudioSpatialProvider { bool TrySetPosition(AudioProviderHandle, Vector3); }
```

`AudioManagerImpl` 探测 `_provider is IXxx` 并转发；`AudioManager` 门面探测 `_instance is AudioManagerImpl`。
代价：core 硬编码扩展程序集名，扩展包必须与 core 同版本配对；第三方 provider 暂时无法实现这些能力
（台账记 `暂不做`，重开判据：出现真实第三方 provider 需要参数/bank/bus/snapshot）。

---

## 三、对象模型与 provider 映射（`[已实现]` / `[设计稿]`）

| FMOD | 核心 / provider | 说明 |
| --- | --- | --- |
| Studio event（`event:/...`） | `AudioManager.Play` 的 `location` | provider 补 `FmodAudioSettings.EventPathPrefix`（默认 `event:/`，已带则不重复） |
| `EventInstance` | provider 槽位表 + `AudioProviderHandle` | 句柄存槽位下标 + 代际，不把 `EventInstance` 塞进 `int` |
| event 参数 / RTPC | `AudioManager.SetParameter` + `InitialParameters` | 起播前由 provider 在 `start()` 前应用；运行中经 internal 能力接口 |
| global parameter | `AudioManager.SetGlobalParameter` | 无句柄 |
| bus | `AudioManager.SetBusVolume/SetBusMuted/SetBusPaused` | 路径如 `bus:/SFX`；核心原样透传 |
| snapshot | `AudioManager.StartSnapshot/StopSnapshot` | 路径如 `snapshot:/Pause`；停止用 ALLOWFADEOUT（Phase 2） |
| bank | `AudioManager.LoadBank/UnloadBank/IsBankLoaded` | 同步加载；大 bank 会阻塞 |
| `set3DAttributes` | `AudioPlayOptions.Position` + `AudioManager.SetPosition` | 起播写一次，之后手动更新；不自动跟随 Transform |
| 事件内建循环 | `AudioPlayOptions.Loop` 不生效 | 以 Studio 事件编排为准；`Poll` 报告真实状态 |

### 3.1 provider 原语映射

| provider 成员 | FMOD 实现（Phase 2 目标） |
| --- | --- |
| `PlayAsync(location, request, ct)` | 解析事件路径 → `RuntimeManager.CreateInstance` → 应用起播前参数 → volume/pitch/3D → `start()`；失败释放并返回 default |
| `Stop(handle)` | `EventInstance.stop(...)`；返回是否真的停掉 |
| `Release(handle)` | 停播（若需要）→ `release()` → 归还槽位、递增代际；幂等 |
| `SetPaused(handle, paused)` | `setPaused`；句柄失效返回 false |
| `SetVolume(handle, volume)` | `setVolume`（最终音量，核心已算好） |
| `Poll(handle)` | `isValid` / `getPlaybackState` / `getPaused` → `Invalid` / `Starting` / `Playing` / `Paused` / `Stopped` |

`Poll` 是纯查询（不推进超时）；`Starting` 超时、循环回收、死源回收由核心 ticker 决定。FMOD one-shot +
`Loop=true` 播完后 `Poll` 返回 `Stopped`，核心正常回收。

---

## 四、分包设计（`[已实现]` Phase 1；`[设计稿]` Phase 2）

```text
Assets/XFramework.Fmod/
├── package.json / README.md / CHANGELOG.md / LICENSE.md
├── Runtime/
│   ├── Venusy609.Xframework.Fmod.asmdef      # 核心 + UniTask；不引用 FMOD
│   ├── AssemblyInfo.cs                       # IVT 集成/测试
│   ├── FmodAudioSettings.cs                  # public：唯一公开类型，配置资产
│   ├── FmodProviderOptions.cs                # internal：settings 快照
│   ├── IFmodEventBackend.cs                  # internal：int 句柄的 FMOD-free 缝
│   ├── FmodEventSlotTable.cs                 # internal：index + generation 槽位表
│   └── FmodAudioProvider.cs                  # internal：IAudioProvider + 五个能力接口
└── Tests/Runtime/                            # provider/设置/桥接 + Architecture 守卫
```

- **FMOD-free provider 程序集始终编译**：干净克隆（无 FMOD）也能测 provider 与守卫。
- **Phase 2 集成程序集**：`Runtime/Integration/Venusy609.Xframework.Fmod.Unity.asmdef`，引用 `FMODUnity`，
  用 `versionDefines`（实际 UPM 包名 + `XFRAMEWORK_FMOD`）+ `defineConstraints` 条件编译；真实
  `FmodEventBackend` 与自动安装放这里。
- **条件 asmdef 的悬空引用必须先做最小探针**：没有 FMOD 时 `defineConstraints` 排除后不得报 missing
  assembly；若报错，回退到「Editor 生成集成源码到消费方 Assets」方案。这是 Phase 2 最大不确定性。
- 包内守卫：README 结构/文件树、`.meta`、包边界、包契约、依赖声明、AutoInit 族、Runtime 公开 API 基线
  （`FmodAudioSettings` + public 构造函数）；`run-tests.ps1 -UpdateFmodApiBaseline` 更新基线。
- 扩展包 XML 文档不在核心 `check-docs` 射程，质量靠评审（已知缺口，与 Luban 同型）。

---

## 五、自动安装与配置资产（`[设计稿]` Phase 2）

- `FmodAudioSettings` 放在任意 `Resources` 目录；`Enabled` 为 false 或无资产 → 不安装。
- 自动安装档位**先用探针确认 FMOD RuntimeManager 初始化时机**，不要直接写死 `AfterAssembliesLoaded`
  （它可能早于 RTM 初始化）；按探针选 `AfterAssembliesLoaded` / `BeforeSceneLoad` / `AfterSceneLoad`，
  必要时显式调用 RTM 初始化入口。
- `XFW_DISABLE_FMOD_AUTOINSTALL=1` → 直接返回（测试/消费方逃生口；`run-tests.ps1` 启动 Unity 前设置）。
- `AudioManager.IsInitialized` 为 true → 记 Warning 并返回，**不覆盖项目已显式注入的管理器/provider**。
- 构造 backend/provider（加载 banks）全部成功后 `AudioManager.SetProvider(provider, options)`；失败先清理
  本次新加载的 bank 与半成品、记 Error，不留半初始化门面。
- `OwnsBanks` 只卸载 provider 自己成功加载、且加载前未加载的 bank；项目预加载的 bank 永不卸载。
- 配置资产在启动时读一次并快照；之后修改资产不生效。
- 有资产且未初始化时安装 provider；项目若仍调用 `AudioManager.Initialize()` 会得到现有「重复初始化」
  Warning（无害，但 README 明确不要调用）。

---

## 六、限制（写进扩展 README 与核心 Audio README）

- 实际听到 ≈ 核心三档乘积（`MasterVolume × ChannelVolume × VolumeScale`）× FMOD 事件音量 × FMOD bus
  音量 × snapshot 影响；核心不知道 bus/snapshot 状态。建议二选一：用核心三档则 bus 保持 0 dB；用
  bus/snapshot 则核心三档保持 1。
- `AudioManager.Pause()` 只暂停活跃 voice，不暂停 bus/snapshot；`AudioListener.pause` / `AudioListener.volume`
  不作用于 FMOD。
- bank 在 `LoadBank` 时同步加载；大 bank 会卡主线程。
- `AudioOptions.Loop` 不生效；`Pitch` / `SpatialBlend` 只能近似；3D 衰减/遮挡由 Studio 事件编排。
- `AudioManager.Play` 返回时可能仍在 Loading：`SetParameter` / `SetPosition` 可能返回 false；起播前参数用
  `InitialParameters`，需要立即逐次控制用 `PlayAsync`。
- 无逐 voice 暂停/音量、无事件回调、无普通事件 stop fade、无 Wwise。
- 扩展包 Phase 1 不能实际播放 FMOD；根 README 只写「开发中」。

---

## 七、测试与门禁（Phase 1 已执行）

- 核心：`run-tests.ps1 -Filter Audio` 全绿（247 通过）；公开 API 基线更新并重跑绿灯；
  `check-docs.ps1 -Enforce` 通过。
- 扩展：`run-tests.ps1 -Filter Fmod` 全绿（50 通过，含 provider/设置/桥接与包内守卫）；
  `-UpdateFmodApiBaseline` 生成基线后重跑绿灯。
- 守卫覆盖：README 必答节/文件树、`.meta`、包边界、包契约、依赖声明、Phase 1 无 AutoInit、公开 API 基线、
  仓库不提交 `FmodAudioSettings.asset`。
- Phase 2 待执行：
  - 条件 asmdef 探针（无 FMOD 不报 missing assembly；有 FMOD 宏自动定义、集成程序集编译）。
  - 真实 bank/event 冒烟（`XFW_FMOD_BANK` / `XFW_FMOD_EVENT`，缺省 `Assert.Ignore`）。
  - 覆盖 Play/Stop、起播前参数、运行中参数、SetPosition、bus、snapshot、bank、双前缀、`OwnsBanks`。
  - 干净克隆 + 有 FMOD 两种环境跑全量；`git status` 核对 `.meta` / manifest / lock 未误提交。

---

## 八、SDK、版本与漂移点

| 事实 | 状态 | 漂移点 |
| --- | --- | --- |
| FMOD for Unity 2.04 为 UPM-only；官方多包商品含 2.03 / 2.04，2.04 子包名 `com.fmod.fmod-for-unity.v204`、版本 `2.4.0`、Unity 最低 `2022.3` | `[已实测]` | 上游可能改包名/版本 |
| 2.04 可从 Asset Store、fmod.com 或本地 UPM 包（`file:`）安装；本仓实测用本地复制的 UPM 包 | `[已实测]` | 消费方渠道/地区限制 |
| 集成程序集名 `FMODUnity` / `FMODUnityEditor` / `FMODUnityHaptics` / `FMODUnityResonance` / `FMODUnityResonanceEditor` | `[已实测]` | 以实际安装版本为准 |
| `RuntimeUtils.PluginBasePath = "Packages/com.fmod.fmod-for-unity.v204"`；本地 `file:` 包也以该虚拟路径暴露 | `[已实测]` | 包名变化会影响 |
| `FMODUnity.asmdef` 硬引用 `Unity.Timeline` / `Unity.Addressables` / `Unity.ResourceManager` / `Unity.RenderPipelines.Universal.Runtime`；**缺失引用被 Unity 从 csc 响应文件丢弃，不构成编译错误** | `[已实测]` | 若 FMOD 源码在 `#if` 外使用缺失类型才需补丁 |
| `RuntimeManager.CreateInstance` / `EventInstance.start/stop/release/setPaused/setVolume/setPitch/set3DAttributes/setParameterByName(WithLabel)/getPlaybackState/getPaused/isValid` | `[已实测]` | 2.04.0 签名与预期一致 |
| bank：`RuntimeManager.LoadBank` / `UnloadBank` / `HasBankLoaded`；bus：`RuntimeManager.GetBus` + `setVolume/setMute/setPaused`；snapshot：`CreateInstance("snapshot:/...")` + `start` / `stop(ALLOWFADEOUT)` / `release` | `[已实测]` | 同上 |
| 2.04 设置资产生成在 `Assets/Resources/FMOD/FMODStudioSettings.asset`；首轮自动创建默认 settings，首轮 `RuntimeManager` 初始化会失败，第二轮才可用 | `[已实测]` | 需 Setup Wizard / 预置 settings |
| 事件循环由 Studio 编排，`AudioPlayOptions.Loop` 不能强制 | `[未实测]` | 需真实事件验证 |

**SDK 分发**：不提交 SDK、`.unitypackage`、本地 manifest / `packages-lock.json` 改动；干净克隆必须能在
无 SDK 时跳过集成程序集。扩展包 README 是 FMOD 版本/安装方式的唯一真相（核心包不声明 FMOD 依赖）。

---

## 九、Phase 2 前置探针结果（2026-11-07）

本轮用 FMOD for Unity 2.04 本地 UPM 包（`com.fmod.fmod-for-unity.v204` / `2.4.0`）在测试壳上完成；
未提交 SDK、manifest 或 `packages-lock.json` 改动。

### 9.1 条件 asmdef 与悬空引用

- `defineConstraints: ["XFRAMEWORK_FMOD"]` 不满足时，程序集被排除；日志只有
  `... not valid. Loading of assembly skipped.`，没有编译错误。
- 缺失的 asmdef 引用名（`Unity.Addressables`、`Unity.ResourceManager`、
  `Unity.RenderPipelines.Universal.Runtime`、`Unity.XR.OpenXR`，以及完全虚构的名字）被 Unity
  **从 csc 响应文件丢弃**；只有本仓已装的 `Unity.ScriptableBuildPipeline.Editor` 被保留。
- 结论：条件集成 asmdef 引用 `FMODUnity` + `versionDefines` / `defineConstraints` 的设计，在无 FMOD
  的干净克隆上是安全的；FMOD asmdef 的硬引用缺失不需要补丁，只要对应代码在 `#if` 里。

### 9.2 `versionDefines`

- 真实包名 `com.fmod.fmod-for-unity.v204` + expression `2.4.0` 命中：IntegrationProbe 的 csc
  响应文件含 `-define:XFRAMEWORK_FMOD` 与 `-r:FMODUnity.ref.dll`，程序集编译成功。
- 临时移除 manifest 的 `file:` 条目后重编：无 `-define:XFRAMEWORK_FMOD`、无 `error CS`，条件程序集
  被排除。

### 9.3 API 签名

用 2.04 的真实 `FMODUnity` 程序集编译通过：

- `RuntimeManager.CreateInstance(string)` → `FMOD.Studio.EventInstance`
- `EventInstance.start/stop(STOP_MODE.ALLOWFADEOUT)/release/setPaused/setVolume/setPitch/`
  `set3DAttributes/setParameterByName/setParameterByNameWithLabel/getPlaybackState/getPaused/isValid`
- `RuntimeManager.LoadBank(string,bool)/UnloadBank(string)/HasBankLoaded(string)/GetBus(string)` →
  `FMOD.Studio.Bus`；`bus.setVolume/setMute/setPaused`
- snapshot：`CreateInstance("snapshot:/...")` + `start/stop/release`

探针自身的 `FMODUnity.STOP_MODE` / `FMOD.Debug` 歧义只需在调用处写全名，不是签名差异。

### 9.4 `RuntimeManager` / `Settings` 初始化时序

- **首次运行**（`FMODStudioSettings.asset` 不存在）：`Settings.Instance` 在所有档位非空（FMOD 自动创建
  默认 settings），但 `RuntimeManager` 在 `SubsystemRegistration`、`AfterAssembliesLoaded`、
  `BeforeSceneLoad`、`AfterSceneLoad` 及测试体均抛
  `SystemNotInitializedException: [FMOD] Initialization failed`；日志伴随
  `Cannot find integration settings, creating default settings` 与
  `Cannot find properties for platform default/playInEditor, creating default properties`。
- **第二次运行**（settings 已落盘）：`Settings.Instance` 非空，`RuntimeManager.IsMuted` 在
  **全部**上述档位返回 OK（`muted=False`）。
- 结论：`Settings.Instance` 在 `SubsystemRegistration` 已可用；但**首轮自动创建的默认 settings 不足以
  让同一轮的 `RuntimeManager` 初始化成功**。Phase 2 自动安装不能盲目在 `SubsystemRegistration`
  初始化；应要求消费方先跑 FMOD Setup Wizard（或至少让 settings 资产先落盘），或把首轮失败当作可重试
  状态，不要留下半初始化门面。

### 9.5 阻塞项

- 真实 bank/event 冒烟仍需 FMOD Studio 2.04 工程 + 构建好的 bank；本机没有 FMOD Studio，本轮未做。
- 最终验证需在干净克隆（无 FMOD）与有 FMOD 两种环境分别跑全量。

---

## 十、实施状态

- **Phase 1（2026-10-26，已完成）**：核心中立控制面 + FMOD-free provider + `FmodAudioSettings` + fake
  backend 测试 + `AudioManager` 桥接 + 包内守卫 + 公开 API 基线。当前不可实际播放 FMOD。
- **Phase 2（探针完成，待实现）**：2026-11-07 用本地 2.04 UPM 包完成条件 asmdef / `versionDefines` /
  API / 初始化时序探针；条件集成程序集、真实 `FmodEventBackend`、自动安装与真实 bank/event 冒烟仍未
  实现。自动安装需按 §9.4 处理首轮 settings 不可用的情况。

---

## 十一、与既有结论的关系（下一轮别重提）

- **旧的 `FmodAudio.Install` / `FmodAudioOptions` 公开门面已废弃**：被 2026-10-26「只经 AudioManager +
  自动安装 + 配置资产」取代。
- **`Play(AudioClip)` 仍不提供**：`Documentation/Modules/Audio.md` 的「不做」条目无重开判据。
- **框架音量 → 项目 AudioMixer 路由仍不内建**：要路由只能自定义 `IAudioProvider` 或整体替换
  `IAudioManager`。
- **Wwise 仍 `暂不做`**：核心已有中立控制面，但 Wwise 的 RTPC/PlayingID/回调/3D emitter 映射未验证；
  重开判据是真实 Wwise 项目 + SDK 可编译验证（见 `Documentation/Wwise.md`）。
- **核心包不声明 FMOD 依赖**；扩展包 `package.json.dependencies` 为空。
