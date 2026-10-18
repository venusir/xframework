# 第三轮全面审计（2026-10-18）

> **点快照**：本文件记录第三轮全面审计在边界提交 `<冻结 SHA>` 上的发现、R2 修复复核、裁定建议与分步实施计划。所有 `file:line` 以该提交为准，不承诺长期有效。
> **收件人**：维护者（执行本轮修复时）+ 下一轮审计者。长期结论按收件人分流到模块 README 与决策台账，本文件不复制它们，只做发现与索引（见 §13）。
> **审计轮只出清单，不动代码**。本文件是审计产出；修复实施是后续独立阶段。
> 判据与探针定义见 `Documentation/ModuleAudit.md`；本文件不重复 18 类判据的全文。

## 0. 元信息与边界

- 边界 SHA：`661a7609b292a59b4a07b194712c098ab9c811e2`（审计开始工作区干净；审计骨架提交 `fd1a7a3` 只改 `Documentation/` 与 `CLAUDE.md`，未触 Runtime）。
- 上一轮边界：`2d584b6b803593010f5548afe7b03f75df8a7ab5`（R2）。
- 深度分档：T1 全量重审 / T2 增量复审 / T3 紧凑留痕 / T2+ 扩展包；24 个核心模块全部留 18 类判据痕。
- 基线门禁（2026-10-18）：BUILD 两个核心 csproj 0 error；FULL PlayMode 1855/1855、EditMode 539/539；`check-docs.ps1 -Enforce` 通过（24 模块 / 370 条台账 / 文件树一致）；`PublicApiBaselineTests` 1/1。
- 本轮**未做**：未修改任何 `Assets/**` 源码、包 README、CHANGELOG、测试；未执行需真机/跨播放会话的手工验收（见 §11）；未修改 `AuditRound2.md`。
- 证据等级：`实测` = 本轮实际跑过探针/读第三方源码可证；`推理` = 调用链/静态状态可证但未运行；`需实测` = 运行期或真机才能确认，已给命令。
- 台账计数：R2 交接记录 370 条；当前按 `check-docs.ps1 -Enforce` 口径为 370 条（已做 104 / 未做 41 / 待裁 14 / 暂不做 36 / 不做 171 / 已推翻 5）。直接 grep 全部模块文档会多算 `Audio.md` 的 1 条非台账状态行（371），本报告以门禁口径为准。

## 1. 范围与方法

### 1.1 分层表

| 层级 | 模块 | 深度 |
|---|---|---|
| T1 全量重审（12） | Asset、Audio、Config、Data、Input、Localization、Save、Serialize、Settings、Timer、UI、Update | 读全部 `.cs`、README、维护向记录、测试、接缝；18 类逐类留痕；R2 本模块 P0/P1/P2 全部复核；新发现 |
| T2 增量复审（10） | Camera、Diagnostics、Event、File、Log、Message、Pipeline、Pool、Reactive、Scene | 读变更文件全文 + 相关上下文；18 类逐类留痕；R2 P0/P1 与开口项逐条复核；P2 状态复核 + 代码类抽样；新发现 |
| T3 紧凑留痕（2） | Bootstrap、Lock | 18 类逐类至少一行；开口项复核；Bootstrap 补 R2 跳过的 18 类留痕 |
| T2+ 扩展包（1） | Cinemachine（`com.venusy609.xframework.cinemachine`） | 与核心 Camera 的接缝 + 扩展包内部增量；依赖三处、包门禁覆盖、示例 |

### 1.2 方法

- 每个模块读全部/变更源文件、模块 README、维护向记录、CHANGELOG 与测试；按 `ModuleAudit.md` 的 18 类判据逐类留痕：命中写发现 ID，未命中写「扫过」，不适用写「不适用 + 理由」。
- R2 修复复核：1 条 P0 + 29 条 P1 逐条映射 `R2-ID → 修复提交 → 测试/文档 → 结论`；224 条 P2 按模块聚合 + 代码类抽样。
- P0/P1 全部经主会话回读源码复核；P2 按分层抽样。
- 第三方对照按模块选取；无明确对标物时写「本轮未做」。
- 统计命令见附录 A。

## 2. 总览

> 快照统计（只作导航；不随开发增长更新）：**1 条 P0、7 条 P1、131 条 P2**，共 139 条；R2 复核：1 条 P0 回归、29 条 P1、224 条 P2 聚合；探针 14 项（5 项 R2 遗留 + 9 项 R3 新增，已执行 2 项编译探针）。

### 2.1 最需要先读的 10 条

1. **R3-CONF-001（P0，回归）**：R2 把行类型示例改成裸字段，但不实现 `IConfigRow<int>.Id`，照抄 CS0535——按随包文档建表直接编译失败。
2. **R3-AST-001（P1）**：`AssetPlayMode` 枚举重排与 tag `0.3.0` 的已序列化值冲突；旧场景 Offline=0 升级后变 EditorSimulate，Player 启动抛 `PlatformNotSupportedException`。
3. **R3-INP-001（P1）**：包 README/安装器缺 `com.unity.inputsystem`，核心 asmdef 却无条件引用；按包文档安装会编译失败。
4. **R3-MSG-003（P1）**：`IDestroyCancellationToken` XML 示例缺 `IMessageSubscriber`，照抄 CS1929（R2 MSG-002 只修了 ODE 半边）。
5. **R3-CONF-002（P1）**：ScriptableObject Table 必须继承公开基类 `ScriptableObjectTable<,>`，使用文档零提及；按文档自建普通 SO 会得到错误归因。
6. **R3-AUD-001（P1）**：README 教「用项目自己的 AudioMixer」，但框架播放源 `outputAudioMixerGroup` 恒 null，`AudioMixer.SetFloat` 对框架声音无效。
7. **R3-TMR-001（P1）**：TMR-002 的补扫只在表内，跨表新建定时器仍可晚约 2.13s。
8. **R3-CIN-001（P1，需实测）**：扩展包 `Activate` 的「强制完成混合」失活/激活 hack 可能不重启混合；文档称「测试锁定」但无对应用例。
9. **R3-AST-002（P2，回归）**：会话复位只退订 `lowMemory`，未拆 `SceneManager.sceneUnloaded`；旧实现跨会话存活。
10. **R3-SET-001（P2，回归）**：加密装饰器实现异步能力后，同步内层把活对象交池线程 `JsonUtility.ToJson`，R2 SET-013 的竞态在推荐用法上复活。

> 另有 R3-UIB-003（HUD 停更后目标丢失不再回收）、R3-UPD-001（Clear 恢复指引与实现相反）、R3-SCN-001（SCN-003 修复未生效）三条 `[回归]`，见 §6。

### 2.2 P1 分布（按模块）

| 模块 | P1 |
|---|---|
| Asset | R3-AST-001（枚举兼容） |
| Input | R3-INP-001（依赖安装） |
| Config | R3-CONF-002（SO 基类文档） |
| Audio | R3-AUD-001（AudioMixer 配方） |
| Timer | R3-TMR-001（跨表补扫） |
| Message | R3-MSG-003（XML 示例不可编译） |
| Cinemachine | R3-CIN-001（强制 cut 需实测） |

### 2.3 跨模块主题索引

- §7.1 R2 修复的文档-代码同步
- §7.2 公开 API 与序列化兼容
- §7.3 依赖声明与安装真相
- §7.4 会话复位族的第二层
- §7.5 测试真实性与新面覆盖
- §7.6 异常隔离与批处理
- §7.7 平台/运行模式矩阵
- §7.8 台账、计数与文档状态

### 2.4 R2 修复复核摘要

- 1 条 P0（CONF-001）**回归**；29 条 P1 中 28 条已修复/文档化，1 条部分（MSG-002 → R3-MSG-003）。
- 224 条 P2 以「已修复/文档化」为主；部分修复与回归集中在 Asset（AST-003/008）、Config（004/012/015/016）、Data（006）、Serialize（002/004）、Localization（004/013）、Input（005）、Update（001/002/004）、Settings（010/012/018）、Save（002/003/004/007）、Audio（003/006/008/010/014）、Timer（004/011）、UI（UI-C-012/UI-B-010）、File（009/012）、Camera（003/005）、Scene（002/003）、Log（003）、Message（002/003/004）。详见 §3.2。

### 2.5 本轮明确未做

- 未修改任何 `Assets/**` 源码、包 README、CHANGELOG、测试；只在 `Documentation/` 与 `CLAUDE.md` 做审计登记。
- 未执行需真机/手工/Server 构建的探针；未跑跨播放会话验收。
- 未修改 `Documentation/AuditRound2.md`；未重新论证 `不做/暂不做` 条目（除 §9 的 14 条待裁）。
- Cinemachine 扩展包做了接缝 + 内部增量，未做全量 A–F 重审。

## 3. R2 修复复核

### 3.1 P0 + P1（1 + 29 条）

| R2 ID | 级别 | 模块 | R2 问题（简） | 修复/处置证据 | R3 结论 |
|---|---|---|---|---|---|
| CONF-001 | P0 | Config | 行类型示例用自动属性，Json/SO 主键恒 0 | `bb6232b`、`f5b8661` | **回归**：改为裸字段后不实现 `IConfigRow<int>.Id` → CS0535，见 R3-CONF-001 |
| POOL-002 | P1 | Pool | 池静态状态跨会话存活且无复位 | `e6166c1` | 已修复（会话复位在位；跨会话手工验收仍见 R3-PRB-05） |
| DIAG-001 | P1 | Diagnostics | 页签幂等登记承诺与实现相反 | `eebbfed` | 已修复（文档化：按实例幂等） |
| SCN-001 | P1 | Scene | 回收段受调用方令牌约束，取消丢句柄 | `c7edcfa` | 已修复；SCN-003 另见 R3-SCN-001 `[回归]` |
| MSG-001 | P1 | Message | 首条 Publish 通道在缓冲写入前被回收 | `c3e16c9` | 已修复 |
| MSG-002 | P1 | Message | IDestroyCancellationToken XML 示例销毁后抛 ODE | `4e8645b` | **部分修复**：ODE 半边已修，示例类仍缺 `IMessageSubscriber` → R3-MSG-003 P1；台账仍标 `未做`（R3-MSG-001 P2） |
| RCT-001 | P1 | Reactive | 链式 Select 中间派生值不可达 | `490bc99` | 文档化处置（README 说明不可达） |
| TMR-001 | P1 | Timer | After<TState> 示例参数顺序写反 | `4e8645b` | 已修复（XML 示例） |
| TMR-002 | P1 | Timer | 回调内新建定时器不进入本趟 min | `b699202` | 已修复；跨表残留见 R3-TMR-001 |
| TMR-003 | P1 | Timer | Create/Restart 锚点取陈旧逻辑时刻 | `63f37f8` | 文档化处置（已知限制）；修法仍待裁（R3-ADJ-11） |
| LOG-001 | P1 | Log | 同会话重建截断 JSONL 且 seq 重计 | `9403343` | 已修复 |
| LOG-002 | P1 | Log | sink 内 Debug.Log 同步重入 → StackOverflow | `184bf54` | 已修复；LOG-003 的 Flush 半边残留见 R3-LOG-001 |
| DATA-001 | P1 | Data | IDataBlock XML 示例照抄编译不过 | `4e8645b` | 已修复（XML 示例） |
| AST-001 | P1 | Asset | 资产加载族取消不释放底层句柄 | `84dec73` | 已修复 |
| AST-002 | P1 | Asset | README 加载模式表仍写「下载 → 激活」 | `e93ea26` | 已修复 |
| FILE-001 | P1 | File | ICryptoProvider.Decrypt null 契约被装饰器透出 | `db2e3e1` | 已修复（两读面统一 `IOException`） |
| FILE-002 | P1 | File | 移动端 Streaming 异步读写不满足任意线程契约 | `6d9f42a` | 文档化处置（明确要求主线程；XML 未同步，P2） |
| CONF-002 | P1 | Config | README 声称没有 internal 类型 | `03d8494` | 已修复（文档）；残留 SO 基类缺口见 R3-CONF-002 |
| LOCK-001 | P1 | Lock | 解锁回调内重加同类型锁被静默丢弃 | `23f5b0d` | 已修复（红证 + 负向控制） |
| INP-001 | P1 | Input | 长按时长只为已读动作计时 | `9a505ae` | 已修复 |
| UIA-001 | P1 | UI | 以基类 T 打开的面板无法关闭/导航/查询 | `57c417d` | 已修复；在途打开键残留见 R3-UIA-001 |
| UIA-002 | P1 | UI | UpdateManager.Clear 后 README 恢复路径不成立 | `5681ce1` | 已修复；SetInstance 文档残留见 R3-UIA-010 |
| UI-B-001 | P1 | UI | HUD 每帧派发无逐实例异常隔离 | `446bc38` | 已修复；回归见 R3-UIB-003 |
| UI-B-002 | P1 | UI | README「先 Destroy 再自行驱动」逃生口不可行 | `df4299a` | 文档化处置（改为 Clear 路径） |
| UI-C-001 | P1 | UI | 弹窗尺寸单位错配 | `069e0c3` | 已修复 |
| SET-001 | P1 | Settings | 加密装饰器遮蔽内层 IAsyncSettingsStore | `d078656` | 已修复；回归见 R3-SET-001 |
| SET-002 | P1 | Settings | LoadAsync 同步降级把 defaultFactory 挪到线程池 | `78a6858` | 文档化处置（订正线程契约） |
| SET-003 | P1 | Settings | 直接 Dispose 门面返回的管理器后留僵尸登记 | `78a6858` | 文档化处置（订正生命周期契约） |
| SET-004 | P1 | Settings | 会话边界不清句柄订阅流 | `78a6858` | 文档化处置（订正会话边界契约） |
| AUD-001 | P1 | Audio | 短于一拍的 one-shot 卡 Starting 到超时 | `248d473` | 已修复 |

### 3.2 P2（224 条）聚合复核

> 抽样规则：T1 代码类 P2 100% 回读；T2 每 3 条抽 1；T3 只核状态与证据存在性。总体以「已修复 / 文档化」为主，下列是**部分修复、未修复或回归**的例外（完整条目仍以 R2 文档为准）。

| 模块 | 部分/未修复/回归 | R3 关联 |
|---|---|---|
| Asset | AST-003/008 部分（`sceneUnloaded` 订阅未拆） | R3-AST-002 `[回归]` |
| Config | CONF-004/012/015 部分；CONF-014 未修；CONF-016 部分；CONF-017 未处理 | R3-CONF-003..011、CONF-014 探针 |
| Data | DATA-006 部分（单块快照未记 format） | R3-DATA-002 |
| Serialize | SER-002/004 部分 | R3-SER-002/003 |
| Localization | LOC-004 文档残留；LOC-013 迁 Message 后台账仍标 `未做` | R3-LOC-004、R3-MSG-001 |
| Input | INP-005 部分（包依赖表/安装器仍缺 Input System） | R3-INP-001（升 P1） |
| Update | UPD-001/002/004 部分 | R3-UPD-001/002/003 |
| Settings | SET-010/018 部分；SET-012 未关闭 | R3-SET-002/003、R3-PRB-01 |
| Save | SAVE-002/003/004/007 部分 | R3-SAVE-001..006 |
| Audio | AUD-003/006/008/010/014 部分 | R3-AUD-002/003/006/007 |
| Timer | TMR-004/011 部分 | R3-TMR-003 |
| UI | UI-C-012 部分；UI-B-010 部分；UI-B-013 文档未写 | R3-UIB-001/012 |
| Event | EVT-001..007 全部修复/处置；`Event.md:53` 理由仍陈旧 | R3-EVT-001 |
| Reactive | RCT-011 部分；RCT-009 台账缺失 | R3-RCT-002 |
| Pipeline | PIPE-001..004 已修；PIPE-005/006 文档化；Bootstrap 子阶段超时只闭合一端 | R3-PIPE-001..004 |
| File | FILE-009 部分（漏 OSXServer）；FILE-012 部分（计数）；FILE-002 XML 未同步 | R3-FILE-001/005 |
| Diagnostics | DIAG-001/004/006 已修；无部分项 | — |
| Camera | CAM-003 部分（三处 XML 仍写 `<= 0`）；CAM-005 条目文字过时 | R3-CAM-003、R3-CIN-007 |
| Scene | SCN-002 部分（只改 README）；SCN-003 **修复未生效** | R3-SCN-001 `[回归]`、R3-SCN-003 |
| Pool | P2 8 条已修/文档化；POOL-010 保持公开；无部分项 | — |
| Log | LOG-003 部分（只修 Write 路径）；LOG-008 1 子项证据不足 | R3-LOG-001 |
| Message | MSG-002 部分；MSG-003 部分；MSG-004 部分 | R3-MSG-003（P1）、R3-MSG-002、R3-INP-006 |
| Cinemachine | CAM-005 的「与核心契约不一致」前提已不成立 | 建议 `不做`/`待裁`，见 §13 |

### 3.3 探针轨

R2 遗留的 SET-012、CONF-014、CONF-017、FILE-009、F8 **均未执行**：审计轮只读，且缺手工/真机/Server 构建环境。R3 新增 9 项中，**R3-PRB-06（CONF-001 编译探针）与 R3-PRB-13（MSG-003 编译探针）已执行**；其余待执行。状态、配方与 owner 见 §11；未执行不阻塞报告收口。

### 3.4 台账实时计数

`check-docs.ps1 -Enforce` 口径：24 个模块 / 370 条（已做 104 / 未做 41 / 待裁 14 / 暂不做 36 / 不做 171 / 已推翻 5）。直接 grep 全部模块文档会多算 `Audio.md` 的 1 条非台账状态行（371），本报告以门禁口径为准。

## 4. R3 P0 清单

### R3-CONF-001 · P0 · A1/D3 · R2 修复把行类型示例改成裸字段，照抄 CS0535

- **位置**：`Runtime/Config/README.md:27`（快速使用）、`:308`（方式一）；`Runtime/Config/ConfigManager.cs:29`（门面 XML 示例）。正确形状见 `Tests/Editor/Config/JsonRowSerializationTests.cs:66-75`（`public int Id;` + `int IConfigRow<int>.Id => Id;`）与 `ExplicitInterfaceRowTests.cs:40-42`。
- **发现**：R2 的 CONF-001 修复把三处示例的自动属性改成裸字段 `public int Id;`，但 `IConfigRow<TKey>` 的成员是 **属性** `TKey Id { get; }`。C# 字段不能实现接口属性，照抄「快速使用」即 `CS0535: 'ItemRow' does not implement interface member 'IConfigRow<int>.Id'`。R2 把「运行期静默主键 0」换成了「编译期直接失败」——方向正确，但示例本身仍不可用，且比原缺陷更早暴露。
- **复核**：主会话回读三处示例与接口定义；临时 `netstandard2.1` 编译探针（`%TEMP%\xfw-audit3\probe-conf001`）实测 `CS0535`；负向控制加 `int IConfigRow<int>.Id => Id;` 后 0 error。`[实测]`
- **失败场景**：第三方按 README「快速使用」复制 `ItemRow` → 编译失败；若改为不实现接口，则 `ConfigTable<T>` 取不到主键类型，构造/注册期抛 `ConfigException`。两条路都不通。
- **建议**：三处示例统一改为「字段 + 显式接口实现」；在「已知限制」写「Json/ScriptableObject 只序列化字段；接口主键需显式实现」；CHANGELOG 记录这是 R2 修复引入的回归。
- **验证**：上述编译探针正/负两态；`run-tests.ps1 -Filter Config -Platform All`；`check-docs.ps1 -Enforce`。
- **落点**：`Runtime/Config/README.md`、`ConfigManager.cs` XML、`CHANGELOG.md`；实施批次 0。

## 5. R3 P1 清单

### R3-AST-001 · P1 · A1/C2/D4 · AssetPlayMode 枚举重排的「包未发布」前提与 tag 0.3.0 冲突

- **位置**：`Runtime/Asset/AssetInitOptions.cs:8-27`（`EditorSimulate=0/Offline=1/Host=2`）；`Runtime/Bootstrap/DefaultGameLauncher.cs:29-32`（`[SerializeField] AssetPlayMode _assetPlayMode`）；`CHANGELOG.md:24-32`；`Documentation/Modules/Asset.md:91`。
- **发现**：CHANGELOG 与模块台账把枚举重排的依据写成「包未发布，无需为既有序列化让位」，并把整条归为「公开 API 源兼容新增」。但仓内存在 tag `0.3.0`，该 tag 的 `AssetPlayMode` 是 `Offline=0/Host=1`，且 `DefaultGameLauncher` 已带可序列化的 `_assetPlayMode` 字段（`git show 0.3.0:...` 实测）。
- **复核**：主会话用 `git show` 核对 tag 内容与当前枚举；`0.3.0` 是否真被第三方消费仓内无法证实。`[实测]`（tag 内容）/ `[需用户确认]`（分发事实）。
- **失败场景**：消费方用 0.3.0 保存过挂 `DefaultGameLauncher` 的场景（`_assetPlayMode: 0` = Offline）；升级后 0 被解释为 `EditorSimulate`：编辑器内静默切到模拟模式，Player 构建在 Phase 0 抛 `PlatformNotSupportedException`，整条启动管线中止。
- **建议**：先核实 0.3.0 分发事实。若分发过，恢复 `Offline=0/Host=1/EditorSimulate=2`（源兼容 + 序列化兼容）；若确认未分发，在 CHANGELOG 迁移表补一行「反序列化后请重新选择运行模式（整数值已变）」，并把「包未发布」改成可证实表述。**属破坏性/兼容性裁定，实施前须单独确认。** Bootstrap 切片复核为同一缺陷（归 R3-AST-001），不另立 ID。
- **验证**：0.3.0 场景存盘 `_assetPlayMode: 0`，升级后用 `SerializedObject` 读回断言 `Options.PlayMode == EditorSimulate`；负向控制显式置 `Host=2` 读回 Host。
- **落点**：`Documentation/Modules/Asset.md`、`CHANGELOG.md`；实施批次 2（需用户裁定）。

