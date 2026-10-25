# Audio —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Audio/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Audio/
├── IAudioManager.cs          # 公开接口 = 后端整体替换点（不含任何 Unity 内置音频类型）
├── AudioManager.cs           # 静态门面（扁平 + #region 分区） + AutoInit
├── AudioManagerImpl.cs       # 默认实现：槽位/通道/音量/暂停/加载编排
├── AudioHandle.cs            # 播放句柄（readonly struct，字段公开且对门面不透明）
├── AudioPlayOptions.cs       # 单次播放参数（六个字段全部后端中立）
├── AudioChannels.cs          # 推荐通道常量（不是枚举，见 README 设计取舍）
├── AudioChannelConfig.cs     # RegisterChannel 的配置（Volume / Muted）
├── AudioChannelInfo.cs       # 诊断回读载荷：CopyChannels 的列表元素（2026-10-16 补录——它一直是公开面，此前漏在本树之外）
├── AudioChannelState.cs      # 一个通道的音量状态（internal，惰性创建）
├── AudioInitOptions.cs       # 初始化选项
├── AudioVoice.cs             # 槽位记录 + AudioVoiceState 状态机（internal）
├── AudioSourcePool.cs        # 宿主与槽位、回收扫尾、池耗尽策略（internal）
├── AudioHost.cs              # 隐藏宿主 MonoBehaviour（internal，纯挂载点）
├── AudioTicker.cs            # IUpdateable：Tier1 + Unscaled 的回收驱动（internal）
├── AudioClipLease.cs         # 资源租约：本体 + 释放动作（internal）
├── IAudioClipLoader.cs       # 加载缝（internal，测试缝而非第二个后端替换点）
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

- 已做：**`Play()` 后同步读一次 `isPlaying`**（2026-10-17 · `248d473`，AUD-001）——短于一拍的 one-shot 不再卡在 `Starting` 到 60 拍超时并误报；实测（P-06）`Play()` 后同步读为真。

- 已做：**v1 功能面建成**（2026-10-01 · 使用方 README + `CHANGELOG`）——门面模板全套（`Initialize` / `SetInstance` / `Destroy` / `EnsureInitialized`）+ `AutoInit`；隐藏宿主 + 池化 `AudioSource` + 代际安全句柄 + 播完回收（含 `Starting` 超时兜底）；字符串通道 + 推荐常量 + 惰性通道状态 + `RegisterChannel`；location 播放路径（`Play` / `PlayAsync`）+ 全部失败/取消/竞态路径；三档音量相乘 + 变更即时生效 + 静音；`Stop` / `StopAll` / 手动 `Pause`/`Resume` / 查询与计数。原「已完成（v1）」清单即上述各项，功能说明与用法在使用方 README，逐条变更证据在 `CHANGELOG`。
- 未做：**并发抢占 / 优先级策略**（2026-10-01 · v2 候选）——需先答「抢谁」= 替使用方定混音策略；`AudioSource.priority` 的引擎语义需实测。
- 未做：**淡入淡出 / crossfade**（2026-10-01 · v2 候选）——需要每 voice 的淡变状态与时间源，且与 `Pause` 交互；自成一块。
- 未做：**切后台自动暂停**（2026-10-01 · v2 候选）——需「谁暂停的」状态机 + iOS/Android 真机实测（见使用方 README 设计取舍）。
- 未做：**借用项目提供的 `AudioSource`**（2026-10-01 · v2 候选）——音量归属 / `Stop` 语义 / 源销毁失效 / 借用期冲突四条语义，可独立提交。
- 不做：**ducking（音效压低 BGM）**（2026-10-01 · v2 候选）——混音设计理念，项目间差异大；Mixer 侧才是它的家。无重开判据。
- 暂不做：**框架音量 → 项目 Mixer 的驱动接口**（2026-10-01 · v2 候选）——先观察是否真有需求；~~现在给的是「框架音量留 1.0、Mixer 独占控制」~~（2026-10-19 订正：该理由不成立——框架动态声源的 `outputAudioMixerGroup` 恒 `null`，声音进不了项目 Mixer，`AudioMixer.SetFloat` 静默无效；结论不变：要接入项目 Mixer 只能整体替换 `IAudioManager`，公开面不新增路由入口。R3-AUD-001）。重开判据：出现真实需求，且接受新增「不暴露 Unity 类型的路由扩展点」。
- 暂不做：**`AudioBootstrapStage` / `AudioSettingsBridge<T>` / `AudioHandle.State` / 通道级上限 / `DumpState` 这批便利面**（2026-10-01 · v2 候选）——等真实反馈；`DumpState` 的诊断面形状另见下方 `待裁` 条。重开判据：出现真实使用反馈。
- 不做：**跟随 Transform 的 3D 发射器**（2026-10-01 · v2 候选）——本质是项目侧 MonoBehaviour。无重开判据。
- 不做：**不提供 `Play(AudioClip, ...)` 重载**（2026-10-01 · v1 评估）——它会同时毁掉两件事：`IAudioManager` 的后端中立性（Wwise 的事件 / FMOD 的 `EventInstance` 都没有 `AudioClip`），以及「资源生命周期只有一套语义」；「回不到 Inspector 直引」这条代价已写进 README 的已知限制。无重开判据。
- 暂不做：**不提供 `IAudioProvider` 分层引擎缝**（2026-10-01 · v1 评估）——本仓「后端可换」有两种形态，分界线是「替换轴只有一条且贯穿全模块 → 整体替换」（Input 换 Rewired / Save / Asset / Localization / Config / UI）vs「替换轴按平台或能力切分、需组合多个实现 → 分层 Provider 缝」（**全仓只有 File**）；Audio 属前者。**不盲做的理由是拿不到 Wwise/FMOD 的 API 形状**：Wwise 的播放单元是 Event + EventInstance、音量是 RTPC、3D 发射器要挂 GameObject，FMOD 是 `EventInstance`——与「clip + volume + transform.position」都不同构，凭记忆猜出来的缝大概率是谁都不合身的最小公约数。重开判据：手上有真实中间件项目（有 Wwise/FMOD 的 API 参考即可设计）。
- 暂不做：**不把 `IAudioClipLoader` 提升为公开面**（2026-10-01 · v1 评估）——与上一条同源（同一「整体替换 vs 分层缝」判据）；先观察是否真有「只换资源来源、不换播放引擎」的需求。重开判据：出现「只换资源来源、不换播放引擎」的真实需求。
- 已推翻：**~~不做只读的通道状态快照~~**（2026-10-01 · v1 评估）——原理由「`GetChannelVolume` / `IsChannelMuted` 已覆盖查询需求」**已被 2026-10-02 的 `CopyChannels` 推翻**：诊断回读面已落地（通道名 / 音量 / 静音 / 在播路数），只剩逐 voice 明细未做（见下方条目）。
- 不做：**框架内建「语音随语言切换」缺席**（2026-10-01 · v1 评估）——那要发明一套「语言 → 语音资源路径」的约定，而 `XLocalization` 本身只有语言表模板、没有「按 key 取资源路径」的概念；且「切语言时当前台词停在半句还是重播」是 GamePlay 时序决定。README 只给订阅 `LanguageChangedMessage` 的做法。无重开判据。
- 不做：**`Play` 与 `PlayAsync` 的句柄契约不对称**（2026-10-01 · v1 记录；2026-10-19 R3-ADJ-02 裁定为不做）——前者在预占槽位时就返回、加载失败只让句柄失效；后者在加载完成后才返回、失败返回 `default`。两条契约各自自洽、也已写进 README 的对照表，统一会改变 `Play` 的「预占即返回」时序。无重开判据。
- 未做：**`AudioSourcePool.StartingTimeoutTicks = 60`（约 2 秒）是自定值，真机未验证**（2026-10-01 · v1 记录）——兜底的是「某平台 `Play()` 之后 `isPlaying` 永不变真」这种情形；本仓只实测了批处理模式下的桌面行为，**真机（尤其移动端）未验证**。
- 未做：**`TryReserve` 的线性扫描**（2026-10-01 · v1 记录）——O(槽位数)，`MaxVoices` 默认 32；若将来允许把上限调到很大，需要换空闲链表。
- 未做：**门面没有逐 voice 明细回读**（2026-10-01 · v1 记录；2026-10-17 第二轮审计刷新）——`CopyChannels` 已提供通道级回读（见上方「已推翻」条）；剩余的是逐 voice 明细，会泄漏 internal 结构，形状未定。重开判据：出现需要逐 voice 诊断的真实使用方。

