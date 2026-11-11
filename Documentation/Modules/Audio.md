# Audio —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Audio/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Audio/
├── IAudioManager.cs          # 公开：整体替换管理器的接口（不含任何 Unity 内置音频类型）
├── IAudioProvider.cs         # 公开：播放引擎缝（核心保留语义，provider 只做播放原语）
├── IAudioParameterProvider.cs # internal：逐次参数能力接口（官方扩展能力，IVT 给 Fmod）
├── IAudioSpatialProvider.cs  # internal：逐次 3D 位置能力接口（官方扩展能力，IVT 给 Fmod）
├── IAudioBankProvider.cs     # internal：bank 能力接口（官方扩展能力，IVT 给 Fmod）
├── IAudioBusProvider.cs      # internal：bus 能力接口（官方扩展能力，IVT 给 Fmod）
├── IAudioSnapshotProvider.cs # internal：snapshot 能力接口（官方扩展能力，IVT 给 Fmod）
├── AudioProviderHandle.cs    # 公开：provider 句柄（readonly struct，字段公开）
├── AudioProviderPlaybackState.cs # 公开：Poll 的返回值
├── AudioPlaybackRequest.cs   # 公开：核心交给 provider 的最终参数（internal 构造）
├── AudioManager.cs           # 静态门面（扁平 + #region 分区） + AutoInit + SetProvider
├── AudioManagerImpl.cs       # 语义层：通道/音量/暂停/计数/诊断/句柄编排/ticker（internal）
├── AudioManagedVoice.cs      # 核心槽位记录 + AudioManagedVoiceState（internal）
├── AudioSourceProvider.cs    # 内置播放引擎：加载/暂停/回收判据（internal）
├── AudioSourcePool.cs        # 内置引擎的宿主与槽位池（internal）
├── AudioVoice.cs             # 内置引擎的槽位记录 + AudioVoiceState（internal）
├── AudioHandle.cs            # 播放句柄（readonly struct，字段公开且对门面不透明）
├── AudioParameter.cs         # provider 中立参数（数值 / 标签），起播前与运行中参数的数据载体
├── AudioPlayOptions.cs       # 单次播放参数（六个既有字段 + InitialParameters 起播前参数）
├── AudioChannels.cs          # 推荐通道常量（不是枚举，见 README 设计取舍）
├── AudioChannelConfig.cs     # RegisterChannel 的配置（Volume / Muted）
├── AudioChannelInfo.cs       # 诊断回读载荷：CopyChannels 的列表元素（2026-10-16 补录——它一直是公开面，此前漏在本树之外）
├── AudioChannelState.cs      # 一个通道的音量状态（internal，惰性创建）
├── AudioInitOptions.cs       # 初始化选项
├── AudioHost.cs              # 隐藏宿主 MonoBehaviour（internal，纯挂载点）
├── AudioTicker.cs            # IUpdateable：Tier1 + Unscaled 的回收驱动（internal）
├── AudioClipLease.cs         # 资源租约：本体 + 释放动作（internal）
├── IAudioClipLoader.cs       # 内置引擎的加载缝（internal，测试缝）
├── AudioClipLoader.cs        # 默认加载缝：经 XAsset 加载（internal）
└── README.md                 # 使用说明

