# Asset —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Asset/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Asset/
├── IAssetManager.cs               # 资源管理器公共接口
├── AssetManager.cs                # 静态外观（全局入口）
├── AssetManagerImpl.cs            # 默认实现（对象池 + 生命周期管理）
├── YooAssetManagerImpl.cs         # YooAsset 底层适配器（多包管理 + 热更链路 + 远端 / 解密适配）
├── IAssetRemoteServices.cs        # 远端资源地址服务接口（主站 / 备站）
├── IAssetDecryptionServices.cs    # 资源解密服务接口（镜像 YooAsset 的 IDecryptionServices）
├── IAssetPoolController.cs        # 资源实例池的受控清理能力（可选能力接口）
├── IAssetSceneLoader.cs           # 场景加载与卸载能力（可选能力接口；2026-10-13 从 IAssetManager 移出）
├── AssetSceneHandle.cs            # 场景句柄（只读结构体；公开构造，Dispose = 即发即忘卸载）
├── ISceneLoadOperation.cs         # 场景加载操作的最小面（internal 测试缝）
├── ISceneLoadSource.cs            # 场景加载来源缝（internal 测试缝）
├── YooAssetSceneLoadSource.cs     # 缝的默认实现：包住 YooAsset.SceneHandle（internal）
├── IAssetLoadWait.cs              # 资产加载等待缝（internal 测试缝）+ HandleBase 适配
├── AssetInitOptions.cs            # 初始化配置（包名 / 运行模式 / 低内存回收 / 远端地址服务 / 解密服务）
├── AssetInitReport.cs             # 初始化进度载荷（readonly struct，中性，不依赖管线）
├── AssetInitProgressRelay.cs      # 进度直写桥：AssetInitReport → PipelineStageContext
├── AssetBootstrapStage.cs         # 引导阶段：资源管理器初始化（Phase 0，最早）
├── AssetOperationStatus.cs        # 资源操作状态（四句柄共用）+ 与 YooAsset.EOperationStatus 的唯一换算
├── AssetHandle.cs                 # 资源句柄（只读结构体，委托 YooAsset.AssetHandle）
├── AssetDownloaderHandle.cs       # 下载器句柄（事件/控制/等待）
├── SubAssetsHandle.cs             # 子资源句柄（图集/多 Sprite）
├── RawFileHandle.cs               # 原始文件句柄（txt/json/二进制）
└── InstanceTracker.cs             # 实例引用追踪组件（内部）
```

## 模块版本记录

模块内部的时间线（按年月记，非版本号）。部分条目在包 `CHANGELOG.md` 里有对应记录、部分没有，保留本表是为了不丢失引入时间线；包级变更见 CHANGELOG。

| 版本 | 说明 |
| --- | --- |
| 2026-08 | 并发初始化修复（共享任务 + 代际号）、低内存自动回收（AutoReclaimOnLowMemory）、LoadAllAsync 批量持有句柄、未初始化消息统一为 [模块] 前缀中文文案 |
| 2026-08 | 接口扩展：多包架构与初始化配置、卸载控制与存在性查询、热更链路（下载器句柄 + 一键下载）、预加载进度回调、同步加载 / 子资源 / RawFile |
| 2026-08 | 池语义修正（方案 A）：回池保活——回池保留句柄、真正销毁才释放；补齐取消支持与池状态统计 |
| 2026-10 | 审计轮（见下两节）：等待者按代际登记（作废路径不再静默）、非成功路径释放句柄、回池去重、门面完备性守卫（第九个采用者）、池 PlayMode 用例、下载器取消路径退订与未 Begin 拒绝、主线程契约（断言 + README 两节） |
| 2026-10-13 | 场景管理立项轮：场景原语**移出** `IAssetManager` 改由能力接口 `IAssetSceneLoader` 承载（破坏性）；场景加载三缺陷修复（失败路径释放句柄、取消后的后台善后、取 `SceneObject` 取代按名回查）；为可测性抽出 internal 场景加载缝（`ISceneLoadSource` / `ISceneLoadOperation`）。使用方文档的「已知限制」新增 4 条。详见 `Documentation/Modules/Scene.md` 与包 `CHANGELOG` |
| 2026-10-13 | **热更编排边界裁定轮**（框架级评估 §5.2 步骤 8）：① 四个句柄的 `Status` 由上游 `YooAsset.EOperationStatus` 换成框架自有 `AssetOperationStatus`（**破坏性**）——公开面不再绑上游类型，四成员一一对应、换算唯一实现在 `AssetOperationStatusMapper`（未知值回落 `None`，绝不回落 `Succeed`，见 `AssetOperationStatusTests`）；② 订正热更链路顺序：**激活必须先于下载**（预检不改变下载基准）——原文档两处 + 接口 XML 照抄会**静默空转**；③ 真缺口与「判给项目侧」的清单见下节 |
| 2026-10-18 | 新增 `AssetPlayMode.EditorSimulate`（仅编辑器；模拟构建由框架自动触发）；Web / Custom 明确不映射，理由与重开判据见决策台账 |

详细变更见 git log。

## 已修（2026-10-01）

- **`AssetInitOptions.PackageName` 此前是静默陷阱**：它只决定「初始化哪个包」（`InitializePackageAsync`），
  而加载族（`LoadAsync` / `InstantiateAsync` / 预载 / 场景）统一解析到硬编码的 `DefaultPackage`。
  于是使用方 `InitializeAsync(new AssetInitOptions { PackageName = "MyPack" })` 的真实结果是
  **初始化了 MyPack、却从一个刚被新建的空 DefaultPackage 上加载**——初始化看着成功、之后每次加载失败
  （DEBUG 下 YooAsset 直接抛）。修法：`InitializeAsync` 用 `ResolveDefaultPackageName` 解析主包名
  （非空即采用），并把解析提成可单测的纯函数。**这是行为修复，非 API 变更**：那条路本来就走不通。

## 决策台账

- 已做：**资产加载取消时释放已创建的 YooAsset 句柄**（2026-10-17 · `84dec73`，AST-001）——三条异步加载路径共用 `WaitOrReleaseAsync`，取消时 `Release()` 后重抛；为可测性抽出 internal 等待缝 `IAssetLoadWait`（照 `ISceneLoadSource` 先例）。真实 YooAsset 包上的「取消后 provider 不增长」仍无端到端用例。

（本节吸收原「已评估未采纳与未决」。逐条理由的展开在使用方 README 的 `## 已知限制` / `## 接口承诺到哪为止`，此处只留「评估过并否决」这一层；**下一轮从这里读起**。历轮：2026-10-01 模块审计（判据 A–F 全类扫过）；2026-10-02 覆盖清点；2026-10-13 热更编排边界裁定（框架级评估 §5.2 步骤 8，裁定全文与真缺口归属见 `../Ledger.md` 的「热更编排」条）；2026-10-17 Bootstrap 审计轮。）

