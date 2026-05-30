# XSerialize

序列化抽象层，解耦 `DataManager` 与具体序列化格式。

## 架构

```
XSerialize/
├── ISerializer.cs          # 序列化器接口（同步，字节数组输出）
├── Serializer.cs           # 静态门面，管理已注册的序列化器（字典索引）
├── JsonSerializer.cs       # 内置默认实现（封装 Unity JsonUtility）
└── README.md               # 本文件
```

- `DataManager` 通过 `Serializer.Get(format)` 获取序列化器，不再硬编码 `JsonUtility`。
- `DataSnapshot` 顶层仍为 JsonUtility 兼容格式（`DataBlockSnapshot.data` 用 Base64 存储原始字节）。
- 第三方可实现 `ISerializer` 并调用 `Serializer.Register()` 扩展自定义格式。

## 内置默认

| Format | 实现             | 依赖              |
| ------ | ---------------- | ----------------- |
| `json` | `JsonSerializer` | Unity JsonUtility |

框架初始化时自动注册 `JsonSerializer`，无需手动操作。

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
