# Luban —— 接入配方（维护侧）

> **收件人**：框架维护者（含「下一轮要不要重新论证内建适配层」这个问题的回答者）。**不是**随包发布的使用文档。
> **使用方契约**在 `Assets/XFramework/Runtime/Config/README.md` 的「方式三」——本文件不复述它，只补它**刻意不写**的部分：随 Luban 版本漂移的那些。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。需要时整篇转发给使用方。

## 〇、核对状态（先读这一节）

本文件每条事实都带标记，**不要把 `[未实测]` 当成已验证**：

| 标记 | 含义 |
| --- | --- |
| `[一手核对]` | 读**本地那份** Luban 5.1.0 发布包自带的模板与程序集元数据得出，可复现。模板在 `Luban/Templates/<codeTarget>/*.sbn`，命令行选项表在 `Luban.dll` 的 `[Option]` 特性字符串里——两者都不需要执行 Luban |
| `[网上读取]` | 读官方文档页或官方示例仓库的文件得出，**没有本地副本可复现**，可信度低于上一档 |
| `[探针实测]` | 用一个独立的 C# 探针程序跑过（复现 XFramework 侧的接口/反射语义），10/10 通过 |
| `[未实测]` | 只有文档或推理支撑，**没跑通过** |

**核对基准**：Luban **5.1.0**，程序集版本 `5.1.0+df34215d2e22035073d1af83d44e3ac0744dc3b3`，目标框架 `net8.0`，2026-10-10 取自 GitHub Release `v5.1.0` 的 `Luban.7z`（1.6 MB，用 7-Zip 解出 155 个文件）。

> **端到端导表一次都没跑过 `[未实测]`。** 本轮没有执行 Luban 本体（本仓无任何配置表源数据，执行下载的第三方二进制需单独授权）。因此：**§二 的命令行逐字可信**（来自选项表 + 官方示例工程），但「跑起来究竟产出什么、文件名长什么样」需要下一次真正导一次表来补。§三、§四 的**框架侧**结论不依赖这一点——它们是探针实测的。

---

## 一、结论：边界画在哪

**框架侧要的东西只有两件**，且都不随 Luban 版本变：

1. 行类型实现 `IConfigRow<TKey>`（`TKey Id { get; }` 是接口要求的唯一成员）
2. 你把行投影成 `Dictionary<TKey, T>` 再包成 `ConfigTable<T>` 注入

**Luban 侧的一切**——表定义怎么写、命令行怎么调、`Tables` 怎么构造、表对象怎么取行——归使用方的工具链，框架不描述、也不代为适配。理由与沿革见 `Documentation/Modules/Config.md` 的「已评估未采纳」：**内建适配层必须猜 Luban 的私有形状，而该形状既不承诺稳定、也不由本框架控制**。

这条边界的具体收益：本文件**可以被 Luban 版本演化淘汰**（整篇重写即可），而包内那份契约不会。

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

`[一手核对]` 5.1.0 里 C# 侧的 codeTarget 有：`cs-bin`、`cs-simple-json`、`cs-newtonsoft-json`（另有 `cs-dotnet-json`、`cs-editor-json`、`cs_pb` 的模板目录）。**codeTarget 要与 dataTarget 配套**（选 `cs-bin` 就配 `-d bin`）。

### 2.3 最小工程结构

**不要在别处复写一份 `luban.conf` 与 schema 范例**——那就是第二份会过期的真相。正确做法是逐字复制官方 `luban_examples/MiniTemplate/` 作基准再改。`[网上读取]` 该模板的结构：`luban.conf`、`Defines/`（表结构与枚举定义）、`Datas/`（数据源）、`gen.bat` / `gen.sh`、`output/`。

框架只对 schema 提**两条要求**，其余随意：

- 要注册成 Table 的表，其 bean 必须有一个主键字段（默认取名为 `id`，生成后成为 C# 侧的 `Id`）——§三 的接缝靠它。
- 表模式决定接到哪个 API：有主键的 map 表 → `RegisterTable`；单行表（`Data`）→ `RegisterGlobal<T>`（要求 `T : class`）；纯列表表没有主键，接不进来。

---

## 三、让生成的行类实现 `IConfigRow<TKey>`（接缝）

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

### 3.2 所以接缝只能写成显式接口实现 `[探针实测]`

