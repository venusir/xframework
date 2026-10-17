# Bootstrap —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Bootstrap/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Bootstrap/
├── Bootstrap.cs            # 静态门面：登记表（Register / Unregister / Clear / Stages）+ RunAsync / Shutdown
├── IBootstrapStage.cs      # 阶段契约：IPhaseStage + 一个同步 Shutdown
├── BootstrapPhases.cs      # 框架内置阶段的相位号常量（0–89 由框架保留）
├── GameLauncher.cs         # 启动器底座（MonoBehaviour）：不含任何配置字段，ConfigureStages 为抽象
└── DefaultGameLauncher.cs  # 开箱默认启动器：底座 + 五组 / 八个 Inspector 字段

Tests/Runtime/Bootstrap/    # GameLauncherTests、GameLauncherFrameTests（用例 3 是「启动途中销毁」的端到端证据）
Tests/Editor/Bootstrap/     # BootstrapTests（登记 / 执行 / 清理语义）、BootstrapStageTests（内置阶段直连）
```

（本节 2026-10-16 补建——此前是 23 份里唯二没有文件结构的一份。）

## 沿革与已否决形状

- **反向清理原先散落在各引导节点的 `OnDestroy` 中**；现由 `Bootstrap.Shutdown` 按执行序逆序（相位降序）统一完成。
- **Phase 1 / 2 曾是 Lock / Message 的相位**：二者改为 `[RuntimeInitializeOnLoadMethod]` 自管理生命周期后被移出，这两个值随之空闲。
- **登记表自 2026-10-02 起支持注销**（`Unregister` / `Unregister<T>`）：此前「替换内置阶段」只能靠调用顺序（先 `Register` 再 `RegisterDefaults`），而启动器在 `Awake` 里会无条件登记默认组合，与使用方 `Awake` 的先后由 Unity 决定。
- **`GameLauncher` 自 2026-10-02 起拆为两层**：抽象底座 `GameLauncher`（只有生命周期接线 + `abstract ConfigureStages`，**零序列化字段**）与默认实现 `DefaultGameLauncher`（八个 Inspector 字段 + 字段驱动的登记）。拆的理由：Unity 无法隐藏继承来的序列化字段，「完全自控的人 Inspector 里干净」只能靠类型层次保证；`abstract` 顺带让编译器兜住「忘了覆写」。**这是破坏性变更**——原先 `class X : GameLauncher` 的代码编译不过（迁移说明见 `CHANGELOG`）。
- **`Shutdown` 自 2026-10-13 起先中止在途 `RunAsync`、再清理**（`948aadb`；`GameLauncher.Start` 同时改传 `destroyCancellationToken`）：此前清理与在途运行互不感知——「被 `Shutdown` 过的后置相位仍会在自己的 `Shutdown` 之后被执行」。修法（Bootstrap 内部链接取消源）与两条实现硬约束见下节未决处置。
- **「`Unregister` 与调用顺序无关」的承诺于 2026-10-17 收窄**：此前 README / XML 声称替换内置阶段与调用顺序无关、在 `DefaultGameLauncher` 在场时同样可用。控制流复核显示启动器在自己的 `Awake` 里**无条件**登记 Asset、并用 `Unregister<SaveBootstrapStage>()` 摘掉所有 Save 阶段——使用方组件的 `Awake` 先跑时仍会被覆盖或双份登记。README 现明确：唯一与 `Awake` 顺序无关的路径是继承启动器覆写 `ConfigureStages`；外部 `Awake` 的替换不保证；`RegisterDefaults` 与启动器混用也会产生同相位双份。

## 决策台账

（2026-10-01 一轮：「如何保证框架加载完成才执行 Update」的定向审计 + 门控方向裁定。该轮全部改动是**文档**，未碰任何行为。）

- 不做：**GameLauncher 自动门控加载期派发**（`Awake` 门控 → `RunAsync` 完成/失败后 `finally` 恢复）（2026-10-01 · 定向审计）——会给 Bootstrap 引入到 Update 的新依赖，让可选启动器变成「有主张」；与节点系统删除时**刻意解除 GameLauncher ↔ Update 耦合**的沿革相反（GameLauncher 曾直接驱动 Update，后改为 Update 自注入）；且失败/中断路径存在「忘了恢复即整机静默冻结」的闩锁形状——Update 模块 2026-09 刚清除过同类故障。无重开判据。
- 暂不做：**Bootstrap 就绪句柄**（`IsCompleted` / 可 await 的完成信号）（2026-10-01 · 定向审计）——使用方在 `await RunAsync()` 后自行注册即为正解；新增静态完成信号要额外定义失败/取消/重复 `RunAsync` 的语义，收益不抵 API 面。重开判据：出现第三方真实诉求。
- 已做：**GameLauncher 启动途中销毁与在途 `RunAsync` 交叠——实测坐实并「彻底修」**（2026-10-13 · `948aadb`，`Tests/Runtime/Bootstrap/GameLauncherFrameTests`）——原登记为「推理，未实测」：`Start` 里 `RunAsync()` 未传 `destroyCancellationToken`，`OnDestroy` 无条件 `Shutdown()`。实测复现的完整链：销毁 → `Shutdown` → `AssetBootstrapStage.Shutdown()` → `AssetManager.Destroy()` 递增初始化代际 → 在途初始化完成时代际校验抛异常 → 管线失败 → `Start` 记 `[GameLauncher] 启动失败`（共**三条 Error**：`Parallel stage failed` / `Pipeline failed` / `启动失败`）。**另实测出一条本文档此前没记、比预言更重的后果**：`Shutdown` 清理的是**整张登记表**（含从未执行的后置相位），而在途管线是装配期快照、不受清理影响——**被 Shutdown 过的阶段仍会在自己的 Shutdown 之后被执行**（用例 2 的直接证据：其 `ExecuteAsync` 开始那一刻 `ShutdownCount` 已为 1）。影响面仍小（`DontDestroyOnLoad`、正常仅退出时销毁），但「清理后还会被初始化」比「空转安全」重一档。**修法候选与实测效果**：`Start` 改传 `destroyCancellationToken`（`RunAsync` 早已把令牌链接进管线，缺的只是调用方传）——临时注入实测：管线在阶段边界停住、后置相位不再执行（用例 2 的钉现状断言在注入修法后**精确翻转**）。**覆盖边界如实记录**：不响应取消的在途阶段仍会失败并产同一条错误链——修法只覆盖「令牌可穿透 + 阶段边界」两条路。**实际采纳的修法比原候选多一半**（2026-10-13 用户裁定「彻底修」并落地）：除 `Start` 传 `destroyCancellationToken`（纵深防御）外，主体是 **Bootstrap 内部的链接取消源**——`RunAsync` 在重入守卫之后创建 `_runCts = CreateLinkedTokenSource(调用方令牌)` 并把链接令牌传给管线，`Shutdown` 第一步 `Cancel()` 它再按相位逆序清理，`ResetForNewSession` 一并中止并丢弃；**第三方不传令牌直接调 `RunAsync` 的交叠由此一并覆盖**（下一条同此关闭）。两条写错会红的实现硬约束：① 取消源创建不得早于重入守卫（否则二次调用覆写字段、首次运行的收尾误 Dispose 掉别人的取消链）；② `finally` 只 Dispose 创建时捕获的局部变量、`ReferenceEquals` 判身份后才清字段（`ResetForNewSession` 在途时清 `_isRunning`，允许新旧运行并存）。**终局 OCE 改携带链接令牌**而非调用方令牌（Shutdown 触发时后者并未取消）——行为变更已进 CHANGELOG。**覆盖边界保留**：用例 3 原样保留（假实现忽略令牌，三条 Error 链即此边界的证据）；用例 2 的钉现状断言按预言精确翻转（`later.ExecuteCount == 0`）；第三方路径由新用例 `BootstrapTests.Shutdown_DuringInFlightRun_CancelsRunAndSkipsLaterPhases` 钉住（OCE 令牌已取消、`_isRunning` 已释放、后置相位不执行），两处均做过改前必红。

---

（2026-10-02 二轮：**A–F 全量审计**，主题「编排方式对第三方自定义的友好度」。发现清单与修复见下节「审计轮次」；本节的结论按「下一轮要不要重新论证」筛选。）

**对照表（第三方启动流程，2026-10-02 新增）**：

| 框架 | 扩展形状 | 覆盖/替换 |
|---|---|---|
| Unity Entities `ICustomBootstrap` | 实现接口整体接管，`return false` 回落默认 | 整体替换或回落——本仓「不调 `RegisterDefaults`」已等价，缺的只是**局部替换**（本轮已补 `Unregister`） |
| Zenject / Extenject | Installer 组合；`BindExecutionOrder(priority)` | 后装 Installer 覆盖先装——顺序是可编程表达，本仓相位是类型级声明 |
| VContainer | 按接口分相（`IInitializable`→`IStartable`→…），同相无顺序保证 | 子 `LifetimeScope` 覆盖父——本仓相位模型与它同构，但没有 scope 这一层 |
| GameFramework / UGF | Procedure FSM：派生 `ProcedureBase` + 配表登记 | 热更后销毁 FSM 重建、换流程表——本仓**没有**「重跑/重建」语义 |
| MS.Extensions.Hosting | `IHostedService`（注册序=启动序）+ `IStartupFilter` 包住启动动作 | Options 按 `Order` 多方贡献；「包装启动」这一形状本仓没有 |

- 不做：**依赖图 / 拓扑排序**（`[CreateAfter]` 式声明）（2026-10-02 · 二轮审计）——与「相位是模块约定值、简单确定」的取向相悖；需要细致定序的场景，可用公开的 `Pipeline.Create()` 在自定义阶段内自建子管线。无重开判据。
- 不做：**命名槽位 / Installer / DI 式注册**（2026-10-02 · 二轮审计）——与 `CLAUDE.md`「不预设 GamePlay 架构」冲突：登记表是「初始化步骤列表」，不是容器。无重开判据。
- 不做：**scoped / 嵌套引导**（2026-10-02 · 二轮审计）——静态服务架构下不存在第二个作用域。无重开判据。
- 不做：**best-effort（失败不致命）阶段标志**（2026-10-02 · 二轮审计）——第三方在自家阶段内 `try/catch` 即可；登记级 flag 会把「失败是否致命」变成登记参数。无重开判据。
- 暂不做：**`RunAsync` 重入时返回共享任务（join 首次）**（2026-10-02 · 二轮审计，本轮只落「警告并忽略」）——要额外定义多个调用方令牌不一致的语义；已被 2026-10-01 的「就绪句柄」否决记录覆盖。重开判据：同「就绪句柄」条（出现第三方真实诉求）。
- 不做：**`Stages` 换成只读包装防强转修改**（2026-10-02 · 二轮审计）——为低危封装泄漏引入分配不值；`IReadOnlyList` + 「实时视图」的文档已足够。无重开判据。

（2026-10-02 追记：用户问「以 `GameLauncher` 为框架配置与模块编排核心是否更合理、框架自身模块初始化顺序是否确定」。顺序的答案是「六条通路、只有 Bootstrap 那条是契约」；核心化的答案是**不采纳**，但采纳了一个最小扩展点——两条结论见下。）

- 不做：**`GameLauncher` 作为框架的配置与编排核心**（2026-10-02 · 追记）——三条理由：① 与三处成文决定正面相撞——包 README「框架刻意**不提供统一的配置文件**……**参数即契约**」、架构的关键设计决策「服务不依赖统一入口」、本组件自己的定性「可选件、不是必需入口」；② 配置的形状是**代码不是数据**（`AssetInitOptions.RemoteServices` / `SaveOptions.CryptoProvider` 是服务实例，Inspector/资产序列化不了），且一半配置的生效时机**早于任何场景**（`LogOptions` 走 `SubsystemRegistration`、File 域根预热走 `BeforeSceneLoad`）——场景组件结构性地覆盖不到，装进去只能得到「半张脸」；③ 两处沿革**刻意不挂靠它**（删节点系统时解除 GameLauncher ↔ Update；Settings 落盘特意不挂它，原话「把落盘挂在一个可缺席的组件上会让本选项的承诺落空」），且它当前**零使用**（全仓无场景/预制体挂载）。**采纳的替代形状**：给它一个最小扩展点 `protected virtual void ConfigureStages()`（默认实现即 `RegisterDefaults()`，覆写即可带配置）——把「带配置」变成一次 override，而不是把配置中心化。无重开判据。
- 暂不做：**把 `Awake` / `Start` / `OnDestroy` 改成 `protected virtual`**（2026-10-02 · 追记）——`async void Start` 变 virtual 会诱使「override 不调 base」这类错误；`ConfigureStages` 已覆盖绝大多数定制需求。重开判据：出现需要改启动/清理时机的真实情形。
- 未做：**Pipeline 侧子阶段粒度超时**（2026-10-02 · 二轮审计）——Bootstrap 每相位装配为一个 `ParallelStage`，而超时只在管线**顶层阶段**粒度存在——从 Bootstrap 侧无法为单个引导阶段设置超时，不响应取消的挂死阶段会挂死整个启动（已记进 Bootstrap README 的已知限制）。修法在 Pipeline（`ParallelStage` 支持子阶段超时），未立项。
- 已做：**静态登记表跨播放会话存活——会话复位钩子落地**（2026-10-12 · `ebeaf4e`）——关闭域重载时上一轮播放登记的阶段不会被自动清除，
  而会话 2 的 `ConfigureStages` 只能**追加**新实例（`Register` 按实例去重、`RegisterDefaults` 按类型跳过）
  ——旧阶段实例留在表里被一起装配：同相位并行双跑（旧阶段握着上一会话已销毁场景的引用），
  退出时还会被双份 `Shutdown`（第二次 `AssetManager.Destroy` 撞上已置空的实例）。
  现在 `Bootstrap` 也有会话复位钩子（`ResetForNewSession`，挂 `RuntimeInitializeOnLoadMethod` 的
  `SubsystemRegistration` 档，早于首场景 `Awake` 因而早于 `GameLauncher.ConfigureStages`）。
  **`Clear()` 的定位不变**（测试复位用）——两者都只清登记表、不影响已初始化的服务。
  回归用例：`Tests/Runtime/Architecture/SessionResetTests.Bootstrap_ResetClearsTheStageRegistry`（已做改前必红）。
- 已做：**运行中 `Shutdown` 与在途 `RunAsync` 交叠——实测并随修复覆盖第三方路径**（2026-10-13 · `948aadb`）——`GameLauncherFrameTests` 用例 2/3 与上条同源同结论：`Shutdown` 不取消任何东西，在途管线（装配期快照）照常执行其剩余阶段；第三方在自己的代码里调 `RunAsync` 且不传令牌时交叠同样成立。**已随上条一并修复**：中止不依赖调用方传令牌（Bootstrap 内部链接取消源），第三方路径由 `BootstrapTests` 的新用例钉住。
- 已做：**运行中的 `RegisterDefaults` / `Clear` 补告警**（2026-10-17 · `f7e50df` · 重新审计）——README / XML 承诺「运行中修改登记表只告警」，但这两条路径静默改表；现在与 `Register` / `Unregister` 同形告警，并说明后果（新增阶段本轮不执行但会被 `Shutdown` 扫到；`Clear` 清掉的阶段在途仍执行但拿不到 `Shutdown`）。回归用例 `BootstrapTests.RegisterDefaults_WhileRunning_WarnsButProceeds` / `Clear_WhileRunning_WarnsButProceeds`（改前必红：`Expected log did not appear`）。
- 已做：**会话复位 × 在途运行的 `_isRunning` 代际修复**（2026-10-17 · `1b13809` · 重新审计）——`ResetForNewSession` 允许新旧运行并存，旧实现却在 `finally` 无条件清 `_isRunning`，会把新运行正在持有的守卫一起清掉，第三个 `RunAsync` 被错误放行、同一批阶段并发跑两遍。改用运行代际号做归属。回归用例 `BootstrapTests.ResetForNewSession_DuringInFlightRun_OldRunDoesNotClearNewRunsGuard`（改前必红：`Expected: 1 But was: 2`）。
- 已做：**Asset / Data / Save 引导阶段的清理所有权对齐**（2026-10-17 · `7d6a4be` + `8d9e650` · 重新审计）——三个阶段新增 `_initializedByThisStage`，`Shutdown` 只销毁自己初始化的那份，与 Localization / Input / UI 的「谁初始化谁清理」一致。**Asset 的标记放在 `await` 之前**：初始化在途时 `Shutdown` 仍须由本阶段作废在途任务——这条由全量 PlayMode 的 `GameLauncherFrameTests` 抓到（标记放在 `await` 之后会让失败链证据消失，`8d9e650` 修正）。回归用例 `BootstrapStageTests` 六个所有权用例（三条改前必红）。逐模块记录见 `Modules/Asset.md` / `Data.md` / `Save.md`。
- 不做：**`DefaultGameLauncher` 按相位补缺 / 同相位冲突告警**（2026-10-17 · 重新审计）——按相位补缺会改变字段面语义（外部登记的同相位阶段是否算「替换」无法判定），冲突告警则会把「使用方有意替换」也变成噪音；本轮采纳的是文档收窄：唯一与 `Awake` 顺序无关的路径是覆写 `ConfigureStages`。重开判据：出现第三方真实踩坑报告。
- 不做：**`BootstrapPhases.PreAsset` 常量 / 负相位契约**（2026-10-17 · 重新审计）——框架不为「早于 Asset」保留槽位；需要时在 `RunAsync` 之前自行初始化。公开常量会把一个未论证的位置变成承诺。重开判据：出现真实第三方诉求。
- 不做：**多启动器守卫**（2026-10-17 · 重新审计）——不给 `GameLauncher` 引入单例静态态；README 写明「同一会话只应有一个启动器」。重开判据：出现真实多启动器用法。
- 不做：**改 `BootstrapPhases.UserStart` 的值**（2026-10-17 · 重新审计）——它与 `Localization` 同值 90 是「不依赖本地化的业务阶段尽早开始」的有意取舍；依赖本地化的阶段取更大值，README 已写明。无重开判据。

## 审计轮次

- **2026-10-01 定向**（单问题）：「如何保证框架加载完成才执行 Update？」。结论：不存在、也不打算有这样的门控——Update 自注入自驱动，真实防线是「使用方 `await` 后注册」。零代码改动，三条否决方向见上节。
- **2026-10-02 全量 A–F**：主题「编排方式对第三方自定义的友好度」。方法：源码全读（Bootstrap + Pipeline 全部文件）+ 测试/文档/样例普查 + 五框架启动流程对照（见上表）。**七项发现全部落地**：① `Shutdown` 改为按相位逆序；② 新增 `Unregister` / `Unregister<T>`；③ 公开 `BootstrapPhases` 常量；④ `FailureReason` 带失败阶段名；⑤ `RunAsync` 重入守卫 + 运行期登记表警告；⑥ `Shutdown` 未执行契约与无超时手段的已知限制；⑦ 本文档的对照表与已评估未采纳。上一节的两条既有未决原样保留。

  **可复用的一条**：这次「`Shutdown` 用逆登记序」是典型的「实现与自己文档里的理由相悖」，而**框架自己推荐的用法**（先 `Register` 再 `RegisterDefaults`）恰好就是触发条件——查时序类缺陷时，把「文档推荐的用法」代进实现走一遍，比顺序读代码更容易撞见。
  **另一条**：相位号升为常量后，「插进框架阶段之间」的正解是 `Unregister` 换掉内置阶段自行编排，而不是赌「空闲值」——已写进 README 的相位约定。
- **2026-10-17 重新审计**：主题「第三方自定义实现的友好度」（承接 2026-10-02 的二轮）。方法：Bootstrap 五个源文件 + 六个内置阶段 + Pipeline 相位契约 + README / 维护向记录 + 全部 Bootstrap 测试逐条读；按 `ModuleAudit.md` 的 A–F 判据扫过（A1/A2/B3/B4/B6/D3/D4 有命中，B1/B2/B5/C1/C2/E/F 无问题）。**发现与处置**：① 收窄「替换与调用顺序无关」承诺（文档；唯一可靠路径 = 启动器 `ConfigureStages` 覆写）；② `RegisterDefaults` / `Clear` 运行期告警（`f7e50df`）；③ 会话复位代际修复（`1b13809`）；④ Asset / Data / Save 清理所有权对齐（`7d6a4be` + `8d9e650`，Asset 的在途初始化标记由全量 PlayMode 反推修正）；⑤ README 新增「行为契约」，补齐会话复位 / 重跑 / 单启动器 / 主线程 / pre-Asset 等第三方义务；⑥ 补 `RunAsync` 进度中继覆盖。**未做**：启动器按相位补缺 / 冲突告警、pre-Asset 常量、多启动器守卫（三条见台账）。

  **可复用的一条**：2026-10-02 引入 `Unregister` 时只验证了「同一段登记代码内」的顺序，没有把**框架自己的启动器**也代进推荐用法——「顺序无关」类承诺必须把所有会登记的组件（尤其是框架自己的 `Awake`）都走一遍。这与上一轮「把文档推荐的用法代进实现」是同一把尺子的两面：上一轮查的是**实现**，这一轮查的是**承诺的射程**。
