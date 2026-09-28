# XFramework / Lock 模块

## 概述

XFramework 锁模块提供对象级别的锁管理功能。通过 `ILockable` 接口，任意对象都可以成为锁主体，支持多类型锁的并发管理。锁模块通过静态外观 `LockManager` 提供全局锁服务，并支持 `using` 语法自动释放锁句柄。

**命名空间**: `XFramework.XLock`

**典型场景**：UI 面板打开时锁定角色移动、技能动画播放时锁定技能输入、网络请求期间锁定 UI 按钮等。

## 架构设计

```
Runtime/Lock/
├── LockManager.cs                # 静态外观（全局入口）
├── ILockable.cs                  # 可锁标记接口
├── LockableExtensions.cs         # ILockable 扩展方法
└── LockHandle.cs                 # 锁句柄（读存储，支持 using）
```

## 快速使用

### 1. 锁类型定义

```csharp
// 定义锁类型枚举（推荐使用常量或枚举）
public static class LockType
{
    public const int Movement = 1;   // 移动锁
    public const int Skill = 2;      // 技能锁
    public const int UI = 3;         // UI 锁
    public const int Damage = 4;     // 伤害锁
}
```

> **`lockType` 是全框架共享的 `int` 命名空间**：两个模块各自定义 `Movement = 1`，那就是同一把锁——
> 编译期毫无提示，运行期互相影响（A 的锁会让 B 的查询也返回 true）。跨模块使用（尤其第三方插件）
> 请先约定分段或集中登记，别让两个模块都从 1 开始各数一遍。

### 2. 使用静态 API

```csharp
using XFramework.XLock;

// 加锁
LockManager.AddLock(player, LockType.Movement, "skill_casting");
LockManager.AddLock(player, LockType.Skill, "cooldown");
LockManager.AddLock(player, LockType.Movement, "dialogue_open");

// 查询锁状态
bool canMove = !LockManager.IsLocked(player, LockType.Movement);
bool canUseSkill = !LockManager.IsLocked(player, LockType.Skill);

// 获取锁数量
int lockCount = LockManager.GetLockCount(player, LockType.Movement);

// 获取所有锁对象列表（调试用）
var lockObjects = LockManager.GetLockObjects(player, LockType.Movement);

// 释放锁
LockManager.RemoveLock(player, LockType.Movement, "skill_casting");
LockManager.RemoveLock(player, LockType.Skill, "cooldown");
LockManager.RemoveLock(player, LockType.Movement, "dialogue_open");
```

### 3. 使用 LockHandle（推荐）

```csharp
// 通过 using 自动管理锁生命周期
public async UniTask CastSkill()
{
    // 加锁（技能持续期间锁定移动）
    // 注意：加锁不会失败（同一主体同类型的锁是持有者集合，可叠加），
    // 因此不需要判断「是否获取成功」——没有获取失败的句柄
    // 唯一的例外是主体已销毁：那时句柄不持有锁（IsHeld 为 false），见「生命周期与清理」
    using var handle = LockManager.AddLock(player, LockType.Movement, "skill_casting");

    // 播放技能动画...
    await PlaySkillAnimation();

    // using 结束时自动释放锁
}
```

**判断持锁状态**：`handle.IsHeld` 是**逐句柄精确**的实时查询（"我这一把还在不在"），
与 `LockManager.IsLocked(subject, type)` 的**聚合**语义不同——后者在"还有别人持有同类型锁"时
同样返回 true：

```csharp
var handle = LockManager.AddLock(player, LockType.Movement, "skill");
lockManagerIsLocked = LockManager.IsLocked(player, LockType.Movement); // true（聚合）
handleIsHeld        = handle.IsHeld;                                   // true（逐句柄）

handle.Dispose();
lockManagerIsLocked = LockManager.IsLocked(player, LockType.Movement); // 无其他持有者时为 false
handleIsHeld        = handle.IsHeld;                                   // false（自己已释放）
```

### 4. 使用全局锁

```csharp
// 全局锁不绑定到任何特定主体，适用于全游戏级别的锁定
LockManager.AddLock(LockManager.Global, LockType.UI, "loading_screen");

// 检查全局锁（会影响到所有主体的同类型锁判断）
bool isAnythingLocked = LockManager.IsLocked(LockManager.Global, LockType.UI);

LockManager.RemoveLock(LockManager.Global, LockType.UI, "loading_screen");
```

### 5. 订阅锁事件

```csharp
using XFramework.XLock;

// 订阅锁定事件
LockManager.OnLocked(player, lockType =>
{
    Debug.Log($"主体被锁定，类型: {lockType}");
});

// 订阅解锁事件
LockManager.OnUnlocked(player, lockType =>
{
    Debug.Log($"主体被解锁，类型: {lockType}");
});

// 通过 ILockable 扩展方法订阅
player.OnLocked(lockType => Debug.Log($"锁定: {lockType}"));
player.OnUnlocked(lockType => Debug.Log($"解锁: {lockType}"));
```

