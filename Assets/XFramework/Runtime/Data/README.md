# XData 运行时数据模块

## 概述

XData 是 XFramework 的运行时可变数据管理模块，负责管理游戏运行过程中产生和变更的数据（如玩家状态、背包物品、任务进度等），并提供存档/读档能力。

### 与 Config 模块的区别

| 特性     | Config (XConfig)                  | Data (XData)               |
| -------- | --------------------------------- | -------------------------- |
| 数据来源 | CSV / ScriptableObject 等只读配置 | 运行时动态产生             |
| 可变性   | 只读                              | 可增删改                   |
| 存档     | 不参与                            | 可序列化存档               |
| 存储     | 内存表                            | 内存表 + 文件持久化        |
| 泛型约束 | `IConfigRow<TKey>` (只读 Id)      | `IDataRow<TKey>` (可写 Id) |

## 架构

```
GameDataNode (节点树启动)
    └── DataManagerImpl (内部实现)
            ├── Table : Dictionary<Type, IDataTable>
            ├── Global : Dictionary<Type, object>
            └── Store  : IDataStore (持久化)

DataManager (静态门面)
    └── 转发到 DataManagerImpl
```

### 设计原则

- **节点树 + 静态服务混合**：`GameDataNode` 挂载在节点树上管理生命周期，初始化后注入 `DataManager` 静态门面供全局访问。
- **Table / Global 两种数据模型**：
  - **Table**：按主键索引的集合数据（如多个背包物品），对应 `DataTable<T>`。
  - **Global**：单一实例数据（如玩家金币数），对应直接类型注册。
- **可插拔存储**：通过 `IDataStore` 接口支持多种序列化方案，默认提供 `JsonFileDataStore`（基于 `JsonUtility` + `PlayerPrefs` / 文件系统）。

## 快速开始

### 一、定义数据模型

```csharp
using System;
using XFramework.XData;

[Serializable]
public class InventoryItem : IDataRow<int>
{
    public int Id { get; set; }        // 主键
    public string Name;
    public int Count;
}

[Serializable]
public class PlayerProgress : IDataRow<string>
{
    public string Id { get; set; }     // 主键，例如 "main_story"
    public int CompletedLevel;
    public int TotalScore;
}

[Serializable]
public class PlayerCurrency
{
    public int Gold;
    public int Gems;
}
```

### 二、读写 Table 数据

```csharp
using XFramework.XData;

// 获取或创建 Table
var inventory = DataManager.GetOrCreateTable<InventoryItem>();

// 添加物品
var sword = new InventoryItem { Id = 1, Name = "铁剑", Count = 1 };
inventory.Upsert(sword);

// 查询
if (inventory.TryGet(1, out var item))
    Debug.Log($"物品: {item.Name}, 数量: {item.Count}");

// 更新（同 Upsert）
item.Count += 5;
inventory.Upsert(item);

// 遍历
foreach (var invItem in inventory.GetAll())
    Debug.Log($"ID: {invItem.Id}, {invItem.Name} x{invItem.Count}");

// 删除
inventory.Remove(1);
```

### 三、读写 Global 数据

```csharp
// 获取或创建
var currency = DataManager.GetOrCreateGlobal<PlayerCurrency>();
currency.Gold += 100;

// 查询
if (DataManager.TryGetGlobal<PlayerCurrency>(out var cur))
    Debug.Log($"金币: {cur.Gold}, 宝石: {cur.Gems}");
```

### 四、存档与读档

```csharp
using Cysharp.Threading.Tasks;

// 保存
await DataManager.SaveAsync("slot_1");

// 加载
await DataManager.LoadAsync("slot_1");

// 检查存档是否存在
if (DataManager.HasSave("slot_1"))
    await DataManager.DeleteSave("slot_1");
```

## 自定义存储

实现 `IDataStore` 或继承 `FileDataStore`：

```csharp
public class BinaryFileDataStore : FileDataStore
{
    protected override string GetPath(string name)
        => Path.Combine(Application.persistentDataPath, $"{name}.dat");

    // 重写 Save/Load 实现二进制序列化
}

// 设置自定义存储
DataManager.SetStore(new BinaryFileDataStore());
```

## ⚠️ 注意事项

### 序列化要求
- 所有数据模型必须标记 `[Serializable]` 并使用 `public` 字段或 `[SerializeField]` 标记私有字段，因为默认 `JsonFileDataStore` 基于 `JsonUtility`。
- **不支持多态序列化**。如果数据模型中有接口/基类引用字段，`JsonUtility` 无法正确序列化。此时应实现自定义 `IDataStore`（如 Newtonsoft.Json 或二进制方案）。

### Table 查询
- `DataTable<T>` 的泛型查询方法（`Get<TKey>`, `TryGet<TKey>`, `Contains<TKey>`）要求在**首次调用时确定主键类型**，之后必须保持一致，否则会触发类型不匹配警告/异常。
- 如果同一个 `T` 实现了多个主键接口（不推荐），仅以首次调用 `GetOrCreateTable<T>` 或 `Upsert` 时的行为为准。

### 存档兼容
- 存档使用 `AssemblyQualifiedName` 存储类型信息。如果类型重命名或迁移程序集，旧存档将无法恢复。建议：
  - 类型重命名时使用 `[FormerlySerializedAs]` 或自定义迁移逻辑。
  - 通过 `SaveData.version` 字段实现版本校验。

### 线程安全
- 所有 `DataManager` API **必须在主线程**调用，内部未做线程同步处理。

## 目录结构

```
XData/
├── IDataRow.cs              # 数据行接口定义
├── IDataManager.cs          # 服务接口
├── DataManager.cs           # 静态门面
├── DataManagerImpl.cs       # 内部实现
├── DataTable.cs             # Table 包装器
├── DataException.cs         # 异常类型
├── SaveData.cs              # 存档快照数据结构
├── GameDataNode.cs          # 节点树桥梁
├── Store/
│   ├── IDataStore.cs        # 存储接口
│   ├── FileDataStore.cs     # 文件存储基类
│   └── JsonFileDataStore.cs # JSON 存储默认实现
└── README.md