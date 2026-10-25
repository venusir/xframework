# Config —— 配置表：Json / CSV / ScriptableObject 三种格式 + 声明式批量加载

## 概述

**命名空间**：`XFramework.XConfig`

`XConfig` 提供统一的配置加载与查询接口，支持三种数据接入方式，覆盖内置格式、自定义格式和已反序列化数据的注入。

- **Table 配置**：多行数据，按主键索引，通过 `ConfigTable<T>` 包装器查询
- **Global 配置**：单例数据，通过 `GetGlobal<T>()` 获取

所有配置加载后常驻内存，通过 `Unload<T>()` 显式释放。

> **相关模块**：三种格式的读取都经 [Asset 模块](../Asset/README.md)（YooAsset）——`ScriptableObject` 读资源、Json / CSV 读 `TextAsset`，**都要求 `AssetManager` 已初始化**；自定义序列化格式可与 [Serialize 模块](../Serialize/README.md) 的 `ISerializer` 配合；与运行时可变数据模块 Data 的区别见 [Data 模块 README](../Data/README.md) 的对比表。

---

## 快速使用

```csharp
using XFramework.XConfig;

// 1. 定义行类型（struct，零 GC）
[Serializable]
public struct ItemRow : IConfigRow<int>
{
    public int Id;  // 主键必须是字段：Json / ScriptableObject 不序列化属性
    public string Name;
    public int Price;

    // 字段不能实现接口属性：必须显式实现 IConfigRow<TKey>.Id 去读字段
    int IConfigRow<int>.Id => Id;
}

// 2. 初始化 & 加载
ConfigManager.Initialize();
var items = await ConfigManager.PreloadTableAsync<ItemRow>("config/items", ConfigFormat.Json);

// 3. 查询（TKey 由实参自动推断，无需指定）
var row = items.Get(1001);
Debug.Log($"Item: {row.Name}, Price: {row.Price}");
```

---

## 模块结构

```text
Runtime/Config/
├── ConfigManager.cs          # 静态外观（全局入口）
├── IConfigManager.cs         # 可替换的后端契约
├── ConfigManagerImpl.cs      # internal：默认实现：五个字典（表 / 全局 / 包装器 / 路径 / 在途）+ 事件派发
├── IConfigRow.cs             # 行契约（TKey Id，含复合主键用法）
├── IConfigTable.cs           # 表契约的非泛型面（零反射注入路径用）
├── ConfigTable.cs            # 表包装器（构造接收 IDictionary，含条件查询与 BuildIndex）
├── ConfigIndexView.cs        # 非主键索引视图（Get / TryGet）
├── IConfigLoader.cs          # 自定义加载器接口（策略对象，框架不持有引用）
├── JsonLoader.cs             # internal：内置 JSON（JsonUtility）
├── CsvLoader.cs              # internal：内置 CSV（Split(',')，方言边界见「已知限制」）
├── ScriptableObjectLoader.cs # internal：内置 ScriptableObject（经 AssetManager；同文件含公开基类 ScriptableObjectTable<,>）
├── ConfigLoadHelper.cs       # internal：泛型委托缓存（消除加载调用的反射）
├── ConfigTextLoader.cs       # internal：文本类格式的公共读入段
├── ConfigTypeHelper.cs       # internal：主键类型推导（reflection 边界，结果缓存）
├── ConfigFormat.cs           # 内置格式枚举（Json / ScriptableObject / Csv）
├── ConfigLoadKind.cs         # 已加载条目种类（Table / Global）
├── ConfigLoadedInfo.cs       # 诊断回读载荷：CopyLoaded 的列表元素
├── ConfigManifest.cs         # 声明式批量清单（AddTable / AddGlobal + 分组；含 internal 条目）
└── ConfigException.cs        # 本模块统一异常（包装底层异常）
```

文件树中标 `internal` 的是模块内部件，其余是使用方直接接触的公开面。

