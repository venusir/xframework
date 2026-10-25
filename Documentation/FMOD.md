# FMOD —— 评估与 `IAudioProvider` 适配器设计（核心 provider 已实现；FMOD 包未实现）

> **收件人**：框架维护者（实现或审计 FMOD 适配器时）。**不是**随包发布的使用文档。
> **状态**：**核心 `IAudioProvider` 已于 2026-10-25 实现**（见 `Documentation/AudioProvider.md`）；
> **`Assets/XFramework.Fmod/` 尚未实现**。FMOD SDK 接入方式（官方 `.unitypackage` 导入命令 /
> 本地标记包 / UPM）**待后续单独讨论**。落地前以 `Documentation/AudioProvider.md` 与
> `Documentation/Modules/Audio.md` 的台账为准。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[官方文档]` | FMOD 官方用户指南 / 官方问答的表述；本次经搜索结果摘要核对，官方页面为动态渲染、未能直接抓取正文 |
| `[本仓核对]` | 读本仓文件得出 |
| `[二手资料]` | 社区仓库 / 问答 / 教程 |
| `[设计稿]` | 本文件提出的候选形状，**未编译、未跑测试** |
| `[未实测]` | 只有文档或推理支撑 |

**核对基准**：FMOD for Unity 2.03；手动 UPM 化的事实来自 FMOD Q&A（2.02.20+）。
**本机未安装 FMOD SDK，没有跑过任何探针。** API 名称用于设计对照，落实现前必须按实际安装版本复核。

---

## 一、结论：FMOD 适配器只实现 `IAudioProvider`

用户 2026-10-25 的裁定：

- **核心已实现公开 `IAudioProvider`**（见 `Documentation/AudioProvider.md`）；
- **FMOD 适配器只实现播放原语**，不实现 `IAudioManager`、不重写通道/音量/暂停/计数；
- **FMOD 分包拆成两个程序集**：FMOD-free 的 provider 逻辑（始终编译、可测）+ 条件集成程序集
  （引用 `FMODUnity`，用 `versionDefines` + `defineConstraints` 门控）；
- **FMOD 分包未实现**；SDK 接入方式待后续讨论，不能把「FMOD UPM 包」当成已定前提。

三条理由：

1. **A 的重复语义有边界但真实**：整体实现 `IAudioManager` 会重写约 200–300 行通道/音量/暂停/计数；
   B 让这些留在核心，适配器只做 FMOD 事件实例的起播/停止/暂停/音量/状态。
2. **B 解决诊断缺口**：`SetProvider` 构造的仍是 `AudioManagerImpl`，`CopyChannels` 可用；
   A 的中间件路径会因 `AudioManagerImpl` 类型探测而恒空。
3. **分包必须可条件编译**：FMOD SDK 不可随仓库分发；FMOD-free provider + 条件集成程序集让
   干净克隆仍能编译、能测 provider 逻辑，只有真实后端被 `XFRAMEWORK_FMOD` 门控。

---

## 二、对象模型与 provider 映射 `[设计稿]`

### 2.1 对照表

| FMOD | 核心 / provider | 说明 |
| --- | --- | --- |
| Studio event（`event:/...`） | `location` 字符串 | provider 把 `location` 解释成事件路径 |
| `EventInstance` | provider 槽位表 + `AudioProviderHandle` | 句柄存槽位索引 + 代际，不把 `EventInstance` 塞进 `int` |
| event 参数 / RTPC | 核心不表达 | 走扩展自己的 API + `AudioManager.TryGetProviderHandle`（待裁） |
| bus | 核心通道 | v1 用**逐实例音量**实现三档相乘；不改 FMOD master bus |
| bank | provider 构造时加载 | `FmodAudioOptions.Banks` / `OwnsBanks` |
| `set3DAttributes` | `AudioPlaybackRequest.Position` | 只在 `SpatialBlend > 0` 时写一次；衰减由事件编排 |
| 事件内建的循环 | `AudioPlaybackRequest.Loop` 不生效 | 以 Studio 事件编排为准；provider 的 `Poll` 报告真实状态 |

### 2.2 `IAudioProvider` 映射（FMOD 侧）

| provider 成员 | FMOD 实现 |
| --- | --- |
| `PlayAsync(location, request, ct)` | 解析事件路径 → `RuntimeManager.CreateInstance` → 写 volume/pitch/3D → `start()`；失败返回 default 并记日志；`request.Paused` 则 `setPaused(true)` |
| `Stop(handle)` | `EventInstance.stop(...)`；返回是否真的停掉 |
| `Release(handle)` | 停播（若需要）→ `release()` → 归还槽位、递增代际；幂等 |
| `SetPaused(handle, paused)` | `setPaused`；句柄失效返回 false |
| `SetVolume(handle, volume)` | `setVolume`（最终音量，核心已算好） |
| `Poll(handle)` | `isValid` / `getPlaybackState` / `getPaused` → `Invalid` / `Starting` / `Playing` / `Paused` / `Stopped` |

`Poll` 是纯查询（不推进超时）；`Starting` 超时、循环回收、死源回收由核心 ticker 决定（见
`Documentation/AudioProvider.md` §四）。FMOD 的 one-shot + `Loop=true` 播完后 `Poll` 返回
`Stopped`，核心正常回收，不会因核心的 `Loop` 特判而永久占槽。

### 2.3 核心侧硬缺口（B 之后）

B 解决了 A 的两个缺口：

- `CopyChannels` 在 `SetProvider` 路径可用（语义层仍是 `AudioManagerImpl`）；
- `AudioChannelInfo` 的 internal 构造不再是问题（核心构造）。

仍存在的：

1. `AudioListener.pause` / `AudioListener.volume` 对 FMOD 后端不自动生效；项目暂停走
   `AudioManager.Pause()`，全局 `AudioListener.pause` 不是 FMOD 的契约；
2. 逐实例音量与 FMOD bus/snapshot 可能双重相乘；v1 明确「框架音量走逐实例，项目不要再对同一
   声音用 bus 音量重复控制」；
3. `Loop` / `Pitch` / 3D 衰减由 Studio 事件编排决定，`AudioPlayOptions` 只能近似；
4. `FmodAudioOptions` 的 bank 加载是同步的；大 bank 会卡启动（后续可评估异步）。

---

## 三、A 与 B 的对比（结论：B）

| 维度 | A：实现 `IAudioManager` | B：实现 `IAudioProvider` |
| --- | --- | --- |
| 核心改动 | 零 | 公开 API + `AudioManagerImpl` 拆分（设计见 `AudioProvider.md`） |
| 适配器工作量 | 重写通道/三档音量/暂停/计数/归一化/句柄 | 只做 FMOD 事件原语 |
| 诊断 `CopyChannels` | 注入后恒空 | 可用 |
| 中间件专属能力 | 只能在 `IAudioManager` 外另开 API | 同样在扩展外，但 `TryGetProviderHandle` 可做逐次控制（待裁） |
| 本仓验证 | 无 SDK 不能编译/测试 | FMOD-free provider 始终可测；真实后端条件编译 |
| 维护 | 两套适配器各自重写语义 | 语义只在核心 |

---

## 四、分包设计（`Assets/XFramework.Fmod/`，设计稿）

### 4.1 程序集拆分

```text
Assets/XFramework.Fmod/
├── package.json / README.md / CHANGELOG.md / LICENSE.md
├── Runtime/
│   ├── Venusy609.Xframework.Fmod.asmdef          # FMOD-free：refs 核心 + UniTask
│   ├── FmodAudioProvider.cs                      # internal sealed : IAudioProvider
│   ├── FmodAudioOptions.cs                       # public：EventPathPrefix / Banks / OwnsBanks
│   ├── IFmodEventBackend.cs                      # internal：provider 与真后端之间的缝
│   ├── FmodVoiceSlot.cs                          # internal 槽位表
│   └── AssemblyInfo.cs                           # InternalsVisibleTo 集成/测试
├── Runtime/Integration/
│   ├── Venusy609.Xframework.Fmod.Unity.asmdef    # 条件：refs provider + FMODUnity
│   │                                             # versionDefines: com.firelight.fmod-for-unity → XFRAMEWORK_FMOD
│   │                                             # defineConstraints: ["XFRAMEWORK_FMOD"]
│   ├── FmodEventBackend.cs                       # internal：RuntimeManager / EventInstance
│   └── FmodAudio.cs                              # public：Install(options)
├── Tests/Runtime/
│   ├── Venusy609.Xframework.Fmod.Tests.asmdef    # provider + fake backend；始终编译
│   ├── FmodAudioProviderTests.cs
│   └── FakeFmodEventBackend.cs
├── Tests/Runtime/Integration/
│   ├── Venusy609.Xframework.Fmod.Unity.Tests.asmdef  # 条件
│   └── FmodIntegrationTests.cs                       # env 驱动，缺省 Ignore
└── Tests/Runtime/Architecture/
    ├── Venusy609.Xframework.Fmod.Guards.asmdef   # 始终编译，PlayMode，不引用 FMOD
    ├── FmodReadmeTests.cs
    ├── FmodMetaFileTests.cs
    ├── FmodPackageBoundaryTests.cs
    ├── FmodConditionalAssemblyTests.cs
    └── FmodPackageContractTests.cs
