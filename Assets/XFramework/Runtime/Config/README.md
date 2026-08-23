# Config 配置系统

## 概述

`XConfig` 提供统一的配置加载与查询接口，支持三种数据接入方式，覆盖内置格式、自定义格式和已反序列化数据的注入。

**命名空间**：`XFramework.XConfig`

- **Table 配置**：多行数据，按主键索引，通过 `ConfigTable<T>` 包装器查询
- **Global 配置**：单例数据，通过 `GetGlobal<T>()` 获取

所有配置加载后常驻内存，通过 `Unload<T>()` 显式释放。

> **相关模块**：`ScriptableObject` 格式经 [Asset 模块](../Asset/README.md)（YooAsset）加载；自定义序列化格式可与 [Serialize 模块](../Serialize/README.md) 的 `ISerializer` 配合；与运行时可变数据模块 Data 的区别见 [Data 模块 README](../Data/README.md) 的对比表。

---

## 快速开始

```csharp
using XFramework.XConfig;

// 1. 定义行类型（struct，零 GC）
[Serializable]
public struct ItemRow : IConfigRow<int>
{
    public int Id { get; set; }
    public string Name;
    public int Price;
}

// 2. 初始化 & 加载
ConfigManager.Initialize();
var items = await ConfigManager.PreloadTableAsync<ItemRow>("config/items", ConfigFormat.Json);

// 3. 查询（TKey 由实参自动推断，无需指定）
var row = items.Get(1001);
Debug.Log($"Item: {row.Name}, Price: {row.Price}");
```

---

## 三种接入方式

| 方式       | 入口                                        | 适用场景                                                          |
| ---------- | ------------------------------------------- | ----------------------------------------------------------------- |
| **方式一** | `PreloadTableAsync<T>(path, ConfigFormat)`  | 内置格式（Json / ScriptableObject / CSV），框架负责 IO + 反序列化 |
| **方式二** | `PreloadTableAsync<T>(path, IConfigLoader)` | 自定义一文件一表格式（protobuf、MessagePack 等）                  |
| **方式三** | `RegisterTable<T>(ConfigTable<T>)`          | 一文件多表（Luban）或第三方已在外部完成反序列化                   |

方式一和方式二由框架控制异步加载流程，方式三由第三方完全控制反序列化时机。

---

### 方式一：内置格式（Json / ScriptableObject / CSV）

适用于项目使用标准 Json、ScriptableObject 或 CSV 存储配置，每个文件对应一张表。

```csharp
// 定义行类型
[Serializable]
public struct ItemRow : IConfigRow<int>
{
    public int Id { get; set; }
    public string Name;
    public int Price;
}

// Json 加载
var items = await ConfigManager.PreloadTableAsync<ItemRow>("config/items", ConfigFormat.Json);

// CSV 加载
var npcs = await ConfigManager.PreloadTableAsync<NpcRow>("config/npcs", ConfigFormat.Csv);

// ScriptableObject 加载
var settings = await ConfigManager.PreloadTableAsync<SettingsRow>("config/settings", ConfigFormat.ScriptableObject);

// 查询
var row = items.Get(1001);
Debug.Log(row.Name);
```

---

### 方式二：自定义 Loader（一文件一表）

当配置使用 Json / ScriptableObject / CSV 以外的格式，且每个文件只包含一张表时，实现 `IConfigLoader` 并传入。

`IConfigLoader` 是临时策略对象，框架不持有引用，调用后可由 GC 回收。

```csharp
// 需要 using System.Linq;（ToDictionary）、using Cysharp.Threading.Tasks;、using XFramework.XConfig;
// 1. 实现 IConfigLoader（以 protobuf 单表为例；ProtobufSerializer 为示意，替换为实际序列化库）
public class ProtobufLoader : IConfigLoader
{
    // 注意：方法签名必须与接口一致——双泛型 <T, TKey>，约束为 IConfigRow<TKey>
    public async UniTask<ConfigTable<T>> LoadTableAsync<T, TKey>(string assetPath)
        where T : IConfigRow<TKey>, new()
    {
        var bytes = await LoadBinary(assetPath);
        var list = ProtobufSerializer.Deserialize<List<T>>(bytes);
        // 构造 ConfigTable（构造函数接收 IDictionary，Dictionary<TKey, T> 直接满足），框架自动提取主键
        var table = new ConfigTable<T>(list.ToDictionary(r => r.Id));
        return table;
    }

    public async UniTask<T> LoadGlobalAsync<T>(string assetPath)
        where T : class, new()
    {
        var bytes = await LoadBinary(assetPath);
        return ProtobufSerializer.Deserialize<T>(bytes);
    }
}

// 2. 使用自定义 Loader 加载
var protoLoader = new ProtobufLoader();
var items = await ConfigManager.PreloadTableAsync<ItemRow>("config/items.pb", protoLoader);

// 3. Loader 之后可丢弃，查询 API 与方式一完全一致
var row = items.Get(1001);
```

