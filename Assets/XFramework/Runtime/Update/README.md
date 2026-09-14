# XFramework / Update 模块

## 概述

Update 模块提供统一的更新调度服务，同时管理**节点树**与**静态服务**的更新需求。

- **三个派发时机**：`Update` / `LateUpdate` / `FixedUpdate`，各自一套调度器
- **两条时间轴**：逻辑时间（受 `timeScale`）与墙钟时间（不受影响）
- **LOD 时间切片**：按档位把更新负载摊到各节拍格上（一格 = 1/60 秒），避免帧消耗集中
- **自动驱动**：注入 PlayerLoop，不需要场景里存在任何 MonoBehaviour

**命名空间**: `XFramework.XUpdate`

## 架构设计

```
Runtime/Update/
├── IUpdateable.cs                # 契约：IUpdateLifecycle / IUpdateable / ILateUpdateable /
│                                 #       IFixedUpdateable / IUpdateTimeMode / UpdateLOD
├── UpdateClock.cs                # 时间基：UpdateClock（time + unscaledTime + isPaused）/ UpdateTimeMode
├── UpdateScheduler.cs            # 纯调度逻辑（LOD 分桶 + 时间切片 + 双时间轴），internal
├── UpdateManager.cs              # 静态门面（含 PlayerLoop 注入驱动）
└── UpdateManagerExtensions.cs    # BaseNode 扩展方法 + 时间轴解析
Runtime/Node/Update/
├── IUpdateNode.cs                # 更新服务接口（启用/禁用/立即处理）
└── UpdateNode.cs                 # 节点树桥梁（自动注册/注销 IUpdateable 节点）
```

## 快速使用

### 1. 节点树节点（自动注册）

实现时机接口的节点会被 `UpdateNode` 自动注册，**无需手动操作**：

```csharp
using XFramework.XNode;
using XFramework.XUpdate;

public class MyNode : EntityNode, IUpdateable
{
    public void OnEnable() { }

    public void OnDisable() { }

    public UpdateLOD OnUpdate(float deltaTime, float time)
    {
        // 返回值决定「下一次派发」采用的 LOD 等级（不是当前这次）
        return UpdateLOD.Tier0;
    }
}
```

### 2. 静态服务注册（非节点树对象）

```csharp
public sealed class MyService : IUpdateable
{
    public MyService()
    {
        // 静态服务自身就是实例，注册时传 this（不能用 static class：接口方法需要实例实现）
        UpdateManager.Register(this, depth: 0, initialLOD: UpdateLOD.Tier0);
    }

    public void OnEnable() { }

    public void OnDisable() { }

    public UpdateLOD OnUpdate(float deltaTime, float time) => UpdateLOD.Tier0;
}
```

### 3. 三个时机怎么选

| 时机 | 接口 | 时间基准 | 适用 |
| ---- | ---- | -------- | ---- |
| `Update` | `IUpdateable` | `Time.time` | 绝大多数逻辑 |
| `LateUpdate` | `ILateUpdateable` | `Time.time` | 需要「本帧所有 Update 都已跑完」：跟随移动目标、相机跟随 |
| `FixedUpdate` | `IFixedUpdateable` | `Time.fixedTime` | 物理、确定性模拟（要固定增量而非每帧变化的 delta） |

同一对象可以实现多个接口，会被分别登记到对应时机。手动注册用 `UpdateManager.Register` /
`RegisterLate` / `RegisterFixed`；注销、启用、禁用、查询不分时机（传任一节点即可）。

### 4. 时间轴怎么选

需要「暂停期间仍运行」的逻辑（暂停菜单、UI 动画、手柄振动到期）请声明墙钟轴：

```csharp
// 方式一：节点声明，UpdateNode 自动注册时读取
public class PauseMenuNode : LeafNode, IUpdateable, IUpdateTimeMode
{
    public UpdateTimeMode TimeMode => UpdateTimeMode.Unscaled;
    // ...
}

// 方式二：手动注册时直接指定
UpdateManager.Register(ticker, depth: 0, timeMode: UpdateTimeMode.Unscaled);
```

固定步长时机没有时间轴参数：Unity 的固定步长本就随 `timeScale` 停摆。

## 机制说明

### LOD 分级调度

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

**固定步轴是个例外**：`Time.fixedTime` 每步恰好前进一个固定步长，本就没有需要修正的漂移，
故那里每个固定步推进一格——档位含义是「每 2^k 个**固定步**」（默认 0.02s 一步即
40/80/160/320/640ms），而不是上表那列毫秒数。

被跳过的格**不会丢失时间**：`OnUpdate` 的 `deltaTime` 是「距上次派发的真实间隔」（可能跨
若干格），所以降频不导致速度失真。节点应始终按 `deltaTime` 积分，而不是按调用次数计数。

帧率高于节拍时会出现「本帧不推进」的空帧（144fps 下这圈轮子约每 2.4 帧转一格）：负载摊得
更粗，但**单帧峰值与按帧分散时相同**——一格该派发多少就派发多少，只是有些帧不做切片工作。

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

- **冻结时切片相位不推进**：恢复后节奏与暂停前接续。若照常推进，长周期节点会白丢一轮——
  `Tier5` 意味着半秒多的空窗
- **恢复不追赶**：`Resume()` 会把时间基准重锚，恢复后第一帧的 `deltaTime` 为 0，
  而不是把整段暂停时长一次性补完。确有追赶需求的逻辑请在节点内自行累加
- **注册与重新启用后的首次派发 `deltaTime` 为 0**：调度器无从知道「注册那一刻」在各时间轴上
  是几点（驱动方给的时间轴未必是 Unity 的 `Time.time`——测试与确定性回放都自带时刻），
  因此不去猜，首次派发只负责定锚。禁用期间累积的间隔同样不会被算进来
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

- **静态服务独立于节点树** — `UpdateManager` 不依赖节点树；只有 `UpdateManagerExtensions`
  与 `UpdateNode`（位于 `Node/Update/`）承担桥接
- **节点树自动注册** — 通过 `UpdateNode` 桥梁监听节点树事件，按节点实现的时机接口分别登记
- **单一写入点** — 所有注册/注销/启用/禁用/迁移都经内部操作队列，帧末由唯一入口应用到桶
- **避免 GC** — 每帧路径无 LINQ、无闭包、无装箱；`UpdateClock` 是栈上结构体

## 已知限制

- **帧长超过 50ms（低于约 20fps）时周期会拉长**：每帧最多补 3 格，补不上就丢弃整格债务
  而不是累积到后续帧（宁可延长也不突发），故此时实际周期随帧率线性变长。上限取 3 而非更小，
  正是为了让 30fps 附近的**抖动**不至于被截断——上限若恰好压在 30fps 上，抖动会让个别帧
  「应补 3 格」而被砍掉，而截断只砍多、不补少
- **不追赶**：暂停恢复后不补算暂停期间的逻辑
- **`ProcessImmediate` 派发期间只重置时间基准**，不执行更新
- **重新启用会回到 `Tier0` 桶**：桶号本身就是 LOD，条目移入禁用表时该信息已丢失
- **同时手动 `Tick` 且注入生效会派发两次**：注入生效时请只依赖自动驱动

## 依赖

- `XFramework.XNode` — 仅 `UpdateNode`（节点树桥梁）与 `UpdateManagerExtensions` 需要；
  `UpdateManager` 与调度器本身不依赖节点树
- 无第三方依赖（PlayerLoop 注入使用 Unity 自带的 `UnityEngine.LowLevel`）
