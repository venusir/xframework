# Luban —— 接入配方（维护侧）

> **收件人**：框架维护者（含「下一轮要不要重新论证内建适配层」这个问题的回答者）。**不是**随包发布的使用文档。
> **使用方契约**在 `Assets/XFramework/Runtime/Config/README.md` 的「方式三」——本文件不复述它，只补它**刻意不写**的部分：随 Luban 版本漂移的那些。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。需要时整篇转发给使用方。
>
> **2026-10-24 更新**：独立可选扩展包 `Assets/XFramework.Luban/` 已落地（Editor 导表 + 生成式行接口实现/适配器 +
> 运行时加载/注册）。本文件保留**维护向事实与版本漂移点**；使用方向见扩展包 README。核心包仍不内建适配层。

## 〇、核对状态（先读这一节）

本文件每条事实都带标记，**不要把 `[未实测]` 当成已验证**：

| 标记 | 含义 |
| --- | --- |
| `[一手核对]` | 读**本地那份** Luban 5.1.0 发布包自带的模板与程序集元数据得出，可复现。模板在 `Luban/Templates/<codeTarget>/*.sbn`，命令行选项表在 `Luban.dll` 的 `[Option]` 特性字符串里——两者都不需要执行 Luban |
| `[网上读取]` | 读官方文档页或官方示例仓库的文件得出，**没有本地副本可复现**，可信度低于上一档 |
| `[探针实测]` | 用一个独立的 C# 探针程序跑过（复现 XFramework 侧的接口/反射语义），10/10 通过 |
| `[未实测]` | 只有文档或推理支撑，**没跑通过** |

**核对基准**：Luban **5.1.0**，程序集版本 `5.1.0+df34215d2e22035073d1af83d44e3ac0744dc3b3`，目标框架 `net8.0`，2026-10-10 取自 GitHub Release `v5.1.0` 的 `Luban.7z`（1.6 MB，用 7-Zip 解出 155 个文件；2026-10-24 再取一次，SHA256 `BAC9A1B8D69CFEAA7EF1D8C67B34A007324D1CD158E0B14294521B3879BA5213`）。

> **端到端导表已于 2026-10-24 实测 `[探针实测]`**：在临时工程跑通 `cs-bin + bin` 与 `cs-simple-json + json`；
> 官方 Unity 运行时 `luban_unity` v1.2.0 编译并运行通过。实测形状：
> 数据**每表一文件**（`<module>_<table>` 全小写，`.bytes` / `.json`）；`Tables` 在 **`namespace cfg`**；
> `Tables(Func<string, ByteBuf>)` / `Tables(Func<string, JSONNode>)`；map 表公开 `DataMap`（运行期 `Dictionary<K,V>`）；
> 单行表公开 `Data`；**带索引 list 没有公开 `DataMap`**（私有 `_dataMap_xxx` / `_dataMapUnion` + `Get`/`GetById`，
> 公开面只有 `DataList`）；纯列表表只有 `DataList`。签入的真实生成物在扩展包
> `Tests/Editor/Fixtures~/`（Bin / Json 各一套）。
>
> §三、§四 的**框架侧**结论不依赖版本（探针实测）；§二 的命令行来自选项表 + 官方示例，已实跑。

---

## 一、结论：边界画在哪

**框架侧要的东西只有两件**，且都不随 Luban 版本变：

1. 行类型实现 `IConfigRow<TKey>`（`TKey Id { get; }` 是接口要求的唯一成员）
2. 你把行投影成 `Dictionary<TKey, T>` 再包成 `ConfigTable<T>` 注入

**Luban 侧的一切**——表定义怎么写、命令行怎么调、`Tables` 怎么构造、表对象怎么取行——归使用方的工具链，框架不描述、也不代为适配。理由与沿革见 `Documentation/Modules/Config.md` 的「已评估未采纳」：**内建适配层必须猜 Luban 的私有形状，而该形状既不承诺稳定、也不由本框架控制**。

这条边界的具体收益：本文件**可以被 Luban 版本演化淘汰**（整篇重写即可），而包内那份契约不会。

> **2026-10-24 补充：可选扩展包。** 上述「框架侧不代劳」的边界不变；独立扩展包
> `Assets/XFramework.Luban/` 在**使用方项目**里生成行接口实现与适配器，核心包仍零依赖、零改动。
> 扩展包本身也不引用 Luban 程序集：Luban 类型只出现在使用方的生成代码与生成适配器里。

---

## 二、端到端步骤

### 2.1 拿到 Luban

`[一手核对]` 官方**不以 dotnet tool 分发**（`dotnet tool search luban` 无结果），发布形态是 GitHub Release 上的压缩包。资产名取自 GitHub Releases API（一手）；**用途描述来自官方文档页（二手，未实测）**：

| 资产 | 用途（二手） |
| --- | --- |
| `Luban.7z` | 主生成器（日常导表用这个） |
| `Luban.Agent.7z` | 供 AI / 脚本做校验与 schema 查询 |
| `Luban.Mcp.7z` | IDE MCP Server |

`Luban.7z` 解出的目录形如：`Luban/Luban.dll`、`Luban/Luban.exe`、`Luban/*.dll`、`Luban/Templates/`。目标框架是 **`net8.0`**，所以运行需要 .NET 8+ 运行时。

另一条路是官方示例仓库 `luban_examples`，其 `MiniTemplate/` 可直接当新工程的基准，`Tools/Luban/` 里带一份编译好的工具。

### 2.2 命令行

`[一手核对]` 下面是 5.1.0 的**完整选项表**，从 `Luban.dll` 的 `[Option]` 特性里读出（短名 → 长名 → 帮助文本原文）：

| 短 | 长 | 含义 |
| --- | --- | --- |
| | `--schemaCollector` | schema collector name |
| | `--conf` | luban conf file |
| `-t` | `--target` | **target name**（如 `all` / `client` / `server`） |
| `-c` | `--codeTarget` | **code target name**（如 `cs-bin`） |
| `-d` | `--dataTarget` | **data target name**（如 `bin`） |
| `-p` | `--pipeline` | pipeline name |
| `-f` | `--forceLoadTableDatas` | 无 dataTarget 时也强制加载表数据 |
| `-i` / `-e` | `--includeTag` / `--excludeTag` | 按 tag 裁剪 |
| | `--variant` | field/table variants |
| `-o` | `--outputTable` | output table |
| | `--timeZone` | time zone |
| | `--customTemplateDir` | **custom template dirs**（自定义模板目录，复数——可给多个） |
| | `--strict` | treat validation failure as error |
| | `--locale` | 报错语言（en / zh），默认跟随系统 |
| | `--errorFormat` | `text` \| `json`（json 供 AI / CI 工具消费） |
| `-x` | `--xargs` | args like `-x a=1 -x b=2` |
| `-l` | `--logConfig` | nlog config file（默认 `nlog.xml`） |
| `-w` | `--watchDir` | 监视目录变化并重新生成 |
| `-v` | `--verbose` | verbose |

> **一条已被澄清的漂移**：网上流传「`-t` 是 `--template_search_path`」的说法来自 **3.x 旧文档**。5.1.0 里 `-t` **就是 target**，而 `--template_search_path` 这个字符串在 5.1.0 的全部程序集里**零命中**，自定义模板目录的选项是 `--customTemplateDir`。这正是本文件要逐条标版本的原因。

`[网上读取]` 官方示例 `luban_examples/MiniTemplate/gen.bat` 的形状（**经网页读取，非本地逐字**；参数名与 §2.2 的选项表相互印证）：

```bat
set WORKSPACE=..
set LUBAN_DLL=%WORKSPACE%\Tools\Luban\Luban.dll
set CONF_ROOT=.
dotnet %LUBAN_DLL% ^
    -t all ^
    -d json ^
    --conf %CONF_ROOT%\luban.conf ^
    -x outputDataDir=output
pause
```