## 行为契约

### 非主键索引

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

### 加载语义与失败处理

- **重复 Preload**：同一类型已加载时，再次 `Preload*Async` 直接返回缓存数据，不重复 IO；若传入的 assetPath 与首次不同，打 LogWarning（`[Config] 'X' is already loaded from 'old', ignoring new assetPath 'new'. To load from a different path, call Unload<X>() first.`）并忽略新路径。需要换路径加载时，先 `Unload<T>()`。
- **并发 Preload**：同一类型、**同一身份**（Table / Global）的并发调用共享同一进行中的加载任务，不会重复 IO；加载失败时所有等待者收到同一异常实例。同一 CLR 类型同时以 Table 与 Global 身份加载时两者**各自独立**（互不加入、互不等待）——`Unload<T>()` 仍同时清两种身份。
- **加载失败**：抛 `ConfigException`（包装底层异常，如资源不存在、反序列化失败），调用方应 try/catch 处理；批量加载（`PreloadGroupAsync` / `PreloadAllAsync`）中单项失败同样抛异常中断。
- **未加载查询**：`GetTable<T>()` / `GetGlobal<T>()` 在未加载时抛 `ConfigException`；`TryGetTable` / `TryGetGlobal` / `TryGet` 返回 `false`，不抛异常。
- **Unload 在途**：某类型仍在加载中时调用 `Unload<T>()` 会打 LogWarning——在途加载仍会完成并注册数据，卸载可能不生效；若确需阻止该配置就绪，请在加载完成后再次 `Unload<T>()`。

---

## 架构设计

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

## API 参考

### 初始化 & 生命周期

| 方法                          | 说明                          |
| ----------------------------- | ----------------------------- |
| `Initialize()`                | 初始化默认实现                |
| `SetInstance(IConfigManager)` | 注入自定义实现（用于测试/DI） |
| `Destroy()`                   | 丢弃门面持有的实例（**不**取消在途加载） |
| `IsInitialized`               | 是否已初始化（静态属性）      |

### Preload（方式一 / 方式二）

| 方法                                                             | 说明                          |
| ---------------------------------------------------------------- | ----------------------------- |
| `PreloadTableAsync<T>(string path, ConfigFormat format = Json)`  | 使用内置格式加载 Table        |
| `PreloadTableAsync<T>(string path, IConfigLoader loader)`        | 使用自定义 Loader 加载 Table  |
| `PreloadGlobalAsync<T>(string path, ConfigFormat format = Json)` | 使用内置格式加载 Global       |
| `PreloadGlobalAsync<T>(string path, IConfigLoader loader)`       | 使用自定义 Loader 加载 Global |

> **`default` 字面量不能直接当第二个实参**（CONF-016）：`PreloadTableAsync<T>(path, default)` 与
> `PreloadGlobalAsync<T>(path, default)` 都会在 `ConfigFormat` / `IConfigLoader` 两个重载间二义（CS0121）。
> 省略格式请写 `ConfigFormat.Json`，或显式强转 `(ConfigFormat)default` / `(IConfigLoader)loader`。
>
> **`loader` 为 null 的校验在首次加载路径**（含已有在途）：抛 `ConfigException`（R4-CONF-001）。
> 已加载后传 null 仍返回缓存，不会被当成非法请求。

> 以上方法均支持可选 `CancellationToken` 参数（取消语义见「典型场景」的「CancellationToken 取消」）。

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

> **非泛型重载会校验行类型**：`RegisterTable(Type rowType, IConfigTable table)` 要求
> `table.Data` 非 null，且其 `Dictionary<TKey, T>` 的键/值类型与 `rowType` 声明一致——
> 不匹配在**注册期**抛 `ConfigException`（与 `ConfigTable<T>` 构造函数的判据相同），
> 不会等到查询时才在包装器构造处失败。

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

### 诊断回读