**聚合锁定状态**：上面两个事件是「每个 (主体,类型) 集合的**边沿**」，与 `IsLocked` 的聚合语义并不
一致——全局锁释放时，仍被自己的锁挡住的主体照样会收到 `OnUnlocked`。要维护「现在能不能动」这类状态
镜像，用聚合事件（只在 `IsLocked(subject, lockType)` 真的翻转时回调，订阅时立即播报一次当前已锁定的
类型）：

```csharp
// 订阅时立即同步回调一次(当前锁定则为 true)，之后只在聚合值翻转时回调
using var sub = player.OnLockStateChanged((lockType, isLocked) =>
{
    if (lockType == LockType.Movement)
        moveButton.interactable = !isLocked;
});
```

> **两个 `OnGlobal*` 事件的名字**：其中的 "Global" 指**框架级事件总线**，不是「只有全局锁才触发」
> ——它对每一把首次锁 / 最后一次解锁都触发（主体锁也算）。只关心全局锁请写
> `LockManager.OnLocked(LockManager.Global, handler)`（以 `Global` 订阅 = 只收到全局锁的加/解锁）。

## ILockable 扩展方法

实现了 `ILockable` 的类型可以直接使用便捷的扩展方法：

```csharp
public class Player : ILockable
{
    public void TryMove()
    {
        // 检查是否被锁定
        if (this.IsLocked(LockType.Movement))
            return;

        // 加锁
        using var lockHandle = this.AddLock(LockType.Movement, "moving");

        // 执行移动...
    }

    public void OpenDialogue()
    {
        // 加锁，自动绑定到 this
        this.AddLock(LockType.Movement, "dialogue");

        // 对话结束
        this.RemoveLock(LockType.Movement, "dialogue");
    }
}
```

## 诊断

排查「玩家一直不能动」「谁的锁忘了放」这类问题时，三个入口：

```csharp
// ① 数字：现在有多少锁、挂在多少主体上、其中多少永远不会自动释放
var s = LockManager.GetSnapshot();      // 零分配，O(主体数 × 类型数)
Debug.Log(s);                           // LockStateSnapshot(主体 3, 锁 5, 未绑定 1, 订阅主体 2, 未绑定订阅 1)

// ② 文本：逐主体列出类型与持有者，Global 排最前并标 global
Debug.Log(LockManager.DumpState());     // 低频接口，允许分配

// ③ 程序化读取：某主体当前被哪些 lockType 锁着（聚合视角，含全局锁）
var types = new List<int>();
int n = player.CopyLockedTypes(types);  // 零分配：调用方持缓冲、先清空、返回条数、行序未定义
bool blockedByAnything = n > 0;
```

**口径**——`GetSnapshot` 的字段都是「可行动的判据」，不是凑数的计数：

| 字段 | 它回答的问题 |
|---|---|
| `LockedSubjectCount` / `LockCount` | 规模 |
| **`UnboundSubjectCount`** | **有锁但没有销毁绑定**——这批永远不会自动释放，只能靠调用方显式 `RemoveAllLocks` 收口（`AutoReleaseOnDestroy` 为 `false` 时它必然等于主体总数） |
| `SubscribedSubjectCount` | 两张订阅表的键之并集 |
| **`UnboundSubscribedSubjectCount`** | 有订阅但没有销毁绑定 = 孤儿订阅，静态表永久残留 |

两条使用须知：

- **持有者名一律是类型名**（`GetType().Name`）：`DumpState` **不**调用持有者的 `ToString()`——那是用户代码，
  可能在诊断路径上二次抛。所以 token 要用「键的语义」里的**类型化 token**，dump 里才会出现有意义的列
  （用 `new object()` 的话这一列全是 `Object`）。
- **这一组不是业务分支的依据**（与 `IEventStream.SubscriptionCount` 同属「拉取面」）：它反映的是实现此刻
  的状态；玩法判断请查 `IsLocked`。

## 机制说明

### 多锁叠加

同一类型的锁支持叠加（多个来源各自加锁），**只有当该类型所有锁都被释放时，锁主体才恢复为解锁状态**。

```
加锁顺序: Skill("cooldown") → Skill("mp_insufficient") → Skill("stun")
查询 IsLocked(Skill): true
释放 Skill("cooldown") → IsLocked(Skill): true
释放 Skill("mp_insufficient") → IsLocked(Skill): true
释放 Skill("stun") → IsLocked(Skill): false  ← 全部释放后才解锁
```

### 键的语义：`lockObj` 是持有者身份