> **注意：** 此方式不适合 Luban 等一文件多表格式。同一份二进制文件被多次 Preload（不同 T）会导致重复 IO 和重复反序列化。请改用方式三。

---

### 方式三：Register 直接注入（多表 / 已反序列化数据）

第三方自行完成 IO 和反序列化，直接将数据注入 `ConfigManager`。框架不参与加载过程，仅管理缓存与提供统一查询接口。

**典型场景：Luban（一文件多表）**

```csharp
// 需要 using System.Linq;（ToDictionary）、using XFramework.XConfig;
// 一次 IO，一次反序列化
var bytes = await LoadLubanData();
var tables = new GameTables(new ByteBuf(bytes));

// 多次注册，每张表一个 ConfigTable
ConfigManager.RegisterTable(new ConfigTable<ItemRow>(tables.TbItem.DataList.ToDictionary(r => r.Id)));
ConfigManager.RegisterTable(new ConfigTable<HeroRow>(tables.TbHero.DataList.ToDictionary(r => r.Id)));
ConfigManager.RegisterTable(new ConfigTable<SkillRow>(tables.TbSkill.DataList.ToDictionary(r => r.Id)));

// 查询
var items = ConfigManager.GetTable<ItemRow>();
var row = items.Get(1001);
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
// 需要 using System;（Activator）、using System.Collections;、using System.Collections.Generic;、using System.Linq;
// 动态遍历 Luban Tables 的 Tb 属性自注册。
// 示意：Luban 各版本 DataList 的 API 不同，下方以「行列表 + Id 属性」的通用方式构建字典
foreach (var prop in typeof(GameTables).GetProperties())
{
    if (!prop.Name.StartsWith("Tb"))
        continue;

    var dataList = prop.GetValue(tables);              // 行列表，如 List<ItemRow>
    var rowType = dataList.GetType().GetGenericArguments()[0];
    var dict = BuildKeyDictionary(rowType, dataList);  // 构建 Dictionary<TKey, T>

    // 反射构造 ConfigTable<T>（构造函数接收 IDictionary），以 IConfigTable 传入非泛型重载
    var table = (IConfigTable)Activator.CreateInstance(
        typeof(ConfigTable<>).MakeGenericType(rowType), dict);
    ConfigManager.RegisterTable(rowType, table);
}

// 从行类型实现的 IConfigRow<TKey> 提取主键类型，按 Id 属性构建 Dictionary<TKey, T>
private static IDictionary BuildKeyDictionary(Type rowType, object dataList)
{
    var keyType = rowType.GetInterfaces()
        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConfigRow<>))
        .GetGenericArguments()[0];
    var idProp = typeof(IConfigRow<>).MakeGenericType(keyType).GetProperty("Id");

    var dict = (IDictionary)Activator.CreateInstance(
        typeof(Dictionary<,>).MakeGenericType(keyType, rowType));
    foreach (var row in (IEnumerable)dataList)
        dict.Add(idProp.GetValue(row), row);
    return dict;
}
```

---

## 批量加载

通过 `ConfigManifest` 声明式描述所有配置及其分组，配合 `PreloadGroupAsync` / `PreloadAllAsync` 实现批量加载，避免分散调用。

```csharp
// 1. 构建清单
var manifest = new ConfigManifest();
manifest.AddTable<ItemRow>("config/items", "Core");
manifest.AddTable<SkillRow>("config/skills", "Combat");
manifest.AddGlobal<GameConfig>("config/game", "Core");

// 2. 按分组加载（仅加载 "Core" 组）
await ConfigManager.PreloadGroupAsync("Core", manifest);

// 3. 或一次加载全部
await ConfigManager.PreloadAllAsync(manifest);

// 4. 查询与普通加载完全一致
var items = ConfigManager.GetTable<ItemRow>();
```

> `ConfigManifest` 为纯 C# 类，不依赖 ScriptableObject，方便版本控制和代码生成。批量加载方法支持可选 `CancellationToken`（取消语义见「CancellationToken 取消」）。

---

## 非主键索引

对 Table 构建非主键索引，按任意字段分组查询。索引仅构建一次并自动缓存，后续查询 O(1)。

