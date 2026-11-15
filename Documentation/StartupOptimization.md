# 启动方式评估与优化计划

> **状态：** 评估完成，计划待裁定；本文件只记录 2026-11-15 的评估与计划，不代表任何条目已批准实施。
> **范围：** Bootstrap / GameLauncher / Pipeline 子阶段超时 / 启动诊断与编辑器校验。
> **读者：** 维护者（实施本轮优化时）+ 下一轮审计者。
> **真相分流：** 本文件只记评估与计划；用户裁定后的长期结论仍按收件人分流到 `Assets/XFramework/Runtime/Bootstrap/README.md`（使用方契约）、`Documentation/Modules/Bootstrap.md`（模块决策台账）、`Documentation/Ledger.md`（跨模块结论）。
> **行号说明：** 下文行号以 2026-11-15 工作区为准；引用时优先看符号名。

---

## 一、背景与结论

用户对现有启动方式仍不满意，确认集中在三处：

1. **无就绪 / 进度 / 失败信号**：`GameLauncher.Start` 是 `async void`，失败只留日志；进度不转发；没有可 await 的完成信号。Integration 样例不得不自造 `AppReadyBootstrapStage`（相位 91）才能让业务流程知道启动结束。
2. **配置只覆盖一半**：`DefaultGameLauncher` 的 8 个 Inspector 字段之外，`RemoteServices` / `DecryptionServices` / `CryptoProvider` / 自定义 provider/controller 只能继承子类写代码；部分模块没有内置引导阶段。
3. **登记与相位太手工**：新增服务 = 实现 `IBootstrapStage` 类 + 手挑一个 int 相位 + 登记；`UserStart` 与 `Localization` 同为 90；`Save` 依赖 `Data` 只是注释约定，机器不校验。

### 1.1 总结论

| 痛点 | 判定 | 建议 | 需重开既有裁决 |
|---|---|---|---|
| 无就绪 / 进度 / 失败信号 | **真缺口**，三个里最值得做 | `Bootstrap` 增加运行状态 + 共享就绪任务 + 重入合并；`GameLauncher` 暴露 `StartupTask` | 是（两条 `暂不做`） |
| 配置只覆盖一半 | **大部分是结构性限制**，小部分是可改善的 ergonomics | 服务实例继续走代码通道；补子类模板与 stage 工厂目录；不建议 Installer 形状 | 否（维持 `不做`） |
| 登记与相位太手工 | **不是设计落后**（整数排序是业界常态），缺便捷路径与机器校验 | `BootstrapStage.Create` + `BootstrapStages` 目录 + 内置不变量测试 | 否（维持 `不做`） |

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
| 服务实例不可序列化 | `DefaultGameLauncher` 8 个字段之外全在代码里 | 远程资源、加密、自定义 provider 必须写子类 |
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
| **XFramework（现状）** | `GameLauncher.Awake/Start`；手动 `RunAsync` | `Phase` int，同相位并行 | `RunAsync` 返回 UniTask，但**启动器不暴露**；重入静默返回 | 8 个 Inspector 字段 + 代码子类 | **无** |

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

**建议**：先提供 `BootstrapStages` 工厂目录（见 4.3），让这些模块有现成的组装件；是否升级为「核心 stage + `DefaultGameLauncher` 字段」需要逐模块论证，**默认不做**。

#### 2c. 字段面维护——可选，低优先级

8 个字段硬编码在 `DefaultGameLauncher`，加模块要改类、改反射字段数测试（`Tests/Runtime/Bootstrap/GameLauncherTests.cs:294-303` 锁定 8 个）。可以拆成按模块的 `[Serializable]` settings 组（组合，不是继承），降低改类频率。纯重构，不增加能力，建议随 2b 一起做或不做。

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
2. **`BootstrapStages` 目录**：`BootstrapStages.Asset(options)` / `.Save(options)` / `.UI(root)` / `.Data()` / `.Localization(lang, table)` / `.Input(options)`，让登记代码可读且集中；`DefaultGameLauncher` 改用它。
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
- 可选的 `playModeStartScene` 菜单开关（默认不覆盖用户配置）。

