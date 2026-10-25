# 第四轮全面审计（2026-10-25）

> **点快照**：本文件记录第四轮全面审计在边界提交 `d29f4f50` 上的发现、前轮修复复核、裁定建议与分步实施计划。所有 `file:line` 以该提交为准，不承诺长期有效。
> **收件人**：维护者（执行本轮修复时）+ 下一轮审计者。长期结论按收件人分流到模块 README 与决策台账，本文件不复制它们，只做发现与索引（见 §13）。
> **审计轮只出清单，不动代码**。本文件是审计产出；修复实施是后续独立阶段。
> 判据与探针定义见 `Documentation/ModuleAudit.md`；本文件不重复 18 类判据的全文。

## 0. 元信息与边界

- 边界 SHA：`d29f4f50a9181346e7278196c7b75ead82f19ae9`（审计开始时工作区干净）。
- 上一轮边界：`661a7609b292a59b4a07b194712c098ab9c811e2`（R3）。
- 区间变更：`661a760..d29f4f50` 共 192 个提交；核心包 259 文件（+12,866 / -2,892），新增独立扩展包 `Assets/XFramework.Luban/` 233 文件（+11,820 / -0）；`Assets/XFramework.Cinemachine/` 删除，Cinemachine 后端并入 `Runtime/Camera/Cinemachine/`。
- 深度分档：T1 全量 11 个核心模块 + Luban 扩展包全量；T2 增量 10 个核心模块；T3 紧凑 3 个核心模块；工程面专项 1 个跨切单元。24 个核心模块全部留 18 类判据痕。
- 基线门禁（2026-10-25）：BUILD 两个核心 csproj 0 error（19 条告警全部来自 PackageCache 的 Input System / Cinemachine）；FULL PlayMode 2106/2106、EditMode 628/628（6 skip）、0 失败；`check-docs.ps1 -Enforce` 通过（24 模块 / 463 条台账 / 文件树一致 / 双示例编译通过）；`PublicApiBaselineTests` 1/1（含在全量内）。
- 证据等级：`实测` = 本轮实际跑过门禁/探针/读第三方源码可证；`推理` = 调用链/静态状态可证但未运行；`需实测` = 运行期或真机才能确认，已给命令。
- 路径约定：未特别说明时，`Runtime/...` / `Editor/...` 相对 `Assets/XFramework/`；Luban 的路径显式以 `Assets/XFramework.Luban/` 开头；`Tests/...` 相对 `Assets/XFramework/Tests/`（Luban 测试除外，显式带包前缀）。
- 台账计数：`check-docs.ps1 -Enforce` 口径为 463 条（模块 405 = 已做 157 / 未做 43 / 待裁 2 / 暂不做 40 / 不做 152 / 已推翻 11，加 `Ledger.md` §三 58 条）；R3 冻结值为 370，增长来自 R3 实施轮与 Luban/相机新增条目。
- 本轮**未做**：未修改任何 `Assets/**` 源码、测试、包 README、CHANGELOG；未执行需真机/跨播放会话的手工验收；未修改 `AuditRound2.md` / `AuditRound3.md`。

## 1. 范围与方法

### 1.1 分层表

| 层级 | 模块 | 深度 |
|---|---|---|
| T1 全量重审（11） | Camera、UI、Config、Asset、Input、Localization、Audio、Save、Settings、Bootstrap、Timer | 读全部生产代码、README、维护向记录、测试、接缝；18 类逐类留痕；R3 本模块 P0/P1/P2 全部复核；新发现 |
| T1 扩展包全量（1） | Luban（`Assets/XFramework.Luban/`） | 首次全面审计：Runtime/Editor/Tests/Samples/包文档/六类守卫；LUB-01..28 复核；跨包契约；门禁射程 |
| T2 增量复审（10） | Update、Message、Data、Pipeline、Pool、Diagnostics、File、Log、Serialize、Scene | 读变更文件全文 + 上下文；18 类逐类留痕；R3 开口项复核；P2 状态复核 + 代码类抽样；新发现 |
| T3 紧凑留痕（3） | Event、Lock、Reactive | 读变更文件全文；18 类至少一行；R3 开口项复核；新发现 |
| 工程面专项（1） | Tools 三脚本、Architecture 守卫、测试壳/基准、Samples~ 四组、两个 package.json、.gitattributes/LICENSE/.meta、包 README 四表、CHANGELOG 迁移表 | 门禁射程、测试真实性、示例、包元数据、0.4.0 发布就绪 |

### 1.2 方法

- 每个模块读全部/变更源文件、模块 README、维护向记录、CHANGELOG 与测试；按 `ModuleAudit.md` 的 18 类判据逐类留痕：命中写发现 ID，未命中写「扫过」，不适用写「不适用 + 理由」。
- R3 修复复核：1 条 P0 + 7 条 P1 逐条映射 `R3-ID → 修复提交 → 测试/文档 → 结论`；131 条 P2 按模块聚合 + 代码类抽样。**不以提交信息缺席推定未实施，也不以 CHANGELOG 声称推定已修**——每条落到代码/测试/文档三处证据之一。
- Luban 前轮复核：`Documentation/Luban.md` §7.2–7.4 的 LUB-01..28 逐条对扩展包 CHANGELOG 与代码/测试。
- 第三方对照按模块选取；无明确对标物时写「本轮未做」。
- 统计与自证命令见附录 A。

## 2. 总览

> 快照统计（只作导航；不随开发增长更新）：**0 条 P0、3 条 P1、51 条 P2、17 条 P3**，共 71 条新发现；R3 复核 139/139 有处置；Luban LUB-01..28 中 26 已修、1 部分、1 开放。R3 实施修复引入 1 条代码回归（R4-CONF-001）与 2 条文档反向回归（R4-UIA-003、R4-UPD-001）；Luban 修复残留 1 条（R4-LBN-005）。

### 2.1 最需要先读的 10 条

1. **R4-UIA-001（P1）**：`Dispose` 与在途 `OpenAsync` 交叠时，续体在 `OnPoolRecycle` 之后对已回池实例执行 `OnClose`，可跨代关闭复用实例。
2. **R4-UIB-001（P1）**：`ShowMask()`（默认 `owner = null`）的遮罩会在任意面板被外部销毁触发剪枝时被当成「owner 已销毁」清掉，模态输入保护静默失效。
3. **R4-UIB-002（P1）**：同一 target 并发 `ShowHudAsync` 产生不在 `_hudMap` 里的孤儿 HUD，`DetachAll`/`CloseAllAsync` 清不掉。
4. **R4-CONF-001（P2，回归）**：`PreloadTableAsync` 的在途分支先 `loader.GetType()` 后校验 null；第二调用传 null loader 得到 NRE 而非 `ConfigException`。
5. **R4-CONF-002（P2）**：Json/SO loader 在「资源类型不符」时抛异常前不 `Dispose` 有效句柄，provider/bundle 永久驻留。
6. **R4-AST-001（P2）**：`AssetManager.SetInstance` 不处置旧 `AssetManagerImpl`，旧实例被 `lowMemory`/`sceneUnloaded` 静态事件钉住，低内存时仍清共享包。
7. **R4-AST-003（P2）**：三个热更 API 对「包存在但初始化失败」的缓存条目没有守卫；Editor 抛上游裸 `Exception`，Player 可能 NRE。
8. **R4-LBN-005（P2）**：`LubanBootstrapStage` 在「加入在途初始化」时也把自己记为所有者，`Shutdown` 会销毁别的调用方初始化的状态。
9. **R4-LBN-002（P2）**：LUB-04 只补了默认加载后端的错误路径；`AssetManagerLubanAssetSource` 的 TextAsset/RawFile 成功映射仍零直接覆盖。
10. **R4-ENG-009（P2）**：`UISorting.MaxIndexInLayer` 31→30 是公开常量值变更，未进「行为变更（编译器抓不到）」表，0.4.0 升级者按该表核对会漏。

> 另有 **R4-SAVE-002**（只剩 `.bak` 的槽位单槽删除返回 false、下次恢复扫描复活）、**R4-AUD-001**（外部销毁音频宿主后坏源永久毒化播放）、**R4-INP-001**（`playerId` 时长无效）、**R4-SET-001**（加密读侧池线程 `JsonUtility`）四条新 P2，以及 R4-CAM-001/002、R4-CONF-003 三条值得优先取证。

### 2.2 P1 分布（按模块）

| 模块 | P1 |
|---|---|
| UI | R4-UIA-001（Dispose × 在途打开）、R4-UIB-001（null owner 遮罩）、R4-UIB-002（HUD 并发附着） |

> 其余 23 个核心模块与 Luban 本轮未发现 P1；不表示无缺陷，P2/P3 见 §6。

### 2.3 跨模块主题索引

- §7.1 前轮修复的文档-代码同步（第二轮）
- §7.2 相机重设计后的公开面、事件顺序与序列化兼容
- §7.3 Luban 分包与核心门禁射程
- §7.4 0.4.0 发布就绪
- §7.5 依赖声明与安装真相
- §7.6 会话复位族的第三层
- §7.7 测试真实性与新面覆盖
- §7.8 修复引入的回归与行为变更
- §7.9 台账、计数与文档状态

### 2.4 R3 修复复核摘要

- 1 条 P0 + 7 条 P1 **全部关闭**（§3.1），无 P0/P1 回归。
- 131 条 P2 **139/139 有处置**：132 条在 CHANGELOG 有条目，7 条由模块 README/源码或 ADJ 条目覆盖（§3.2）；未发现完全未处置项。
- 14 条 R3-ADJ 全部落盘，1 条仍 `未做`（R3-ADJ-08 Save 线程双路径）。
- **1 条代码修复回归**：R3-CONF-003 的在途告警拼接 `loader.GetType()` 早于 null 校验（R4-CONF-001）。
- **2 条文档反向回归**：R3-UIA-010 的代码已改为 `SetInstance` 重接驱动器，UI README 两处仍写「不重接」（R4-UIA-003）；R3-UPD-001 的修复把 Update README/XML 写成「SetInstance 不重接」（R4-UPD-001）。
- Luban：LUB-01..28 中 26 已修、LUB-04 部分（生产默认后端成功路径零覆盖）、LUB-26 开放（tag 是用户动作）。

### 2.5 本轮明确未做

- 未修改任何 `Assets/**` 源码、测试、包 README、CHANGELOG（审计轮只出清单；本报告与登记更新均只动 `Documentation/` 与 `CLAUDE.md`）。
- 未新增/修改测试代码；门禁快照为现有全量（PlayMode 2106 / EditMode 634）。
- 未执行需真机 / IL2CPP / 跨播放会话 / 手工编辑器设置的探针（只登记配方）。
- T2/T3 模块以「变更文件 + R3 相关代码」为主，未逐行重读全部历史代码；T1 与 Luban 为全量。
- 未重新论证 R3 §12 的 `不做/暂不做` 条目（除有新证据，见 §12）。

## 3. 前轮修复复核

### 3.1 R3 P0 + P1（1 + 7 条）

> 复核口径：每条落到「代码 / 测试 / 文档」三处证据之一；提交信息只是索引，不作为修复证明。

| R3 ID | 级别 | 模块 | R3 问题（简） | 本轮证据 | 结论 |
|---|---|---|---|---|---|
| R3-CONF-001 | P0 | Config | 行类型示例只写裸字段，不实现 `IConfigRow<TKey>.Id`，照抄 CS0535 | 三处示例均为「字段 + `int IConfigRow<int>.Id => Id;`」（`Runtime/Config/README.md:27-32,311-316,585-587`）；`ScriptableObjectTableTests` 与 `ExplicitInterfaceRowTests` 在位；CHANGELOG `:1194` 记录根因 | **已修复** |
| R3-AST-001 | P1 | Asset | `AssetPlayMode` 枚举重排与 0.3.0 已序列化值冲突 | `AssetInitOptions.cs:16/21/26` 为 `EditorSimulate=0/Offline=1/Host=2`；2026-10-19 用户确认 0.3.0 **从未分发**；CHANGELOG `:357-360` 行为变更表写明「反序列化后需重新选择运行模式」 | **已修复（文档化）** |
| R3-INP-001 | P1 | Input | 包 README / 安装器缺 Input System，按文档安装编译失败 | 包 README 依赖表新增 Input System 1.19.0（`:328`）与 Cinemachine 3.1.7（`:329`）；安装器 `RequiredUPMDependencies` 同步（`XFrameworkDependencyInstaller.cs:47,51`）；manifest JSON 同步（README `:345`） | **已修复** |
| R3-CONF-002 | P1 | Config | ScriptableObject Table 必须继承公开基类，文档零提及 | README `:544-557` 给出最小子类与创建步骤；`ScriptableObjectTableTests` 覆盖正确/错误形状、空列表与重复主键；CHANGELOG `:1200` | **已修复** |
| R3-AUD-001 | P1 | Audio | README 的「用项目自己的 AudioMixer」配方不可实现 | README `:304-305` 与 `:396-400` 已改为「当前不支持 / 需整体替换 `IAudioManager`」，删除不可实现配方 | **已修复（文档化）** |
| R3-TMR-001 | P1 | Timer | 跨表新建定时器不进入本趟 `min`，可晚约 2.13s | `TimerCore.cs:335-350` 全部表扫完后按 `firedAny` 统一补扫 `GetBornThisPassMin`；`TimerTierTests.cs:100-131` 跨表红证（改前必红） | **已修复** |
| R3-MSG-003 | P1 | Message | `IDestroyCancellationToken` XML 示例缺 `IMessageSubscriber`，照抄 CS1929 | `IDestroyCancellationToken.cs:17` 类声明已为 `: IMessageSubscriber, IDestroyCancellationToken`；`:35-36` 注释说明原因 | **已修复** |
| R3-CIN-001 | P1 | Camera | `Activate` 的强制完成混合 hack 不重启混合 | `CinemachineCameraBackend.cs:327/426/637` 改用上游 `Brain.ActiveBlend = null`；`CinemachineCameraBackendTests.cs:211` `Activate_DuringBlendToSameTarget_ForcesCutImmediately` 在位；CHANGELOG `:1073` | **已修复** |

> 8 条全部关闭，无 P0/P1 回归。R3-CIN-001 的「需实测」已由 PlayMode 用例转为实测。

### 3.2 R3 P2（131 条）聚合复核

> 口径：按模块逐条对 CHANGELOG、模块 README/XML 与测试取证；CHANGELOG 只是索引，不是修复证明。

- **139/139 条有处置**：132 条在 `CHANGELOG.md` 有明确条目（粗略分类：代码/测试修复 87 条、文档/契约订正 45 条），其余 7 条由模块 README/源码或 R3-ADJ 条目覆盖：
  - `R3-CAM-001`（NaN/±∞ 有限性）由 `R3-CIN-002` 的后端校验与 `CameraBlendStyle` 文档覆盖（CHANGELOG `:1078`）；
  - `R3-SAVE-004` / `R3-SAVE-006`（`RecoverAsync` 线程表述）由 `R3-ADJ-08` 条目覆盖（CHANGELOG `:1428`，台账 `Modules/Save.md:41`）；
  - `R3-TMR-003`（粗档提前触发）由 Timer README 的已知限制与台账覆盖（CHANGELOG `:1524`）；
  - `R3-UIB-004`（回池钩子必须调 base）由 `UIViewBase.cs:209` / `UIPanelBase.cs:209` XML 与 UI README `:1179` 覆盖；
  - `R3-UIB-008`（绑定句柄示例）由 UI README `:463-464` 改为 `Track` / `RegisterBinding` 覆盖；
  - `R3-UIB-010` 与 `R3-ADJ-12` 同源，由 `SetUIRoot` 补调修复与 CHANGELOG `:354` 覆盖。
- **未发现完全未处置的 P2**；少数以「文档化 / 契约订正」结案而非改代码（如 `R3-AST-004` 热更 API 无包语义、`R3-UPD-004` `ProcessImmediate` 对禁用/缓冲节点静默、`R3-SER-005` `Serialize` 的 `type` 形参），这些在对应模块 README 有明确表述。
- **回归复核**：R3 报告点名的三条 `[回归]`（R3-AST-002 / R3-SET-001 / R3-UIB-003）均已在 2026-10-19 的修复批次中关闭，并有红证用例（`YooAssetManagerImplTests` 两例、`EncryptedSettingsStoreTests`、`UIHudManager` 停更后目标丢失用例）。本轮未发现由 R3 修复引入的新回归；相机重设计引入的公开面变更是有意的破坏性变更（见 §7.2）。

### 3.3 R3 的 14 条待裁裁定落盘复核

| R3-ADJ | 模块 | 台账状态 | 落点 | 复核 |
|---|---|---|---|---|
| 01 | Asset | 暂不做 | `Modules/Asset.md:82` | 预检/下载版本绑定；无使用方，维持 |
| 02 | Audio | 不做 | `Modules/Audio.md:64` | `Play` / `PlayAsync` 句柄失败形态不对称，维持 |
| 03 | Localization | 不做 | `Modules/Localization.md:39` | 并发切换不引入代次作废，维持 |
| 04 | Localization | 暂不做 | `Modules/Localization.md:40` | 覆盖当前语言不补事件面，维持 |
| 05 | Log | 暂不做 | `Modules/Log.md:116` | 结构化参数，维持 |
| 06 | Log | 不做 | `Modules/Log.md:122` | Editor 两处无前缀 `Debug.LogError`，维持 |
| 07 | Log | 不做 | `Modules/Log.md:123` | `UIDefaultController` verbose 保持 Info，维持 |
| 08 | Save | 未做 | `Modules/Save.md:41` | 文档已订正；`RecoverAsync` 双路径线程改造仍未做（本轮复核见 §6-SAVE） |
| 09 | Settings | 不做 | `Modules/Settings.md:45` | 旧管理器 `wantsToQuit` root，随会话边界裁定，维持 |
| 10 | Timer | 暂不做 | `Modules/Timer.md:64` | `UpdateManager.Clear()` 后不自动侦测，维持 |
| 11 | Timer | 暂不做 | `Modules/Timer.md:68` | 锚点需 Update 当前轴时刻，维持 |
| 12 | UI | 已做 | `Modules/UI.md:170` | 注入 provider 补调 `SetUIRoot`，有行为变更与用例 |
| 13 | UI | 已做 | `Modules/UI.md:171` | `AllPanelsClosedMessage` 仅 `OpenCount == 0` 发布，有行为变更 |
| 14 | Ledger | 已做 | `Ledger.md:121` | 包自身 Git URL 说明已补，docs-only |

