# 第二轮全面审计（2026-10-17）

> **点快照**：本文件记录第二轮全面审计在边界提交 `2d584b6b803593010f5548afe7b03f75df8a7ab5` 上的发现、证据与实施建议。所有 `file:line` 以该提交为准，不承诺长期有效。
> **收件人**：维护者（执行本轮修复时）+ 下一轮审计者。长期结论按收件人分流到模块 README 与决策台账，本文件不复制它们，只做发现与索引（见 §10）。
> **审计轮只出清单，不动代码**。
> 审计日期：2026-10-17。判据与探针定义见该模块的维护向记录与框架级审计手册；本文件不重复 18 类判据的全文。

## 0. 元信息与边界

- 边界 SHA：`2d584b6b803593010f5548afe7b03f75df8a7ab5`（审计开始工作区干净）。
- 深度分档：未审模块与 UI 整模块做全量 A–F；2026-10-02 后有行为变更的已审模块做增量复审；Lock 只复核开口项；Bootstrap 跳过（2026-10-17 刚完成重新审计）。
- 基线门禁：审计开始前 `Tools/check-docs.ps1 -Enforce` 通过（包内文档告警十一码全 0、源文件全部参与编译、台账结构合规、模块 README 文件树与磁盘一致）。
- 本轮**未做**：未运行 Unity 测试/构建/真机；未执行需跨播放会话的手工验收；未审计扩展包内部（只审核心与之接缝）；未修改任何源码或包内 README。
- 证据等级：`实测` = 本轮实际跑过探针/读第三方源码可证；`推理` = 调用链/静态状态可证但未运行；`需实测` = 运行期或真机才能确认，已给命令。

## 1. 范围与方法

| 深度 | 模块 | 说明 |
|---|---|---|
| 全量 A–F | Event、Serialize、Scene、Pipeline、Pool、Diagnostics、Camera、Timer、Reactive、Message、Audio、Log、Settings | 此前从未做过 A–F；18 类判据逐类留痕 |
| 全量 A–F（切片） | UI | 仅做过门面定向；按门面/导航/生命周期、View/Data/HUD/Tip、排序/弹窗定位/遮罩/控制器切三片 |
| 增量复审 | Asset、Save、Data、File、Update、Config、Localization、Input | 13 类失效矩阵 × 2026-10-02 后变更提交；旧结论与开口项逐条复核 |
| 开口项复核 | Lock | 期间零行为提交，只复核台账开口项 |
| 跳过 | Bootstrap | 2026-10-17 重新审计完成；审计期间无新提交 |

方法：每个模块读全部 `.cs`、模块 README、维护向记录（文件结构/沿革/决策台账/审计轮次/实测记录/边界）、CHANGELOG 与测试；用审计手册的探针定位；对旧结论逐条给「仍成立 / 已失效 / 部分失效 / 证据不足」；第三方对照按模块选取。P0/P1 全部经主会话回读源码复核；P2 按报告证据采信。

## 2. 总览

> 快照统计（只作导航；不随开发增长更新）：**1 条 P0、29 条 P1、224 条 P2、4 条待取证**。

### 2.1 最需要先读的 10 条

1. **CONF-001（P0）**：README 行类型示例用自动属性，Json/SO 路径主键恒 0——按默认文档建表必然错。
2. **AST-001（P1）**：资产加载族取消不释放 YooAsset 句柄，反复取消无界增长。
3. **UIA-001（P1）**：以基类 `T` 打开的面板无法关闭/导航，随包示例正是该写法。
4. **SET-001（P1）**：加密装饰器遮蔽内层 `IAsyncSettingsStore`，平台存档被静默绕过。
5. **MSG-001（P1）**：首条 `Publish` 时同步订阅清零会让该条消息永久失去可重放性。
6. **TMR-002/003（P1）**：回调内新建定时器可晚一个档周期；新建定时器可提前一个档周期触发。
7. **FILE-002（P1）**：移动端 Streaming 异步读在池线程触碰 Unity API，与「任意线程」契约相反。
8. **UIA-002（P1）**：`UpdateManager.Clear()` 后 README 的恢复路径不成立，分档面板永久静止。
9. **SCN-001（P1）**：场景加载成功后的回收段受调用方令牌约束，取消后句柄丢失。
10. **INP-001（P1）**：长按时长只为已被其它读取 API 缓存的动作计时，README 蓄力条示例恒 0。

### 2.2 P1 分布（按模块）

| 模块 | P1 |
|---|---|
| Settings | SET-001/002/003/004 |
| Timer | TMR-001/002/003 |
| UI | UIA-001/002、UI-B-001/002、UI-C-001 |
| Asset | AST-001/002 |
| Message | MSG-001/002 |
| File | FILE-001/002 |
| Log | LOG-001/002 |
| Pool / Scene / Reactive / Audio / Diagnostics / Data / Config / Input / Lock | 各 1 条（POOL-002 / SCN-001 / RCT-001 / AUD-001 / DIAG-001 / DATA-001 / CONF-002 / INP-001 / LOCK-001） |

### 2.3 跨模块主题索引

- §6.1 会话级复位族不完整
- §6.2 随包文档示例与 README 模板句
- §6.3 线程契约只在散文里
- §6.4 取消与失败路径的不对称
- §6.5 可选能力接口与装饰器语义
- §6.6 测试假绿与真实缝覆盖不足
- §6.7 键空间与相等语义
- §6.8 公开 API 纪律（重载源兼容/收窄/迁移表）
- §6.9 载体与多观测面

### 2.4 本轮明确未做

- 未运行 Unity 测试/构建/真机；所有「需实测/待取证」项在附录 B 与各模块报告中给了探针命令。
- 未修改任何源码或包内 README；模块 README 四节的更新留给实施轮（报告只给建议落点）。
- 未审计 Cinemachine 扩展包内部（只审与核心 Camera 的接缝）。
- 未重新论证框架级 `不做/暂不做` 清单（§9 逐条复核均维持）。

## 3. P0 清单

### CONF-001 · P0 · A1/D4/C2：README/XML 行类型示例用自动属性，Json/SO 路径主键恒 0

- **位置**：`Runtime/Config/README.md:27`（快速使用）、`:299`（方式一）、`:518`（struct vs class 表）；`Runtime/Config/ConfigManager.cs:29`（门面 XML 示例）；`Runtime/Config/JsonLoader.cs:22`（`JsonUtility.FromJson<ListWrapper<T>>`）；`Runtime/Config/ScriptableObjectLoader.cs:19-34`。
- **发现**：三处行类型示例把主键写成自动属性 `public int Id { get; set; }`。Json 路径用 `JsonUtility`、SO 路径用 Unity 序列化，二者都**不序列化属性** → `Id` 恒为 `default`：多行 JSON 撞 `Duplicate Id '0'` 加载失败，单行静默产出键 0 的表（`.Get(1001)` 报 not found）；SO 路径的 Inspector 里 `Id` 根本无处填写。CSV 走反射写属性，恰好不受影响，进一步掩盖问题。仓内自证：`Runtime/Settings/README.md:54` 与 `CHANGELOG.md:755` 都写明「属性不会被 JsonUtility 序列化」。
- **复核**：主会话已核对 README 示例、`JsonLoader`/`ScriptableObjectLoader` 与仓内自证；按本轮 P0 定义「按默认文档写法必然得到错误结果」升级。建议补一条 `JsonUtility.FromJson` 探针（属性版 `Id==0`、字段版 `Id==1001`）坐实。
- **建议**：三处示例的 `Id` 改为字段；README `已知限制/行为契约` 写「Json/ScriptableObject 行类型只能用字段，属性不会被写入；CSV 不受此限」；XML 同步。
- **验证**：EditMode 探针 `JsonUtility.FromJson<Row>("{\"Id\":1001}")` 断言 `Id==0`；负向控制用字段版断言 1001。

## 4. P1 清单

### POOL-002 · P1 · B5/B6：池的静态状态跨会话存活且无会话复位

- **位置**：`Runtime/Pool/PoolManager.cs:33-41`（静态 `_pools`/`_configs` + 仅 `Application.quitting` 订阅）、`:178-184`（池已存在即忽略 `Configure`）；`Runtime/Pool/CollectionPool/ListPool.cs:22-25`（静态 `_pool` + 建池闭包）、`:78-87`（`Configure` 守卫/重建）；`Tests/Runtime/Architecture/AutoInitTests.cs:56-77`（族清单无 Pool）。
- **发现**：关闭域重载时，第二播放会话继承上一会话的池实例、配置与生成器闭包；`Configure` 被静默忽略（有启动早期 Warning），`MaxSize`/`PrewarmSize` 失效；生成器闭包可能捕获上一会话已销毁对象；上一会话未归还实例会让集合池 `Configure` 永久被拒。
- **复核**：主会话已按代码链复核；跨会话实际表现需手工两遍 PlayMode（关闭域重载）。
- **建议**：评估补 `PoolManager.ResetForNewSession()`（`SubsystemRegistration` 清 `_pools`/`_configs`，只丢引用不 Dispose）并到族清单登记；集合池需内部重置入口。若有意跨会话保留，必须在模块 README `已知限制` 写明三条后果。
- **验证**：手工两遍 PlayMode：第一遍 `Configure<Foo>(MaxSize=1, Prewarm=2)` + `Get`；第二遍 `Configure<Foo>` 应命中 Warning、`GetPool<Foo>()` 仍是旧池。`run-tests.ps1` 不适用（每次全新进程）。
- **落点**：模块台账 `待裁`（复位 vs 明示保留）+ 跨模块主题「会话复位族完整性」。

### DIAG-001 · P1 · A1/B5：诊断页签「幂等登记」承诺与实现相反

- **位置**：`Runtime/Diagnostics/DiagnosticsManager.cs:20-22`（XML 自称 `[RuntimeInitializeOnLoadMethod]` 重跑「重复登记被幂等吸收」）、`:67-75`（去重只按同一实例 `ReferenceEquals`）、`:77-86`（不同实例同标题两者都保留）；`Samples~/Example/DiagnosticsSample.cs:41-50`（每次 `new GameDiagnosticPanel()`）；`_panels` 为静态字段（`:32`），设计上不复位（`:15-23`）。
- **发现**：按文档写法（`RuntimeInitializeOnLoadMethod` + `new` 实例）在关闭域重载下每进一次播放多一个同名页签；标题重复告警每会话递增。管理器自身 XML 与示例都做了错误承诺。
- **复核**：主会话已按代码链复核通过，范围比代理报告更广（管理器 XML 也错）。
- **建议**：二选一——(a) 改文档承诺（示例/快速使用改为静态只读实例 + 登记该实例，或示范退出 `Unregister`；XML 改为「按实例幂等」）；(b) 改去重语义为按类型/标题（行为变更，需用户裁定）。
- **验证**：关闭域重载连续进出播放两次，诊断窗口应只有一个页签；文档修复无自动化红证。
- **落点**：模块 README/示例/Samples（report-only）；若选 (b) 进模块台账 `待裁`。

### SCN-001 · P1 · B3/D4/A1：加载成功后的回收阶段仍受调用方令牌约束，取消丢句柄

- **位置**：`Runtime/Scene/SceneManager.cs:57-70`（加载与回收共用调用方令牌；OCE 时 `return handle` 不可达）、`:33-35`（XML 只承诺「取消时不执行 Activate/UnloadUnused」）；README 示例 A `:106-109` 正是 `unloadUnusedAfterLoad: true` + `destroyCancellationToken`；后端 `Runtime/Asset/YooAssetManagerImpl.cs:161-170`（回收观察令牌且跨帧）。
- **发现**：加载成功 → 进入跨帧回收 → 令牌取消 → OCE 抛出、句柄从未交给调用方；Additive 场景留在 Hierarchy 且外部无卸载凭据，Single 模式调用方误以为加载失败。
- **复核**：主会话已复核调用链；触发窗口需实测（回收跨帧）。
- **建议**：回收调用改用 `CancellationToken.None`，并在 XML/README 写明「加载成功后的回收阶段不响应取消」；或先交出句柄再 fire-and-forget（更大改动，另议）。
- **验证**：扩展 `FakeSceneAssetManager.UnloadUnusedAssetsAsync` 观察令牌后写取消用例；改前必红（取消后拿不到句柄）。
- **落点**：模块 README `已知限制` + XML（修复后）。

### MSG-001 · P1 · B5/B2：首条 `Publish` 时同步订阅清零导致通道在缓冲写入前被回收

- **位置**：`Runtime/Message/MessageBroker.cs:79-88`（`Sync?.Emit` 后 `GetOrCreateBuffered().Emit`）、`:91-113`/`:134-140`/`:156-161`（键值与 `PublishAsync` 同形）、`:1050-1054`（`ReclaimChannelIfEmpty`）；`Runtime/Message/Internal/MessageChannel.cs:103-106`（首次发布前 `_buffered == null` 即可回收）；`Runtime/Event/Internal/EventStreamImpl.cs:213-232`（订阅数归零时锁外回调 `_onEmpty`）。
- **发现**：某类型首条 `Publish` 时，最后一个同步订阅者在回调里自退订 → `onEmpty` 判定 `IsReclaimable` 为真 → 通道从 `_channels` 摘除 → 随后的缓冲写入落在孤儿通道上；该条消息永久失去可重放性，静默且统计面无痕。键值/异步路径同形。
- **复核**：主会话已按代码链复核（与 Event 审计的 EVT-007 合并为同一条）。
- **建议**：给 `MessageChannel` 加「派发中不回收」保护，或 `Publish` 在缓冲写入前复核通道仍在表内；不改公开面。
- **验证**：一次性同步订阅（收到即退订）→ `Publish` → `SubscribeBuffered`，断言收到重放（改前空）。

### MSG-002 · P1 · A1：`IDestroyCancellationToken` 的 XML 示范写法在销毁后抛 `ObjectDisposedException`

- **位置**：`Runtime/Message/IDestroyCancellationToken.cs:17-27`（示例 `=> _cts.Token`，`Dispose` 里 `_cts.Dispose()`）；`MessageManager.cs:718-745` 的 `TryBindToDestroy` 每次订阅读该 getter；台账错记在 `Documentation/Modules/Localization.md:39`。
- **发现**：照抄示例 = 对象销毁后再订阅抛 ODE，异常点远离销毁时刻；README 的 `PlayerModel` 示例只 `Cancel()` 不 `Dispose()`（正确），错的是随包 XML。
- **复核**：主会话已读示例与 `TryBindToDestroy` 复核。
- **建议**：示例改为构造时缓存 `_token = _cts.Token`；台账条目迁回 Message。
- **验证**：实现该接口并 `Dispose` 后再 `Subscribe`，断言不抛（改前 ODE）。

### RCT-001 · P1 · B2/B5：链式 `Select` 的中间派生值不可达、无法释放

- **位置**：`Runtime/Reactive/ReadOnlyReactiveProperty.cs:59-74`（源订阅句柄存在**返回值**上）、`:127-134`（`Dispose` 只拆自己那条）；`README.md:42`（把链式当卖点）、`:54-58`（只警告「就地丢弃返回值」）。
- **发现**：`d2 = d1.Select(g)` 后 d1 不在任何可达引用上；`d2.Dispose()` 只退订 d2→d1，`d1→root` 的订阅与 selector 活到 root 被 Dispose，且每次源变化继续跑中间 selector。长寿命属性上的订阅/CPU/GC 泄漏。
- **复核**：主会话已按代码链复核。
- **建议**：README `已知限制` + 台账 `待裁`（是否给派生值增加「拥有上游」的显式构造/组合入口；不建议 Dispose 级联，会误杀共享下游）。
- **验证**：`root.Select(f).Select(g).Dispose(); root.Value=1;` 断言中间 selector 不再运行且 `root.SubscriptionCount == 0`（现状 1）。

### TMR-001 · P1 · A1：`After<TState>` 示例参数顺序写反，照抄 CS1503

- **位置**：`Runtime/Timer/TimerManager.cs:34`（XML 示例）、`Runtime/Timer/README.md:118-119`；真实签名 `TimerManager.cs:93`；正确写法 `Samples~/Example/TimerSample.cs:47`。
- **发现**：示例写 `After(1.5f, this, static self => ..., ...)`，`this` 落在 `Action<TState>`、lambda 落在 `TState`，两个重载都不匹配。自 2026-10-01 建模块起存在，XML 随包发布。
- **复核**：主会话已核对签名。
- **建议**：两处改为 `After(1.5f, static self => ..., this, cancellationToken: ...)`。
- **验证**：编译该片段（改前 CS1503）。

### TMR-002 · P1 · C2：回调内新建定时器落在已走访槽位，不进入本趟 `min`

- **位置**：`Runtime/Timer/TimerTable.cs:226-268`（只把本轮访问到的槽位计入 `min`）、`:410-414`（自由链 LIFO 复用下标）、`:479-482`（一次性先释放再回调）；`TimerCore.cs:284-305`；`TimerTicker.cs:83-84`（文档称「同轴不受影响」）。
- **发现**：一次性回调里新建的定时器复用刚释放的下标，而 `Sweep` 循环已越过它 → 本趟 `min` 漏算 → `SelectTier` 保持粗档；同轴新建的 0.1s 定时器可晚约 2.13s 触发，直接推翻两处成文承诺。
- **复核**：主会话已按代码链复核。
- **建议**：扫尾收尾时按真实存活槽位重算最近截止，或把升档请求排到下一次 Tick 开头。
- **验证**：`After(60f)` + `After(0.1f, () => After(0.1f, ...))`，推进 0.33s 断言触发（现状需等到 Tier7 相位）。

### TMR-003 · P1 · C2：`Create`/`Restart` 锚点取陈旧逻辑时刻，粗档下可提前触发

- **位置**：`Runtime/Timer/TimerCore.cs:124-125,156-157`（`Allocate(..., _logicalNow[axis])`）、`:284-287`（`_logicalNow` 只在派发时推进）；`TimerTable.cs:426-428`；`TimerTicker.cs:102-106`。
- **发现**：两次派发之间创建短定时器时锚点落在上一次派发时刻，截止可能已在过去；下一次派发一次性跳过时间 → 提前最多一个档位周期触发（60s 定时器存在时 `After(1f)` 可约 40ms 后触发）。违反「delay 秒后回调」与 CHANGELOG「锚点落在 Create 那一刻」。
- **复核**：主会话已按代码链复核。
- **建议**：短期 README `已知限制`；干净修法需 Update 提供当前轴时刻查询面（跨模块，`待裁`）。
- **验证**：`After(60f)` + 推进后 `After(1f)`，50ms 内断言未触发（现状已触发）。

### LOG-001 · P1 · B3/C2/B5：同会话重建实现截断同名 JSONL 且 `seq` 重计

- **位置**：`Runtime/Log/Internal/LogFileSink.cs:52-55`（基名 = 会话起始时刻 + 会话 id）、`:172`（`FileMode.Create`）；`Runtime/Log/LogManager.cs:120-134`（`Configure`）、`:140-150`（`Shutdown`）；`LogSession.Reset()` 唯一调用点 `LogManager.cs:537`（AutoInit）；`LogManagerImpl.cs:29,97`（`_sequence` 为实例字段）。
- **发现**：同一次运行内 `Configure`/`Initialize`/`Shutdown` 后再用会以同一文件名截断本会话已写 JSONL，启动段丢失；`seq` 从 1 重计，违背 README「会话内单调」。默认 Editor/Development 开文件 sink。
- **复核**：主会话已按代码链复核。
- **建议**：基名改用 sink 创建时刻或进程内序号；`seq` 提到会话级；或文档明写「Configure 必须在首条日志前」。
- **验证**：`ConfigureFileSink → Info("before") → ConfigureFileSink → Info("after")`，断言同一文件同时存在两条且 `seq(after) > seq(before)`（现状只剩 after）。

### LOG-002 · P1 · B3/D4：自定义 sink 在 `Write` 内调 `Debug.Log*` 同步重入捕获 → StackOverflow

- **位置**：`Runtime/Log/LogManagerImpl.cs:455-474`（`DispatchCaptured` 只跳过内置 `ConsoleLogSink`）；`Runtime/Log/Internal/UnityLogCapture.cs:37-51`（调用线程同步回调，只查内置 echo 标志）；`README.md:90,208,214`（「sink 内再写日志是安全的（可重入）」）。
- **发现**：自定义 sink 在 `Write` 里调 `Debug.Log*` → 同步捕获 → 同一 sink 再 `Write` → 无限递归，StackOverflow 不可捕获；文档未区分「经 LogManager 再写」与「经 Debug.Log 再写」。需自定义 sink 才触发，但这是文档明确鼓励的扩展点。
- **复核**：主会话已按代码链复核。
- **建议**：捕获路径加 `[ThreadStatic]` 重入闩锁（零分配零锁）；README 区分两种重入并写明前者安全、后者会被丢弃。
- **验证**：自定义 sink 内 `Debug.Log`，断言该条目只出现一次且进程不崩（改前栈溢出）。

### DATA-001 · P1 · A1/D3：`IDataBlock` 的 XML 示例照抄编译不过（缺 `DataVersion` / `OnMigrate`）

- **位置**：`Runtime/Data/IDataBlock.cs:10-27`（`<example>` 只实现 `BlockName/OnSave/OnLoad/OnClear`）；接口 `:43` 要求 `DataVersion`、`:59` 要求 `OnMigrate`（无默认实现）；同一模块 README 的快速使用 `:44-82` 是完整示例。
- **发现**：照抄随包 XML 示例得到 CS0535；XML 与 README 自相矛盾。自 `13cf0c0` 引入迁移链起存在，2026-09-30 的 A–F 审计漏掉。
- **复核**：主会话已读接口与示例复核。
- **建议**：XML 示例补 `DataVersion => 0` 与恒等 `OnMigrate`，或改为引用 README 的完整示例。
- **验证**：把示例粘进临时程序集编译（改前 CS0535）。

### AST-001 · P1 · B3/B5/D4：资产加载族取消不释放底层句柄

- **位置**：`Runtime/Asset/YooAssetManagerImpl.cs:487-497`（`LoadAsync`）、`:352-364`（`LoadSubAssetsAsync`）、`:393-405`（`LoadRawFileAsync`）；对照场景路径 `:531-535`（`SettleCancelledSceneLoad`）；YooAsset 2.3.18 `ProviderOperation.cs:244-247`（`CreateHandle` 即 `RefCount++`）、`:227`（`CanDestroyProvider` 要求 `RefCount <= 0`）。
- **发现**：三条资产加载路径在取消（OCE）时把已 `RefCount++` 的 YooAsset 句柄丢掉，无人 `Release` → provider 与 bundle 引用永久驻留，`UnloadUnusedAssetsAsync` 再也回收不掉。场景族有取消善后、资产族没有——同一「取消支持」承诺下的不对称；Audio/Localization/UI 预载等默认路径会传令牌。README:497「取消后返回的句柄需自行 Dispose」是反话（取消根本不返回句柄）。
- **复核**：主会话已核对代码链与 YooAsset 源码。
- **建议**：三条路径 catch OCE → `operation.Release()` → 重抛；订正 README。
- **验证**：真 YooAsset Offline 最小包（或抽 `IAssetLoadSource` 缝）：取消后 `UnloadUnusedAssetsAsync` 断言 provider 不增长（改前 +1）。