把它按你的项目补成两条线即可 `[未实测]`：

```powershell
# bin（二进制，体积小、解析快）
dotnet <LUBAN>\Luban.dll -t all -c cs-bin          -d bin  --conf <CONF_DIR> -x outputCodeDir=<CODE_DIR> -x outputDataDir=<DATA_DIR>

# json（可读、便于排查）
dotnet <LUBAN>\Luban.dll -t all -c cs-simple-json  -d json --conf <CONF_DIR> -x outputCodeDir=<CODE_DIR> -x outputDataDir=<DATA_DIR>
```

> **2026-10-24 已实跑**：把 `-t all` 换成工程里的 target（如 `client`）即可；两种 target 均成功。
> 日常用扩展包的 `XFramework/Luban/生成配置表` 菜单会调用同样的命令并继续生成行接口实现/适配器；
> 手工命令只作为回退。

`[一手核对]` 5.1.0 里 C# 侧的 codeTarget 有：`cs-bin`、`cs-simple-json`、`cs-newtonsoft-json`（另有 `cs-dotnet-json`、`cs-editor-json`、`cs_pb` 的模板目录）。**codeTarget 要与 dataTarget 配套**（选 `cs-bin` 就配 `-d bin`）。

### 2.3 最小工程结构

**不要在别处复写一份 `luban.conf` 与 schema 范例**——那就是第二份会过期的真相。正确做法是逐字复制官方 `luban_examples/MiniTemplate/` 作基准再改。`[网上读取]` 该模板的结构：`luban.conf`、`Defines/`（表结构与枚举定义）、`Datas/`（数据源）、`gen.bat` / `gen.sh`、`output/`。

`[探针实测]` 5.1.0 的最小工程也可以完全用 XML schema（`Defines/*.xml`），不必用 Excel：

- `luban.conf` 的 `schemaFiles` 只留 `{"fileName":"Defines","type":""}`；`dataDir` 指向 `Data`；
- `Defines/builtin.xml` 可以是空 `<module name=""/>`（否则会额外生成 `vec2/3/4`、`AudioType` 等内置类型）；
- 表定义：`<table name="TbItem" value="Item" index="id" input="*@item_tbitem.json"/>`；
  单行 `mode="one"`、纯列表 `mode="list"`、联合索引 `mode="list" index="a+b"`；
- JSON 数据：map/list 的 `input` 必须写成 `*@file.json`（或 `*field@file.json`），单行表用普通对象文件；
  输出数据在 `outputDataDir` 下按 `<module>_<table>.bytes|json` 命名。

框架只对 schema 提**两条要求**，其余随意：

- 要注册成 Table 的表，其 bean 必须有一个主键字段（默认取名为 `id`，生成后成为 C# 侧的 `Id`）——§三 的行接口实现靠它。
- 表模式决定接到哪个 API：有主键的 map 表 → `RegisterTable`；单行表（`Data`）→ `RegisterGlobal<T>`（要求 `T : class`）；纯列表表没有主键，接不进来。

---

## 三、让生成的行类实现 `IConfigRow<TKey>`（行接口实现）

这是整条链路上唯一需要你写代码的地方。

### 3.1 生成物的形状 `[一手核对]`

Luban 5.1.0 的 `Templates/cs-bin/bean.sbn` 逐字：

```scriban
{{~if __bean.is_value_type~}}
public partial struct {{__name}}
{{~else~}}
public {{class_modifier __bean}} partial class {{__name}} : {{if parent_def_type}}{{__bean.parent}}{{else}}Luban.BeanBase{{end}}
{{~end~}}
```

字段声明那一段逐字：

```scriban
    public readonly {{declaring_type_name field.ctype}} {{format_property_name __code_style field.name}};
```

**两条结论**：

1. 行类是 **`partial`** —— 你可以另写一个 partial 部分补接口，不必碰生成代码。
2. 主键是 **`public readonly` 字段**，不是属性 —— 而 `IConfigRow<TKey>` 要的是 `TKey Id { get; }`。**字段不实现接口属性**，且 `Id` 这个名字已被生成的字段占用，再声明同名属性会 **CS0102**。

### 3.2 所以行接口文件只能写成显式接口实现 `[探针实测]`

```csharp
// 你写的行接口文件（每张表一小段）
public partial class Item : XFramework.XConfig.IConfigRow<int>
{
    // 显式接口实现：与生成的 readonly 字段 Id 同名不冲突
    int IConfigRow<int>.Id => Id;
}
```

`[探针实测]` 复现过的事实：

- 这样接完之后 `typeof(Item)` 确实实现 `IConfigRow<int>`，`ConfigTypeHelper` 的接口扫描取得到 `TKey`。
- **框架其实从不读 `Id` 成员**（`ConfigTypeHelper` 只读接口的泛型参数，`ConfigTable` 的字典校验也只比类型）。所以显式实现完全够用，不存在「框架读不到」的问题。
- **但反射代码要小心**：显式实现在**具体类型**上不可见——`typeof(Item).GetProperty("Id")` 返回 **null**；必须从**接口**上取，`typeof(IConfigRow<int>).GetProperty("Id").GetValue(row)` 才能读到值。包内 README 的反射批注册示例正是后者，是对的。
- **复合键（多键表）**：`TKey` 用 `ValueTuple`，且**元组元素名必须与接口声明逐字一致**——写 `IConfigRow<(int skillId, int level)>` 却用 `(int, int) IConfigRow<(int, int)>.Id` 会 **CS0540**（这条是探针第一次编译就撞出来的）。

**2026-10-24：行接口实现可由扩展包生成。** `LubanCodeParser` 解析生成代码，`LubanIntegrationEmitter` 生成 `partial` 行接口实现；
行接口实现与生成代码必须处于**同一个 asmdef**（partial 类型不能跨程序集合并），扩展包会先校验。下面的手写行接口文件仍是回退路径。

**实测的四种表形状**（扩展包按此生成注册代码）：

| 形状 | 公开面 | 注册方式 |
|---|---|---|
| map（默认） | `DataMap` + `DataList` | `RegisterTable(DataMap)`（零拷贝/复制） |
| 单行 `mode="one"` | `Data` | `RegisterGlobal(Data)`（bean 必须是 class） |
| 带索引 list `mode="list" index="..."` | 私有字典 + `DataList` + `Get`/`GetById` | 从 `DataList` + 键表达式重建字典 |
| 纯列表 `mode="list"` 无 index | 只有 `DataList` | 无主键，跳过 |

多索引（`index="a,b,c"`）没有单一主键，扩展包生成期报错；联合索引（`index="a+b"`）的键是 `ValueTuple`，可注册。

**若主键字段不叫 `Id`**（比如列名是 `item_id` → 字段 `ItemId`），行接口文件可以写成普通的隐式实现 `public int Id => ItemId;`，比上面更简单。

> **这套写法有机械守卫**：`Assets/XFramework/Tests/Editor/Config/ExplicitInterfaceRowTests.cs`（复刻生成物的形状——`partial` + `readonly` 字段 + 只有带参构造——并锁住主键从接口泛型实参取、值经接口的 `PropertyInfo` 读得到、键类型不符仍早失败）。该文件做过红证：把 `ConfigTypeHelper.ExtractKeyType` 临时改成「从具体类型取 `Id` 属性」后，6 例中 5 例当场变红，而既有用例全绿。改动本文件里的建议时，那条守卫会先响。

### 3.3 备选：改 Scriban 模板让生成期就带上接口 `[未实测]`

走 `--customTemplateDir`（复数，可给多个）覆盖 `bean.sbn`。**不推荐**，两条理由：

