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
| 2 | B1 R4 修复复核 | 待做 | §3 | 71 条 + ADJ + PRB |
| 3 | B2 FMOD 包全量 | 待做 | §6、§8、附录 C | 首次审计；默认无 SDK |
| 4 | B3 UIAuthoring 包全量 | 待做 | §6、§8、附录 C | 首次审计 |
| 5 | B4 核心新面（Audio / UI 校验器） | 待做 | §6、§8、附录 C | |
| 6 | B5 核心增量（T2 / T3 / Luban 复审） | 待做 | §6、§8、附录 C | |
| 7 | B6 工程面与发布 | 待做 | §6、§7 | |
| 8 | B7 报告收口 | 待做 | §2 总览、§4–§13、附录 B/C | |
| 9 | B8 收尾验证 | 待做 | 附录 A | FULL + DOC + 自证 |

## 3. 前轮修复复核（R4）

（待 B1 填充：71 条 R4 发现的 `ID → 提交 → 代码/测试/文档 → 结论`；R4-ADJ-01..11 落盘复核；R4-PRB-01..18 探针轨状态；R4 修复引入的回归与部分修复。）

## 4. R5 P0 清单

（待填）

## 5. R5 P1 清单

（待填）

## 6. R5 P2 清单（按模块）

（待填：UI / Camera / Config / Asset / Input / Localization / Audio / Settings / Save / Update / Pipeline / Pool / File / Serialize / Scene / Diagnostics / Luban / FMOD / UIAuthoring / 工程面）

## 7. 跨模块主题

（待 B6/B7 填充）

## 8. 逐模块审计记录

（待 B2–B6 填充：24 个核心模块 + 3 个扩展包 + 工程面，每个对象记录范围与输入、18 类判据留痕、前轮修复偏差、旧结论复核、开口项、第三方对照、残留与不确定。）

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

（待 B7 填充）