### R3-INP-001 · P1 · A1/D5 · 包 README/安装器缺 Input System，按文档安装编译失败

- **位置**：`Assets/XFramework/README.md:313-347`（第三方依赖表与安装 JSON）；`Editor/XFrameworkDependencyInstaller.cs:40-45`（`RequiredUPMDependencies`）；`Runtime/Venusy609.Xframework.asmdef:3-9`（无条件引用 `Unity.InputSystem`）；`Packages/manifest.json:9` 开发环境有 `com.unity.inputsystem: 1.19.0`。
- **发现**：核心 asmdef 无条件引用 `Unity.InputSystem`，但包 README 的依赖表、安装 JSON 与一键安装器只列 UniTask / YooAsset / Newtonsoft。第三方按包 README 安装后，`Unity.InputSystem` 程序集引用解析不到，整个运行时程序集编译失败。R2 把 INP-005 记为 P2；R3 按「默认安装路径必然失败」升 P1。
- **复核**：主会话逐字核对 README、安装器、asmdef 与 manifest；`Runtime/Input/README.md:594-600` 已写 Input System 依赖，但包级入口没有。`[实测]`
- **失败场景**：全新工程只按包 README 装三个依赖 + XFramework → asmdef 引用 `Unity.InputSystem` 失败（CS0246 / 程序集引用缺失），Runtime 整体不可用。
- **建议**：把 `com.unity.inputsystem`（`1.19.0`）加入包 README 依赖表、安装 JSON、安装器清单；同步更新 `DependencyDeclarationTests` 的期望（它只校验已列出的依赖，不列就不会红）。若希望 Input System 保持「可选后端」语义，则 asmdef 必须改为可选引用/条件编译——但当前公开面 `InputSystemOptions` 等已依赖它，短期以「硬依赖 + 文档列全」为准。
- **验证**：`DependencyDeclarationTests`；裸消费方探针工程只按 README 安装后编译 XFramework。
- **落点**：包 README、安装器、`CHANGELOG.md`；实施批次 1。

### R3-CONF-002 · P1 · A1/A2/D5 · ScriptableObject Table 必须继承公开基类，但使用文档零提及

- **位置**：`Runtime/Config/ScriptableObjectLoader.cs:21`（`LoadSOAsync<ScriptableObjectTable<T,TKey>>`）、`:78`（`public abstract class ScriptableObjectTable<TRow,TKey>`）；`Runtime/Config/README.md:319-320`（只给 `PreloadTableAsync<SettingsRow>(..., ConfigFormat.ScriptableObject)`，未说资产形状）；文件树 `README.md:57` 只把 `ScriptableObjectLoader.cs` 标 internal。
- **发现**：SO Table 的唯一可用形态是让资产继承 `ScriptableObjectTable<TRow,TKey>`；这是公开类型，但 README/包 README/维护向文档零提及，文件树还把它所在文件标成 internal。用户按文档自建普通 `ScriptableObject` 包装列表，加载时会被 cast 成 `null`，最终得到「Ensure AssetManager is initialized and the asset exists」的归因错误；该路径端到端零测试。
- **复核**：主会话核对 loader、公开类型与 README；`[实测]`（源码与文档对照），端到端加载未跑。
- **失败场景**：用户按「方式一」建 `SettingsRow` 的普通 SO 资产 → 加载失败 → 错误提示指向 AssetManager 初始化，排查方向完全错误。
- **建议**：README 的 SO 段补最小子类示例（`class SettingsTable : ScriptableObjectTable<SettingsRow,int> { public List<SettingsRow> Rows; }`）与资产创建步骤；文件树把公开类型与 internal loader 分开标注；补一条 EditMode 用例覆盖 SO Table 形状（失败面给明确消息）。
- **验证**：EditMode 用例构造 `ScriptableObjectTable` 子类并加载；负向控制普通 SO 得到文档化的错误消息。
- **落点**：Config README、`Documentation/Modules/Config.md`；实施批次 1。

### R3-AUD-001 · P1 · A1/A2/D4 · README 的「用项目自己的 AudioMixer」配方不可实现

- **位置**：`Runtime/Audio/README.md:386-390`；`Runtime/Audio/AudioSourcePool.cs:318-327`（`go.AddComponent<AudioSource>()`，未设 `outputAudioMixerGroup`）；`Runtime/Audio/AudioManagerImpl.cs:594-612`（无路由入口）。
- **发现**：README 说「要让声音进入项目自己的总线/效果链，把混音交给 Mixer：让框架通道音量留在 1.0，项目用 `AudioMixer.SetFloat` 控制总线音量。两者是相乘关系」。但框架动态创建的播放源 `outputAudioMixerGroup` 恒为 null，声音进不了项目 Mixer 的总线；`AudioMixer.SetFloat` 对框架声音静默无效。公开面也没有设置输出组/路由的入口。
- **复核**：主会话逐点核对 README、`CreateVoice` 与 `outputAudioMixerGroup` 的搜索（全模块零赋值）；`[实测]`（静态），运行期听感未验。
- **失败场景**：用户按「与音频中间件的边界」接入自己的 Mixer 总线，调音量/效果器完全无效果，且没有任何告警。
- **建议**：优先改文档：明确「使用项目自己的 Mixer 需要整体替换 `IAudioManager`/自定义后端（或等待路由扩展点）」，删除当前不可实现的配方；若确需保留该场景，再评估新增一个不暴露 Unity 类型的路由扩展点（公开 API 新增，另议）。
- **验证**：文档对照；运行期探针需能读到 `AudioSource.outputAudioMixerGroup`（当前公开面不可达）。
- **落点**：`Runtime/Audio/README.md`；实施批次 1（文档）。

### R3-TMR-001 · P1 · C2/B5 · TMR-002 的补扫只在表内，跨表新建定时器仍可晚约 2.13s

- **位置**：`Runtime/Timer/TimerCore.cs:314-331`（按 `_allTables` 顺序逐表 `Sweep`，`tableCountAtStart` 只处理「回调里新建表」）；`Runtime/Timer/TimerTable.cs:272-287`（补扫只扫本表槽位）；`Runtime/Timer/TimerTicker.cs:83-84`（文档称「同轴创建由扫尾补算覆盖」）。
- **发现**：TMR-002 给「回调里新建定时器」加了表内补扫，但补扫只在触发回调的那张表内。若状态表（带 `TState`）的回调里创建**无状态定时器**，它落在默认表；默认表若在 `_allTables` 中已先被扫过，本趟 `min` 不会包含新定时器的截止，档位停在粗档。示例：一条 60s 状态定时器把档位拉到 Tier7，其回调创建 0.1s 普通定时器 → 下一次派发要等约 2.13s，0.1s 定时器严重晚点。TimerTicker 的注释只承诺「同轴创建由扫尾补算覆盖」，跨表不成立。
- **复核**：主会话读 `Sweep`/`TimerTable.Sweep`/`TimerTicker` 调用链；`[推理]`，需 PlayMode 探针坐实（`-Filter TimerTier`）。
- **失败场景**：连续技能/冷却链在状态回调里创建短普通定时器，短定时器被拖到下一个粗档才触发；表现为动作卡顿/延迟，且无告警。
- **建议**：把补扫从「表内」提升到「本趟所有表」：在全部表扫完后，若 `firedAny`（需要 `Sweep` 汇总一个「本趟是否触发过回调」的旗标），再扫所有表里 `BornPass == _sweepPass` 的槽位取 min；或每表返回「本趟新建槽位的最小截止」并由 `TimerCore` 汇总。补一条「状态表回调创建普通定时器」的跨表回归用例（改前必红）。
- **验证**：PlayMode：60s 状态定时器 + 回调创建 0.1s 普通定时器，断言 0.1s 在下一拍附近触发；负向控制无状态表场景保持现状。
- **落点**：Timer 代码/测试/README；实施批次 1。

### R3-MSG-003 · P1 · A1/A3 · IDestroyCancellationToken XML 示例缺 IMessageSubscriber，照抄 CS1929

- **位置**：`Runtime/Message/IDestroyCancellationToken.cs:17`（`public sealed class PlayerModel : IDestroyCancellationToken`）、`:34`（`model.Subscribe<PlayerDiedMessage>(OnPlayerDied);`）；`Subscribe<T>` 扩展方法的接收者是 `IMessageSubscriber`（`MessageManager.cs:503` 起）。
- **发现**：R2 的 MSG-002 修复了「销毁后读 `_cts.Token` 抛 ODE」半边，但示例类仍只实现 `IDestroyCancellationToken`，没实现 `IMessageSubscriber`；`Subscribe<T>(this IMessageSubscriber, ...)` 因此找不到接收者，照抄示例得到 **CS1929**（"best extension method overload requires a receiver of type 'IMessageSubscriber'"；按写法不同也可能显示 CS1061）。随包 XML 示例仍不可编译。
- **复核**：主会话回读 XML 与扩展方法签名；临时 `netstandard2.1` 编译探针（`%TEMP%\xfw-audit3\probe-msg001`）原样 → CS1929；负向控制补 `IMessageSubscriber` 后 0 error。`[实测]`
- **失败场景**：第三方照抄示例定义 `PlayerModel` → 编译失败；若改用静态 `MessageManager.Subscribe` 则能编译，但绕开示例想教的「扩展方法 + 自动绑定」用法。
- **建议**：类声明改为 `: IMessageSubscriber, IDestroyCancellationToken`；保留 `using XFramework.XMessage;`；`model`/`OnPlayerDied` 片段占位加注释说明。与 R3-CONF-001 同属「R2 示例修复只修了一半」。
- **验证**：上述编译探针正/负两态。
- **落点**：`Runtime/Message/IDestroyCancellationToken.cs` XML；实施批次 0/1。

### R3-CIN-001 · P1 · [需实测] · Activate 的「强制完成混合」失活/激活 hack 不重启混合

- **位置**：`Assets/XFramework.Cinemachine/Runtime/CinemachineCameraManager.cs:417-423`（`ActivateSlot` 的 off/on）；`Documentation/Cinemachine.md:54`（「测试锁定」）。
- **发现**：`ActivateSlot` 先设 `DefaultBlend = Cut`、改优先级，再在「Brain 正在混合且目标就是当前活动相机」时短暂失活/激活 GameObject，注释称「强制重新入队」。但 Cinemachine 3.1.7 的 Brain 只在 `TopCameraFromPriorityQueue() != frame.Source.CamB` 时新建混合；同步 off/on 后下一帧仍是同一目标，原过渡按原时长继续。文档称「测试锁定」，但 `CinemachineCameraManagerTests` 没有一条用例在「正混合到该目标」的中途调用 `Activate` 并断言混合立即结束。上游官方强制完成入口是 `CinemachineBrain.ActiveBlend = null`。
- **复核**：主会话回读 `ActivateSlot` 与 Cinemachine 文档；上游 3.1.7 源码核对来自代理报告，未在本轮直接读包源码；运行期行为需 PlayMode 证据。`[推理]` / `[需实测]`。
- **失败场景**：从 A 过渡到 B（时长 2s），第 0.5s 调 `Activate("B")` 期望立即 cut：当前实现仍以剩余 1.5s 混合，画面构图与调用方预期不符；若调用方用 Activate 做「紧急切镜」会失效。
- **建议**：先写一条 PlayMode 红证（ManualUpdate 推进中途 `Activate`，断言 `IsBlending == false` / `ActiveBlend == null`）。若证伪，改用 `_brain.ActiveBlend = null`（官方强制完成）或零时长 blend + 一次 Update；若确认当前 hack 有效，则补上缺失的测试并把文档「测试锁定」坐实。
- **验证**：`run-tests.ps1 -Filter Cinemachine -Platform PlayMode`；改前必红。
- **落点**：扩展适配器/测试/`Documentation/Cinemachine.md`；实施批次 3（若确认）或批次 2（文档订正）。

## 6. R3 P2 清单（按模块）

> 一条一行；`[回归]` 表示由 R2 修复引入，`[跨模块]` 表示归拥有方立条，`[需实测]` 表示需运行期/真机证据。

### Asset

- **R3-AST-002 · P2 · B2/B5/B6 [回归]**：`YooAssetManagerImpl.cs:44-49,749-756,764-775` —— 会话复位只退订 `Application.lowMemory`，未拆 `SceneManager.sceneUnloaded`；旧实现跨会话存活，包复用后 YooAsset 处理器先执行。建议加幂等 `DetachSceneUnloadedHandler`。
- **R3-AST-003 · P2 · B5/D4**：`YooAssetSceneLoadSource.cs:87-108`、`YooAssetManagerImpl.cs:643-667` —— 卸载失败时适配器入口即丢 `_handle` 并 `Release`，管理器只在 `unloaded==true` 时清登记；失败后登记项成永久残影。建议拿到终态前不丢句柄。
- **R3-AST-004 · P2 · A1/A2/D4**：`IAssetManager.cs:72-90` 等 —— 三个热更 API 对不存在的包静默返回，违背 XML「失败抛 `InvalidOperationException`」。建议抛异常或改文档。
- **R3-AST-005 · P2 · B3/D4**：`YooAssetManagerImpl.cs:590-603,628-633` —— 场景加载进度回调抛非 OCE 异常时句柄既不释放也不登记。建议包 try/catch 记 Error 后继续。
- **R3-AST-006 · P2 · B3**：`AssetDownloaderHandle.cs:151-155` —— 下载器三个用户事件直接 `Invoke`，订阅者异常可打断 YooAsset `OperationSystem` 整轮更新。建议逐订阅者隔离。
- **R3-AST-007 · P2 · C1**：`YooAssetManagerImpl.cs:528` —— `WaitOrReleaseAsync` 以接口形参收 `HandleBaseWait`，每条加载路径装箱一次。建议改泛型约束。
- **R3-AST-008 · P2 · A2/D4**：`AssetManagerImpl.cs:299-311` —— `PreloadAllAsync(null)` 抛 NRE，`LoadAllAsync` 抛 ANE。建议对齐。
- **R3-AST-009 · P2 · A1/D4**：`DefaultGameLauncher.cs:29-32` —— Inspector 提供 `Host` 但无 `RemoteServices` 注入点，选 Host 必在启动抛 `ArgumentException`；字段 tooltip 未提示。

### UI-A（门面/导航/生命周期）

- **R3-UIA-001 · P2 · A2/E/D3**：`UIManagerImpl.cs:600,614` vs `:592,1792-1798` —— 在途打开键是请求类型，已打开去重是「具体类型 → 资源地址」两步；基类 `T` 并发打开不同资源会串台。建议在途键改为 `(requestType, assetPath)`。
- **R3-UIA-002 · P2 · A1/B2/F**：`UIManager.cs:862,877,948,982` —— 重复打开已存在面板时遮罩/捕获层排序按「请求层」而非面板实际 `Layer`。建议改用 `panel.Layer`。
- **R3-UIA-003 · P2 · B2/B5/D4/F [需实测]**：`UIManagerImpl.cs:1010-1020,1200-1223,1264-1276` —— 绕过 `CloseAsync` 直接销毁面板时，其遮罩/捕获层持有不回收；捕获层空栈点击提前 return，全屏挡板可永久驻留。建议每帧剪枝同步清理。
- **R3-UIA-004 · P2 · B3/D4**：`UIManagerImpl.cs:821-822,544-548` —— `CloseAllAsync`/`Dispose` 对第三方 provider `DetachAll` 无异常隔离。建议逐 provider try/catch。
- **R3-UIA-005 · P2 · F/A1**：`UIManagerImpl.cs:1990-1998,927,948,2051-2055` —— `FocusPanel` 无条件补发 OnFocus/OnResume；`PopToAsync` 目标已栈顶、`PopToRootAsync` 栈深≤1、重开已聚焦面板都会发假 OnResume。建议按状态守卫。
- **R3-UIA-006 · P2 · B2/B3**：`UIManagerImpl.cs:656-662,675-682,704-710` —— 回滚路径 `pending` 清空在 `RollbackPanel` 之后；回滚自身抛异常会二次回滚并覆盖原异常。建议先置空再回滚。
- **R3-UIA-007 · P2 · B2/D4**：`UIManagerImpl.cs:502-517` —— Dispose 的面板循环无「仍在册才回收」认领守卫；在途打开续体可对同一面板二次 `RecyclePanel`。`DestroyInstance` 的 `IsPooled` 守卫避免了双入池，但 `OnPoolRecycle`/对话框结果兜底会二次执行。
- **R3-UIA-008 · P2 · D3**：`AssetPanelFactory.cs:19-49` —— 默认面板工厂全部测试零直接构造（R2 UI-C-010 补测试半未闭合）。
- **R3-UIA-009 · P2 · A1**：`UIManager.cs:433-443` —— 门面探测豁免源码枚举漏 `IsLayerVisible`/`IsLayerInteractive`。
- **R3-UIA-010 · P2 · A1**：`Runtime/UI/README.md:1333-1335` —— 称 `Initialize`/`SetInstance` 都会重接全部分档驱动器；`SetInstance` 实际不成立。建议改文档。

### UI-B+C（View/Data/HUD/Tip/排序/弹窗/遮罩/控制器）

- **R3-UIB-001 · P2 · A1**：`Controller/UIDefaultController.cs:13-14` —— 注释仍写「每次拦截都会打日志/带字符串插值」，与「默认静默 + 模板化」相反。
- **R3-UIB-002 · P2 · B2/B3 [需实测]**：`UIHudManager.cs:207-245,271-277` —— HUD 倒序索引循环；在用户 `OnUpdate` 内 `HideHud` 其他 HUD 或 `CloseAllAsync` 会错位/越界。建议派发前重排。
- **R3-UIB-003 · P2 · B3/B5/F [回归]**：`UIHudManager.cs:225-227`、`View/UIHudItem.cs:119-125` —— HUD 抛异常停更后，目标销毁不再触发 `OnTargetLost`，HUD 常驻屏幕。建议目标丢失判定先于 `UpdateFaulted` 跳过。
- **R3-UIB-004 · P2 · A1/B2**：`View/UIViewBase.cs:205-233`、`View/UIPanelBase.cs:196-212` —— 回池钩子可重写被 XML 引导，但「必须调用 base」未写入使用方文档；源码注释与外层 `finally` 复位自相矛盾。
- **R3-UIB-005 · P2 · B2/B3 [需实测]**：`Data/UIBinder.cs:302-306,372-376` —— 双向绑定释放顺序在控件已销毁时泄漏属性订阅，后续写入抛进写入方。建议先解属性订阅再摘控件监听。
- **R3-UIB-006 · P2 · B3**：`Data/UIPanelBinding.cs:63-78`、`Data/ViewModelBase.cs:44-53` —— `OnUnbound` 与 `Dispose` 同 try；ViewModel 内部两层无逐项隔离。
- **R3-UIB-007 · P2 · B3 [需实测]**：`Tip/UITipManager.cs:122-139` —— Tip 帧循环无逐项隔离，一个 Tip 抛异常中断本轮。
- **R3-UIB-008 · P2 · A1/D4**：`Runtime/UI/README.md:463` —— 教用户把句柄存进私有 `UIPanelBinding._bindings`，照抄不可用；正确出口是 `Track`/`RegisterBinding`。
- **R3-UIB-009 · P2 · A1/D4**：`Data/UIPanelBinding.cs:156-173` —— `img_` 类型不匹配无编辑器告警，与 README「未找到会告警」相反。
- **R3-UIB-010 · P2 · A1/A2**：`IUIHudProvider.cs:16`、`Tip/IUITipProvider.cs:14` —— 自定义 provider 经 `SetTipProvider`/`SetHudProvider` 注入后收不到 `SetUIRoot`（R3-ADJ-12）。
- **R3-UIB-011 · P2 · C1**：`View/UIHudItem.cs:127-132` —— 无主相机时 HUD 每帧重查 `Camera.main`。建议降频/一次性告警停试。
- **R3-UIB-012 · P2 · D3**：`Data/UIBinder.cs:24-96`、`Tip/UITipManager.cs` —— 四个公开绑定方法零直接覆盖；真实 `UITipManagerImpl.Update`/`UITipItem.Tick` 仍零覆盖。
- **R3-UIB-013 · P2 · E/D4**：`Data/UIPanelBinding.cs:243-278` —— 面板内同名约定节点只绑到其中一个，另一个永久未绑且无告警。

### Config

