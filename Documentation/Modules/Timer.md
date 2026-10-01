# Timer —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Timer/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Timer/
├── TimerManager.cs   # 公开门面（扁平 + #region 分区）：After/Every 四个重载、CancelAll、ActiveCount、AutoInit
├── TimerHandle.cs    # 句柄：readonly struct : IDisposable，字段私有、构造 internal
├── TimerCore.cs      # 引擎：两条轴各自的逻辑时钟、表集合、句柄操作、扫尾、选档公式与周期表（internal）
├── TimerTable.cs     # ITimerTable + TimerTable<TState> + TimerSlot<TState> + NoState（internal）
├── TimerTicker.cs    # IUpdateable：每轴一个的驱动器，持有档位镜像与「上次收到的时刻」（internal）
└── README.md         # 使用说明

Tests/Runtime/Timer/  # 全部 PlayMode；无 EditMode 侧（没有 Editor 工具面）
```

**不开 `Internal/` 子目录**：先例是 `UpdateScheduler.cs` 与 `SettingsAutoSaveTicker.cs`——internal 类型平铺在模块目录是既有形态；`Internal/` 只给 Message/Event 那种「多个 internal 类型互相依赖成一组」的形态。

## 实测记录

`TimerTierTableTests` 每次运行都会把量到的值写进测试结果文件（`TestContext.WriteLine`），下面是 `2026-10-01` 的一次：

| 档位 | 实测周期 | 标称（`2^k / 60`） | 驱动 600 帧内的派发次数 |
| --- | --- | --- | --- |
| Tier1 | 33.33 ms | 33.33 ms | 300 |
| Tier2 | 66.67 ms | 66.67 ms | 150 |
| Tier3 | 133.33 ms | 133.33 ms | 75 |
| Tier4 | 266.67 ms | 266.67 ms | 37 |
| Tier5 | 533.33 ms | 533.33 ms | 19 |
| Tier6 | 1066.67 ms | 1066.67 ms | 10 |
| Tier7 | 2133.34 ms | 2133.33 ms | 4 |

**这几条改变了什么**：`UpdateScheduler.TickPeriod` 是 `internal const`（跨模块读不到），Timer 只能自己抄一份 60Hz 基准——抄来的常量没有编译器兜底。实测与标称逐位吻合说明「公式化周期表 + 手动驱动 1/60 步长」这条路成立，**故该用例的容差取 2% 而不是收紧**：它要抓的是「基准换了」（60→50 差 17%）或「补格算法改了」这类结构性失配，不是浮点噪声。

**另一条实测**（`TimerAllocationTests`）：稳态下推进 60 帧、创建/释放 1000 次、值类型载荷创建/释放 1000 次，`GC.GetAllocatedBytesForCurrentThread()` 增量**均为 0**。顺带确认了 `UpdateManager.Tick` 自身也是零分配的——若哪天不是，这条会先红。

## 已完成功能与未做（roadmap）

### 已完成（v1，2026-10-01）

- 一次性延时 `After` / 固定间隔 `Every`，各带 `Action` 与零闭包 `Action<TState>` 两种重载
- 池化槽位 + 代际安全句柄 + 自由链复用 + 2 倍增长（无上限）
- 不漂移的固定间隔（锚点 + k×interval 网格）+ 跳拍不补发
- 双时间轴（`Scaled` / `Unscaled`），暂停与时间缩放零代码转接
- 档位自动升降档 + 周期表跨模块集成锁定
- 取消令牌（含 `destroyCancellationToken`）、回调异常隔离、遍历期重入安全
- 句柄查询与控制：`Remaining` / `IsActive` / `Stop` / `Restart` / `Dispose`；门面 `CancelAll` / `ActiveCount`

### 未做（v2 候选）

| 项 | 为什么不在 v1 |
| --- | --- |
| 每档一个驱动器（分桶） | 收益是档位互不干扰，代价是管理器自维护分桶、且「新定时器落哪档」变成创建时的静态决定——「临近自动升档」这条需求本身就没有了。见下节「已评估未采纳」 |
| `ITimerManager` 接口 | 见下节 |
| 首拍立即的重载 | 「立刻做一次」在语义上等于调用方在创建处自己先调一次；为它开一个参数不值 |
| 按 owner 分组取消 | 撞「不预设实体模型」；`destroyCancellationToken` 已是现成的机制 |
| 帧数定时器（每 N 帧一次） | 那是 Update 档位的本职，再加一套就是第二条时间口径 |
| 公开的档位诊断面（`GetDispatchTier`） | 已实现为 internal 测试缝。加是兼容变更、减不是，故先不加 |

## 已评估未采纳与未决

**已评估未采纳**：

- **不做 `ITimerManager` 接口**（2026-10-01）：本仓「有接口」的分界线是**是否存在可替换的后端**——Audio → FMOD/Wwise、Input → Rewired、File → 平台 Provider。Timer 没有：它是纯编排原语，第三方没有「另一套定时器引擎」要接。多一个接口只会多出一份必须长期同步的转发契约（仓库有 `SetLayerVisibility` 那道疤），以及一个「门面完备性测试」。与 Update / Lock / Message / Pool 同族。
- **不用 `CancellationToken.Register`，改为把令牌存进槽位、在扫尾与查询点上直接读 `IsCancellationRequested`**（2026-10-01）：`Register` 会引入一个在自己的回调里改活表的跨线程写入口（必须用「只写标记 + 计数」避让，还得处理扩容换数组时标记写丢的竞态），而且**它自身就分配一次**——那会让「不传令牌时零分配」这条承诺打折。直接轮询的代价只是「槽位回收晚一拍」，而 `IsActive` 读的是令牌本身，观感上仍是立刻失效。
- **不用 `NeedsAnchor`（首次扫尾只定锚不触发），改用「出生趟号 + 创建时按逻辑时钟定锚」**（2026-10-01）：`NeedsAnchor` 有两个代价——`After(0f)` 要多等一拍才触发，且锚点被记在「下一趟扫尾」而不是 `Create` 那一刻（短延时的首拍平白多等）。出生趟号既挡住了「回调在 `Create` 返回之前跑起来」，又让锚点落在调用时刻，还少一个字段。
- **不把「重估档位」放在分配槽位之前**（2026-10-01）：那样扫尾看不见新定时器，算出来的还是旧档位——白拉细一趟。
- **不用「每次创建都重新 `Register` 驱动器」来换档位**（2026-10-01，**这条已读源码验证**）：`UpdateScheduler.ApplyOp` 的注册分支插入的是一条 `NeedsAnchor = true` 的新条目，下一次派发 `deltaTime` 记 0——每次创建都吞掉一个档位周期，高频创建时逻辑时钟会**永久停摆、定时器永不触发**。故已派发过的驱动器一律走 `ProcessImmediate`，并把上一次收到的时刻原样传回。
- **不做「每档一个驱动器」的分桶**（2026-10-01）：那是 UI 的 `TierDriver` 范式。用在这里收益是「一条短定时器不再拖累同轴的长定时器」，代价见上表。当前形态（单驱动器 + 动态档位）是「自动升降档」这条需求的直接推论；若真有大规模混用场景，这是可以重开的方向。
- **不设容量上限**（2026-10-01）：封顶后唯一能给的失败形态是「静默返回空句柄」，比内存增长更难诊断。替代品是单表活跃数 1024 时的一条一次性告警。
- **不为「停止的定时器」保留重开能力之外的额外语义**（2026-10-01）：`Stop` 保留槽位是刻意的（见 README 设计取舍），但一次性定时器触发后即释放——「触发过的还想重开」请先 `Stop`。

**未决**：

- **回调里跨轴创建更近的定时器，最坏晚一个旧档周期**：调度器的 `_isIterating` 是**跨调度器共享的静态闩锁**，它下面 `ProcessImmediate` 只重锚不派发。同轴创建不受影响（那趟扫尾的返回值本就涵盖它）。若真实需求出现，可加一条「延迟到下次 Tick 开头的升档请求队列」。
- **升档通道依赖 Update 的既有语义**：`ProcessImmediate` 不重置 `NeedsAnchor`、只把 `LastUpdateTime` 写成传入时刻（`UpdateScheduler.cs` 的 `ApplyImmediateTimeBase`）。这是本模块最脆的一处耦合，`TimerTierTests.TierSwitch_DoesNotMoveTheDeadline` 是它的哨兵——Update 将来改这条语义会红。
- **`_lastSeen` 用 `float` 回传**：`OnUpdate` 只给 `float time`，回传给 `ProcessImmediate` 时相对调度器内部的 `double` 有一次 ≤ 0.5 ULP 的**一次性**截断（一小时会话约 0.24ms），不累积、不随档位切换次数增长。当前接受，未写用例。
- **安全系数 8 是自定值**：给出「最坏相对超时 12.5%、截止前至少 8 次派发机会」两个可核对的数字，但没有实测「卡顿到多少 fps 才会破防」。若将来收到「触发偶尔偏晚」的真实反馈，这里是第一个该调的地方。
- **`UpdateManager.Clear()` 之后模块失联**：Clear 摘掉驱动器而 TimerCore 无感知（同 UI 的 P1 事故形态）。恢复路径是调用 `TimerManager.CancelAll()` 再创建，已写进已知限制，但没有自动侦测——**不能**像 UI 那样「每次入口都重注册」，那会踩上面那条「吞时间」。是否要加侦测未定。
- **`_sweepPass` 的理论溢出**：`int` 每趟 +1，长会话下约 400 天绕回。绕回时「本趟出生」的判定可能误伤一两个新生槽位（表现为该槽位晚一拍触发），不产生错误触发。当前不处理。

## 与其它模块的边界（下一轮审计时先看这里）

- **对 `XUpdate` 的接触只有两处**：`TimerTicker.cs`（注册/注销/`ProcessImmediate`、档位返回值）与 `TimerCore.cs` 的档位常量与 `UpdateTimeMode` 校验。审计时若发现第三处，那是边界泄漏。
- **不引用 Update 的 Internal 命名空间**：`UpdateScheduler.TickPeriod` 读不到，故 `TimerCore.TicksPerSecond = 60` 是**副本**，由 `TimerTierTableTests` 对着真实派发节奏锁定。这是本模块唯一一处跨模块常量耦合。
- **不依赖 Settings / UI / Message**（全是可选模块），README 只给接线说明。
- **不用 `PoolManager`**：那套池服务纯 C# 对象的常规复用；定时器的槽位是**同质数组 + 自由链**（每次分配都是同一形状），比通用池更快也更省。
