# XFramework.XLog — 日志

分级、分类过滤、模板化调用、可插拔输出端，以及一份**给 AI 读的**结构化时间线。

## 概述

它比 Unity 自带的 `Debug.unityLogger` + 自定义 `ILogHandler` 多给四样东西（前三样是本模块立项时的门槛——答不出「多给什么」就不该建）：

1. **按分类过滤**：Unity 只有全局开关，没有「只关 Save 的 Debug」。这里每个分类可以单独设档位。
2. **零 GC 调用路径**：`Debug.Log($"x {a}")` 无论开不开都必然分配；这里**未启用就不格式化**（有测试锁定）。
3. **前缀不再靠人手抄**：`[模块]` 由分类渲染层补，约定从文档纪律变成代码保证。
4. **结构化落盘 + 全量捕获**：每条日志一行 JSONL，引擎 / 第三方 / 未捕获异常也收进同一条时间线。

## 快速使用

```csharp
using XFramework.XLog;

// 分类从 LogCategories 取（框架内置全表），前缀由它渲染
LogManager.Warning(LogCategories.Save, "跳过空存档文件: {0}", path);
LogManager.Info(LogCategories.Pipeline, "Stage '{0}' start", stage.Name);

// 控制台输出：[Save] 跳过空存档文件: slot/1.sav
```

**写模板 + 参数，不要先插值**。`LogManager.Warning(cat, "x {0}", a)` 在档位未启用时**不格式化**、零分配；`LogManager.Warning(cat, $"x {a}")` 则无论开不开都已经分配过了。参数仍在调用方求值（与插值一致）——代价高的先用 `LogManager.IsEnabled(level, category)` 探测。

## 分级与过滤

六档，数值越大越严重，**比较即过滤**：

| 级别 | 控制台 LogType | 默认 |
|---|---|---|
| `Verbose` | `Log` | 全环境关闭（需要时手动开） |
| `Debug` | `Log` | Editor / Development 开 |
| `Info` | `Log` | 全环境开 |
| `Warning` | `Warning` | 全环境开 |
| `Error` | `Error` | 全环境开 |
| `Fatal` | `Error` | 全环境开 |
| `Off` | — | **仅作阈值**（静默某分类），不是可写入的级别 |

默认全局档位：**Editor / Development 为 `Debug`，Release 为 `Info`**（Release 不得更低——迁移前的 `Debug.Log` 映射到 `Info`，低于它就会静默改变既有可见性）。

```csharp
// 只关掉某个模块的诊断日志
LogManager.SetCategoryLevel(LogCategories.UpdateScheduler, LogLevel.Warning);
LogManager.ResetCategoryLevel(LogCategories.UpdateScheduler);   // 还原为跟随全局
```

## API 参考

- **写日志**：`Verbose / Debug / Info / Warning / Error / Fatal`，每档 4 个重载（现成字符串、模板 + 1/2/3 参）；通用入口 `Log(level, category, …)`。
- **异常**：`Exception(category, exception, message = null)`（级别固定 `Error`）与 `Exception(level, category, exception, message = null)`（级别可指定——「跳过一条损坏的存档」这种可恢复失败用 `Warning`）。两者都把异常对象送进 JSONL 的 `exc` 字段；`exception` 为 null 时什么都不做；**正文不要重复异常文本**（类型、消息与堆栈由渲染层补）。
- **档位**：`MinimumLevel`、`SetCategoryLevel`、`ResetCategoryLevel`、`IsEnabled`。
- **分类**：内置见 `LogCategories`（31 个，与迁移前的 `[前缀]` 一一对应）；自定义用 `LogCategory.Get("MyTag")`（幂等、任意线程安全）。`default(LogCategory)` 合法，呈现为 `[Unregistered]`。
- **输出端**：`AddSink(ILogSink)` / `RemoveSink` / `Flush` / `DroppedSinkCount`。
- **回读（诊断）**：`CopyCategories(List<LogCategoryInfo>)` / `CopySinks(List<ILogSink>)` / `SinkCount`。回答「这条分类现在会不会输出、为什么」（`LogCategoryInfo.EffectiveLevel` 在调用时现算：设了覆盖取覆盖，否则取当前全局档）与「谁在收日志」。分类总数用 `LogCategory.RegisteredCount`。
  > **只对内置实现成立**：注入第三方 `ILogManager` 后这三个成员一律返回空/0——那类实现不提供回读。它们**刻意不进 `ILogManager`**（给主接口加成员会让所有实现者编译不过）。
