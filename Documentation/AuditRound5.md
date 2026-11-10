# 第五轮全面审计（2026-11-10）

> **点快照**：本文件记录第五轮全面审计在边界提交 `b2bb5a8a` 上的发现、前轮修复复核、裁定建议与分步实施计划。所有 `file:line` 以该提交为准，不承诺长期有效。
> **收件人**：维护者（执行本轮修复时）+ 下一轮审计者。长期结论按收件人分流到模块 README 与决策台账，本文件不复制它们，只做发现与索引（见 §13）。
> **审计轮只出清单，不动代码**。本文件是审计产出；修复实施是后续独立阶段。
> 判据与探针定义见 `Documentation/ModuleAudit.md`；本文件不重复 18 类判据的全文。
> **执行进度**见 §2 的进度表：每完成一个批次就更新该表并在对应节填入发现；收口时在 §2 表上方补总览统计。

## 0. 元信息与边界

- 边界 SHA：`b2bb5a8a`（`b2bb5a8aed038a938de51bb2654111e8bf5a9684`，2026-11-10；审计开始时工作区干净）。
- 上一轮边界：`d29f4f50`（R4，2026-10-25）。
- 区间变更：`d29f4f50..b2bb5a8a` 共 147 个提交、487 个文件、+33,534 / −1,510。核心 Runtime 变动集中在 Audio（provider 缝 + 控制面）与 UI（R4 修复批次 + 编辑期预制体校验器）；Config / Asset / Diagnostics / Save / Input / Camera / Update / Settings / Pool / Pipeline / Localization / File / Serialize / Scene 为 R4 修复批次；Bootstrap / Data / Event / Lock / Log / Message / Reactive / Timer 的 Runtime 零变更。新增两个扩展包：`Assets/XFramework.Fmod/`（76 文件 / +5,358）、`Assets/XFramework.UIAuthoring/`（178 文件 / +13,314）；Luban 扩展包 24 文件变更（R4 批次 3/4 修复）。
- 深度分档：T1 全量 4 个对象（Fmod 包、UIAuthoring 包、核心 Audio、核心 Editor UI 预制体校验器）+ T1 增量复审 1 个（Luban）+ T2 增量 15 个核心模块 + T3 紧凑 8 个核心模块 + 工程面专项 1 个跨切单元。24 个核心模块与 3 个扩展包全部留 18 类判据痕（见 §8）。
- 基线门禁（B0 实测，2026-11-10）：BUILD 两个核心 csproj 0 error（20 / 34 条告警全部来自 PackageCache 的 Input System / Cinemachine / YooAsset，包内仅 1 条 CS0618 不在文档门禁统计面）；FULL PlayMode 2405/2405、EditMode 749/749（743 通过 + 6 skip）、0 失败；`check-docs.ps1 -Enforce` 通过（门禁码 0 条、24 模块 / 535 条台账、README 文件树 24/24、三组示例编译通过）。
- 台账计数（`check-docs.ps1 -Enforce` 口径，2026-11-10）：535 条（模块 24 个 + `Ledger.md` 框架级）。
- 证据等级：`实测` = 本轮实际跑过门禁/探针/读第三方源码可证；`推理` = 调用链/静态状态可证但未运行；`需实测` = 运行期或真机才能确认，已给命令。
- 路径约定：未特别说明时，`Runtime/...` / `Editor/...` / `Tests/...` 相对 `Assets/XFramework/`；扩展包的路径显式以 `Assets/XFramework.Luban/` / `Assets/XFramework.Fmod/` / `Assets/XFramework.UIAuthoring/` 开头；`Documentation/...` 相对仓库根。
- 本轮**未做**（计划）：未修改任何 `Assets/**` 源码、测试、包 README、CHANGELOG；未执行需真机 / IL2CPP / 跨播放会话的手工验收；未修改 `AuditRound2.md` / `AuditRound3.md` / `AuditRound4.md`。

## 1. 范围与方法

### 1.1 分层表

| 层级 | 对象 | 深度 |
|---|---|---|
| T1 全量（首次审计） | `Assets/XFramework.Fmod/` 全包（Runtime 8 + Integration 4 + Tests 16 文件 / 12 fixture / 92 例 + 6 个守卫 fixture + 文档）、`Assets/XFramework.UIAuthoring/` 全包（Runtime 31 + Editor 7 + Tests 25 文件 / 124 例 + 7 个守卫 fixture + 模板/示例 + 文档） | 读全部生产代码 / 测试 / 守卫 / README / CHANGELOG / Samples；18 类逐类留痕；包守卫射程复核；FMOD 2.04 SDK 源码对照 |
| T1 全量（核心新面） | Audio（provider 缝 + 控制面，33 文件 / +2,263）、UI 编辑期预制体校验器（`Editor/UI/` 9 文件） | 读全部新增/变更代码；18 类逐类留痕；Audio 兼查 R4-AUD-001 修复 |
| T1 复审（增量） | Luban（R4 已全量审计；24 文件变更 = R4 批次 3/4 修复） | 逐条复核 R4-LBN-002/003/004/005/006/008/010/013/015/017；读全部变更文件 + 上下文 |
| T2 增量复审 | Config(9)、Asset(5)、UI Runtime(8)、Diagnostics(4)、Save(3)、Input(3)、Camera(3)、Update(2)、Settings(2)、Pool(2)、Pipeline(2)、Localization(2)、File(2)、Serialize(1)、Scene(1) | 读变更文件全文 + 上下文；18 类逐类留痕；R4 修复回归检查 |
| T3 紧凑留痕 | Bootstrap、Data、Event、Lock、Log、Message、Reactive、Timer | 读模块 README/台账关键面；18 类至少一行；新证据才重开 |
| 工程面专项 | `Tools/` 三脚本、核心/三包 Architecture 守卫、测试壳、Samples~ 四组、四个 `package.json`、tag/发布就绪、包 README 四表、CHANGELOG、文档分层登记 | 门禁射程、测试真实性、示例、包元数据、发布就绪 |

### 1.2 方法

- 每个模块按 `ModuleAudit.md` 的 18 类判据逐类留痕：命中写发现 ID，未命中写「扫过」，不适用写「不适用 + 理由」。
- R4 修复复核：71 条（P1 逐条、P2 聚合 + 代码类抽样、P3 聚合）+ R4-ADJ-01..11 + R4-PRB-01..18；每条落到「代码 / 测试 / 文档」三处证据之一，**不以提交信息或 CHANGELOG 声称推定已修**。
- 回归检查：R4 实施批次曾引入 2 条回归与 5 条部分修复；本轮对 R4 修复提交逐模块复核「修复是否引入新问题」。
- 新包审计：README 承诺 × 代码逐条核；包守卫实际射程；测试真实性与零覆盖面；与核心的接缝契约。
- 第三方对照：FMOD 2.04 本地 SDK 源码实读（`E:\UnityPackages\com.fmod.fmod-for-unity.v204`）；Wwise/AudioProvider 设计文档沿用；UIAuthoring 对照设计文档 2026-11-08 的结论；Luban 5.1.0 事实沿用 R4。
- 普查用子代理并行，判定由本人逐条回源码核；进入报告前每条必须有 `file:line`。

### 1.3 普查线索（未定级，待各批次取证）

> 本节只记录普查阶段**看到、尚未定级**的线索，供 B1–B6 逐条取证；取证后升级为 §5/§6 的正式发现，或记为「非缺陷」并说明理由。**不要把本节当结论。**

- **FMOD**：`Documentation/FMOD.md:153` 写「settings 资产可放任意 Resources 目录」，而 `FmodAutoInstall.cs:18,38` 只按根级 `"FmodAudioSettings"` 加载；`Documentation/AudioProvider.md:406` 引用不存在的 fixture 名 `IntegrationAsmdef_IsConditionallyGated`（实际 `IntegrationAsmdefs_BindDefineToConstraints`）；`OwnsBanks` 在 factory lambda 中实时读取 vs「安装开始快照」的表述；手动定义 `XFRAMEWORK_FMOD` 但未装 FMOD 时 Integration asmdef 硬引用 `FMODUnity` 的编译后果；AutoInstall 在 `AudioManager` 已初始化时跳过的时序依赖；真实 bank/event 冒烟未验证；主线程无断言。
- **UIAuthoring**：`package.json:6` 与 README `:24` 的 Kind 列表漏 Popup；`UIActionKind.HideSelf` 三条路径均不可达；`CloseWithResultAsync` 在关闭被控制器拦截时残留 `_pendingResultId`；`UIActionBinding.ButtonNodeName` 文档说「该节点或其根上」而代码只 `GetComponent<Button>()`；缺本地化 key 的告警复用 `TextNodeMissing` 规则 ID；HUD 校验提前 return 跳过动作级诊断；`UIActionExecutor` 构造参数 `definition` 未使用；`UIAuthoringNavigation.OpenAsync` 异地址拒绝时返回已存在面板（非 null）；生产 `UIManagerActionRuntime` 零直接测试；包守卫射程（依赖守卫只查 Runtime asmdef、README 树只查 Runtime/Editor `.cs`、`.meta` 跳过 `Samples~`）；设计文档的 `ParentClosed` 状态码不存在、模板被称为 Prefab Variant 实为普通 Prefab、`### 8.6` 重复。
- **Luban**：R4 修复的回归复核（`LubanIntegrationWriter` 的 staging/backup/rollback、`LubanProcessRunner` UTF-8、`LubanBootstrapStage` 所有权、`LubanPublicApiBaselineCheck` 拆分）。
- **核心 Audio**：`SetProvider` 先 `Destroy` 旧实例再构造；`AudioManagerImpl` 对 `SetPaused`/`SetVolume` 返回 false 的回收语义；`AudioPlayOptions` 构造函数 6→7 参的源兼容与基线行变化；`AudioParameter` 校验与快照复制；取消竞态（validity token）与 `AudioManagedVoice` 代际。
- **核心 UI 校验器**：9 条规则与扫描器；`YooAssetUIPrefabAddressProvider` 避开静态门面；Editor asmdef 新增 `YooAsset` / `YooAsset.Editor` 引用（依赖声明守卫未反查 asmdef，R4 遗留 `未做`）。
- **工程面**：四个包均无发布 tag（核心 0.4.0 / Luban 0.1.0 / Fmod 0.2.0 / UIAuthoring 0.1.0）；根 README `:77` 仍写「阶段 2a 已交付」而 `:86` 已列 2b/3/4；`Documentation/Modules/README.md`「可选扩展包」只登记 Luban/Fmod，未登记 UIAuthoring。

**复查后未升级为发现的线索**（逐条说明理由）：

- UIAuthoring HUD 校验在 `HudActionNotSupported` 后提前 return、不再跑逐动作诊断——HUD 不支持动作是设计边界，一条总错误已足够（R4 同型处理）。
- FMOD `OwnsBanks` 由 factory lambda 在安装开始时读取，而 `FmodInstaller` 的「快照」在紧邻的同步代码里执行——同一主线程调用内无可观察差异，不单列。
- FMOD provider/backend 无主线程断言——`Modules/Audio.md:99` 已记「暂不做」，与核心 Audio 一致，不重开。
- 核心 UI 校验器对 HUD 只查 `001`/`008`——核心 UI README `:351,354` 已写明是已知边界，不单列。
- `UIPrefabValidator` 规则 003 的重复名提示在组件缺失时少于运行时——与运行时缓存语义一致（先修类型再重名），仅文档边界。

## 2. 执行进度

> 状态词：`待做` / `进行中` / `已完成`。每步一个原子提交（`docs(audit): ...`）；提交前跑本步验证。§2 的「总览统计」在 B7 收口时补到本表上方；本表保留为过程记录。

### 2.1 总览

- **34 条新发现：0 P0 / 1 P1 / 3 P2 / 30 P3。** 分布：UIAuthoring 14、FMOD 7、Luban 3（扩展包合计 24）；核心 Audio 2、核心 UI 校验器 2；工程面 / 发布 6。
- **R4 复核**：71/71 有处置；R4-ADJ-01..11 与代码/台账一致；R4-PRB 中 14 条落成回归用例、2 条已执行、2 条仍 `未做`（PRB-03/09），另有 R3-PRB-02/03/04 继续 `未做`。**未发现 R4 修复引入的二次回归。**
- **本轮最需要先读的 10 条**：
  1. **R5-UAU-001（P1）**：控制器拦下 `CloseSelf`/`Replace`/`CloseWithResult` 后会话仍失效；面板仍开着但按钮全空，对话框残留结果可能在后续关闭时误兑现。
  2. **R5-UAU-002（P2）**：`ShowDialogAsync` 把 `OperationCanceledException` 吞成 `OpenFailed` + Error 日志。
  3. **R5-FMD-001（P2）**：`SetGlobalParameter` 在 RuntimeManager 未初始化时静默 `false`（先设 RTPC 再起播的常见顺序）。
  4. **R5-ENG-001（P2）**：扩展包 XML 文档告警实测非零（UIAuthoring 8 CS1591 + Luban 1 CS1574），且无任何门禁。
  5. **R5-LBN-001（P3）**：`LoadTextAssetsAsync` 先释放句柄、后读 `TextAsset.bytes`；依赖 `AutoUnloadBundleWhenUnused=false`。
  6. **R5-UAU-003（P3）**：`Open`/`Push` 校验接受 `UIDeclarativePopupPanel` 作目标，与「只用于 C# 面板」不符。
  7. **R5-UAU-010（P3）**：生产 `UIManagerActionRuntime` 零直接测试，UAU-001/002 正落在未覆盖分支上。
  8. **R5-AUD-001（P3，需实测）**：Audio 的跨会话静态态只靠 `Application.quitting`；关闭域重载 + 该事件不触发时 FMOD 自动安装不会重跑。
  9. **R5-UAU-006（P3）**：UIAuthoring 8 个公开/受保护成员缺 XML 注释（CS1591），公开 API 基线包含它们。
  10. **R5-ENG-005（P3）**：四个包均无发布 tag，`[Unreleased]` 均有未发内容（核心 Audio/UI、Fmod 修复、UIAuthoring 2b/3/4）。

