# XPool —— 纯 C# 对象池

## 概述

XPool 是一个**零 GC 分配**的泛型对象池系统，用于复用频繁创建和销毁的 C# 对象。

- **纯 C# 对象**：仅管理引用类型（class），不涉及 GameObject、MonoBehaviour 或资源引用。
- **零配置开箱即用**：首次 `Get<T>()` 时自动创建池，无需初始化。
- **惰性预热**：支持 `Configure<T>(config)` 预创建实例，减少运行时分配。
- **委托回调**：`OnRent` / `OnReturn` 回调，也可通过 `IPoolable` 接口实现。
- **Editor 调试**：检测重复归还、租借不匹配等错误，Release 构建零开销。
- **与 AssetManager 解耦**：AssetManager 内部继续管理 GameObject/Prefab 池，PoolManager 只管纯 C# 对象。

## 文件结构

```
Runtime/Pool/
├── IPoolable.cs                   # 生命周期回调接口（OnRent / OnReturn）
├── PoolConfig.cs                  # 配置结构体（值类型，零装箱）
├── IPool.cs                       # 池操作接口（用于 DI / 测试）
├── Pool.cs                        # 泛型池实现（核心）
├── PoolManager.cs                 # 全局静态管理器
├── PoolManagerExtensions.cs       # 扩展方法（this.GetFromPool<T>()）
└── README.md
```

## 快速开始

```csharp
using XFramework.XPool;
using UnityEngine;

// ===== 零配置使用 =====
public class BulletData : IPoolable
{
    public Vector3 Position;
    public Vector3 Velocity;
    public bool IsAlive;

    void IPoolable.OnRent() => IsAlive = true;
    void IPoolable.OnReturn() => IsAlive = false;
}

class BulletShooter : MonoBehaviour
{
    void Shoot()
    {
        var bullet = PoolManager.Get<BulletData>();
        bullet.Position = transform.position;
        bullet.Velocity = transform.forward * 10f;
    }
}

class BulletSystem
{
    void OnBulletExpire(BulletData bullet)
    {
        PoolManager.Return(bullet);
    }
}
```

## API 速查

| API                                           | 说明                               |
| --------------------------------------------- | ---------------------------------- |
| `PoolManager.Get<T>()`                        | 从池获取实例，池空时 `new T()`     |
| `PoolManager.Get<T>(generator)`               | 从池获取实例，自定义生成器         |
| `PoolManager.Return(item)`                    | 归还实例到池                       |
| `PoolManager.Configure<T>(config)`            | 预配置池（预热数量、最大容量）     |
| `PoolManager.Configure<T>(config, generator)` | 预配置池 + 自定义生成器            |
| `PoolManager.HasPool<T>()`                    | 指定类型的池是否已创建             |
| `PoolManager.GetPool<T>()`                    | 获取 IPool<T> 实例（用于高级操作） |
| `PoolManager.RemovePool<T>()`                 | 移除并清空指定类型的池             |
| `PoolManager.ClearAll()`                      | 清空所有池                         |
| `this.GetFromPool<T>()`                       | 扩展方法，从池获取                 |
| `this.GetFromPool<T>(generator)`              | 扩展方法，从池获取（自定义生成器） |
| `item.ReturnToPool()`                         | 扩展方法，归还实例                 |

## 配置

### 池容量与预热

```csharp
// 在首次 Get<EnemyData>() 之前配置
PoolManager.Configure<EnemyData>(new PoolConfig
{
    PrewarmSize = 20,    // 初始化时预创建 20 个实例
    MaxSize = 100        // 池中最多保留 100 个闲置实例
});
```

### 自定义生成器（无无参构造函数的类型）

```csharp
// 示例：Enemy 需要配置参数
PoolManager.Configure<Enemy>(new PoolConfig { MaxSize = 50 },
    generator: () => new Enemy(enemyConfigSO));
```

### 委托回调（替代 IPoolable）