`lockObj` 不是「附带说明」，它**就是持有者身份**——存在 `HashSet<object>` 里、按**值相等**判定，
于是**键相等 = 同一把锁**：

```csharp
LockManager.AddLock(player, LockType.Movement, "dialogue");   // 对话系统
LockManager.AddLock(player, LockType.Movement, "dialogue");   // 另一个系统恰好用了同一个字面量

LockManager.GetLockCount(player, LockType.Movement);          // → 1，不是 2

// 其中一方释放时——
LockManager.RemoveLock(player, LockType.Movement, "dialogue");
LockManager.IsLocked(player, LockType.Movement);              // → false：另一方的锁也被一并解掉
```

值语义本身是有意的（同一来源重复加锁即幂等）；危险的是**两个互不相关的来源取了同一个名字**
（字符串字面量会被驻留，必然相等）。**推荐每个加锁点用一枚专用 token**：

```csharp
private static readonly object SkillCastingToken = new object();

using (LockManager.AddLock(player, LockType.Movement, SkillCastingToken))
{
    // ...
}
```

主体（`ILockable`）那一侧规则相反：按**引用同一**判定，不看 `Equals` 重写（见 `ILockable` 的文档）——
主体是「锁谁」，身份就该是那个对象本身；`lockObj` 是「谁锁的」，是一个调用方选定的名字。

## 全局锁影响范围

全局锁（`LockManager.Global`）对某个类型的锁定，会影响**所有主体**的该类型锁判断：

```csharp
// 全局锁定移动
LockManager.AddLock(LockManager.Global, LockType.Movement, "server_pause");

// 所有主体的移动锁都被判定为锁定
LockManager.IsLocked(player, LockType.Movement);   // → true
LockManager.IsLocked(enemy, LockType.Movement);    // → true

// 全局解锁后恢复
LockManager.RemoveLock(LockManager.Global, LockType.Movement, "server_pause");
```

## 生命周期与清理

锁与订阅都挂在 `LockManager` 的静态表上，键是**主体本身**。主体在持有锁/订阅的状态下死亡（实体销毁、
切场景、回池），若没人释放，那一项会永久留存——主体自己再也解不开，而且因为表里是**强引用键**，
该对象也无法被 GC 回收。两条收口路径：

**① 自动（推荐）**：主体是 `MonoBehaviour`（用 `destroyCancellationToken`），或实现了
`XMessage.IDestroyCancellationToken` 的普通对象时，销毁会自动释放它的全部锁并丢弃它的全部订阅：

```csharp
public sealed class Player : MonoBehaviour, ILockable { }

LockManager.AddLock(player, LockType.Movement, SkillToken);   // 挂上后，player 销毁时自动释放
LockManager.AutoReleaseOnDestroy = false;                     // 或整项目关掉这套（默认 true）
```

销毁时的顺序是**先丢订阅、再放锁**：濒死对象自己的回调不会再跑一遍用户代码，而**别人**的订阅者
（含两个总线事件）照常收到解锁——批量清理与逐把手动释放走同一条通知语义。

**② 显式**：既非 `MonoBehaviour` 也非 `IDestroyCancellationToken` 的主体不会被自动释放（不告警，
由调用方负责），或在需要提前收口时手动调用：

```csharp
int released = player.RemoveAllLocks();        // 释放该主体的全部锁，返回释放数量
player.RemoveAllSubscriptions();               // 只丢订阅，不动锁
```

> **加锁的唯一失败形态**：主体已销毁（令牌已取消）时 `AddLock` 会被忽略——返回的句柄
> `IsHeld` 为 `false` 并记一条 `[Lock]` 告警。之所以不「先加上再让回调清掉」，是因为那样会派发
> 一对「加了又解」的幻影事件，订阅者无从与真实事件区分。关掉 `AutoReleaseOnDestroy` 即恢复
> 「一律加上」的行为。

## 设计原则

- **组合式锁** — 多类型锁独立管理，互不干扰
- **多来源叠加** — 同一类型锁可被多个来源持有，全部释放才解锁
- **LockHandle 安全释放** — 通过 `readonly struct` + `IDisposable` 实现零 GC 的 `using` 安全释放
- **全局锁** — 支持跨主体的全局锁，适合服务器暂停、全屏 Loading 等场景
- **事件驱动** — 锁状态变化可被订阅，解耦业务逻辑
- **生命周期收口** — 主体销毁时自动释放（`AutoReleaseOnDestroy`），或经 `RemoveAllLocks` 显式收口

## 依赖

- `XMessage.IDestroyCancellationToken`（公开接口，用于「非 MonoBehaviour 主体」的销毁绑定）
- `MessageManager.TryBindToDestroy` 未复用：它绑的是**订阅者**的令牌而本模块要绑**主体**的，且它丢弃
  `CancellationTokenRegistration`——逐次注册会在长寿命主体的令牌上堆积永不回收的回调节点

