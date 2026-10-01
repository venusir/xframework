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

> 节的排列即建议的开工顺序。Audio 已于 2026-10-01 建成（见 2.1）；余下 Logger 与 Timer 仍待立项。

### 2.1 Audio —— ✅ 已建成（2026-10-01）

> **本条已搬迁**。维护向记录在 `Documentation/Modules/Audio.md`，使用方文档在
> `Assets/XFramework/Runtime/Audio/README.md`——本文只留指针，不再复述。

立项时表格里的四条边界，三条照原样落地（分类允许自定义 → 纯字符串 + 推荐常量；音量不绑 Mixer；
播放句柄 `readonly struct`）；**第三条被推翻了**：「clip 来源两者都要」在评审后端到端重估——保留
`Play(AudioClip)` 重载会同时毁掉 `IAudioManager` 的后端中立性（Wwise 的事件、FMOD 的
`EventInstance` 都没有 `AudioClip`）与「资源生命周期只有一套语义」，最终改为**只走 location**。
这一轮还多裁定了一条本文原先没列的边界：**句柄必须能被第三方实现构造**——照抄 `LockHandle` 的
`internal` 构造会把「整体替换引擎」这条路彻底堵死。全部理由与代价见模块文档的「已评估未采纳与未决」。

### 2.2 Logger

**为什么该建**

- 18 个模块各自 `Debug.Log` + 手写 `[模块]` 前缀。框架只在 `CLAUDE.md` 里约定了格式，**没有给它一个落实处**。
- 第三方接入后同样面临这个问题，而框架不提供入口。

**前提：先答得出「它比 Unity 自带的 `Debug.unityLogger` + 自定义 `ILogHandler` 多给什么」**

答不上来就不该建——那只是一层没有价值的包装。站得住的只有三条：

1. **按分类过滤**：Unity 只有全局开关，没有「只关 Save 的 Debug」。
2. **零 GC 调用路径**：`Debug.Log($"[Save] {x}")` 必然分配；门面收 category + 参数、未启用时不格式化，这是实打实的性能差。
3. **前缀不再靠人手抄**：`[模块]` 约定从文档纪律变成代码保证。

**两个已知坑**

- **命名**：`ILogger` 已被 `UnityEngine.ILogger` 占用。按本仓 `IXxxManager` 约定用 `ILogManager`。
- **回填才是真成本**：模块落地后若不回填，框架里就有**两条日志通路**（比一条更糟——使用方开了文件 sink，却发现框架自己的消息不在里面）。而回填 18 个模块属大面积改动，按 `CLAUDE.md`「提交拆分与授权」**须单独请用户确认**。建议：模块本身先落地，回填作为独立批次、按模块拆成可独立 review 的提交。

**为什么排在 Audio 之后**：Logger 是横向切面，落地后「新建模块该用哪一个」立刻成为问题。先做 Audio（沿用既有的 `Debug.Log` 约定），Logger 落地时一次回填覆盖到它——避免「第 19 个模块与前面 18 个不一致」。

### 2.3 Timer —— 建议最后做

**为什么这是三个里价值论证最薄的**

框架已强制 UniTask，而 `UniTask.Delay(TimeSpan, DelayType, PlayerLoopTiming)` **已经覆盖了**延时、重复、以及受不受 `timeScale` 影响。立项前必须答得出「比 `UniTask.Delay` 多给什么」，否则做出来就是一个多余的层。能站住的只有四条：

1. **可查询的句柄**（`Remaining` / `IsCoolingDown`）——`UniTask.Delay` 查不了，这是最硬的一条。
2. **零分配的可复用句柄**——`UniTask.Delay` 每次要 `CancellationTokenSource`。
3. **不漂移的固定间隔**——`UniTask.Delay` 累加会漂。
4. **与 Update 的 Tier 联动**——低频定时器自动降频。

**红线**：暂停与时间缩放**必须架在 Update 的双时间轴之上**，不得另开一套时间口径——那就是在替使用方定时间模型，撞 `CLAUDE.md` 的第一条非目标。

**依赖**：Update（下游）。先例 `Asset ← Event`、`UI ← Update`，方向一致、无环。

## 三、待裁（常见，但边界未定，先别开工）

| 候选 | 缺什么 | 卡在哪条边界 |
|---|---|---|
| **Network 基础层** | 现只有 File 里的 `UnityWebRequest`（仅供 Streaming 读取）；超时 / 重试 / 主线程回调 / 请求队列 / 断线重连无统一入口 | 「不预设协议」这条线一松就是 GamePlay。序列化可复用 `Serialize`，但协议、鉴权、错误码体系都是项目的 |
| **代码热更（HybridCLR）** | 资源热更已有 `Asset`（YooAsset），代码侧空白 | 与构建流程、包体、AOT 补充元数据强耦合——多半该是 Editor 工具 + 文档，而不是 Runtime 模块 |
| **红点系统** | 无 | 争议在于它的树形结构算不算 GamePlay；且「红点怎么算」高度依赖策划配置 |
| **画质 / 性能档位** | 与 `Settings` 天然搭（帧率上限、分辨率、后处理开关、阴影质量） | 「档位」的定义项目间差异极大，框架给死的档位几乎必然被改 |
| **GM / 调试命令台** | 无 | 可复用件已有（`Input` 的输入抽象、File 的 `ConsoleFileProvider`）；边界在于它是 Editor 工具，还是随包发布的运行时功能 |
| **埋点 / 统计上报** | 无 | 渠道与后端相关，多半属项目侧；框架最多提供 sink 接口（可与 Logger 的 sink 面合一） |

**另有一项不进本表**：**App 生命周期**（前后台 / 焦点 / 低内存 / 退出）。它更像**补债**而非补缺——`Application` 的静态事件里没有 `pause`，而 `OnApplicationPause` 只能由 MonoBehaviour 接收，`Settings` 为此已经自持了一个隐藏宿主（`Runtime/Settings/SettingsPauseNotifier.cs` 的注释正是写着这件事）。它要解决的多半是「会话级复位」那条待立项（见 §五），故归到那里一并考虑。

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
