# Integration 全流程接入示例

> 收件人：维护者（实施 / 审计本示例时）。
> 本文档是 `Samples~/Integration` 的**实施与进度文档**：每完成一步更新 §五 状态表与 §七 进度日志（追加，不覆盖历史）。使用方文档在 `Assets/XFramework/Samples~/Integration/README.md`。

## 一、背景与目标

- 现存示例最大覆盖面是 `Samples~/Playable`（9/24 个核心模块、零资产、IMGUI）。
- 决策台账留有一条显式残余：**「完整 UI 纵切等有真实消费方的 YooAsset 配置后再做」**——本示例就是那个消费方。
- 目标流程：**启动游戏 → 选择游戏类型 → 设置 → 语言 → 进入游戏 → 播放声音 → 暂停 → 结算 → 退出游戏**，是一条完整的接入纵切；内置极简玩法只用于驱动流程，**不是在主张一套小游戏架构**。
- 目标覆盖面：**近乎全量核心模块**（24 个核心模块均有直接使用点），体量 2000+ 行可接受。
- 示例名：`Integration`（接入示例）；命名空间 `XFramework.Samples.Integration`；资源文件名统一前缀 `Integration.`。

## 二、已裁定事项（2026-11-12）

| # | 决策 | 内容 |
|---|---|---|
| D1 | 资源管线 | **真实全流程**：随示例附 UI 预制体 / 音频 / 场景 / 配置表；导入消费方后需把示例资源加入其 `AssetBundleCollectorSetting`；`EditorSimulate` 仅编辑器可跑 |
| D2 | Unity MCP | **不接**。验证走仓内脚本通道（`check-samples` / `run-tests` / 一次性 batchmode 探针） |
| D3 | 覆盖范围 | **近乎全量**：24 个核心模块 |
| D4 | 示例位置 | 核心包 `Assets/XFramework/Samples~/Integration/`（`Samples~` 由 UPM Samples 页签导入） |
| D5 | 与 Playable 的关系 | **保留 `Samples~/Playable`**（零配置导入即玩），新增 `Integration` 作为真实资源接入参考；二者受众不同 |
| D6 | 命名 | 不用 `MiniGame` 这类玩法概念；目录/命名空间/前缀统一用 `Integration` |

## 三、硬约束（已探明）

1. `Samples~/` 不参与 Unity 编译；`Tools/check-samples.ps1` 把所有 `.cs` 编成一个**平坦**程序集 `XFramework.Samples.CompileCheck`（无 asmdef、无 `DefineConstants`、无 `#if` 符号），且不在任何 `InternalsVisibleTo` → **只能用公开 API，不能写依赖 `UNITY_EDITOR` 等符号的代码块**。
2. 核心包 `Samples~/` 内部文件与目录**必须有 `.meta`**（`check-docs.ps1` 的 .meta 项递归覆盖）；`Samples~/` 本身与 `.sample.json` 例外。
3. **`Samples~` 对 Unity AssetDatabase 不可见**：YooAsset 采集不到、编辑器打不开其中的场景/预制体。仓内验收必须「复制到 `Assets/Samples/Integration` → 建临时 collector → 跑探针 → 清理」。
4. YooAsset `SettingLoader.LoadSettingData<AssetBundleCollectorSetting>()`：缺失时自动建 `Assets/AssetBundleCollectorSetting.asset`（空表 → 模拟构建报「pack asset info is empty」）；**存在两份及以上直接抛异常**。示例**不能**随包附带 collector setting。
5. `UIManager.OpenAsync<T>(assetPath)` / `ShowHudAsync` 与 `AudioManager.Play(location)` 只接受 YooAsset 地址；`IUIPanelFactory`、`IAudioClipLoader` 是 internal，无公开的代码直出通道。
6. 包内文本（含示例 README 与代码注释）不得出现 `Documentation/` 路径（`PackageBoundaryTests` 扫 `Samples~` 的 `.cs` 与 `.md`）。
7. `NamingConventionTests`（`UI`/`IO` 两字母缩写全大写等）与 `AllocationRulerTests` 同样扫 `Samples~` 的 `.cs`。
8. 公开 API 基线：**预期不变**（示例只用公开 API、不改框架源码）。若实施中确需改公开面，停下单独请用户确认。
9. **TMP 字体**：本仓库未导入 TMP Essentials，`TMP_Settings.defaultFontAsset` 不存在。示例面板用普通 `UnityEngine.UI.Text`，不依赖 TMP；`UIBinder` 的 TMP 绑定与 `UITipItem` 暂不使用（见 §六 偏离记录）。

## 四、示例设计

### 4.1 玩法与逐屏流程

示例的定位是**接入参考**：内置玩法只用来驱动流程（给设置、语言、存档、音频、UI 一个真实的使用场景），不是要让第三方照搬这套游戏架构。