### 2.2 严重度分布（按模块）

| 模块 | P1 | P2 | P3 |
|---|---:|---:|---:|
| UIAuthoring | 1 | 1 | 12 |
| FMOD | 0 | 1 | 6 |
| 核心 Audio | 0 | 0 | 2 |
| 核心 UI 校验器 | 0 | 0 | 2 |
| Luban | 0 | 0 | 3 |
| 工程面 / 发布 | 0 | 1 | 5 |

> 其余 24 个核心模块本轮未发现新 P1/P2（T2 抽样/T3 紧凑；见 §8）。

### 2.3 执行进度表

| # | 批次 | 状态 | 产出节 | 备注 |
|---|---|---|---|---|
| 0 | 生成审计文档骨架与登记 | 已完成 | 全文骨架 | `CLAUDE.md` 文档分层表 + `ModuleAudit.md` 轮次列表已同步 |
| 1 | B0 边界与基线 | 已完成 | §0、附录 A | BUILD 0 error；PLAY 2405/2405、EDIT 743 通过 + 6 skip；DOC 通过（535 条台账） |
| 2 | B1 R4 修复复核 | 已完成 | §3 | 3 P1 逐条读 diff + 用例；51 P2 / 17 P3 聚合 + 抽样；11 ADJ、18 PRB 对台账；未发现二次回归 |
| 3 | B2 FMOD 包全量 | 已完成 | §6、§8、附录 C | 首次审计；1 P2 + 6 P3；FMOD-free XML 告警探针 0 条；Integration 默认不编译，已登记 |
| 4 | B3 UIAuthoring 包全量 | 已完成 | §6、§8、附录 C | 首次审计；1 P1 + 1 P2 + 12 P3；XML 文档探针实测 8 条 CS1591；控制器拦截路径零覆盖 |
| 5 | B4 核心新面（Audio / UI 校验器） | 已完成 | §6、§8、附录 C | provider 缝逐行复核未发现新 P1/P2；2 条 Audio P3 + 2 条 UI 校验器 P3 |
| 6 | B5 核心增量（T2 / T3 / Luban 复审） | 已完成 | §6、§8、附录 C | T2 15 模块 + T3 8 模块；Luban R4 批次 3/4 修复复核；3 条 Luban P3 |
| 7 | B6 工程面与发布 | 已完成 | §6、§7 | 门禁/守卫/四包元数据/发布就绪；1 P2 + 5 P3 |
| 8 | B7 报告收口 | 已完成 | §2 总览、§4–§13、附录 B/C | 总览 34 条（1 P1 / 3 P2 / 30 P3）；ADJ/实施计划/探针轨/台账索引落盘 |
| 9 | B8 收尾验证 | 已完成 | 附录 A | FULL 2405/2405 + 743 通过（6 skip）、DOC 通过；ID 34 = 附录 C 34 行；`git status` 只含本轮文档 |

## 3. 前轮修复复核（R4）

> 复核口径：每条落到「代码 / 测试 / 文档」三处证据之一；提交信息只是索引，不作为修复证明。本轮对 P1 逐条读 diff 与用例；P2/P3 按模块聚合复核 + 代码类抽样（抽样名单见各模块条目）；ADJ 逐条对台账与代码；PRB 对台账与配方。

### 3.1 R4 P1（3 条）

| R4 ID | 修复提交 | 本轮证据 | 结论 |
|---|---|---|---|
| R4-UIA-001 | `616d64d8` | `UIViewBase` 新增 `IsRecycled` / `RecycleGeneration` / `MarkRecycled` / `MarkReopened`（`UIViewBase.cs:48-70`）；`UIManagerImpl.OpenCoreAsync` 三处「世代变化即放弃」守卫（`UIManagerImpl.cs:637-760`），`SettleOpening` 改按 entry 身份摘账（`:1928-1937`）；回归 `UIPanelConcurrencyTests.Dispose_DuringInFlightOpen_DoesNotCallOnCloseAfterRecycle` / `..._ReinitializedPanelStaysOpen`（改前双红） | **已修复** |
| R4-UIB-001 | `28116446` | `PruneOverlayHolds` 改为 `!ReferenceEquals(owner, null) && owner == null`（`UIManagerImpl.cs:2284-2299`）；回归 `UIMaskTests.ShowMask_WithoutOwner_SurvivesUnrelatedPanelDestroyed` | **已修复** |
| R4-UIB-002 | `5559dc2b` | `UIHudManager` 新增按 target 实例 ID 的单槽在途表 `_inFlightAttach`、`AttachCoreAsync` 注册前复检、`SettleAttach` 身份摘账、`MarkReopened` 复位认领标记（`UIHudManager.cs:75-84,150-300`）；回归 `UIHudManagerTests.ShowHudAsync_SameTargetConcurrent_InstantiatesOnce` / `CloseAllAsync_AfterConcurrentAttach_LeavesNoActiveHud`（改前 2 次实例化 / 残留 1 个） | **已修复** |

### 3.2 R4 P2（51 条）聚合复核

> 按模块列出处置证据与抽样复核结果。全部 51 条均落到代码/测试/文档之一，**未发现完全未处置项**。P3 的 17 条见 §3.3。

- **UI（9 条）**：UIA-002（`OnPoolRecycle` try/catch + `UIPanelPoolLifecycleTests.CloseAsync_OnPoolRecycleThrows_StillPublishesClosedAndCallsAfterClose` / `UIHudManagerTests.ShowHudAsync_OnOpenAndPoolRecycleThrow_PreservesOnOpenException`）、UIA-003/UIA-004/UIB-006/UIB-007（README 四处：`SetInstance` 重接、`MaxIndexInLayer = 30`、`BindToLocalizedText` 释放口径、§11 围栏）、UIA-005（`ApplyLayerInteractivityToContainer` + `UILayerApiTests.SetLayerInteractive_BeforeContainerExists_AppliesToNewContainer`）、UIB-003（控制器全关回调门控 + `UIPanelSelfCloseTests.CloseAllAsync_NoPanels_*` / `..._ControllerBlocksClose_*`）、UIB-004（换 provider 异常隔离 + `UIAsyncTokenTests` 三例）、UIB-005（`img_` 类型不匹配 `#if UNITY_EDITOR` 告警 + `UIPanelBindingConventionTests.ImgNodeWithUnsupportedSourceType_Warns`）。**抽样读 diff：UIA-001/002/005、UIB-002/003/004/005，均与报告承诺一致。**
- **Config（4 条）**：CONF-001（null 校验前移到在途分支之前 + `ConfigManagerImplTests.PreloadTableAsync_NullLoaderWhileTableInFlight_ThrowsConfigException` / `...Global...`）、CONF-002（`ConfigAssetLease` + `ConfigAssetLoaderTests.LoadTextAsync_TypeMismatch_ReleasesLeaseOnceAndExplains` / `LoadTableAsync_...`）、CONF-003（`ScriptableObjectAssetRoundTripTests` 真实 `CreateAsset/SaveAssets/LoadAssetAtPath`）、CONF-005（在途登记按 `(Type, Kind)` 分槽 + `PreloadTableAsync_ThirdCallerJoinsAfterCrossIdentityInFlight_LoadsOnce`）。
- **Asset（3 条）**：AST-001（`SetInstance` 处置旧实例/退订 + `AssetManagerFacadeTests.SetInstance_AfterFrameworkInitialize_DisposesPreviousInstance` 等三例）、AST-002（`AssetBootstrapStage` 按实例身份清理 + `BootstrapStageTests`）、AST-003（`YooAssetPackageStateTests` 两例 + 三入口守卫）。
- **Audio（1 条）**：AUD-001（坏源重建/丢弃 + `AudioPoolTests.ExternallyDestroyedHost_NextPlayRebuildsSlotAndSucceeds` / `...Source_NextPlayRebuildsSource`）。
- **Camera（5 条）**：CAM-001（README 写明同一目标中途 cut 的例外 + `CinemachineCameraEventTests.SameTargetCut_DuringBlend_PublishesDeactivatedAndCompletedWithoutActivated`）、CAM-002（`Activate_WhileExternalOverrideMakesTargetActive_TakesBoostOwnership`）、CAM-003/004（`Subscribe` 令牌说法与重载示例文档）、CAM-005（`DestroyedRegisteredActiveCamera_DeactivatedCarriesItsId`）。
- **Diagnostics（2 条）**：DIAG-001（`Register_OrderGetterThrows_StillRegistersAtEnd` / `..._ExistingPanelOrderThrows_...`）、DIAG-002（`Table_EmptyTrailingColumn_HeaderAndSeparatorAgree`）。
- **Save（2 条）**：SAVE-001（`Modules/Save.md:41` 去掉会漂移的计数）、SAVE-002（`SaveManagerImplTests.DeleteSlotAsync_BackupOnly_DeletesAndDoesNotRecover` / `SlotExistsAsync_BackupOnly_ReturnsTrue`）。
- **Input（1 条）**：INP-001（`InputSystemProviderTests.GetButtonPressDuration_IsPerPlayer` + 预热用例）。
- **Settings（1 条）**：SET-001（`EncryptedSettingsStoreTests.LoadAsync_SyncInner_DecryptsOnPoolAndParsesOnMainThread`）。
- **Scene（1 条）**：SCN-001（`SceneLoadTests.LoadSceneAsync_ActivateWithInvalidScene_WarnsWithLocationAndReturnsHandle`）。
- **Update（1 条）**：UPD-001（`Runtime/Update/README.md:327` 与 `UpdateManager.cs:371` 已改为「`Initialize` / `SetInstance` 都会重接」）。
- **Luban（14 条）**：LBN-001/007/011/012/016（包 README 收窄措辞/补类型/零拷贝说明）、LBN-002（`LubanAssetProviderTests` 成功映射九例）、LBN-003/015（`LubanProcessRunnerTests` 成功/非零/超时/大输出/中文 UTF-8）、LBN-004（`LubanIntegrationGeneratorTests.Generate_WriteTrue_EmitsAllFilesAndManifest`）、LBN-005（`LubanBootstrapStageTests` 加入在途所有权）、LBN-006（`LubanConfigTests` provider 级错误带表名/location）、LBN-008（`LubanIntegrationWriterTests` 目录拒绝 + manifest 回滚，writer 改为 staging→backup→commit→rollback）、LBN-009（`LubanSettingsValidator`/`LubanProjectValidator` 平台路径策略，Linux 探针仍 `未做`）、LBN-010（`LubanConfigTests.InitializeAsync_JoiningDifferentAdapter_Warns`）、LBN-013（`LubanEditorPublicApiBaseline.txt` + fixture）、LBN-014（`Documentation/Luban.md` 计数口径）。
- **工程面（7 条）**：ENG-001（基线注释不再写固定数）、ENG-002（`Workflow.md:108-111` 四个更新开关齐备）、ENG-003（`ModuleReadme.md` 文件树门禁描述已改双向）、ENG-004（check-docs 不覆盖扩展包——R5-ENG-001 重新提交）、ENG-005（`run-tests.ps1 -TimeoutSeconds`）、ENG-006（核心/Luban Editor 公开 API 基线已建）、ENG-009（`CHANGELOG.md:408` 行为变更表收录 `MaxIndexInLayer` 31→30）。

### 3.3 R4 P3（17 条）聚合复核

17 条均已处置：AST-004（日志/README 改「回池等待复用」）、AST-005（下载最终进度走隔离）、AST-006（`SessionResetTests` 在途 impl 退订）、AST-007（README `Destroy()` 文案）、CONF-004（`ConfigTable` 键==Id XML）、CONF-006（在途告警比较 loader 实例 + 用例）、FILE-001（`IFileProvider.cs:19` null/空=域根）、LBN-015（ProcessRunner UTF-8 + 中文用例）、LBN-016（零拷贝字典说明）、LBN-017（未使用 using 删除）、LOC-001（`GetFormat` 花括号 + 用例）、PIPE-001（容器终态写入 + 用例）、POOL-001（Return 满池隔离 + 用例）、SER-001（`ResetForNewSession` 文案）、UIA-006（`PopTo`/`PopToRoot` 与 Dispose provider 用例）；ENG-007 维持「示例只守编译」（`Workflow.md` 已写明）、ENG-008 由 R4-ADJ-01 记为 `暂不做`（`Modules/README.md:83`）。

### 3.4 R4-ADJ-01..11 落盘复核

| ADJ | R4 建议 | 当前台账/代码 | 结论 |
|---|---|---|---|
| 01 | `暂不做`（Luban 台账留在 `Luban.md`） | `Modules/README.md:83`、`Ledger.md:140` | 一致 |
| 02 | `已做`（Editor 公开 API 基线） | `Ledger.md:141`；`EditorPublicApiBaseline.txt` + `LubanEditorPublicApiBaseline.txt` 均存在 | 一致 |
| 03 | `暂不做`（check-docs 不覆盖 Luban XML/源文件） | `Ledger.md:63`；`check-docs.ps1` 路径仍硬绑 `Assets/XFramework`，第六项覆盖三个 `Samples~` | 一致 |
| 04 | `未做`（0.4.0 / Luban 0.1.0 tag） | `git tag` 仍只有 `0.3.0` | 一致（用户动作） |
| 05 | `已做`（run-tests 超时） | `Tools/run-tests.ps1:48,85,324` 有 `-TimeoutSeconds`；`Workflow.md:104` | 一致 |
| 06 | `已做`（Luban 写盘暂存/回滚） | `LubanIntegrationWriter.cs:114-200` staging/backup/rollback；`Ledger.md:144` | 一致 |
| 07 | `已做`（UISorting 行为变更表） | `CHANGELOG.md:408` | 一致 |
| 08 | `已做`（Luban 措辞） | `a05decb8`；包 README 已收窄 | 一致 |
| 09 | `已做`（主线程断言射程） | `Assets/XFramework.Luban/README.md:182-188` 写「写入口断言、查询面不设」 | 一致 |
| 10 | `已做`（ConfigTable 键==Id） | `ConfigTable.cs:14-16` XML | 一致 |
| 11 | `未做`（Save 双路径线程改造） | `Modules/Save.md:41` | 一致 |

