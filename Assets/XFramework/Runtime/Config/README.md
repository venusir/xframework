# 配置管理（ConfigManager）

`ConfigManager` 提供统一的配置加载与查询接口，支持三种配置格式。

## 快速使用

```csharp
using XFramework.XConfig;

// 1. 加载配置（异步）
await ConfigManager.PreloadTableAsync<ItemRow>("config/items");

// 2. 查询配置
var item = ConfigManager.Get<ItemRow>(1001);
var allItems = ConfigManager.GetAll<ItemRow>();
var hero = ConfigManager.GetFirst<HeroRow>(h => h.Name == "mage");
```

## 支持格式

| 格式                 | 用途                | 示例                                                                    |
| -------------------- | ------------------- | ----------------------------------------------------------------------- |
| **JSON**             | 本地测试 / 快速原型 | `.json` 文件 → `TextAsset` → `JsonUtility.FromJson`                     |
| **ScriptableObject** | Editor 可视化编辑   | `ScriptableObject` 资产，运行时零解析开销                               |
| **Luban**            | 生产级大型配置      | Luban 工具链导出 `.bytes` → 原生反序列化，支持嵌套 bean、多态等高级特性 |

## 自定义格式

实现 `IConfigLoader` 接口并通过 `ConfigManager.SetLoader()` 注入即可。

---

## Luban 集成（可选）

Luban 集成由独立程序集 `Venusy609.Xframework.Luban` 提供。**不使用 Luban 的项目直接删除 `Config/Luban/` 目录即可，零副作用。**

### 安装 Luban

```bash
dotnet tool install -g Luban.Tool
```

或在项目 `NuGet.Config` 中配置 Bright.Serialization 包源。

### 使用步骤

1. **配置 Luban 项目**（`.conf` / `.xml`），参考 [Luban 官方文档](https://luban.doc.code-philosophy.com/)
2. **使用 XFramework 提供的模板**生成代码（模板位于 `Editor/LubanTemplates/`）
   - `LubanRowTemplate.cs.txt` — Row 类型模板（实现 `IConfigRow`）
   - `LubanTablesTemplate.cs.txt` — Tables 入口类型模板
3. **生成配置二进制**：`luban --conf luban.conf`
4. **将 `.bytes` 文件放入 YooAsset 资源包**
5. **一行代码加载**：

```csharp
using XFramework.XConfig;

// 加载 Luban 生成的完整 Tables
await ConfigManager.LoadAsync<GameTables>("config/tables");

// 加载后，所有 Row 类型自动注册，与 JSON/SO 使用方式完全一致
var item = ConfigManager.Get<ItemRow>(1001);
```

### 程序集隔离

LubanLoader 位于独立程序集 `Venusy609.Xframework.Luban.asmdef`，仅引用 `Venusy609.Xframework`、`UniTask` 和 `YooAsset`。主框架通过反射动态发现 LubanLoader——如果程序集不存在，ConfigManager 自动跳过，不报任何异常。