# XFramework / Timer 模块

## 概述

命名空间 `XFramework.XTimer`。一次性延时与固定间隔定时器，返回**可查询、可重开、零分配**的句柄。

**零配置**：纯静态服务，没有可替换的后端，也没有 `Initialize`——进入播放时已就绪（`UpdateManager` 是它唯一的依赖，也是自动就绪的）。

三条贯穿全文的约定：

- **时间口径完全来自 `XUpdate`**。定时器的截止时刻建在各轴 `deltaTime` 累加出的逻辑时钟上，本模块不读 `UnityEngine.Time`、也不提供自己的暂停开关。受不受 `timeScale` 影响、暂停时冻不冻，由一个 `UpdateTimeMode` 参数决定——**取舍口径与 Update 完全一致，不需要再学一套**。
- **句柄就是入口**。`Stop` / `Restart` / `Remaining` 都在 `TimerHandle` 上，门面不重复暴露同名静态方法。
- **回调里发生的一切都是安全的**。创建、停自己、重开自己、全停、抛异常，都在用例里被钉住。

**它在 `UniTask.Delay` 之外多给什么**（答不出这条就不该有这个模块）：

| | Timer | `UniTask.Delay` |
|---|---|---|
| 查询剩余量 | `handle.Remaining` | 做不到 |
| 重复起停同一件事 | 句柄复用，零分配 | 每次要一个新的 `CancellationTokenSource` |
| 固定间隔长期运行 | 落在「锚点 + k × 间隔」网格上，不漂 | 逐次累加，会漂 |
| 低频定时器的开销 | 自动降频，60 秒的定时器每 2.1 秒才被扫一次 | 每帧一个 `PlayerLoop` 回调 |

## 典型场景

### A. 技能冷却：读条 + 重置

```csharp
private TimerHandle _cooldown;
private bool _ready = true;

private void Awake()
{
    _cooldown = TimerManager.Every(8f, () => _ready = true);
}

public bool TryCast()
{
    if (!_ready) return false;
    _ready = false;
    _cooldown.Restart();      // 冷却重置回满
    return true;
}

// UI 每帧读它，不需要缓存任何东西
public float CooldownProgress => _ready ? 1f : 1f - _cooldown.Remaining / 8f;
```

### B. 延时做一件事，并随对象销毁自动取消

```csharp
TimerManager.After(1.5f, this, static self => self.OnWarmupDone(),
    cancellationToken: destroyCancellationToken);
```

`Action<TState>` 重载配 `static` lambda 是**零闭包**写法：注册期一个字节都不分配。不传令牌时整条路径同样零分配。

### C. 周期结算

```csharp
TimerManager.Every(1f, () => Settlement.Tick());
```

第 k 拍恒在「锚点 + k × 间隔」上，不随帧率漂移。**一帧跨多拍时只补发一次**——卡顿一秒不会让 0.1 秒的定时器连发十次。

### D. 暂停菜单里仍要走完的倒计时

```csharp
TimerManager.After(3f, () => ResumeGame(), UpdateTimeMode.Unscaled);
```

默认的 `Scaled` 跟着 `timeScale` 走，`UpdateManager.Pause()` 与 `timeScale = 0` 都会冻住它。**本模块没有自己的 `Pause`**——见「设计取舍」。

### E. 清场

```csharp
_cooldown.Dispose();     // 单个：停表并释放槽位
TimerManager.CancelAll(); // 全部：彻底复位，之后仍可继续创建
```

## 快速使用

```csharp
// 1. 延时（一次性）：到点回调一次，随后句柄自动失效
TimerManager.After(3f, () => Respawn());

// 2. 固定间隔：返回句柄以便查询与停止
var tick = TimerManager.Every(1f, () => Score++);
float left = tick.Remaining;

// 3. 不用了释放槽位（句柄是可复制的值，离开作用域不会自动停表）
tick.Dispose();
```

## 完整 API 参考

### 创建（`TimerManager`）

| 成员 | 说明 |
| --- | --- |
| `After(float delay, Action callback, UpdateTimeMode = Scaled, CancellationToken = default)` | 一次性延时。`delay >= 0` 且有限 |
| `After<TState>(float delay, Action<TState> callback, TState state, ...)` | 同上，回调收到 `state`。**零闭包、值类型不装箱** |
| `Every(float interval, Action callback, ...)` | 固定间隔。`interval > 0` 且有限 |
| `Every<TState>(float interval, Action<TState> callback, TState state, ...)` | 同上 |

`timeMode` 排在 `cancellationToken` **之前**——与全仓公开 API 的参数序一致（配置参数在前、令牌收尾）。

两条失败形态：

- **参数非法抛异常**：`callback` 为 null → `ArgumentNullException`；时长非有限、`interval <= 0`、`timeMode` 越界 → `ArgumentOutOfRangeException`。
- **创建时令牌已被取消**：记一条 `[Timer]` 告警并返回 `default(TimerHandle)`，**不抛也不同步回调**。

