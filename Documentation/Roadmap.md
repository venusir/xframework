# XFramework 候选模块路线图

> **收件人**：框架作者在做模块选型时。**不是**使用文档，也**不是**审计判据。
> 规则在 `CLAUDE.md`，流程与门禁在 `Documentation/Workflow.md`，审计判据在 `Documentation/ModuleAudit.md`——本文不复述它们。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 快照日期：2026-10-01。「已有模块」的真相在 `Assets/XFramework/Runtime/` 与 `Documentation/Modules/`，本文不维护副本。

## 一、这份文档怎么用

- 只回答两件事：**该不该建**、**边界画在哪**。门面形状、文件清单、测试与提交拆分属立项后的计划与该模块 README，本文不写。
- **「不该做」也写成结论**（依据 `ModuleAudit.md` §四.2）：已否决项一律带**理由 + 出处**，否则下一轮会把它们当新问题重新论证一遍。
- **模块建成后搬迁**：该模块的条目搬进 `Documentation/Modules/<模块>.md`，本文原地留一行指针。本文只保留**尚未立项**的。
- 状态词只有两个：**待立项**（已确认要做）、**待裁**（常见，但边界未定，先别开工）。

## 二、待立项（已确认要做）

> 节的排列即建议的开工顺序。**本节三个模块已于 2026-10-01 全部建成**（Audio 2.1 / Logger 2.2 / Timer 2.3），
> 各自只留指针；本节目前为空——下一个候选见第三、四节。

### 2.1 Audio —— ✅ 已建成（2026-10-01）

> **本条已搬迁**。维护向记录在 `Documentation/Modules/Audio.md`，使用方文档在
> `Assets/XFramework/Runtime/Audio/README.md`——本文只留指针，不再复述。

立项时表格里的四条边界，三条照原样落地（分类允许自定义 → 纯字符串 + 推荐常量；音量不绑 Mixer；
播放句柄 `readonly struct`）；**第三条被推翻了**：「clip 来源两者都要」在评审后端到端重估——保留
`Play(AudioClip)` 重载会同时毁掉 `IAudioManager` 的后端中立性（Wwise 的事件、FMOD 的
`EventInstance` 都没有 `AudioClip`）与「资源生命周期只有一套语义」，最终改为**只走 location**。
这一轮还多裁定了一条本文原先没列的边界：**句柄必须能被第三方实现构造**——照抄 `LockHandle` 的
`internal` 构造会把「整体替换引擎」这条路彻底堵死。全部理由与代价见模块文档的「已评估未采纳与未决」。

### 2.2 Logger —— ✅ 已建成（2026-10-01）

> **本条已搬迁**。维护向记录在 `Documentation/Modules/Log.md`（含 18 个模块回填的施工图：
> 配方 + 分类映射表），使用方文档在 `Assets/XFramework/Runtime/Log/README.md`——本文只留指针，不再复述。

立项门槛「答得出比 `Debug.unityLogger` + 自定义 `ILogHandler` 多给什么」**三条全部落地**（按分类过滤、
零 GC 调用路径、前缀不靠手抄），评审时另加了一条用户提出的维度：**便于 AI 分析**——结构化 JSONL
落盘 + `logMessageReceivedThreaded` 全量捕获，引擎 / 第三方 / 未捕获异常进同一条时间线。
两个已知坑按预案处理：接口叫 `ILogManager`（`ILogger` 被 UnityEngine 占用）；**回填作为独立批次**，
按模块拆成可独立 review 的提交（施工图已写进模块文档）。

**与立项时的差异**：分类名最后定为「与迁移前的 `[标签]` **逐字**对应」，因此同一模块内历史遗留的多个
标签各是一个分类（`Config` 与 `ConfigManager`、`AssetManager` 与 `YooAssetManager`）——换来的是迁移期间
控制台文本零变化、30+ 处 `LogAssert` 零改动。机械提取全部调用点还纠正了三处靠人肉记的账
（多出 `LanguageAssetLoader` / `Reactive` / `XSerialize` 三个标签）。

### 2.3 Timer —— ✅ 已建成（2026-10-01）

> **本条已搬迁**。维护向记录在 `Documentation/Modules/Timer.md`，使用方文档在
> `Assets/XFramework/Runtime/Timer/README.md`——本文只留指针，不再复述。

立项时写下的四条理由**全部落地**：可查询的 `Remaining`、零分配的可复用句柄、不漂移的固定间隔、
与 Update 的 Tier 自动升降档联动。**红线照原样执行**：暂停与时间缩放完全转接自 Update 的双时间轴，
模块内没有一行暂停代码、也没有第二条时间口径（`TimerManager` 上刻意没有 `Pause`）。

一轮额外裁定：**不做 `ITimerManager` 接口**。本仓「有接口」的分界线是「是否存在可替换的后端」
（Audio → FMOD、Input → Rewired），Timer 没有——多一个接口只会多出一份必须长期同步的转发契约。
全部理由与代价见模块文档的「已评估未采纳与未决」。

## 三、待裁（常见，但边界未定，先别开工）