> 14 条全部落盘，状态与实施事实一致；没有「台账写已做但代码没做」或相反的情况。`R3-ADJ-08` 是唯一仍开着的实现项。

### 3.4 Luban LUB-01..28 复核

| 结论 | 条目 | 证据 |
|---|---|---|
| **已修复（26 条）** | LUB-01/02/03/05/06/07/08/09/10/11/12/13/14/15/16/17/18/19/20/21/22/23/24/25/27/28 | 扩展包 CHANGELOG `### Fixed` 与 `### Added`；代码：`LubanSettingsValidator` 绝对路径/受保护目录、`LubanConfigImpl` 代际+CTS、`LubanBootstrapStage._initializedByThisStage`、`LubanIntegrationWriter` preflight/stale `.meta`、`LubanProjectValidator` 双平台 asmdef、`LubanConfigBridge` 重复键上下文、`LubanIntegrationEmitter` BOM 剥离；测试：`LubanSettingsValidatorTests` 17 例、`LubanConfigTests` 23 例、`LubanGoldenFileTests`、`LubanFixtureSyncTests`、`LubanSampleArtifactTests`、`LubanConfigTests.ConfigChanged_FiresPerTable_BeforeIsInitialized`；守卫：六个 Architecture fixture；`check-docs.ps1:516-518` 编 Luban `Samples~` |
| **部分修复（1 条）** | LUB-04 | 默认后端的**错误路径**有覆盖（`LubanAssetProviderTests.DefaultSource_WithoutAssetManager_ThrowsHint`），但 `AssetManagerLubanAssetSource` 的 TextAsset/RawFile **成功映射**仍只经 fake `ILubanAssetSource`；见 §6 Luban P2 |
| **仍开放（1 条，用户动作）** | LUB-26 | `git tag` 仍只有 `0.3.0`；扩展包 `0.1.0` 未打 tag，README 安装 URL 仍是 `#<tag>` 占位。打 tag 是用户动作，审计轮不执行 |

> `Documentation/Luban.md` §七是 2026-10-24 的**点快照**：其正文保留当时的「发现」口吻（如 LUB-06 建议 `wasInitialized` 前置，而实现选择了 `await` 前置 + 失败复位），`§7.7` 末尾已标注批次 A–F 落地。本轮不把它当缺陷，只核对实现状态（见 §6 Luban P2 的文档项）。

### 3.5 探针轨与台账实时计数

- **R3 探针轨**：
  - 已执行并结案：`R3-PRB-01`（wantsToQuit，2026-10-20）、`R3-PRB-05`（关闭域重载两遍，2026-10-20 复跑全绿）、`R3-PRB-11`（裸消费方依赖安装，2026-10-20）、`R3-PRB-06/13`（编译探针，审计期）。
  - 随修复以「改前必红」用例执行：`R3-PRB-07/09/12/14`。
  - 转文档/源码核对：`R3-PRB-08`（0.3.0 未分发，转行为变更说明）、`R3-PRB-10`（AudioMixer 路由，转文档截断）。
  - 仍 `未做`：`R3-PRB-02`（真 YooAsset 低内存 `GetGlobal` 假 null）、`R3-PRB-03`（IL2CPP High stripping `[Preserve]`）、`R3-PRB-04`（macOS Server 构建）——均需真机/真包环境，配方保留在对应模块台账。
- **台账计数**：`check-docs.ps1 -Enforce` 口径 463 条（模块 405 + `Ledger.md` §三 58）；R3 冻结值 370。增长来自 R3 修复批次、相机重设计、Luban 扩展包与场景/设置等新增条目。

## 4. R4 P0 清单

**本轮未发现 P0。** 139 条 R3 发现无 P0 回归；新面（相机重设计、Luban 分包、使用驱动阶段）未发现「默认路径必然损坏数据或整体不可用」的缺陷。

## 5. R4 P1 清单

### R4-UIA-001 · P1 · B2/B6/D4/D3 · Dispose × 在途打开后，续体仍对已回池实例执行 `OnClose`

- **位置**：`Runtime/UI/UIManagerImpl.cs:679-685`（`!IsInitialized` 分支 `await panel.DoCloseAsync(immediate: true)`）、`:2040-2067`（`RecyclePanel` 只防二次回池）；`Runtime/UI/View/UIViewBase.cs:271-286`（`DoCloseAsync` 无「已回池」守卫）。
- **发现**：`Dispose` 经 `RecyclePanel` 执行 `OnPoolRecycle`（释放 `Track`、解绑并 Dispose ViewModel、复位状态）并把实例还池；被阻塞的 `OpenAsync` 续体醒来后看到 `IsInitialized == false`，仍调用 `DoCloseAsync`，于是 `OnClose` 在 `OnPoolRecycle` **之后**运行。若期间已重新 `Initialize` 并重开同一资源，池化实例可能已被新管理器复用，旧续体会关闭/失活新面板，甚至触发新对话框的 `SetResult`。
- **失败场景**：场景切换时 `UIManager.Destroy()`，旧场景在途打开在资源加载完成后返回；`OnClose` 访问已 Dispose 的 ViewModel 抛异常；或新场景重开同一面板后，旧续体把新面板 `IsOpen` 置 false。现有 `Dispose_DuringInFlightOpen_RecyclesPanelOnce` 只断言回池次数，不覆盖顺序与复用。
- **建议**：`!IsInitialized` 分支只回滚、不 `DoCloseAsync`；或在 `RecyclePanel` 增加「打开世代」并由续体比对；回池后禁止再走关闭路径。
- **验证**：PlayMode：Gate 挂起首次打开 → `Destroy()` → 断言 `OnClose` 不在 `OnPoolRecycle` 之后；第二阶段 Destroy→重新 Initialize→重开同一地址→放行旧 Gate→断言新面板仍 `IsOpen`。改前必红。
- **落点**：`Runtime/UI/UIManagerImpl.cs`、`View/UIViewBase.cs`、测试、CHANGELOG 行为变更；实施批次 0/1。

### R4-UIB-001 · P1 · B2/B5/F/D4 · `owner=null` 的遮罩在任意面板被外部销毁时被误剪

- **位置**：`Runtime/UI/UIManagerImpl.cs:2202-2216`（`PruneOverlayHolds` 用 `_maskEntries[i].Owner == null` 判失效）、`:982-995`（`ShowMask(style, owner = null)` 是公开默认路径，`UIManager.cs:595` 亦然）。
- **发现**：`Owner` 为 `null` 有两种含义——「无持有者面板」与「Unity 伪 null（已销毁）」。`PruneOverlayHolds` 在任意面板外部销毁导致剪枝时，会把所有 `Owner == null` 的遮罩条目一并删除并隐藏，即使这些遮罩是 `UIManager.ShowMask()` 正常创建、句柄仍被业务持有。
- **失败场景**：某系统 `var h = UIManager.ShowMask()` 打开模态遮罩；另一个面板被第三方 `Destroy`（R3-UIA-003 明确支持的路径）；下一次 `OpenCount`/`Update`/`Panels` 触发剪枝后遮罩消失、`h.IsValid == false`，模态输入保护静默失效。
- **建议**：区分「从未有 owner」与「owner 已销毁」——`ReferenceEquals(Owner, null)` 跳过，`(object)Owner != null && Owner == null` 才剪；补「null owner + 外部销毁他面板」用例。
- **验证**：PlayMode：`ShowMask()`（省略 owner）→ 打开 FakePanel → `DestroyImmediate(panel.gameObject)` → `UIManager.Update(0,0)` → 断言 `IsMaskShowing` 与 `handle.IsValid` 仍 true。改前必红。
- **落点**：`UIManagerImpl.cs`、`UIMaskHandle` 测试、README 行为契约；实施批次 0/1。

### R4-UIB-002 · P1 · B2/B5/E/F/D4 · HUD 同目标并发 Attach 产生不在映射里的孤儿，`DetachAll` 清不掉

- **位置**：`Runtime/UI/UIHudManager.cs:93-173`（`AttachAsync` 只在实例化前查一次 `_hudMap`，无在途去重/世代）、`:188-205`（`DetachAll` 只遍历 `_hudMap`）、`:359-372`（`RecycleHud` 无认领守卫）。
- **发现**：两个 `ShowHudAsync<T>(target, ...)` 并发进入时都看到 `_hudMap` 为空，各自实例化并执行 `OnOpen`；后完成者写 `_hudMap[target]` 覆盖前者，但两个实例都留在 `_activeHudList`。第一个成为「不在 map 里的孤儿」，仍被每帧驱动；`HideHud(target)` / `CloseAllAsync` 只碰 map 中的第二个，孤儿继续显示，直到目标被销毁。
- **失败场景**：两个系统同时请求同一目标的血条且未 await 第一次调用；屏幕出现双血条；`CloseAllAsync` 后仍残留一条，`Destroy` 后 GameObject 留在 `Layer_HUD`。
- **建议**：按 target 建在途表（或 generation），同 target 后来者加入同一次 Attach；`DetachAll` 同步清理 `_activeHudList` 中无映射的实例；`RecycleHud` 加实例级认领守卫。
- **验证**：PlayMode：给 `FakeAssetManager.InstantiateAsync` 加 Gate（仿 `FakePanelFactory`），同一 target 连发两次 `ShowHudAsync`，放行后断言 `Layer_HUD` 活跃 HUD 为 1；`CloseAllAsync` 后为 0。改前红。
- **落点**：`UIHudManager.cs`、测试、README 行为契约；实施批次 0/1。

> 三条 P1 均为「生命周期/并发窗口」缺陷，不是默认单线程顺序调用会触发；但都发生在框架明确支持的路径（Destroy、外部销毁、异步 UI）上，故按 P1 处理。每条都必须先写「改前必红」用例。

## 6. R4 P2 清单（按模块）

> 一条一行；`[回归]` 表示由前轮修复引入，`[跨模块]` 表示归拥有方立条，`[需实测]` 表示需运行期/真机证据。

### UI