### 5.2 诊断面板升级

现有 `BootstrapDiagnosticPanel` 只列阶段名/相位/权重。建议补：运行状态、开始/结束时间、进度、失败原因；与 4.1 的 `State` / 进度快照共用数据。

### 5.3 子阶段超时（模块台账唯一 `未做`）

现状是相位包成 `ParallelStage`，拿不到逐阶段超时；不观察取消的阶段会挂死启动。修法在 Pipeline（`ParallelStage` 支持子阶段超时），台账已指定方向。必须如实标注边界：超时只能报告失败并取消令牌，**不响应取消的阶段仍会跑完**。

### 5.4 文档订正

- `Documentation/Modules/Bootstrap.md:64` 仍称 GameLauncher「当前零使用（全仓无场景/预制体挂载）」——Integration 样例已挂 `IntegrationLauncher`（`Samples~/Integration/Scenes/Integration.Boot.unity`），需订正。
- Bootstrap README 补「启动器与就绪面」的使用指引（若 4.1 落地）。

### 5.5 可选自动启动（VContainer 式）

settings asset + 根预制体 + `BeforeSceneLoad` 兜底，解决「任意场景按 Play」。用户未把它列为痛点，且触及「启动器可选」的定位，建议列为独立待裁项，不并入本轮。

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

## 七、分阶段实施计划

> 每阶段以「用户裁定通过」为前提；未裁定的阶段不实施。每阶段是一个原子提交边界，验证通过后再提交，不提前实现后续阶段。

| 阶段 | 内容 | 公开 API | 验证 |
|---|---|---|---|
| 0 | 文档订正（`Bootstrap.md`「零使用」）+ 裁定落台账 | 无 | `Tools/check-docs.ps1 -Enforce` |
| 1 | 就绪状态 + 共享任务 + 重入 join + `GameLauncher.StartupTask` | 新增 | API 基线 + CHANGELOG `### Added`；`run-tests.ps1 -Filter Bootstrap` + 全量 |
| 2 | `BootstrapStage.Create` + `BootstrapStages` 目录 + 内置不变量测试 | 新增 | 定向 + 全量；API 基线 |
| 3 | 编辑器 / 构建时校验 + 诊断面板升级 | Editor 新增 | Editor 测试 + `check-docs` |
| 4 | 子阶段超时（Pipeline） | 视形状 | Pipeline + Bootstrap 测试 |
| 5 | 待裁：Settings/Config 核心 stage、可选自动启动 | 视裁定 | 按裁定 |

### 阶段 1 细节

**改动文件（预计）：**

- `Assets/XFramework/Runtime/Bootstrap/Bootstrap.cs`：`BootstrapState`、`State`、`WaitUntilReadyAsync`、重入 join、最近进度快照；`ResetForNewSession` 复位等待者。
- `Assets/XFramework/Runtime/Bootstrap/GameLauncher.cs`：`StartupTask`。
- `Assets/XFramework/Runtime/Bootstrap/README.md`：行为契约、已知限制、设计取舍补条目。
- `Assets/XFramework/Tests/Editor/Bootstrap/BootstrapTests.cs`、`Tests/Runtime/Bootstrap/GameLauncherTests.cs`、`GameLauncherFrameTests.cs`：状态迁移、等待者、失败重抛、重入 join、销毁取消。
- `Assets/XFramework/Tests/Runtime/Architecture/PublicApiBaseline.txt`、`CHANGELOG.md`。

**必须定稿的语义（实施前请用户确认）：**

| 场景 | 建议语义 |
|---|---|
| 第二次 `RunAsync` 在途 | 返回第一次运行的任务；第二调用方的令牌不参与本次运行（文档写明） |
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

- 新增 `Assets/XFramework/Runtime/Bootstrap/BootstrapStage.cs`（委托工厂）与 `BootstrapStages.cs`（内置目录）。
- 委托阶段的 `Shutdown` 由调用方提供；不替调用方判断所有权。
- 内置不变量测试放 `Tests/Editor/Bootstrap/BootstrapInvariantTests.cs`（或并入现有 Bootstrap 测试）。
- `DefaultGameLauncher` 改用 `BootstrapStages`，行为不变。

