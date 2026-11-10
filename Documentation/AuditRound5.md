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

## 2. 执行进度

> 状态词：`待做` / `进行中` / `已完成`。每步一个原子提交（`docs(audit): ...`）；提交前跑本步验证。§2 的「总览统计」在 B7 收口时补到本表上方；本表保留为过程记录。

| # | 批次 | 状态 | 产出节 | 备注 |
|---|---|---|---|---|
| 0 | 生成审计文档骨架与登记 | 已完成 | 全文骨架 | `CLAUDE.md` 文档分层表 + `ModuleAudit.md` 轮次列表已同步 |
| 1 | B0 边界与基线 | 已完成 | §0、附录 A | BUILD 0 error；PLAY 2405/2405、EDIT 743 通过 + 6 skip；DOC 通过（535 条台账） |
| 2 | B1 R4 修复复核 | 已完成 | §3 | 3 P1 逐条读 diff + 用例；51 P2 / 17 P3 聚合 + 抽样；11 ADJ、18 PRB 对台账；未发现二次回归 |
| 3 | B2 FMOD 包全量 | 已完成 | §6、§8、附录 C | 首次审计；1 P2 + 6 P3；FMOD-free XML 告警探针 0 条；Integration 默认不编译，已登记 |
| 4 | B3 UIAuthoring 包全量 | 已完成 | §6、§8、附录 C | 首次审计；2 P2 + 12 P3；XML 文档探针实测 8 条 CS1591；控制器拦截路径零覆盖 |
| 5 | B4 核心新面（Audio / UI 校验器） | 待做 | §6、§8、附录 C | |
| 6 | B5 核心增量（T2 / T3 / Luban 复审） | 待做 | §6、§8、附录 C | |
| 7 | B6 工程面与发布 | 待做 | §6、§7 | |
| 8 | B7 报告收口 | 待做 | §2 总览、§4–§13、附录 B/C | |
| 9 | B8 收尾验证 | 待做 | 附录 A | FULL + DOC + 自证 |

## 3. 前轮修复复核（R4）

> 复核口径：每条落到「代码 / 测试 / 文档」三处证据之一；提交信息只是索引，不作为修复证明。本轮对 P1 逐条读 diff 与用例；P2/P3 按模块聚合复核 + 代码类抽样（抽样名单见各模块条目）；ADJ 逐条对台账与代码；PRB 对台账与配方。

### 3.1 R4 P1（3 条）

| R4 ID | 修复提交 | 本轮证据 | 结论 |
|---|---|---|---|
| R4-UIA-001 | `616d64d8` | `UIViewBase` 新增 `IsRecycled` / `RecycleGeneration` / `MarkRecycled` / `MarkReopened`（`UIViewBase.cs:48-70`）；`UIManagerImpl.OpenCoreAsync` 三处「世代变化即放弃」守卫（`UIManagerImpl.cs:637-760`），`SettleOpening` 改按 entry 身份摘账（`:1928-1937`）；回归 `UIPanelConcurrencyTests.Dispose_DuringInFlightOpen_DoesNotCallOnCloseAfterRecycle` / `..._ReinitializedPanelStaysOpen`（改前双红） | **已修复** |
| R4-UIB-001 | `28116446` | `PruneOverlayHolds` 改为 `!ReferenceEquals(owner, null) && owner == null`（`UIManagerImpl.cs:2284-2299`）；回归 `UIMaskTests.ShowMask_WithoutOwner_SurvivesUnrelatedPanelDestroyed` | **已修复** |
| R4-UIB-002 | `5559dc2b` | `UIHudManager` 新增按 target 实例 ID 的单槽在途表 `_inFlightAttach`、`AttachCoreAsync` 注册前复检、`SettleAttach` 身份摘账、`MarkReopened` 复位认领标记（`UIHudManager.cs:75-84,150-300`）；回归 `UIHudManagerTests.ShowHudAsync_SameTargetConcurrent_InstantiatesOnce` / `CloseAllAsync_AfterConcurrentAttach_LeavesNoActiveHud`（改前 2 次实例化 / 残留 1 个） | **已修复** |

### 3.2 R4 P2（51 条）聚合复核

> 按模块列出处置证据与抽样复核结果。全部 51 条均落到代码/测试/文档之一，**未发现完全未处置项**。