### 3.5 R4 探针轨（PRB-01..18）

- **已落成回归用例**（随批次 0/1 修复）：PRB-01（三条 UI P1 红证）、PRB-02（CONF-001 null loader）、PRB-04（AST-001 换出旧 impl 退订）、PRB-05（AST-003 初始化失败包）、PRB-07（LBN-002 默认资产源）、PRB-08（LBN-003 ProcessRunner）、PRB-10/11（CAM-001/002）、PRB-13（SAVE-002）、PRB-14（AUD-001）、PRB-15（INP-001）、PRB-16（SET-001）、PRB-17（PIPE-001）。
- **已执行并结案**：PRB-12（0.3.0→0.4.0 迁移表编译探针，`9e9c622c`）、PRB-18（Serialize 更早注册被清，`bc0c4abd`）。
- **仍 `未做`**：PRB-03（真 YooAsset「有效句柄 + 类型不符」的 RefCount，`Modules/Config.md:101`）、PRB-09（Linux Editor 大小写 / cwd，`Documentation/Luban.md:526`）；R3-PRB-02/03/04（真包低内存 / IL2CPP / macOS Server）继续 `未做`（`Modules/Config.md:98-99`、`Modules/File.md:48`）。
- **状态与 R4 §11 一致**，无「台账写已做但代码无证据」的项。

### 3.6 R4 修复引入的回归复核（R5 视角）

- R4 §7.8 点名的 2 条回归（R4-CONF-001、R4-UIA-003/UPD-001 文档反向）与 5 条部分修复（UIB-005/006、AST-005、LBN-005、SAVE-002、AUD-001）**均已在批次 0–3 关闭**，本轮抽样读 diff 未发现二次回归。
- 抽样读的修复 diff：`616d64d8`（UIA-001）、`28116446`（UIB-001）、`5559dc2b`（UIB-002）、`4f4165b4`（CONF-001）、`c95b784d`（SET-001）、`19870f43`（LBN-005）、`140f2b1f`/`6645fbad`/`3b7e2e7a`/`2719c4f2`（AST-001/002/003/006）、`0fb50617`（LOC-001）。**未发现修复引入的新缺陷**；B2–B5 的模块审计将继续覆盖未抽样的修复面。

## 4. R5 P0 清单

**本轮未发现 P0。** R4 的 71 条无 P0 回归；新面（Audio provider 缝、UI 预制体校验器、FMOD Phase 2、UIAuthoring 阶段 2a/2b/3/4）未发现「默认路径必然损坏数据或整体不可用」的缺陷。

## 5. R5 P1 清单

### R5-UAU-001 · P1 · B1/B2/B6/D4 · 控制器拦下关闭后会话仍失效；对话框残留待兑现结果

- **位置**：`Assets/XFramework.UIAuthoring/Runtime/UIActionExecutor.cs:72-85`（`CloseSelf` / `Replace` 在 `await` 后无条件 `_session.Invalidate()`）、`Runtime/UIDeclarativeDialogPanel.cs:44-49,83-94`（`_pendingResultId` 先写、关闭失败不清）、核心 `Assets/XFramework/Runtime/UI/UIManagerImpl.cs:2058-2064`（`OnBeforeCloseAsync` 返回 false 时直接 `return false`）与 `UIManager.cs:393-399`（`CloseAsync` 返回 `UniTask`，不向调用方暴露是否关闭）。
- **发现**：`IUIController` 的关闭拦截是框架公开能力；`UIManager.CloseAsync` 在控制器拦下时不抛、不返回结果，UIAuthoring 的执行器无法区分「已关闭」与「被拦下」。`CloseSelf` / `Replace` 无论结果都 `_session.Invalidate()`：面板实际仍打开，但 `UIActionExecutor.ExecuteAsync` 在 `!_session.IsValid` 时直接返回，面板上所有按钮动作变成空操作。`CloseWithResultAsync` 还先把 `_pendingResultId` 写入并在 `OnClose` 兑现；关闭被拦后该字段不清，面板仍开着，之后被 `CloseAllAsync` / 父面板关闭 / Dispose 回池时会把过期结果当作本次选择兑现。Dispose 回池路径不经过 `OnClose`（R4-UIA-001 修复后），池化实例下一次复用时仍带上一代的 `_pendingResultId`。
- **失败场景**：对话框用 `CloseWithResult("confirm")` 确认；控制器因「未填必填项」拦下关闭；玩家取消后父面板关闭/`CloseAllAsync` → 核心结果通道收到 `"confirm"`；或声明式面板的控制器拦下 `CloseSelf` → 面板留在屏幕上但所有按钮不再响应，只能靠外部关闭。
- **建议**：让 `IUIActionRuntime.CloseAsync` / `ReplaceDeclarativeAsync` 返回是否真的关闭（或执行器在 `await` 后检查 `_session.Panel.IsOpen`），**仅真正关闭时**失效会话；`_pendingResultId` 在 `OnOpen` 与关闭失败分支清空；控制器拦截是正常业务分支，不应把面板变成不可操作。
- **验证**：PlayMode 用 `IUIController.OnBeforeCloseAsync` 返回 false 的 fake controller，分别试点 `CloseSelf` / `CloseWithResult` / `Replace`：断言会话仍有效、按钮动作仍可执行、后续真正关闭时结果不误兑现；改前必红。另补一条「Dispose 回池后重开同一对话框，结果通道不携带上一代 pending」。
- **落点**：`Runtime/UIActionExecutor.cs`、`Runtime/UIDeclarativeDialogPanel.cs`、`Runtime/IUIActionRuntime.cs`、`Runtime/UIManagerActionRuntime.cs`、测试、CHANGELOG 行为变更；实施批次 0。

## 6. R5 P2 清单（按模块）

> 一条一行；`[回归]` 表示由前轮修复引入，`[跨模块]` 表示归拥有方立条，`[需实测]` 表示需运行期/真机证据。P3 也列在各模块下，便于下一轮按模块取用。

### Audio（T1 核心新面：provider 缝 + 控制面）

- **R5-AUD-001 · P3 · B6/D4 [需实测]**：`Assets/XFramework/Runtime/Audio/AudioManager.cs:692-700`、`Assets/XFramework.Fmod/Runtime/FmodInstaller.cs:42-47` —— `AudioManager` 的跨会话静态态（`_instance` / `_instanceInitialized` / provider / 通道 / 暂停）**不在 `ResetForNewSession` 族**（`Tests/Runtime/Architecture/SessionResetTests.cs:161-171` 的清单里没有 Audio），只靠 `AutoInit` 订阅 `Application.quitting` 来 `Destroy`。关闭域重载 + 若该事件不触发，第二会话会复用上一会话的 manager；内置 `AudioSourceProvider` 能靠坏源重建自愈，但 FMOD 自动安装会因 `FmodInstaller` 的 `AudioManager.IsInitialized` 早退（`:42-47`）**不再重装 provider / 加载 bank**。现有手工「关闭域重载连跑两遍」验收（2026-10-14/10-20）包含 Audio 用例但不能区分 `quitting` 是否真的触发。建议：把 Audio 纳入会话复位族（或明确写入台账「依赖 quitting」），并用关闭域重载 + FMOD 的手工探针确认第二会话 bank/事件可用。
- **R5-AUD-002 · P3 · A1/D4**：`Assets/XFramework/Runtime/Audio/README.md:350-351` —— 「生命周期与清理」的探测型豁免总表只写「未初始化 / 已销毁时安全返回 false」，没有写**加载窗口内**（`Loading` 状态）同样返回 `false`；同一 README 的 API 参考 `:123` 与 `SetPosition` 的 XML（`AudioManager.cs:520`）各自写了这一点，但只读总表的人会误以为拿到句柄后控制面立即可用。建议在总表下补一句「加载窗口内一律 false；起播前请用 `InitialParameters` / `AudioPlayOptions.Position`」。

### UI 编辑期预制体校验器（T1 核心新面）

- **R5-UIE-001 · P3 · A1/D4**：`Assets/XFramework/Editor/UI/UIPrefabValidator.cs:110-136` —— 面板根节点**完全没有** `UIPanelBinding` 时，`rootBindings.Length == 0` 分支只在「子节点存在 binding」时报告 `UI-PREFAB-002`；若整个预制体没有 binding，则约定节点的前缀/深度/类型/重名检查（规则 003–005）全部跳过，也不报告「缺少 binding」。核心 UI README `:350` 把「`UIPanelBinding` 与脚本同根」写成预制体要求，校验器没有对应的「缺失」诊断。建议补一条 `BindingMissing`（Warning）或把规则 002 的文案/射程写清；补一条 EditMode 用例。
- **R5-UIE-002 · P3 · D3**：`Assets/XFramework/Editor/UI/UIPrefabValidatorWindow.cs`（293 行）与 `UIPrefabValidationReport` 的窗口渲染/排序/汇总没有自动化用例；`UIPrefabValidatorTests`（29）与 `UIPrefabScannerTests`（7）覆盖了规则与扫描主体。IMGUI 窗口是编辑器手工验收项，建议在 README 的「已知限制」保留「窗口未自动化」。

> 核心 Audio 的 provider 缝与内置 `AudioSourceProvider` 本轮逐行复核（`PlayAsync` 预占 → validity 注册 → 加载 → 取消/作废代际 → 起播 → 租约交接；`Stop`/`Release`/`SetPaused`/`SetVolume`/`Poll`；池的 `Invalidate`/`RebuildVoice`；核心 `PlayCoreAsync` 的 OCE 与 false 分支、`SweepVoices`、`Pause`/`Resume`、`RefreshVolumes`）。**未发现新的 P1/P2**；R4-AUD-001 的坏源重建与 R4 后的 provider 缝修复（P1-1/P1-2/P1-3、C1–C6）均有代码 + 用例证据。

### 工程面（门禁 / 守卫 / 包元数据 / 发布）

- **R5-ENG-001 · P2 · A3/D3**：`Tools/check-docs.ps1`（编译范围只列 `Assets/XFramework/Runtime` 与 `Assets/XFramework/Editor`）——本轮用 `dotnet build <包 csproj> -p:DocumentationFile=...` 对三个扩展包实测 XML 文档告警：**UIAuthoring 8 条 CS1591**（7 个公开/受保护 override + `UIPanelDefinitionEditor.OnInspectorGUI`）、**Luban 1 条 CS1574**（`AssetLubanDataProvider` 的死 cref，见 R5-LBN-002）、Fmod-free 0 条。这些告警不进任何门禁（包内守卫只查结构/API 形状，不编译文档），R4-ADJ-03 的「暂不做」由此有了第一份实测证据。建议至少给三个包各加一条 DocumentationFile 编译探针（或把 `check-docs.ps1` 的扫描根/项目列表泛化），否则包内 XML 文档只能靠评审。
- **R5-ENG-002 · P3 · D1**：`Assets/XFramework/Editor/Venusy609.Xframework.Editor.asmdef:2-8` —— 新增的 UI 预制体校验器让核心 Editor 程序集引用 `YooAsset` 与 `YooAsset.Editor`；`Tests/Editor/Architecture/DependencyDeclarationTests` 仍只校验「README 依赖表 / 安装器清单 / manifest 三处已列项逐字一致」，**不反查 asmdef 引用**（R4 已登记「未做」）。当前 YooAsset 已在三处声明，故不构成缺陷；但这是该缺口的第二个实例（第一个是 Input System / Cinemachine 漏列）。建议在反查机制落地前，至少把「Editor asmdef 的引用集合」写进依赖声明测试的射程说明或台账。
- **R5-ENG-003 · P3 · A1**：`README.md:77` —— UIAuthoring 小节开头仍写「阶段 2a 已交付」，而同节 `:86` 已列 2b/3/4；同一段落自相矛盾。建议改为「阶段 2a/2b/3/4 已交付」或把首句改成不绑阶段的描述。
- **R5-ENG-004 · P3 · D4**：`Documentation/Modules/README.md:81-84` —— 「可选扩展包」只有 Luban 与 Fmod 的「不建 `Modules/<包>.md`」条目；UIAuthoring 的维护向记录同样在 `Documentation/UIAuthoring.md`（`Ledger.md` §二与 `CLAUDE.md` 文档分层已登记），但本节没有对应指针。建议补一条同形条目，避免维护者在本文件里找不到 UIAuthoring 的台账位置。
- **R5-ENG-005 · P3 · 发布**：四个 `package.json` 版本分别为核心 `0.4.0`、Luban `0.1.0`、Fmod `0.2.0`、UIAuthoring `0.1.0`；`git tag` 仍只有 `0.3.0`，四个包的 `[Unreleased]` 均有未发布内容（核心 Audio provider 缝 + UI 校验器；Fmod 的 bank/backend 修复；UIAuthoring 的 2b/3/4）。R4-ADJ-04 的「0.4.0 / Luban 0.1.0 tag」仍未做，本轮新增 Fmod 0.2.0 与 UIAuthoring 0.1.0 两个 tag 缺口。建议发布流程按「核心 0.4.0 + 三个扩展包同 tag」一次核对（tag 是用户动作）。
- **R5-ENG-006 · P3 · D3**：`Assets/XFramework.UIAuthoring/Tests/Runtime/Architecture/UIAuthoringDependencyDeclarationTests.cs:33-50` 与 `Assets/XFramework.Fmod/Tests/Runtime/Architecture/FmodDependencyDeclarationTests.cs` —— 两个包的依赖守卫只查 Runtime asmdef；UIAuthoring 的 **Editor asmdef** 已引用 `YooAsset` / `YooAsset.Editor` / TMP，Fmod 的 Integration asmdef 引用 `FMODUnity`（有专门守卫）。Editor 侧新增依赖目前没有包内守卫，也没有 README 依赖表变更；当前 YooAsset 是核心既有依赖，故不构成缺陷。建议把 Editor asmdef 纳入包依赖守卫的射程说明。

