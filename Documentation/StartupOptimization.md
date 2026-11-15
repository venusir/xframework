# 启动方式评估与优化计划

> **状态：** 阶段 0–6 已实施（2026-11-15）；阶段 7 / 8 待裁。
> **同日后续裁定（2026-11-15，晚于本计划实施）：**「启动组合整体交还使用方」——`DefaultGameLauncher`、`BootstrapProfile`、`BootstrapStages.FromProfile` 与 `Bootstrap.RegisterDefaults` 已删除，保留 `GameLauncher` 底座与 `BootstrapStage.Create` 委托工厂；中央目录 `BootstrapStages` 同日稍后也删除。D2 / D3 / D7 的默认实现与目录一半随之中止，长期结论见 `Modules/Bootstrap.md` 台账。本文档保留原评估与计划快照，不重写。
> **范围：** Bootstrap / GameLauncher / BootstrapProfile / Pipeline 子阶段超时 / provider 替换路径 / 启动诊断与编辑器校验。
> **读者：** 维护者（实施本轮优化时）+ 下一轮审计者。
> **阅读顺序：** 先读 §0（最终结论与计划）；§1–§6 是评估依据，§10–§11 是追加论证；**冲突时以 §0 + §7 为准**。
> **真相分流：** 本文件只记评估、结论与计划；用户裁定后的长期结论仍按收件人分流到 `Assets/XFramework/Runtime/Bootstrap/README.md`（使用方契约）、`Documentation/Modules/Bootstrap.md`（模块决策台账）、`Documentation/Ledger.md`（跨模块结论）。
> **行号说明：** 下文行号以 2026-11-15 工作区为准；引用时优先看符号名。

---

## 零、最终结论与分步计划（2026-11-15）

> 本节是最终结论与阅读入口。**与早期章节冲突时以本节 + §7 为准。** §1–§6 是评估依据，§10–§11 是追加论证；§7 是唯一执行计划，§8 是裁定状态。

### 0.1 最终设计 D1–D10

| # | 结论 | 落地位置 |
|---|---|---|
| **D1** | **就绪面**：`BootstrapState` / `State` / `WaitUntilReadyAsync` / 重入 join / `GameLauncher.StartupTask`。批准本计划即视为重开两条 `暂不做`。 | 阶段 1 |
| **D2** | **Profile + launcher**：`BootstrapProfile` 装 7 个数据值；`DefaultGameLauncher` 只保留 `_profile` + `_uiRoot`；7 个数据字段移除；零配置语义不变；破坏性迁移。**2026-11-15 后续**：三者当日整体移除，Profile 形状移入示例。 | 阶段 3 |
| **D3** | **继承拆分保留**：`GameLauncher` 抽象零字段 + `DefaultGameLauncher` 两字段；合并留独立主版本。**2026-11-15 后续**：默认实现已删除，抽象底座保留。 | 阶段 3 |
| **D4** | **入口**：N0 启动场景 Launcher 主线；N3 自动启动根可选；N4 仅 headless/纯静态。 | 阶段 3/4、7 |
| **D5** | **遮罩悖论**：UI 启动流程必须有根对象；遮罩是表现层，不是 `IBootstrapStage`。 | §10.7 |
| **D6** | **Provider 替换**：无统一容器；`ConfigureStages` 是组合根；管理器级/provider 级/注册追加级；Profile data-only。 | §11 |
| **D7** | **登记便捷层**：`BootstrapStage.Create` + `BootstrapStages` 目录 + 内置不变量测试。**2026-11-15 后续**：目录已删除，工厂与不变量测试保留。 | 阶段 2 |
| **D8** | **编辑器校验 + 诊断面板升级**。 | 阶段 5 |
| **D9** | **Pipeline 子阶段超时**。 | 阶段 6 |
| **D10** | **内置阶段 provider/manager 参数 + `AudioBootstrapStage`**（中期、可延后）。 | 阶段 8 |

### 0.2 明确不做

- DI 容器 / Installer / `ProviderProfile` / provider 注册表；全局 Profile；N5 懒初始化。
- 删除 `GameLauncher`；本轮合并继承结构；Settings/Config 核心 stage（默认不做）。
- 把 provider 塞进 `BootstrapProfile`（data-only）。

### 0.3 裁定状态

- **已裁定（对话）**：P1 窄 Profile；P1-a + 移除 7 个数据字段；N0 启动场景 Launcher；保留 `GameLauncher`/`DefaultGameLauncher` 拆分；Profile data-only；阶段 8 作为中期改进方向。**2026-11-15 后续**：不再保留默认实现——启动组合整体交还使用方（见文首状态行）。
- **批准即裁定（推荐执行）**：就绪面与重入 join（重开两条 `暂不做`）；阶段 2 工厂/目录；阶段 5 编辑器校验；阶段 6 超时；阶段 3 零配置语义与迁移方案。
- **仍待裁**：阶段 7 的 Settings/Config 核心 stage 与 N3；阶段 8 的实际排期；模板/校验是否与阶段 3 同批。

### 0.4 历史章节处理

- §1/§2/§3/§4.2c 的「8 个字段」是 §10 裁定前的评估口径，不作为实施依据；实施口径是 `BootstrapProfile` + `_uiRoot`（2 字段）。
- §7 是唯一执行计划；§8 是裁定状态；§10/§11 只保留论证，不另写阶段编号。

---

## 一、背景与结论

用户对现有启动方式仍不满意，确认集中在三处：

1. **无就绪 / 进度 / 失败信号**：`GameLauncher.Start` 是 `async void`，失败只留日志；进度不转发；没有可 await 的完成信号。Integration 样例不得不自造 `AppReadyBootstrapStage`（相位 91）才能让业务流程知道启动结束。
2. **配置只覆盖一半（历史口径）**：原 `DefaultGameLauncher` 的 8 个 Inspector 字段之外，`RemoteServices` / `DecryptionServices` / `CryptoProvider` / 自定义 provider/controller 只能继承子类写代码；部分模块没有内置引导阶段。§10 已裁定改为 `BootstrapProfile` + `_uiRoot`（7 个数据字段移除），但「服务实例仍只能写代码」的结论不变。
3. **登记与相位太手工**：新增服务 = 实现 `IBootstrapStage` 类 + 手挑一个 int 相位 + 登记；`UserStart` 与 `Localization` 同为 90；`Save` 依赖 `Data` 只是注释约定，机器不校验。

### 1.1 总结论

| 痛点 | 判定 | 建议 | 需重开既有裁决 |
|---|---|---|---|
| 无就绪 / 进度 / 失败信号 | **真缺口**，三个里最值得做 | `Bootstrap` 增加运行状态 + 共享就绪任务 + 重入合并；`GameLauncher` 暴露 `StartupTask` | 是（两条 `暂不做`） |
| 配置只覆盖一半 | **大部分是结构性限制**，小部分是可改善的 ergonomics | 服务实例继续走代码通道；补子类模板与 stage 工厂目录；不建议 Installer 形状 | 否（维持 `不做`） |
| 登记与相位太手工 | **不是设计落后**（整数排序是业界常态），缺便捷路径与机器校验 | `BootstrapStage.Create` + `BootstrapStages` 目录 + 内置不变量测试（目录已于 2026-11-15 后续删除） | 否（维持 `不做`） |

外围另有四项，按性价比排序：**编辑器 / 构建时校验**（最高）、诊断面板升级、子阶段超时（模块台账唯一 `未做`）、文档订正（`Bootstrap.md` 的「零使用」已过期）。

### 1.2 一句话判断

启动内核（登记表 + 相位装配 + Pipeline + 逆序清理 + 取消传播 + 会话复位）在清理与取消纪律上强于本次对照的多数第三方，**不建议推倒重来**；薄的是消费侧边缘——启动器把运行结果藏起来了。

---

## 二、现状基线

### 2.1 强项（本轮不建议改动）

- **逆相位序 Shutdown**，同相位取登记逆序，保证确定性（`Bootstrap.Shutdown`）。
- **Shutdown 先取消在途运行，再清理**；取消回调异常隔离；登记表入口快照（R3-BST-001/003）。
- **阶段所有权判定**：只销毁自己初始化的实例；Asset 还带初始化代际比较（R4-AST-002）。
- **会话复位**：`ResetForNewSession` 挂 `SubsystemRegistration`，带运行代际守卫（`1b13809`）。
- **显式登记、零反射**；失败/取消在 `RunAsync` 处抛出而非吞日志。
- **管线契约**：同相位并行、加权进度聚合、失败即停、取消传播、终局状态可拉取。

这些是 R2–R5 审计逐个打磨出来的，任何优化都应绕过而不是重写它们。

### 2.2 消费侧薄弱点

| 薄弱点 | 位置 | 后果 |
|---|---|---|
| 启动器无完成/失败信号 | `GameLauncher.Start`（`Assets/XFramework/Runtime/Bootstrap/GameLauncher.cs:34-54`） | 业务侧只能自造阶段或轮询门面 |
| 重入静默返回 | `Bootstrap.RunAsync`（`Bootstrap.cs:287-291`） | 第二个调用方误以为启动完成 |
| 不转发进度 | `GameLauncher` 未使用 `RunAsync` 的 `IProgress` 参数 | 启动画面拿不到进度 |
| 服务实例不可序列化 | 原 8 个字段之外全在代码里（§10 后为 Profile + `_uiRoot`；结论不变） | 远程资源、加密、自定义 provider 必须写子类 |
| 相位靠人记 | `BootstrapPhases`（`BootstrapPhases.cs`） | `UserStart == Localization == 90`；依赖关系无机器校验 |
| 零编辑器校验 | 全仓无 `IPreprocessBuildWithReport` / `playModeStartScene` 引用 | 启动场景缺失、重复阶段、Host 缺服务只能运行时才炸 |
| 诊断面只列表 | `Editor/Diagnostics/Panels/BootstrapDiagnosticPanel.cs:23` | 没有运行状态、耗时、失败原因 |

---

## 三、第三方对照

### 3.1 对照表

| 框架 | 启动触发 | 排序机制 | 就绪信号 | 配置载体 | 编辑器校验 |
|---|---|---|---|---|---|
| **VContainer** | `LifetimeScope.Awake`（`DefaultExecutionOrder(-5000)`）；根作用域经 `VContainerSettings` Preloaded Asset + 根预制体 + 编辑器 `BeforeSceneLoad` 自动拉起 | PlayerLoop 时机接口；同阶段内无序保证 | `IAsyncStartable` **不阻塞后续阶段**，框架不提供全局就绪 | 代码 `Configure` + settings 资产（只装预制体引用） | 容器构建异常 |
| **Zenject / Extenject** | `ProjectContext`（Resources 懒加载）+ `SceneContext.Awake` | 安装器类别 + `BindExecutionOrder(int)`，小值先 | 无（`IInitializable` 同步） | `MonoInstaller` / `ScriptableObjectInstaller`（**资产里写代码**） | Validate Current Scene（最强） |
| **EllanJiang GameFramework** | 组件 `Awake` 自注册；`BaseComponent.Update` 泵核心 | 模块 `Priority` int，大值先 | 无（同步） | 预制体上的组件字段 | 无 |
| **QFramework** | 静态懒加载 `Architecture<T>.Interface` | 先 Model 后 System，登记序 | 无 | 纯代码 `Init()` | 无 |
| **Unity 内建** | `RuntimeInitializeOnLoadMethod` 五档；同档内**顺序不保证** | `DefaultExecutionOrder`（小值先） | 无 | — | `playModeStartScene`（编辑器辅助） |
| **XFramework（评估时）** | `GameLauncher.Awake/Start`；手动 `RunAsync` | `Phase` int，同相位并行 | `RunAsync` 返回 UniTask，但**启动器不暴露**；重入静默返回 | 原 8 字段；§10 后为 Profile + `_uiRoot` + 代码子类 | **无** |

### 3.2 三条可迁移结论