- **UI（10 条）**：UIA-002（`OnPoolRecycle` try/catch + `UIPanelPoolLifecycleTests.CloseAsync_OnPoolRecycleThrows_StillPublishesClosedAndCallsAfterClose` / `UIHudManagerTests.ShowHudAsync_OnOpenAndPoolRecycleThrow_PreservesOnOpenException`）、UIA-003/UIA-004/UIB-006/UIB-007（README 四处：`SetInstance` 重接、`MaxIndexInLayer = 30`、`BindToLocalizedText` 释放口径、§11 围栏）、UIA-005（`ApplyLayerInteractivityToContainer` + `UILayerApiTests.SetLayerInteractive_BeforeContainerExists_AppliesToNewContainer`）、UIA-006（`PopTo`/`PopToRoot` 假 OnResume + Dispose provider 隔离，`e33a6d84`）、UIB-003（控制器全关回调门控 + `UIPanelSelfCloseTests.CloseAllAsync_NoPanels_*` / `..._ControllerBlocksClose_*`）、UIB-004（换 provider 异常隔离 + `UIAsyncTokenTests` 三例）、UIB-005（`img_` 类型不匹配 `#if UNITY_EDITOR` 告警 + `UIPanelBindingConventionTests.ImgNodeWithUnsupportedSourceType_Warns`）。**抽样读 diff：UIA-001/002/005、UIB-002/003/004/005，均与报告承诺一致。**
- **Config（6 条）**：CONF-001（null 校验前移到在途分支之前 + `ConfigManagerImplTests.PreloadTableAsync_NullLoaderWhileTableInFlight_ThrowsConfigException` / `...Global...`）、CONF-002（`ConfigAssetLease` + `ConfigAssetLoaderTests.LoadTextAsync_TypeMismatch_ReleasesLeaseOnceAndExplains` / `LoadTableAsync_...`）、CONF-003（`ScriptableObjectAssetRoundTripTests` 真实 `CreateAsset/SaveAssets/LoadAssetAtPath`）、CONF-004（`ConfigTable` XML 写明键==Id）、CONF-005（在途登记按 `(Type, Kind)` 分槽 + `PreloadTableAsync_ThirdCallerJoinsAfterCrossIdentityInFlight_LoadsOnce`）、CONF-006（在途告警比较 loader 实例 + `PreloadTableAsync_SameTypeDifferentLoaderInstance_WarnsAndSharesFirst`）。
- **Asset（7 条）**：AST-001（`SetInstance` 处置旧实例/退订 + `AssetManagerFacadeTests.SetInstance_AfterFrameworkInitialize_DisposesPreviousInstance` 等三例）、AST-002（`AssetBootstrapStage` 按实例身份清理 + `BootstrapStageTests`）、AST-003（`YooAssetPackageStateTests` 两例 + 三入口守卫）、AST-004（日志/README 改「回池等待复用」）、AST-005（下载最终进度走隔离）、AST-006（`SessionResetTests` 在途 impl 退订）、AST-007（README `Destroy()` 文案与交叉引用）。
- **Camera（5 条）**：CAM-001（README 写明同一目标中途 cut 的例外 + `CinemachineCameraEventTests.SameTargetCut_DuringBlend_PublishesDeactivatedAndCompletedWithoutActivated`）、CAM-002（`Activate_WhileExternalOverrideMakesTargetActive_TakesBoostOwnership`）、CAM-003/004（`Subscribe` 令牌说法与重载示例文档）、CAM-005（`DestroyedRegisteredActiveCamera_DeactivatedCarriesItsId`）。
- **Diagnostics（2 条）**：DIAG-001（`Register_OrderGetterThrows_StillRegistersAtEnd` / `..._ExistingPanelOrderThrows_...`）、DIAG-002（`Table_EmptyTrailingColumn_HeaderAndSeparatorAgree`）。
- **Save（2 条）**：SAVE-001（`Modules/Save.md:41` 去掉会漂移的计数）、SAVE-002（`SaveManagerImplTests.DeleteSlotAsync_BackupOnly_DeletesAndDoesNotRecover` / `SlotExistsAsync_BackupOnly_ReturnsTrue`）。
- **Input（1 条）**：INP-001（`InputSystemProviderTests.GetButtonPressDuration_IsPerPlayer` + 预热用例）。
- **Settings（1 条）**：SET-001（`EncryptedSettingsStoreTests.LoadAsync_SyncInner_DecryptsOnPoolAndParsesOnMainThread`）。
- **Scene（1 条）**：SCN-001（`SceneLoadTests.LoadSceneAsync_ActivateWithInvalidScene_WarnsWithLocationAndReturnsHandle`）。
- **Localization（1 条）**：LOC-001（`LocalizationManagerTests.GetFormat_PlaceholderValueWithCompositeItem_RendersLiterally`）。
- **Update（1 条）**：UPD-001（`Runtime/Update/README.md:327` 与 `UpdateManager.cs:371` 已改为「`Initialize` / `SetInstance` 都会重接」）。
- **Pipeline（1 条）**：PIPE-001（`SequenceStageTests.Execute_Success_WritesCompletedTerminal`，容器在 finally 前写 Completed/1f）。
- **Pool（1 条）**：POOL-001（`PoolTests.Return_WhenPoolFull_DiscardCallbackThrows_IsIsolated` + `Get_RentCallbackThrows_InvokesDiscardCallback`）。
- **File（1 条）**：FILE-001（`IFileProvider.cs:19` 改为「null/空 = 域根」）。
- **Serialize（1 条）**：SER-001（`Runtime/Serialize/README.md:74,86` 写明 `ResetForNewSession` 会清掉更早注册）。
- **Luban（10 条 P2）**：LBN-002（`LubanAssetProviderTests` 成功映射九例）、LBN-003/015（`LubanProcessRunnerTests` 成功/非零/超时/大输出/中文 UTF-8）、LBN-004（`LubanIntegrationGeneratorTests.Generate_WriteTrue_EmitsAllFilesAndManifest`）、LBN-005（`LubanBootstrapStageTests` 加入在途所有权）、LBN-006（`LubanConfigTests` provider 级错误带表名/location）、LBN-008（`LubanIntegrationWriterTests` 目录拒绝 + manifest 回滚，writer 改为 staging→backup→commit→rollback）、LBN-010（`LubanConfigTests.InitializeAsync_JoiningDifferentAdapter_Warns`）、LBN-013（`LubanEditorPublicApiBaseline.txt` + fixture）、LBN-001/007/011/012/016（包 README 收窄措辞/补类型/零拷贝说明）。
- **工程面（4 条 P2）**：ENG-001（基线注释不再写固定数）、ENG-002（`Workflow.md:108-111` 四个更新开关齐备）、ENG-003（`ModuleReadme.md` 文件树门禁描述已改双向）、ENG-009（`CHANGELOG.md:408` 行为变更表收录 `MaxIndexInLayer` 31→30）。