### Luban（扩展包，R4 复审）

- **R5-LBN-001 · P3 · A1/B2**：`Assets/XFramework.Luban/Runtime/AssetManagerLubanAssetSource.cs:24-48` —— `LoadTextAssetsAsync` 先把句柄映射成 `LubanTextSlice`（保存 `TextAsset` 引用），在 `finally` 里 `Dispose` 全部句柄，**之后**才调用 `MapTextAssets` 读 `slice.Asset.bytes`。类注释写的是「把 TextAsset / RawFile 句柄读成字节后立即释放」，实现顺序相反；当前依赖 YooAsset `AutoUnloadBundleWhenUnused=false`（默认）让资源在 Release 后仍存活。若消费方把该选项置 true 或在此期间触发未使用资源卸载，映射可能读到已卸载的 `TextAsset`（`bytes` 空/异常）。建议在 `try` 内把 `bytes` 复制进切片（`LubanTextSlice` 存 `byte[]`），与 `LoadRawFileAsync` 的 `GetRawFileData()` 在 `using` 内读取同形。
- **R5-LBN-002 · P3 · A3**：`Assets/XFramework.Luban/Runtime/AssetLubanDataProvider.cs:9` —— `<see cref="AssetManager"/>` 缺 `using XFramework.XAsset;`（或全名），实测 `dotnet build ... -p:DocumentationFile` 报 **CS1574**；本包不在核心文档门禁射程（R4-ADJ-03），该死链不会被任何门禁看见。建议补 using/全名，并见 R5-ENG-001。
- **R5-LBN-003 · P3 · D3/A1**：`Assets/XFramework.Luban/README.md:22,34` 的安装 URL 仍是 `#<tag>` 占位，`package.json` 为 `0.1.0` 且无 tag；R4-ADJ-04 仍 `未做`（用户动作）。建议在发布流程中替换，或在 README 明示「当前只能按 `main` 安装」。

### UIAuthoring（扩展包，首次审计）

- **R5-UAU-001 · P1 · B1/B2/B6/D4**：`Assets/XFramework.UIAuthoring/Runtime/UIActionExecutor.cs:72-85` 与 `UIDeclarativeDialogPanel.cs:44-49,83-94` —— 控制器拦下关闭时（核心 `ClosePanelInternalAsync` 返回 `false`，`UIManagerImpl.cs:2058-2064`；`UIManager.CloseAsync` 返回 `UniTask` 不暴露结果，`UIManager.cs:393-399`），`CloseSelf` / `Replace` 的执行器仍无条件 `_session.Invalidate()`，面板实际没关但动作会话失效——面板继续显示，其按钮全部变成空操作。`CloseWithResultAsync` 还会先把 `_pendingResultId` 写入、关闭被拦后不清：面板仍在，之后被 `CloseAllAsync` / 父面板 / Dispose 回池关闭时，`OnClose` 会把过期结果兑现给核心结果通道；Dispose 回池不复位该字段，池化实例下一次复用时仍带着上一代的待兑现结果。现有用例只覆盖成功路径（`UIActionExecutorTests.Replace_UsesRuntimeReplace` 断言 `session.IsValid == false`），无「控制器拦下」用例。建议：`IUIActionRuntime.CloseAsync` / `ReplaceDeclarativeAsync` 返回是否真的关闭（或执行器在 await 后查 `Panel.IsOpen`），仅在真正关闭时失效会话；`_pendingResultId` 在 `OnOpen` 与关闭失败分支清空。验证：PlayMode 用拦下关闭的 Fake controller 试点 `CloseSelf` / `CloseWithResult` / `Replace`，改前必红。
- **R5-UAU-002 · P2 · B3/D4**：`Assets/XFramework.UIAuthoring/Runtime/UIManagerActionRuntime.cs:111-126` —— `ShowDeclarativeDialogAsync` 的 `catch (Exception ex)` 把 `OperationCanceledException` 一并吞掉：`UIAuthoringNavigation.ShowDialogAsync(..., cancellationToken)` 的取消会变成 `OpenFailed` 返回、发布一条结果消息并记一条 Error 日志。同一包的 `OpenAsync` 会正常把 OCE 抛给调用方（`UIAuthoringNavigation.cs:43-55`），两条公开入口的取消语义不一致；公开 API 的 XML 也未写「取消会返回 OpenFailed」。建议单独 `catch (OperationCanceledException)` 并重抛（或明确文档化），补一条取消用例。
- **R5-UAU-003 · P3 · A1/A2**：`Assets/XFramework.UIAuthoring/Editor/UIAuthoringValidator.cs:483-497` —— `Open` / `Push` 的目标根检查只拒绝 `UIDeclarativePanel` 与 `UIDialogPanel<>`，**没有拒绝 `UIDeclarativePopupPanel`**（它是 `UIPanelBase`，谓词返回 true）；README 的行为契约写「`Open` / `Push`：只用于 C# 面板」。运行期 `UIManager.OpenAsync<UIPanelBase>` 也会照常打开一个弹窗预制体（按普通面板层），绕过弹窗的锚定/层语义。建议把 `target.GetComponent<UIDeclarativePopupPanel>() != null` 加入拒绝条件，并补一条校验用例。
- **R5-UAU-004 · P3 · A2/D4**：`Assets/XFramework.UIAuthoring/Runtime/UIActionKind.cs:51-52`、`Editor/UIAuthoringValidator.cs:375-380`、`Runtime/UIDeclarativeHudItem.cs:52-57`、`Runtime/UIActionExecutor.cs:158-160` —— `HideSelf` 三条路径都不可达：校验器对所有 Kind 直接报错「只适用于 HUD」，HUD 从不绑定动作，执行器只打一条「只对 HUD 有效」的 Warning。它作为公开枚举值存在但没有任何可用场景。建议明确「保留为未来 HUD 动作、当前不可用」写进 XML/README，或在预 1.0 的版本里删除（公开 API 变更需单独确认）。
- **R5-UAU-005 · P3 · A1**：`Assets/XFramework.UIAuthoring/Runtime/UIActionBinding.cs:12` 写「按钮节点名（该节点**或其根上**必须有 `Button`）」，而 `UIDeclarativeBinder.BindAction`（`:200-215`）与校验器（`:344-353`）都只查节点自身 `GetComponent<Button>()`；把 `Button` 放在父节点会静默不绑定。建议把文档改为「该节点上必须有 Button」。
- **R5-UAU-006 · P3 · A3**：包内 8 个公开/受保护成员缺 XML 注释（实测 `dotnet build ... -p:DocumentationFile`）：`UIDeclarativePanel.OnOpen/OnClose`、`UIDeclarativeDialogPanel.OnOpen/OnClose`、`UIDeclarativePopupPanel.OnOpen/OnClose`、`UIDeclarativeHudItem.OnOpenImpl`、`UIPanelDefinitionEditor.OnInspectorGUI`，全部 CS1591。它们都在公开 API 基线内（基线把 `protected` 视为可见），但核心 `check-docs.ps1 -Enforce` 只扫 `Assets/XFramework`，这 8 条在任何门禁里都不会出现。建议补 `<inheritdoc/>` 或 `<summary>`，并把该探针纳入工程面门禁讨论（见 R5-ENG-001）。
- **R5-UAU-007 · P3 · A1/D4**：`Assets/XFramework.UIAuthoring/Editor/UIAuthoringValidator.cs:172-176` —— 「启用本地化但没有 LocalizationKey」复用 `UI-AUTH-010 TextNodeMissing` 规则号；规则名与实际条件不符，IDE/报告里无法区分「节点不存在」与「缺 key」。建议新增独立规则号（如 `UI-AUTH-013`），或把该分支并入 010 的明确文案。
- **R5-UAU-008 · P3 · A1**：`Assets/XFramework.UIAuthoring/package.json:6` 与 `README.md:24` 的形态列表只写 `Panel` / `Dialog` / `Hud`，漏了 `Popup`（`UIPanelDefinitionKind.Popup=3`，README 第 8 节已完整描述阶段 4）。建议同步包描述与 Kind 列表。
- **R5-UAU-009 · P3 · A1**：`Documentation/UIAuthoring.md` 的维护向记录与实现漂移：多处仍列 `ParentClosed` 状态（`:494,528,674,725,930,1059,1062`），而 `UIAuthoringDialogStatus` 只有 `Selected/Canceled/OpenFailed`，包 README `:247` 已写「ParentClosed 归入 Canceled」；`:430-437` 的 HUD 设计草图仍写「绑定文本/动作/数据」与 `HideSelf` 调 `UIManager.HideHud`，实现只绑定文本与数据；`### 8.6` 标题重复（`:804` 与 `:808`）；模板被称为 `Prefab Variant`（`:244,342,344,416,836,847`）而实际是普通 Prefab（无 `m_PrefabInstance`）。建议按实现订正或标注「设计稿，未照此实现」。
- **R5-UAU-010 · P3 · D3**：`Assets/XFramework.UIAuthoring/Runtime/UIManagerActionRuntime.cs` 的生产实现没有直接测试；`UIAuthoringNavigationTests` / `UIActionExecutorTests` 全部用 `FakeActionRuntime`。`ShowDeclarativeDialogAsync` 的「是否真正打开」判定（订阅 `PanelOpenedMessage` + `GetPanel<...>()?.AssetPath`）、单飞拒绝、异常与取消路径、`ReplaceDeclarativeAsync` 的拦截分支均只经 fake 间接覆盖；UAU-001/002 正落在这些未覆盖分支上。建议至少补「生产 runtime + 假 UIManager」或把状态判定抽成可测的纯函数。
- **R5-UAU-011 · P3 · C1**：`Assets/XFramework.UIAuthoring/Runtime/UIActionExecutor.cs:18-22` —— 构造函数接收 `UIPanelDefinition definition` 但从未使用；三个面板 OnOpen 都传了它。建议删除参数（内部类型，无公开影响）。
- **R5-UAU-012 · P3 · A1/F**：`Assets/XFramework.UIAuthoring/Runtime/UIAuthoringNavigation.cs:49-53`（面板）与 `:139-143`（弹窗）—— 异地址拒绝时返回**已存在的面板/弹窗**（非 null）；README `:43` 只写「异地址会被拒绝并 Warning」，`快速使用` 的 `if (panel == null)` 注释只提「资源加载失败/被控制器拦下」。调用方拿到的非 null 返回值可能指向另一个地址，照 `panel` 操作会作用错对象。建议文档写明返回值语义，或拒绝时返回 null 并让调用方重新查询。
- **R5-UAU-013 · P3 · D4**：`Assets/XFramework.UIAuthoring/Runtime/UIAuthoringNavigation.cs:78-83` —— `ShowDialogAsync` 在 `assetPath`/`actionId` 为空时直接返回 `OpenFailed`，**不发布** `UIAuthoringDialogResultMessage`；而同包 `UIManagerActionRuntime.ShowDeclarativeDialogAsync` 对空参数发布结果消息（`:83-97`）。同一语义两条路径的可观测性不一致；若项目只靠消息驱动 UI，会漏掉这类失败。
- **R5-UAU-014 · P3 · E**：`UIAuthoringActionMessage` / `UIAuthoringDialogResultMessage` 按 `actionId` 走 `MessageManager` 全局键空间；两个面板使用同一个 `actionId`（如都叫 `confirm`）会互相收到消息。README 的「自定义动作」节只写「项目按 actionId 订阅」，未写「actionId 需全项目唯一 / 建议加面板前缀」。建议在行为契约补一句键空间约定。

### FMOD（扩展包，首次审计）

