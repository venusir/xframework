# XFramework 工作流

> 本文是**开发者向**的流程手册：提交节奏、门禁命令、验证纪律，以及这套流程**没覆盖到**的地方。
> 规则本身（命名、风格、注释、异步、日志、依赖等）在仓库根的 `CLAUDE.md`，本文不重复，只写流程。
> 快照日期：2026-09-27。

## 一、节奏：一轮一个提交单元

```
计划 → 圈定范围 → 逐提交实现 → 验证 → 核对文档 → 提交 → 下一个
```

- **计划**阶段就定死**原子提交边界**与每个提交的验证命令；候选方案要排序并给理由，「不该做」也写成结论
- **实现**一次只做一个提交：改完 → 验证 → 提交，然后进入下一个（授权见下条）
- **核对文档（两问，答「不用」是合法结论）**：① 本次改动有没有改变使用方可见的行为、契约或限制？有 → 改该模块 README；② 有没有产生「下一轮审计需要知道」的结论（已评估未采纳、跨模块能力缺口、历史形态）？有 → 记 `Documentation/Modules/<模块>.md`。两问都为否就跳过，**不留痕**。这不是门禁——三条门禁都机械可判定（0 error / 0 失败 / 0 告警），这一条不是，做成门禁只会走过场
- **授权**：**计划批准即授权本阶段的全部提交**——每个提交跑绿计划里写明的验证命令后直接提交，不必逐次请示。**两类例外必须单独请用户确认**：公开 API 变更、破坏性变更（含仅对仓内的）；需要用户裁定的设计取舍（API 形状、「不该做」的结论）同样停下
- **不自动推送**，推不推由人决定（提交信息格式的规定在 `CLAUDE.md`，本文不重复）

### 审计轮（可选的前置）

不确定「该改什么」时先走一轮审计——它**只产出发现清单，不动代码**。

本流程是**两个嵌套的环**：内环 = 每个提交单元；外环 = 「审计 → 修 → 归档」。外环的回边**由触发器拉起，不是每轮都回到审计**（每轮重审是浪费）：

```
内环（每个提交）    计划 → 实现 → 验证 → 核对文档 → 提交 → 下一个

外环（阶段级）      审计 → 发现清单 → 人圈定范围 →（内环若干轮）→ 阶段收尾 → 归档
                     ↑                                                      │
                     └──────────────── 触发器 ◄───────────────────────────────┘
```

- **什么时候做（外环回边的三个触发器）**：接手陌生模块、发布前、某类事故之后；**不是每轮都做**
- **产出**：按**严重度排序**的发现清单，每条带理由；**「不该做」也写成结论**；标明哪些是**实测**、哪些是**推理**（见 §三.2）
- **判据与探针**：`Documentation/ModuleAudit.md`（缺陷类清单 + 每条配的命令 + 每条抓不到什么）
- **归档（外环的收口，也是下一轮的起点）**：结论与**已否决项**写进该模块 README（优先并入 `已知限制` / `设计取舍`）。**下一轮审计从上一轮的已否决项读起**——否则同一件事会被重新论证一遍，环就白转了

## 二、验证：三条通道

| 通道 | 命令 | 通过标准 |
|---|---|---|
| 编译 | `dotnet build <csproj> --artifacts-path "$env:TEMP\xfw-artifacts" -v q --nologo` | 0 error |
| 测试 | `pwsh -File Tools/run-tests.ps1 [-Filter <片段>] [-Platform PlayMode\|EditMode\|All]` | 全量双平台 **0 失败** |
| 文档 | `pwsh -File Tools/check-docs.ps1 -Enforce` | **门禁码**（十一个 XML 文档告警码，清单见脚本头部「覆盖范围」）的包内告警 0、**所有源文件都参与编译**、所有条目都有 `.meta`、**决策台账结构合规**（每模块有 `## 决策台账`、条目状态词合法、`Modules/README.md` 文件表与 `Runtime/` 双向相等——规范见 `Documentation/Modules/README.md`；只守结构，不守内容属实）、**模块 README 的文件树与磁盘一致**（每个模块目录下的 `.cs` 都要列进「`## 模块结构`」——按 basename 比对，两个方向都抓）、**示例代码编译通过**（`Samples~/` 不参与 Unity 编译，由 `Tools/check-samples.ps1` 生成临时 csproj 编一遍） |