- **生命周期**：`Initialize(impl)`（注入自定义后端）、`Configure(options)`、`Shutdown()`、`Flush()`。

`ILogSink` 是第三方扩展点：实现 `Write(in LogEntry)` 与 `Flush()`，**任意线程调用、不得抛异常**（抛出的 sink 会被静默摘除并计入 `DroppedSinkCount`——「因为日志坏了而记一条日志」会递归）。实现 `IDisposable` 时框架在摘除/关闭时释放它。分发**不持锁**（sink 数组是 copy-on-write 快照），因此 sink 在自己的 `Write` 里再写日志是安全的。

## 输出端

- **控制台**（`ConsoleLogSink`，默认开）：渲染成 `[Category] message` 后交给 `Debug.Log/LogWarning/LogError`。渲染文本与 LogType 映射**与迁移前手抄前缀的写法逐字一致**——这是 193 处迁移能靠「测试全绿」验证的前提。
- **全量捕获**（`CaptureUnityLogs`，默认与文件输出端同开关）：挂 `Application.logMessageReceivedThreaded`，把**引擎、第三方库、未捕获异常**的日志也收进同一份文件，每条标 `src:"unity"`；框架自己的日志标 `src:"fw"`，不会因回显被写两遍。外部日志走同一套档位过滤（`MinimumLevel` 与分类覆盖都适用），分类从 `[标签]` 前缀解析（解析不出呈现为 `Unregistered`）。回调在 `AutoInit` 时**无条件挂上**——没有可写输出端时它第一行即返回，因此不存在「第一次 `LogManager` 调用之前发生的日志捕获不到」的时序陷阱。
- **JSONL 文件**（`LogFileSink`，默认 Editor/Development 开、Release 关）：每条日志一行 JSON，落在 `{persistentDataPath}/XLog/`，**每会话一个文件**（`xlog-{时间}-{会话id}-p{n}.jsonl`）。超 32 MiB 切分片，每个分片首行都重写会话头；目录内保留最新 10 个文件。**运行时即可读**：写入端持有共享读的写句柄，读取方需自行声明共享写（.NET 里是 `FileShare.ReadWrite`；`jq` / `grep` 这类经 CRT 打开文件的工具默认即可）。**Warning 及以上立即落盘**，其余每 64 条批量落盘，`Shutdown` 与 `Application.quitting` 时冲刷——崩溃后要能读到现场。文件写入失败（磁盘满、目录不可写）时该输出端**静默停用**：不记日志（会递归）、不抛异常、不影响控制台通路。

## 配置（`LogOptions`）

不配置也能用（门面按 `LogOptions.Default` 懒建实现）；要改就一次性交给 `LogManager.Configure`：

```csharp
LogManager.Configure(new LogOptions
{
    MinimumLevel  = LogLevel.Info,           // 全局最低档（Release 默认就是 Info）
    EnableFileSink = true,                   // Release 里显式打开文件输出
    FileDirectory = @"D:\MyGameLogs",        // 换落盘目录
    MaxRetainedFiles = 20,                   // 目录内保留份数
    ImmediateFlushMinLevel = LogLevel.Error, // 只让 Error 及以上立即落盘
});

// 配置之后再追加自定义输出端（Configure 会重建实现，旧的 sink 会被释放）
LogManager.AddSink(new MyRemoteSink());
```

