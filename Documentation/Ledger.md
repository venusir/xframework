# XFramework 决策台账

> **收件人**：框架作者（选型 / 排期 / 审计时）。**不是**使用文档，也**不是**审计判据。
> 规则在 `CLAUDE.md`，流程与门禁在 `Documentation/Workflow.md`，审计判据在 `Documentation/ModuleAudit.md`——本文不复述它们。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 本文回答一件事：**框架级的「已做 / 未做 / 待裁 / 暂不做 / 不做」分别是什么**。
> 单模块的条目在 `Documentation/Modules/<模块>.md` 的「决策台账」节，本文只指针、不复述
> （`Roadmap.md` 于 2026-10-15 并入本文，其 §三/§四/§五 的条目与专段全部迁到下面）。

## 一、怎么用

- **条目格式与状态词表见** `Documentation/Modules/README.md` →「决策台账的条目格式」——**一处定义、两处使用**（模块台账与本文），不在此复制。
- 本文收**框架级**结论（跨模块 / 候选模块 / 工程化与发布形态 / 外部依赖）；**单模块**的边界仍归模块 README 与 `Modules/<模块>.md`。
- **「不该做」也写成结论**（依据 `ModuleAudit.md` §四.2）：已否决项一律带**理由 + 出处**，否则下一轮会把它们当新问题重新论证一遍。
- **「暂不做」与「不做」不要折叠**：「暂不做」有可判定的重开判据、判据触发才重开；「不做」是结构性的不该做。折叠会让待裁被误读成已否决。
- **模块建成后**：该模块的条目搬进 `Modules/<模块>.md` 的决策台账，本文原地留一行指针。

## 二、模块台账在哪

模块清单与链接的唯一维护处是 `Documentation/Modules/README.md` 的文件表——本文**不复制那份清单**（同一份名单三处维护就是漂移的起点）。每个模块文件的「决策台账」节是该模块条目的唯一落点；`Tools/check-docs.ps1 -Enforce` 会核「每个模块都有台账」与「文件表与 `Runtime/` 子目录双向相等」。

四份**随外部漂移**的维护向文档不在台账体系里、在此登记为指针：

- `Documentation/CI.md`——CI 的成本要点与裁定（暂不接入 + 三条重开判据）；
- `Documentation/Luban.md`——配置表工具接入配方（已核对事实与版本漂移点；2026-10-24 起记录扩展包工作流与端到端探针结论）；
- `Documentation/FairyGUI.md`——第三方 UI 方案评估（不接成后端 / 不做平级包 + 重开判据）；
- `Documentation/Cinemachine.md`——Cinemachine 3.1.7 后端的版本 / 3.x API 实测 / 升级与漂移点（已并入核心包 `Runtime/Camera/Cinemachine/`，核心 asmdef 硬引用 Cinemachine）。

## 三、框架级决策

- 暂不做：**Network 基础层**（2026-10-13 · 用户确认）——仓内零消费者，且「**没有使用方则形状无依据**」（注意这不是「没人要」论据——那是本仓标注为最弱的一类）。重开判据：① 有真实使用方提出该需求，并给出其协议 / 鉴权形状（主要路径）；② `File` 之外的模块出现 HTTP 需求。

  **为什么不现在做**：① 仓内唯一的网络代码是 `File/MobileFileProvider.cs` 的 `UnityWebRequest`（**仅供 Streaming 域读取**：5 个 `private static` 方法，无超时 / 重试 / 进度），它绑死在「读 StreamingAssets」上、**不构成可复用的请求面**——这一处**不可能**由框架内部补上。② 这一层真正的价值在**编排**（请求队列 / 优先级 / 标签 / 超时 / 主线程回调 / 并发上限），而协议、鉴权、错误码体系全是项目自己的；没有使用方时形状就是猜的（`CLAUDE.md`「避免臆测」挡的正是这个）。

  **第三方对照（一手，2026-10-13 读 UGF 源码，非网页转述）**：GameFramework 有同形模块（`GameFramework/WebRequest/`，12 个文件）——`IWebRequestManager` = 请求队列 + 优先级 + 标签 + `Timeout` 属性 + 三个事件 + 并发（由注入的 agent 数量决定），**它同样没有重试**；接口对协议 / 鉴权 / 错误码**零假设**。也就是说：真要做，形状是已知的（照它即可）；缺的不是知识，是使用方。

  **判据复核**：随框架级评估每轮回填（先例见下方 App 生命周期那条的「判据复核」）。

  **与「网络同步框架」是两件事，不要折叠**：否决的是**同步模型**（帧同步 / 状态同步，GamePlay 本体）；本条是**请求 / 重试 / 超时 / 主线程回调**（基础设施）。原句见下方不做组的复核记录。

- 暂不做：**代码热更（HybridCLR）——不建 Runtime 模块；形态＝Editor 工具 + 配方文档**（2026-10-13 · 用户确认）——HybridCLR 本身就是第三方包（自带上载器与 Editor 工具，与 YooAsset 同型，框架不包它）；框架代码不该被热更（热更要求热更代码与 AOT 代码分属不同程序集，而本仓 Runtime 是**单一 asmdef** 的既定不变量）；与构建流程 / 包体强耦合的部分只有项目自己的构建管线知道。重开判据：有真实使用方在本框架上接入代码热更——届时**先接、再写配方**，且配方里每条事实都必须带核对状态标记。

  **现状是一手核实的「彻底零」**：全仓 `HybridCLR` 只在两处出现——原先的本条，与 `Documentation/Luban.md` 把它当「Editor 工具 + 文档，而不是 Runtime 模块」的**同取向先例**引用。代码与配置里：**没有**依赖、**没有** `link.xml`、**没有** AOT 补充元数据、**没有**任何构建钩子（`IPreprocessBuildWithReport` / `BuildPlayer` 全仓零命中）。

  **形态（若接入）**：`Documentation/HybridCLR.md`——镜像 `Luban.md` 的形状（〇 核对状态 → 一 结论 / 边界 → 二~四 步骤 / 接缝 → 末节 边界与重开判据），**不随包发布**。届时配方要回答的大概是这两件（**预期，未验证**）：**AOT 泛型**（本框架重度使用泛型——`Message<T>` / `EventStream<T>` / `ReactiveProperty<T>` / `Pool<T>`——热更侧调用会不会撞 AOT 泛型缺失）与**上载器接在哪一相位**（`BootstrapPhases`）。

  **配方现在不写**：本仓从未接入过 HybridCLR，配方里每一步都**未实测**——写了就是臆测。`Luban.md` 的配方是**接完才写的**，每条事实都带核对状态标记；`FairyGUI.md` 同理。

- 已做：**Luban 独立扩展包 `com.venusy609.xframework.luban`**（2026-10-24 · 提交 `6946105e` / `0184e632`；真实 5.1.0 生成物端到端用例 `LubanEndToEndTests`）——核心包零改动、零依赖；扩展包用「生成式 `IConfigRow<TKey>` 行接口实现 + 强类型适配器」把 map / 单行 / 带索引 list 注册进 `ConfigManager`，纯列表跳过；运行时不引用 Luban 程序集。无重开判据。

