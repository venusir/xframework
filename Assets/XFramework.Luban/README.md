# XFramework Luban

XFramework 的**可选** Luban 配置扩展：Editor 一键导表、生成式行接口实现与强类型适配器、运行时经
`AssetManager` 加载并把表注册进 `ConfigManager`。

> **行接口实现**：为行类型生成的 `partial` 声明，使其实现 `IConfigRow<TKey>`。
> **接入代码**：行接口实现 + 强类型适配器 + 清单文件的总称。

- **核心包零改动、零 Luban 依赖**：本包是独立 UPM 包，不用 Luban 的项目不需要安装它。
- **运行时不引用 Luban 程序集**：Luban 类型只出现在使用方项目（Luban 生成代码与本包生成的适配器）中。
- **编译期守门**：行接口实现缺失时生成的适配器直接编译不过，而不是等到运行时才静默缺表。

## 快速使用

### 1. 安装顺序

1. 核心包 `com.venusy609.xframework`：

   ```
   https://github.com/venusir/xframework.git?path=Assets/XFramework#<tag>
   ```

2. Luban 官方 Unity 运行时 `com.code-philosophy.luban`（Git URL 钉修订）：

   ```
   https://github.com/focus-creative-games/luban_unity.git#v1.2.0
   ```

3. 本扩展包：

   ```
   https://github.com/venusir/xframework.git?path=Assets/XFramework.Luban#<tag>
   ```

> UPM 的 `package.json` 不支持 Git URL 依赖，所以本包的 `dependencies` 为空；三条依赖都需要手动加入
> `Packages/manifest.json`。安装顺序很重要：先核心包，再 Luban 运行时，最后本包。

### 2. 配置项目设置

打开 **Project Settings → XFramework Luban**，填写：

| 字段 | 说明 |
|---|---|
| dotnet 路径 | 默认 `dotnet`（从 PATH 查找） |
| Luban.dll 路径 | 项目相对或绝对 |
| luban.conf 路径 | 项目相对或绝对 |
| Target 名 | 通常 `client` 或 `all` |
| Tables 类名 | 默认 `Tables` |
| 代码 / 数据输出目录 | 必须在 `Assets/` 下 |
| 接入代码目录 | 必须在 `Assets/` 下，且不能位于 Luban 会清理的两个输出目录内 |
| 代码目标 / 数据目标 | `cs-bin + bin` 或 `cs-simple-json + json`，必须配套 |
| 适配器命名空间 / 类名 | 生成代码的落点 |
| 生成行接口实现（IConfigRow） | 默认开启；关闭时由你手写 |
| 附加参数 | 原样追加到 Luban 命令行 |

### 3. 生成

菜单 **XFramework/Luban/生成配置表**：

1. 调用 `dotnet Luban.dll` 生成代码与数据；
2. 解析生成的代码；
3. 生成行接口实现、强类型适配器与 `.luban-integration-manifest`。

也可以分开执行：

- **仅重新生成接入代码**：不跑 Luban，只解析已有生成代码并重写行接口实现/适配器；
- **校验接入**：只解析并报告表形状、asmdef 与引用问题，不写盘。

### 4. 启动时加载

一行接入启动流程：

```csharp
using XFramework.XBootstrap;
using XFramework.XConfig.Luban;
using YourGame.Generated; // 生成的适配器命名空间

Bootstrap.Register(new LubanBootstrapStage(new GeneratedLubanConfigAdapter()));
```

或直接初始化：

```csharp
await LubanConfig.InitializeAsync(
    new GeneratedLubanConfigAdapter(),
    new LubanOptions { DataRoot = "configs" });
```

随后即可查询：

```csharp
var item = ConfigManager.Get<cfg.demo.Item, int>(1001);
var table = ConfigManager.GetTable<cfg.demo.Item>();
var global = ConfigManager.GetGlobal<cfg.demo.Global>();

// 同时保留 Luban 原生强类型访问
var tables = LubanConfig.GetTables<cfg.Tables>();
var spawnPoints = tables.TbSpawnPoint.DataList;
```