## 已知限制

- **主线程专用**：四个容器都不加锁、也没有线程断言（与 `XEvent` 的流、`DisposableBag` 同一约定）。
  销毁令牌的回调跑在**取消者线程**上——跨线程取消 `IDestroyCancellationToken` 会并发改写容器
  （`MonoBehaviour.destroyCancellationToken` 由 Unity 在主线程取消，不受影响）。
- **跨对象订阅不在自动释放的覆盖范围内**：自动释放绑的是**主体**的令牌。A 订阅 B 的锁事件、A 先死时
  不会自动退订——需持有句柄或用 `DisposableBag` 收口。
- **既非 `MonoBehaviour` 也非 `IDestroyCancellationToken` 的主体不会被自动释放**（也不告警）：由调用方
  用 `RemoveAllLocks` / `RemoveAllSubscriptions` 显式收口。
- **同一条多播里的订阅者会被排在前面抛异常的那个饿死**：主体分支与两个总线事件是整条多播一次
  try/catch（只有全局派发分支是逐订阅者隔离）。取舍同 `Pipeline.DispatchSafely`——`GetInvocationList`
  逐个隔离要给每次派发分配一个委托数组。
- **`lockObj` 按值相等判定**（键相等 = 同一把锁）、**`lockType` 是全框架共享的 `int` 命名空间**：
  见「键的语义」。
- **池化订阅句柄的 ABA**：`ActionDisposable` 归还池后被重新租出时，原持有者的第二次 `Dispose()` 会
  静默退掉**新**订阅。常见形态（同一句柄调两次）由 `_targetDict == null` 挡住，只有「旧句柄跨越一次
  归还」才会命中。
- **`ILockable` 必须由引用类型实现**：struct 每次装箱都是新身份，键永不相等。

## 设计取舍

- **订阅表用多播委托 + 快照派发**，不改成「每订阅一个节点」（XEvent 的链表形态）：快照已经解决重入，
  节点化则要给每次订阅分配对象、给句柄引入按节点定位的问题，收益不成立。快照用**池**而不是复用单字段
  ——回调里再加一把全局锁会重入派发，单字段会被内层清空。
- **主体键按引用同一、`lockObj` 保持值相等**：主体是「锁谁」，身份就是那个对象；`lockObj` 是「谁锁的」，
  是调用方选定的名字。两条规则不同是有意的（见「键的语义」）。
- **自动释放没有复用 `MessageManager.TryBindToDestroy`**：它绑订阅者的令牌（本模块要绑主体），且丢弃
  registration。本模块自持「每主体一次、主体空闲时注销」的绑定。
- **不告警**：主体两类都不是时不记 warning（`UIManager.BindToContext` 在同类情况下会告警）。理由：
  `Global` 是合法主体、加锁边沿可能高频，逐次告警是噪音。
- **`Dispose()` 不改名**：它的语义是「重置并继续可用」，名字沿用已久且被 `Application.quitting` 与
  各 fixture 调用；误导的是它原先所在的 `#region Reset`（已改名）。
- **两份 `ActionDisposable` 不合并**（本模块与 `XMessage.Internal`）：仓内既定取舍是「不为十行适配器
  建立跨模块实现依赖」，且本模块这份是池化的、形状本就不同。

## 审计记录

2026-09-27 按 `Documentation/ModuleAudit.md` 审计一轮（本轮修了什么见 `CHANGELOG` 的 `[Unreleased]`）；
本节只留**下一轮需要知道的**。

**已评估未采纳**：
- `lockObj` 改引用相等 —— 会推翻既有幂等语义（同一来源重复加锁幂等）。改为文档化 + 推荐专用 token。
- `OnGlobalLocked` / `OnGlobalUnlocked` 改名（如 `OnAnyLocked`）—— 非破坏优先，改为把文档写成与实现一致。
- 订阅者异常**逐条**隔离（`GetInvocationList`）—— 每次派发要分配一个委托数组，取舍与 `Pipeline` 一致，
  已记入「已知限制」。
- 主线程断言（`MainThreadGuard` 式）—— 本轮只写文档，不动行为。
- `SubscriptionTracker` 那句「全仓订阅只有两处登记点」（Lock 是第三处、且不受跟踪）—— 属 Event 侧文档，
  经裁定本轮不动。

**未决**：
- **跨对象订阅的自动退订**：形态是 `OnLocked(subject, handler, IDestroyCancellationToken owner)` 重载
  （arity 不同，无重载二义风险），本轮未做。
- **`ActionDisposable` 池化的 ABA**：可改成「Rent 返回带代号的包装」消除，代价是每次订阅多一次分配。