- 已推翻：**~~不做 Editor 一键导表 / 不建 `Tools/luban`~~**（原判 2026-10-10 · `Documentation/Luban.md` §五）——原否决理由是「把 Luban 安装位置、schema、输出目录固化进框架」；扩展包把全部路径放进使用方项目内的 `ProjectSettings` 设置，理由不再成立。取代者：〈Luban 独立扩展包〉（2026-10-24）。核心仓仍不建 `Tools/luban/`。

- 未做：**Luban 表解析仍主线程**（2026-10-24）——`Tables` 构造是同步 CPU 工作，v1 不做线程池解析；大表会造成启动停顿。重开判据：真实使用方出现超阈值卡顿。

- 已做：**Luban 扩展包 2026-10-24 审计修复**（2026-10-25）——按 `Documentation/Luban.md` §七的批次 A–F 落地 6 项 P1 与 22 项 P2 中的 21 项（LUB-26 发布 tag 见下条「未做」），并补扩展包侧守卫（README/文件树、`.meta`、包边界、`AutoInit`、Runtime 公开 API 基线、依赖声明）与示例编译通道；LUB-07 的 stale `.meta` 清理按「旧 manifest + auto-generated 头」窄规则实施；无公开 API 签名变更。

- 未做：**Luban 扩展包发布 tag**（2026-10-24）——扩展包版本 0.1.0 尚未打 tag，README 的安装 URL 目前只能写 `<发布 tag>` 占位。重开判据：用户决定版本号并打 tag（不自动推送）。

- 暂不做：**把扩展包纳入 check-docs 的 XML 文档/`.meta` 门禁**（2026-10-24）——`check-docs.ps1` 的路径过滤硬绑 `Assets/XFramework`，扩展包改用包内自设守卫；XML 文档告警仍靠评审。重开判据：扩展包出现文档漂移事故，或 `check-docs.ps1` 的扫描根泛化为可配置。

- 暂不做：**GM / 调试命令台**（2026-10-13 · 用户确认）——命令集 / 权限 / 发布形态（仅 Editor，还是真机也要——后者是渠道与安全问题）全是项目的；没有使用方时「解析器支持什么语法」是猜的。重开判据：① 有真实使用方提出该需求，并给出其**命令集形态**（命令从哪来：显式注册 / 反射扫描 / 配置表？参数怎么传？）与**运行场景**（仅 Editor，还是真机也要）；② 届时形状从**诊断窗口那一半**起步（Editor 工具 + Runtime 数据契约），不重造。

  **先订正一处误读**（2026-10-13）：此前把 `File` 的 `ConsoleFileProvider` 列为「可复用件」——它是**游戏机平台（Xbox / PS5 / Switch）的文件提供者抽象基类**，与「控制台 / 命令台」无关，且仓内**零派生、零引用**（`Modules/File.md` 记着它 12 个公开方法零覆盖）。真正算得上「已有的一半」的只有诊断窗口：18 个页签、`IDiagnosticPanel`（`Title` / `Order` / `Collect`）+ 静态登记表（显式登记，无反射），渲染层只在 Editor 程序集、不进 Player 构建。

  **缺的另一半，仓内没有原语**：**命令解析**（语法）、**权限**（谁能用）、**输入接线**——最后一项尤其：`IInputProvider` 是**纯动作名轮询**（`bool` / `float` / `Vector2` + 长按时长，全仓 `TextInput` 零命中），**没有文本输入 / 字符流 / 组合键 / 按键序列**。要做就得新开一条通道，而通道的形状取决于项目要支持到什么程度。

  **与诊断窗口的关系：不是「它的下一步」，是另一件工具。** 诊断窗口是**拉模式只读**——契约写死「`Collect` 无副作用、不得抛、不得被每帧调用」；命令台是**写模式**（执行动作、可能失败、可能要权限）。两者的数据契约不能合并，登记表也不该复用。

- 不做：**埋点 / 统计上报**（2026-10-01 · 首轮评估；框架级评估维持不单独立项）——渠道与后端相关，多半属项目侧；**框架侧那一半已经给了**：`XLog.ILogSink` 落地后，追加一个上报输出端即可，不必再单独立项。无重开判据。

- 不做：**热更编排（不新建模块）**（2026-10-13 · 用户确认，**推翻**「编排半可进框架」的旧判）——逐条核对依赖源码后发现「下载编排」这一半已经由 YooAsset 提供，框架再包一层只会与上游版本耦合地重复一遍编排逻辑；真缺口按性质分两处归属（下表）。无重开判据。

  **YooAsset 2.3.18 已经提供的**（2026-10-13 逐条读包内源码核对，**不是网页转述**）：并发控制（`DOWNLOAD_MAX_CONCURRENCY` / `DOWNLOAD_MAX_REQUEST_PER_FRAME`）、看门狗超时（`DOWNLOAD_WATCH_DOG_TIME`）、**断点续传**（`RESUME_DOWNLOAD_MINMUM_SIZE` / `RESUME_DOWNLOAD_RESPONSE_CODES` + `UnityDownloadResumeFileOperation`）、暂停 / 恢复 / 取消（`DownloaderOperation.PauseDownload` / `ResumeDownload` / `CancelDownload`）、进度与逐文件开始回调（`CurrentDownloadCount` / `Bytes`、`DownloadFileBeginCallback`）、**单文件失败重试**（`failedTryAgain` 参数 + `DownloadPackageBundleOperation` 的 `TryAgain` 状态）、CDN 主备站（`IRemoteServices.GetRemoteMainURL` / `GetRemoteFallbackURL`，`_requestCount % 2` 交替）。本仓 `Asset` 已把这些包成 7 个门面成员与一个下载器句柄。

  **真缺口（逐条，有证据）**：① **预检与下载的版本绑定断了**——这是**缺陷**不是缺口（`PreDownloadContentAsync` 把 operation 丢掉，而 `CreateDownloader` 基于**当前激活清单**，于是「预检之后下载」实际下载的是旧清单的缺口），归 **`Asset`**（见 `Modules/Asset.md` 的决策台账）；② **版本对比**（本地 vs 远端）→ 项目侧；③ **整体失败重试 / 续跑、下载等待的超时**（单文件重试有）→ 项目侧；④ **更新决策**（强制门槛 / 公告文案 / 灰度）→ 项目侧；⑤ **更新 UI 接线** → 项目侧。

  **第三方对照（一手，2026-10-13 读 UGF）**：UGF 有独立 `Download`（断点续传 + 并发下载器数）与 `WebRequest` 模块，但**没有任何**「更新策略 / 强制更新 / CDN」概念——「更新资源流程」在使用方自己的 Procedure（状态机）里。本仓把 ②–⑤ 判给项目侧，与它同向。