两个小模式共享统一会话接口 `IIntegrationSession`（`StartAsync / SetPaused / StopAsync / Dispose`，`ReactiveProperty<int> Score`、`ReactiveProperty<float> TimeLeft`）：

- **TapRush（点星）**：6 个目标随机亮起，点中得分，30 秒倒计时；目标对象走 `PoolManager`，计时走 `TimerManager`，命中流走 `EventStream`。
- **OddOneOut（找异色）**：4 个色块选异色，连续 N 回合、每回合限时；回合推进走 `UpdateManager` + `IUpdateable`，候选索引用 `ListPool<int>`。

逐屏：

| 屏 | UI 形态 | 主要驱动模块 |
|---|---|---|
| 启动加载 | 场景内 IMGUI 启动屏（不走 `UIManager`）+ 模块清单 | Bootstrap、Asset、Data、Save、UI、Input、Localization、Settings、Log |
| 主菜单 | `UIPanelBase` 面板 | UI、Localization、Settings、Save、Message、Audio、File/Serialize（上次游玩摘要） |
| 类型选择 | `UIPanelBase`，两张模式卡片（时长/回合来自 Config） | Config、Localization、UI、Lock |
| 设置 | `UIPanelBase`（Popup 层）：总音量/BGM/音效、提示开关、恢复默认/应用 | Settings、Reactive、UI、Audio、Message |
| 语言 | `UIPanelBase`（Popup 层）：中/英 | Localization、Settings、UI、Message |
| 进入游戏 | `UIPanelBase`（Top 层）：进度条 + 阶段描述 | Pipeline、Asset、Scene、UI |
| 游戏中 | `UIPanelBase`：分数、倒计时、目标/选项、暂停 | 会话、Reactive、Event、Timer、Update、Pool、Input、UI、Audio、Camera、Lock |
| 暂停 | `UIPanelBase`（Popup）：继续/重开/主菜单/退出 | UI 显示栈、Lock、Settings |
| 结算 | `UIPanelBase`（Popup）：得分/最佳、重试/主菜单 | Message、Data、Save、Audio、Camera、Localization |
| 确认框 | `UIDialogPanel<bool>`（`ShowDialogAsync`） | UI 对话框 + 模态遮罩 |
| 退出 | 关面板 → 存档 → `Bootstrap.Shutdown` → `Application.Quit` | Bootstrap、Save、Settings、File/Serialize、Audio、Message/Timer/Pool/Lock |

### 4.2 模块覆盖清单（24 个核心模块）

| 模块 | 示例中的直接使用点 |
|---|---|
| Asset | `EditorSimulate` 初始化、`PreloadAllAsync`、`UnloadUnusedAssetsAsync`、`CheckLocationValid`、`GetPoolStatus` |
| Audio | `Initialize`、`Play/PlayAsync`、`MasterVolume`、`SetChannelVolume`、`Pause/Resume`、`StopAll`、`CopyChannels`、`Destroy` |
| Bootstrap | `GameLauncher.ConfigureStages` 覆写、各内置阶段的直接构造、`IBootstrapStage`、`BootstrapPhases.UserStart`、`Bootstrap.Shutdown` |
| Camera | `Initialize(CinemachineBrain)`、`Register`、`SwitchTo`、`SetFollow`、`Shake`、`SwitchToAsync`、`Destroy`、`CopyCameras/GetState` |
| Config | `Initialize`、`PreloadTableAsync<GameModeRow>`、`PreloadGlobalAsync<IntegrationRules>`、`Get/GetGlobal`、`Unload`、`CopyLoaded` |
| Data | `GetOrCreateBlock`、`MarkDirty`、`HasDirtyBlocks` |
| Diagnostics | `IDiagnosticPanel`、`DiagnosticsManager.Register/Unregister`、`IDiagnosticReport` |
| Event | `EventStream.Create<T>`、`Subscribe/Emit`、`DisposableBag`（TapRush 命中流） |
| File | `ReadAllBytes/WriteAllBytesAsync`、`Exists`、`GetPhysicalPath`、`FileDomain.AppData` |
| Input | `HasAction`、`SwitchActionMap`、`ObservePressed`、`WasPressedThisFrame`、`GetBindingDisplayString`、`LastActiveDeviceType` |
| Localization | `LanguageAssetPath`、`SwitchLanguageAsync`、`HasLanguage`、`CurrentLanguage`、`Subscribe`、`CopyLoadedLanguages` |
| Lock | `LockManager.Global`、`AddLock`、`IsLocked`、`GetSnapshot`、`Dispose` |
| Log | `LogCategory.Get`、`Info/Warning/Error/Exception`、`IsEnabled`、`CopyCategories` |
| Message | `Publish/Subscribe/SubscribeBuffered/RequestAsync`、`GetStats`、`Clear` |
| Pipeline | `Pipeline.Create()` + 阶段（预载 → 加载场景 → 打开面板），进度上报 |
| Pool | `PoolManager.Get/Return/Configure/CopyPoolStats/ClearAll`、`ListPool<int>` |
| Reactive | `ReactiveProperty<int/float>`、`SettingRef`（`IReactiveProperty`） |
| Save | `SaveAsync`、`TryLoadAsync`、`SlotExistsAsync`、`GetSlotMetaAsync`、`IsBusy` |
| Scene | `SceneManager.LoadSceneAsync`、`SceneLoadOptions.Additive`、`SetActiveScene`、`ActiveScene`、`AssetSceneHandle.UnloadAsync` |
| Serialize | `Serializer.Default.Serialize/Deserialize`、`TryGet`、`IsInitialized`（上次游玩摘要） |
| Settings | `Initialize<T>`、`Ref`、`Observe`、`SetValidator`、`SetMigrator`、`SaveAsync`、`MarkDirty`、`IsDirty` |
| Timer | `Every/After<TState>`、`TimerHandle.Remaining/Stop/Restart/Dispose`、`CancelAll`、`CopyActiveTimers` |
| UI | `UIRootNode`、`OpenAsync/PushAsync/PopAsync/CloseAllAsync`、`ShowMask`、`ShowDialogAsync`、`PreloadAsync`、`OnLanguageChanged` |
| Update | `Register/Unregister/Enable/Disable`、`IUpdateable`、`UpdateTier.Tier0`、`CopyNodes` |