| 候选 | 缺什么 | 卡在哪条边界 |
|---|---|---|
| **Network 基础层** | 现只有 File 里的 `UnityWebRequest`（仅供 Streaming 读取）；超时 / 重试 / 主线程回调 / 请求队列 / 断线重连无统一入口 | 「不预设协议」这条线一松就是 GamePlay。序列化可复用 `Serialize`，但协议、鉴权、错误码体系都是项目的 |
| **代码热更（HybridCLR）** | 资源热更已有 `Asset`（YooAsset），代码侧空白 | 与构建流程、包体、AOT 补充元数据强耦合——多半该是 Editor 工具 + 文档，而不是 Runtime 模块 |
| **红点系统** | 无 | 争议在于它的树形结构算不算 GamePlay；且「红点怎么算」高度依赖策划配置 |
| **画质 / 性能档位** | 与 `Settings` 天然搭（帧率上限、分辨率、后处理开关、阴影质量） | 「档位」的定义项目间差异极大，框架给死的档位几乎必然被改 |
| **GM / 调试命令台** | 无 | 可复用件已有（`Input` 的输入抽象、File 的 `ConsoleFileProvider`）；边界在于它是 Editor 工具，还是随包发布的运行时功能 |
| **埋点 / 统计上报** | 无 | 渠道与后端相关，多半属项目侧；框架最多提供 sink 接口——**这一半已由 `XLog.ILogSink` 落地**（2026-10-01），追加一个上报输出端即可，不必再单独立项 |

**另有一项不进本表**：**App 生命周期**（前后台 / 焦点 / 低内存 / 退出）。2026-10-01 专项盘点后**裁定暂不做**，理由与升级判据记在这里。

**现状几乎不是缺口。** 五个事件里三个已有消费者且工作正常——`Application.quitting` 六处（Update / Lock / Message / Audio / Pool / InstanceTracker，有 `AutoInitTests` 族级守卫）、`Application.lowMemory` 一处（Asset，带开关）、`Application.wantsToQuit` 一处（Settings，带开关）；第四个 `Application.focusChanged` **零消费者**。唯一的真缺口是 `pause`：Unity 的静态事件里没有它，只能由 MonoBehaviour 接收，而全仓只有一处消费者（`Settings` 自持 80 行宿主，`Runtime/Settings/SettingsPauseNotifier.cs` 的注释正是写着这件事）。使用方入口确实为零——第三方要接前后台只能自己写 MonoBehaviour——但那是十行的轮子。

**为什么不现在做**：唯一真缺口只有 1 个消费者，代价却是一个完整模块（对照刚建成的 Audio：17 文件 / 5 提交 / 136 用例）。更要紧的是，为 1.5 个消费者设计一个公开事件源时，形状（边沿还是电平？`pause` 与 `focus` 是否归一？桌面上什么算「切后台」？订阅顺序？）**每一条都是猜的**，撞 `CLAUDE.md` 的「避免臆测」。

**升级判据（可判定，不是「想起来就做」）**：当**想接 `OnApplicationPause` 的地方达到 2~3 处**（例如 Audio 的 v2 自动暂停落地、或又一个模块被迫自建宿主），**或有真实使用方提出该需求**时，它从「补债」变为「补缺」——届时形状也有依据。

**与「会话级复位」是两件事，不要折叠。** 本文件曾把它归入 §五 的会话级复位一并考虑，那是错的：两者在**验收层面**不成立——

| | 会话级复位 | App 生命周期 |
|---|---|---|
| 本质 | **编辑器开发期**的静态状态跨播放会话存活（关闭域重载才复现） | **运行时**的前后台 / 焦点 / 低内存 / 退出通知 |
| 验收 | 关闭域重载**连跑两遍 PlayMode 全量** | 事件能正确送达订阅者 |

`quitting` 的拆除动作会**暴露**门面没复位这个潜在缺陷，所以两者有交叉；但即使没有 `quitting`，把静态状态带进第二会话本身也是缺陷——**缺的不是「事件」，是「谁在会话开始时把静态状态复位」**。做了 App 生命周期，会话级复位的验收照样通不过。

## 四、已否决（不该做）

| 项 | 理由 | 出处 |
|---|---|---|
| 状态机 / 行为树 / 实体组件模型 / 时间模型 / 生命周期树 | 任何「使用方必须按某种架构组织游戏逻辑」的设计都不属于本框架 | `CLAUDE.md` → 项目定位 |
| Update 的「组」API | 「组一旦进入框架，紧接着就要回答『组的生命周期』『组内嵌套』『组之间的顺序』——那就是生命周期树」 | `Documentation/Modules/Update.md` |
| DI 容器（VContainer / Extenject 那类） | 与「静态门面 + 接口注入 + `InternalsVisibleTo`」的既有分层冲突；本仓的注入形态刻意差异化，不追求容器统一 | 本轮评估（2026-10-01） |
| Tween / 缓动 / 动画 | DOTween、PrimeTween 等成熟第三方已覆盖，框架内建只会与它们竞争 | 本轮评估（2026-10-01） |
| Addressables | 资源层已选 YooAsset | `CLAUDE.md` → 项目定位 |
| 新手引导 | 与 UI 否决「列表虚拟化」同一理由：细则由策划配置决定、项目间差异极大，且「自动做一半比不做更糟」 | `Documentation/Modules/UI.md` |
| 平台 SDK 聚合（登录 / 支付 / 推送 / 广告） | 渠道相关，适合项目侧包裹；框架内建会把自己绑到渠道上 | 本轮评估（2026-10-01） |
| 网络同步框架（帧同步 / 状态同步） | GamePlay 本体 | `CLAUDE.md` → 项目定位 |

## 五、不在这里的（只给指针，不复述）

已成模块的待立项与未决另有其家——都在 `Documentation/Modules/<模块>.md` 的「已评估未采纳与未决」里：

- **会话级复位**（关闭域重载后第二个播放会话静默失效）——归口跨 Input / UI / Asset / Config / Localization / File
- **File 补异步删除原语**——Save 的删除 / 移动因此被迫在主线程做同步 IO
- **`Data.CreateSnapshot(bool clearDirty)`**——Save 回滚恢复不了脏标记
- **主线程断言抽公共防线**——Message / Pipeline / Asset 已有三份同形内联副本
- **Update 节点被自动注销对注册方不可观测**

本文不复制它们的内容；要查就回上表那几处。
