# FMOD —— 评估与适配器设计（维护侧）

> **收件人**：框架维护者（含「下一轮要不要重新论证这件事」的回答者）。**不是**随包发布的使用文档。
> **使用方契约**在 `Assets/XFramework/Runtime/Audio/README.md`；本文件回答的是**框架侧要不要动**，
> 以及**一个项目若仍要用 FMOD，适配器该怎么设计、坑在哪**。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[官方文档]` | 来自 FMOD 官方用户指南 / 官方问答的表述；本次经搜索结果摘要核对，**官方页面为动态渲染、未能直接抓取正文** |
| `[本仓核对]` | 读本仓文件得出，可当场复现 |
| `[二手资料]` | 社区仓库 / 问答 / 教程，不是 FMOD 官方文档 |
| `[未实测]` | 只有文档或推理支撑，**没有在 Unity 里编译或跑过** |

**核对基准**：FMOD for Unity **2.03**（官方用户指南）；手动 UPM 化的事实来自 FMOD Q&A（2.02.20+）。
**本机未安装 FMOD SDK，没有跑过任何探针。** 本文件的 API 名称用于设计对照，落实现前必须按实际安装版本复核。

---

## 一、结论：框架侧零改动

**不建官方 FMOD 扩展包；不新增 `IAudioProvider` 分层缝；不改核心公开 API。**

若一个项目确实要用 FMOD，唯一与既有裁定一致的路径是：

1. 在**项目自己的程序集**里实现 `IAudioManager`（公开面后端中立，`location` 可以解释成 FMOD 事件路径）；
2. `AudioManager.Destroy()`（若之前已 `Initialize`）→ `AudioManager.SetInstance(new FmodAudioManager(options))`；
3. FMOD 专属能力（事件参数、RTPC、bus、bank、3D 属性）留在项目侧或适配器自己的 API 上，不进入 `IAudioManager`。

三条理由，按重要性排列：

1. **`IAudioManager` 是整体替换点，不是分层缝**。`Documentation/Modules/Audio.md:60` 已记录「Audio 属整体替换，
   不做 `IAudioProvider` 分层缝」，重开判据是「手上有真实中间件项目（有 API 参考即可设计）」。
   本项目目前没有已落地的 FMOD 工程，按判据不重开。
2. **官方包在本仓无法验证**。FMOD 的 Unity 集成不是官方 UPM 源（§六），SDK 不可随包再分发；
   一个 `Assets/XFramework.Fmod/` 会让本仓在未装 FMOD 的机器上编译失败，且无法进六道门禁。
   Luban 扩展包能成立，是因为它在 UPM 上、本仓能跑测试与包内守卫；FMOD 不具备这个前提。
3. **门面能表达的东西远少于 FMOD 的核心**。`IAudioManager` 只有 6 个播放参数 + 三档音量；
   FMOD 的项目侧价值在 event 参数、RTPC、bus、snapshot、bank 生命周期、3D 属性——这些都必须绕过门面。

---

## 二、它是什么 / 与 Audio 门面的映射

### 2.1 对象模型对照 `[官方文档/二手资料，未实测]`

| FMOD | 本仓 `XAudio` | 可否一一对应 |
| --- | --- | --- |
| Studio event（`event:/...` 路径） | `location` 字符串 | **可**：`location` 直接解释成事件路径 |
| `EventInstance` | `AudioHandle` + 内部槽位 | **可**：槽位表持有实例，句柄存槽位索引 + 代际 |
| event 参数 / RTPC | 无 | **不可**：`AudioPlayOptions` 只有 `VolumeScale` / `Loop` / `Pitch` / `SpatialBlend` / `Position` |
| bus（`bus:/...`） | 通道（字符串）+ 三档音量 | **部分**：通道可映射到 bus，但 bus 的嵌套 / snapshot / 效果链不在门面里 |
| bank | 无 | **不可**：bank 加载是适配器 options 或项目侧的事 |
| `EventInstance.set3DAttributes` | `SpatialBlend` + `Position` | **部分**：只能写入一次位置；衰减曲线、跟随、遮挡由事件编排 |
| 事件内建的循环 / 参数 | `Loop` | **部分**：`Loop` 不能强制改写事件编排 |

### 2.2 `IAudioManager` 成员逐条映射 `[本仓核对]`

接口源码：`Assets/XFramework/Runtime/Audio/IAudioManager.cs:29-172`；门面：`AudioManager.cs`。

| 接口成员 | FMOD 侧设计 | 备注 |
| --- | --- | --- |
| `Play(location, options)` | 预留槽位 → `RuntimeManager.CreateInstance(path)` → 写参数 → `start()` → 返回句柄 | 句柄在**预占槽位时**返回；实例创建失败则句柄静默失效（对齐内置实现的失败契约） |
| `PlayAsync(...)` | 等 bank 就绪 / 实例创建完成后返回；失败返回 `default(AudioHandle)` | bank 可用性用 `RuntimeManager.HasBankLoaded` 或项目提供的委托判断 |
| `Stop(handle)` | 校验代际 → `EventInstance.stop(STOP_MODE.ALLOWFADEOUT)` → `release()` | 探测型：未知/过期句柄返回 `false`，不抛 |
| `StopAll(channel)` | 遍历槽位表，按通道过滤后停止 | 返回实际停止数 |
| `Pause()` / `Resume()` | 管理器闸门 + 对每个在播实例 `setPaused(true/false)` | 暂停期间新起的播放也要立即暂停 |
| `IsPaused` | 适配器自己的闸门状态 | 不是 FMOD 全局状态 |
| `MasterVolume` / `MasterMuted` | 映射到 master bus（`bus:/`）或每个实例的 `setVolume` | 建议走 master bus，避免逐实例扫描 |
| `SetChannelVolume` / `SetChannelMuted` | 通道 → bus 的映射表（options 配置） | 未映射的通道退回逐实例 `setVolume` |
| `GetChannelVolume` / `IsChannelMuted` | 适配器自己的通道状态表 | 查询不建状态、未知通道返回 `1` / `false` |
| `RegisterChannel` | 写入通道状态表 | 调用时快照；保留名 `"master"` 拒绝 |
| `IsPlaying(handle)` | 槽位有效 && 代际匹配 && `isValid()` && 未 stopped | 暂停中的算「在播」；`getPaused` 可用于区分 |
| `ActiveVoiceCount` | 槽位表活跃数（含加载中 / 暂停） | — |
| `GetActiveVoiceCount(channel)` | 按通道过滤槽位表 | 未声明通道返回 `0` |
| `Dispose()` | 停止全部、`release()` 每个实例、卸载自持 bank | 幂等；不要重复订阅 `Application.quitting` |
| `CopyChannels`（**门面专属，不在接口**） | **无法实现**：门面只对 `AudioManagerImpl` 探测 | 注入 FMOD 后诊断面板通道表恒空（§4.7） |

### 2.3 门面表达不了、且不应硬塞进去的东西

- event 参数 / RTPC / switch / state / snapshot；
- bank 的加载顺序与生命周期；
- bus 的嵌套、效果链、snapshot 过渡；
- 3D 发射器的跟随、遮挡、衰减曲线；
- 事件回调（`TIMELINE_BEAT`、`MARKER`、`SOUND_PLAYED` 等）。

这些属于 FMOD 的领域知识，**放进 `IAudioManager` 会把「引擎中立」这条底线拆掉**；适配器应把它们留在自己的 API。
这也是 `Documentation/Modules/Audio.md:59` 否决 `Play(AudioClip)` 的同一条理由。

---

## 三、两种方案对比

| 维度 | A. 项目内实现 `IAudioManager`（整体替换） | B. 核心新增 `IAudioProvider`（分层缝） |
| --- | --- | --- |
| 核心改动 | **零** | 公开 API + 基线 + CHANGELOG；拆 `AudioManagerImpl`；改 `CopyChannels` 探测 |
| 与既有裁定 | **一致**（Audio README 已推荐；Input→Rewired 同型） | **冲突**（Audio.md:60 暂不做） |
| 框架语义（通道 / 三档音量 / 暂停闸门 / 归一化 / 保留名 / 计数） | **每个适配器重写** | **框架保留** |
| 中间件能力表达 | 6 个播放参数 + 三档音量；其余在适配器外 | 同样受限；要表达 RTPC/参数就得让 provider 泄漏 FMOD 类型 |
| 诊断回读 | `CopyChannels` 恒空；`AudioChannelInfo` 构造 internal | 可保留，但需新增能力探测并改核心 |
| 本仓可验证性 | 无 SDK 不能编译/测试 | 同左；核心改动还要过全量门禁 |
| 维护成本 | 一套适配器 + FMOD 版本矩阵 | 一条核心缝 + 适配器（更多） |
| 失败模式 | 语义漂移：预占句柄、`MaxVoices`、`ActiveVoiceCount` 在 FMOD 上没有天然对应 | **最小公分母**：抽象先于实现 |

**结论：A 优先于 B。** 若真要做，先在项目内做 A；等两家（FMOD / Wwise）的真实适配器都出现，
再评估是否抽 B，且只抽**重叠面**，不抽并集。现在做 B 等于按想象设计一条要同时容纳两家的缝——
`Documentation/Modules/Audio.md:60` 明确拒绝的正是这一点。

---

## 四、若做 A：适配器设计（只写设计，不写代码）

### 4.1 安装与生命周期

- 适配器的公开入口建议为 `FmodAudioManager.Install(FmodAudioOptions options)`：
  `AudioManager.Destroy()`（若已初始化）→ 构造实例 → `AudioManager.SetInstance(instance)`。
- **不要**自己订阅 `Application.quitting`：门面的 `AutoInit` 已经订阅并会调用 `Destroy()`（`AudioManager.cs:416-424`）；
  适配器的 `Dispose` 必须幂等，承受「门面销毁 + 项目手动销毁」两种顺序。
- 适配器自持的 bank / 事件实例由适配器释放；**不要**卸载项目自己加载的 bank（options 有 `OwnsBanks` 之类的开关）。
- 日志走 `XFramework.XLog` 的 `Audio` 分类；不写 `[Audio]` 前缀（分类渲染层负责）。
- 全部成员主线程；FMOD 的回调如果落在非主线程，适配器要自行切回（本设计不承诺线程安全）。

### 4.2 句柄

- `AudioHandle.Id` 放**适配器内部槽位表索引**，`Generation` 放槽位代际（从 1 起、递增时跳过 0）。
- **不要把 `EventInstance` 或 FMOD 的原生句柄直接塞进 `int`**：`EventInstance` 是结构体、内部句柄宽度随平台变化，
  且实例有独立生命周期；槽位索引 + 代际是唯一能同时满足「可复制、零 GC、ABA 安全」的编码。
- `default(AudioHandle)`（`Generation == 0`）必须视为无效；`Stop` / `IsPlaying` 对未知/过期句柄返回 `false`，不抛。

### 4.3 通道与音量

- 字符串通道 + **序数比较**（区分大小写）+ `"master"` 保留名 + 惰性状态；
- 实际音量 = 主音量 × 通道音量 × 单次 `VolumeScale`，任一档静音即 0，最终钳 `[0,1]`；
- 推荐映射：`MasterVolume` → master bus（`bus:/`）的 `setVolume`；通道 → bus 的 `setVolume`；
  `VolumeScale` → `EventInstance.setVolume`。三档相乘仍由适配器计算，避免 FMOD 侧再乘一遍。
- `AudioPlayOptions.Loop` / `Pitch` / `SpatialBlend`：`Pitch` → `setPitch`；`SpatialBlend > 0` 时写一次
  `set3DAttributes`；`Loop` **不能强制改写事件编排**，适配器应在文档里明确「以事件编排为准」。

### 4.4 播放、停止、暂停

- `Play`：预留槽位（池满返回 `default` + 限流告警）→ 创建 `EventInstance` → 写音量/音高/3D → `start()`；
  创建失败让句柄静默失效（与内置实现的 `Play` 契约一致）。
- `PlayAsync`：在 `start()` 成功且 bank 可用后返回；失败返回 `default(AudioHandle)`；取消时释放槽位与实例。
- `Stop`：`stop(STOP_MODE.ALLOWFADEOUT)` → `release()` → 归还槽位；重复调用幂等。
- `StopAll(channel)`：遍历槽位表；`channel == null` 停全部；返回实际停止数。
- `Pause` / `Resume`：管理器闸门 + 逐实例 `setPaused`；暂停期间新起的播放要立即 `setPaused(true)`；
  暂停中的播放不回收、仍计入 `ActiveVoiceCount`、`IsPlaying` 返回 `true`。

### 4.5 查询与诊断

- `IsPlaying`：槽位有效 && 代际匹配 && 实例 `isValid()` && 未 stopped；暂停中的算在播。
- `ActiveVoiceCount` / `GetActiveVoiceCount`：槽位表计数，含「创建中 / 暂停 / 在播」。
- **`CopyChannels` 无法工作**：门面只对 `AudioManagerImpl` 探测（`AudioManager.cs:361-371`）。
  适配器如果需要通道回读，只能在自己的 API 上另开一份；内置诊断面板的通道表会空白。
- 可选：适配器自己的诊断快照（事件路径 / 通道 / 状态 / 是否暂停），形状自定，不进入核心。

### 4.6 options（建议字段）

| 字段 | 用途 |
| --- | --- |
| `EventPathPrefix` | `location` → FMOD 事件路径的前缀（如 `event:/`） |
| `ChannelToBus` | 通道名 → bus 路径的映射；未映射的通道走逐实例音量 |
| `MasterBusPath` | 主音量映射的 bus，默认 `bus:/` |
| `Banks` | 适配器负责加载的 bank 列表 |
| `OwnsBanks` | 是否在 `Dispose` 时卸载这些 bank |
| `BankReady` | 可选委托：判断某个事件依赖的 bank 是否就绪（`PlayAsync` 用） |

### 4.7 核心侧硬缺口（不要误以为适配器能全做）

1. `AudioManager.CopyChannels` 内部 `is AudioManagerImpl` 类型探测 → 注入 FMOD 后恒返回 0；
2. `AudioChannelInfo` 构造函数是 `internal` → 扩展程序集无法构造这个公开类型；
3. `Initialize(AudioInitOptions)` 永远构造内置实现 → 适配器只能自带 options + `SetInstance`；
4. `AudioListener.pause` / `AudioListener.volume` 对 FMOD 后端不自动生效，项目暂停必须走 `AudioManager.Pause()`；
5. 无 fade / 优先级抢占 / Mixer 路由——仍只能在适配器自己的 API 或项目侧。

这五条要修都必须动核心公开面，**不在本文件授权范围内**；要做另立条目。

---

## 五、若做 B：为什么现在不做

- `Audio.md:60` 的判据是「替换轴只有一条且贯穿全模块 → 整体替换」。Audio 的替换轴确实是「整套引擎」，
  不是按平台/能力切分；全仓唯一的分层 Provider 缝是 File。
- provider 缝要覆盖 `Play/Stop/Pause/Volume/Position` 五类原语，而 FMOD 的 loop / 3D 衰减 / 参数 / bank
  都不在这些原语里。缝要么只覆盖最小公分母（价值有限），要么长出 FMOD 专属成员（不再是中立抽象）。
- 正确顺序：**先有 A 的两家真实实现，再抽重叠面**。没有两个真实适配器之前，B 的设计只能靠猜。

---

## 六、分发、许可与门禁

- **官方安装形态**：Asset Store 或 `.unitypackage`；`[官方文档]`
- **UPM**：FMOD 没有官方 UPM 源；**2.02.20+ 可手动 UPM 化**——把集成目录里的 `FMOD` 文件夹与 `FMOD.meta`
  一起搬进带 `package.json` 的目录，保留 `FMOD.meta` 的 GUID（FMOD 靠它定位基目录），再从磁盘或自建 Git 导入。`[二手资料]`
- **程序集**：集成目录包含 `FMODUnity.asmdef`（`"name": "FMODUnity"`）。`[二手资料]` 官方文档未列程序集名，以实际版本为准。
- **不可再分发**：FMOD 的集成源码与原生库有许可证约束，不能随本仓的包发布；消费方必须自己安装 SDK。
- **本仓后果**：任何硬引用 `FMODUnity` 的源码都不能进 `Assets/XFramework/` 或本仓的常驻编译树；
  一个独立扩展包也会让未装 FMOD 的机器编译失败，且无法进六道门禁。

---

## 七、重开判据与验证方式

三条**都**成立才值得重新评估（前两条是台账判据的展开）：

1. **有真实 FMOD 项目**，且 FMOD SDK 可在本仓或 CI 编译验证（不只是「可能要用」）；
2. 至少一个适配器已在真实项目跑通：真实 bank、真实事件参数、至少一个目标平台真机；
3. 两家（FMOD / Wwise）的真实适配器都出现后，才评估是否抽 `IAudioProvider`——届时另立条目，
   不直接改本文件的结论。

验证方式（将来执行时）：一次性探针工程 → 装 FMOD 集成 → 实现最小适配器 → 跑 bank / 事件 / 3D / 暂停 / 销毁
五条路径；探针跑完即弃，不纳入本仓常驻测试（与 `Documentation/FairyGUI.md:249-251` 同一取向）。

---

## 八、已核对事实与漂移点

| 事实 | 状态 | 漂移点 |
| --- | --- | --- |
| FMOD for Unity 官方支持 Unity 2021.3 / 2022.3 / 6.0 / 6.3 | `[官方文档]` | 版本矩阵随 FMOD 发布变化 |
| 官方安装 = Asset Store / `.unitypackage` + Setup Wizard | `[官方文档]` | 可能新增官方 UPM 源 |
| 2.02.20+ 可手动 UPM 化；必须保留 `FMOD.meta` GUID | `[二手资料]` | 上游后续版本可能改变目录定位方式 |
| 集成程序集名 `FMODUnity` | `[二手资料]` | 以实际安装版本为准 |
| `RuntimeManager.CreateInstance` / `EventInstance.start/stop/setPaused/setVolume/setPitch/set3DAttributes/getPlaybackState` | `[官方文档/二手资料，未实测]` | 2.02 / 2.03 签名可能微调 |
| bus：`RuntimeManager.GetBus`；bank：`LoadBank` / `HasBankLoaded` | `[官方文档/二手资料，未实测]` | 同上 |
| 事件循环由 Studio 编排决定，`AudioPlayOptions.Loop` 不能强制改写 | `[未实测]` | 需要真实事件验证 |

---

## 九、与既有结论的关系（下一轮别重提）

- **`Play(AudioClip)` 不再论证**：`Audio.md:59` / `Ledger.md:105` / `CHANGELOG.md` 已三次否决，
  理由是拆掉后端中立性与「资源生命周期只有一套语义」；无重开判据。
- **框架音量 → 项目 AudioMixer 路由不再论证**：`Audio.md:56` / R3-AUD-001 已实测 `outputAudioMixerGroup`
  恒 `null`，`AudioMixer.SetFloat` 对框架声音静默无效；结论是「要接入只能整体替换 `IAudioManager`」。
- **`IAudioProvider` 分层缝维持暂不做**：`Audio.md:60`；本文件 §三 / §五 是同一结论的展开。
- **不把 FMOD 类型带进核心公开面**：`location` 是唯一的资源标识；`AudioHandle` 只存两个整数。
  这条与 Input 的 `IInputProvider` 保持插件中立是同一条边界。
