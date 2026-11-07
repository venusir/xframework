# XFramework FMOD

## 概述

XFramework 的可选 FMOD 音频扩展。目标形态是：第三方引入本包并配置 FMOD 后，**控制音效只经
`AudioManager`**——播放、停止、暂停、三档音量、参数/RTPC、bank、bus、snapshot、逐次 3D 位置都从
`AudioManager` 进入；本包不提供 `FmodAudio` 之类的第二套公开门面。

> **当前阶段（0.1.0，FMOD-free 层）**：本包只包含 FMOD-free 的 provider 逻辑、配置资产与测试，
> **不能实际播放 FMOD**。真实 FMOD 集成程序集（条件编译 + `FMODUnity` + 自动安装）需要 FMOD 2.04
> Asset Store UPM 与 SDK 验证，尚未加入。请勿在当前阶段把本包当作可用的 FMOD 适配器发布。

## 快速使用

以下为目标形态（Phase 2 生效；当前阶段不会自动安装，也不会创建 FMOD 实例）：

1. 安装核心包 `com.venusy609.xframework`。
2. 安装 **FMOD for Unity 2.04**（本包不打包、不代装）：
   - 官方 UPM 包名 **`com.fmod.fmod-for-unity.v204`**，版本 **`2.4.0`**。
   - 渠道：Unity Asset Store（若可用）/ fmod.com 免费账号下载 / 本地 UPM 包。
   - 本地 UPM：Package Manager → **+ → Add package from disk**，或在 `Packages/manifest.json` 加
     `"com.fmod.fmod-for-unity.v204": "file:<本机路径>"`（开发期本地改动，不要提交）。
   - 若实际包名不是 `com.fmod.fmod-for-unity.v204`，`versionDefines` 不会命中；需在 Player Settings 的
     Scripting Define Symbols 加 `XFRAMEWORK_FMOD`，否则条件集成程序集被排除，只剩 FMOD-free 层。
3. 运行 FMOD Setup Wizard，准备好 FMOD 工程与 bank。
4. 在 `Assets/Resources/` 下创建 `FmodAudioSettings`（菜单 `Assets > Create > XFramework > FMOD Audio Settings`）。
5. 不调用 `AudioManager.Initialize()`；扩展会在启动时检测配置资产并安装 provider。
6. 用 `AudioManager` 操作音效：

```csharp
// 事件路径以 event:/ 开头；FmodAudioSettings.EventPathPrefix 默认 event:/
var handle = await AudioManager.PlayAsync("event:/SFX/Footstep",
    new AudioPlayOptions(
        channel: AudioChannels.SE,
        initialParameters: new[] { new AudioParameter("Surface", "Grass") }));

AudioManager.SetParameter(handle, "Health", 0.5f);
AudioManager.SetPosition(handle, transform.position);

AudioManager.LoadBank("Master");
AudioManager.LoadBank("Master.strings");
AudioManager.SetBusVolume("bus:/SFX", 0.8f);
AudioManager.StartSnapshot("snapshot:/Pause");
AudioManager.StopSnapshot("snapshot:/Pause");
```

起播前参数用 `AudioPlayOptions.InitialParameters`；`AudioManager.Play` 返回时可能仍在加载，
逐次控制需要句柄已激活时请用 `PlayAsync`。

## 模块结构

```text
Runtime/
├── Venusy609.Xframework.Fmod.asmdef  # 程序集定义（核心 + UniTask；不引用 FMOD）
├── AssemblyInfo.cs                    # InternalsVisibleTo（集成/测试）
├── FmodAudioSettings.cs               # public：唯一公开类型，配置资产
├── FmodProviderOptions.cs             # internal：settings 快照
├── IFmodEventBackend.cs               # internal：FMOD-free backend 缝
├── FmodEventSlotTable.cs              # internal：index + generation 槽位表
└── FmodAudioProvider.cs               # internal：IAudioProvider + 五个能力接口实现
```