- 暂不做：**App 生命周期**（前后台 / 焦点 / 低内存 / 退出）（2026-10-01 专项盘点后裁定）——五个事件里三个已有消费者且工作正常（`Application.quitting`、`Application.lowMemory`、`Application.wantsToQuit`），第四个 `focusChanged` 零消费者；**唯一的真缺口是 `pause`**——Unity 的静态事件里没有它，只能由 MonoBehaviour 接收，而全仓只有一处消费者（`Settings` 自持约 80 行宿主），那是十行的轮子。为 1.5 个消费者设计公开事件源时，形状（边沿还是电平？`pause` 与 `focus` 是否归一？桌面上什么算「切后台」？订阅顺序？）**每一条都是猜的**。重开判据：**想接 `OnApplicationPause` 的地方达到 2~3 处**（例如 Audio 的 v2 自动暂停落地、或又一个模块被迫自建宿主），**或有真实使用方提出该需求**。

  **判据复核（2026-10-11，框架级评估）**：**未触发，维持不做。** 消费者仍是 `Settings` 自持宿主那一处（`Runtime/Settings/SettingsPauseNotifier.cs`），当轮探索未发现新增。

  > **2026-10-12 订正**：本节前身（`Roadmap.md`）原写「`Application.quitting` 六处……有 `AutoInitTests` 族级守卫」，其中对 `InstanceTracker` **当时不成立**——它用的是**静态构造函数 + 匿名 lambda**，而族清单按特性扫描，扫不到静态构造（`Modules/Input.md` 当时就指出过这处矛盾）。该轮已把 `InstanceTracker` 改成具名 `AutoInit`（幂等订阅 + 会话开始清空计数）并登记进族清单，这句话从当天起才成立。原文保留以留痕。

  **与「会话级复位」是两件事，不要折叠**（本台账的前身曾把它们归到一起，那是错的）：两者在**验收层面**不成立——会话级复位是**编辑器开发期**的静态状态跨播放会话存活（验收 = 关闭域重载连跑两遍 PlayMode 全量），App 生命周期是**运行时**的前后台 / 焦点 / 低内存 / 退出通知（验收 = 事件能正确送达订阅者）。`quitting` 的拆除动作会**暴露**门面没复位这个潜在缺陷，所以两者有交叉；但即使没有 `quitting`，把静态状态带进第二会话本身也是缺陷——**缺的不是「事件」，是「谁在会话开始时把静态状态复位」**。做了 App 生命周期，会话级复位的验收照样通不过。

- 不做：**状态机 / 行为树 / 实体组件模型 / 时间模型 / 生命周期树**（出处：`CLAUDE.md` → 项目定位）——任何「使用方必须按某种架构组织游戏逻辑」的设计都不属于本框架。无重开判据。
- 不做：**Update 的「组」API**（出处：`Modules/Update.md`）——「组一旦进入框架，紧接着就要回答『组的生命周期』『组内嵌套』『组之间的顺序』——那就是生命周期树」。无重开判据。
- 不做：**DI 容器（VContainer / Extenject 那类）**（2026-10-01 · 首轮评估）——与「静态门面 + 接口注入 + `InternalsVisibleTo`」的既有分层冲突；本仓的注入形态刻意差异化，不追求容器统一。无重开判据。
- 不做：**Tween / 缓动 / 动画**（2026-10-01 · 首轮评估）——DOTween、PrimeTween 等成熟第三方已覆盖，框架内建只会与它们竞争。无重开判据。
- 不做：**Addressables**（出处：`CLAUDE.md` → 项目定位）——资源层已选 YooAsset。无重开判据。
- 不做：**新手引导**（出处：`Modules/UI.md`）——与 UI 的「不内建列表虚拟化」同一理由：细则由策划配置决定、项目间差异极大，且「自动做一半比不做更糟」。无重开判据。
- 不做：**平台 SDK 聚合（登录 / 支付 / 推送 / 广告）**（2026-10-01 · 首轮评估）——渠道相关，适合项目侧包裹；框架内建会把自己绑到渠道上。无重开判据。
- 不做：**网络同步框架（帧同步 / 状态同步）**（出处：`CLAUDE.md` → 项目定位）——GamePlay 本体。**与上方「Network 基础层」是两件事，不要折叠**（见复核记录）。无重开判据。
- 不做：**统一框架 Profile / 全局配置文件**（一个文件装 Audio / UI / Log / Save 等模块的值）（2026-10-01 评估；2026-10-02 判据状态）——① 与三条既定立场正面冲突：包文档明文承诺「无需额外的框架配置」、架构的**关键设计决策**就是「服务不依赖统一入口」、DI 容器已因「不追求统一」被否；② **引导顺序无解**：Profile 若经 `Config`/`Asset` 加载，就**无法承载 Asset 自身的参数**（包名 / PlayMode / 远端地址——恰恰最需要配的），若自建加载通道则等于新增一条配置来源（时机 / 失败回退 / 加密 / 热更版本化），与「零配置即可用」相斥；③ **共享耦合点**：一个文件装多模块的值，把「模块需要什么」从签名挪进共享类型，破坏「可单独取用」。**真问题另有其解**：「框架约定的名字」清单见包 README（集中登记、零运行时代价）；少数写死的地址要可配，正确形状是各模块自己的 options 字段 / 公开属性（先例 `LogOptions.FileDirectory`、`LocalizationManager.LanguageAssetPath`）。**将来若重开的判据**：只装「编译期决定不了、且与 Asset 引导无关」的值；不替代模块 Options；先回答引导顺序。**判据状态（2026-10-02）**：第三条已满足——引导顺序已契约化（`BootstrapPhases` 常量 + Bootstrap README 的相位表）；但这不构成重开理由，因为第二条判据排除的正是「中心化」这个形状（同轮已把「GameLauncher 作为配置与编排核心」按同样理由否决，见 `Modules/Bootstrap.md`）。无重开判据。
- 不做：**把模块内部策略接口化**（UI 排序分层 / Update 切片与节拍基 / Message 缓冲「只留最后一条」/ Event 投递顺序 / Timer·Pool·Lock 的静态性）（2026-10-02 · 全面清点）——这些不是「配置」而是**模块不变量**：换了它，模块的其它保证（遮罩与栈重排、进度聚合与档位估计、缓冲重放语义、订阅快照语义）就不再成立，等于换一个模块。**判据（本条即「三档判据」的出处）**：对每个预设问「不想要它时的代价」——一次注入 / 一个 options 字段 = 健康；抄一个实现 = 有边界但要写清；**不可能 = 越界**。该轮据此处理了三处越界出口（Asset 解密服务、Update 自动驱动开关、Input 初始 map 名）与一处死选项，取舍与遗留项见各模块 README 的「已知限制」。无重开判据。**该判据经受过一次实战检验**（2026-10-11）：遮罩缺 `GraphicRaycaster` 是**缺陷**而非「模块不变量」，补上它不需要接口化任何东西——「不变量」与「漏做」的区分标准因此有了一个正面实例。
- 已推翻：**~~相机管理~~**（2026-10-11 框架级评估 §4.1；用户确认 2026-10-13）——~~Cinemachine 已覆盖相机编排，框架内建只会与之竞争（与「Tween」同一理由）；UI 里两处 `Camera.main`（`UITipItem.cs:76` / `UIHudItem.cs:80`）是**缓存过的**，不构成每帧开销，不值得为它开模块。无重开判据。~~ 被 2026-10-17「XCamera 核心模块 + Cinemachine 扩展包」取代：用户给出真实场景「用 Cinemachine，但要统一入口」；一手核对 Cinemachine 3.1.7 源码确认激活 / 反激活 / 过渡已由它完整实现，框架只补统一入口——**编排仍归 Cinemachine，核心包零 Cinemachine 依赖**，Cinemachine 3.x 实现放独立可选扩展包。详见 `Modules/Camera.md` 与 `Cinemachine.md`。**2026-10-22 进一步把 Cinemachine 3.1.7 后端硬合并进核心包**（见下条）。
- 已推翻：**~~XCamera 核心模块 + Cinemachine 扩展包建成~~**（2026-10-17）——~~核心 `ICameraManager` / `CameraManager` / `UnityCameraManager`（零 Cinemachine 依赖）；`com.venusy609.xframework.cinemachine` `0.1.0`（Cinemachine 3.1.7，单独 asmdef / 单独安装）。**命名 / 动态双键**：命名相机 string id；随预制创建 / 销毁的动态相机用 `CameraHandle`（`Register<T>`，主接口直扩）；**Timeline / 过场相机不接管**（由过场系统管理，未注册活动相机告警）。边界与重开判据见 `Modules/Camera.md`；扩展包的 3.x 实测与升级见 `Cinemachine.md`。~~ 被 2026-10-22「Cinemachine 3.1.7 后端硬合并进核心包」取代：扩展包删除，过场边界 / 适配器非行为库保留；**双键已被同日「统一句柄」重整取代**。
- 已做：**Cinemachine 3.1.7 后端硬合并进核心包**（2026-10-22 · 用户裁定）——`CinemachineCameraManager` 移入 `Runtime/Camera/Cinemachine/`，命名空间不变、程序集变为 `Venusy609.Xframework`；核心 asmdef 硬引用 `Unity.Cinemachine`；`com.unity.cinemachine` 3.1.7 按 Input System 先例落安装器 + 核心 README + 开发 manifest 三处，`package.json` `dependencies` 仍留空。扩展包 / 独立 asmdef / `CinemachineDependencyTests` 删除。理由：Input System 先例、Unity 6000.3 最低版本 + Cinemachine 3 默认、自研相机后端非真实受众。门面 / 契约保持中立，由 `ModuleBoundaryTests.CinemachineTypes_StayInsideTheCinemachineBackend` 守卫。**公开 API 基线 +1 类型（18 个成员），经用户确认**。
- 不做：**远程配置下发**（2026-10-11 框架级评估 §4.1；用户确认 2026-10-13）——与「平台 SDK 聚合」同性质：后端与渠道相关，适合项目侧包裹；而 `Config` 的 `IConfigLoader` 已经是「取配置」该有的那个接口。无重开判据。
- 不做：**崩溃上报**（2026-10-11 框架级评估 §4.1；用户确认 2026-10-13）——渠道相关（Bugly / Crashlytics / 自建），框架侧那一半已由 `Log` 给出——引擎 / 第三方 / 未捕获异常收进同一条 JSONL 时间线，正是上报系统要吃的输入。无重开判据。
- 不做：**红点系统**（徽标 / 未读计数 / 树形聚合）（2026-10-13 · 步骤 9 裁定）——与「新手引导」同一族：**树结构与父子计数是领域模型**、「红点怎么算」由策划配置决定——框架再包一层等于替项目规定一种红点模型。而**框架侧那一半已经给了**：`ReactiveProperty`（订阅即回调当前值 + 相同值去重）与集合变更流提供节点可观察性。**真问题另有其解**：树与聚合项目自建——Reactive 刻意只有一个 `Select`、**没有** `Where` / `CombineLatest` / 聚合算子（不做算子面，与 Event 模块同一取舍）。无重开判据。
- 不做：**画质 / 性能档位**（低 / 中 / 高 → 一组引擎参数）（2026-10-13 · 步骤 9 裁定）——与「统一框架 Profile」同族：框架给死的档位**几乎必然被改**（值的定义项目间差异极大）。**真问题另有其解**：项目自定义 settings POCO——`Validator` 用来钳范围（`QualitySettings.names.Length - 1`）、默认值工厂按设备性能定档（`SystemInfo.graphicsMemorySize > 4096 ? 3 : 1`，两例都在模块 README 里），载体是引擎自己的 `QualitySettings`；框架代码**零** `QualitySettings` / `vSyncCount` / `Screen.SetResolution`，这是刻意的。**别与 `Update` 的「更新档位」（`UpdateTier`）折叠**——那是 LOD 调度、是另一件事（它的「外部策略没有入口」已有三条解法）。无重开判据。

