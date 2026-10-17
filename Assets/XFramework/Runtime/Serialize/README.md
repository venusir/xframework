# Serialize —— 按格式名取用的序列化注册表，解耦 Data / Save 与具体序列化实现

## 概述

命名空间 `XFramework.XSerialize`。序列化抽象层，解耦 `DataManager` 与具体序列化格式。

**它解决的是「换格式不用改调用方」**：`Data` / `Save` 只认 `ISerializer` 这个接口与一个格式名，具体实现（Newtonsoft / Unity JsonUtility / 第三方）由注册表决定。开箱已注册两个格式，见「API 参考」的「内置默认」。

## 快速使用

```csharp
// 默认序列化器（format = "json"，基于 Newtonsoft）——框架进入播放时已自动注册
ISerializer json = Serializer.Default;

byte[] bytes = json.Serialize(myObject, typeof(MyType));
MyType back = (MyType)json.Deserialize(bytes, typeof(MyType));

// 按格式名取用；未注册的格式抛 KeyNotFoundException，探测用 TryGet
ISerializer legacy = Serializer.Get("json-utility");
```

接入 `Data` / `Save` 时**不需要直接调用本模块**——它们自己通过格式名走注册表。直接用它只有两种场合：**扩展一种新格式**（见「自定义实现」），或**自己做一次格式无关的序列化**。

## 模块结构

```text
Runtime/Serialize/
├── Serializer.cs           # 静态门面：注册表（Dictionary<格式名, ISerializer>）+ AutoInit
├── ISerializer.cs          # 序列化器接口：Format + Serialize / Deserialize（同步、字节数组进出）
├── NewtonsoftSerializer.cs # 内置默认实现（format = "json"）
└── JsonSerializer.cs       # 遗留格式实现（format = "json-utility"，封装 Unity JsonUtility）
```

四个文件都是公开面，本模块没有 `internal` 类型。

## 架构设计

- `DataManager` 通过 `Serializer.Get(format)` 获取序列化器，不再硬编码 `JsonUtility`。
- `DataSnapshot` 顶层仍为 JsonUtility 兼容格式（`DataBlockSnapshot.data` 用 Base64 存储原始字节）。
- 第三方可实现 `ISerializer` 并调用 `Serializer.Register()` 扩展自定义格式。

## API 参考

### 内置默认

框架初始化时（`[RuntimeInitializeOnLoadMethod]`，见 `Serializer.AutoInit`）自动注册两个：

| Format | 实现 | 依赖 | 说明 |
| ------ | ---- | ---- | ---- |
| `json` | `NewtonsoftSerializer` | Newtonsoft.Json | **默认**——`Serializer.Default` 取的就是它 |
| `json-utility` | `JsonSerializer` | Unity JsonUtility | 遗留格式，保留用于读写旧版本写出的存档 |

> Unity 自带的 `JsonUtility` 不支持字典、多态与 `null` 语义，故默认实现是 Newtonsoft；`json-utility`
> 只作兼容保留，新数据请用 `json`。

### 注册表成员

| 成员 | 说明 |
| ---- | ---- |
| `Get(format)` | 取指定格式的序列化器；**未注册时抛 `KeyNotFoundException`** |
| `TryGet(format, out serializer)` | 同上但不抛（未注册返回 `false`）——探测用；`format` 为 `null` 仍抛 `ArgumentNullException`（SER-004） |
| `Default` | 默认序列化器（`format = "json"`）；未初始化时抛 `InvalidOperationException` |
| `Register(serializer)` | 按 `serializer.Format` 注册；**同名覆盖** |
| `Unregister(format)` | 摘除指定格式；`format` 为 `null` 抛 `ArgumentNullException` |
| `IsInitialized` | 模块是否已初始化 |
| `Initialize()` | 注册内置两个（幂等，框架已自动调用） |
| `Shutdown()` | 清空全部注册并把模块复位为未初始化 |

## 自定义实现

本模块的实现方入口：实现并注册序列化器。