1. **「全局就绪句柄」不是行业共识**：VContainer 的 `IAsyncStartable` 也是发射后不管；UGS 的 `UnityServices.State` 是少数有明确状态面的。但本仓不同——`RunAsync` 已经返回 UniTask，只是启动器把它私藏了，暴露成本极低。
2. **整数/优先级排序是常态**：Zenject、UGF 都用 int，没有一家做运行时依赖图。相位模型不落后，落后的是「少写类」的便捷路径。
3. **「从任意场景启动」只有 VContainer 解决得干净**（settings asset + 根预制体 + 编辑器钩子）；Zenject 要求场景里有 `SceneContext`；UGF / QFramework 都不解决。这是可选加分项，不是必需项。

### 3.3 与既有对照表的关系

`Documentation/Modules/Bootstrap.md:45-53` 已有一张 2026-10-02 的五框架对照表，主题是**扩展形状**（Installer / 作用域 / 程序表 / 包装启动）。本表主题是**启动触发 / 排序 / 就绪 / 配置 / 校验**，两表互补，不重复。

---

## 四、逐痛点

### 4.1 痛点一：就绪 / 进度 / 失败信号

#### 证据

- `GameLauncher.Start`（`GameLauncher.cs:34-54`）：`async void`，`OperationCanceledException` 静默、其余只记 `[GameLauncher] 启动失败`。没有公开的完成/失败信号，也不转发 `IProgress`。
- `Bootstrap.RunAsync`（`Bootstrap.cs:287-291`）：重入只警告并**立即返回**——第二个调用方以为自己等到了启动完成，实际上什么都没等。
- Integration 样例为此自造 `AppReadyBootstrapStage`（`Samples~/Integration/Scripts/Bootstrap/AppReadyBootstrapStage.cs:26,41`，相位 91），在 `ExecuteAsync` 末尾调 `AppFlowController.NotifyBootstrapped()`。
- `AppFlowController` 在 `:239` 持有 `UniTaskCompletionSource`，`:264` 等 `_booted.Task`，`:328` 才 `MarkReady()`。
- **代码路径推断**（非实测）：若相位 0–90 任一阶段失败，后续相位（含 91）不会执行，`_booted` 永不完成，BootScreen 永远停在「启动中」——失败表现为挂起。验证方式见附录 C。

#### 第三方怎么做

- VContainer：`IAsyncStartable.StartAsync(ct)` 在 `Startup` 调度，令牌随作用域销毁取消；**后续 PlayerLoop 阶段不等它**。就绪仍由服务自己暴露。
- UGS：`UnityServices.InitializeAsync()` + `UnityServices.State`（Uninitialized / Initializing / Initialized），是本次对照里最接近「就绪状态面」的约定。
- DOTween：首次使用自动初始化，不要求启动流程。
- Zenject / UGF / QFramework：没有异步就绪契约。

#### 选项

**A. `Bootstrap` 层（核心，推荐）**

```csharp
public enum BootstrapState { Idle, Running, Completed, Failed, Cancelled }

public static BootstrapState State { get; }
public static UniTask WaitUntilReadyAsync(CancellationToken ct = default);
```

- 重入改为**返回第一次运行的任务**（join），而不是警告后立即返回。
- `WaitUntilReadyAsync`：`Completed` 正常完成；`Failed` / `Cancelled` 重抛本次运行的异常（与 `RunAsync` 的抛出契约一致）；`Idle` 时等待下一次运行（可取消）。
- 建议同时保存最近一次 `PipelineProgress` 快照，供启动画面轮询。
- 语义细节（实施前必须定稿）：失败后再次 `RunAsync` 是否把状态重置为 `Running`；`Shutdown` 在 `Idle` 时是否改变状态；`WaitUntilReadyAsync` 在 `Idle` 且无人启动时是等待还是立即失败。

**B. `GameLauncher` 层（薄糖）**

```csharp
public UniTask StartupTask { get; }   // Start 中捕获；失败时该任务以异常收场
```

不把 `Start` 改成可覆写，不加事件（事件会引入「订阅时机错过」的第二类问题）。

**C. 结果对象（`BootstrapResult`：状态、失败原因、耗时、逐阶段耗时）**

先不做；等诊断面板需要时再引入，避免一次扩太多公开面。

#### 与台账的关系

| 台账条目 | 现状态 | 重开判据 | 本次证据 | 建议 |
|---|---|---|---|---|
| 就绪句柄（`IsCompleted` / 可 await 完成信号） | `暂不做`（2026-10-01） | 「出现第三方真实诉求」 | Integration 样例的 `AppReadyBootstrapStage` 是结构性需求；重入静默返回是可直接踩到的假就绪 | **建议重开** |
| `RunAsync` 重入返回共享任务（join 首次） | `暂不做`（2026-10-02） | 同上 | 同上 | **建议重开** |

> 如果只允许一条最小修法：把「重入静默返回」改成**警告并抛出**，避免假就绪；这不新增能力面，但会改变现有调用方行为，仍需裁定。

#### 影响面

公开 API 新增（`BootstrapState` / `State` / `WaitUntilReadyAsync` / `StartupTask`）；公开 API 基线、CHANGELOG `### Added`；`Bootstrap` 多两个静态状态（`WaitUntilReadyAsync` 的等待者集合），需在 `ResetForNewSession` 里复位。

---

### 4.2 痛点二：配置只覆盖一半

拆成三个不同性质的问题。

#### 2a. 服务实例类配置——结构性限制，建议不硬解

`RemoteServices` / `DecryptionServices` / `CryptoProvider` / `IUIController` / `IInputProvider` 是普通 C# 对象或接口实现，Unity 无法序列化进 Inspector。`[SerializeReference]` 也救不了：它要求类型可序列化且无引擎不友好状态，不能通用地持有第三方 provider。

Zenject 的解法是 `ScriptableObjectInstaller`——**资产里写代码**。这正是本仓台账已否决的 Installer 形状（`不做`，无重开判据）。

**建议**：承认「代码即配置」，但把它做得更顺手：

1. 编辑器菜单生成启动器子类模板（预置 `ConfigureStages` + 常用 `using` + 服务实例字段），放在现有 `Tools/XFramework` 菜单下。
2. README 给一份「服务实例配置」配方（先 `base.ConfigureStages()`，再 `Unregister` + `Register`）。
3. 不建议新增 `IBootstrapStageProvider` ScriptableObject 之类的 Installer 变体——除非用户裁定重开。

#### 2b. 缺内置 stage 的模块——比表面看起来小

核心只有 Asset / Data / Save / Input / UI / Localization 六个阶段（`Runtime` 下 `: IBootstrapStage` 的实现）。Settings / Config 没有核心 stage，Integration 样例各自写了一个。

但核实后要区分：

- `SettingsManager.Initialize<T>(path, factory, options)` 是**按设置类型泛型**的——一个通用的 `SettingsBootstrapStage` 无法知道 `T`，样例里的 `GameSettingsBootstrapStage` 就是预期的形状。**这不是框架缺口。**
- `ConfigManager.Initialize()` 是无参通用的，但「预加载哪些表」是项目知识，框架无法代劳。
- Luban 扩展包已有自己的 `LubanBootstrapStage`。

**建议**：先提供 `BootstrapStages` 工厂目录（见 4.3），让这些模块有现成的组装件（2026-11-15 后续：该目录已删除，构造入口回到各阶段自己的构造函数）；是否升级为「核心 stage + `DefaultGameLauncher` 字段」需要逐模块论证，**默认不做**。

#### 2c. 字段面维护——可选，低优先级（历史口径）

原 8 个字段硬编码在 `DefaultGameLauncher`，加模块要改类、改反射字段数测试（`Tests/Runtime/Bootstrap/GameLauncherTests.cs:294-303` 锁定 8 个）。§10 已裁定改为 `_profile` + `_uiRoot`（2 字段），本节不再作为实施项；字段数断言按阶段 3 改为 2。

#### 与台账的关系

维持 2026-10-02 的三条裁决：GameLauncher 不作为配置/编排核心；配置是代码不是数据；不做 Installer/命名槽位。本节的建议都在这些裁决的边界内。

---

### 4.3 痛点三：登记与相位太手工

#### 现状

- 每个服务一个 `IBootstrapStage` 类；相位手挑 int；`UserStart` 与 `Localization` 同为 90（`BootstrapPhases.cs:42,49`）；`Save` 依赖 `Data` 只是注释。
- `RegisterDefaults()` 只含 Asset / Data / Save（`Bootstrap.cs:176-204`）。
- `DefaultGameLauncher.ConfigureStages`（`DefaultGameLauncher.cs:99-136`）的 register → `RegisterDefaults` → `Unregister` → `Register` 舞蹈，是「用现有 API 拼组合」的典型。

#### 第三方

整数排序是常态（Zenject `BindExecutionOrder`、UGF `Priority`）；VContainer 按 PlayerLoop 时机分组、同组无序；没有一家做运行时依赖图。**相位模型本身不需要换。**

#### 建议

1. **`BootstrapStage.Create(...)` 工厂**：`BootstrapStage.Create(phase, name, ct => UniTask, shutdown: null)`，消灭「只调一次 Initialize」的样板类。注意：委托阶段不替你管所有权，文档要写明。
2. **`BootstrapStages` 目录**：`BootstrapStages.Asset(options)` / `.Save(options)` / `.UI(root)` / `.Data()` / `.Localization(lang, table)` / `.Input(options)`，让登记代码可读且集中；`DefaultGameLauncher` 改用它。（2026-11-15 后续：目录已删除。）
3. **`RegisterDefaults` 的 skip 参数 / 构建器**：`RegisterDefaults(BootstrapDefaults.SkipAsset)` 之类，替掉 unregister 舞蹈。可选。
4. **内置不变量测试**：`Save.Phase > Data.Phase`、`UserStart == Localization` 是有意为之、`BootstrapPhases` 预留区间不被内置阶段占用——纯测试，无 API。
5. **校验型依赖元数据（可选，不推荐先做）**：可选接口 `IBootstrapStageDependency { DependsOn }`，只在登记时校验「依赖已登记且相位严格更小」，**不参与排序**。它是注释约定与拓扑排序之间的中间态；如果 4 的测试够用，就不必引入。

**不做**：拓扑排序、属性发现、源生成（远期可选，但会引入第二套登记真相，当前规模不值得）。

#### 与台账的关系

- 维持：依赖图/拓扑排序 `不做`；Installer/命名槽位 `不做`；改 `UserStart` `不做`；`PreAsset` `不做`。
- 新增：工厂与目录是 Source-compatible 的便利层，不改变登记语义。

---

## 五、外围改进

按性价比排序：

### 5.1 编辑器 / 构建时校验（最高性价比，纯 Editor 代码）

对应 Zenject 的 Validate Current Scene——本仓完全没有。建议检查：

- 启动场景是否在 Build Settings 且为 index 0（本仓 `ProjectSettings/EditorBuildSettings.asset` 目前 `m_Scenes: []`，消费方项目应校验）。
- 启动场景是否恰好一个 `GameLauncher`（多启动器无守卫，见台账）。
- 是否有重复的内置阶段类型（`RegisterDefaults` 与 `DefaultGameLauncher` 混用）。
- `AssetPlayMode.Host` 是否注入了 `RemoteServices`（R3-AST-009）。
- `_uiRoot` 是否已填（若期望 UIBootstrapStage 初始化）。
- `BootstrapProfile` 是否存在、是否与旧字段冲突（迁移后）。
- 自定义 provider 是否接线（Host 的 `RemoteServices`、自定义 `IInputProvider`/`IAudioProvider` 等）。
- 可选的 `playModeStartScene` 菜单开关（默认不覆盖用户配置）。

### 5.2 诊断面板升级

现有 `BootstrapDiagnosticPanel` 只列阶段名/相位/权重。建议补：运行状态、开始/结束时间、进度、失败原因；与 4.1 的 `State` / 进度快照共用数据。

### 5.3 子阶段超时（模块台账唯一 `未做`）

现状是相位包成 `ParallelStage`，拿不到逐阶段超时；不观察取消的阶段会挂死启动。修法在 Pipeline（`ParallelStage` 支持子阶段超时），台账已指定方向。必须如实标注边界：超时只能报告失败并取消令牌，**不响应取消的阶段仍会跑完**。