### AST-002 · P1 · A1：README「加载模式选择」表仍写「下载 → 激活」

- **位置**：`Runtime/Asset/README.md:417`；同文件 `:151-157`、`:478-488`。
- **发现**：决策表写「版本 → 预检 → 下载 → 激活」，与同文件 §7/状态流转/已知限制 12 的「激活必须在下载之前」相反；`39a0aa8` 修三处时漏了这第四处。照表做 = 预下载静默空转。
- **复核**：主会话已对照文档原文。
- **建议**：改为「版本 → 预检 → 激活 → 下载」，并 grep 全文确认无第四处。
- **验证**：`grep -n "预检 → 下载\|预检 → 激活" Runtime/Asset/README.md` 应为零命中。

### FILE-001 · P1 · F/B3/A2：`ICryptoProvider.Decrypt` 的 `null` 契约被装饰器原样透出

- **位置**：`Runtime/File/ICryptoProvider.cs:21`（「解密失败应返回 null」）；`Runtime/File/CryptoFileProvider.cs:118-135`（文本面 `Encoding.UTF8.GetString(_crypto.Decrypt(bytes))`、字节面直接返回 null）；`IFileProvider` 契约「文件不存在 → 读方法返回 null」；Save 侧把 null 当「不存在或解密失败」报 `Missing`。
- **发现**：同一解密失败在 `ReadAllBytesAsync` 返回 `null`（=文件不存在）、`ReadAllTextAsync` 抛 `ArgumentNullException`（既非 IOException 也不在失败契约表里）。按 XML 实现第三方 AES provider 即踩中；Save 据此把 `Corrupt` 报成 `Missing`。内置 XOR 永不返回 null，默认路径不触发。
- **复核**：主会话已核对代码与契约。
- **建议**：装饰器把 null 转成 `IOException("解密失败")`（推荐），或改接口契约禁止返回 null（需裁定）；补返回 null 的假 provider 用例。
- **验证**：注入 `NullDecryptCryptoProvider`，断言两个读面对同一密文给出同一种结果（改前不一致）。

### FILE-002 · P1 · B4/C2：移动端 Streaming 异步读写不满足「任意线程」契约

- **位置**：`Runtime/File/README.md:464-470,483`（域根缓存后「文件原语可从任意线程调用」）；`Runtime/File/MobileFileProvider.cs:225-233`（每次读 `Application.streamingAssetsPath`）、`:157-217`（构造 `UnityWebRequest`）；对照 `DesktopFileProvider` 的 `PrimeRoots` 与 CHANGELOG:1221 的同类实测。
- **发现**：移动端池线程调用 `ReadAllTextAsync(FileDomain.Streaming, ...)` / `ExistsAsync` 会在 Unity API 处抛 `UnityException`；桌面/移动非 Streaming 域不受影响。README 的「调用方无需关心自己身处哪个线程」与推荐异步形态相反。
- **复核**：主会话已核对代码路径。
- **建议**：把移动端 Streaming 异步入口纳入「必须主线程」成文契约，或缓存 URL 并把 `UnityWebRequest` 构造切回主线程。
- **验证**：真机 dev build 中池线程调用 Streaming 读，观察 `UnityException`（改文档后仍会抛，但不再与承诺冲突）。

### CONF-002 · P1 · A1：README 声称「没有 internal 类型」，实际 10 个 internal 声明

- **位置**：`Runtime/Config/README.md:68`；实际声明见 `ConfigManagerImpl.cs:15`、`JsonLoader.cs:13`、`CsvLoader.cs:16`、`ScriptableObjectLoader.cs:15`、`ConfigTypeHelper.cs:11`、`ConfigTextLoader.cs:10`、`ConfigLoadHelper.cs:11,83,106`。
- **发现**：README 称「十九个文件都是公开面，本模块没有 `internal` 类型」，实际 10 个 internal 类型分布在 7 个文件。照 README 判断公开面会 CS0122；也违反 README 文件树的 internal 标注约定（`check-docs` 只比文件名，抓不到）。
- **复核**：主会话已 grep 源码复核。
- **建议**：删掉计数与「没有 internal」句，逐文件标注 internal。
- **验证**：人工核对；可补一条「README 文件树 internal 集合 = 磁盘含 internal 类型的文件集合」的守卫。

### LOCK-001 · P1 · B3/F：解锁回调内重加同类型锁被随后的清理静默丢弃

- **位置**：`Runtime/Lock/LockManager.cs:610-638`（`RemoveLock` 先派发、后摘除）；对照 `:654-673`（`RemoveAllLocks` 先摘除、后派发，XML 明写回调内加锁安全）。
- **发现**：最后一把锁移除后，`RemoveLock` 先派发 `OnGlobalUnlocked`/`OnUnlocked`，再 `typeDict.Remove(lockType)`/`_locks.Remove(subjectKey)`。回调里对同一 (主体, 类型) `AddLock` 会取到尚未摘除的空集合、加锁成功并播报 `OnGlobalLocked`，返回后整个集合被删——新锁无解锁事件地消失，`IsHeld`/`IsLocked` 为 false，聚合订阅镜像可能停在 `true`。同模块 `RemoveAllLocks` 处置相反且承诺「回调里再加锁不会打坏本轮清理」。
- **复核**：主会话已按代码链复核。
- **建议**：照 `RemoveAllLocks` 把摘除移到派发之前；`TryUnbindDestroyBinding` 可在派发后按现有条件执行。
- **验证**：`OnUnlocked` 回调里 `AddLock` 同类型，`Dispose` 后断言 `IsLocked` 为 true（现状 false）；负向控制用 `RemoveAllLocks` 同形状应绿。

### INP-001 · P1 · A2/F/D3：长按时长只为「已被其它读取 API 访问过」的动作计时

- **位置**：`Runtime/Input/Default/InputSystemProvider.cs:230-237`（`GetButtonPressDuration` 只读 `_buttonPressStartTimes`）、`:242-259`（`UpdateButtonPressDurations` 只遍历 `_actionCache`）、`:780-820`（`GetAction` 是 `_actionCache` 唯一写入点）；`Runtime/Input/InputManager.cs:689-708`（`ObservePressDuration` 只调 `GetButtonPressDuration`）；`Runtime/Input/README.md:232`（蓄力条示例单独使用它）。
- **发现**：从未被 `WasPressedThisFrame`/`IsPressed`/`Read*` 等读取 API 访问过的动作永远不会进入 `_actionCache`，`UpdateButtonPressDurations` 不会为它计时，`GetButtonPressDuration`/`ObservePressDuration` 恒返回 0，无异常、无日志；`InvalidateCaches`（切 map/启停 map）后仅靠 `ObservePressDuration` 的项目会永久失去计时。`FakeInputProvider` 直接返回 Duration，掩盖了真实 provider 的计时路径。存量缺陷自 2026-05 起；上轮「零高危」由此部分失效。
- **复核**：主会话已按代码链复核；需 PlayMode 红证（未读过动作 → Tick → 时长为 0；读过一次后重按 → >0）。
- **建议**：`GetButtonPressDuration` 开头 `GetAction(action)` 预热（或让计时遍历独立于读取缓存）；在 `IInputProvider` XML 与 README 写明保证。
- **验证**：`InputSystemProviderTests` 新增用例，改前必红。

### UIA-001 · P1 · F/E/A2：以基类 `T` 打开的面板在关闭/导航/查询路径失效

- **位置**：`Runtime/UI/UIManagerImpl.cs:566-580`（打开键 = `typeof(T)`）、`:1716`（`_activePanels[type]`）、`:711-723`（`CloseAsync` 键 = `panel.GetType()`）、`:741-758`（`CloseLayerAsync` 反而用字典键 `kv.Key`）、`:1386-1396`（`BringToFront` 用 `panel.GetType()`）；`Samples~/Example/UISample.cs:67,74`。
- **发现**：打开时按泛型实参 `typeof(T)` 记账，关闭/导航/置顶却按实例运行期类型 `GetType()` 反查。`T` 是基类时（随包示例正是 `OpenAsync<UIPanelBase>`）第二次打开会命中原面板并 Refocus、`CloseAsync(panel)` 静默不关、`Pop*` 静默失效，只有 `BringToFront` 一条误导告警。仓内同类先例（XPool `Return`）已按 `GetType()` 回落修复。
- **复核**：主会话已按代码链复核。
- **建议**：按实例反查 `_activePanels`（`PruneActivePanels` 已有同形扫描），或文档限定 `T` 必须是实例具体类型并修正示例；至少做一件并加测试。
- **验证**：以基类 T 打开的第二个面板应真正打开、可自关、可 Pop（改前均失败）；负向控制用具体类型。

### UIA-002 · P1 · A1/D4/F：`UpdateManager.Clear()` 后 README 的恢复路径不成立

- **位置**：`Runtime/UI/UIManager.cs:81-110`（`Initialize` 早退在 `EnsureFrameDriverRegistered` 之前）、`:123-137`（只注册每帧驱动器）、`:160-196`（`SetInstance`）；`Runtime/UI/UIManagerImpl.cs:1549-1563`（档位需求只在边沿回调）；`Runtime/UI/README.md:1323-1325`。
- **发现**：`UpdateManager.Clear()` 摘掉驱动器后，README 说下一次 `Initialize`/`SetInstance` 会重新注册。实际：`Initialize` 因 `_instanceInitialized` 早退，连每帧驱动器也不重注册；`SetInstance` 只恢复每帧驱动器，Tier1–7 面板因 `_tierDemand` 边沿不重发而永久静止；`TierDriverCount` 仍报门面记账旧值。唯一完整恢复路径是 `Destroy()` + `Initialize()`，README 未写。
- **复核**：主会话已按代码链复核。
- **建议**：把 `EnsureFrameDriverRegistered` 提到早退前，并让它重注册现存 `_tierDrivers`；或把 README 改为「只有 Destroy + Initialize 能完整恢复」。
- **验证**：Tier2 面板 → `UpdateManager.Clear()` → 按 README 恢复 → `Tick`，断言面板被驱动且 `GetCount(Tier2)==1`（改前失败）。

### UI-B-001 · P1 · B3：HUD 每帧派发无逐实例异常隔离

- **位置**：`Runtime/UI/UIHudManager.cs:207-224`（循环直接 `hud.OnUpdate`，无 try/catch）；对照 `Runtime/UI/UIManagerImpl.cs:1491-1519`（面板侧逐面板隔离 + `UpdateFaulted`）；外层 `UIManagerImpl.cs:1422-1444` 的 `UpdateHudProvider` 只保住整条 UI 通路并阻尼日志。
- **发现**：一个 HUD 的 `OnUpdate` 抛异常会中断本轮 HUD 遍历，其余 HUD 本帧不被驱动；异常被外层捕获、只记首条日志，症状是「其他 HUD 静默停更」。面板侧已有正确先例。
- **复核**：主会话已按代码链复核。
- **建议**：照面板侧加逐 HUD try/catch + 停更标记（回池复位），或重构为统一派发器。
- **验证**：两个 HUD，第一个 `OnUpdate` 抛异常，断言第二个仍被驱动（改前停更）。

### UI-B-002 · P1 · A1：README 的「先 Destroy 再自行驱动」逃生口不可行

- **位置**：`Runtime/UI/README.md:1216`；`Runtime/UI/UIManager.cs:741-759`（`Update` 先 `EnsureGlobalInitialized`）。
- **发现**：README 说暂停时可「停掉手动调用，或先 `Destroy` 再自行驱动」；`Destroy` 后 `_instance` 为空/未初始化，`UIManager.Update` 直接抛「尚未初始化」。可行替代是 `UpdateManager.Clear()`（README:1323-1325 另行记录）或接受双倍步进。
- **复核**：主会话已按代码链复核。
- **建议**：改为「用 `UpdateManager.Clear()` 摘驱动器后自行调用」，或提供只注销驱动器的公开入口（公开 API 变更需裁定）。
- **验证**：按 README 做（Destroy → Update）应抛；按修订后做法应正常。

### UI-C-001 · P1 · C2/A1：弹窗尺寸单位错配（Canvas 参考单位当屏幕像素）

- **位置**：`Runtime/UI/UIManager.cs:938-945`（`ShowPopupAsync` 把 `rect.rect.size` 传给 `UIPopupPosition.CalculatePosition`，同时传 `Screen.safeArea` 作为屏幕矩形）；`Runtime/UI/UIPopupPosition.cs:83-101`（参数文档明确 `popupSize`/`screenRect`/返回坐标均为屏幕像素）；`Runtime/UI/README.md` 的手动示例同形。
- **发现**：`rect.rect.size` 是 Canvas 本地/参考单位，`Screen.safeArea` 与 `CalculatePosition` 的语义是屏幕像素；挂 `CanvasScaler`（项目侧常规，FairyGUI 文档也这么写）时两者不成比例，弹窗位置/尺寸与钳制整体错位。`UIPopupTests` 的根节点无 Canvas/CanvasScaler，测试结构性看不见。
- **复核**：主会话已核对调用与 `CalculatePosition` 的像素契约。
- **建议**：改传 `UIPopupPosition.GetScreenRect(rect).size`（与 `anchor` 同一条转换）；红证在 fixture 根节点加 `ScaleWithScreenSize` 的 CanvasScaler。
- **验证**：带 CanvasScaler 的弹窗定位断言（改前错位）；负向控制保持无缩放场景绿。

### SET-001 · P1 · A2/D5：加密装饰器遮蔽内层 `IAsyncSettingsStore` 能力

- **位置**：`Runtime/Settings/EncryptedSettingsStore.cs:35`（只实现 `ISettingsStore`）；`SettingsManagerImpl.cs:376`（能力探测只认装饰器本身）；`README.md:283-284,360`（一边要求平台后端实现异步接口，一边说可叠加加密装饰器）。
- **发现**：装饰器包住平台异步后端后，管理器探测不到 `IAsyncSettingsStore` → 降级为线程池调同步成员；平台侧数据永不参与读写且无告警。违反 CLAUDE.md 的「装饰器必须实现底层可选能力接口」不变量（File 的 `CryptoFileProvider` 是正例）。
- **复核**：主会话已按代码链复核。
- **建议**：装饰器实现 `IAsyncSettingsStore`（内层异步则转发，否则自身降级），或另建异步装饰器并改文档。
- **验证**：假 `IAsyncSettingsStore` 包进装饰器后走 `SaveAsync/LoadAsync`，断言异步调用数为 1（改前 0）。

### SET-002 · P1 · A1/B4：`LoadAsync` 的同步降级路径把 `defaultFactory` 挪到线程池

- **位置**：`SettingsManagerImpl.cs:386,400-406,511-514`；`README.md:585-587`（线程契约只列迁移器/校验器）、`:447`（工厂示例用 `SystemInfo.graphicsMemorySize`）。
- **发现**：同步 store + `LoadAsync` + 无存档时，默认值工厂在池线程执行；按 README 示例写会在首次启动抛 `UnityException`。与「降级语义与同步 Load 完全一致」的自述相反。
- **复核**：主会话已按代码链复核；线程 id 需实测。
- **建议**：README 补「工厂与迁移器/校验器同线程规则」，或把 `CreateDefault` 切回主线程。
- **验证**：工厂记录线程 id，断言等于主线程（改前红）。

### SET-003 · P1 · B1/F：直接 `Dispose` 门面返回的管理器后留下僵尸登记

- **位置**：`SettingsManager.cs:138-147,229-239,246-249`；`SettingsManagerImpl.cs:291-312,614-620`；`SettingRef.cs:137-150`。
- **发现**：`ISettingsManager<T>` 是公开 `IDisposable`，直接 Dispose 后门面字典仍留条目：`IsRegistered` 仍 true、`TrySettings` 抛 `ObjectDisposedException`、`TryWriteValue` 把异常抛进 UI 事件、`CopyRegisteredTypes` 也抛。直接违约两条「未初始化不抛/不可用返回 false」契约。
- **复核**：主会话已按代码链复核。
- **建议**：文档写明「释放请用 `SettingsManager.Destroy()`」；或门面跳过已释放条目（改 `IsInitialized` 语义需裁定）。
- **验证**：`Initialize<T>()` → 直接 Dispose → 断言三处 `false`/不抛（改前红）。

### SET-004 · P1 · B6/A1：会话边界不清句柄订阅流，「旧订阅者不会复活」是错的

- **位置**：`SettingRefRegistry.cs:77-81`（边界只清 `Refs` + 递增代际）；`SettingRef.cs:157-178`（读写即重登记）；`README.md:612-617`、`CHANGELOG.md:175-177`；模块自己的 `SettingsSessionResetTests.SessionBoundary_HandleReadInNewSession_StillReplays:143-159`。
- **发现**：句柄在会话 2 被使用（含只读一次）即重新入表，**会话 1 的订阅者仍在流上**并被重放——现有绿灯用例正是靠它通过。README/CHANGELOG 的「旧订阅者不会复活」与实现相反；第二会话可能回调已销毁 Unity 对象并记 `[Event]` Error。
- **复核**：主会话已读用例与注册表代码复核（证据等级：实测——用例自身锁定相反事实）。
- **建议**：订正 README/CHANGELOG 并补钉现状用例；彻底解决需在会话边界清句柄订阅流（破坏性设计，`待裁`）。
- **验证**：会话 1 订阅 → 边界 → 会话 2 读一次 → `Reset` → 旧处理器计数 +1（现状）；文档修复无需红证。

### AUD-001 · P1 · B5/C2：短于一拍的 one-shot 卡 `Starting` 到 60 拍超时并误报

- **位置**：`Runtime/Audio/AudioManagerImpl.cs:248-261`；`Runtime/Audio/AudioSourcePool.cs:224-241`（`StartingTimeoutTicks = 60` 在 `:24`）；`Runtime/Audio/AudioTicker.cs:5-11`（Tier1 ≈ 33ms）；`:357-366`（`Pause`）与 `AudioSourcePool.cs:245-249`（Paused 永不回收）。
- **发现**：`PlayCoreAsync` 在 `Source.Play()` 后置 `Starting`，只有下一拍 `Sweep` 观察到 `isPlaying` 才转 `Playing`。短于一拍的 one-shot（60fps 下 <33ms；10-20fps 移动端 50-100ms）在下一次 `Sweep` 时已播完、`isPlaying == false`，于是占槽 60 拍（60fps≈2s、10fps≈6s）后被强制回收并打「起播超时」误报告警。同窗口 `Pause()` 会把已播完的声音置 `Paused`（永不回收），`Resume` 后再等 60 拍。
- **复核**：主会话已按代码链复核；需 PlayMode 红证（P-01）。
- **建议**：`Play()` 后同步读一次 `isPlaying`（需 P-06 实测确认）；超时告警措辞改为「从未观察到 isPlaying 变真」；补短 clip 用例。
- **验证**：0.01s clip → `Play` → 0.3s 后断言 `ActiveVoiceCount == 0`（现状 1，约 2s 后才归零）。

## 5. P2 清单（按模块分组）

> P2 明细按发现模块归组；跨模块条目在标题处标注归属。

### Event

- **EVT-001 · P2 · B5**：`EventStreamImpl.cs:310-317` 的 `Return` 不清 `Next`，池中节点钉住「派发中退订放弃回池」的节点及其 Handler 闭包；关域重载时跨会话。建议 `Return` 补 `node.Next = null;`；白盒用例改前必红。report-only。
- **EVT-002 · P2 · B3/D4**：`DisposableBag.cs:65-82` 的 `Clear` 一项 `Dispose` 抛异常即中断并清空列表，其余句柄永久丢失且不可观测。照 Settings 先例逐项隔离 + 记 Error；修后进 README `行为契约`。
- **EVT-003 · P2 · A1/C1**：`Runtime/Event/README.md:105` 称「不产生每订阅一次的分配」，实际每次 `Subscribe` 都 `new EventSubscription`（`EventStreamImpl.cs:80`）；同文另一处自相矛盾。改 README。
- **EVT-004 · P2 · A1**：`Runtime/Event/README.md:7`「被两个模块使用」与 `:180-184`「被四个模块使用」冲突（实为 Message/Reactive/Input/Settings）；`:56`「五个公开文件」实为四个公开 + 一个 internal。改 README。
- **EVT-005 · P2 · A1/D3**：`SubscriptionTracker.cs:25-26,60-61` 称 `Reset` 由域重载与每个 fixture 的 SetUp 调用，实际无任何自动钩子、也不是每个 fixture。改文档，或补 `RuntimeInitializeOnLoadMethod` 并登记族清单。
- **EVT-006 · P2 · D3**：`EventStreamImpl.cs:216-233` 的 `Remove==false` 分支（流 Dispose 后旧句柄 Dispose）零直接覆盖；守卫被删会让计数变负、节点二次回池。补一条用例（不改生产代码）。
- **EVT-007 · P2 · B5/A2（跨模块 · Message）**：`MessageBroker.cs:79-85` 首条 `Publish` 时最后同步订阅者自退订 → 通道在缓冲写入前被回收（`MessageChannel.IsReclaimable` 首次发布前为 true），该条消息无法被后续缓冲订阅重放。**Message 审计已复核成立，与 MSG-001 为同一缺陷（以 MSG-001 为正式条目）**；本行保留为跨模块线索。

### Serialize

- **SER-001 · P2 · A1/B1/F**：`Serializer.cs:147-160` 的 XML 写 `KeyNotFoundException`，实际抛 `InvalidOperationException`；异常消息建议的 `Initialize()` 在「已初始化后注销 json」路径上是 no-op。改 XML 与消息。
- **SER-002 · P2 · A1**：`Runtime/Serialize/README.md:136-137`「替换内置格式必须早于初始化，晚了会被内置盖回」与实现**相反**：`Register` 任何时候都静默覆盖且无告警；告警只在「提前注册」路径。改 README。
- **SER-003 · P2 · A1/D1**：模块 README、包 README 模块索引、`ReadmeTableTests` 豁免理由都把 Config 列为 Serialize 消费方，Config 实际零引用。三处一起改为「Data / Save」。
- **SER-004 · P2 · A2/B3/D4**：`Serializer.cs:138-145,114-121` 的 `TryGet`「不抛——探测用」对 `null` format 不成立（ANE）；`Unregister(null)` 同样。建议对 null 返回 false，或在 XML 明写并限定 README 的「不抛」。
- **SER-005 · P2 · D3**：`Tests/Runtime/Serialize/SerializerTests.cs` 失败面零断言（`Default` 异常类型、`TryGet(null)`、`Unregister` false、`Shutdown` 后行为），正是 SER-001/004 存活的原因。补 4–5 条失败面用例。
- **SER-006 · P2 · B5/A1**：`Serializer.cs:30-31` 静态注册表跨会话存活且无复位入口；台账理由「无跨会话静态态」失实。需裁定：补 `ResetForNewSession` 或明示跨会话保留并订正台账。

### Scene