- **实现哪个契约**：`ISerializer`（`Format` 字符串 + 同步的 `Serialize` / `Deserialize`）。
- **怎么接线**：`Serializer.Register(serializer)` / `Unregister(format)`；替换内置格式须在模块初始化**之前**注册同名格式。
- **要守什么**：格式名是跨模块契约（`DataSnapshot.defaultFormat` / `DataBlockSnapshot.format` 用它）；替换 `"json"` 是全局动作，且必须早于初始化；未注册的格式 `Get` 抛 `KeyNotFoundException`，未初始化取 `Default` 抛 `InvalidOperationException`（见「已知限制」）。

实现 `ISerializer`，然后注册：

```csharp
Serializer.Register(new MyMessagePackSerializer());
```

> **替换内置格式**（如用自定义实现接管 `"json"`）：在模块初始化**之前**注册同名格式即可——
> `Initialize` 遇到已存在的同名注册会**保留你的**并打一条 `[Serialize]` 告警，不再静默覆盖。
> 想恢复内置实现请 `Unregister("json")` 后重新 `Register(new NewtonsoftSerializer())`。
> （`Register` 本身的「同名覆盖」不变——那是显式动作。）

### MessagePack 集成示例（可选）

1. 通过 Unity Package Manager Git URL 安装 MessagePack-CSharp：
   ```
   https://github.com/Cysharp/MessagePack-CSharp.git?path=src/MessagePack.UnityClient/Assets/Scripts/MessagePack
   ```

2. 实现序列化器：

```csharp
public sealed class MessagePackSerializer : ISerializer
{
    public string Format => "msgpack";

    public byte[] Serialize(object obj, Type type)
    {
        return MessagePack.MessagePackSerializer.Serialize(type, obj);
    }

    public object Deserialize(byte[] data, Type type)
    {
        return MessagePack.MessagePackSerializer.Deserialize(type, data);
    }
}
```

3. 注册到框架：

```csharp
Serializer.Register(new MessagePackSerializer());
```

4. 设置 `DataSnapshot.defaultFormat = "msgpack"` 或单个 `DataBlockSnapshot.format` 即可切换格式。

## 线程契约

**注册表本身没有任何同步原语，也没有主线程断言**——`Get` / `TryGet` / `Register` / `Unregister` / `Shutdown` 直接读写同一个静态字典，从别的线程并发调用即数据竞争。与框架其余静态门面同一口径：**一律在主线程调用**。

`ISerializer` 的实现不涉及 I/O、也没有等待点（接口注释原文：「序列化本质为同步 CPU 操作」），框架**不在内部切线程**——谁调用 `Serializer.Get(...).Serialize(...)`，序列化就在谁的线程上跑。

## 已知限制

知道边界比以为没有边界安全。以下都是刻意的取舍或跨模块的能力缺口，**不是待修的缺陷**：

- **`json-utility` 只为读旧数据保留。** Unity 自带的 `JsonUtility` 不支持字典、多态与 `null` 语义，
  用它写出的数据会**静默丢字段**（落默认值，不报错）——新数据请一律用 `json`。
- **覆盖 `"json"` 是个全局动作。** `Data` 与 `Save` 都走 `Serializer.Default`，换掉它等于同时改变
  两者的落盘格式；已经写出去的旧存档不会跟着迁移，要自己保证新实现读得懂旧数据。
- **替换内置格式：`Register` 总是静默覆盖，内置注册只在 `Initialize` 时让位于使用方。** 任何时候调用
  `Register` 都会覆盖同名的现有注册，**没有告警**；内置实现只在 `Initialize` / `AutoInit` 注册时检查同名条目——
  若使用方已先注册，则**保留使用方的**并打一条 `[Serialize]` 告警。想恢复内置请 `Unregister("json")`
  后重新 `Register` 内置类型。
- **格式名是跨模块契约。** `DataSnapshot.defaultFormat` / `DataBlockSnapshot.format` 里存的是字符串，
  改名或注销会连带影响 Data / Save 的读写。
- **`Get` / `Default` / `Shutdown` 三处的失败都是抛异常。** 未注册的格式抛 `KeyNotFoundException`，
  未初始化时取 `Default` 抛 `InvalidOperationException`（框架经 `AutoInit` 自动初始化，正常不会遇到）；
  `Shutdown()` 之后连内置格式都没有，两个入口全抛。