### 5.4 文档订正

- `Documentation/Modules/Bootstrap.md:64` 仍称 GameLauncher「当前零使用（全仓无场景/预制体挂载）」——Integration 样例已挂 `IntegrationLauncher`（`Samples~/Integration/Scenes/Integration.Boot.unity`），需订正。
- Bootstrap README 补「启动器与就绪面」的使用指引（若 4.1 落地）。

### 5.5 可选自动启动（VContainer 式）

settings asset + 根预制体 + `BeforeSceneLoad` 兜底，解决「任意场景按 Play」。用户未把它列为痛点，且触及「启动器可选」的定位，建议列为独立待裁项，不并入本轮。**2026-11-15 追加：已在第十节与「Mono + Profile」「静态入口 + Profile」等方案逐项比较，推荐结论见 §10.4。**

---

## 六、已否决形状复核表

| 条目 | 原状态 | 重开判据 | 本次证据 | 建议 |
|---|---|---|---|---|
| 就绪句柄 | `暂不做` | 第三方真实诉求 | 样例 `AppReadyBootstrapStage`；重入假就绪 | **重开（4.1 A）** |
| `RunAsync` 重入 join | `暂不做` | 同上 | 同上 | **重开（4.1 A）** |
| 把 `Awake`/`Start`/`OnDestroy` 改 virtual | `暂不做` | 需改启动/清理时机的真实情形 | 无 | 维持 |
| 依赖图 / 拓扑排序 | `不做` | 无重开判据 | 无 | 维持 |
| Installer / 命名槽位 / DI 式注册 | `不做` | 无重开判据 | 无；示例问题用代码通道解决 | 维持 |
| scoped / 嵌套 bootstrap | `不做` | 无重开判据 | 无 | 维持 |
| best-effort 阶段标志 | `不做` | 无重开判据 | 无 | 维持 |
| 自动门控 Update | `不做` | 无重开判据 | 无 | 维持 |
| `PreAsset` / 负相位 | `不做` | 真实第三方诉求 | 无 | 维持 |
| 改 `UserStart` 值 | `不做` | 无重开判据 | 无 | 维持 |
| 多启动器守卫 | `不做` | 真实多启动器用法 | 无 | 维持 |
| `DefaultGameLauncher` 逐相位补齐 | `不做` | 第三方踩坑报告 | 无 | 维持 |
| 反射 / 属性发现 | `不做` | 无重开判据 | 无 | 维持 |
| 只读 `Stages` 包装器 | `不做` | 无重开判据 | 无 | 维持 |

---

## 七、分步实施计划（唯一执行计划）

> 本节是唯一的阶段/提交拆分；§10.4/§11.6 只保留论证，不再另写编号。每阶段以裁定通过为前提；每个提交是原子边界，先定向、再全量、再文档门禁；不提前实现后续提交。

| 阶段 | 提交 | 内容 | 依赖 | 验证 |
|---|---|---|---|---|
| 0 | 0.1 | 本文档定稿（§0/§7/§8、历史口径标注） | — | `check-docs -Enforce` |
| 0 | 0.2 | `Documentation/Modules/Bootstrap.md`「零使用」订正；裁定落模块台账 | — | `check-docs -Enforce` |
| 1 | 1.1 | `BootstrapState` / `State` / `WaitUntilReadyAsync` / 重入 join；`ResetForNewSession` 等待者复位；测试 | — | `run-tests -Filter Bootstrap` |
| 1 | 1.2 | `GameLauncher.StartupTask`；测试 | 1.1 | 定向 |
| 1 | 1.3 | Public API 基线 + CHANGELOG `### Added` + Bootstrap README | 1.1/1.2 | 基线重跑 + `check-docs` |
| 2 | 2.1 | `BootstrapStage.Create`；测试 + 基线 | — | 定向 |
| 2 | 2.2 | `BootstrapStages` 目录（Asset/Data/Save/UI/Input/Localization）；测试 + 基线（2026-11-15 后续：已删除） | 2.1 | 定向 |
| 2 | 2.3 | 内置不变量测试（`Save.Phase > Data.Phase` 等）+ README | 2.2 | 定向 + 全量 |
| 3 | 3.1 | `BootstrapProfile` + `BootstrapStages.FromProfile`；测试 + 基线（2026-11-15 后续：已删除） | 2 | 定向 |
| 3 | 3.2 | `DefaultGameLauncher` 改造（7 字段移除、`_profile` + `_uiRoot`）+ 测试 + XML（2026-11-15 后续：已删除） | 3.1 | 定向 + 全量 |
| 3 | 3.3 | 包 README / 模块 README / 模块台账 / CHANGELOG 迁移表 | 3.2 | `check-docs` |
| 4 | 4.1 | `Integration.BootstrapProfile.asset` + `Integration.Boot.unity` 迁移 | 1、3 | 样例编译 |
| 4 | 4.2 | 删除 `AppReadyBootstrapStage`；`AppFlowController` 等 `WaitUntilReadyAsync`；`BootScreen` 读状态；样例文档 | 4.1 | `run-tests -Filter Sample` + 手动冒烟 |
| 5 | 5.1 | `BootstrapValidator` + 菜单 + Editor 测试 | 3 | Editor 测试 |
| 5 | 5.2 | `BootstrapDiagnosticPanel` 升级 + Editor 测试 | 5.1 | Editor 测试 |
| 6 | 6.1 | Pipeline `ParallelStage` 子阶段超时 + 测试 + README | — | Pipeline/Bootstrap 测试 |
| 7 | — | 待裁：Settings/Config 核心 stage；N3 自动启动根 | — | 按裁定 |
| 8 | 8.1 | `SaveBootstrapStage(options, factory)` + `DataBootstrapStage(manager)` + 测试 | 3、5 | 定向 |
| 8 | 8.2 | `UIBootstrapStage(root, controller, manager)` + `LocalizationBootstrapStage(..., manager)` + 测试 | 8.1 | 定向 |
| 8 | 8.3 | `AudioBootstrapStage(options, provider)` + 测试 + README | 8.2 | 定向 + 全量 |

**依赖与排期：** 阶段 2 先于 3（`FromProfile` 复用目录；该 API 已于 2026-11-15 删除，目录本身保留）；阶段 1 先于 4（样例用 `WaitUntilReadyAsync`）；阶段 3 先于 4、5；阶段 6 独立可后置；阶段 8 是中期改进，不阻塞 0–6；阶段 7 默认不做/可选，不进入主线提交。

### 阶段 1 细节

**提交拆分：** 1.1 核心（`Bootstrap` 就绪面/重入 join/会话复位）；1.2 `GameLauncher.StartupTask`；1.3 API 基线/CHANGELOG/README。

**改动文件（预计）：**

- `Assets/XFramework/Runtime/Bootstrap/Bootstrap.cs`：`BootstrapState`、`State`、`WaitUntilReadyAsync`、重入 join、最近进度快照；`ResetForNewSession` 复位等待者。
- `Assets/XFramework/Runtime/Bootstrap/GameLauncher.cs`：`StartupTask`。
- `Assets/XFramework/Runtime/Bootstrap/README.md`：行为契约、已知限制、设计取舍补条目。
- `Assets/XFramework/Tests/Editor/Bootstrap/BootstrapTests.cs`、`Tests/Runtime/Bootstrap/GameLauncherTests.cs`、`GameLauncherFrameTests.cs`：状态迁移、等待者、失败重抛、重入 join、销毁取消。
- `Assets/XFramework/Tests/Runtime/Architecture/PublicApiBaseline.txt`、`CHANGELOG.md`。

**语义（批准本计划即按此实施）：**

| 场景 | 语义 |
|---|---|
| 第二次 `RunAsync` 在途 | 返回第一次运行的任务；第二调用方的令牌不参与本次运行（文档写明） |
| `GameLauncher.StartupTask` 在 `Start` 前/后 | 均等待当前/下一次运行完成；失败时与 `RunAsync` 同样抛出 |
| `WaitUntilReadyAsync` 在 `Completed` | 立即完成 |
| `WaitUntilReadyAsync` 在 `Failed` / `Cancelled` | 抛出本次运行的异常 |
| `WaitUntilReadyAsync` 在 `Idle` | 等待下一次运行开始并结束；可取消 |
| 失败后再次 `RunAsync` | 状态回 `Running`，重新装配；上次异常不再影响新运行 |
| `Shutdown` 在 `Idle` | 不改状态 |
| 会话复位 | 清空等待者、状态回 `Idle` |

**验证命令：**

```powershell
Tools/run-tests.ps1 -Filter Bootstrap
Tools/run-tests.ps1
Tools/run-tests.ps1 -UpdateApiBaseline   # 只写盘，随后不带开关重跑
Tools/check-docs.ps1 -Enforce
```

### 阶段 2 细节

**提交拆分：** 2.1 `BootstrapStage.Create`；2.2 `BootstrapStages` 目录；2.3 内置不变量测试/README。

- 新增 `Assets/XFramework/Runtime/Bootstrap/BootstrapStage.cs`（委托工厂）与 `BootstrapStages.cs`（内置目录）。
- 委托阶段的 `Shutdown` 由调用方提供；不替调用方判断所有权。
- 内置不变量测试放 `Tests/Editor/Bootstrap/BootstrapInvariantTests.cs`（或并入现有 Bootstrap 测试）。
- 后续阶段 3 的 Profile 映射复用 `BootstrapStages`。

### 阶段 3 细节（Profile + launcher 改造）

**提交拆分：** 3.1 `BootstrapProfile` + `BootstrapStages.FromProfile`；3.2 `DefaultGameLauncher` 改造/测试；3.3 文档/迁移表。

- 新增 `Assets/XFramework/Runtime/Bootstrap/BootstrapProfile.cs`：`public sealed class BootstrapProfile : ScriptableObject`，7 个 `[SerializeField] private` 字段 + `internal` getter；默认值镜像旧字段。
- 新增 `BootstrapStages.FromProfile(BootstrapProfile profile, Transform uiRoot)`：显式登记 Asset/Data/Save + 条件 UI/Input/Localization。
- 改造 `DefaultGameLauncher`：移除 7 个数据字段，保留 `_profile` + `_uiRoot`；`ConfigureStages` 分支（profile 非空 / 空 → `RegisterDefaults()` + 条件 UI）。
- `GameLauncher` 抽象底座不动（零字段 + `abstract ConfigureStages`）。
- 测试：`GameLauncherTests` 字段数 8 → 2；`Awake_WithFields_*` 改为构造 `BootstrapProfile`；新增「profile 为空 = 默认三件 + 可选 UI」用例。
- 文档：`Runtime/Bootstrap/README.md`、`DefaultGameLauncher` XML、包 README 的「三种用法」改为「零配置 / Profile / 子类覆写」。
- 模板：`Tools/XFramework` 菜单生成启动器子类模板，预置 Asset/Save/Input/UI/Config/File/Settings/Audio 的 provider 注入示例（见 §11.3）。
- CHANGELOG：`### Added`（`BootstrapProfile`、`BootstrapStages.FromProfile`）+ 迁移表（7 字段移除；创建 Profile、复制值、赋 `_profile`）。

### 阶段 4 细节（样例迁移）

**提交拆分：** 4.1 Profile 资产/场景迁移；4.2 删 `AppReadyBootstrapStage`/就绪迁移/样例文档。

- 新增 `Samples~/Integration/Data/Integration.BootstrapProfile.asset`，复制旧场景值（package ""、EditorSimulate、autoReclaim true、saveVersion 1、`Integration.InputOptions`、zh_Hans、language table）。
- `Integration.Boot.unity`：`Launcher` 的 `_profile` 指向新资产；清掉 7 个旧字段键；保留 `_uiRoot`；`IntegrationLauncher` 继承不变。
- 删除 `AppReadyBootstrapStage`；`AppFlowController.Start` 改为 `await Bootstrap.WaitUntilReadyAsync(destroyCancellationToken)`；`BootScreen` 读 `Bootstrap.State`/进度。
- 手动冒烟：启动、切场景、退出。