- **模板拿不到所属表的主键类型**。`bean.sbn` 的作用域里只有 `__bean`（其字段是 `export_fields` / `id` 等），拿不到「这张表的 key 是什么类型」。于是 `TKey` 只能在模板里写死成项目约定的类型——**多键表与非 int 主键就覆盖不了**，而这恰恰是行接口文件天然支持的。
- **覆盖整文件会静默丢掉新版本逻辑**。Luban 升级时若改了 `bean.sbn`，你的整文件副本不会报错，只会安静地少生成东西。

真要走这条路，纪律是「在你自己版本的 `bean.sbn` 里找锚点、做最小编辑」（例如在两处类声明后追加 `, global::XFramework.XConfig.IConfigRow<int>`），让它在上游重构时**响亮地失败**而不是静默过期。改模板时加 `--disable_cache` 关闭模板缓存。

---

## 四、注册接入代码

### 4.1 取数据的方式决定成本

`[一手核对]` 5.1.0 的 `Templates/cs-bin/tables.sbn` 逐字给出 `Tables` 的入口：

```csharp
public partial class Tables
{
    public cfg.item.TbItem TbItem {get; }
    public Tables(System.Func<string, ByteBuf> loader) { ... }
}
```

（`cs-simple-json` 同形，只是委托换成 `System.Func<string, JSONNode> loader`。）

**注意这与包内 README 旧的写法不一样**：不是 `new GameTables(new ByteBuf(bytes))`，而是 `new Tables(loader)`——`loader` 按文件名返回该表的缓冲。这正是「包内不写这些」的原因。

`[一手核对]` 表对象的公开面（`cs-bin/table.sbn`，map 模式）：

```csharp
private readonly Dictionary<K, V> _dataMap;
public IReadOnlyDictionary<K, V> DataMap => _dataMap;
public IReadOnlyList<V> DataList => _dataList;
public V GetOrDefault(K key);
public V Get(K key);
public V this[K key];
```

多键表（多键模式的 `table.sbn`）的键是 `ValueTuple`，`Get(k1, k2)`；单行表暴露 `Data`；纯列表表只有 `DataList`（没有主键，接不进来）。

### 4.2 三种写法，推荐第一种

**2026-10-24 起首选扩展包生成的适配器**：它按四种形状逐表调用 `LubanConfigBridge.RegisterTable/RegisterGlobal`，
并生成行接口实现；手写 helper 仍是回退路径。下面的漂移面评价对两者都成立。

| 写法 | 漂移面 | 评价 |
| --- | --- | --- |
| **手写一个小 helper + 逐表调用** | 无 | **推荐**。约 3 行，见下 |
| 覆盖 `tables.sbn` 让生成期产出 `RegisterAll` | 整个 `tables.sbn`（含入口构造与 `ResolveRef` 接线，是 Luban 各版本改动最频繁的部分） | 不划算——为省「每表一行」把最易变的部分纳入你的维护面 |
| 反射批注册（遍历 `Tb*` 属性） | 无，但依赖属性命名约定 | 表特别多时可用；代价是反射，且 §3.2 那条「从接口取 Id」的坑必须处理对 |

```csharp
// 框架侧照抄，与 Luban 版本无关
static void Register<T, TKey>(IEnumerable<T> rows) where T : IConfigRow<TKey>
    => ConfigManager.RegisterTable(new ConfigTable<T>(rows.ToDictionary(r => r.Id)));
```

**可以省掉 `ToDictionary` 这一份拷贝** `[探针实测]`：`tb.DataMap` 声明为 `IReadOnlyDictionary<K,V>`，但运行期对象就是那个私有的 `Dictionary<K,V>`，所以能零拷贝复用。**判据要写成 `dataMap as Dictionary<TKey,T>`（含派生类），不要写成裸 `(IDictionary)dataMap`**——`ReadOnlyDictionary` 也能转成 `IDictionary`，但会被 `ConfigTable<T>` 的字典校验拒绝；扩展包的 `LubanConfigBridge` 就是按这个判据做的（失败才复制并记 Info）。代价是依赖 Luban 的内部实现（哪版把 `_dataMap` 换成别的实现即失效）——失效形态是响亮的（构造期抛 `ConfigException`），不会静默错。

**IO 接线**：`Tables` 的 loader 是**同步**委托，而框架的 `AssetManager` / `FileManager` 全是异步。正确顺序是先把字节读全进内存，再让 loader 只从内存里取。整段解析是同步 CPU 工作——大表放主线程会掉帧；要挪到线程池，记住**解析回到主线程后才能注册**（见包内 README 的「线程契约」）。

### 4.3 生成脚本：核心仓不提供，扩展包提供

**2026-10-24 更新（原结论已部分推翻）**：原结论「不建 `Tools/luban/`、不提供生成脚本」只针对**核心仓**；
独立扩展包 `Assets/XFramework.Luban/` 现在提供 Editor 工具，示例 `Samples~/Luban/LubanProject/` 也带
`gen.ps1` / `gen.sh`。本目录仍不建 `Tools/luban/`。

**核心仓不建脚本的理由（仍然成立）**：

- 行接口实现是普通 C#，没有「模板文件」这种产物要放；
- 生成命令行只有两行，而 `<LUBAN>` / `<CONF_DIR>` / 输出目录**逐项目不同**，写死一份脚本只会腐化；
- 本仓没有 CI，脚本不会被任何东西执行到——留一个「永远没人跑」的脚本，比不写更糟。

将来若要做**漂移探测**（Luban 发新版本时确认 `partial` 还在、`DataMap` 还是 `Dictionary`），正确的形状是**一次性探针**：在仓库外跑一次最小 schema，断言观察结果，跑完即弃——不要留脚本。扩展包的 fixtures 与端到端用例已经是这种探针的常驻形态。

---

## 五、边界

- **不重建内建适配层**。理由、沿革与重开判据见 `Documentation/Modules/Config.md` 的「已评估未采纳」。2026-10-24 起独立扩展包提供适配，但核心包仍不内建。
- **不做 Editor 一键导表（原结论已推翻，2026-10-24）**。原理由是「把 Luban 的安装位置、schema 位置、输出目录固化进框架」；扩展包把全部路径放进使用方项目内的 `ProjectSettings` 设置，理由不再成立。工具在扩展包 `Editor/` 里，核心仓不引入外部进程调用。
- **包内 README 不指向本文件**。规则「包内不指向包外」：第三方装了包却打不开仓库。使用方需要的东西已经在包内 README 里写全了（§一是它的摘要）。

## 六、扩展包工作流（2026-10-24）

- 安装：核心包 → `com.code-philosophy.luban`（`luban_unity` v1.2.0）→ `Assets/XFramework.Luban`；
- 设置：Project Settings → XFramework Luban；
- 生成：`XFramework/Luban/生成配置表`（运行 Luban + 生成行接口实现/适配器），或 `仅重新生成接入代码` / `校验接入`；
- 启动：`Bootstrap.Register(new LubanBootstrapStage(new GeneratedLubanConfigAdapter()))`，或
  `await LubanConfig.InitializeAsync(adapter, new LubanOptions { DataRoot = "configs" })`；
- 生成物：`ILubanConfigAdapter` 实现、`IConfigRow<TKey>` 行接口实现、`.luban-integration-manifest`；
- 运行时：扩展包不引用 Luban 程序集；`Tables` 以 `object` 持有，强类型访问经 `LubanConfig.GetTables<T>()`；
- 四种形状与跳过规则见 §3.2 / §4.2；纯列表表不注册是唯一无法打通的形状；
- 版本事实：5.1.0 + `cs-bin` / `cs-simple-json` 已实测；其它版本/target 需重跑 fixtures 与端到端用例。

---

## 七、审计记录（2026-10-24）

