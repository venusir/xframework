# Config 配置系统

## 概述

`XConfig` 提供统一的配置加载与查询接口，支持三种数据接入方式，覆盖内置格式、自定义格式和已反序列化数据的注入。

- **Table 配置**：多行数据，按主键索引，通过 `ConfigTable<T>` 包装器查询
- **Global 配置**：单例数据，通过 `GetGlobal<T>()` 获取

所有配置加载后常驻内存，通过 `Unload<T>()` 显式释放。

---

## 快速开始

```csharp
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
// 1. 实现 IConfigLoader（以 protobuf 单表为例）
public class ProtobufLoader : IConfigLoader
{
    public async UniTask<ConfigTable<T>> LoadTableAsync<T>(string assetPath)
        where T : IConfigRow, new()
    {
        var bytes = await LoadBinary(assetPath);
        var list = Serializer.Deserialize<List<T>>(bytes);
        // 构造 ConfigTable，框架自动提取主键
        var table = new ConfigTable<T>(list.ToDictionaryGeneric<T>());
        return table;
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

> `ConfigManifest` 为纯 C# 类，不依赖 ScriptableObject，方便版本控制和代码生成。可使用 `CancellationToken` 取消正在进行的批量加载任务。

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

## 后处理钩子

实现 `IConfigPostProcessor` 接口，在配置加载/注册完成后执行自定义逻辑（如建立跨表关联、校验数据完整性）。

```csharp
public class ItemPostProcessor : IConfigPostProcessor<ItemRow>
{
    public void PostProcess(ConfigTable<ItemRow> table)
    {
        // 校验数据：价格不能为负
        foreach (var row in table.GetAll())
        {
            if (row.Price < 0)
                Debug.LogWarning($"Item {row.Id} has negative price: {row.Price}");
        }
    }
}

// 注册后处理（在 Initialize 之后、加载之前）
ConfigManager.AddPostProcessor(new ItemPostProcessor());

// 加载时自动触发 PostProcess
await ConfigManager.PreloadTableAsync<ItemRow>("config/items");
```

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

## 完整 API 参考

### 初始化 & 生命周期

| 方法                          | 说明                          |
| ----------------------------- | ----------------------------- |
| `Initialize()`                | 初始化默认实现                |
| `SetInstance(IConfigManager)` | 注入自定义实现（用于测试/DI） |
| `Destroy()`                   | 销毁并释放所有数据            |
| `IsInitialized`               | 是否已初始化（静态属性）      |

### Preload（方式一 / 方式二）

| 方法                                                       | 说明                          |
| ---------------------------------------------------------- | ----------------------------- |
| `PreloadTableAsync<T>(string path, ConfigFormat format)`   | 使用内置格式加载 Table        |
| `PreloadTableAsync<T>(string path, IConfigLoader loader)`  | 使用自定义 Loader 加载 Table  |
| `PreloadGlobalAsync<T>(string path, ConfigFormat format)`  | 使用内置格式加载 Global       |
| `PreloadGlobalAsync<T>(string path, IConfigLoader loader)` | 使用自定义 Loader 加载 Global |

> 以上方法均支持 `CancellationToken` 取消参数。

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

| 成员                                  | 说明                                  |
| ------------------------------------- | ------------------------------------- |
| `.Get<TKey>(key)`                     | 按主键查询，TKey 自动推断             |
| `.TryGet<TKey>(key, out value)`       | 安全查询                              |
| `.Contains<TKey>(key)`                | 判断主键是否存在                      |
| `.GetAll()`                           | 获取所有行（零 GC，返回内部缓存数组） |
| `.Count`                              | 行数                                  |
| `.BuildIndex<TIndex>(name, selector)` | 构建非主键索引                        |

### Query — Global

| 方法                            | 说明                             |
| ------------------------------- | -------------------------------- |
| `GetGlobal<T>()`                | 获取 Global 配置，未加载时抛异常 |
| `TryGetGlobal<T>(out T config)` | 安全获取 Global 配置             |

### 索引

| 类型                                              | 说明                                  |
| ------------------------------------------------- | ------------------------------------- |
| `ConfigTable<T>.BuildIndex<TIndex>(string, Func)` | 构建索引（O(n)，仅首次）              |
| `ConfigIndexView<T, TIndex>.Get(key)`             | 按索引键查询，返回 `IReadOnlyList<T>` |
| `ConfigIndexView<T, TIndex>.TryGet(key, out)`     | 安全查询索引                          |

### 后处理

| 方法                                                         | 说明           |
| ------------------------------------------------------------ | -------------- |
| `ConfigManager.AddPostProcessor<T>(IConfigPostProcessor<T>)` | 注册后处理钩子 |

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
    ScriptableObject = 1, // ScriptableObject，通过 Resources.Load 加载
    Csv = 2,              // CSV，第一行为表头，后续行为数据行
}
```

---

## IConfigLoader 接口

```csharp
public interface IConfigLoader
{
    UniTask<ConfigTable<T>> LoadTableAsync<T>(string assetPath) where T : IConfigRow, new();
    UniTask<T> LoadGlobalAsync<T>(string assetPath) where T : class, new();
}
```

- 无状态设计：建议实现为无字段的轻量对象，每次使用 `new` 传入即可
- `assetPath` 由调用方定义语义（文件路径、AssetBundle 地址等），Loader 自行解析
- `LoadTableAsync<T>` 返回 `ConfigTable<T>`，框架从中提取内部字典进行管理

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

所有异步加载方法均支持 `CancellationToken` 取消正在进行的任务：

```csharp
var cts = new CancellationTokenSource();

// 500ms 超时
cts.CancelAfter(TimeSpan.FromMilliseconds(500));
await ConfigManager.PreloadTableAsync<ItemRow>("config/items", cancellationToken: cts.Token);
```

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
                    ├── _tables: Dictionary<Type, IDictionary>  (Table 数据)
                    ├── _globals: Dictionary<Type, object>      (Global 数据)
                    ├── _tableWrappers: Dictionary<Type, object> (ConfigTable 缓存)
                    └── _postProcessors: Dictionary<Type, List>  (后处理钩子)

IConfigLoader (自定义加载器)
    ├── JsonLoader : IConfigLoader
    ├── ScriptableObjectLoader : IConfigLoader
    └── CsvLoader : IConfigLoader

ConfigTable<T> : IConfigTable  (Table 包装器)
    └── BuildIndex → ConfigIndexView<T, TIndex>  (非主键索引)

ConfigManifest  (批量加载清单)
    └── ConfigManifestEntry[]  (条目列表)
```

各层职责：
- **ConfigManager**：静态外观，对外暴露所有 API，内部委托到 `IConfigManager` 实例
- **ConfigManagerImpl**：默认实现，管理字典缓存、加载调度、后处理分发、事件派发
- **IConfigLoader**：策略接口，每种配置格式对应一个实现
- **ConfigTable\<T\>**：只读包装器，封装字典查询，支持索引构建
- **ConfigManifest**：声明式清单，描述配置的加载路径和分组