| 方法 | 说明 |
| ---- | ---- |
| `InFlightLoadCount` | 进行中的加载任务数（并发调用共享同一任务） |
| `CopyLoaded(List<ConfigLoadedInfo>)` | 已加载的表 / 全局配置清单（类型、种类、条目数、资源路径），回答「这个项目加载了哪些配置」 |

> 两者**只对内置实现成立**：`SetInstance` 注入自定义 `IConfigManager` 后一律返回空/0——那类实现不提供回读。它们**刻意不进 `IConfigManager`**（给主接口加成员会让所有实现者编译不过）。

### 索引

| 类型                                              | 说明                                                              |
| ------------------------------------------------- | ----------------------------------------------------------------- |
| `ConfigTable<T>.BuildIndex<TIndex>(string, Func)` | 构建索引（O(n)，仅首次）                                         |
| `ConfigIndexView<T, TIndex>.Get(key)`             | 按索引键查询，返回 `IReadOnlyList<T>`（键不存在返回空数组，非 null） |
| `ConfigIndexView<T, TIndex>.TryGet(key, out)`     | 安全查询索引                                                      |

### 变更事件

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

| 成员                          | 说明                           |
| ----------------------------- | ------------------------------ |
| `ConfigManager.ConfigChanged` | 配置变更事件（`Action<Type>`） |

> 事件参数为配置行类型或 Global 配置类型，可通过 `typeof(T)` 判断具体变更。
>
> **注入自定义实现时本事件不触发，而且触发不了**：`ConfigChanged` 是门面上的**静态字段式事件**，C# 只允许
> **声明它的类型**触发它——第三方 `IConfigManager` 实现没有任何办法让它响（原文档写的「注入的自定义实现
> 需要自行派发」在 C# 里做不到）。门面只在实现是框架自带的 `ConfigManagerImpl` 时才接上它。需要变更通知的
> 自定义实现请自建事件源。

### 卸载

| 方法            | 说明                                  |
| --------------- | ------------------------------------- |
| `Unload<T>()`   | 卸载指定类型的配置（Table 或 Global） |
| `IsLoaded<T>()` | 判断指定类型是否已加载                |

### IConfigLoader 接口

```csharp
public interface IConfigLoader
{
    UniTask<ConfigTable<T>> LoadTableAsync<T, TKey>(string assetPath) where T : IConfigRow<TKey>, new();
    UniTask<T> LoadGlobalAsync<T>(string assetPath) where T : class, new();
}
```

- 无状态设计：建议实现为无字段的轻量对象，每次使用 `new` 传入即可
- `assetPath` 由调用方定义语义（文件路径、AssetBundle 地址等），Loader 自行解析
- `LoadTableAsync<T, TKey>` 返回 `ConfigTable<T>`（构造函数接收 `IDictionary`，如 `Dictionary<TKey, T>`），框架从中提取内部字典进行管理；构造时校验字典键/值类型与行类型 `IConfigRow<TKey>` 声明一致，不一致抛 `ConfigException`（早失败，错误信息含真实键类型）

---

## 典型场景

### 三种接入方式

| 方式       | 入口                                        | 适用场景                                                          |
| ---------- | ------------------------------------------- | ----------------------------------------------------------------- |
| **方式一** | `PreloadTableAsync<T>(path, ConfigFormat)`  | 内置格式（Json / ScriptableObject / CSV），框架负责 IO + 反序列化 |
| **方式二** | `PreloadTableAsync<T>(path, IConfigLoader)` | 自定义一文件一表格式（protobuf、MessagePack 等）                  |
| **方式三** | `RegisterTable<T>(ConfigTable<T>)`          | Luban 等整包工具（每表一数据文件）或第三方已在外部完成反序列化   |

方式一和方式二由框架控制异步加载流程，方式三由第三方完全控制反序列化时机。

---

#### 方式一：内置格式（Json / ScriptableObject / CSV）