## 模块结构

```text
Assets/XFramework.Luban/
├── Runtime/
│   ├── ILubanConfigAdapter.cs       生成适配器契约
│   ├── LubanOptions.cs              数据根路径 / 加载方式 / location 解析
│   ├── LubanDataLoadMode.cs         TextAsset / RawFile
│   ├── LubanInitReport.cs           进度载荷
│   ├── LubanConfigException.cs      加载/构造/注册异常
│   ├── LubanConfig.cs               静态门面
│   ├── LubanConfigBridge.cs         注册桥接（零拷贝/复制、索引重建、Global、Unload）
│   ├── LubanBootstrapStage.cs       引导阶段（Phase 默认 90，可覆盖）
│   ├── LubanConfigImpl.cs           internal：加载 → 构造 → 注册
│   ├── ILubanDataProvider.cs        internal：数据后端契约
│   └── AssetLubanDataProvider.cs    internal：AssetManager 后端
├── Editor/
│   ├── LubanGeneratorSettings.cs    ProjectSettings 设置
│   ├── LubanSettingsProvider.cs     Project Settings 页面
│   ├── LubanCodeTarget.cs           代码/数据目标枚举
│   ├── LubanSettingsValidator.cs    结构校验
│   ├── LubanCommandBuilder.cs       命令行构建（纯函数）
│   ├── LubanProcessRunner.cs        子进程执行（超时/捕获输出）
│   ├── LubanCodeParser.cs           解析 Luban 生成代码
│   ├── LubanGeneratedModel.cs       解析模型
│   ├── LubanIntegrationEmitter.cs   行接口实现/适配器文本
│   ├── LubanIntegrationWriter.cs    写盘、幂等、stale 清理、覆盖保护
│   ├── LubanProjectValidator.cs     asmdef 同域/引用/行类型唯一性
│   ├── LubanIntegrationGenerator.cs 解析 → 校验 → 生成
│   └── LubanMenu.cs                 菜单与流程编排
├── Tests/
│   ├── Runtime/                     门面、桥接、端到端（真实 5.1.0 生成物）
│   └── Editor/                      解析器、发射器、写盘、asmdef 校验、示例防腐
└── Samples~/Luban/                  最小可运行示例
```

## 行为契约

### 表形状与注册

| Luban 形状 | 生成代码的公开面 | 本包的处理 |
|---|---|---|
| map（默认） | `DataMap` + `DataList` | `ConfigManager.RegisterTable` |
| 单行（`mode="one"`） | `Data` | `ConfigManager.RegisterGlobal`（要求 class） |
| 带索引 list（`mode="list"` + `index`） | 私有字典 + `DataList` + `Get`/`GetById` | 从 `DataList` + 键表达式重建字典后注册 |
| 纯列表（`mode="list"` 无 index） | 只有 `DataList` | 无主键，**跳过**并在适配器中留注释 |

- 多索引（`index="a,b,c"`，多个独立索引）无法表达为单一 `IConfigRow<TKey>`，生成器会报错；请改用联合索引
  （`index="a+b"`）或手写适配。
- 同一行类型被两张表复用会报错：`ConfigManager` 按行类型索引，无法区分。
- 单行表行类型是 struct 会报错：`RegisterGlobal<T>` 要求 `T : class`。

### 运行时顺序

1. `adapter.TableNames` + `DataFileExtension` + `LubanOptions.LocationResolver` 得到全部 location；
2. 经 `AssetManager` 加载（`TextAsset` 或 `RawFile`）；
3. 释放 `AssetHandle`；
4. 主线程同步调用 `adapter.CreateTables(loadBytes)` 构造整包 `Tables`；
5. 若 `ConfigManager` 未初始化则自动 `ConfigManager.Initialize()`（**永不** `Destroy` 它）；
6. `adapter.RegisterToConfigManager(tables)`；
7. 门面持有 `object Tables`，`IsInitialized = true`。