### 两个易踩点

- **`Tests/Editor/` 下的用例必须显式 `-Platform EditMode`**：`run-tests.ps1` 的默认平台是 `All`（双平台都跑），但一旦用 `-Filter` 且未显式指定平台，它只跑 `PlayMode`——此时 EditMode 侧的用例会给出「0 用例」的假绿。
- **文档检查必须全量重编**：增量构建不重发警告，会得到假的「0 条」。因此它比测试慢（约 90 秒/工程，两工程约 180 秒）。两条门禁**刻意没合并**——合并会给门禁再加这段耗时。

### 门禁的定义

- 测试门禁 = **全量 0 失败**，**不写固定例数**（真相来源是 runner 的输出，抄进文档就是第二份真相）
- 阶段收尾跑两条：`pwsh -File Tools/run-tests.ps1` + `pwsh -File Tools/check-docs.ps1 -Enforce`

## 三、两条验证纪律

### 1. 改前必红

新增回归测试时，**先证明它在修复前会失败**——否则无法区分「测试有效」与「测试恒真」。
实测出处（2026-09-27，XPool 一役）：

- 引用相等比较器：摘掉比较器后同用例即红（`Expected: 2, But was: 1`）
- 集合池 `Configure` 守卫：把守卫改回旧算法后同用例即红
- **门禁退出码本身**：临时注入一个必失败用例，验证「第二个平台失败时汇总退出码非 0」

### 2. 区分实测与推理

报告里标明哪些是**实测**、哪些是**推理**；**未能复现或未能解释的残留要摆到台面上**，不要用「大概无关」搪塞。

实测例：XSave 的 `LoadAsync_AppliesSnapshotOnCallingThread` 曾偶发失败（`Expected: 9, But was: 1`）。查明**产品回调在主线程**（符合契约），落在线程池的是用例自己的续体，修法是把基准线程挪到 `await` 之前；但「续体为何落在线程池」**在 7 次全量 / 23+ 个样本中未能复现**，因此如实写进用例注释与提交信息，而不是宣称「就是个坏测试」。

### 3. 公开 API 重载的源兼容探针

新增或修改公开 API 重载时，**手工做一次调用形状编译探针**：在临时工程里分别以旧写法与新写法调用（覆盖 `null` / 默认值 / 隐式转换这几类边界），确认旧写法仍能编译；再做一个负向控制（故意写错形状，确认编译器报 CS0121 / CS1503 等，而不是静默选中另一个重载）。**不引入 Roslyn / 机械守卫**——公开面形状变化低频，而探针要的是「调用方仍能编译」这一事实，不是 AST 比对。先例：LOC-001 的 `(string, TextAsset)` 重载使既有 `("en", null)` 变 CS0121；SET-015 / CONF-016 / SAVE-015 的 `default` 字面量二义同族。

## 四、约定（索引）

规则不止本文，为避免两处真相，这里只给入口：

| 主题 | 位置 |
|---|---|
| 命名 / 风格 / 注释 / 可见性 | `CLAUDE.md` → 编码规范 |
| 性能与 GC、异步、日志与异常 | `CLAUDE.md` → 对应小节 |
| 分层与扩展方式（静态服务、可选能力接口、管线、Bootstrap） | `CLAUDE.md` → 架构分层 |
| 各模块用法 | `Assets/XFramework/Runtime/<模块>/README.md` |
| 模块 README 的结构规范（一级骨架 / 命名唯一写法 / 文件树分工） | `Documentation/ModuleReadme.md` |
| 维护向记录（文件结构、版本沿革、审计记忆） | `Documentation/Modules/<模块>.md` |
| 框架级决策（候选模块 / 跨模块 / 工程化 / 外部）与模块台账索引 | `Documentation/Ledger.md` |
| 什么内容该写进哪份文档 | `CLAUDE.md` → 文档分层 |
| 审计判据与探针 | `Documentation/ModuleAudit.md` |
| 变更历史 | `Assets/XFramework/CHANGELOG.md` |

## 五、工具与运行环境

### `Tools/run-tests.ps1`

