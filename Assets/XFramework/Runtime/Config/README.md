# Config 配置系统

## 概述

`XConfig` 提供统一的配置加载与查询接口，支持三种数据接入方式，覆盖内置格式、自定义格式和已反序列化数据。

所有配置加载后常驻内存，通过 `Unload<T>()` 显式释放。Table 按 `IConfigRow.Id` 索引，Global 为单例获取。

---

## 三种接入方式

| 方式       | 入口                                        | 适用场景                                                    |
| ---------- | ------------------------------------------- | ----------------------------------------------------------- |
| **方式一** | `PreloadTableAsync<T>(path, ConfigFormat)`  | 内置格式（Json / ScriptableObject），框架负责 IO + 反序列化 |
| **方式二** | `PreloadTableAsync<T>(path, IConfigLoader)` | 自定义一文件一表格式（protobuf 单表、MessagePack 等）       |
| **方式三** | `RegisterTable<T>(Dictionary<int, T>)`      | 一文件多表（Luban）或第三方已在外部完成反序列化             |

方式一和方式二由框架控制异步加载流程，方式三由第三方完全控制反序列化时机。

---

## 方式一：内置格式（Json / ScriptableObject）

适用于项目使用标准 Json 或 ScriptableObject 存储配置，每个文件对应一张表。

```csharp
// 1. 定义行类型（struct 零 GC）
[Serializable]
public struct ItemRow : IConfigRow
{
    public int Id { get; set; }
    public string Name;
    public int Price;
}

// 2. 初始化 & 加载
ConfigManager.Initialize();
await ConfigManager.PreloadTableAsync<ItemRow>("config/items", ConfigFormat.Json);

// 3. 查询
var item = ConfigManager.Get<ItemRow>(1001);
Debug.Log(item.Name);
```

---

## 方式二：自定义 Loader（一文件一表）

当配置使用 Json / ScriptableObject 以外的格式，且每个文件只包含一张表时，实现 `IConfigLoader` 并传入。

`IConfigLoader` 是临时策略对象，框架不持有引用，调用后可由 GC 回收。每次调用可传入不同 Loader 实例（或复用同一个）。

```csharp
// 1. 实现 IConfigLoader（以 protobuf 单表为例）
public class ProtobufLoader : IConfigLoader
{
    public async UniTask<Dictionary<int, T>> LoadTableAsync<T>(string assetPath)
        where T : IConfigRow, new()
    {
        var bytes = await LoadBinary(assetPath);
        var list = Serializer.Deserialize<List<T>>(bytes);
        var dict = new Dictionary<int, T>(list.Count);
        foreach (var row in list)
            dict[row.Id] = row;
        return dict;
    }

    public async UniTask<T> LoadGlobalAsync<T>(string assetPath)
        where T : class, new()
    {
        var bytes = await LoadBinary(assetPath);
        return Serializer.Deserialize<T>(bytes);
    }
}

// 2. 使用自定义 Loader 加载
var protoLoader = new ProtobufLoader();
await ConfigManager.PreloadTableAsync<ItemRow>("config/items.pb", protoLoader);

// 3. Loader 之后可丢弃，查询 API 与方式一完全一致
var item = ConfigManager.Get<ItemRow>(1001);
```

**注意：** 此方式不适合 Luban 等一文件多表格式。同一份二进制文件被多次 Preload（不同 T）会导致重复 IO 和重复反序列化。请改用方式三。

---

## 方式三：Register 直接注入（多表 / 已反序列化数据）

第三方自行完成 IO 和反序列化，直接将数据注入 `ConfigManager`。框架不参与加载过程，仅管理缓存与提供统一查询接口。

**典型场景：Luban（一文件多表）**