| 字段 | 默认值 | 说明 |
|---|---|---|
| `MinimumLevel` | Editor/Development=`Debug`；其余=`Info` | 全局最低档（分类未设覆盖时按它判定） |
| `EnableConsoleSink` | `true` | 控制台输出端；**关掉它 `LogAssert` 就看不到任何东西** |
| `CaptureStackTrace` | Editor/Development=`true`；Release=`false` | 是否抓调用点托管堆栈 |
| `StackTraceMinLevel` | `Error` | 抓栈的最低级别 |
| `EnableFileSink` | Editor/Development=`true`；Release=`false` | JSONL 文件输出端 |
| `CaptureUnityLogs` | 跟随 `EnableFileSink` | 是否把引擎 / 第三方 / 未捕获异常收进同一份文件 |
| `FileDirectory` | `{persistentDataPath}/XLog`（取不到时回退临时目录） | 落盘目录 |
| `MaxFileBytes` | `32 * 1024 * 1024`（32 MiB） | 单个分片文件的上限，超过则切 `-p2` |
| `MaxRetainedFiles` | `10` | 目录内保留的最新文件数（启动时轮转） |
| `ImmediateFlushMinLevel` | `Warning` | 达到该级别即立即 flush（崩溃可读性的下限） |
| `FlushEveryEntries` | `64` | 低于立即 flush 门槛的条目按条数批量 flush |

> **`Configure` 是「重建实现」**：它会复位分类档位（`SetCategoryLevel` 的覆盖）、释放此前
> `AddSink` 的自定义输出端，并把整个实现换成按新配置构建的一份。所以顺序是
> **先 `Configure`、后 `SetCategoryLevel` / `AddSink`**。

## JSONL schema

固定键序，**可选字段整体省略**（不写 `null`）；一行一条，行内绝不出现裸换行。

### 会话头行（`t = "session"`，每个分片文件的首行）

| 键 | 类型 | 说明 |
|---|---|---|
| `t` | string | 固定 `"session"`——行类型判别符 |
| `v` | int | schema 版本，当前 `1` |
| `session` | string | 8 位十六进制，进程内唯一；**每次运行重新生成** |
| `ts` | string | 会话起始的 ISO8601 UTC 毫秒（`2026-10-01T11:30:12.345Z`） |
| `up` | int | 自会话开始的毫秒（单调时钟，不受系统时间调整影响） |
| `unity` / `product` / `company` / `platform` | string | 环境快照（引擎版本、产品名、公司名、平台） |
| `dev` / `editor` | bool | 是否 Development 构建 / 是否编辑器 |
| `lvl` | string | 配置的全局档位 token |
| `part` | int | 分片号，从 1 起 |
| `culture` | string | 主线程 culture（解释 `F2` 这类格式说明符的落点） |

### 日志行（`t = "log"`，键序固定）

| 键 | 类型 | 说明 |
|---|---|---|
| `t` | string | 固定 `"log"` |
| `session` | string | 与会话头一致 |
| `seq` | long | 会话内单调递增（跨线程用 `Interlocked`）——**并发下唯一可靠的定序键** |
| `ts` | string | ISO8601 UTC 毫秒 |
| `up` | int | 自会话开始的毫秒 |
| `lvl` | string | `verbose` / `debug` / `info` / `warning` / `error` / `fatal`——全小写自描述 |
| `cat` | string | 分类名（不含方括号）；`src:"unity"` 时从 condition 的 `[标签]` 解析，解析不出为 `Unregistered` |
| `msg` | string | **框架条目**：已格式化正文（不含 `[Category] ` 前缀，前缀可从 `cat` 重建）；**捕获条目**：Unity 的 condition 原文（不裁剪） |
| `frame` / `thread` | int | 主线程帧号 / 托管线程 id |
| `src` | string | `fw`（框架自产）\| `unity`（从 Unity 日志系统捕获） |
| `exc` | string? | 异常条目：`exception.ToString()` |
| `stack` | string? | 调用点堆栈（框架条目）或 Unity 附带的堆栈（捕获条目） |