| 参数 | 说明 |
|---|---|
| `-Filter <片段>` | **正则**匹配（Unity `-testFilter` 的语义，不是子串）；留空 = 全量（门禁）。点号不转义即「任意字符」，`PoolTests` 会连 `CollectionPoolTests` 一起捞 |
| `-Fixture <类名>` | 按 fixture 精确匹配（内部生成 `\.<类名>\.`）；与 `-Filter` 互斥 |
| `-Platform` | `All`（默认）/ `PlayMode` / `EditMode`；带 `-Filter` 且未显式指定时只跑 `PlayMode` |
| `-ShrinkTolerance` | 全量跑时，用例总数比上次下降超过它就告警（发现「测试集静默缩水」）。**全量跑 0 用例直接判失败**（2026-10-14 补：缩到 0 恰好绕过上面那条告警，而它对使用者的呈现与真绿一模一样——详见 `Documentation/Ledger.md` 的「`Tests~` / 把测试移出包目录」条） |
| `-UnityPath` | 留空则按 `ProjectSettings/ProjectVersion.txt` 自动探测 |
| `-UseRepo` / `-Setup` | 强制在仓库本体跑（需先关编辑器）/ 创建测试壳后退出 |
| `-UpdateApiBaseline` | **公开 API 基线的更新模式**：重写 `Tests/Runtime/Architecture/PublicApiBaseline.txt`。自带过滤器（只跑基线 fixture）、与 `-Filter`/`-Fixture` 互斥；**写完按设计判失败**并打印差异，绿灯由随后一次不带开关的重跑给出。见 §六 |

### `Tools/check-docs.ps1`

| 参数 | 说明 |
|---|---|
| （无） | 按模块汇总包内 XML 文档告警（**按码分列**，十一个码与中文标签见脚本头部「覆盖范围」），并报告缺 `.meta` 的条目 |
| `-Module <名字>` | 逐条列出某模块的告警（文件:行号 + 按码打标 + 文案） |
| `-Project <csproj…>` | 默认检查包自身的两个程序集（Runtime 与 Editor） |
| `-Enforce` | 门禁模式：**门禁码**告警须 0、所有源文件都参与编译、所有条目都有 `.meta`、决策台账与模块 README 文件树合规、**示例代码编译通过**，任一不满足即失败（失败清单点名码） |

### `Tools/check-samples.ps1`

- 生成临时 csproj，把 `Samples~/**/*.cs` 编进包程序集与 Unity 托管程序集；`check-docs.ps1 -Enforce` 已自动调用（第六项）。
- **前提与 dotnet 门禁相同**：先让 Unity 导入一次（生成 csproj 与 `Library/ScriptAssemblies`）。前置缺失时脚本直接失败，不给假绿。
- 只守编译：场景 / 预制体的引用由人工验收或一次性探针覆盖。

### 性能基准套件

- 运行：`pwsh -File Tools/run-tests.ps1 -Fixture XFrameworkPerformanceBenchmarks -Platform EditMode`
- 全套件标 `[Explicit]` + `[Category("Performance")]`，**不进入全量门禁**（全量里计为跳过）。
- 结果在 `TestResults/run-*.xml` 的 `##performancetestresult2` 行（每个用例的 Median / SampleCount / 分布），路径随测试壳。
- 只测量耗时，不做零 GC 断言——分配尺子在本环境不可用（见 §六）。

### 测试壳

`<仓库名>.TestRun` 通过 junction 共享本仓库的 `Assets/Packages/ProjectSettings`，但拥有独立 `Library`。
因此**跑测试不必关闭编辑器**，且 Unity 为新文件生成的 `.meta` 会直接落回真实仓库。
新机器上先跑一次 `pwsh -File Tools/run-tests.ps1 -Setup`。

## 六、这套流程没覆盖到的地方

知道边界比以为没有边界安全：