```csharp
var items = ConfigManager.GetTable<ItemRow>();

// 按 Quality 字段构建索引
var byQuality = items.BuildIndex("Quality", r => r.Quality);

// 查询所有 Epic 品质的物品
var epics = byQuality.Get(ItemQuality.Epic); // IReadOnlyList<ItemRow>

// 支持 TryGet 安全查询
if (byQuality.TryGet(ItemQuality.Legendary, out var legendaries))
{
    foreach (var item in legendaries)
        Debug.Log(item.Name);
}
```

> 构建时 O(n) 遍历全表一次，后续查询 O(1)，零额外 GC。同一 `indexName` 重复调用返回缓存。

---

## 变更事件

订阅 `ConfigManager.ConfigChanged` 事件，在 Table 注册/加载、Global 注册/加载、卸载时接收通知。

```csharp
ConfigManager.Initialize();
ConfigManager.ConfigChanged += OnConfigChanged;

private void OnConfigChanged(Type type)
{
    Debug.Log($"Config changed: {type.Name}");
    // 例如：刷新 UI、重建缓存
}
```

> 事件参数为配置行类型或 Global 配置类型，可通过 `typeof(T)` 判断具体变更。

---

## 加载语义与失败处理

- **重复 Preload**：同一类型已加载时，再次 `Preload*Async` 直接返回缓存数据，不重复 IO；若传入的 assetPath 与首次不同，打 LogWarning（`[Config] 'X' is already loaded from 'old', ignoring new assetPath 'new'. To load from a different path, call Unload<X>() first.`）并忽略新路径。需要换路径加载时，先 `Unload<T>()`。
- **并发 Preload**：同一类型的并发调用共享同一进行中的加载任务，不会重复 IO；加载失败时所有等待者收到同一异常实例。
- **加载失败**：抛 `ConfigException`（包装底层异常，如资源不存在、反序列化失败），调用方应 try/catch 处理；批量加载（`PreloadGroupAsync` / `PreloadAllAsync`）中单项失败同样抛异常中断。
- **未加载查询**：`GetTable<T>()` / `GetGlobal<T>()` 在未加载时抛 `ConfigException`；`TryGetTable` / `TryGetGlobal` / `TryGet` 返回 `false`，不抛异常。
- **Unload 在途**：某类型仍在加载中时调用 `Unload<T>()` 会打 LogWarning——在途加载仍会完成并注册数据，卸载可能不生效；若确需阻止该配置就绪，请在加载完成后再次 `Unload<T>()`。

---

## 完整 API 参考

### 初始化 & 生命周期

| 方法                          | 说明                          |
| ----------------------------- | ----------------------------- |
| `Initialize()`                | 初始化默认实现                |
| `SetInstance(IConfigManager)` | 注入自定义实现（用于测试/DI） |
| `Destroy()`                   | 销毁并释放所有数据            |
| `IsInitialized`               | 是否已初始化（静态属性）      |

### Preload（方式一 / 方式二）

| 方法                                                             | 说明                          |
| ---------------------------------------------------------------- | ----------------------------- |
| `PreloadTableAsync<T>(string path, ConfigFormat format = Json)`  | 使用内置格式加载 Table        |
| `PreloadTableAsync<T>(string path, IConfigLoader loader)`        | 使用自定义 Loader 加载 Table  |
| `PreloadGlobalAsync<T>(string path, ConfigFormat format = Json)` | 使用内置格式加载 Global       |
| `PreloadGlobalAsync<T>(string path, IConfigLoader loader)`       | 使用自定义 Loader 加载 Global |

> 以上方法均支持可选 `CancellationToken` 参数（取消语义见「CancellationToken 取消」）。

### 批量预加载

| 方法                                                       | 说明               |
| ---------------------------------------------------------- | ------------------ |
| `PreloadGroupAsync(string group, ConfigManifest manifest)` | 按分组批量加载     |
| `PreloadAllAsync(ConfigManifest manifest)`                 | 加载清单中全部配置 |

### Register（方式三）

| 方法                                              | 说明                        |
| ------------------------------------------------- | --------------------------- |
| `RegisterTable<T>(ConfigTable<T> table)`          | 注入已反序列化的 Table 数据 |
| `RegisterTable(Type rowType, IConfigTable table)` | 非泛型重载，供反射调用      |
| `RegisterGlobal<T>(T config)`                     | 注入 Global 配置单例        |

### Query — Table

| 方法                                     | 说明                               |
| ---------------------------------------- | ---------------------------------- |
| `GetTable<T>()`                          | 获取 Table 包装器，未加载时抛异常  |
| `TryGetTable<T>(out ConfigTable<T>)`     | 安全获取 Table 包装器              |
| `Get<T, TKey>(TKey key)`                 | 按主键直接获取行（无需先拿包装器） |
| `TryGet<T, TKey>(TKey key, out T value)` | 安全按主键获取行                   |