- **SCN-002 · P2 · A1/A2/D4**：后端缺 `IAssetSceneLoader` 时 `SceneManager.LoadSceneAsync` 返回无效句柄，唯一提示是 Asset 侧每会话一次 Warning；Scene 文档未写降级语义，调用方无法区分「加载失败」与「后端缺能力」。补 README `已知限制` + XML。
- **SCN-003 · P2 · B5/D5（跨模块 · Asset）**：`YooAssetManagerImpl.cs:562-565,575-599,655-660` 的 `_sceneOperations` 只在显式卸载/Destroy 清理；Single 模式切场景后每条加载残留一项，会话内线性增长（YooAsset 侧已自愈，不泄漏 bundle）。建议订阅 `SceneManager.sceneUnloaded` 按场景移除（推荐），或加载时顺手清旧项，或文档写明。
- **SCN-004 · P2 · D4/B3**：`SceneManager.cs:60-63` 丢弃 `SetActiveScene` 返回值，激活失败不可观测（`ActivateAfterLoad` 正是为避免新物体挂旧场景）。失败时记 Warning。
- **SCN-005 · P2 · B4/A1**：`Runtime/Scene/README.md:204-205` 称「越过 Asset 侧已有的主线程守卫」，但 Asset 的 `LoadSceneAsync`/`UnloadSceneAsync`（`AssetManager.cs:414-445`）没有 `AssertMainThread`。改理由句，或加 Editor-only 断言。
- **SCN-006 · P2 · D3（跨模块 · Asset 测试）**：`Tests/Runtime/Asset/SceneLoadOperationTests.cs:311-320` 造第二个场景不清理，污染同一 player。补 TearDown/用例内卸载。

### Pipeline

- **PIPE-001 · P2 · A1**：`Runtime/Pipeline/README.md:130,138,146` 与 `Documentation/Modules/Pipeline.md:48` 把 internal 的 `StageExecution` 写成公开面；照写会 CS0122。三处一起改（公开面实为 8 个类型）。
- **PIPE-002 · P2 · A1/B3**：`Pipeline.cs:268-280` 超时失败未写 `failStageName`（主动失败在 `:308` 写），`FailureReason` 退化为 `Failed: : Stage 'X' timed out…`；现有断言全是 `Contains("timed out")`，结构性掩盖。补一行 + 把断言改 `StartsWith`（改前必红）。
- **PIPE-003 · P2 · F**：失败/取消终局先 `Broadcast()` 后写 `_status`（`Pipeline.cs:327-353`），完成终局相反（`:354-367`）；订阅者在终局进度回调里读到 `Running`。把 `_status` 移到广播前（两行），或文档写明。
- **PIPE-004 · P2 · D4/A1**：`Pipeline.cs:149-151` 的 `AddStage(null)` 静默忽略，XML 未写；与仓内参数防御约定不一致。抛 ANE、告警后忽略、或补文档，需一次裁定。
- **PIPE-005 · P2 · A1/B6**：`ParallelStage`/`SequenceStage` 的接收方/聚合器在实例字段上（`ParallelStage.cs:59-68,137`），同一实例跨管线并发执行会串台；文档一字未提。补 `已知限制`/XML，或加重入断言。
- **PIPE-006 · P2 · A1/B1**：`IPipeline.cs:83-88` 称 `Destroy` 后「回落 Idle/不再广播」，实际在途运行结束后仍写终局状态（`PipelineTests.cs:108-127` 断言 `Completed`）。改 XML 措辞，或让 `Destroy` 真正关闸（需与 `OnProgressUpdate=null` 语义一起想）。

### Pool

- **POOL-001 · P2 · B3/F**：`Pool.cs:136-163,168-204` 用户回调抛异常时账本已改（`_activeCount`/Editor 追踪集），实例悬空、计数永久虚高、集合池 `Configure` 被永久粘住；同 `Get` 内 generator 抛出则是安全的。把账本更新移到回调后或 `try/finally` 恢复。
- **POOL-003 · P2 · A1**：`PoolStats.cs:23` 注释「只增不减，除非池被清空」与 `Clear()`/`IPool`/README 相反；真正归零的是集合池 `Configure` 重建（`ListPool.cs:87`），README 未提。改注释与 README。
- **POOL-004 · P2 · A1**：`PoolConfig.MaxSize <= 0` 静默等于无上限（`Pool.cs:97`），文档未写；想表达「不保留闲置」的调用方得到相反行为。文档写明，或改为按字面值生效（行为变更需裁定）。
- **POOL-005 · P2 · A1**：`Runtime/Pool/README.md:367` API 表写集合池 `Configure`「首次 Get 前」，与同文件 `:130,:395` 的「无活跃实例时」矛盾。改 API 表。
- **POOL-006 · P2 · E**：`Internal/PoolLogCategories.cs:16,22,28,37-38` 用 `typeof(T).Name` 作 `LogCategory` 键，跨命名空间同名类型合并、级别互相影响。改 `FullName`（可观察文案变更，需裁定）或文档写明。
- **POOL-007 · P2 · B1/D4**：集合池 `Configure` 重建后，先前 `GetPool()` 得到的 `IPool<T>` 成为孤儿且无告警（`ListPool.cs:74-88,101-104`）。在 `GetPool()` XML/README 写明「重建后失效」，或原地重置。
- **POOL-008 · P2 · D3**：三处测试问题——`PoolDiagnosticsTests` TearDown 只 `ClearAll` 不 `RemovePool`（同会话重跑必红）；注册计数依赖静态构造一次性（重跑必红）；「两边都有池仍拒绝」是 Editor-only 行为被当普遍契约。修 fixture 与注释。
- **POOL-009 · P2 · A1/B1**：`PoolManager.RemovePool` 的闲置实例不走丢弃回调（README 清单未列）；`PooledObject.Dispose` 归还走旧 `IPool` 引用，不满足「池已不存在即静默忽略」的承诺。补文档/精确 XML。
- **POOL-010 · P2 · A2**：`CollectionPoolManager.Register` 公开面过宽（只为四集合池静态构造服务、文档自述「不应手动使用」），且进公开 API 基线。收窄为 `internal` 属破坏性变更，需用户单独确认。

### Diagnostics

- **DIAG-002 · P2 · F/D4**：`DiagnosticsWindowModel.cs:60-77,127-147` 注册表变空时 `Tick` 早退不重绘、`_report` 不清，`DiagnosticsWindow.cs:98-99` 的「复制」仍输出上一个页签的陈旧内容。清空报告并请求一次重绘/禁用复制。
- **DIAG-003 · P2 · B5/B2/D3**：fixture 的 `Clear()` 复位会抹掉框架页签，关闭域重载时本编辑器会话内无法恢复（`FrameworkPanels.Register` 是私有 `InitializeOnLoadMethod`）。给测试恢复入口，或在 `Clear` 文档写明。
- **DIAG-004 · P2 · A1/D3（跨模块 · Camera）**：`OverviewDiagnosticPanel.cs:23-28,43-69` 自称模块状态矩阵却漏 XCamera；注释只解释 Event/Reactive/Pipeline。加一行或改措辞。
- **DIAG-005 · P2 · D3**：没有守卫保证每个 `*DiagnosticPanel` 都在 `FrameworkPanels` 登记（新增页签会静默不显示）。加一条 EditMode 反射守卫（双向集合相等 + 负向控制）。
- **DIAG-006 · P2 · A1**：`Documentation/Modules/Diagnostics.md:26` 写死「18 个框架页签」，违反「不写会随开发增长的量」。删计数。

### Camera

- **CAM-001 · P2 · A1/F/D4/B5**：`UnityCameraManager.cs:175-183,196-206,265-271,317-322` 被外部销毁的相机仍被视为活动：`OutputCamera` 返回已销毁对象引用（`is null`/`??` 不成立），`ActiveHandle`/`IsActive` 仍报活；README 的「返回 null」不成立。修行为（解析时视为无效）或改文档。
- **CAM-002 · P2 · B1/B3**：`CameraManager.cs:209-216` 先 `Dispose` 后置空，第三方 `Dispose` 抛出即永久闩锁；Audio 是先清槽再 Dispose 的先例。改为先置空/复位再 Dispose。
- **CAM-003 · P2 · C2/A1/D4**：`UnityCameraManager.cs:491-504,336-366` 过渡时长无有限性校验：NaN 使过渡永不结束且逐帧写坏位姿，+∞ 永久挂起；文档 `<=0` vs 实现 `<=0.0001` 不符。入口校验非有限值，统一三处文档边界。
- **CAM-004 · P2 · D3/B6/B1**：句柄 ABA 关键路径无测试——现有 stale 用例只覆盖「槽位空置」，未覆盖「槽位被复用后旧句柄失效」。补 `StaleHandle_AfterSlotReuse_IsNoOp`（锁定型 + 负向注入）。
- **CAM-005 · P2 · A1/D5/F（跨模块 · 扩展包）**：`ICameraManager.cs:25-28,74-78` 与随包 Cinemachine 适配器不一致：`OutputCamera` 在适配器恒非空；`Unregister` 只清 Manager 视图、Brain 仍用该 vcam（已读 Cinemachine 3.1.7 源码）。需裁定：窄化核心契约措辞 vs 改扩展包行为；两者都要在扩展包台账记账。
- **CAM-006 · P2 · A1**：`Documentation/Modules/Camera.md:13` 的「5 查询 + 4 字符串 + 4 句柄操作」计数已错且属固定计数。删计数。

### Message

- **MSG-003 · P2 · A1/D4/D5**：`Internal/MessageChannel.cs:139-147` 的 `EvictBuffered*` 会 `Dispose` 整条缓冲流，**静默终止仍在订阅的缓冲订阅者**；README:366「只丢重放缓存，不会回收仍有活订阅者的通道」与实现相反。最小修法是改文档并立 `待裁`（「只清缓存保订阅」需要 Event 新原语，给 `IBufferedEventStream` 加成员对第三方是源码破坏）。
- **MSG-004 · P2 · B5**：`MessageManager.cs:741` 的 `TryBindToDestroy` 丢弃 `CancellationTokenRegistration`；长寿命 `IDestroyCancellationToken` 所有者反复订阅/退订会累积登记（.NET 语义确定）。把 registration 与订阅句柄合并进返回的 `IDisposable`。
- **MSG-005 · P2 · B4/A1**：主线程断言只覆盖 6 处发布/订阅入口；`EvictBuffered*`/`TrimEmptyChannels`/统计族（`MessageBroker.cs:451-612,642-800`）改同一批无锁字典却零断言，README 的「覆盖不到三处」也不含它们。补断言或扩写线程契约。
- **MSG-006 · P2 · A1**：`Runtime/Message/README.md:210-216` 的公开面表漏列 4 个公开类型（`MessagePublishStrategy` + 3 个统计 struct），与同文 `:206` 自相矛盾。补表。
- **MSG-007 · P2 · B3/D4**：同步订阅级过滤器异常记成 `[Event] … handler threw exception`（与处理器异常同文案、不在 `[Message]` 检索面），异步侧却是 `[Message] … filter threw`。同步侧改为同形 `[Message]` 文案。
- **MSG-008 · P2 · D3**：最贴近 MSG-001 的用例（`MessageEvictionTests.cs:355-370`）只断言「不抛 + 重建后能收新消息」，恰好缺缓冲重放断言；另 `EvictBuffered*` + 活缓冲订阅者零覆盖、扩展 `SubscribeAsync` 四个重载零覆盖。补三条用例。
- **MSG-009 · P2 · A2/A1**：`MessageManager.cs:686-702` 的 `SetInstance` 是 internal 且全仓零调用点，XML 却宣传「注入 mock broker」，而 `MessageBroker` 是 sealed 具体类。删除，或补用例并订正 XML。

### Reactive

- **RCT-002 · P2 · A1/E**：`ReactiveList.cs:84-86,145-147` 的 XML 称「引用类型元素即引用相等」，实为 `EqualityComparer<T>.Default` 对重写 `Equals` 的类型走 `Equals`（同模块 `IdKey` 用例即反证）。改 XML + README `已知限制`。
- **RCT-003 · P2 · A1**：`ReadOnlyReactiveProperty.cs:200` 的 XML 示例引用仓内不存在的 `FloatComparer`，照抄 CS0103。改成自足写法或只留语义说明。
- **RCT-004 · P2 · A1**：`ReactiveDictionary.cs:242-244` 与 README:171-172 称 `Keys`/`Values` 每次访问都分配视图；BCL 首次访问后缓存视图（遍历装箱那半句成立）。两处同批改。
- **RCT-005 · P2 · F/E**：`ReactiveDictionary.cs:127-135,200-208` 在注入等值键比较器时，`Replace`/`Remove` 载荷的 `Key` 是调用方实例，字典保留首次插入的键实例；「键稳定」只对按比较器等值成立。补 XML/README 一句。
- **RCT-006 · P2 · F/B3/D4**：`ReactiveList.cs:199-206` 的 `Sort(comparer)` 抛出时列表可能已部分重排且不补发 `Reset`；`List.Sort` 的包装异常（`InvalidOperationException`）未写进 XML。补文档（「异常后重读」+ `<exception>`）。
- **RCT-007 · P2 · E/台账**：`ReactiveDictionary.cs:116,130` 的值相等比较器写死 `EqualityComparer<TValue>.Default` 且不可注入；列表侧同族问题有台账条目，字典侧没有。新增台账 `未做` + README 点明「值相等不可注入」。
- **RCT-008 · P2 · D3**：`ReactiveListTests.cs:150-163` 的 `Sort(comparer)` 参数零覆盖；`ReadOnlyReactiveProperty.SubscriptionCount` 零覆盖；链式用例对 RCT-001 结构性失明；`Values` 枚举器失效未测。补 4 条用例（含 RCT-001 红证）。
- **RCT-009 · P2 · B4**：「主线程专用」只在 README，代码零守卫（Message/Asset/Pipeline 均有入口断言）。二选一：加 Editor-only 内联断言，或台账 `暂不做` 并写明理由。
- **RCT-010 · P2 · C1**：`T` 为未实现 `IEquatable<T>` 的结构体时，每次去重/`Remove` 查找装箱两次（`ReactiveProperty.cs:92`、`ReactiveList.cs:96,155`、`ReactiveDictionary.cs:130`）；文档未提。README 补一句前提。
- **RCT-011 · P2 · A1/A2**：三处文档小缺口——容量构造缺负值 `<exception>`；`ReadOnlyReactiveProperty` 摘要误写来源为 `ReactiveProperty<T>`（`Select` 可作用于任意 `IReactiveProperty<T>`）；`IReactiveProperty.Subscribe` 未写「立即回调抛出时订阅不得登记/由实现清理」，而 `Select` 依赖这一隐含要求。

### Timer

- **TMR-001 · P1 · A1**：`TimerManager.cs:34` 的 XML 示例与 `Runtime/Timer/README.md:118-119` 把 `After<TState>` 的参数顺序写反（`this` 落在 `Action<TState>`、lambda 落在 `TState`），照抄 CS1503；正确写法见 `Samples~/Example/TimerSample.cs:47`。两处同批改。
- **TMR-002 · P1 · C2**：`TimerTable.cs:226-268` 的 `Sweep` 只把**本轮访问到的**槽位计入 `min`；一次性定时器在 `:479-482` 先释放槽位再回调，回调里新建的定时器经自由链 LIFO 复用刚释放的下标，而循环已越过它 → min 漏算 → `SelectTier` 保持粗档，同轴新建的 0.1s 定时器可晚约 2.13s 触发。直接推翻 `TimerTicker.cs:83-84` 与 README:199「同轴创建不受影响」。扫尾收尾时按真实存活槽位重算最近截止（或升档请求排到下一次 Tick 开头）。
- **TMR-003 · P1 · C2**：`TimerCore.cs:124-125,156-157` 的 `Create`/`Restart` 用 `_logicalNow` 定锚，而它只在派发时推进（`:286`）；两次派发之间新建的短定时器截止可能已在过去，下一次派发一次性跳过时间 → 提前最多一个档位周期触发（60s 定时器存在时 `After(1f)` 可约 40ms 后触发）。违反 `After` 的「delay 秒后回调」与 CHANGELOG 的「锚点落在 Create 那一刻」。短期文档化；干净修法需 Update 暴露当前轴时刻（跨模块，`待裁`）。
- **TMR-004 · P2 · A1**：五处文档小错——`CancelAll` 声称重置逻辑时钟（实际没有）；`TimerHandle.Dispose` 称「安全空操作」（实际释放槽位）；`TimerInfo.IsRunning` 未写「令牌已取消」；`TimerHandle` 注释仍写 `Id`（成员是 `Slot`）；`CancelAll` 的计数说明漏了「令牌已取消未回收」。逐条订正。
- **TMR-005 · P2 · B5/D4**：`TimerTable.cs:113-114,589-599` 的 1024 泄漏告警只看 `RunningCount`，「Stop 后忘记 Dispose」这一主泄漏形态下计数回到 0，告警永不响。改用 `_allocatedCount`（或两者都看）并在消息里分列两个数。
- **TMR-006 · P2 · B4**：`Runtime/Timer/README.md:192-194` 声明主线程专用，但全部写路径无守卫（`Allocate/Stop/Restart/Release/Sweep/CancelAll`）。复用既有 Editor-only 断言形态（只报首错），或写明「有意不设」及代价。
- **TMR-007 · P2 · E**：`TimerHandle` 被文档鼓励「存入容器」，但缺 `IEquatable`/`==`/`GetHashCode`；作为字典键时走 `ValueType.Equals(object)`，每次比较装箱 + 反射式字段比较。补 `IEquatable`（源兼容公开面新增，需确认 + 更新基线 + CHANGELOG `### Added`），或改文档明写「当键会分配」。
- **TMR-008 · P2 · D3**：覆盖缺口——核心换代/`AutoInit` 零行为用例、`After<TState>/Every<TState>` 的状态投递零断言、出生趟号立即扫尾路径未测、`Remaining` 断言按 Tier0 算而实际 Tier2（靠容差盖住）、终态档位断言可被「触发并释放」满足、诊断 fixture 隔离弱。逐条补最小用例。
- **TMR-009 · P2 · C2/D1**：`TimerCore.cs:32` 的 `AxisCount = 2` 是第二份跨模块常量副本（文档称 `TicksPerSecond` 是唯一一处），且 `UpdateTimeMode` 扩容时现有测试会假绿。改为从 `Enum.GetValues` 派生并补守卫。
- **TMR-010 · P2 · C2/B5**：新表 `_sweepPass` 从 0 起算（`TimerTable.cs:134`）；回调里用全新 `TState` 建 `After(0f)` 时，新表在同一趟被扫到而趟号已 +1，新生槽位被当作旧槽位 → 同一拍触发，早于「下一拍」。新表应继承所属轴当前趟号。
- **TMR-011 · P2 · A1**：「回调里跨轴创建」的已知限制写窄了——`_isIterating` 是跨时机共享的静态闩锁，任何 XUpdate 派发回调里创建更近定时器都受影响；「同轴不受影响」有 TMR-002 反例。改写限制并单列反例。

### Log

- **LOG-001 · P1 · B3/C2/B5**：`Internal/LogFileSink.cs:52-55,172` 的文件基名只含会话起始时刻 + 会话 id，同一次运行内恒定，首次写入用 `FileMode.Create` 截断；`LogManager.cs:120-134` 的 `Configure`/`Shutdown` 重建实现时 `LogSession.Reset()` 只在 AutoInit 调用（`:537`），且新实现的 `seq` 从 1 重计。后果：同会话重建会丢掉已写 JSONL 的启动段、`seq` 违背「会话内单调」。基名改用 sink 创建时刻或进程内序号、`seq` 提到会话级，或文档明写顺序约束。
- **LOG-002 · P1 · B3/D4**：`LogManagerImpl.cs:455-474` 的 `DispatchCaptured` 只跳过内置 `ConsoleLogSink`；`UnityLogCapture.cs:37-51` 的捕获回调在调用线程同步触发。自定义 sink 在 `Write` 里调 `Debug.Log*` 会无限重入 → StackOverflow（不可捕获）；README:90,208,214 却写「sink 内再写日志是安全的（可重入）」，未区分经 `LogManager` 与经 `Debug.Log`。加 `[ThreadStatic]` 重入闩锁 + 订正文档。
- **LOG-003 · P2 · B3/D4/F**：`LogFileSink.cs:109-112,224-228` 的 IO 失败 `Die()` 后 sink 仍在 `_sinks`、不计 `DroppedSinkCount`、诊断页签照旧显示；`IsDead` 测试缝全仓零引用。与「sink 抛异常 → 摘除 + 计数」不对称。让 `Die()` 通知 impl 摘除并计数，或补健康回读能力。
- **LOG-004 · P2 · C1/A1**：`LogManagerImpl.cs:84-87,494-505` 的捕获路径在档位过滤前 `ParseCategory`（带 `[标签]` 的外部日志每条分配 `Substring`）；README:95 的「没有可写输出端时第一行即返回」实现中不存在。先查有无非控制台 sink + 先按全局档早退，再解析。
- **LOG-005 · P2 · E/B5**：分类是全局裸字符串键（`Internal/LogRegistry.cs:18-19`），同名作者静默合并；外部 `[标签]` 被永久注册、注册表只增不减（与 POOL-006 同根）。文档补「全局命名空间/建议加前缀/注册只增不减」。
- **LOG-006 · P2 · B2/B3**：`LogManagerImpl.cs:344-370` 的 `AddSink` 允许重复登记，`RemoveSink` 只摘首个并立即 `Dispose` → 已释放实例仍在册、`Shutdown` 二次 Dispose。移除全部匹配（只 Dispose 一次）或按引用去重，或文档写明不要重复登记。
- **LOG-007 · P2 · D3**：`Tests/Editor/Log/LogDiagnosticsReadbackTests.cs:46-57` 的 `CopyCategories`「先清空」断言恒真；`RemoveSink` 释放契约零覆盖；`IsDead` 缝未用。改哨兵断言并补释放用例。
- **LOG-008 · P2 · A1/A2**：8 条文档/实现口径不一致——`DroppedSinkCount` 漏 Flush、`Shutdown`「不可用」vs `IsInitialized` 恒 true、`LogOptions`「仅 Configure 读取」、`LogSession` 陈旧字段、README `CaptureUnityLogs`「跟随」实为一次性默认拷贝、维护文档行号 `135` 应为 `134` 等。批量订正。
- **LOG-009 · P2 · B6/B4**：会话复位残留——`_cachedFrame` 跨会话陈旧（首条工作线程日志可能带上一会话帧号）、分类注册表只复位档位不清名字、`_impl` 无屏障读（低概率）。补复位/文档化。
- **LOG-010 · P2 · D4/A2**：`LogOptions` 零校验——`MaxFileBytes=0` 每条日志切一个新文件、`FileDirectory=null` 静默 `Die()`、`MaxRetainedFiles<=0` 删光旧文件、`FlushEveryEntries=0` 每条 flush。`Configure` 做最小参数校验。
- **LOG-011 · P2 · B4**：懒建路径可能在**工作线程**读 `Application.*`/`Debug.isDebugBuild`（`LogOptions.cs:22-24`、`LogManager.cs:57-70`），与「环境快照必须主线程」口径不一致；`Shutdown` 后工作线程首条日志可触发。快照化默认选项或文档限定主线程。

### Settings

