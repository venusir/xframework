# XPool —— 纯 C# 对象池

## 概述

XPool 是一个**零 GC 分配**的泛型对象池系统，用于复用频繁创建和销毁的 C# 对象。

- **纯 C# 对象**：仅管理引用类型（class），不涉及 GameObject、MonoBehaviour 或资源引用。
- **零配置开箱即用**：首次 `Get<T>()` 时自动创建池，无需初始化。
- **惰性预热**：支持 `Configure<T>(config)` 预创建实例，减少运行时分配。
- **委托回调**：`OnRent` / `OnReturn` / `OnDestroy` 回调，也可通过 `IPoolable` / `IPoolDiscardable` 接口实现。
- **池满丢弃回调**：闲置达到 `MaxSize` 后归还的实例会被丢弃，此时触发 `onDestroy` / `IPoolDiscardable.OnDiscard()`，用于确定性释放资源。
- **Editor 调试**：检测重复归还、租借不匹配等错误，Release 构建零开销。
- **线程不安全**：与 `UnityEngine.Pool` 相同，只能在主线程使用；确需跨线程请在调用侧自行加锁。
- **与 AssetManager 解耦**：AssetManager 内部继续管理 GameObject/Prefab 池，PoolManager 只管纯 C# 对象。

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

| API                                             | 说明                               |
| ----------------------------------------------- | ---------------------------------- |
| `PoolManager.Get<T>()`                          | 从池获取实例，池空时 `new T()`     |
| `PoolManager.Get<T>(generator)`                 | 从池获取实例，自定义生成器         |
| `PoolManager.GetPooled<T>(out item)`            | using 方式获取实例，块结束自动归还 |
| `PoolManager.GetPooled<T>(generator, out item)` | using + 自定义生成器               |
| `PoolManager.Return(item)`                      | 归还实例到池                       |
| `PoolManager.Configure<T>(config)`              | 预配置池（预热数量、最大容量）     |
| `PoolManager.Configure<T>(config, generator)`   | 预配置池 + 自定义生成器            |
| `PoolManager.Configure<T>(config, generator, destroyer)` | 预配置池 + 生成器 + 池满丢弃回调 |
| `PoolManager.HasPool<T>()`                      | 指定类型的池是否已创建             |
| `PoolManager.GetPool<T>()`                      | 获取 IPool<T> 实例（用于高级操作） |
| `PoolManager.RemovePool<T>()`                   | 移除并清空指定类型的池             |
| `PoolManager.ClearAll()`                        | 清空闲置实例（池注册与配置保留，可重复调用，切场景安全） |
| `PoolManager.PoolCount`                         | 已创建的池数量（诊断） |
| `PoolManager.CopyPoolStats(List<PoolStats>)`    | 各池的规模快照（闲置/活跃/累计，诊断；只反映已创建的池） |

## 配置

### 池容量与预热

配置在该类型**首次建池**（首次 `Get` / `GetPool`）时一次性消费生效，之后不再读取。若类型已建过池再调用 `Configure<T>`，会告警并忽略；需要重新配置时请先 `RemovePool<T>()` 再 Configure。

**四个集合池（`ListPool` / `HashSetPool` / `DictionaryPool` / `StringBuilderPool`）的 `Configure` 口径不同**：它们**在「无活跃实例」时生效**——首次 `Get` 之前，**或全部归还之后**都可以重配（会重建池对象）；有活跃实例时才告警忽略。

同一类型全局单池：`Get<T>(generator)` 传入的生成器仅首次建池生效；无参入口使用 Configure 的生成器，显式传入的生成器优先，Configure 的容量与预热仍生效。

```csharp
// 在首次 Get<EnemyData>() 之前配置
PoolManager.Configure<EnemyData>(new PoolConfig
{
    PrewarmSize = 20,    // 初始化时预创建 20 个实例
    MaxSize = 100        // 池中最多保留 100 个闲置实例
});
```

`PrewarmSize` 会钳到 `MaxSize`：预热是「提前造多少」，上限是「最多留多少」，配出 `PrewarmSize > MaxSize` 时以 `MaxSize` 为准。

不配置时 `MaxSize` 为 **无上限**——闲置实例只增不减，直到 `Clear()` / `ClearAll()`。长期运行且池化类型较多时，建议显式设一个上限。