- 重复 `InitializeAsync` 告警并忽略。
- 并发 `InitializeAsync` 共享同一次初始化：首个调用者的 `options` / `progress` / `cancellationToken` 生效；
  `Destroy()` 或会话复位会以 `OperationCanceledException` 作废在途初始化，且不会注册任何表。
- 注册阶段 `ConfigManager.ConfigChanged` **逐表触发**；事件发生时 `LubanConfig.IsInitialized` 仍为 `false`，
  在事件里查 `LubanConfig.Tables` 会抛 `InvalidOperationException`。需要整包完成请 await `InitializeAsync`
  或检查 `IsInitialized`。
- `LubanConfigBridge.Unload<T>()` 即使 `T` 未加载也会触发 `ConfigChanged`（核心 `ConfigManager` 的既有语义）。
- 主线程契约在 Editor 下由入口断言（首错记一次 Error）；Release 构建无运行时开销。
- 注册失败会尽力 `UnregisterFromConfigManager()` 回滚，再抛 `LubanConfigException`。
- `Destroy()` 在 `ConfigManager` 仍初始化时卸载本扩展注册的表；若 `ConfigManager` 已被销毁则跳过并告警。
- 取消只中断本次等待；已完成的加载/注册不回滚。
- 所有公开 API 必须在主线程调用。

### 错误

- 加载失败：`LubanConfigException`，消息带 `[LubanConfig]` 前缀、表名与 location；
- 初始化前访问 `Tables` / `GetTables<T>`：`InvalidOperationException`，带修复提示；
- 类型不符：`InvalidOperationException`；
- 适配器契约非法（扩展名/表名列表/位置解析）：`LubanConfigException`。

## 架构设计

### 为什么是「生成式适配器」而不是运行时反射

旧的内建 Luban 适配层（2026-05 删除）在运行时反射 `Tb*` 属性、硬绑 `ByteBuf` 类型名与
`Dictionary<int,row>` 主键，Luban 升级即静默失配。本包改为：

- Editor 在导出后解析生成代码，产出 `IConfigRow<TKey>` 行接口实现与强类型适配器；
- 适配器在**使用方程序集**里编译，`ConfigTable<T>` 的泛型约束在编译期验证行接口实现；
- 运行时零反射、零 Luban 类型依赖；生成物随 Luban 版本显式重生成。

### 为什么扩展包不引用 `Luban.Runtime`

适配器由使用方项目生成，Luban 的 `ByteBuf` / `SimpleJSON` 只在生成代码中出现。包本身只认
`Func<string, byte[]>` 与 `IReadOnlyDictionary<TKey,T>`，因此：

- 扩展包可以独立编译；
- Luban 运行时版本只影响生成代码与使用方程序集；
- 测试不需要把 Luban 类型带进包运行时。

### 数据加载

- 默认 location：`<DataRoot>/<表名><扩展名>`；
- collector 地址不同时用 `LubanOptions.LocationResolver` 覆盖；
- `.bytes` / `.json` 默认按 `TextAsset` 加载；collector 标记为 RawFile 时把 `LoadMode` 改为 `RawFile`。

### 接入代码写盘

- 文件带 `// <auto-generated by XFramework Luban...>` 头；
- 内容未变不写；已存在但无该头的文件拒绝覆盖；
- `.luban-integration-manifest` 记录生成文件与表清单，用于清理旧文件；
- 行接口实现与生成代码必须处于**同一个 asmdef 作用域**——partial 类型不能跨程序集合并。生成前会校验；
  若使用方 asmdef（非 Assembly-CSharp），它还必须在 `references` 里直接包含
  `Venusy609.Xframework`、`Venusy609.Xframework.Luban`、`Luban.Runtime`。

## API 参考

### `ILubanConfigAdapter`