- **SET-005 · P2 · B3/B2**：`SettingsManagerImpl.cs:232-234` 的 `Observe` 在「订阅即回调」抛异常时泄漏订阅（句柄交不出去、节点仍在流上）；`SettingRef.Subscribe` 已按同一理由先退订再上抛。补 `try { callback } catch { handle.Dispose(); throw; }`。
- **SET-006 · P2 · A2/F**：`SettingRef.cs:147-149` 的 `TryWriteValue` 在写入派发后重新读 `Value`；订阅者在派发中销毁管理器会把异常抛进 UI 事件，违背 `IReactivePropertyWriter` 的成文理由。`actual` 直接回传 `value`（Settings 的 setter 不做规范化）。
- **SET-007 · P2 · B1/A2**：`SettingsManagerImpl.cs:242,249` 的 `Migrator`/`Validator` 是自动属性，`Dispose` 后仍可读写，违背接口「释放后所有成员抛 ODE」的契约。补 `ThrowIfDisposed` 或改契约措辞。
- **SET-008 · P2 · B1**：在飞的 `SaveAsync`/`LoadAsync` 不感知 `Dispose`（`SettingsManagerImpl.cs:343-394`），释放后仍会写盘/替换设置并广播；`Dispose` 的 XML 明确说要避免「已释放却仍在写盘」。在 `SwitchToMainThread` 之后补 `_disposed` 检查。
- **SET-009 · P2 · B3/D4**：`defaultFactory` 返回 `null` 无防御（`SettingsManagerImpl.cs:511-514`），而 store 返回 null 有防御 + 告警。`CreateDefault` 对工厂产出做 null 检查并回退 `new T()`。
- **SET-010 · P2 · E/B5**：`DefaultPathClaims` 在成功注册之前就登记（`SettingsManager.cs:105-119`），构造失败或「已用显式路径初始化后再调默认路径」会留下不符的占用；键比较器大小写敏感而文件系统不敏感。claim 移到创建成功后写，比较器改 `OrdinalIgnoreCase`。
- **SET-011 · P2 · D4/A1**：`JsonFileStore.cs:78-82` 的「主文件缺失不回退备份」理由（Reset 复活）在正常 Reset 下不成立（`Delete` 同时删 `.bak`），代价是原子替换降级窗口内备份存在却不用。订正理由句或改行为（`待裁`）。
- **SET-012 · P2 · C2/A1（待实测）**：`SettingsOptions.cs:44-45`/README:500-505 断言 `wantsToQuit` 在编辑器播放模式不触发；Unity 6 文档只注记「返回値被忽略」。实测定案后三处一起订正。
- **SET-013 · P2 · C2/B4**：`SettingsManagerImpl.cs:437-444` 的同步降级 `SaveAsync` 把**活的**设置对象交给池线程 `JsonUtility.ToJson`，与主线程继续修改竞态（Unity 文档明确禁止）。主线程序列化快照，或 README 写明未定义行为。
- **SET-014 · P2 · B1/D4**：在飞 `SaveAsync` 与随后的 `Reset`/`Load` 无序化（`SettingsManagerImpl.cs:206-216,343-364`），重置可能被旧快照复活、`_savedCount` 被旧快照覆盖。加管理器级闸门或文档限定调用顺序。
- **SET-015 · P2 · D2/A2**：`SettingRef.cs:103-109` 的 `Ref<T,TField>` 在类型不一致但可隐式转换时抛 BCL 裸 `ArgumentException`；`Initialize<T>(null)` 可能 CS0121。需编译复现后补修复提示/文档。
- **SET-016 · P2 · D3**：门面 `GetStore`/`SetStore`、`SaveAsync`/`LoadAsync` 转发零行为覆盖（`SettingsFacadeCompletenessTests` 自述只查名字）。补四条行为用例 + 反向注入。
- **SET-017 · P2 · D3**：`SettingsPauseFlushTests.cs:152` 用 `Assume` 开场，宿主计数泄漏会让用例 skip 而非失败；直接构造的 `SaveOnPause` 管理器不在 TearDown 回收。改断言或 SetUp 清场 + try/finally。
- **SET-018 · P2 · B4**：成文线程契约无任何代码守卫（`SettingRef.cs:34`、`SettingRefRegistry.cs:40`、README:584-590）；两个静态 `List` 并发写入会静默损坏。按 Ledger 判据决定补内联断言或写明「不做」。

### Audio

- **AUD-002 · P2 · B2/B5**：`AudioManagerImpl.cs:687-706` 用本地 `_tickerRegistered` 记账，从不向 `UpdateManager` 核对；公开 `UpdateManager.Clear()` 或调度器因异常自动注销后，`Play` 不再重新注册，回收永久停摆、无日志，直到池耗尽。用 `UpdateManager.IsRegistered(_ticker)` 或每次 `Play` 无条件 Register；若维持现状须写进 `已知限制`。
- **AUD-003 · P2 · B3**：`AudioSourcePool.cs:224-226`（`Sweep`）与 `AudioManagerImpl.cs:558-576`（`ConfigureSource`）、`:357-392`（Pause/Resume）直接访问可能被外部销毁的 `AudioSource`，无 fake-null 防御；一次异常会留下永久 `Loading` 且持有租约的槽位。对 `voice.Source == null`（Unity 重载）明确收尾。
- **AUD-004 · P2 · B3/D4**：`AudioClipLease.cs:37-41,62,72` 与 `AudioManagerImpl.cs:653-656`、`AudioSourcePool.cs:332-338`：`IsValid == false` 但仍有释放动作的租约永不 `Dispose`——location 指向非 `AudioClip` 时 YooAsset 加载成功但 `Asset` 为 null，引用计数无界增长且无正确日志。让失败路径无条件 `lease.Dispose()`。
- **AUD-005 · P2 · D3**：`NoPlayback_DoesNotRegisterTicker`（`AudioTickerTests.cs:126-131`）是恒真断言；`ReclaimedSlot_IsReused` 只比 Id、`Play_WithoutOptions` 名不副实；租约 fixture 的「离开时 OutstandingLeases 为 0」无 TearDown 断言。逐条修正。
- **AUD-006 · P2 · D3/D4**：`CopyChannels`（`AudioManager.cs:348-358`）内容路径零覆盖；未初始化时静默返回 0，与 README 生命周期表「其余全部抛」冲突。补内容用例，并在 README 写明或改为抛（需裁定）。
- **AUD-007 · P2 · A1/A2**：`Play` 的失败契约在 3 处随包 XML + README 中与实现相反（加载失败不返回 default；加载窗口内 `IsPlaying` 也是 false，无法判失效）。订正 `<returns>`/接口 remarks/README 指引。
- **AUD-008 · P2 · A1**：「通道在首次播放时惰性创建」四处失实——`SetChannelVolume`/`SetChannelMuted`/`RegisterChannel` 也会创建；`AudioChannelConfig.Volume` 的钳制点在 `RegisterChannel` 而非 `SetChannelVolume`。统一订正。
- **AUD-009 · P2 · A1**：`AudioHandle.cs:73` 称「Stop 后代际递增」，`AudioSourcePool.cs:155-159` 明确说不在 Release 递增。改 XML。
- **AUD-010 · P2 · A1**：`README.md:72` 的文件分类句把 internal 的 `AudioManagerImpl` 算进「公开面」。改分类句与树标注。
- **AUD-011 · P2 · 台账**：`Documentation/Modules/Audio.md:60`「不做只读通道快照」已被 2026-10-02 的 `CopyChannels` 推翻，应改 `已推翻`；`:65` 的「只能看 ActiveVoiceCount 与分通道计数」已过期，刷新为「剩余逐 voice 明细」。
- **AUD-012 · P2 · A2**：`AudioSourcePool.cs:73` 的 `HasHost` 文档称「测试用来断言」但无任何测试引用；`:55` 的 `MaxVoices` 全仓零引用。删除或接上消费点。
- **AUD-013 · 待取证 · C2**：`AudioListener.pause` 与 `AudioSource.isPlaying` 的相互作用未实测，README/接口却把「连带暂停」写成期望行为；若 `isPlaying` 变 false，`Sweep` 会把被全局暂停的播放当播完回收。需真机/编辑器探针。
- **AUD-014 · P2 · D3**：默认 `AudioClipLoader`（`AudioClipLoader.cs:17-21`）与 `AudioManager.Initialize()` 的生产路径零覆盖，全部播放 fixture 用 `FakeAudioClipLoader`；AUD-004 正被这层替身结构性绕开。补最小集成用例。

### Data

- **DATA-002 · P2 · B1/B6/A1**：`Runtime/Data/DataBootstrapStage.cs:41-43` 在阶段被重复执行时把 `_initializedByThisStage` 从 true 覆写为 false；Bootstrap README 明确认可「失败重试/再次 `RunAsync`」→ 重试后 `Shutdown` 不再注销 DataManager（门面 + 块泄漏）。Asset 阶段是早退形、免疫；`SaveBootstrapStage.cs:67-69` 同形。改为只在首次成功初始化时置位。
- **DATA-003 · P2 · B6**：`DataManager.cs:28` 的 `_impl` 是跨会话静态态，却不在 `AutoInitTests`/`SessionResetTests` 的会话复位族里（默认 `GameLauncher` 路径会在新会话重新注入而自愈）。裁定「补 `ResetForNewSession` + 族登记」或台账 `不做` 并写明理由。
- **DATA-004 · P2 · E/B5**：`DataManagerImpl.cs:19,340,346,353` 的脏集合 `HashSet<IDataBlock>` 用默认 `Equals`；重写 `Equals` 的不同类型块会塌成一条，`GetDirtyBlocks` 漏块 → 官方增量保存配方静默丢改动。改用仓内已两份先例的 `ReferenceEqualityComparer`。
- **DATA-005 · P2 · E/B6**：名称索引删除按名字而非身份（`DataManagerImpl.cs:66,81`）——两个不同类型同名时，`RemoveBlock` 被遮蔽者会删掉占用者的索引（后者无法恢复），`RegisterBlock` 替换路径还会吞掉撞名 `LogError`；`BlockName` 为 null 时在 `_blocks[type] = block` 之后抛 ANE，留半注册态。按身份删索引 + 补齐 null 前置校验。
- **DATA-006 · P2 · A1/D4/E/F**：`DataManagerImpl.cs:171` 的写入端恒用 `Serializer.Default`，读取端按 `defaultFormat`（`:216,258`）；经 `Factory` 子类把 `defaultFormat` 设为非 `"json"` 会让整份存档读不回；单块恢复（`:404`）恒回落 `Serializer.Default.Format`，无格式记忆。裁定写入端格式契约（行为变更）。
- **DATA-007 · P2 · B3/D4**：`ApplySnapshot` 的清空阶段是唯一无保护的一步（`IDataManager` 的 `<returns>` 只列恢复阶段失败原因）；第三方 `OnClear` 抛异常会向外抛、内存停在部分块已清的状态。Save 靠 catch 回滚，直接使用 Data 的第三方没有兜底。补文档或隔离。
- **DATA-008 · P2 · B3/D4**：`DataManagerImpl.cs:143-148` 的 `CreateSnapshot` 直接遍历活字典；`OnSave` 里增删块会抛 `InvalidOperationException` 打断整次存档。照 `ForEachBlock` 先取快照。
- **DATA-009 · P2 · A2/D5**：`DataSnapshot` 的脏集合载体是 internal 字段（`DataSnapshot.cs:49`），第三方 `IDataManager` 实现无法兑现接口的脏集合还原契约；可行绕行（旁表）文档未提。README 限定 + 公开载体问题 `待裁`（公开 API 变更需确认）。
- **DATA-010 · P2 · D4/B3**：`ApplySnapshot` 不防御 `blocks` 里的 null 元素，中途 NRE 且无失败计数。补 null 分支并计数。
- **DATA-011 · P2 · D5（跨模块 · Save）**：块版本过新被 Data 计为失败 → Save 整份回滚并报 `Corrupt` 而非 `VersionTooNew`（`DataManagerImpl.cs:268-274`、`SaveManagerImpl.cs:333`）。需 Save 侧配合或只做文档对齐。
- **DATA-012 · P2 · A1**：`Runtime/Data/README.md:256` 写死「`IDataManager`（16 个成员）」，实际 15，且违反「不写会随开发增长的量」。删计数。
- **DATA-013 · P2 · A1**：`OnClear` 的调用点清单漏了 `ApplyBlockSnapshot`；单块恢复的半态未进「已知限制」。补文档。

### Save

- **SAVE-001 · P2 · B6**：Save（与 Data）不在会话级复位族内，`AutoInitTests.Registry`/`Ledger.md` 无排除理由；默认启动器路径安全，只在「手动 `Initialize` 且无 `Shutdown` 钩子 / 会话未走 `OnDestroy`」时显形，后果含 `_playerId` 陈旧、`_busyFlag` 卡死、加密接线在 File 复位后丢失（编辑器）。手工两遍 PlayMode 若复现 (c) 可升 P1；决定「纳入复位族」或登记「为何排除」。
- **SAVE-002 · P2 · B5/A1**：`.meta.bak` / `.meta.tmp` 不在任何清理面内——侧车也经原子写，`DeleteSlotFilesAsync`（`SaveManagerImpl.cs:1153-1162`）与恢复扫描的孤儿清理（`:1078-1084`）都不删；README「最多四类文件 / 全部配套文件一并清除」不实。按槽位删除轮换的游戏会无界累积。加断言并清理。
- **SAVE-003 · P2 · F/A2**：查询面（`SlotExistsAsync`/`GetSlotMetaAsync`/`GetSlotMetasAsync`）只看载荷，而 `TryLoadAsync` 能 `LoadedFromBackup`——「仅备份可恢复」的槽位被 UI 隐藏且无法删除。查询面反映备份可恢复态，或文档写明。
- **SAVE-004 · P2 · B3/D4**：同一 IO 失败（`IOException`）在列表→`isCorrupted` 条目、单槽位/加载→直接抛，三路径不一致；`TryLoadAsync` 的「预期失败不抛」在文件被占用/权限错误下不成立；`bytes==null` 注释称「解密失败」与 File 契约（null=不存在）不符。统一失败形态并订正注释。
- **SAVE-005 · P2 · 生命周期/B6**：`SaveBootstrapStage.cs:83-90` 的 `_initializedByThisStage = !wasInitialized` 在同一 stage 实例重跑时被覆写为 false（Bootstrap README 明写「失败后重试从头重跑」）→ `Shutdown` 不注销第一轮初始化的门面。`DataBootstrapStage` 同形，Asset 免疫。改为 `|= !wasInitialized` 并补重跑用例。
- **SAVE-006 · P2 · F/B3**：删除跨帧后，读面仍可见已删槽位的元数据。删除完成后同步更新读面或文档说明窗口。
- **SAVE-007 · P2 · A1**：`RecoverAsync` 的线程承诺仍有 5 处与实现相反（`SaveManager.cs:118-119`、`ISaveManager.cs:14-16`、`SaveBootstrapStage.cs:60`、`File/FileManager.cs:99-100`、`BootstrapStageTests.cs:125`）；实际每次读侧车都会 `ReturnToMainThread`（`TryReadSidecarAsync:953-954`），不止「重建侧车」。台账「三处冲突」清单已陈旧。文档一半可立即修，设计层（5 处同步 Delete + 2 处同步 Exists）维持 `待裁`。
- **SAVE-008 · P2 · A1/A2**：`ISaveManager.cs:51-57` 声称「内置实现会检查忙碌并抛异常」，实现无守卫（台账裁定的「不加忙碌守卫」仍成立）。修接口 XML，不改裁定。
- **SAVE-009 · P2 · B1/A2**：`Initialize` 半初始化后重试被忽略（重复初始化告警早退），门面停在半初始化态。补失败回滚或文档化。
- **SAVE-010 · P2 · B4**：公开 API 的**入口线程**要求既未成文也无守卫——`SaveAsync` 在首个 `await` 前就触碰 `DataManager` 与第三方 `IDataBlock.OnSave`；池线程调用会把「只能在主线程调用」的异常指向使用方的 Block。补 README 线程契约（或入口断言）。
- **SAVE-011 · P2 · A1**：`Runtime/Save/README.md` 称「十个文件都是公开面，本模块没有 internal 类型」，实际 `SavePathUtility`/`SaveIntegrity` 是 internal，文件树也未标注。订正。
- **SAVE-012 · P2 · B3/D4**：恢复扫描无异常隔离，一个槽位损坏会中止后续相位。逐槽位隔离并计数。
- **SAVE-013 · P2 · E**：`slot_01.save` 别名（前导零）未在路径校验中拒绝/规范化，可能产生同槽位双文件。明确规则。
- **SAVE-014 · P2 · D3**：删除 API 的取消路径零覆盖。补预取消令牌用例（注意池线程异常陷阱）。
- **SAVE-015 · 待取证 · D2**：`SaveManager.GetSlotMetasAsync(default)` 的 `default` 字面量可能重载二义，需编译复现并配负向控制。

### Asset

- **AST-003 · P2 · B5/D3（与 SCN-003 同一现象，补 Asset 侧根因）**：`YooAssetManagerImpl.cs:575-599` 的「外部卸载后清登记项」分支在真适配器下**不可达**——YooAsset 的 `sceneUnloaded` 同步 `Release()` 后 `SceneObject` 归 default，`operation.SceneObject != handle.Scene` 在清理分支之前先命中；对应用例 `SceneLoadOperationTests.cs:324-336` 是结构性假绿（假件保留 SceneObject）。改为先判 `handle.Scene.IsValid()/isLoaded`，或订阅 `SceneManager.sceneUnloaded` 按场景移除。
- **AST-004 · P2 · B6/A1**：`AssetBootstrapStage.cs:76,89-96` 的所有权标记在**失败路径**不复位——初始化失败后用户手动 init/`SetInstance`，随后 `Shutdown` 会销毁不是本阶段建的那份；字段注释「确实完成了初始化」与实现相反。`SaveBootstrapStage.cs:69` 同形。失败路径复位标记或改注释。
- **AST-005 · P2 · A1**：`Runtime/Asset/README.md:288` 称「标 internal 的四个……其余是公开面」，实际 `AssetManagerImpl`/`YooAssetManagerImpl`/`AssetInitProgressRelay` 也是 internal（同文件 `:319` 自相矛盾）。订正文件树与分类句。
- **AST-006 · P2 · A1/A2**：`ClearPool`/`ClearAllPools` 未进 README API 速查（真正释放内存的必要一步只有半句旁引）。补表。
- **AST-007 · P2 · A1/B3**：`LoadSceneAsync` 在「操作报成功但场景对象无效」的失败分支已补发 1f，违反 `IAssetSceneLoader`「失败路径不补发 1f」的契约。移除补发或改契约。
- **AST-008 · P2 · B2/B6**：会话复位「只丢引用、不 Dispose」，但旧 `AssetManagerImpl` 仍被 `Application.lowMemory` 的实例订阅钉住 → 关闭域重载时跨会话泄漏实例，第二会话低内存事件会回调上一会话的实例。复位时退订或把订阅改为静态转发。
- **AST-009 · P2 · A1**：CLAUDE.md 的豁免清单（三处）缺 `AssetManager.UnloadSceneAsync`（代码自称「第三处宽容豁免」）。规则文本变更需用户确认后补。

### File

- **FILE-003 · P2 · A1**：`Runtime/File/README.md:80` 称「十三个文件都是公开面（`CryptoFileProvider` 是 public），本模块没有 internal 类型」，实际 `CryptoFileProvider.cs:23` 是 `internal sealed`，同页文件树自己标着「（内部）」。与 SAVE-011 是同一模板句，建议同批改。
- **FILE-004 · P2 · B5/A1**：原子写的两个未成文边界——取消/失败会在盘上留下 `.tmp`（`DesktopFileProvider.cs:239-257`）；`ReplaceFileAtomically` 降级路径（`FilePathUtility.cs:154-158`）存在「正式文件短暂不存在」窗口（Settings 已为此加锁并写进 README，File README 未提）。补文档或清理 `.tmp`。
- **FILE-005 · P2 · B3/D4**：移动端 Streaming 的文件/目录枚举静默返回空数组，与「可选能力缺失即告警」不对称；README 已知限制未写。补一条或改为告警。
- **FILE-006 · P2 · D4**：`DeleteAsync` 执行中取消时删除已完成、任务仍抛 OCE（UniTask 尾部检查），文档只说「已进入委托的会跑完」。补一句「可能抛出但删除已完成」。
- **FILE-007 · P2 · A1**：`ConsoleFileProvider` 两个 abstract 成员的 `<inheritdoc/>` 无基成员，生成文档为空。补直接文档。
- **FILE-008 · P2 · D3**：`ReplaceFileAtomically` 降级分支零覆盖，真实 Desktop 只测枚举。开 internal 测试缝或说明不测的理由。
- **FILE-009 · P2 · C2**：平台 switch 缺 `WindowsServer`/`LinuxServer`（Dedicated Server 构建会落到默认分支）。补平台分支或文档写明。
- **FILE-010 · P2 · A1**：`MobileFileProvider` 的写入注释称会重定向到可写域，实际写只读 Streaming 会失败。订正注释/README。
- **FILE-011 · P2 · B3/D4**：`GetDomainRoot` 的空根兜底绕过了 `PrimeRoots` 的空串校验（可达性待取证）。补校验或说明。
- **FILE-012 · P2 · A1**：台账/README 写了会随开发增长的数量（文件数等），订正为不带计数的表述。

### Update

- **UPD-001 · P2 · A1/D4**：`UpdateManager.Clear()`（`UpdateManager.cs:339-355`）未声明「摘除框架内各模块驱动器」的连带责任；Input/Settings/Audio 的驱动器被摘且自记账挡住重建、永不自愈、无日志；UI/Timer/Camera 仅在下一次生命周期事件恢复。Timer/UI README 各自记录过，宿主模块 Update 反而不写。在 XML 与 README `已知限制` 补连带影响与各自恢复入口。
- **UPD-002 · P2 · B6/F**：`Clear()` 未纳入有序待处理操作协议（`UpdateScheduler.cs:997-1006,1274-1283`）——派发回调里 `Clear(); Register(x);` 会先插入并宣告 `OnEnable`、再被 `ClearImmediate` 静默清掉，违背 README「后者为准」与 `Clear`「清空后仍可 Register」；`IsRegistered` 不反映 pending Clear；`FlushPending` 注释失实。把 Clear 做成有序 `PendingOp`（行为变更，需裁定）或文档写明「Clear 恒压过同窗口操作」。
- **UPD-003 · P2 · A2/A1**：门面 `ProcessImmediate` 形参是 `IUpdateable`（`UpdateManager.cs:700,713`），纯 `ILateUpdateable`/`IFixedUpdateable` 节点够不到（内部实现已收 `IUpdateLifecycle`，`UpdateScheduler.cs:899`）；且跨时机节点一次调用会 `OnUpdate`+`OnLateUpdate` 各回调一次，XML 却写「一次更新」。放宽形参属公开 API 变更，需裁定；文档先写清跨时机语义。
- **UPD-004 · P2 · A1/台账**：消费方事实过期——README:318「四个消费方」、`Documentation/Modules/Update.md:63`「LateUpdate/FixedUpdate/Unscaled 零生产使用者」（Camera 已用 `RegisterLate`、Audio/Timer 已用 `Unscaled`；FixedUpdate 仍零）、`OnDisable` 计数 6→7、自动注销用例 9→8。改为不含计数的表述并刷新台账。
- **UPD-005 · P2 · A1**：`OnUpdate`/`OnLateUpdate` 的 `time` 参数 XML（`IUpdateable.cs:93-94,143-144`）与 README:51-55 写 `Time.time`，`Unscaled` 轴实际是 `Time.unscaledTime`；暂停菜单/UI 动画正是该轴。三处一起订正。
- **UPD-006 · P2 · D3**：`CopyNodes` 的「含已禁用」分支零覆盖，`UpdateDiagnosticsTests.cs:8-11` 却称由 `UpdateScheduler*Tests` 覆盖（实际 0 命中）；`IsRegistered` 的 pending-Register 分支无用例；`UpdateTier.Max` 与枚举最大值无机械守卫（新增档位忘改 Max 会被静默钳到 7）。补 3 条用例并删错误注释。
- **UPD-007 · P2 · B5**：跨时机操作在「永不 tick 的调度器」（`timeScale=0` 下的 FixedUpdate）上 `_pendingOps` 无界缓冲（`UpdateScheduler.cs:1085-1094,389`）；当前仓内无实际风险但无上界。加水位告警或文档化。
- **UPD-008 · P2 · 会话复位**：`AutoDriveEnabled` 是跨会话存活的静态状态（`UpdateManager.cs:76-95,125`），`OnQuitting`/`AutoInit` 都不复位——关闭域重载第二会话可能继承 false，整套自动派发静默停摆而 `IsDrivingPlayerLoop`/`IsInitialized` 仍报 true。在会话边界复位（或改文档写明保留选择），补 `SessionResetTests` 断言。