- **R5-FMD-001 · P2 · D4/A2**：`Assets/XFramework.Fmod/Runtime/Integration/FmodEventBackend.cs:162-168` —— `SetGlobalParameter` 在 `RuntimeManager.IsInitialized == false` 时直接返回 `false`，而其余 FMOD 入口（`CreateInstance` / `LoadBank` / `GetBus` / `HasBankLoaded`）都会经 `RuntimeManager.Instance` 懒初始化。首次在「还没有 bank/播放」时设置全局 RTPC（常见顺序：先设全局参数再起播）会静默失败——核心 `AudioManagerImpl.TrySetGlobalParameter`（`:207-220`）只透传 `false`，不记日志；包 README 只写「参数经 AudioManager」，未写这个前置。建议删掉该早退（`StudioSystem` 会懒初始化，`SystemNotInitializedException` 已被下方 catch），或至少打一条一次性 Warning 并写入已知限制。验证：PlayMode 探针，在未加载 bank/未播放时 `AudioManager.SetGlobalParameter` → 断言 `true`；改前必红。
- **R5-FMD-002 · P3 · A1/C2 [需实测]**：`Assets/XFramework.Fmod/README.md:107,115,126` 与 `Documentation/FMOD.md` 的「bank 加载同步阻塞」——FMOD 2.04 的 `RuntimeManager.LoadBank` 在 **Android + `AndroidUseOBB`** 与 **WebGL** 走协程（SDK `RuntimeManager.cs:1003-1013` 的 `StartCoroutine(loadFromWeb)`），`FmodEventBackend.LoadBank`（`:191-216`）在协程尚未完成时就返回 `true`；安装成功不等于 bank 已可用，异步失败也不会回传给安装器。建议 README 按平台写明该例外（或安装后轮询 `RuntimeManager.HaveAllBanksLoaded`），并标 `需实测`。
- **R5-FMD-003 · P3 · A1**：`Documentation/FMOD.md:153` 写「`FmodAudioSettings` 放在任意 `Resources` 目录」，而 `FmodAutoInstall.cs:18,38` 用 `Resources.Load<FmodAudioSettings>("FmodAudioSettings")`——只认 Resources 根级（或命名为该路径的资源）；`Resources/Settings/FmodAudioSettings.asset` 这类嵌套路径不会被找到。包 README `:29` 的「在 `Assets/Resources/` 下创建」是准确的，维护向文档应同步收窄。建议改为「任意 `Resources` 目录的根级」。
- **R5-FMD-004 · P3 · A1**：`Documentation/AudioProvider.md:407` 引用不存在的 fixture 名 `IntegrationAsmdef_IsConditionallyGated`；实际 fixture 是 `FmodDependencyDeclarationTests.IntegrationAsmdefs_BindDefineToConstraints`（`FmodDependencyDeclarationTests.cs:88`）。建议按真实名订正。
- **R5-FMD-005 · P3 · A1/D1**：`Assets/XFramework.Fmod/README.md:24-25` 的「包名不是预期时手动加 `XFRAMEWORK_FMOD`」没有写明前提：Integration asmdef 硬引用 `FMODUnity`（`Runtime/Integration/Venusy609.Xframework.Fmod.Unity.asmdef:4`），若 FMOD 未安装却手动定义该符号，Integration 程序集会进入编译并因缺少 `FMODUnity` / `FMOD.Studio` 类型报 CS0246。建议补一句「仅在 FMOD 已安装（或同名 `FMODUnity` 程序集存在）时手动定义」。
- **R5-FMD-006 · P3 · D3**：`Assets/XFramework.Fmod/Runtime/Integration/FmodEventBackend.cs` 与 `Tests/Runtime/Integration/FmodEventBackendTests.cs` 只在 `XFRAMEWORK_FMOD` 定义时编译（两个 asmdef 都有 `defineConstraints`）。干净克隆的默认全量（本仓当前 2405 例）**不包含**这 7 个集成用例，也不编译 Integration 源码；现有证据是 2026-11-07 的一次性两态实测（`Documentation/FMOD.md` §七/§九）。建议在包 README 的「已知限制」写明「默认门禁只编译 FMOD-free 层」，并把「有 SDK 全量」登记为一次性探针（R5-PRB-xx）。
- **R5-FMD-007 · P3 · D3**：`Tests/Runtime/FmodSettingsTests.cs` 只以 `ScriptableObject.CreateInstance` 构造配置，没有资产往返（`AssetDatabase.CreateAsset` / `LoadAssetAtPath`）用例；`FmodAudioSettings` 的序列化字段一旦在某 Unity 版本不序列化，`FmodAutoInstall` 会读到默认值而测试全绿。建议补一条 EditMode 资产往返（与 R4-CONF-003 同形），或写明「配置资产由 Inspector 负责、包不承诺序列化契约」。

## 7. 跨模块主题

### 7.1 前轮修复的残留与「修复只改一处」

R4 的 71 条全部有处置，但本轮仍能看到同族问题的第二次显形：`R5-UAU-001`（控制器拦下关闭后会话失效）与 R4-UIB-003 的控制器门控同源——都是「关闭路径返回了，但调用方不知道」；`R5-AUD-001`（Audio 不在复位族）与 R4-AST-006 的「在途 impl 订阅静态事件」同族——静态态只丢引用不够。共同根因仍是「修复提交只改被引用的一处」；实施轮继续按「一个修复核对五处 + 改前必红」。

### 7.2 扩展包文档门禁射程（R5-ENG-001）

R4-ADJ-03 把扩展包 XML 文档留给评审；本轮实测两个包已有 **9 条**文档告警（UIAuthoring 8 CS1591 + Luban 1 CS1574），证明这条射程缺口在增长。是否把 `check-docs.ps1` 的扫描根/项目列表泛化，还是给每个包加一条 DocumentationFile 探针，是实施轮的工程裁定（R5-ADJ 候选）；不改变「核心六道门禁只扫核心包」的现有口径。

### 7.3 发布就绪（四包）

- 核心 `package.json` 0.4.0、`[Unreleased]` 含 Audio provider 缝 + UI 校验器；`git tag` 仍只有 0.3.0（R4-ADJ-04 未做）。
- Luban 0.1.0、Fmod 0.2.0、UIAuthoring 0.1.0 均无 tag；三个包的 `[Unreleased]` 分别有 R4 修复 / Phase 2 修复 / 阶段 2b/3/4。
- 根 README 与包 README 的安装 URL 仍是 `#<发布 tag>` 占位（核心 `:39,54,83`、Luban `:22,34`）。
- 结论：**未就绪**；tag 与 URL 是发布动作，不阻塞代码审计。

### 7.4 依赖真相与 asmdef 反查（R5-ENG-002/006）

核心三处（包 README 依赖表 / 安装器清单 / manifest）仍一致（`DependencyDeclarationTests` 全绿）；但 `DependencyDeclarationTests` 不反查 asmdef，本轮新增的 Editor asmdef 引用（`YooAsset` / `YooAsset.Editor`）与 UIAuthoring Editor asmdef 的引用只在运行时被读到，不在包依赖守卫射程。当前不构成编译缺陷（YooAsset 已声明），但反查机制未建的缺口由 R4 的「未做」继续承接。

### 7.5 「类型即身份」边界在新包上的复用

UIAuthoring 的 `UIDeclarativePanel` / `UIDeclarativeDialogPanel` / `UIDeclarativePopupPanel` 用**不同具体类型**绕开核心「按具体类型登记」的单根限制（`Documentation/UIAuthoring.md` §2.3）；核心零改动。这不是新机制，而是对既有约束的正面利用。R5 复核：三种类型可同时打开、各自单飞，与设计一致；`Open`/`Push` 校验接受 Popup 是这条边界的反例（R5-UAU-003）。

### 7.6 条件编译与测试真实性（FMOD / UIAuthoring）

FMOD 的 Integration 程序集与 7 个集成用例只在 `XFRAMEWORK_FMOD` 下编译，干净克隆的默认全量不编译它们（R5-FMD-006）；UIAuthoring 的生产 `UIManagerActionRuntime` 零直接测试（R5-UAU-010）。两处共同说明：**包内守卫覆盖「结构/形状」，行为覆盖仍靠 fake 与一次性探针**。实施轮应优先把「生产运行时/真实后端」的缝补上，而不是增加结构守卫。

### 7.7 台账、计数与文档状态

- `check-docs -Enforce` 535 条台账（R4 冻结 463；增长来自 R4 实施轮、FMOD/UIAuthoring 包与新增模块条目）。
- `Modules/README.md` 可选扩展包缺 UIAuthoring 指针（R5-ENG-004）；根 README 阶段 2a（R5-ENG-003）；`Documentation/UIAuthoring.md` 维护向漂移（R5-UAU-009）。
- `Documentation/ModuleAudit.md` 与 `CLAUDE.md` 的 AuditRound5 登记已在本轮步骤 0 完成。

## 8. 逐模块审计记录

> 每个对象记录：范围与输入、18 类判据留痕、前轮修复偏差、旧结论复核、开口项、第三方对照、残留与不确定。P0/P1 明细见 §4/§5，P2/P3 明细见 §6。

### FMOD（T1 扩展包全量，首次）

- **范围与输入**：`Assets/XFramework.Fmod/` Runtime 8 个 `.cs`（`FmodAudioProvider` / `FmodEventSlotTable` / `FmodAudioSettings` / `FmodProviderOptions` / `IFmodEventBackend` / `FmodInstaller` / `AssemblyInfo`）、`Integration/` 4 个（`FmodEventBackend` / `FmodAutoInstall` / `AssemblyInfo` / asmdef）、Tests 16 文件（12 fixture / 92 例）、Architecture 6 个守卫 fixture、README / CHANGELOG / package.json、`Documentation/FMOD.md` / `Documentation/AudioProvider.md`、核心 Audio 接缝；FMOD 2.04 本地 SDK 源码（`E:\UnityPackages\com.fmod.fmod-for-unity.v204`，`src/RuntimeManager.cs` / `src/Settings.cs` / `src/RuntimeUtils.cs`）。
- **18 类留痕**：A1 命中（`FMOD.md:153` 的 Resources 射程、README 的 bank 同步说法、`AudioProvider.md:407` fixture 名、手动 define 前提，R5-FMD-002/003/004/005）；A2 命中（`SetGlobalParameter` 首次调用早退，R5-FMD-001）；A3 **人工扫过**（FMOD-free 程序集用 `dotnet build -p:DocumentationFile` 实测 0 条 XML 告警；Integration 因无 SDK 未编译，人工读注释未发现未转义泛型/死链，不能声称编译器已验）；B1 扫过（`Dispose` 后全部成员返回 default/false，`FmodAudioProvider` 与 `FmodEventBackend` 分别有幂等 Dispose 与 after-dispose 用例）；B2 扫过（槽位表代际 + `Release` 幂等 + 所有失败路径先归还槽位；`Dispose` 逆序释放 bank）；B3 扫过（provider 对 backend 的每次调用都 try/catch；installer 对 bank/backend 清理逐段隔离；backend 自身只对已知 FMOD 异常兜底，未知异常由 provider/installer 兜住）；B4 **命中-已文档化**（README 声明全部主线程，代码零断言；`Modules/Audio.md:99` 已记「暂不做」，R5 不重开，列为残留）；B5 扫过（`_instances` 按事件释放、`_ownedBanks` 按卸载移除、槽位表容量固定）；B6 不适用（无共享协调协议；provider/backend 无静态状态）；C1 扫过（provider 原语同步、`UniTask.FromResult` 无额外堆分配；`BuildEventPath` 拼接已写入「性能代价」）；C2 命中（Android OBB / WebGL 的 bank 协程，R5-FMD-002 标 `需实测`）；D1 扫过（FMOD-free 不引用 `FMODUnity`；Integration 硬引用只在条件程序集；包边界/依赖声明守卫在位）；D2 不适用（无重载）；D3 命中（Integration 默认不编译/不跑、配置资产无往返，R5-FMD-006/007）；D4 命中（全局参数首次调用静默 false，R5-FMD-001）；D5 扫过（依赖核心 `IAudioProvider` + 五个 internal 能力接口，无缺失原语；`SetPaused`/`SetVolume` 的宽 false 语义已在 README 与核心台账 `暂不做`）；E 扫过（bank/bus/snapshot 名称交给 FMOD 键空间，重名语义由 FMOD 定义并写入 README）；F 命中-低（`UnloadBank` 返回「是否已卸载」= `!HasBankLoaded`，与核心 XML 一致；引用了别的持有者时返回 false 属可接受语义，不单列）。
- **前轮修复偏差**：R4 未审计 FMOD；2026-11-08 的包内修复批（`da815828` / `0f0b8d60` / `1501fb30` / `176e5cc7` / `f81b76fc` / `58631415` / `d73d5f32` / `e2e6a816` / `3d624463`）均有代码 + 用例/守卫证据，本轮未发现二次回归。
- **旧结论复核**：`Documentation/AudioProvider.md` §九与 `Documentation/FMOD.md` §十一的「不提供 FmodAudio 门面 / 能力接口 internal / 配置资产自动安装 / 不内建 SDK」全部维持；`Modules/Audio.md` 的「SetPaused/SetVolume 宽 false 只记录」与「主线程断言暂不做」维持。
- **开口项**：R5-FMD-001 建议进修复批次；R5-FMD-002/006 进探针轨（Android OBB/WebGL、有 SDK 全量）；R5-FMD-003/004/005/007 为文档/测试补齐。
- **第三方对照**：FMOD 2.04 `RuntimeManager.LoadBank` 的引用计数（`loadedBanks.RefCount`）与异步平台分支、`RegisterLoadedBank` 的 `BankLoadException`（同步路径会抛；异步路径在协程内失败只记日志）、`Settings.SettingsAssetPath` = `FMOD/FMODStudioSettings`、`RuntimeManager` 懒初始化（`Instance` 属性）。**上游注意**：`RuntimeManager.LoadBank` 在 `RegisterLoadedBank` 抛 `BankLoadException` 时已 `loadingBanksRef++` 但不会 `--`，`HaveAllBanksLoaded` 会永久为 false；本包不使用该属性，如实记录为第三方事实。
- **残留与不确定**：真实 bank/event 冒烟（需 Studio 2.04 工程 + `XFW_FMOD_EVENT` / `XFW_FMOD_BANK`）仍未跑；Integration 程序集与 7 个集成用例在本环境默认不编译；Android OBB/WebGL 的异步 bank 未实测；包无发布 tag（0.2.0）。

### Audio（T1 全量：provider 缝 + 控制面）