### 句柄（`TimerHandle`）

| 成员 | 说明 |
| --- | --- |
| `IsDefault` | 是否 `default(TimerHandle)`。纯字段检查，任何阶段都安全 |
| `IsActive` | 是否仍在计时。**实时查询**（句柄是不可变值类型，缓存不了） |
| `Remaining` | 距下一拍还剩多少秒；无效/已停/已取消 → `0` |
| `Stop()` | 停表但**保留槽位**，可 `Restart` |
| `Restart()` | 用原时长重新起算（首拍 = 此刻 + 原时长） |
| `Dispose()` | 停表**并释放槽位**，句柄随之失效 |

`IsActive` 的一条边界：**被暂停的定时器仍算在计时**（冻结的是时间轴，不是定时器的存在）；而**令牌已取消的立刻算作不在计时**，不等槽位被回收。

### 门面其余成员

| 成员 | 说明 |
| --- | --- |
| `CancelAll()` | 停净全部（含已 `Stop` 未释放的），返回被取消的数量。**彻底复位**，之后仍可继续创建 |
| `ActiveCount` | 正在计时的数量（两轴合计） |

## 与相邻模块的关系

| 模块 | 关系 |
| --- | --- |
| **Update** | **唯一依赖**。定时器是 Update 的消费者：时间来自它的 `deltaTime`，档位用它的 `UpdateTier`，暂停用它的双时间轴 |
| Settings / UI / Audio / … | **不依赖**。需要联动请在项目侧接线（例如把 `Remaining` 喂给进度条） |

## 内部机制

### 逻辑时钟

每轴一个累加器：`_logicalNow[axis] += deltaTime`。截止时刻全部建在它上面，**本模块不读 `UnityEngine.Time`**——这是「暂停与时间缩放零代码」的来源，也让整个模块可以被单测精确到帧地驱动。

### 槽位与代际

槽位按 **(时间轴, 状态类型)** 分表，表内是连续数组 + 侵入式自由链，分配与回收都是 O(1)，容量按 2 倍增长。

句柄的身份是 `(表, 槽位下标, 代际)` 三元组。**槽位会被复用，没有代际就无法让旧句柄失效**；`default(TimerHandle)` 的代际是 `0`，保留为无效标记。

### 档位选择

驱动器每拍按「该轴上最近一次截止的剩余」决定自己下一拍落在哪一档：

```
取最大的 k ∈ [1, 7] 使 (2^k / 60) × 8 ≤ 剩余，都不满足则 Tier0
```

- **安全系数 8** 给出两个硬数字：最坏相对超时 **12.5%**，截止前**至少还有 8 次派发机会**（足以吸收低帧率下 Update 的周期拉长）。
- 实测曲线：0.1 秒 → 每帧、0.5 秒 → 33ms、1 秒 → 67ms、2 秒 → 133ms、5 秒 → 533ms、10 秒 → 1067ms、60 秒 → 2133ms。
- 截止临近时自动升回 `Tier0`，因此**触发精度不受降频影响**。

换档位有三条通道，各自的时间基准都不丢：扫尾中的返回值（常规）、`ProcessImmediate` 立即重估（已派发过的驱动器遇到更近的新定时器）、重新注册（尚未派发过的驱动器）。

### 出生趟号

扫尾分「派发轮」与「立即重估」两种。后者的那一趟**不推进趟号**，于是刚建好的槽位仍算「本趟出生」——**只参与最近截止的估计，绝不触发**。少了这条，回调里创建的 `After(0f)` 会在 `Create` 还没返回、句柄还没拿到时当场跑起来。

## 生命周期与清理

| 动作 | 行为 |
| --- | --- |
| 进入播放 | `AutoInit` 自动复位（丢弃上一会话残留的槽位与句柄）并挂上退出清理。关闭域重载时也生效 |
| 退出播放 | `Application.quitting` 停净全部并丢弃核心 |
| 未创建过任何定时器 | 驱动器**不注册**，零开销 |
| 全部结束/停止 | 驱动器自动退出调度；下一次创建再挂回去 |
| `CancelAll()` | 彻底复位。句柄（含已停止的）全部失效 |

**句柄可以比核心活得更久**：`AutoInit` 换核心之后，旧句柄上的一切查询返回 `false` / `0`，`Dispose` 是空操作，不会抛。

## 线程契约

**所有 API 必须在主线程调用，模块内没有任何守卫**——注册、扫尾、句柄操作全部直接改写槽位数组，既没有断言也没有同步原语。从池线程调用不会报错，而是静默地与主线程的派发竞争。

取消令牌是唯一允许跨线程的输入：在别的线程上取消是安全的（模块只读 `IsCancellationRequested`）。

## 设计原则