- **R4-UIA-002 · P2 · B3/D4/F**：`Runtime/UI/UIManagerImpl.cs:2012-2031,2048-2066` —— `OnPoolRecycle` 抛异常会中断外层 `finally`：`PanelClosedMessage` 不发、`OnAfterCloseAsync` 不调；HUD 的 `RecycleHud` 同形且在 fire-and-forget 中逃逸。建议把发布/后置回调移出受回池钩子影响的位置，钩子异常记录后继续。
- **R4-UIA-003 · P2 · A1 [回归·文档]**：`Runtime/UI/README.md:1332-1334,1341-1344` —— 仍写「`SetInstance` 不会重接驱动器」，而 `UIManager.SetInstance` 在 `:170` 调 `EnsureFrameDriverRegistered`，`UIManagerFrameDriverTests` / `UIPanelTierTests` 已锁定新行为。R3-UIA-010 的代码已修，文档面反向回归。建议删掉两处「不会」，统一为「Initialize / SetInstance 都会重接每帧与现存分档驱动器」。
- **R4-UIA-004 · P2 · A1**：`Runtime/UI/README.md:603,621,1335-1337` —— 同一 README 的排序节写 `MaxIndexInLayer = 31`、每层 31 个面板，而实现 `UISorting.cs:31-45` 是 30（遮罩占第 31 位）；又写「每次打开/关闭/置顶都重排」，实现只在 `BringToFront` / `RegisterPanel` 调 `RestampSortingOrders`（关闭只摘栈，序号可能留空洞）。建议按实现订正。
- **R4-UIA-005 · P2 · C2/B2/F [需实测]**：`Runtime/UI/UIManagerImpl.cs:1470-1522` —— `SetLayerVisibility` 只 `SetActive`；`SetLayerInteractive` 用 `GetComponentsInChildren`（默认不含 inactive），层隐藏时面板全 inactive，拿不到其 raycaster；恢复可见后不会重新应用 `_layerInteractive=false`，出现「查询说禁用、实际可交互」。建议恢复可见时重放层交互状态，或改用 includeInactive。
- **R4-UIB-003 · P2 · F/A1**：`Runtime/UI/UIManagerImpl.cs:855-861` —— R3-ADJ-13 只给消息面加了 `toClose.Count > 0 && OpenCount == 0` 门控；`_controller.OnAllPanelsClosedAsync` 仍无条件调用。控制器在 `OnBeforeCloseAsync` 拦下全部关闭时仍收到「全关」回调。建议同门控或明确文档化两套语义；同步 `AllPanelsClosedMessage` XML 与 `IUIController` XML。
- **R4-UIB-004 · P2 · B3**：`Runtime/UI/UIManagerImpl.cs:250-279,313-338,307-310` —— `SetTipProvider` / `SetHudProvider` 直接调旧 provider `DetachAll`，`HideHud` 直接 `Detach`，均无异常隔离；同一失败面在 `CloseAllAsync` / `Dispose` 走 `SafeDetachAll`。旧 provider 抛异常时新 provider 不会被赋值，管理器停在旧实现上。建议统一走隔离包装，先落新引用再隔离旧调用。
- **R4-UIB-005 · P2 · A1/D4 [回归·部分]**：`Runtime/UI/Data/UIPanelBinding.cs:166-183` —— `img_` 命中且组件正确、但源值不是 `Sprite`/`Color`/`float` 时无条件 `return`，完全静默；README 仍承诺「类型不匹配会在编辑器打告警」。R3-UIB-009 只修了「组件缺失」。建议在三个类型判断都失败时按 `#if UNITY_EDITOR` 告警。
- **R4-UIB-006 · P2 · A1**：`Runtime/UI/README.md:458-465` —— 示例把 `BindToLocalizedText` 返回值说成「交给 `Track` / `RegisterBinding`」；实际方法在 `UIViewBase` 下已自动 `Track`（`UIBinder.cs:107-133`），且 `RegisterBinding` 不接受 `IDisposable`。建议改为「在面板/HUD 下直接调用即可；无宿主时自行 Dispose」。
- **R4-UIB-007 · P2 · A1/D3**：`Runtime/UI/README.md:482-512` —— 关闭代码块在 `:503` 结束，取消订阅三行（`:508-511`）落在围栏外，`:512` 是孤立 ```；Markdown 结构损坏，可能吞掉后续「快速使用 12」。建议把三行放回代码块并做一次围栏配对检查。
- **R4-UIA-006 · P3 · D3**：`Runtime/UI/README.md:635-638` 与测试缺口 —— `PopToAsync` 目标已栈顶、`PopToRootAsync` 栈深 ≤1 的「假 OnResume」守卫已实现但无直接用例；`R3-UIA-004` 的 Dispose provider 隔离无直接用例。建议补两条低成本用例。

### Camera

- **R4-CAM-001 · P2 · F/A1 [需实测]**：`Runtime/Camera/Cinemachine/CinemachineCameraBackend.cs:606-638,853-883` —— A→B 混合途中对**同一目标 B** 做 cut 时，CM 只发 `Deactivated(A)` + `BlendFinished(B)`（incoming==outgoing，不发 `CameraActivated`）；消息面缺 README 承诺的 `Activated(IsCut=true)`。建议在 README/契约写明该例外，或在 cut 经 BlendFinished 结算时补发一次 `Activated`。PlayMode 探针：2s 过渡第 0.5s `SwitchTo("b")`，断言消息序列。
- **R4-CAM-002 · P2 · F/D4 [需实测]**：`CinemachineCameraBackend.cs:640-653` —— `TransitionToSlot` 在「已是活动相机且未混合」时直接 `SettleRequest(Completed)`，不执行 `RestorePreviousBoosted`/`Boost`。若外部（Timeline `SetCameraOverride`）把已注册 B 设为活动、管理器此前 boost 的是 A，则 `SwitchTo("b", 0.5f)` 提前完成；override 释放后 A 仍被 boost 为最高优先级，输出回到 A 而栈顶是 B。建议提前返回前也执行 boost 归属切换，或文档化「外部 override 期间不接管 boost」。
- **R4-CAM-003 · P2 · A1**：`Runtime/Camera/CameraManager.cs:434-440` —— `Subscribe` 的 XML 说「也可配合 `destroyCancellationToken` 使用」，但仓内只有 `DisposableBagExtensions.AddTo(DisposableBag)`，没有 `AddTo(CancellationToken)` 或 token 订阅重载。建议删该半句或补 API（公开面裁定）。
- **R4-CAM-004 · P2 · D2 [需实测]**：`Runtime/Camera/CameraManager.cs:441-471,148,164` —— 三个 `Subscribe(Action<TMessage>)` 对无类型 lambda（`m => Use(m.CameraId)`）可能 CS0121；`Register(null)` 在 string/vcam 重载间二义。建议文档示例统一显式消息类型；`Register` 注释说明 null 行为，或加显式 `Register((CinemachineVirtualCameraBase)null)` 用例。
- **R4-CAM-005 · P2 · F/A1 [需实测]**：`CinemachineCameraBackend.cs:972-999` —— 已注册但被外部销毁的相机仍 `IsRegistered==true`、计入 `CameraCount`，其 `Deactivated`/`BlendFinished` 事件经 `ResolveCameraId` 得 `null`，按「外部相机」发布。建议槽位按引用（含 fake-null）反查 id，或在 README 明确「已销毁注册相机的事件 id 为 null」。

### Diagnostics

- **R4-DIAG-001 · P2 · B3/D4**：`Runtime/Diagnostics/DiagnosticsManager.cs:90-100` —— R3-DIAG-001 只给 `Title` 加了 `SafeTitle`；`Order` 仍是第三方 getter 裸读。`Order` 抛异常会让 `Register` 整体抛出、页签不登记，与 Title 隔离策略不对称。建议加 `SafeOrder`（记 Error、按 `int.MaxValue`/0 兜底）或明确「Order 不得抛」。
- **R4-DIAG-002 · P2 · A1/D3**：`Runtime/Diagnostics/DiagnosticReportFormatter.cs:136-146,157-177` —— 表头行裁掉尾部空列，但列宽与分隔线仍按声明列数绘制；表头含空尾列时表头与数据行宽度不一致。建议表头不参与尾部裁剪，或按 `ColumnCount` 输出；补格式用例。

### Scene

- **R4-SCN-001 · P2 · B3/D4 [需实测]**：`Runtime/Scene/SceneManager.cs:62-70` —— 激活失败告警直接取 `handle.Scene.name`；`SetActiveScene` 已判 `IsValid/isLoaded`，告警字符串没有同样守卫。自定义 `IAssetSceneLoader` 返回「非 default 句柄 + 无效 Scene」时，取 `name` 可能抛 `ArgumentException`，让「返回句柄」路径变成异常。建议告警前判 `Scene.IsValid()`，无效时用 location 代替。

### Luban（扩展包）

- **R4-LBN-001 · P2 · B4/A1**：`Runtime/LubanConfig.cs:175-187` —— 主线程断言只覆盖 `InitializeAsync` / `Destroy` / 测试注入 / 会话复位；`Tables` / `GetTables<T>` / `TryGetTables<T>` / `IsInitialized` 无入口断言，而 README 写「所有公开 API 必须在主线程调用；Editor 下入口断言」。建议把查询面纳入同一守卫，或把 README 收窄为「写入口断言」。
- **R4-LBN-002 · P2 · D3**：`Runtime/AssetManagerLubanAssetSource.cs:19-74` —— LUB-04 只补了「AssetManager 未初始化」错误路径；TextAsset 批量映射、RawFile 单文件映射、句柄释放仍只经 fake `ILubanAssetSource` 间接覆盖。生产默认路径一旦映射错，现有测试全绿。建议用 `AssetManager.SetInstance` 假实现或内部 seam 覆盖成功/失败两态。
- **R4-LBN-003 · P2 · D3**：`Editor/LubanProcessRunner.cs:46-113` —— `Run` 的超时、`Kill`、退出码、stdout/stderr 截断与编码零直接覆盖；`LubanProcessRunnerTests` 实际只测 `LubanProcessOutputBuffer`。建议补一个可控子进程 fixture（成功/非零/超时/大输出）。
- **R4-LBN-004 · P2 · D3**：`Editor/LubanIntegrationGenerator.cs:25-87`、`:89-120`、`Editor/LubanMenu.cs` —— `Generate(write:true)`、`BuildSummary`、菜单入口与 `RunIntegration` 无组合覆盖；现有用例全部 `write:false`，Writer/Emitter 各自独立测。建议补「解析 → 校验 → 发射 → 写盘」端到端（临时目录）与失败回滚用例。
- **R4-LBN-005 · P2 · B1/B6 [回归·修复残留]**：`Runtime/LubanBootstrapStage.cs:71-83` —— `_initializedByThisStage = true` 在 `await` 之前；若另一个调用方的手动 `InitializeAsync` 已在途，本阶段只是加入同一次初始化，成功后却被记为所有者，`Shutdown` 会 `Destroy` 别人初始化的状态。现有用例只覆盖「进入 Execute 前已初始化」。建议区分「本次创建」与「加入在途」，例如由门面返回所有权结果或记录 `wasInitializing`。
- **R4-LBN-006 · P2 · A1/D4**：`Runtime/LubanConfigImpl.cs:148-152` —— provider 级失败（如 AssetManager 未初始化）只包 `[LubanConfig] Failed to load Luban data files: {inner}`，不含具体表名与 location；README「错误」承诺「消息带表名与 location」。建议外层保留首个失败表名/location，或把承诺收窄。
- **R4-LBN-007 · P2 · A1**：`Runtime/LubanConfigImpl.cs:487`、`Editor/LubanCommandBuilder.cs:112-116` —— README「`DataRoot` 原样拼接，不归一化反斜杠、首斜杠或空白」与实现 `TrimEnd('/')` 不符；「附加参数原样追加」与 `Trim()` 不符。建议改实现或改措辞（二选一，别让文档与代码各说一套）。
- **R4-LBN-008 · P2 · D4**：`Editor/LubanIntegrationWriter.cs:77-110,169-185` —— preflight 只覆盖「校验失败」；写入过程中 IO 失败仍会留下部分新文件 + 旧 manifest；`WriteAtomically` 是「写 tmp → 删旧 → move」，删旧与 move 之间崩溃会丢目标文件；manifest 最后写。建议写临时目录整体替换，或至少在失败时回滚已写文件并给出可操作消息。
- **R4-LBN-009 · P2 · C2 [需实测]**：`Editor/LubanSettingsValidator.cs:334-365`、`Editor/LubanProjectValidator.cs:314-320` —— 路径比较全平台 `OrdinalIgnoreCase`，Linux 大小写敏感文件系统可能误判；`File.Exists(path)` 的 `path` 来自 `AssetDatabase.GUIDToAssetPath`（项目相对），依赖 Editor 当前工作目录。建议用 `Path.GetFullPath(Path.Combine(projectRoot, path))` 并区分平台比较策略；补非项目根 cwd 的探针。
- **R4-LBN-010 · P2 · D4**：`Runtime/LubanConfigImpl.cs:89-92,311-335` —— 并发 `InitializeAsync` 共享首个调用者的结果，但第二个调用者的 `adapter` 不同时被静默忽略（无 Warning）；核心 Config 在 R3-CONF-003 已确立「参数不一致要告警」的先例。建议在加入前比较 adapter 实例/类型并告警，或写入行为契约。
- **R4-LBN-011 · P2 · A1**：`Assets/XFramework.Luban/README.md:328` —— 「核心包的六道门禁不覆盖本包」过宽：`check-docs.ps1 -Enforce` 的第六项会编译 `Assets/XFramework.Luban/Samples~`（`Tools/check-docs.ps1:516-518`）。建议改为「XML/`.meta`/台账/README 文件树不覆盖；示例编译覆盖」。
- **R4-LBN-012 · P2 · A1**：`Assets/XFramework.Luban/README.md:340-346` —— 「接口承诺到哪为止」列了 6 个类型，漏 `LubanDataLoadMode`、`LubanInitReport`（二者在公开 API 基线内）。建议补齐或说明「枚举/载荷结构不单独承诺」。
- **R4-LBN-013 · P2 · D1/D3**：`Editor/LubanGeneratorSettings.cs:10`、`Editor/LubanCodeTarget.cs:6,18` —— Editor 程序集有 3 个公开类型，但无公开 API 基线；核心 Editor 的 5 类型/10 成员同样不在 `PublicApiBaselineTests` 射程。建议至少为 Luban Editor 建基线，或在「接口承诺」明确 Editor 面不受语义化版本约束。
- **R4-LBN-014 · P2 · A1**：`Documentation/Luban.md:311` —— §七头部写「Runtime 13 个 `.cs`、Editor 14 个」，当前为 15/15（含 `AssemblyInfo.cs` 与新增的 `AssetManagerLubanAssetSource.cs` / `LubanProcessOutputBuffer.cs`）。点快照的计数应订正或标明统计口径。
- **R4-LBN-015 · P3 · C2 [需实测]**：`Editor/LubanProcessRunner.cs:46-55` —— 未设置 `StandardOutputEncoding` / `StandardErrorEncoding`；中文路径/输出的编码行为未验证。建议显式 UTF-8 并补一条含中文输出的探针。
- **R4-LBN-016 · P3 · B5**：`Runtime/LubanConfigBridge.cs:29-33` —— `Dictionary` 走零拷贝，调用方后续修改同一个字典会静默改变 Config 内容且不触发 `ConfigChanged`。建议在 README「性能代价」补一句「零拷贝共享调用方字典，注册后不要改」。
- **R4-LBN-017 · P3 · C1**：`Runtime/LubanConfigImpl.cs:6` —— `using XFramework.XAsset;` 未使用（文件内无该命名空间类型）。建议删除。

### Config

- **R4-CONF-001 · P2 · A2/B3/D4 [回归]**：`Runtime/Config/ConfigManagerImpl.cs:212-221` —— `PreloadTableAsync<T>(path, IConfigLoader)` 的「同身份在途」分支先拼 `"Loader:" + loader.GetType().Name`，`loader == null` 的校验在 `:220`；第二次调用传 null loader 且已有 Table 在途时抛 **NRE**，不是 XML 承诺的 `ConfigException`。Global 版用 `loader?.GetType()` 不 NRE，但在「已加载/在途」分支静默接受 null。R3-CONF-003 的修复引入。建议把 null 校验提前到所有分支之前。
- **R4-CONF-002 · P2 · B5/D4**：`Runtime/Config/ConfigTextLoader.cs:21-35`、`Runtime/Config/ScriptableObjectLoader.cs:75-87` —— `handle.Asset == null` 的检查在 `try/finally` **之前**，类型不符（YooAsset `GetAssetObject<T>` 返回 null 但句柄有效）时抛异常且不 `Dispose`，provider/bundle 引用永久驻留。建议把检查放进 `try` 内或先 `handle.Dispose()` 再抛；错误消息区分「资源不存在」与「类型不符」。
- **R4-CONF-003 · P2 · D3/C2 [需实测]**：`Runtime/Config/ScriptableObjectLoader.cs:92-105`、`Tests/Editor/Config/ScriptableObjectTableTests.cs:60-71` —— SO Table 的用例通过反射写 `Items` 的 backing field，从未经过 Unity 资产保存/重载；README 教的「Create 资产 → Inspector 填 Items → 加载」端到端零覆盖。若泛型基类的 `[field: SerializeField] List<TRow>` 在某 Unity 版本不序列化，所有用户 SO 表都会稳定得到「contains no items」而测试全绿。建议补 EditMode 资产往返用例（`AssetDatabase.CreateAsset`/`SaveAssets`/`LoadAssetAtPath`）。
- **R4-CONF-004 · P3 · A1/E/D4**：`Runtime/Config/ConfigTable.cs:32-52,340-365`、`Runtime/Config/README.md:403-404`、`Runtime/Luban/LubanConfigBridge.cs:58-84` —— `ConfigTable<T>` 只校验字典键/值**类型**，从不承诺「键 == 行 `Id`」；Luban list 桥的 `keySelector` 允许两者分叉。当前生成器同源，但公共重载可写出「注册成功、`Get(row.Id)` 查不到」的表。建议在契约里写明该前提，或构造期做一次 O(n) 校验；Luban list 重载可直接用 `row.Id` 消除分叉。
- **R4-CONF-005 · P2 · B6/E**：`Runtime/Config/ConfigManagerImpl.cs:108-125,162-179` —— 在途登记按 `Type` 单槽存放 `InFlightLoad`，Table 与 Global 同类型并发时后启动者会**替换**槽位；此时第三个同身份（Table）调用方看不到先前的 Table 在途项，会再启动一次重复加载（`CompleteInFlight` 的引用比较保证旧任务仍唤醒自己的等待者）。R3-CONF-009/010 未覆盖「第三种同身份调用方在跨身份替换后加入」。建议键改 `(Type, Kind)` 或每身份一槽；补「Table 在途 + Global 在途 + 再 Table」用例。
- **R4-CONF-006 · P3 · D4**：`Runtime/Config/ConfigManagerImpl.cs:797-812,229,324` —— 在途参数不一致的告警只比较 `"Loader:" + loader.GetType().Name`；同一 Loader 类型的两个不同实例（可能带不同状态）不告警，仍静默复用首个结果。建议比较实例引用，或文档写明「只按类型比较」。

### Asset

- **R4-AST-001 · P2 · B1/B2/D4**：`Runtime/Asset/AssetManager.cs:206-222` —— `SetInstance` 只替换引用，不 `Dispose` 也不 `DetachSessionSubscriptions` 旧 `AssetManagerImpl`；旧实例被 `Application.lowMemory` / `SceneManager.sceneUnloaded` 静态事件钉住，低内存时仍会对共享的 YooAsset 包执行清池/整包卸载。XML 说「需要释放请先自行处置」，但 `InitializeAsync` 创建的实例从未暴露给调用方，实际上无法处置。建议 `SetInstance` 对旧实例先退订再 Dispose，或禁止初始化后替换并抛异常。
- **R4-AST-002 · P2 · B1/A1**：`Runtime/Asset/AssetBootstrapStage.cs:73-106` —— `_initializedByThisStage` 只表示「本阶段确实初始化过」，不校验当前 `AssetManager` 实例是否仍属于自己；阶段初始化成功后使用方 `SetInstance(customBackend)`，`Bootstrap.Shutdown` 仍会 `Destroy()` 掉 customBackend。与 R3-LOC-002 / Luban 阶段所有权同族。建议记录初始化时的实例身份，Shutdown 仅在身份未变时销毁。
- **R4-AST-003 · P2 · A1/D4/C2**：`Runtime/Asset/YooAssetManagerImpl.cs:252-300` —— 三个热更入口只 `GetPackage` 检查存在性；「包存在但在 `InitializePackageAsync` 中失败」的缓存条目不在守卫内。Editor 下上游 `DebugCheckInitialize`（`[Conditional("DEBUG")]`）抛裸 `Exception`，与 XML「其余失败抛 `InvalidOperationException`」不符；Player 下检查被编译掉，可能 NRE。建议三入口统一检查 `package.InitializeStatus` 并抛带包名的 `InvalidOperationException`。
- **R4-AST-004 · P3 · A1/D4/B5**：`Runtime/Asset/AssetManagerImpl.cs:253-285,435-457` —— `InstantiateAsync<T>` 缺组件时日志写「Destroying instance」，实际 `DestroyInstance` 会先回池（池未满时不销毁）；README `:364` 也写「销毁」。建议改为真销毁，或把日志/README 改为「回池等待复用」。
- **R4-AST-005 · P3 · B3/D4/C1**：`Runtime/Asset/YooAssetManagerImpl.cs:334-355` —— `DownloadAssetsAsync` 末尾补发的最终进度是裸 `progress?.Invoke`，不在 `InvokeIsolated` 内；回调抛异常会让已成功的下载以异常收场。另 `AssetDownloaderHandle.InvokeIsolated` 每次 `GetInvocationList()` 分配一个 `Delegate[]`（R3-AST-006 修复引入的 C1 代价），未写入「性能代价」。建议最终补发也走隔离；单订阅者走直接调用。
- **R4-AST-006 · P3 · B6/B2 [需实测]**：`Runtime/Asset/AssetManager.cs:261-282,174-197` —— 会话复位只对已提交为 `_instance` 的 impl 调 `DetachSessionSubscriptions`；`InitializeAsync` 在途、尚未赋给 `_instance` 的 impl（其 `YooAssetManagerImpl` 构造时已订阅 `sceneUnloaded`）拿不到。关闭域重载时旧任务若不恢复，适配器被静态事件钉住跨会话存活。建议门面记录「正在创建的 impl」并在复位时退订，或把 `sceneUnloaded` 订阅推迟到初始化成功后。
- **R4-AST-007 · P3 · A1**：`Runtime/Asset/README.md:349` vs `:610-612` —— API 表写 `Destroy()`「释放全部资源」，接口承诺 3 与实现明确「不卸载 YooAsset 侧包与已加载资源」；`:615` 的「见已知限制第 14 条」实际应为第 15 条。建议订正 API 表与交叉引用。

### Input

- **R4-INP-001 · P2 · A2/F/D4**：`Runtime/Input/Default/InputSystemProvider.cs:246-258,263-295` —— `GetButtonPressDuration(action, playerId)` 从不读 `playerId`，计时表按 action 名建键、`UpdateButtonPressDurations` 取 0 号玩家的 action 边沿；README 声称所有读取 API 接受 `playerId`，已知限制只列了复合 raw/重绑定。建议键改 `(action, playerId)` 并逐玩家取控件边沿，或明确「时长仅支持 playerId=0」并告警。

### Localization

- **R4-LOC-001 · P3 · F/A1**：`Runtime/Localization/LocalizationManagerImpl.cs:165-168,346-411,425-498` —— 先 `ReplacePlaceholders` 再 `NormalizeBracesForFormat`；若替换值本身含合法复合格式项（如 `"{0}"`），会被 `GetFormat` 的 `args` 二次解释，而不是按字面输出；参数不足时可能抛 `FormatException`。建议先归一化表文本再做替换，或对替换值再次转义；若判定为可接受语义则写入行为契约。

### Audio

- **R4-AUD-001 · P2 · B3/B5/D3**：`Runtime/Audio/AudioSourcePool.cs:99-107,154-174,201-221`、`Runtime/Audio/AudioManagerImpl.cs:251-259` —— 外部销毁音频宿主/播放源后，活跃槽被 `Release` 成 Free，但 `AudioVoice.Source` 仍指向已销毁对象（Unity 伪 null）；`TryReserve` 恒取第一个 Free 槽且不重建 Source，下一次播放加载完 clip 后在 `Source == null` 判定处再次 `Release` 并返回 default。该槽继续是最小下标 Free，**每次播放都重复同一失败**，健康槽位永远不会被尝试；`EnsureHost` 只重建宿主，不重建既有槽位的 Source。建议 `TryReserve`/`Release` 检测伪 null 时重建或丢弃槽位；补「宿主外部销毁后仍能起播」用例。

### Settings

- **R4-SET-001 · P2 · B4/C2/A1 [需复核]**：`Runtime/Settings/EncryptedSettingsStore.cs:109-115,140-145,167-186` —— 内层同步时 `LoadAsync<T>` 把整段 `Load<T>`（含 `_inner.Load`、解密、`JsonUtility.FromJson`）丢到线程池；写侧 R3-SET-001 已改成「调用线程序列化、池线程只做 IO」，读侧没有对应处理，与 README 示例「JsonUtility 解析前切回主线程」不一致。建议池线程只做 IO/解密，切回主线程后再 `FromCipherBlob`；先确认 Unity 版本对 `JsonUtility.FromJson` 的线程约束，再定「代码修」或「文档订正」。

### Save

- **R4-SAVE-001 · P2 · A1**：`Documentation/Modules/Save.md:41` —— 条目写「恢复扫描里剩下的 5 处同步 `Delete` 与 2 处同步 `Exists`」；当前 `SaveManagerImpl.RecoverSlotAsync` 有 8 个 `FileManager.Delete` 调用点（`:1225,1229,1234,1236,1243,1244,1245,1246`）与 2 个 `Exists`（`:1214,1239`）。数字是维护向记录里会漂移的量，建议改为「恢复扫描仍有同步 `Delete` / `Exists` 调用点（见代码）」或按当前值订正。
- **R4-SAVE-002 · P2 · F/B5/A2**：`Runtime/Save/SaveManagerImpl.cs:694-720` vs `:858-876,222-250` —— `DeleteSlotAsync` 只用 `ExistsAsync(slotPath)` 判存在；仅剩 `.bak` 的槽位在 `SlotExistsAsync`/`GetSlotMetaAsync` 中表现为「存在/可恢复」，单槽删除却返回 false 并留下 `.bak/.meta`，下次恢复扫描会把槽位复活。R3-SAVE-001 只修了复制/移动/查询/批量，漏了单槽删除。建议存在性判定改为 `payload || backup`，补「只剩备份时删除」用例。

### Update

- **R4-UPD-001 · P2 · A1 [回归·文档]**：`Runtime/Update/README.md:327`、`Runtime/Update/UpdateManager.cs:371` —— R3-UPD-001 把恢复指引改成「UI 重进 `Initialize`（`SetInstance` 不重接驱动器）」，但 `UIManager.SetInstance` 在 `:170` 调 `EnsureFrameDriverRegistered`，实际会重接。这是 R3-UPD-001 修复引入的新反向错误，与 R4-UIA-003 同源（同一错误句在两个模块 README）。建议两处统一为「`Initialize` / `SetInstance` 都会重接每帧与现存分档驱动器」，补一条 `Clear → SetInstance` 的驱动器重注册断言。

### Pipeline

- **R4-PIPE-001 · P3 · A2/F [需复核]**：`Runtime/Pipeline/ParallelStage.cs:142`、`SequenceStage.cs:138`、`StageExecution.cs:23-29` —— R3-PIPE-004 在 `finally` 把 `_stageCtx.Owner` 置 null，发生在 `StageExecution` 的契约兜底写入（`SetProgress(1)`/`SetState(Completed)`）之前；顶层管线无碍（完成终局无条件广播），但**嵌套容器**的最终完成/进度写入不再通知父 `StageAggregator`。失败状态在 finally 之前写入，故失败检测不受影响。建议把 Owner 清理移到终态读取之后，或容器在 finally 前显式写 Completed/1f；补嵌套容器聚合用例。

### Pool

- **R4-POOL-001 · P3 · A1/B3/D4**：`Runtime/Pool/Pool.cs:170-187,230-240` —— R3-POOL-001 让租出回调异常时调用 `_onDestroy`/`OnDiscard` 并隔离其异常，但 (1) `onDestroy` 的 XML/README 仍只写「池满被丢弃」，未写租出失败也会触发；(2) `Return` 满池路径的丢弃回调没有 try/catch，异常会冒泡给 `Return` 调用方（实例已丢弃、计数已减），与租出路径不对称。建议文档写明两个触发点，`Return` 满池路径同形隔离或明确取舍。

### File

- **R4-FILE-001 · P3 · A1/D4**：`Runtime/File/IFileProvider.cs:18` vs `Runtime/File/FilePathUtility.cs:44-57`、`DesktopFileProvider.cs:218-223`、`MobileFileProvider.cs:225-238` —— 读失败契约仍写「参数非法（null、路径穿越等）→ ANE/ArgumentException」，但 `TryNormalizeRelativePath` 明确「空或 null 合法（表示域根）」，桌面/移动实现都把 null/空拼成域根而不是抛异常。R3-FILE-003 改了同一 remarks 块却未订正这一行。建议改为「null/空 = 域根；仅盘符/UNC/`..` 等非法」，或在 Provider 入口显式拒绝 null（破坏性，二选一）。

### Serialize

- **R4-SER-001 · P3 · A1/D4 [需复核]**：`Runtime/Serialize/README.md:80-86` vs `Runtime/Serialize/Serializer.cs:114-120,133-136` —— R3-SER-002 的新文案称「初始化之前注册则 Initialize 会保留你的…两种时机都可用，区别只是多一条告警」；但 `SubsystemRegistration` 档的 `ResetForNewSession` 先 `Shutdown()` 清空再 `Initialize()`，凡在该钩子之前完成的注册（静态初始化、更早的 SubsystemRegistration 回调、关域重载的上一会话）会被静默清掉，且不会出现「保留 + 告警」。建议 README 补「ResetForNewSession 之前的注册会被清掉」，或对非内置格式告警。

### 工程面（门禁 / 测试基础设施 / 发布）

- **R4-ENG-001 · P2 · A1**：`Assets/XFramework/Tests/Runtime/Architecture/PublicApiBaselineTests.cs:21` —— 类注释写「222 个公开类型 / 1961 行」，当前基线实测 237 个类型 / 2113 条（`PublicApiBaseline.txt` 2126 行含 13 行注释）。建议改为不写固定数（或写「以文件为准」），避免每次公开面增长都要改注释。
- **R4-ENG-002 · P2 · A1**：`Documentation/Workflow.md:106` —— `run-tests.ps1` 参数表只列 `-UpdateApiBaseline`，未列脚本实际存在的 `-UpdateLubanGolden` / `-UpdateLubanApiBaseline`（`Tools/run-tests.ps1:83-84,122-156`）。建议补齐两行，并写明「写盘后按设计判失败」。
- **R4-ENG-003 · P2 · A1/D3**：`Documentation/ModuleReadme.md:233` 写文件树门禁「不进门禁」，`:240` 写「子集校验是单向的」；`Tools/check-docs.ps1:503-511,558` 实际已把文件树纳入 `-Enforce`，且同时抓漏收与残影（双向）。建议以脚本为准订正这两句。
- **R4-ENG-004 · P2 · D1/D3 [门禁射程]**：`Tools/check-docs.ps1:105-108,165,185,291,414` —— Luban 扩展包只在第六项「示例编译」被覆盖；XML 文档告警、源文件参与编译、`.meta`、决策台账、README 文件树都不在核心脚本射程。包内六个守卫补了 `.meta`/README 树/公开面/边界/AutoInit/依赖，但**XML 文档告警与源文件参与编译仍无机械覆盖**。建议在 `check-docs.ps1` 的路径过滤泛化为可配置，或增加一条包内 Luban XML 编译告警守卫；若维持现状，在 `Ledger.md` 明确「暂不做」的射程缺口（现状已有该条，重开判据未触发）。
- **R4-ENG-005 · P2 · D3**：`Tools/run-tests.ps1:258-278` —— 调用 Unity 用 `Start-Process -Wait`，没有超时参数；Unity 卡死/编译错误等待与正常运行无法区分（仓内无超时记录，实测编译错误退出约 633s 来自会话经验，不是脚本事实）。建议加可选 `-TimeoutSeconds`（默认不启用），并把超时与「编译错误」两种失败分开报。
- **R4-ENG-006 · P2 · D1/D3**：两个包的 **Editor 程序集公开面均无 API 基线**：核心 Editor 5 个公开类型（`XFrameworkDependencyInstaller`、`DiagnosticsWindow`、`DiagnosticItemDrawer`、`DiagnosticReportView`、`IDiagnosticPanelView`），Luban Editor 3 个（`LubanGeneratorSettings`、`LubanCodeTarget`、`LubanDataTarget`）。`PublicApiBaselineTests` 明确只查 Runtime。建议至少为 Luban Editor 建基线，或在发布说明中明确 Editor 面不承诺语义化兼容。
- **R4-ENG-007 · P3 · D3**：`Tools/check-samples.ps1` 只编译 `.cs`，不检查场景/prefab 引用；`Samples~/Playable.unity` 与 `Samples~/Cinemachine` 的场景绑定仍靠人工/一次性探针（`Workflow.md:121` 已写明）。本轮无新增缺口，维持现状。
- **R4-ENG-008 · P3 · A1**：`Documentation/Modules/README.md` 的模块文件表只列 24 个核心模块；Luban 的维护向记录在 `Documentation/Luban.md`，不在该表。若希望 Luban 也受「每模块有台账」结构守卫，需新增 `Modules/Luban.md` 并处理模块集来源（当前来自核心 `Runtime/` 直接子目录）；否则在 `Modules/README.md` 或 `Ledger.md` 写明「Luban 的台账在 `Luban.md` §7.8」。列为 §9 的裁定项。
- **R4-ENG-009 · P2 · A1/D4**：`Assets/XFramework/CHANGELOG.md:247` —— `UISorting.MaxIndexInLayer` 由 31 改为 30 是**公开常量值变更**（编译器抓不到），但只出现在 `### Changed` 顶部，未进 `#### 行为变更（编译器抓不到，升级前请自己核对）` 小节。0.4.0 升级者按该小节核对时会漏掉它。建议移到行为变更表并写明「同层第 31 个及以后面板被钳到 30」。