### Config

- **CONF-003 · P2 · A1/D4**：JSON 文件 schema 未文档化（实际要求 `{"Items":[...]}`，`JsonLoader.cs:22`），快速使用缺「AssetManager 需先初始化」前置；「相关模块」只把 Asset 依赖挂在 SO 上。补 schema 与前置条件。
- **CONF-004 · P2 · A1/D4**：9 处 XML/文档与实现口径不符——注入实现需自行派发、异常消息教不存在的 `PreloadAsync`、`IConfigLoader` 宣称可扩展枚举、`IConfigManager` 称反射提取字典、`ConfigLoadedInfo` 示例文案、`IConfigRow` 三键示例、`ConfigLoadHelper` 描述、CSV「可省略 Id 列」、Register null 异常未写。逐条订正。
- **CONF-005 · P2 · D4**：CSV Global 只有表头行时把表头当数据行（`CsvLoader.cs:88-100`），全 string 字段静默得到「字段值 = 列名」。要求 `lines.Length >= 2`。
- **CONF-006 · P2 · B3/D4**：`PreloadGlobalAsync`（自定义 Loader）不校验返回 null（`ConfigManagerImpl.cs:316-321`），而 `RegisterGlobal` 拒绝 null；null 配置被记为已加载。核心路径补 null 检查并抛 `ConfigException`。
- **CONF-007 · P2 · B5/A1**：`RegisterTable`/`RegisterGlobal` 不清理 `_assetPaths`（`:496-534`）→ `CopyLoaded` 报旧路径、下一次 Preload 打假 Warning；README「注入条目 AssetPath 恒为 null」在反方向失真。Register 时移除路径记录并订正文档。
- **CONF-008 · P2 · D4**：非泛型 `RegisterTable(Type, IConfigTable)` 不校验 `rowType` 与 `table.Data` 行类型一致（`:512-522`）→ `IsLoaded`/`CopyLoaded` 报错类型，查询期才炸。构造期校验并抛 `ConfigException`。
- **CONF-009 · P2 · B6/D4（F8 复核）**：F8 仍成立，且新增更硬的一面——Table 侧加入 Global 在途任务时 `_tables[type]` 抛未包装的 `KeyNotFoundException`（`:107-111,199-203`）。台账补记该面，修法给 `InFlightLoad` 记结果种类。
- **CONF-010 · P2 · B6/D4**：等待者广播同步执行续体（`ConfigManagerImpl.cs:694-720`，UniTask `TrySetResult` 同步回调已读源码）；先醒的调用方重入 `Unload<T>()` 会让后续等待者读 `_tables[type]` 抛 `KeyNotFoundException`。让 `InFlightLoad` 携带结果，或把广播移到 `_inFlightLoads.Remove` 之后。
- **CONF-011 · P2 · B3/B5/D4**：`ConfigTable` 三处契约缺口——null 字典抛 NRE、`BuildIndex` 的 null selector 抛 NRE、`IConfigTable.Data` 暴露可变字典使 `Count`/`GetAll` 与 `Get` 脱钩（`ConfigTable.cs:30-46,140-146,336-337`）。补防御并写明只读要求或返回只读包装。
- **CONF-012 · P2 · D3**：Json / SO / 文本读入段与 manifest 正路径零直接覆盖（全部 fixture 注入 `FakeConfigLoader`），正是 CONF-001 长期存活的原因；CSV 是唯一被抽成纯解析缝并覆盖的 Loader。照 `CsvLoader.ParseTable` 先例抽 `JsonLoader` 解析段并补用例。
- **CONF-013 · P2 · A1/D3/台账**：测试注释「审计 F10 已归档」但台账从未有条目；门面完备性注释的固定计数已过期。补台账条目、删计数。
- **CONF-014 · P2 · D5/B5（需实测）**：SO 格式 Global 在 `handle.Dispose()` 后被 `_globals` 长期缓存（`ScriptableObjectLoader.cs:37-67`）；低内存/UI 卸载经 YooAsset `AssetBundle.Unload(true)` 销毁该 SO → `GetGlobal` 返回 Unity 假 null，违背「配置常驻内存」。保活句柄或写进已知限制。
- **CONF-015 · P2 · B6/A1（F15 复核）**：F15 仍成立（失败路径不缓存）但影响低；另有死代码/重复写缓存（`ConfigTypeHelper.cs:22-62`）。建议台账改 `不做` + 重开判据，并清理死代码。
- **CONF-016 · 待取证 · D2**：`PreloadTableAsync<T>("path", default)` 在 `ConfigFormat` 与 `IConfigLoader` 两个重载间可能 CS0121，需编译复现。
- **CONF-017 · 待取证 · C1/上游**：`ConfigLoadHelper` 靠反射读泛型静态类的 `Delegate` 字段（`ConfigLoadHelper.cs:42-48,68-74`），IL2CPP High stripping / AOT 泛型实例化下是否存活未验证；真机构建第一次加载可能 NRE。需真机验证或补 `[Preserve]`/link.xml。

### Lock（开口项复核；2026-09-27 后行为提交为 0）

- **LOCK-002 · P2 · A1**：`Runtime/Lock/README.md:152` 称「本模块没有 internal 类型」，实际 `ActionDisposable`/`EmptyDisposable`/`LockSubjectReferenceComparer`/`LockSnapshotPool` 均为 internal，且同文件下文自引 `ActionDisposable`。与 SAVE-011/FILE-003/CONF-002 同一模板句。
- **LOCK-003 · P2 · A1**：`CopyLockedTypes` XML 称成本 O(n+m)（`LockManager.cs:922`），实现因 `buffer.Contains` 是 O((n+m)²)。订正文档。
- **LOCK-004 · P2 · A1**：`RemoveAllSubscriptions`「三张表」实为两张表 + 组合订阅（`:680`）；`UnboundSubscribedSubjectCount` 把 Global/长寿命订阅一并称为「孤儿订阅」（`LockStateSnapshot.cs:30` / README:229）。订正措辞。
- **开口项复核**：台账三条全部仍开，均维持 `未做`——① `SubscriptionTracker`「全仓订阅只有两处登记点」原文仍在，属 Event 文档（转 Event 切片）；② 跨对象订阅自动退订无 owner 形参，README 已知限制已写，绕行为持句柄/`DisposableBag`；③ `ActionDisposable` 池化 ABA 无代号，README 已写清、无测试钉住。E/F 旧结论仍成立（主体键引用比较器、边沿事件 vs 聚合查询 + `OnLockStateChanged` 补聚合面），2026-09-27 已修项无回归。

### Localization

- **LOC-001 · P2 · D2**：`LocalizationBootstrapStage.cs:44,60` 新增 `(string, TextAsset)` 重载使既有 `new LocalizationBootstrapStage("en", null)` 变 CS0121（Roslyn 同形复现 + 负向控制）；`CHANGELOG.md:576` 只记「新增」，未提示源不兼容。文档补「显式 null 需强转」，或把 TextAsset 入口改静态工厂（破坏性，需裁定）。
- **LOC-002 · P2 · A1**：`Runtime/Localization/README.md:278` 称只有 `LanguageAssetLoader` 是 internal，实际 `LocalizationManagerImpl` 也是（基线无它）。与 SAVE-011/FILE-003/CONF-002/LOCK-002 同一模板句。
- **LOC-003 · P2 · A1/C2**：`LocalizationManagerImpl.cs:491-501` 把合法的带空白对齐格式项 `{0, 5}` 判为非法并转义为字面量，与 README:368「合法的格式项照旧」及 Impl「输出逐字不变」相反（.NET 语义与上游源码已核实；Unity Mono 待实测）。解析器在逗号后跳过空格，或订正文档。
- **LOC-004 · P2 · B6**：在途 `SwitchLanguageAsync` 续体经静态门面写回（`LocalizationManager.cs:192-197`、`LanguageAssetLoader.cs:59-80`）——加载期间 `SetInstance` 会把旧配置的表注入新实例，`Destroy` 会让续体抛「尚未初始化」；会话复位不作废在途任务。让 loader 持发起时实例并加代次守卫，或写进已知限制。
- **LOC-005 · P2 · B1/B3**：`LocalizationManager.cs:66-74` 的 `Destroy()` 无 `try/finally`；第三方 `Dispose` 抛异常后 `_instanceInitialized` 仍 true → `Initialize` 永久早退、调用打到半销毁实例，并中断 `Bootstrap.Shutdown` 逆序清理。补 `try/catch/finally` 或文档写明 `Dispose` 不得抛。
- **LOC-006 · P2 · B3/D4**：空语言表两条入口相反——引导阶段静默装上空表（`IsInitialized == true`、零警告），异步加载器硬失败（`LanguageAssetLoader.cs:71-76`）。`data.Count == 0` 与 null 同路（警告 + 跳过），或统一「空表 = 合法语言」。
- **LOC-007 · P2 · D4**：`LanguageAssetLoader.cs:157-158` 的 JSON 重复键静默后者胜，与本模块「严格于静默丢数据的畸形」判定线不一致。加 Warning（或抛）并写进 README。
- **LOC-008 · P2 · B4/A1**：线程契约只在 README（`:356-358`），四个写入口无守卫；「`Get` 从任意线程读是安全的」缺「无并发写」前提（`Dictionary` 无同步）。订正措辞并评估 Editor-only 断言。
- **LOC-009 · P2 · B3**：空值参数在四个公开入口三种处置——`FallbackLanguage = null` 下次 `Get` 抛裸 ANE；`SwitchLanguageAsync("")` 去加载 `localization/lang_`；`SetPlaceholder(name, null)` 静默替换为空串。按模块判定线补齐校验。
- **LOC-010 · P2 · D3**：两个 fixture 的 `SetUp` 不先 `Destroy`（违反静态门面复位纪律）；`SwitchLanguageAsync` 零直接覆盖；Facade 测试注释成员计数陈旧（13 vs 14）。补 `Destroy`、补两条门面用例、删计数。
- **LOC-011 · P2 · E**：占位符名是无命名空间的全局注册表（`LocalizationManagerImpl.cs:249-273`），两个不相关作者同名即静默覆盖，无告警、无查询能发现冲突。README 补「建议前缀、后注册者覆盖」。
- **LOC-012 · P2 · 台账陈旧**：`Documentation/Modules/Localization.md:33`「InputManager 的 11 处同形手写订阅绑定（未做）」已被 `fc07a5b`（2026-09-30）修复，应改 `已做`；`Input.md` 仍未认领该事实，建议补交叉引用。
- **LOC-013 · P2 · 记录归属**：`IDestroyCancellationToken` 实现范例的坑仍记在 `Localization.md:39`，修法属 Message（已由 MSG-002 认领）。迁移台账条目，Localization 侧留指针。

### Input

- **INP-002 · P2 · B6/B5**：`InputManager.cs:205-212` 的 `ResetForNewSession` 只丢 provider 引用不 Dispose；旧默认 provider 仍挂在 `InputSystem.onDeviceChange` 上（1.19 源码确认静态回调表跨会话存活）→ 关闭域重载后每会话叠加一份设备消息。加 internal 解绑入口（只 `-=`）后再丢引用。
- **INP-003 · P2 · B6/D4**：`RegisterTicker` 盲信 `UpdateManager.Register`；调度器为 null（`Initialize` 早于 AutoInit / `OnQuitting` 之后）时静默空转，`IsInitialized` 为真但 `Observe*` 全哑、零日志。与 UPD-001 同根因；`Register` 静默失败是否改 Warning/Input 侧重试需裁定。
- **INP-004 · P2 · A1**：`Runtime/Input/README.md` 称「14 文件全公开、无 internal」，实际两个实现是 internal（`140d985` 新引入）。与 SAVE-011/FILE-003/CONF-002/LOCK-002/LOC-002 同一模板句。
- **INP-005 · P2 · A1**：README 依赖写 Unity 2022.3 / InputSystem 1.7.0，而包要求 6000.3、manifest 1.19.0。是否把 Input System 纳入包 README 依赖表（牵动安装器与 `DependencyDeclarationTests`）需跨模块裁定。
- **INP-006 · P2 · A1**：类 XML 称「每帧需调 Tick」，与自动驱动矛盾。订正。
- **INP-007 · P2 · A1**：`SetProvider` 不注册帧驱动，模块 README/XML 未写（包 README 有）。补文档。
- **INP-008 · P2 · D4**：`SwitchActionMap` 名写错时先禁用全部 map 再 Warning；`DisableActionMap` 找不到 map 完全静默——失败路径三态不对称。统一为「先校验后动作 + 一致告警」。
- **INP-009 · P2 · B5**：`_playerBindingPaths` 不在重绑定/覆盖变更时失效 → `playerId>0` 重绑定后仍读旧控件。在重绑定/覆盖变更时失效缓存。
- **INP-010 · P2 · F/A1**：`playerId>0` 对 D-pad/2DVector 复合只取第一个子项（`ButtonControl`）→ `ReadVector2` 恒零；`playerId=0` 实为 action 级全设备读取，README「当前活跃手柄/互斥」表述不准确。补文档/完整档（完整档需裁定）。
- **INP-011 · P2 · D4**：`Observe*(null, …)` 订阅成功，之后每帧在 EventStream 抛 `ArgumentNullException`，日志前缀是 `[Event]`。入口补 null 校验。
- **INP-012 · P2 · A1**：空 `bindingId` 回退时 `OnCompleted` 的 Id 恒为 `""`。文档/实现对齐。
- **INP-013 · P2 · B3**：重绑定事件回调无异常隔离（与 `Observe*` 不对称）。统一隔离。
- **INP-014 · P2 · E/F（待裁）**：同名动作跨已启用 map 由资产顺序静默胜出、缓存只按名字失效。加歧义告警或文档写明（完整档需裁定）。

### UI

- **UIA-003 · P2 · B1**：`UIManager.cs:147-196` 的 `ReleaseCurrentInstance` 在旧实例 `Dispose` 抛异常时只置 `_instance = null`，不复位 `_instanceInitialized` → `Initialize` 永久早退、调用照抛。`finally` 里一并复位。
- **UIA-004 · P2 · A1/A2**：`IUIManager.Update` 与门面文档称「遍历所有 IsOpen 面板」，实现只驱动 Tier0（`UIManagerImpl.cs:1409-1420`）；`timeScale=0` 的手动逃生口对分档面板无效。订正文档或让手动入口覆盖全档。
- **UIA-005 · P2 · B3**：语言切换刷新无逐面板异常隔离（`UIManagerImpl.cs:2014-2035`），一个面板的 `OnLanguageChanged` 抛异常会中断其后所有面板刷新，异常落到 `[Event]` 分类。照 `DriveTier` 逐面板隔离。
- **UIA-006 · P2 · B2/A2**：`ShowDialogAsync`/`ShowPopupAsync` 会向「OnOpen 内自关」的面板挂遮罩/捕获层（`UIManager.cs:851-866,928-958`），遮罩成孤儿、捕获层永久吃输入且点击静默 no-op。在挂载前校验面板仍打开。
- **UIA-007 · P2 · B3/D4**：`CloseLayerAsync`/`CloseAllAsync` 对单面板关闭异常无隔离（`UIManagerImpl.cs:741-793`），整批中断、HUD/Tip 已先回收、`AllPanelsClosedMessage` 不发布。逐面板隔离并收尾。
- **UI-B-003 · P2 · A1**：`Runtime/UI/README.md:918,924` 的 Tip 一节仍描述已删除的自驱动帧通路与不存在的 `tipItem.PlayAsync()`。订正。
- **UI-B-004 · P2 · B2**：`View/UIViewBase.cs:62-87,197-206` 的 `Track` 只在回池路径释放；直接销毁面板（框架支持）时订阅永久残留，`UIPanelBinding` 有 `OnDestroy` 兜底而 `_tracked` 没有。补 `OnDestroy` 释放。
- **UI-B-005 · P2 · B5**：`UIHudManager.cs:233-236,187-204,213-220` 的 `_hudMap` 中「已销毁 HUD」条目永远摘不掉（`DetachInternal` 在 `hud == null` 时提前 return，且无 `Clear`）。补清理。
- **UI-B-006 · P2 · B1/B5/F**：回池不复位 `_state`/`IsFocused`（`UIViewBase.cs:197-206`、`UIPanelBase.cs:78,125-139`）——`UIManager.Destroy()` 后被回池的面板仍报 `IsOpen == true`；关闭后 `IsFocused`/`Raycaster` 停在旧值，复位还依赖用户重写时调 `base`。补框架侧复位。
- **UI-B-007 · P2 · A1**：`Data/UIPanelBinding.cs:41-42,56-58` 的 `Unbind` XML 仍说「通过 OnDestroy 自动调用」并要求关闭前 `Unbind`，与现行设计相反。订正。
- **UI-B-008 · P2 · B3**：`UIPanelBinding.Unbind` / `UIViewBase.DisposeTracked` 的释放循环无异常隔离（`UIPanelBinding.cs:59-73`、`UIViewBase.cs:77-87`），第一个抛出会跳过其余全部。逐项隔离。
- **UI-B-009 · P2 · A1**：`Tip/IUITipProvider.cs:30-37` 的 XML 声称 Tip「可被档位降频」，实际 HUD/Tip 恒由 `FrameDriver`（Tier0）驱动，无档位声明通道。订正。
- **UI-B-010 · P2 · D3**：`UIBinder.BindToLocalizedText`（历史泄漏修复点）与真实 `UITipManagerImpl.Update` / `UITipItem.Begin/Tick` 零回归覆盖。补用例（TMP 环境限制先探针）。
- **UI-B-011 · P2 · D4/B3**：HUD 容器创建失败只记 Error 并继续用 null 父节点实例化（`UIHudManager.cs:349-358`），Tip 侧同失败早退。统一为早退。
- **UI-B-012 · P2 · A1**：`Runtime/UI/README.md:745,753` 的核心类型表两处陈旧（`UIManagerImpl` 仍写「排序计数器」、`IViewModel` 仍写「标记型」）。订正。
- **UI-B-013 · P2 · F/B6（待裁）**：对已打开对话框再次 `ShowDialogAsync` 不复位结果通道（`View/UIDialogPanel.cs:70-96`、`UIManagerImpl.cs:576-580`），可能立即返回上一轮结果。需裁定是否重置结果通道。
- **UI-C-002 · P2 · C2/D3**：遮罩与同层第 31 个面板同序（`MaskIndex == MaxIndexInLayer == 31`，面板序号从 1 起），「遮罩挡住该层及以下所有面板」在边界不成立；`UISortingTests:66-70` 用 `MaxIndexInLayer-1` 恰好避开边界。把上限改为 30 或文档写明。
- **UI-C-003 · P2 · C2/A2**：公开 `UISorting.PanelOrder/MaskOrder` 不钳 `layer`：`MaskOrder(2000)=64031` 静默回绕（历史同类事故），内部调用点都先 `Clamp`。公开入口补钳制或文档警示。
- **UI-C-004 · P2 · A1**：遮罩样式「最后一次 `ShowMask` 为准」只写在源码注释/CHANGELOG，使用方 README 未写；`IUIManager` 却称「不再互相踩」。补 README 行为契约。
- **UI-C-005 · P2 · A1/D4**：`SetMaskClickToClose` 只判实例存在不判显示中；`HideMask` 后调用静默无效，与 README「未显示时告警」相反（测试只覆盖「从未创建」）。补显示中判定与告警。
- **UI-C-006 · P2 · B2/F**：捕获层单目标——嵌套弹窗打开后外层失去点外部关闭、关内层不恢复；嵌套子菜单是 `UIPopupPosition` 自己列出的用途。改为栈式或多目标。
- **UI-C-007 · P2 · A2**：`PreconditionChain` 无法接收/透传 `CancellationToken`（`IUIController` 文档要求透传），且该公开类零测试。补令牌重载（公开 API，需 D2 编译探针）与用例。
- **UI-C-008 · P2 · B1/A1**：`UIBootstrapStage` 已初始化早退时静默丢弃构造传入的 controller；README 同时展示 UIRootNode 自动初始化 + 带 controller 登记。补应用 controller 或告警。
- **UI-C-009 · P2 · A1**：控制器文档与实现相反——`OnAfterClose`「已销毁」实为回池（同一成员内自相矛盾）；自我关闭面板跳过 `OnAfterOpen`、after 回调抛异常时状态已提交未写。逐条订正。
- **UI-C-010 · P2 · B3/D5（跨模块 · Asset）**：`AssetPanelFactory.Release` 依赖 AssetManager 存活；`UIManagerImpl.Dispose` 的回收循环无异常隔离 → Asset 已销毁且有打开面板时 `Destroy()` 抛异常并截断清理；`AssetPanelFactory` 在全部测试下零构造。补隔离与测试。
- **UI-C-011 · P2 · D3**：`UIPopupPosition` 测试仅零原点 `screenRect`，`Screen.safeArea` 非零原点与 offset 触发 Auto 翻转未覆盖。补用例。
- **UI-C-012 · P2 · A1**：`UIDefaultController` 及测试注释「五条带字符串插值的日志」已被 `b8395c6` 模板化重构淘汰。订正。

## 6. 跨模块主题

> 以下主题由多个模块的独立发现收敛而来。主题只给共同根因与统一动作建议；单条发现的证据与修法仍在 §3–§5。

### 6.1 会话级复位族不完整：静态状态、订阅与旧实例跨会话存活