### 阶段 5 细节（编辑器校验 + 诊断）

**提交拆分：** 5.1 `BootstrapValidator` + 菜单；5.2 诊断面板。

- 新增 `Assets/XFramework/Editor/Bootstrap/BootstrapValidator.cs` + 菜单项（`Tools/XFramework` 下）。
- 可选的 `IPreprocessBuildWithReport` 构建前校验。
- 校验项：Profile 存在、Host 有 `RemoteServices`、自定义 provider 未接线、启动场景 index 0、无重复 Launcher。
- `BootstrapDiagnosticPanel` 升级；Editor 公开 API 基线按需更新。
- 探头：在 Integration 场景制造「缺 `RemoteServices` 的 Host」与「重复内置阶段」，确认校验能报出来。

### 阶段 6 细节（超时）

**提交拆分：** 6.1 Pipeline 子阶段超时。

- `Runtime/Pipeline/ParallelStage.cs` 支持子阶段超时；Bootstrap 侧按需传入。
- 边界如实写进 Pipeline README 与 Bootstrap README：超时只能报告失败并取消令牌，不响应取消的阶段仍会跑完。

### 阶段 7 细节

- Settings/Config 核心 stage：先逐模块论证初始化 API 是否可通用；默认不做。
- N3 自动启动根：独立可选；需要 settings asset + 根预制体 + `BeforeSceneLoad`；默认关闭，不改变「Launcher 可选」的契约。

### 阶段 8 细节（provider/manager 参数，中期）

**提交拆分：** 8.1 Save factory + Data manager；8.2 UI manager + Localization manager；8.3 `AudioBootstrapStage`。

- `SaveBootstrapStage(SaveOptions options = null, SaveManagerFactory factory = null)`：工厂透传；已初始化时早退、不接管清理。
- `DataBootstrapStage(IDataManager manager = null)`：`null` 走默认 `DataManagerImpl`。
- `UIBootstrapStage(Transform uiRoot, IUIController controller = null, IUIManager manager = null)`：manager 非空走 `SetInstance`。
- `LocalizationBootstrapStage(string defaultLanguage = "zh_Hans", Dictionary<string,string> data = null, ILocalizationManager manager = null)`（TextAsset 重载同）：manager 非空走 `SetInstance`。
- `AudioBootstrapStage(AudioInitOptions options = null, IAudioProvider provider = null)`（新阶段）：provider 非空走 `SetProvider`。
- 约束：source-compatible；注入实例归调用方，阶段只销毁自己初始化的；API 基线 + CHANGELOG `### Added`；测试覆盖「注入实例不被阶段 `Shutdown` 销毁」。
- 排期：不阻塞阶段 0–6，可在阶段 5 之后单独排。

---

## 八、裁定状态

| # | 裁定项 | 状态 | 结论 |
|---|---|---|---|
| 1 | 就绪句柄 / 重入 join | 批准即裁定 | 重开，按 D1 |
| 2 | 最小修法（警告并抛出） | 不采用 | 直接实现 join |
| 3 | `GameLauncher.StartupTask` | 批准即裁定 | 暴露 |
| 4 | 配置面（Installer 形状） | 已裁定 | 不重开；代码 + 模板/工厂 |
| 5 | Settings/Config 核心 stage | 已裁定 | 默认不做 |
| 6 | 编辑器 / 构建时校验 | 批准即裁定 | 阶段 5 |
| 7 | 子阶段超时 | 批准即裁定 | 阶段 6 |
| 8 | 报告落点 | 已裁定 | 本文件 + 裁定后落模块台账 |
| 9 | Profile 形状 | 已裁定 | P1 窄 Profile |
| 10 | 启动入口形状 | 已裁定 | N0（N3 可选；N4 headless） |
| 11 | Profile 消费方式 | 已裁定 | `DefaultGameLauncher` 的 `_profile` + `_uiRoot`；7 数据字段移除；保留拆分 |
| 12 | Profile 加载通道 | 已裁定 | 直接引用（N0/N3）；N4 headless 另行 |
| 13 | Mono 入口形状 | 已裁定 | N0 |
| 14 | 7 字段移除的迁移 | 已裁定 | CHANGELOG 迁移表 + 样例同步 |
| 15 | 继承结构 | 已裁定 | 保留拆分 |
| 16 | 内置阶段 provider/manager 参数 + `AudioBootstrapStage` | 已裁定方向，排期待裁 | 阶段 8 |
| 17 | 启动器子类模板 + provider 编辑器校验 | 批准即裁定 | 阶段 3/5 |
| 18 | Profile 是否容纳 provider | 已裁定 | data-only |

---

## 九、明确不做（与台账一致）

- DI 容器 / Installer / 命名槽位（`Ledger.md` 已否决，无重开判据）。
- 依赖图 / 拓扑排序（异步编排问题可在自定义阶段里用公开 `Pipeline` 解决）。
- 作用域 / 嵌套 bootstrap（静态服务架构没有第二作用域）。
- best-effort 阶段标志（第三方可在自己阶段里 try/catch）。
- 自动门控 Update（会把「忘记恢复 = 全应用静默冻结」重新引入）。
- 默认开启的自动启动（破坏「Launcher 可选」）。
- 改 `UserStart` / 加 `PreAsset`（未论证的承诺）。
- 多启动器守卫（无真实用法）。
- 反射 / 属性发现（与零反射取向冲突）。
- 把 `GameLauncher` 做成配置/编排核心。
- 只读 `Stages` 包装器（低风险封装泄漏不值得分配）。
- 逐相位补齐 `DefaultGameLauncher`。

---

## 十、Profile 资产与无 Mono 入口（2026-11-15 追加）

> 本节回答两个追加问题：配置能否独立为类似 "Profile" 的 ScriptableObject 资产；有没有比强制依赖 Mono 更优雅的启动方式。结论见 §10.4；待裁定项见第八节 9–12。

### 10.0 两个问题的短答

- **Profile：** 可以，但只能装「一半」。**P1 窄 Profile（数据资产）可行**，落在现有 `DefaultGameLauncher` 上；P3 全局 Profile 与 P4 可编程 Profile 不建议。
- **无 Mono 入口：** 启动期间一旦需要 UI（加载遮罩），静态入口就不可行——首场景对象在 `BeforeSceneLoad` 尚未激活，自造 UI 根又回到 Mono/根对象。因此 **有 UI 的启动流程：Mono + Profile**；推荐 **P1 + N0（启动场景 Launcher）**，N3（自动启动根预制体）作为可选，N4（静态入口 + Profile）只用于 headless/纯静态项目。

### 10.1 Profile 的四种形状

#### P1 窄 Profile（单资产，装现有 7 个数据字段；`_uiRoot` 除外）

```csharp
[CreateAssetMenu(menuName = "XFramework/Bootstrap Profile", fileName = "BootstrapProfile")]
public sealed class BootstrapProfile : ScriptableObject
{
    [Header("Asset")]
    [SerializeField] private string _assetPackageName = "";
    [SerializeField] private AssetPlayMode _assetPlayMode = AssetPlayMode.Offline;
    [SerializeField] private bool _autoReclaimOnLowMemory = true;

    [Header("Save")]
    [SerializeField] private int _saveVersion = 1;

    [Header("Input")]
    [SerializeField] private InputSystemOptionsAsset _inputOptions;

    [Header("Localization")]
    [SerializeField] private string _defaultLanguage = "";
    [SerializeField] private TextAsset _languageTable;
}
```

消费方式三选一：

| 方式 | 形状 | 优点 | 缺点 |
|---|---|---|---|
| **P1-a（本轮采用）** | `DefaultGameLauncher` 只保留 `[SerializeField] BootstrapProfile _profile` + `[SerializeField] Transform _uiRoot`；7 个数据字段移除 | 配置面清晰、无双真相；Profile 可复用/做环境变体；组件类型不变 | **破坏性**：旧场景的 7 个字段值不会自动迁移；需要 Profile 资产 + 场景迁移 |
| **P1-b** | 新建 `ProfileGameLauncher : GameLauncher`，只有 `_profile` + `_uiRoot` | 语义干净；不动现有 8 字段契约与测试 | 多一个组件类型；项目要在两个组件间选 |
| **P1-c** | Profile 只给静态入口用，`DefaultGameLauncher` 完全不动 | 零破坏；与无 Mono 路径天然配对 | 场景组件路径享受不到 Profile 的复用与环境变体 |

**本轮裁定（2026-11-15）：采用 P1-a，且移除 7 个数据字段。** `DefaultGameLauncher` 的字段面 = `_profile` + `_uiRoot`；`GameLauncher` 抽象底座保持零字段（`GameLauncherTests:294-303` 的底座 0 字段断言不变，`DefaultGameLauncher` 的字段数断言 8 → 2）。`BootstrapProfile` 的 7 个值默认与旧字段逐字相同（空包名、`Offline`、`autoReclaim=true`、`saveVersion=1`、`_inputOptions=null`、空语言、`_languageTable=null`）。映射放在 `DefaultGameLauncher.ConfigureStages`：`_profile != null` 走 `BootstrapStages.FromProfile(_profile, _uiRoot)`，否则走 `RegisterDefaults()` + 条件 UI。

**零配置语义不变：** `_profile == null` → 默认 Asset/Data/Save + `_uiRoot` 非空时的 UI 阶段；Input/Localization 只有 Profile 或子类能提供。**迁移要求：** 旧场景的 7 个值不会自动搬运；必须新建 `BootstrapProfile` 资产、复制旧值、赋给 `_profile`；`Integration.Boot.unity` 与下游场景同步；CHANGELOG 写迁移说明。**继承结构保留：** 抽象 `GameLauncher` + `DefaultGameLauncher` 的拆分不动；合并为单一 `GameLauncher` 会带来场景脚本 GUID/组件类型迁移，留到独立主版本，不与本次字段迁移捆绑。

**能装：** Asset 三值、Save 版本、`InputSystemOptionsAsset`、语言 + `TextAsset`。全是 plain value 或 Unity 资源引用；`InputSystemOptionsAsset` 已是仓内先例（`Runtime/Input/Default/InputSystemOptionsAsset.cs`：`[SerializeField]` + `ToOptions()`）。

**不能装：**

| 装不进 | 原因 |
|---|---|
| `RemoteServices` / `DecryptionServices` / `CryptoProvider` | 接口型服务实例；Unity 不序列化普通接口字段。只有 provider 自己实现成 SO 时，才能用**具体 SO 类型**字段引用（`Runtime/Bootstrap/README.md` 已有先例） |
| `_uiRoot` | 场景 `Transform`；SO 资产不能跨场景引用 |
| `LogOptions` / File 域根预热 | 生效时机在 `SubsystemRegistration` / `BeforeSceneLoad`，场景组件引用 Profile 结构性地太晚 |
| `SettingsManager.Initialize<T>` 的类型参数 | 泛型无法装进非泛型资产；`Config` 预加载哪些表是项目知识 |
| Asset 自身参数（若 Profile 经 Asset/Config 加载） | 鸡生蛋：Asset 没初始化就加载不了 Profile，而 Profile 又要提供解析 Asset 所需的包名/PlayMode |

**优点：** 配置面从 8 个字段缩到 1 个 Profile 引用；Profile 可复用、做环境变体（dev/prod 两个 asset）；`.asset` 比场景 YAML 更易 diff；有现成先例（`InputSystemOptionsAsset`）；不再有「字段 vs Profile」双真相。

**缺点：** **破坏性**——7 个序列化字段移除，旧场景的值不会自动迁移；需要新建 Profile、复制值、改场景，并写 CHANGELOG 迁移说明；多一个公开类型 `BootstrapProfile` + API 基线 + 测试改写（字段数 8→2）；`.asset` 有版本/合并问题（字段改名要 `[FormerlySerializedAs]`，缺类型会静默变 null）；仍是「一个资产装多模块」，只是耦合面比全局 Profile 小。

**与台账的关系：** 这不是 `Documentation/Ledger.md:99` 的「统一框架 Profile」——它不承担 Asset/Log/File 的前置配置，也不替代模块 options。但需要在模块台账新增一条 `待裁`，把两者的界线写死，否则后人会把它当成重开。