> 扩展包 Luban / Fmod / UIAuthoring 不在本示例射程内（它们是独立可选件）。若某模块在实施中找不到自然用法，记录到 §七 并请用户裁定，**不硬塞**。

### 4.3 文件结构

每个文件与目录都要有同名 `.meta`（`.sample.json` 与 `Samples~` 根除外；目录 meta 用 `folderAsset: yes`）。

```text
Samples~/Integration/
├── .sample.json                       # Samples 页签显示名与说明
├── README.md                          # 导入 / 采集 / 运行 / 退出说明 + 模块接线表
├── Scripts/
│   ├── Bootstrap/  IntegrationLauncher.cs、IntegrationBootstrapProfile.cs、GameSettingsBootstrapStage.cs
│   ├── App/        AppFlowController.cs、IntegrationAddresses.cs、IntegrationMessages.cs、
│   │              LastRunStore.cs、IntegrationLogCategories.cs
│   ├── Settings/   IntegrationSettings.cs、IntegrationSettingRefs.cs、
│   │              IntegrationSettingsValidator.cs、IntegrationSettingsMigrator.cs
│   ├── Config/     GameModeRow.cs、IntegrationRules.cs
│   ├── Data/       IntegrationData.cs
│   ├── Game/       IIntegrationSession.cs、IntegrationResult.cs、GameBoardView.cs、
│   │              TapRushSession.cs、OddOneOutSession.cs、GameSessionFactory.cs、
│   │              GameSceneController.cs
│   ├── Pipeline/   GameLoadingPipeline.cs
│   ├── UI/         BootScreen.cs、MainMenuPanel.cs、ModeSelectPanel.cs、SettingsPanel.cs、
│   │              LanguagePanel.cs、LoadingPanel.cs、GamePanel.cs、PausePanel.cs、
│   │              ResultPanel.cs、ConfirmDialog.cs
│   └── Diagnostics/IntegrationDiagnosticPanel.cs
├── Prefabs/        Integration.MainMenu.prefab、Integration.ModeSelect.prefab、
│                   Integration.Settings.prefab、Integration.Language.prefab、
│                   Integration.Loading.prefab、Integration.GamePanel.prefab、
│                   Integration.Pause.prefab、Integration.Result.prefab、
│                   Integration.Confirm.prefab
├── Scenes/         Integration.Boot.unity（启动器 + UIRootNode + EventSystem + BootScreen + 相机）
│                   Integration.Game.unity（CinemachineBrain + 两台 vcam + 3D 背景 + GameSceneController）
├── Audio/          Integration.Bgm.wav、Integration.Click.wav、Integration.Correct.wav、Integration.Wrong.wav
└── Data/           Integration.GameModes.json、Integration.Rules.json、
                    Integration.Lang_zh_Hans.json、Integration.Lang_en.json、
                    Integration.Actions.inputactions、Integration.InputOptions.asset、
                    Integration.BootstrapProfile.asset
```

### 4.4 启动与退出接线

- `IntegrationLauncher : GameLauncher`，Inspector 面 = `_profile`（自建的 `IntegrationBootstrapProfile` 资产）+ `_uiRoot`；`ConfigureStages()`：
  1. 用各内置阶段的构造函数装配 Asset(0, Profile 的 `EditorSimulate`) / Data(3) / Save(4, Profile 的版本) / UI(2, `_uiRoot`) / Input(1, Profile 的输入资产) / Localization(90, Profile 的语言与表)；
  2. `Bootstrap.Register(new GameSettingsBootstrapStage())`（Phase 90，与 Localization 同相位并行——两者无相互依赖）；
  3. `Bootstrap.Register(new GameConfigBootstrapStage())`（Phase 90）。