- **范围与输入**：`Assets/XFramework/Runtime/Audio/` 29 个 `.cs`（R4 后新增 12 个：`IAudioProvider` / `AudioProviderHandle` / `AudioProviderPlaybackState` / `AudioPlaybackRequest` / `AudioParameter` / `AudioSourceProvider` / `AudioManagedVoice` 与五个能力接口；`AudioManagerImpl` +1,022 行、`AudioManager` +304、`AudioSourcePool` 重写）；Tests 22 文件 / 217 例（`AudioProviderSeamTests` 41、`AudioControlSurfaceTests` 15、`AudioMixerControlTests` 12、`AudioParameterTests` 3、`AudioSpatialTests` 6 等）；README、`Documentation/Modules/Audio.md`、`Documentation/AudioProvider.md`；FMOD 包的 provider 实现。
- **18 类留痕**：A1 命中（探测型表缺「加载窗口内 false」的统一表述，R5-AUD-002）；A2 扫过（公开 `IAudioProvider` + `TryGetProviderHandle`，能力接口 internal 经 IVT，与 README「中间件能力」一致）；A3 扫过（核心文档门禁 0 条）；B1 扫过（`Dispose` 后全部成员安全；`Destroy` 隔离 provider Dispose 异常）；B2 扫过（validity 注册 → `Invalidate` 归还池槽；代际防晚到续体；租约释放点恰好一次）；B3 扫过（provider 调用、取消回调、Dispose、音量刷新逐段 try/catch）；B4 **命中-已文档化**（无主线程断言，README/台账已写）；B5 扫过（槽位固定、通道表只随写入增长、租赁随释放清理）；B6 命中（会话复位族不含 Audio，R5-AUD-001）；C1 扫过（`PlayAsync` 同步原语 + 每次播放一次 validity 注册，无每帧分配；`BuildEventPath` 只在 FMOD provider）；C2 扫过（`StartingTimeoutTicks` 真机未验证，已在台账 `未做`）；D1 扫过；D2 扫过（`AudioParameter` 两个构造按 `float` / `string` 区分，`null` 只匹配 string；`AudioPlayOptions` 7 参构造源兼容）；D3 扫过（provider 缝 41 例覆盖失败/取消/异常/容量）；D4 命中（加载窗口 false 的文档表述，R5-AUD-002）；D5 扫过；E 扫过（通道字符串、保留 master）；F 扫过（`IsPlaying` vs `ActiveVoiceCount`、`Poll` 与状态查询一致）。
- **前轮修复偏差**：R4-AUD-001（坏源重建/丢弃）已修并有 `AudioPoolTests` 两例；R4 之后的 provider 缝修复批（`2142ec25` P1-1/2/3、`53265018` P2、`31e47c3b` CTS 复用）均有代码 + 用例证据，本轮未发现二次回归。
- **旧结论复核**：不提供 `Play(AudioClip)`、不建 Mixer 路由入口、v2 候选（抢占/淡入淡出/切后台/借用 AudioSource）全部维持；`Modules/Audio.md` 的 `暂不做`（主线程断言、宽 false 语义）维持。
- **开口项**：R5-AUD-001 进探针轨（关闭域重载 + FMOD 手工两遍）；R5-AUD-002 文档订正。
- **第三方对照**：FMOD 2.04 / Wwise 的播放原语映射沿用 `Documentation/AudioProvider.md`；本轮用本地 FMOD SDK 复核了 `RuntimeManager` 懒初始化与 bank 引用计数（见 FMOD 条）。
- **残留**：`StartingTimeoutTicks` 真机、`AudioListener.pause` 平台差异、IL2CPP、关闭域重载两遍（含 FMOD）、每次 Play 的 validity 注册分配未量化。

### UI 编辑期预制体校验器（T1 核心新面）

- **范围与输入**：`Assets/XFramework/Editor/UI/` 9 个 `.cs`（`UIPrefabValidator` 394 行、扫描器 232、地址目录 193、YooAsset 实现 102、报告 199、窗口 293、候选探测 42、TMP 探测 50、规则号 84）；Tests 2 文件 / 36 例（校验器 29、扫描器 7）；核心 UI README 的「预制体要求」与「自检」两节；`Venusy609.Xframework.Editor.asmdef` 新增 `YooAsset` / `YooAsset.Editor` 引用。
- **18 类留痕**：A1 命中（面板无 binding 时约定检查静默跳过，R5-UIE-001）；A2 扫过（实现全 internal，无公开面）；A3 扫过（核心文档门禁覆盖 Editor 程序集，0 条）；B1 不适用（无生命周期对象）；B2 不适用（不回调、不回池）；B3 扫过（扫描器逐预制体 try/catch，地址 provider 失败回退报告通知）；B4 不适用（编辑器同步）；B5 扫过（每次扫描建目录，扫描结束即丢）；B6 不适用（无静态状态）；C1 扫过（编辑器路径，允许 `BuildPath` 的字符串分配）；C2 扫过（`simulateBuild: true` 的主资源语义已核对 YooAsset 2.3.18）；D1 扫过（Editor asmdef 新增 YooAsset 引用属预期；依赖声明守卫不反查 asmdef，见 §7.5）；D2 不适用（无重载）；D3 命中（窗口/报告无自动化，R5-UIE-002）；D4 扫过（地址不可用时以报告通知跳过，不静默）；D5 扫过；E 扫过（节点名键按大小写不敏感，与运行时一致）；F 扫过（规则与运行时同一套前缀/深度/重名语义）。
- **前轮修复偏差**：R4 未覆盖（本功能 2026-11-08 新增）；R4-UIB-009 的 `img_` 编辑器告警与本校验器无交集。
- **旧结论复核**：「校验器手动、不在保存/构建时自动触发」「不做地址字符串扫描/自动修复」维持（核心 UI README `:354` 与台账）。
- **开口项**：R5-UIE-001 补规则/文档；R5-UIE-002 登记窗口未自动化。
- **第三方对照**：YooAsset 2.3.18 `BeginCollect(simulateBuild:true)` 的主资源/依赖语义与 `AssetBundleCollectorSetting` 静态门面会创建资产的行为（避开方式与 UIAuthoring 包地址 provider 相同）。
- **残留**：HUD 只查 001/008（核心 README `:351` 已写明）、窗口无自动化、地址检查需工程内唯一采集配置、Unity 6 TMP 类型探测按全名。

### Luban（T1 复审：R4 批次 3/4 修复）

- **范围与输入**：`Assets/XFramework.Luban/` Runtime 15 个 `.cs`、Editor 15 个、Tests 71 文件（R4 后变更 24 文件）、Architecture 守卫 fixture（AutoInit / 依赖 / Editor+Runtime API 基线 / `.meta` / 包边界 / README）、README / CHANGELOG / package.json、`Documentation/Luban.md`；核心 `Config` / `Asset` / `Bootstrap` / `Log` 接缝。
- **18 类留痕**：A1 命中（TextAsset 释放顺序、死 cref，R5-LBN-001/002）；A2 扫过（`ILubanConfigAdapter` 契约完整；`LubanConfigBridge` 依赖核心公开面）；A3 命中（实测 CS1574，R5-LBN-002；包不在文档门禁射程）；B1 扫过（`Destroy`/`ResetForNewSession` 代际 + 在途取消；引导阶段所有权已按 `CurrentImpl` 比对）；B2 命中（TextAsset 句柄先释放后读字节，R5-LBN-001）；B3 扫过（注册失败 `TryUnregister`、引导失败复位、写盘回滚）；B4 **命中-已文档化**（主线程断言只覆盖写入口，R4-ADJ-09 已收窄 README）；B5 扫过（等待者按代际摘除，无无界缓存）；B6 扫过（`_generation` + `_inflightCts` + `_initWaiters` 按代际过滤；`ResetForNewSession` 只丢引用）；C1 扫过（初始化路径分配；无每帧路径）；C2 扫过（路径大小写 / cwd 仍待 Linux 探针 R4-PRB-09）；D1 扫过（FMOD-free 包不引用 Luban 程序集；包边界守卫）；D2 扫过（`RegisterTable` 两个重载按参数个数区分）；D3 命中（TextAsset 真实句柄生命周期只经 seam 间接覆盖，R5-LBN-001）；D4 扫过（provider 级错误带表名/location；写盘失败带可操作消息）；D5 扫过（核心已有 `RegisterTable` / `RegisterGlobal` / `AssetManager.LoadAllAsync`）；E 扫过（表键 = 行类型 + 主键）；F 扫过（`ConfigChanged` 逐表触发已文档化）。
- **前轮修复偏差**：R4-LBN-002/003/004/005/006/008/010/013/015/017 逐条有代码 + 用例/守卫证据；LBN-001/007/011/012/016 为文档；LBN-009（Linux 大小写/cwd）仍 `未做`（R4-PRB-09）；LBN-014 已按口径订正。未发现二次回归。
- **旧结论复核**：`Documentation/Luban.md` §7.8 六条（不重建核心适配层、不公开 `ILubanDataProvider`、不做 bin-offset/分片、不支持三个 JSON target、不做 Roslyn generator、不移入核心 Runtime）全部维持。
- **开口项**：R5-LBN-001（TextAsset 字节复制）建议进修复批次；R5-LBN-002 文档；R5-LBN-003 发布动作。
- **第三方对照**：Luban 5.1.0 生成物事实（`Tables` 在 `namespace cfg`、map 表 `DataMap`、list 私有字典 + `DataList`）沿用 R4，本轮未重读生成器。
- **残留与不确定**：Linux Editor 路径探针（R4-PRB-09）、真实 YooAsset TextAsset 的释放-读取时序、包发布 tag。

### T2 增量复审（15 个核心模块）

> 方法：读 R4 修复提交 diff + 当前变更文件全文 + 对应回归用例；18 类逐类扫过，只列命中与残留。除下列条目外**未发现新 P1/P2**。

- **Config**：`ConfigManagerImpl`（(Type, Kind) 在途槽、loader 实例比较）、`ScriptableObjectLoader`/`ConfigTextLoader`（`IConfigAssetLease` + `using`）、新增 `ConfigAssetLease`。抽样确认 CONF-001/002/005/006 修复无回归；A3 文档门禁 0 条。**残留**：SO 资产在句柄 Release 后仍被表持有（R3-PRB-02 低内存假 null 探针仍 `未做`），本轮不重开。
- **Asset**：`AssetManager.SetInstance`（旧实例按所有权 Dispose/Detach）、`AssetBootstrapStage`（代际 + 所有权）、`YooAssetManagerImpl.GetInitializedPackage`、`SessionResetTests` 在途 impl。抽样确认 AST-001/002/003/006 无回归；B6 代际槽已按 R4 修复。**残留**：真 YooAsset 包状态 / EditorSimulate 探针沿用既有登记。
- **UI Runtime**：三条 P1 修复已逐条读 diff 与用例（§3.1）；UIA-002/005、UIB-003/004/005 修复在读 `UIManagerImpl`/`UIHudManager` 当前代码时复核，未发现二次回归。**残留**：`UIViewBase.IsRecycled` 是 internal 新状态，未进公开 API 基线（internal 不承诺）。
- **Diagnostics**：DIAG-001 `SafeOrder`（坏 Order 兜底 `int.MaxValue` + Error）、DIAG-002 `lastColumn` 统一表头/分隔线；用例在位。
- **Save**：SAVE-002 `DeleteSlotAsync` 改「载荷 || 备份」；`DeleteSlotAsync_BackupOnly_DeletesAndDoesNotRecover` 锁定。
- **Input**：INP-001 `(action, playerId)` 计时 + `_durationRequests` + 每玩家控件边沿；`GetButtonPressDuration_IsPerPlayer` 锁定。
- **Camera**：CAM-002 `TransitionToSlot` 提前返回也接管 boost；CAM-005 `FindSlotByCamera`/`ResolveCameraId` 用 `ReferenceEquals` 保留已销毁注册相机 id；用例 `Activate_WhileExternalOverride...` / `DestroyedRegisteredActiveCamera_...` 在位。
- **Update**：UPD-001 README/XML 已改为「`Initialize` / `SetInstance` 都会重接」；无代码变更。
- **Settings**：SET-001 读侧池线程只做 IO/解密、解析切回主线程；`LoadAsync_SyncInner_DecryptsOnPoolAndParsesOnMainThread` 锁定。
- **Pool**：POOL-001 `Return` 满池丢弃回调 try/catch；`Return_WhenPoolFull_DiscardCallbackThrows_IsIsolated` 锁定。
- **Pipeline**：PIPE-001 容器在 finally 前写 `Completed`/`1f`；`Execute_Success_WritesCompletedTerminal` 锁定。
- **Localization**：LOC-001 `GetFormat` 的替换值花括号转义、`Get` 不转义；`GetFormat_PlaceholderValueWithCompositeItem_RendersLiterally` 锁定。
- **File**：仅 README（null/空 = 域根）变更；无代码。
- **Serialize**：仅 README（`ResetForNewSession` 清更早注册）变更；无代码。
- **Scene**：SCN-001 激活失败告警用 `Scene.IsValid() && isLoaded ? name : location`；`LoadSceneAsync_ActivateWithInvalidScene_WarnsWithLocationAndReturnsHandle` 锁定。

### T3 紧凑留痕（8 个核心模块）

> R4 后 Runtime 零变更；读模块 README/台账关键面，18 类逐类扫过，无新证据即维持旧结论。

- **Bootstrap**：阶段所有权/逆序 Shutdown/取消传播维持；R4-BST 无新项。
- **Data**：快照脏集合/`OnClear` 隔离/会话复位维持。
- **Event**：`EventStream` 池化与 `onEmpty` 隔离维持；`SubscriptionTracker` 的 Editor 计数不自动清零仍是测试夹具约定。
- **Lock**：先绑定后派发、包内表述维持；ABA 红证仍缺（R3 开口项）。
- **Log**：`ILogSink` 摘除、Flush、JSONL 维持；无新面。
- **Message**：`TryBindToDestroy` / `BindToContext` 维持；keyed 全局键空间与 `Release` 无主线程防线仍是 R3 开口项。
- **Reactive**：双承诺文档化、`Select` 单算子、集合变更流维持；装箱量无法用本仓尺子实测（RCT-010）。
- **Timer**：档位/跨表补扫/锚点维持；`TimerTierTests` 红证在位。

### UIAuthoring（T1 扩展包全量，首次）