**成本：** 低到中（1 个 SO + 1 个映射器 + 编辑器校验 + 测试 + 文档）。

#### P2 每模块独立 SO

```csharp
[CreateAssetMenu] public sealed class AssetBootstrapOptionsAsset : ScriptableObject
{
    [SerializeField] private string _packageName;
    [SerializeField] private AssetPlayMode _playMode;
    [SerializeField] private bool _autoReclaim;
    public AssetBootstrapStage CreateStage() => new(new AssetInitOptions { /* ... */ });
}
// InputSystemOptionsAsset 已存在；Save / Localization 同理
```

**优点：** 没有「一个文件装多模块」的共享耦合，最贴近「参数即契约」；每模块可独立复用、演进、校验；`InputSystemOptionsAsset` 已证明可行；可逐模块增量上线。

**缺点：** 资产数量多、配置分散，「一个 Profile 看全貌」的便利消失；启动器/静态入口的接线变多，或需要一个组合列表（又回到中心化）；仍装不了服务实例、场景引用、前置配置；公开 API 面更大；资产自带 `CreateStage()` 会让资产类型与阶段构造耦合。

**与台账的关系：** 不触碰「统一 Profile」的裁决，最容易论证为「健康形状」（每个模块自己的 options asset）。但要回答「谁来组合这些资产」——组合器本身可能变成新的中心。

**成本：** 中（多个类型 + 组合器）。

#### P3 全局 Profile

一个 `FrameworkProfile` 资产装 Asset / Log / File / Save / UI / Input / Localization / Settings / Config / Audio 的全部值，经 Preloaded Assets 或 `Resources` 在 `BeforeSceneLoad` / `SubsystemRegistration` 读取，再由静态加载器映射到所有模块。

**优点：** 一个地方看全；策划/运维友好；环境变体只换一个资产。

**缺点：** 与 `Ledger.md:99` 的三条理由正面冲突——破坏「参数即契约」与「服务不依赖统一入口」；Asset 自身参数无法经 Asset 加载；一个文件装多模块破坏「可单独取用」。还需要新增 Preloaded/Resources 加载通道，并自带失败回退、版本化、热更/加密故事（台账说的「第二条配置来源」）。`LogOptions` 的 `SubsystemRegistration` 时机与 Preloaded Assets 的可用性需要实测。会替代模块 options（重开判据明确禁止）。公开面与迁移成本最大。

**与台账的关系：** `不做`，维持。重开判据未满足。

**成本：** 高，且需要先推翻既有裁决。

#### P4 可编程 Profile / SO Installer

```csharp
[Serializable]
public sealed class AssetConfigurator : IBootstrapConfigurator
{
    public string PackageName;
    public AssetPlayMode PlayMode;
    public void Configure() =>
        Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions { /* ... */ }));
}

public sealed class BootstrapProfile : ScriptableObject
{
    [SerializeReference] private List<IBootstrapConfigurator> _configurators;
}
```

或每个模块一个 `ScriptableObjectInstaller` 子类，覆写 `InstallStages()`。

**优点：** 唯一能把「代码形状的配置」搬进资产的形状——配置器可以在 `Configure()` 里 `new MyRemoteServices(...)`，从而间接覆盖服务实例；模块可选、顺序可表达；一个资产组合全部启动步骤。

**缺点：** 这正是 `Documentation/Modules/Bootstrap.md:56` 否决的 **Installer / 命名槽位** 形状；重开需要明确裁定，且原文是「无重开判据」。`[SerializeReference]` 不能序列化 `UnityEngine.Object` 引用，也不能保存带注入依赖/委托/原生句柄的运行时服务实例；配置器字段仍受普通序列化限制。启动顺序/失败语义从类型声明挪进数据，机器更难校验；资产里的代码字段与运行时代码存在版本漂移；场景引用仍然进不去。

**与台账的关系：** `不做`（Installer / 命名槽位），需要显式重开。

**成本：** 高，且属于架构方向变更。

#### 10.1.5 Profile 形状对比

| 维度 | P1 窄 Profile | P2 每模块 SO | P3 全局 Profile | P4 可编程 Profile |
|---|---|---|---|---|
| 覆盖面 | 现有 7 个数据字段（`_uiRoot` 除外） | 现有 7 个数据字段（拆分） | 全模块 | 可含服务实例构造 |
| 服务实例 | 否 | 否（SO provider 可） | 否 | 可（代码构造） |
| 场景引用 | 否 | 否 | 否 | 否 |
| 前置配置（Log/File） | 否 | 否 | 是（需新通道） | 是（需新通道） |
| 耦合 | 中（一个资产多模块） | 低 | 高 | 高 |
| 与台账 | 新增窄形状，需划线 | 最容易论证健康 | `不做` | `不做`（Installer） |
| 公开面/成本 | 低-中 | 中 | 高 | 高 |
| 建议 | **可做候选** | **可做候选** | 维持不做 | 不建议 |

### 10.2 启动入口的五种形状

#### N0 现状：场景 `GameLauncher`

**形式：** 用户把 `DefaultGameLauncher`（或子类）挂在启动场景根对象上；`Awake` 里 `DontDestroyOnLoad` + `ConfigureStages`，`Start` 里 `RunAsync(destroyCancellationToken)`，`OnDestroy` 里 `Shutdown`（`Runtime/Bootstrap/GameLauncher.cs`）。

**优点：** 场景引用齐全（`_uiRoot`、`_appFlow`）；Unity 生命周期完整；Inspector 可配；测试可用 `AddComponent` 复现；职责清晰。

**缺点：** 必须手工摆放；从其他场景按 Play 不启动；多启动器无守卫；必须挂根对象；`Awake` 顺序不确定；测试要复位静态表。

**评价：** 首选；启动场景就是 composition root，遮罩与 UI 根都在同一场景里。

#### N1 静态钩子 + 代码登记（已有能力）

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Boot()
{
    Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions { /* ... */ }));
    Bootstrap.RegisterDefaults();
    BootAsync().Forget();
}

static async UniTaskVoid BootAsync()
{
    try { await Bootstrap.RunAsync(cancellationToken: Application.exitCancellationToken); }
    catch (OperationCanceledException) { }
    catch (Exception e) { LogManager.Error(LogCategories.Bootstrap, "启动失败：{0}", e); }
}
```

**启动时机：** `BeforeSceneLoad`，在 `SubsystemRegistration` 的登记表清空之后、场景 `Awake` 之前；场景对象已加载但未激活。

**优点：** 零新 API；任意场景可启动；最早的安全登记点；不需要 Mono；`UpdateManager` 照常派发；`Application.exitCancellationToken` 天然做生命周期令牌。

**缺点：** 配置是代码；拿不到场景引用；异常处理、就绪信号、退出清理都要自己写；多个钩子会重复登记；PlayMode 测试也会触发；没有 Inspector。

**评价：** 纯静态项目的合理选择；作为文档配方，不新增框架面。

#### N2 `BootstrapEntry` 静态入口助手

```csharp
public static class BootstrapEntry
{
    // 由使用方在自己的 BeforeSceneLoad 钩子里调用（框架不反射发现）
    public static void Start(Func<IBootstrapStage[]> configure);
    public static UniTask StartupTask { get; }
    public static event Action<Exception> Failed;
}
```

内部负责：登记、`RunAsync(Application.exitCancellationToken)`、异常日志、`Application.quitting` 幂等订阅 + `Shutdown`、重复启动守卫、最近进度/状态（与阶段 1 的就绪面共用）。

**启动时机：** 仍由使用方的 `BeforeSceneLoad` 钩子触发；`BootstrapEntry` 只收敛样板。

**优点：** 比 N1 少写重复代码；统一失败/取消/清理语义；可作为无 Mono 路径的官方入口；不引入资产/加载通道。

**缺点：** 仍需使用方写一个静态钩子；新增公开 API 与语义（谁是「已启动」、测试如何关闭、多入口如何处理）；没有场景引用；移动端暂停不覆盖。

**评价：** 作为 N4/headless 的配套 API 有价值；单独存在时只是样板收敛器。

#### N3 settings asset + 启动根预制体自动实例化（VContainer 式）

```csharp
[CreateAssetMenu(menuName = "XFramework/Bootstrap Settings")]
public sealed class BootstrapSettings : ScriptableObject
{
    [SerializeField] private GameLauncher _launcherPrefab;
    [SerializeField] private BootstrapProfile _profile;   // 可选
    [SerializeField] private bool _autoStart = true;
    [SerializeField] private bool _runInTests = false;
}
```

- 资产放进 **Preloaded Assets**（编辑器助手自动加入，VContainer 的做法）或 `Resources`（FMOD 包已有先例）。
- 框架的 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 读取 settings；若 `_autoStart` 且本会话尚未创建，则实例化 `_launcherPrefab` 并 `DontDestroyOnLoad`。
- 预制体可自带 `GameLauncher`（或子类）+ UIRoot（Canvas/UIRootNode）+ EventSystem；场景引用全部在预制体内部。
- 重复守卫：静态 `_created` 标志 + 场景中已有 Launcher 的检查；`_runInTests` 防测试运行器自动创建（VContainer issue #689 是真实反例）。

**启动时机：** `BeforeSceneLoad` 预加载 settings → 实例化预制体（其 `Awake` 立即跑）→ 首个场景加载 → 预制体的 `Start` 跑 `RunAsync`。

**优点：** 任意场景可启动；不需要手工摆放；场景引用、完整生命周期、Inspector、`destroyCancellationToken` 全部保留；复用现有 Launcher；预制体让 UIRoot/EventSystem 与 Launcher 一起常驻；VContainer 已验证。

**缺点：** 仍然是 Mono，只是自动创建；多一个 settings asset + prefab；需要 Preloaded Assets 编辑器助手（或 Resources 魔法路径）；预制体不能引用场景对象；重复创建、场景重载、测试运行器、关闭域重载下的静态 settings 实例都是实际故障点；关闭域重载时 `OnEnable` 的静态单例可能不重跑。

**评价：** 可选；适合「任意场景按 Play」或「遮罩要早于第一场景」的项目。否则 N0 更简单。

#### N4 静态入口 + SO Profile（完全无 Mono）

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Boot()
{
    var profile = Resources.Load<BootstrapProfile>("BootstrapProfile"); // 或 Preloaded
    if (profile == null) { LogManager.Warning(LogCategories.Bootstrap, "未找到 BootstrapProfile。"); return; }
    BootstrapEntry.Start(profile, config =>
    {
        // 服务实例从这里注入
        // config.Asset.RemoteServices = new MyRemoteServices();
        // config.Register(new GameSettingsBootstrapStage());
    });
}
```

**启动时机：** `BeforeSceneLoad`；场景对象未激活，`Awake` 未跑。

**优点：** 完全无 Mono；任意场景；配置在资产里；启动早于场景 `Awake`；与 P1/P2 天然配对；对无 UI/无场景引用的项目最简洁；业务侧可 `await Bootstrap.WaitUntilReadyAsync`，不再需要 `AppReadyBootstrapStage`。

**缺点：** 拿不到 `_uiRoot` / EventSystem；UI 根必须靠 `UIRootNode` 或另一个 `AfterSceneLoad` 适配器；`OnApplicationPause` / 移动端退出不覆盖（仓内已为 Settings 造隐藏 `DontDestroyOnLoad` 宿主，真要处理暂停还是得有一个 Mono）；Profile 的加载通道与失败回退要定义；异常、就绪、退出清理都要自己写（除非配合 N2）；测试运行器会触发静态钩子，需要关闭开关；服务实例仍然只能代码注册。

**评价：** 只适合无 UI/纯静态项目；UI 游戏的遮罩悖论见 §10.7。作为 headless 备选，不作为主线。

#### N5 懒初始化

各门面首次使用时 `EnsureStartedAsync()`，内部做一次性异步初始化（Addressables / DOTween 式）。

**优点：** 零入口；任意场景；最少仪式。