### 第三个字段：`CollectionCheck`

`PoolConfig` 还有一个字段，默认 `true`：

- **`CollectionCheck`** — 重复归还检测（同一实例被 `Return` 多次）。**仅 Editor 下生效，Release 构建自动关闭、零开销**；默认配置（`PoolConfig.Default`）把它设为 `true`。除非你要在 Editor 里刻意压测最坏路径，否则不用动它——它抓的是「用方把同一个对象还了两次」这类缺陷，代价只在编辑器的开发期。

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

即：如果传入了 `onRent` 委托，则不会调用 `IPoolable.OnRent()`。丢弃回调同理：传入了 `onDestroy` 委托就不会调用 `IPoolDiscardable.OnDiscard()`。

## 计数与丢弃语义

| 属性（`IPool<T>`） | 含义                                                                             |
| ------------------ | -------------------------------------------------------------------------------- |
| `CountInactive`    | 池内当前闲置实例数（可立即复用）                                                 |
| `CountActive`      | 已取出、尚未归还的实例数                                                         |
| `CountAll`         | 自建池以来生成过的总实例数——**只增不减**，归还、清空与被丢弃都不会让它减小       |

因此 `CountAll - CountInactive` **不等于** `CountActive`：被丢弃的实例仍计入 `CountAll`，却既非活跃也非闲置。要判断「还有多少实例在外面」请用 `CountActive`。

闲置实例达到 `MaxSize` 后，归还的实例不再入池而是交给 GC，此时触发丢弃回调。两点需要注意：

- 归还回调（`onReturn` / `IPoolable.OnReturn()`）**先于**容量判断触发，所以被丢弃的实例会先收到归还回调（清理状态）、再收到丢弃回调（释放资源）。
- `Clear()` / `ClearAll()` **不触发**丢弃回调——它们清的是闲置实例，且 `ClearAll` 会在应用退出时自动调用，此时跑用户回调容易碰到已销毁的 Unity 对象。

`PooledObject<T>` 的 `Dispose()` 幂等（重复 `using` 或手动调用不会重复归还），但只对同一个存储位置成立：`var h2 = h; h.Dispose(); h2.Dispose();` 这类复制出去的副本仍会归还两次。

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

### 示例 4：using 语法自动归还（零 GC）

```csharp
using XFramework.XPool;

// ===== PoolManager using 语法 =====
void ProcessDamage(DamageMessage msg)
{
    // GetPooled 返回 PooledObject（struct），与 using 配合自动归还，零 GC
    using (PoolManager.GetPooled<BulletData>(out var bullet))
    {
        bullet.Position = transform.position;
        bullet.Velocity = transform.forward * 10f;
        // 使用 bullet...
    } // using 结束自动调用 PoolManager.Return(bullet)
}

// ===== 集合池 using 语法 =====
List<Vector3> CalculatePath(Vector3 start, Vector3 end)
{
    var result = new List<Vector3>();
    using (ListPool<Vector3>.GetPooled(out var waypoints))
    {
        // 临时计算路径点...
        waypoints.Add(start);
        waypoints.Add((start + end) * 0.5f);
        waypoints.Add(end);
        result.AddRange(waypoints);  // 取走需要的数据
    } // using 结束自动 Clear() + Return()
    return result;
}

// ===== StringBuilder using 语法（高频日志场景） =====
void LogHealth(int currentHp, int maxHp)
{
    using (StringBuilderPool.GetPooled(out var sb))
    {
        sb.Append("HP: ").Append(currentHp).Append("/").Append(maxHp);
        Debug.Log(sb.ToString());  // ToString() 后数据已取出
    } // using 结束自动 Clear() + Return()
}
```

## 集合池（CollectionPool）

XPool 内置常用集合类型的静态池，**Return 时自动调用 `Clear()`**，无需手动清空。

### 集合池 API