- **编译覆盖不可靠**：`dotnet build` 只编 csproj 里 `&lt;Compile Include&gt;` 列出的文件，而 csproj 由 Unity 生成、**重生成时机不可预测**（2026-09-27 实测：`Assets/XFramework/Editor/` 下的 3 个文件先是被报「不在任何 csproj 里」，随后又自动出现在声明中）。唯一保障是 `check-docs.ps1 -Enforce` 的「未参与编译即失败」——但它只管文档线；编译线仍需每次先 `grep` 确认文件在列表里。
- **没有 CI（已评估，2026-10-13 裁定：暂不接入）**：三条门禁全靠人工在阶段收尾时跑。成本要点（Unity 许可形态 / runner / 与测试壳关系）与三条重开判据见 `Documentation/CI.md`。
- **偶发用例会打红门禁**：脚本有「用例总数骤降」告警，但没有「疑似偶发」的识别。遇到与本次改动无关的失败，先单独重跑该用例（`-Filter <用例名>`）判断，再决定是修用例还是查产品。
- **门禁耗时**：测试约 82 秒（双平台），文档约 90~180 秒。
- **审计不可自动化的部分**：判据清单是「见过的缺陷类」的归纳，新类型的缺陷它照不到；清单里的探针只做诊断、不进门禁，判定仍靠人。
- **文档分层靠人**：模块 README 与 `Documentation/Modules/` 之间的搬运没有门禁——`check-docs.ps1` 对**包内** `.md` 零覆盖（它读 `Documentation/` 下三处只为决策台账结构，不是内容检查）。要机械验证只能自建（「源侧删除行 ⊆ 目标侧新增行」的 git 断言，逐（源→目标）配对跑），且属一次性工程，不挂进阶段收尾。
- **包 README 的四张清单表另有测试侧守卫（2026-10-13）**：`Tests/Runtime/Architecture/ReadmeTableTests` 守
  「模块索引 / 可替换的实现一览 / 可配置项一览」的模块集与 `Runtime/` 直接子目录**双向相等**、模块索引表的
  文档链接落盘、命名空间列与程序集双向覆盖、「主要功能」表按登记表守（未登记即红）。**它不改变上一条**：
  `check-docs.ps1` 对**包内** `.md` 仍零覆盖，文档搬迁仍靠人；守卫也**不守**行内说明文字的陈旧与四表刻意各异的顺序。
- **公开 API 基线与 `CHANGELOG` 的迁移表之间没有机械联系**：基线知道「公开面变成什么样了」，但不知道
  「哪次变更被记录过」——迁移表是给人读的三列表格，没有机器键；测试也读不到 git（仓库根在测试壳里不可达）。
  反向的漂移不需要防（写了迁移表却忘更新基线，下一次跑必红）。三处指认：**失败消息正文**（一定会被人读到）、
  **基线文件头部**（`#` 注释，不参与比较，故永不 churn）、本节这一条。
- **基线是「测试在显式开关下反写仓库文件」的唯一入口**（2026-10-13）：开关是 `Tools/run-tests.ps1
  -UpdateApiBaseline`（内部设 `XFW_UPDATE_API_BASELINE=1`，测试只认 `"1"`）。写入的基线**必须与它的
  `.meta` 一起提交**——`.meta` 由 Unity 经测试壳回写，漏 `git add` 没有任何机械信号（同上一条）。