- `BootScreen` 用 IMGUI 显示 `Bootstrap.State` / `LastProgress` 与模块就绪清单；`AppFlowController.Start` 调 `Bootstrap.WaitUntilReadyAsync(destroyCancellationToken)` 等启动就绪（失败时重抛并显示错误）。示例不再自造「启动完成」阶段。
- 启动后：` LocalizationManager.LanguageAssetPath = "Integration.Lang_{0}"` → 按设置切语言 → 读上次游玩摘要（File+Serialize）→ 关闭 BootScreen → 打开主菜单。
- 退出：`LockManager.Global.AddLock(Exit)` 防重复 → 二次确认 → `UIManager.CloseAllAsync` → `SaveManager.SaveAsync` → `SettingsManager.SaveAsync` → `LastRunStore.WriteAsync` → `MessageManager.Clear / TimerManager.CancelAll / PoolManager.ClearAll / LockManager.Dispose` → `Bootstrap.Shutdown()` → `Application.isEditor ? 显示「已清理，请手动停止 Play」 : Application.Quit()`。**不用 `#if UNITY_EDITOR`**（`Application.isEditor` 运行期判断）。
- `GameLauncher.OnDestroy` 会再调一次 `Bootstrap.Shutdown()`；所有自定义阶段必须幂等。

### 4.5 资源与地址方案

- YooAsset 收集器用 **`AddressByFileName`**（`Path.GetFileNameWithoutExtension`），包勾选 **Enable Addressable**，地址 = 文件名去扩展名（`Integration.MainMenu.prefab` → `Integration.MainMenu`）。
- 所有地址集中在 `IntegrationAddresses.cs`（唯一真相）；L2 守卫校验「地址常量 ↔ 资源文件 basename」一一对应。
- 消费方 README 三步：① Package Manager 导入示例；② 在 `AssetBundleCollectorSetting` 的 `DefaultPackage` 勾 Enable Addressable，新建组 + `MainAssetCollector`（Address Rule `AddressByFileName`、Pack Rule `PackDirectory`、Filter Rule `CollectAll`），路径指向导入的 `Integration` 根目录；③ 打开 `Integration.Boot.unity` 按 Play。要出 Player：把启动器的 Asset Play Mode 改为 Offline/Host 并先构建资源包。
- 仓内 L3：复制到 `Assets/Samples/Integration` → 一次性 Editor 方法创建唯一 collector setting → 跑探针 → 删除导入目录 / setting / `Bundles/`。**这些临时物一律不提交**。
- **仓内一键启动（2026-11-15）**：菜单 `Tools/XFramework/Samples/Integration/Import and Open Boot Scene` 把示例复制到 `Assets/Samples/Integration`、用 `Assets/DevTools/Editor/IntegrationCollectorSetup.cs` 幂等补齐采集组 `IntegrationDev`，然后打开 `Integration.Boot.unity`；`Import, Open and Play` 直接进 Play。清理菜单 `Tools/XFramework/Samples/Clean Imported Samples` 删除导入副本、工具创建的采集组与 `Bundles/`。这些开发产物已进 `.gitignore`。验收脚本仍走严格版 `CreateCollectorSetting`（已有任何采集设置则拒绝），不再复制临时 Editor 方法。

### 4.6 资产制作（不用 MCP）

- **手写并提交**：全部 `.cs`、`.json`、`.inputactions`、`.meta`。
- **生成物（已提交）**：预制体、两个场景、`Integration.InputOptions.asset` 都在 `Samples~/Integration/` 里。**原一次性生成器已删除**（2026-11-15）：`Tools/SampleGeneration/IntegrationAssetGenerator.cs` 与 `Tools/generate-integration-sample.ps1` 在 Profile 迁移后已无法运行（仍写 7 个已删字段、反射旧的 `_inputOptions`）。要重建资产，按本节与「生成物约定」手工做，或另写一个新生成器。
- **采集设置**：`Assets/DevTools/Editor/IntegrationCollectorSetup.cs`（原 `Tools/SampleGeneration/IntegrationVerifySetup.cs`，2026-11-15 迁移）；严格版供 `Tools/verify-integration-sample.ps1` 的 `-executeMethod` 调用，幂等版供开发菜单复用已有配置。
- **音频**：PowerShell `BinaryWriter` 合成 PCM 16-bit mono 44.1kHz `.wav`。
- 生成物约定（重建时按此检查）：面板根挂具体面板脚本、`RectTransform` 显式全屏拉伸、`Canvas`/`GraphicRaycaster` 由 `RequireComponent` 自动补、子节点按 `txt_/btn_/sld_/tgl_` 命名；启动器私有字段用 `SerializedObject` 写入；`EventSystem` 用 `InputSystemUIInputModule`；Boot 相机持唯一 `AudioListener`。

