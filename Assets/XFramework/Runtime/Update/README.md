# XFramework / Update 模块

## 概述

Update 模块提供统一的更新调度服务，管理任意对象（静态服务、MonoBehaviour、普通 C# 类）的更新需求。

- **三个派发时机**：`Update` / `LateUpdate` / `FixedUpdate`，各自一套调度器
- **两条时间轴**：逻辑时间（受 `timeScale`）与墙钟时间（不受影响）
- **档位时间切片**：按档位把更新负载摊到各节拍格上（一格 = 1/60 秒），避免帧消耗集中
- **自动驱动**：注入 PlayerLoop，不需要场景里存在任何 MonoBehaviour

**命名空间**: `XFramework.XUpdate`

## 架构设计

```
Runtime/Update/
├── IUpdateable.cs                # 契约：IUpdateLifecycle / IUpdateable / ILateUpdateable /
│                                 #       IFixedUpdateable / UpdateTier
├── UpdateClock.cs                # 时间基：UpdateClock（time + unscaledTime + isPaused）/ UpdateTimeMode
├── UpdateScheduler.cs            # 纯调度逻辑（档位分桶 + 时间切片 + 双时间轴），internal
└── UpdateManager.cs              # 静态门面（含 PlayerLoop 注入驱动）
```

## 快速使用

### 1. 注册到更新调度

任何对象——静态服务、MonoBehaviour、普通 C# 类——实现时机接口后直接调 `UpdateManager.Register` 注册自身：

```csharp
using XFramework.XUpdate;

public sealed class MyService : IUpdateable
{
    public MyService()
    {
        // 自身就是实例，注册时传 this（不能用 static class：接口方法需要实例实现）
        UpdateManager.Register(this, order: 0, initialTier: UpdateTier.Tier0);
    }

    public void OnEnable() { }

    public void OnDisable() { }

    public UpdateTier OnUpdate(float deltaTime, float time)
    {
        // 返回值决定「下一次派发」采用的档位（不是当前这次）
        return UpdateTier.Tier0;
    }
}
```

### 2. 三个时机怎么选

| 时机 | 接口 | 时间基准 | 适用 |
| ---- | ---- | -------- | ---- |
| `Update` | `IUpdateable` | `Time.time` | 绝大多数逻辑 |
| `LateUpdate` | `ILateUpdateable` | `Time.time` | 需要「本帧所有 Update 都已跑完」：跟随移动目标、相机跟随 |
| `FixedUpdate` | `IFixedUpdateable` | `Time.fixedTime` | 物理、确定性模拟（要固定增量而非每帧变化的 delta） |

同一对象可以实现多个接口，会被分别登记到对应时机。注册用 `UpdateManager.Register` /
`RegisterLate` / `RegisterFixed`；注销、启用、禁用、查询不分时机（传任一对象即可）。

### 3. 时间轴怎么选

需要「暂停期间仍运行」的逻辑（暂停菜单、UI 动画、手柄振动到期）请在注册时声明墙钟轴：

```csharp
// 注册时显式传 timeMode（默认 Scaled）
UpdateManager.Register(ticker, order: 0, timeMode: UpdateTimeMode.Unscaled);
```

时间轴**以注册时传入的实参为准**——调度器不读取对象上的声明；中途要改变轴，须先注销再重新注册。

固定步长时机没有时间轴参数：Unity 的固定步长本就随 `timeScale` 停摆。

## 机制说明

### 档位分级调度

节拍按**时间**走：一格 = 1/60 秒（60Hz 基准），第 k 档的周期即 2^k 格。因此周期与帧率
无关——`Tier3` 在 30fps 与 144fps 下都是约 133ms，不再随帧率缩放。

| 档位 | 变步长轴周期（Update / LateUpdate） | 固定步轴（默认 50Hz） | 适用场景 |
| --- | --- | --- | --- |
| `Tier0` (0) | 每帧 | 每步 | 输入、移动 |
| `Tier1` (1) | 约 33ms | 2 个固定步 | AI 决策 |
| `Tier2` (2) | 约 67ms | 4 个固定步 | 动画状态机 |
| `Tier3` (3) | 约 133ms | 8 个固定步 | 视野检测 |
| `Tier4` (4) | 约 267ms | 16 个固定步 | UI 刷新 |
| `Tier5` (5) | 约 533ms | 32 个固定步 | 后台数据同步 |
| `Tier6` (6) | 约 1067ms | 64 个固定步 | 每秒级轮询（自动保存、统计上报） |
| `Tier7` (7) | 约 2133ms | 128 个固定步 | 数秒级低频任务 |

**固定步轴是个例外**：`Time.fixedTime` 每步恰好前进一个固定步长，本就没有需要修正的漂移，
故那里每个固定步推进一格——档位含义是「每 2^k 个**固定步**」（默认 0.02s 一步即
40/80/160/320/640ms），而不是上表那列毫秒数。

