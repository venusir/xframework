# XSerialize

序列化抽象层，解耦 `DataManager` 与具体序列化格式。

## 架构

- `DataManager` 通过 `Serializer.Get(format)` 获取序列化器，不再硬编码 `JsonUtility`。
- `DataSnapshot` 顶层仍为 JsonUtility 兼容格式（`DataBlockSnapshot.data` 用 Base64 存储原始字节）。
- 第三方可实现 `ISerializer` 并调用 `Serializer.Register()` 扩展自定义格式。

## 内置默认

框架初始化时（`[RuntimeInitializeOnLoadMethod]`，见 `Serializer.AutoInit`）自动注册两个：

| Format | 实现 | 依赖 | 说明 |
| ------ | ---- | ---- | ---- |
| `json` | `NewtonsoftSerializer` | Newtonsoft.Json | **默认**——`Serializer.Default` 取的就是它 |
| `json-utility` | `JsonSerializer` | Unity JsonUtility | 遗留格式，保留用于读写旧版本写出的存档 |

> Unity 自带的 `JsonUtility` 不支持字典、多态与 `null` 语义，故默认实现是 Newtonsoft；`json-utility`
> 只作兼容保留，新数据请用 `json`。

## API 参考

| 成员 | 说明 |
| ---- | ---- |
| `Get(format)` | 取指定格式的序列化器；**未注册时抛 `KeyNotFoundException`** |
| `TryGet(format, out serializer)` | 同上但不抛——探测用 |
| `Default` | 默认序列化器（`format = "json"`）；未初始化时抛 `InvalidOperationException` |
| `Register(serializer)` | 按 `serializer.Format` 注册；**同名覆盖** |
| `Unregister(format)` | 摘除指定格式 |
| `IsInitialized` | 模块是否已初始化 |
| `Initialize()` | 注册内置两个（幂等，框架已自动调用） |
| `Shutdown()` | 清空全部注册并把模块复位为未初始化 |

## 第三方扩展

实现 `ISerializer`，然后注册：

```csharp
Serializer.Register(new MyMessagePackSerializer());
```

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
