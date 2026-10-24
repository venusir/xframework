# Luban 配置示例

最小可运行示例：Luban schema + 生成代码 + 生成式接缝/适配器 + 运行时经 `AssetManager` 加载，
并把 map 表与单行表注册进 `ConfigManager`，同时保留纯列表表的强类型 `DataList` 访问。

## 目录

| 路径 | 内容 |
|---|---|
| `LubanProject/` | Luban 工程：`luban.conf`、`Defines/`、`Datas/` 源数据、`gen.ps1` / `gen.sh` |
| `Generated/Code/` | Luban 生成的 C# 代码（`cs-simple-json`） |
| `Generated/Glue/` | 本扩展生成的 `IConfigRow` 接缝与强类型适配器 |
| `Data/` | Luban 生成的数据（`json`） |
| `Scripts/LubanSample.cs` | 示例入口 |

## 快速开始

安装顺序：核心包 → `com.code-philosophy.luban` → 本扩展包（见包 README）。

1. 在 Package Manager 的 Samples 页签导入本示例。
2. 把导入后的 `Data/` 目录加入 YooAsset collector，使资源 location 与
   `configs/<表名>.json` 一致（`demo_tbitem.json`、`demo_tbglobal.json`、`demo_tbspawnpoint.json`）。
   如果 collector 的地址规则不同，修改 `LubanSample` 组件上的 `_dataRoot`，
   或在代码里给 `LubanOptions.LocationResolver` 提供自己的映射。
3. 新建一个空场景，创建 GameObject 并挂上 `LubanSample`。
4. 进入 Play Mode。示例用 `AssetPlayMode.EditorSimulate` 初始化 AssetManager，
   因此需要项目的 `AssetBundleCollectorSetting`；Player 构建不适用。
5. Console 应出现：

   ```
   [LubanSample] item=Sword price=100, version=1.0.0, spawnPoints=2
   ```

## 它演示了什么

- `AssetManager` 异步加载全部数据文件；
- `LubanSampleConfigAdapter` 构造 `cfg.Tables`，并逐表注册：
  - `TbItem`（map）→ `ConfigManager.GetTable<cfg.demo.Item>()`
  - `TbGlobal`（单行）→ `ConfigManager.GetGlobal<cfg.demo.Global>()`
  - `TbSpawnPoint`（纯列表）→ 无主键，不注册；仍可经 `LubanConfig.GetTables<cfg.Tables>()` 访问 `DataList`
- `LubanConfig.Destroy()` 卸载本扩展注册的表。

## 重新生成

正常流程用 Editor 菜单：

1. `XFramework/Luban/设置...`，把 Luban.dll、`LubanProject/luban.conf`、
   代码目录 `Generated/Code`、数据目录 `Data`、胶水目录 `Generated/Glue` 填好；
2. `XFramework/Luban/生成配置表`。

手动回退：`LubanProject/gen.ps1 -LubanDll <path>` 或 `LUBAN_DLL=<path> LubanProject/gen.sh`。

> 示例的生成物是签入包内的；在本仓库里直接对 `Samples~/` 运行 Editor 工具会被路径守卫拒绝
> （`Samples~` 属于包目录）。导入到你的项目后（通常在 `Assets/Samples/` 下）再生成即可。

## 已知限制

- 示例使用 EditorSimulate，仅编辑器可运行；
- 需要项目已配置 YooAsset collector；
- 纯列表表无主键，不进入 `ConfigManager`；
- 示例不包含场景文件，需要手动挂 `LubanSample`。