由此它上面的档位是**仿真频率**而非采样频率：Tier k 的节点每 2^k 步派发一次，`deltaTime` 恒为
`2^k × Time.fixedDeltaTime`（档位不变则增量不变），即该子系统的固定速率是物理速率的 1/2^k。
需要「经济结算 1Hz、AI 决策 12.5Hz」这类**不同的固定速率**时用它；但它**不减少物理成本**
（Unity 的物理照旧每步跑），所以别当帧预算旋钮用——摊帧预算请走变步长轴。直接驱动物理的对象
（写刚体速度/位置的控制环）也不宜降档：控制频率降到 1/2^k，却仍作用在每步积分的物理上。

被跳过的格**不会丢失时间**：`OnUpdate` 的 `deltaTime` 是「距上次派发的真实间隔」（可能跨
若干格），所以降频不导致速度失真。对象应始终按 `deltaTime` 积分，而不是按调用次数计数。

帧率高于节拍时会出现「本帧不推进」的空帧（144fps 下这圈轮子约每 2.4 帧转一格）：负载摊得
更粗，但**单帧峰值与按帧分散时相同**——一格该派发多少就派发多少，只是有些帧不做切片工作。

同一帧内**不会重复访问同一档位**：一帧补多格时，第 lod 档只走帧内的前 2^lod 格——该档本只有
2^lod 个切片相位，前 2^lod 格恰好把它们各覆盖一次。这条保证了两件事：「每帧派发」的对象不会被
切片档反超（20fps 下每帧补 3 格，不截断的话 `Tier1` 会在一帧里轮到约 1.5 次、比 `Tier0` 还频繁），
以及同一对象不会在一帧里收到两次回调（同帧的多格共用同一个时刻，第二次的 `deltaTime` 必为 0）。

**档位不是精度，是采样间隔**：它只决定「多久被检查一次」。要 33ms 以内更细的节奏时**没有中间档**
可用——一格以下不存在（派发不可能比帧更密），正确做法是留在 `Tier0`、在对象内按 `deltaTime`
累加出自定周期（框架自己的 `SettingsAutoSaveTicker` 就是这么做的）。必须立刻反应的事（受击、
玩家指令）应走事件（`MessageManager`），而不是等下一次节拍。

### 档位由谁决定

档位有两条写入通道，分别对应两类需求：

| 需求 | 通道 | 说明 |
| --- | --- | --- |
| **静态档位**（设计决定） | `Register` / `RegisterLate` / `RegisterFixed` 的 `initialTier` | 推荐默认用它——「这个系统就该以 133ms 跑」是设计决定，声明在注册处最清楚 |
| **运行时自适应** | `OnUpdate` / `OnLateUpdate` / `OnFixedUpdate` 的返回值 | 状态变化时表达新档位，**决定下一次**派发（滞后一拍是设计如此） |

框架自己两条都在用：`InputManager` 与 `UIManager` 的每帧驱动器恒返回 `Tier0`，而
`SettingsAutoSaveTicker` 按状态在 `Tier3`（空闲）/ `Tier0`（热窗口）/ `Tier5`（已释放）之间切换。

**外部策略目前没有入口**：「按可见性统一降档」「画质/性能档批量降档」这类由调度器之外的系统决定的
档位，`UpdateManager` 没有对应 API（没有 `SetTier`）。现有两条解法，各有代价：

1. **节点自查全局状态，再用返回值表达**——等于把一条全局策略复制进 N 个节点，策略一改要改 N 处；
2. **注销 + 以新档位重新注册**——代价是首次派发 `deltaTime = 0`（锚定规则），且最坏要等一个整周期
   才轮到首次派发（`Tier7` 约 2.1 秒，见「已知限制」）。

**档位属于设计决定、且对象数量大时，还有第三种范式**（UI 模块在用）：把档位声明在对象上
（`UIViewBase.UpdateTier`，Inspector 可配、运行时可改），由上层管理器按档位分桶、每档注册一个
驱动器承载整桶——调度器只看到「每档一个节点」，档位与对象解耦。

### 派发时机与驱动

每帧由注入 PlayerLoop 的三个驱动系统推进（分别落在 `Update.ScriptRunBehaviourUpdate`、
`PreLateUpdate.ScriptRunBehaviourLateUpdate`、`FixedUpdate.ScriptRunBehaviourFixedUpdate`），
因此**不需要场景里存在 `GameLauncher` 或其它 MonoBehaviour**。

- 注入基于 `PlayerLoop.GetCurrentPlayerLoop()` 且只插入不替换，因此与 UniTask 等同样靠注入
  PlayerLoop 工作的库共存；`IsDrivingPlayerLoop` 可查询三个驱动是否都已生效
- 注入失败会打 `LogWarning`（门面本身是宽容语义、不会抛异常，不留痕的话表现只是「静止」）
- 手动驱动用 `UpdateManager.Tick(time)`（两个变步长时机）与 `TickFixed(fixedTime)`；
  **注入生效时不要再手动调用**，否则同一帧会派发两次

### 时间轴与暂停