## 7. 跨模块主题

### 7.1 前轮修复的文档-代码同步（第二轮）

- **涉及**：R3 的 45 条文档/契约订正（§3.2）、Luban 的 LUB-18/19/20、`PublicApiBaselineTests` 注释、`Workflow.md` 参数表、`ModuleReadme.md` 文件树门禁描述。
- **共同根因**：R3 实施轮把「代码修复」与「文档订正」分成不同提交，部分文档只改被引用的一处；示例/XML/README/台账/CHANGELOG 五处同步仍是人工纪律。Luban 的 `Documentation/Luban.md` 点快照与 CHANGELOG 实施状态并存，是「快照不改写」与「状态要可查」的边界问题。
- **统一动作**：实施轮每个修复提交仍按五处核对；点快照类文档（`AuditRound*.md`、`Documentation/Luban.md` §七）不改写历史，只在头部/末尾补「已实施批次」指针。
- **不做什么**：不引入文档内容运行时门禁。

### 7.2 相机重设计后的公开面、事件顺序与序列化兼容

- 公开面：2026-10-23 的从零重设计是**有意的破坏性变更**，迁移表（`CHANGELOG.md:307-342`）覆盖 `ICameraManager`/`CameraHandle`/`CinemachineCameraManager` 删除、`Initialize(CinemachineBrain)`、`Register(..., vcam)`、`TryGetCamera`、`SwitchTo/Push/Pop/PopAll/Clear`、四个异步动词、消息载荷改名、`GetState`/`CopyCameras` 等；`PublicApiBaseline` 已同步（237 类型 / 2113 条）。旧独立扩展包的删除与「程序集由 `Venusy609.Xframework.Cinemachine` 变为 `Venusy609.Xframework`」也在表内。
- 事件顺序：`Deactivated → Activated(IsCut) → BlendCompleted`（cut）与「开始帧 Activated、结束帧 Deactivated → BlendCompleted」（blend）已有 `CinemachineCameraEventTests` 锁定；**同一目标中途 cut 是例外**——不发 `Activated`（R4-CAM-001），README 未写明。
- 序列化兼容：`AssetPlayMode` 整数值变化已在行为变更表（0.3.0 未分发）；Camera 的公开枚举 `CameraBlendStyle` 追加 `HardIn/HardOut` 在末尾，既有数值不变。`DefaultGameLauncher._assetPlayMode` 的旧场景值风险已由 AST-001 的处理覆盖。
- 外部接管：Timeline `SetCameraOverride` + `SwitchTo` 已活动相机的 boost 归属存在边界（R4-CAM-002）；已销毁注册相机的事件 id 为 null（R4-CAM-005）。

### 7.3 Luban 分包与核心门禁射程

- Luban 在核心门禁中的唯一硬覆盖是 `check-docs.ps1 -Enforce` 第六项（编译 `Assets/XFramework.Luban/Samples~`）；XML 文档、源文件参与、`.meta`、台账、README 文件树不在核心脚本射程。
- 包内六个 Architecture 守卫补了 README 结构/文件树、`.meta`、包边界、AutoInit、Runtime 公开 API 基线、依赖声明；**XML 文档告警与源文件参与编译仍无机械覆盖**（R4-ENG-004）。
- 结论：现状可接受（Luban 是可选包，且已有六类守卫），但 README 的「六道门禁不覆盖本包」应改为精确射程（R4-LBN-011）；是否把 `check-docs` 路径泛化，维持 `Ledger.md` 的 `暂不做` + 重开判据。

### 7.4 0.4.0 发布就绪（机械核对项）

| # | 检查项 | 现状 | 结论 |
|---|---|---|---|
| 1 | `PublicApiBaseline` 的「已消失/已变更」段与 CHANGELOG 迁移表逐条对应 | 基线在 0.3.0 tag（2026-10-11）之后（2026-10-13）才引入，无法机械计算 0.3.0→current 的公开面差异；迁移表是唯一 0.3.0 升级记录 | **需人工核对**；实施轮应按迁移表逐条跑一次「旧写法 → 新写法」编译探针（`Workflow.md` §三.3） |
| 2 | `[Unreleased]` 切成 `[0.4.0]` 小节 | 仍是 `[Unreleased]`（`CHANGELOG.md:7`），含 2026-10-17 及更早条目；头部已写「下次发版应为 0.4.0」 | **未完成**；发版前需按日期切分并核对行为变更表 |
| 3 | `package.json` 版本与 tag | 核心仍 `0.3.0`；`git tag` 只有 `0.3.0`（2026-10-11） | **未完成**；发版需 bump 0.4.0 + 打 tag（用户动作） |
| 4 | 包 README 安装 URL 与依赖表 | 默认 URL 跟随 `main`（`:366`），示例仍写「钉 `0.3.0`」（`:375`）；依赖表含 UniTask 2.5.11 / YooAsset 2.3.18 / Input System 1.19.0 / Cinemachine 3.1.7 | **需随发版更新**「钉 0.3.0」示例；依赖三处（README/installer/manifest）当前一致 |
| 5 | Luban 0.1.0 的 tag / 安装说明 / 兼容声明 | 无 tag；README 安装 URL 为 `#<tag>` 占位；`documentationUrl`/`changelogUrl` 指向 `main` | **未完成**；tag 是用户动作；README 需替换占位并写明与核心 0.4.0 的兼容关系 |
| 6 | 各 `Documentation/Modules/<模块>.md` 的版本记录 | 只有部分模块有「模块版本记录」节；本轮新增 Camera/Luban 未改变核心模块的独立版本号语义 | **无需机械更新**；发版时按需补 |
| 7 | `.gitattributes` / `LICENSE` / `.sample.json` / `.meta` | `.gitattributes` 已含 `*.bytes binary`、`*.xml` / `luban.conf` / `.luban-integration-manifest` 文本；两个包都有 `LICENSE.md`；`.meta` 门禁覆盖核心，Luban 由包内守卫覆盖 | **通过** |
| 8 | OpenUPM / 发布渠道 | 仓内无 OpenUPM 配置；根 README 只给 Git URL 安装 | **仓外动作**；不阻塞代码审计 |

> 结论：**0.4.0 尚未就绪**，缺口集中在版本小节切分、tag、README 的 0.3.0 示例与 Luban 的 `<tag>` 占位；这些是发版动作，不是代码缺陷。审计轮不建议直接改版本号（属发布流程）。

### 7.5 依赖声明与安装真相

- 核心三处（包 README 依赖表 / `XFrameworkDependencyInstaller.cs` / `Packages/manifest.json`）当前一致：UniTask 2.5.11、YooAsset 2.3.18、Input System 1.19.0、Cinemachine 3.1.7；由 `DependencyDeclarationTests` 守。
- Luban 的依赖真相在扩展包 README「依赖」表与 `Packages/manifest.json`（`com.code-philosophy.luban#v1.2.0`），由 `LubanDependencyDeclarationTests` 守；消费方 PackageCache 布局下该守卫按设计跳过。
- 缺口：`DependencyDeclarationTests` 仍只校验「已列出的依赖」，不反查 asmdef 引用（R3 遗留 `未做`）；本轮 Input System / Cinemachine 漏列事故已修，但反查机制未建。

### 7.6 会话复位族的第三层

- R3 修复后，Asset 的 `sceneUnloaded`、Input 的 `onDeviceChange` 已在 `ResetForNewSession` 中退订；`SessionResetTests` 覆盖六个门面 + Bootstrap + InstanceTracker。
- 残余：实例级订阅仍按「谁创建谁清理」归使用方（2026-10-18 裁定）；Luban 的 `ResetForNewSession` 只丢引用并取消在途，不 unregister 表（由 ConfigManager 自己复位）。
- 本轮未发现新的跨会话静态态；关闭域重载两遍的**手工**验收仍是唯一真验收（`Workflow.md` §六配方），R3-PRB-05 已结案。

### 7.7 测试真实性与新面覆盖

- 核心 Tests 区间 +6,268/-1,032（105 文件）；Luban 142 例。全量 2106 + 628（6 skip = 6 个性能基准）。
- 新面覆盖的薄弱点集中在 Luban：生产默认加载后端成功路径、`LubanProcessRunner.Run`、`Generator(write:true)`/菜单入口、`LubanSettingsProvider`（R4-LBN-002/003/004/014）。
- 核心 Camera 的 PlayMode 用例已覆盖事件帧序、异步结果、注册生命周期、外部接管；UI 的 ADJ-12/13 行为变更需确认有用例（见 §8 UI）。
- 基准套件 `[Explicit]` + `[Category("Performance")]`，不污染门禁；分配尺子仍只有 `AllocationRuler` 一把可用。

### 7.8 修复引入的回归与行为变更

- **新回归 1 条**：R4-CONF-001（R3-CONF-003 的 `"Loader:" + loader.GetType().Name` 早于 null 校验，null loader 从承诺的 `ConfigException` 变成 NRE）。
- **修复残留/部分修复**：R4-UIA-003 + R4-UPD-001（R3-UIA-010/UPD-001 代码已修、README 反向回归，同一句错误在两个模块）、R4-UIB-005（R3-UIB-009 只修组件缺失、类型不匹配仍静默）、R4-UIB-006（R3-UIB-008 文案仍错）、R4-AST-005（R3-AST-006 的逐订阅者隔离每次分配）、R4-LBN-005（LUB-06 的所有权残留）、R4-SAVE-002（R3-SAVE-001 只修复制/移动/查询/批量，漏单槽删除）、R4-AUD-001（R3-AUD-003 只修非 Free 槽）。
- **行为变更（有意）**：R3-ADJ-12 `SetUIRoot` 注入补调、R3-ADJ-13 `AllPanelsClosedMessage` 门控、Update 自动注销补发 `OnDisable`、Bootstrap `Shutdown` 中止在途、Camera 公开面重写、`AssetPlayMode` 整数值、`UISorting.MaxIndexInLayer` 31→30（未进行为变更表，R4-ENG-009）。
- **共同根因**：修复提交只改被引用的一处；文档/示例/相邻 API 面未同批核对。统一动作仍是「一个修复提交核对五处 + 改前必红」。

### 7.9 台账、计数与文档状态

- `PublicApiBaselineTests` 注释 222/1961 vs 实测 237/2113（R4-ENG-001）。
- `Workflow.md` §五缺两个 Luban 更新开关（R4-ENG-002）。
- `ModuleReadme.md` 文件树门禁描述与脚本不一致（R4-ENG-003）。
- `Documentation/Luban.md` §七头部 13/14 vs 15/15（R4-LBN-014）。
- 台账 463 条（模块 405 + 框架 58），R3 冻结 370；增长已解释。