**ConfigTable\<T\> 成员：**

| 成员                                  | 说明                                                        |
| ------------------------------------- | ----------------------------------------------------------- |
| `.Get<TKey>(key)`                     | 按主键查询，TKey 自动推断                                   |
| `.TryGet<TKey>(key, out value)`       | 安全查询                                                    |
| `.Contains<TKey>(key)`                | 判断主键是否存在                                            |
| `.GetAll()`                           | 获取所有行（零 GC，返回内部缓存数组，**不要修改**）         |
| `.Count`                              | 行数                                                        |
| `.TryGet(predicate, out value)`       | 按条件查首行，未找到返回 false（零 GC）                     |
| `.GetRows(predicate, List<T> result)` | 按条件查多行，追加填充（零 GC）                             |
| `.GetRows(predicate)`                 | 按条件查多行（便捷版，返回新列表）                          |
| `.GetRows(predicate, comp, result)`   | 按条件查多行并按比较器排序（追加填充，零 GC）               |
| `.GetRows(predicate, comp)`           | 按条件查多行并排序（便捷版，返回新列表）                    |
| `.Exists(predicate)`                  | 是否存在满足条件的行（零 GC）                               |
| `.BuildIndex<TIndex>(name, selector)` | 构建非主键索引                                              |

> **条件查询定位**：`.TryGet(predicate)` / `.GetRows(predicate)` 是 O(n) 全表扫描，适合一次性/低频条件查询；高频固定条件查询请用 `.BuildIndex<TIndex>()`（构建一次、O(1) 查询）。
>
> **顺序语义**：`GetAll()` / `GetRows` 的结果顺序为构造时字典枚举的快照顺序（无删除操作时通常等于插入序，但**不保证**与配置文件行序一致）；依赖严格顺序请按字段自行排序。

### Query — Global

| 方法                            | 说明                             |
| ------------------------------- | -------------------------------- |
| `GetGlobal<T>()`                | 获取 Global 配置，未加载时抛异常 |
| `TryGetGlobal<T>(out T config)` | 安全获取 Global 配置             |

### 索引

| 类型                                              | 说明                                                              |
| ------------------------------------------------- | ----------------------------------------------------------------- |
| `ConfigTable<T>.BuildIndex<TIndex>(string, Func)` | 构建索引（O(n)，仅首次）                                         |
| `ConfigIndexView<T, TIndex>.Get(key)`             | 按索引键查询，返回 `IReadOnlyList<T>`（键不存在返回空数组，非 null） |
| `ConfigIndexView<T, TIndex>.TryGet(key, out)`     | 安全查询索引                                                      |

### 变更事件

| 成员                          | 说明                           |
| ----------------------------- | ------------------------------ |
| `ConfigManager.ConfigChanged` | 配置变更事件（`Action<Type>`） |

### 卸载

| 方法            | 说明                                  |
| --------------- | ------------------------------------- |
| `Unload<T>()`   | 卸载指定类型的配置（Table 或 Global） |
| `IsLoaded<T>()` | 判断指定类型是否已加载                |

---

## 支持的数据格式

```csharp
public enum ConfigFormat
{
    Json = 0,             // JSON，通过 JsonUtility 反序列化
    ScriptableObject = 1, // ScriptableObject，通过 AssetManager（YooAsset）加载
    Csv = 2,              // CSV，第一行为表头，后续行为数据行
}
```

---

## IConfigLoader 接口

```csharp
public interface IConfigLoader
{
    UniTask<ConfigTable<T>> LoadTableAsync<T, TKey>(string assetPath) where T : IConfigRow<TKey>, new();
    UniTask<T> LoadGlobalAsync<T>(string assetPath) where T : class, new();
}
```

- 无状态设计：建议实现为无字段的轻量对象，每次使用 `new` 传入即可
- `assetPath` 由调用方定义语义（文件路径、AssetBundle 地址等），Loader 自行解析
- `LoadTableAsync<T, TKey>` 返回 `ConfigTable<T>`（构造函数接收 `IDictionary`，如 `Dictionary<TKey, T>`），框架从中提取内部字典进行管理

---

## 高级特性

### 复合主键

`IConfigRow<TKey>` 支持 `ValueTuple` 作为复合主键：