- 已做：**引导阶段的 `Shutdown` 只销毁自己初始化的那份**（2026-10-17 · Bootstrap 审计轮）——`AssetBootstrapStage` 新增 `_initializedByThisStage`：已初始化早退路径不再在 `Shutdown` 里销毁使用方注入的管理器，与 Localization / Input / UI 的「谁初始化谁清理」对齐。回归用例 `BootstrapStageTests.AssetBootstrapStage_AlreadyInitialized_ShutdownDoesNotDestroy` / `..._InitializedByStage_ShutdownDestroys`（前者改前必红：`Expected: True But was: False`）。

- 未做：**真实 YooAsset 初始化路径的端到端覆盖（覆盖缺口）**（2026-10-02 · 覆盖清点，下一轮先看这条）——既有测试全走假实现（`FakeAssetManager` / `ImplFactory`），于是「离线模式的 `BuildinFileSystemParameters` 从未接线」这个**默认 PlayMode 直接启动失败**的缺陷活到了 2026-10-02 才被清点发现（修法与证据见 `CHANGELOG`）。本轮补上的一半是**参数映射单测**（`AssetDecryptionServicesTests`：`CreatePlayModeParameters` 提成 `internal` 后直测两种模式的产物 + 解密服务是否真的接进 YooAsset 参数，经反射读 `CreateParameters`）；**仍未覆盖的一半**是端到端初始化（需要构建产物，测试工程没有）——引入 Asset 侧集成测试（哪怕是**最小空包**）之前，凡是改动 `YooAssetManagerImpl` 与 YooAsset 的接线，都要**回到 YooAsset 源码逐行核对它的消费路径**，不要只靠编译通过。**2026-10-18 补充**：EditorSimulate 的 `SimulateBuild` 编排同样无端到端用例——本仓没有 YooAsset 构建配置资产，只能在消费方工程手测；参数接线由纯函数 `CreateEditorSimulateParameters` 的用例覆盖。
- 不做：**不整体改用 `AsyncLazy` / `UniTask.Preserve()` 重写门面的初始化协调协议**（2026-10-01 · 审计轮）——现有语义（同一异常实例、代际作废）有用例钉住，重写改动面大于收益；只修正了「promise 只支持单个 continuation」那句理由句的表述范围。无重开判据。
- 不做：**不合并门面与 `AssetManagerImpl` 的两层同构协议**（2026-10-01 · 审计轮）——实现层失败时刻意不销毁 `_managerImpl`，包复用语义保证重试安全。无重开判据。
- 不做：**不给 `AssetManagerImpl` 的同构协议补代际防护**（2026-10-01 · 审计轮）——它是 internal，且经门面使用时已被代际保护，那条路径不可达。无重开判据。
- 不做：**不统一 `[YooAssetManager]` 日志前缀为 `[AssetManager]`**（2026-10-01 · 审计轮）——内部适配层与门面的有意区分，信息量更大。无重开判据。
- 已推翻：**~~不给门面的 `_poolCapabilityWarned` 加复位入口~~**（2026-10-01 · 审计轮）——当时理由「进程级闩锁正是『只告警一次』承诺的实现；测试改用反射复位」。被 2026-10-12「会话级复位同时复位两个『只报一次』闩锁（`_poolCapabilityWarned` / `_mainThreadViolationLogged`）——跨会话仍只报一次，但换了会话就该重新提示」取代。
- 不做：**不优化 `PreloadAllAsync` 的每迭代闭包**（2026-10-01 · 审计轮）——低频批量路径，「频度是判据的前提」。无重开判据。
- 不做：**不把三个句柄从 readonly struct 改成类，也不给它们加双 Dispose 守卫**（2026-10-01 · 审计轮）——零分配是刻意设计；YooAsset 已兜底。无重开判据。
- 不做：**不删掉 `GetOrCreatePackage` 调用点的 `package == null` 防御分支**（2026-10-01 · 审计轮）——实测不可达，但保留零成本。无重开判据。
- 已推翻：**~~给 `UnloadUnusedAssetsAsync` 补 `operation.Status` 状态检查（审计取证：不检查即「失败静默」）~~**（2026-10-01 · 审计轮）——修复写完被**编译器当场拦下**（该操作没有 `LastError`）；读 YooAsset 源码确认 `UnloadUnusedAssetsOperation` **没有失败路径**（只置 `Succeed`，从不设 `Error`），**按纪律撤回修复**，改为就地注释记下已验证的事实——加一个恒真的检查是死代码，还会让人以为「失败会被报告」。被 2026-10-01「`UnloadUnusedAssetsOperation` 没有失败路径」取代。
- 不做：**不建「热更编排」模块**（2026-10-13 · 热更编排边界裁定）——原判「下载编排（队列 / 重试 / 续传 / 进度）是纯基础设施、可进框架」经核对依赖源码后不成立：**这一半 YooAsset 已经提供**（2.3.18 读包内源码逐条核对，清单见 `../Ledger.md` 该条），`Asset` 已把它包成 7 个门面成员 + 一个下载器句柄；框架再包一层只会与上游版本耦合地重复编排逻辑。无重开判据。
- 不做：**版本对比 / 整体失败重试（含续跑）/ 下载等待超时 / 更新决策（强制门槛 · 公告 · 灰度）/ 更新 UI 接线——一律判给项目侧**（2026-10-13 · 热更编排边界裁定）——本模块不接：判据是它们各自依赖「版本号语义」「退避策略」「运营口径」「界面契约」，四样全是项目的。**第三方对照（一手，2026-10-13 读 UGF 源码）**：UGF 有独立 `Download`（断点续传 + 并发下载器数）与 `WebRequest` 模块，但**没有任何**「更新策略 / 强制更新 / CDN」概念——更新流程由使用方自己的 Procedure 承担。无重开判据。
- 待裁：**预检与下载的版本绑定断了**（2026-10-13 · 源码核实；这是**接线缺陷**，不是缺口）——`PreDownloadContentAsync`（`YooAssetManagerImpl.cs:256-269`）只 `await` 预检 operation 取状态、**把 operation 丢掉**；而 `CreateDownloader`（`:289-298`）走 `package.CreateResourceDownloader(...)` → **`ActiveManifest`**（YooAsset 2.3.18 `ResourcePackage/PlayMode/PlayModeImpl.cs:107/113`）。于是「预检之后下载」拿到的是**旧激活清单**的缺口——**预下载静默空转**，激活之后才按需下载。**YooAsset 那边本就有这块能力**：`PreDownloadContentOperation.CreateResourceDownloader(...)` 基于它自己那份**未激活**的私有 `_manifest` 建下载器（`PreDownloadContentOperation.cs:104-112`），而激活清单是 `UpdatePackageManifestOperation.cs:81` 的职责——**是我们这层把它丢了**。**影响面**：`IAssetManager.cs` 里「成功后调用 `CreateDownloader` 会基于该版本创建下载器」那句承诺**不成立**，`Runtime/Asset/README.md` 的示例与「热更链路状态流转」也按这个不成立的顺序写（**2026-10-13 已把两处文档订正为可工作的顺序**）。**待决的形状**：要让「预检即可知道要下多少、下哪些」成立，得给使用方一个从预检产出的入口（待下字节数或一个下载器句柄）——那是**公开 API 新增**，另起一轮。
- 未做：**`Application.lowMemory` 的回调线程未证实**（2026-10-01 · 审计轮）——Unity 官方文档未声明该回调在哪个线程触发，而 `OnLowMemory` 直接触碰 GameObject（清池）与 `Resources.UnloadUnusedAssets`——若该回调可能在非主线程触发即为缺陷。**需真机实测**；未证实前不写进使用方文档。
- 未做：**Asset 加载链路缺 PlayMode 集成用例**（2026-10-01 · 审计轮）——`AssetHandle<T>` 只有一个 internal 构造、必须包真的 YooAsset 句柄，替身造不出来，于是几条**行为已修复却无法自动化验证**（类型不匹配释放句柄、失败路径释放句柄、回池保活与引用计数端到端）；建一套最小 YooAsset Offline 测试环境是后续选项。**（2026-10-01 部分收口）** 主包名解析已提出纯函数 `AssetManagerImpl.ResolveDefaultPackageName` 并由 `AssetPackageNameTests` 钉住三态——它此前零覆盖，而它决定的正是「初始化与加载是否同一个包」（见「已修（2026-10-01）」节）。**（2026-10-13 §4.2 处置）**：Runtime 侧现状为 2 fixture / 24 例（对象池语义 + 真 `SceneManager` 跨帧场景缝，后者由场景立项轮新增）——「Asset 的 Runtime 侧只有 1」的旧盘点已漂移；剩余部分不变：真 YooAsset 端到端初始化仍需构建产物，普通 PlayMode 用例补不到。
- 已做：**主线程断言第三份内联副本——不抽公共防线**（2026-10-15 · `Tests/Editor/Asset/AssetMainThreadGuardTests`（4 例）；判据「共享的那部分是不是有难度的那部分」见 `../Ledger.md` 的同名条）——Message 的 `MainThreadGuard`、Pipeline 的阶段写入断言、Asset 门面三份副本评估后都**各自保留**；本模块那份**补齐了两个缺口**：① 覆盖——新增 `AssetMainThreadGuardTests`（负向控制 / 闩锁 / 真接线 / 会话复位让提示重新具备），此前这份守卫**零覆盖**；② 门控对齐——闩锁字段 `_mainThreadViolationLogged` 移入 `#if UNITY_EDITOR`（三份里唯一在外的），该改动**无观测面**，属代码一致性。
- 已做：**会话级复位**（2026-10-12 · `ResetForNewSession`；回归用例 `Tests/Runtime/Architecture/SessionResetTests`，3 条已做改前必红）——域重载关闭时 `AssetManager._instanceInitialized` 跨播放会话存活，`AssetBootstrapStage` 见到 `IsInitialized` 为真即跳过初始化，第二个播放会话的 `_instance` 指向上一会话的实例（其 `_managerImpl` 持有已销毁的 YooAsset 包）；现在门面在会话开始时复位静态状态（`ResetForNewSession`，挂 `RuntimeInitializeOnLoadMethod` 的 `SubsystemRegistration` 档）。**比其它门面多两件事**：① 作废在途初始化（`InvalidateInflightInit` + 清任务）——上一会话半路中断的初始化不得在第二会话里让实例「复活」（含「在途初始化复活」那条回归用例）；② 复位两个「只报一次」闩锁（`_poolCapabilityWarned` / `_mainThreadViolationLogged`）——它们是**每会话**一次的提示（即上方「已推翻」那条的改判：跨会话仍只报一次，但换了会话就该重新提示）。配套新增 Runtime 侧最小假件 `Tests/Runtime/Asset/FakeAssetManager`（此前 Runtime 树只有 UI 侧那个池替身，它对 `InitializeAsync` 抛 NotUsed）。
- 已做：**`InstanceTracker` 的退出订阅改为具名 `AutoInit` 并登记进族清单**（2026-10-12 · `SessionResetTests.InstanceTracker_ResetClearsStaleActiveCounts`，已做改前必红）——它原用**静态构造函数 + 匿名 lambda** 订阅 `Application.quitting`：lambda 退订不了、按特性扫描的族清单**扫不到静态构造**、`_activeCounts` 也没有复位入口（`AssetPoolTests` 此前只能用「计数平衡」代替复位）；现改为具名 `AutoInit`（挂 `SubsystemRegistration`：会话开始清空计数 + 幂等 `-=`/`+=` 订阅）。`Ledger.md` 的「App 生命周期」条（原 `Roadmap.md` §三）那句「`Application.quitting` 六处……有族级守卫」当时对它是**失实**的，已订正。