```csharp
// 一次 IO，一次反序列化
var bytes = await LoadLubanData();
var tables = new GameTables(new ByteBuf(bytes));

// 多次注册，每张表对应一个 Dictionary<int, T>
ConfigManager.RegisterTable(tables.TbItem.DataList.ToDictionary(r => r.Id));
ConfigManager.RegisterTable(tables.TbHero.DataList.ToDictionary(r => r.Id));
ConfigManager.RegisterTable(tables.TbSkill.DataList.ToDictionary(r => r.Id));

// 查询
var item = ConfigManager.Get<ItemRow>(1001);
var hero = ConfigManager.Get<HeroRow>(1);
```

**Global 配置注册：**

```csharp
var globalCfg = new GameGlobalCfg { MaxLevel = 100 };
ConfigManager.RegisterGlobal(globalCfg);

// 查询
var cfg = ConfigManager.GetGlobal<GameGlobalCfg>();
```

**反射批注册（非泛型重载）：**

```csharp
// 动态遍历 Luban Tables 的 Tb 属性自注册
foreach (var prop in typeof(GameTables).GetProperties())
{
    if (prop.Name.StartsWith("Tb"))
    {
        var dataList = prop.GetValue(tables);
        var dataListType = dataList.GetType();
        var rowType = dataListType.GetGenericArguments()[0];
        var dict = dataListType.GetMethod("ToDictionary")?.Invoke(dataList, new[] { null });
        ConfigManager.RegisterTable(rowType, (System.Collections.IDictionary)dict);
    }
}
```

---

## 完整 API 参考

### Preload（方式一 / 方式二）

| 方法                                                       | 说明                          |
| ---------------------------------------------------------- | ----------------------------- |
| `PreloadTableAsync<T>(string path, ConfigFormat format)`   | 使用内置格式加载 Table        |
| `PreloadTableAsync<T>(string path, IConfigLoader loader)`  | 使用自定义 Loader 加载 Table  |
| `PreloadGlobalAsync<T>(string path, ConfigFormat format)`  | 使用内置格式加载 Global       |
| `PreloadGlobalAsync<T>(string path, IConfigLoader loader)` | 使用自定义 Loader 加载 Global |

### Register（方式三）

| 方法                                            | 说明                        |
| ----------------------------------------------- | --------------------------- |
| `RegisterTable<T>(Dictionary<int, T> data)`     | 注入已反序列化的 Table 数据 |
| `RegisterTable(Type rowType, IDictionary data)` | 非泛型重载，供反射调用      |
| `RegisterGlobal<T>(T config)`                   | 注入 Global 配置单例        |

### Query

| 方法                            | 说明                              |
| ------------------------------- | --------------------------------- |
| `Get<T>(int id)`                | 按 Id 查询 Table 行，不存在抛异常 |
| `TryGet<T>(int id, out T row)`  | 安全查询 Table 行                 |
| `GetAll<T>()`                   | 获取 Table 所有行（有数组分配）   |
| `Contains<T>(int id)`           | 判断 Id 是否存在                  |
| `Count<T>()`                    | Table 行数                        |
| `GetGlobal<T>()`                | 获取 Global 配置，未加载抛异常    |
| `TryGetGlobal<T>(out T config)` | 安全获取 Global 配置              |

### 生命周期

| 方法            | 说明               |
| --------------- | ------------------ |
| `Initialize()`  | 初始化配置管理器   |
| `Destroy()`     | 销毁并释放所有数据 |
| `Unload<T>()`   | 卸载指定配置       |
| `IsLoaded<T>()` | 是否已加载         |

---

## IConfigLoader 接口

```csharp
public interface IConfigLoader
{
    UniTask<Dictionary<int, T>> LoadTableAsync<T>(string assetPath) where T : IConfigRow, new();
    UniTask<T> LoadGlobalAsync<T>(string assetPath) where T : class, new();
}
```

- 无状态设计：建议实现为无字段的轻量对象（或值类型），每次使用 `new` 传入即可
- `assetPath` 由调用方定义语义（文件路径、AssetBundle 地址等），Loader 自行解析