适用于项目使用标准 Json、ScriptableObject 或 CSV 存储配置，每个文件对应一张表。

```csharp
// 定义行类型
[Serializable]
public struct ItemRow : IConfigRow<int>
{
    public int Id;  // 主键必须是字段：Json / ScriptableObject 不序列化属性
    public string Name;
    public int Price;

    // 字段不能实现接口属性：必须显式实现 IConfigRow<TKey>.Id 去读字段
    int IConfigRow<int>.Id => Id;
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

#### 方式二：自定义 Loader（一文件一表）

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

> **注意：** 此方式不适合 Luban 这类整包入口。Luban 5.x 每张表一个数据文件，但 `Tables` 是一次构造全部表的整包入口，没有「单表 loader」；为每张表单独 Preload 会反复构造整包。请改用方式三（或可选的 Luban 扩展包）。

---

#### 方式三：Register 直接注入（整包多表 / 已反序列化数据）

第三方自行完成 IO 和反序列化，直接将数据注入 `ConfigManager`。框架不参与加载过程，仅管理缓存与提供统一查询接口。

**动手前先对三个前提。** 三条都是**框架侧**的契约，不随外部工具版本变化；对不上会编译不过或行为不符预期：

1. **行类型必须实现 `IConfigRow<TKey>`。** 否则 `ConfigTable<T>` 取不到主键类型，构造/注册期即抛 `ConfigException`（错误信息含真实类型名）。**外部工具生成的行类型默认不实现它**——那一步属于你的工具链配置，框架不代劳。
2. **IO 走框架的异步通道，而生成代码的加载入口往往是同步委托。** 正确顺序是：先用 `AssetManager` / `FileManager` 把字节读全，再让同步委托只从内存里取——不要在同步委托里做 IO。
3. **注入不是 `Preload` 的等价物，也不是零成本**：逐条见「已知限制」。

> **使用 Luban 时可以先看可选扩展包。** `com.venusy609.xframework.luban` 会在导表后生成
> `IConfigRow<TKey>` 行接口实现与强类型适配器，并处理 map / 单行 / 带索引 list / 纯列表四种形状；
> 核心包不依赖它。不使用扩展包时，按下面的方式三手写。

> **`Register` 与 `Preload` 的约束不同。** `Preload*Async` 要求 `new()`（`where T : IConfigRow, new()`），`RegisterTable` **不要求**——所以只有带参构造的行类型**可以注册，但不能 Preload**。

**典型场景：Luban（每表一数据文件、整包 `Tables`）**

```csharp
// 需要 using System.Linq;、using System.Collections.Generic;、using XFramework.XConfig;

// ① Luban 侧：先把全部数据文件读进内存，再让生成的整包入口按表名从内存取字节。
//    具体构造形态随 Luban 版本与代码目标（cs-bin / cs-simple-json / …）变化，以 Luban 文档为准。
var tables = /* 由 Luban 生成的整包入口；loader 只从内存字典取字节 */;

// ② 框架侧：逐表投影 + 注册。这一段与 Luban 版本无关，照抄即可。
static void Register<T, TKey>(IEnumerable<T> rows) where T : IConfigRow<TKey>
    => ConfigManager.RegisterTable(new ConfigTable<T>(rows.ToDictionary(r => r.Id)));

// ③ 取出某张表的行集合——「怎么取」是唯一随 Luban 版本变化的地方
IEnumerable<ItemRow> itemRows = /* 从 tables 里取 Item 表的所有行 */;
Register<ItemRow, int>(itemRows);

// ④ 查询（与其它方式完全一致）
var items = ConfigManager.GetTable<ItemRow>();
var row = items.Get(1001);
```

> **可以省掉一次投影**：若你的生成结果已经把行按主键存成了 `Dictionary<TKey, T>`（不少工具的表对象内部就是它，只是对外暴露成只读视图），可以直接把它转成 `IDictionary` 传给 `ConfigTable<T>` 的构造。前提是**运行期那个对象确实是 `Dictionary<TKey, T>`（含派生类）**——`ConfigTable` 构造时沿基类链校验，不满足即抛 `ConfigException`（早失败），不会静默出错。

**Global 配置注册：**

```csharp
var globalCfg = new GameGlobalCfg { MaxLevel = 100 };
ConfigManager.RegisterGlobal(globalCfg);