## 五、分步实施计划与状态

> 每步 = 一个可独立验证的原子提交边界；每个提交前跑该步验证命令，绿后再提交。
> 状态词：`未开始` / `进行中` / `已完成` / `阻塞` / `已否决`。

| 步骤 | 内容 | 产出 | 验证命令 | 提交信息（建议） | 状态 |
|---|---|---|---|---|---|
| **M0** | 前置探针（不提交）：核实 §六 的待核项 | 结论回填 §六 | 读源码 | — | 已完成 |
| **M1** | 第一条可玩纵切：启动 → 主菜单 → 设置 → 语言 → 退出 | 启动器/AppFlow/Settings/语言/主菜单/设置/语言/确认框 + Boot 场景 + 4 个预制体 + 中英表 + 输入资产 + 生成器 | `check-samples.ps1`；`check-docs.ps1 -Enforce`；生成器跑通 | `feat(samples): Integration 启动与设置纵切`（脚本+数据）<br>`feat(samples): Integration 预制体与启动场景`（生成器+资产） | 已完成（运行期验收留 M5） |
| **M2** | 进入游戏：类型选择 + TapRush + 音频 + 结算 + 存档 | Config 行/规则/阶段、Data、会话接口、TapRush、ModeSelect/Game/Result 面板、Game 场景、模式表/规则表、4 个 wav | `check-samples.ps1`；L3 探针（加载 Game 场景、得分、存档） | `feat(samples): Integration 玩法与结算` | 已完成（运行期验收留 M5） |
| **M3** | 第二种玩法 + 高级模块接线 | OddOneOut、暂停/加载面板、GameLoadingPipeline、Cinemachine 相机、诊断页、Lock/Update/Pool/Event 接线 | `check-samples.ps1`；L3 探针 | `feat(samples): Integration 高级模块接线` | 已完成（运行期验收留 M5） |
| **M4** | 常驻结构守卫：prefab/scene YAML 的脚本 GUID、地址、`.meta`、`samples` 路径 | `Tests/Runtime/Samples/IntegrationSampleStructureTests.cs` + meta | `run-tests.ps1 -Fixture IntegrationSampleStructureTests -Platform PlayMode` | `test(samples): Integration 资产结构守卫` | 已完成 |
| **M5** | 导入验收探针 + 脚本 | `IntegrationProbeTests.cs` + meta、`Assets/DevTools/Editor/IntegrationCollectorSetup.cs`（2026-11-15 迁移）、`Tools/verify-integration-sample.ps1` | fixture（无 env 跳过）+ 脚本跑通 L3 | `test(samples): Integration 导入验收探针` | 已完成（1/1 通过） |
| **M6** | 登记与文档：`package.json` samples、包 README 示例段、示例 README、CHANGELOG、决策台账残余结案 | 文档与登记 | `run-tests.ps1 -Fixture DependencyDeclarationTests -Platform EditMode`；`-Filter PackageBoundaryTests -Platform PlayMode`；`check-docs.ps1 -Enforce` | `docs(samples): Integration 示例登记与说明`<br>`docs(ledger): 完整 UI 纵切残余结案` | 已完成 |
| **M7** | 收尾：全量双平台 + 文档门禁 + 行尾检查 + 进度回填 | 门禁结果 | `run-tests.ps1`（全量，0 失败）；`check-docs.ps1 -Enforce`；`git ls-files --eol` | 如有修复再提交 | 已完成 |
| **M8** | 仓库内一键启动 + 人工全流程走查 | `Assets/DevTools/Editor/SampleDevMenu.cs`、`IntegrationCollectorSetup.cs`、菜单与清理；`Documentation/Workflow.md` | 菜单导入 / 打开 / Play / 清理；走查记录见 §5.1 | `feat(tools): 仓库内示例一键导入与启动`<br>`docs(samples): Integration 人工全流程验收记录` | 进行中（工具已落地，待人工走查） |

### 5.1 人工全流程验收记录

> **M5 是导入探针**（18 个地址 + 一个面板 + 场景加载），**不是**完整流程走查。本节记录第一次真人按 Play 走完流程；用户报告前保持「待执行」。

| 日期 | Unity | YooAsset | commit | 执行人 | 步骤结果 | Console 异常 | 后续 |
|---|---|---|---|---|---|---|---|
| 待执行 | 6000.4.5f1 | 2.3.18 | — | — | — | — | — |

走查清单（详细预期见 `Documentation/Workflow.md` 的 Samples 小节）：