生成适配器实现的契约：`DataFileExtension`、`TableNames`、`CreateTables(Func<string, byte[]>)`、
`RegisterToConfigManager(object)`、`UnregisterFromConfigManager()`。手写适配器也可以实现它。

### `LubanOptions`

`DataRoot`（默认 `"configs"`）、`LoadMode`（默认 `TextAsset`）、`LocationResolver`（可选）。

### `LubanConfig`

- `InitializeAsync(adapter, options = null, progress = null, cancellationToken = default)`
- `IsInitialized` / `Tables` / `GetTables<T>()` / `TryGetTables<T>(out T)`
- `Destroy()`

### `LubanBootstrapStage`

`new LubanBootstrapStage(adapter, options = null, phase = BootstrapPhases.UserStart)`，
`Shutdown()` 调 `LubanConfig.Destroy()`。

### `LubanConfigBridge`

- `RegisterTable<T,TKey>(IReadOnlyDictionary<TKey,T>)`：`Dictionary<TKey,T>` 零拷贝，其余复制；
- `RegisterTable<T,TKey>(IReadOnlyList<T>, Func<T,TKey>)`：带索引 list 重建字典；
- `RegisterGlobal<T>(T)`；`Unload<T>()`。

## 典型场景

**用 Bootstrap 阶段（推荐）**

```csharp
Bootstrap.Register(new AssetBootstrapStage(...));
Bootstrap.Register(new LubanBootstrapStage(new GeneratedLubanConfigAdapter()));
await Bootstrap.RunAsync();
```

**自定义 collector 地址**

```csharp
await LubanConfig.InitializeAsync(adapter, new LubanOptions
{
    LocationResolver = table => "configs/" + table + ".json",
});
```

**RawFile collector**

```csharp
await LubanConfig.InitializeAsync(adapter, new LubanOptions
{
    LoadMode = LubanDataLoadMode.RawFile,
});
```

## 自定义实现

- 不想自动生成行接口实现：设置里关闭「生成行接口实现（IConfigRow）」，自己写 `partial` 行接口实现，再执行「仅重新生成接入代码」；
- 不想用生成适配器：手写实现 `ILubanConfigAdapter`，或在任意地方用 `LubanConfigBridge` 手动注册；
- 使用自己的数据后端：包内 `ILubanDataProvider` 是 internal（当前没有需要公开替换的真实后端）；
  如需非 YooAsset 的加载方式，可先自行读取数据构造 `Tables`，再用 `LubanConfigBridge` 注册。

## 生命周期与清理

- `LubanConfig.InitializeAsync` 只会创建/持有门面状态；
- `Destroy()` 卸载本扩展注册的表并清空 Tables；
- 关闭域重载时，`SubsystemRegistration` 档的会话复位只丢引用（`ConfigManager` 自己也会复位）；
- `ConfigManager` 的生命周期始终归使用方，本包只会在未初始化时调用一次 `Initialize`，永不 `Destroy`。

## 线程契约

- 所有公开 API 主线程调用；
- `AssetManager` 加载是异步的，但 `Tables` 构造是同步 CPU 工作，在主线程执行；
- 大表构造会造成可感知的启动停顿，v1 不提供线程池解析。

## 性能代价

- `DataMap` 运行期是 `Dictionary<TKey,T>` 时零拷贝；`ReadOnlyDictionary` 等会复制一份并记 Info；
- `ConfigTable<T>` 构造还会快照一份 `T[]`（`GetAll()` 的返回值），因此内存约为
  「Luban 原始字典 + 行数组」；零拷贝只省字典这一份；
- 复制路径使用默认相等比较器，自定义 comparer 不保留；
- 所有数据文件同时驻留内存直到 `Tables` 构造完成，大表项目的峰值内存需自行评估。

## 已知限制