// 查询
var cfg = ConfigManager.GetGlobal<GameGlobalCfg>();
```

**反射批注册（非泛型重载）：**

`RegisterTable(Type, IConfigTable)` 是为「运行时按类型遍历」准备的入口，但它同样要求行类型实现
`IConfigRow<TKey>`。Luban 5.x 的表有四种形状：map（`DataMap`）、单行（`Data`）、带索引 list
（私有字典 + `DataList`）、纯列表（只有 `DataList`，无主键）。按 `Tb*` 前缀反射时必须逐形状处理；
纯列表表没有主键，接不进来是应当的。

推荐直接用可选的 Luban 扩展包（生成式适配器已处理四种形状），或按上面的方式三手写。

---

### 批量加载

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

### 高级特性

#### 复合主键

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

#### CancellationToken 取消

所有 Preload 与批量加载方法均支持可选 `CancellationToken`：

```csharp
var cts = new CancellationTokenSource();

// 500ms 超时
cts.CancelAfter(TimeSpan.FromMilliseconds(500));
await ConfigManager.PreloadTableAsync<ItemRow>("config/items", cancellationToken: cts.Token);
```

> **取消语义**：取消仅中断调用方等待（`await` 抛出 `OperationCanceledException`）；底层加载仍会完成并注册数据。取消后若需要该配置，直接通过 `GetTable<T>()` 等查询即可，无需重新加载。

#### struct vs class 行类型

|         | struct                            | class                              |
| ------- | --------------------------------- | ---------------------------------- |
| GC 分配 | 零 GC                             | 每次装箱/缓存产生 GC               |
| 可变性  | 数据用字段承载（Json / ScriptableObject 只序列化字段），字段直接可改 | 引用类型，注意不要意外修改缓存数据 |
| 推荐    | ✅ 优先使用 struct                 | 仅在需要继承或多态时使用 class     |

> 框架对 `IConfigRow` 不约束 struct 或 class，两者均可实现。

---

## 自定义实现

本模块的实现方入口：整体替换管理器、传入自定义 Loader，或直接注册表。

- **实现哪个契约**：`IConfigManager`（整体替换，经 `SetInstance`）；`IConfigLoader`（逐次传入的自定义加载器）；表 / Global 也可经 `RegisterTable` / `RegisterGlobal` 直接注入已反序列化数据。
- **怎么接线**：`ConfigManager.SetInstance(manager)`；自定义 Loader 在 `PreloadTableAsync` / `PreloadGlobalAsync` 的调用点传入；批量注册见「典型场景」的「三种接入方式」方式三。
- **要守什么**：`IConfigLoader` 必须无状态；行类型必须实现 `IConfigRow<TKey>`，否则 `ConfigTable<T>` 构造期抛 `ConfigException`（见「API 参考」）；注入自定义 `IConfigManager` 后 `ConfigChanged` 不会触发且无法触发，诊断回读一律空/0（见「API 参考」的「变更事件」）。

### 自定义实现注入

通过 `SetInstance` 注入自定义 `IConfigManager` 实现，适用于测试、依赖注入或完全替换内部逻辑：

```csharp
var customManager = new MyCustomConfigManager();
ConfigManager.SetInstance(customManager);