- **`.meta` 门禁看的是磁盘、不是索引**（2026-10-12 实测）：`check-docs.ps1` 报「全部条目都有 `.meta`」，而四个新测试目录的**目录级** `.meta` 一直没入库——它们由 Unity 在导入新目录时生成、经测试壳回写，人没 `git add` 就一直是未跟踪状态。**门禁本来就只能这样**（`.meta` 由 Unity 生成，不查磁盘就没有意义），所以收尾时**还要用 `git status --short` 肉眼核一遍未跟踪文件**——这一步没有机械替代。
- **工作区行尾（2026-10-14 已归一；判据随时可查）**：**判据用 `git ls-files --eol`**——`w/crlf` 表示该文件的磁盘字节是 CRLF 而索引是 LF，`w/lf` 才是与仓规一致。**`git status` 看不见这件事**（比较时会归一，所以提交内容一直是对的），只有这一条判据能发现。2026-10-13 发现时残留 259 个 `w/crlf`，2026-10-14 已就地归一（归零、`git status` 仍净、**内容零变化故无提交**）。
  **成因**：系统级 `core.autocrlf=true` 加 `.gitattributes`（2026-10-11）落地**之前** git 自身物化工作区留下的**冻结核**（决定性证据：reflog 里 2026-09-10 的 `reset` 恰好只重写 3 个文件而 3 个全是 `w/crlf`；属性层对这批与 LF 批逐字同规则，故不是规则漏网），此后零新增——`Tools/` 两脚本（唯一写跟踪文本的 `-UpdateApiBaseline` 走 C# 显式 `\n`）、Editor 侧唯一写盘路径（`XFrameworkDependencyInstaller` 写 `manifest.json`、保留原字节）、Unity 的 `.meta` 导入（新 `.meta` 实测全 LF）三条嫌疑均已实测排除。
  **未封堵的一处**：Unity 菜单新建的脚本会以 CRLF 落盘（`ProjectSettings/EditorSettings.asset` 的 `m_LineEndingsForNewScripts = 0` / OS Native，2026-10-14 用户裁定不改动该工程设置）——它只影响工作区字节，提交时的 `CRLF will be replaced by LF` 警告属正常。
  **再归一化配方**（Git Bash；两步缺一不可，先确认 `git status` 干净、Unity 已关闭）：
  `git ls-files --eol | awk -F'\t' '$1 ~ /w\/crlf/ {print $2}' > /tmp/eol.txt`（生成清单）→
  `xargs -0 -a /tmp/eol.txt sed -i 's/\r$//'`（字节归一）→
  `xargs -0 -a /tmp/eol.txt git checkout --`（**落定索引 stat**：`sed` 单独跑会后留**假报的 ` M`**——哈希与索引逐字相同、`git diff` 空，但 `git status` 说改了；这一条从索引重写并刷新 stat，M 即消失）。
  不要走 `rm` + `git checkout --`（同样有效，但中途会短暂删掉工作区文件）；`git checkout-index -f` 实测是空操作。全量重检出（`git rm --cached -r . && git reset --hard`）代价大而收益为零——1043 个本来就是 LF。
- **零分配断言在本环境可能恒真（尺子坏了）——2026-10-12 已处置**：2026-10-11 实测
  `GC.GetAllocatedBytesForCurrentThread()` **恒返回 0**（分配 1 MB 数组后增量仍是 0 B），
  `Profiler.GetTotalAllocatedMemoryLong()` 与 `Profiler.GetMonoHeapSizeLong()` 同样恒 0；
  当时 **5 个文件 / 11 处** `Assert.AreEqual(0L, …)` 因此全是恒真的（清点与处置见各模块的决策台账：`Modules/{Log,Timer,Message,UI,Reactive}.md`）。
  2026-10-12 的处置：11 处全部改成**两道防线**，并补了尺子自检与源码守卫。现在这一节的纪律是**机械的**：
  - **唯一合法的分配尺子是共享工具** `Tests/Runtime/Architecture/AllocationRuler.cs`（`Profiler.GetMonoUsedSizeLong()`，
    粒度约 4–8 KB）。**不要自己写测量循环**——预热次数、双 Collect、放大倍数、阈值口径只有一份才不会各自漂移。
  - **两把恒零尺子已被源码守卫封禁**（`AllocationRulerTests.BrokenRulers_AreNotUsedAnywhereInThePackage`，
    扫**整个包**）：测试里用它们是恒真断言，生产里用它们是假遥测。要跟它们对话（例如再做一次环境实测探针）
    就做成**不入仓**的一次性探针。
  - **尺子自检先跑**：`AllocationRulerTests` 的三条自检（看得见 1 MB、看得见 10 万个 32 B 小对象、
    零分配时保持安静）是全部粗尺子断言的**前提**。尺子坏掉时红在那里，而不是让下游断言集体退化成恒真。
    若哪天可用尺子也坏了：回到结构断言 + 直接观测，或改用 `GC.GetTotalMemory(false)` 并重跑自检。
  - **优先结构断言**：能指出「这正是分配/装箱的原因」的事实（反射可验或行为可观测）就别靠尺子——
    先例是 `GetEnumerator()` 返回类型即装箱的充要原因、`TimerSlot<TState>.State` 的字段类型即装箱的充要原因。
    粗尺子只作第二道防线，阈值取**实测信号 1/4**（注入「每迭代一个对象」量到 S 再定）。
  - **一条实测教训（2026-10-12）**：粗尺子对**瞬时垃圾**型回归的灵敏度会被测量窗口内的 GC 削掉一截
    （格式化回归实测只读到 417,792 B，而同批的持有式注入读到 1.5–3.2 MB）。那类回归**以直接观测为准**
    （探针 `ToString()` 计数、计数 sink——见 `LogAllocationTests`），尺子是粗网。