- 已做：**会话复位退订旧实例的 `Application.lowMemory`**（2026-10-18 · `68db39a`）——`AssetManagerImpl` 抽出幂等 `Attach/DetachLowMemoryHandler`，复位在丢引用前退订（只 `-=`、不 Dispose）；第三方实现自带静态订阅时框架探测不到，README 已写明（AST-008）。
- 已做：**CLAUDE.md 豁免清单补 `AssetManager.UnloadSceneAsync`**（2026-10-18 · `deee388`）——它是第三处「未初始化也不抛」的宽容语义豁免（AST-009）。
- 已做：**运行模式范围裁定为 Offline / Host / EditorSimulate**（2026-10-18 · `f8a64bf`，用户裁定；`AssetDecryptionServicesTests.CreateEditorSimulateParameters_*`）——EditorSimulate 以显式枚举成员 opt-in，初始化时由框架调用 `EditorSimulateModeHelper.SimulateBuild` 自动模拟构建；枚举成员追加末位（`= 2`）避免既有场景反序列化错位；该模式用虚拟 bundle 直接读原始资源，故忽略 `DecryptionServices` / `RemoteServices`。
- 暂不做：**映射 WebPlayMode**（2026-10-18 · 用户裁定）——它仅 WebGL 可用，且需要 WebServer / WebRemote 文件系统参数与 Web 专用解密接口 `IWebDecryptionServices`（与现有 `IDecryptionServices` 形状不同）；框架其它模块在 WebGL 也均无开箱支持。重开判据：出现真实 WebGL 发布需求，且愿为它增加 Web 文件系统配置面。
- 不做：**映射 CustomPlayMode**（2026-10-18 · 用户裁定）——它要求使用方提供 `List<FileSystemParameters>`（YooAsset 类型），与「公开面不出现第三方类型」的原则冲突；整体替换 `IAssetManager` 已是该场景的出口。无重开判据。
- 不做：**EditorSimulate 在非编辑器平台静默降级为 Offline**（2026-10-18 · 实现裁定）——静默降级会让「模拟模式误打进包」永远不被发现；初始化失败必须显式可见。无重开判据。
- 暂不做：**缓存 SimulateBuild 产物**（2026-10-18 · 上游行为核对）——YooAsset 每次调用都会重跑模拟构建；框架加缓存会引入失效判定与包复用语义冲突。重开判据：实测模拟构建成为进入播放的显著瓶颈。