### 阶段 3 细节

- 新增 `Assets/XFramework/Editor/Bootstrap/BootstrapValidator.cs` + 菜单项（`Tools/XFramework` 下）。
- 可选的 `IPreprocessBuildWithReport` 构建前校验。
- `BootstrapDiagnosticPanel` 升级；Editor 公开 API 基线按需更新。
- 探头：在 Integration 场景制造「缺 `RemoteServices` 的 Host」与「重复内置阶段」，确认校验能报出来。

### 阶段 4 细节

- `Runtime/Pipeline/ParallelStage.cs` 支持子阶段超时；Bootstrap 侧按需传入。
- 边界如实写进 Pipeline README 与 Bootstrap README：超时只能报告失败并取消令牌，不响应取消的阶段仍会跑完。

### 阶段 5 细节

- Settings/Config 核心 stage：先逐模块论证初始化 API 是否可通用；默认不做。
- 可选自动启动：需要 settings asset + 根预制体 + `BeforeSceneLoad`；默认关闭，不改变「Launcher 可选」的契约。

---

## 八、待裁定清单

| # | 裁定项 | 选项 | 默认推荐 | 影响面 |
|---|---|---|---|---|
| 1 | 是否重开「就绪句柄」与「重入 join」两条 `暂不做` | 重开 / 维持 | **重开** | 公开 API、基线、CHANGELOG |
| 2 | 若只允许一条最小修法：是否接受「重入改为警告并抛出」 | 接受 / 不接受 | 接受作为退路 | 行为变更（现有调用方） |
| 3 | `GameLauncher` 是否暴露 `StartupTask` | 暴露 / 不暴露 | 暴露 | 公开 API |
| 4 | 配置面：接受「服务实例只能写代码 + 模板/工厂改善」，还是重开 Installer 形状 | 前者 / 重开 | **前者** | 如重开则触及台账 `不做` |
| 5 | 是否为 Settings/Config 补核心 stage | 补 / 不补 | **不补**（Settings 泛型按类型、Config 预加载是项目知识） | 公开 API、默认组合 |
| 6 | 编辑器 / 构建时校验是否排期 | 排期 / 不排 | **排期** | 纯 Editor，无运行时 API |
| 7 | 子阶段超时是否排期 | 排期 / 不排 | 后置 | Pipeline 公开面 |
| 8 | 本轮报告的落点 | 本文件 / 模块台账 / 仅对话 | 本文件 + 裁定后落模块台账 | 文档 |

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

## 附录 A：事实核对状态

**已核对（源码 / 官方文档）：**

- 本仓 Bootstrap / GameLauncher / DefaultGameLauncher / BootstrapPhases / Pipeline 的行为与契约；测试锁定（含 `GameLauncherTests:294-303` 的 8 字段断言）。
- `ProjectSettings/EditorBuildSettings.asset` 的 `m_Scenes: []`。
- `SettingsManager.Initialize<T>` 为按类型泛型；`ConfigManager.Initialize()` 无参。
- 第三方：VContainer `LifetimeScope` / `VContainerSettings` / `IAsyncStartable`；Zenject `ProjectContext` / `SceneContext` / `ScriptableObjectInstaller` / `BindExecutionOrder`；UGF `GameFrameworkEntry` / `GameEntry` / `BaseComponent`；QFramework `Architecture<T>`；Unity `RuntimeInitializeOnLoadMethod` / `DefaultExecutionOrder` / `playModeStartScene`；UGS `UnityServices.State`；DOTween 自动初始化；SRDebugger `SRDebug.Init()`。

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
- Addressables 初始化：<https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/InitializeAsync.html>

**其他**

- Unity Gaming Services：<https://docs.unity.com/en-us/services/services-core-api>
- UniTask：<https://github.com/Cysharp/UniTask>
- DOTween：<https://dotween.demigiant.com/documentation.php>
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