### 3.3 R4 P3（17 条）聚合复核

17 条均已处置：AST-004/005/006/007 与 CONF-004/006、LBN-015/016/017、LOC-001、PIPE-001、POOL-001、SER-001、UIA-006 有对应代码/测试（见 §3.2 同 ID）；ENG-007 维持「示例只守编译」（`Workflow.md` 已写明）、ENG-008 由 R4-ADJ-01 记为 `暂不做`（`Modules/README.md:83`）。

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

（待填）

## 5. R5 P1 清单

（待填）

## 6. R5 P2 清单（按模块）

> 一条一行；`[回归]` 表示由前轮修复引入，`[跨模块]` 表示归拥有方立条，`[需实测]` 表示需运行期/真机证据。P3 也列在各模块下，便于下一轮按模块取用。

### UIAuthoring（扩展包，首次审计）

- **R5-UAU-001 · P2 · B1/B2/B6/D4**：`Assets/XFramework.UIAuthoring/Runtime/UIActionExecutor.cs:72-85` 与 `UIDeclarativeDialogPanel.cs:44-49,83-94` —— 控制器拦下关闭时（核心 `ClosePanelInternalAsync` 返回 `false`，`UIManagerImpl.cs:2058-2064`；`UIManager.CloseAsync` 返回 `UniTask` 不暴露结果，`UIManager.cs:393-399`），`CloseSelf` / `Replace` 的执行器仍无条件 `_session.Invalidate()`，面板实际没关但动作会话失效——面板继续显示，其按钮全部变成空操作。`CloseWithResultAsync` 还会先把 `_pendingResultId` 写入、关闭被拦后不清：面板仍在，之后被 `CloseAllAsync` / 父面板 / Dispose 回池关闭时，`OnClose` 会把过期结果兑现给核心结果通道；Dispose 回池不复位该字段，池化实例下一次复用时仍带着上一代的待兑现结果。现有用例只覆盖成功路径（`UIActionExecutorTests.Replace_UsesRuntimeReplace` 断言 `session.IsValid == false`），无「控制器拦下」用例。建议：`IUIActionRuntime.CloseAsync` / `ReplaceDeclarativeAsync` 返回是否真的关闭（或执行器在 await 后查 `Panel.IsOpen`），仅在真正关闭时失效会话；`_pendingResultId` 在 `OnOpen` 与关闭失败分支清空。验证：PlayMode 用拦下关闭的 Fake controller 试点 `CloseSelf` / `CloseWithResult` / `Replace`，改前必红。
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

（待 B6/B7 填充）

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

### UIAuthoring（T1 扩展包全量，首次）