转义：`"` `\` `\n` `\r` `\t` 与全部 `<0x20` 控制符；**中文原样保留**（文件是 UTF-8，转义成 `\uXXXX` 只会让人读不懂、AI 读更慢）。

> **`exc` 与 `stack` 不是一回事，别混读**：`exc` 是**异常自己**的 `ToString()`（类型、异常链、抛出点堆栈），只在调用点手上有异常对象时才有值；`stack` 是**日志调用点**的托管堆栈（门面 → sink → 业务），Error 及以上默认抓。想知道「异常从哪儿抛的」看 `exc`；想知道「谁记的这条日志」看 `stack`。

## 用 AI 分析日志

```bash
# 报错速览（先按会话切片：多份日志混在一起时）
jq -r 'select(.t=="log" and .lvl=="error") | "\(.ts) [\(.cat)] \(.msg)"' xlog-*.jsonl

# 引擎 / 第三方 / 未捕获异常（框架日志之外的全部现场）
jq -c 'select(.src=="unity")' xlog-*.jsonl | head -50

# 谁在刷屏
jq -r 'select(.t=="log") | .cat' xlog-*.jsonl | sort | uniq -c | sort -rn | head

# 崩溃现场：取错误的堆栈
jq -r 'select(.lvl=="error" or .lvl=="fatal") | .stack // empty' xlog-*.jsonl

