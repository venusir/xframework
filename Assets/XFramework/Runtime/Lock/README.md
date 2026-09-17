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
public void CastSkill()
{
    // 加锁（技能持续期间锁定移动）
    // 注意：加锁不会失败（同一主体同类型的锁是持有者集合，可叠加），
    // 因此不需要判断「是否获取成功」——没有获取失败的句柄
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

### 全局锁影响范围

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

## 设计原则

- **组合式锁** — 多类型锁独立管理，互不干扰
- **多来源叠加** — 同一类型锁可被多个来源持有，全部释放才解锁
- **LockHandle 安全释放** — 通过 `readonly struct` + `IDisposable` 实现零 GC 的 `using` 安全释放
- **全局锁** — 支持跨主体的全局锁，适合服务器暂停、全屏 Loading 等场景
- **事件驱动** — 锁状态变化可被订阅，解耦业务逻辑

## 依赖

- 无框架内模块依赖（`ILockable` 标记接口定义于本模块）