```csharp
// 你写的接缝文件（每张表一小段）
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

**若主键字段不叫 `Id`**（比如列名是 `item_id` → 字段 `ItemId`），接缝可以写成普通的隐式实现 `public int Id => ItemId;`，比上面更简单。

> **这套写法有机械守卫**：`Assets/XFramework/Tests/Editor/Config/ExplicitInterfaceRowTests.cs`（复刻生成物的形状——`partial` + `readonly` 字段 + 只有带参构造——并锁住主键从接口泛型实参取、值经接口的 `PropertyInfo` 读得到、键类型不符仍早失败）。该文件做过红证：把 `ConfigTypeHelper.ExtractKeyType` 临时改成「从具体类型取 `Id` 属性」后，6 例中 5 例当场变红，而既有用例全绿。改动本文件里的建议时，那条守卫会先响。

### 3.3 备选：改 Scriban 模板让生成期就带上接口 `[未实测]`

走 `--customTemplateDir`（复数，可给多个）覆盖 `bean.sbn`。**不推荐**，两条理由：

- **模板拿不到所属表的主键类型**。`bean.sbn` 的作用域里只有 `__bean`（其字段是 `export_fields` / `id` 等），拿不到「这张表的 key 是什么类型」。于是 `TKey` 只能在模板里写死成项目约定的类型——**多键表与非 int 主键就覆盖不了**，而这恰恰是接缝文件天然支持的。
- **覆盖整文件会静默丢掉新版本逻辑**。Luban 升级时若改了 `bean.sbn`，你的整文件副本不会报错，只会安静地少生成东西。

真要走这条路，纪律是「在你自己版本的 `bean.sbn` 里找锚点、做最小编辑」（例如在两处类声明后追加 `, global::XFramework.XConfig.IConfigRow<int>`），让它在上游重构时**响亮地失败**而不是静默过期。改模板时加 `--disable_cache` 关闭模板缓存。

---

## 四、注册胶水

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

**可以省掉 `ToDictionary` 这一份拷贝** `[探针实测]`：`tb.DataMap` 声明为 `IReadOnlyDictionary<K,V>`，但运行期对象就是那个私有的 `Dictionary<K,V>`，所以 `(IDictionary)tb.DataMap` 能成功，且能通过 `ConfigTable` 的字典校验。代价是**依赖了 Luban 的内部实现**（哪版把 `_dataMap` 换成别的实现就失效）——失效形态是响亮的（构造期抛 `ConfigException`），不会静默错。要用就在接缝文件旁边写一行注释说明这个依赖。

**IO 接线**：`Tables` 的 loader 是**同步**委托，而框架的 `AssetManager` / `FileManager` 全是异步。正确顺序是先把字节读全进内存，再让 loader 只从内存里取。整段解析是同步 CPU 工作——大表放主线程会掉帧；要挪到线程池，记住**解析回到主线程后才能注册**（见包内 README 的「线程契约」）。

### 4.3 本目录不提供生成脚本

**结论：不建 `Tools/luban/`。** 理由：

- 接缝是 §3.2 那三行普通 C#，没有「模板文件」这种产物要放；本文件里的代码块就是全部。
- 生成命令行只有两行，而 `<LUBAN>` / `<CONF_DIR>` / 输出目录**逐项目不同**，写死一份脚本只会腐化。
- 本仓没有 CI，脚本不会被任何东西执行到——留一个「永远没人跑」的脚本，比不写更糟。

将来若要做**漂移探测**（Luban 发新版本时确认 `partial` 还在、`DataMap` 还是 `Dictionary`），正确的形状是**一次性探针**：在仓库外跑一次最小 schema，断言观察结果，跑完即弃——不要留脚本。

---

## 五、边界

- **不重建内建适配层**。理由、沿革与重开判据见 `Documentation/Modules/Config.md` 的「已评估未采纳」。
- **不做 Editor 一键导表**。它会往包里引入「调用外部进程」这条零先例的路径，且把 Luban 的安装位置、schema 位置、输出目录这些逐项目的东西固化进框架。同取向先例见 `Roadmap.md` 对 HybridCLR 的裁定（「多半该是 Editor 工具 + 文档，而不是 Runtime 模块」）。结论落在**文档**这一侧。
- **包内 README 不指向本文件**。规则「包内不指向包外」：第三方装了包却打不开仓库。使用方需要的东西已经在包内 README 里写全了（§一是它的摘要）。