- **涉及**：POOL-002（池静态表）、SAVE-001 / DATA-003（Save/Data 门面不在族内）、SER-006（Serialize 注册表）、UPD-008（`AutoDriveEnabled`）、AST-008（旧 `AssetManagerImpl` 被 `lowMemory` 订阅钉住）、LOG-009（帧缓存/注册表）、EVT-005（`SubscriptionTracker` 复位文档失实）、SET-004（句柄订阅流不清）、INP-002（旧 provider 仍挂 `onDeviceChange`）、MSG-004（销毁令牌登记不回收）。
- **共同根因**：会话级复位目前是「按门面逐个补」的增量工作，没有一张「模块 × 静态槽 × 生命周期终点」的清单；「只丢引用不 Dispose」在存在实例事件订阅或第三方实现的 `Dispose` 时并不完整。
- **统一动作建议**：以 `AutoInitTests` 族清单为索引，逐模块补一张静态槽表（写入/读取点、会话边界动作、是否刻意保留）；先裁定框架级问题「会话边界该不该替使用方 Dispose/清订阅」，再按裁定批量补 `ResetForNewSession` 或写明保留理由。相关：`Ledger.md` 的会话复位族条目与 Settings 的「七门面 Dispose」待裁条目。
- **不做什么**：不引入统一的「自动反射复位」框架（与显式登记、零反射的取向冲突）。

### 6.2 随包文档示例与 README 模板句：按示例写必然错/编译不过

- **涉及**：CONF-001（P0，自动属性主键）、TMR-001 / DATA-001（示例缺参/参数顺序错）、SER-001/002（异常类型与覆盖语义写反）、RCT-002/003/004（相等语义、`FloatComparer`、`Keys` 分配）、PIPE-001、AST-002/005、MSG-006、LOC-001、AUD-007/008/009/010、SAVE-011、FILE-003、CONF-002、LOCK-002、LOC-002、INP-004、UPD-004/005、CAM-006、DIAG-006、DATA-012、POOL-003/005。
- **共同根因**：① `Samples~/` 与 XML `<code>` 不参与编译，示例零验证；② 2026-10-16 的 README 骨架重整批量引入了「N 个文件都是公开面 / 没有 internal / 固定计数」等模板句，只做了文件名级守卫；③ 「修一处找齐所有落点」在某些修复里只覆盖了部分落点（AST-002 是 `39a0aa8` 的第四处）。
- **统一动作建议**：先做一轮「文档示例可编译性」清理（P0 CONF-001 优先），再评估把 `<code>` 示例抽成可编译片段或测试的守卫；同时对全部模块 README 做一次模板句清扫（公开/internal/计数），并考虑用 `PublicApiBaseline` 与文件树做机械比对。
- **不做什么**：不为文档内容本身加运行时门禁（手册已明确 `check-docs` 只查结构）。

### 6.3 线程契约只在散文里：执行面缺失且各模块标准不一

- **涉及**：FILE-002（P1，移动端 Streaming）、SAVE-010、SET-002（默认值工厂线程）、MSG-005（淘汰/统计族无断言）、LOG-011（懒建读 Unity API）、LOC-008（读安全缺前提）、RCT-009、TMR-006、INP（Observe 相关）、EVT 的 `待裁`（主线程断言）、SCN-005/Asset 场景入口。
- **共同根因**：框架级已裁定「主线程断言三份副本不抽公共防线」（Message/Pipeline/Asset），但这条裁定被理解为「其余模块不必有」；同时若干「任意线程」承诺没有限定条件（并发写、移动端 Streaming、懒建路径）。
- **统一动作建议**：逐模块做一次「契约措辞 × 执行物」对照：要么加 Editor-only 首错断言（照既有三份之一，不抽公共防线），要么明确写「本模块不做断言 + 违约后果」。优先修 FILE-002 与 SAVE-010 这类「承诺任意线程但实现会抛」的硬冲突。
- **不做什么**：不新增第四种断言形态，不把断言抽成公共防线。

### 6.4 取消与失败路径的不对称

- **涉及**：AST-001（P1，资产加载取消泄漏 YooAsset 句柄）、SCN-001（P1，加载成功后回收段受调用方令牌约束）、SAVE-004/006/014、LOG-001/003、MSG-001（派发中回收）、POOL-001（回调异常账本不回滚）、CONF-005/006/010、LOC-005/006/009、INP-008/011、RCT-006。
- **共同根因**：取消/异常后的「资源与状态善后」按路径各写一遍，没有按模块维护「失败路径 × 状态一致性」矩阵；同一失败在查询面/事件面/资源面给出不同观测。
- **统一动作建议**：先修两条 P1（Asset 取消释放、Scene 回收段令牌），再把各模块的失败路径矩阵作为修复批次的前置产物；对「加载成功后的后处理」「批量资源的逐项释放」「异步在途与 Dispose/Reset 的交互」统一成可判定的契约。
- **不做什么**：不要求所有 API 都支持取消（现有「不做取消」的取舍继续有效）。

### 6.5 可选能力接口与装饰器语义

- **涉及**：SET-001（P1，加密装饰器遮蔽 `IAsyncSettingsStore`）、FILE-001（P1，`ICryptoProvider.Decrypt` 的 null 契约）、CLAUDE.md 的装饰器不变量；正面先例 `CryptoFileProvider`。
- **共同根因**：装饰器的能力转发与「判定权归门面」只在 File 侧被系统执行过；Settings 侧未跟进，且可选接口的失败语义（null/异常/降级）没有统一约定。
- **统一动作建议**：按 CLAUDE.md 两条不变量审计全部装饰器（实现底层可选接口、门面只对 `_baseProvider` 探测）；为可选接口补「失败语义」章节（返回 null 还是抛、由谁转译）。
- **不做什么**：不给主接口加成员来表达可选能力（现有形态继续有效）。

### 6.6 测试假绿与真实缝覆盖不足

- **涉及**：AST-003（场景外部卸载用例假绿）、SCN-006（场景泄漏）、INP-001（`FakeInputProvider` 掩盖计时路径）、AUD-005/006/014（恒真断言、真实加载缝零覆盖）、CONF-012（Json/SO 正路径零覆盖）、POOL-008（重跑红/Editor-only 当普遍契约）、SET-016/017（Assume 吞失败、转发零行为覆盖）、TMR-008、LOG-007、RCT-008、MSG-008、DIAG-005、EVT-006、PIPE-003/D3、SCN-001 的 fake 绕开令牌。
- **共同根因**：替身按「接口方便」而非「生产语义」实现；断言存在但缺判别力；「改前必红」只在新修复里执行，存量守卫没有负向控制。
- **统一动作建议**：按风险面补真实缝/结构断言，而不是追求覆盖率数字；对每条新增守卫配一次负向注入；优先修被假件掩盖的生产路径（Asset 加载族、Input provider、Config Loader、Save 文件 Provider）。
- **不做什么**：不为覆盖率而补无判别力的用例。

### 6.7 键空间与相等语义：全局裸键的静默合并

- **涉及**：POOL-006（简单类型名作日志分类键）、LOG-005（分类名全局裸字符串、外部标签只增）、LOC-011（占位符全局命名空间）、LOCK 的 `lockType`（已文档化）、Message 的 Key 全局性（文档建议）、Config 的 Type 键（安全）。
- **共同根因**：凡「按名字寻址」的全局表，同名即合并/覆盖，且没有查询面能发现冲突；日志分类与占位符尤其容易跨系统相撞。
- **统一动作建议**：在 README `行为契约` 统一写明「全局命名空间 + 建议前缀」；Pool 的日志分类改用 `FullName`（可观察文案变更，`待裁`）；不新增枚举/所有权 API（生命周期问题大于收益）。
- **不做什么**：不把分类/占位符改成强类型句柄（会破坏字符串配置与迁移面）。

### 6.8 公开 API 纪律：重载源兼容、能力接口收窄与迁移表

- **涉及**：LOC-001（新增 `TextAsset` 重载使 `(string, null)` 变 CS0121，CHANGELOG 只记新增）、UPD-003（门面形参收窄 `IUpdateable`）、POOL-010（公开面过宽）、TMR-007（缺 `IEquatable`）、DATA-009（脏集合载体 internal）、SER 报告的 `PublicApiBaseline` 对 sealed 类 virtual-final 的盲区、Cinemachine 扩展包的 `Unregister` 语义。
- **共同根因**：公开 API 基线只守「形状增删」，不守**重载决议的源兼容**；迁移表与基线之间没有机械联系；「新增重载」被当作纯新增。
- **统一动作建议**：对涉及重载的公开面变更补一次调用形状编译探针（含负向控制），并写进迁移表；评估把 `PublicApiBaseline` 扩到「重载集合稳定性」；能力接口收窄统一走「单独确认 + 基线更新 + 迁移表」。
- **不做什么**：不把扩展包纳入核心门禁（既有边界继续有效）。

### 6.9 载体与多观测面：查询面与事件面/资源面的分歧

- **涉及**：POOL-001（回调面已报归还、`CountActive` 未变）、PIPE-003（终局广播时 `Status` 仍 Running）、DIAG-002（注册表空但复制旧报告）、CAM-001/005（`IsActive` 与 `OutputCamera`）、Localization/Reactive 的查询面与事件面粒度差、Data/Save 的查询面与索引面。
- **共同根因**：状态更新与事件/通知的先后顺序、以及「查询面是否反映资源真实可用性」没有统一约定。
- **统一动作建议**：按模块把「落位先于事件」写成契约（PIPE-003 是先例），并让查询面显式回答「资源是否仍可用」（CAM-001）。
- **不做什么**：不引入全局事件总线来统一观测面（模块边界继续有效）。

## 7. 逐模块审计记录

> 每个模块记录：范围与输入、18 类判据留痕、旧结论复核（仅列偏差）、开口项复核（仅列变化/建议）、第三方对照（简述）、残留与不确定。发现明细见 §4/§5。

### Event（全量 A–F，首次）

- **范围**：`Runtime/Event/` 全部 9 个文件 + README + 4 个消费方（Message/Reactive/Input/Settings）+ 3 个测试 fixture；边界 SHA 上只读。
- **留痕**：A1 命中 EVT-003/004/005；A2 扫过无问题（无门面/接口双层，接口承诺逐条有实现与用例）；A3 扫过（人工扫描，未跑门禁）；B1 扫过（`Complete`/`Dispose` 后行为幂等，守卫覆盖缺口见 EVT-006）；B2 扫过（回池配对 `Unsubscribe↔Return`，派发中退订只置标志）；B3 命中 EVT-002（`onEmpty` 异常未隔离为残留）；B4 命中待裁项（主线程断言；复核建议收口为 Editor-only 断言或明确不做——原「每帧热路径付分支」成本论据在 Release 不成立）；B5 命中 EVT-001（EVT-007 为消费侧）；B6 不适用（无代际协议，静态池/计数见 B5）；C1 扫过（`Emit` 热路径零分配；订阅每次一个句柄对象见 EVT-003）；C2 不适用；D1 扫过（四个消费方单向依赖）；D2 不适用；D3 命中 EVT-006 + 两处缺口（`onEmpty` 派发中触发无回调级用例、LIFO 顺序靠 Reactive 测试间接覆盖）；D4 命中 EVT-002；D5 不适用；E 不适用；F 扫过（`onEmpty` 1→0 边沿与 `SubscriptionCount` 契约已写清）。
- **旧结论复核**：「不为 Event 补 PlayMode 用例」理由「无跨会话静态态」**部分失效**（有三处静态态），结论可维持，理由需订正；其余沿革/否决条目仍成立。
- **开口项**：`待裁`「主线程断言」建议收口（需用户裁定）；`不做`「不合并两份 internal 实现」维持；`暂不做`「不拆观察面/投递面」维持。
- **第三方对照**：与 Rx 系相比，本仓刻意 LIFO、无算子面、只重放最近 1 条、异常隔离且保留订阅；`onEmpty` 为 Message 通道回收依赖的本仓特有面。
- **残留**：EVT-001 白盒用例、EVT-007 的 Message 侧用例、`onEmpty` 派发中触发与异常处置、并发退订窗口均需运行期证据。

### Serialize（全量 A–F，首次）

- **范围**：`Runtime/Serialize/` 4 个文件 + README + Data/Save 消费方 + 测试；确认 Config 零引用。
- **留痕**：A1 命中 SER-001/002/003（另有一处「支持多态」措辞过度未立项）；A2 命中 SER-004；A3 扫过（人工；`JsonSerializer` 与 Newtonsoft 同名靠命名空间优先解析）；B1 命中 SER-001（异常消息教 no-op）；B2 不适用；B3 命中 SER-004；B4 命中已文档化（无断言，与 Event 同族，建议随 Event 待裁一起裁定）；B5 命中 SER-006；B6 不适用；C1 扫过；C2 不适用（JsonUtility 静默丢字段已文档化）；D1 扫过（Data/Save→Serialize，Config 不在依赖图）；D2 不适用；D3 命中 SER-005；D4 命中 SER-004；D5 不适用；E 命中已文档化（格式名裸字符串键，覆盖/保留两条语义已写）；F 命中并入 SER-001（`IsInitialized` 与 `Default` 可用性双面不一致）。
- **旧结论复核**：「不为 Serialize 补 PlayMode 用例」理由「无跨会话静态态」**部分失效**（两个静态字段、无复位）；`RegisterBuiltin` 不覆盖使用方、内置默认表、AutoInit 档位均仍成立。
- **开口项**：`已做` 内置注册不覆盖使用方——维持；`不做` 不补 PlayMode——结论维持、理由订正；SER-006 是否补会话复位建议立 `待裁`。
- **第三方对照**：按格式名取用的注册表无直接对标物；同步字节数组契约与 Newtonsoft/JsonUtility 一致；空输入语义刻意对齐 JsonUtility 但未写进 README。
- **残留**：`JsonUtility` 畸形 JSON 行为、Newtonsoft 多态边界、`PublicApiBaseline` 对 sealed 类 virtual-final 的渲染盲区（Architecture 侧复核）。

### Scene（全量 A–F，首次；文档「审计轮次」实为实施轮）

- **范围**：`Runtime/Scene/` 5 个文件 + README + Asset 场景接缝（门面/实现/句柄/能力接口/YooAsset 2.3.18 源码）+ 测试。
- **留痕**：A1 命中 SCN-001 的 XML 承诺、SCN-002、SCN-005（另有一处「每个源文件都写别名」措辞过宽）；A2 扫过（静态门面无自建接口，`IAssetSceneLoader` 两成员均可达）；A3 扫过（人工）；B1 扫过（未初始化 IOE、无效场景 false、重复卸载 false、default 句柄安全）；B2 扫过（`Dispose` 即发即忘有 try/catch 收口）；B3 命中 SCN-001/004；B4 命中 SCN-005；B5 命中 SCN-003（跨模块 Asset）；B6 不适用；C1 扫过（每次加载一次分配，非每帧）；C2 不适用；D1 扫过（Scene→Asset 单向）；D2 不适用；D3 命中 SCN-006 + 三处缺口（清理段取消、激活失败、外部卸载后的登记表）；D4 命中 SCN-001/002/004；D5 命中 SCN-003/005；E 扫过（句柄 Id 同时校验 Id 与 Scene 对象）；F 扫过（无事件面）。
- **旧结论复核**：v1 功能面与十条「不做/暂不做」全部仍成立；实施轮的两条未覆盖记录仍成立，SCN-003 新增一条缝之上缺口。
- **开口项**：无状态变化；新增建议开口项 SCN-003（登记表清理）、SCN-005（主线程断言/理由订正）。
- **第三方对照**：加载结果不如 Addressables（无失败原因）；卸载刻意无引用计数；外部卸载感知不如 Addressables（句柄响应式、登记表不是）。
- **残留**：SCN-001/004/006 的用例、SCN-003 的内部登记表观测、真 YooAsset 包人工验收、`SceneManager` 同名规避无机械守卫。

### Pipeline（全量 A–F，首次）

- **范围**：`Runtime/Pipeline/` 全部 12 个文件 + README + Bootstrap 消费方 + 测试 + UniTask 2.5.11 源码核对。
- **留痕**：A1 命中 PIPE-001/002/004/005/006；A2 扫过（工厂非转发门面，12 成员与基线一致）；A3 扫过（人工）；B1 命中 PIPE-006（轻）；B2 不适用；B3 部分命中 PIPE-002（五条失败路径仅超时缺阶段名）；B4 扫过（Editor-only 主线程断言有执行物与用例）；B5 扫过（阶段表去重、上下文每轮重建）；B6 扫过（无全局静态槽；Destroy 代际为契约外路径）；C1 扫过（无帧泵、进度事件驱动、节流广播）；C2 扫过（`Clamp01` 拦 NaN、Realtime 有 PlayMode 用例）；D1 扫过（仅依赖 XLog）；D2 扫过（无重载二义）；D3 命中（超时断言 Contains 掩盖 PIPE-002 等三处）；D4 命中 PIPE-004；D5 命中（Bootstrap 子阶段超时，修法归属需补一句「可用公开 API 装饰绕过」）；E 不适用；F 命中 PIPE-003。
- **旧结论复核**：`FailureReason` 带阶段名**部分失效**（超时分支名字为空）；其余七条仍成立；「WhenAny 观测全部任务」经 UniTask 源码核实属实。
- **开口项**：五条 `不做` 与一条 `已做` 维持；Bootstrap 的「Pipeline 侧子阶段粒度超时」维持 `未做`，建议在两端已知限制各补一句「可装饰绕过/仅顶层阶段粒度」。
- **第三方对照**：容器树介于 MediatR（纯串行）与 Dataflow（图）之间；超时乐观语义与 Polly 一致；失败取首失败诊断。
- **残留**：PIPE-002 断言写法确认、PIPE-005 并发串台实测、PIPE-003 终局广播观测、PIPE-006 销毁语义裁定。

### Pool（全量 A–F，首次）

- **范围**：`Runtime/Pool/` 全部 14 个文件 + README + 消费方（UI/Diagnostics）+ 测试。
- **留痕**：A1 命中 POOL-003/004/005/009；A2 命中 POOL-010（反向缺口）；A3 扫过（历史未转义已修）；B1 命中 POOL-007/009b；B2 扫过（生命周期回调同步，无回池回调配对）；B3 命中 POOL-001；B4 扫过（文档明写「不做线程断言」为有意边界）；B5 命中 POOL-002；B6 命中 POOL-002；C1 扫过（热路径零分配）；C2 扫过（无引擎取值范围跨界）；D1 扫过（仅依赖 XLog）；D2 扫过（无重载二义）；D3 命中 POOL-008；D4 命中 POOL-007/009/001；D5 扫过；E 命中 POOL-006；F 命中并入 POOL-001。
- **旧结论复核**：15 条已做/不做均维持；「`Clear()/ClearAll()` 不触发丢弃回调」**部分成立**——`RemovePool` 同样丢弃闲置实例但未列入。
- **开口项**：「集合池没有 `RemovePool`」建议改 `暂不做` 并写重开判据（与 POOL-002 复位入口关联）；五条 `不做` 维持。
- **第三方对照**：生命周期回调/丢弃回调/诊断投影/using 包装比 UnityEngine.Pool 完整；归还拒收可观测性不如 Microsoft；线程模型一致（明确不安全）。
- **残留**：POOL-002 跨会话手工验收、POOL-001 的 generator 返回 null、POOL-008 同会话重跑、POOL-006 `FullName` 文案变更、POOL-010 公开面收窄需单独确认。

### Diagnostics（全量 A–F，首次）

- **范围**：`Runtime/Diagnostics/` 11 个文件 + `Editor/Diagnostics/` 窗口/渲染/18 页签 + 示例 + 测试。
- **留痕**：A1 命中 DIAG-001/004/006（另有一处「只显示一条错误提示」措辞部分失效）；A2 扫过；A3 扫过（人工）；B1 不适用；B2 扫过（DIAG-003 的 `Clear` 不可恢复）；B3 扫过（一处登记期 Title 异常未写进 `<exception>`）；B4 扫过（框架级已裁定不加第四份断言）；B5 命中 DIAG-001/003；B6 不适用；C1 扫过（无运行时每帧路径，诊断路径允许分配）；C2 不适用；D1 扫过（Runtime 零 GUI 依赖）；D2 扫过；D3 命中 DIAG-005/003；D4 命中 DIAG-002；D5 扫过（8 项跨模块回读面仍开放）；E 扫过（引用身份去重，标题不是键）；F 命中 DIAG-002。
- **旧结论复核**：示例「登记是幂等的」**已失效**；「Collect 异常只显示一条提示」部分失效；其余 8 条仍成立。
- **开口项**：3 条 `未做` 与 5 条 `不做` 维持。
- **第三方对照**：显式登记零反射为刻意取舍；纯数据契约、拉模式、无运行时覆盖层均为刻意。
- **残留**：DIAG-001 关闭域重载手工复现、DIAG-002 重绘时机、DIAG-003 恢复入口取舍、门禁与测试命令。

### Camera（全量 A–F，首次；「审计记忆」不是审计轮）

- **范围**：`Runtime/Camera/` 全部 6 个文件 + README + 测试 + Cinemachine 扩展包接缝与 Cinemachine 3.1.7 源码核对。
- **留痕**：A1 命中 CAM-001/003/005/006；A2 扫过（14 成员门面转发完备、`CameraFacadeCompletenessTests` 守）；A3 扫过（人工）；B1 命中 CAM-002；B2 扫过（ticker 三终点注销，会话边界由 UpdateManager 清）；B3 命中 CAM-002/003；B4 扫过（框架级裁定不加断言；调用直达 Unity API）；B5 命中 CAM-001；B6 扫过（代际实现正确，测试缺口 CAM-004）；C1 扫过（过渡每帧纯值运算）；C2 命中 CAM-003；D1 扫过（仅依赖 XLog/XUpdate，核心零 Cinemachine）；D2 扫过（`Activate(default)` 形状需编译复现，不改变既有调用）；D3 命中 CAM-004；D4 命中 CAM-001/003；D5 命中 CAM-005；E 扫过（Ordinal 命名 id，句柄相等性 = Id+Generation）；F 命中 CAM-001/005。
- **旧结论复核**：`duration <= 0` 文档、`OutputCamera` null 语义、`Unregister` 活动状态、被销毁相机表现**部分失效**；句柄代际、过渡位姿恢复、Scaled 时间轴、懒注册、会话复位、核心零 Cinemachine、同名风险 7 条仍成立。
- **开口项**：4 条 `已做` 与 9 条 `不做/暂不做` 维持；CAM-005 需新增处理条目（扩展包接缝）。
- **第三方对照**：框架只补统一入口与状态查询；过渡/跟随/瞄准归后端；无栈/分屏为刻意。
- **残留**：CAM-001/003 运行期确认、CAM-004 负向注入、CAM-005 语义裁定、扩展包内部不在射程。

### Message（全量 A–F，首次）