- 已做：**Audio 建成**（2026-10-01 · 候选模块第一个）——立项四条边界三条照原样落地（分类允许自定义 → 纯字符串 + 推荐常量；音量不绑 Mixer；播放句柄 `readonly struct`）；**第三条被推翻**：「clip 来源两者都要」在评审后端到端重估——保留 `Play(AudioClip)` 重载会同时毁掉 `IAudioManager` 的后端中立性（Wwise 的事件、FMOD 的 `EventInstance` 都没有 `AudioClip`）与「资源生命周期只有一套语义」，最终**只走 location**。另多裁定一条立项时没列的边界：**句柄必须能被第三方实现构造**——照抄 `LockHandle` 的 `internal` 构造会把「整体替换引擎」这条路彻底堵死。理由与代价见 `Modules/Audio.md`。
- 已做：**Logger 建成**（2026-10-01）——立项门槛「答得出比 `Debug.unityLogger` + 自定义 `ILogHandler` 多给什么」**三条全部落地**（按分类过滤、零 GC 调用路径、前缀不靠手抄）；评审时另加用户提出的维度：**便于 AI 分析**（结构化 JSONL 落盘 + `logMessageReceivedThreaded` 全量捕获，引擎 / 第三方 / 未捕获异常进同一条时间线）。**与立项时的差异**：分类名定为「与迁移前的 `[标签]` **逐字**对应」，因此同一模块内历史遗留的多个标签各是一个分类（换来迁移期间控制台文本零变化、30+ 处 `LogAssert` 零改动）；机械提取纠正了两类误收，分类表定为 31 个（Reactive 与 Serialize 没有日志可回填）。回填作为独立批次、按模块拆成可独立 review 的提交（施工图在 `Modules/Log.md`）。
- 已做：**Timer 建成**（2026-10-01）——立项四条理由**全部落地**（可查询的 `Remaining`、零分配的可复用句柄、不漂移的固定间隔、与 Update 的 Tier 自动升降档联动）；**红线照原样执行**：暂停与时间缩放完全转接自 Update 的双时间轴，模块内没有一行暂停代码、也没有第二条时间口径（`TimerManager` 上刻意没有 `Pause`）。一轮额外裁定：**不做 `ITimerManager` 接口**——本仓「有接口」的分界线是「是否存在可替换的后端」（Audio → FMOD、Input → Rewired），Timer 没有。见 `Modules/Timer.md`。
- 已做：**ReactiveList 建成**（2026-10-11）——立项六条边界**全部照原样落地**（只做列表；单一变更流 + `readonly struct` 载荷 + `Reset` 兜底；不做 batch 与 Range；回调内改动的三种后果文档化；零 GC 且 `foreach` 装箱如实写明；`IReactiveList<T> : IReadOnlyList<T>` 且订阅不做立即回调）。**两处与立项时不同**：① 拟有的 `Version` 计数器在实现前砍掉——它是本仓无先例的新契约面，而主线程同步下「订阅后立即读一次」已是原子的一致点（列入 v1.1 候选）；② `Sort` 定为 `Sort(IComparer<T> comparer = null)`。另一处实现期修正：`foreach` 无论直接用还是经接口**都会**装箱枚举器（原以为只有接口路径会）。
- 已做：**ReactiveDictionary 建成**（2026-10-11）——立项六条边界**全部照原样落地**；**与立项时的差异只有一处**：词表原计划新建 `ReactiveDictionaryChangeKind`，实施前改判为**改名共享**（`ReactiveCollectionChangeKind`，39 处 / 6 文件，未发布成本最低）。**该条不能用 ReactiveList 的论证来理解**（下一轮别套错）：列表的依据是「同一个轮子被每个模块各写一遍」，字典逐条核对后 **0 处可替换既有设施**，依据是前瞻性使用方场景。见 `Modules/Reactive.md`。
- 已做：**场景管理（XScene）建成**（2026-10-13 · 原候选表最大的一条）——立项四条边界**全部照原样落地**（不做场景栈、不内建加载界面、不碰 UI、不给传参通道）；「最容易做过头」的立项风险**没有兑现**（四条边界全是「原语 + 文档」，没有一条变成新 API）。一轮额外裁定：**落点选 S2**——场景原语从 `IAssetManager` 移出，改由能力接口 `IAssetSceneLoader` 承载（第三方实现零影响）；「`XScene` 是唯一公开场景 API」是**职责与文档层**结论而非编译期封死（门面不能暴露 `Instance`，Asset 侧必须保留一个能力转发成员）。该轮还关掉了立项时未列出的三个**真实缺陷**（失败路径句柄从不 `Release`、取消不中止且此后无人收拾、按名字回查场景）。全文见 `Modules/Scene.md` 的「沿革」。
- 已做：**会话级复位——关闭域重载后第二个播放会话不再静默失效**（2026-10-12 落地 · 2026-10-14 手工验收结案）——八个门面 `ResetForNewSession`（六门面 + `Bootstrap.StageList` + `InstanceTracker`；Settings 是 2026-10-14 第二遍验收抓到的漏网第八个：门面 `Initialize` 告警早退 + 重放注册表跨会话累积 + `PauseNotifier` 计数不归零，`018aad6` + `4701bbf` 修复）；只丢引用不 `Dispose`。回归用例 `Tests/Runtime/Architecture/SessionResetTests`。**验收本身是判据的一部分**——「关闭域重载连跑两遍」脚本表达不了，只能手工：第一遍绿、第二遍红，修复后重跑两遍全绿。各模块落点见 `Modules/Input.md` 等的决策台账。2026-10-17 第二轮审计：Save/Data/Pool/Serialize/Update/Audio/Input/UI 的静态态与订阅流存在同族残留，已新增「会话边界该不该替使用方 Dispose / 清订阅」`待裁`条目统一裁定（见本账 §三）。
- 已做：**主线程断言三份副本——不抽公共防线**（2026-10-15 · `ac9a9da`）——Message 的 `MainThreadGuard`、Pipeline 的阶段写入断言、Asset 门面三份内联副本评估后**各自保留**：共同部分只有三行，而断言绑在哪个语义位置、文案与闩锁寿命逐模块不同，抽成公共防线会把三处各自的语义压成一处；同轮补齐 Asset 那份的零覆盖缺口（`Tests/Editor/Asset/AssetMainThreadGuardTests` 4 例，含「会话复位让提示重新具备」）。「不变量 vs 漏做」的区分标准见「把模块内部策略接口化」条。
- 已做：**公开 API 基线的形态——自建反射比对，不用 Roslyn 分析器 / APICompat**（2026-10-13 · `c86e37b` + `36b551e`）——**为什么不用现成方案**：Roslyn 分析器在 Unity 侧没有可提交的 csproj 挂载点（生成的 csproj 被 git 忽略），且 `PublicAPI.Shipped.txt` 要经 `AdditionalFiles` 传给分析器——那条路本仓走不通。现形态：`PublicApiBaselineTests` 反射渲染 Runtime 程序集的公开面成扁平行集合，与 `PublicApiBaseline.txt` 逐行比对，差异天然两分（「已消失 / 已变更」= 破坏性，「新增」= 源兼容）；更新走 `Tools/run-tests.ps1 -UpdateApiBaseline`（**只写盘、永不给绿灯**，绿灯由不带开关的重跑给出）。**Editor 程序集（5 类型 / 10 成员）不在射程内**。

