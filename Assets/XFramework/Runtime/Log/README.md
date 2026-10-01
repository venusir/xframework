# XFramework.XLog — 日志

分级、分类过滤、模板化调用与可插拔输出端的日志模块。

## 概述

它比 Unity 自带的 `Debug.unityLogger` + 自定义 `ILogHandler` 多给四样东西（前三样是立项时的门槛，见 `Documentation/Roadmap.md` §2.2 的历史记录）：

1. **按分类过滤**：Unity 只有全局开关，没有「只关 Save 的 Debug」。这里每个分类可以单独设档位。
2. **零 GC 调用路径**：`Debug.Log($"x {a}")` 无论开不开都必然分配；这里**未启用就不格式化**（有测试锁定）。
3. **前缀不再靠人手抄**：`[模块]` 由分类渲染层补，约定从文档纪律变成代码保证。
4. **结构化落盘**：每条日志一行 JSONL，把引擎/第三方/未捕获异常也收进同一时间线，供 AI 分析。

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

- **写日志**：`Verbose / Debug / Info / Warning / Error / Fatal`，每档 4 个重载（现成字符串、模板 + 1/2/3 参）；通用入口 `Log(level, category, …)`；异常专用 `Exception(category, exception, message = null)`（级别固定 `Error`，正文默认取 `Exception.Message`）。
- **档位**：`MinimumLevel`、`SetCategoryLevel`、`ResetCategoryLevel`、`IsEnabled`。
- **分类**：内置见 `LogCategories`（33 个，与迁移前的 `[前缀]` 一一对应）；自定义用 `LogCategory.Get("MyTag")`（幂等、任意线程安全）。`default(LogCategory)` 合法，呈现为 `[Unregistered]`。
- **输出端**：`AddSink(ILogSink)` / `RemoveSink` / `Flush` / `DroppedSinkCount`。
- **生命周期**：`Initialize(impl)`（注入自定义后端）、`Configure(options)`、`Shutdown()`、`Flush()`。

`ILogSink` 是第三方扩展点：实现 `Write(in LogEntry)` 与 `Flush()`，**任意线程调用、不得抛异常**（抛出的 sink 会被静默摘除并计入 `DroppedSinkCount`——「因为日志坏了而记一条日志」会递归）。实现 `IDisposable` 时框架在摘除/关闭时释放它。

## 输出端

- **控制台**（`ConsoleLogSink`，默认开）：渲染成 `[Category] message` 后交给 `Debug.Log/LogWarning/LogError`。渲染文本与 LogType 映射**与迁移前手抄前缀的写法逐字一致**——这是 193 处迁移能靠「测试全绿」验证的前提。
- **全量捕获**（`CaptureUnityLogs`，默认与文件输出端同开关）：挂 `Application.logMessageReceivedThreaded`，把**引擎、第三方库、未捕获异常**的日志也收进同一份文件，每条标 `src:"unity"`；框架自己的日志标 `src:"fw"`，不会因回显被写两遍。外部日志走同一套档位过滤（`MinimumLevel` 与分类覆盖都适用），分类从 `[标签]` 前缀解析（解析不出呈现为 `Unregistered`）。回调在 `AutoInit` 时**无条件挂上**——没有可写输出端时它第一行即返回，因此不存在「第一次 `LogManager` 调用之前发生的日志捕获不到」的时序陷阱。
- **JSONL 文件**（`LogFileSink`，默认 Editor/Development 开、Release 关）：每条日志一行 JSON，落在 `{persistentDataPath}/XLog/`，**每会话一个文件**（`xlog-{时间}-{会话id}-p{n}.jsonl`）。超 32 MiB 切分片，每个分片首行都重写会话头；目录内保留最新 10 个文件。**运行时即可读**：写入端持有共享读的写句柄，读取方需自行声明共享写（.NET 里是 `FileShare.ReadWrite`；`jq` / `grep` 这类经 CRT 打开文件的工具默认即可）。**Warning 及以上立即落盘**，其余每 64 条批量落盘，`Shutdown` 与 `Application.quitting` 时冲刷——崩溃后要能读到现场。文件写入失败（磁盘满、目录不可写）时该输出端**静默停用**：不记日志（会递归）、不抛异常、不影响控制台通路。

## 线程契约

- `LogManager` 的日志调用**任意线程可用**（序号用 `Interlocked` 分配，并发后仍可靠 `LogEntry.Sequence` 定序）。这一条继承自 `Debug.Log` 的既有用法，不是本模块新增的承诺。
- 非主线程的条目 `Frame` 取最近一次主线程读到的值（不跨线程触碰 Unity API）。
- 自定义 sink 需自行保证线程安全。

## 性能代价

- **未启用路径**：零分配、零格式化（`Tests/Runtime/Log/LogAllocationTests` 锁定）。
- **启用路径**：一次格式化字符串 +（Error 及以上、且开启抓栈时）一次托管堆栈。泛型重载避免的是 `params object[]` 数组分配；值类型参数在启用路径仍会装箱——这是刻意的取舍（3 参以内不分配数组，4 参起请先拼好字符串）。
- 含**字面花括号**的消息（如 JSON 片段）请走「现成字符串」重载，或按 `string.Format` 规则写成 `{{` `}}`；模板解析失败时会退回原文，绝不抛。

## 已知限制

- 控制台里 Error 的调用点堆栈比迁移前**深了几帧**（多了日志门面与 sink 的内部帧）——栈没丢，只是多了中间层。
- `Fatal` 与 `Error` 在控制台**同为 `LogError`**（Unity 没有第五种 LogType）；文件端会区分。

## 设计取舍

- **为什么叫 `ILogManager` 而不是 `ILogger`**：后者已被 `UnityEngine.ILogger` 占用。
- **为什么不替换 `Debug.unityLogger.logHandler`**：全局副作用大（Unity Console、测试框架的 `LogAssert`、第三方工具都挂在上面），且 `ILogHandler.LogFormat(…, params object[])` 自身就分配数组。
- **为什么不做编译期剥离（`[Conditional]`）**：现有日志 175/193 是 Warning/Error，Release 也必须输出；且 `[Conditional]` 会连实参求值一起删掉，与「按档位过滤」语义不同。
- **为什么 `LogManager` 永不抛**：日志是错误路径的最后一张面孔——让「模块没初始化」把它升级成二次故障，代价比一致性大得多。