- **范围**：`Runtime/Message/` 全部 14 个文件 + README + 测试（8 Runtime + 1 Editor）+ Event 上游。核实确属首次 A–F（台账只有 1 条测试尺子条目）。
- **留痕**：A1 命中 MSG-002/003/006（兼 MSG-005/009 的文档半边）；A2 命中 MSG-009（轻）；A3 需实测→**基线门禁已排除 CS0419**（HEAD 上 `check-docs -Enforce` 为 0）；B1 扫过（`Clear` 后可再用；旧句柄安全 no-op）；B2 命中 MSG-001/003；B3 命中 MSG-007（轻）；B4 命中 MSG-005；B5 命中 MSG-001/004；B6 扫过（`Clear`/`SetInstance` 换 broker 无跨代写回）；C1 扫过（稳态零分配，`PublishAsync` 唯一数组分配已注释）；C2 扫过（仅依赖 UniTask 的主线程判定）；D1 扫过（只引用 Event 公开面）；D2 扫过（两处二义由重载形状化解，有运行期用例钉住）；D3 命中 MSG-008；D4 命中 MSG-003/007（轻）；D5 命中 MSG-003（`IBufferedEventStream` 缺「清缓存保订阅」原语）；E 扫过（Type 键安全；Key 是全局裸值，建议 README 补一句「Key 的全局性」）；F 扫过（统计口径差逐条有文档与互校验用例）。
- **旧结论复核**：`IDestroyCancellationToken` 示例坑**仍成立且落点错误**（记在 Localization）；「断言覆盖不到三处」**部分失效**（漏了淘汰/统计族）；「无值缓冲通道不可回收」「缓冲内存无界」两条已修且复核属实；其余 5 条维持。
- **开口项**：`Localization.md` 的 `未做` 条目迁回本模块；新增 `待裁` MSG-003（是否向 Event 要原语）；MSG-004 建议直接修；MSG-009 建议删除（无消费者的 internal 入口）。
- **第三方对照**：类型/Key 通道、缓冲最后一条、异步、请求-响应、全局过滤器齐备；DI/作用域、跨线程投递、算子面、节点作用域刻意不做；淘汰纪律是本仓特色（MessagePipe 用作用域自动释放解决同一问题）。
- **残留**：MSG-001 红证（PlayMode 用例）、MSG-004 登记累积实测、MSG-003 行为锁定用例、两处 D2 二义的负向控制未入库。

### Reactive（全量 A–F，首次）

- **范围**：`Runtime/Reactive/` 全部 11 个文件 + README + 4 个测试 fixture + Event 上游；全同步纯托管。
- **留痕**：A1 命中 RCT-002/003/004/010-文档面/011；A2 扫过（无静态门面，四接口成员全部可达；一处隐含契约并入 RCT-011）；A3 扫过（人工）；B1 扫过（Dispose 后读宽容/写抛，异常消息可照做，4 类型幂等）；B2 命中 RCT-001（立即回调异常清理路径正确，0.3.0 已修）；B3 命中 RCT-006；B4 命中 RCT-009；B5 命中 RCT-001；B6 不适用（零静态态）；C1 命中 RCT-010（其余热路径零 LINQ/闭包，Event 快照池化）；C2 不适用；D1 扫过（仅引用 Event 公开面）；D2 扫过（既有 CS0121 已知项，不复报）；D3 命中 RCT-008；D4 命中 RCT-006 + RCT-011；D5 扫过（键生命周期 `暂不做` 的两条硬阻断仍成立）；E 命中 RCT-002/005/007；F 命中 RCT-005/006。
- **旧结论复核**：0.3.0 的「立即回调异常泄漏」「集合零装箱」「Value 读宽容/写严格」「TryWriteValue」修复均在位；「字典键稳定」**部分失效**（键实例不替换）；列表「引用类型即引用相等」**已失效**；`Keys`/`Values`「每次访问分配」**已失效**；其余 7 条维持。
- **开口项**：10 条全部维持原状态；新增 `未做`（字典值比较器注入）与 `待裁`（链式 `Select` 中间派生值释放形状）。
- **第三方对照**：立即回放、去重、只读派生、集合变更流、字典值去重为本仓特征；不做 `Where`/`CombineLatest`/聚合算子、Move/分桶/batch、集合 `Select`、线程安全容器、创建栈跟踪（均为刻意）。UniRx/R3 列本轮为转述、未复核。
- **残留**：RCT-001 红证、RCT-002/005 的 BCL 语义探针、RCT-006 部分重排实测、RCT-010 装箱量、RCT-004 视图缓存、RCT-009 裁定、D2 编译复核。

### Timer（全量 A–F，首次）

- **范围**：`Runtime/Timer/` 全部 6 个文件 + README + 11 个 Runtime 测试文件 + Update 依赖（静态闩锁/`ProcessImmediate`）。
- **留痕**：A1 命中 TMR-001/003/004/009/011；A2 扫过（无 `ITimerManager` 是台账 `不做`；句柄全部经门面可达；公开面与基线一致）；A3 扫过（人工，最近一次存量清理在 `b9ad5f1`）；B1 扫过（`default`/已释放/换核心句柄全部短路；`UpdateManager.Clear` 失联可恢复）；B2 扫过（`ReleaseSlot` 清委托/状态，异常收尾带代际守卫）；B3 扫过（回调异常与扫尾异常都收口，不漏给 Update）；B4 命中 TMR-006；B5 命中 TMR-005；B6 扫过（全局单调 `_nextTableId` 使旧句柄必然落空）；C1 扫过（每帧路径零 LINQ/闭包/装箱，分配有结构断言 + 粗尺子）；C2 命中 TMR-002/003/009；D1 扫过（无跨模块 Internal，常量副本见 TMR-009）；D2 扫过（无二义；TMR-001 是参数类型不匹配）；D3 命中 TMR-008；D4 命中 TMR-005/002/003；D5 命中 TMR-003（修法在 Update）；E 命中 TMR-007；F 命中（轻，TMR-004-3/-5 的文档口径）。
- **旧结论复核**：「锚点落在 Create 那一刻」**部分失效**（逻辑时间成立、真实时间可提前）；「同轴创建不受影响」**失效**（TMR-002）；`CancelAll` 归零逻辑时钟**失效**；「唯一一处跨模块常量耦合」**失效**；`ProcessImmediate` 措辞**部分失效**；其余 4 条维持。
- **开口项**：7 条中 6 条维持并需订正措辞；「`UpdateManager.Clear()` 失联」建议由 `待裁` 改判为可做（`UpdateManager.IsRegistered` 已提供 O(1) 检测面）。
- **第三方对照**：可查询剩余量、可复用零 GC 句柄、固定间隔不漂移、双时间轴、自动 LOD 为核心差异；不设上限、不做 Pause、不做帧数定时器、不按 owner 分组均为刻意。未找到做过同等自动 LOD 的开源 Timer 库（如实标注）。
- **残留**：TMR-002/003 的改前必红用例与 `-Filter Timer` 实跑、门禁、零分配粗尺子复跑、档位周期复跑、`UpdateManager.Clear` 恢复路径实跑、低帧率安全系数。

### Log（全量 A–F，首次）

- **范围**：`Runtime/Log/` 全部 20 个文件 + `Editor/` 日志面板 + README（JSONL schema/线程契约）+ 8 Runtime + 5 Editor 测试文件。
- **留痕**：A1 命中 LOG-008（8 条）/004 文档半边/003 措辞；A2 命中 LOG-008/010（门面完备性本身通过，且查元数）；A3 扫过（基线门禁在 HEAD 绿；手工核对约 40 个 cref）；B1 部分命中 LOG-008-2/3（`Shutdown` 后仍可用 vs「不可用」措辞）；B2 命中 LOG-006；B3 命中 LOG-001/002/003；B4 命中 LOG-011 + LOG-009 的无屏障读；B5 命中 LOG-001/005；B6 命中 LOG-009（轻）；C1 命中 LOG-004（其余未启用路径零分配有探针 + 尺子实证）；C2 命中 LOG-001；D1 扫过（零模块依赖）；D2 扫过（无同元数二义）；D3 命中 LOG-007；D4 命中 LOG-003/010/008-7；D5 不适用（零模块依赖为有意取舍）；E 命中 LOG-005；F 命中 LOG-003。
- **旧结论复核**：18 条中 17 条维持；「会话级复位已做」**部分成立**（帧缓存/注册表名字未复位，LOG-009）；Editor 两处无前缀 `Debug.LogError` 仍在（行号订正为 `:83,134`）。
- **开口项**：3 条 `待裁` 全部维持（行号订正）；新增建议 `未做` 4 条（LOG-001/002/010/011）、`待裁` 2 条（LOG-003/006）。
- **第三方对照**：分类过滤、未启用零格式化、固定 JSONL schema、引擎日志统一、任意线程写入、诊断回读为本仓特征；per-sink 档位、后台写线程、第三方日志库、`[Conditional]`、替换 `logHandler`、命名参数（待裁）均刻意不做。Serilog/NLog/ZLogger 为常识对照、未读源码；UnityCsReference 读的是 master（与 6000.4 有漂移）。
- **残留**：LOG-001 PlayMode 复现、LOG-002 崩溃形态单跑、LOG-011 工作线程探针、域重载退出时文件句柄、多实例同目录、`_session` 一致性无守卫、门禁复跑。

### Settings（全量 A–F，首次）

- **范围**：`Runtime/Settings/` 全部 18 个文件 + README + 17 个 Runtime 测试文件 + Message/Event/File/Update/Reactive 接缝。
- **留痕**：A1 命中 SET-002/004/011/012（待实测）；A2 命中 SET-001/003/006/007；A3 扫过（人工；门禁未跑）；B1 命中 SET-003/007/008；B2 命中 SET-005；B3 命中 SET-005/009/015（另记 `ICryptoProvider.Decrypt` 违约异常不隔离）；B4 命中 SET-002/013/018；B5 扫过（`Refs` 无淘汰是成文取舍，`DefaultPathClaims` 有界）；B6 命中 SET-004（其余静态槽逐个清点无漏网）；C1 扫过（读写路径零 LINQ/闭包/装箱）；C2 命中 SET-012/013；D1 扫过（全部公开命名空间）；D2 命中 SET-015（需编译复现）；D3 命中 SET-016/017；D4 命中 SET-009（SET-011 补理由句）；D5 扫过；E 命中 SET-010；F 命中 SET-003/006。
- **旧结论复核**：会话级复位仍成立但新增 SET-004 缺口；`DefaultPathClaims` 不清理维持；旧管理器被 `wantsToQuit` root 维持 `待裁`（补充「旧引用仍可写旧 store」）；`TryWriteValue` 不抛**部分失效**（SET-003/006）；加密装饰器「可叠任意后端、能力全保留」**部分失效**（SET-001）；`SaveOnQuit` 编辑器触发事实**证据不足**（SET-012）；其余 7 条维持。
- **开口项**：2 条维持；新增 SET-010 修复项与 SET-004 的 `待裁`（会话边界清订阅流）。
- **第三方对照**：强类型 POCO + 字段级句柄、JSON 原子写 + 一代备份、可选校验/迁移/加密、自动保存兜底为本仓差异点；平台存档后端、管理器整体替换、运行时 Watch 刻意不做；PlayerPrefs/MEO/VContainer 为有限对照。
- **残留**：SET-012 手工探针、SET-015 编译复现、SET-001/002/005/008/014 的改前必红用例、跨会话 SET-004 手工验收、门禁复跑、`SettingsPauseNotifier` 宿主计数隔离。

### Audio（全量 A–F，首次）

- **范围**：`Runtime/Audio/` 全部 17 个文件 + README + 15 个 Runtime 测试文件 + Asset/Update 接缝。
- **留痕**：A1 命中 AUD-007/008/009/010；A2 命中 AUD-007/012（门面完备性本身通过）；A3 扫过（人工）；B1 扫过（`Destroy` 先置空再 Dispose；唯一例外 AUD-006 的 `CopyChannels`）；B2 命中 AUD-002；B3 命中 AUD-003/004；B4 扫过（README 明写「不做线程断言」）；B5 命中 AUD-001/002；B6 扫过（代际过滤逐点核对）；C1 扫过（每帧 `Sweep` 零分配）；C2 需实测（AUD-013、`StartingTimeoutTicks` 的帧率相关墙钟）；D1 扫过（Asset/Update 公开面）；D2 扫过（无重载二义）；D3 命中 AUD-005/006/014；D4 命中 AUD-004/006/007；D5 扫过；E 扫过（通道名是设计上的共享键）；F 扫过（`IsPlaying` 与计数的粒度差已文档化）。
- **旧结论复核**：「`Play` 加载失败返回 default」**已失效**；「接口实现方加载失败返回 default」**部分失效**；「通道首次播放时创建」**已失效**；「Stop 后代际递增」**已失效**；「前九个公开面」**部分失效**；「Tier1 对音效池无感」**部分失效**（AUD-001）；`AudioListener.pause` 连带暂停**证据不足**；其余 4 条 2026-10-01 实测记录维持。
- **开口项**：6 条维持；`StartingTimeoutTicks` 因 AUD-001 更值得关注；`DumpState` **部分失效**（`CopyChannels` 已落地，剩逐 voice 明细）；`Play`/`PlayAsync` 不对称维持 `待裁`，但 AUD-007 的文档矛盾须先修。
- **第三方对照**：句柄代际、通道音量三档、池化播放源为本仓特征；不窃取/无优先级、不做 crossfade/后台暂停/借用外部 AudioSource/3D 跟随/Mixer/`Play(AudioClip)` 均为刻意（FMOD/Wwise 只作能力面对照、未读源码）。
- **残留**：P-01…P-10 探针（短音效、`Clear` 后回收、销毁宿主、无效租约、`AudioListener.pause`、`Play` 后同帧 `isPlaying`、低帧率拍间隔、真实加载缝、`UpdateManager` 行为、加载永不返回）；AUD-002 的定级取决于 `UpdateManager.Clear()` 的实际使用频率。

### Data（增量复审，上次 A–F 2026-09-30）

- **范围**：`Runtime/Data/` 全部 7 个文件 + README + 测试 + Save/Serialize/Asset 接缝；13 类失效矩阵命中 1/4/6/7/8/11，其余 7 类未命中并写明理由。
- **留痕**：A1 命中 DATA-001/006/012/013；A2 命中 DATA-009（门面完备性本身通过）；B1/B6 命中 DATA-002/003；B3 命中 DATA-007/008/010；B5 命中 DATA-004；D3 命中 DATA-001/007；D4 命中 DATA-006/010/011；E 命中 DATA-004/005/006；F 命中 DATA-005/006。
- **旧结论复核**：15 条中 9 条仍成立；5 条**部分失效**（引导所有权重跑、未来版本跳过的跨模块后果、`ApplySnapshot` 半态、单块管线格式来源、撞名边界）；1 条**已失效**（「16 个成员」）。
- **开口项**：`format` 恒 null、`Factory` 可变静态均维持 `未做`；新增 `未做`（脏集合对第三方不可兑现）、`待裁`（块版本过新经 Save 成 `Corrupt`）。
- **第三方对照**：按块组织、块级版本迁移链、块级失败隔离为本仓特征；常见实现多为整档版本或无迁移链（未读源码，常识对照）。
- **残留**：DATA-001 编译复现、DATA-002/004/005/008/010 改前必红、DATA-003 复位/不做裁定、DATA-006 写入端格式裁定、DATA-009 公开面裁定、DATA-011 Save 侧配合、跨会话手工验收。

### Save（增量复审，上次 2026-09-30；另有 2026-09-27 首轮）

- **范围**：`Runtime/Save/` 全部 10 个文件 + README + 测试 + File/Data/Bootstrap 接缝；13 类失效矩阵命中 1/4/5/6/7/8 等，未命中类写明理由。
- **留痕**：A1 命中 SAVE-002/007/008/011；A2 命中 SAVE-003/008/009；B1 命中 SAVE-009；B3 命中 SAVE-004/006/012；B4 命中 SAVE-010；B5 命中 SAVE-002；B6 命中 SAVE-001/005；D3 命中 SAVE-014；D4 命中 SAVE-004/010/012；E 命中 SAVE-013；F 命中 SAVE-003/006。
- **旧结论复核**：台账 11 条中 7 条维持；3 条**部分失效**（引导所有权重跑、删除「彻底删除」表述、`RecoverAsync` 冲突清单）；首轮「零高危」条目未在仓内留存、**证据不足**（本轮结果间接支持无 P0/P1）。README 四节中「线程契约」本文件已订正但生产注释仍反、「盘上文件布局」被 SAVE-002 部分推翻。
- **开口项**：`RecoverAsync` 维持 `待裁`（文档可先行修，与设计裁定解耦）；写盘失败窗口、Factory、PlayMode 侧、不加忙碌守卫维持；新增 `未做`（`.meta.bak` 残留清理）与跨模块 `待裁`（Save/Data 会话复位族）。
- **第三方对照**：原子替换 + 一代备份、启动恢复扫描、侧车、快照级版本门禁、玩家隔离为本仓强项；加密刻意不做密码学、移动非原子为已知限制；平台 SDK 细节未一手核实。
- **残留**：SAVE-001 两遍 PlayMode、SAVE-002 断言、SAVE-005 重跑用例、SAVE-015 编译复现、SAVE-014 取消用例、测试读写真实 `persistentDataPath` 的隔离问题、门禁复跑。

### Asset（增量复审，上次 A–F 2026-10-01）

- **范围**：`Runtime/Asset/` 全部 22 个文件 + README + 测试 + YooAsset 2.3.18 源码 + Scene/Bootstrap 接缝；以 2026-10-02 后变更文件为圆心重扫 18 类，13 类失效矩阵逐项给理由。
- **留痕**：A1 命中 AST-002/005/006/007/009；A2 命中 AST-006（门面完备性本身通过）；B3 命中 AST-001/007；B5 命中 AST-001/003/008；B6 命中 AST-004/008；D3 命中 AST-003 + SCN-006；D4 命中 AST-001；其余类按增量留痕表逐项写明。
- **旧结论复核**：台账 18 条中 15 条维持；3 条**部分失效**（引导所有权失败路径、会话级复位对 lowMemory 订阅不完整、README 行为契约 #1 的取消句柄）；4 条开口项全部维持（真 YooAsset 端到端、预检/下载绑定、lowMemory 线程、加载链路集成用例），前两条现在是 AST-001/AST-003 的唯一可靠红证环境。
- **开口项**：4 条维持；真 YooAsset 端到端建议升优先级并登记两处需要真包/缝验证的面。
- **第三方对照**：加载失败返回 default + LastError（对照 Addressables 抛异常）、句柄 + 引用计数、对象池、场景句柄、热更编排归项目侧；取消泄漏是本仓当前弱项。Addressables 按公开语义对照、未实测；UGF 仅一格一手。
- **残留**：AST-001 端到端、lowMemory 线程真机、会话复位两遍 PlayMode、门禁与全量测试、`AutoUnloadBundleWhenUnused` 复验、`LoadAsync(location, default)` 二义未复现、`AssetSceneHandle.Dispose`/RemoteServicesAdapter 零覆盖。

### File（增量复审，上次 A–F 2026-09-30）

- **范围**：`Runtime/File/` 全部 13 个文件 + README + 11 个 Runtime 测试文件 + Save/Settings 接缝；13 类失效矩阵逐项复核。
- **留痕**：A1 命中 FILE-003/004/007/010/012；A2 命中 FILE-001/003；B3 命中 FILE-001/005/011；B4 命中 FILE-002；B5 命中 FILE-004；C2 命中 FILE-002/009；D3 命中 FILE-008；D4 命中 FILE-005/006/011；F 命中 FILE-001。
- **旧结论复核**：台账 12 条全部维持；上轮「A–F 零高危」**部分失效**（本轮 2 条 P1、无 P0）；README 行为契约/线程契约/已知限制各有一处需订正（FILE-001/002/004/005）。正面确认：`CryptoFileProvider` 实现两个可选能力接口、判定权在门面 `_baseProvider`（CLAUDE.md 正例成立）；`DeleteAsync` 池线程有实测断言与同步对照；会话复位登记齐全。
- **开口项**：`CheckStreamingExists` 忙等、`FileRootsTimingTests` 命名空间均维持 `未做`。
- **第三方对照**：Desktop/Mobile/Console 三 Provider；原子写 + 一代备份、可选能力接口、门面判定权为本仓强项；移动端 Streaming 的线程例外与解密失败语义是弱项。
- **残留**：FILE-001 语义裁定（与 Save SAVE-004 同源、修复顺序不能反）、FILE-002 真机探针、FILE-009 平台构建、FILE-008 测试缝、门禁与全量测试。

### Update（增量复审，上次 2026-10-01 第二轮定向）

- **范围**：`Runtime/Update/` 全部 5 个文件 + README + 测试 + 六个消费方（Input/UI/Settings/Timer/Audio/Camera）；13 类失效矩阵逐项复核。
- **留痕**：A1 命中 UPD-001/004/005；A2 命中 UPD-003；B5 命中 UPD-007；B6 命中 UPD-002/008；D3 命中 UPD-006；D4 命中 UPD-001；F 命中 UPD-002。
- **旧结论复核**：两轮审计的已修项（`Enable` 口径、固定步 ProcessImmediate、门面摘要、异常日志、自动注销可观测）全部**已失效（已修）**且复核属实；「不加线程断言」等 8 条 `不做` 维持；「LateUpdate/FixedUpdate/Unscaled 零生产使用者」**部分失效**；README「已知限制」缺三条新边界。两轮「无高危」结论维持（本轮 P0/P1 = 0）。
- **开口项**：3 条维持（其中「零生产使用者」需改写内容）；新增 `未做`（Clear 连带责任文档）、`待裁`（Clear 有序化 / ProcessImmediate 形参 / AutoDriveEnabled 会话复位）。
- **第三方对照**：三套调度器 + 档位切片 + 双时间轴 + 纯函数调度器为本仓特征；并行/Job、相位稳定、加载期硬门控、外部策略批量降档均刻意不做；Unreal/UniTask/Unity 为典型对照。
- **残留**：UPD-002 红证用例、UPD-003 双回调与 CS1503 编译探针、UPD-007 缓冲增长探针、UPD-008 跨会话手工验收、非编辑器平台 PlayerLoop 子系统名、`IsDrivingPlayerLoop` 外部事实。

### Config（增量复审，上次 A–F 2026-09-30）

- **范围**：`Runtime/Config/` 全部 19 个文件 + README + 测试 + Asset/Serialize 接缝；13 类失效矩阵逐项复核。
- **留痕**：A1 命中 CONF-001/002/004/007/013/015；A2 扫过（门面完备性守卫在）；A3 扫过（人工）；B1 扫过（Destroy 后可恢复）；B2 命中但已文档化（`ConfigChanged` 句柄）；B3 命中 CONF-006/010/011；B4 扫过（已裁定不加断言）；B5 命中 CONF-007/011/014；B6 命中 CONF-009/010/015（会话复位面完整）；C1 扫过；C2 命中 CONF-001；D1 扫过（Config→Asset，无 XSerialize 引用）；D2 命中待取证 CONF-016；D3 命中 CONF-012/013；D4 命中 CONF-005/006/008/009/010/011/014；D5 命中 CONF-014；E 扫过（Type 键）；F 扫过。
- **旧结论复核**：CSV struct 装箱、订阅者异常隔离两条修复仍成立；README 已知限制几乎全部仍成立（仅「注入条目 AssetPath 恒为 null」**部分失效**，CONF-007）；已推翻的 `SetInstance` 双订阅仍正确；会话复位（`b6c9e8a`）族登记与改前必红齐全。**上轮漏掉了 CONF-001（P0）**——原因是 Json/SO 正路径零覆盖（CONF-012）。
- **开口项**：F8 维持 `未做` 并补记新面；F15 建议改 `不做` + 重开判据；F10 在测试注释里「已归档」但台账无条目，需补。
- **第三方对照**：三种内置格式 + 自定义 Loader、反射一次缓存、分组预载、Luban 接缝为本仓形态；JSON schema 未文档化与 CONF-001 是当前最大使用面缺口。
- **残留**：JsonUtility 探针（坐实 P0）、真 YooAsset 包 SO Global、CONF-016 编译复现、IL2CPP High stripping 构建、CSV BOM、F8/F15/F10 台账裁定。