## 8. 逐模块审计记录

> 每条记录：范围与输入、18 类判据留痕、前轮修复偏差、旧结论复核、开口项、第三方对照、残留与不确定。P0/P1 明细见 §4/§5，P2 明细见 §6。

### Luban（T1 扩展包全量，首次）

- **范围与输入**：`Assets/XFramework.Luban/` Runtime 15 个 `.cs`、Editor 15 个 `.cs`、Tests 142 例（PlayMode 66 / EditMode 76）、`Samples~/Luban` 23 文件、README/CHANGELOG/package.json/两个 asmdef、六个 Architecture 守卫、`Documentation/Luban.md`、核心 Config/Asset/Bootstrap/Log 接缝。
- **18 类留痕**：A1 命中（主线程射程、错误消息、`DataRoot`/`ExtraArguments` 措辞、公开 API 列表、门禁覆盖表述，R4-LBN-001/006/007/011/012）；A2 命中（`ILubanConfigAdapter` 契约完整；`LubanConfigBridge` 未强制 ConfigManager 已初始化但会抛核心异常，可接受）；A3 **不适用/未覆盖**（扩展包不在 `check-docs` 的 XML 告警射程，包内守卫也不编译文档；本轮人工 grep 未发现未转义泛型，但不能声称编译器已验）；B1 命中（引导阶段所有权，R4-LBN-005）；B2 不适用（无对象池、无事件订阅）；B3 扫过（初始化/注册异常统一包 `LubanConfigException`；OCE 回滚已在 `LubanConfigImpl.cs:206-210` 补齐）；B4 命中（R4-LBN-001）；B5 扫过（单实例 `_tables`，无按实体增长的缓存；零拷贝字典的共享风险记 R4-LBN-016）；B6 命中（代际 + `_inflightCts` + 等待者广播，`LubanConfigImpl.cs:311-405` 逐点核对）；C1 扫过（只在初始化路径分配，无每帧路径、无反射）；C2 命中（路径大小写与 cwd、进程输出编码，R4-LBN-009/015）；D1 扫过（Runtime/Editor 只引核心公开面；`Luban.Runtime` 只在测试程序集）；D2 扫过（`RegisterTable` 两个重载按参数个数区分，无二义；未发现需编译探针的新形状）；D3 命中（R4-LBN-002/003/004/013）；D4 命中（R4-LBN-006/008/010）；D5 扫过（核心已提供 `RegisterTable`/`RegisterGlobal`/`AssetManager.LoadAllAsync`；懒加载缺口属能力未做）；E 扫过（键是行类型 + 主键，`ValueTuple` 键无另一来源合并风险）；F 命中（`ConfigChanged` 逐表触发 vs `IsInitialized` 整包，LUB-28 已文档化 + 测试锁定）。
- **前轮修复偏差**：LUB-01..28 中 26 条已关闭、LUB-04 部分（生产默认加载后端的成功路径仍零直接覆盖）、LUB-26 仍开放（tag 是用户动作）。LUB-06 的修复形状与 `Documentation/Luban.md` 的建议不同（`await` 前置 + 失败复位），并留下 R4-LBN-005。
- **旧结论复核**：`Documentation/Luban.md` §7.8 的六条（不重建核心内建适配层、不公开 `ILubanDataProvider`、不做 `bin-offset`/分片、不支持三个 JSON target、不做 Roslyn generator、不移入核心 Runtime 树）全部维持；本轮未发现推翻证据。
- **开口项**：R4-LBN-005（引导阶段所有权）建议实施轮按「本次创建 vs 加入在途」修；R4-LBN-002/003/004（测试缺口）与 R4-LBN-008（写盘原子性）进实施批次 4；R4-LBN-009/015 需真机/非 Windows 探针。
- **第三方对照**：Luban 5.1.0 生成物（`Tables` 在 `namespace cfg`、每表一文件、map 表 `DataMap` 运行期 `Dictionary`、带索引 list 无公开 `DataMap`）与 `com.code-philosophy.luban` v1.2.0 运行时；TEngine 的 `bin-sharded`/懒加载是项目级扩展，不是 vanilla 5.x 内置——本包 v1 不做是合理取舍。`SimpleJSON` 命名空间漂移已在「已知限制」写明。
- **残留**：IL2CPP / Android / iOS 未验证；真实 YooAsset TextAsset/RawFile 与裸消费方 PackageCache 已在 2026-10-25 探针跑通；`LubanProcessRunner` 输出编码未验证；扩展包无发布 tag；Editor 公开面无基线。

### Camera（T1 全量）

- **范围与输入**：`Runtime/Camera/` 8 个 `.cs` + `Cinemachine/` 3 个 + 消息类型；README、`Modules/Camera.md`、`Documentation/Cinemachine.md`；`Tests/Runtime/Camera/` 7 个 fixture（167 例）；`Samples~/Cinemachine`；`Editor/Diagnostics/Panels/OverviewDiagnosticPanel.cs`；ModuleBoundary/PublicApiBaseline/AutoInit。
- **18 类留痕**：A1 命中（Subscribe 的 token 说法、README 的 Brain 销毁后探测读、同一目标中途 cut 的事件顺序，R4-CAM-001/003）；A2 扫过（后端 internal，无门面完备性问题）；A3 扫过（手读 XML，未跑 check-docs）；B1/B2/B3 扫过（Destroy 先置空、静态事件成对退订、消息异常隔离）；B4 命中-已文档化（主线程无断言，`Modules/Camera.md:77` 已登记待裁）；B5 扫过（死槽位惰性剪除，文档明示）；B6 扫过（ResetForNewSession 退订 + GetBlendOverride 恢复）；C1 扫过（无 ticker）；C2 命中-已核对（CM 3.1.7 `ActiveBlend=null`、`PrioritySettings`、HardIn/Out 与文档一致）；D1 扫过（CM 类型只在 Camera 模块）；D2 命中（R4-CAM-004）；D3 命中（缺门面/消息面中途 cut、已销毁目标路径、`CameraInfo.ToString`）；D4 命中（死槽位静默、Brain 销毁后探测读过宽，R4-CAM-002/005）；D5 不适用（扩展包已并入）；E 扫过（Ordinal、同 id 异实例抛异常）；F 命中（R4-CAM-001/002/005）。
- **前轮修复偏差**：R3-CAM-001/002/003、R3-CIN-001..008 全部关闭；R3-CIN-005 为「已修/部分」——强制 cut 已有 PlayMode 用例，ABA/句柄代际声明随 string-id 重设计消失，无替代用例（特性已不存在）。
- **旧结论复核**：「核心不引用 Cinemachine」被 2026-10-22 用户裁定推翻并已改文档；「编排归 Cinemachine、核心只统一入口」当前是「CM 是唯一内置后端、后端 internal」——维持。Timeline 接管仍只有推理与文档。
- **第三方对照**：Cinemachine 3.1.7 源码本轮实读（`BlendManager`、`ProcessActiveCamera`、`GetBlendOverride`、`PrioritySettings`）；UE/Godot 沿用 2026-10-24 二手结论，本轮未复核。
- **残留**：真实 `PlayableDirector` 接管/释放、R4-CAM-001/002/005 的运行期探针、关闭域重载、IL2CPP、`Subscribe` 重载编译探针。

### UI（T1 全量）

- **范围与输入**：`Runtime/UI/` 34 个 `.cs`（门面/Impl/Controller/Data/Tip/View/HUD/排序/遮罩/弹窗/Bootstrap/RootNode/SafeArea/StateSnapshot）；README、`Modules/UI.md`；`Tests/Runtime/UI/` 48 文件（约 9.7k 行）；`Samples~/Example/UISample.cs`；`Documentation/FairyGUI.md`。
- **18 类留痕**：A1 命中（SetInstance 驱动器、排序常量/重排、BindToLocalizedText、AllPanelsClosed XML、§11 围栏，R4-UIA-003/004、R4-UIB-003/006/007）；A2 扫过（门面完备性守卫通过）；A3 扫过；B1 扫过；B2 命中（Dispose × 在途打开、owner=null 遮罩、HUD 并发附着、OnPoolRecycle 异常，R4-UIA-001、R4-UIB-001/002、R4-UIA-002）；B3 命中（provider 换绑/HideHud 未隔离，R4-UIB-004）；B4 不适用（README 明确无断言）；B5 命中（HUD 孤儿、遮罩误剪）；B6 命中（在途续体跨代，R4-UIA-001）；C1 扫过；C2 命中（层交互 restore，R4-UIA-005）；D1 扫过；D2 扫过；D3 命中（R4-UIA-002/006 与多条部分修复）；D4 命中（img_ 静默、遮罩句柄静默作废）；D5 扫过；E 扫过；F 命中（OnAllPanelsClosed 两面不一致、遮罩观测面）。
- **前轮修复偏差**：R3-UIA-001..010 中 8 条已修/文档化，2 条部分（UIA-007 只有回池次数红证、UIA-010 文档反向回归）；R3-UIB-001..013 中 8 条已修/文档化，4 条部分（UIB-008 文案仍错、UIB-009 类型不匹配仍静默、UIB-012 仍有零覆盖、UIB-004 已修）。R3-UIB-003 已真正修好（停更 HUD 目标丢失）。
- **旧结论复核**：FairyGUI 结论不变（不能接成 `IUIManager` 后端）；`UIManagerImpl.Dispose` 半拆窗口、`IUIManager : IDisposable` 可绕过门面、Destroy 不发关闭消息均为已文档化/未决项，本轮未重开。
- **第三方对照**：GameFramework 无源码副本，本轮未做；FairyGUI 沿用 `Documentation/FairyGUI.md`；UniTask 只核对同步续体行为。
- **残留**：R4-UIA-001/005、R4-UIB-001/002 的 PlayMode 红证；R3-UIB-005/007/012 的覆盖缺口；真实 TMP/预制体/移动端 safeArea。

### Diagnostics（T2 增量）

- **范围与输入**：`Runtime/Diagnostics/` 10 个 `.cs`、`Editor/Diagnostics/`（Window/Model/FrameworkPanels/19 Panels/Rendering）、README、`Modules/Diagnostics.md`、Runtime/Editor 测试、各模块诊断消费者。
- **18 类留痕**：A1 命中-低（静默忽略措辞、Order 异常语义未写）；A2 已文档化（Clear 不恢复框架页签）；A3 扫过；B1 不适用；B2 扫过；B3 命中（Order 未隔离，R4-DIAG-001）；B4 已文档化（不加第四份断言）；B5 扫过；B6 已文档化；C1 扫过；C2 扫过；D1 扫过；D2 不适用；D3 命中（R4-DIAG-001/002）；D4 已文档化；D5 不适用；E 去重按引用；F 已对齐。
- **前轮修复偏差**：R3-DIAG-001 已修（SafeTitle 两处 + 用例）；R3-DIAG-002 文档化（不新增恢复 API）；R3-DIAG-003 已修（提示两行 + 删计数）。无回归。
- **旧结论复核**：按实例幂等、不加主线程断言、无反射发现、纯数据契约全部维持。
- **第三方对照**：无直接同类库，本轮未做。
- **残留**：R4-DIAG-001/002；包外宿主 Clear 后恢复结构上不可达，只能文档化。

### Scene（T2 增量）

- **范围与输入**：`Runtime/Scene/` 4 个 `.cs`、README、`Modules/Scene.md`、Runtime/Editor 测试、Asset 场景路径（`YooAssetManagerImpl`/`YooAssetSceneLoadSource`/假件）。
- **18 类留痕**：A1 扫过（R3-SCN-003 已修；R4-SCN-001 的 `Scene.name` 未守卫）；A2 已文档化（能力缺失返回 default）；A3 扫过；B1/B2 不适用；B3 命中（R4-SCN-001）；B4 已文档化（与 Asset 同口径不设断言）；B5 已修（R3-SCN-001 真语义成立）；B6 不适用；C1 扫过；C2 需实测（`default(Scene).name`）；D1 已修（LogCategories.SceneManager）；D2 不适用；D3 命中（R4-SCN-001 无用例）；D4 已修/文档化；D5 文档化；E/F 扫过。
- **前轮修复偏差**：R3-SCN-001 已修且真适配器语义成立（登记项存加载时 `Scene` 快照，YooAsset 先 Release 不再导致比对恒 false；假件 `Release()` 置空，回归不再是结构性假绿）；R3-SCN-002 已修（回收段 try/catch）；R3-SCN-003 已修（日志分类 + 依赖说明）。
- **旧结论复核**：无引用计数、外部卸载感知、同名规避、无场景栈/加载界面/传参通道均维持。
- **第三方对照**：YooAsset 2.3.18 源码本轮实读（`SceneHandle.SceneObject`、`ResourceManager.OnSceneUnloaded`）；Addressables 未读。
- **残留**：真 YooAsset 场景卸载序手工探针（R3-PRB-14）、R4-SCN-001 的 `Scene.name` 引擎行为、Asset 场景方法主线程守卫缺口（SCN-005）。

### Config（T1 全量）

- **范围与输入**：`Runtime/Config/` 19 个 `.cs`、README（619 行）、`Modules/Config.md`、`Tests/Editor/Config`（11 文件）、`Tests/Runtime/Config`（2 文件）、Luban 桥接/生成物、Asset/Serialize 接缝。
- **18 类留痕**：A1 命中（CONF-001 已修；null-loader 契约不一致、SO 资产流程未端到端，R4-CONF-001/003）；A2 命中（在途/已加载分支绕过 null 校验，R4-CONF-001）；A3 扫过；B1 扫过（Destroy 后可重建）；B2 扫过；B3 命中（类型不符句柄泄漏，R4-CONF-002）；B4 扫过；B5 命中（R4-CONF-002）；B6 扫过（Reset 未退订旧 impl，残留）；C1 扫过；C2 命中-已修+残留（CSV culture 已修，BOM 未实测）；D1 扫过；D2 已文档化；D3 命中（R4-CONF-003）；D4 命中（R4-CONF-001/002/004）；D5 命中（CONF-014 低内存假 null 仍 `未做`）；E 命中（R4-CONF-004）；F 扫过。
- **前轮修复偏差**：R3-CONF-001/002/004..011 已修；R3-CONF-003 已修但引入 R4-CONF-001（null loader NRE）；R3-CONF-002 的 SO 端到端零覆盖（R4-CONF-003）。
- **旧结论复核**：`ConfigTypeHelper` 失败不缓存、Global/Table 双身份独立、CSV 方言、`Destroy` 不取消在途、`Register` 静默覆盖、`ConfigTable.Data` 可变引用均维持。
- **第三方对照**：Luban 5.1.0 生成物实读，四种表形状与扩展包 README 逐项对应；核心契约未被扩大（除 R4-CONF-004 的隐式前提）。
- **残留**：R3-PRB-02（真包低内存）/03（IL2CPP）、CSV BOM、R4-CONF-003 资产往返探针。

### Asset（T1 全量）

- **范围与输入**：`Runtime/Asset/` 23 个 `.cs`、README（640 行）、`Modules/Asset.md`、Tests Editor/Runtime 各 7 文件、Bootstrap/DefaultGameLauncher/Scene 接缝、YooAsset 2.3.18 源码。
- **18 类留痕**：A1 命中（R4-AST-004/007）；A2 命中（热更 API 对初始化失败包的异常类型，R4-AST-003）；A3 扫过；B1 命中（SetInstance 旧实例、阶段所有权，R4-AST-001/002）；B2 命中（R4-AST-006）；B3 命中（R4-AST-005）；B4 扫过（门面断言 + 文档）；B5 命中（R4-CONF-002 跨模块句柄泄漏、R4-AST-004 池常驻）；B6 命中（R4-AST-006）；C1 命中-低（InvokeIsolated 分配，R4-AST-005）；C2 命中（R4-AST-003 的 DEBUG 门控、枚举重排已文档化）；D1 扫过；D2 扫过；D3 命中（R3-AST-002 接线零覆盖、R4-AST-006）；D4 命中（R4-AST-001/003/004/005）；D5 扫过（预检/下载绑定 `暂不做`）；E/F 扫过。
- **前轮修复偏差**：R3-AST-001 文档化（0.3.0 未分发）；AST-002/003/005/006/007/008/009 已修；AST-004 已修（文档分支）但未覆盖「包存在但初始化失败」（R4-AST-003）；AST-002 的 Reset→Detach 接线零覆盖。
- **旧结论复核**：`Destroy()` 不卸载 YooAsset 资源、包复用、EditorSimulate 不静默降级、Web/Custom 不映射、预检/下载 `暂不做` 均维持，但 `Destroy()` 的 README 表述自相矛盾（R4-AST-007）。
- **第三方对照**：YooAsset 2.3.18 源码实读——`GetAssetObject<T>` 为 `as`（支持 R4-CONF-002 的「有效句柄 + null」）、`ResourceManager.OnSceneUnloaded` 自行 Release（支持 Scene 侧只清表）、`DownloaderOperation` 直接 Invoke（支持逐订阅者隔离）、`DebugCheckInitialize` 为 `[Conditional("DEBUG")]` 抛裸 Exception（支持 R4-AST-003）。
- **残留**：真 YooAsset 端到端（含 EditorSimulate `SimulateBuild`）、lowMemory 回调线程、R3-PRB-08 序列化读回、R4-AST-001/002/003/006 探针。

### 工程面专项（Tools / 守卫 / 示例 / 包元数据 / 发布）

- **范围与输入**：`Tools/run-tests.ps1` / `check-docs.ps1` / `check-samples.ps1`；11 个核心 Architecture 守卫 + 3 个基础设施文件；`Tests/Editor/Performance` 6 个基准；四个 `Samples~`；两个 `package.json`；`.gitattributes` / `LICENSE` / `.meta`；根 README 与包 README 四张表；`CHANGELOG` 迁移表与行为变更节；Luban 六个守卫。
- **18 类留痕**：A1 命中（R4-ENG-001/002/003/009、R4-LBN-011/014）；A2 扫过（门面完备性 11 处守卫覆盖）；A3 命中-射程缺口（Luban XML 告警不在任何门禁，R4-ENG-004）；B1 扫过；B2 扫过；B3 扫过；B4 扫过；B5 扫过；B6 扫过；C1 扫过（基准只测量，不设零 GC 断言）；C2 命中（run-tests 无超时，R4-ENG-005）；D1 命中（Editor 公开面无基线，R4-ENG-006）；D2 扫过；D3 命中（R4-ENG-004/005、Luban 测试缺口）；D4 命中（文档与脚本不一致）；D5 扫过；E/F 扫过。
- **新发现**：R4-ENG-001..009（§6 工程面）。
- **前轮修复偏差**：R3-CIN-008 由「Cinemachine 并入核心」结构性关闭；R3 §12.3 的「文档示例可编译守卫」已由 `check-samples.ps1` 落地；「依赖清单反查」仍未做。
- **旧结论复核**：CI 暂不接入（`CI.md` 三条重开判据未触发）；包在子目录/`Tests` 不移出/不做 Git 钩子均维持；Editor API 基线是否值得建，列为 §9 R4-ADJ-02。
- **第三方对照**：无（工程面为本仓自定流程）。
- **残留**：Luban XML 告警无机械覆盖、两个 Editor 程序集无 API 基线、`run-tests` 无超时、0.4.0/Luban tag 未发。

