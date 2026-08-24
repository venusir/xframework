# XData 运行时数据模块

## 概述

XData 是 XFramework 的运行时可变数据管理模块，负责管理游戏运行过程中产生和变更的数据（如玩家状态、背包物品、任务进度等），并向 SaveLoadModule 提供序列化/反序列化接口。

> **职责分离**：DataManager 不再直接执行文件 I/O。存读档（文件读写、存储后端管理、加密、云同步等）由 **SaveLoadModule**（独立模块，待实现）负责。DataManager 仅暴露 `CreateSnapshot()` 和 `ApplySnapshot(data)` 两个序列化接口。

数据按 **GamePlay 模块** 组织——一个 `IDataBlock` 对应一个游戏子系统，内部自行管理数据结构，不再强制主键约束。

> **从 Table 模型迁移**：v2 版本已将 `DataTable<T>` + `IDataRow` 体系替换为 `IDataBlock`。如果你还在使用旧版 `GetOrCreateTable<T>()` 等 API，请按[迁移指南](#迁移指南)更新代码。

### 与 Config 模块的区别

| 特性     | Config (XConfig)                  | Data (XData)                               |
| -------- | --------------------------------- | ------------------------------------------ |
| 数据来源 | CSV / ScriptableObject 等只读配置 | 运行时动态产生                             |
| 可变性   | 只读                              | 可增删改                                   |
| 存档     | 不参与                            | 可序列化存档                               |
| 存储     | 内存表                            | 内存 Block（持久化由 SaveLoadModule 负责） |
| 数据模型 | `IConfigRow<TKey>` (按主键索引)   | `IDataBlock` (按模块组织)                  |

## 架构

```
GameDataNode (节点树启动)
    └── DataManagerImpl (内部实现)
            └── Blocks : Dictionary<Type, IDataBlock>   ← 按 GamePlay 模块组织

DataManager (静态门面)
    └── 转发到 DataManagerImpl

SaveLoadModule (独立模块，待实现)
    └── 持有 IDataStore，通过 DataManager.CreateSnapshot() / ApplySnapshot() 实现持久化
```

### 设计原则

- **节点树 + 静态服务混合**：`GameDataNode` 挂载在节点树上管理生命周期，初始化后注入 `DataManager` 静态门面供全局访问。
- **Block 数据模型**：所有需要持久化的数据都应实现 `IDataBlock`，按 GamePlay 模块组织（如背包系统、任务系统）。每个 Block 内部可自由使用 List、Dictionary、单值等结构，简单全局设置也可以作为 Block 实现。
- **序列化接口**：`CreateSnapshot()` 遍历所有 Block 调用 `OnSave()` 生成 `DataSnapshot`；`ApplySnapshot(data)` 恢复数据。
- **存读档分离**：文件读写、加密、云同步等持久化操作由 SaveLoadModule（独立模块）负责，不在 DataManager 职责范围内。

## 快速开始

### 一、定义数据块

```csharp
using System;
using System.Collections.Generic;
using XFramework.XData;

[Serializable]
public class BagItem
{
    public int id;
    public string name;
    public int count;
}

[Serializable]
public class BagData : IDataBlock
{
    public string BlockName => "Bag";
    public int DataVersion => 0; // 未引入版本控制时返回 0

    public List<BagItem> Items = new();
    public int Gold;

    // 快照结构（推荐定义内部 [Serializable] struct）
    [Serializable]
    private struct SaveSnap
    {
        public List<BagItem> items;
        public int gold;
    }

    public object OnSave()
    {
        return new SaveSnap { items = Items, gold = Gold };
    }

    public object OnMigrate(object saveData, int fromVersion) => saveData; // 恒等迁移

    public void OnLoad(object data)
    {
        if (data is SaveSnap s)
        {
            Items = s.items ?? new List<BagItem>();
            Gold = s.gold;
        }
    }

    public void OnClear()
    {
        Items.Clear();
        Gold = 0;
    }
}
```

```csharp
[Serializable]
public class QuestData : IDataBlock
{
    public string BlockName => "Quest";
    public int DataVersion => 0;

    public Dictionary<string, int> CompletedQuests = new(); // 任务ID -> 完成次数
    public string ActiveQuestId;

    [Serializable]
    private struct SaveSnap
    {
        public List<KeyValuePair<string, int>> completedQuests;
        public string activeQuestId;
    }

    public object OnSave()
    {
        return new SaveSnap
        {
            completedQuests = new List<KeyValuePair<string, int>>(CompletedQuests),
            activeQuestId = ActiveQuestId
        };
    }

    public object OnMigrate(object saveData, int fromVersion) => saveData;

    public void OnLoad(object data)
    {
        if (data is SaveSnap s)
        {
            CompletedQuests.Clear();
            if (s.completedQuests != null)
            {
                foreach (var kv in s.completedQuests)
                    CompletedQuests[kv.Key] = kv.Value;
            }
            ActiveQuestId = s.activeQuestId;
        }
    }

    public void OnClear()
    {
        CompletedQuests.Clear();
        ActiveQuestId = null;
    }
}
```

### 二、读写 Block 数据

```csharp
using XFramework.XData;

// 获取或创建数据块
var bag = DataManager.GetOrCreateBlock<BagData>();

// 操作数据
bag.Items.Add(new BagItem { id = 1001, name = "铁剑", count = 1 });
bag.Gold += 100;

// 安全获取（可能未创建）
if (DataManager.TryGetBlock<BagData>(out var existingBag))
    Debug.Log($"背包物品数量: {existingBag.Items.Count}");

// 检查是否存在
if (!DataManager.HasBlock<QuestData>())
    DataManager.GetOrCreateBlock<QuestData>();

// 移除模块（触发 OnClear）
DataManager.RemoveBlock<QuestData>();
```

### 三、创建与恢复数据快照

```csharp
using XFramework.XData;

// 导出当前所有 Block 的快照（供 SaveLoadModule 写入文件）
var snapshot = DataManager.CreateSnapshot();

// 从快照恢复（清空现有数据后加载）
DataManager.ApplySnapshot(snapshot);
```

## 序列化原理

1. **导出快照**：`CreateSnapshot()` 遍历所有已注册的 `IDataBlock`，调用 `OnSave()` 获取快照对象，通过 `XSerialize.Serializer` 序列化为字节数组并 Base64 编码，连同 `blockName`、`saveType` 存入 `DataBlockSnapshot`。默认序列化器为 `NewtonsoftSerializer`（基于 Newtonsoft.Json，format = "json"）；`JsonSerializer`（JsonUtility）以 format = "json-utility" 保留，用于读写旧 JsonUtility 格式存档。
2. **恢复快照**：`ApplySnapshot(data)` 先清空所有已注册 Block 的数据（仅清数据、保留注册），再遍历快照，按 `blockName` 索引已注册的 Block（不创建实例、不反射），按 `saveType` 反序列化后调用 `OnLoad(saveData)`。
3. **`OnSave()` 返回 `null`** 的 Block 不参与快照。
4. **旧存档兼容**：快照缺失 `saveType` 或类型无法解析（如类型重命名）时，回退使用 Block 自身类型反序列化并输出 `[Data]` 前缀警告；该用法要求 `OnSave()` 返回类型与 Block 类型一致方可正确恢复。

> 文件 I/O 等持久化逻辑由 **SaveLoadModule**（独立模块，待实现）负责，可通过 `DataManager.CreateSnapshot()` 获取数据后写入文件。

## 注意事项

### 序列化要求
- 快照结构体（含 Block 自身）必须使用 `public` 字段：默认序列化器 `NewtonsoftSerializer`（Newtonsoft.Json）不序列化私有字段，JsonUtility 的 `[SerializeField]` 私有字段约定不再适用；自定义序列化器（如 MessagePack）可放宽。
- **`OnSave()` 返回值**必须可被默认序列化器正确序列化。推荐定义内部 `[Serializable]` struct 作为快照。
- **Dictionary 原生支持**：默认序列化器可直接序列化 Dictionary（旧 JsonUtility 不支持），复杂结构无需再手转 `List<KeyValuePair>`。
- **不建议直接存储 Unity 内置 struct**（如 Vector3）：Newtonsoft 会额外写出只读属性（magnitude 等）导致存档体积膨胀；如需请自行配置 JsonConverter。
- **不支持多态序列化**。如果数据模型中有接口/基类引用字段，SaveLoadModule 可自定义序列化方案，自行遍历 Block 替代 `CreateSnapshot()`。

### 存档兼容
- 快照的 `saveType` 字段以 `AssemblyQualifiedName` 存储 `OnSave()` 返回对象的类型信息。类型重命名或迁移程序集后旧快照的 `saveType` 将无法解析，恢复时回退使用 Block 自身类型并输出警告（需 `OnSave()` 返回类型与 Block 类型一致方可正确恢复）。
- **版本迁移**：`DataBlockSnapshot.version` 写入时为 `IDataBlock.DataVersion`。恢复时若快照版本低于当前 `DataVersion`，在反序列化之后、`OnLoad` 之前按版本差依次执行 `OnMigrate(saveData, fromVersion)`；旧存档无 version 字段时为 0，自动进入迁移链。快照版本高于当前代码版本（如代码回滚）时跳过该块并输出警告，防止旧代码被新结构数据污染。
- **迁移契约**：`OnMigrate` 入参是快照中旧结构的反序列化实例，返回迁移到下一版本的实例（通常就地修改后返回同一实例）。每步迁移只推进一个版本，数据结构变更（新增/调整字段）时 `DataVersion` +1 并提供对应迁移逻辑。
- **旧结构体必须保留**：迁移链入参依赖旧版本快照结构体（如 `SaveSnap`）仍存在于代码中，否则旧档无法反序列化。旧结构体的字段只能新增、不可删除或改名。
- **迁移指南**：已存在、尚未接入版本控制的第三方 Block，补 `DataVersion => 0` + 恒等 `OnMigrate(object s, int v) => s` 即可保持原行为；后续结构变更时再 +1 并实现真实迁移。
- **版本号分工**：`DataSnapshot.version` 保留为存档级格式版本，与块级 `DataBlockSnapshot.version` 相互独立，各自管理。

### 线程安全
- 所有 `DataManager` API **必须在主线程**调用，内部未做线程同步处理。

## 迁移指南（从 v1 Table 模型）

| v1 API                                     | v2 API                              |
| ------------------------------------------ | ----------------------------------- |
| `IDataRow<TKey>`                           | 废弃，Block 内部自由管理键值        |
| `DataManager.GetOrCreateTable<T>()`        | `DataManager.GetOrCreateBlock<T>()` |
| `DataTable<T>.Get() / Upsert() / Remove()` | Block 内部自行定义方法              |
| `table.Upsert(row)`                        | `block.Items.Add(item)` 等          |

迁移步骤：
1. 将原有的 `[Serializable] class X : IDataRow<TKey>` 重构为 `[Serializable] class XData : IDataBlock`
2. 在 Block 内部定义 `OnSave / OnLoad / OnClear` 回调
3. 将 `DataManager.GetOrCreateTable<X>()` 替换为 `DataManager.GetOrCreateBlock<XData>()`

## 目录结构

```
XData/
├── IDataBlock.cs             # 数据块接口定义
├── IDataManager.cs           # 服务接口
├── DataManager.cs            # 静态门面
├── DataManagerImpl.cs        # 内部实现
├── DataException.cs          # 异常类型
├── DataSnapshot.cs           # 存档快照数据结构
└── README.md