- **R3-CONF-003 · P2 · D4/A1**：`ConfigManagerImpl.cs:106-112,202-207,283-288,394-399,425-430` —— 在途合并只按 Type+Kind 匹配，忽略第二调用方的 assetPath/format/loader，无告警。
- **R3-CONF-004 · P2 · A1/D4**：`README.md:614-618` —— 「Register 与 Preload 互相覆盖、后到者赢」与实现相反（Register→Preload 静默不加载）。
- **R3-CONF-005 · P2 · C2/D4**：`CsvLoader.cs:211-232` —— CSV 数值解析走当前区域文化；de-DE 等小数逗号区域解析错误。建议 `InvariantCulture`。
- **R3-CONF-006 · P2 · A1/D4**：`ConfigManagerImpl.cs:583,632` —— 未加载异常提示缺必填 `assetPath`，照抄 CS7036。
- **R3-CONF-007 · P2 · B3/D4**：`ConfigManagerImpl.cs:249-250` vs `:327-333` —— 自定义 Table loader 返回 null 只有 NRE 文本，Global 有专名消息。
- **R3-CONF-008 · P2 · D2/A1**：`ConfigManager.cs:210-215`、`README.md:154-156` —— `PreloadGlobalAsync<T>(path, default)` 同形二义，XML 只给 Table 写了提示。
- **R3-CONF-009 · P2 · A1 [回归]**：`CsvLoader.cs:13-15` —— 新写的「CSV 必须包含主键列」对计算/显式实现主键不成立。
- **R3-CONF-010 · P2 · E/A1**：`ConfigTypeHelper.cs:32-62` —— 基类回退循环是死代码；类型实现多个 `IConfigRow<T>` 时静默取第一个。
- **R3-CONF-011 · P2 · A1/D4**：`ConfigIndexView.cs:35-44` —— `TryGet` 失败时 `values = null`，与 `Get` 返回空数组不对称，XML 未说明。

### Localization

- **R3-LOC-001 · P2 · A1/F**：`LocalizationLanguageInfo.cs:32-36` —— `ToString` 的 XML 样例 `zh-Hans(当前, 128 条)` 与实现 `zh-Hans(当前), 128 条` 不符。
- **R3-LOC-002 · P2 · B1/B6**：`LocalizationBootstrapStage.cs:116-118,104-112` —— 同一 stage 二次 `ExecuteAsync` 把 `_initializedByThisStage` 覆写 false，`Shutdown` 漏销毁。
- **R3-LOC-003 · P2 · A1/B3/D4 [需实测]**：`LocalizationManagerImpl.cs:420-423,521-526` —— 格式说明内出现 `{`（如 `{0:0{0}`）仍抛 `FormatException`，违反「非格式项按字面」。
- **R3-LOC-004 · P2 · D4/A1**：`LocalizationManager.cs:222-227`、`LanguageAssetLoader.cs:72` —— `LanguageAssetPath` 缺 `{0}` 时静默忽略语言；坏模板抛无前缀 `FormatException`。
- **R3-LOC-005 · P2 · D4**：`LanguageAssetLoader.cs:237-245,257-271` —— JSON 原始控制字符被静默接受，构成文档未列明的第三种宽容。
- **R3-LOC-006 · P2 · D4/E**：`LanguageAssetLoader.cs:166-182`、`LocalizationManagerImpl.cs:317-318` —— 空键 `""` 入库后不可达，且零告警。
- **R3-LOC-007 · P2 · D3**：`Tests/Editor/Localization/*` —— 五处零直接断言（ToString 三态、MRU 顺序、stage 二次执行、同实例 SetInstance、门面缓存未命中/取消）。
- **R3-LOC-008 · P2 · A1/D3**：`Tests/Editor/Localization/LocalizationSubscribeTests.cs:23-26` —— 注释仍称「仓内无 PlayMode 用例 / 已记入 README 审计记录」，与事实不符。

### Input

- **R3-INP-002 · P2 · F/D4 [需实测]**：`Default/InputSystemProvider.cs:260-277` —— 同一帧「按下并释放」时释放分支被 `else if` 吞掉，长按时长虚增。
- **R3-INP-003 · P2 · A2/C2/D4**：`Default/InputSystemProvider.cs:202-237` —— 复合绑定（WASD/2DVector/1DAxis/D-pad）的 raw 读恒为默认值，与平滑读不一致；README 未写限制。
- **R3-INP-004 · P2 · B1/B3/D4**：`InputManager.cs:157-184` —— 第三方 `IInputProvider.Dispose` 抛异常时 `Destroy` 留下 `_provider`/`_initialized`/订阅半状态。
- **R3-INP-005 · P2 · C1**：`Default/InputSystemProvider.cs:24,323` —— `_currentMapName` 只写不读的死字段。
- **R3-INP-006 · P2 · B5/A2 [跨模块 · Message]**：`Input/InputManager.cs:310-322,645-…-906` —— MSG-004 的合并句柄在 `BindToContext` 被丢弃；`Observe*` 仍返回原 `sub`，手动 Dispose 时登记与 bound 钉在 CTS 上直到所有者销毁。建议 `BindToContext` 返回 bound，11 个 `Observe*` 转发。

### Update

- **R3-UPD-001 · P2 · A1/D4 [回归]**：`Runtime/Update/README.md:323-326`、`UpdateManager.cs:362-366` —— Clear 恢复指引把 Audio 写成「不会自愈」、Timer 写成「下一次 After 即可」，与实现相反。
- **R3-UPD-002 · P2 · A1**：`UpdateScheduler.cs:1245-1264` —— `FlushPending` 注释仍写「回调里可能调用 Clear 清空本缓冲」，与 UPD-002 修复相反。
- **R3-UPD-003 · P2 · A1**：`Documentation/Modules/Update.md:39` —— 仍写「四个消费方」固定计数。
- **R3-UPD-004 · P2 · D4/A2**：`UpdateManager.cs:720-743`、README:298-312 —— 对已 Disable/缓冲中的节点调 `ProcessImmediate` 静默无操作，文档未写。

### Settings

- **R3-SET-001 · P2 · B4/C2/A1 [回归]**：`EncryptedSettingsStore.cs:118-131`、`SettingsManagerImpl.cs:483-516` —— 装饰器实现异步能力后管理器不再做快照；内层同步 store 时把活对象交池线程 `JsonUtility.ToJson`，SET-013 的竞态在推荐用法上复活。
- **R3-SET-002 · P2 · A2/E**：`SettingsManager.cs:111-131` —— 默认路径 `Initialize<T>()` 在已用显式路径初始化时仍返回旧实例并写 `DefaultPathClaim`。
- **R3-SET-003 · P2 · B4/A1**：`SettingsManager.cs:274-277,322-412`、`SettingsManagerImpl.cs:707-715` —— README 说主线程约束射程是「句柄与管理器」，管理器写入口却无断言。
- **R3-SET-004 · P2 · B6/B1**：`SettingsPauseNotifier.cs:50-62,73-83` —— 引用计数是会话级静态量、无代际；会话复位后旧 Release 可串到新会话。
- **R3-SET-005 · P2 · D4**：`JsonFileStore.cs:110-116` vs `123-135` —— `Directory.CreateDirectory` 与 `JsonUtility.ToJson` 在 try 之外，失败只告警。
- **R3-SET-006 · P2 · D4/A1**：`EncryptedSettingsStore.cs:160-165` vs `178-187` —— 内层文件存在但不是密文外壳时静默回默认，不告警。

### Save

- **R3-SAVE-001 · P2 · B5/F**：`SaveManagerImpl.cs` 的删除/复制/移动/玩家枚举只认载荷，不认备份；与查询面（已认备份）不一致。
- **R3-SAVE-002 · P2 · A1/D4**：主文件缺失 + 备份损坏时报 `Missing`，而不是 `Corrupt`。
- **R3-SAVE-003 · P2 · B3/D4**：`GetSlotMetaAsync` 无异常隔离，解密/IO 异常直抛。
- **R3-SAVE-004 · P2 · B4/A1**：`RecoverAsync` 文档低估「切回主线程」次数（每次读侧车都切）。
- **R3-SAVE-005 · P2 · B5**：恢复扫描「载荷存在」分支仍不清 `.meta.tmp`。
- **R3-SAVE-006 · P2 · A1/D4**：`RecoverAsync`「同步抛出」的表述不成立。

### Data

- **R3-DATA-001 · P2 · B1/B5**：`DataManagerImpl.cs:70-79` —— `RegisterBlock` 同实例重复注册会先 `OnClear` 清空自身数据再原样注册。
- **R3-DATA-002 · P2 · E/F**：`DataManagerImpl.cs:202-221,425-465` —— 单块快照不记录 format；换默认序列化格式后旧单块快照读不回。
- **R3-DATA-003 · P2 · B3**：`DataManagerImpl.cs:70-98` vs `236-249,462-478` —— `OnClear` 异常在 `RegisterBlock`/`RemoveBlock` 外泄并中止，`ClearAll`/`ApplySnapshot` 却逐块隔离。
- **R3-DATA-004 · P2 · A1/D4**：`IDataManager.cs:69-75`、README:283 —— `ApplySnapshot` 失败计数漏 `OnClear` 异常与 null 条目。
- **R3-DATA-005 · P2 · A1**：`IDataManager.cs:27-30`、`IDataBlock.cs:32-36` —— `RegisterBlock<T>` 静态类型键与 `BlockName` 注册期稳定性两个隐式前提未文档化。

### Serialize

- **R3-SER-001 · P2 · A1**：`NewtonsoftSerializer.cs:9`、README:53,132 —— 「Newtonsoft 支持多态」与 `TypeNameHandling.None` 及 Data README 相反。
- **R3-SER-002 · P2 · A1**：`Runtime/Serialize/README.md:74-75,83-86` —— SER-002 残留的「须/必须在模块初始化之前」两句。
- **R3-SER-003 · P2 · D4**：`Serializer.cs:143-156` —— `Get(null)` 抛 ANE，XML 只承诺 KNFE。
- **R3-SER-004 · P2 · D3**：`Tests/Runtime/Data/NewtonsoftSerializerTests.cs:33-41` —— `AutoInit_DefaultAvailable` 因 SetUp 已 Initialize 而自证，删 AutoInit 仍绿。
- **R3-SER-005 · P2 · A2**：`ISerializer.cs:20-26` —— `Serialize` 的 `type` 形参两个内置实现均未使用，跨实现语义分叉。
- **R3-SER-006 · P2 · A1/F**：`Runtime/Serialize/README.md:62`、`Serializer.cs:178-194` —— README 把 `Default` 写成「未初始化时抛」，实际是「json 是否有注册项」，与 `IsInitialized` 双面不一致。

### Audio

- **R3-AUD-002 · P2 · B3/B5**：`AudioManagerImpl.cs` 音量刷新使用假 null 判断，已销毁声源不会被正确跳过。
- **R3-AUD-003 · P2 · B3/B5**：`AudioVoicePool.cs` 死槽位永不可恢复；`Sweep` 的 `break` 使 ticker 过早注销。
- **R3-AUD-004 · P2 · E**：`AudioHandle` 缺 `IEquatable`/相等运算符，与 `TimerHandle` 本轮补齐的形状不一致。
- **R3-AUD-005 · P2 · B1**：直接 `Dispose` 门面返回的管理器后门面卡死。
- **R3-AUD-006 · P2 · A1**：README/XML 仍保留 R2 AUD-008/010 的「钳制点」句与文件树 internal 标注。
- **R3-AUD-007 · P2 · A1/D4**：3D 衰减/空间参数不可配置且未写已知限制。

### Timer

- **R3-TMR-002 · P2 · D3**：`UpdateManager.Clear()` 后「CancelAll + 重建」恢复路径零覆盖。
- **R3-TMR-003 · P2 · A1**：`Documentation/Modules/Timer.md:60` 仍写「同轴创建不受影响」，与跨表缺口（R3-TMR-001）矛盾。

### Bootstrap

- **R3-BST-001 · P2 · B3/B1**：`Bootstrap.cs:383`（同型 `:249`）——`Shutdown` 第一行 `_runCts?.Cancel()` 无异常隔离；阶段注册的取消回调抛异常时 `AggregateException` 在任何 `stage.Shutdown()` 之前逃出，整轮清理被跳过，`ResetForNewSession` 同形。建议 try/catch 记 Error 后继续。
- **R3-BST-002 · P2 · C2**：`GameLauncher.cs:27`、`README.md:88` —— `DontDestroyOnLoad` 仅对根 GameObject 生效；启动器挂子对象时切场景即销毁并触发 `Shutdown`，拆掉全部全局服务。建议文档写明「必须挂根对象」。
- **R3-BST-003 · P2 · B3/D4**：`Bootstrap.cs:399-409,387-393` —— `Shutdown` 在活引用 `StageList` 上索引遍历；阶段 `Shutdown` 里 `Clear/Unregister` 会改变表长，导致越界或跳过其余阶段。建议入口先快照。

### Lock

- **R3-LOCK-001 · P2 · B5/B2**：`LockManager.cs:581-592` —— `AddLock` 在派发「首次锁定」事件之后才注册销毁绑定；订阅者在回调里清空该主体锁后，主体已空闲却残留 `_destroyBindings`，静态表强引用到 `Dispose` 或下一次「有锁→空闲」转变。建议先绑定后派发或派发后重查。
- **R3-LOCK-002 · P2 · A1**：`LockManager.cs:930`、`LockStateSnapshot.cs:30`、`README.md:230` —— R2 审计编号 `LOCK-003/004` 写进随包 XML/README，第三方无法解析。建议移回维护向记录、包内改自足表述。

### Event

- **R3-EVT-001 · P2 · A1/D3**：`Documentation/Modules/Event.md:53` —— 「不为 Event 补 PlayMode 用例」的理由仍写「无跨会话静态态」，与模块内三处静态池/计数矛盾（`EventStreamImpl.cs:300`、`DispatchListPool.cs:20`、`SubscriptionTracker.cs:40,42`）。建议订正理由，结论不变。
- **R3-EVT-002 · P2 · B3/D4**：`Event/Internal/EventStreamImpl.cs:231-232` —— `_onEmpty?.Invoke()` 无异常隔离：派发路径被误归因为 handler 异常，非派发路径冒泡给 `handle.Dispose()` 调用方。建议 try/catch 记专用日志。

### Reactive

- **R3-RCT-001 · P2 · A1/F**：`Reactive/IReactivePropertyWriter.cs:20-24`、`ReactiveProperty.cs:154-168`、`Settings/SettingRef.cs:151-166` —— 接口文档同时承诺「actual 是写入后实际值」与「恒等于 value」；两个实现各满足一条，重入写入时分叉。建议改接口口径或统一实现。
- **R3-RCT-002 · P2 · D3/A1**：`Runtime/Reactive/README.md:242`、`Documentation/Modules/Reactive.md` —— RCT-009 的「不加主线程断言」裁定未进维护向台账，下一轮会重复论证。建议补 `暂不做` 条目。

### Pipeline

- **R3-PIPE-001 · P2 · A2/D4**：`Pipeline/ParallelStage.cs:39`、`SequenceStage.cs:42` —— 容器构造只校验列表 null/空，不校验元素 null；`new ParallelStage(new IPipelineStage[]{ null })` 抛裸 NRE，与 `AddStage(null)` 的 ANE 防御不一致。建议补元素校验。
- **R3-PIPE-002 · P2 · D4/C2/A1**：`Pipeline/Pipeline.cs:107-108,251-256`、`README.md:206` —— 超时只拦 0/负值/NaN；`float.PositiveInfinity` 或 >~9.2e11 秒时 `TimeSpan.FromSeconds` 抛 `OverflowException`，被外层 catch 成 Failed 且不取消在途阶段。建议把非有限值按「不启用」或入口抛 AOORE。
- **R3-PIPE-003 · P2 · B1/B3**：`ParallelStage.cs:116-124`、`SequenceStage.cs:95-99,114-122` —— 容器的取消路径跳过 `_aggregator.Settle()`，成功/失败路径都 Settle；取消后子阶段迟到写入仍可能聚合/广播。建议移到 `finally`。
- **R3-PIPE-004 · P2 · B5**：`ParallelStage.cs:61,136-137`、`SequenceStage.cs:64,134-135` —— `_stageCtx` 沉降后保持引用并持有 `Owner = PipelineImpl`；长寿命容器跨管线复用时钉住上一轮管线与订阅者闭包。建议沉降后置 null 或文档写明。

### Message

- **R3-MSG-001 · P2 · A1**：`Documentation/Modules/Message.md:36` —— `IDestroyCancellationToken` XML 示例已在 `4e8645b` 修复，台账仍标 `未做`。建议改为 `已做` 并带提交号。
- **R3-MSG-002 · P2 · A1/D3**：`Documentation/Modules/Message.md`、`Runtime/Message/README.md:366` —— README 写「记 `待裁`」，但台账没有 MSG-003 条目，指针悬空。建议补 `待裁` 条目（是否向 Event 要「只清缓存保订阅」原语）。
- **R3-MSG-004 · P2 · B1/D4**：`Message/MessageManager.cs:705-709` —— `TryBindToDestroy` 把 `!token.CanBeCanceled` 与 `IsCancellationRequested` 并成一枝；`IDestroyCancellationToken` 返回 `CancellationToken.None` 时订阅被立即 Dispose 并静默返回，处理器永远收不到消息。建议拆两枝：None 视为「无法绑定」并返回原句柄。
- **R3-MSG-005 · P2 · A1**：`Message/MessageBroker.cs:217-220`、`MessageBusStats.cs:40,43` —— 订阅过滤异常 `<remarks>` 仍说由事件流兜底（实际就地记 `[Message]`）；统计字段仍说「自上次 Clear/SetInstance 以来」（`SetInstance` 已删）。建议同步 XML。

### Log

- **R3-LOG-001 · P2 · B3/D4/F**：`Log/LogManagerImpl.cs:417-431`、`Log/Internal/LogFileSink.cs:120-139,227-231` —— LOG-003 只修了 `Write` 路径；文件 sink 在 `Flush` 内 IO 失败 → `Die()`，`Flush` 不复查 `IsDead`，死 sink 留在 `_sinks`、不计 `DroppedSinkCount`。建议 `Flush` 后同形检查并摘除。
- **R3-LOG-002 · P2 · A1**：`Log/README.md:95` —— 同一条「全量捕获」bullet 先说分类覆盖适用外部日志，后说分类覆盖无法让低于全局档的外部日志通过，两句相反。建议改为「先全局档早退，再分类覆盖」。
- **R3-LOG-003 · P2 · A1**：`Documentation/Modules/Log.md:15` —— 文件树写死「33 个框架内置分类」，磁盘为 34（CameraManager 加入后未跟）。建议删计数。
- **R3-LOG-004 · P2 · A1/C2**：`Log/Internal/LogFileSink.cs:9-12`、`README.md:96` —— 类 XML 仍写「每会话一文件」，LOG-001 修复后同会话 `Configure` 重建会新建文件。建议改为「每个文件输出端一个文件；正常流程每会话一个，重建会新建」。

### Pool

- **R3-POOL-001 · P2 · B2/B3/D4/F**：`Pool/Pool.cs:156-175,194-222` —— 用户回调抛出时账本已回滚，但实例被静默丢弃：不再入栈、也不走 `onDestroy`/`IPoolDiscardable.OnDiscard`，调用方与池都不再持有它；资源泄漏且无日志。建议不可归还分支调用丢弃回调，或文档明写「回调异常 = 实例作废且不触发丢弃回调」。

### File

- **R3-FILE-001 · P2 · C2/A1**：`File/FileManager.cs:110-132` —— FILE-009 补了 Windows/Linux Server，仍漏 `OSXServer`；该平台落 default 抛 `PlatformNotSupportedException`，本可用 `DesktopFileProvider`。建议补 `case RuntimePlatform.OSXServer`。
- **R3-FILE-002 · P2 · A2/E/D4**：`File/MobileFileProvider.cs:225-233,31-64,87-110` —— 移动 Streaming 域既不归一化反斜杠、也不走 `TryNormalizeRelativePath` 沙箱；`../` 可原样拼进 URL，README 的输入契约在该路径不成立。建议入口归一并对非法路径抛 ANE。
- **R3-FILE-003 · P2 · A1/D4**：`File/IFileProvider.cs:13-17`、`README.md:90-96`、`DesktopFileProvider.cs:96-119` —— 公开读失败契约把权限列为 `IOException`，桌面实际抛 `UnauthorizedAccessException`；按契约只 catch IOException 的调用方接不住。建议包装或改契约。
- **R3-FILE-004 · P2 · B1/D4**：`File/FileManager.cs:143-151,193-207` —— `Destroy` 后 `SetCryptoProvider`/`GetCryptoProvider` 不查闩锁，销毁期间设置会被下一次 `Initialize` 采纳。建议加 `ThrowIfDestroyed`。
- **R3-FILE-005 · P2 · A1**：`File/README.md:508` —— 「11 处」固定计数与代码不符（实际 12 个 `RunOnThreadPool` 调用点）。建议删计数。

### Diagnostics