- 已做：**Free 槽坏源在原下标重建**（2026-10-25 · `b9b3a67f`，R4-AUD-001，本轮修复）——外部销毁宿主/播放源后 `Source` 为 Unity 伪 null，`TryReserve` 恒取该 Free 槽且不重建，每次播放都失败。修法：`AudioVoice` 记槽位 GameObject；命中坏源时补 `AudioSource` 或按原名重建 `Voice {n}`，保留旧代际后递增。回归 `AudioPoolTests.ExternallyDestroyedHost_NextPlayRebuildsSlotAndSucceeds` / `ExternallyDestroyedSource_NextPlayRebuildsSource`（改前第二次播放取不到播放源）。

## 与其它模块的边界（下一轮审计时先看这里）

- **对 `XAsset` 的接触只有两处**：`AudioClipLease.cs` 与 `AudioClipLoader.cs`。审计时若发现第三处，那是边界泄漏。
- **不依赖 Settings / Localization**（两个可选模块），README 只给接线片段。
- **不用 `PoolManager`**：那套池只服务纯 C# 对象。播放源池是模块自持的第三套（Asset 侧预设体池是第二套）。

- 已做：**`AudioHandle` 相等语义**（2026-10-20 · R3-AUD-004，公开 API 源兼容新增）——实现 `IEquatable<AudioHandle>` 与 `==`/`!=`/`GetHashCode`，按 Id / Generation 比较；代际是身份的一部分（ABA 防线）；基线已同步，回归 `Handles_CompareByIdAndGeneration`。
- 已做：**音量刷新假 null、Sweep 的 break**（2026-10-20 · R3-AUD-002/003，本轮修复）——外部销毁的播放源上写 `volume` 会抛 MissingReference；`Sweep` 的 `break` 让后面槽位不计入 active、其余死槽位轮不到。修法：刷新时跳过 `Source == null`；`break` 改 `continue`。
- 已做：**全局暂停期间不回收在播音效（AudioListener.pause）**（2026-10-18 · 探针 + 本提交）——探针实测 `AudioListener.pause = true` 会让 `AudioSource.isPlaying` 变 false；`Sweep` 的 `Playing` 与 `Starting` 两态都识别全局暂停（暂停期间不回收、不计超时拍），恢复后照常判定（AUD-013）。