```csharp
// 直接构造 Pool 实例（绕过 PoolManager）
var pool = new Pool<MyData>(
    generator: () => new MyData(),
    config: new PoolConfig { PrewarmSize = 5 },
    onRent: item => item.Reset(),
    onReturn: item => item.Cleanup()
);
```

## 回调优先级

当同时传入委托和实现 `IPoolable` 接口时：

```
委托回调 > IPoolable 接口
```

即：如果传入了 `onRent` 委托，则不会调用 `IPoolable.OnRent()`。

## 与 AssetManager 的关系

| 功能     | PoolManager              | AssetManager                    |
| -------- | ------------------------ | ------------------------------- |
| 管理对象 | 纯 C# 引用类型           | GameObject / Prefab 实例        |
| 池 key   | `Type`                   | `string location`（资源路径）   |
| 归还语义 | 仅放回池                 | 放回池 + 释放 YooAsset 资源引用 |
| 生命周期 | 静态，应用退出时自动清理 | `InitializeAsync` / `Dispose`   |

两者完全独立，互不依赖。GameObject 的池化仍由 AssetManager 通过 `InstantiateAsync` / `DestroyInstance` 管理。

## GC 与性能

| 特性            | 实现                                        |
| --------------- | ------------------------------------------- |
| **内部存储**    | `Stack<T>` — 连续内存，无链式节点分配       |
| **配置**        | `PoolConfig` 是 `struct` — 栈分配，0 GC     |
| **泛型调用**    | 直接泛型方法，无 `object` 转换，无拆箱      |
| **惰性创建**    | 首次 `Get<T>()` 时才建池，非预热类型 0 开销 |
| **Editor 检测** | `#if UNITY_EDITOR` 包裹，Release 零开销     |

## 使用示例

### 示例 1：子弹数据池

```csharp
public class BulletData : IPoolable
{
    public Vector3 StartPosition;
    public Vector3 Direction;
    public float LifeTime;
    public bool IsActive;

    void IPoolable.OnRent() => IsActive = true;
    void IPoolable.OnReturn() => IsActive = false;
}

// 射击系统
class WeaponSystem
{
    void Fire()
    {
        var bullet = PoolManager.Get<BulletData>();
        bullet.StartPosition = muzzleTransform.position;
        bullet.Direction = muzzleTransform.forward;
        bullet.LifeTime = 0f;
    }
}

// 子弹更新系统
class BulletUpdateSystem
{
    void UpdateBullets()
    {
        // 遍历活跃子弹...
        foreach (var bullet in activeBullets)
        {
            bullet.LifeTime += Time.deltaTime;
            if (bullet.LifeTime > maxLifeTime)
            {
                PoolManager.Return(bullet);
            }
        }
    }
}
```

### 示例 2：寻路节点池

```csharp
// 零配置，直接使用
void FindPath(Vector3 start, Vector3 end)
{
    var currentNode = PoolManager.Get<PathfindingNode>();
    currentNode.Position = start;
    // ... 寻路逻辑 ...
    PoolManager.Return(currentNode);
}
```

### 示例 3：消息数据池

```csharp
// 配合 MessageManager 使用
void SendDamageEvent(int damage)
{
    var msg = PoolManager.Get<DamageMessage>();
    msg.Damage = damage;
    this.Publish(msg);
    PoolManager.Return(msg);  // 消息发送后可立即归还
}
```

### 示例 4：扩展方法语法

```csharp
class MyComponent : MonoBehaviour
{
    void DoSomething()
    {
        // 通过扩展方法获取，无需显式引用 PoolManager 类名
        var data = this.GetFromPool<MyData>();
        data.Value = 10;
        // ...
        data.ReturnToPool();
    }
}
```

## 依赖

- Unity 2022.3 LTS 或更新版本
- 无第三方依赖

## 版本记录

| 版本  | 说明     |
| ----- | -------- |
| 1.0.0 | 初始版本 |