- **R3-DIAG-001 · P2 · B3/A1/D4**：`Diagnostics/DiagnosticsManager.cs:65,79-88` —— `Register` 读 `panel.Title` 零异常隔离；坏 getter 让登记整体抛出并破坏幂等承诺，与渲染侧 `SafeTitle` 不对称。建议用同款 try/catch 包住两次 Title 读取。
- **R3-DIAG-002 · P2 · A2/D4**：`Diagnostics/DiagnosticsManager.cs:163-173`、`Editor/Diagnostics/FrameworkPanels.cs:60-61` —— 公开 `Clear()` 抹掉框架页签，但恢复入口只在包内 Editor 程序集的 internal 方法上；包外宿主按「彻底重启用」调用后无法恢复。建议加 preserve 参数/菜单或改文档。
- **R3-DIAG-003 · P2 · A1**：`Diagnostics/IDiagnosticPanel.cs:11-12`、`README.md:86,66` —— 「只显示一条错误提示」实际写两条（提示 + 堆栈）；README 又写死「十个文件」计数。建议改契约句并删计数。

### Camera

- **R3-CAM-001 · P2 · C2/D5/A2 [跨包接缝]**：`Camera/ICameraManager.cs:59,97`、`XFramework.Cinemachine/Runtime/CinemachineCameraManager.cs:426-446` —— 核心接口承诺 NaN/±∞ 抛异常，扩展适配器无有限性校验，直接把 NaN 交给 `CinemachineBlendDefinition`；同一接口两实现行为相反。建议扩展补同形校验。
- **R3-CAM-002 · P2 · B1/B3/D4**：`Camera/UnityCameraManager.cs:463-469,507-512,589-599` —— 被外部销毁的相机在查询面已无效，但变更面仍当有效槽：`Activate`/`TransitionTo` 静默成功并把其它活相机全部关闭，无告警。建议变更面追加 `IsUsable` 校验。
- **R3-CAM-003 · P2 · A1**：`Camera/ICameraManager.cs:28-31`、`README.md:97-99` —— 「Cinemachine 适配器返回 Brain 的输出相机（恒非空）」是过度承诺；Brain 对象无 Camera 时 `OutputCamera` 为 null。建议改为「可能为 null」。

### Scene

- **R3-SCN-001 · P2 · B5/D3/D5 [回归]**：`Asset/YooAssetManagerImpl.cs:730-747`、`Asset/YooAssetSceneLoadSource.cs:78`、`Tests/Runtime/Asset/FakeSceneLoadSource.cs:67` —— SCN-003 的 `sceneUnloaded` 清表在真适配器下无效：YooAsset 先 `Release`，`SceneObject` 已 default，比较恒 false；假件 `Release()` 不置空，测试是结构性假绿。建议登记项存加载时捕获的 `Scene` 快照，并让假件模拟真语义。
- **R3-SCN-002 · P2 · B3/D4**：`Scene/SceneManager.cs:73-78` —— 回收段只处理了取消，非取消异常仍让 `return handle` 不可达；与 SCN-001 同形的「Additive 场景留下且无卸载凭据」。建议 try/catch 后仍返回句柄。
- **R3-SCN-003 · P2 · A1/D1**：`Scene/SceneManager.cs:67`、`Log/LogCategories.cs`、`Scene/README.md:288-292` —— `LogCategory.Get("SceneManager")` 未进内置分类表，README 仍称 XLog 是间接依赖。建议加 `LogCategories.SceneManager` 并改依赖说明。

### Cinemachine（扩展包）

- **R3-CIN-002 · P2 · C2/D5/A2 [跨包接缝]**：`CinemachineCameraManager.cs:426-446`、`Assets/XFramework.Cinemachine/Runtime/README.md:62` —— 过渡时长未校验有限性：NaN 被上游过滤成 cut（不是「永不结束」），`+∞` 则永久混合；核心 `ICameraManager` 已承诺 NaN/±∞ 抛异常，扩展未实现。建议补同形有限性校验。
- **R3-CIN-003 · P2 · B3/D4**：`CinemachineCameraManager.cs` 的槽位/反查 —— vcam 被外部销毁后 `Deactivate` 抛 `MissingReferenceException`，`Activate` 静默 no-op；`_reverse` 残留陈旧键。建议变更面统一按 `IsUsable` 告警 no-op。
- **R3-CIN-004 · P2 · A1**：扩展 README/`package.json`/`Documentation/Cinemachine.md` —— 「兼容核心 0.3.0」失实：tag 0.3.0 无 Camera 模块与扩展包；依赖 pin 的说明也需与 tag 事实对齐。
- **R3-CIN-005 · P2 · D3/A1**：`CinemachineCameraManagerTests` / `Documentation/Cinemachine.md:54` —— 声称「测试锁定」的强制 cut、ABA 等场景没有对应用例；`CinemachineDependencyTests` 不覆盖 18 节骨架/API 基线。建议补用例或改声称。
- **R3-CIN-006 · P2 · A1**：扩展 README 的「Brain 无公开完成回调」失实（`IsBlending` / `ActiveBlend` / `CameraActivated` 等可观测）；建议改为「没有 await 形态的完成回调」。
- **R3-CIN-007 · P2 · A1/A2/F**：`ICameraManager.cs:25-32`、`Runtime/README.md` —— `OutputCamera`「与目标一致」的义务不可兑现（Brain 的输出相机可空/可为过渡源）；建议改为「可能为 null、不保证等于目标」。
- **R3-CIN-008 · P2 · D1/D3 [门禁]**：扩展包不在核心 `check-docs`（编译/源文件参与/`.meta`/台账）、`PublicApiBaseline`、`ReadmeTableTests`、`ReadmeSectionTests`、`PackageBoundaryTests`、`NamingConventionTests`、`ModuleBoundaryTests` 的射程；扩展自有 `CinemachineDependencyTests` 只替代五项。建议补独立门禁或登记射程缺口。

## 7. 跨模块主题

### 7.1 R2 修复的文档-代码同步

- **涉及**：R3-CONF-001（示例改字段后不实现接口）、MSG-002 台账仍标 `未做`、UI-C-012 部分修复、Data/Serialize 的 README 残留、Update README 的 Clear 恢复指引（R3-UPD-001）、Save 的线程文案（R3-ADJ-08）。
- **共同根因**：R2 修复改代码时，示例、XML、README、台账四类文本没有一次同步；文档示例不参与编译，错误的「修复」可以存活一整轮。
- **统一动作**：修复提交必须同时检查「示例 / XML / README / 台账 / CHANGELOG」五处；文档示例的编译探针进实施批次 0。
- **不做什么**：不引入文档内容运行时门禁（`check-docs` 仍只守结构）；文档示例可编译性守卫维持 `未做`。

### 7.2 公开 API 与序列化兼容

- **涉及**：R3-AST-001（枚举整数值重排）、R2 op_* 基线补录（`34edb5c`）、Update `ProcessImmediate` 形参放宽、TimerHandle 相等语义、UI `PreconditionChain` 取消重载。
- **共同根因**：`PublicApiBaseline` 只校验公开面的**形状**（名称/签名/可见性），不校验**枚举整数值**与「已序列化字段的默认值」；CHANGELOG 迁移表只覆盖编译器抓得到的命名/签名变更。
- **统一动作**：公开枚举新增/重排时，基线之外再核对整数值与序列化字段；迁移表补「反序列化后需重新选择」一类说明。是否把枚举整数值纳入基线，列为框架级待裁。
- **不做什么**：不把行为变更塞进 `PublicApiBaseline`（它只守形状）；不引入 Roslyn 式全量兼容守卫。

### 7.3 依赖声明与安装真相

- **涉及**：R3-INP-001（包 README/安装器缺 Input System）、Cinemachine 扩展包门禁覆盖（W5 待填）、`DependencyDeclarationTests` 只校验「已列出的依赖」。
- **共同根因**：依赖的「唯一真相」表是**人工维护的清单**，测试只能证明「清单里的每项都一致」，不能证明「程序集引用的每项都在清单里」；扩展包又不在核心 `check-docs`/API 基线射程。
- **统一动作**：从 asmdef 引用反查依赖清单；把扩展包纳入独立的文档/编译门禁或在核心守卫里登记射程缺口。
- **不做什么**：不因为扩展包而把 Cinemachine 引入核心包。

### 7.4 会话复位族的第二层

- **涉及**：R3-AST-002（`SceneManager.sceneUnloaded` 实例订阅未拆）、R3-SET-004（PauseNotifier 会话级计数无代际）、R3-UIA-007（Dispose 在途打开）、R3-INP-004（provider Dispose 半状态）、Audio/Input 的静态/设备回调。
- **共同根因**：R2 的会话复位解决了「静态字段 / 静态字典 / lowMemory 订阅」第一层，但**实例级引擎事件订阅**与**跨对象引用计数**仍会跨会话存活；「只丢引用不 Dispose」的框架级裁定留下了实例订阅的尾巴。
- **统一动作**：每个持有引擎事件订阅的实例提供幂等 `Detach*`；会话复位在丢引用前调用；族清单登记。
- **不做什么**：不引入反射式自动复位；不替使用方 Dispose（维持 2026-10-18 裁定）。

### 7.5 测试真实性与新面覆盖

- **涉及**：R3-CONF-001（示例不编译）、R3-CONF-002（SO Table 端到端零测试）、R3-AUD-001（AudioMixer 无路由）、R3-TMR-001（跨表定时器无回归）、Input raw/复合读、Asset EditorSimulate 真包、Save/File 可抛 IO 缝。
- **共同根因**：新公开面先有文档/实现、后补测试，且测试多用替身绕开真实缝；文档示例不参与编译，恒真断言（`AllocationRuler` 历史）掩盖失败。
- **统一动作**：每个新公开面至少一条「真实缝或可复现探针」；「改前必红」写进提交证据；对无法自动化的项明确标 `需实测` 并进附录 B。
- **不做什么**：不把绿测试当修复证明；不为绿而加恒真断言。

### 7.6 异常隔离与批处理

- **涉及**：R3-UIB-002/006/007（HUD/Tip/ViewModel 派发）、R3-AST-005/006（进度回调/下载器事件）、R3-CONF-007（loader null）、R3-INP-004（provider Dispose）、Save/File provider 调用。
- **共同根因**：R2 在面板/池/日志等主路径补了逐项隔离，但**用户回调在循环里调用**的边角（HUD 倒序、Tip 帧循环、下载器事件、ViewModel 释放）仍会「一个抛、整批断」。
- **统一动作**：任何「遍历用户回调」的循环按「逐项 try/catch + 记 Error + 继续」检查；失败面写进行为契约。
- **不做什么**：不把异常吞掉不记；不改变「失败仍完成状态变更」的既有语义。

### 7.7 平台/运行模式矩阵

- **涉及**：Asset `EditorSimulate`（编辑器专用 + Player 抛）、FILE-009 Server 平台、CONF-017 IL2CPP High stripping、FILE-002 移动端 Streaming、CONF-014 低内存、AUD-001 的 `isPlaying` 平台前提、AudioListener.pause。
- **共同根因**：R3 的新面横跨「编辑器 / Player / Server / 移动端 / IL2CPP」多个运行矩阵，审计只能静态确认分支，真机行为仍需探针。
- **统一动作**：这些项统一进 §11 探针轨；报告标 `需实测`，不因未跑而阻塞。
- **不做什么**：不用编辑器行为推断真机；不为通过审计而跳过平台分支。

### 7.8 台账、计数与文档状态

- **涉及**：370（门禁口径）vs 371（全文件 grep，多算 `Audio.md` 一条非台账状态行）；`Modules/README` 的 23→24 已删除计数；MSG-002 台账状态陈旧；`Modules/Update.md:39` 固定计数。
- **共同根因**：计数口径不统一、状态更新滞后；历史文本中的固定数字会漂移。
- **统一动作**：报告只写「快照统计 + 命令」；台账状态随修复提交更新；不写会随开发增长的量。
- **不做什么**：不改历史快照里的 23/23 记录（它们是带日期的历史事实）。

## 8. 逐模块审计记录

> 每条记录：范围与输入、18 类判据留痕、R2 修复复核（只列偏差）、R2 旧结论复核、开口项、第三方对照、残留与不确定。P0/P1 明细见 §4/§5，P2 明细见 §6。

### Asset（T1 全量）

- **范围与输入**：`Runtime/Asset/` 全部 23 个 `.cs`、README、`Modules/Asset.md`、Runtime/Editor 测试、Scene/Bootstrap/DefaultGameLauncher/YooAsset 接缝；冻结 SHA 只读。
- **18 类留痕**：A1/A2 命中（热更三 API「失败抛」实为静默返回、Host 无 RemoteServices 注入点，R3-AST-004/009）；A3 扫过（cref 人读）；B1 扫过；B2/B5/B6 命中（`sceneUnloaded` 实例订阅不随会话复位，R3-AST-002）；B3 命中（进度回调/下载器事件无逐订阅者隔离，R3-AST-005/006）；B4 扫过；C1 命中（`HandleBaseWait` 接口形参每条路径装箱，R3-AST-007）；C2 命中（枚举重排，R3-AST-001）；D1/D2 扫过（`LoadAsync(location, default)` 不二义，编译探针已关闭旧残留）；D3 命中（`AssetPanelFactory`/真 YooAsset 端到端缺口，R3-AST-008 相关）；D4 命中（卸载失败残影、`PreloadAllAsync(null)` NRE，R3-AST-003/008）；D5 扫过；E/F 扫过。
- **R2 修复复核**：AST-001/002 已修复；AST-004/005/006/007/009 已修复；AST-003/008 **部分修复**（`SceneManager.sceneUnloaded` 订阅仍漏，R3-AST-002 标 `[回归]`）；R2 的「default 重载二义」与「`AssetSceneHandle.Dispose` 覆盖」两项残留本轮关闭。
- **R2 旧结论复核**：Asset 台账的 `已做/不做/暂不做` 除 R3-ADJ-01 外维持；EditorSimulate 的三条裁定（不做 Web/Custom、不静默降级）仍成立，但「包未发布」前提失实（R3-AST-001）。
- **开口项**：预检/下载版本绑定维持 `待裁`（R3-ADJ-01 建议 `暂不做`）。
- **第三方对照**：YooAsset 2.3.18 的 `PreDownloadContentOperation.CreateResourceDownloader` 本可用；框架层丢弃了该能力。其余沿用 R2。
- **残留**：真 YooAsset 端到端（含 EditorSimulate `SimulateBuild`）、lowMemory 回调线程、RemoteServicesAdapter 覆盖均未实测。

### UI-A（T1 全量，门面/导航/生命周期）

- **范围与输入**：`UIManager.cs`、`UIManagerImpl.cs`、`IUIManager.cs`、`UIBootstrapStage.cs`、`UILayers.cs`、`UIStateSnapshot.cs`、`AssetPanelFactory.cs`、`IUIPanelFactory.cs`；README、`Modules/UI.md`、门面/导航/生命周期测试。
- **18 类留痕**：A2 命中（在途打开键=请求类型，R3-UIA-001）；A1 命中（遮罩/捕获层按请求层、Dispose 文档，R3-UIA-002/010）；B2 命中（Dispose 循环无认领守卫、回滚 `pending` 顺序，R3-UIA-007/006）；B3 命中（provider `DetachAll` 无隔离、回滚二次异常，R3-UIA-004/006）；B5 命中（外部销毁面板的遮罩/捕获持有不回收，R3-UIA-003）；D4 命中（同上）；E 命中（在途键）；F 命中（FocusPanel 假 OnResume、遮罩观测面）；A3/B1/B4/B6/C1/C2/D1/D2/D5 扫过。
- **R2 修复复核**：UIA-001/002 已修复；UIA-003/004/005/007、UI-B-002、UI-C-008 确认修复；UIA-006 代码已修但**零回归用例**；UI-C-010 的 Dispose 循环隔离已落，但 `AssetPanelFactory` 仍零直接构造（R3-UIA-008）。
- **R2 旧结论复核**：门面完备性人工核对通过；本切片 8 文件无公开 API 变化（`PreconditionChain` 归 UI-B+C）。
- **开口项**：自定义 Tip/HUD provider `SetUIRoot`（R3-ADJ-12）、`AllPanelsClosedMessage`（R3-ADJ-13）均建议 `未做`。
- **第三方对照**：GameFramework 的打开去重按资源名/实例，本仓在途表按泛型实参，支撑 R3-UIA-001 的修法方向。
- **残留**：全部 P2 只读推演，需 PlayMode 红证；`AssetPanelFactory` 真实路径需 YooAsset 环境。

### UI-B+C（T1 全量，View/Data/HUD/Tip/排序/弹窗/遮罩/控制器）

- **范围与输入**：`Controller/`、`Data/`、`Tip/`、`View/`、`UIHudManager`、`IUIHudProvider`、`UIMaskHandle/Style`、`UIPopupPosition`、`UISorting`；README、`Modules/UI.md`、对应测试。
- **18 类留痕**：A1 命中（UIDefaultController 注释、README 绑定示例、img_ 告警，R3-UIB-001/008/009）；A2 命中（provider `SetUIRoot`，R3-UIB-010）；B2 命中（HUD 重入、绑定释放顺序，R3-UIB-002/005）；B3 命中（HUD/Tip/ViewModel 异常隔离，R3-UIB-002/006/07）；B5 命中（停更 HUD 常驻，R3-UIB-003 `[回归]`）；C1 命中（HUD 每帧 `Camera.main`，R3-UIB-011）；D3 命中（四个 UIBinder 方法零直接覆盖，R3-UIB-012）；D4 命中（R3-UIB-008/009）；E 命中（同名节点覆盖，R3-UIB-013）；F 命中（R3-UIB-003）；A3/B1/B4/B6/C2/D1/D2/D5 扫过或已文档化。
- **R2 修复复核**：UI-B-001 已修但引出 R3-UIB-003 `[回归]`；UI-B-004/006 已修但有 OnPoolRecycle 依赖边界（R3-UIB-004）；UI-B-010 部分修复（真实 Tip 帧循环仍零覆盖，R3-UIB-012）；UI-C-012 部分修复（注释残留，R3-UIB-001）；UI-B-013 已修但 README 未写重复 ShowDialog 语义。
- **R2 旧结论复核**：排序空间/弹窗定位/遮罩/控制器结论维持；`PreconditionChain` 新重载无二义。
- **开口项**：R3-UIB-010 = R3-ADJ-12；R3-UIB-003 回归需在 R3 §10 批次 4 修。
- **第三方对照**：沿用 R2；增量一条——HUD 派发缺面板侧「派发前重排、遍历外修改」同款防重入设计。
- **残留**：HUD 重入、Tip 隔离、绑定释放顺序需 PlayMode/EditMode 红证。

### Config（T1 全量）

- **范围与输入**：`Runtime/Config/` 全部约 19 个 `.cs`、README、`Modules/Config.md`、Runtime/Editor 测试、Serialize/Asset/Luban 接缝。
- **18 类留痕**：A1 命中（R3-CONF-001 P0、README「后到者赢」、异常提示缺 assetPath、CSV 主键措辞，R3-CONF-001/004/006/009）；A2 命中（SO Table 公开基类不可达，R3-CONF-002）；A3 扫过（约 60 个 cref 人读）；B1/B2 扫过；B3 命中（自定义 loader null 文案，R3-CONF-007）；B4 扫过；B5 扫过；B6 扫过（ResetForNewSession 未退订旧 impl 记残留）；C1 扫过；C2 命中（CSV 区域文化，R3-CONF-005）；D1 扫过；D2 命中（`PreloadGlobalAsync(path, default)` 二义文档，R3-CONF-008）；D3 命中（SO/CSV/manifest/CopyLoaded 等零覆盖）；D4 命中（在途合并忽略 path/format、Register→Preload 静默，R3-CONF-003/004）；D5 命中（SO Global 句柄即弃，CONF-014）；E 命中（多接口实现静默取第一个，R3-CONF-010）；F 扫过。
- **R2 修复复核**：CONF-001 **回归**（R3-CONF-001 P0）；CONF-002 已修但残留 SO 基类缺口（R3-CONF-002）；13 条 P2 中 9 已修、3 部分（004/012/015）、1 未修（014）；016 部分、017 未处理（IL2CPP stripping）。
- **R2 旧结论复核**：`ConfigTypeHelper` 失败路径不缓存、Global/Table 双入口等结论维持；新增 `ConfigDictionaryValidator` 与 InFlightLoad 身份/结果逐行核对一致。
- **开口项**：CONF-014 维持 `未做/待实测`；R3-ADJ-01 属 Asset，不在此。
- **第三方对照**：Luban 接入契约仍成立；SO Table 形状缺文档是本轮新增。
- **残留**：真 YooAsset 低内存、IL2CPP stripping、CSV BOM/文化、SO Table 端到端、三处示例编译（已由 R3-CONF-001 探针覆盖）等。

### Localization（T1 全量）