> 本节是 **2026-10-24 的单轮审计点快照**，不是使用说明；长期结论仍按收件人分流到包 README 与决策台账。
> 审计对象：`Assets/XFramework.Luban/`（Runtime 13 个 `.cs`、Editor 14 个、Tests、Samples~）。
> 方法：按 `Documentation/ModuleAudit.md` 的 A–F 判据逐类扫；只读探针 `git ls-files --eol` / `git tag` / `diff -rq` / `.meta` 完整性；Luban 官方与第三方资料对照。
> **本轮没有运行 Unity 测试**：下文所有「改前会红」都是源码阅读推导，实施时必须补红证。状态标记：`[读码]` 源码阅读；`[实测]` 本轮只读命令；`[网读]` 官方/第三方页面；`[未验证]` 无本地证据。

### 7.1 结论摘要

- **无 P0**。没有发现会直接损坏已提交数据或绕过核心承诺的缺陷。
- **P1 6 项、P2 22 项（按主题分组）、P3 若干记录项**；详见 7.2–7.5。
- 设计取向（生成式适配器、运行时零 Luban 依赖、编译期守门、四种表形状）正确，与 Luban 官方「单一 `Tables` 对象 + 按文件名 loader」的推荐一致；问题集中在 **Editor 边界校验、运行时生命周期竞态、测试覆盖与仓库治理**。
- 两条 repo 级实测：
  - `[实测]` 扩展包 43 个生成的 `.cs` / `.json` 工作区行尾为 CRLF（`git ls-files --eol`），是 2026-10-14 行尾归零后的新漂移；`.gitattributes` 也没有把 `*.bytes` 显式标为 binary。
  - `[实测]` 仓库只有 `0.3.0` tag，且该 tag 不包含任何 Luban 扩展提交；包 README 让使用者钉 `<发布 tag>`，目前无 tag 可钉。

### 7.2 P1 发现（6 项）

#### LUB-01 绝对路径被设置校验器拒绝

- **证据** `[读码]`：`LubanSettingsValidator.ValidateProjectPath` 对所有路径一律 `if (Path.IsPathRooted(normalized))` 报「必须是项目相对路径」——`Assets/XFramework.Luban/Editor/LubanSettingsValidator.cs:185-189`。但包 README 的表格写 `Luban.dll 路径 / luban.conf 路径：项目相对或绝对`（`Assets/XFramework.Luban/README.md:42-54`），设置页标签同样写「项目相对或绝对」（`Assets/XFramework.Luban/Editor/LubanSettingsProvider.cs:45-49`），且 `LubanCommandBuilder.ResolvePath` 与 `LubanMenu.ResolveProjectPath` 都显式支持绝对路径。
- **影响**：把 Luban 工具放在项目外（很常见）时，文档允许、UI 允许，点「生成配置表」却报设置错误——功能阻断。
- **建议修法**：`ValidateProjectPath` 增加 `allowAbsolute`（或对 Dll/Conf 单独走一条校验）；绝对路径跳过 `ForbiddenPrefixes` / `requireAssets`，但仍检查与输出目录的重叠。补测试：绝对 Dll/Conf 通过，绝对输出目录仍被拒绝。
- **验证**：新增 `LubanSettingsValidatorTests` 用例；`pwsh -File Tools/run-tests.ps1 -Filter LubanSettingsValidator -Platform EditMode`。

#### LUB-02 受保护目录「精确等于」漏网

- **证据** `[读码]`：`ForbiddenPrefixes` 的条目都带尾斜杠（`"Assets/XFramework/"`、`"Assets/XFramework.Luban/"`，`LubanSettingsValidator.cs:30-39`），检查是 `normalized.StartsWith(prefix)`；而 `Normalize` 会去掉尾斜杠（同文件 `:123-132`）。于是 `CodeOutputDir = "Assets/XFramework"` 或 `IntegrationOutputDir = "Assets/XFramework.Luban"` 这种**精确等于包根**的路径会通过。
- **影响**：前者把生成代码写进核心包；若 Luban 真的清理输出目录（校验器注释如此声称，同文件 `:90-96`），会删除核心包文件。后者把 `Adapter/`、`RowInterfaces/`、manifest 直接写进扩展包根目录。
- **建议修法**：同时比较 `normalized == prefix.TrimEnd('/')` 或 `normalized.StartsWith(prefix)`。补两个精确路径的测试。
- **验证**：EditMode 新用例；**另需一个哨兵探针确认 Luban 是否真的清理输出目录**（在临时工程输出目录放哨兵文件，跑一次生成后看是否还在）。在确认前按「会清理」防御。

#### LUB-03 并发初始化与 Destroy 在途竞态

- **证据** `[读码]`：`LubanConfig.InitializeAsync` 只做 `_impl ??= new LubanConfigImpl(...)`，没有在途任务/代际/取消（`LubanConfig.cs:51-62`）。`LubanConfigImpl.InitializeAsync` 只在**入口**检查 `_initialized`（`LubanConfigImpl.cs:61-66`），随后 await 加载、构造、注册；`Destroy` / `ResetForNewSession` 只清字段（同文件 `:162-198`），不取消在途。
- **可复现的三种后果**：
  1. 同一帧内两次 `InitializeAsync` 都通过入口检查 → 两次加载、两次 `CreateTables`、两次注册，`ConfigChanged` 重复触发。
  2. `InitializeAsync` 在 `await LoadAsync` 期间被 `Destroy()` → 门面 `_impl = null`，但在途 impl 继续走完并注册；`LubanConfig.IsInitialized` 为 false，而 `ConfigManager` 里留下了永远不会被 `Destroy` 卸载的表。
  3. 两个不同 adapter 并发：后完成者的 `_adapter` 覆盖前者；`Destroy` 只卸载后者的表，前者的表成为孤儿。
- **影响**：启动流程中重复登记阶段、用户手动初始化与 Bootstrap 阶段重叠、场景切换时调用 Destroy，都是现实路径。孤儿注册会跨场景残留，并与后续 schema 冲突。
- **建议修法**：`LubanConfigImpl` 增加代际号 + `CancellationTokenSource`：入口若已有在途任务，返回同一个 `UniTask`；`Destroy`/`ResetForNewSession` 递增代际并取消 CTS；每个 await 之后、注册前后检查代际，不一致就抛 `OperationCanceledException` 且**不注册**；注册失败回滚与代际检查共用同一收尾。
- **验证（改前必红）**：用带门闩的 `FakeLubanProvider`（`UniTaskCompletionSource`）写三条 PlayMode 用例：并发两次 `CreateCalls == 1`；Destroy 在途后 `ConfigManager.IsLoaded<FakeRow>() == false`；Destroy 后重新初始化，旧在途不得污染新代际。

#### LUB-04 生产默认加载后端零覆盖

- **证据** `[读码]`：所有运行时测试都注入 `FakeLubanProvider`（`Tests/Runtime/LubanConfigTests.cs`、`LubanEndToEndTests.cs`），**没有任何用例走到 `LubanConfig.InitializeAsync` + `AssetLubanDataProvider`**。`AssetLubanDataProvider` 的 TextAsset / RawFile 两条路径、错误消息、句柄释放全部未测（`AssetLubanDataProvider.cs:16-127`）。
- **影响**：这是所有使用方默认走的路径；YooAsset 句柄释放、错误文案、`LoadAllAsync` 顺序假设一旦出错，现有测试全绿。属于审计手册 D3「替身绕开了什么」的典型。
- **建议修法（分层）**：
  1. **低成本**：`AssetManager.SetInstance(fake)`（公开 API）注入只返回 `default(AssetHandle<T>)` 的假实现，走 `LubanConfig.InitializeAsync(adapter)`，断言「表名 + location + LastError」出现在异常里（同时覆盖 LUB-12）。
  2. **中成本**：给 `AssetLubanDataProvider` 抽一个内部 seam（例如 `Func<...>` 或 `ILubanAssetReader`），让 TextAsset/RawFile 的成功/失败映射在单测里可覆盖；生产默认实现仍走 AssetManager。
  3. **高成本**：PlayMode + `EditorSimulate` + 临时 collector 做真实 YooAsset 集成（与示例同型）。
