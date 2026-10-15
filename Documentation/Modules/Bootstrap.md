# Bootstrap —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Bootstrap/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 沿革与已否决形状

- **反向清理原先散落在各引导节点的 `OnDestroy` 中**；现由 `Bootstrap.Shutdown` 按执行序逆序（相位降序）统一完成。
- **Phase 1 / 2 曾是 Lock / Message 的相位**：二者改为 `[RuntimeInitializeOnLoadMethod]` 自管理生命周期后被移出，这两个值随之空闲。
- **登记表自 2026-10-02 起支持注销**（`Unregister` / `Unregister<T>`）：此前「替换内置阶段」只能靠调用顺序（先 `Register` 再 `RegisterDefaults`），而启动器在 `Awake` 里会无条件登记默认组合，与使用方 `Awake` 的先后由 Unity 决定。
- **`GameLauncher` 自 2026-10-02 起拆为两层**：抽象底座 `GameLauncher`（只有生命周期接线 + `abstract ConfigureStages`，**零序列化字段**）与默认实现 `DefaultGameLauncher`（八个 Inspector 字段 + 字段驱动的登记）。拆的理由：Unity 无法隐藏继承来的序列化字段，「完全自控的人 Inspector 里干净」只能靠类型层次保证；`abstract` 顺带让编译器兜住「忘了覆写」。**这是破坏性变更**——原先 `class X : GameLauncher` 的代码编译不过（迁移说明见 `CHANGELOG`）。
- **`Shutdown` 自 2026-10-13 起先中止在途 `RunAsync`、再清理**（`948aadb`；`GameLauncher.Start` 同时改传 `destroyCancellationToken`）：此前清理与在途运行互不感知——「被 `Shutdown` 过的后置相位仍会在自己的 `Shutdown` 之后被执行」。修法（Bootstrap 内部链接取消源）与两条实现硬约束见下节未决处置。

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

## 审计轮次

- **2026-10-01 定向**（单问题）：「如何保证框架加载完成才执行 Update？」。结论：不存在、也不打算有这样的门控——Update 自注入自驱动，真实防线是「使用方 `await` 后注册」。零代码改动，三条否决方向见上节。
- **2026-10-02 全量 A–F**：主题「编排方式对第三方自定义的友好度」。方法：源码全读（Bootstrap + Pipeline 全部文件）+ 测试/文档/样例普查 + 五框架启动流程对照（见上表）。**七项发现全部落地**：① `Shutdown` 改为按相位逆序；② 新增 `Unregister` / `Unregister<T>`；③ 公开 `BootstrapPhases` 常量；④ `FailureReason` 带失败阶段名；⑤ `RunAsync` 重入守卫 + 运行期登记表警告；⑥ `Shutdown` 未执行契约与无超时手段的已知限制；⑦ 本文档的对照表与已评估未采纳。上一节的两条既有未决原样保留。

  **可复用的一条**：这次「`Shutdown` 用逆登记序」是典型的「实现与自己文档里的理由相悖」，而**框架自己推荐的用法**（先 `Register` 再 `RegisterDefaults`）恰好就是触发条件——查时序类缺陷时，把「文档推荐的用法」代进实现走一遍，比顺序读代码更容易撞见。
  **另一条**：相位号升为常量后，「插进框架阶段之间」的正解是 `Unregister` 换掉内置阶段自行编排，而不是赌「空闲值」——已写进 README 的相位约定。