// 之后所有 ConfigManager 静态调用委托到 customManager
```

> 注入的实例生命周期由调用方管理，`Destroy()` 只会清除静态引用，不会销毁注入的实例。

## 支持的数据格式

```csharp
public enum ConfigFormat
{
    Json = 0,             // JSON，通过 JsonUtility 反序列化
    ScriptableObject = 1, // ScriptableObject，通过 AssetManager（YooAsset）加载
    Csv = 2,              // CSV，第一行为表头，后续行为数据行
}
```

### 文件形状与前置条件

- **Json（Table）**：对象包一个 `Items` 数组——`{"Items":[{"Id":1001,"Name":"..."}]}`（加载器用 `ListWrapper<T>` 包装；`JsonUtility` 不支持顶层裸数组）。Global 直接是一个 JSON 对象。
- **ScriptableObject（Table）**：**资产必须继承公开基类 `ScriptableObjectTable<TRow, TKey>`**——普通 `ScriptableObject` 会得到明确的 `ConfigException`（「must inherit ScriptableObjectTable…」），而不是「AssetManager 未初始化 / 资源不存在」的误归因。最小形状：

  ```csharp
  [Serializable]
  public struct SettingsRow : IConfigRow<int>
  {
      public int Id;      // 字段承载 Unity 序列化
      public int Volume;
      int IConfigRow<int>.Id => Id;   // 字段不能实现接口属性
  }

  // Table 资产：继承公开基类，行列表由基类的 Items 承载
  [CreateAssetMenu(menuName = "Config/SettingsTable")]
  public sealed class SettingsTable : ScriptableObjectTable<SettingsRow, int> { }
  ```

  在 Project 窗口用 **Create → Config → SettingsTable** 创建资产，把行填进 Inspector 的 `Items`，再用
  `PreloadTableAsync<SettingsRow>("config/settings", ConfigFormat.ScriptableObject)` 加载。
- **ScriptableObject（Global）**：直接是目标类型的 SO。
- **CSV**：第一行表头、后续数据行；Global 只取最后一行。
- **前置**：三种格式都经 `AssetManager` 读取（SO 读资源、Json / CSV 读 `TextAsset`），**必须先 `AssetManager.InitializeAsync`**；未初始化时加载抛 `ConfigException`（CONF-003）。

## 线程契约

- **所有 `ConfigManager` API 必须在主线程调用**，模块内未做任何线程同步：门面的 `_instance` /
  `_instanceInitialized` 与 `ConfigManagerImpl` 的五个字典都是裸字段；`Loaders` / `ConfigLoadHelper` /
  `ConfigTypeHelper` / `CsvLoader` 的缓存都是**无锁静态字典**。`ConfigManagerImpl` 里那句「单线程模型下，
  进入『进行中』分支时创建者任务必未完成，广播不会漏掉」是**假设**，不是断言。
- **加载的 `await` 之后落在主线程**：`AssetManager.LoadAsync` 走 UniTask 的 `IEnumerator` 路径
  （`PlayerLoopTiming.Update`），反序列化段随之也在主线程。这条**依赖底层 awaitable 的调度行为**——
  模块内没有任何代码保证它；换成在后台线程 pump 的 awaitable，写字典与派发事件就会落在非主线程。

## 已知限制

知道边界比以为没有边界安全。以下都是刻意的取舍或跨模块的能力缺口，**不是待修的缺陷**：

- **Json / ScriptableObject 行类型只能用字段**：这两条内置路径都走 Unity 序列化，属性（`{ get; set; }`）
  **不会被写入**——主键写成自动属性会恒为 `default`：多行 JSON 直接撞 `Duplicate Id '0'` 加载失败，
  单行则静默产出键 0 的表（`.Get(1001)` 报 not found），SO 路径的 Inspector 里也无处填写。
  CSV 走反射写成员，属性可用，因此这个坑只在 Json / ScriptableObject 路径显形；
  `Tests/Editor/Config/JsonRowSerializationTests` 钉住了该事实。
  **且必须显式实现 `IConfigRow<TKey>.Id`**：C# 字段不能实现接口属性，只写 `public int Id;` 会得到
  `CS0535: 'ItemRow' does not implement interface member 'IConfigRow<int>.Id'`。正确形状是
  「字段承载序列化 + `int IConfigRow<int>.Id => Id;` 兑现接口」（三键/复合键同理，见 `IConfigRow` 示例）。
- **重复注册会静默覆盖**：`RegisterTable` / `RegisterGlobal` 对同一类型再注册直接替换，且**已发出去的
  旧 `ConfigTable<T>` 引用与新 `GetTable<T>()` 不是同一实例**（包装器缓存被丢弃）。
- **`Unload<T>()` 在途时可能不生效**：在途加载完成后仍会注册数据（上节已写明）；更隐蔽的一半是——
  在途未完成时再次 `Preload`，会**加入那个旧任务把它「复活」**。
- **`Destroy()` 不取消在途加载**：丢的只是门面持有的引用，在途任务完成后写入的是一个**门面已不再持有**
  的实例（事件也已解绑，`ConfigChanged` 不再转发）。
- **`ConfigChanged` 的触发面**：加载成功、注册、卸载触发；**重复 Preload 的早退分支、加入在途任务、
  清单里「已加载则跳过」都不触发**；而 `Unload<T>()` 对**从未加载**的类型也会触发一次。
- **订阅者异常被隔离，但会饿死后面的订阅者**：一次派发是**整条多播**一个 `try/catch`（取舍同
  `Pipeline.DispatchSafely` 与 Lock 的派发——逐个隔离每次派发要分配一个委托数组）；同一多播里排在抛异常者
  **之后**的订阅者不会被调用。
- **CSV 的方言边界**：不支持引号与转义（`Split(',')`）——含逗号的字段会被**静默切错列**；空单元格赋类型
  默认值；Global 路径**只取最后一行**，前面的行静默忽略。
- **`BuildIndex` 只按 `indexName` 缓存**：同名换 `TIndex` 会抛 `InvalidCastException`（不是
  `ConfigException`）、换 selector 会**静默返回旧索引**。
- **`GetAll()` 返回内部数组引用**（零 GC 的代价），请勿修改元素；`IConfigTable.Data` 以非泛型
  `IDictionary` 暴露内部字典——那是「零反射注入路径」的代价。**两者都按只读对待**：`Data` 返回的是
  可变引用，修改它会使 `Count` / `GetAll` / `BuildIndex`（构造期快照）与 `Get` / `TryGet` / `Contains`
  （实时字典）互相脱钩。
- **空表与键类型不匹配的处置不对称**：`JsonLoader` 允许空数组（注册一张空表），
  `ScriptableObjectLoader` 会抛；`ConfigTable` 的键类型不匹配在 `Get` 上抛、在 `TryGet` / `Contains`
  上只告警并返回 `false`。
- **经 `RegisterTable` / `RegisterGlobal` 注入的表没有资源路径，也不参与 `Preload` 的语义**：注入只写
  数据与包装器——不写 `_assetPaths`（`CopyLoaded` 里这类条目的 `AssetPath` 恒为 null）、不进
  `_inFlightLoads`。两个可观察后果：① 同一个类型**先注入再 `Preload`** 时，`Preload` 命中「已加载」直接
  返回注入的数据、**不会加载 assetPath**（也没有路径 Warning）；**先 `Preload` 再注入**时 `Register`
  会替换已加载的数据（后到者赢）。② 并发的 `Preload` 与注册之间没有去重，各自完成、各自写入。
- **经 ConfigManager 走一遍会留下多份索引视图**：外部工具的表对象自己有一份按主键的索引，你为
  `ConfigTable<T>` 建的 `Dictionary<TKey, T>` 是第二份，`ConfigTable` 构造时还会拷出一份行数组
  （`GetAll()` 返回的就是它）。这是「让 ConfigManager 成为统一查询面」的代价——**只想省掉这份开销、
  不需要统一查询面的项目，直接用外部工具自己的表对象即可**。