- **范围与输入**：`Assets/XFramework.UIAuthoring/` Runtime 31 个 `.cs`、Editor 7 个、Tests 25 文件 / 124 例（63 Runtime + 12 Architecture + 49 Editor）、7 个 Architecture 守卫 fixture、`Samples~/Templates`（Panel / Dialog / Hud / Popup 普通 Prefab）与 `Samples~/DeclarativeDemo`、README / CHANGELOG / package.json、`Documentation/UIAuthoring.md`；核心 UI 接缝（`UIPanelBase` / `UIDialogPanel<string>` / `UIHudItem` / `UIManager.ShowPopupAsync` / `UIBinder`）。
- **18 类留痕**：A1 命中（Kind 列表、`ButtonNodeName` 文档、缺本地化 key 的规则号、维护向记录漂移，R5-UAU-005/007/008/009）；A2 命中（`Open`/`Push` 目标接受 `UIDeclarativePopupPanel`，R5-UAU-003）；A3 命中（8 条 CS1591 实测，R5-UAU-006）；B1 命中（控制器拦下关闭后会话仍失效，R5-UAU-001）；B2 命中（`_pendingResultId` 跨 Dispose 回池存活，R5-UAU-001）；B3 命中（`ShowDialog` 取消被吞，R5-UAU-002）；B4 **命中-已文档化**（全部主线程、代码零断言；核心 UI 同样无断言，列为残留不重开）；B5 扫过（注册表按项目注销、面板级订阅由 `Track` 释放）；B6 扫过（`UIDataProviderRegistry.ResetForNewSession` 在 `SubsystemRegistration`，守卫登记）；C1 扫过（绑定只在 OnOpen，值变化走强类型回调；`Format` 每次变化一次字符串已写入「性能代价」）；C2 扫过（YooAsset 构建图与模板导入探针已做）；D1 扫过（只用核心公开面；Editor asmdef 新增 `YooAsset` / `YooAsset.Editor` 引用，依赖声明守卫只查 Runtime asmdef）；D2 不适用（无重载）；D3 命中（生产 `UIManagerActionRuntime` 零直接测试、拦截路径零覆盖，R5-UAU-010）；D4 命中（`ShowDialogAsync` 空参数不发布结果、异地址返回非 null，R5-UAU-012/013）；D5 扫过（核心已给 `OpenAsync` / `ShowPopupAsync` / `ShowDialogAsync` / HUD 原语，无缺失）；E 命中（`actionId` 全局键空间，R5-UAU-014）；F 命中（异地址拒绝时返回已存在面板，R5-UAU-012）。
- **前轮修复偏差**：R4 未覆盖本包（阶段 2a/2b/3/4 均在 R4 之后）；2026-11-08 的设计审计（`Documentation/UIAuthoring.md` §1.5）已修的事实与引用错误本轮复核维持；实现期的 `9981bf5c`（扫描器按根组件读定义）有代码 + 用例证据。
- **旧结论复核**：设计文档的三条裁定（单根、只重开模板、HUD/Dialog 纳入）与三条「不做」（可视化设计器、保存/构建门禁、向导/脚本/地址生成）全部维持；SO 定义资产、keyed 消息、独立 Popup 具体类型的取舍与实现一致。
- **开口项**：R5-UAU-001/002 建议进修复批次；R5-UAU-003..014 为文档/测试/规则号补齐。
- **第三方对照**：FairyGUI / UI Toolkit 的结论沿用 `Documentation/UIAuthoring.md` §6.6 与 `FairyGUI.md`，本轮未重做；本轮新增的对照点是「声明式制作层与核心具体类型注册的耦合」——单根限制来自核心按具体类型登记，Popup 用独立类型绕开，与设计文档一致。
- **残留与不确定**：P6 IL2CPP/AOT（`IUIDataProvider.TryGet<T>` 泛型实例化）未验证；TMP 只有一次性探针、自动化仍只测 UGUI `Text`；真实核心 `UIManager` 集成仍以 fake 为主；`Samples~` 的 Prefab/定义资产未做本轮导入回归；包无发布 tag（0.1.0，阶段 2b/3/4 在 `[Unreleased]`）。

### 工程面（Tools / 守卫 / 示例 / 包元数据 / 发布）

- **范围与输入**：`Tools/run-tests.ps1`（`-TimeoutSeconds`、四个 `-Update*` 开关）、`check-docs.ps1`（覆盖范围/门禁码/三组 Samples 根）、`check-samples.ps1`；核心 11 个 Architecture 守卫 + 三个扩展包的守卫清单；四个 `package.json`、`git tag`、四份 `CHANGELOG` 的 `[Unreleased]`；根 README、`Documentation/Modules/README.md`、`Documentation/Workflow.md`、`Documentation/Ledger.md`、`CLAUDE.md` 的文档分层登记。
- **18 类留痕**：A1 命中（根 README 阶段 2a、Modules README 可选包缺 UIAuthoring，R5-ENG-003/004）；A2 扫过（核心 11 处门面完备性守卫 + 包内门面/可见性守卫）；A3 命中（扩展包 XML 文档告警实测非零，R5-ENG-001）；B1–B6 扫过（工程面无生命周期/并发/共享槽）；C1 扫过（基准套件 6 个 `[Explicit]` 不进门禁）；C2 扫过（`run-tests` 超时已实现，退出码 3 与失败/无结果区分，`Workflow.md:104`）；D1 命中（Editor asmdef 新引用与依赖反查缺口，R5-ENG-002；包依赖守卫 Editor 侧射程，R5-ENG-006）；D2 扫过；D3 命中（窗口/集成态/包文档测试缺口已分别登记在 UAU/UIE/FMD/LBN 条目）；D4 命中（Modules README 可选包指针，R5-ENG-004）；D5 扫过；E/F 扫过。
- **门禁快照**：BUILD 0 error；FULL PlayMode 2405/2405、EditMode 749（743 + 6 skip）；DOC 通过（0 门禁码、535 条台账、24/24 文件树、三组 Samples 编译）。`-Update*` 四个开关（核心 / Luban / Fmod / UIAuthoring 双基线）均在 `Workflow.md:107-111` 与脚本中一致。
- **新发现**：R5-ENG-001..006（§6）。
- **前轮修复偏差**：R4-ENG-001/002/003/005/009 已修；ENG-004（check-docs 不覆盖扩展包）以 R5-ENG-001 的实测证据重新提交；ENG-006（Editor 基线）已做；ENG-007（示例只守编译）维持。
- **旧结论复核**：CI 仍暂不接入（`CI.md` 三条重开判据未触发）；包在子目录 / `Tests` 不移出 / 不做 Git 钩子维持；OpenUPM 仍待上架（外部动作）。
- **第三方对照**：无（工程面为本仓流程）。
- **残留**：扩展包 tag、OpenUPM、真实 CI、`check-docs` 射程泛化、依赖反查。

## 9. 待裁的一次性裁定建议

> 审计期只写建议；台账状态由实施轮经用户确认后更新。状态词按 `Modules/README.md` 的六词表。

| ID | 模块 | 问题 | 推荐 | 理由 | 落点 | 重开判据 |
|---|---|---|---|---|---|---|
| R5-ADJ-01 | UIAuthoring | `UIActionKind.HideSelf` 三条路径均不可达 | `暂不做`（保留枚举、文档写清「为未来 HUD 动作预留」） | 删除是公开 API 变更且预 1.0；补 HUD 动作需要新设计 | 包 README / XML / 台账 | 出现真实的 HUD 动作需求 |
| R5-ADJ-02 | 工程面 | 扩展包 XML 文档告警无门禁（实测 9 条） | `未做`（每包加 DocumentationFile 探针） | 成本低、证据已现形；泛化 `check-docs` 可后置 | `Tools/check-docs.ps1` 或包内守卫 | 探针接入后关闭 |
| R5-ADJ-03 | 工程面 | 依赖清单从 asmdef 反查仍未建 | `未做`（维持 R4 结论） | 当前三处声明一致；Editor asmdef 是第二个实例但无缺陷 | `Documentation/Ledger.md` | 再出现一次漏列事故 |
| R5-ADJ-04 | 发布 | 四个包均无 tag / 安装 URL 占位 | `未做`（用户执行） | 发布流程动作，不是代码缺陷 | `CHANGELOG.md` / `package.json` / tag | 发布流程启动 |
| R5-ADJ-05 | Audio | 会话复位族是否纳入 Audio | `待裁` | 加入 `ResetForNewSession` 会改变「只丢引用」边界；不改则依赖 `Application.quitting` 需实测 | `Modules/Audio.md` | 关闭域重载 + FMOD 探针显示第二会话失效 |
| R5-ADJ-06 | UIAuthoring | 异地址拒绝时返回已存在面板 | `待裁`（文档化 vs 返回 null） | 返回 null 是行为变更；文档化成本低 | 包 README / XML | 调用方依赖返回值区分的真实需求 |
| R5-ADJ-07 | FMOD | `AudioManager` 已初始化时自动安装跳过 | `暂不做`（文档写清「先装 FMOD 再初始化」） | 支持晚装会与 `SetProvider` 的 Destroy 语义冲突 | 包 README / `Documentation/FMOD.md` | 出现「先初始化 AudioManager 再想装 FMOD」的真实使用方 |

> `未做/暂不做/待裁` 在写入台账前须经用户确认；公开 API / 破坏性变更（R5-ADJ-01 若选择删除）须单独确认。

## 10. R5 分步实施计划

> 审计轮只出计划；实施轮按「一行一提交、测试与修复同提交、行为变更同提交写 CHANGELOG」执行。命令缩写：`PM(x)` = `run-tests.ps1 -Filter "x" -Platform PlayMode`；`EM(x)` = EditMode；`ALL(x)` = 双平台；`FULL` = 全量；`DOC` = `check-docs.ps1 -Enforce`；`BUILD` = 两个核心 csproj；`API` = 公开 API 基线流程。每条 P1/P2 修复先写「改前必红」。

### 批次 0：P1（先止血）

| # | 提交信息 | 覆盖 | 验证 |
|---|---|---|---|
| 0.1 | `fix(ui-authoring): 控制器拦下关闭时保持会话与结果 (R5-UAU-001)` | `UIActionExecutor` / `UIDeclarativeDialogPanel` / `IUIActionRuntime` / `UIManagerActionRuntime`、PlayMode 红证、CHANGELOG 行为变更 | `PM(UIAuthoring)`；改前必红；`DOC` |
| 0.2 | `fix(ui-authoring): ShowDialog 取消不再吞成 OpenFailed (R5-UAU-002)` | `UIManagerActionRuntime`、取消用例、CHANGELOG | `PM(UIAuthoring)`；改前必红；`DOC` |

### 批次 1：P2 与证据型工程项

| # | 提交信息 | 覆盖 | 验证 |
|---|---|---|---|
| 1.1 | `fix(fmod): SetGlobalParameter 不再早退于 RuntimeManager 初始化前 (R5-FMD-001)` | `FmodEventBackend`、FMOD-free fake 不能覆盖真实 RuntimeManager，需集成态或探针 | `PM(Fmod)`；`DOC`；集成态标 `需实测` |
| 1.2 | `chore(tools): 扩展包 XML 文档探针 (R5-ENG-001)` | `check-docs.ps1` 或包内守卫；UIAuthoring/Luban/Fmod 三个 csproj | `DOC`；红证：注入一条 CS1591 必红 |
| 1.3 | `fix(luban): TextAsset 字节在句柄释放前复制 (R5-LBN-001)` | `AssetManagerLubanAssetSource`、映射用例 | `PM(Luban)`；`EM(Luban)`；改前必红；`DOC` |

### 批次 2：P3 代码 / 测试补齐

- 代表项：R5-UAU-003（Open/Push 拒绝 Popup）、R5-UAU-010（生产 runtime 测试缝）、R5-UAU-011（删未用构造参数）、R5-UAU-013（空参数发布结果）、R5-FMD-007（配置资产往返）、R5-UIE-001（missing binding 规则）、R5-LBN-002（死 cref）、R5-ENG-002/006（依赖反查/守卫射程）。
- 验证：`PM/EM/ALL`；改前必红；`DOC`。

### 批次 3：P3 文档 / 发布清扫

- 代表项：R5-UAU-004..009/012/014（包 README/XML/维护记录/规则号）；R5-FMD-002..006（README/FMOD.md/AudioProvider.md）；R5-AUD-002（探测型总表）；R5-LBN-003（tag 占位）；R5-ENG-003/004（根 README、Modules README）；R5-ENG-005（四包 tag/URL，用户动作）。
- 验证：`DOC` + 人工对照；发布动作单独走流程。

### 批次 4：裁定与探针

- 用户逐条裁定 §9 的 R5-ADJ-01..07，写入模块台账 / `Ledger.md`。
- 执行 §11 的 R5-PRB-01..08 中可在本环境运行的项；其余登记 `未做` + 配方。

## 11. 遗留承接与探针轨

### 11.1 前轮遗留（仍未执行）

| ID | 内容 | 状态 | 配方 |
|---|---|---|---|
| R3-PRB-02 | 真 YooAsset 低内存 `GetGlobal` 假 null | 未做 | 真包环境；`Modules/Config.md:98` |
| R3-PRB-03 | IL2CPP High stripping `[Preserve]` | 未做 | 真机构建；`Modules/Config.md:99` |
| R3-PRB-04 | macOS Server 构建 | 未做 | Server Build Support；`Modules/File.md:48` |
| R4-PRB-03 | 真 YooAsset「有效句柄 + 类型不符」RefCount | 未做 | `Modules/Config.md:101` |
| R4-PRB-09 | Linux Editor 大小写 / cwd | 未做 | `Documentation/Luban.md:526` |

### 11.2 R5 新增探针

| ID | 关联 | 内容 | 状态 |
|---|---|---|---|
| R5-PRB-01 | R5-AUD-001 | 关闭域重载连跑两遍 + `FmodAudioSettings`/bank：第二会话断言 FMOD provider 已重装、bank 可加载 | 待执行（手工；需 Studio 工程） |
| R5-PRB-02 | R5-FMD-002 | Android OBB / WebGL：`LoadBank` 返回后 `HaveAllBanksLoaded` 与首次 `Play` 的时序 | 待执行（需真机/平台构建） |
| R5-PRB-03 | R5-LBN-001 | `AutoUnloadBundleWhenUnused=true` 下 TextAsset 释放后读 `bytes` 的结果 | 待执行（YooAsset 初始化参数探针） |
| R5-PRB-04 | R5-UAU-001 | 控制器拦下 `CloseSelf`/`Replace`/`CloseWithResult` 的红证（批次 0 落成用例，不以探针结案） | 待执行（实施批次 0） |
| R5-PRB-05 | R5-UAU-002 | `ShowDialogAsync` 取消路径的 OCE 断言 | 待执行（实施批次 0） |
| R5-PRB-06 | R5-ENG-001 | 三个扩展包 DocumentationFile 探针纳入门禁后的红证 | 待执行（实施批次 1） |
| R5-PRB-07 | R5-UAU-006 | UIAuthoring IL2CPP/AOT（P6）与 TMP 自动化 | 待执行（环境项） |
| R5-PRB-08 | R5-FMD-006 | 有 FMOD SDK 的全量（Integration 7 例） | 待执行（2026-11-07 已有两态记录，本轮未复跑） |

