# Wwise —— 评估与 `IAudioProvider` 适配器设计（核心 provider 已实现；Wwise 未实现）

> **收件人**：框架维护者（未来实现或审计 Wwise 适配器时）。**不是**随包发布的使用文档。
> **状态**：用户 2026-10-25 选择的 B 方案「核心新增 `IAudioProvider`」**已实现核心**；
> Wwise 适配器**仍未实现**，本文件只记录它将来如何落在这条缝上。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[官方文档]` | Audiokinetic 官方文档 / 许可声明的表述；部分官方页面为动态渲染、未能直接抓取正文 |
| `[本仓核对]` | 读本仓文件得出 |
| `[二手资料]` | 社区文档 / 源码镜像 / 教程 |
| `[设计稿]` | 本文件提出的候选形状，**尚未编译、尚未跑测试** |
| `[未实测]` | 只有文档或推理支撑 |

**核对基准**：Wwise Unity Integration（2024/2025 代际的官方安装流程）与 `WwiseUnityAddressables`
官方文档。**本机未安装 Wwise SDK，没有跑过任何探针。**

---

## 一、结论：缝已实现，Wwise 适配器仍暂不做

用户 2026-10-25 裁定 B：核心已实现公开 `IAudioProvider` 播放引擎缝（见
`Documentation/AudioProvider.md`）。对 Wwise 的含义：

- **将来 Wwise 适配器只实现 `IAudioProvider`**，不实现 `IAudioManager`、不重写通道/音量/暂停/计数；
- **Wwise 适配器现在仍不做**：没有真实 Wwise 项目落地、没有 SDK 可编译验证；
- **本次只出设计文档**；核心缝与内置 `AudioSourceProvider` 作参照已实现，FMOD 适配器仍未实现，
  Wwise 等真实项目再开。

Wwise 侧比 FMOD 多出来的硬差异（决定了它不能靠猜缝）：

1. **没有按 PlayingID 的「是否在播」查询**：`Poll` 要靠 `AK_EndOfEvent` 回调维护槽位表；
2. **3D 需要注册的 GameObject**：适配器要自建隐藏宿主并按槽位/通道分配 emitter；
3. **音量通常走 RTPC / bus**，没有「逐实例音量」的统一入口；
4. 集成源码头部明确限制再分发（见 §五）。

这正是 `Documentation/Modules/Audio.md` 的「已推翻：不提供 `IAudioProvider` 分层引擎缝」条目
原来说「凭记忆猜缝大概率是谁都不合身的最小公约数」的原因；现在有了核心缝 + 内置
`AudioSourceProvider` 作参照（FMOD 适配器仍未实现），Wwise 仍要等真实 API 落地再设计细节。

---

## 二、对象模型与 provider 映射 `[设计稿]`

### 2.1 对照表

| Wwise | 核心 / provider | 说明 |
| --- | --- | --- |
| Event（名字 / id） | `location` 字符串 | provider 解释成 Event 名 |
| `PlayingID`（`uint`） | provider 槽位表 + `AudioProviderHandle` | `uint` 不塞进 `int`；句柄存槽位索引 + 代际 |
| 注册的 GameObject（emitter） | provider 自建隐藏宿主 | 每槽位/通道一个 emitter；`DontDestroyOnLoad` |
| RTPC / switch / state | 核心不表达 | 走扩展自己的 API + `TryGetProviderHandle`（已实现公开） |
| bank | provider 构造时加载 | `WwiseAudioOptions.Banks` / `OwnsBanks`（候选） |
| `AK_EndOfEvent` 回调 | provider 槽位回收 | `Poll` 读槽位表，不直接查引擎 |
| bus / snapshot | 核心通道 | v1 用逐 emitter 音量/RTPC 近似三档相乘；snapshot 不在门面里 |

### 2.2 `IAudioProvider` 映射（Wwise 侧）

| provider 成员 | Wwise 实现 |
| --- | --- |
| `PlayAsync(location, request, ct)` | `AkSoundEngine.PostEvent(eventName, emitterGo, callback, cookie)`；返回 `uint` PlayingID；失败（`AK_INVALID_PLAYING_ID`）返回 default 并记日志 |
| `Stop(handle)` | `StopPlayingID(playingID, transitionMs, curve)`；返回是否停掉 |
| `Release(handle)` | 停播（若需要）→ 归还槽位、递增代际；不注销项目自己的 emitter（若借用） |
| `SetPaused(handle, paused)` | `ExecuteActionOnPlayingID(Pause/Resume, ...)`；句柄失效返回 false |
| `SetVolume(handle, volume)` | 逐 emitter 的 `SetGameObjectOutputBusVolume` / RTPC；最终音量由核心算好 |
| `Poll(handle)` | 槽位表 + `AK_EndOfEvent` 回调：表中存在且未停止 → `Playing`/`Starting`；回调已回收 → `Stopped`；`Invalid` 表示句柄失效 |

`Poll` 是纯查询（不推进超时）；`Starting` 超时、暂停、Loop 回收由核心 ticker 决定
（见 `Documentation/AudioProvider.md` §四）。Wwise 的 Event 循环由 Studio 编排；`Loop` 不生效。

### 2.3 关键差异

1. **在播判定靠回调**：`Poll` 只能读适配器维护的表；回调丢失（bank 卸载、引擎未初始化）会泄漏槽位。
   适配器需要兜底巡检（`StopAll` 时清理），并明确「不是框架级保证」。
2. **3D 需要 GameObject**：`PostEvent` 的 GameObject 重载要求 emitter 已注册
   （`RegisterGameObject` / `AkGameObject`）；适配器自建隐藏宿主，不能复用 `AudioSourcePool`。
3. **初始化归属**：`AkInitializer` / `AkWwiseInitializer` 负责引擎初始化；适配器不重复初始化，
   只在 `Dispose` 停止/注销自己创建的东西。

---

## 三、A 与 B 的对比（结论：B）

| 维度 | A：实现 `IAudioManager` | B：实现 `IAudioProvider` |
| --- | --- | --- |
| 核心改动 | 零 | 公开 API + `AudioManagerImpl` 拆分（见 `AudioProvider.md`） |
| 适配器工作量 | 重写通道/音量/暂停/计数/归一化/句柄 | 只做 Wwise 事件原语 |
| 诊断 `CopyChannels` | 注入后恒空 | 可用 |
| 中间件专属能力 | 只能在 `IAudioManager` 外另开 API | 同样在外，但 `TryGetProviderHandle` 可做逐次控制（已实现公开） |
| 本仓验证 | 无 SDK 不能编译/测试 | 可仿 FMOD 拆成 Wwise-free provider + 条件集成程序集 |

B 的 Wwise 适配器必须等真实 API 落地后再定细节；现在只记录映射与风险。

---

## 四、若实现：分包形状（设计稿，未实现）

仿 FMOD 的两段式：

- `Venusy609.Xframework.Wwise`：Wwise-free 的 provider 逻辑 + options + `IWwiseEventBackend`
  内部缝 + fake backend 测试；始终编译；
- `Venusy609.Xframework.Wwise.Unity`：条件程序集，引用 Wwise 集成程序集（实际 asmdef 名以安装
  版本为准），放真实 backend 与 `WwiseAudio.Install`；用条件编译/define 门控；
- 守卫放 PlayMode 的 `Tests/Runtime/Architecture`，不引用 Wwise 程序集；
- 用户入口 `WwiseAudio.Install(WwiseAudioOptions, AudioInitOptions = null)` → `AudioManager.SetProvider`；
- `WwiseAudioProvider` internal，`WwiseAudioOptions` public。

`WwiseAudioOptions` 候选：`EventNamePrefix`、`ChannelToBus`、`MasterBusName`、`Banks`、`OwnsBanks`、
`EmitterRoot`（最终字段以真实 API 为准）。

---

## 五、分发、许可与门禁

- **核心集成不是官方 UPM 包**：Wwise Launcher → Integrate Wwise in Project（`.unitypackage`；
  离线包解压进 `Assets/`）；只有 `WwiseUnityAddressables` 附属包提供官方 Git URL UPM。`[官方文档]`
- **不可再分发**：集成源码头部明确
  「The content of this file includes portions of the proprietary AUDIOKINETIC Wwise Technology …
  may not be used without valid licenses to the AUDIOKINETIC Wwise Technology.」
  ⇒ 不能把 Wwise SDK 放进本仓或作为扩展依赖分发。`[官方文档/许可声明]`
- **本仓后果**：Wwise 代码只能做条件程序集/仓外本地包；干净克隆跳过；扩展自设 PlayMode 守卫；
  不在核心六道门禁射程。

---

## 六、重开判据与验证方式

三条**都**成立才值得实现 Wwise 适配器：

1. **有真实 Wwise 项目**，且 Wwise SDK 可在本仓或 CI 编译验证；
2. provider 缝已实现（核心）；FMOD 适配器仍待后续，不作为 Wwise 的前置；
3. 至少一个 Wwise 适配器在真实项目跑通：真实 bank、Event 回调、3D emitter、至少一个目标平台真机。

验证方式（将来执行）：一次性探针工程 → Launcher 集成 Wwise → 最小 provider → 跑 bank / Event /
3D / 暂停 / 回调回收五条路径；探针跑完即弃，不纳入本仓常驻测试。

---

## 七、已核对事实与漂移点

| 事实 | 状态 | 漂移点 |
| --- | --- | --- |
| 核心集成由 Wwise Launcher 安装（`.unitypackage` / 离线包进 `Assets/`） | `[官方文档]` | 版本与安装界面随 Launcher 变化 |
| 只有 `WwiseUnityAddressables` 提供官方 Git URL UPM | `[官方文档]` | 上游可能把核心集成也 UPM 化 |
| 集成源码许可证限制再分发 | `[官方文档/许可声明]` | 许可证条款随版本变化 |
| `AkSoundEngine.PostEvent` 返回 `uint` PlayingID；`StopPlayingID` / `ExecuteActionOnPlayingID` | `[官方文档/二手资料，未实测]` | 签名随集成版本变化 |
| `AK_EndOfEvent` 回调用于回收槽位 | `[二手资料，未实测]` | 回调线程与生命周期需实测 |
| 3D 需要注册的 GameObject / `AkGameObject` | `[二手资料，未实测]` | 集成版本对注册方式有差异 |
| `AkInitializer` / `AkWwiseInitializer` 负责引擎初始化 | `[二手资料，未实测]` | 初始化钩子随版本变化 |

---

## 八、与既有结论的关系（下一轮别重提）

- **`Play(AudioClip)` 不再论证**：`Documentation/Modules/Audio.md` 的「不做：不提供
  `Play(AudioClip, ...)` 重载」条目已否决，无重开判据。
- **框架音量 → 项目 Mixer 路由不再论证**：R3-AUD-001 已实测；要路由只能自定义 provider。
- **`IAudioProvider` 已实现**：2026-10-25 用户裁定 B，核心已落地；Wwise 适配器仍 `暂不做`。
- **不把 Wwise 类型带进核心公开面**：`location` 是唯一资源标识；`AudioProviderHandle` 只存两个整数。