### Input（T1 全量）

- **范围与输入**：`Runtime/Input/` 14 个 `.cs`（含 `Default/InputSystemProvider`）、README、`Modules/Input.md`、Runtime 测试、Update/Settings/Event/Message 接缝。
- **18 类留痕**：A1 命中（R3-INP-001 依赖表已补；INP-003 raw 复合限制已写）；A2 命中（`GetButtonPressDuration` 忽略 `playerId`，R4-INP-001）；B1 命中（provider Dispose 半状态已修）；B2/B3/B4/B5 扫过；B6 扫过（INP-002 会话复位已修）；C1 命中（死字段 `_currentMapName` 已删）；C2 命中（raw 复合读恒默认，已文档化）；D1/D2 扫过；D3 命中（振动/热插拔零覆盖；多玩家时长零覆盖，R4-INP-001）；D4 命中（INP-002/003/004 已修；R4-INP-001）；D5 不适用；E 扫过；F 命中（同帧按下+释放已修；多玩家时长口径不一致）。
- **前轮修复偏差**：R3-INP-001..006 全部已修/文档化；无回归。`BindToContext` 返回合并句柄、11 个 `Observe*` 转发已在位。**新发现 R4-INP-001（P2）**。
- **旧结论复核**：Provider 可整体替换（Rewired 等）、Input System 只在 `Default/` 命名空间、无接口改动均维持。
- **第三方对照**：Rewired 等整体替换边界沿用；无源码对照。
- **残留**：振动/设备热插拔、非编辑器 PlayerLoop 子系统名、跨会话手工验收。

### Localization（T1 全量）

- **范围与输入**：`Runtime/Localization/` 约 7 个 `.cs`、README、`Modules/Localization.md`、Runtime/Editor 测试、Input/Message/Asset 接缝。
- **18 类留痕**：A1 命中（LOC-001 样例已修、LOC-008 陈旧注释已订正；R4-LOC-001 的替换值二次解释）；A2/A3 扫过；B1 命中（LOC-002 阶段二次执行所有权已修）；B2 扫过；B3 命中（LOC-003 格式说明 `{` 已修）；B4 扫过；B5 扫过；B6 扫过（LOC-002 相邻面已修）；C1 命中（R4-LOC-001 路径分配，频度低）；C2 扫过；D1/D2 扫过；D3 命中（LOC-007 五处零断言已补）；D4 命中（LOC-004/005/006 已修/文档化）；D5 扫过；E/F 命中（R4-LOC-001）。
- **前轮修复偏差**：R3-LOC-001..008 全部处置；无回归。**新发现 R4-LOC-001（P3）**。
- **旧结论复核**：不做「永不抛解析异常的 CompositeFormat」、`TextAsset` 源兼容、并发切换不引入代次作废（R3-ADJ-03）均维持。
- **第三方对照**：.NET CompositeFormat 语义；无新增推翻。
- **残留**：Unity Mono/IL2CPP 的格式解析边界、阶段二次执行后的 Shutdown 用例。

### Audio（T1 全量）

- **范围与输入**：`Runtime/Audio/` 约 17 个 `.cs`、README、`Modules/Audio.md`、Runtime 测试、Update/Pool/Asset 接缝。
- **18 类留痕**：A1 命中（AUD-001 AudioMixer 配方已删、AUD-006/007 文档已订正）；A2 扫过；B1 命中（AUD-005 直接 Dispose 后门面卡死已修）；B3 命中（AUD-002 假 null、AUD-003 Sweep break 已修；R4-AUD-001 的 Free 槽坏源）；B5 命中（死槽位/ticker 已修；R4-AUD-001）；B4 扫过；B6 扫过；C1/C2 扫过（StartingTimeout 平台前提残留）；D1/D2 扫过；D3 残留（isPlaying 平台、AudioListener.pause 无真机；宿主外部销毁零覆盖，R4-AUD-001）；D4 扫过；D5 扫过；E 命中（AUD-004 `AudioHandle` 相等语义已补）；F 扫过。
- **前轮修复偏差**：R3-AUD-001..007 全部已修/文档化；AUD-003 为「部分」——`Sweep` 已改，但 Free 槽坏源残留（R4-AUD-001）。**新发现 R4-AUD-001（P2）**。
- **旧结论复核**：暂停/恢复、虚拟声部、通道音量、3D 定点、无 Mixer 路由均维持。
- **第三方对照**：AudioMixer 边界已文档化；无新增。
- **残留**：`isPlaying` 平台前提、AudioListener.pause、低帧率安全系数、路由扩展点。

### Save（T1 全量）

- **范围与输入**：`Runtime/Save/` 约 10 个 `.cs`、README、`Modules/Save.md`、Runtime 测试、Serialize/File/Data 接缝。
- **18 类留痕**：A1 命中（SAVE-002/004/006 文档已订正；R4-SAVE-001 计数过期）；A2 命中（SAVE-004 查询面已修；R4-SAVE-002 单槽删除面不认备份）；B3 命中（SAVE-003 已修）；B4 命中（SAVE-004 线程表述已订正，双路径仍 `未做`）；B5 命中（SAVE-001/005 已修；R4-SAVE-002）；B2 不适用；B6 扫过；C1/C2 扫过；D1/D2 扫过；D3 命中（可抛 IO 缝仍缺）；D4 命中（SAVE-002/005 已修）；D5 命中（R3-ADJ-08 承接）；E/F 命中（SAVE-006 已修；R4-SAVE-002）。
- **前轮修复偏差**：R3-SAVE-001..006 全部处置；R3-SAVE-001 为「部分」——单槽删除遗漏（R4-SAVE-002）；R3-ADJ-08 仍 `未做`；新 R4-SAVE-001（维护文档计数 5→8）、R4-SAVE-002（P2）。
- **旧结论复核**：原子写、备份、删除先清备份、序列化移出主线程、会话边界只丢引用均维持。
- **第三方对照**：平台存档 SDK 对照沿用；无新增。
- **残留**：R3-ADJ-08 双路径、可抛 IO Provider 测试缝、RecoverAsync 主线程探针。

### Settings（T1 全量）

- **范围与输入**：`Runtime/Settings/` 约 18 个 `.cs`、README、`Modules/Settings.md`、Runtime 测试、Serialize/File/Asset 接缝。
- **18 类留痕**：A1/A2 命中（SET-001 装饰器异步能力已修、SET-003 线程契约已订正；R4-SET-001 读侧线程口径不一致）；B4 命中（SET-003 管理写入口已补断言；R4-SET-001）；B6 命中（SET-004 PauseNotifier 代际已修）；C2 命中（SET-001 已修；R4-SET-001 读侧池线程解析）；D4 命中（SET-005/006 目录/静默回默认已修/文档化）；A3/B1/B2/B3/B5/C1/D1/D2/D3/D5/E/F 扫过。
- **前轮修复偏差**：R3-SET-001..006 全部处置；SET-001 有回归修复（`EncryptedSettingsStore.SaveAsync` 快照）与用例，但读侧留下 R4-SET-001（P2，需复核 JSON 线程亲和性）。
- **旧结论复核**：声明式字段、POCO + SettingRef、加密装饰器、会话边界不替使用方 Dispose 均维持。
- **第三方对照**：无新增。
- **残留**：R3-SET-001 的并发竞态、旧管理器 `wantsToQuit` root（R3-ADJ-09 `不做`）。

### Bootstrap（T1 全量）

- **范围与输入**：`Runtime/Bootstrap/` 约 5 个 `.cs`、README、`Modules/Bootstrap.md`、Runtime/Editor 测试、Pipeline/Asset/Input/UI/Settings 接缝、`DefaultGameLauncher`。
- **18 类留痕**：A1 命中（BST-002 必挂根对象已写）；A2/A3 扫过；B2 已文档化（阶段 Shutdown 逆序）；B3 命中（BST-001/003 已修）；B4 扫过；B5 扫过；B6 已修（代际）；C1/C2 命中（BST-002 根对象约束已文档化）；D1/D2 扫过；D3 命中（GameLauncher 真生命周期 3 例）；D4 扫过；D5 命中（Asset/Data/Save 阶段所有权已修）；E/F 扫过。
- **前轮修复偏差**：R3-BST-001/002/003 全部已修/文档化；无回归。`Shutdown` 先取消在途、快照登记表、逐阶段隔离；`RunAsync` OCE 携带链接令牌。
- **旧结论复核**：内置阶段可重复执行、谁初始化谁清理、「Shutdown 不修改登记表」均维持。
- **第三方对照**：五框架启动表沿用；无新增。
- **残留**：不响应取消的在途阶段（已知边界）、子对象启动器、Localization ExecuteTwice。

### Timer（T1 全量）

- **范围与输入**：`Runtime/Timer/` 约 6 个 `.cs`、README、`Modules/Timer.md`、Runtime 测试、Update/UI 接缝。
- **18 类留痕**：A1 命中（TMR-003 文档化、R3-TMR-003 已订正）；A2/A3 扫过；B1/B2/B3/B4/B5 扫过；B6 扫过；C1 扫过（槽位数组零指针追逐）；C2 命中（TMR-001 跨表补扫已修、TMR-003 粗档提前触发已文档化）；D1/D2 扫过；D3 命中（TMR-002 Clear 恢复路径已补用例）；D4 扫过；D5 命中（TMR-003 干净修法 `暂不做`）；E/F 扫过。
- **前轮修复偏差**：R3-TMR-001 已修（`TimerCore.Sweep` 跨表补扫 + `TimerTierTests` 红证）；R3-TMR-002/003 已处置；无回归。
- **旧结论复核**：档位语义、锚点、回调内新建、无 `ITimerManager` 接口均维持。
- **第三方对照**：无新增。
- **残留**：TMR-003 实时探针、跨表定时器 PlayMode 红证（已有用例）、Update Clear 恢复路径。

### T2 增量复审（10 模块；Diagnostics / Scene 见上方详录）

> 方法：读 R3 后变更文件全文 + 上下文、R3 P2 状态复核 + 代码抽样；未命中新 P0/P1 的模块只列结论与残留。

- **Update**：变更集中在自动注销补发 `OnDisable`、`ProcessImmediate` 形参放宽、`IsRegistered`、会话复位代际。R3-UPD-001..004 全部有处置（CHANGELOG `:1350-1355`、`:406-413`；`UpdateSchedulerTests.cs:1900-2030` 六例锁定自动注销边沿）。18 类：A1 命中（R4-UPD-001 的 SetInstance 句反了）；A2 扫过；B2/B3 扫过；B4 扫过；B6 已修；C1/C2 扫过；D3 有新用例；D4 的 `ProcessImmediate` 静默已文档化；E/F 扫过。**新发现 R4-UPD-001（P2，文档回归）**。残留：非编辑器 PlayerLoop 子系统名、跨会话 `_isIterating` 反射探针。
- **Message**：变更集中在 `TryBindToDestroy` 的 None/已取消拆分、`BindToContext` 合并句柄、XML 示例、台账。R3-MSG-001..005 全部处置（CHANGELOG `:573`、`:1129`；`DestroyTokenBindingTests`、`InputManagerReactiveTests`）。18 类：A1 已闭合；A2/B1/B2 扫过；B3 扫过；B5 键值缓冲结论维持；B6 已修；D3 的 Input 接缝有测试；D4 的 None 令牌返回原句柄；E 的 Key 全局性仍未写进 README（R3 开口项，非新发现）。**本轮未发现新 P0/P1/P2**。残留：Key 全局性、Release 无主线程防线。
- **Data**：变更集中在快照携带脏集合、单块快照 format、`OnClear` 隔离、会话复位。R3-DATA-001..005 全部处置（CHANGELOG `:414-421`、`:615-618`；`DirtyTrackingTests` 三条）。18 类：A1 已闭合；B3 已修；B5 扫过；D3 有红证；D4 已修；E/F 扫过。**本轮未发现新 P0/P1/P2**。残留：`DataSnapshot.Factory` 跨会话、DATA-011 边界。
- **Pipeline**：变更集中在容器 null 元素、非有限超时、取消路径 `Settle` 移入 finally、`_stageCtx.Owner` 置 null。R3-PIPE-001..004 全部有代码 + 测试（`Pipeline.cs:173`、`ParallelStage.cs:138-142`、`SequenceStage.cs:135-138`）。18 类：A2/D4 已闭合；B3 已修；B5 已修但 Owner 清理时序有嵌套聚合疑问（R4-PIPE-001）；C2 已修；其余扫过。**新发现 R4-PIPE-001（P3，需复核）**。残留：并发串台、容器顺序复用、超时放弃阶段的令牌 ODE（R3 残留）。
- **Pool**：变更集中在租出回调异常时走丢弃回调（R3-POOL-001）。`PoolTests.Get_RentCallbackThrows_InvokesDiscardCallback` 在位。18 类：B2 已闭合；B3 在 `Return` 满池路径仍不对称（R4-POOL-001）；D4 的 onDestroy 文档未写租出失败触发（R4-POOL-001）；B5/B6 会话复位维持；D3 注册计数断言放宽为「≤+1」；其余扫过。**新发现 R4-POOL-001（P3）**。残留：跨会话手工验收、generator 返回 null。
- **File**：变更集中在 `OSXServer` 分支、移动 Streaming 归一/沙箱、Destroy 后 crypto 闩锁、权限契约、计数。R3-FILE-001..005 全部处置（CHANGELOG `:557-563`、`:1382`）。18 类：A1 命中（R4-FILE-001 的 null 契约）；A2 已闭合；B1 已修；B4 文档化；C2 已修；D3 有缝用例；D4 文档化+README null 契约矛盾（R4-FILE-001）；E 已归一；F 扫过。**新发现 R4-FILE-001（P3）**。残留：macOS Server 构建、Android/iOS Streaming、权限异常类型（R3-PRB-04 仍 `未做`）。
- **Log**：变更集中在 `Flush` 死亡 sink 摘除、捕获口径、分类计数、LogFileSink 文档。R3-LOG-001..004 全部处置（`LogManagerImpl.cs:432-434`；`LogCaptureTests.Flush_DeadFileSink_IsDroppedAndCounted`；CHANGELOG `:551`、`:1390`）。18 类：A1 已闭合；B3/D4/F 已修；B6/C2/D3 有记录；其余扫过。**本轮未发现新 P0/P1/P2**。残留：毫秒基名碰撞、session/seq 窄窗、域重载文件句柄。
- **Serialize**：变更集中在多态/`Default`/`type` 形参口径、会话复位。R3-SER-001..006 全部处置（CHANGELOG `:1385`、`:1358`、`:449-450`）。18 类：A1 命中（R4-SER-001 的 ResetForNewSession 文案）；A2 已修；B3 扫过；D4 命中（R4-SER-001）；D3 的 AutoInit 自证已修；F 已对齐；其余扫过。**新发现 R4-SER-001（P3，需复核）**。残留：JsonUtility 畸形 JSON、多态边界、`Register(null)`/`Unregister(null)` 零断言。

### T3 紧凑留痕（3 模块）

- **Event**：读 `EventStream.cs`、`DisposableBag.cs`、`SubscriptionTracker.cs`、`Internal/*` 全部变更文件。R3-EVT-001 理由订正、EVT-002 `onEmpty` 隔离均有证据（`EventStreamTests.OnEmpty_Throws...`；CHANGELOG `:579`、`:1388`）。18 类：A1 命中-已修；B3/D4 已修；B4 不加主线程断言（维持裁定）；B6 的静态池只存清空后的 List/Node，无跨会话引用；D3 的 `onEmpty` 与派发中触发两处缺口仍在；E/F 扫过。**本轮未发现新 P0/P1/P2**。残留：`SubscriptionTracker` 的 Editor-only 计数跨会话不自动清零（测试夹具自行 `Reset`，非生产面）。
- **Lock**：读 `LockManager.cs`、`LockStateSnapshot.cs`、README。R3-LOCK-001（先绑定后派发）与 LOCK-002（包内表述）均有证据（`LockInternalStateTests`；CHANGELOG `:597`、`:1371`）。18 类：B2/B5 已修；A1 已修；D3 的 ABA 红证仍缺；E/F 已文档化/已测。**本轮未发现新 P0/P1/P2**。残留：池化 ABA 无测试、跨对象自动退订。
- **Reactive**：读 `ReactiveProperty.cs`、`IReactivePropertyWriter.cs`、`ReactiveList.cs`、`ReactiveDictionary.cs`、README。R3-RCT-001（双承诺文档化 + 回归锁）、RCT-002（台账）均已处置（CHANGELOG `:1385`）。18 类：A1/F 已闭合；B2 的 RCT-001 维持文档化；B4 不加主线程断言（维持）；C1 的装箱量仍无法用本仓尺子实测；D3 的容量负值/Sort 部分重排用例仍缺。**本轮未发现新 P0/P1/P2**。残留：RCT-010 装箱无法实测、RCT-006 部分重排未实测。

## 9. 待裁的一次性裁定建议

> 审计期只写建议；台账状态由实施轮经用户确认后更新。状态词按 `Modules/README.md` 的六词表。

