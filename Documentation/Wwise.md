# Wwise —— 评估与适配器设计（维护侧）

> **收件人**：框架维护者（含「下一轮要不要重新论证这件事」的回答者）。**不是**随包发布的使用文档。
> **使用方契约**在 `Assets/XFramework/Runtime/Audio/README.md`；本文件回答的是**框架侧要不要动**，
> 以及**一个项目若仍要用 Wwise，适配器该怎么设计、坑在哪**。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[官方文档]` | 来自 Audiokinetic 官方文档 / 许可声明的表述；本次经搜索结果摘要核对，**官方页面部分为动态渲染、未能直接抓取正文** |
| `[本仓核对]` | 读本仓文件得出，可当场复现 |
| `[二手资料]` | 社区文档 / 源码镜像 / 教程，不是 Audiokinetic 官方文档 |
| `[未实测]` | 只有文档或推理支撑，**没有在 Unity 里编译或跑过** |

**核对基准**：Wwise Unity Integration（2024/2025 代际的官方安装流程）与 `WwiseUnityAddressables`
官方文档。**本机未安装 Wwise SDK，没有跑过任何探针。** 本文件的 API 名称用于设计对照，落实现前必须按实际安装版本复核。

---

## 一、结论：框架侧零改动

**不建官方 Wwise 扩展包；不新增 `IAudioProvider` 分层缝；不改核心公开 API。**

若一个项目确实要用 Wwise，唯一与既有裁定一致的路径是：

1. 在**项目自己的程序集**里实现 `IAudioManager`（公开面后端中立，`location` 可以解释成 Wwise Event 名）；
2. `AudioManager.Destroy()`（若之前已 `Initialize`）→ `AudioManager.SetInstance(new WwiseAudioManager(options))`；
3. Wwise 专属能力（RTPC、switch/state、snapshot、bank、3D 发射器、事件回调）留在项目侧或适配器自己的 API 上。

三条理由与 FMOD 同源（见 `Documentation/FMOD.md` §一），Wwise 侧还多一条更硬的事实：

- **Wwise 集成源码的许可证明确限制再分发**：`[官方文档/许可声明]` 源码头部写着
  「The content of this file includes portions of the proprietary AUDIOKINETIC Wwise Technology
  released in source code form as part of the game integration package. The content of this file may
  not be used without valid licenses to the AUDIOKINETIC Wwise Technology.」
  ⇒ 本仓既不能把 Wwise 集成放进仓库，也不能把它作为扩展包的依赖分发。
- **Wwise 的播放模型和 `IAudioManager` 差异最大**：播放单元是 Event + PlayingID，3D 需要注册的
  GameObject，音量通常走 RTPC / bus，没有「按 PlayingID 查询是否在播」的直接 API（§2.2）。
  这正是 `Audio.md:60` 所说「凭记忆猜出来的缝大概率是谁都不合身的最小公约数」的典型。

---

## 二、它是什么 / 与 Audio 门面的映射

### 2.1 对象模型对照 `[官方文档/二手资料，未实测]`

| Wwise | 本仓 `XAudio` | 可否一一对应 |
| --- | --- | --- |
| Event（名字 / id） | `location` 字符串 | **可**：`location` 直接解释成 Event 名 |
| `PlayingID`（`uint`） | `AudioHandle` + 内部槽位 | **部分**：`uint` 不能无损塞进 `int`；用槽位索引 + 代际 |
| 注册的 GameObject（emitter） | 无 | **不可**：适配器必须自建隐藏宿主并注册 `AkGameObject` |
| RTPC / switch / state | 无 | **不可**：`AudioPlayOptions` 只有 6 个字段 |
| bank | 无 | **不可**：bank 加载是适配器 options 或项目侧的事 |
| bus / snapshot | 通道（字符串）+ 三档音量 | **部分**：通道可映射到 bus，snapshot 不在门面里 |
| `AK_EndOfEvent` 回调 | 播完回收 | **必须**：适配器靠它维护槽位表（§2.2） |

### 2.2 `IAudioManager` 成员逐条映射 `[本仓核对]`

接口源码：`Assets/XFramework/Runtime/Audio/IAudioManager.cs:29-172`；门面：`AudioManager.cs`。

| 接口成员 | Wwise 侧设计 | 备注 |
| --- | --- | --- |
| `Play(location, options)` | 预留槽位 → `AkSoundEngine.PostEvent(eventName, emitterGo)` → 返回句柄 | 注册回调 `AK_EndOfEvent`，用于回收槽位；`PostEvent` 失败（返回 `AK_INVALID_PLAYING_ID`）时句柄静默失效 |
| `PlayAsync(...)` | 等 bank 就绪 / `PostEvent` 成功后返回；失败返回 `default` | bank 就绪判断由 options 的委托或 `LoadBank` 完成结果提供 |
| `Stop(handle)` | 校验代际 → `AkSoundEngine.StopPlayingID(playingID, transitionMs, curve)` → 归还槽位 | 探测型：未知/过期句柄返回 `false` |
| `StopAll(channel)` | 遍历槽位表，按通道过滤后停止 | 返回实际停止数 |
| `Pause()` / `Resume()` | 管理器闸门 + 对每个 PlayingID `ExecuteActionOnPlayingID(Pause/Resume, ...)` | 暂停期间新起的播放也要立即暂停 |
| `IsPaused` | 适配器自己的闸门状态 | 不是 Wwise 全局状态 |
| `MasterVolume` / `MasterMuted` | 映射到 master bus 的 RTPC / `SetRTPCValue` | 具体 bus 名与 RTPC 名由 options 配置 |
| `SetChannelVolume` / `SetChannelMuted` | 通道 → bus / RTPC 的映射表 | 未映射的通道退回逐 emitter 的 `SetGameObjectOutputBusVolume` |
| `GetChannelVolume` / `IsChannelMuted` | 适配器自己的通道状态表 | 查询不建状态、未知通道返回 `1` / `false` |
| `RegisterChannel` | 写入通道状态表 | 调用时快照；保留名 `"master"` 拒绝 |
| `IsPlaying(handle)` | 槽位有效 && 代际匹配 && 未收到 EndOfEvent && 未 Stop | **没有直接查询 API**（§2.3），只能靠回调维护的表 |
| `ActiveVoiceCount` | 槽位表活跃数（含暂停 / 启动中） | — |
| `GetActiveVoiceCount(channel)` | 按通道过滤槽位表 | 未声明通道返回 `0` |
| `Dispose()` | 停止全部、注销 GameObjects、卸载自持 bank | 幂等；不要重复初始化 `AkInitializer` |
| `CopyChannels`（**门面专属，不在接口**） | **无法实现**：门面只对 `AudioManagerImpl` 探测 | 注入 Wwise 后诊断面板通道表恒空（§4.7） |

### 2.3 与 FMOD 相比多出来的两个硬差异

1. **没有按 PlayingID 的「是否在播」查询**。`[二手资料，未实测]` Wwise 的常见做法是用
   `AK_EndOfEvent` 回调把结束事件写回表；`IsPlaying` 只能回答「表里还有没有这个槽位」。
   若回调丢失（例如 bank 卸载、引擎未初始化），槽位可能泄漏——适配器需要一个兜底巡检（定时或
   `StopAll` 时清理），并明确这不是框架级保证。
2. **3D 需要 GameObject**。`PostEvent` 的 GameObject 重载要求 emitter 已注册（`AkSoundEngine.RegisterGameObject`
   或 `AkGameObject` 组件）；适配器要自建一个 `DontDestroyOnLoad` 的隐藏宿主，按槽位或按通道分配 emitter。
   这与内置实现「动态创建 `AudioSource`」同型，但**不能复用** `AudioSourcePool`（它是 internal 且绑死 `AudioSource`）。

---

## 三、两种方案对比

| 维度 | A. 项目内实现 `IAudioManager`（整体替换） | B. 核心新增 `IAudioProvider`（分层缝） |
| --- | --- | --- |
| 核心改动 | **零** | 公开 API + 基线 + CHANGELOG；拆 `AudioManagerImpl`；改 `CopyChannels` 探测 |
| 与既有裁定 | **一致**（Audio README 已推荐；Input→Rewired 同型） | **冲突**（Audio.md:60 暂不做） |
| 框架语义（通道 / 三档音量 / 暂停闸门 / 归一化 / 保留名 / 计数） | **每个适配器重写** | **框架保留** |
| 中间件能力表达 | 6 个播放参数 + 三档音量；其余在适配器外 | 同样受限；要表达 RTPC/switch/state 就得让 provider 泄漏 Wwise 类型 |
| 诊断回读 | `CopyChannels` 恒空；`AudioChannelInfo` 构造 internal | 可保留，但需新增能力探测并改核心 |
| 本仓可验证性 | 无 SDK 不能编译/测试 | 同左；核心改动还要过全量门禁 |
| 维护成本 | 一套适配器 + Wwise 版本矩阵（2021/2022/2023/2024…） | 一条核心缝 + 适配器（更多） |
| 失败模式 | 语义漂移：`uint PlayingID`、回调驱动的在播判定、3D emitter 在门面里没有对应 | **最小公分母**：抽象先于实现 |

**结论：A 优先于 B。** 若真要做，先在项目内做 A；等两家（ FMOD / Wwise）的真实适配器都出现，
再评估是否抽 B，且只抽**重叠面**，不抽并集。Wwise 的差异比 FMOD 更大，先抽 B 的风险也更高。

---

## 四、若做 A：适配器设计（只写设计，不写代码）

### 4.1 安装与生命周期

- 公开入口建议为 `WwiseAudioManager.Install(WwiseAudioOptions options)`：
  `AudioManager.Destroy()`（若已初始化）→ 构造实例 → `AudioManager.SetInstance(instance)`。
- **不要**重复 `AkInitializer` / `AkWwiseInitializer` 的职责：Wwise 的引擎初始化由集成包负责；
  适配器只负责「事件播放」这一层，并在 `Dispose` 里停止/注销自己创建的东西。
- **不要**自己订阅 `Application.quitting`：门面 `AutoInit` 已订阅并调用 `Destroy()`；
  适配器 `Dispose` 必须幂等，承受「门面销毁 + 项目手动销毁」两种顺序。
- 适配器自持的 bank / emitter 由适配器释放；**不要**卸载项目自己加载的 bank（options 有 `OwnsBanks` 之类的开关）。
- 日志走 `XFramework.XLog` 的 `Audio` 分类；全部成员主线程；Wwise 回调若不在主线程，适配器要自行切回。

### 4.2 句柄

- `AudioHandle.Id` 放**适配器内部槽位表索引**，`Generation` 放槽位代际（从 1 起、递增时跳过 0）。
- **不要把 `uint PlayingID` 直接塞进 `int`**：会溢出且不能表达代际；`PlayingID` 存在槽位记录里。
- `default(AudioHandle)`（`Generation == 0`）必须视为无效；`Stop` / `IsPlaying` 对未知/过期句柄返回 `false`，不抛。

### 4.3 通道与音量

- 字符串通道 + **序数比较** + `"master"` 保留名 + 惰性状态；
- 实际音量 = 主音量 × 通道音量 × 单次 `VolumeScale`，任一档静音即 0，最终钳 `[0,1]`；
- 推荐映射：`MasterVolume` → master bus 的 RTPC；通道 → 目标 bus 的 RTPC；
  `VolumeScale` → 逐 emitter 的 `SetGameObjectOutputBusVolume`（Wwise 侧没有统一的「实例音量」，
  这一层需要按实际版本选择 RTPC / bus volume，`[未实测]`）。
- `AudioPlayOptions.Loop` / `Pitch` / `SpatialBlend`：`Loop` 与 `Pitch` 通常由事件与 RTPC 编排，
  **不能强制改写**；`SpatialBlend > 0` 时把位置写到 emitter，2D 事件会忽略它。适配器文档要明确「以事件编排为准」。

### 4.4 播放、停止、暂停

- `Play`：预留槽位（池满返回 `default` + 限流告警）→ `PostEvent(eventName, emitterGo, callback, cookie)` →
  记录 `PlayingID` → 返回句柄；`PostEvent` 失败（`AK_INVALID_PLAYING_ID`）时句柄静默失效。
- `PlayAsync`：在 bank 就绪且 `PostEvent` 成功后返回；失败返回 `default`；取消时停止并归还槽位。
- `Stop`：`StopPlayingID(playingID, transitionMs, curve)` → 归还槽位；重复调用幂等。
- `StopAll(channel)`：遍历槽位表；`channel == null` 停全部；返回实际停止数。
- `Pause` / `Resume`：管理器闸门 + 逐 PlayingID `ExecuteActionOnPlayingID(Pause/Resume)`；
  暂停期间新起的播放立即暂停；暂停中的播放不回收、仍计入 `ActiveVoiceCount`、`IsPlaying` 返回 `true`。
- `AK_EndOfEvent` 回调：按 `cookie` 找到槽位 → 归还槽位；对未知 / 已归还的 PlayingID 忽略。

### 4.5 查询与诊断

- `IsPlaying`：槽位有效 && 代际匹配 && 未收到 EndOfEvent && 未被 `Stop`；暂停中的算在播。
- `ActiveVoiceCount` / `GetActiveVoiceCount`：槽位表计数，含「启动中 / 暂停 / 在播」。
- **`CopyChannels` 无法工作**：门面只对 `AudioManagerImpl` 探测（`AudioManager.cs:361-371`）；
  `AudioChannelInfo` 构造函数是 `internal`，扩展程序集也造不出来。内置诊断面板的通道表会空白。
- 可选：适配器自己的诊断快照（Event 名 / 通道 / PlayingID / 状态 / 是否暂停），形状自定，不进入核心。

### 4.6 options（建议字段）

| 字段 | 用途 |
| --- | --- |
| `EventNamePrefix` | `location` → Wwise Event 名的前缀（如 `Play_`） |
| `ChannelToBus` | 通道名 → bus / RTPC 的映射；未映射的通道走逐 emitter 音量 |
| `MasterBusName` | 主音量映射的 bus，默认 `Master Audio Bus` |
| `Banks` | 适配器负责加载的 bank 列表 |
| `OwnsBanks` | 是否在 `Dispose` 时卸载这些 bank |
| `BankReady` | 可选委托：判断某个 Event 依赖的 bank 是否就绪（`PlayAsync` 用） |
| `EmitterRoot` | 可选：emitter 宿主的父节点；默认自建 `DontDestroyOnLoad` 宿主 |

### 4.7 核心侧硬缺口（不要误以为适配器能全做）

1. `AudioManager.CopyChannels` 内部 `is AudioManagerImpl` 类型探测 → 注入 Wwise 后恒返回 0；
2. `AudioChannelInfo` 构造函数是 `internal` → 扩展程序集无法构造这个公开类型；
3. `Initialize(AudioInitOptions)` 永远构造内置实现 → 适配器只能自带 options + `SetInstance`；
4. `AudioListener.pause` / `AudioListener.volume` 对 Wwise 后端不自动生效，项目暂停必须走 `AudioManager.Pause()`；
5. 无 fade / 优先级抢占 / Mixer 路由——仍只能在适配器自己的 API 或项目侧；
6. Wwise 的 `AkInitializer` 生命周期在核心之外，适配器不能假设「门面初始化 = 音频引擎已就绪」。

这六条要修都必须动核心公开面，**不在本文件授权范围内**；要做另立条目。

---

## 五、若做 B：为什么现在不做

- `Audio.md:60` 的判据是「替换轴只有一条且贯穿全模块 → 整体替换」。Audio 的替换轴确实是「整套引擎」，
  不是按平台/能力切分；全仓唯一的分层 Provider 缝是 File。
- Wwise 的差异比 FMOD 更大：`PlayingID` 是 `uint`、3D 要 GameObject、音量走 RTPC、在播判定靠回调。
  一条 provider 缝要同时容纳这些与 FMOD 的 `EventInstance` / bus / `set3DAttributes`，几乎必然要么
  只覆盖最小公分母（价值有限），要么长出 Wwise 专属成员（不再是中立抽象）。
- 正确顺序：**先有 A 的两家真实实现，再抽重叠面**。没有两个真实适配器之前，B 的设计只能靠猜。

---

## 六、分发、许可与门禁

- **官方安装形态**：Wwise Launcher → Integrate Wwise in Project（`.unitypackage`；或下载离线包解压进 `Assets/`）。`[官方文档]`
- **UPM**：核心集成**不是**官方 UPM 包；只有 `WwiseUnityAddressables` 附属包提供官方 Git URL UPM 安装。`[官方文档]`
- **程序集**：集成源码位于 `Assets/Wwise/API/...`；程序集名 / asmdef 形状**未在本次核对中验证**，`[未实测]`。
- **不可再分发**：集成源码头部明确「may not be used without valid licenses to the AUDIOKINETIC Wwise Technology」。`[官方文档/许可声明]`
  ⇒ 本仓不能把 Wwise 集成放进仓库，也不能把它作为扩展包的依赖分发；消费方必须自己通过 Launcher 安装。
- **本仓后果**：任何硬引用 Wwise 程序集的源码都不能进 `Assets/XFramework/` 或本仓的常驻编译树；
  一个独立扩展包也会让未装 Wwise 的机器编译失败，且无法进六道门禁。

---

## 七、重开判据与验证方式

三条**都**成立才值得重新评估（前两条是台账判据的展开）：

1. **有真实 Wwise 项目**，且 Wwise SDK 可在本仓或 CI 编译验证（不只是「可能要用」）；
2. 至少一个适配器已在真实项目跑通：真实 bank、Event 回调、3D emitter、至少一个目标平台真机；
3. 两家（FMOD / Wwise）的真实适配器都出现后，才评估是否抽 `IAudioProvider`——届时另立条目，
   不直接改本文件的结论。

验证方式（将来执行时）：一次性探针工程 → Launcher 集成 Wwise → 实现最小适配器 → 跑 bank / Event /
3D / 暂停 / 回调回收五条路径；探针跑完即弃，不纳入本仓常驻测试（与 `Documentation/FairyGUI.md:249-251` 同一取向）。

---

## 八、已核对事实与漂移点

| 事实 | 状态 | 漂移点 |
| --- | --- | --- |
| 核心集成由 Wwise Launcher 安装（`.unitypackage` / 离线包进 `Assets/`） | `[官方文档]` | 版本与安装界面随 Launcher 变化 |
| 只有 `WwiseUnityAddressables` 提供官方 Git URL UPM | `[官方文档]` | 上游可能把核心集成也 UPM 化 |
| 集成源码许可证限制再分发 | `[官方文档/许可声明]` | 许可证条款随版本变化；每次升级复核 |
| `AkSoundEngine.PostEvent` 返回 `uint` PlayingID；`StopPlayingID` / `ExecuteActionOnPlayingID` | `[官方文档/二手资料，未实测]` | 签名随集成版本变化 |
| `AK_EndOfEvent` 回调用于回收槽位 | `[二手资料，未实测]` | 回调线程与生命周期需实测 |
| 3D 需要注册的 GameObject / `AkGameObject` | `[二手资料，未实测]` | 集成版本对注册方式有差异 |
| `AkInitializer` / `AkWwiseInitializer` 负责引擎初始化 | `[二手资料，未实测]` | 初始化钩子随版本变化 |

---

## 九、与既有结论的关系（下一轮别重提）

- **`Play(AudioClip)` 不再论证**：`Audio.md:59` / `Ledger.md:105` / `CHANGELOG.md` 已三次否决，
  理由是拆掉后端中立性与「资源生命周期只有一套语义」；无重开判据。
- **框架音量 → 项目 AudioMixer 路由不再论证**：`Audio.md:56` / R3-AUD-001 已实测 `outputAudioMixerGroup`
  恒 `null`，`AudioMixer.SetFloat` 对框架声音静默无效；结论是「要接入只能整体替换 `IAudioManager`」。
- **`IAudioProvider` 分层缝维持暂不做**：`Audio.md:60`；本文件 §三 / §五 是同一结论的展开。
- **不把 Wwise 类型带进核心公开面**：`location` 是唯一的资源标识；`AudioHandle` 只存两个整数。
  这条与 Input 的 `IInputProvider` 保持插件中立是同一条边界。