- **范围与输入**：`Runtime/Localization/` 约 7 个 `.cs`、README、`Modules/Localization.md`、Runtime/Editor 测试、Input/Message/Asset 接缝。
- **18 类留痕**：A1 命中（`ToString` 样例、格式说明 `{`、Editor fixture 注释，R3-LOC-001/003/008）；B1 命中（Bootstrap 二次执行漏销毁，R3-LOC-002）；B3 命中（格式说明解析，R3-LOC-003）；B6 命中（R3-LOC-002 相邻面）；D3 命中（5 处零断言，R3-LOC-007）；D4 命中（坏模板、JSON 控制字符、空键，R3-LOC-004/005/006）；A2/A3/B2/B4/B5/C1/C2/D1/D2/D5/E/F 扫过或不适用。
- **R2 修复复核**：LOC-001..013 全部有处置；003/004/005/006/007/009 代码修复 + 用例，001/002/008/011 文档订正，010 夹具补齐，012 台账改 `已做`，013 迁 Message；无 `[回归]`。偏差两处：LOC-004 README 未区分「同实例重复 SetInstance」；Message.md 的 LOC-013 迁移条目仍标 `未做`（R3-MSG-001）。
- **R2 旧结论复核**：§7 的「2 条部分失效 + 1 条文档承诺失效」全部关闭；2 条 `待裁`（R3-ADJ-03/04）与 1 条 `暂不做`（TextAsset 源兼容）维持。
- **开口项**：R3-ADJ-03 建议 `不做`、R3-ADJ-04 建议 `暂不做`。
- **第三方对照**：本仓相当于「永不抛解析异常的 CompositeFormat」；无新增推翻。
- **残留**：R3-LOC-003 的 Unity Mono/IL2CPP 边界、R3-LOC-002 二次执行后 Shutdown 需用例坐实。

### Input（T1 全量）

- **范围与输入**：`Runtime/Input/` 约 14 个 `.cs`、README、`Modules/Input.md`、Runtime 测试、Update/Settings/Event 接缝。
- **18 类留痕**：A1 命中（依赖表/换后端语义、raw 复合未文档，R3-INP-001/003）；A2 命中（R3-INP-003）；B1 命中（provider Dispose 半状态，R3-INP-004）；B2/B3/B4/B5 扫过；B6 扫过（INP-002 已修）；C1 命中（死字段 `_currentMapName`，R3-INP-005）；C2 命中（R3-INP-003）；D1/D2 扫过；D3 命中（振动/热插拔零覆盖）；D4 命中（R3-INP-002/003/004）；D5 不适用；E 扫过；F 命中（同帧按下+释放，R3-INP-002）。
- **R2 修复复核**：INP-001 已修复；12 条 P2 已修/文档化；INP-005 部分修复（模块 README 对齐，包依赖表/安装器仍缺，R3-INP-001 升 P1）。
- **R2 旧结论复核**：Provider 大改的调度/map/缓存/回调隔离结论维持；会话复位 INP-002 已修。
- **开口项**：无新增；R3-INP-001 是安装路径 P1。
- **第三方对照**：本模块即「可替换后端」边界；Rewired 等整体替换仍成立。
- **残留**：振动、设备热插拔、非编辑器 PlayerLoop 子系统名、跨会话手工验收。

### Update（T1 全量）

- **范围与输入**：`Runtime/Update/` 约 5 个 `.cs`、README、`Modules/Update.md`、Runtime 测试、Timer/UI/Audio 接缝。
- **18 类留痕**：A1 命中（Clear 恢复指引与 Audio/Timer 相反、FlushPending 注释、Modules/Update.md 固定计数，R3-UPD-001/002/003）；A2 扫过（UPD-003 已修）；B1/B2/B3/B4 扫过；B5 命中（UPD-007 已文档化）；B6 命中（UPD-008 已修，`_isIterating` 跨会话残留记不确定）；C1/C2 扫过；D1/D2 扫过；D3 命中（UPD-006 已修，UPD-002 无红证）；D4 命中（R3-UPD-001/004）；D5 不适用；E/F 扫过。
- **R2 修复复核**：UPD-003/005/006/007/008 已修/文档化；UPD-001 部分修复且文档面 `[回归]`（R3-UPD-001）；UPD-002 部分（注释失实，R3-UPD-002）；UPD-004 部分（R3-UPD-003）。
- **R2 旧结论复核**：`ProcessImmediate` 形参放宽的源兼容结论成立；分档语义维持。
- **开口项**：无新增待裁。
- **第三方对照**：沿用 R2；无新增推翻。
- **残留**：非编辑器 PlayerLoop 子系统名、跨会话 `_isIterating` 反射探针。

### Settings（T1 全量）

- **范围与输入**：`Runtime/Settings/` 约 18 个 `.cs`、README、`Modules/Settings.md`、Runtime 测试、Serialize/File/Asset 接缝。
- **18 类留痕**：A1/A2 命中（装饰器异步能力、线程契约，R3-SET-001/003）；B4 命中（管理器写入口无断言，R3-SET-003）；B6 命中（PauseNotifier 无代际，R3-SET-004）；C2 命中（R3-SET-001）；D4 命中（目录创建在 try 外、装饰器静默回默认，R3-SET-005/006）；A3/B1/B2/B3/B5/C1/D1/D2/D3/D5/E/F 扫过。
- **R2 修复复核**：SET-001 已修但引出 R3-SET-001 `[回归]`（P2）；SET-010 部分；SET-012 未关闭（wantsToQuit 编辑器探针）；SET-018 部分；其余已修。
- **R2 旧结论复核**：声明式字段、POCO + SettingRef、加密装饰器结论维持；会话边界「不替使用方 Dispose」维持（R3-ADJ-09）。
- **开口项**：R3-ADJ-09 建议 `不做`。
- **第三方对照**：沿用 R2；无新增推翻。
- **残留**：SET-012 手工探针、R3-SET-001 并发竞态、R3-SET-004 旧管理器 Release。

### Save（T1 全量）

- **范围与输入**：`Runtime/Save/` 约 10 个 `.cs`、README、`Modules/Save.md`、Runtime 测试、Serialize/File/Data 接缝。
- **18 类留痕**：A1/A2 命中（RecoverAsync 线程文案、缺失/损坏报 Missing、查询面，R3-SAVE-002/004）；B3 命中（GetSlotMetaAsync 无异常隔离，R3-SAVE-003）；B4 命中（RecoverAsync 线程契约，R3-SAVE-004）；B5 命中（备份槽删除/复制/枚举不一致，R3-SAVE-001）；D4 命中（R3-SAVE-002/005）；F 命中（R3-SAVE-006）；B2 不适用；其余扫过。
- **R2 修复复核**：SAVE-001/005/006/008..015 已修；SAVE-002/003/004/007 部分（R3-SAVE-001/002/003/004/005/006 为残留）；无 R2 P0/P1。
- **R2 旧结论复核**：原子写、备份、删除先清备份、序列化移出主线程等结论维持。
- **开口项**：R3-ADJ-08 建议 `未做`（RecoverAsync 线程双路径，单独计划）。
- **第三方对照**：平台存档 SDK 对照沿用 R2；无新增推翻。
- **残留**：可抛 IO 的 Provider 测试缝、RecoverAsync 主线程探针。

### Data（T1 全量）

- **范围与输入**：`Runtime/Data/` 约 7 个 `.cs`、README、`Modules/Data.md`、Runtime 测试、Save/Serialize/Config 接缝。
- **18 类留痕**：A1 命中（DATA-001/012/013 已修回读）；A2 命中（DATA-009 已文档化）；B3 命中（R3-DATA-003 OnClear 异常外泄）；B5 扫过；D3 命中（同实例重复注册无红基线）；D4 命中（R3-DATA-004 失败计数漏项）；其余扫过或不适用。
- **R2 修复复核**：DATA-001..005/007/008/010..013 已修/文档化；DATA-006 部分（单块快照未记 format，R3-DATA-002）；DATA-009 `不做` 已文档化。
- **R2 旧结论复核**：块版本过新、脏集合不公开、快照/查询面结论维持。
- **开口项**：无新增待裁。
- **第三方对照**：沿用 R2；无新增推翻。
- **残留**：`DataSnapshot.Factory` 跨会话、DATA-011 边界。

### Serialize（T1 全量）

- **范围与输入**：`Runtime/Serialize/` 约 4 个 `.cs`、README、`Modules/Serialize.md`、Runtime 测试、Data/Save/Config 接缝。
- **18 类留痕**：A1 命中（Newtonsoft 多态、`Default` 双面，R3-SER-001/006）；A2 命中（`Get(null)` 异常类型、type 形参未用，R3-SER-003/005）；B3 命中（R3-SER-003）；D3 命中（AutoInit 自证，R3-SER-004）；D4 命中（R3-SER-003）；F 命中（R3-SER-006）；其余扫过或不适用。
- **R2 修复复核**：SER-001/003/006 已修；SER-002/004 部分（R3-SER-002/003）；SER-005 已修（四类失败面），但 `Unregister(null)`/`Register(null)` 仍零断言。
- **R2 旧结论复核**：格式名裸字符串键、内置注册不覆盖使用方、会话复位 SER-006 已修等维持。
- **开口项**：无新增待裁。
- **第三方对照**：Newtonsoft/JsonUtility 边界；无新增推翻。
- **残留**：JsonUtility 畸形 JSON、多态边界。

### Audio（T1 全量）

- **范围与输入**：`Runtime/Audio/` 约 17 个 `.cs`、README、`Modules/Audio.md`、Runtime 测试、Update/Pool/Asset 接缝。
- **18 类留痕**：A1 命中（AudioMixer 配方、R2 AUD-008/010 残留，R3-AUD-001/006/007）；B1 命中（直接 Dispose 后门面卡死，R3-AUD-005）；B3 命中（音量刷新假 null、死槽位、Sweep break，R3-AUD-002/003）；B5 命中（死槽位/过早注销，R3-AUD-002/003）；E 命中（AudioHandle 缺 IEquatable，R3-AUD-004）；C2 残留（StartingTimeoutTicks）；D3 残留；其余扫过。
- **R2 修复复核**：AUD-001 已修复；AUD-002/004/005/007/009/011/012/013 已修；AUD-003/006/008/010/014 部分（R3-AUD-002/003/006/007 等）。
- **R2 旧结论复核**：暂停/恢复、虚拟声部、通道音量、3D 定点等结论维持；AUD-013 探针已关闭。
- **开口项**：R3-ADJ-02 建议 `不做`（Play/PlayAsync 不对称）。
- **第三方对照**：沿用 R2；AudioMixer 路由是新增缺口。
- **残留**：AUD-001 的 `isPlaying` 平台前提、AudioListener.pause 真机、低帧率安全系数。

### Timer（T1 全量）

- **范围与输入**：`Runtime/Timer/` 约 6 个 `.cs`、README、`Modules/Timer.md`、Runtime 测试、Update/UI 接缝。
- **18 类留痕**：A1 命中（TMR-004 README 残留、R3-TMR-003 台账陈旧）；C2 命中（R3-TMR-001 跨表 + TMR-003 提前触发）；D3 命中（Clear 恢复路径零覆盖，R3-TMR-002）；D5 命中（TMR-003 干净修法待裁）；其余扫过。
- **R2 修复复核**：TMR-001 已修；TMR-002 已修但跨表残留（R3-TMR-001 P1）；TMR-003 文档化；TMR-005..010 已修；TMR-004/011 部分（R3-TMR-003 等）。
- **R2 旧结论复核**：档位语义、锚点、回调内新建等结论维持；「同轴创建由扫尾补算覆盖」的表述对跨表不成立。
- **开口项**：R3-ADJ-10 建议 `暂不做`、R3-ADJ-11 建议 `暂不做`。
- **第三方对照**：沿用 R2；无新增推翻。
- **残留**：TMR-003 实时探针、跨表定时器 PlayMode 红证。

### Bootstrap（T3 紧凑留痕）

- **范围与输入**：`Runtime/Bootstrap/` 全部约 5 个 `.cs`、README、`Modules/Bootstrap.md`、Runtime/Editor 测试、Pipeline/Asset/Input/UI/Settings 接缝；特别核对 `f8a64bf`/`661a760` 对 `DefaultGameLauncher` 的接线。
- **18 类留痕**：A1 命中（Localization 阶段重跑所有权，R3-LOC-002）；B3 命中（取消回调异常跳过 Shutdown，R3-BST-001）；C2 命中（启动器非根对象，R3-BST-002）；D3 命中（字段接线零覆盖）；D5 命中（超时装饰器绕过补句未落盘）；B6 命中-已修复（代际）；B2 命中-已文档化；A2/A3/B1/B4/B5/C1/D1/D2/D4/E 扫过；F 不适用。
- **R2 修复复核**：R2 跳过依据对逻辑仍成立（2026-10-17 重审六项全部落地：告警、代际、所有权、契约/进度中继/承诺收窄），但**接缝已失效**：`DefaultGameLauncher` 的 `_assetPlayMode` 序列化字段经 `661a760` 重排改变启动模式（R3-AST-001）；Localization 阶段重跑所有权未对齐（R3-LOC-002）。无 `[回归]` 新立条（两条均归拥有方）。
- **R2 旧结论复核**：内置阶段可重复执行、谁初始化谁清理等结论维持；R2 §7 建议的「超时装饰器绕过」一句仍未落盘。
- **开口项**：无新增待裁。
- **第三方对照**：五框架启动表对照沿用 R2；无新增推翻。
- **残留**：取消回调异常、子对象启动器、Localization ExecuteTwice 均需用例坐实。

### Lock（T3 紧凑留痕）

- **范围与输入**：`Runtime/Lock/` 全部约 5 个 `.cs`、README、`Modules/Lock.md`、Runtime 测试；冻结 SHA 后零行为变更。
- **18 类留痕**：B5/B2 命中（派发后注册销毁绑定，R3-LOCK-001）；A1 命中（审计编号进包内文档，R3-LOCK-002）；D3 命中（ABA 无测试 + 三处零覆盖）；E/F 命中-已文档化/已测；D5 命中-已文档化（无 owner 重载）；A2/A3/B1/B3/B4/B6/C1/D1/D2/D4 扫过；C2 不适用（纯托管）。
- **R2 修复复核**：LOCK-001 P1 已修复（`23f5b0d`，红证 + 负向控制）；LOCK-002/003/004 三条 P2 全部修复（`03d8494`/`ed7d2b3`）；**无偏差、无回归**。开口项 3 条维持（SubscriptionTracker 措辞在 Event 侧、跨对象自动退订、池化 ABA 无测试）。
- **R2 旧结论复核**：先摘后派发、按主体身份、无 owner 重载等结论维持。
- **开口项**：无新增待裁。
- **第三方对照**：补 `CountdownEvent`（无持有者身份）对照；GAS 计数语义已入台账。
- **残留**：R3-LOCK-001 反射探针、ABA 红证、关闭域重载两遍播放（手工）。

### Event（T2 增量）

- **范围与输入**：`Runtime/Event/` 全部约 4 个 `.cs`、README、`Modules/Event.md`、Runtime 测试、Message/Reactive/Input/Settings 接缝。
- **18 类留痕**：A1 命中（台账理由陈旧，R3-EVT-001）；B3/D4 命中（`onEmpty` 异常未隔离，R3-EVT-002）；B4 已裁定（不加主线程断言）；A2/A3/B1/B2/B5/C1/D1/D2/D3/F 扫过（D3 有 `onEmpty` 异常与派发中触发两处缺口）；B6/C2/D5/E 不适用。
- **R2 修复复核**：EVT-001/002/003/004/006/007 已修复（EVT-007 经 Message 侧 `c3e16c9` 闭合）；EVT-005 已处置（暂不做 + 台账）；偏差：R2 §7 要求订正的「无跨会话静态态」理由仍未改（R3-EVT-001）。
- **R2 旧结论复核**：LIFO、无算子面、只重放最近 1 条、异常隔离保留订阅等维持。
- **开口项**：EVT-005 维持 `暂不做`。
- **第三方对照**：`onEmpty` 回调异常语义无第三方先例；其余沿用 R2。
- **残留**：`onEmpty` 异常处置、派发中触发、并发退订窗口。

### Reactive（T2 增量）

- **范围与输入**：`Runtime/Reactive/` 全部约 5 个 `.cs`、README、`Modules/Reactive.md`、Runtime 测试、Settings/UI 接缝。
- **18 类留痕**：A1/F 命中（`IReactivePropertyWriter` 双承诺，R3-RCT-001）；B4/D3 命中（RCT-009 裁定未入台账，R3-RCT-002）；B2 命中 RCT-001（已按用户裁定文档化 + 回归锁）；A2/A3/B1/B3/B5/C1/D1/D2/D4/E/F 扫过（D3 仍缺容量负值与 Sort 部分重排用例）；B6/C2/D5 不适用或已裁。
- **R2 修复复核**：RCT-001（P1）按用户裁定「文档化 + 回归锁」结案；RCT-002/003/004/005/006/008/010 已修复；RCT-007 已处置；RCT-011 部分（容量构造 `<exception>` 未补）；RCT-009 部分（台账无条目，R3-RCT-002）。
- **R2 旧结论复核**：不引入 R3 式「拥有上游」、链式 Select 中间派生值文档化等维持。
- **开口项**：RCT-001 维持文档化；RCT-009 建议补台账。
- **第三方对照**：明确不引入 R3 式拥有上游；无新增推翻。
- **残留**：RCT-010 装箱量无法用本仓尺子实测（尺子恒 0）；RCT-006 部分重排未实测。

### Pipeline（T2 增量）

- **范围与输入**：`Runtime/Pipeline/` 全部约 3 个 `.cs`、README、`Modules/Pipeline.md`、Runtime/Editor 测试、Bootstrap 接缝。
- **18 类留痕**：A2/D4 命中（容器元素 null 裸 NRE，R3-PIPE-001）；A1/C2/D4 命中（`+∞` 超时溢出，R3-PIPE-002）；B3 命中（取消路径跳过 Settle，R3-PIPE-003）；B5 命中（`_stageCtx` 钉住上一轮管线，R3-PIPE-004）；A3/B1/B4/B6/C1/D1/D2/E/F 扫过；B2/D5 不适用/半闭合（Bootstrap 子阶段超时只闭合一端）。
- **R2 修复复核**：PIPE-001/002/003/004 已修复；PIPE-005/006 已按文档化处置；R2 开口项「Bootstrap 子阶段超时两端各补一句」只闭合 Bootstrap 侧。
- **R2 旧结论复核**：容器树介于 MediatR 与 Dataflow、超时乐观语义、失败取首失败诊断等维持。
- **开口项**：Bootstrap 子阶段超时在 Pipeline 侧仍缺一句。
- **第三方对照**：`+∞` 超时缺口是本仓特有；其余沿用 R2。
- **残留**：PIPE-005 并发串台、容器顺序复用、R3-PIPE-003 迟到广播、超时放弃阶段的令牌 ODE。

### File（T2 增量）

- **范围与输入**：`Runtime/File/` 全部约 7 个 `.cs`、README、`Modules/File.md`、Runtime 测试、Save/Settings/Asset 接缝。
- **18 类留痕**：A1 命中（权限契约、README 计数，R3-FILE-003/005）；A2 命中（移动 Streaming 路径缺口，R3-FILE-002）；B1 命中（Destroy 后 crypto 设置，R3-FILE-004）；B3 FILE-001 已修；B4 FILE-002 文档化；C2 命中 OSXServer（R3-FILE-001）；D3 FILE-008 已补缝；D4 命中（权限/计数）；D5 Save 删除缺口已闭合；E 命中（移动 Streaming 未归一/未沙箱）；F 扫过。
- **R2 修复复核**：FILE-001 已修复；FILE-002 文档化（XML 未同步）；FILE-009 **部分修复**（漏 OSXServer，R3-FILE-001）；FILE-012 部分（R3-FILE-005）；其余已修。
- **R2 旧结论复核**：原子写、Provider 能力接口、装饰器不变量等维持。
- **开口项**：FILE-009 探针（R3-PRB-04）仍未结。
- **第三方对照**：沿用 R2；无新增推翻。
- **残留**：macOS Server 构建、Android/iOS Streaming、权限异常类型。

### Diagnostics（T2 增量）

- **范围与输入**：`Runtime/Diagnostics/` 约 2 个 `.cs`、Editor 窗口/页签、README、`Modules/Diagnostics.md`、测试。
- **18 类留痕**：A1 命中（R3-DIAG-003）；A2 命中（R3-DIAG-002）；B3 命中（R3-DIAG-001）；B2 DIAG-003 已修；D3 DIAG-005 守卫已补（射程限 `*DiagnosticPanel`）；D4 DIAG-002 已修；E 引用身份去重；F 复制/显示两面已对齐；其余扫过或不适用。
- **R2 修复复核**：DIAG-001/004/006 已修；DIAG-002 已修；DIAG-003 已修；无回归。
- **R2 旧结论复核**：不加主线程断言、页签按实例幂等等维持。
- **开口项**：R3-DIAG-002 建议补恢复入口或改文档。
- **第三方对照**：沿用 R2；无新增推翻。
- **残留**：坏 Title 页签用例、包外 `Clear` 恢复探针。

### Camera（T2 增量）