| ID | 模块 | 问题 | 推荐 | 理由 | 落点 | 重开判据 |
|---|---|---|---|---|---|---|
| R4-ADJ-01 | Luban | 扩展包没有 `Documentation/Modules/Luban.md`，长期结论在 `Documentation/Luban.md` §7.8 | `暂不做` | 新建 `Modules/Luban.md` 会撞 `check-docs` 的模块集（来自核心 `Runtime/` 直接子目录）与 `Modules/README.md` 文件表；现有 `Luban.md` §7.8 已承担「不做/暂不做」结论 | `Documentation/Modules/README.md` 或 `Luban.md` 注明台账位置 | Luban 需要与核心模块同构的台账门禁时 |
| R4-ADJ-02 | 工程面 | 两个包的 Editor 程序集公开面无 API 基线（核心 5 类型 / Luban 3 类型） | `未做` | Editor 面不是使用方运行时契约；加基线要另写 EditMode 反射 fixture，收益低于成本 | `Documentation/Ledger.md` | Editor 公开面出现外部消费者或再次增长 |
| R4-ADJ-03 | 工程面 | `check-docs` 不覆盖 Luban 的 XML 告警/源文件参与/.meta/台账/README 树 | `暂不做`（维持 Ledger 现有条目） | 包内六类守卫已覆盖大部分；XML 文档漂移风险低；泛化路径过滤是独立改造 | `Documentation/Ledger.md` | Luban 出现文档漂移事故，或 `check-docs` 扫描根可配置 |
| R4-ADJ-04 | 发布 | 0.4.0 未切版本小节、未 bump `package.json`、无 tag；Luban 0.1.0 无 tag | `未做`（用户执行） | 版本与 tag 是发布流程动作，不是代码缺陷；切口径见 §7.4 | `Assets/XFramework/CHANGELOG.md`、`package.json`、tag | 发布流程启动 |
| R4-ADJ-05 | 工程面 | `run-tests.ps1` 的 Unity 调用 `Start-Process -Wait` 无超时 | `待裁` | 加可选 `-TimeoutSeconds` 成本低，但会改变本地/未来 CI 的失败语义；当前无 CI，先登记 | `Tools/run-tests.ps1`、`Workflow.md` | 接入 CI 或出现一次挂死事故 |
| R4-ADJ-06 | Luban | `WriteAtomically` 非真正原子、manifest 最后写，IO 失败留部分文件 | `未做` | 生成物可重跑，数据不丢；改成临时目录整体替换是独立小改造 | `Editor/LubanIntegrationWriter.cs`、包 README 已知限制 | 出现一次真实半套生成事故 |
| R4-ADJ-07 | UI | `UISorting.MaxIndexInLayer` 31→30 未进行为变更表 | `未做`（docs-only） | 只是文档放置问题，不改行为 | `CHANGELOG.md` 行为变更节 | 发版前必做 |
| R4-ADJ-08 | Luban | `DataRoot`/`ExtraArguments` 的「原样」措辞与 `Trim` 实现不符 | `未做`（docs-only） | `Trim` 行为无害；改文档比改实现便宜 | 包 README | 出现依赖首尾空白的真实用法 |
| R4-ADJ-09 | Luban | 主线程断言只覆盖 4 个入口，README 说「所有公开 API」 | `未做`（docs 收窄或补守卫，二选一） | 查询面短读不构成并发风险；补守卫会扩大 Editor-only 代码面 | 包 README / `LubanConfig.cs` | 出现跨线程查询的真实用法 |
| R4-ADJ-10 | Config | `ConfigTable<T>` 的「字典键 == 行 Id」从未写进契约 | `未做`（docs-only） | 加 O(n) 校验会拖慢大表构造；当前 Luban 生成器同源 | `ConfigTable.cs` XML、Config README | 出现一次真实的键分叉注册事故 |
| R4-ADJ-11 | Save | `RecoverAsync` 双路径线程改造（R3-ADJ-08 承接） | `未做` | 文档已订正；改造要动线程路径，需单独计划 | `Modules/Save.md` | 出现必须在恢复扫描期间保证「不切回主线程」的真实使用方 |

> 上述 `未做/暂不做/待裁` 在写入台账前须经用户确认；本报告只登记建议与理由。公开 API / 破坏性变更类条目（R4-CAM-003 若选择补 API、R4-ADJ-02 若选择建基线）须单独确认。

## 10. R4 分步实施计划

> 审计轮只出计划；实施轮按「一行一提交、测试与修复同提交、行为变更同提交写 CHANGELOG」执行。命令缩写：`PM(x)` = `run-tests.ps1 -Filter "x" -Platform PlayMode`；`EM(x)` = EditMode；`ALL(x)` = 双平台；`FULL` = 全量；`DOC` = `check-docs.ps1 -Enforce`；`BUILD` = 两个核心 csproj；`API` = 公开 API 基线流程。每条 P1/P2 修复先写「改前必红」。

### 批次 0：P1 与修复回归（先止血）

| # | 提交信息 | 覆盖 | 验证 |
|---|---|---|---|
| 0.1 | `fix(UI): Dispose 后不再让在途打开续体关闭面板 (R4-UIA-001)` | `UIManagerImpl` / `UIViewBase`、PlayMode 红证、CHANGELOG | `PM(UI)`；改前必红；`DOC` |
| 0.2 | `fix(UI): null owner 遮罩不被剪枝误删 (R4-UIB-001)` | `UIManagerImpl.PruneOverlayHolds`、PlayMode 红证 | `PM(UI)`；改前必红；`DOC` |
| 0.3 | `fix(UI): HUD 同目标并发附着去重与孤儿清理 (R4-UIB-002)` | `UIHudManager.AttachAsync/DetachAll/RecycleHud`、PlayMode 红证 | `PM(UI)`；改前必红；`DOC` |
| 0.4 | `fix(Config): 在途分支的 null loader 先校验 (R4-CONF-001)` | `ConfigManagerImpl` 两处入口、回归用例、CHANGELOG | `PM(Config)`；改前必红；`DOC` |

### 批次 1：P2 正确性与生命周期

| # | 提交信息 | 覆盖 | 验证 |
|---|---|---|---|
| 1.1 | `fix(Config): 类型不符资源释放句柄并区分错误 (R4-CONF-002)` | `ConfigTextLoader` / `ScriptableObjectLoader`、假 AssetManager 用例 | `PM(Config)`；改前必红；`DOC` |
| 1.2 | `fix(Config): 在途登记按 (Type, Kind) 分槽 (R4-CONF-005)` | `ConfigManagerImpl._inFlightLoads`、三调用方用例 | `PM(Config)`；`DOC` |
| 1.3 | `fix(Asset): SetInstance 处置旧实例并退订静态事件 (R4-AST-001)` | `AssetManager.SetInstance`、假件/反射用例、CHANGELOG | `PM(Asset)`；改前必红；`DOC` |
| 1.4 | `fix(Asset): 引导阶段按实例身份清理 (R4-AST-002)` | `AssetBootstrapStage`、Bootstrap 用例 | `PM(Asset)`；`DOC` |
| 1.5 | `fix(Asset): 热更 API 检查包初始化状态 (R4-AST-003)` | `YooAssetManagerImpl` 三入口、初始化失败包用例 | `PM(Asset)`；`DOC` |
| 1.6 | `fix(Camera): 同一目标中途 cut 补发 Activated (R4-CAM-001)` 或 `docs(Camera): 写明该例外` | `CinemachineCameraBackend`、PlayMode 消息序列用例 | `PM(Cinemachine)`；改前必红；`DOC` |
| 1.7 | `fix(Camera): 外部 override 下 SwitchTo 的 boost 归属 (R4-CAM-002)` | `TransitionToSlot` 提前返回分支、PlayMode 用例 | `PM(Cinemachine)`；改前必红；`DOC` |
| 1.8 | `fix(Camera): 已销毁注册相机事件 id 稳定 (R4-CAM-005)` | `ResolveCameraId`/槽位反查、PlayMode 用例 | `PM(Cinemachine)`；`DOC` |
| 1.9 | `fix(Scene): 激活失败告警守卫无效 Scene (R4-SCN-001)` | `SceneManager`、假件用例 | `PM(Scene)`；`DOC` |
| 1.10 | `fix(Diagnostics): Order getter 异常隔离 (R4-DIAG-001)` | `DiagnosticsManager`、坏 Order 用例 | `PM(Diagnostics)`；`DOC` |
| 1.11 | `fix(UI): OnPoolRecycle 异常不跳过关闭消息 (R4-UIA-002)` | `UIManagerImpl` / `UIHudManager`、红证 | `PM(UI)`；`DOC` |
| 1.12 | `fix(UI): 层可见性恢复重放交互状态 (R4-UIA-005)` | `SetLayerVisibility/Interactive`、PlayMode 探针 | `PM(UI)`；`[需实测]`；`DOC` |
| 1.13 | `fix(UI): 控制器全关回调与消息面同门控 (R4-UIB-003)` **行为变更需单独确认** | `UIManagerImpl`、控制器计数用例、CHANGELOG 行为变更 | `PM(UI)`；`DOC` |
| 1.14 | `fix(UI): provider 换绑/HideHud 异常隔离 (R4-UIB-004)` | `UIManagerImpl`、Throwing provider 用例 | `PM(UI)`；`DOC` |
| 1.15 | `fix(Luban): 引导阶段区分本次创建与加入在途 (R4-LBN-005)` | `LubanBootstrapStage`、并发在途用例 | `PM(Luban)`；`EM(Luban)`；`DOC` |
| 1.16 | `fix(Save): 单槽删除认备份 (R4-SAVE-002)` | `DeleteSlotAsync`、只剩 `.bak` 用例 | `PM(Save)`；改前必红；`DOC` |
| 1.17 | `fix(Audio): Free 槽坏源重建/丢弃 (R4-AUD-001)` | `AudioSourcePool.TryReserve/Release/Sweep`、宿主外部销毁用例 | `PM(Audio)`；改前必红；`DOC` |
| 1.18 | `fix(Input): 长按时长按 playerId 分键 (R4-INP-001)` | `InputSystemProvider` 计时键/控件边沿、双玩家用例 | `PM(Input)`；改前必红；`DOC` |
| 1.19 | `fix(Settings): 加密读侧解析切回主线程 (R4-SET-001)` **先确认 JsonUtility 线程约束** | `EncryptedSettingsStore.LoadAsync`、线程 id 用例 | `PM(Settings)`；`[需复核]`；`DOC` |

### 批次 2：P2 文档/契约清扫

- 代表项：R4-UIA-003/004、R4-UIB-006/007（UI README）；R4-UPD-001（Update README/XML 的 SetInstance 句）；R4-CONF-004（ConfigTable 键==Id）；R4-AST-007（Asset Destroy 文案）；R4-LBN-001/006/007/011/012/014（Luban README/维护记录）；R4-ENG-001/002/003/009（PublicApi 注释、Workflow 参数表、ModuleReadme 门禁描述、UISorting 行为表）；R4-SAVE-001（Sync 计数）；R4-FILE-001（null 契约）；R4-SER-001（ResetForNewSession 文案）。
- 每个模块/主题一个原子提交；验证 `DOC` + 人工对照；涉及行为变更表的提交与发版同批。

### 批次 3：P2 代码/测试补齐

- 代表项：R4-LBN-002/003/004/008/010/013（Luban 生产路径、ProcessRunner、Generator 组合、写盘原子性、并发 adapter、Editor 基线）；R4-CONF-003（SO 资产往返）；R4-AST-004/005/006（Asset 日志/分配/在途退订）；R4-DIAG-002（表头格式）；R4-CAM-003/004（Subscribe 文档/重载探针）；R4-UIA-006（UI 覆盖缺口）；R4-UIB-005（img_ 类型不匹配告警）；R4-PIPE-001（嵌套聚合时序）；R4-POOL-001（Return 满池隔离）；R4-LOC-001（替换值二次解释）。
- 验证：`PM/EM/ALL`；改前必红；`DOC`。

### 批次 4：裁定与发布

- 用户逐条裁定 §9 的 R4-ADJ-01..11，写入模块台账 / `Ledger.md`。
- 0.4.0 发版：切 `[0.4.0]` 小节、bump `package.json`、打 tag（用户执行）；README 示例从 `0.3.0` 改 `0.4.0`；Luban 0.1.0 打 tag 并替换 `<tag>` 占位。
- 若 R4-ADJ-05 批准：`chore(tools): run-tests 可选超时`。

### 批次 5：探针轨与收尾

- 执行 §11 的 R4-PRB-01..11 中可在本环境运行的项；其余登记 `未做` + 配方。
- 关闭域重载连跑两遍（手工）、真 YooAsset 场景卸载序、真机/IL2CPP 按环境。
- `FULL + DOC + API + BUILD`；`git status` 只含预期文件。

### 依赖与风险

- **0.1–0.4 必须最先**：三条 P1 与一条修复回归，影响默认生命周期路径；每条先写红证。
- **R4-UIB-003 是行为变更**（控制器回调门控），实施前单独确认；`R4-CAM-001` 若选择补发 `Activated` 也属行为变更。
- **R4-AST-001/002 互相耦合**（`SetInstance` 语义）；同一计划、同一批次，避免两次改线程/所有权路径。
- **R4-CONF-003 需真 Unity 序列化环境**；若本环境不能建资产，只能登记探针，不得以内存用例冒充端到端。
- **R4-LBN-008 改原子写**会动 Editor 写盘路径；与 R4-LBN-002/003/004 的测试补齐同批，避免反复改夹具。
- **发版必须等批次 0–3 落地**；否则 `[0.4.0]` 小节会混入未修项。

## 11. 遗留承接与探针轨

### 11.1 R3 遗留探针（仍未执行）

| ID | 内容 | 状态 | owner / 配方 |
|---|---|---|---|
| R3-PRB-02 | 真 YooAsset 低内存：加载 SO Global → `UnloadUnusedAssetsAsync` → 断言 `GetGlobal` 是否假 null | 未做 | 真包环境；见 `Modules/Config.md:96` |
| R3-PRB-03 | IL2CPP High stripping 下 `[Preserve]` 充分性 | 未做 | 真机构建；见 `Modules/Config.md:97` |
| R3-PRB-04 | macOS Server 构建的平台分支运行验证 | 未做 | Server Build Support；见 `Modules/File.md:48` |

### 11.2 R4 新增探针

| ID | 关联 | 内容 | 状态 |
|---|---|---|---|
| R4-PRB-01 | R4-UIA-001/002、R4-UIB-001/002 | PlayMode 红证：Dispose × 在途打开、null owner 遮罩、HUD 并发附着、OnPoolRecycle 异常 | 待执行（实施批次 0/1 必须先写） |
| R4-PRB-02 | R4-CONF-001 | 门控 Fake loader + 第二调用 null loader，断言 `ConfigException`（改前 NRE） | 待执行 |
| R4-PRB-03 | R4-CONF-002 | 真 YooAsset 或假 `IAssetManager`：类型不符地址 → 异常类型 + 句柄 `RefCount`/可回收性 | 待执行 |
| R4-PRB-04 | R4-AST-001 | `SetInstance` 换出后旧 impl 的 `Dispose`/`lowMemory` 退订（反射读） | 待执行 |
| R4-PRB-05 | R4-AST-003 | 让 `InitializePackageAsync("Extra")` 失败，再调三个热更 API，断言异常类型/消息 | 待执行 |
| R4-PRB-06 | R4-CONF-003 | EditMode 资产往返：`CreateInstance` → `SerializedObject` → `CreateAsset/SaveAssets` → `LoadAssetAtPath` → `BuildTable` | 待执行 |
| R4-PRB-07 | R4-LBN-002 | Luban 默认 `AssetManagerLubanAssetSource` 的 TextAsset/RawFile 成功映射（假 `AssetManager.SetInstance`） | 待执行 |
| R4-PRB-08 | R4-LBN-003 | `LubanProcessRunner.Run` 成功/非零/超时/大输出/中文编码 | 待执行 |
| R4-PRB-09 | R4-LBN-009 | Linux Editor 大小写敏感路径 + 非项目根 cwd 的 GUID 解析 | 待执行（需 Linux） |
| R4-PRB-10 | R4-CAM-001 | 同一目标中途 cut 的消息序列（A→B 0.1s 后 `SwitchTo("b")`） | 待执行 |
| R4-PRB-11 | R4-CAM-002 | 外部 `SetCameraOverride` + `SwitchTo("b")` + 释放 override 的 boost 归属 | 待执行 |
| R4-PRB-12 | §7.4 | 0.3.0→0.4.0 迁移表逐条「旧写法 → 新写法」编译探针（含 `null`/默认值边界） | 待执行 |
| R4-PRB-13 | R4-SAVE-002 | 写槽位 → 删载荷留 `.bak` → 断言 `SlotExistsAsync==true`、`DeleteSlotAsync==true` 且配套文件消失 | 待执行 |
| R4-PRB-14 | R4-AUD-001 | `Play` 成功后 `Destroy(host)` → Sweep → 再 `Play`，断言返回有效句柄 | 待执行 |
| R4-PRB-15 | R4-INP-001 | 两个 Fake 手柄：1P 不按、2P 按住，断言 `GetButtonPressDuration(...,1)>0` | 待执行 |
| R4-PRB-16 | R4-SET-001 | 记录 `FromCipherBlob` 执行线程；查 Unity 版本对 `JsonUtility.FromJson` 的线程约束 | 待执行（需查证） |
| R4-PRB-17 | R4-PIPE-001 | 嵌套 Parallel/Sequence + 记录 `OnProgressUpdate`，断言内层完成后外层快照翻转 | 待执行 |
| R4-PRB-18 | R4-SER-001 | 更早的 SubsystemRegistration 注册自定义格式 → ResetForNewSession → 断言是否被清掉 | 待执行 |

> 探针不阻塞报告收口；未执行项在实施轮按 §13.4 登记 `未做` + 配方。R4-PRB-01/02 应在批次 0 直接落成用例，不以「探针」结案。

## 12. 已评估未采纳 / 不重开清单

### 12.1 承接 R3 §12.1 的框架级 7 条

| 条目 | R3 状态 | R4 复核 | 结论 |
|---|---|---|---|
| Network 基础层 | 暂不做 | 仓内仍零消费者；File 的 HTTP 只服务 Streaming | 判据未触发，维持 |
| 代码热更（HybridCLR） | 暂不做 | 本轮未出现真实接入方；Luban 是配置表，不是代码热更 | 判据未触发，维持 |
| GM / 调试命令台 | 暂不做 | 仍无使用方给出命令集/权限/发布形态 | 判据未触发，维持 |
| App 生命周期 | 暂不做 | `pause` 仍只有 Settings 一处消费者 | 判据未触发，维持 |
| 性能基准套件 | 未做 | 2026-10-21 已建成（6 个 `[Explicit]` 基准，全量 6 skip） | **已做** |
| 示例覆盖面 | 未做 | 2026-10-21 新增 `Samples~/Playable`，示例编译进文档门禁 | **已做**（质量仍靠评审） |
| 包自身 Git URL 说明 | 待裁 | R3-ADJ-14 已落地（README 说明 + 钉 0.3.0 示例） | **已做**；0.4.0 发版时需更新示例 |