## 12. 已评估未采纳 / 不重开清单

### 12.1 承接 R4 §12.1 的框架级条目

| 条目 | R4 状态 | R5 复核 | 结论 |
|---|---|---|---|
| Network 基础层 | 暂不做 | 仓内仍零消费者 | 维持 |
| 代码热更（HybridCLR） | 暂不做 | 仍无真实接入方；Luban 不是代码热更 | 维持 |
| GM / 调试命令台 | 暂不做 | 仍无使用方给出命令集/权限/发布形态 | 维持 |
| App 生命周期 | 暂不做 | `pause` 仍只有 Settings 一处消费者 | 维持 |
| 性能基准套件 | 已做 | 6 个 `[Explicit]` 基准仍在 | 维持 |
| 示例覆盖面 | 已做 | `Samples~/Playable` 仍在 | 维持 |
| 包自身 Git URL 说明 | 已做 | README 已说明 + 钉 0.4.0 示例 | 维持（发版时更新） |

### 12.2 承接 R4 §12.2 / §12.3

- 框架级结构性 `不做` 组（状态机/行为树/ECS/时间模型/生命周期树、Update 组 API、DI 容器、Tween、Addressables、新手引导、平台 SDK 聚合、网络同步、统一 Profile、模块内部策略接口化、远程配置、崩溃上报、红点、画质档位、埋点/统计上报、热更编排）——R5 未发现推翻证据，**不重开**。
- 带判据的 `不做`（CI、`Tests~`/移出包目录、包在子目录、Git 钩子/Issue·PR 模板/`CONTRIBUTING`）——判据未触发，维持。
- Luban §7.8 六条（不重建核心适配层、不公开 `ILubanDataProvider`、不做 bin-offset/分片、不支持三个 JSON target、不做 Roslyn generator、不移入核心 Runtime）——维持。
- UIAuthoring 设计文档的三条「不做」（可视化设计器、保存/构建门禁、向导/脚本/地址生成）——维持。

### 12.3 R5 新增否决 / 不重开

| 条目 | 状态 | 理由 | 重开判据 |
|---|---|---|---|
| 为扩展包新建 `Modules/<包>.md` | 暂不做（R4-ADJ-01 + Fmod 条目） | 维护记录在 `Documentation/Luban.md` / `FMOD.md` / `UIAuthoring.md`；新建会撞模块集 | 扩展包需要与核心模块同构的台账门禁时 |
| 把 FMOD SDK / manifest 条目提交入仓 | 不做 | 不可再分发；包 README 是版本真相 | 无 |
| 给 UIAuthoring 加保存/构建自动校验 | 暂不做 | 设计文档 §5.3 的代价与误报理由仍成立 | 团队要求「无效预制体不得入库/出包」且已有稳定 CI |

## 13. 台账同步索引

> 审计期**不写台账**；以下为实施轮（或用户确认后）的落点。R5 新发现修复落地后，才由实施提交记 `已做`。

### 13.1 裁定建议

| 建议 ID | 模块 | R5 建议 | 落点 |
|---|---|---|---|
| R5-ADJ-01 | UIAuthoring | `暂不做`（HideSelf 保留并文档化） | 包 README / XML / `Documentation/UIAuthoring.md` |
| R5-ADJ-02 | 工程面 | `未做`（扩展包 XML 探针） | `Tools/check-docs.ps1` / 包内守卫 |
| R5-ADJ-03 | 工程面 | `未做`（asmdef 反查，维持 R4） | `Documentation/Ledger.md` |
| R5-ADJ-04 | 发布 | `未做`（四包 tag/URL，用户动作） | `CHANGELOG.md` / `package.json` / tag |
| R5-ADJ-05 | Audio | `待裁`（会话复位族是否含 Audio） | `Modules/Audio.md` |
| R5-ADJ-06 | UIAuthoring | `待裁`（异地址返回语义） | 包 README / XML |
| R5-ADJ-07 | FMOD | `暂不做`（晚装自动安装） | 包 README / `Documentation/FMOD.md` |

### 13.2 R5 发现的台账落点

- **P1（R5-UAU-001）**：实施后记 `已做`，带提交号与 PlayMode 用例；落 `Documentation/UIAuthoring.md`（暂不在 `Modules/`）。
- **P2/P3**：修复落地前不记台账；落地后按模块记 `已做`（纯文档订正按需记）。落点：`Documentation/UIAuthoring.md`（UAU-*）、`Documentation/FMOD.md`（FMD-*，核心 Audio 的条目落 `Modules/Audio.md`）、`Documentation/Luban.md`（LBN-*）、`Modules/UI.md`（UIE-* 与核心 UI 的校验器边界）、`Modules/Audio.md`（AUD-*）、`Documentation/Ledger.md`（ENG-* 框架级）。
- **工程面**：R5-ENG-001..006 落 `Documentation/Workflow.md` / `ModuleReadme.md` / `Modules/README.md` / `Ledger.md`；是否需要台账条目按 §9 裁定。

### 13.3 框架级台账（`Documentation/Ledger.md`）

- 新增 `未做`：扩展包 XML 文档探针（R5-ADJ-02）；四包 tag/URL（R5-ADJ-04）。
- 维持 `未做`：依赖清单从 asmdef 反查（R5-ADJ-03，R4 遗留）。
- 新增 `待裁`：Audio 会话复位族（R5-ADJ-05）；UIAuthoring 异地址返回语义（R5-ADJ-06）。
- 新增 `暂不做`：UIAuthoring HideSelf（R5-ADJ-01）；FMOD 晚装自动安装（R5-ADJ-07）。
- §四复核记录追加：R4 探针轨状态 + R5 新探针指针。

### 13.4 探针轨

- R3-PRB-02/03/04、R4-PRB-03/09 维持 `未做` + 原配方。
- R5-PRB-01..08 在对应模块/维护文档登记 `未做`，配方见 §11；R5-PRB-04/05 在实施批次 0 落成用例后改 `已做`；R5-PRB-06 在批次 1 落成门禁后改 `已做`。
- 关闭域重载两遍仍是跨模块手工项（`Ledger.md` §四 / `Workflow.md` §六），R5 未复跑。

## 附录 A. 探针与命令

### A.1 基线与门禁（2026-11-10，边界 `b2bb5a8a`）

```bash
# 构建（幂等；--artifacts-path 避开 Unity 生成的 obj/bin）
dotnet build Venusy609.Xframework.csproj --artifacts-path "$env:TEMP\xfw-r5-artifacts" -v q --nologo
dotnet build Venusy609.Xframework.Editor.csproj --artifacts-path "$env:TEMP\xfw-r5-artifacts" -v q --nologo

# 全量测试（PlayMode + EditMode，含三个扩展包）
pwsh -File Tools/run-tests.ps1

# 文档门禁
pwsh -File Tools/check-docs.ps1 -Enforce
```

结果：

- BUILD：Runtime 0 error / 20 warning；Editor 0 error / 34 warning（告警全部来自 PackageCache；包内仅 `UIHudManager.cs:131` 的 CS0618，不在文档门禁统计面）。
- FULL：PlayMode 2405/2405、EditMode 749（743 通过 + 6 skip）、0 失败；合计 54.1s（PlayMode 32.7s + EditMode 20.9s）。
- DOC：门禁码 0 条；24 模块 / 535 条台账；README 文件树 24/24；三组示例编译通过。
- **B8 复跑（2026-11-10）**：FULL PlayMode 2405/2405、EditMode 749（743 通过 + 6 skip）、0 失败；DOC 再次通过（与基线一致）；报告自证 34 个 ID 去重 = 34 行附录 C。

### A.2 审计自证

```bash
# 发现 ID 去重数（应等于附录 C 行数）
grep -oE 'R5-[A-Z]+-[0-9]{3}' Documentation/AuditRound5.md | sort -u | wc -l
# 附录 C 行数
grep -cE '^\| R5-[A-Z]+-[0-9]{3} \|' Documentation/AuditRound5.md
# 严重度分布
grep -oE '^\| R5-[A-Z]+-[0-9]{3} \| P[0-3]' Documentation/AuditRound5.md | sort | uniq -c
```

## 附录 B. 待取证

- **R5 新增**：R5-PRB-01..08（§11.2）；优先级最高的是 R5-PRB-01（关闭域重载 + FMOD 第二会话）与 R5-PRB-04/05（UAU-001/002 的红证，实施批次 0 必做）。
- **前轮遗留**：R3-PRB-02（真 YooAsset 低内存 `GetGlobal` 假 null）、R3-PRB-03（IL2CPP High stripping）、R3-PRB-04（macOS Server）、R4-PRB-03（真 YooAsset 类型不符 RefCount）、R4-PRB-09（Linux 路径）。
- **环境项**：UIAuthoring IL2CPP/AOT（P6）、TMP 自动化、有 FMOD SDK 的全量（Integration 7 例）、Android OBB/WebGL bank 异步、真实 bank/event 冒烟。
- **手工项**：关闭域重载连跑两遍（`Workflow.md` §六）、真实 YooAsset 场景卸载序、真实 `PlayableDirector` 接管/释放。
- **无法在本环境验证**：真机性能、Console/平台存档 SDK、OpenUPM 上架。

## 附录 C. R5 发现 ID 索引

> 按 ID 排序；一句话为报告内标题/首句的压缩，完整证据见对应节。其余模块的行随 B3–B7 追加。

| ID | 级别 | 模块 | 一句话 |
|---|---|---|---|
| R5-FMD-001 | P2 | FMOD | `SetGlobalParameter` 在 RuntimeManager 未初始化时静默返回 false |
| R5-FMD-002 | P3 | FMOD | Android OBB / WebGL 下 bank 异步加载，README 称同步阻塞 |
| R5-FMD-003 | P3 | FMOD | `FMOD.md` 写「任意 Resources 目录」，实际只认根级路径 |
| R5-FMD-004 | P3 | FMOD | `AudioProvider.md` 引用不存在的 fixture 名 |
| R5-FMD-005 | P3 | FMOD | README 手动定义 `XFRAMEWORK_FMOD` 未写 FMOD 必须已安装 |
| R5-FMD-006 | P3 | FMOD | Integration 程序集与 7 个集成用例默认不编译/不跑 |
| R5-FMD-007 | P3 | FMOD | `FmodAudioSettings` 配置资产无序列化往返用例 |
| R5-UAU-001 | P1 | UIAuthoring | 控制器拦下关闭后动作会话仍失效、`CloseWithResult` 残留待兑现结果 |
| R5-UAU-002 | P2 | UIAuthoring | `ShowDialog` 的取消被吞成 `OpenFailed` + Error 日志 |
| R5-UAU-003 | P3 | UIAuthoring | `Open`/`Push` 校验接受声明式 Popup 作目标 |
| R5-UAU-004 | P3 | UIAuthoring | `UIActionKind.HideSelf` 三条路径均不可达 |
| R5-UAU-005 | P3 | UIAuthoring | `ButtonNodeName` 文档说「或根上」，代码只查节点自身 |
| R5-UAU-006 | P3 | UIAuthoring | 8 个公开/受保护成员缺 XML 注释（CS1591） |
| R5-UAU-007 | P3 | UIAuthoring | 缺本地化 key 复用 `UI-AUTH-010` 规则号 |
| R5-UAU-008 | P3 | UIAuthoring | package.json / README 的 Kind 列表漏 Popup |
| R5-UAU-009 | P3 | UIAuthoring | 维护向文档的 ParentClosed / Variant / 8.6 / HUD 动作漂移 |
| R5-UAU-010 | P3 | UIAuthoring | 生产 `UIManagerActionRuntime` 零直接测试 |
| R5-UAU-011 | P3 | UIAuthoring | `UIActionExecutor` 构造参数 `definition` 未使用 |
| R5-UAU-012 | P3 | UIAuthoring | 异地址拒绝返回已存在面板，调用方无法区分 |
| R5-UAU-013 | P3 | UIAuthoring | `ShowDialogAsync` 空参数不发布结果消息 |
| R5-UAU-014 | P3 | UIAuthoring | `actionId` 是全局键空间，未写唯一性约定 |
| R5-AUD-001 | P3 | Audio | 跨会话静态态只靠 `Application.quitting`，FMOD 自动安装可能第二会话不重装 |
| R5-AUD-002 | P3 | Audio | 探测型总表未写「加载窗口内也返回 false」 |
| R5-UIE-001 | P3 | UI 校验器 | 面板无 `UIPanelBinding` 时约定检查静默跳过、不报缺失 |
| R5-UIE-002 | P3 | UI 校验器 | 校验窗口/报告无自动化用例 |
| R5-LBN-001 | P3 | Luban | TextAsset 句柄先释放、后读 `bytes` |
| R5-LBN-002 | P3 | Luban | `AssetLubanDataProvider` 的 `AssetManager` cref 无法解析（CS1574） |
| R5-LBN-003 | P3 | Luban | README 安装 URL 仍为 `#<tag>` 占位、包无 tag |
| R5-ENG-001 | P2 | 工程面 | 扩展包 XML 文档告警实测非零且无门禁（UIAuthoring 8 / Luban 1） |
| R5-ENG-002 | P3 | 工程面 | 核心 Editor asmdef 新引用 YooAsset，依赖反查仍缺 |
| R5-ENG-003 | P3 | 工程面 | 根 README 的 UIAuthoring 小节仍写「阶段 2a」 |
| R5-ENG-004 | P3 | 工程面 | `Modules/README.md` 可选扩展包缺 UIAuthoring 指针 |
| R5-ENG-005 | P3 | 发布 | 四个包均无发布 tag，`[Unreleased]` 均有未发内容 |
| R5-ENG-006 | P3 | 工程面 | 扩展包依赖守卫不覆盖 Editor asmdef |