```

### 4.2 条件程序集与守卫

- **Runtime 与 Runtime/Integration 都要各自的 `versionDefines` + `defineConstraints`**：Unity 的
  versionDefines 符号按程序集，不跨 asmdef 传递。
- `versionDefines.expression` 先用 `"0.0.0"`，在第一个工程实测是否匹配；不匹配改用
  `"[0.0.0,999.999.999]"` 或实际版本。
- **FMOD-free 的 provider 程序集不引用 `FMODUnity`**：干净克隆也能编译、能跑 fake backend 测试；
  只有 Integration 程序集引用 FMOD。
- **守卫放 PlayMode 的 `Tests/Runtime/Architecture`**，不引用条件程序集/FMOD；本仓 `-Filter`
  未指定平台时只跑 PlayMode，Editor 守卫会被静默跳过。
- 守卫至少覆盖：README 必答节 + 文件树、`.meta`、包边界（不得出现 `Documentation/`）、
  `package.json` 名称/版本/依赖（不得把 FMOD 写进 dependencies）、条件 asmdef 的
  `versionDefines`/`defineConstraints` 一致性、`FmodAudioOptions` 公开面形状。
- 条件程序集的悬空引用必须先用最小 asmdef 探针验证（见 `AudioProvider.md` §七）。

### 4.3 用户入口

```csharp
// 设计稿，未实现
FmodAudio.Install(new FmodAudioOptions
{
    EventPathPrefix = "event:/",
    Banks = new[] { "Master" },
    OwnsBanks = true,
});
```

- `FmodAudio.Install(FmodAudioOptions fmodOptions, AudioInitOptions audioOptions = null)`：
  构造 real backend + `FmodAudioProvider` → `AudioManager.SetProvider(provider, audioOptions)`；
  `SetProvider` 会先 Dispose 旧实例。
- `FmodAudioProvider` **internal**；用户只经 `FmodAudio.Install`；`FmodAudioOptions` public。
- `Install` 失败（bank 加载失败、事件后端不可用）不得留下半初始化门面：先构造成功再 `SetProvider`。

### 4.4 限制（写进扩展 README）

- `AudioPlayOptions.Loop` 不生效；循环由 Studio 事件编排决定。
- `AudioPlayOptions.SpatialBlend`/`Position` 只写一次 3D 属性；衰减/遮挡由事件编排。
- 主/通道音量按**逐实例音量**实现；不改 FMOD master bus，避免与项目 bus 控制双重相乘。
- `AudioListener.pause` / `AudioListener.volume` 不作用于 FMOD；项目暂停走 `AudioManager.Pause()`。
- bank 在 `Install` 时同步加载；大 bank 会卡启动。
- `CopyChannels` 可用（核心语义层），但 FMOD 专属诊断（RTPC/bus/snapshot）需扩展自己的 API。
- `AudioPlayOptions.VolumeScale <= 0` 等归一化仍由核心做；provider 不得再乘。

---

## 五、分发、许可与门禁

- **FMOD 集成不是官方 UPM 源**：SDK 接入方式（官方 `.unitypackage` 导入命令 / 本地标记包 /
  UPM / 私有仓库）**待后续单独讨论**；SDK 不入本仓。
- **本仓消费（待定）**：无论最终选哪种，都**不提交** SDK、`.unitypackage`、本地 manifest /
  `packages-lock.json` 的改动；干净克隆必须能在无 SDK 时跳过集成程序集。
- **扩展包不在核心六道门禁射程**：核心 `check-docs` 只扫 `Assets/XFramework`；扩展自设 PlayMode
  守卫；XML 文档告警不覆盖扩展（同 Luban 的已知缺口）。
- **干净克隆验证**：无 FMOD 包时 provider 程序集与守卫仍编译/运行，Integration 程序集与测试跳过；
  有 FMOD 时 Integration 编译并跑集成测试。

---

## 六、实现顺序与验证（核心已完成；FMOD 包未执行）

1. ✅ 核心 `IAudioProvider` + 内置 `AudioSourceProvider` 拆分（行为不变）；Audio 167/167。
2. ✅ `AudioManager.SetProvider` + `TryGetProviderHandle` + fake provider 测试。
3. ⬜ 扩展包 FMOD-free provider + fake backend 测试 + 守卫。
4. ⬜ Integration 程序集 + 真实 bank 冒烟（`XFW_FMOD_BANK` / `XFW_FMOD_EVENT`）。
5. ⬜ SDK 接入方式定稿与文档/台账同步（把本文件从「设计稿」改为「已实现」）。

每个阶段的门禁：`run-tests.ps1` 全量 + `check-docs.ps1 -Enforce`；扩展包在有无 FMOD 两种环境各跑一次。

---

## 七、已核对事实与漂移点

| 事实 | 状态 | 漂移点 |
| --- | --- | --- |
| FMOD for Unity 2.03 官方支持 Unity 2021.3 / 2022.3 / 6.0 / 6.3 | `[官方文档]` | 版本矩阵随发布变化 |
| 官方安装 = Asset Store / `.unitypackage` + Setup Wizard | `[官方文档]` | 可能新增官方 UPM 源 |
| 2.02.20+ 可手动 UPM 化；保留 `FMOD.meta` GUID | `[二手资料]` | 上游后续版本可能改变目录定位 |
| 集成 asmdef 名 `FMODUnity` | `[二手资料]` | 以实际安装版本为准 |
| `RuntimeManager.CreateInstance` / `EventInstance.start/stop/setPaused/setVolume/setPitch/set3DAttributes/getPlaybackState` | `[官方文档/二手资料，未实测]` | 2.02 / 2.03 签名可能微调 |
| bus：`RuntimeManager.GetBus`；bank：`LoadBank` / `HasBankLoaded` | `[官方文档/二手资料，未实测]` | 同上 |
| 事件循环由 Studio 编排，`AudioPlayOptions.Loop` 不能强制 | `[未实测]` | 需真实事件验证 |

---

## 八、与既有结论的关系（下一轮别重提）

- **`Play(AudioClip)` 仍不提供**：`Documentation/Modules/Audio.md:59` 已否决（后端中立性 +
  资源生命周期单语义），无重开判据。
- **框架音量 → 项目 AudioMixer 路由仍不内建**：R3-AUD-001 已实测 `outputAudioMixerGroup` 恒 null；
  B 之后要路由只能实现自定义 provider。
- **`IAudioProvider` 已实现**：2026-10-25 用户裁定 B，核心已落地（公开 API 源兼容新增）；
  FMOD 扩展仍未实现。设计见 `Documentation/AudioProvider.md`。
- **`IAudioManager` / `SetInstance` 不删除**：B 是推荐的中间件路径，不是唯一替换路径。
- **FMOD SDK 不入本仓**：分包依赖的 FMOD SDK 接入方式待后续讨论；核心包不声明 FMOD 依赖。
