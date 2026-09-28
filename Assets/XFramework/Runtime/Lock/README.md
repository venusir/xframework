# XFramework / Lock 模块

## 概述

XFramework 锁模块提供对象级别的锁管理功能。通过 `ILockable` 接口，任意对象都可以成为锁主体，支持多类型锁的并发管理。锁模块通过静态外观 `LockManager` 提供全局锁服务，并支持 `using` 语法自动释放锁句柄。

**命名空间**: `XFramework.XLock`

**典型场景**：UI 面板打开时锁定角色移动、技能动画播放时锁定技能输入、网络请求期间锁定 UI 按钮等——详见下节。

## 典型场景

七族。每族只给最小形状，完整 API 见「快速使用」。

### A. 角色控制封锁：多来源叠加是它唯一的硬价值

技能读条、受击硬直、剧情演出、载具切换、死亡到复活——这些来源会**同时**发生，而它们互不认识：

```csharp
private sealed class DialogueLockToken { }                      // 类型化 token，见「键的语义」
private static readonly object Dialogue = new DialogueLockToken();

using (player.AddLock(LockType.Movement, Dialogue))
{
    await PlayCutsceneAsync();      // 期间受击再加一把，互不干扰
}
```

用 bool 或 `IsMoving` 表达的话，**后关闭的那一方会把别人的封锁一起关掉**——这正是本模块存在的理由。

### B. 全局与系统级：一次加锁，影响所有主体

断线重连、加载屏、场景切换、赛季结算。不必遍历实体逐个封锁：`IsLocked(任何主体, type)` 都返回 true。

```csharp
using (LockManager.AddLock(LockType.Input, LoadingToken))
{
    await LoadSceneAsync();
}
```

> ⚠️ **全局锁没有所有者**：它挂在 `Global` 哨兵上，而哨兵既不是 `MonoBehaviour` 也不实现
> `IDestroyCancellationToken`——**永不自动释放、也不随场景切换清理**。上面那个 `using` 不是可选的：
> 加载/断线这类长流程被中断时，漏放的全局锁会跨场景存活，而它**是全局的**——这是「玩家一直不能动」
> 最常见的成因。

### C. UI 交互门与防重入

请求发出到响应期间禁用按钮：用**请求本身**当持有者，而不是给按钮置一个 bool。

```csharp
if (LockManager.IsLockedBy(ui, LockType.Confirm, request)) return;   // 同一请求正在飞，防重入
using var gate = ui.AddLock(LockType.Confirm, request);
await SendAsync();
```

### D. 替代互相打架的 bool

```csharp
if (!player.IsLocked(LockType.Movement))   // 聚合：自己的锁 + 全局锁一起算
    Move();
```

**等「解锁了再继续」**（框架不提供等待原语，正确写法如下）：

```csharp
if (!LockManager.IsLocked(player, LockType.Movement)) return;   // ← 少了这行会永久挂起
using var sub = player.OnLockStateChanged((t, locked) =>
{
    if (t == LockType.Movement && !locked) tcs.TrySetResult();
});
await tcs.Task.AttachExternalCancellation(ct);
```

> 先查一次不是冗余：**没有锁定时订阅不会收到任何回调**（聚合事件只播报「当前已锁定」的类型），
> 少了那行 `tcs` 永远等不到结果——而且挂起时没有任何症状指向这里。

### E. 状态镜像与表现层

```csharp
// 订阅时立即播报一次当前状态，之后只在聚合值真翻转时回调
using var sub = player.OnLockStateChanged((type, locked) =>
{
    if (type == LockType.Movement) moveStick.SetInteractable(!locked);
});
```

要把它接到 UI 属性上，见「与相邻能力的边界」里的合用桥。

### F. 事故排查

「玩家一直不能动」用 `DumpState()` 一眼看出是谁锁着，见「诊断」；**先怀疑全局锁**（它不会自动释放）。

### G. 生命周期收口

实体回池 / 销毁时自动释放（`AutoReleaseOnDestroy`），或显式 `RemoveAllLocks()`，见「生命周期与清理」。

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

## 与相邻能力的边界

仓内还有两个能力常在同一个问题上被想起，另有一个看着像但不一样：