- **只支持 Luban 5.1.0 的 `cs-bin` / `cs-simple-json` 输出**；其它 target（`cs-newtonsoft-json`、
  `cs-editor-json` 等）与其它版本需要重新验证生成物形状；
- **纯列表表不进入 `ConfigManager`**（无主键）；
- **单行表行类型必须是 class**；struct 无法 `RegisterGlobal`；
- **多个独立索引的 list 表不支持**；请用联合索引（`index="a+b"`）；
- **行接口实现与生成代码必须在同一 asmdef**，且该 asmdef 必须直接引用核心包、本包与 `Luban.Runtime`；
- **主线程同步解析**；大表会卡启动帧；
- **不支持热重载 / 多 schema 并行挂载**；一次一个适配器；
- **不要与同类型的 `Preload` 混用**：注册会覆盖同类型的既有数据，并触发 `ConfigChanged`；
- **Editor 工具会调用外部 `dotnet`**，需要 `.NET 8+` 与可用的 Luban.dll；超时 5 分钟；
- **示例使用 EditorSimulate**，需要 YooAsset collector，Player 构建不适用；
- **核心包的六道门禁不覆盖本包**（它们只扫 `Assets/XFramework`）；本包自带 Runtime/Editor 测试，
  文档与 `.meta` 靠自检与评审。

## 设计取舍

- **生成式行接口实现 + 适配器**：把「猜 Luban 私有形状」的风险前移到 Editor 生成期，并用编译错误兜底；
  代价是生成物随 Luban 版本重生成。
- **包运行时不引用 Luban.Runtime**：换取独立编译与零版本耦合；代价是适配器必须由使用方程序集承载。
- **自动初始化 ConfigManager 但不 Destroy**：开箱即用与所有权清晰之间的折中。
- **纯列表表跳过而不是报错**：它没有主键，`ConfigManager` 无法表达；生成期注释与测试锁定。
- **文本解析而不是反射已编译类型**：生成后立即可用，不需要等待域重载；形状失败时给「文件:行号」并拒绝写盘。

## 接口承诺到哪为止

- 本包公开 API（`ILubanConfigAdapter`、`LubanOptions`、`LubanConfig`、`LubanConfigBridge`、
  `LubanBootstrapStage`、`LubanConfigException`）按语义化版本承诺；
- **生成代码的形状、`Tables` 的构造函数、`DataMap`/`Data`/`DataList` 的公开面属于 Luban**，
  本包只承诺「对 5.1.0 已验证」；升级 Luban 需重跑生成器与测试；
- `.luban-integration-manifest` 与生成文件布局是内部实现，不作为对外契约。

## 与相邻模块的关系

- **Config**：把表注册进 `ConfigManager`，查询面由 Config 提供；
- **Asset**：数据经 `AssetManager` 加载；
- **Bootstrap / Pipeline**：`LubanBootstrapStage` 接入启动编排；
- **Log**：使用 `LogCategory.Get("Luban")`，不改核心 `LogCategories`。

## 设计原则

- 可选：核心包不为 Luban 背负任何依赖或成本；
- 编译期优先：能在编译期失败的（行接口实现缺失、引用缺失）不推迟到运行时；
- 响亮失败：解析/注册失败给文件行号与修复提示，不静默跳过（纯列表表除外，见行为契约）；
- 不预设 GamePlay 架构：只提供配置加载与注册，不规定启动之外的任何组织方式。

## 依赖

| 包 | 版本/URL | 说明 |
|---|---|---|
| `com.venusy609.xframework` | 核心包（同 tag） | 必需；提供 Config / Asset / Bootstrap |
| `com.code-philosophy.luban` | `https://github.com/focus-creative-games/luban_unity.git#v1.2.0` | 必需；Luban 官方 Unity 运行时 |
| UniTask | 随核心包依赖安装 | 运行时/Editor 的异步与进程执行 |

本包 `package.json` 的 `dependencies` 为空：UPM 不支持 Git URL 依赖，安装顺序见「快速使用」。