- **不新建时间模型** — 时间、暂停、时间缩放全部转接自 Update；本模块只有一个 `UpdateTimeMode` 参数
- **句柄即入口** — 同一语义只有一处真相，门面不重复暴露
- **回调里什么都能做** — 创建、停止、重开、全停、抛异常都被用例钉住，不要求调用方遵守额外纪律
- **不做并行** — 回调在主线程上同步执行，没有等待点；需要异步的工作请在回调里自己起 UniTask

## 依赖

- **框架内**：`XUpdate`（唯一）。时间口径、档位、双时间轴全部来自它
- **第三方**：无

## 已知限制

- **同轴最细的定时器决定其余所有定时器的扫描频率**：一条轴只有一条驱动器，档位取该轴上最近的截止。一条 0.5 秒的定时器会把同轴那条 60 秒的一起拉到细档——此时降频收益归零。双轴已经把「暂停菜单类」与「玩法定时器」天然分开，但同一轴内的混用无法避免。
- **回调里跨轴创建更近的定时器，最坏晚一个旧档周期**：调度器的迭代闩锁下 `ProcessImmediate` 只重锚不派发。同轴创建不受影响（那趟扫尾的返回值本就涵盖它）。
- **`interval` 短于实际帧长时，触发被量化到帧上**：平均间隔会被拉长到帧长——这是「不可能比帧更密」的物理边界，不是缺陷。
- **`Stop` 之后忘记 `Dispose` 会一直占着一个槽位**：停止的定时器不占 CPU（驱动器随之退出），但表仍然持有它的槽位、以及回调捕获的一切。
- **一次性定时器触发后句柄立即失效**：想保留重开能力，请在创建后先 `Stop()`，要用时再 `Restart()`。
- **`Remaining` 只在派发时更新**：它读的是逻辑时钟，而逻辑时钟只在派发里前进。粗档下它是**阶梯状**的（大跳的跨度就是一个档位周期）——用它做读条在长定时器上会一跳一跳。做进度条请用短定时器，或自己在项目侧插值。
- **`ActiveCount` 与 `IsActive` 最坏相差一个派发周期**：令牌取消后 `IsActive` 立刻为 `false`，而计数要等下一趟扫尾才扣。
- **`UpdateManager.Clear()` 会连带摘掉本模块的驱动器**（它清的是全局注册表）。此后新建的定时器不会触发；调用 `TimerManager.CancelAll()` 再创建即可恢复。
- **所有 API 限主线程**（见上）。

## 设计取舍

### 为什么没有 `Pause` / `Resume`

暂停与时间缩放必须架在 Update 的双时间轴之上，本模块不得另开一套时间口径——那等于替使用方决定时间模型。所以这里只有 `UpdateTimeMode` 一个参数：`Scaled` 跟着 `timeScale` 走，`Unscaled` 不受影响，`UpdateManager.Pause()` 一冻就是一整条轴。用户理解的暂停语义与 Update 里学到的是同一套，不需要为定时器再学一遍。

### 为什么 `Stop` 保留槽位、而不是直接释放

`Stop` 的用途是**暂时停表、稍后重开**（受击硬直、条件性轮询）。若它顺手释放槽位，`Restart` 就没有立足之地，「用原时长重新起算」这件事只能靠重新 `Create`——那正是句柄复用想避免的。代价「忘记 `Dispose` 会占槽位」已写进已知限制。

### 为什么不设容量上限

Audio 的播放源封顶，是因为槽位背后是稀缺的引擎资源；定时器的槽位是纯托管内存。封顶之后唯一能给的失败形态是「静默返回空句柄」——把一个可观测、可诊断的内存增长，换成一个不容易发现的失效定时器，更糟。替代品是单表活跃数达到 1024 时的一条**一次性**告警（只报一次，不刷屏），用来发现漏 `Dispose`。

### 为什么按状态类型分表

`Action<TState>` 无法转成 `Action<object>`。若用一个 `object` 字段存状态，**值类型载荷会在每次创建时装箱**——那会让「零闭包、零分配」这条承诺对一整类用法失效。分表让值类型状态也零装箱，代价只是带状态的创建路径多一次字典查找（无状态路径由字段直连，不查字典）。

### 为什么回调抛异常就停掉那个定时器

先例是 Update 的两条分流：每帧都会被调用的回调（`OnUpdate`）抛异常即注销节点，只在状态迁移时触发的回调（`OnEnable`/`OnDisable`）只记日志。定时器的回调**按拍重复**，属前者——只记日志会让一个固定间隔定时器每拍刷一条错误。停掉它并说明原因，比每拍刷屏更有用。

### 为什么不提供「按 owner 分组取消」

一个对象的多个定时器各存句柄即可；若确实需要「一锅端」，那通常说明该对象的生命周期本身该由使用方的架构管——框架替它决定实体模型，撞第一条非目标。真的要一锅端，`destroyCancellationToken` 是现成的机制。