- **验证**：新增 `LubanAssetProviderTests`；`run-tests.ps1 -Filter LubanAssetProvider`。

#### LUB-05 代码 / 数据 / 工具输入目录可重叠

- **证据** `[读码]`：`LubanSettingsValidator` 只检查 `integration` 与 `code` / `data` 的双向包含与相等（`LubanSettingsValidator.cs:80-108`）。**`code` 与 `data` 之间没有检查**；`LubanDllPath` / `ConfPath` / `DotnetPath` 落在两个输出目录内也没有检查。
- **影响**：`CodeOutputDir == DataOutputDir`、或 `data` 嵌套在 `code` 下时，Luban 清理其中一个可能删除另一个；`Luban.dll` / `luban.conf` 放在输出目录内则可能被自动清理删掉。这是 Editor 一键操作里的潜在数据破坏。
- **建议修法**：增加 `code != data`、双向非包含；把 Dll / Conf（以及带路径的 DotnetPath）与两个输出目录做重叠检查。补测试。
- **验证**：EditMode 用例；与 LUB-02 同批。

#### LUB-06 Bootstrap 阶段无条件销毁全局状态

- **证据** `[读码]`：`LubanBootstrapStage.Shutdown` 直接 `LubanConfig.Destroy()`（`LubanBootstrapStage.cs:69-73`），不区分「本阶段是否真的初始化过」。核心 `AssetBootstrapStage` 有 `_initializedByThisStage` 的现成范式。
- **影响**：用户先手动 `LubanConfig.InitializeAsync`，再登记阶段；阶段 `ExecuteAsync` 因重复初始化告警早退，`Shutdown` 却把用户的状态销毁。阶段从未执行时同样。
- **建议修法**：执行前记录 `wasInitialized = LubanConfig.IsInitialized`；成功后 `_initializedByThisStage = !wasInitialized`；`Shutdown` 仅在本阶段初始化过时 Destroy。
- **验证**：测试先 `InitializeForTestsAsync` 注入 fake，再 `stage.Shutdown()`，断言仍初始化；另一条阶段初始化后 Shutdown 销毁（需要内部 provider 注入或改用 seam）。

### 7.3 P2 发现

#### Editor 写盘与生成

- **LUB-07 stale 清理不删除 `.meta`** `[读码]`：`LubanIntegrationWriter.Write` 对旧文件只 `File.Delete(fullPath)`（`LubanIntegrationWriter.cs:70-89`），同目录的 `.g.cs.meta` 留下。仓库已有孤儿 meta 的实测史；建议同时删 `fullPath + ".meta"`，加单测（临时目录造 `.cs` + `.cs.meta`）。
- **LUB-08 写盘非原子** `[读码]`：`Write` 在循环中先写前面的文件、后遇到「拒绝覆盖非生成文件」或写入过程中的 IO 异常才抛（`LubanIntegrationWriter.cs:44-92`）→ 部分新文件 + 旧 manifest（坏 manifest 会在循环前抛，不留部分文件；这一点已核对）。建议先做全量 preflight（覆盖保护），再写文件；或写临时目录后整体替换。加单测：第二个文件触发拒绝时第一个文件保持不变。
- **LUB-09 `Sanitize` 文件名碰撞静默覆盖** `[读码]`：`files["RowInterfaces/" + Sanitize(RowTypeFullName) + ".g.cs"]`（`LubanIntegrationEmitter.cs:38`）以字典索引赋值；`cfg.a_b.C` 与 `cfg.a.b_C` 都变成 `cfg_a_b_C`，后写覆盖前写。建议生成前检测碰撞并报 `LubanGenerationException`（含两个行类型名）。加单测。
- **LUB-10 生成期不校验 target 与数据目录** `[读码]`：`LubanIntegrationGenerator` 只看 `codeDir` 存在（`LubanIntegrationGenerator.cs:39-49`），不校验生成代码的 `Tables` loader 类型（`ByteBuf` vs `JSONNode`）是否与 `CodeTarget` 一致，也不校验 `DataOutputDir` 是否真有 `TableNames` 对应的数据文件。建议：解析 `Tables(Func<string, X>)` 的 `X` 与 settings 对照；对照 `DataOutputDir` 的文件名/扩展名；错误在生成期给出。
- **LUB-11 JSON loader 不处理 UTF-8 BOM** `[读码]`：生成的适配器对 JSON 用 `Encoding.UTF8.GetString(bytes)` 后直接 `JSON.Parse`（`LubanIntegrationEmitter.cs:84-89`）。若数据文件带 BOM（人工编辑常见），SimpleJSON 可能解析失败。建议在生成的 lambda 里剥掉首字符 `U+FEFF`，或写入已知限制。
- **LUB-17 索引表重复键错误缺上下文** `[读码]`：索引 list 重建时 `dictionary.Add` 的 `ArgumentException` 只带 .NET 默认文案（`LubanConfigBridge.cs:66-73`）。建议捕获并附「表/行类型 + 重复键」。

#### 运行时

- **LUB-12 错误消息埋 InnerException** `[读码]`：`LubanConfigImpl` 把 provider 的 `InvalidOperationException`（如 AssetManager 未初始化）包成通用 `[LubanConfig] Failed to load Luban data files.`（`LubanConfigImpl.cs:86-89`）。README 承诺「消息带表名与 location」，但外层只有通用文案。建议外层拼接 `ex.Message` 或对 `InvalidOperationException` 不包装。加测试。
- **LUB-13 主线程契约只在文档里** `[读码]`：`LubanConfig` 类文档声明所有成员主线程（`LubanConfig.cs:13`），但没有任何断言；核心 Message / Pipeline / Asset 有 `PlayerLoopHelper.IsMainThread` 的 Editor-only 守卫先例。建议在 `InitializeAsync` / `Destroy` / `ResetForNewSession` 入口加 Editor-only 守卫（只报首错），或在 README 明确「无机械守卫」。
- **LUB-28 `ConfigChanged` 逐表触发，订阅者看到半初始化状态** `[读码]`：生成的 `RegisterToConfigManager` 逐表调用 `RegisterTable` / `RegisterGlobal`（`LubanIntegrationEmitter.cs:98-124`），每次触发 `ConfigChanged`；`LubanConfig._initialized` 在全部注册完成后才置 true（`LubanConfigImpl.cs:155-157`）。订阅者在第一条事件里查 `LubanConfig.Tables` 会抛。建议写入包 README 的「行为契约」，并加一条行为测试锁定。

#### Editor 体验与平台

- **LUB-14 批处理/CI 风险** `[读码]`：`LubanMenu` 用 `Directory.GetCurrentDirectory()` 当项目根（`LubanMenu.cs:100` 等），并用 `EditorUtility.DisplayDialog` 报结果（同文件 `:134` 等）。批处理或从其他目录启动时 cwd 不可靠；`DisplayDialog` 在 batchmode 下可能阻塞/无意义。建议用 `Path.GetDirectoryName(Application.dataPath)`；`Application.isBatchMode` 时只 Log。
- **LUB-15 平台校验误判** `[读码]`：`ValidateAsmdefScope` 只要 `includePlatforms` 含 `Editor` 就报「Editor-only」（`LubanProjectValidator.cs:97-101`）。`["Editor","StandaloneWindows64"]` 是合法的双平台 asmdef，会被误报。建议只有「非空且全部为 Editor」时才报；同时检查 `excludePlatforms` 是否排除了运行平台、`defineConstraints` 是否可能排除运行时代码（至少给 Warning）。
- **LUB-16 路径解析会抛未捕获异常** `[读码]`：`LubanCommandBuilder.TryBuild`、`LubanMenu.ValidateForRun`、`LubanIntegrationGenerator.Generate` 在 `try` 之外调用 `Path.GetFullPath`（`LubanCommandBuilder.cs:207-213`、`LubanIntegrationGenerator.cs:142-148`）。非法路径字符会让菜单直接抛异常而不是显示可读错误。建议包一层 try → 返回 error。