- 不做：**无 CI——暂不接入**（2026-10-13 · 用户裁定）——当前 0 fork / 无 PR 流程，且三条门禁由人跑到今天**尚未发生过「漏跑门禁」的实际事故**：CI 今天买到的是「对一件没发生过的故障的保险」，代价是一条会腐烂的维护链（许可续期、版本可得性、冷导入）。**自托管 runner 被单独排除**（GitHub 明确劝阻把自托管 runner 挂到公开仓——fork PR 会在那台机器上执行任意代码）。重开判据（成文于 `CI.md` §四）：① 出现第二个贡献者或外部 PR 流程；② 发布节奏显著加快**且**实际发生过一次漏跑门禁；③ 仓库转私有。成本要点与「若重开的最小路径」见 `Documentation/CI.md`。
- 不做：**`Tests~` / 把测试移出包目录——不采纳（维持现状）**（2026-10-14 · 步骤 11）——`~` 在 Unity 里只是「隐藏 / 不导入」、**不是「不分发」**（本机 PackageCache 里第三方包的 `Documentation~` / `Samples~` / `Tests/` 都实实在在躺在磁盘上），且改名后**门禁会给假绿**（实测：双平台各报 0 用例而脚本退出码 0，该缺口已随轮补上）；「移出包目录」可行但收益不抵成本——代价实测：**499 个文件**（228 `.cs` / 268 `.meta` / 2 asmdef / 1 基线文本，共 **51,056 行**）+ **两处代码级硬路径**（`PublicApiBaselineTests.cs` 的基线路径、`AllocationRulerTests.cs` 的白名单常量）+ **96 行文档引用**（含 `CLAUDE.md` 8 处规则；`CHANGELOG` 的 11 处按本仓纪律属有意保留的历史）；而收益只是消费方少 ~499 个文件的导入——**不是「少编译 45k 行」**，因为测试程序集在导入方本就不产出（**裸消费方探针复核**：`file:` 引本仓包的工程里同工程五个官方包——inputsystem / ugui / timeline 系 / test-framework / SBP——的 `Tests/` 也一个都没产出 ⇒「`testables` 未列即不编译」是全局规则，不是本包特例）。重开判据：消费方实测反馈「导入耗时 / 包体」不可接受，或 Unity 改变 `~` / `testables` 的语义。
- 不做：**包在子目录——不重构（维持现状）**（2026-10-14 · 步骤 11）——`?path=` 是 UPM 的官方支持用法，「不能直接当子模块包用」**≠**「不支持子模块」（包 README「方式二」已给 `file:` 配方）；把包搬到仓库根能修好「子模块即包」，代价是全仓重构（测试壳 junction、两条 `Tools/*.ps1` 的仓库根推导、全部 `Application.dataPath` 推导、96 行文档引用）。重开判据：出现「必须用 submodule、且不接受 `file:` 配方」的真实使用方；或包要拆成独立仓库发布。
- 不做：**Git 钩子 / Issue·PR 模板 / `CONTRIBUTING` 三项**（2026-10-14 · 用户确认）——与 CI 项同源判据（0 fork / 无 PR 流程 / 未发生过漏跑门禁）；钩子另有硬成本（每次提交付约 82s 测试 + 90~180s 文档）；模板塑造的是**外部来件**而实测 0 issue / 0 PR；本仓流程真相已有两处（`Workflow.md` 管流程、`CLAUDE.md` 管规则），另起一份就是第二份真相。重开判据（与 `CI.md` 联动，届时**四项同轮重裁**）：① 出现第二个贡献者或外部 PR 流程；② 实际发生过一次漏跑门禁。
- 已做：**性能基准套件**（2026-10-21）——`Tests/Editor/Performance/XFrameworkPerformanceBenchmarks.cs`，6 个 `[Explicit]` 基准（Message / ReactiveProperty / ReactiveList / Pool / Update 1000 节点 / Serializer 往返），`[Category("Performance")]` 隔离、不进入全量门禁；结果在 `TestResults/run-*.xml` 的 `##performancetestresult2`。只测量、不做零 GC 断言（分配尺子在本环境不可用）。运行方式见 `Documentation/Workflow.md` §五。
- 已做：**示例覆盖面**（2026-10-21）——新增 `Samples~/Playable` 零资产可玩纵切（Bootstrap(Data→Save) + Data/Save/Message/Reactive/Timer/File/Log），保留 `Samples~/Example` 的模块级示例。**范围取舍**：Playable 刻意不依赖 Asset/UI/Audio——`AssetPlayMode.EditorSimulate` 需要项目的 `AssetBundleCollectorSetting`，没有它模拟构建会在「pack asset info is empty」失败；完整 UI 纵切等有真实消费方的 YooAsset 配置后再做。
- 已做：**包自身 Git URL 不钉修订（跟随 `main`）已在包 README 补说明**（2026-10-11 登记；2026-10-19 R3-ADJ-14 落地）——它与「第三方依赖必须钉 `#tag`」形成**有意的不对称**；README 写明原因、如何钉 `0.3.0`（`?path=` 必须在 `#` 之前），并提示 `0.3.0` 早于 Camera / Scene 且 `AssetPlayMode` 整数值与当前不同。
- 不做：**不替使用方 Dispose、不清使用方的订阅流**（2026-10-18 · 用户裁定）——会话复位的边界是「只丢引用」；实例清理与订阅流归使用方（谁创建谁清理）。SET-004 的句柄订阅流同样不清，只订正文档。无重开判据。
- 已做：**框架自己挂上的静态引擎订阅在会话复位时解绑**（2026-10-18 · `68db39a` / `0a079d3`）——`AssetManagerImpl` 的 `Application.lowMemory` 与默认 `InputSystemProvider` 的 `InputSystem.onDeviceChange` 都只 `-=`、不 Dispose；第三方实现自带的静态订阅由实现方自行退订（AST-008 / INP-002）。
- 已做：**文档示例可编译性守卫**（2026-10-21）——`Tools/check-samples.ps1` 生成临时 csproj，把 `Samples~/**/*.cs` 编进包程序集与 Unity 托管程序集；已接进 `check-docs.ps1 -Enforce` 作为第六项。红证：`#error` 注入后脚本退出 1，还原后 0 error。**边界**：只守编译，不守场景 / 预制体引用（Playable 场景另做了一次性 `EditorSceneManager.OpenScene` 探针，结果为 `SCENE_CHECK_OK`）；XML `<code>` 仍不参与编译。
- 已做：**公开 API 重载源兼容探针纳入流程**（2026-10-18 · `Workflow.md` §三）——新增/修改重载时手工做一次调用形状编译探针（含负向控制），不引入 Roslyn/机械守卫。
- 不做：**不为文档内容加运行时门禁**（2026-10-18 · 裁定）——`check-docs` 只守结构（格式、编译参与、`.meta`、台账结构、文件树一致），文档内容属实仍是人工判据。无重开判据。
- 待裁：**公开枚举整数值纳入 `PublicApiBaseline` 或迁移检查**（2026-10-19 · 第三轮审计 R3-AST-001）——`AssetPlayMode` 重排暴露：基线只守公开面的形状（名称/签名/可见性），不守枚举整数值与已序列化字段的默认值；CHANGELOG 迁移表只收编译器抓得到的命名/签名变更。是否把整数值纳入基线（或加一条独立的序列化兼容检查）未定。重开判据：出现第二个因枚举值重排导致的序列化兼容事故。
- 未做：**依赖清单从 asmdef 反查**（2026-10-19 · 第三轮审计 R3-INP-001）——`DependencyDeclarationTests` 只能证明「清单里的每项都一致」，不能证明「程序集引用的每项都在清单里」；`Unity.InputSystem` 漏列即由此存活。本轮只把 Input System 补进三处真相。重开判据：实施后关闭（出现可接受的反查映射方式）。
- 已做：**Cinemachine 后端纳入核心文档/编译门禁**（2026-10-22 · 并入核心包，关闭 R3-CIN-008）——扩展包删除、源码移入 `Runtime/Camera/Cinemachine/`，其源码 / `.meta` / README 文件树 / XML 文档 / 公开 API 基线 / 示例编译从此都在核心 `check-docs -Enforce` 与测试射程内；原「未做：扩展包纳入独立门禁」由并入结构性关闭。
- 已做：**相机模块全面重整（Cinemachine 唯一内置后端 / 统一句柄 / GetBlendOverride）**（2026-10-22 · 用户裁定）——删除 `UnityCameraManager`；命名 / 动态相机统一经 `Register` 返回 `CameraHandle`，接口只收句柄，字符串只是门面糖；目标动态 boost 到「场景内同通道最大 + 1」并恢复作者状态；过渡走 `CinemachineCore.GetBlendOverride` 请求 duration / style（不改 `DefaultBlend`）；`IsTransitioning` 只统计管理器自有非 cut 过渡；门面探测型读宽容。详见 `Modules/Camera.md` 与 `Cinemachine.md`。**该形状已于 2026-10-23 被下方从零重设计取代。**
- 已做：**相机模块从零重设计：string id + 相机栈 + 中立主面 + 原生逃生舱**（2026-10-23 · 用户裁定；`58a9b62` / `ebe10d7` / `849f199` / `6f99584` / `04661ea`）——删除 2026-10-22 的 `ICameraManager` / `CameraHandle` / `CinemachineCameraManager`；门面改为 `SwitchTo` / `Push` / `Pop` / `PopAll` / `Clear` + 四个 async + `SetFollow` / `SetLookAt` / `SetFieldOfView` / `Shake`；公开 `ICameraBackend` 原语 + internal `CameraDirector`；动态 id `@auto/N` + `CinemachineCameraRegistration`；外部 incoming 消息 id 为 `null`、`RequestSettled` 每请求一次。**该条的公开后端契约已于同日第二轮收为 internal（见下条）。** 详见 `Modules/Camera.md` 与 `Cinemachine.md`。
- 已做：**相机主面混合边界：gameplay id + 组合根 / 高级访问 Cinemachine + 后端 internal**（2026-10-23 · `e6d56db`）——`Initialize(CinemachineBrain)`、`Register(..., CinemachineVirtualCameraBase)`、`TryGetCamera(..., out vcam)`、`ActiveCamera` 使用 CM 类型；`ICameraBackend` / `CameraBackendActivation` / `ICameraBackendSessionReset` / `CinemachineCameraBackend` 收为 internal；gameplay 动词 / `CameraBlendStyle` / 消息 / 快照保持 string id 与中立类型；`ModuleBoundaryTests` 射程放宽为 `Runtime/Camera/`。理由：公开可替换后端无真实受众；保留 id 是为了让 gameplay 代码不出现 `using Unity.Cinemachine`。
- 不做：**不引入反射式自动会话复位**（2026-10-18 · 裁定）——与仓内「显式登记、零反射」取向一致；会话复位族继续显式登记（`AutoInitTests.Registry`）。无重开判据。
- 待裁：**最低 Unity 版本是否下调至 2022.3 LTS**（2026-10-21 · 静态扫描完成，探针未跑）——`package.json` 当前 `unity` 为 `6000.3`，2022.3 的在产项目无法安装。**静态事实**：Runtime 无 Unity 6 专属 API（`Awaitable` 零命中；`Time.timeAsDouble` / `FindAnyObjectByType` 均在 2022.3 射程内；无 `Rigidbody.linearVelocity` 一类改名）；四个依赖的最低版本均 ≤ 2022.3（Input System 1.19.0 恰好要求 2022.3）。**结构性差异**：TMP——Unity 6 由 UGUI 2.0 内置，2022.3 需另装 `com.unity.textmeshpro` 3.0.x，README 依赖表 / 安装器需要版本分支。**下一步（重开判据）**：装 `2022.3.62f3`，建裸探针工程按 README 装依赖、`file:` 引入本包，编译 Runtime + Editor 并跑全量；全部通过才改 `package.json` 的 `unity` 并同步 README / 安装器 / CHANGELOG。本机当前只有 `6000.4.5f1`，探针未执行——**不据静态扫描宣称兼容**。