- **范围与输入**：`Runtime/Camera/` 约 4 个 `.cs`、README、`Modules/Camera.md`、测试、Cinemachine 接缝（扩展内部归 W5）。
- **18 类留痕**：A1 CAM-001/005/006 已修、CAM-003 部分（文档 `<=0`，R3-CAM-003）；B1 CAM-002 已修，新命中外部销毁相机变更面（R3-CAM-002）；B3 同 R3-CAM-002；C2 命中扩展 NaN（R3-CAM-001）；D3 CAM-001/003/004 有红证，缺口 2 条；D4 命中（R3-CAM-002）；D5 命中 CAM-005 接缝（R3-CAM-001）；E/F CAM-001 三面统一；其余扫过。
- **R2 修复复核**：CAM-001/005/006 已修复；CAM-003 部分（三处公开 XML 仍写 `<= 0`）；CAM-004 已补 ABA 用例；无 `[回归]`。
- **R2 旧结论复核**：核心不引用 Cinemachine、Brain 混合与过场边界等维持。
- **开口项**：CAM-005 的扩展行为对齐仍 `未做`（Documentation/Cinemachine.md）。
- **第三方对照**：Cinemachine 3.1.7 `OutputCamera` 可空；「恒非空」是过度承诺（R3-CAM-003）。
- **残留**：扩展 NaN/∞、外部销毁相机、CAM-003 文档。

### Scene（T2 增量）

- **范围与输入**：`Runtime/Scene/` 约 2 个 `.cs`、README、`Modules/Scene.md`、测试、Asset 接缝。
- **18 类留痕**：A1 SCN-001/004/005 已修、SCN-002 部分（XML 未同步）+ R3-SCN-003；B3 SCN-001 已修 + R3-SCN-002；B5 SCN-003 **修复未生效**（R3-SCN-001 `[回归]`）；D1 命中（R3-SCN-003）；D3 SCN-006 已修、SCN-003 假绿；D4 命中（R3-SCN-002）；D5 SCN-003/005 跨模块；其余扫过或不适用。
- **R2 修复复核**：SCN-001 已修复；SCN-002 部分（只改 README）；SCN-003 **修复未生效**（R3-SCN-001，与 R3-AST-002 同根因）；SCN-004/005 已修；SCN-006 已修。
- **R2 旧结论复核**：无引用计数、外部卸载感知、同名规避等维持。
- **开口项**：SCN-003 修复需重做（R3-SCN-001）；SCN-005 主线程断言仍文档化。
- **第三方对照**：Addressables 的失败原因/句柄响应式仍优于本仓。
- **残留**：真 YooAsset 场景卸载序、假件 `Release` 真语义、回收段异常。

### Log（T2 增量）

- **范围与输入**：`Runtime/Log/` 全部约 8 个 `.cs`、README、`Modules/Log.md`、Runtime/Editor 测试、全模块 sink 消费者。
- **18 类留痕**：A1 命中（README 捕获 bullet 自相矛盾、维护文档 33 分类、LogFileSink XML，R3-LOG-002/003/004）；B3/D4/F 命中（Flush 内死亡 sink 不摘除，R3-LOG-001）；B6/C2/D3 有记录（session/seq 窄窗、毫秒基名碰撞、Flush 死亡零覆盖）；A2/A3/B1/B2/B4/B5/C1/D1/D2/E 扫过；D5 不适用。
- **R2 修复复核**：LOG-001/002 已修复；LOG-003 **部分修复**（只修 Write 路径，R3-LOG-001）；LOG-008 7/8 已修，1 子项证据不足；其余已修/文档化。
- **R2 旧结论复核**：sink 异常静默摘除、JSONL schema、分类键等维持；3 条待裁（R3-ADJ-05/06/07）维持。
- **开口项**：R3-ADJ-05/06/07 建议 `暂不做/不做/不做`。
- **第三方对照**：Serilog/ZLogger 基线沿用 R2；无新增推翻。
- **残留**：Flush 死亡探针、毫秒基名碰撞、session/seq 窄窗、域重载文件句柄、LOG-011 线程懒建。

### Pool（T2 增量）

- **范围与输入**：`Runtime/Pool/` 全部约 9 个 `.cs`、README、`Modules/Pool.md`、Runtime/Editor 测试、UI/Diagnostics/Audio 接缝。
- **18 类留痕**：A1/B2/B3/D4/F 命中（回调异常时实例静默丢弃，R3-POOL-001）；B5/B6 会话复位已扫（POOL-002 修复在位）；D3 记录注册计数断言放宽为「≤+1」；A2/A3/B1/B4/C1/C2/D1/D2/E 扫过；D5 不适用。
- **R2 修复复核**：POOL-002 P1 已修复；P2 8 条已修/文档化；POOL-010 按裁定保持公开；无回归。
- **R2 旧结论复核**：池配置、集合池重建、RemovePool 等维持。
- **开口项**：R3-POOL-001 需裁定「回调异常是否触发丢弃回调」。
- **第三方对照**：UnityEngine.Pool / Microsoft.Extensions.ObjectPool 基线沿用 R2。
- **残留**：POOL-002 跨会话手工、generator 返回 null、旧 `IPool<T>` 写孤儿池。

### Message（T2 增量）

- **范围与输入**：`Runtime/Message/` 全部约 5 个 `.cs`、README、`Modules/Message.md`、Runtime/Editor 测试、Event/Input/Settings/UI 接缝。
- **18 类留痕**：A1 命中（台账陈旧、MSG-003 指针悬空、XML 示例缺 `IMessageSubscriber`、两处陈旧 XML，R3-MSG-001/002/003/005）；B2/D4 命中（None 令牌静默退订，R3-MSG-004）；B5/A2/D3 命中（Input 合并句柄被丢弃，R3-INP-006）；B3/B6/C1/C2/D1/D2/F 扫过；E 扫过但 Key 全局性/默认比较器仍未写进 README。
- **R2 修复复核**：MSG-001/005/006/007/008/009 已修复；MSG-002 **部分**（XML 示例缺接口，R3-MSG-003 P1）；MSG-003 部分（台账无条目，R3-MSG-002）；MSG-004 部分（Input 接缝，R3-INP-006）；无 `[回归]`。
- **R2 旧结论复核**：通道回收、键值缓冲、异步订阅等维持。
- **开口项**：MSG-003 的 `待裁` 需真正写入台账；Event「只清缓存保订阅」原语待裁。
- **第三方对照**：MessagePipe/MediatR 基线沿用 R2；无新增推翻。
- **残留**：MSG-004 登记累积无直接测量、Key 全局性、Release 无主线程防线。

### Cinemachine（T2+ 扩展包）

- **范围与输入**：`Assets/XFramework.Cinemachine/` 全部文件（1 个 runtime + 2 个测试 fixture + README/package/CHANGELOG/sample）、核心 `Runtime/Camera/` 接缝、`Documentation/Cinemachine.md`。
- **18 类留痕**：A1 命中（强制 cut 声称、0.3.0 兼容、完成回调、OutputCamera 义务，R3-CIN-001/004/005/006/007）；A2 适配器实现 `ICameraManager` 全成员；A3 人读扫过（扩展不在 check-docs 编译射程）；B1 Dispose 幂等；B2 无订阅/ticker，`_reverse` 陈旧键（R3-CIN-003）；B3 命中（销毁 vcam，R3-CIN-003）；B4 仅文档；B5 槽位复用/代际；B6 代际正确但缺 ABA 用例（R3-CIN-005）；C1 无每帧路径；C2 命中（NaN/±∞，R3-CIN-002）；D1 扩展只引核心公开面 + Cinemachine，核心零 Cinemachine；D2 无重载二义；D3 命中（R3-CIN-001/005）；D4 命中（R3-CIN-002/003/004）；D5 命中（R3-CIN-002/007）；E Ordinal/引用身份；F 命中（R3-CIN-007）。
- **R2 CAM-005 复核**：核心已在 `deee388` 窄化措辞，适配器行为不再违反核心契约；`Documentation/Cinemachine.md:89` 的 `未做` 前提「与核心契约不一致」不再成立。建议改 `不做`（核心已接受）或 `待裁`（若仍想对齐默认实现），并修 R3-CIN-007 措辞。
- **开口项**：R3-CIN-001（P1 候选）需 PlayMode 红证；CAM-005 条目文字过时。
- **第三方对照**：Cinemachine 3.1.7 官方 `Brain.ActiveBlend`/`OutputCamera` 语义；本包刻意不做跟随/震屏/缩放。
- **残留**：R3-CIN-001 运行期、Timeline 集成（推理）、示例 `Awake/OnDestroy` 无条件 Initialize/Destroy。

## 9. 14 条待裁的一次性裁定建议

> 审计期只写建议；台账状态由实施轮经用户确认后更新。状态词按 `Modules/README.md` 的六词表。

| ID | 模块 | 问题 | 推荐 | 理由 | 落点 | 验证/重开判据 |
|---|---|---|---|---|---|---|
| R3-ADJ-01 | Asset | 预检与下载的版本绑定断了；`PreDownloadContentAsync` 丢弃 operation，`CreateDownloader` 走 ActiveManifest | `暂不做` | 要修得新增「从预检产出的下载器/字节数」公开 API；当前无使用方，README 已订正为可工作顺序 | `Documentation/Modules/Asset.md` | 重开：出现必须在激活前预下载的真实热更流程 |
| R3-ADJ-02 | Audio | `Play` 与 `PlayAsync` 句柄失败形态不对称 | `不做` | 两条契约各自自洽且已文档化；统一会改变 `Play` 的「预占即返回」时序 | `Documentation/Modules/Audio.md` | 无重开判据（若出现使用方混淆再评估） |
| R3-ADJ-03 | Localization | 并发切换是否引入「后完成者胜」代次作废 | `不做` | 当前语义可接受；代次作废增加状态机复杂度，无使用方需求 | `Documentation/Modules/Localization.md` | 无重开判据（若出现需要确定性顺序的并发切换再评估） |
| R3-ADJ-04 | Localization | `SetLanguageData` 覆盖当前语言数据是否补事件面 | `暂不做` | 覆盖当前语言是低频运维动作；新增事件扩大公开面 | `Documentation/Modules/Localization.md` | 重开：使用方运行期覆盖当前语言数据且绑定文本需自动刷新 |
| R3-ADJ-05 | Log | 结构化参数（给 193 个调用点起字段名） | `暂不做` | 迁移成本翻倍；现有单行 JSON + cat/lvl 已满足 AI 分析 | `Documentation/Modules/Log.md` | 重开：日志分析管线明确要求命名字段 |
| R3-ADJ-06 | Log | Editor 两处无前缀 `Debug.LogError` | `不做` | 安装器是 Editor 工具，直写 `Debug.LogError` 有意避免依赖 LogManager 初始化；改前缀只影响仓内文案 | `Documentation/Modules/Log.md` | 无重开判据 |
| R3-ADJ-07 | Log | `UIDefaultController` 5 条 verbose 日志保持 `Info` | `不做` | 降到 `Debug` 会让 `verbose: true` 的使用方在 Release 突然看不到日志，属行为变更且无收益 | `Documentation/Modules/Log.md` | 无重开判据 |
| R3-ADJ-08 | Save | `RecoverAsync`「全程不切回主线程」与实现不一致；恢复扫描仍有 5 处同步 Delete + 2 处同步 Exists | `未做` | 真实契约缺口；先把 5 处错误表述订正，再单独立项做「切回/不切回」双路径线程改造，不与其它 Save 行为混批 | `Documentation/Modules/Save.md`；§10 批次 2 | 验证：主线程探针 + grep 恢复路径无同步 IO |
| R3-ADJ-09 | Settings | 被丢弃的旧管理器仍被 `Application.wantsToQuit` root | `不做` | 与 2026-10-18 框架级裁定「会话边界只丢引用、不替使用方 Dispose」一致；`SaveOnQuit` 下是可接受残留 | `Documentation/Modules/Settings.md` | 重开：框架级会话边界裁定被推翻 |
| R3-ADJ-10 | Timer | `UpdateManager.Clear()` 后 Timer 模块失联 | `暂不做` | 恢复路径已文档化（`CancelAll` + 重建）；自动侦测会在每次入口重注册，踩「吞时间」；UI 的每入口重注册不适用于 Timer | `Documentation/Modules/Timer.md` | 重开：使用方无法调用 `CancelAll`（拿不到引用）或出现真实事故 |
| R3-ADJ-11 | Timer | `Create`/`Restart` 锚点需要 Update 当前轴时刻 | `暂不做` | R2 已裁定只文档化；干净修法要 Update 暴露当前轴时刻查询面（跨模块公开 API） | `Documentation/Modules/Timer.md` | 重开：出现对短定时器提前触发敏感的使用方（沿用 R2 判据） |
| R3-ADJ-12 | UI | 自定义 Tip/HUD provider 运行期注入拿不到 `SetUIRoot` | `未做` | 接口文档承诺 `Initialize` 时调用；注入路径也应兑现，优先「注入时补调 `SetUIRoot(UIRoot)`」 | `Documentation/Modules/UI.md`；§10 批次 2 | 验证：Initialize 后注入的假 provider 收到一次 UIRoot；默认 provider 不重复调用 |
| R3-ADJ-13 | UI | `AllPanelsClosedMessage` 无条件发布 | `未做` | 事件面应与查询面一致：仅在 `OpenCount == 0` 时发布，否则订阅者会误判「全关」 | `Documentation/Modules/UI.md`；§10 批次 2（行为变更需单独确认） | 验证：控制器拦下关闭/无面板时不发；最后一个面板关闭时发一次 |
| R3-ADJ-14 | Ledger | 包自身 Git URL 不钉修订是否在包 README 补说明 | `未做` | 第三方依赖必须钉 tag 与包自身跟随 `main` 的不对称是有意的；补一句说明 + 如何钉 `0.3.0`，docs-only | `Documentation/Ledger.md` + 包 README（实施轮） | 验证：README 出现说明；依赖表不变 |

## 10. R3 分步实施计划

> 审计轮只出计划；实施轮按「一行一提交、测试与修复同提交、行为变更同提交写 CHANGELOG」执行。公开 API/破坏性变更与设计裁定单列，须用户确认。命令缩写：`PM(x)` = `run-tests.ps1 -Filter "x" -Platform PlayMode`；`EM(x)` = EditMode；`ALL(x)` = 双平台；`FULL` = 全量；`DOC` = `check-docs.ps1 -Enforce`；`BUILD` = 两个核心 csproj；`API` = 公开 API 基线流程。

### 批次 0：P0 与 R2 回归示例（先止血）

| # | 提交信息 | 覆盖 | 验证 |
|---|---|---|---|
| 0.1 | `fix(Config): 行类型示例改回字段+显式接口实现 (R3-CONF-001)` | README 三处、ConfigManager XML、CHANGELOG | 编译探针正/负；`PM(Config)`；`DOC` |
| 0.2 | `fix(Message): XML 示例补 IMessageSubscriber (R3-MSG-003)` | `IDestroyCancellationToken.cs` XML、CHANGELOG | 编译探针正/负；`DOC` |

### 批次 1：P1 正确性（无裁定或已推荐修法）

| # | 提交信息 | 覆盖 | 验证 |
|---|---|---|---|
| 1.1 | `docs(Input): 依赖表与安装器补 Input System (R3-INP-001)` | 包 README、安装器、DependencyDeclarationTests、CHANGELOG | `EM(Architecture)`；裸消费方探针；`DOC` |
| 1.2 | `docs(Config): 补 ScriptableObjectTable 资产形状 (R3-CONF-002)` | Config README、Modules/Config、SO 形状用例 | `EM(Config)`；负向控制；`DOC` |
| 1.3 | `docs(Audio): 删除不可实现的 AudioMixer 配方 (R3-AUD-001)` | Audio README、Modules/Audio | `DOC`；人工核对 |
| 1.4 | `fix(Timer): 跨表补扫本趟新建定时器 (R3-TMR-001)` | TimerCore/TimerTable、跨表回归用例、README | `PM(Timer)`；改前必红；`DOC` |
| 1.5 | `fix(Cinemachine): 强制 cut 改用 ActiveBlend=null (R3-CIN-001)` **或** `docs(Cinemachine): 订正强制 cut 声称 (R3-CIN-001)` | `CinemachineCameraManager`、PlayMode 用例、`Documentation/Cinemachine.md` | `PM(Cinemachine)`；改前必红；`DOC` |

> R3-AST-001（枚举重排）进入批次 2，先由用户裁定 0.3.0 分发事实与修法。

### 批次 2：裁定与行为变更

| # | 提交信息 | 覆盖 | 验证 |
|---|---|---|---|
| 2.1 | `fix(Asset): 恢复枚举整数值兼容 (R3-AST-001)` **或** `docs(Asset): 补序列化迁移说明 (R3-AST-001)` | AssetInitOptions/CHANGELOG/Modules/Asset | 序列化读回探针；基线；`PM(Asset)`；`DOC` |
| 2.2 | `fix(Save): RecoverAsync 线程契约双路径 (R3-ADJ-08)` | SaveManager/ISaveManager/Impl、5 处文档、TMP 测试 | 主线程探针；`ALL(Save)`；`DOC` |
| 2.3 | `fix(UI): 注入 provider 补调 SetUIRoot (R3-ADJ-12)` | UIManagerImpl、假 provider 用例、README | `PM(UI)`；`DOC` |
| 2.4 | `fix(UI): AllPanelsClosedMessage 仅在 OpenCount==0 发布 (R3-ADJ-13)` | UIManagerImpl、控制器/空面板用例、CHANGELOG 行为变更 | `PM(UI)`；`DOC` |
| 2.5 | `docs(README): 包自身 Git URL 说明 (R3-ADJ-14)` | 包 README、Ledger | `DOC` |
| 2.6 | `docs(台账): 归档 R3 待裁裁定（不做/暂不做）` | 各模块台账 + Ledger | `DOC` |

### 批次 3：P2 文档/契约清扫

- 线程契约、失败语义、README 计数/陈表、XML 与实现相反、台账状态陈旧；按模块/主题原子提交。
- 代表项：R3-UPD-001/002/003、R3-UIA-010、R3-UIB-001/008/009、R3-CONF-004/006/009、R3-SER-001/002/006、R3-AUD-006/007、R3-SAVE-002/004/006、R3-DATA-004/005、R3-LOC-001/004/008、R3-MSG-001/002/005、R3-LOG-002/003/004、R3-EVT-001、R3-RCT-002、R3-FILE-005、R3-DIAG-003、R3-CAM-003、R3-SCN-003、R3-CIN-004/005/006/007 等。
- 验证：`DOC`；涉及测试的补模块 Filter。

### 批次 4：P2 代码/测试补齐

- 生命周期/容器、异常隔离、测试假绿/零覆盖、真实缝用例；按模块原子提交。
- 代表项：R3-AST-002..009、R3-UIA-003..009、R3-UIB-002..007/011..013、R3-CONF-003/005/007/008/010/011、R3-LOC-002/003/005/006/007、R3-INP-002..006、R3-SET-001..006、R3-SAVE-001/003/005、R3-DATA-001..003、R3-SER-003/004/005、R3-AUD-002..005、R3-TMR-002/003、R3-EVT-002、R3-RCT-001、R3-PIPE-001..004、R3-FILE-001..004、R3-DIAG-001/002、R3-CAM-001/002、R3-SCN-001/002、R3-LOG-001、R3-POOL-001、R3-MSG-004、R3-CIN-002/003。
- 验证：`PM/EM/ALL`；改前必红；`DOC`。

### 批次 5：探针轨、会话复位与扩展包门禁

- 5 项遗留探针（SET-012 / CONF-014 / CONF-017 / FILE-009 / F8）：能跑则跑，不能跑在台账登记 `未做` + 配方。
- Cinemachine 扩展包门禁覆盖（R3-CIN-008）；核心包不引入 Cinemachine 依赖。
- 跨会话手工两遍 PlayMode；`FULL + DOC + API + BUILD`。

### 依赖与风险

- 0.1 必须最先；R3-CONF-001 是 R2 修复回归，影响所有按文档建表的用户。
- 2.1 取决于 0.3.0 分发事实；恢复旧整数值是源兼容 + 序列化兼容的首选。
- 2.2 是线程路径改造，单独计划，不与其它 Save 行为混批。
- 2.3/2.4 是行为变更，实施前单独确认。
- 1.1 只改文档/安装器/测试，不动 asmdef；若要把 Input System 变为可选，另立公开面议题。
- 探针轨依赖真机/环境，不阻塞代码批次；未结案进台账。

## 11. 遗留承接与探针轨

### 11.1 R2 遗留探针

| ID | 来源 | 内容 | 执行条件 / owner | 预期证据 | 状态 |
|---|---|---|---|---|---|
| R3-PRB-01 | SET-012 | `Application.wantsToQuit` 编辑器探针，按结果订正三处文档 | 手工/编辑器（用户） | 订阅回调是否触发 + 三处文案核对 | 未执行（审计轮只读，需手工） |
| R3-PRB-02 | CONF-014 | 真 YooAsset 低内存：加载 SO Global → `UnloadUnusedAssetsAsync` → 断言 `GetGlobal` 是否假 null | 真包/低内存环境（用户） | `GetGlobal` 返回值 + 资源销毁状态 | 未执行（缺真包环境） |
| R3-PRB-03 | CONF-017 | IL2CPP High stripping 真机构建，验证 `[Preserve]` 是否足够 | 真机/IL2CPP（用户） | 首次 `Preload` 是否成功 + 日志 | 未执行（缺真机） |
| R3-PRB-04 | FILE-009 | Server 平台构建验证（代码侧平台分支已在 B37 完成） | Server Build Support（用户） | 构建退出码 + 运行期文件路径 | 未执行（未验证模块安装） |
| R3-PRB-05 | F8 | 关闭域重载连跑两遍 PlayMode，验收会话复位族 | 手工编辑器设置（用户） | 第二遍无旧订阅/旧实例复活 | 未执行（需手工；配方见 `Documentation/Workflow.md` §六） |