#### 文档

- **LUB-18 README 文件树漏 `LubanLog.cs`** `[读码]`：`## 模块结构` 的 Runtime 清单没有 `LubanLog.cs`（`Assets/XFramework.Luban/README.md:104-116`）；扩展包不在 `check-docs.ps1` 的模块树门禁内，所以没红。建议补行，并考虑加一条包内自检。
- **LUB-19 README 缺 `## 概述`** `[读码]`：包 README 有 18 节骨架的绝大多数，但没有 `Documentation/ModuleReadme.md` 要求的 `## 概述`。建议在开头补该节。
- **LUB-20 维护文档两处陈旧** `[读码]`：本文件 §3.2 说「包内 README 的反射批注册示例正是后者」，但核心 `Assets/XFramework/Runtime/Config/README.md` 当前没有该示例（只有一段「推荐直接用扩展包」）；本文件 §4.2 的表格仍把手写 helper 标为「推荐」，而正文已说扩展包适配器是首选（本文件 `:256-260`）。建议改成「回退路径」并删掉失效指路。

#### 测试与治理

- **LUB-21 核心门禁不覆盖扩展包，且 `Workflow.md` 的「自建测试」说法不实** `[读码]`：`Tools/check-docs.ps1`、`check-samples.ps1`、`PublicApiBaselineTests`、`ReadmeTableTests` / `ReadmeSectionTests`、`AutoInitTests`、`PackageBoundaryTests`、`NamingConventionTests` 都只扫 `Assets/XFramework`；`Documentation/Workflow.md:208-211` 说「`.meta`、README 与公开面靠自建测试与评审」，但包里并没有 README 文件树/公开面的自建测试。建议至少补：`check-samples.ps1 -SamplesRoot Assets/XFramework.Luban/Samples~`（当前示例代码**完全没编译**）；包内 README 必答节 + 文件树与磁盘一致；`PackageBoundaryTests` 覆盖扩展包；扩展包自己的 `AutoInitTests`；扩展 Runtime 程序集的公开 API 基线。
- **LUB-22 测试路径助手对消费方布局判断错误** `[读码]`：`LubanTestPaths` / `LubanRuntimeTestPaths` 检查 `Packages/com.venusy609.xframework.luban`（`LubanTestPaths.cs:24-31`），但 Git 安装的 UPM 包在 `Library/PackageCache/<name>@<hash>`，`Packages/<name>` 只对 embedded 包成立。消费方把这些测试加入 `testables` 时会 `DirectoryNotFoundException`。建议用 `UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LubanConfig).Assembly)`（Editor 测试）或扫描 `Library/PackageCache` 前缀（Runtime 测试）。
- **LUB-23 生成物夹具无全量比对** `[读码]`：`LubanIntegrationEmitterTests` 只断言关键子串（`LubanIntegrationEmitterTests.cs:56-88`）；`Tests/Runtime/Fixtures/Generated/Integration` 与示例生成物是签入的，emitter 稍改（加 using、改注释）不会红。建议加 golden-file 测试：用当前 emitter 对同一 fixture 重新生成，与签入文件逐字比较。
- **LUB-24 两份生成夹具无同步守卫** `[实测]`：`Tests/Editor/Fixtures~/Bin` 与 `Tests/Runtime/Fixtures/Luban` 当前内容逐字相同（`diff -rq`），但无守卫；`Fixtures~/Json` 是另一套。建议加一条断言（Bin 夹具 == Runtime 夹具），或在 fixture 生成脚本里从同一源复制。
- **LUB-25 示例未进入编译门禁** `[读码]`：`check-samples.ps1` 默认只扫 `Assets/XFramework/Samples~`；Luban 示例的 `LubanSample.cs` 与生成物没有任何编译通道。LUB-21 的第一条即可闭合。
- **LUB-26 无发布 tag** `[实测]`：`git tag --list` 只有 `0.3.0`，且 `git tag --contains` 不包含 Luban 提交；README 的扩展包安装 URL 让使用者钉 `<发布 tag>`，目前无 tag 可钉（`main` 与「钉 tag」纪律冲突）。发布前需打 tag（用户执行；不自动推送）。
- **LUB-27 `CLAUDE.md` 未登记扩展包** `[读码]`：`## 项目定位` 仍只说「内嵌 UPM 包 `com.venusy609.xframework`」（`CLAUDE.md:3-9`）；`## 依赖管理` 的「依赖版本只有一份真相」只覆盖核心 README / installer / manifest（`CLAUDE.md:83-90`）。建议补一段：可选包位置、不在核心六道门禁射程、测试随全量、`com.code-philosophy.luban` 的版本真相在扩展包 README + manifest（并把三者纳入机械守卫）。**修改 `CLAUDE.md` 需按规则单独请用户确认。**

### 7.4 P3 / 记录项

- **性能与加载**：RawFile 逐文件串行加载（TextAsset 走 `LoadAllAsync` 并行）；TextAsset 进度只在全部句柄返回后跳变（0.05→0.75），文件多时进度条不真实；`LubanOptions.DataRoot` 未归一化反斜杠/首斜杠/空白；`LubanConfigImpl.BuildFiles` 对表名只查空/重复，未限制 `^[A-Za-z0-9_]+$`。
- **契约与错误**：`LubanConfig` 的异常 XML 文档列出 `ArgumentNullException` / `LubanConfigException`，但空 `DataRoot` 抛 `ArgumentException`；注册路径的 `catch (OperationCanceledException)` 不回滚（`LubanConfigImpl.cs:139-147`）；`LubanConfigBridge.Unload` 即使类型未加载也会触发 `ConfigChanged`（核心行为，可文档提一句）；`LubanConfigException` 文档说「消息统一带 `[LubanConfig]` 前缀」，但类本身不强制。
- **Editor 工具**：`LubanProcessRunner` 无输出上限（`--verbose` 大工程可能撑大 StringBuilder）；未设置 `StandardOutputEncoding`（中文可能有编码风险，需探针）；`LubanMenu.ShowProcessFailure` 硬编码「5 分钟」，与 `DefaultTimeoutMilliseconds` 有漂移风险；`LubanSettingsValidator` 的关键字表缺上下文关键字（`record` / `init` / `nint`…）；路径归一化不折叠 `//`。
- **解析器/生成器**：`LubanCodeParser` 的 `Data` / `Loader` 正则不支持 `global::`、多行泛型、自动属性 `DataMap`；5.1.0 当前形状不需要，但失败时有的地方是「拒绝写盘」（好），`DataMap` 属性缺失时会退化成 IndexedList（功能正确、丢零拷贝），建议加显式识别。`ResolveType` 在「typeText 带命名空间但只有唯一同名候选」时会接受不同命名空间的类型；`SimpleIdentifierRegex` 会接受 C# 关键字；不支持 `record` / 泛型行类型 / `readonly struct` 的 partial 修饰符一致性。Luban 5.1.0 不生成这些形状，升级时需重跑夹具。
- **死字段/引用**：`LubanGeneratedTable.TableTypeFullName` 只赋值不读；`LubanIntegrationWriter.ManifestData.Tables` 在生产路径只写不读（测试读）；Editor / Editor-Tests 的 asmdef 引用了未使用的 `Venusy609.Xframework` / `UniTask`；`LubanDataTarget` 与 `LubanCodeTarget` 同文件（`LubanCodeTarget.cs`）。
- **版本耦合**：生成的 JSON adapter 硬编码 `Luban.SimpleJSON.JSON`；官方 runtime 在较新版本把命名空间改为 `Luban.SimpleJson`（`[网读]` Luban v4.6.0 release note）。包 README 已钉 v1.2.0，升级会编译失败（响亮），建议在「已知限制」写明。
- **懒加载/分片**：官方 5.x 有 `dataTarget=bin-offset`（记录级 offset，配合自定义模板做懒加载）；TEngine 在其上做了 `bin-sharded` + `ConfigSystem`。本包 v1 全量加载，建议写进「已知限制」+ 台账「未做」，重开判据=真实使用方出现启动卡顿/大表内存压力。
- **不支持 target**：`cs-newtonsoft-json` / `cs-editor-json` / `cs-dotnet-json` 需重新验证生成物形状；包 README 已写「其它 target 需重新验证」，可明确列为未支持。
- **未验证**：IL2CPP / Android / iOS / 真实 YooAsset TextAsset+RawFile / 消费方 UPM 布局 / `LubanProcessRunner` 输出编码。
- **行尾与属性**：`git ls-files --eol` 实测扩展包 43 个生成文件工作区为 CRLF（索引侧仍是 LF）；归一化配方见 `Documentation/Workflow.md` §六。建议 `.gitattributes` 增加 `*.bytes binary`（Luban bin 数据是二进制，不能靠 `text=auto` 猜测），并考虑把 `*.xml` / `.luban-integration-manifest` 显式标为文本。
- **包元数据**：`package.json` 的 `documentationUrl` / `changelogUrl` 指向 `main`（版本化文档建议指 tag）；缺 `licensesUrl`；CHANGELOG 无 `[Unreleased]`（0.1.0 未发 tag，后续修复应先归入 0.1.0 或新版本）。