**缺点：** 异步首次使用会造成帧尖峰与顺序隐式；失败可见性差；QFramework 的「异常后半初始化、无法重试」是直接反例；与现有显式相位/失败即停模型冲突；清理无生命周期。

**评价：** 不建议用于全局启动；叶子服务可另议。

#### 10.2.6 入口形状对比

| 维度 | N0 场景 Launcher | N1 静态钩子 | N2 静态助手 | N3 自动启动根 | N4 静态+Profile | N5 懒初始化 |
|---|---|---|---|---|---|---|
| 手工摆放 | 是 | 否 | 否 | **否** | 否 | 否 |
| 任意场景 | 否 | 是 | 是 | **是** | 是 | 是 |
| 场景引用 | 是 | 否 | 否 | **是（预制体内）** | 否 | 之后才有 |
| Mono | 是 | 否 | 否 | 是（自动创建） | 否 | 否 |
| 完整生命周期 | 是 | 自己写 | 部分 | **是** | 自己写 | 否 |
| 移动端暂停 | 是 | 否 | 否 | **是** | 否（需隐藏宿主） | 否 |
| 配置载体 | Inspector | 代码 | 代码/Profile | Inspector/Profile | Profile | 代码 |
| 失败可见性 | 日志 | 自己写 | 统一 | 日志 | 自己写 | 差 |
| 测试隔离 | 中 | 差（钩子自动跑） | 中 | 需 `_runInTests` | 差 | 差 |
| 新增 API | 无 | 无 | 小 | 中 | 中 | 大 |
| 建议 | **首选** | 文档配方 | 可选配套 | **可选** | headless 专用 | 不建议 |

### 10.3 六种可行组合

| 组合 | 形状 | 任意场景 | 场景引用 | Mono | 配置资产 | 与台账 |
|---|---|---|---|---|---|---|
| **A0（推荐）** | P1 + N0 启动场景 Launcher | 否（需启动场景） | 是（场景内） | 是 | 是 | 新增窄 Profile 待裁；N0 是现有路径 |
| **A** | P1 + N3 自动启动根 | 是 | 是（预制体） | 是（自动） | 是 | 新增窄 Profile 待裁；N3 独立待裁 |
| **B** | P1 + N4 静态入口 | 是 | 否 | 否 | 是 | 仅 headless/纯静态；UI 场景不适用 |
| **C** | P2 每模块 SO + N3 | 是 | 是 | 是（自动） | 是（多个） | 最容易论证「不集中」 |
| **D** | P4 可编程 + N4 | 是 | 否 | 否 | 是（Installer） | 需重开 Installer 的 `不做` |
| **E** | 不做 Profile + N1/N3 | 是（N3） | 是（N3） | 是/否 | 否 | 不碰既有裁决 |

### 10.4 推荐：P1 + N0（Mono + Profile）

**主线：P1（`DefaultGameLauncher` 的 `_profile` + `_uiRoot`）+ N0（启动场景 Launcher）。**

- `BootstrapProfile` 只做 descriptor，不做容器；`BootstrapStages.FromProfile(profile, uiRoot)` 负责映射；`DefaultGameLauncher.ConfigureStages` 在 `_profile != null` 时调用它，否则走 `RegisterDefaults()` + 条件 UI。
- 启动场景就是 composition root：Launcher 持 `_uiRoot`（以及遮罩所在的 UIRoot），Profile 持 Asset/Save/Input/Localization 数据；`Start` 在场景 `Awake` 之后跑，遮罩已可见。
- 就绪面（阶段 1）让遮罩读 `State`/进度，业务 `await Bootstrap.WaitUntilReadyAsync(destroyCancellationToken)`；`AppReadyBootstrapStage` 可以删除。
- 服务实例与项目自定义阶段仍走 `ConfigureStages` 覆写（`IntegrationLauncher` 即此形状）。
- **继承拆分保留**：抽象 `GameLauncher`（零字段 + `abstract ConfigureStages`）+ `DefaultGameLauncher`（`_profile` + `_uiRoot`）。合并为单一 `GameLauncher` 会带来场景脚本 GUID/组件类型迁移，留到独立主版本。

**为什么不是 N4 主线：** 遮罩悖论——`BeforeSceneLoad` 时首场景对象已加载但未激活、`Awake` 未跑，静态入口拿不到场景 UI；如果它自己创建 UI 根，就已经有 Mono/根对象，「无 Mono」不成立。UI 游戏的启动流程必须有根对象（详见 §10.7）。

**为什么 N3 是可选：** 它能把遮罩提前到第一场景之前，并解决「任意场景按 Play」；代价是 settings asset + 启动根预制体 + 重复创建/测试守卫。如果项目不需要这两点，N0 更简单。

**为什么不是 P3/P4/N5：** P3 全局 Profile 维持 `Ledger.md:99` 的 `不做`；P4 是 `Bootstrap.md:56` 否决的 Installer 形状；N5 懒初始化会让启动失败与顺序变成隐式的（QFramework 的半初始化是反例）。

**实施顺序：** 见 §7（唯一执行计划）。主线：阶段 1 就绪面 → 阶段 2 工厂/目录 → 阶段 3 Profile → 阶段 4 样例 → 阶段 5 校验 → 阶段 6 超时；阶段 7/8 可选。

**样例迁移后的形态（示意）：**

```text
Integration.Boot.unity
  UIRoot (Canvas + UIRootNode)
  EventSystem
  AppFlow (AppFlowController: Start -> await Bootstrap.WaitUntilReadyAsync)
  BootScreen (读 Bootstrap.State / 进度)
  Launcher (IntegrationLauncher : DefaultGameLauncher)
    _profile: Integration.BootstrapProfile
    _uiRoot: UIRoot
    ConfigureStages -> base + GameSettings/GameConfig stages
Integration.BootstrapProfile.asset
  Asset: package "", EditorSimulate, autoReclaim true
  Save: version 1
  Input: Integration.InputOptions
  Localization: zh_Hans + language table
```

### 10.5 边界与风险

- **UI 根/遮罩必须由场景或根对象提供：** 遮罩是表现层，不是 `IBootstrapStage`；它观察 `Bootstrap.State`/进度。N0 的遮罩在启动场景里；N3 的遮罩在启动根预制体里；N4 不适合需要遮罩的项目。
- **服务实例仍要代码：** Profile 装不了接口型 provider；`ConfigureStages` 覆写（或 N4/N2 的 `configure` 回调）是唯一诚实通道。
- **Profile 直接引用，不需要加载通道：** N0/N3 由场景组件/预制体直接引用 Profile；`Resources`/Preloaded 只在 N4 headless 时才需要，且不在本轮推荐范围。
- **迁移是显式的：** 7 个字段移除后，旧场景值不会自动搬进 Profile；必须新建 Profile、复制值、赋 `_profile`，并在 CHANGELOG 写迁移说明。样例 `Integration.Boot.unity` 同步迁移。
- **继承拆分保留：** `GameLauncher` 抽象零字段 + `DefaultGameLauncher` 两字段；合并为单一 `GameLauncher` 会带来组件类型/GUID 迁移，留独立主版本。
- **N0 需要启动场景：** 编辑器校验（Build Settings index 0 / `playModeStartScene`）兜底；如果必须任意场景 Play，选 N3。
- **会话复位（N3/headless）：** 静态入口的已启动标志、等待者、订阅必须在 `SubsystemRegistration` 复位，与 `Bootstrap.ResetForNewSession` 同档且幂等。
- **`Application.quitting` 平台限制：** 强制退出/崩溃/Android 暂停/UWP 不可靠；退出清理不能只靠它，样例的显式 `RequestExitAsync` 仍要保留。
- **两条路径并存：** `GameLauncher`/`DefaultGameLauncher` 与 N3/N4 入口同一会话只能选一个；文档要与编辑器校验一起说明。
- **就绪语义：** `WaitUntilReadyAsync` 在失败时重抛、在 `Idle` 时等待下一次运行等语义，必须在阶段 1 定稿（见 §7 阶段 1 的语义表）。

### 10.6 待裁定

第八节裁定状态 9–15：Profile 形状、启动入口形状、Profile 消费方式、加载通道、Mono 入口形状、7 字段迁移、继承结构。本节不另立裁定项；**provider 替换路径见 §11（裁定状态 16–18）**。

### 10.7 遮罩悖论：为什么 UI 启动流程必须有根对象

- `BeforeSceneLoad` 时首场景对象已加载但**未激活**、`Awake` 未跑；场景里的 UIRoot/遮罩不可用。
- 静态入口若在此时开始初始化，玩家看到黑屏；若它自己实例化 Canvas/遮罩，就已经创建了根对象；UI 组件本身也是 Mono，移动端暂停/退出还要隐藏宿主。
- `AfterSceneLoad` 时遮罩可见，但已晚于场景 `Awake`；`Start` 与 RIL 的顺序没有契约。
- 结论：**只要启动期间需要 UI，入口就必须锚在一个对象上**。这不是框架缺陷，是 Unity 的场景/生命周期模型。N0（启动场景 Launcher）与 N3（启动根预制体）是两种诚实的锚定方式；N4 只适合无 UI/纯静态项目。
- 遮罩仍是**表现层**：它读 `Bootstrap.State`/进度，不进 `IBootstrapStage`；交互式 UI（登录/同意条款）属于 `AppFlowController` 的业务流程，放在 `WaitUntilReadyAsync` 之后。

---

## 十一、第三方替换 provider 的路径（2026-11-15 追加）

> 本节回答「第三方怎么改各类功能的 provider」。**权威索引是包 README 的「可替换的实现一览」**（`Assets/XFramework/README.md:179-207`）；本节补的是**启动流程里的注入位置、时机与所有权**，以及现有阶段的缺口。

### 11.0 短答

- 框架**没有统一的 provider 容器**，这是刻意的：注入形态按模块差异化（`Initialize(实例)` / 工厂委托 / `SetInstance` / options / `SetProvider` / 注册表 / 无注入点），DI 容器与 Installer 形状均已被台账否决。
- **provider 是代码，不是数据**：`BootstrapProfile` 只装 7 个数据值，不装 provider；换 provider 的位置是启动器子类的 `ConfigureStages` 覆写（唯一与 `Awake` 顺序无关的组合根），或在 `RunAsync` 之前的代码注入。
- 替换分三层：**管理器级**（换整个后端）、**provider 级**（换单个引擎/服务）、**注册追加级**（不换主服务，只加 sink/格式/页签）；无后端的模块按设计不提供接口。

### 11.1 三层替换

| 层级 | 换什么 | 代表接线 |
|---|---|---|
| **管理器级** | 整个后端实现 | Asset `SetInstance(IAssetManager)`；Save `Initialize(factory, options)`；Data `Initialize(IDataManager)`；Localization `SetInstance`；UI `SetInstance(IUIManager)`；Config `SetInstance(IConfigManager)`；Audio `SetInstance(IAudioManager)`（逃生口）；Log `Initialize(ILogManager)` |
| **Provider 级** | 单个引擎/服务 | Asset `RemoteServices` / `DecryptionServices`；Save `CryptoProvider`；Input `Initialize(IInputProvider)` / `InputBootstrapStage(provider)` / `SetProvider`；UI `SetController` / `SetTipProvider` / `SetHudProvider`；Audio `SetProvider(IAudioProvider)`；File `Initialize(IFileProvider)` + `IAtomicFileProvider` / `IDirectoryProvider` + `SetCryptoProvider`；Settings `ISettingsStore` / `IAsyncSettingsStore` / 迁移器 / 校验器；Config `IConfigLoader`；Serialize `Register(ISerializer)`；Log `AddSink(ILogSink)` |
| **注册追加级** | 不换主服务，只加条目 | Serialize `Register` / `Unregister`；Log `AddSink` / `RemoveSink`；Diagnostics `Register(IDiagnosticPanel)`；Message `AddFilter`；Pool `Configure<T>` |
| **无注入点（按设计）** | 不可换 | Scene（走 Asset 的 `IAssetSceneLoader`）；Camera（后端 internal，仅 Cinemachine）；Message broker（internal，只能加过滤器）；Update / Timer / Lock（纯静态）；Event / Reactive（无注入句柄）；Pool 管理器（单池可 `new Pool<T>`） |

