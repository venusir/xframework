# Bootstrap —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Bootstrap/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 沿革与已否决形状

- **反向清理原先散落在各引导节点的 `OnDestroy` 中**；现由 `Bootstrap.Shutdown` 按执行序逆序（相位降序）统一完成。
- **Phase 1 / 2 曾是 Lock / Message 的相位**：二者改为 `[RuntimeInitializeOnLoadMethod]` 自管理生命周期后被移出，这两个值随之空闲。
- **登记表自 2026-10-02 起支持注销**（`Unregister` / `Unregister<T>`）：此前「替换内置阶段」只能靠调用顺序（先 `Register` 再 `RegisterDefaults`），而 `GameLauncher.Awake` 会无条件登记默认组合，与使用方 `Awake` 的先后由 Unity 决定。

## 已评估未采纳与未决

（2026-10-01 一轮：「如何保证框架加载完成才执行 Update」的定向审计 + 门控方向裁定。该轮全部改动是**文档**，未碰任何行为。）

**已评估未采纳**：

- **GameLauncher 自动门控加载期派发**（`Awake` 门控 → `RunAsync` 完成/失败后 `finally` 恢复）：不采纳。会给 Bootstrap 引入到 Update 的新依赖，让可选启动器变成「有主张」；与节点系统删除时**刻意解除 GameLauncher ↔ Update 耦合**的沿革相反（GameLauncher 曾直接驱动 Update，后改为 Update 自注入）；且失败/中断路径存在「忘了恢复即整机静默冻结」的闩锁形状——Update 模块 2026-09 刚清除过同类故障。
- **Bootstrap 就绪句柄**（`IsCompleted` / 可 await 的完成信号）：不采纳。使用方在 `await RunAsync()` 后自行注册即为正解；新增静态完成信号要额外定义失败/取消/重复 `RunAsync` 的语义，收益不抵 API 面。若有第三方真实诉求可重开。

**未决**：

- **GameLauncher 启动途中销毁与在途 `RunAsync` 交叠**（推理，未实测）：`Start` 里 `RunAsync()` 未传 `destroyCancellationToken`，`OnDestroy` 无条件 `Shutdown()`——加载中途销毁时，清理会与在途启动交叠（最坏一条「启动失败」错误日志）。GameLauncher 是 `DontDestroyOnLoad`、正常仅在退出时销毁，影响面小；修法（传令牌 / Shutdown 前取消）未评估。

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

**已评估未采纳（2026-10-02 追加）**：

- **依赖图 / 拓扑排序**（`[CreateAfter]` 式声明）：不采纳。与「相位是模块约定值、简单确定」的取向相悖；需要细致定序的场景，可用公开的 `Pipeline.Create()` 在自定义阶段内自建子管线。
- **命名槽位 / Installer / DI 式注册**：不采纳。与 `CLAUDE.md`「不预设 GamePlay 架构」冲突——登记表是「初始化步骤列表」，不是容器。
- **scoped / 嵌套引导**：不采纳。静态服务架构下不存在第二个作用域。
- **best-effort（失败不致命）阶段标志**：不采纳。第三方在自家阶段内 `try/catch` 即可；登记级 flag 会把「失败是否致命」变成登记参数。
- **`RunAsync` 重入时返回共享任务（join 首次）**：不采纳（本轮只落「警告并忽略」）。要额外定义多个调用方令牌不一致的语义；已被 2026-10-01 的「就绪句柄」否决记录覆盖，重开判据在那条里。
- **`Stages` 换成只读包装防强转修改**：不采纳。为低危封装泄漏引入分配不值；`IReadOnlyList` + 「实时视图」的文档已足够。

（2026-10-02 追记：用户问「以 `GameLauncher` 为框架配置与模块编排核心是否更合理、框架自身模块初始化顺序是否确定」。顺序的答案是「六条通路、只有 Bootstrap 那条是契约」；核心化的答案是**不采纳**，但采纳了一个最小扩展点——两条结论见下。）

**已评估未采纳（2026-10-02 追记）**：