### 11.2 R3 新增探针

| ID | 关联 | 内容 | 状态 |
|---|---|---|---|
| R3-PRB-06 | R3-CONF-001 | 临时 `netstandard2.1` 编译探针：字段版 → CS0535；显式接口实现版 → 0 error | **已执行**（实测，见 §4） |
| R3-PRB-07 | R3-TMR-001 | PlayMode：60s 状态定时器回调创建 0.1s 普通定时器，断言下一拍附近触发 | 待执行（`-Filter TimerTier`） |
| R3-PRB-08 | R3-AST-001 | 0.3.0 场景 `_assetPlayMode: 0` 升级后用 `SerializedObject` 读回，断言变为 EditorSimulate | 待执行（需 Unity 序列化环境） |
| R3-PRB-09 | R3-UIA-007 | PlayMode：在 `DoOpenAsync` 窗口内 Dispose，断言面板不被二次回池/二次 OnPoolRecycle | 待执行 |
| R3-PRB-10 | R3-AUD-001 | 路由探针：读 `AudioSource.outputAudioMixerGroup`（当前公开面不可达，需反射/内部缝） | 待执行（结构性） |
| R3-PRB-11 | R3-INP-001 | 裸消费方探针工程：只按包 README 安装依赖，编译 XFramework | 待执行（实施轮） |
| R3-PRB-12 | R3-CIN-001 | PlayMode：ManualUpdate 推进中途 `Activate` 同一目标，断言 `IsBlending==false` / `ActiveBlend==null` | 待执行（`-Filter Cinemachine`） |
| R3-PRB-13 | R3-MSG-003 | 临时 `netstandard2.1` 编译探针：原样示例 → CS1929；补 `IMessageSubscriber` → 0 error | **已执行**（实测，见 §5） |
| R3-PRB-14 | R3-SCN-001 | 缝级：假件 `Release()` 置空 `SceneObject` 并模拟上游先 Release，断言 `SceneOperationCount==0` | 待执行（`-Filter Scene`） |

> 探针不阻塞报告收口；未执行项在实施轮按 §13.4 登记 `未做` + 配方。

## 12. 已评估未采纳 / 不重开清单

### 12.1 承接 R2 §9.1 的框架级 7 条

| 条目 | R2 状态 | R3 复核 | 结论 |
|---|---|---|---|
| Network 基础层 | 暂不做 | 仓内仍零消费者；File/MobileFileProvider 的 HTTP 仅服务 Streaming | 判据未触发，维持 |
| 代码热更（HybridCLR） | 暂不做 | 本轮未出现真实接入方；热更链路仍是 Asset 下载/激活 | 判据未触发，维持 |
| GM / 调试命令台 | 暂不做 | 仍无使用方给出命令集/权限/发布形态 | 判据未触发，维持 |
| App 生命周期 | 暂不做 | `pause` 仍只有 Settings 一处消费者 | 判据未触发，维持 |
| 性能基准套件 | 未做 | R3 未新增吞吐/耗时基准；`AllocationRuler` 仍只回答「有没有分配」 | 维持 `未做` |
| 示例覆盖面扩展 | 未做 | R3 再次发现示例正确性问题（R3-CONF-001），覆盖面与质量是两件事 | 维持 `未做` |
| 包自身 Git URL 说明 | 待裁 | 现有 tag `0.3.0`；R3 建议补说明（R3-ADJ-14） | 建议 `未做`（docs-only），待确认 |

### 12.2 R2 §9.2 已评估未采纳（维持）

- 框架级结构性 `不做` 组（状态机/行为树/ECS/时间模型/生命周期树、Update 组 API、DI 容器、Tween、Addressables、新手引导、平台 SDK 聚合、网络同步、统一 Profile、模块内部策略接口化、远程配置、崩溃上报、红点、画质档位、埋点/统计上报、热更编排）——R3 未发现推翻证据，**不重开**。
- 带判据的四条 `不做`（CI、`Tests~`/移出包目录、包在子目录、Git 钩子/Issue·PR 模板/`CONTRIBUTING`）——判据未触发，维持。
- 各模块台账中的 `不做/暂不做` 条目：R3 逐模块复核（见 §8），除 §9 的 14 条待裁外**全部维持**；未发现需要推翻的旧结论。
- 主线程断言「三份副本不抽公共防线」——维持；R3 未新增第四种形态。
- 会话复位「只丢引用不 Dispose」——维持 2026-10-18 框架级裁定；实例订阅残留按 §7.4 作实现缺口处理。

### 12.3 R3 新增建议（需用户确认后入台账）

| 建议 | 状态建议 | 理由 | 重开判据 |
|---|---|---|---|
| 公开枚举整数值纳入 `PublicApiBaseline` 或迁移检查 | `待裁` | R3-AST-001 暴露：基线只守形状，整数值重排无机械守卫 | 出现第二个因枚举值重排导致的序列化兼容事故 |
| 依赖清单从 asmdef 反查（R3-INP-001） | `未做` | `DependencyDeclarationTests` 只校验已列依赖，漏项不红 | 实施后关闭 |
| Cinemachine 扩展包纳入独立文档/编译门禁（R3-CIN-008） | `未做` | 核心 `check-docs`/API 基线不覆盖扩展包 | 实施后关闭 |
| 文档示例可编译性守卫 | `未做`（维持 R2） | R3-CONF-001 再次证明示例不编译会存活整轮 | 出现可接受的轻量实现 |
| 结构化日志参数（R3-ADJ-05） | `暂不做` | 193 个调用点迁移成本翻倍；JSONL cat/lvl 已够用 | 日志分析管线明确要求命名字段 |
| 反射式自动会话复位 | `不做`（维持） | 与显式登记、零反射取向冲突 | 无重开判据 |

> 上述 `不做/暂不做/待裁` 在写入台账前须经用户确认；本报告只登记建议与理由。

## 13. 台账同步索引

> 审计期**不写台账**；以下为实施轮（或用户确认后）的落点。R3 新发现修复落地后，才由实施提交记 `已做`。

### 13.1 14 条待裁的裁定建议

| 建议 ID | 模块 | 现状态 | R3 建议 | 落点 |
|---|---|---|---|---|
| R3-ADJ-01 | Asset | 待裁 | `暂不做`（预检/下载版本绑定；无使用方） | `Modules/Asset.md` |
| R3-ADJ-02 | Audio | 待裁 | `不做`（Play/PlayAsync 契约不对称是刻意的） | `Modules/Audio.md` |
| R3-ADJ-03 | Localization | 待裁 | `不做`（并发切换不引入代次作废） | `Modules/Localization.md` |
| R3-ADJ-04 | Localization | 待裁 | `暂不做`（SetLanguageData 不补事件面） | `Modules/Localization.md` |
| R3-ADJ-05 | Log | 待裁 | `暂不做`（结构化参数） | `Modules/Log.md` |
| R3-ADJ-06 | Log | 待裁 | `不做`（Editor 两处 Debug.LogError 保持原样） | `Modules/Log.md` |
| R3-ADJ-07 | Log | 待裁 | `不做`（UIDefaultController verbose 保持 Info） | `Modules/Log.md` |
| R3-ADJ-08 | Save | 待裁 | `未做`（RecoverAsync 线程契约双路径；单独计划） | `Modules/Save.md` |
| R3-ADJ-09 | Settings | 待裁 | `不做`（旧管理器 wantsToQuit root，随框架级裁定） | `Modules/Settings.md` |
| R3-ADJ-10 | Timer | 待裁 | `暂不做`（Clear 后不自动侦测） | `Modules/Timer.md` |
| R3-ADJ-11 | Timer | 待裁 | `暂不做`（锚点需 Update 当前轴时刻；沿用 R2 判据） | `Modules/Timer.md` |
| R3-ADJ-12 | UI | 待裁 | `未做`（注入 provider 补调 SetUIRoot） | `Modules/UI.md` |
| R3-ADJ-13 | UI | 待裁 | `未做`（AllPanelsClosedMessage 仅在 OpenCount==0 发布） | `Modules/UI.md` |
| R3-ADJ-14 | Ledger | 待裁 | `未做`（包自身 Git URL 说明，docs-only） | `Ledger.md` + 包 README |

### 13.2 R3 发现的台账落点

- **R3-CONF-001（P0，回归）**：实施后记 `已做`，带修复提交号；同时在 `Modules/Config.md` 记一笔「R2 修复把运行期错值换成编译错误，示例需字段 + 显式接口实现」。
- **R3-AST-001（P1，兼容性）**：裁定后记 `已做`（恢复整数值或补迁移说明），带用户裁定日期与提交号。
- **R3-INP-001 / R3-CONF-002 / R3-AUD-001 / R3-TMR-001（P1）**：实施后记 `已做`，带提交号与验证用例。
- **P2（§6）**：修复落地前不记台账；落地后按模块记 `已做`，纯文档订正按需记。
- **MSG-002 台账陈旧**：`Modules/Message.md:36` 的 `未做` 改为 `已做`，证据 `4e8645b`。

### 13.3 框架级台账（`Documentation/Ledger.md`）

- 新增 `未做`：依赖清单从 asmdef 反查（R3-INP-001）。
- 新增 `未做`：Cinemachine 扩展包门禁覆盖（R3-CIN-008）。
- `Documentation/Cinemachine.md:89` 的 CAM-005 条目：核心已窄化措辞，`未做` 的前提「与核心契约不一致」不再成立；建议改 `不做`（核心已接受）或 `待裁`（若仍想对齐默认实现），并修 R3-CIN-007 的 OutputCamera 措辞。
- 新增 `待裁`：公开枚举整数值纳入公开面基线/迁移检查（R3-AST-001）。
- §四复核记录追加：R2 §9.1 的 7 条复核结论（§12.1）。

### 13.4 探针轨

- 5 项 R2 遗留探针在对应模块台账（或 `Ledger.md`）登记 `未做`，证据写「R3 探针轨 R3-PRB-xx（配方见 `Documentation/AuditRound3.md` §11）」；能执行时改 `已做`/`不做`。
- F8（关闭域重载两遍 PlayMode）是跨模块项，落 `Ledger.md` §四复核记录。

## 附录 A. 探针与命令

### A.1 基线与门禁（2026-10-18，冻结 SHA `661a760`）

```bash
# 构建
dotnet build Venusy609.Xframework.csproj --artifacts-path "$env:TEMP\xfw-artifacts" -v q --nologo
dotnet build Venusy609.Xframework.Editor.Tests.csproj --artifacts-path "$env:TEMP\xfw-artifacts" -v q --nologo

# 全量测试
pwsh -File Tools/run-tests.ps1

# 文档门禁
pwsh -File Tools/check-docs.ps1 -Enforce

# 公开 API 基线
pwsh -File Tools/run-tests.ps1 -Fixture PublicApiBaselineTests
```

结果：BUILD 0 error；FULL PlayMode 1855/1855、EditMode 539/539；DOC 通过（24 模块 / 370 条台账 / 文件树一致）；API 1/1。

### A.2 审计自证

```bash
# ID 唯一性（应为空）
grep -oE 'R3-[A-Z]+-[0-9]{3}' Documentation/AuditRound3.md | sort | uniq -d
# 与附录 C 条数一致
grep -oE 'R3-[A-Z]+-[0-9]{3}' Documentation/AuditRound3.md | sort -u | wc -l

# 台账计数（与 check-docs 同口径）
files=$(ls Documentation/Modules/*.md | grep -v 'Modules/README.md')
for s in 已做 未做 待裁 暂不做 不做 已推翻; do
  printf '%s ' "$s"; { grep -h "^- $s：" $files Documentation/Ledger.md; } | wc -l
done

# 引用落盘抽检（示例）
git show 661a760:Assets/XFramework/Runtime/Config/README.md | sed -n '27p'
```

### A.3 CONF-001 编译探针（实测）

```bash
# 临时目录；正例（字段版）应 CS0535，负例（显式接口实现）应 0 error
# 见 %TEMP%\xfw-audit3\probe-conf001
dotnet build probe.csproj -v q --nologo
```

## 附录 B. 待取证

- R3-PRB-01..05：见 §11.1（SET-012 / CONF-014 / CONF-017 / FILE-009 / F8）。
- R3-PRB-06：已执行（CONF-001 编译探针）。
- R3-PRB-07：Timer 跨表补扫 PlayMode 红证。
- R3-PRB-08：AssetPlayMode 序列化读回。
- R3-PRB-09：UI Dispose × 在途打开。
- R3-PRB-10：AudioMixer 路由（结构性）。
- R3-PRB-11：裸消费方依赖安装。
- R3-PRB-12：Cinemachine 强制 cut（PlayMode）。
- R3-PRB-13：Message XML 示例编译探针（已执行）。
- R3-PRB-14：Scene `sceneUnloaded` 清表缝探针。
- 各模块 `[需实测]` 项：Asset 真 YooAsset/EditorSimulate、Config SO Table/低内存/IL2CPP、Localization Mono/IL2CPP、Input 振动/热插拔、Audio `isPlaying` 平台前提/AudioListener.pause、Timer TMR-003 实时锚点、UI HUD 重入/Tip 隔离/绑定释放、Save 可抛 IO 缝、Settings wantsToQuit、File OSXServer/移动 Streaming/权限异常、Camera-Cinemachine NaN/外部销毁、Scene 真卸载序、Log Flush 死亡、Pool 回调异常丢弃、Message None 令牌。

## 附录 C. R3 发现 ID 索引

> 按 ID 排序；一句话为报告内标题/首句的压缩，完整证据见对应节。