### Lock（开口项复核）

- **范围**：`Runtime/Lock/` 5 个文件 + README + 10 个 Runtime 测试文件；2026-09-27 首轮 A–F 后零行为提交，本轮只复核台账开口项与 E/F 抽查。
- **留痕**：E（主体键引用比较器、`lockType` 共享 int 域）与 F（边沿事件 vs 聚合查询 + `OnLockStateChanged`）旧结论仍成立；2026-09-27 已修项无回归。新增 LOCK-001（P1）与 3 条 P2 文档问题。
- **开口项**：3 条全部仍开、维持 `未做`；① 属 Event 文档（转 Event 切片），②③ 已在 README 已知限制写明。
- **残留**：LOCK-001 改前必红用例；3 条 P2 文档订正可随批次顺手做。

### Localization（增量复审，上次 A–F 2026-09-29）

- **范围**：`Runtime/Localization/` 全部 7 个文件 + README + 测试 + Message/Asset/UI 接缝；13 类失效矩阵逐项复核。
- **留痕**：A1 命中 LOC-002/003/004 的文档面；B1 命中 LOC-005；B3 命中 LOC-005/006/009；B4 命中 LOC-008；B6 命中 LOC-004；C2 命中 LOC-003；D2 命中 LOC-001；D3 命中 LOC-010；D4 命中 LOC-006/007；E 命中 LOC-011。
- **旧结论复核**：2 条**部分失效**（「`Get` 任意线程安全」缺前提、「合法格式项照旧」对 `{0, 5}` 不成立）+ 1 条新文档承诺失效（「只有 LanguageAssetLoader 是 internal」）；其余 README 线程契约/已知限制/设计取舍与 0.3.0 修复全部仍成立。
- **开口项**：13 条维持；LOC-012 改 `已做`（`fc07a5b`）；LOC-013 迁移 Message；`SetLanguageData` 事件面与并发代次两条维持 `待裁`（新增证据：Initialize 不发事件、同语言双切换后完成者不发事件、实例代际是独立问题）。
- **第三方对照**：JSON 解析严格度、占位符命名空间、语言表缓存为本仓形态；重复键后者胜与主流解析器一致，但与本模块自述的判定线不一致。
- **残留**：Unity Mono 侧 `{0, 5}` 探针、LOC-001 文档 vs API 裁定、LOC-006 空表口径裁定、LOC-004/005/009 夹具复现、门禁复跑。

### Bootstrap（跳过）

- **依据**：2026-10-17 已完成重新审计（边界 `2d584b6`；台账见 `Documentation/Modules/Bootstrap.md:84-91`），本轮审计期间无新提交，结论沿用。其唯一开口项「Pipeline 侧子阶段粒度超时」在 Pipeline 审计中复核：维持 `未做`，修法归属需补一句「可用公开 API 写超时装饰器绕过；框架级统一形状仍归 Pipeline」。

### Input（增量复审，上次 A–F 2026-09-30）

- **范围**：`Runtime/Input/` 全部 14 个文件 + README + 7 个 Runtime 测试文件 + Update/Message/Asset 接缝；13 类失效矩阵逐项复核。
- **留痕**：A1 命中 INP-004/005/006/007/010/012；A2 命中 INP-001；B3 命中 INP-013；B5 命中 INP-002/009；B6 命中 INP-002/003；D3 命中 INP-001（`FakeInputProvider` 假绿）；D4 命中 INP-003/008/011；E 命中 INP-014；F 命中 INP-001/010/014。
- **旧结论复核**：会话复位静态态修复仍成立但有 INP-002 残留；上轮「零高危」**部分失效**（INP-001 是 2026-05 起的存量缺陷）；11 处订阅绑定已统一到 `BindToContext → TryBindToDestroy`（`fc07a5b`，与 LOC-012 闭合）；`Input.md` 无需认领，可补一行指回 Localization。
- **开口项**：Input 台账原本无开口项；新增建议：INP-001 修复前记 `未做`，INP-002/003/008/009 记 `未做`，INP-010/014 记 `待裁`；把 `Input.md:46` 的「零高危」改为带日期的增量结论。
- **第三方对照**：Unity Input System 1.19 / Rewired 后端中立；默认 provider 的复合设备读取与缓存失效是弱项。
- **残留**：INP-001 运行期坐实（最高优先）、INP-002/003 单会话夹具、INP-005 依赖表裁定、门禁复跑。

### UI（首次整模块 A–F，三切片；首轮仅有门面定向）

- **范围**：`Runtime/UI/` 全部 34 个文件 + README + 42 个 Runtime 测试文件 + Asset/Update/Localization/Message 接缝；UI-A 门面/`UIManagerImpl`/导航/层/遮罩调用侧，UI-B View/Data/HUD/Tip/绑定，UI-C 排序/弹窗定位/遮罩样式/控制器/引导阶段。
- **留痕（UI-A）**：A2 命中 UIA-001；A1 命中 UIA-002/004；B1 命中 UIA-003；B2 命中 UIA-006；B3 命中 UIA-005/007；D4 命中 UIA-002/007；F 命中 UIA-001/002。
- **留痕（UI-B）**：B2 命中 UI-B-004；B5 命中 UI-B-005/006；B1 命中 UI-B-006；B3 命中 UI-B-001/008/011；A1 命中 UI-B-002/003/007/009/012；D3 命中 UI-B-010；D4 命中 UI-B-011；F 命中 UI-B-006/013。
- **留痕（UI-C）**：A1 命中 UI-C-001/004/005/008/009/012；A2 命中 UI-C-003/007；B2 命中 UI-C-006；B3 命中 UI-C-010；C2 命中 UI-C-001/002/003；D3 命中 UI-C-002/011；D4 命中 UI-C-005；F 命中 UI-C-006；排序空间与遮罩 64031 两条历史缺陷已修，未当新发现。
- **旧结论复核**：首轮定向的门面结论部分失效（UIA-002 使「Clear 后可恢复」的指令不成立；UIA-003 的 Dispose 闩锁仍在）；历史事故（面板回池不解绑、`BindToLocalizedText` 归口、Tip 失效、HUD 先 Forget 后回收）均仍处于已修状态，但 `BindToLocalizedText` 零回归覆盖。台账 5 条：遮罩挖洞/`IUIManager:IDisposable` 绕过/`Dispose` 半拆维持 `未做`，provider `SetUIRoot` 与 `AllPanelsClosedMessage` 维持 `待裁`。
- **开口项**：新增 `待裁`（UI-B-013 结果通道重置）；UIA-001/002 修复形状需裁定（行为变更 vs 文档）。
- **第三方对照**：排序空间（层步长/每层序号/上限）是模块不变量，接近 Unity 原生 sortingOrder 的 16 位边界；弹窗定位以屏幕像素为契约，依赖调用方给对单位；遮罩/捕获层是内置实现能力（自定义 `IUIManager` 拿不到，已告警）。
- **残留**：UIA-001/002、UI-B-001/005/006/013 的 PlayMode 探针；UIA-001 修法涉及公开行为；UI-B-002 修法涉及公开 API；门禁复跑。

## 8. 实施批次建议

> 按「先止血、再清扫、后裁定」排序。每批内部按模块拆原子提交；公开 API/破坏性变更仍须单独确认。验证统一包含 `Tools/check-docs.ps1 -Enforce`；改代码的批次另跑对应模块测试与全量。

### 批次 0：P0 + 文档示例清扫（低成本、先做）

- **条目**：CONF-001（P0）、TMR-001、DATA-001、SER-001、SER-002、RCT-003、AST-002、MSG-002、LOC-002、CONF-002、SAVE-011、FILE-003、LOCK-002、INP-004、UPD-004/005、MSG-006、CAM-006、DIAG-006、DATA-012、POOL-003/005、AUD-007/008/009/010、LOC-001 的文档侧。
- **要点**：CONF-001 先补 `JsonUtility` 探针坐实；全部示例改字段/改参数顺序；README 模板句（公开面/internal/计数）一次清扫。
- **验证**：示例片段编译探针；`check-docs.ps1 -Enforce`；`run-tests.ps1 -Filter "Config|Timer|Data|Serialize|Reactive"`（若触及测试）。

### 批次 1：P1 正确性（无裁定或已推荐修法）

- **条目**：AST-001（取消释放）、SCN-001（回收段令牌）、MSG-001（通道回收时机）、AUD-001（Starting 短音）、FILE-001（Decrypt null 语义）、LOG-001（JSONL 截断/seq）、LOG-002（捕获重入闩锁）、LOCK-001（先摘后派发）、INP-001（时长预热）、UI-B-001（HUD 逐实例隔离）、UI-C-001（弹窗尺寸单位）、UIA-002（重注册全部分档驱动器）、TMR-002（扫尾重算 min）、SET-001（装饰器实现异步能力）。
- **验证**：每条的先红用例（报告 §4/§5 的「验证」）；模块 Filter 测试；行为变更进 CHANGELOG `#### 行为变更`；`SET-001` 若改公开类型接口实现，跑公开 API 基线。

### 批次 2：P1 需用户裁定的取舍

- **条目**：POOL-002（会话复位 vs 明示保留）、TMR-003（Update 当前轴时刻查询）、DIAG-001（文档 vs 去重语义）、SET-002（文档 vs 主线程构造）、SET-003（文档 vs 门面剔除）、SET-004（文档 vs 清订阅流）、FILE-002（文档 vs 线程模型）、UIA-001（行为修 vs 文档+示例）、UI-B-002（文档 vs 公开只注销入口）、RCT-001（文档 vs 派生值拥有上游）。
- **动作**：先按 §9.3 做框架级裁定（会话边界、公开 API 源兼容），再逐条落地。

### 批次 3：P2 文档/契约清扫

- **条目**：线程契约类（SAVE-010、MSG-005、LOG-011、LOC-008、RCT-009、TMR-006、UPD-001 等）；失败语义/已知限制类（CONF-003/004/005、SER-004、LOG-003/008、DATA-013、AUD-006、LOC-006/007/009、INP-006/007/010 等）；README 计数/陈表类（PIPE-001、AUD-006、UI-B-003/009/012、UI-C-004/009/012、UPD-004 等）。
- **验证**：人工对照；`check-docs -Enforce`。

### 批次 4：P2 代码/测试补齐

- **条目**：D3 类（AST-003、SCN-006、POOL-008、LOG-007、SET-016/017、AUD-005/014、CONF-012、MSG-008、EVT-006、TMR-008、RCT-008、UPD-006、INP?、UI-B-010、UI-C-011 等）；容器/生命周期类（POOL-001/007/009、SAVE-002/003/005/012、DATA-004/005/007/008/010、LOG-006/009/010、MSG-003/004、AUD-002/003/004、FILE-004/005/006/008/009/011、INP-002/003/008/009/011/012/013、UI-B-004/005/006/008/011、UIA-003/005/006/007、UI-C-002/003/005/006/008/010）。
- **验证**：新增用例的「改前必红」；负向控制；模块 Filter → 全量。

### 批次 5：设计裁定与台账驱动

- 会话复位族（§6.1）、公开 API 重载源兼容探针（§6.8）、文档示例可编译性守卫（§9.3）、各模块 `待裁` 条目（§10）。
- **验证**：裁定后按「单独确认 + 基线 + 迁移表」流程；`check-docs -Enforce` + 全量测试。

## 9. 已评估未采纳 / 不重开清单

### 9.1 框架级 7 条开口项的判据复核

| 条目（`Ledger.md`） | 状态 | 本轮复核 | 结论 |
|---|---|---|---|
| Network 基础层 | 暂不做 | 仓内仍零消费者；`File/MobileFileProvider` 的 HTTP 仅服务 Streaming，不可复用 | 判据未触发，维持 |
| 代码热更（HybridCLR） | 暂不做 | 本轮未出现真实接入方；热更链路仍是 Asset 的下载/激活，不涉代码 | 判据未触发，维持 |
| GM / 调试命令台 | 暂不做 | 仍无使用方给出命令集/权限/发布形态 | 判据未触发，维持 |
| App 生命周期 | 暂不做 | `pause` 仍只有 Settings 一处消费者；本轮未新增 | 判据未触发，维持 |
| 性能基准套件 | 未做（单议） | 本轮发现若干性能面（AUD-010 装箱、LOG-004 分配、RCT-010 装箱、TMR C2），但均非吞吐/耗时基准；`AllocationRuler` 仍只回答「有没有分配」 | 维持 `未做` |
| 示例覆盖面扩展 | 未做 | 本轮反而发现示例/文档示例的正确性问题（DIAG-001、TMR-001、CONF-001）；覆盖面问题与示例质量问题是两件事 | 维持 `未做`；另立「文档示例可编译性」候选（见 9.3） |
| 包自身 Git URL 不钉修订是否补说明 | 待裁 | 现有 tag `0.3.0` 仍使「无 tag 可钉」前提不成立；文档分层未变 | 维持 `待裁` |

### 9.2 已评估未采纳（本轮复核维持）

- 框架级结构性 `不做` 组（状态机/行为树/ECS/时间模型/生命周期树、Update 组 API、DI 容器、Tween、Addressables、新手引导、平台 SDK 聚合、网络同步、统一 Profile、模块内部策略接口化、远程配置、崩溃上报、红点、画质档位、埋点/统计上报、热更编排）——本轮未发现推翻证据，**不重开**。
- 带判据的四条 `不做`（CI、`Tests~`/移出包目录、包在子目录、Git 钩子/Issue·PR 模板/`CONTRIBUTING`）——判据未触发，维持。
- 各模块台账中的 `不做/暂不做` 条目：本轮被审模块逐条复核（Asset 8、Bootstrap 已完成重新审计、Pool 5、Reactive 7、Update 8、Scene 10、Serialize 1、Diagnostics 5、Camera 9、Lock 若干、Config 若干等），除 9.3 列出的状态调整建议外**全部维持**；未发现需要推翻的旧结论。
- 主线程断言「三份副本不抽公共防线」——本轮多个模块缺少断言，但这不构成推翻该裁定的证据；建议按模块各自决定（见 §6.3），**不新增第四种形态**。
- 会话复位「只丢引用不 Dispose」——本轮发现它在存在实例事件订阅时并不完整（AST-008/SAVE-001/SET-004），这是**实现缺口**而非裁定被推翻；具体「该不该替使用方 Dispose」仍待用户裁定（见 9.3）。

### 9.3 本轮新增的框架级结论建议（需裁定后入台账）

- `待裁`：**会话边界该不该替使用方 Dispose / 清订阅**——POOL-002、SAVE-001、DATA-003、SER-006、UPD-008、AST-008、SET-004、INP-002 共同指向；需一次框架级裁定，再按模块落地。
- `未做`：**文档示例可编译性守卫**——`Samples~/` 与 XML `<code>` 不参与编译，本轮 P0 CONF-001 与 TMR-001/DATA-001 均由此存活；建议评估把示例抽成可编译片段/测试（与 `ModuleAudit.md` §八 的可自动化候选同族）。
- `待裁`：**公开 API 重载源兼容探针**——新增/修改重载时补一次调用形状编译探针（LOC-001 是实例）；是否纳入公开面流程由用户裁定。
- `不做`（候选）：**不为文档内容加运行时门禁**——`check-docs` 只守结构；内容属实仍靠评审与审计（维持既有边界）。
- `不做`（候选）：**不引入反射式自动会话复位**——与显式登记、零反射取向冲突。

> 上述「不做/暂不做」候选在写入台账前须经用户确认；本轮报告只登记建议与理由。

## 10. 台账同步索引

> 只列本轮要写入/调整的长期结论；新增 P0/P1/P2 修复建议仍留在本报告，实施轮落地后由实施提交记 `已做`。`不做/暂不做/已推翻` 条目须经用户确认后写入（见 §10.3）。

### 10.1 模块台账调整（`Documentation/Modules/<模块>.md`）

| 模块 | 动作 | 条目摘要 | 证据 |
|---|---|---|---|
| Localization | `未做` → `已做` | InputManager 的 11 处订阅绑定已改走 `TryBindToDestroy` | `fc07a5b`（2026-09-30） |
| Localization | 迁移 | `IDestroyCancellationToken` 范例坑移至 `Message.md` | MSG-002 / 本轮源码复核 |
| Message | 新增 `未做` | `IDestroyCancellationToken` XML 示例在销毁后抛 ODE（自 Localization 迁入） | MSG-002 |
| Settings | 新增 `待裁` | 加密装饰器遮蔽内层异步能力：装饰器实现 `IAsyncSettingsStore` vs 另建异步装饰器 | SET-001 |
| Settings | 新增 `待裁` | 会话边界是否替使用方清句柄订阅流（「旧订阅者不会复活」与实现相反） | SET-004 |
| Pool | 新增 `待裁` | 池静态状态是否补 `ResetForNewSession`（或明示跨会话保留） | POOL-002 |
| Pool | 新增 `待裁` | `CollectionPoolManager.Register` 是否收窄为 internal（公开面收窄） | POOL-010 |
| Serialize | 新增 `待裁` | 序列化注册表跨会话保留 vs 补复位 | SER-006 |
| Data | 新增 `待裁` | `DataManager._impl` 会话复位 vs 明示保留 | DATA-003 |
| Data | 新增 `待裁` | 写入端格式契约（`Serializer.Default` vs `defaultFormat`） | DATA-006 |
| Data | 新增 `待裁` | 脏集合载体是否提升为公开面（第三方 `IDataManager` 可兑现性） | DATA-009 |
| Data | 新增 `待裁` | 块版本过新经 Save 变成整份 `Corrupt`（与 Save 协同） | DATA-011 |
| Save | 新增 `待裁` | Save 是否纳入会话复位族（默认启动器路径安全） | SAVE-001 |
| Save | 内容更新 | `RecoverAsync` 冲突清单更新为 5 处生产注释 + 1 处 File 注释 + 1 处测试注释 | SAVE-007 |
| Update | 新增 `待裁` | `Clear()` 是否改成有序 `PendingOp`（Clear 后 Register 静默丢失） | UPD-002 |
| Update | 新增 `待裁` | 门面 `ProcessImmediate` 形参是否放宽为 `IUpdateLifecycle`（公开 API） | UPD-003 |
| Update | 新增 `待裁` | `AutoDriveEnabled` 会话复位 vs 保留用户选择 | UPD-008 |
| Timer | 新增 `待裁` | `Create`/`Restart` 锚点需要 Update 当前轴时刻（跨模块公开面） | TMR-003 |
| Event | 新增 `待裁` | `SubscriptionTracker.Reset` 的文档/自动钩子二选一 | EVT-005 |
| Diagnostics | 新增 `待裁` | 页签登记「按实例幂等」的文档承诺 vs 改去重语义 | DIAG-001 |
| Camera | 新增 `待裁` | `OutputCamera` null 语义与 `Unregister` 活动状态：改核心措辞 vs 改扩展包行为 | CAM-005 |
| UI | 新增 `待裁` | 重复 `ShowDialogAsync` 是否重置结果通道 | UI-B-013 |
| Reactive | 新增 `待裁` | 链式 `Select` 中间派生值的释放形状（文档 vs API） | RCT-001 |
| Localization | 新增 `待裁` | 空语言表两条入口口径（引导静默 vs 加载器硬失败） | LOC-006 |
| Localization | 新增 `待裁` | 在途 `SwitchLanguageAsync` 与 `SetInstance`/`Destroy` 的代次守卫 vs 文档 | LOC-004 |
| Localization | 新增 `待裁` | `TextAsset` 重载的源不兼容：文档声明 vs 改静态工厂 | LOC-001 |
| Log | 新增 `待裁` | 文件 sink 死亡的可观测面（摘除+计数 vs 健康回读） | LOG-003 |
| Log | 新增 `待裁` | `AddSink` 重复登记 × `RemoveSink` 释放首个的语义 | LOG-006 |

### 10.2 框架级台账（`Documentation/Ledger.md`）

- 新增 `待裁`：**会话边界该不该替使用方 Dispose / 清订阅**（横跨 Pool/Save/Data/Serialize/Update/Audio/Input/UI；证据见 §6.1 与各模块条目）。
- 新增 `未做`（候选）：**文档示例可编译性守卫**（`Samples~/` 与 XML `<code>` 不参与编译；CONF-001 为实例）。
- 新增 `待裁`（候选）：**公开 API 重载源兼容探针**（LOC-001 为实例；是否纳入公开面流程）。
- §四复核记录追加：框架级 7 条开口项本轮逐条复核，判据均未触发（Network/HybridCLR/GM/App 生命周期）或维持原状态（性能基准/示例覆盖/Git URL）。

### 10.3 需用户确认后才能写入的「不做/暂不做」

| 模块 | 拟改 | 条目 | 理由 / 重开判据 |
|---|---|---|---|
| Pool | `未做` → `暂不做` | 集合池没有 `RemovePool` 这类移除入口 | `Configure` 已覆盖主场景；重开判据：出现需要丢弃集合池注册的使用方，或 POOL-002 的复位入口需要它 |
| Config | `未做` → `不做` | `ConfigTypeHelper` 的失败路径不缓存（F15） | 失败路径以抛异常为主成本；重开判据：出现高频失败重试路径（如热更后反复重载同一坏类型） |
| Event | `待裁` → `不做`（或 `未做`） | Event 层要不要加主线程断言 | Editor-only 断言成本可接受但 Event 是每帧热路径；若选 `不做` 无重开判据，若选 `未做` 则列入实施 |

> 其余 `不做/暂不做/已推翻` 旧条目本轮复核均维持，不产生台账变更。

## 附录 A. 探针与命令

- 门禁：`pwsh -File Tools/check-docs.ps1 -Enforce`（基线已通过；审计代理未跑 Unity 测试/构建）。
- 各发现的「验证方式」已写在 §4/§5 条目内；需运行期证据的项见附录 B。
- 跨会话验收（关闭域重载）无法由 `run-tests.ps1` 表达，需手工两遍 PlayMode。

## 附录 B. 待取证

- POOL-002 跨会话实际表现（手工两遍 PlayMode）。
- DIAG-001 关闭域重载页签累积（手工）。
- SCN-001 清理段取消（需扩展 fake 后跑用例）。
- CAM-001（销毁相机的 `is null`/`IsActive`）与 CAM-003（NaN/∞ 过渡）运行期确认。
- EVT-001 白盒用例、EVT-007 的 Message 侧重放用例。
- PIPE-005 并发串台、PIPE-003 终局广播观测。
- 各模块报告中标注「需实测」的其余项，随对应波次补充。