1. 启动 → 主菜单：BootScreen 各行 `[ok]`，主菜单显示标题、四个按钮与「累计 / 上次模式 / 最佳」。
2. 设置：滑条可拖；应用写盘；**恢复默认后 BGM 音量立即回默认**。
3. 语言：English 即时生效且无「语言 'en' 未加载」；重启后恢复已保存语言。
4. 模式选择：点星 / 找异色与时长正确。
5. 点星：加载进度、30 秒倒计时、点中加分、结算；**主菜单不在游戏 UI 后面**。
6. 重试 / 结算 → 主菜单：摘要显示本局更新后的值。
7. 找异色：5 回合、20 秒/回合、答错扣时。
8. 暂停：Esc 打开、倒计时冻结；继续 / 重开 / 主菜单 / 退出 均正常。
9. 退出：确认框 → 存档 / 设置 / 摘要落盘 → BootScreen 显示「服务已清理…」。
10. 持久化：再次 Play，摘要与最佳分保留。

## 六、风险与待核（M0 结论）

1. `AddressByFileName` + `EnableAddressable`：`AddressByFileName` 已核实为 `Path.GetFileNameWithoutExtension`（`DefaultAddressRule.cs:14-21`）；仍待 L3 确认 Enable Addressable 未勾选时的退化行为。
2. `EditorSimulate` 需要唯一 `AssetBundleCollectorSetting`；缺失时自动建空表 → 「pack asset info is empty」。
3. 导入路径含版本号：README 不写死绝对路径；L3 用 `Assets/Samples/Integration`。
4. 面板预制体根要求：根组件具体面板类型、`Canvas`/`GraphicRaycaster` 由 `RequireComponent` 自动补、根 `RectTransform` 全屏拉伸、单位变换。
5. `UIViewBase`/`UIPanelBase` 的 `OnUpdate/OnLanguageChanged/OnPause/OnResume` 为 `protected internal virtual`；跨程序集覆写只能写 `protected override`。
6. ~~`DefaultGameLauncher.Start` 不转发进度~~：2026-11-15 已由 `Bootstrap.State` / `LastProgress` / `WaitUntilReadyAsync` 解决，示例改用就绪面（见 §4.4）。
7. `SceneLoadOptions.Additive` + `ActivateAfterLoad` 与 `SetActiveScene` 的配合；卸载后 `CameraManager.Destroy`。
8. Cinemachine `Shake` 需要 vcam 上有 `CinemachineImpulseListener`（`CinemachineImpulse.cs:47-55`）。
9. `InputSystemUIInputModule` 与示例 `.inputactions` 的 ActionMap 切换共存。
10. Localization 的 `LanguageAssetPath` 模板与文件名前缀一致。
11. `SettingsManager.Ref` 必须在 `Initialize<T>` 之后首次访问；退出时 `SaveAsync` 顺序。
12. batchmode 下 `AudioManager.PlayAsync` 是否返回非 default 句柄（无音频设备）。
13. `UITipItem` 需要 `CanvasGroup` + 子节点 `TMP_Text`；**本仓库未导入 TMP Essentials**，故 M1 不使用 Tip（见偏离记录）。
14. `DiagnosticsManager` 页签在模块未初始化时 `Collect` 不抛。
15. `.meta`/GUID：生成器复制脚本/JSON/actions 的 `.meta` 后再生成；提交前检查未跟踪 `.meta`。
16. 平坦示例程序集：`Integration` 类型名/命名空间不得与其他示例冲突。
17. 生成脚本运行环境：测试壳避锁、`-logFile` 绝对路径、`-executeMethod` 用 `-quit`、测试**不加** `-quit`。
18. 临时 collector setting / 导入目录 / `Bundles/` 不得进入提交。
19. 若某模块找不到自然用法：记录并请用户裁定，不硬塞。

### 偏离记录

- **M1 · TMP → 普通 Text**：本仓库没有导入 TMP Essentials（`Assets/TextMesh Pro` 不存在、`TMP_Settings` 无默认字体），因此面板文本用 `UnityEngine.UI.Text`，不引用 `UIBinder`（其绑定目标是 `TMP_Text`）。`UITipItem` 也不在 M1 使用。UI 模块的其余能力（OpenAsync / 栈 / 对话框 / 遮罩 / 语言回调）照常演示。

## 七、进度日志（追加式，不覆盖历史）