`FmodAudioSettings` 字段：`Enabled`、`EventPathPrefix`、`Banks`、`LoadBankSamples`、`OwnsBanks`、
`MaxVoices`、`MasterVolume`、`MasterMuted`。它是配置资产，不是控制 API；provider/backend/options
全部 internal。

## 行为契约

- 第三方控制入口只有 `AudioManager`；本包没有公开的 `FmodAudio.Install`、`FmodAudioOptions` 或
  provider 接口。
- `AudioManager` 的逐次参数、bank、bus、snapshot、3D 位置方法为探测型：能力不支持、句柄无效、
  未初始化时返回 `false`，不抛；名称空白抛 `ArgumentException`。
- `SetBusVolume` 原样透传、不做核心钳制；框架三档音量与 FMOD bus 音量是两层。
- 起播前参数由核心校验并复制；provider 在事件 `start()` 前应用。
- 内置 provider 不支持参数/bank/bus/snapshot；`AudioSourceProvider` 支持 `SetPosition`。
- **provider 已实现 core internal 的五个能力接口**（参数/bank/bus/snapshot/3D）；当前用 fake backend
  验证 `AudioManager` 控制面到 provider 的端到端桥接。真实 FMOD backend 在 Phase 2。
- 本包当前不产生运行时效果；自动安装与真实 backend 在 Phase 2。

## 线程契约

- provider/backend 全部成员限主线程调用。
- bank 加载可能是同步阻塞的；大 bank 请延后或分帧。

## 性能代价

- 槽位表零 GC（数组 + 代际）；每次播放一次 provider 句柄分配（值类型，无堆分配）。
- 起播前参数数组由核心在非空时复制一次；无参播放不分配。
- FMOD 调用本身的开销未实测（Phase 2）。

## 已知限制

- **Phase 1 不可实际播放 FMOD**：真实集成程序集未实现，配置资产当前只提供形状/默认值。
- 逐次参数/bank/bus/snapshot 需要真实 FMOD backend；内置 provider 一律返回 `false`。
- 无逐 voice 暂停/音量、无事件回调、无普通事件 stop fade。
- FMOD 的 `Loop` / `Pitch` / 3D 衰减最终由 Studio 事件编排决定；框架参数只能近似。
- `AudioListener.pause` / `AudioListener.volume` 不作用于 FMOD；项目暂停走 `AudioManager.Pause()`。
- bank 加载同步阻塞；没有异步 bank 加载 API。
- 扩展包的 XML 文档不随核心文档门禁检查，质量靠评审。

## 设计取舍

- **为什么不提供 `FmodAudio` 门面**：用户要求公开控制面只有 `AudioManager`；第二套入口会让第三方
  学两套 API，也让内置 provider 与 FMOD provider 的行为分叉。
- **为什么能力接口 internal**：只服务官方扩展，避免把 FMOD 专属概念（参数/bank/bus/snapshot）
  变成第三方 provider 的公开承诺；代价是核心与扩展必须同版本配对。
- **为什么用配置资产自动安装**：导入包即用，同时以资产存在为 opt-in，避免仓库/测试在没有 FMOD 时
  被自动安装污染。
- **为什么不内建 FMOD SDK**：SDK 不可再分发；本包只调用消费方安装的 FMOD。

## 依赖

| 包 | 版本/来源 | 说明 |
| --- | --- | --- |
| `com.venusy609.xframework` | 核心包（同 tag） | 必需；提供 `AudioManager` 与核心能力接口 |
| UniTask | 随核心包依赖安装 | provider 的异步签名 |
| FMOD for Unity | 2.04（UPM；`com.fmod.fmod-for-unity.v204` / 2.4.0） | Phase 2 前置；不随本包分发 |

本包不包含 FMOD SDK，也不会自动安装 FMOD；`package.json` 的 `dependencies` 为空（UPM 不支持 Git URL
依赖，FMOD 也不在公开 UPM 注册表）。FMOD SDK 的许可与安装由消费方自行负责；本包不包含任何 FMOD
二进制或源代码。