- **`GameLauncher` 作为框架的配置与编排核心**：不采纳。三条理由：① 与三处成文决定正面相撞——包 README「框架刻意**不提供统一的配置文件**……**参数即契约**」、架构的关键设计决策「服务不依赖统一入口」、本组件自己的定性「可选件、不是必需入口」；② 配置的形状是**代码不是数据**（`AssetInitOptions.RemoteServices` / `SaveOptions.CryptoProvider` 是服务实例，Inspector/资产序列化不了），且一半配置的生效时机**早于任何场景**（`LogOptions` 走 `SubsystemRegistration`、File 域根预热走 `BeforeSceneLoad`）——场景组件结构性地覆盖不到，装进去只能得到「半张脸」；③ 两处沿革**刻意不挂靠它**（删节点系统时解除 GameLauncher ↔ Update；Settings 落盘特意不挂它，原话「把落盘挂在一个可缺席的组件上会让本选项的承诺落空」），且它当前**零使用**（全仓无场景/预制体挂载）。**采纳的替代形状**：给它一个最小扩展点 `protected virtual void ConfigureStages()`（默认实现即 `RegisterDefaults()`，覆写即可带配置）——把「带配置」变成一次 override，而不是把配置中心化。
- **把 `Awake` / `Start` / `OnDestroy` 改成 `protected virtual`**：不采纳。`async void Start` 变 virtual 会诱使「override 不调 base」这类错误；`ConfigureStages` 已覆盖绝大多数定制需求。真出现需要改启动/清理时机的情形再开。

**未决（2026-10-02 追加）**：

- **Pipeline 侧子阶段粒度超时**：Bootstrap 每相位装配为一个 `ParallelStage`，而超时只在管线**顶层阶段**粒度存在——从 Bootstrap 侧无法为单个引导阶段设置超时，不响应取消的挂死阶段会挂死整个启动（已记进 Bootstrap README 的已知限制）。修法在 Pipeline（`ParallelStage` 支持子阶段超时），未立项。
- **静态登记表跨播放会话存活**：关闭域重载（Enter Play Mode Options → 取消 Reload Domain）时，上一轮播放登记的阶段不会被自动清除，而 `Clear()` 目前被文档标注为「测试复位用」。与 Update / Input 等模块的「会话级复位」是同一族问题（框架级，非本模块可独解）。
- **运行中 `Shutdown` 与在途 `RunAsync` 交叠**：与上面 GameLauncher 那条同源，仍未实测。

## 审计轮次

- **2026-10-01 定向**（单问题）：「如何保证框架加载完成才执行 Update？」。结论：不存在、也不打算有这样的门控——Update 自注入自驱动，真实防线是「使用方 `await` 后注册」。零代码改动，三条否决方向见上节。
- **2026-10-02 全量 A–F**：主题「编排方式对第三方自定义的友好度」。方法：源码全读（Bootstrap + Pipeline 全部文件）+ 测试/文档/样例普查 + 五框架启动流程对照（见上表）。**七项发现全部落地**：① `Shutdown` 改为按相位逆序；② 新增 `Unregister` / `Unregister<T>`；③ 公开 `BootstrapPhases` 常量；④ `FailureReason` 带失败阶段名；⑤ `RunAsync` 重入守卫 + 运行期登记表警告；⑥ `Shutdown` 未执行契约与无超时手段的已知限制；⑦ 本文档的对照表与已评估未采纳。上一节的两条既有未决原样保留。

  **可复用的一条**：这次「`Shutdown` 用逆登记序」是典型的「实现与自己文档里的理由相悖」，而**框架自己推荐的用法**（先 `Register` 再 `RegisterDefaults`）恰好就是触发条件——查时序类缺陷时，把「文档推荐的用法」代进实现走一遍，比顺序读代码更容易撞见。
  **另一条**：相位号升为常量后，「插进框架阶段之间」的正解是 `Unregister` 换掉内置阶段自行编排，而不是赌「空闲值」——已写进 README 的相位约定。