# 结构化异常：每条异常的首行（类型 + 消息），以及哪些分类在产出异常
jq -r 'select(.exc) | "\(.cat): \(.exc | split("\n")[0])"' xlog-*.jsonl
jq -r 'select(.exc) | .cat' xlog-*.jsonl | sort | uniq -c | sort -rn
```

三条纪律：

1. **先按 `session` 切片**——同一目录里的文件可能来自多次运行。
2. **排序用 `seq`，不要用 `ts`**——`ts` 是墙钟，并发写入可能落在同一毫秒。
3. **`frame` 定位「崩在第几帧」**，`up`（自会话起的毫秒）定位「启动后多久」。

## 线程契约

- `LogManager` 的日志调用**任意线程可用**（`seq` 用 `Interlocked` 分配，并发后仍能靠 `LogEntry.Sequence` 定序）。这一条继承自 `Debug.Log` 的既有用法，不是本模块新增的承诺。
- 非主线程的条目 `Frame` 取最近一次主线程读到的值（不跨线程触碰 Unity API）。
- 自定义 sink 需自行保证线程安全；框架分发时不持锁，sink 内再写日志是安全的（可重入）。
- 捕获回调在**调用线程**上同步触发（实测），框架自己的输出经线程本地的回显深度去重。

## 性能代价

- **未启用路径**：零分配、零格式化（`Tests/Runtime/Log/LogAllocationTests` 锁定，含「参数照常求值」这条反向契约）。
- **启用路径**：一次格式化字符串 + 一条 JSON 行；泛型重载避免的是 `params object[]` 数组分配，值类型参数仍会装箱（3 参是不分配数组的上限，4 参起请先拼好字符串）。
- **抓栈**（`CaptureStackTrace`，默认 Editor/Development 开、门槛 `Error`）：每次达标日志一次托管堆栈——错误路径才付，且可关。
- 文件写入在**调用线程上同步完成**（加锁 + 缓冲），Warning 及以上立即 flush。日志不在每帧路径上，这是用「崩溃可读性」换来的取舍。

## 已知限制

- 控制台里 Error 的调用点堆栈比迁移前**深了几帧**（多了日志门面与 sink 的内部帧）——栈没丢，只是多了中间层；精确调用点在 JSONL 的 `stack` 里。
- `Fatal` 与 `Error` 在控制台**同为 `LogError`**（Unity 没有第五种 LogType）；文件里以 `lvl` 区分。
- Release 默认**不写文件**（`EnableFileSink` 默认随 Editor/Development），需要现场日志时显式开。
- 分类名与迁移前的 `[标签]` 是**逐字对应**的，所以同一模块内历史遗留的多个标签各是一个分类（`Config` 与 `ConfigManager`、`AssetManager` 与 `YooAssetManager` 都是两个）——按模块统一静音时要逐个设，或先改调用点。
- 外部日志的 `[标签]` 若从未注册过，会被 `LogCategory.Get` 顺带注册成一个分类（幂等）——捕获带标签的第三方日志有这一处副作用。
- 用 .NET 的 `File.ReadAllText` / `File.ReadAllLines` 读运行中的日志文件会撞共享冲突（它们声明的是 `FileShare.Read`），需显式传 `FileShare.ReadWrite`。
- `exc` **只在调用点手上有异常对象时**才有值：只拿到消息文本的降级路径（平台降级、取消这类本就没有异常的静默分支）不会有它。这不等于丢信息——那些路径本来就没有异常。
- `src:"unity"` 的捕获条目 `exc` **恒空**（Unity 的回调只给文本与堆栈）：异常文本在 `msg`（condition 原文）与 `stack` 里，用 `lvl` 与 `src` 组合筛选即可。
- **注入自定义 `ILogManager` 会停用 Unity 全量捕获**（引擎与第三方库的日志不再进任何输出端）——因为捕获只认内置实现（`CaptureTarget => _impl as LogManagerImpl`）。`LogManager.Initialize` 会在替换**之前**打一条 `[LogManager]` 警告提醒（这条警告本身经当前实现发出，因此不可能递归、也不经过你注入的实现）；需要保留捕获请改用 `LogManager.Configure(LogOptions)`。
- **JSONL 的文件命名不可配**：目录可改（`LogOptions.FileDirectory`），但 `xlog-{时间}-{会话}-p{n}.jsonl` 的命名是内置 sink 的私有常量——要换命名只能自行实现 `ILogSink`（或整体换 `ILogManager`）。

## 设计取舍

- **为什么叫 `ILogManager` 而不是 `ILogger`**：后者已被 `UnityEngine.ILogger` 占用。
- **为什么不替换 `Debug.unityLogger.logHandler`**：全局副作用大（Unity Console、测试框架的 `LogAssert`、第三方工具都挂在上面），且 `ILogHandler.LogFormat(…, params object[])` 自身就分配数组。`logMessageReceivedThreaded` 是只读旁路，代价小得多。
- **为什么不做编译期剥离（`[Conditional]`）**：现有日志 175/193 是 Warning/Error，Release 也必须输出；且 `[Conditional]` 会连实参求值一起删掉，与「按档位过滤」语义不同。
- **为什么 `LogManager` 永不抛**：日志是错误路径的最后一张面孔——让「模块没初始化」把它升级成二次故障，代价比一致性大得多。因此门面懒初始化、sink 违约只摘除、模板解析失败退回原文、IO 失败静默停用。
- **为什么模板参数止步 3 个**：`string.Format` 的 3 参重载不分配 `object[]`，第 4 个起掉进 `params` 重载——3 是「最大且仍零数组」的元数，不是随手定的。
- **为什么文件 sink 不开后台写线程**：后台队列会把最后几条留在内存里，而崩溃后要读的正是那几条。
- **为什么是 JSONL 而不是单个 JSON**：**日志的价值在崩溃现场，格式必须匹配「随时可能被打断」这个前提**——JSONL 每行自足，已 flush 的行永远可解析，最后一行哪怕写了一半也只坏那一行；单个 JSON 数组被打断就是 `]` 缺失、**整份语法无效**。推论三条：追加写零跨行状态（`Write(line)` 即可，不必维护 `[`/`]`/逗号与「是不是第一条」）；可流式消费（`tail -f` 边跑边看、`grep`/`head` 直接筛、逐行 parse 不必把整片读进内存）；每行自带 `session`/`seq`/`lvl`/`cat`，过滤后不丢上下文。**「一行一条」是硬保证**：写路径转义全部换行与控制符，`MessageWithLineBreaks_StaysOnOnePhysicalLine` 钉着它。
- **为什么不引第三方日志库（ZLogger / Serilog）**：ZLogger 的核心卖点是 `InterpolatedStringHandler`，需要 C# 10（本包 `LangVersion 9.0`）；且它们都不解决本包真正的问题（分类前缀约定 + 与 Unity Console / `LogAssert` 的逐字兼容）。

## 依赖

零模块依赖：只用 `System.*` 与 `UnityEngine`，写文件走 `System.IO` 直写（不经 `FileManager`——否则「FileManager 报错要记日志」就是循环）。