### 12.2 承接 R3 §12.2（维持）

- 框架级结构性 `不做` 组（状态机/行为树/ECS/时间模型/生命周期树、Update 组 API、DI 容器、Tween、Addressables、新手引导、平台 SDK 聚合、网络同步、统一 Profile、模块内部策略接口化、远程配置、崩溃上报、红点、画质档位、埋点/统计上报、热更编排）——R4 未发现推翻证据，**不重开**。
- 带判据的 `不做`（CI、`Tests~`/移出包目录、包在子目录、Git 钩子/Issue·PR 模板/`CONTRIBUTING`）——判据未触发，维持。
- 各模块台账中的 `不做/暂不做` 条目：R4 逐模块复核（§8），除 §9 的 11 条建议外**全部维持**；未发现需要推翻的旧结论。
- 主线程断言「三份副本不抽公共防线」——维持；R4 未新增第四种形态（Camera/Config 的「不加断言」是另一族）。
- 会话复位「只丢引用不 Dispose」——维持 2026-10-18 框架级裁定；R4-AST-001/002 是「换实例/阶段所有权」的处置问题，不是会话边界。

### 12.3 Luban §7.8 六条（维持）

- 不重建核心内建 Luban 适配层；不把 `ILubanDataProvider` 公开；不做 `bin-offset`/分片；不支持三个 JSON target；不做 Roslyn source generator；不把扩展包移入核心 Runtime 树。R4 未发现推翻证据。

### 12.4 R3 §12.3 建议的复核

| 建议 | R3 状态 | R4 复核 | 结论 |
|---|---|---|---|
| 公开枚举整数值纳入基线/迁移检查 | 待裁 | R4-AST-001 仍是文档化；未出现第二个因枚举值重排导致的事故 | 维持 `待裁` |
| 依赖清单从 asmdef 反查 | 未做 | 三处依赖当前一致；反查机制未建 | 维持 `未做` |
| Cinemachine 扩展包门禁覆盖 | 未做 | 2026-10-22 并入核心，结构性关闭 | **已做** |
| 文档示例可编译性守卫 | 未做 | 2026-10-21 `check-samples.ps1` 进 `check-docs -Enforce` | **已做** |
| 结构化日志参数 | 暂不做 | 未出现日志分析管线需求 | 维持 |
| 反射式自动会话复位 | 不做 | 与显式登记取向冲突 | 维持 |

## 13. 台账同步索引

> 审计期**不写台账**；以下为实施轮（或用户确认后）的落点。R4 新发现修复落地后，才由实施提交记 `已做`。

### 13.1 裁定建议

| 建议 ID | 模块 | R4 建议 | 落点 |
|---|---|---|---|
| R4-ADJ-01 | Luban | `暂不做`（台账留在 `Luban.md` §7.8） | `Modules/README.md` 或 `Luban.md` |
| R4-ADJ-02 | 工程面 | `未做`（Editor 公开面无基线） | `Ledger.md` |
| R4-ADJ-03 | 工程面 | `暂不做`（check-docs 不覆盖 Luban XML/源文件） | `Ledger.md` |
| R4-ADJ-04 | 发布 | `未做`（0.4.0 切版本/tag；Luban 0.1.0 tag） | `CHANGELOG.md` / `package.json` |
| R4-ADJ-05 | 工程面 | `待裁`（run-tests 超时） | `Ledger.md` / `Workflow.md` |
| R4-ADJ-06 | Luban | `未做`（写盘原子性） | `Modules` 或包 README 已知限制 |
| R4-ADJ-07 | UI | `未做`（UISorting 常量进行为变更表） | `CHANGELOG.md` |
| R4-ADJ-08 | Luban | `未做`（DataRoot/ExtraArguments 措辞） | 包 README |
| R4-ADJ-09 | Luban | `未做`（主线程断言射程/README 收窄） | 包 README / `LubanConfig.cs` |
| R4-ADJ-10 | Config | `未做`（ConfigTable 键==Id 契约） | `ConfigTable.cs` XML / Config README |
| R4-ADJ-11 | Save | `未做`（RecoverAsync 双路径，承接 R3-ADJ-08） | `Modules/Save.md` |

### 13.2 R4 发现的台账落点

- **P1（R4-UIA-001、R4-UIB-001/002）**：实施后记 `已做`，带提交号与 PlayMode 回归用例；落 `Modules/UI.md`。
- **P2/P3**：修复落地前不记台账；落地后按模块记 `已做`（纯文档订正按需记）。重点落点：`Modules/Config.md`（R4-CONF-001..006）、`Modules/Asset.md`（R4-AST-001..007）、`Modules/UI.md`（R4-UIA/UIB）、`Modules/Camera.md`（R4-CAM-001..005）、`Modules/Diagnostics.md`（R4-DIAG-001/002）、`Modules/Scene.md`（R4-SCN-001）、`Modules/Save.md`（R4-SAVE-001/002）、`Modules/Input.md`（R4-INP-001）、`Modules/Localization.md`（R4-LOC-001）、`Modules/Audio.md`（R4-AUD-001）、`Modules/Settings.md`（R4-SET-001）、`Modules/Update.md`（R4-UPD-001）、`Modules/Pipeline.md`（R4-PIPE-001）、`Modules/Pool.md`（R4-POOL-001）、`Modules/File.md`（R4-FILE-001）、`Modules/Serialize.md`（R4-SER-001）、`Documentation/Luban.md` §7.8（R4-LBN-001..017）。
- **工程面**：R4-ENG-001..009 落 `Documentation/Workflow.md` / `ModuleReadme.md` / `CHANGELOG.md`；是否需要台账条目按 §9 裁定。

### 13.3 框架级台账（`Documentation/Ledger.md`）

- 新增 `未做`：0.4.0 发版与 Luban 0.1.0 tag（R4-ADJ-04）。
- 新增 `未做`：Editor 程序集公开 API 基线（R4-ADJ-02）。
- 维持 `暂不做`：`check-docs` 不覆盖 Luban XML/源文件（R4-ADJ-03，重开判据不变）。
- 新增 `待裁`：`run-tests.ps1` 超时（R4-ADJ-05）。
- 新增 `未做`：`ConfigTable` 键==Id 契约（R4-ADJ-10）；`Save` 双路径线程改造（R4-ADJ-11，承接 R3-ADJ-08）。
- §四复核记录追加：R3 §12.1 的 7 条复核对齐（§12.1）与 R4 探针轨指针。

### 13.4 探针轨

- R3-PRB-02/03/04 维持 `未做` + 原配方（`Modules/Config.md`、`Modules/File.md`）。
- R4-PRB-01..12 在对应模块台账登记 `未做`，证据写「R4 探针轨 R4-PRB-xx（配方见 `Documentation/AuditRound4.md` §11）」；实施批次 0/1 的条目在落成用例后改 `已做`。
- F8（关闭域重载两遍）仍是跨模块手工项，落 `Ledger.md` §四；R3 已结案，R4 无新证据则不改状态。

## 附录 A. 探针与命令

### A.1 基线与门禁（2026-10-25，冻结 SHA `d29f4f50`）

```bash
# 构建（幂等；--artifacts-path 避开 Unity 生成的 obj/bin）
dotnet build Venusy609.Xframework.csproj --artifacts-path "$env:TEMP\xfw-r4-artifacts" -v q --nologo
dotnet build Venusy609.Xframework.Editor.csproj --artifacts-path "$env:TEMP\xfw-r4-artifacts" -v q --nologo

# 全量测试（PlayMode + EditMode）
pwsh -File Tools/run-tests.ps1

# 文档门禁
pwsh -File Tools/check-docs.ps1 -Enforce

# 公开 API 基线（已含在全量内；单独看用 -Fixture）
pwsh -File Tools/run-tests.ps1 -Fixture PublicApiBaselineTests
```

结果：BUILD 0 error（19 条第三方告警）；FULL PlayMode 2106/2106、EditMode 628/628（6 skip）；DOC 通过（24 模块 / 463 条台账 / 文件树 24/24 / 双示例编译）；API 1/1。

### A.2 审计自证

```bash
# 发现 ID 去重数（应等于附录 C 行数）
grep -oE 'R4-[A-Z]+-[0-9]{3}' Documentation/AuditRound4.md | sort -u | wc -l
# 附录 C 行数（当前 71）
grep -cE '^\| R4-[A-Z]+-[0-9]{3} \|' Documentation/AuditRound4.md
# 严重度分布（当前 0 P0 / 3 P1 / 51 P2 / 17 P3）
grep -oE '^\| R4-[A-Z]+-[0-9]{3} \| P[0-3]' Documentation/AuditRound4.md | sort | uniq -c
# 台账计数（与 check-docs 同口径）
files=$(ls Documentation/Modules/*.md | grep -v 'Modules/README.md')
for s in 已做 未做 待裁 暂不做 不做 已推翻; do
  printf '%s ' "$s"; { grep -h "^- $s：" $files Documentation/Ledger.md; } | wc -l
done
```

> 注：`grep -oE ... | sort | uniq -d` 会把正文中的**重复引用**也报出来，不能当「ID 定义重复」的判据；应比较「去重数 = 附录 C 行数」。

## 附录 B. 待取证

- **R3 遗留**：R3-PRB-02（真 YooAsset 低内存 `GetGlobal` 假 null）、R3-PRB-03（IL2CPP High stripping）、R3-PRB-04（macOS Server 构建）。
- **R4 新增**：R4-PRB-01..18（§11.2），其中优先级最高的是 R4-PRB-01（三条 UI P1 的 PlayMode 红证）、R4-PRB-02（Config null-loader 回归）、R4-PRB-06（SO 资产往返）、R4-PRB-10/11（Camera 消息/boost）、R4-PRB-13/14（Save 删除、Audio 坏源）。
- **手工/真机类**：关闭域重载连跑两遍（`Workflow.md` §六配方）、真实 YooAsset 场景卸载序、真实 `PlayableDirector` 接管/释放、Android/iOS Streaming 与 IL2CPP、Luban IL2CPP/真机、Linux Editor 大小写路径、`LubanProcessRunner` 中文编码。
- **各模块 `[需实测]`**：R4-CAM-001/002/004/005、R4-UIA-005、R4-CONF-003、R4-SCN-001、R4-LBN-009/015。
- **无法在本环境验证**：0.3.0→0.4.0 的序列化读回（需 Unity 序列化环境）、真机性能、Console/平台存档 SDK。

## 附录 C. R4 发现 ID 索引

> 按 ID 排序；一句话为报告内标题/首句的压缩，完整证据见对应节。

| ID | 级别 | 模块 | 一句话 |
|---|---|---|---|
| R4-AST-001 | P2 | Asset | `SetInstance` 不处置旧 impl，旧实例被静态事件钉住 |
| R4-AST-002 | P2 | Asset | 引导阶段所有权不校验当前实例身份 |
| R4-AST-003 | P2 | Asset | 热更 API 对「初始化失败的缓存包」抛裸异常/可能 NRE |
| R4-AST-004 | P3 | Asset | 缺组件日志称「销毁」实际回池 |
| R4-AST-005 | P3 | Asset | 下载最终进度未隔离；逐订阅者隔离每次分配 |
| R4-AST-006 | P3 | Asset | 会话复位退订不到在途初始化 impl |
| R4-AST-007 | P3 | Asset | README `Destroy()` 文案与契约相反、交叉引用错位 |
| R4-AUD-001 | P2 | Audio | Free 槽坏源永不重建，毒化后续播放 |
| R4-CAM-001 | P2 | Camera | 同一目标中途 cut 不发 `Activated` |
| R4-CAM-002 | P2 | Camera | 外部 override 下 `SwitchTo` 的 boost 归属错误 |
| R4-CAM-003 | P2 | Camera | `Subscribe` 的 token 说法无对应 API |
| R4-CAM-004 | P2 | Camera | 三个 `Subscribe` 重载对无类型 lambda 可能二义；`Register(null)` 二义 |
| R4-CAM-005 | P2 | Camera | 已销毁注册相机的事件 id 为 null |
| R4-CONF-001 | P2 | Config | 在途分支 null loader 先于校验被解引用（回归） |
| R4-CONF-002 | P2 | Config | 类型不符资源抛异常前不释放有效句柄 |
| R4-CONF-003 | P2 | Config | SO Table 资产序列化端到端零覆盖 |
| R4-CONF-004 | P3 | Config | `ConfigTable` 的「键==Id」隐式前提未成文 |
| R4-CONF-005 | P2 | Config | Table/Global 同类型在途共用一个槽，第三调用方重复加载 |
| R4-CONF-006 | P3 | Config | 在途参数告警只比较 Loader 类型名，不比较实例 |
| R4-DIAG-001 | P2 | Diagnostics | `Order` getter 未做异常隔离 |
| R4-DIAG-002 | P2 | Diagnostics | 表头尾部空列宽度与数据行不一致 |
| R4-ENG-001 | P2 | 工程面 | `PublicApiBaselineTests` 注释计数已过期 |
| R4-ENG-002 | P2 | 工程面 | `Workflow.md` 参数表缺两个 Luban 更新开关 |
| R4-ENG-003 | P2 | 工程面 | `ModuleReadme.md` 文件树门禁描述与脚本不一致 |
| R4-ENG-004 | P2 | 工程面 | `check-docs` 不覆盖 Luban 的 XML/源文件参与等 |
| R4-ENG-005 | P2 | 工程面 | `run-tests.ps1` 无超时参数 |
| R4-ENG-006 | P2 | 工程面 | 两个包的 Editor 公开面无 API 基线 |
| R4-ENG-007 | P3 | 工程面 | 示例只守编译，不守场景/prefab（维持） |
| R4-ENG-008 | P3 | 工程面 | Luban 的台账不在 `Modules/README.md` 文件表 |
| R4-ENG-009 | P2 | 工程面 | `UISorting.MaxIndexInLayer` 未进行为变更表 |
| R4-FILE-001 | P3 | File | 读失败契约写 null→异常，实现把 null 当域根 |
| R4-INP-001 | P2 | Input | `GetButtonPressDuration` 忽略 `playerId` |
| R4-LBN-001 | P2 | Luban | 主线程断言只覆盖 4 个入口 |
| R4-LBN-002 | P2 | Luban | LUB-04 部分：生产默认加载后端成功路径零覆盖 |
| R4-LBN-003 | P2 | Luban | `LubanProcessRunner.Run` 零直接测试 |
| R4-LBN-004 | P2 | Luban | `Generator(write:true)`/菜单入口零组合覆盖 |
| R4-LBN-005 | P2 | Luban | 引导阶段把「加入在途」误记为「本次创建」 |
| R4-LBN-006 | P2 | Luban | provider 级失败消息不含表名/location |
| R4-LBN-007 | P2 | Luban | `DataRoot`/`ExtraArguments`「原样」与 `Trim` 不符 |
| R4-LBN-008 | P2 | Luban | 写盘 IO 失败留部分文件、manifest 最后写 |
| R4-LBN-009 | P2 | Luban | 路径大小写/项目相对 GUID 解析的平台风险 |
| R4-LBN-010 | P2 | Luban | 并发初始化第二个 adapter 被静默忽略 |
| R4-LBN-011 | P2 | Luban | 「六道门禁不覆盖本包」过宽（示例编译覆盖） |
| R4-LBN-012 | P2 | Luban | README 公开 API 列表漏两个类型 |
| R4-LBN-013 | P2 | Luban | Luban Editor 公开面无基线 |
| R4-LBN-014 | P2 | Luban | `Documentation/Luban.md` §七文件计数 13/14 vs 15/15 |
| R4-LBN-015 | P3 | Luban | `LubanProcessRunner` 未设输出编码 |
| R4-LBN-016 | P3 | Luban | 零拷贝字典可被调用方后续修改且无事件 |
| R4-LBN-017 | P3 | Luban | `LubanConfigImpl` 未使用的 `using` |
| R4-LOC-001 | P3 | Localization | 占位符替换值被当作复合格式项二次解释 |
| R4-PIPE-001 | P3 | Pipeline | Owner 清理早于终态写入，嵌套聚合可能陈旧 |
| R4-POOL-001 | P3 | Pool | Return 满池丢弃回调未隔离；onDestroy 文档未同步 |
| R4-SAVE-001 | P2 | Save | `Modules/Save.md` 的同步 Delete/Exists 计数过期（5 vs 8） |
| R4-SAVE-002 | P2 | Save | 仅剩 `.bak` 的槽位单槽删除返回 false |
| R4-SCN-001 | P2 | Scene | 激活失败告警直接取无效 `Scene.name` |
| R4-SER-001 | P3 | Serialize | README 称 Initialize 前注册会保留，ResetForNewSession 会清掉 |
| R4-SET-001 | P2 | Settings | 加密读侧在内层同步时把 `JsonUtility` 放池线程 |
| R4-UIA-001 | P1 | UI | Dispose × 在途打开，续体在回池后执行 `OnClose` |
| R4-UIA-002 | P2 | UI | `OnPoolRecycle` 异常跳过 `PanelClosedMessage`/`OnAfterClose` |
| R4-UIA-003 | P2 | UI | README 仍称 `SetInstance` 不重接驱动器（反向回归） |
| R4-UIA-004 | P2 | UI | README 排序常量 31 vs 30、关闭不重排 |
| R4-UIA-005 | P2 | UI | 层恢复可见后未重放「禁交互」状态 |
| R4-UIA-006 | P3 | UI | `PopTo`/`PopToRoot` 假 OnResume 与 Dispose provider 无用例 |
| R4-UIB-001 | P1 | UI | `owner=null` 遮罩被剪枝误删 |
| R4-UIB-002 | P1 | UI | HUD 同目标并发附着产生不可清理孤儿 |
| R4-UIB-003 | P2 | UI | 控制器「全关」回调与消息面门控不一致 |
| R4-UIB-004 | P2 | UI | 换 provider/HideHud 的 Detach 未隔离 |
| R4-UIB-005 | P2 | UI | `img_` 源值类型不匹配完全静默 |
| R4-UIB-006 | P2 | UI | `BindToLocalizedText` 示例仍教错误释放方式 |
| R4-UIB-007 | P2 | UI | README §11 代码围栏断裂 |
| R4-UPD-001 | P2 | Update | README/XML 称 `SetInstance` 不重接驱动器（反向回归） |