### 11.2 逐模块 cookbook

| 功能 | 契约 | 接线 | 时机与所有权 |
|---|---|---|---|
| **Asset 管理器** | `IAssetManager` | `AssetManager.SetInstance` | 任意时刻；设置即视为已初始化、作废在途初始化。框架创建的旧实例被退订并 Dispose；注入的 `AssetManagerImpl` 只退订；第三方实现不动。`Destroy` 会销毁当前实例（含注入） |
| **Asset 远端/解密** | `IAssetRemoteServices` / `IAssetDecryptionServices` | `AssetInitOptions` 字段 | 首次 `InitializeAsync` 前；`Host` 必须非空 `RemoteServices`；`EditorSimulate` 忽略两者 |
| **Save 后端** | `ISaveManager` | `SaveManager.Initialize(factory, options)` | 首次使用前；重复 Initialize 告警忽略。`SaveBootstrapStage` 无 factory 参数，必须前置注入或自写阶段；阶段看到已初始化会早退、不接管清理 |
| **Save 加密** | `ICryptoProvider` | `SaveOptions.CryptoProvider` | 阶段构造前；仅作用于 `FileDomain.SaveData`；换密钥会让旧存档不可读 |
| **Input 后端** | `IInputProvider` | `Initialize(provider)` / `InputBootstrapStage(provider)` / `SetProvider` | 阶段 1；`Initialize` 注册帧驱动，`SetProvider` 不注册（需自行 `Tick`）；`Destroy` 释放 provider；第三方静态订阅需自行退订 |
| **UI 管理器** | `IUIManager` | `UIManager.SetInstance` | 任意时刻；旧实例只在「门面自己创建」时 Dispose；`Destroy` 会销毁当前实例（含注入）；注入实现只接每帧档 |
| **UI 拦截/提示/HUD** | `IUIController` / `IUITipProvider` / `IUIHudProvider` | `SetController` / `SetTipProvider` / `SetHudProvider`；`UIBootstrapStage(root, controller)` | 初始化后也可；`SetController` 只对内置 `UIManagerImpl` 生效；Tip/HUD 注入会补调 `SetUIRoot`，旧 provider 只 `DetachAll` 不 Dispose |
| **Audio 引擎** | `IAudioProvider` | `AudioManager.SetProvider` | 首次播放前；会先销毁旧管理器并 Dispose 旧 provider；同一实例重复传入抛异常 |
| **Audio 管理器** | `IAudioManager` | `AudioManager.SetInstance` | 首次使用前；不 Dispose 旧实例，已初始化时先 `Destroy` |
| **File 后端** | `IFileProvider`（可选 `IAtomicFileProvider` / `IDirectoryProvider`） | `FileManager.Initialize(provider)` **一次性** | 首次文件操作前；换实现必须先 `Destroy()`；缺失可选能力按「降级 + 告警」处理；门面探测的是 `_baseProvider` |
| **File 加密** | `ICryptoProvider` | `SetCryptoProvider(provider, domain)` | 任意时刻；按域生效；换密钥会让旧文件不可读 |
| **Settings 存储** | `ISettingsStore`（可选 `IAsyncSettingsStore`） | `Initialize<T>(store, ...)` / `SetStore<T>` | 该类型首次使用前；按类型绑定；`SetStore` 只换后端、不重载/迁移 |
| **Settings 迁移/校验** | `ISettingsMigrator<T>` / `ISettingsValidator<T>` | `SetMigrator<T>` / `SetValidator<T>` | 该类型实例存在后；构造期初始加载已完成，注册后需再 `Load<T>()` |
| **Config 管理器** | `IConfigManager` | `ConfigManager.SetInstance` | 首次加载/查询前；不 Dispose 注入实例；`ConfigChanged` 只对内置实现触发 |
| **Config 加载器** | `IConfigLoader` | `PreloadTableAsync` / `PreloadGlobalAsync` 调用点 | 调用时逐次传入；**不能全局注册新格式**；加载器必须无状态 |
| **Data 管理器** | `IDataManager` | `DataManager.Initialize(impl)` | 首次访问前；`null` 等价于 `Shutdown`；无 options/工厂；`Shutdown` 不触发 `OnClear` |
| **Localization 管理器** | `ILocalizationManager` | `LocalizationManager.SetInstance` | 首次使用前；不 Dispose 被替换实例；**资产加载路径不可换**（`SwitchLanguageAsync` 固定走 `LanguageAssetLoader` + Asset） |
| **Serialize 格式** | `ISerializer` | `Register` / `Unregister` | 任意时刻；同名后注册者胜；会话复位会清掉第三方格式，需复位后重注册 |
| **Log 管理器/sink** | `ILogManager` / `ILogSink` | `Initialize` / `AddSink` / `RemoveSink` | `LogOptions` 必须在场景加载前；`Configure` 会丢弃自定义 sink 并复位档位；替换 `ILogManager` 会停用 Unity 全量捕获 |
| **Diagnostics 页签** | `IDiagnosticPanel` / `IDiagnosticPanelView` | `Register` / 实现视图 | 任意时刻；注册表无后端；运行时页签在关闭域重载时需自行避免累积 |
| **Camera** | 无公开后端契约 | `Initialize(CinemachineBrain)` | 无公开替换点；`ICameraBackend` internal，替换后端等于替换模块 |
| **Scene** | 无 `ISceneManager` | 通过 Asset 的 `IAssetSceneLoader` | 要换场景后端，需整体替换 `IAssetManager` 并实现该能力接口 |
| **Message** | 无 broker 注入 | `AddFilter` / `RemoveFilter` | 无后端；`IMessageBroker` internal |
| **Update / Timer / Lock** | 无 | 注册 / 创建 API | 纯静态服务，无后端可换 |
| **Pool** | 管理器级无注入；单池可 `new Pool<T>` | `Configure<T>` / `GetPool<T>` | `Configure` 必须在首次建池前；已建池需先 `RemovePool<T>` |
| **Event / Reactive** | 无注入句柄 | 自行实现接口 | 框架没有接收入口 |
| **Bootstrap / Pipeline** | 不是换后端，是装配入口 | `Bootstrap.Register` / `Unregister<T>`；`Pipeline.Create()` + `AddStage` | 阶段登记必须在 `RunAsync` 前；运行期修改只告警 |

### 11.3 放到启动流程里

**唯一与 `Awake` 顺序无关的组合根是启动器的 `ConfigureStages` 覆写。** 在里面有两种写法：

**A. 阶段构造注入（推荐，若阶段支持）**

- `new AssetBootstrapStage(new AssetInitOptions { RemoteServices = ..., DecryptionServices = ... })`
- `new InputBootstrapStage(new RewiredProvider())`
- `new UIBootstrapStage(uiRoot, new MyUIController())`
- `new SaveBootstrapStage(new SaveOptions { CryptoProvider = ... })`
- `new LocalizationBootstrapStage("en", table)`

**B. 预注入，让内置阶段早退（阶段不支持 provider 时）**

- `DataManager.Initialize(custom)`、`SaveManager.Initialize(factory, options)`、`UIManager.SetInstance(custom)`、`LocalizationManager.SetInstance(custom)`、`AssetManager.SetInstance(custom)`——**在 `RunAsync` 之前**。
- 内置阶段执行时看到「已初始化」就早退，并且不接管清理；你的实例仍归你。
- 注意：`AssetManager.Destroy()` / `UIManager.Destroy()` 会销毁注入实例；`SaveManager.Shutdown()` / `DataManager.Shutdown()` 只丢引用。

**C. 非阶段模块**

- Config / File / Settings / Serialize / Log / Audio 没有内置引导阶段，在首次使用前注入。
- `LogOptions` 必须在场景加载前（`BeforeSceneLoad`），启动器覆盖不到。
- Audio 没有 `AudioBootstrapStage`；在组合根或 FMOD 自动安装里初始化。

**示例（`DefaultGameLauncher` 子类）：**

```csharp
protected override void ConfigureStages()
{
    // 1) 预注入：内置阶段随后看到已初始化并早退，不接管清理。
    //    Save 的 options 只在这里生效（Initialize 重复调用会告警忽略），
    //    所以 crypto/factory 必须放在这一次；不要再登记第二个 Save 阶段改 options。
    SaveManager.Initialize(() => new MySaveManager(), new SaveOptions
    {
        CurrentVersion = 1,
        CryptoProvider = new AesCryptoProvider(),
    });
    DataManager.Initialize(new MyDataManager());

    // 2) 让基类登记 Profile/UI 阶段（可选；也可以完全不调 base 自行登记）
    base.ConfigureStages();

    // 3) 阶段构造注入：替换/追加带 provider 的阶段
    Bootstrap.Unregister<AssetBootstrapStage>();
    Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions
    {
        PackageName = "Main",
        PlayMode = AssetPlayMode.Host,
        RemoteServices = new MyRemoteServices(),
        DecryptionServices = new MyDecryptionServices(),
    }));
    Bootstrap.Unregister<InputBootstrapStage>();
    Bootstrap.Register(new InputBootstrapStage(new RewiredProvider()));
    Bootstrap.Unregister<UIBootstrapStage>();
    Bootstrap.Register(new UIBootstrapStage(_uiRoot, new MyUIController()));

    // 4) 非阶段模块
    ConfigManager.SetInstance(new MyConfigManager());
    FileManager.Initialize(new MyFileProvider());
    SettingsManager.Initialize<MySettings>(new MyStore());
    Serializer.Register(new MySerializer());
    LogManager.AddSink(new MyLogSink());
}
```

> `IntegrationLauncher` 目前只演示阶段登记与数据配置（`GameSettingsBootstrapStage` 用 `SettingsManager.Initialize<T>` + store/validator/migrator；`GameConfigBootstrapStage` 用 `ConfigManager.Initialize()`），**没有注入 provider**；真实 provider 注入的仓内先例是 FMOD 扩展的 `FmodInstaller` 调 `AudioManager.SetProvider`。

### 11.4 Profile 边界

- `BootstrapProfile` 只装 7 个数据值；**provider 是代码，不是数据**，装不进 Profile。
- 唯一「资产化 provider」的路径：provider 自己实现成 `ScriptableObject`，自定义启动器用**具体 SO 类型**字段引用、代码里转接口（Bootstrap README 的 `MyRemoteServicesAsset` 先例）。它不是通用机制；普通 C# provider 只能代码注入。
- **本轮裁定：Profile 保持 data-only，不新增 provider 槽。** `ProviderProfile` / provider 注册表 / Installer 形状一律不建议——只有 SO provider 能进资产，且会重开已否决的 DI/Installer 裁决。

### 11.5 现有缝的尖锐处

- `SaveBootstrapStage` 只有 `SaveOptions`，**没有 factory 参数**；换 `ISaveManager` 必须在 `RunAsync` 前注入或自写阶段。
- `DataBootstrapStage` 内部固定 `new DataManagerImpl()`，**没有实例参数**；同理。
- `UIBootstrapStage` 只收 `Transform` + `IUIController`，**不能注入 `IUIManager` / Tip / HUD**；管理器要前置 `SetInstance`。
- `LocalizationBootstrapStage` 只收语言数据，**不能注入 `ILocalizationManager`**。
- **没有 `AudioBootstrapStage`**；Audio 必须在组合根或 FMOD 自动安装里初始化。
- File base provider 一次性；换必须 `Destroy()` + `Initialize()`。
- Serialize 的第三方格式会被会话复位清掉，需复位后重新注册。
- `LogManager.Configure` 会丢弃自定义 sink 并复位档位；替换 `ILogManager` 会停用 Unity 全量捕获。
- Camera 后端、Audio 高级能力接口（参数/bank/bus/snapshot/3D）为 internal：第三方只能换 `IAudioProvider` 的公开原语，或用 `TryGetProviderHandle` 自行处理逐次控制。
- Settings 没有管理器工厂；Localization 的资产加载路径不可换；Message broker / Update 调度器 / Timer 核心没有注入点。