- **范围与输入**：`Assets/XFramework.UIAuthoring/` Runtime 31 个 `.cs`、Editor 7 个、Tests 25 文件 / 124 例（63 Runtime + 12 Architecture + 49 Editor）、7 个 Architecture 守卫 fixture、`Samples~/Templates`（Panel / Dialog / Hud / Popup 普通 Prefab）与 `Samples~/DeclarativeDemo`、README / CHANGELOG / package.json、`Documentation/UIAuthoring.md`；核心 UI 接缝（`UIPanelBase` / `UIDialogPanel<string>` / `UIHudItem` / `UIManager.ShowPopupAsync` / `UIBinder`）。
- **18 类留痕**：A1 命中（Kind 列表、`ButtonNodeName` 文档、缺本地化 key 的规则号、维护向记录漂移，R5-UAU-005/007/008/009）；A2 命中（`Open`/`Push` 目标接受 `UIDeclarativePopupPanel`，R5-UAU-003）；A3 命中（8 条 CS1591 实测，R5-UAU-006）；B1 命中（控制器拦下关闭后会话仍失效，R5-UAU-001）；B2 命中（`_pendingResultId` 跨 Dispose 回池存活，R5-UAU-001）；B3 命中（`ShowDialog` 取消被吞，R5-UAU-002）；B4 **命中-已文档化**（全部主线程、代码零断言；核心 UI 同样无断言，列为残留不重开）；B5 扫过（注册表按项目注销、面板级订阅由 `Track` 释放）；B6 扫过（`UIDataProviderRegistry.ResetForNewSession` 在 `SubsystemRegistration`，守卫登记）；C1 扫过（绑定只在 OnOpen，值变化走强类型回调；`Format` 每次变化一次字符串已写入「性能代价」）；C2 扫过（YooAsset 构建图与模板导入探针已做）；D1 扫过（只用核心公开面；Editor asmdef 新增 `YooAsset` / `YooAsset.Editor` 引用，依赖声明守卫只查 Runtime asmdef）；D2 不适用（无重载）；D3 命中（生产 `UIManagerActionRuntime` 零直接测试、拦截路径零覆盖，R5-UAU-010）；D4 命中（`ShowDialogAsync` 空参数不发布结果、异地址返回非 null，R5-UAU-012/013）；D5 扫过（核心已给 `OpenAsync` / `ShowPopupAsync` / `ShowDialogAsync` / HUD 原语，无缺失）；E 命中（`actionId` 全局键空间，R5-UAU-014）；F 命中（异地址拒绝时返回已存在面板，R5-UAU-012）。
- **前轮修复偏差**：R4 未覆盖本包（阶段 2a/2b/3/4 均在 R4 之后）；2026-11-08 的设计审计（`Documentation/UIAuthoring.md` §1.5）已修的事实与引用错误本轮复核维持；实现期的 `9981bf5c`（扫描器按根组件读定义）有代码 + 用例证据。
- **旧结论复核**：设计文档的三条裁定（单根、只重开模板、HUD/Dialog 纳入）与三条「不做」（可视化设计器、保存/构建门禁、向导/脚本/地址生成）全部维持；SO 定义资产、keyed 消息、独立 Popup 具体类型的取舍与实现一致。
- **开口项**：R5-UAU-001/002 建议进修复批次；R5-UAU-003..014 为文档/测试/规则号补齐。
- **第三方对照**：FairyGUI / UI Toolkit 的结论沿用 `Documentation/UIAuthoring.md` §6.6 与 `FairyGUI.md`，本轮未重做；本轮新增的对照点是「声明式制作层与核心具体类型注册的耦合」——单根限制来自核心按具体类型登记，Popup 用独立类型绕开，与设计文档一致。
- **残留与不确定**：P6 IL2CPP/AOT（`IUIDataProvider.TryGet<T>` 泛型实例化）未验证；TMP 只有一次性探针、自动化仍只测 UGUI `Text`；真实核心 `UIManager` 集成仍以 fake 为主；`Samples~` 的 Prefab/定义资产未做本轮导入回归；包无发布 tag（0.1.0，阶段 2b/3/4 在 `[Unreleased]`）。

（其余对象待 B4–B6 填充：Luban、Audio、UI 校验器、15 个 T2 核心模块、8 个 T3 核心模块、工程面。）

## 9. 待裁的一次性裁定建议

（待 B7 填充）

## 10. R5 分步实施计划

（待 B7 填充）

## 11. 遗留承接与探针轨

（待 B7 填充：R3/R4 遗留探针 + R5 新增探针）

## 12. 已评估未采纳 / 不重开清单

（待 B7 填充：R4 §12 的维持复核 + R5 新增否决项）

## 13. 台账同步索引

（待 B7 填充）

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

（待 B7 填充）

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
| R5-UAU-001 | P2 | UIAuthoring | 控制器拦下关闭后动作会话仍失效、`CloseWithResult` 残留待兑现结果 |
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
