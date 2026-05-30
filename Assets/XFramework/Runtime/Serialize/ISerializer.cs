using System;

namespace XFramework.XSerialize
{
    /// <summary>
    /// 序列化器接口，用于将数据块或存档元数据序列化为字节数组。
    /// <para>序列化本质为同步 CPU 操作，不涉及 I/O，故不提供异步版本。</para>
    /// <para>内置默认实现：<see cref="JsonSerializer"/>（封装 Unity JsonUtility）。</para>
    /// <para>第三方可实现此接口并注册到 <see cref="Serializer"/>，扩展自定义格式（如 MessagePack、Protobuf）。</para>
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// 序列化格式标识，例如 "json", "msgpack", "protobuf"。
        /// <para>框架通过此标识查找对应的序列化器。</para>
        /// </summary>
        string Format { get; }

        /// <summary>
        /// 将对象序列化为字节数组。
        /// </summary>
        /// <param name="obj">待序列化的对象，不可为 null。</param>
        /// <param name="type">对象类型。</param>
        /// <returns>序列化后的字节数组。</returns>
        byte[] Serialize(object obj, Type type);

        /// <summary>
        /// 将字节数组反序列化为指定类型的对象。
        /// </summary>
        /// <param name="data">序列化后的字节数组。</param>
        /// <param name="type">目标类型。</param>
        /// <returns>反序列化后的对象。</returns>
        object Deserialize(byte[] data, Type type);
    }
}