| 对照 | 它是什么 | 判据 |
|---|---|---|
| **`ReactiveProperty<bool>`** | 布尔**值**：可派生、可绑定 UI、闭合来源的布尔代数 | 封锁来源会不会**在互不认识的情况下增加**？会 → 用锁；不会（就是已知的那几个条件）→ 用 Reactive |
| **`UIMaskHandle`**（`UIManager.ShowMask`） | UI 层**触及**门：引用计数句柄 + owner 联动 + 强制清空逃生阀 | 这个「不能做」是 UI 层级问题（点击穿透）还是**逻辑状态**问题？两者常成对出现，不是二选一 |
| **`PreconditionChain` / `IUIController`** | **单次操作**的允许 / 拒绝（可含 `await` 校验），不留状态 | 是「这一下能不能做」还是「这段时间都不能做」？前者用校验链，后者用锁 |

**合用的桥**：门禁用 Lock，显示用 `ReactiveProperty<bool>`——订阅 `OnLockStateChanged` 把聚合值写进属性，
再交给现有的 UI 绑定通路。反过来（把 `ReactiveProperty` 当门禁去查）会丢掉「谁挡着」这条信息。

**锁与遮罩的键语义是相反的**，别混：遮罩每次 `ShowMask` 发一枚**单调令牌**，同一来源持两次算两把；锁的
`lockObj` 是**值相等**，同一 token 加两次只算一把（幂等）。两者都各自被测试钉住了。

**相邻的三个「电平开关」**（`Time.timeScale` / `UpdateManager.Pause` / `InputManager.DisableActionMap`）：
它们都是**单写者**开关，不叠加——两个系统各关一次、一方恢复即丢失另一方的意图。**Lock 的存在就是为了替换
这一类用法**，但不接管它们（各归各的模块，Lock 对框架内模块零依赖）。

> **锁只回答「要不要响应」**：执行侧（`InputManager` 的 ActionMap、面板的 `Raycaster.enabled`）仍可能被别处
> 直接写而绕过。仓内成文的先例是 `UIManager.SetLayerInteractive`——它必须「记住期望值 + 在焦点变化后重贴」，
> 否则一次焦点变化就把外部禁用的意图冲掉。同理：**别处直接改执行侧状态，锁挡不住**。

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
（字符串字面量会被驻留，必然相等）。**推荐每个加锁点用一枚专用 token——用类型化 token**：

```csharp
// 类型化 token：名字会出现在诊断输出里（DumpState 用 GetType().Name，不碰 ToString）
private sealed class SkillCastingToken { }
private static readonly object SkillCasting = new SkillCastingToken();

using (LockManager.AddLock(player, LockType.Movement, SkillCasting))
{
    // ...
}
```

两种模式按需要选：

| 想要 | 写法 | 效果 |
|---|---|---|
| **幂等 / 共享**（默认） | 每个来源一枚**固定** token | 同一来源重复加锁只算一把，嵌套 `using` 安全 |
| **嵌套计数** | **每次 acquire 一枚新 token**（`new object()`） | 两次 acquire 就是两把锁，各自的 `using` 只解自己那把（代价：每次加锁一次小分配） |

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
- **全局锁没有所有者**：`Global` 哨兵既不是 `MonoBehaviour` 也不实现 `IDestroyCancellationToken`，所以
  **永不自动释放、也不随场景切换清理**——加载/断线这类长流程必须 `using` 或 `try/finally` 收口。
  诊断时 `DumpState` 会把它排在最前并标 `global`（见「典型场景 B」）。
- **聚合事件是电平，不是增量**：回调里抛异常会被隔离（记 `[Lock]` 日志），但**该类型的值不会重播**——
  镜像错位时请主动查一次 `IsLocked` 恢复。
- **全局锁边沿是 O(全部订阅主体)**：一次全局加/解锁会唤醒所有主体订阅者（每个聚合订阅还要各查一次
  `IsLocked`）。加载屏期间恰恰是「全局锁 + 满场实体」同时成立——**订阅面只挂在真正关心的主体上**，
  别给每个敌人都挂聚合订阅。
- **异常日志的事件名不区分聚合订阅**：聚合订阅的回调走的是 `OnLocked` / `OnUnlocked` 的派发路径，所以
  日志里的 `OnLocked subscriber threw` 也可能是聚合订阅的回调（要区分得给每个 handler 带元数据，与组合式
  实现相冲突）。