### 11.6 改进

**短期（推荐）：**

- 把本节 cookbook 作为维护向索引；使用方入口仍指向包 README 与各模块 README。
- 启动器子类模板：预置 Asset/Save/Input/UI/Config/File/Settings/Audio 的注入示例，放在 `Tools/XFramework` 菜单下。
- 编辑器校验：Host 缺 `RemoteServices`、自定义 provider 未接线、Profile/字段冲突、启动场景 index 0、无重复 Launcher。

**中期（待裁，本轮推荐排期）：**

给内置阶段加**可选 provider/manager 参数**，并补 `AudioBootstrapStage`。这是 source-compatible 的构造注入，不是 DI 容器：

- `SaveBootstrapStage(SaveOptions options = null, SaveManagerFactory factory = null)`
- `DataBootstrapStage(IDataManager manager = null)`
- `UIBootstrapStage(Transform uiRoot, IUIController controller = null, IUIManager manager = null)`
- `LocalizationBootstrapStage(string defaultLanguage = "zh_Hans", Dictionary<string,string> data = null, ILocalizationManager manager = null)`（TextAsset 重载同）
- `AudioBootstrapStage(AudioInitOptions options = null, IAudioProvider provider = null)`（新阶段）

约束：可选参数/新类型，source-compatible；注入实例归调用方，阶段只销毁自己初始化的；公开 API 基线 + CHANGELOG `### Added`；测试覆盖「注入实例不被阶段 Shutdown 销毁」。

**不做：** `ProviderProfile` / provider 注册表 / Installer 形状 / 把 provider 塞进 Profile。

### 11.7 待裁定

第八节裁定状态 16–18：内置阶段 provider/manager 参数、启动器模板 + 编辑器校验、Profile 是否容纳 provider。本节不另立裁定项。

---

## 附录 A：事实核对状态

> **口径说明：** §1/§2/§3/§4.2c 的「8 字段」是 §10 裁定前的评估；最终实施口径见 §0/§7（`BootstrapProfile` + `_uiRoot`，2 字段）。

**已核对（源码 / 官方文档）：**

- 本仓 Bootstrap / GameLauncher / DefaultGameLauncher / BootstrapPhases / Pipeline 的行为与契约；测试锁定（含 `GameLauncherTests:294-303` 的字段断言；本轮裁定后 `DefaultGameLauncher` 从 8 字段改为 2 字段，底座仍 0）。
- `ProjectSettings/EditorBuildSettings.asset` 的 `m_Scenes: []`；`ProjectSettings/ProjectSettings.asset` 的 `preloadedAssets: []`。
- `SettingsManager.Initialize<T>` 为按类型泛型；`ConfigManager.Initialize()` 无参。
- `UIRootNode`（`Runtime/UI/View/UIRootNode.cs`）：`Awake` 在 `UIManager` 未初始化时以自身 `Transform` 初始化；`OnDestroy` 只在自己是当前根时销毁。
- 仓内隐藏 Mono 宿主先例：`Runtime/Settings/SettingsPauseNotifier.cs`（`OnApplicationPause`）、`Runtime/Audio/AudioSourcePool.cs` 的 `AudioHost`。
- `Application.exitCancellationToken`：Unity 2022.3 文档确认「退出 Play 模式（编辑器）或退出应用时取消」；6000.4 实施前需用编译探针确认。
- `Application.quitting`：强制退出/崩溃/Android 暂停/UWP 不可靠；`Application.wantsToQuit` 在编辑器退出 Play 时返回值被忽略、iOS/iPadOS 无法阻止终止。
- `RuntimeInitializeOnLoadMethod`：`BeforeSceneLoad` 时场景对象已加载但未激活、`Awake` 未跑；`AfterSceneLoad` 时 `Awake`/`OnEnable` 已跑、可 `FindObjectsByType`；同档回调顺序不保证。
- **遮罩时序（本轮修正的直接依据）：** `BeforeSceneLoad` 时场景 UI（遮罩）不可用；静态入口若自造 UI 根则已创建对象。详见 §10.7。
- **本轮裁定的破坏性变更：** `DefaultGameLauncher` 的 7 个数据字段移除，字段面 8→2（`_profile` + `_uiRoot`）；抽象底座仍 0 字段；需要 CHANGELOG 迁移表 + Integration 场景同步。
- **Provider 可替换性：** 权威表在 `Assets/XFramework/README.md:179-207`；注入形态刻意差异化（`Initialize(实例)` / 工厂 / `SetInstance` / options / `SetProvider` / 注册表 / 无注入点）。Save/Data/UI/Localization 阶段当前缺少 provider/manager 构造参数；Audio 没有内置引导阶段；Camera 后端与 Audio 高级能力接口为 internal。详见 §11。
- **Profile 与 provider：** `BootstrapProfile` 只装 7 个数据值；provider 是代码，不能进 Profile（本轮裁定 data-only，不新增 provider 槽）。
- 第三方：VContainer `LifetimeScope` / `VContainerSettings`（Preloaded Settings + 根 `LifetimeScope` 预制体 + 编辑器 `BeforeSceneLoad` 钩子）/ `IAsyncStartable`；Zenject `ProjectContext` / `SceneContext` / `ScriptableObjectInstaller` / `BindExecutionOrder`；Reflex `ReflexSettings`（`Resources`）+ `ContainerScope`（每场景仍需）；Addressables `IInitializableObject` / `ObjectInitializationData`（在 catalog 之前、无 teardown）；UGF `GameFrameworkEntry` / `GameEntry` / `BaseComponent`；QFramework `Architecture<T>`（懒初始化、半初始化风险）；Unity `RuntimeInitializeOnLoadMethod` / `DefaultExecutionOrder` / `playModeStartScene`；UGS `UnityServices.State`；DOTween 自动初始化 + 隐藏 DDOL driver；SRDebugger `SRDebug.Init()`。

**代码路径推断（未实测）：**

- Integration 样例早期阶段失败 → `AppReady` 不执行 → `_booted` 永不完成 → BootScreen 挂起。
- 「每个项目都要重写 Settings/Config 引导阶段」——只观察到一个样例，属于概括。
- 全仓无 `IPreprocessBuildWithReport` / `playModeStartScene` 引用（grep 结果，非门禁）。

**建议探针（阶段 1 前可做）：**

1. 在 Integration 场景的 Asset 阶段注入一个必失败阶段，确认 BootScreen 是否挂起（验证 4.1 的推断）。
2. 连续两次 `RunAsync`，确认第二次返回时 `await` 立即完成（验证重入假就绪）。
3. 在空场景按 Play，确认无任何引导发生（验证入口依赖场景）。

---

## 附录 B：第三方来源

**VContainer**

- LifetimeScope：<https://github.com/hadashiA/VContainer/blob/master/VContainer/Assets/VContainer/Runtime/Unity/LifetimeScope.cs>
- VContainerSettings：<https://github.com/hadashiA/VContainer/blob/master/VContainer/Assets/VContainer/Runtime/Unity/VContainerSettings.cs>
- Entry points：<https://github.com/hadashiA/VContainer/blob/master/website/docs/integrations/entrypoint.mdx>
- UniTask 集成：<https://github.com/hadashiA/VContainer/blob/master/website/docs/integrations/unitask.mdx>
- Lifetime 概览：<https://vcontainer.hadashikick.jp/scoping/lifetime-overview>
- Project root LifetimeScope（Preloaded Settings + 根预制体）：<https://vcontainer.hadashikick.jp/scoping/project-root-lifetimescope>

**Addressables**

- 初始化流程：<https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/InitializeAsync.html>
- `IInitializableObject`：<https://docs.unity3d.com/Packages/com.unity.addressables@2.7/api/UnityEngine.ResourceManagement.Util.IInitializableObject.html>
- `ObjectInitializationData`：<https://docs.unity3d.com/Packages/com.unity.addressables@2.7/api/UnityEngine.ResourceManagement.Util.ObjectInitializationData.html>

**Reflex**

- 仓库与 README：<https://github.com/gustavopsantos/Reflex>
- `ReflexSettings`：<https://raw.githubusercontent.com/gustavopsantos/Reflex/main/Assets/Reflex/Configuration/ReflexSettings.cs>
- `UnityInjector`：<https://raw.githubusercontent.com/gustavopsantos/Reflex/main/Assets/Reflex/Injectors/UnityInjector.cs>

**Zenject / Extenject**

- 仓库与 README：<https://github.com/modesttree/Zenject>
- ProjectContext：<https://github.com/modesttree/Zenject/blob/master/UnityProject/Assets/Plugins/Zenject/Source/Install/Contexts/ProjectContext.cs>
- SceneContext：<https://github.com/modesttree/Zenject/blob/master/UnityProject/Assets/Plugins/Zenject/Source/Install/Contexts/SceneContext.cs>
- ScriptableObjectInstaller：<https://github.com/modesttree/Zenject/blob/master/UnityProject/Assets/Plugins/Zenject/Source/Install/ScriptableObjectInstaller.cs>

**EllanJiang GameFramework**

- GameFrameworkEntry：<https://github.com/EllanJiang/GameFramework/blob/master/GameFramework/Base/GameFrameworkEntry.cs>
- GameEntry：<https://github.com/EllanJiang/UnityGameFramework/blob/master/Scripts/Runtime/Base/GameEntry.cs>
- BaseComponent：<https://github.com/EllanJiang/UnityGameFramework/blob/master/Scripts/Runtime/Base/BaseComponent.cs>

**QFramework**

- QFramework.cs：<https://github.com/liangxiegame/QFramework/blob/master/QFramework.cs>

**Unity**

- RuntimeInitializeOnLoadMethod：<https://docs.unity.com/en-us/engine/current/script-reference/unityengine/runtimeinitializeonloadmethodattribute>
- DefaultExecutionOrder：<https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DefaultExecutionOrder.html>
- playModeStartScene：<https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/scenemanagement/editorscenemanager/playmodestartscene>
- Application.quitting：<https://docs.unity.com/en-us/engine/current/script-reference/unityengine/application/quitting>
- Application.wantsToQuit：<https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/application/wantstoquit>
- Application.exitCancellationToken：<https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-exitCancellationToken.html>
- Script serialization：<https://docs.unity3d.com/2022.3/Documentation/Manual/script-Serialization.html>
- SerializeReference：<https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SerializeReference.html>
- Addressables 初始化：<https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/InitializeAsync.html>

**其他**

- Unity Gaming Services：<https://docs.unity.com/en-us/services/services-core-api>
- UniTask：<https://github.com/Cysharp/UniTask>
- DOTween：<https://dotween.demigiant.com/documentation.php>
- DOTween 源码（`AutoInit` / 隐藏 driver）：<https://raw.githubusercontent.com/Demigiant/dotween/develop/_DOTween.Assembly/DOTween/DOTween.cs>
- DOTweenComponent：<https://raw.githubusercontent.com/Demigiant/dotween/develop/_DOTween.Assembly/DOTween/Core/DOTweenComponent.cs>
- SRDebugger：<https://www.stompyrobot.uk/tools/srdebugger/documentation/>

---

## 附录 C：探针建议

| 探针 | 目的 | 做法 | 预期 |
|---|---|---|---|
| P1 失败挂起 | 验证 4.1 的推断 | Integration 场景注入必失败阶段 | BootScreen 停在启动中；无错误 UI |
| P2 重入假就绪 | 验证重入静默返回 | 连续两次 `RunAsync`，第二次 `await` | 第二次立即完成，不代表启动结束 |
| P3 空场景 | 验证入口依赖场景 | 空场景按 Play | 无任何引导发生；门面未初始化 |
| P4 重复阶段 | 验证编辑器校验的必要性 | `RegisterDefaults` + `DefaultGameLauncher` 混用 | 同相位双份；门面幂等吞掉一份 |
| P5 Host 缺服务 | 验证 R3-AST-009 | `DefaultGameLauncher` 选 Host、不注入 `RemoteServices` | 启动抛 `ArgumentException` |

探针只用于评估，不进入门禁；结果回填本文件或对应台账。