- 2026-11-12：文档建立；用户裁定 D1 真实资源 / D2 不接 MCP / D3 近乎全量 / D5 保留 Playable、新增示例 / D6 命名 `Integration`。
- 2026-11-12：M0 完成。核实 `AddressByFileName`、Config JSON `{"Items":[...]}`、面板根要求、`UIViewBase` 钩子可见性、`UITipItem` 要求、`Shake` 需要 `CinemachineImpulseListener`、`InputSystemOptionsAsset` 字段名。发现本仓库无 TMP Essentials → M1 偏离：用普通 `Text`。
- 2026-11-12：M1 进行中——已落 `Integration.meta`、`Scripts/{Bootstrap,App,Settings,UI}`、`Data/` 目录与启动器 / 设置 / 语言 / 主菜单 / 确认框脚本；下一步补语言表、输入资产、`.sample.json`、README 骨架与资产生成器。
- 2026-11-12：**M1 完成（运行期验收留 M5）**。26 个 `.cs` 通过 `check-samples.ps1`；生成器产出 4 个面板预制体、`Integration.Boot.unity`、`Integration.InputOptions.asset`；`check-docs.ps1 -Enforce` 全绿（示例编译 / `.meta` / 文档门禁）。
  - **生成器两个坑**：① `& Unity.exe` 会立即返回、拿不到退出码（Unity 派生工作进程）→ 改用 `Start-Process -Wait`，与 `run-tests.ps1` 一致；② `SerializedObject` 对继承自包程序集的 `_inputOptions` 字段在 batchmode 下序列化成 `fileID: 0` → 改用反射写字段 + `EditorUtility.SetDirty`，已实测落盘为 `{fileID: 11400000, guid: ...}`。
  - Unity 安装路径不在 Hub 候选里（本机为 `D:\Program Files\Unity\6000.4.5f1\Editor\Unity.exe`），已在脚本候选中补 `D:\Program Files\Unity\<version>\Editor\Unity.exe`。
  - **偏离**：示例文本用普通 `UnityEngine.UI.Text`（仓库未导入 TMP Essentials），`UIBinder` / `UITipItem` 暂不使用。
  - **M1 提交**：`a46c256a`（脚本 + 数据 + 本文档）、`f8c3f85d`（生成器 + 预制体/场景/InputOptions）、`f51b2e38`（回填提交号）。
- 2026-11-12：**M2 完成（运行期验收留 M5）**。新增 Config 表/规则与引导阶段、Data 块、会话接口/棋盘视图/点星玩法、模式选择/玩法/结算面板、Game 场景、4 个合成 WAV 与音频接线；`check-samples` 37 个 `.cs` 通过，`check-docs.ps1 -Enforce` 全绿。
  - 踩坑：`IDataBlock` 实际要求 `DataVersion` / `OnSave` / `OnMigrate`（XML 示例只展示了一部分）；`GamePanel` 曾把 `Image.fillAmount` 写到 `Text` 上；生成器的 `#region` 结构因插入新方法失配（CS1028）。
  - 生成器新增「预置已有 `.meta`」逻辑，尽量避免每次重生成换 GUID（能否被 Unity 保留待下一次生成验证）。
- 2026-11-12：**M3 完成（运行期验收留 M5）**。新增 OddOneOut（Update + ListPool + 计时）、`GameLoadingPipeline`（Asset 预载 → Scene 加载 → UI 打开）、`GameSceneController`（Cinemachine 注册 / 切换 / 跟随 / 震屏）、暂停 / 加载面板、诊断页签、Input 暂停键与 Lock 进入锁；`check-samples` 43 个 `.cs` 通过。
  - **GUID 预置验证通过**：本次重生成只改了 `.prefab/.unity` 内容，既有 `.meta` 与 InputOptions 资产 GUID 未漂移（`git status` 只剩资源文件 ` M`，meta 无 ` M`）。
- 2026-11-12：**M4 完成**。新增 `Tests/Runtime/Samples/IntegrationSampleStructureTests.cs`（10 例全绿）：必需文件、`.meta` 完整性、`package.json` 登记、地址常量 ↔ 资源文件名一一对应、代码字面量都是登记地址、prefab/scene 脚本 GUID 可解析、面板根脚本继承基类、场景引用控制器、无条件编译/`UnityEditor`、无 `Documentation/` 引用。`package.json` 的 samples 数组已在 M4 登记（早于 M6，因为守卫要读它）。
  - 踩坑：`Path.Combine` 的 `/` 与 `Directory.GetFiles` 的 `\` 混用导致 `StartsWith` 误判——守卫里加了统一分隔符的 `IsUnder`。
- 2026-11-12：**M5 完成，导入验收探针实测通过（1/1）**。`Tools/verify-integration-sample.ps1` 复制示例到 `Assets/Samples/Integration` → 临时 Editor 方法创建 `DefaultPackage` 采集设置（Enable Addressable + `AddressByFileName` / `PackDirectory` / `CollectAll`）→ `XFW_INTEGRATION_PROBE=1` 跑 `IntegrationProbeTests` → finally 清理全部临时物。
  - 探针实测：18 个地址全部 `CheckLocationValid == true`；MainMenu 预制体与 Click 音频加载有效；`UIManager.OpenAsync<UIPanelBase>("Integration.Loading")` 成功；Game 场景 Additive 加载 + 卸载成功。
  - 两个坑：① Unity 的 NUnit 版本没有 `Assert.Warn`（CS0117）→ 改成 `Debug.LogWarning`；② 探针单独加载 Game 场景时没有 Boot 场景的 `CinemachineBrain`，`GameSceneController` 原先记 **Error** 被测试框架判为未处理错误 → 下调为 Warning（无 Brain 只影响相机演示，不影响场景其余内容）。
- 2026-11-12：**M6 完成**。包 README 示例清单改为四组并登记 `Integration`；CHANGELOG `[Unreleased] → ### Added → #### Samples` 增加条目；决策台账新增「完整 UI 纵切示例」并显式关闭 2026-10-21 的残余项；示例 README 已含导入三步 / 操作 / 已知限制。
  - **待用户确认**：`CLAUDE.md` 的「文档分层」表尚未登记 `Documentation/Integration.md`（按仓规，CLAUDE.md 的修改需用户确认）。→ **2026-11-13 用户确认并已登记。**
  - **M6 提交**：`3a4a2870`。