Tests/Runtime/Audio/          # 全部 PlayMode；无 EditMode 侧（没有 Editor 工具面）
```

## 实测记录

来自 `2026-10-01` 的一次性探针（`-batchmode`、无音频设备）。这几条改变了测试的形状，也是若干设计判断的依据——**下一轮不要凭记忆重推**。

| 命题 | 实测结果 | 对设计的影响 |
| --- | --- | --- |
| batchmode 下音频是否推进 | **推进**。0.2s 内 `time` 走到 `0.192`；播完后 `isPlaying=False` 且 `time` 归零 | 回收用例走**真实播放驱动**，不需要造内部 seam |
| `Pause()` 之后 `isPlaying` | **变成 `False`** | 回收判据**不能只看 `isPlaying`**——只看它会把暂停中的播放当场回收。`AudioVoiceState.Paused` 因此存在 |
| `Stop()` 对未播放的 source | 安全、不抛 | 回收路径的健壮性 |
| `dspTime` | 正常推进（0.2s 内推进 0.2133s） | — |
| `AudioSource.volume` 的 setter | 引擎钳到 `[0, 1]`（`2.5 → 1.0`，`-1 → 0.0`） | 框架仍**自行钳制**：公开承诺不该押在引擎行为上 |

## 决策台账

- 已做：**探测型总表补「加载窗口内一律 false」**（2026-11-11 · `b9315b56`，R5-AUD-002）——`Runtime/Audio/README.md` 的探测型总表下方补一句：`PlayAsync` 已起播但资源仍在加载时，参数 / bank / bus / snapshot / 位置控制面全部返回 `false`、不排队；起播前要用 `AudioPlayOptions.InitialParameters` / `Position`。
- 已做：**`AudioManager` 纳入会话复位族**（2026-11-11 · `65e5536a`，R5-ADJ-05）——新增 `AudioManager.ResetForNewSession`（`SubsystemRegistration`，只丢引用不 Dispose），登记 `AutoInitTests` 族清单与 `SessionResetTests`（`Audio_ReinitializesAfterSessionReset`，去掉复位调用即红）。关闭域重载且 `Application.quitting` 未触发时，第二会话不再复用上一会话的 manager，FMOD 自动安装会重跑。**边界**：关闭域重载 + FMOD 的手工两遍仍是 R5-PRB-01（`未做`）。
- 已做：**`Play()` 后同步读一次 `isPlaying`**（2026-10-17 · `248d473`，AUD-001）——短于一拍的 one-shot 不再卡在 `Starting` 到 60 拍超时并误报；实测（P-06）`Play()` 后同步读为真。

- 已做：**v1 功能面建成**（2026-10-01 · 使用方 README + `CHANGELOG`）——门面模板全套（`Initialize` / `SetInstance` / `Destroy` / `EnsureInitialized`）+ `AutoInit`；隐藏宿主 + 池化 `AudioSource` + 代际安全句柄 + 播完回收（含 `Starting` 超时兜底）；字符串通道 + 推荐常量 + 惰性通道状态 + `RegisterChannel`；location 播放路径（`Play` / `PlayAsync`）+ 全部失败/取消/竞态路径；三档音量相乘 + 变更即时生效 + 静音；`Stop` / `StopAll` / 手动 `Pause`/`Resume` / 查询与计数。原「已完成（v1）」清单即上述各项，功能说明与用法在使用方 README，逐条变更证据在 `CHANGELOG`。
- 未做：**并发抢占 / 优先级策略**（2026-10-01 · v2 候选）——需先答「抢谁」= 替使用方定混音策略；`AudioSource.priority` 的引擎语义需实测。
- 未做：**淡入淡出 / crossfade**（2026-10-01 · v2 候选）——需要每 voice 的淡变状态与时间源，且与 `Pause` 交互；自成一块。
- 未做：**切后台自动暂停**（2026-10-01 · v2 候选）——需「谁暂停的」状态机 + iOS/Android 真机实测（见使用方 README 设计取舍）。
- 未做：**借用项目提供的 `AudioSource`**（2026-10-01 · v2 候选）——音量归属 / `Stop` 语义 / 源销毁失效 / 借用期冲突四条语义，可独立提交。
- 不做：**ducking（音效压低 BGM）**（2026-10-01 · v2 候选）——混音设计理念，项目间差异大；Mixer 侧才是它的家。无重开判据。
- 暂不做：**框架音量 → 项目 Mixer 的驱动接口**（2026-10-01 · v2 候选）——先观察是否真有需求；~~现在给的是「框架音量留 1.0、Mixer 独占控制」~~（2026-10-19 订正：该理由不成立——框架动态声源的 `outputAudioMixerGroup` 恒 `null`，声音进不了项目 Mixer，`AudioMixer.SetFloat` 静默无效；结论不变：内置实现不提供路由入口；2026-10-25 起也可自定义 `IAudioProvider` 接管输出（框架仍不新增路由入口）。R3-AUD-001）。重开判据：出现真实需求，且接受新增「不暴露 Unity 类型的路由扩展点」。
- 暂不做：**`AudioBootstrapStage` / `AudioSettingsBridge<T>` / `AudioHandle.State` / 通道级上限 / `DumpState` 这批便利面**（2026-10-01 · v2 候选）——等真实反馈。重开判据：出现真实使用反馈。
- 不做：**跟随 Transform 的 3D 发射器**（2026-10-01 · v2 候选）——本质是项目侧 MonoBehaviour；2026-10-26 新增 `AudioManager.SetPosition` 允许手动更新位置，但仍不自动跟随。无重开判据。
- 不做：**不提供 `Play(AudioClip, ...)` 重载**（2026-10-01 · v1 评估）——它会同时毁掉两件事：`IAudioManager` 的后端中立性（Wwise 的事件 / FMOD 的 `EventInstance` 都没有 `AudioClip`），以及「资源生命周期只有一套语义」；「回不到 Inspector 直引」这条代价已写进 README 的已知限制。无重开判据。
- 已推翻：**~~不提供 `IAudioProvider` 分层引擎缝~~**（2026-10-01 · v1 评估）——本仓「后端可换」有两种形态，分界线是「替换轴只有一条且贯穿全模块 → 整体替换」（Input 换 Rewired / Save / Asset / Localization / Config / UI）vs「替换轴按平台或能力切分、需组合多个实现 → 分层 Provider 缝」（**全仓只有 File**）；Audio 属前者。**不盲做的理由是拿不到 Wwise/FMOD 的 API 形状**：Wwise 的播放单元是 Event + EventInstance、音量是 RTPC、3D 发射器要挂 GameObject，FMOD 是 `EventInstance`——与「clip + volume + transform.position」都不同构，凭记忆猜出来的缝大概率是谁都不合身的最小公约数。重开判据：手上有真实中间件项目（有 Wwise/FMOD 的 API 参考即可设计）。被 2026-10-25『`IAudioProvider` 播放引擎缝（已实现）』取代——用户已有真实 FMOD Studio 工程与 bank，重开判据触发；设计见 `Documentation/AudioProvider.md`。
- 已做：**`IAudioProvider` 播放引擎缝**（2026-10-25 · 用户裁定 B，本期实现）——核心保留通道 / 三档音量 / 暂停闸门 / 计数 / 归一化 / 诊断；provider 只做 PlayAsync / Stop / Release / SetPaused / SetVolume / Poll 六个原语（外加 `Dispose`）；`AudioManager.SetProvider` 注入、`TryGetProviderHandle` 取 provider 句柄。公开 API：`IAudioProvider` / `AudioProviderPlaybackState` / `AudioProviderHandle` / `AudioPlaybackRequest`（源兼容新增，基线 +33）。内置 `AudioSourceProvider` 承接原池 / 加载 / `AudioListener.pause`；`Starting` 超时仍由核心 `AudioManagerImpl` 的 ticker 负责。回归：`AudioProviderSeamTests` + 现有 Audio fixture（Audio 定向全绿；全量双平台 0 失败）。设计见 `Documentation/AudioProvider.md`。
- 已做：**provider 缝复审：取消竞态与状态机兜底**（2026-10-25 · `AudioPoolTests.StopWhileLoading_ImmediateReplay_StartsTheNewPlayback` / `AudioProviderSeamTests`）——validity 取消时立即归还池槽，晚到续体按代际只归还本地租约（P1-1/P1-2）；起播段异常统一回收（P1-3）；未知 `Poll` 状态兜底回收、`Playing`→`Starting` 重新累计超时、暂停中也 `Poll` 回收死源；provider 的取消回调异常隔离；provider 返回后补检查调用方取消（抛 OCE）；`SetProvider` 同实例显式抛异常。细节见 `CHANGELOG` 同条与 `Documentation/AudioProvider.md` §4。
- 已推翻：**~~官方 FMOD / Wwise 适配器扩展包~~**（2026-10-25 · 用户评估）——原理由：可行的接缝是整体替换 `IAudioManager`（`SetInstance`），核心零改动；但不建官方包：两家的 Unity 集成不是官方 UPM 源、SDK 不可再分发、本仓无法编译/测试/进六道门禁；且 `IAudioManager` 只能表达 6 个播放参数 + 三档音量，中间件的 RTPC / 事件参数 / bank / 3D 发射器等仍需项目侧直接用 SDK。被 2026-10-25『FMOD 适配器分包』与『Wwise 适配器分包（暂不做）』取代——B 方案下不再重写 `IAudioManager`；FMOD 的 FMOD-free 层已于 2026-10-26 落地。
- 已做：**`AudioManager` provider 中立控制面**（2026-10-26 · `d87ae75e` / `ac1a54c1`，`AudioControlSurfaceTests` / `AudioMixerControlTests` / `AudioSpatialTests`）——新增逐次/全局参数、bank、bus、snapshot、逐次 3D 的公开方法；`AudioParameter` + `AudioPlayOptions.InitialParameters` 承载起播前参数（核心校验并快照复制）；能力接口为 core internal，经 `InternalsVisibleTo` 给官方 FMOD 扩展；内置 provider 仅实现 `SetPosition`，其余返回 `false`。公开 API 基线已同步。
- 已做：**FMOD 适配器分包（FMOD-free 层）**（2026-10-26 · `07605197` / `dfd1f473` / `7bfa15d5`）——`Assets/XFramework.Fmod/`：`FmodAudioSettings` 配置资产 + `FmodAudioProvider`（internal，`IAudioProvider` + 五个 core 能力接口）+ `IFmodEventBackend` 缝 + fake backend 测试 + 包内守卫（README/文件树、`.meta`、包边界、包契约、依赖声明、AutoInit、Runtime 公开 API 基线）。干净克隆无 FMOD 也编译、也跑；Phase 1 时不可实际播放 FMOD（Phase 2 见下条）。
- 已做：**FMOD 2.04 真实集成程序集与自动安装**（2026-11-07 · `cc4e79c1` / `18b2e4a7` / `98911464`）——条件集成 asmdef（`FMODUnity` + `versionDefines` / `defineConstraints`）、真实 `FmodEventBackend`、`FmodInstaller` / `FmodAutoInstall` 自动安装、条件测试与包内守卫。自动安装 `BeforeSceneLoad`；首轮 `FMODStudioSettings` 缺失时 Error + 跳过；真实 bank/event 冒烟需 `XFW_FMOD_BANK` / `XFW_FMOD_EVENT` 与 Studio 2.04 工程，缺省跳过。
- 已推翻：**~~`FmodAudio.Install` 公开入口 / `FmodAudioOptions`~~**（2026-10-26 · 用户裁定）——第二套公开门面与「公开控制面只有 `AudioManager`」冲突；被 2026-10-26「`AudioManager` 中立控制面 + `Resources/FmodAudioSettings` 自动安装」取代。
- 暂不做：**公开 provider 能力接口**（2026-10-26）——`IAudioParameterProvider` / `IAudioBankProvider` / `IAudioBusProvider` / `IAudioSnapshotProvider` / `IAudioSpatialProvider` 为 core internal，只经 `InternalsVisibleTo` 给官方 FMOD 扩展；第三方 provider 仍用 `TryGetProviderHandle`。重开判据：出现真实第三方 provider 需要参数/bank/bus/snapshot。
- 暂不做：**Wwise 适配器分包**（2026-10-25）——provider 缝已实现（`Documentation/Wwise.md`），但无真实 Wwise 项目 / SDK 可编译验证；Wwise 的 PlayingID / 回调 / 3D emitter 与 FMOD 差异大，不在没有真实 API 时盲做。重开判据：真实 Wwise 项目 + SDK 可编译验证。
- 暂不做：**不把 `IAudioClipLoader` 提升为公开面**（2026-10-01 · v1 评估；2026-10-25 订正理由）——公开缝是 `IAudioProvider`（见上方 `已做` 条）；loader 仍是内置 provider 的 internal 加载/测试缝，不构成第二个后端替换点。先观察是否真有「只换资源来源、不换播放引擎」的需求。重开判据：出现「只换资源来源、不换播放引擎」的真实需求。
- 已推翻：**~~不做只读的通道状态快照~~**（2026-10-01 · v1 评估）——原理由「`GetChannelVolume` / `IsChannelMuted` 已覆盖查询需求」；诊断回读面已落地（通道名 / 音量 / 静音 / 在播路数），只剩逐 voice 明细未做（见下方条目）。被 2026-10-02『`CopyChannels` 诊断回读落地』取代。
- 不做：**框架内建「语音随语言切换」缺席**（2026-10-01 · v1 评估）——那要发明一套「语言 → 语音资源路径」的约定，而 `XLocalization` 本身只有语言表模板、没有「按 key 取资源路径」的概念；且「切语言时当前台词停在半句还是重播」是 GamePlay 时序决定。README 只给订阅 `LanguageChangedMessage` 的做法。无重开判据。
- 不做：**`Play` 与 `PlayAsync` 的句柄契约不对称**（2026-10-01 · v1 记录；2026-10-19 R3-ADJ-02 裁定为不做）——前者在预占槽位时就返回、加载失败只让句柄失效；后者在加载完成后才返回、失败返回 `default`。两条契约各自自洽、也已写进 README 的对照表，统一会改变 `Play` 的「预占即返回」时序。无重开判据。
- 未做：**`AudioManagerImpl.StartingTimeoutTicks = 60`（约 2 秒）是自定值，真机未验证**（2026-10-01 · v1 记录；2026-10-25 常量随 provider 拆分迁到核心）——兜底的是「某平台 `Play()` 之后 `isPlaying` 永不变真」这种情形；本仓只实测了批处理模式下的桌面行为，**真机（尤其移动端）未验证**。
- 未做：**`TryReserve` 的线性扫描**（2026-10-01 · v1 记录）——O(槽位数)，`MaxVoices` 默认 32；若将来允许把上限调到很大，需要换空闲链表。
- 未做：**门面没有逐 voice 明细回读**（2026-10-01 · v1 记录；2026-10-17 第二轮审计刷新）——`CopyChannels` 已提供通道级回读（见上方「已推翻」条）；剩余的是逐 voice 明细，会泄漏 internal 结构，形状未定。重开判据：出现需要逐 voice 诊断的真实使用方。

- 已做：**Free 槽坏源在原下标重建**（2026-10-25 · `b9b3a67f`，R4-AUD-001，本轮修复）——外部销毁宿主/播放源后 `Source` 为 Unity 伪 null，`TryReserve` 恒取该 Free 槽且不重建，每次播放都失败。修法：`AudioVoice` 记槽位 GameObject；命中坏源时补 `AudioSource` 或按原名重建 `Voice {n}`，保留旧代际后递增。回归 `AudioPoolTests.ExternallyDestroyedHost_NextPlayRebuildsSlotAndSucceeds` / `ExternallyDestroyedSource_NextPlayRebuildsSource`（改前第二次播放取不到播放源）。
- 已做：**`AudioHandle` 相等语义**（2026-10-20 · R3-AUD-004，公开 API 源兼容新增）——实现 `IEquatable<AudioHandle>` 与 `==`/`!=`/`GetHashCode`，按 Id / Generation 比较；代际是身份的一部分（ABA 防线）；基线已同步，回归 `Handles_CompareByIdAndGeneration`。
- 已做：**音量刷新假 null、Sweep 的 break**（2026-10-20 · R3-AUD-002/003，本轮修复）——外部销毁的播放源上写 `volume` 会抛 MissingReference；当时 `Sweep`（现拆为 `AudioManagerImpl.SweepVoices` + `AudioSourceProvider.Poll`）的 `break` 让后面槽位不计入 active、其余死槽位轮不到。修法：刷新时跳过 `Source == null`；`break` 改 `continue`。
- 已做：**全局暂停期间不回收在播音效（AudioListener.pause）**（2026-10-18 · 探针 + 本提交）——探针实测 `AudioListener.pause = true` 会让 `AudioSource.isPlaying` 变 false；`SweepVoices` 与 `AudioSourceProvider.Poll` 的 `Playing` / `Starting` 两态都识别全局暂停（暂停期间不回收、不计超时拍），恢复后照常判定（AUD-013）。
- 已做：**FMOD 自动安装失败回滚按「本次成功加载的 bank」记账**（2026-11-08 · `da815828` / `0f0b8d60`）——安装开始时快照并去重 `Banks`，`loadedBanks` 只记录本次成功加载的 bank；失败点之后出现的项目预加载 bank 不再被误卸载，重复条目不再导致回滚漏卸；`backendFactory == null` 检查前移到全部早退之前。回归 `FmodInstallerTests` 五条新用例（修复前红）。
- 已做：**FMOD provider/backend 失败路径收紧**（2026-11-08 · `1501fb30` / `176e5cc7` / `f81b76fc`）——`PlayAsync` 对 null/空白 location 返回 default 且不占槽；`BuildEventPath` 移入 try；backend 的 bus 三方法在 Dispose 后返回 false；`SafeStopAndRelease` 的 `isValid()` 纳入异常隔离；删除未使用的 internal `FmodProviderOptions.Default`。
- 已做：**FMOD 包内守卫射程加宽**（2026-11-08 · `58631415` / `d73d5f32` / `e2e6a816` / `3d624463`）——公开类型正则覆盖修饰符/record/delegate；asmdef 绑定 `versionDefines.define` 与 `defineConstraints` 同值并覆盖 Tests asmdef；README 精确包名/版本；Resources 全目录扫描；README 文件树按相对路径；`.meta` 根仅 Assets 布局；AutoInit 属性正则；包边界全文本扫描。另补 `UnloadBank` 桥接用例（`6061a78a`）。
- 暂不做：**不改核心对 `SetPaused` / `SetVolume` 返回 false 的回收行为**（2026-11-08 · 用户裁定）——核心把 false 当作音源已消失并回收槽位；FMOD backend 在任意 `RESULT != OK` 时也返回 false，语义更宽，瞬时失败可能提前回收 voice。本轮只写包 README 已知限制与 `FMOD.md`。重开判据：真实 FMOD 调用出现可复现的瞬时 false 导致误回收。
- 已做：**明确显式 `AudioManager.UnloadBank` 不受 `OwnsBanks` 限制**（2026-11-08 · 文档订正）——「项目预加载的 bank 永不卸载」只适用于自动安装失败清理与 provider Dispose；包 README 与 `FMOD.md` 已写明。
- 暂不做：**FMOD provider/backend 的主线程断言**（2026-11-08）——README 声明全部成员限主线程，但代码没有断言；核心 Audio 也没有同类断言，扩展包单独加会制造不一致。重开判据：出现跨线程误用的真实事故，或核心 Audio 统一加上主线程断言。
- 未做：**预加载 bank 的 `LoadBankSamples` 补载**（2026-11-08）——若项目已加载 bank 且当时未加载采样数据，配置里的 `LoadBankSamples=true` 不会改变它；`FmodEventBackend.LoadBank` 对已加载 bank 提前返回。需要真实 bank 证据再决定是否补载。

## 与其它模块的边界（下一轮审计时先看这里）

- **对 `XAsset` 的接触只有两处**：`AudioClipLease.cs` 与 `AudioClipLoader.cs`。审计时若发现第三处，那是边界泄漏。
- **不依赖 Settings / Localization**（两个可选模块），README 只给接线片段。
- **不用 `PoolManager`**：那套池只服务纯 C# 对象。播放源池是模块自持的第三套（Asset 侧预设体池是第二套）。