## 审计轮次

2026-10-01 按 `../ModuleAudit.md` 审计一轮（判据 A–F 全类扫过，逐类「扫过、无问题」的留痕在当轮提交信息里；
修了什么见 `CHANGELOG` 的 `[0.3.0]`）。本节只留**下一轮需要知道的**。

**范围**：由「审计 `AssetManager.cs`」起，经裁定扩至整个模块（门面 + impl + YooAsset 适配层 + 三个句柄 +
下载器 + 池 + 引导阶段）。

**本轮最重要的两条方法论回报**：

1. **跨代际广播（高危）改前必红**：`_initWaiters` 是全局列表而广播不区分代际，`Destroy()` 后旧代际收尾会
   放行新代际的加入者（假成功），或把旧代际的异常广播给它。写用例时**实测复现**：5 条红，其中一条**无关**
   用例因旧实现泄漏的异常被 UniTaskScheduler 播出而连带判红——「并发缺陷的冒烟用例绿色不构成证据」的又一例。
2. **一条发现被编译器当场证伪**（`UnloadUnusedAssetsAsync` 的状态检查，见上节）：取证读的是「其余 14 处都有
   检查」这个**模式**，而不是「YooAsset 那个操作能不能失败」这个**事实**。下一轮取证时先问后者。

**新增判据**：B6 共享协调协议代际覆盖面 —— 已写入 `ModuleAudit.md` 第三节。

**本轮未审到的**：YooAsset 适配层的 `CreatePlayModeParameters` 参数组合（Host/Offline 双文件系统的取舍）
只做了「与 README 承诺一致」的核对，未评估参数本身的合理性（如 `AutoUnloadBundleWhenUnused` 默认关闭是否
是最优——它决定了 `PreloadAsync` 的「秒回」承诺成立与否，实测成立）。