### 7.5 判据逐类扫（留痕）

| 类 | 结论 | 要点 |
|---|---|---|
| A1 文档/注释与实现相反 | **命中** | LUB-01、LUB-18、LUB-20、LUB-12（错误消息承诺）；其余 README 承诺逐条核对基本属实 |
| A2 契约面缺口 | 扫过 | 门面转发不适用（扩展包无 `IXxxManager`）；`ILubanConfigAdapter` 成员齐全；`LubanConfigBridge` 公共入口未强制 `ConfigManager` 已初始化（调用会抛核心异常，可接受） |
| A3 XML 未转义/cref 死链 | 扫过，未验证 | 扩展包不在 `check-docs` 射程；人工 grep 未发现未转义泛型；**不能声称编译器已验**，建议纳入 LUB-21 |
| B1 单向闩锁/释放后仍可用 | **命中** | LUB-03（Destroy 在途仍可用）、LUB-06（阶段所有权） |
| B2 回池/销毁后仍被回调 | 扫过 | 无对象池、无订阅；不适用 |
| B3 异常隔离不对称 | 小命中 | 加载异常统一包成 `LubanConfigException`，但 OCE 在注册路径不回滚；`Destroy` / `TryUnregister` 只告警 |
| B4 线程契约只在散文里 | **命中** | LUB-13 |
| B5 该回收的没回收/陈旧重放 | 扫过 | 无按实体增长的缓存；`_tables` 单实例 |
| B6 共享协调协议代际 | **命中** | LUB-03（无代际/在途任务） |
| C1 每帧路径分配/反射 | 扫过 | 运行时代码只在初始化路径分配；无每帧路径；无反射 |
| C2 平台/引擎隐式行为 | 小命中 | 路径归一化、CRLF 漂移、`*.bytes` binary、批处理 cwd/dialog、编码未验证 |
| D1 跨模块 Internal/依赖方向 | 扫过 | Runtime/Editor 只用核心公开面；无 `XFramework.*.Internal` 引用；`Luban.Runtime` 只在 Tests 程序集 |
| D2 重载决议与二义 | 扫过 | `RegisterTable` 两个重载按参数个数区分，无二义；`InitializeAsync` 可选参数形状无冲突；未发现需编译复现的新形状 |
| D3 测试假绿/边界 | **命中** | LUB-04、LUB-23、LUB-24、LUB-25、LUB-26 |
| D4 静默失败 | **命中** | LUB-08 部分写、LUB-09 碰撞、LUB-10 目标不校验、LUB-11 BOM、LUB-12 消息埋藏 |
| D5 跨模块能力缺口 | 扫过 | 核心已提供 `RegisterTable` / `RegisterGlobal` / `ConfigTable` / `AssetManager.LoadAllAsync`；无「本模块偷懒」证据；懒加载缺口属能力未做，见 P3 |
| E 键空间与相等语义 | 扫过 | 键是行类型 + 主键；无全局裸值注册表；`ValueTuple` 键可用；无另一来源合并风险 |
| F 双观测面语义不一致 | **命中** | LUB-28（`ConfigChanged` 逐表 vs `IsInitialized` 整包） |

### 7.6 第三方与 Luban 文档收集

#### Luban 官方（`[网读]`，5.x）

- 安装与运行要求：<https://www.datable.cn/docs/guide/install>（.NET SDK 8.0+；Release 包 / `Tools/Luban` / 自行编译；CLI `dotnet Luban.dll --conf ... -t ... -c ... -d ...`）。
- 运行时加载：<https://www.datable.cn/docs/guide/load-runtime>（单一 `Tables` 对象；loader 按文件名返回 JSON / ByteBuf；`cs-simple-json+json` 开发、`cs-bin+bin` 发布；代码/数据 target 必须配套；Android 直接读 StreamingAssets 可能失败，应先读入内存；生成代码同步，异步需包装）。
- 内置 target：<https://www.datable.cn/docs/reference/builtins>（C#：`cs-bin`、`cs-simple-json`、`cs-newtonsoft-json`、`cs-editor-json`、`cs-dotnet-json`、`cs-protobuf2/3`；数据：`bin`、`bin-offset`（记录级 offset，懒加载原语）、`json`、`json2`、`lua`、`xml`、`yaml`、`bson`、`msgpack` 等）。
- 生成与运行时总览：<https://www.datable.cn/docs/runtime>。
- 仓库：<https://github.com/focus-creative-games/luban>、<https://github.com/focus-creative-games/luban_unity>、<https://github.com/focus-creative-games/luban_examples>。

#### 本仓一手核对（`[一手核对]`）

- `Library/PackageCache/com.code-philosophy.luban@061314200e2f/`（v1.2.0）：`Runtime/Luban.Runtime.asmdef`（autoReferenced true）、`Runtime/ByteBuf.cs`（`ByteBuf(byte[])` 构造）、`Runtime/SimpleJSON/SimpleJSON.cs`（`namespace Luban.SimpleJSON`、`JSON.Parse(string)`）、`package.json`。
- 本文件 §二–§四：Luban 5.1.0 命令行选项表、模板形状、`Tables(Func<string, ByteBuf>)`、四种表形状、行接口实现与反射注意点。
- `Assets/XFramework.Luban/Tests/Editor/Fixtures~/Bin|Json`、`Tests/Runtime/Fixtures/Luban`：真实 5.1.0 生成物（`Tables` 在 `namespace cfg`、map / 单行 / 带索引 list / 纯列表）。
- `Assets/XFramework/Runtime/Config/README.md`「方式三」与 `Documentation/Modules/Config.md` 的 Luban 台账条目。

#### 第三方对照（`[网读]`）