| 场景 | 逻辑轴（`Scaled`） | 墙钟轴（`Unscaled`） |
| ---- | ------------------ | -------------------- |
| 正常运行 | 派发，delta 为距上次的真实间隔 | 派发，同样是真实间隔 |
| `Time.timeScale = 0` | **冻结**：不派发、切片相位不推进 | 照常 |
| `UpdateManager.Pause()` | **冻结** + 恢复时重锚（见下） | 照常 |
| `Time.timeScale = 0.5` | 派发；delta 减半，且**节拍按逻辑时间走**，故墙钟周期翻倍 | 照常 |
| `Time.timeScale < 0` | 负间隔钳制为 0 | 照常 |
| `FixedUpdate` 时机 | 随 Unity 固定步停摆 | 不支持（无此轴） |

- **冻结时切片相位不推进**：恢复后节奏与暂停前接续。若照常推进，长周期对象会白丢一轮——
  `Tier5` 意味着半秒多的空窗
- **恢复不追赶**：`Resume()` 会把时间基准重锚，恢复后第一帧的 `deltaTime` 为 0，
  而不是把整段暂停时长一次性补完。确有追赶需求的逻辑请在对象内自行累加
- **注册与重新启用后的首次派发 `deltaTime` 为 0**：调度器无从知道「注册那一刻」在各时间轴上
  是几点（驱动方给的时间轴未必是 Unity 的 `Time.time`——测试与确定性回放都自带时刻），
  因此不去猜，首次派发只负责定锚。禁用期间累积的间隔同样不会被算进来
- **首次派发最坏要等一个整周期**：新条目落在桶内哪个下标上决定了它的切片相位，所以注册后可能
  立刻被轮到，也可能等满 2^k 格——`Tier7` 约 2.1 秒。需要「注册即生效」请调 `ProcessImmediate`，
  或把对象留在细档位
- `Time.timeScale = 0` 与 `Pause()` 的区别：后者不改动 Unity 时间，供「暂停但不希望 UI 动画、
  手柄振动跟着慢下来」的场景使用

### 派发期间的注册 / 注销 / 启用 / 禁用

派发期间（`OnUpdate` 等回调里）发起的这些操作**按调用顺序在帧末统一生效**，因此：

- 当前帧剩余时间里，被注销或禁用的对象仍可能再收到一次回调，但不会出现
  「`OnDisable` 之后又 `OnUpdate`」的倒序
- 同一帧内的多次操作**以后者为准**：`Register → Unregister → Register` 得到「注册一次」
- 无需担心遍历中被改动：派发期间没有任何代码会改活表
- `IsEnabled` 会反映尚未落表的待处理操作，与帧末状态一致

`Clear()` 是例外之外的一点：它**不回调 `OnDisable`**（与 `Unregister` 一致），
但会一并复位暂停开关。

### 立即处理

`ProcessImmediate` 用于「逻辑变化后需要立刻响应，不等下一次时间切片」。注意它在**派发期间
调用不会执行更新**，只重置时间基准（在别人的 `OnUpdate` 里再次回调自己会形成嵌套派发）。

## 设计原则

- **不依赖场景对象** — `UpdateManager` 自注入 PlayerLoop 驱动，不依赖任何 MonoBehaviour；
  调用方直接调 `Register` / `RegisterLate` / `RegisterFixed` 登记自身
- **显式注册** — 时机与时间轴都由注册时的实参与接口实现决定，调度器不做任何自动发现
- **单一写入点** — 所有注册/注销/启用/禁用/迁移都经内部操作队列，帧末由唯一入口应用到桶
- **避免 GC** — 每帧路径无 LINQ、无闭包、无装箱；`UpdateClock` 是栈上结构体

## 已知限制

- **约 30fps 及以下时 `Tier1` 与 `Tier0` 同频**：帧长达到 2 格后，`Tier1`（2 格一轮）每帧都会被
  轮到一次，档位单调但不再更省。这是刻意的取舍——帧率低于 30 时对象本就无法比帧更早被观察到，
  让它在帧内多跑一次只会加重最吃紧设备的每帧负担
- **帧长超过 50ms（低于约 20fps）时周期会拉长**：每帧最多补 3 格，补不上就丢弃整格债务
  而不是累积到后续帧（宁可延长也不突发），故此时实际周期随帧率线性变长。上限取 3 而非更小，
  正是为了让 30fps 附近的**抖动**不至于被截断——上限若恰好压在 30fps 上，抖动会让个别帧
  「应补 3 格」而被砍掉，而截断只砍多、不补少
- **不追赶**：暂停恢复后不补算暂停期间的逻辑
- **`ProcessImmediate` 派发期间只重置时间基准**，不执行更新
- **重新启用会回到 `Tier0` 桶**：桶号本身就是档位，条目移入禁用表时该信息已丢失
- **同时手动 `Tick` 且注入生效会派发两次**：注入生效时请只依赖自动驱动

## 依赖

- 无框架内模块依赖（`UpdateClock` 的时间由驱动方传入，`UpdateManager` 与调度器不读 `UnityEngine.Time`）
- 无第三方依赖（PlayerLoop 注入使用 Unity 自带的 `UnityEngine.LowLevel`）