## 四、复核记录

已否决项与「暂不做」项**逐条复核**的记录（2026-10-15 由当时的框架级评估报告 §六 迁入本账）。

> 依据用户裁定的口径：**逐条复核，有新证据才重开**。复核方式 = 重读否决理由 + 检查该轮探索是否产生了推翻它的事实。
> **结论（2026-10-11 轮）：10 条全部维持，0 条重开。** 逐条依据如下（原文与完整理由见本文不做组，此处只记复核动作与结论）。
> **2026-10-17 补充复核 1 条（相机管理，表末行）：推翻旧结论，改为立项。**

| 项 | 复核结论 | 依据 |
|---|---|---|
| 状态机 / 行为树 / ECS / 时间模型 / 生命周期树 | **维持** | 该轮无新事实。同轮的「场景管理」候选**特意做了对照**：它通过「是否要求使用方按某种架构组织游戏逻辑」这一判据，而本条各项都不通过 |
| Update 的「组」API | **维持** | 「组 → 组的生命周期 → 组内嵌套 → 组间顺序」这条推理链未被任何新事实动摇 |
| DI 容器（VContainer / Extenject） | **维持** | 该轮确认「可替换点」表（包 README）里的注入形态**刻意差异化**（`Initialize(实例)` / 工厂委托 / `SetInstance` / 无注入点四种并存），与容器统一化方向相反 |
| Tween / 缓动 / 动画 | **维持** | 无新事实 |
| Addressables | **维持** | 已选 YooAsset；该轮确认 `Asset` 的场景加载也走 YooAsset |
| 新手引导 | **维持** | 该轮确认 UI 侧「只做原语（遮罩挖洞），不做引导编排」的落地情况一致（`Modules/UI.md` 决策台账里「遮罩挖洞」是唯一的引导相关未做项，且形态就是「只给原语」） |
| 平台 SDK 聚合 | **维持** | 该轮新增的「远程配置 / 崩溃上报」两个候选**用的是同一条判据**（渠道相关 → 项目侧），该判据因此得到一次独立印证 |
| 网络同步框架 | **维持** | 与本文的「Network 基础层」条（暂不做）**不是同一件事**：否决的是**同步模型**（GamePlay 本体），**暂不做**的是**请求 / 重试 / 超时 / 主线程回调**（基础设施）。两者不要折叠 |
| 统一框架 Profile | **维持** | 三条重开判据逐条对照：①「只装编译期决定不了且与 Asset 引导无关的值」——该轮探索未发现这样的值；②「不替代模块 Options」——未满足；③「先回答引导顺序」——**2026-10-02 已满足，但原文已明写这不构成重开理由**（第二条判据排除的正是「中心化」这个形状） |
| 把模块内部策略接口化 | **维持** | 该轮新增一条**支持**该判据的实例：遮罩缺 `GraphicRaycaster` 是**缺陷**而非「模块不变量」，补上它不需要接口化任何东西——「不变量」与「漏做」的区分标准（本文的「把模块内部策略接口化」条，即「三档判据」的出处）经受住了一次实战检验 |
| 相机管理 | **推翻（改立项）** | 2026-10-17 用户给出真实场景「用 Cinemachine，但要统一入口」；一手核对 Cinemachine 3.1.7 源码（vcam 优先级 + Brain 混合）确认编排已由它覆盖。结论改为「核心包提供后端中立统一入口 + Unity 默认实现；Cinemachine 3.1.7 放独立可选扩展包，不硬依赖」；**2026-10-22 再改为硬合并并删除 Unity 默认实现 / 统一句柄**（Input System 先例 / Unity 6000.3 + CM3 默认 / 自研后端非真实受众）；**2026-10-23 从零重设计为 string id + 相机栈，并同日收敛为 gameplay id / 中立类型 + 组合根 / 高级访问 CM + 后端 internal**（见本账「相机模块从零重设计」与「相机主面混合边界」条），见「Cinemachine 3.1.7 后端硬合并进核心包」「相机模块全面重整」条 |