- **「关闭域重载连跑两遍」这条验收脚本表达不了**（2026-10-12 实测）：`Tools/run-tests.ps1` 每次是一个
  全新的 batchmode 进程，**跨播放会话的静态存活根本不会发生**，所以它验不了会话级复位。
  自动化等价物 = `Tests/Runtime/Architecture/SessionResetTests`（单会话内模拟「初始化 → 退出 → 复位 → 再初始化」）
  + `AutoInitTests` 族清单守卫；**真验收是手工的**：把 `ProjectSettings/EditorSettings.asset` 的
  `m_EnterPlayModeOptions` 临时置 1（关域重载），编辑器内连跑两遍 PlayMode 全量，跑完改回
  ——仓内**不常住**该设置。
  **2026-10-14 已执行并结案**：第一遍绿、第二遍红——红在 XSettings 的跨会话静态态累积（重复重放；
  两遍唯一不对称是 `[SettingsManager]` 27→28）⇒ 按「会话边界清空 + 句柄用即重登记」修复
  （`018aad6` + `4701bbf`）⇒ 重跑两遍全绿（`Unhandled log message` 0、两遍逐类完全相同）。
  **这条验收自证了价值**：同一个用例在全新 batchmode 进程里 1/1 通过，缺陷只在跨会话下显形——
  「脚本表达不了」的那部分正是它抓到的。配方原样保留，回归时按上面三步重跑（改设置 → 连跑两遍 → 改回），
  跑完核 `git status` 干净（该设置不留 diff）。

## 七、发布与分发

### 发布一个版本

1. 按 `CHANGELOG.md` 顶部的版本建议改 `Assets/XFramework/package.json` 的 `version`，并把 `[Unreleased]` 落成带日期的版本小节。
2. 跑两条门禁：`Tools/run-tests.ps1`（全量双平台 0 失败）与 `Tools/check-docs.ps1 -Enforce`（0 告警）。
3. 在仓库根打与包版本一致的 tag（例如 `0.4.0`），**推送由人决定**（与提交授权同一条纪律）。
4. 扩展包 `Assets/XFramework.Cinemachine` 用同一个 tag 发布（它单独安装、不随核心包发布）。

### OpenUPM（待上架）

- OpenUPM 只跟随**发布 tag**，不跟 `main`；仓库需公开（已满足，MIT）。
- 提交入口：openupm.com → Add package → 仓库填 `https://github.com/venusir/xframework`，**包目录填 `Assets/XFramework`**（与 UPM 的 `?path=` 同一个目录）。
- 上架后再补两处安装说明：根 README 与包 README 的 scoped registry 一行式安装。`package.json` 的 `dependencies` 是否可以改为注册表依赖（UniTask / YooAsset 均在 OpenUPM）**等上架后逐条核对包名与版本再做**，不预先声明。
- 核对清单：OpenUPM 页面上的包名 / 版本 / 包目录 / license 与 `package.json` 逐字一致；用一次裸工程按 OpenUPM 的 scoped registry 安装，确认 `Library/ScriptAssemblies` 实际产出 `Venusy609.Xframework.dll`（看产出，不看退出码）。
- **外部字段与流程会漂移**：以 OpenUPM 页面当时的字段为准；本文只记「要核对什么」，不抄写页面。

## 附：一个完整回合

1. `dotnet build Venusy609.Xframework.Editor.Tests.csproj --artifacts-path "$env:TEMP\xfw-artifacts" -v q --nologo` → 0 error
2. `pwsh -File Tools/run-tests.ps1 -Platform EditMode -Filter <模块>` → 0 失败
3. `git commit`（信息格式见 `CLAUDE.md`；验证证据写进提交信息）→ 进入下一个提交单元。
   **两类例外停下请确认**：公开 API 变更、破坏性变更
5. 阶段收尾：`pwsh -File Tools/run-tests.ps1` + `pwsh -File Tools/check-docs.ps1 -Enforce`