| API                                       | 说明                                             |
| ----------------------------------------- | ------------------------------------------------ |
| `ListPool<T>.Get()`                       | 获取 `List<T>`，池空时自动 `new List<T>()`       |
| `ListPool<T>.GetPooled(out list)`         | using 方式获取 `List<T>`，块结束自动归还         |
| `ListPool<T>.Return(list)`                | 归还 `List<T>`，自动 `Clear()`                   |
| `HashSetPool<T>.Get()`                    | 获取 `HashSet<T>`                                |
| `HashSetPool<T>.GetPooled(out set)`       | using 方式获取 `HashSet<T>`，块结束自动归还      |
| `HashSetPool<T>.Return(set)`              | 归还 `HashSet<T>`，自动 `Clear()`                |
| `DictionaryPool<K,V>.Get()`               | 获取 `Dictionary<K,V>`                           |
| `DictionaryPool<K,V>.GetPooled(out dict)` | using 方式获取 `Dictionary<K,V>`，块结束自动归还 |
| `DictionaryPool<K,V>.Return(dict)`        | 归还 `Dictionary<K,V>`，自动 `Clear()`           |
| `StringBuilderPool.Get()`                 | 获取 `StringBuilder`                             |
| `StringBuilderPool.GetPooled(out sb)`     | using 方式获取 `StringBuilder`，块结束自动归还   |
| `StringBuilderPool.Return(sb)`            | 归还 `StringBuilder`，自动 `Clear()`             |
| `XXXPool<T>.Configure(PoolConfig config)` | 预配置池参数（首次 `Get()` 前）                  |
| `XXXPool<T>.GetPool()`                    | 获取内部 `IPool<T>` 接口，用于依赖反转           |
| `CollectionPoolManager.ClearAll()`        | 一键清空所有集合池的闲置实例                     |

> **注意：** 泛型集合池按闭合泛型类型独立建池。例如 `ListPool<int>` 和 `ListPool<Vector3>` 是两个独立的池，仅在首次 `Get()` 时创建。
>
> **两套注册表：** `CollectionPoolManager.ClearAll()` 只清集合池，`PoolManager.ClearAll()` 只清业务对象池——切场景时两者都要调（见示例 7）。

### 使用示例 5：集合池

```csharp
using XFramework.XPool;

// List<T> — 临时收集查询结果
var hitResults = ListPool<int>.Get();
Physics.OverlapSphereNonAlloc(position, radius, colliders);
foreach (var col in colliders)
    hitResults.Add(col.GetInstanceID());
// 处理逻辑...
ListPool<int>.Return(hitResults);

// Dictionary<K,V> — 临时映射表
var scoreMap = DictionaryPool<string, int>.Get();
scoreMap["player_a"] = 100;
scoreMap["player_b"] = 200;
int aScore = scoreMap["player_a"];
DictionaryPool<string, int>.Return(scoreMap);

// HashSet<T> — 去重集合
var uniqueIds = HashSetPool<int>.Get();
uniqueIds.Add(1);
uniqueIds.Add(1); // 去重
HashSetPool<int>.Return(uniqueIds);

// StringBuilder — 高频字符串拼接（UI / 日志）
var sb = StringBuilderPool.Get();
sb.Append("HP: ").Append(currentHp).Append("/").Append(maxHp);
healthText.text = sb.ToString();
StringBuilderPool.Return(sb);
```

### 示例 6：预配置集合池容量

```csharp
// 在初始化阶段（如 DefaultGameLauncher.Awake 中）预配置高频集合池
ListPool<Vector3>.Configure(new PoolConfig { PrewarmSize = 8, MaxSize = 64 });
DictionaryPool<string, object>.Configure(new PoolConfig { PrewarmSize = 4, MaxSize = 32 });
StringBuilderPool.Configure(new PoolConfig { PrewarmSize = 4, MaxSize = 32 });

// 之后在运行时代码中无感知使用
var vecList = ListPool<Vector3>.Get();
vecList.Add(transform.position);
ListPool<Vector3>.Return(vecList);
```

### 示例 7：切场景时一键清空

```csharp
// 在 SceneManager.sceneUnloaded 回调中
void OnSceneUnloaded(Scene scene)
{
    CollectionPoolManager.ClearAll();  // 清空所有被触碰过的集合池
    PoolManager.ClearAll();            // 清空业务对象池
    // 二者都只清闲置实例：池注册与 Configure 保留，后续 Get / Return 照常工作
}
```

## 依赖

- Unity 6000.3 或更新版本（与 package.json 的 `unity` 字段一致）
- 无第三方依赖
