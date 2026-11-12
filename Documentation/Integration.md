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
| Bootstrap | `DefaultGameLauncher.ConfigureStages` 覆写、`IBootstrapStage`、`BootstrapPhases.UserStart`、`Bootstrap.Shutdown` |
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
│   ├── Bootstrap/  IntegrationLauncher.cs、GameSettingsBootstrapStage.cs、
│   │              AppReadyBootstrapStage.cs
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
                    Integration.Actions.inputactions、Integration.InputOptions.asset
```

### 4.4 启动与退出接线

- `IntegrationLauncher : DefaultGameLauncher`，`ConfigureStages()`：
  1. `base.ConfigureStages()` → Asset(0, 字段 `EditorSimulate`) / Input(1, 字段) / UI(2, 字段 `_uiRoot`) / Data(3) / Save(4, 字段) / Localization(90, 字段)；
  2. `Bootstrap.Register(new GameSettingsBootstrapStage())`（Phase 90，与 Localization 同相位并行——两者无相互依赖）；
  3. `Bootstrap.Register(new AppReadyBootstrapStage(_appFlow))`（Phase 91，最后通知 AppFlow 启动完成）。
- `BootScreen` 用 IMGUI 显示模块就绪清单；`AppReadyBootstrapStage` 完成 `UniTaskCompletionSource`；`AppFlowController.Start` 等待该信号（带 `destroyCancellationToken`）。
- 启动后：` LocalizationManager.LanguageAssetPath = "Integration.Lang_{0}"` → 按设置切语言 → 读上次游玩摘要（File+Serialize）→ 关闭 BootScreen → 打开主菜单。
- 退出：`LockManager.Global.AddLock(Exit)` 防重复 → 二次确认 → `UIManager.CloseAllAsync` → `SaveManager.SaveAsync` → `SettingsManager.SaveAsync` → `LastRunStore.WriteAsync` → `MessageManager.Clear / TimerManager.CancelAll / PoolManager.ClearAll / LockManager.Dispose` → `Bootstrap.Shutdown()` → `Application.isEditor ? 显示「已清理，请手动停止 Play」 : Application.Quit()`。**不用 `#if UNITY_EDITOR`**（`Application.isEditor` 运行期判断）。
- `GameLauncher.OnDestroy` 会再调一次 `Bootstrap.Shutdown()`；所有自定义阶段必须幂等。

### 4.5 资源与地址方案

- YooAsset 收集器用 **`AddressByFileName`**（`Path.GetFileNameWithoutExtension`），包勾选 **Enable Addressable**，地址 = 文件名去扩展名（`Integration.MainMenu.prefab` → `Integration.MainMenu`）。
- 所有地址集中在 `IntegrationAddresses.cs`（唯一真相）；L2 守卫校验「地址常量 ↔ 资源文件 basename」一一对应。
- 消费方 README 三步：① Package Manager 导入示例；② 在 `AssetBundleCollectorSetting` 的 `DefaultPackage` 勾 Enable Addressable，新建组 + `MainAssetCollector`（Address Rule `AddressByFileName`、Pack Rule `PackDirectory`、Filter Rule `CollectAll`），路径指向导入的 `Integration` 根目录；③ 打开 `Integration.Boot.unity` 按 Play。要出 Player：把启动器的 Asset Play Mode 改为 Offline/Host 并先构建资源包。
- 仓内 L3：复制到 `Assets/Samples/Integration` → 一次性 Editor 方法创建唯一 collector setting → 跑探针 → 删除导入目录 / setting / `Bundles/`。**这些临时物一律不提交**。

### 4.6 资产制作（不用 MCP）

- **手写并提交**：全部 `.cs`、`.json`、`.inputactions`、`.meta`。
- **一次性 Editor 生成**：预制体、两个场景、`Integration.InputOptions.asset`；生成器源码提交在 `Tools/SampleGeneration/IntegrationAssetGenerator.cs`，由 `Tools/generate-integration-sample.ps1` 复制到临时 `Assets/__XfwIntegrationGen__/`（连同示例脚本与 `.meta`，保持 GUID）后用 Unity batchmode `-executeMethod ...Generate -quit -logFile <绝对路径>` 运行（优先测试壳 `<repo>.TestRun` 避免编辑器锁）。生成物 + Unity 写出的 `.meta` 移回 `Samples~/Integration/`，删除临时目录。
- **音频**：PowerShell `BinaryWriter` 合成 PCM 16-bit mono 44.1kHz `.wav`。
- 生成器注意：面板根挂具体面板脚本、`RectTransform` 显式全屏拉伸、`Canvas`/`GraphicRaycaster` 由 `RequireComponent` 自动补、子节点按 `txt_/btn_/sld_/tgl_` 命名；`DefaultGameLauncher` 私有字段用 `SerializedObject` 写入；`EventSystem` 用 `InputSystemUIInputModule`；Boot 相机持唯一 `AudioListener`。