- **容器未池化**：`AddLock` 对新主体 / 新类型各 `new` 一次字典或集合（加一次就解的临时主体会产生两次
  分配）。容器生命周期与主体、类型同长，不属于「频繁创建销毁」；池化后把它们交给第三方可长期持有的静态
  表，会引入仓内反复警惕的陈旧引用与 ABA 风险。

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
- **`lockObj` 改计数语义**（`Dictionary<object,int>`，照 Unreal GAS 的 loose tag 容器）—— 这**不是**「代价大
  一点」，而是推翻已被 README 与测试钉住的幂等语义，属**破坏性变更**；嵌套计数由「每次 acquire 一枚新
  token」零 API 覆盖（见「键的语义」的两行小表）。
- **`HasAnyLock(subject)` / `GetLockedTypes(subject)`** —— 形状本身有语义歧义：含全局锁会把「全局 UI 锁」
  也算进「移动键该灰」，不含又表达不了「加载屏期间整块置灰」，两种选法都有场景给出错答案。GAS 的对应物是
  `HasAny(调用方给的集合)`——**成熟实现没有「任意类型」这个查询**；调用方自持 `static readonly int[]`
  加一次循环即可（零分配、语义明确）。程序化枚举类型域的需求由 `CopyLockedTypes` 覆盖。
- **`RegisterLockTypeName(int, string)`**（让 dump 打出 `Movement` 而不是 `3`）—— 仓内**没有 int→name 注册表
  的先例**（id 域的先例是 `UILayers` 那样「带名字的 const 类」），而 GAS 需要注册表是因为它的 tag 是**数据
  驱动**的。顺序应当是：先用 `DumpState`，再实测「裸数字到底有没有真妨碍排查」，然后才决定。
- `OnGlobalLocked` / `OnGlobalUnlocked` 改名（如 `OnAnyLocked`）—— 非破坏优先，改为把文档写成与实现一致。
- 订阅者异常**逐条**隔离（`GetInvocationList`）—— 每次派发要分配一个委托数组，取舍与 `Pipeline` 一致，
  已记入「已知限制」。
- **等待原语**（`WaitUntilUnlockedAsync`）—— 与 UI README 的同型裁定一致（「框架**没有**内建的『等你回结果』
  通道」）：等一个由使用方拥有的状态翻转，配方式写法见「典型场景 D」，且**必须先查一次**否则会永久挂起。
- 主线程断言（`MainThreadGuard` 式）—— 本轮只写文档，不动行为。
- `SubscriptionTracker` 那句「全仓订阅只有两处登记点」（Lock 是第三处、且不受跟踪）—— 属 Event 侧文档，
  经裁定本轮不动。

**未决**：
- **跨对象订阅的自动退订**：形态是 `OnLocked(subject, handler, IDestroyCancellationToken owner)` 重载
  （arity 不同，无重载二义风险），本轮未做。
- **`ActionDisposable` 池化的 ABA**：可改成「Rent 返回带代号的包装」消除，代价是每次订阅多一次分配。
  仓内另有更彻底的答案可参考：UI 遮罩用**单调令牌 + 条目表**（`UIManagerImpl`），令牌不回收即无 ABA。
- **`AutoInit` 形态落后于 Update**：本模块仍是 `#if UNITY_EDITOR [InitializeOnLoadMethod] #else
  [RuntimeInitializeOnLoadMethod]` + 裸 `Application.quitting += Dispose`（不幂等）。**关闭域重载**
  （Enter Play Mode Options）时，`#else` 分支根本没编进编辑器程序集 → 进入播放不再执行本回调；而这条
  `quitting` 订阅在关闭域重载时**跨播放会话存活**——重复 `+=` 会逐次累积（正常退出播放仍会触发清理，
  故**危害不在"锁残留"**，而在订阅累积与会话未走到 quitting 时的静态残留）。
  同形的还有 `MessageManager` / `Serializer` / `DesktopFileProvider`——**File 也在名单里**（它的注释
  写了「两个特性都要挂」而代码是旧的，属文档与实现相反），经裁定单独立项统一。