# Config 配置系统

## 概述

`XConfig` 提供统一的配置加载与查询接口。内置 JSON / ScriptableObject 格式支持，并可通过 `RegisterTable<T>` / `RegisterGlobal<T>` 注入其他格式（Luban、protobuf 等）的配置数据，保持查询 API 一致。

## 基本使用

```csharp
// 1. 初始化
ConfigManager.Initialize();

// 2. 加载 JSON 表
await ConfigManager.PreloadTableAsync<ItemRow>("config/items");

// 3. 查询
var item = ConfigManager.Get<ItemRow>(1001);
Debug.Log(item.Name);
```

## 核心接口

| 方法                                   | 说明                 |
| -------------------------------------- | -------------------- |
| `Initialize()`                         | 初始化配置管理器     |
| `Destroy()`                            | 销毁并释放所有配置   |
| `PreloadTableAsync<T>(path, format?)`  | 预加载 Table 配置    |
| `PreloadGlobalAsync<T>(path, format?)` | 预加载 Global 配置   |
| `Get<T>(id)`                           | 按 Id 查询 Table 行  |
| `TryGet<T>(id, out T)`                 | 安全查询 Table 行    |
| `GetAll<T>()`                          | 获取 Table 所有行    |
| `Contains<T>(id)`                      | 是否存在某 Id        |
| `Count<T>()`                           | Table 行数           |
| `GetGlobal<T>()`                       | 获取 Global 配置     |
| `TryGetGlobal<T>(out T)`               | 安全获取 Global 配置 |
| `RegisterTable<T>(Dictionary<int,T>)`  | 注入 Table 数据      |
| `RegisterGlobal<T>(T config)`          | 注入 Global 数据     |
| `Unload<T>()`                          | 卸载指定配置         |

## 扩展自定义格式

第一步：定义行类型（实现 `IConfigRow`）：

```csharp
public partial class MyRow : IConfigRow
{
    public int Id { get; set; }
    public int Value;
}
```

第二步：自行反序列化后注册：

```csharp
// 例如使用 Luban 的 ByteBuf 反序列化
var tables = new GameTables(new ByteBuf(bytes));

// 遍历 Tb 表注册到 ConfigManager
ConfigManager.RegisterTable(tables.TbItem.DataList.ToDictionary(r => r.Id));
ConfigManager.RegisterTable(tables.TbHero.DataList.ToDictionary(r => r.Id));
```

第三步：使用统一接口查询：

```csharp
var item = ConfigManager.Get<ItemRow>(1001);
var hero = ConfigManager.Get<HeroRow>(1);
```

- 如果仅需注入单个 Global 配置，使用 `ConfigManager.RegisterGlobal(config)`。
- 如果使用静态反射遍历所有 Tb 表，可使用非泛型重载 `RegisterTable(Type, IDictionary)`。