```csharp
[Serializable]
public struct SkillEffectRow : IConfigRow<(int skillId, int level)>
{
    public int SkillId;
    public int Level;
    public int Damage;
    public (int skillId, int level) Id => (SkillId, Level);
}

var table = await ConfigManager.PreloadTableAsync<SkillEffectRow>("config/effects");
var row = table.Get((1001, 5)); // 双键查询
```

### 自定义实现注入

通过 `SetInstance` 注入自定义 `IConfigManager` 实现，适用于测试、依赖注入或完全替换内部逻辑：

```csharp
var customManager = new MyCustomConfigManager();
ConfigManager.SetInstance(customManager);

// 之后所有 ConfigManager 静态调用委托到 customManager
```

> 注入的实例生命周期由调用方管理，`Destroy()` 只会清除静态引用，不会销毁注入的实例。

### CancellationToken 取消

所有 Preload 与批量加载方法均支持可选 `CancellationToken`：

```csharp
var cts = new CancellationTokenSource();

// 500ms 超时
cts.CancelAfter(TimeSpan.FromMilliseconds(500));
await ConfigManager.PreloadTableAsync<ItemRow>("config/items", cancellationToken: cts.Token);
```

> **取消语义**：取消仅中断调用方等待（`await` 抛出 `OperationCanceledException`）；底层加载仍会完成并注册数据。取消后若需要该配置，直接通过 `GetTable<T>()` 等查询即可，无需重新加载。

### struct vs class 行类型

|         | struct                            | class                              |
| ------- | --------------------------------- | ---------------------------------- |
| GC 分配 | 零 GC                             | 每次装箱/缓存产生 GC               |
| 可变性  | 通过 `{ get; set; }` 属性保持可变 | 引用类型，注意不要意外修改缓存数据 |
| 推荐    | ✅ 优先使用 struct                 | 仅在需要继承或多态时使用 class     |

> 框架对 `IConfigRow` 不约束 struct 或 class，两者均可实现。

---

## 架构说明

```
ConfigManager (静态外观)
    └── IConfigManager (接口)
            └── ConfigManagerImpl (默认实现)
                    ├── _tables: Dictionary<Type, IDictionary>     (Table 数据)
                    ├── _globals: Dictionary<Type, object>         (Global 数据)
                    ├── _tableWrappers: Dictionary<Type, object>   (ConfigTable 缓存)
                    ├── _assetPaths: Dictionary<Type, string>      (首次加载路径，用于路径变化检测)
                    └── _inFlightLoads: Dictionary<Type, ...>      (进行中的加载任务，并发共享)

IConfigLoader (自定义加载器)
    ├── JsonLoader : IConfigLoader
    ├── ScriptableObjectLoader : IConfigLoader
    └── CsvLoader : IConfigLoader

ConfigTable<T> : IConfigTable  (Table 包装器，构造函数接收 IDictionary)
    └── BuildIndex → ConfigIndexView<T, TIndex>  (非主键索引)

ConfigManifest  (批量加载清单)
    └── List<ConfigManifestEntry>  (条目列表)
```

各层职责：
- **ConfigManager**：静态外观，对外暴露所有 API，内部委托到 `IConfigManager` 实例
- **ConfigManagerImpl**：默认实现，管理字典缓存、加载调度（并发共享同一任务）、事件派发
- **IConfigLoader**：策略接口，每种配置格式对应一个实现
- **ConfigTable\<T\>**：只读包装器，封装字典查询，支持索引构建
- **ConfigManifest**：声明式清单，描述配置的加载路径和分组

## 版本记录

| 版本 | 说明 |
| --- | --- |
| 2026-08 | `ConfigTable<T>.GetRows` 新增 `Comparison<T>` 排序重载（缓冲版/便捷版，参照 GameFramework `GetDataRows(Predicate, Comparison)` 形态） |
| 2026-08 | `ConfigTable<T>` 新增谓词条件查询：`TryGet(predicate, out)` 单匹配、`GetRows(predicate[, List<T>])` 多匹配（缓冲版零 GC）、`Exists(predicate)` 存在性判断 |
| 2026-08 | 取消语义澄清（取消仅中断调用方等待，底层加载仍会完成并注册）、CsvLoader 按列名匹配成员、文档对齐实现（示例修正为真实签名、移除未实现的「后处理钩子」描述） |
| 2026-05 | 新增 CSV 格式、非主键索引（`ConfigIndexView`）、批量加载（`ConfigManifest` + `PreloadGroupAsync` / `PreloadAllAsync`）、自定义 `IConfigLoader` 注入、`ConfigTable<T>` 包装器（TKey 由实参自动推断）、`IConfigManager` 接口抽象、`Get` / `TryGet` 便捷查询 |

详细变更见 git log。