| ID | 级别 | 模块 | 一句话 | 状态 |
|---|---|---|---|---|
| R3-AST-001 | P1 | Asset | AssetPlayMode 枚举重排的「包未发布」前提与 tag 0.3.0 冲突 | 主会话复核 |
| R3-AST-002 | P2 | Asset | `YooAssetManagerImpl.cs:44-49,749-756,764-775` —— 会话复位只退订 `Application.lowMemory`，未拆 … | 按证据采信 |
| R3-AST-003 | P2 | Asset | `YooAssetSceneLoadSource.cs:87-108`、`YooAssetManagerImpl.cs:643-667` —— 卸载失败时适配器入口即丢 … | 按证据采信 |
| R3-AST-004 | P2 | Asset | `IAssetManager.cs:72-90` 等 —— 三个热更 API 对不存在的包静默返回，违背 XML「失败抛 `InvalidOperationExcepti… | 按证据采信 |
| R3-AST-005 | P2 | Asset | `YooAssetManagerImpl.cs:590-603,628-633` —— 场景加载进度回调抛非 OCE 异常时句柄既不释放也不登记。建议包 try/catc… | 按证据采信 |
| R3-AST-006 | P2 | Asset | `AssetDownloaderHandle.cs:151-155` —— 下载器三个用户事件直接 `Invoke`，订阅者异常可打断 YooAsset `Operati… | 按证据采信 |
| R3-AST-007 | P2 | Asset | `YooAssetManagerImpl.cs:528` —— `WaitOrReleaseAsync` 以接口形参收 `HandleBaseWait`，每条加载路径装箱… | 按证据采信 |
| R3-AST-008 | P2 | Asset | `AssetManagerImpl.cs:299-311` —— `PreloadAllAsync(null)` 抛 NRE，`LoadAllAsync` 抛 ANE。建… | 按证据采信 |
| R3-AST-009 | P2 | Asset | `DefaultGameLauncher.cs:29-32` —— Inspector 提供 `Host` 但无 `RemoteServices` 注入点，选 Host … | 按证据采信 |
| R3-AUD-001 | P1 | Audio | README 的「用项目自己的 AudioMixer」配方不可实现 | 主会话复核 |
| R3-AUD-002 | P2 | Audio | `AudioManagerImpl.cs` 音量刷新使用假 null 判断，已销毁声源不会被正确跳过。 | 按证据采信 |
| R3-AUD-003 | P2 | Audio | `AudioVoicePool.cs` 死槽位永不可恢复；`Sweep` 的 `break` 使 ticker 过早注销。 | 按证据采信 |
| R3-AUD-004 | P2 | Audio | `AudioHandle` 缺 `IEquatable`/相等运算符，与 `TimerHandle` 本轮补齐的形状不一致。 | 按证据采信 |
| R3-AUD-005 | P2 | Audio | 直接 `Dispose` 门面返回的管理器后门面卡死。 | 按证据采信 |
| R3-AUD-006 | P2 | Audio | README/XML 仍保留 R2 AUD-008/010 的「钳制点」句与文件树 internal 标注。 | 按证据采信 |
| R3-AUD-007 | P2 | Audio | 3D 衰减/空间参数不可配置且未写已知限制。 | 按证据采信 |
| R3-BST-001 | P2 | Bootstrap | `Bootstrap.cs:383`（同型 `:249`）——`Shutdown` 第一行 `_runCts?.Cancel()` 无异常隔离；阶段注册的取消回调抛异常时… | 按证据采信 |
| R3-BST-002 | P2 | Bootstrap | `GameLauncher.cs:27`、`README.md:88` —— `DontDestroyOnLoad` 仅对根 GameObject 生效；启动器挂子对象时… | 按证据采信 |
| R3-BST-003 | P2 | Bootstrap | `Bootstrap.cs:399-409,387-393` —— `Shutdown` 在活引用 `StageList` 上索引遍历；阶段 `Shutdown` 里 `… | 按证据采信 |
| R3-CAM-001 | P2 | Camera | `Camera/ICameraManager.cs:59,97`、`XFramework.Cinemachine/Runtime/CinemachineCameraMan… | 按证据采信 |
| R3-CAM-002 | P2 | Camera | `Camera/UnityCameraManager.cs:463-469,507-512,589-599` —— 被外部销毁的相机在查询面已无效，但变更面仍当有效槽：`… | 按证据采信 |
| R3-CAM-003 | P2 | Camera | `Camera/ICameraManager.cs:28-31`、`README.md:97-99` —— 「Cinemachine 适配器返回 Brain 的输出相机（… | 按证据采信 |
| R3-CIN-001 | P1 | Cinemachine | Activate 的「强制完成混合」失活/激活 hack 不重启混合 | 需实测 |
| R3-CIN-002 | P2 | Cinemachine | `CinemachineCameraManager.cs:426-446`、`Assets/XFramework.Cinemachine/Runtime/README.md:62` —— 过渡时长未校验有限性：NaN 被上游过滤成… | 按证据采信 |
| R3-CIN-003 | P2 | Cinemachine | `CinemachineCameraManager.cs` 的槽位/反查 —— vcam 被外部销毁后 `Deactivate` 抛 `MissingReferenceE… | 按证据采信 |
| R3-CIN-004 | P2 | Cinemachine | 扩展 README/`package.json`/`Documentation/Cinemachine.md` —— 「兼容核心 0.3.0」失实：tag 0.3.0 无… | 按证据采信 |
| R3-CIN-005 | P2 | Cinemachine | `CinemachineCameraManagerTests` / `Documentation/Cinemachine.md:54` —— 声称「测试锁定」的强制 cu… | 按证据采信 |
| R3-CIN-006 | P2 | Cinemachine | 扩展 README 的「Brain 无公开完成回调」失实（`IsBlending` / `ActiveBlend` / `CameraActivated` 等可观测）；建… | 按证据采信 |
| R3-CIN-007 | P2 | Cinemachine | `ICameraManager.cs:25-32`、`Runtime/README.md` —— `OutputCamera`「与目标一致」的义务不可兑现（Brain 的… | 按证据采信 |
| R3-CIN-008 | P2 | Cinemachine | 扩展包不在核心 `check-docs`（编译/源文件参与/`.meta`/台账）、`PublicApiBaseline`、`ReadmeTableTests`、`Rea… | 按证据采信 |
| R3-CONF-001 | P0 | Config | R2 修复把行类型示例改成裸字段，照抄 CS0535 | 主会话复核 |
| R3-CONF-002 | P1 | Config | ScriptableObject Table 必须继承公开基类，但使用文档零提及 | 主会话复核 |
| R3-CONF-003 | P2 | Config | `ConfigManagerImpl.cs:106-112,202-207,283-288,394-399,425-430` —— 在途合并只按 Type+Kind 匹配… | 按证据采信 |
| R3-CONF-004 | P2 | Config | `README.md:614-618` —— 「Register 与 Preload 互相覆盖、后到者赢」与实现相反（Register→Preload 静默不加载）。 | 按证据采信 |
| R3-CONF-005 | P2 | Config | `CsvLoader.cs:211-232` —— CSV 数值解析走当前区域文化；de-DE 等小数逗号区域解析错误。建议 `InvariantCulture`。 | 按证据采信 |
| R3-CONF-006 | P2 | Config | `ConfigManagerImpl.cs:583,632` —— 未加载异常提示缺必填 `assetPath`，照抄 CS7036。 | 按证据采信 |
| R3-CONF-007 | P2 | Config | `ConfigManagerImpl.cs:249-250` vs `:327-333` —— 自定义 Table loader 返回 null 只有 NRE 文本，Gl… | 按证据采信 |
| R3-CONF-008 | P2 | Config | `ConfigManager.cs:210-215`、`README.md:154-156` —— `PreloadGlobalAsync<T>(path, defaul… | 按证据采信 |
| R3-CONF-009 | P2 | Config | `CsvLoader.cs:13-15` —— 新写的「CSV 必须包含主键列」对计算/显式实现主键不成立。 | 按证据采信 |
| R3-CONF-010 | P2 | Config | `ConfigTypeHelper.cs:32-62` —— 基类回退循环是死代码；类型实现多个 `IConfigRow<T>` 时静默取第一个。 | 按证据采信 |
| R3-CONF-011 | P2 | Config | `ConfigIndexView.cs:35-44` —— `TryGet` 失败时 `values = null`，与 `Get` 返回空数组不对称，XML 未说明。 | 按证据采信 |
| R3-DATA-001 | P2 | Data | `DataManagerImpl.cs:70-79` —— `RegisterBlock` 同实例重复注册会先 `OnClear` 清空自身数据再原样注册。 | 按证据采信 |
| R3-DATA-002 | P2 | Data | `DataManagerImpl.cs:202-221,425-465` —— 单块快照不记录 format；换默认序列化格式后旧单块快照读不回。 | 按证据采信 |
| R3-DATA-003 | P2 | Data | `DataManagerImpl.cs:70-98` vs `236-249,462-478` —— `OnClear` 异常在 `RegisterBlock`/`Rem… | 按证据采信 |
| R3-DATA-004 | P2 | Data | `IDataManager.cs:69-75`、README:283 —— `ApplySnapshot` 失败计数漏 `OnClear` 异常与 null 条目。 | 按证据采信 |
| R3-DATA-005 | P2 | Data | `IDataManager.cs:27-30`、`IDataBlock.cs:32-36` —— `RegisterBlock<T>` 静态类型键与 `BlockName… | 按证据采信 |
| R3-DIAG-001 | P2 | Diagnostics | `Diagnostics/DiagnosticsManager.cs:65,79-88` —— `Register` 读 `panel.Title` 零异常隔离；坏 ge… | 按证据采信 |
| R3-DIAG-002 | P2 | Diagnostics | `Diagnostics/DiagnosticsManager.cs:163-173`、`Editor/Diagnostics/FrameworkPanels.cs:60… | 按证据采信 |
| R3-DIAG-003 | P2 | Diagnostics | `Diagnostics/IDiagnosticPanel.cs:11-12`、`README.md:86,66` —— 「只显示一条错误提示」实际写两条（提示 + 堆栈… | 按证据采信 |
| R3-EVT-001 | P2 | Event | `Documentation/Modules/Event.md:53` —— 「不为 Event 补 PlayMode 用例」的理由仍写「无跨会话静态态」，与模块内三处静… | 按证据采信 |
| R3-EVT-002 | P2 | Event | `Event/Internal/EventStreamImpl.cs:231-232` —— `_onEmpty?.Invoke()` 无异常隔离：派发路径被误归因为 h… | 按证据采信 |
| R3-FILE-001 | P2 | File | `File/FileManager.cs:110-132` —— FILE-009 补了 Windows/Linux Server，仍漏 `OSXServer`；该平台落… | 按证据采信 |
| R3-FILE-002 | P2 | File | `File/MobileFileProvider.cs:225-233,31-64,87-110` —— 移动 Streaming 域既不归一化反斜杠、也不走 `TryN… | 按证据采信 |
| R3-FILE-003 | P2 | File | `File/IFileProvider.cs:13-17`、`README.md:90-96`、`DesktopFileProvider.cs:96-119` —— 公开… | 按证据采信 |
| R3-FILE-004 | P2 | File | `File/FileManager.cs:143-151,193-207` —— `Destroy` 后 `SetCryptoProvider`/`GetCryptoPr… | 按证据采信 |
| R3-FILE-005 | P2 | File | `File/README.md:508` —— 「11 处」固定计数与代码不符（实际 12 个 `RunOnThreadPool` 调用点）。建议删计数。 | 按证据采信 |
| R3-INP-001 | P1 | Input | 包 README/安装器缺 Input System，按文档安装编译失败 | 主会话复核 |
| R3-INP-002 | P2 | Input | `Default/InputSystemProvider.cs:260-277` —— 同一帧「按下并释放」时释放分支被 `else if` 吞掉，长按时长虚增。 | 按证据采信 |
| R3-INP-003 | P2 | Input | `Default/InputSystemProvider.cs:202-237` —— 复合绑定（WASD/2DVector/1DAxis/D-pad）的 raw 读恒为… | 按证据采信 |
| R3-INP-004 | P2 | Input | `InputManager.cs:157-184` —— 第三方 `IInputProvider.Dispose` 抛异常时 `Destroy` 留下 `_provide… | 按证据采信 |
| R3-INP-005 | P2 | Input | `Default/InputSystemProvider.cs:24,323` —— `_currentMapName` 只写不读的死字段。 | 按证据采信 |
| R3-INP-006 | P2 | Input | `Input/InputManager.cs:310-322,645-…-906` —— MSG-004 的合并句柄在 `BindToContext` 被丢弃；`Obse… | 按证据采信 |
| R3-LOC-001 | P2 | Localization | `LocalizationLanguageInfo.cs:32-36` —— `ToString` 的 XML 样例 `zh-Hans(当前, 128 条)` 与实现 `… | 按证据采信 |
| R3-LOC-002 | P2 | Localization | `LocalizationBootstrapStage.cs:116-118,104-112` —— 同一 stage 二次 `ExecuteAsync` 把 `_ini… | 按证据采信 |
| R3-LOC-003 | P2 | Localization | `LocalizationManagerImpl.cs:420-423,521-526` —— 格式说明内出现 `{`（如 `{0:0{0}`）仍抛 `FormatExc… | 按证据采信 |
| R3-LOC-004 | P2 | Localization | `LocalizationManager.cs:222-227`、`LanguageAssetLoader.cs:72` —— `LanguageAssetPath` 缺… | 按证据采信 |
| R3-LOC-005 | P2 | Localization | `LanguageAssetLoader.cs:237-245,257-271` —— JSON 原始控制字符被静默接受，构成文档未列明的第三种宽容。 | 按证据采信 |
| R3-LOC-006 | P2 | Localization | `LanguageAssetLoader.cs:166-182`、`LocalizationManagerImpl.cs:317-318` —— 空键 `""` 入库后不… | 按证据采信 |
| R3-LOC-007 | P2 | Localization | `Tests/Editor/Localization/*` —— 五处零直接断言（ToString 三态、MRU 顺序、stage 二次执行、同实例 SetInstanc… | 按证据采信 |
| R3-LOC-008 | P2 | Localization | `Tests/Editor/Localization/LocalizationSubscribeTests.cs:23-26` —— 注释仍称「仓内无 PlayMode … | 按证据采信 |
| R3-LOCK-001 | P2 | Lock | `LockManager.cs:581-592` —— `AddLock` 在派发「首次锁定」事件之后才注册销毁绑定；订阅者在回调里清空该主体锁后，主体已空闲却残留 `_… | 按证据采信 |
| R3-LOCK-002 | P2 | Lock | `LockManager.cs:930`、`LockStateSnapshot.cs:30`、`README.md:230` —— R2 审计编号 `LOCK-003/0… | 按证据采信 |
| R3-LOG-001 | P2 | Log | `Log/LogManagerImpl.cs:417-431`、`Log/Internal/LogFileSink.cs:120-139,227-231` —— LOG-… | 按证据采信 |
| R3-LOG-002 | P2 | Log | `Log/README.md:95` —— 同一条「全量捕获」bullet 先说分类覆盖适用外部日志，后说分类覆盖无法让低于全局档的外部日志通过，两句相反。建议改为「先全… | 按证据采信 |
| R3-LOG-003 | P2 | Log | `Documentation/Modules/Log.md:15` —— 文件树写死「33 个框架内置分类」，磁盘为 34（CameraManager 加入后未跟）。建议… | 按证据采信 |
| R3-LOG-004 | P2 | Log | `Log/Internal/LogFileSink.cs:9-12`、`README.md:96` —— 类 XML 仍写「每会话一文件」，LOG-001 修复后同会话 … | 按证据采信 |
| R3-MSG-001 | P2 | Message | `Documentation/Modules/Message.md:36` —— `IDestroyCancellationToken` XML 示例已在 `4e8645… | 按证据采信 |
| R3-MSG-002 | P2 | Message | `Documentation/Modules/Message.md`、`Runtime/Message/README.md:366` —— README 写「记 `待裁`… | 按证据采信 |
| R3-MSG-003 | P1 | Message | IDestroyCancellationToken XML 示例缺 IMessageSubscriber，照抄 CS1929 | 主会话复核 |
| R3-MSG-004 | P2 | Message | `Message/MessageManager.cs:705-709` —— `TryBindToDestroy` 把 `!token.CanBeCanceled` 与 … | 按证据采信 |
| R3-MSG-005 | P2 | Message | `Message/MessageBroker.cs:217-220`、`MessageBusStats.cs:40,43` —— 订阅过滤异常 `<remarks>` 仍… | 按证据采信 |
| R3-PIPE-001 | P2 | Pipeline | `Pipeline/ParallelStage.cs:39`、`SequenceStage.cs:42` —— 容器构造只校验列表 null/空，不校验元素 null；`… | 按证据采信 |
| R3-PIPE-002 | P2 | Pipeline | `Pipeline/Pipeline.cs:107-108,251-256`、`README.md:206` —— 超时只拦 0/负值/NaN；`float.Positi… | 按证据采信 |
| R3-PIPE-003 | P2 | Pipeline | `ParallelStage.cs:116-124`、`SequenceStage.cs:95-99,114-122` —— 容器的取消路径跳过 `_aggregator… | 按证据采信 |
| R3-PIPE-004 | P2 | Pipeline | `ParallelStage.cs:61,136-137`、`SequenceStage.cs:64,134-135` —— `_stageCtx` 沉降后保持引用并持有… | 按证据采信 |
| R3-POOL-001 | P2 | Pool | `Pool/Pool.cs:156-175,194-222` —— 用户回调抛出时账本已回滚，但实例被静默丢弃：不再入栈、也不走 `onDestroy`/`IPoolDi… | 按证据采信 |
| R3-RCT-001 | P2 | Reactive | `Reactive/IReactivePropertyWriter.cs:20-24`、`ReactiveProperty.cs:154-168`、`Settings/S… | 按证据采信 |
| R3-RCT-002 | P2 | Reactive | `Runtime/Reactive/README.md:242`、`Documentation/Modules/Reactive.md` —— RCT-009 的「不加主… | 按证据采信 |
| R3-SAVE-001 | P2 | Save | `SaveManagerImpl.cs` 的删除/复制/移动/玩家枚举只认载荷，不认备份；与查询面（已认备份）不一致。 | 按证据采信 |
| R3-SAVE-002 | P2 | Save | 主文件缺失 + 备份损坏时报 `Missing`，而不是 `Corrupt`。 | 按证据采信 |
| R3-SAVE-003 | P2 | Save | `GetSlotMetaAsync` 无异常隔离，解密/IO 异常直抛。 | 按证据采信 |
| R3-SAVE-004 | P2 | Save | `RecoverAsync` 文档低估「切回主线程」次数（每次读侧车都切）。 | 按证据采信 |
| R3-SAVE-005 | P2 | Save | 恢复扫描「载荷存在」分支仍不清 `.meta.tmp`。 | 按证据采信 |
| R3-SAVE-006 | P2 | Save | `RecoverAsync`「同步抛出」的表述不成立。 | 按证据采信 |
| R3-SCN-001 | P2 | Scene | `Asset/YooAssetManagerImpl.cs:730-747`、`Asset/YooAssetSceneLoadSource.cs:78`、`Tests/R… | 按证据采信 |
| R3-SCN-002 | P2 | Scene | `Scene/SceneManager.cs:73-78` —— 回收段只处理了取消，非取消异常仍让 `return handle` 不可达；与 SCN-001 同形的「… | 按证据采信 |
| R3-SCN-003 | P2 | Scene | `Scene/SceneManager.cs:67`、`Log/LogCategories.cs`、`Scene/README.md:288-292` —— `LogCa… | 按证据采信 |
| R3-SER-001 | P2 | Serialize | `NewtonsoftSerializer.cs:9`、README:53,132 —— 「Newtonsoft 支持多态」与 `TypeNameHandling.Non… | 按证据采信 |
| R3-SER-002 | P2 | Serialize | `Runtime/Serialize/README.md:74-75,83-86` —— SER-002 残留的「须/必须在模块初始化之前」两句。 | 按证据采信 |
| R3-SER-003 | P2 | Serialize | `Serializer.cs:143-156` —— `Get(null)` 抛 ANE，XML 只承诺 KNFE。 | 按证据采信 |
| R3-SER-004 | P2 | Serialize | `Tests/Runtime/Data/NewtonsoftSerializerTests.cs:33-41` —— `AutoInit_DefaultAvailable… | 按证据采信 |
| R3-SER-005 | P2 | Serialize | `ISerializer.cs:20-26` —— `Serialize` 的 `type` 形参两个内置实现均未使用，跨实现语义分叉。 | 按证据采信 |
| R3-SER-006 | P2 | Serialize | `Runtime/Serialize/README.md:62`、`Serializer.cs:178-194` —— README 把 `Default` 写成「未初始… | 按证据采信 |
| R3-SET-001 | P2 | Settings | `EncryptedSettingsStore.cs:118-131`、`SettingsManagerImpl.cs:483-516` —— 装饰器实现异步能力后管理器… | 按证据采信 |
| R3-SET-002 | P2 | Settings | `SettingsManager.cs:111-131` —— 默认路径 `Initialize<T>()` 在已用显式路径初始化时仍返回旧实例并写 `DefaultPa… | 按证据采信 |
| R3-SET-003 | P2 | Settings | `SettingsManager.cs:274-277,322-412`、`SettingsManagerImpl.cs:707-715` —— README 说主线程约… | 按证据采信 |
| R3-SET-004 | P2 | Settings | `SettingsPauseNotifier.cs:50-62,73-83` —— 引用计数是会话级静态量、无代际；会话复位后旧 Release 可串到新会话。 | 按证据采信 |
| R3-SET-005 | P2 | Settings | `JsonFileStore.cs:110-116` vs `123-135` —— `Directory.CreateDirectory` 与 `JsonUtility… | 按证据采信 |
| R3-SET-006 | P2 | Settings | `EncryptedSettingsStore.cs:160-165` vs `178-187` —— 内层文件存在但不是密文外壳时静默回默认，不告警。 | 按证据采信 |
| R3-TMR-001 | P1 | Timer | TMR-002 的补扫只在表内，跨表新建定时器仍可晚约 2.13s | 主会话复核 |
| R3-TMR-002 | P2 | Timer | `UpdateManager.Clear()` 后「CancelAll + 重建」恢复路径零覆盖。 | 按证据采信 |
| R3-TMR-003 | P2 | Timer | `Documentation/Modules/Timer.md:60` 仍写「同轴创建不受影响」，与跨表缺口（R3-TMR-001）矛盾。 | 按证据采信 |
| R3-UIA-001 | P2 | UI-A | `UIManagerImpl.cs:600,614` vs `:592,1792-1798` —— 在途打开键是请求类型，已打开去重是「具体类型 → 资源地址」两步；基类… | 按证据采信 |
| R3-UIA-002 | P2 | UI-A | `UIManager.cs:862,877,948,982` —— 重复打开已存在面板时遮罩/捕获层排序按「请求层」而非面板实际 `Layer`。建议改用 `panel.… | 按证据采信 |
| R3-UIA-003 | P2 | UI-A | `UIManagerImpl.cs:1010-1020,1200-1223,1264-1276` —— 绕过 `CloseAsync` 直接销毁面板时，其遮罩/捕获层持有… | 按证据采信 |
| R3-UIA-004 | P2 | UI-A | `UIManagerImpl.cs:821-822,544-548` —— `CloseAllAsync`/`Dispose` 对第三方 provider `Detach… | 按证据采信 |
| R3-UIA-005 | P2 | UI-A | `UIManagerImpl.cs:1990-1998,927,948,2051-2055` —— `FocusPanel` 无条件补发 OnFocus/OnResume… | 按证据采信 |
| R3-UIA-006 | P2 | UI-A | `UIManagerImpl.cs:656-662,675-682,704-710` —— 回滚路径 `pending` 清空在 `RollbackPanel` 之后；回… | 按证据采信 |
| R3-UIA-007 | P2 | UI-A | `UIManagerImpl.cs:502-517` —— Dispose 的面板循环无「仍在册才回收」认领守卫；在途打开续体可对同一面板二次 `RecyclePanel… | 按证据采信 |
| R3-UIA-008 | P2 | UI-A | `AssetPanelFactory.cs:19-49` —— 默认面板工厂全部测试零直接构造（R2 UI-C-010 补测试半未闭合）。 | 按证据采信 |
| R3-UIA-009 | P2 | UI-A | `UIManager.cs:433-443` —— 门面探测豁免源码枚举漏 `IsLayerVisible`/`IsLayerInteractive`。 | 按证据采信 |
| R3-UIA-010 | P2 | UI-A | `Runtime/UI/README.md:1333-1335` —— 称 `Initialize`/`SetInstance` 都会重接全部分档驱动器；`SetInst… | 按证据采信 |
| R3-UIB-001 | P2 | UI-B+C | `Controller/UIDefaultController.cs:13-14` —— 注释仍写「每次拦截都会打日志/带字符串插值」，与「默认静默 + 模板化」相反。 | 按证据采信 |
| R3-UIB-002 | P2 | UI-B+C | `UIHudManager.cs:207-245,271-277` —— HUD 倒序索引循环；在用户 `OnUpdate` 内 `HideHud` 其他 HUD 或 `… | 按证据采信 |
| R3-UIB-003 | P2 | UI-B+C | `UIHudManager.cs:225-227`、`View/UIHudItem.cs:119-125` —— HUD 抛异常停更后，目标销毁不再触发 `OnTarge… | 按证据采信 |
| R3-UIB-004 | P2 | UI-B+C | `View/UIViewBase.cs:205-233`、`View/UIPanelBase.cs:196-212` —— 回池钩子可重写被 XML 引导，但「必须调用 … | 按证据采信 |
| R3-UIB-005 | P2 | UI-B+C | `Data/UIBinder.cs:302-306,372-376` —— 双向绑定释放顺序在控件已销毁时泄漏属性订阅，后续写入抛进写入方。建议先解属性订阅再摘控件监听。 | 按证据采信 |
| R3-UIB-006 | P2 | UI-B+C | `Data/UIPanelBinding.cs:63-78`、`Data/ViewModelBase.cs:44-53` —— `OnUnbound` 与 `Dispos… | 按证据采信 |
| R3-UIB-007 | P2 | UI-B+C | `Tip/UITipManager.cs:122-139` —— Tip 帧循环无逐项隔离，一个 Tip 抛异常中断本轮。 | 按证据采信 |
| R3-UIB-008 | P2 | UI-B+C | `Runtime/UI/README.md:463` —— 教用户把句柄存进私有 `UIPanelBinding._bindings`，照抄不可用；正确出口是 `Trac… | 按证据采信 |
| R3-UIB-009 | P2 | UI-B+C | `Data/UIPanelBinding.cs:156-173` —— `img_` 类型不匹配无编辑器告警，与 README「未找到会告警」相反。 | 按证据采信 |
| R3-UIB-010 | P2 | UI-B+C | `IUIHudProvider.cs:16`、`Tip/IUITipProvider.cs:14` —— 自定义 provider 经 `SetTipProvider`/… | 按证据采信 |
| R3-UIB-011 | P2 | UI-B+C | `View/UIHudItem.cs:127-132` —— 无主相机时 HUD 每帧重查 `Camera.main`。建议降频/一次性告警停试。 | 按证据采信 |
| R3-UIB-012 | P2 | UI-B+C | `Data/UIBinder.cs:24-96`、`Tip/UITipManager.cs` —— 四个公开绑定方法零直接覆盖；真实 `UITipManagerImpl.… | 按证据采信 |
| R3-UIB-013 | P2 | UI-B+C | `Data/UIPanelBinding.cs:243-278` —— 面板内同名约定节点只绑到其中一个，另一个永久未绑且无告警。 | 按证据采信 |
| R3-UPD-001 | P2 | Update | `Runtime/Update/README.md:323-326`、`UpdateManager.cs:362-366` —— Clear 恢复指引把 Audio 写成… | 按证据采信 |
| R3-UPD-002 | P2 | Update | `UpdateScheduler.cs:1245-1264` —— `FlushPending` 注释仍写「回调里可能调用 Clear 清空本缓冲」，与 UPD-002 … | 按证据采信 |
| R3-UPD-003 | P2 | Update | `Documentation/Modules/Update.md:39` —— 仍写「四个消费方」固定计数。 | 按证据采信 |
| R3-UPD-004 | P2 | Update | `UpdateManager.cs:720-743`、README:298-312 —— 对已 Disable/缓冲中的节点调 `ProcessImmediate` 静默… | 按证据采信 |

> 共 139 条。