- **TEngine 的 Luban 集成（ConfigSystem）**：单例桥接类、四加载模式（懒加载默认推荐 / UniTask / 同步 / 服务器 Task）、`standard` vs `lazyload`（`-c cs-bin -d bin-sharded` + `--customTemplateDir` + `dataExporter=sharded`）、业务层 `XxxConfigMgr : Singleton<...>` 封装；数据由 YooAsset 管理。参考：<https://blog.gitcode.com/c5da64bd72622773200db3483419ef56.html>、<https://blog.gitcode.com/e12c4d3d1894f88ba9a6e5c93f9c5d7c.html>、<https://deepwiki.com/Alex-Rachel/TEngine/7.2-luban-integration>（本次 429，未读取正文）。
  - **对照结论**：懒加载/分片是 TEngine 在官方 `bin-offset` 之上做的项目级扩展，不是 vanilla Luban 5.x 的内置 `bin-sharded`；本包不做它是合理的 v1 取舍，但应在 README「已知限制」+ 台账「未做」写明。
- **GameFrameX 的 JSON vs BIN 说明**：开发期 JSON、发布期 bin，与本包支持的两种 target 一致；参考 <https://gameframex-docs.vercel.app/ja/config/tasks/json-vs-bin.html>。
- **SimpleJSON 命名空间漂移**：Luban v4.6.0 release note 提到 `SimpleJSON` → `Luban.SimpleJson` 以避免与 `com.unity.collab-proxy` 的 TextMateSharp 冲突；教程也提到 Unity 2021 上 `cs-simple-json` 编译问题与 `cs-newtonsoft-json` 备选。参考 <https://mygit.top/release/302200067>、<https://blog.csdn.net/weixin_43796392/article/details/159763971>。
  - **对照结论**：本包硬编码 `Luban.SimpleJSON`，在 v1.2.0 正确；升级官方 runtime 会编译失败（响亮），建议写入「已知限制」。

**能力面对照（点快照；对照目的不是抄，而是知道自己在哪）**：

| 能力面 | 本扩展包 | Luban 官方 runtime / 文档 | TEngine ConfigSystem |
|---|---|---|---|
| 生成代码加载 | 生成式 `ILubanConfigAdapter`；先异步读全字节，再同步构造 `Tables` | 生成的强类型 `Tables`；官方 loader 同步，按文件名取 JSON / ByteBuf | `ConfigSystem` 桥接；支持同步 / UniTask / 服务器 Task / 懒加载 |
| 数据 target | `cs-bin + bin`、`cs-simple-json + json` | 另有 `bin-offset`、`json2`、`cs-newtonsoft-json`、protobuf 等 | `bin`（standard）与项目级 `bin-sharded` + custom template（lazyload） |
| 懒加载 / 分片 | 不支持；v1 全量构造 | `bin-offset` 是记录级 offset 原语，需自定义模板使用 | 支持；`lazyload` 按属性/按 key 取片 |
| 注册到框架 | 逐表 `ConfigManager.RegisterTable/RegisterGlobal` | 不绑定框架；调用方自行持有 `Tables` | 自有 `ConfigSystem` + 业务 `XxxConfigMgr` 封装 |
| 编辑器一键导表 | Project Settings + 菜单；生成行接口实现/适配器/manifest | CLI + 示例脚本；无框架级设置页 | 脚本 + 模板复制 + 业务封装 |
| 运行时依赖 | 生产程序集不引用 Luban 程序集（生成物在使用方程序集）；Tests 夹具引用 `Luban.Runtime` | 需要 `com.code-philosophy.luban` 运行时包 | 深度集成 Luban + YooAsset + 框架自身 |
| 适用场景 | 已用 XFramework、想保持核心零依赖的项目 | 任意 Unity 项目 | 采用 TEngine 全家桶的项目 |

#### 设计替代（工程判断，非第三方文档）

- `--customTemplateDir` 直接在 `bean.sbn` 注入 `IConfigRow`：本文件 §3.3 已评估否决（模板拿不到表主键类型；整文件覆盖会静默丢新逻辑）。
- 运行时反射批注册：本文件 §4.2 已列为表多时的备选；缺点是按 `Tb*` 属性约定、反射、`Id` 显式实现的坑。
- Roslyn source generator：可在编译期生成 partial，但需要 analyzer 分发与 Unity 集成，且无法替代 Luban 导表；本包的「Editor 菜单 + 文本解析 + 生成 partial」更简单，保持。

### 7.7 建议后续批次（未实施）

> 以下是建议的原子提交边界，**本轮未执行**。实施时每批先写「改前必红」用例，再改代码；计划批准后按批提交。

- **批次 A 设置校验器**（LUB-01 / 02 / 05 / 16）：`fix(Luban): 允许工具与配置的绝对路径并加固输出目录校验`；`fix(Luban): 路径解析失败返回可读错误`。验证：`run-tests.ps1 -Filter LubanSettingsValidator -Platform EditMode`、`-Filter LubanCommandBuilder -Platform EditMode`。
- **批次 B 运行时生命周期**（LUB-03 / 06 / 12 / 13）：`fix(Luban): 串行化初始化并在 Destroy 时取消在途`；`fix(Luban): 引导阶段按所有权清理`；`fix(Luban): 保留加载失败的可操作消息`；`feat(Luban): 主线程入口守卫（Editor-only）`。
- **批次 C 写盘/生成**（LUB-07 / 08 / 09 / 10 / 11 / 17）：`fix(Luban): 写盘前置校验并删除 stale .meta`；`fix(Luban): 生成期检测文件名碰撞与 target/数据不一致`；`fix(Luban): 索引表重复键错误带上表上下文`。
- **批次 D 测试/文档**（LUB-04 / 18 / 19 / 20 / 22 / 23 / 24 / 28）：`test(Luban): 覆盖默认 AssetManager 加载路径的错误分支`；`test(Luban): golden-file 比对生成物与夹具`；`docs(Luban): 同步 README 文件树、概述与已知限制`；`test(Luban): 修正消费方包路径定位`。
- **批次 E 门禁/治理**（LUB-21 / 25 / 26 / 27，需单独批准）：`chore(Luban): 示例编译纳入 check-samples`；`test(Luban): 扩展包 README/公开面/边界/AutoInit 守卫`；`docs(Luban): 登记扩展包到 CLAUDE.md/Workflow/Ledger`；`chore(release): 为扩展包打 tag`（用户执行，不自动推送）。

**定向测试注意**：`run-tests.ps1 -Filter` 未显式 `-Platform EditMode` 时只跑 PlayMode，`Tests/Editor/` 下的用例会假绿；看 Editor 侧必须显式指定平台。

### 7.8 不做 / 暂不做（结论）

- **不重建核心内建 Luban 适配层**——扩展包形态已隔离成本（`Documentation/Modules/Config.md` 已记）。无重开判据。
- **不把 `ILubanDataProvider` 公开**——没有真实可替换后端；需要自定义加载时用 `LubanConfigBridge` 手动注册（包 README 已写）。
- **不做 `bin-offset` / 分片懒加载**——v1 全量；重开判据=真实使用方出现启动卡顿/大表内存压力。
- **不支持 `cs-newtonsoft-json` / `cs-editor-json` / `cs-dotnet-json`**——需先探针生成物形状；当前两种 target 已覆盖开发/发布。
- **不做 Roslyn source generator**——Editor 菜单 + 文本解析已满足；避免新增 analyzer 分发面。
- **不把扩展包移入核心 Runtime 树**——保持可选包边界。

### 7.9 开放项 / 未验证

- **Luban 是否真的清理输出目录**：校验器注释如此声称，本轮未跑探针；LUB-02 / LUB-05 的严重度依赖该事实，建议实施前跑一次哨兵探针。
- **真实 YooAsset 的 TextAsset/RawFile 加载**：未在 EditorSimulate / 真机跑；LUB-04 的成功路径仍属未验证。
- **IL2CPP / Android / iOS**：未验证。
- **消费方 UPM 布局**：未在裸消费方工程里跑 Luban 测试；LUB-22 为读码结论。
- **官方与第三方资料为 `[网读]`**，不是本地可复现的一手事实；本文件 §〇 的 `[一手核对]` / `[探针实测]` 仍以其自身标记为准。