- 2026-11-12：**M7 进行中**——全量双平台测试后台运行中；提交链：`a46c256a` / `f8c3f85d` / `f51b2e38`（M1）、`4e5cf2fa`（M2）、`e1a5f7cf`（M3）、`e8fa483b`（M4）、`9d700c19`（M5）、`3a4a2870`（M6）。
- 2026-11-12：**M7 完成，全量门禁绿**。
  - 首轮全量：PlayMode 2437 例 / **1 失败**（`NamingConventionTests`：探针方法名 `AndUiAudioSceneWork` 里的 `Ui` 应为 `UI`）；EditMode 752 例 / 0 失败。
  - 修正：探针方法改名；`SettingsPanel.OnApply` 与 `LanguagePanel.SwitchAsync` 的 `async void` 改为 `UniTask`（仓规：`async void` 仅限 Unity 生命周期入口）；找异色单回合时长 45s → 20s。
  - 复跑：**PlayMode 2437 例（2436 通过 / 0 失败 / 1 跳过 = 探针环境变量门控）；EditMode 752 例（746 通过 / 0 失败 / 6 跳过）**；`check-samples` 43 个 `.cs` 通过；`check-docs.ps1 -Enforce` 通过；最终示例的导入探针复跑 **1/1 通过**。
  - 行尾：`git ls-files --eol` 确认示例文本文件全部 `i/lf`，4 个 WAV 为 `-text`（二进制，符合 `.gitattributes`）。
  - **2026-11-13**：`Documentation/Integration.md` 已登记进 `CLAUDE.md` 的「文档分层」表（用户确认）。
- 2026-11-15：**M8 启动前修复**。代码阅读发现并修复五处会在走查中暴露的缺陷：语言切换被 `HasLanguage` 缓存判断拦截（首次切 English / 启动恢复语言）、「恢复默认」不重推音量、结算后 `_lastRun` 不更新（主菜单/退出落盘显示上一会话）、结算「返回主菜单」不重新打开主菜单、进入游戏不关闭主菜单（`Push` 只失焦不隐藏）。`AudioSample` 增加无 YooAsset 地址时的降级 Warning。提交 `a5be4894`。
- 2026-11-15：**开发菜单落地**。新增 `Assets/DevTools/Editor/SampleDevMenu.cs`（导入 / 打开 / Play / 清理）与 `IntegrationCollectorSetup.cs`（从 `Tools/SampleGeneration/IntegrationVerifySetup.cs` 迁移，严格版 + 幂等版 + 清理）；`Tools/verify-integration-sample.ps1` 改为直接调用新类，且已有采集设置时**拒绝**而不是删除。提交 `a8a74a27`。
- 2026-11-15：**Example / Cinemachine 场景与 README 落地**。新增 `ExampleSampleHub` / `CinemachineSampleOverlay`、两个场景与 README、生成器 `ExampleSceneGenerator.cs` / `CinemachineSceneGenerator.cs` 与通用 `Tools/generate-sample-scene.ps1`；提交 `00776d17` / `fd729141`。新增结构守卫 `ExampleSampleStructureTests` / `CinemachineSampleStructureTests`（红灯证明后 9/9、10/10 全绿），提交 `5323f9a1`。
- 2026-11-15：**M8 进行中**。菜单可导入 / 配置 / 打开；人工走查由用户执行，结果填入 §5.1。M8 完成前不标记「已完成」。
- 2026-11-15：**人工走查首个缺陷（已修）**。停止 Play 时出现「回池面板 ResultPanel 时抛出异常」：`GameLauncher.OnDestroy → Bootstrap.Shutdown()` 先关掉 AssetManager，`UIRootNode.OnDestroy → UIManager.Dispose` 随后回池面板，`AssetPanelFactory.Release` 调 `AssetManager.DestroyInstance` 抛「尚未初始化」。修在框架拆卸宽容层：AssetManager 未初始化时直接销毁实例（已无池可回，实例本就随场景销毁）；回归用例 `AssetPanelFactoryTests.Release_AssetManagerUninitialized_DestroysInstanceWithoutThrowing`。