## 五、分步实施计划与状态

> 每步 = 一个可独立验证的原子提交边界；每个提交前跑该步验证命令，绿后再提交。
> 状态词：`未开始` / `进行中` / `已完成` / `阻塞` / `已否决`。

| 步骤 | 内容 | 产出 | 验证命令 | 提交信息（建议） | 状态 |
|---|---|---|---|---|---|
| **M0** | 前置探针（不提交）：核实 §六 的待核项 | 结论回填 §六 | 读源码 | — | 已完成 |
| **M1** | 第一条可玩纵切：启动 → 主菜单 → 设置 → 语言 → 退出 | 启动器/AppFlow/Settings/语言/主菜单/设置/语言/确认框 + Boot 场景 + 4 个预制体 + 中英表 + 输入资产 + 生成器 | `check-samples.ps1`；`check-docs.ps1 -Enforce`；生成器跑通 | `feat(samples): Integration 启动与设置纵切`（脚本+数据）<br>`feat(samples): Integration 预制体与启动场景`（生成器+资产） | 已完成（运行期验收留 M5） |
| **M2** | 进入游戏：类型选择 + TapRush + 音频 + 结算 + 存档 | Config 行/规则/阶段、Data、会话接口、TapRush、ModeSelect/Game/Result 面板、Game 场景、模式表/规则表、4 个 wav | `check-samples.ps1`；L3 探针（加载 Game 场景、得分、存档） | `feat(samples): Integration 玩法与结算` | 已完成（运行期验收留 M5） |
| **M3** | 第二种玩法 + 高级模块接线 | OddOneOut、暂停/加载面板、GameLoadingPipeline、Cinemachine 相机、诊断页、Lock/Update/Pool/Event 接线 | `check-samples.ps1`；L3 探针 | `feat(samples): Integration 高级模块接线` | 已完成（运行期验收留 M5） |
| **M4** | 常驻结构守卫：prefab/scene YAML 的脚本 GUID、地址、`.meta`、`samples` 路径 | `Tests/Runtime/Samples/IntegrationSampleStructureTests.cs` + meta | `run-tests.ps1 -Fixture IntegrationSampleStructureTests -Platform PlayMode` | `test(samples): Integration 资产结构守卫` | 未开始 |
| **M5** | 导入验收探针 + 脚本 | `IntegrationProbeTests.cs` + meta、`IntegrationVerifySetup.cs`、`Tools/verify-integration-sample.ps1` | fixture（无 env 跳过）+ 脚本跑通 L3 | `test(samples): Integration 导入验收探针` | 未开始 |
| **M6** | 登记与文档：`package.json` samples、包 README 示例段、示例 README、CHANGELOG、决策台账残余结案 | 文档与登记 | `run-tests.ps1 -Fixture DependencyDeclarationTests -Platform EditMode`；`-Filter PackageBoundaryTests -Platform PlayMode`；`check-docs.ps1 -Enforce` | `docs(samples): Integration 示例登记与说明`<br>`docs(ledger): 完整 UI 纵切残余结案` | 未开始 |
| **M7** | 收尾：全量双平台 + 文档门禁 + 行尾检查 + 进度回填 | 门禁结果 | `run-tests.ps1`（全量，0 失败）；`check-docs.ps1 -Enforce`；`git ls-files --eol` | 如有修复再提交 | 未开始 |

## 六、风险与待核（M0 结论）

1. `AddressByFileName` + `EnableAddressable`：`AddressByFileName` 已核实为 `Path.GetFileNameWithoutExtension`（`DefaultAddressRule.cs:14-21`）；仍待 L3 确认 Enable Addressable 未勾选时的退化行为。
2. `EditorSimulate` 需要唯一 `AssetBundleCollectorSetting`；缺失时自动建空表 → 「pack asset info is empty」。
3. 导入路径含版本号：README 不写死绝对路径；L3 用 `Assets/Samples/Integration`。
4. 面板预制体根要求：根组件具体面板类型、`Canvas`/`GraphicRaycaster` 由 `RequireComponent` 自动补、根 `RectTransform` 全屏拉伸、单位变换。
5. `UIViewBase`/`UIPanelBase` 的 `OnUpdate/OnLanguageChanged/OnPause/OnResume` 为 `protected internal virtual`；跨程序集覆写只能写 `protected override`。
6. `DefaultGameLauncher.Start` 不转发进度：用 `AppReadyBootstrapStage` + BootScreen 轮询。
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
