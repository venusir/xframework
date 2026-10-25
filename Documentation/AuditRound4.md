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

（待补）

### 2.1 最需要先读的 10 条

（待补）

### 2.2 P1 分布（按模块）

（待补）

### 2.3 跨模块主题索引

（待补）

### 2.4 R3 修复复核摘要

（待补）

### 2.5 本轮明确未做

（待补）

## 3. 前轮修复复核

### 3.1 R3 P0 + P1（1 + 7 条）

（待补）

### 3.2 R3 P2（131 条）聚合复核

（待补）

### 3.3 R3 的 14 条待裁裁定落盘复核

（待补）

### 3.4 Luban LUB-01..28 复核

（待补）

### 3.5 探针轨与台账实时计数

（待补）

## 4. R4 P0 清单

（待补）

## 5. R4 P1 清单

（待补）

## 6. R4 P2 清单（按模块）

（待补）

## 7. 跨模块主题

（待补）

## 8. 逐模块审计记录

（待补）

## 9. 待裁的一次性裁定建议

（待补）

## 10. R4 分步实施计划

（待补）

## 11. 遗留承接与探针轨

（待补）

## 12. 已评估未采纳 / 不重开清单

（待补）

## 13. 台账同步索引

### 13.1 裁定建议

（待补）

### 13.2 R4 发现的台账落点

（待补）

### 13.3 框架级台账

（待补）

### 13.4 探针轨

（待补）

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
# ID 唯一性（应为空）
grep -oE 'R4-[A-Z]+-[0-9]{3}' Documentation/AuditRound4.md | sort | uniq -d
# 与附录 C 条数一致
grep -oE 'R4-[A-Z]+-[0-9]{3}' Documentation/AuditRound4.md | sort -u | wc -l
```

## 附录 B. 待取证

（待补）

## 附录 C. R4 发现 ID 索引

（待补）