**另有一条该轮明确「不是重开理由」的**：本文的「App 生命周期」条维持「暂不做」。它的升级判据是**可判定的**（想接 `OnApplicationPause` 的地方达到 2~3 处，或有真实使用方提出）——该轮探索**未发现**新的消费者（仍是 `Settings` 自持宿主那一处）。**判据未触发，不做。**

> **「暂不做」项的判据复核**随框架级体检每轮回填（先例：本文「App 生命周期」条内的 2026-10-11 复核：未触发，维持不做）；复核结论追加在**本表**，不回写各条目。
>
> **2026-10-17 第二轮全面审计的判据复核**：Network / 代码热更 / GM 命令台 / App 生命周期四条 `暂不做` 的判据均未触发，维持；性能基准套件、示例覆盖面维持 `未做`；包自身 Git URL 说明维持 `待裁`。逐条依据见 `AuditRound2.md` §9.1。
>
> **2026-10-19 第三轮全面审计的判据复核**：R2 §9.1 的七条逐条复核——Network / 代码热更 / GM 命令台 / App 生命周期四条 `暂不做` 判据未触发，维持；性能基准套件、示例覆盖面维持 `未做`（R3-CONF-001 再次证明示例覆盖与质量是两件事）；包自身 Git URL 说明已落地（R3-ADJ-14），转 `已做`。逐条依据见 `AuditRound3.md` §12.1。
>
> **2026-10-20 第三轮探针轨结案**：`R3-PRB-06`（CONF-001 编译探针）与 `R3-PRB-13`（MSG-003 编译探针）审计期已执行；`R3-PRB-07/09/12/14` 随对应修复以「改前必红」用例执行；`R3-PRB-08` 因 `0.3.0` 未分发改为文档说明（AST-001）；`R3-PRB-10` 由源码核对与文档截断（AUD-001）；**`R3-PRB-11` 本轮已执行**——临时工程按包 README 的四个依赖 + `file:` 引入本仓包，批处理编译后 `Library/ScriptAssemblies` 实际产出 `Venusy609.Xframework.dll` 与 `...Editor.dll`、日志零 `error CS`（Input System 已随 INP-001 补入后通过；`Tests` 程序集未产出，印证 `testables` 全局规则）。`R3-PRB-01/02/03/04` 属手工 / 真机，已分别登记（PRB-01 → `Modules/Settings.md`，**2026-10-20 已执行并结案**；PRB-02/03 → `Modules/Config.md`；PRB-04 → `Modules/File.md`，仍 `未做` + 配方）；**`R3-PRB-05`（F8，关闭域重载连跑两遍）** 是跨模块项、按 `AuditRound3.md` §13.4 落在本条：2026-10-14 结案（见本文 §三「会话级复位」条），**2026-10-20 在本轮实施后复跑两遍 PlayMode 全量均全绿**（手工执行，设置已还原，仓内无残留）。配方见 `Documentation/Workflow.md` §六。

## 五、同类框架对照（快照）

> **这是快照，不是判据。** 对照对象（GameFramework / QFramework / ET / TEngine / YukiFrameWork / EasyFramework / Scylla / JKFrame / CocKleBurs）的信息来自 **2025-10 前后的公开资料汇总**，**未逐一到各自仓库一手核对**（标 `[未一手核对]`）；**本仓那一列是一手事实**（零 grep 命中 + 无目录）。用途是**支撑「该不该建」的论证**，不是「谁更强」的排名——本仓纪律是**判能力有无先一手核对**（读依赖源码 / 跑探针），这张表只作调研的**起点**。
> 2026-10-15 由当时的框架级评估报告（`FrameworkReview.md`，已删）§三 + §二 的「已经领先的地方」迁入——**决策早已在本账、流水在 `CHANGELOG`、过程档案按裁定不再保留**。

| 能力 | 同类普遍性 `[未一手核对]` | 本仓 | 判定 |
|---|---|---|---|
| 资源管理 | 人人都有 | ✅ Asset（YooAsset） | — |
| UI 管理 | 人人都有 | ✅ UI（含导航栈 / HUD / Tip / 对话框） | — |
| 事件 / 消息 | 人人都有 | ✅ Message + Event + Reactive | — |
| 对象池 | 人人都有 | ✅ Pool（对象 + 集合四件套） | — |
| 配置表 | 人人都有 | ✅ Config + 可选 Luban 扩展包 | — |
| 本地化 | 多数有 | ✅ Localization | — |
| 音频 | 多数有 | ✅ Audio | — |
| 存档 | 多数有 | ✅ Save + Data + Settings | — |
| 日志 / 调试台 | 多数有 | ✅ Log + Diagnostics（**覆盖面领先**） | — |
| 场景管理 | **几乎人人都有** | ✅ Scene（2026-10-13 建成：加载 / 配对卸载 / 激活 / 进度契约） | — |
| 热更编排 | 中大型框架都有 | ❌ 无编排模块——但 `Asset` 已把 YooAsset 的下载能力（队列并发 / 续传 / 单文件重试 / 主备站）包成门面 | **裁定「不建模块」**，见本账「热更编排」条 |
| 网络基础层 | 多数有（UGF 有 Network + WebRequest；**一手核对其源码**） | ❌ 零命中（唯一网络代码在 `File` 内部，不可复用） | **裁定「暂不做 + 可判定判据」**，见本账「Network 基础层」条。**不是已否决项，别折叠** |
| 代码热更 | TEngine 等以它为主打 | ❌ 零命中（无依赖 / 无 `link.xml` / 无 AOT 元数据 / 无构建钩子） | **裁定「不建 Runtime 模块 + 形态＝Editor 工具 + 配方文档」**（配方待真实接入再写），见本账「代码热更」条 |
| 状态机 / 流程 | UGF 有 FSM + Procedure；QFramework 有 | ❌ | **刻意不做**（本账不做组） |
| 实体 / GameObject 管理 | UGF 有 Entity | ❌ | **刻意不做**（本账不做组） |
| 红点 | 少数（YukiFrameWork 等） | ❌（`ReactiveProperty` + 集合变更流已给「框架侧那一半」） | **已否决**，见本账「红点系统」条 |
| 新手引导 | 少数且多标注「不完善」 | ❌ | **刻意不做**（本账不做组） |
| 画质 / 性能档位 | 少数 | ❌（`Settings` 的 POCO + Validator + 默认值工厂已给正解范式） | **已否决**，见本账「画质 / 性能档位」条 |
| GM 命令台 | 少数 | ❌（诊断窗口给了「Editor 工具 + Runtime 数据契约」那一半；另一半——文本输入 / 解析 / 权限——仓内没有原语） | **裁定「暂不做 + 可判定判据」**，见本账「GM / 调试命令台」条 |
| 相机管理 | 少（多交给 Cinemachine） | ✅ Camera（Cinemachine 唯一内置后端 + internal 后端契约；string id + 相机栈；gameplay 中立 / 组合根原生） | — |
| 埋点 / 统计上报 | 少（多属项目侧） | ❌（`ILogSink` 已给框架侧那一半） | **已半解决**（不单独立项），见本账「埋点 / 统计上报」条 |
| 远程配置下发 | 少（属后端） | ❌ | **已否决**，见本账「远程配置下发」条 |
| 崩溃上报 | 少（属渠道） | ❌（Log 的 JSONL 是框架侧那一半） | **已否决**，见本账「崩溃上报」条 |

**一句话**：**基础设施面本仓不输**（日志与诊断还领先）。「一局游戏怎么跑起来」那一层的编排——场景、热更、网络——曾是缺口：**场景已于 2026-10-13 补齐**，另外两条各有裁定（上表）。

### 已经领先的地方（同一张对照的优势列）

> 与对照表成对保留的理由：只列缺口，这张表读起来会像一份「到处都是坑」的清单，而事实不是。

| 项 | 为什么算领先 |
|---|---|
| **日志系统** | 六档分级 + 分类过滤 + 模板化零分配调用 + 每条一行 JSONL 落盘 + 引擎 / 第三方 / 未捕获异常收进同一条时间线。多数框架只包一层 `Debug.Log` |
| **诊断窗口** | 18 个模块面板 + 页签注册契约（`IDiagnosticPanel`）+ 可测的渲染核心。GameFramework 的 Debugger 是同类物，本仓的模块覆盖面更广 |
| **Update 档位时间切片** | `IUpdateable.OnUpdate` 返回 `UpdateTier`，调度器按时长自动升降档。这是 LOD 式更新调度，同行普遍没有 |
| **编排原语** | `Pipeline`（阶段串行 / 并行 / 加权进度 / 失败即停）+ `Bootstrap`（显式登记 + 相位装配 + 逆序清理）。多数框架把启动顺序写死在某个 Manager 里 |
| **模块级文档密度** | 23 个使用方 README + 23 个维护向模块文档（含决策台账）+ 审计判据手册 + 审计技能。这个密度在开源 Unity 框架里罕见 |
| **待办纪律** | 源码里 `TODO/FIXME/HACK` **真标记 0 处**——所有待办与已否决都收敛进文档层（本账 + 各模块台账） |
| **测试规模** | 45k 行 ≈ Runtime 1:1（当时口径），含架构守卫（模块边界 / 命名约定 / 自动初始化族清单 / 台账结构） |
