using System;
using System.Collections.Generic;

namespace XFramework.XData
{
    /// <summary>
    /// 数据快照结构（JSON 兼容，可直接由 Unity JsonUtility 序列化）。
    /// <para>每个 <see cref="IDataBlock"/> 通过 <see cref="DataBlockSnapshot"/> 持久化。</para>
    /// <para>实际数据块的序列化/反序列化委托给 <see cref="XSerialize.Serializer"/>，
    /// 通过 <see cref="DataBlockSnapshot.format"/> 指定序列化格式，默认使用 <see cref="DataSnapshot.defaultFormat"/>。</para>
    /// </summary>
    [Serializable]
    public sealed class DataSnapshot
    {
        /// <summary>存档格式版本号，用于向前兼容。</summary>
        public string version;

        /// <summary>存档时间戳（ISO 8601）。</summary>
        public string timestamp;

        /// <summary>
        /// 存档级默认序列化格式，对应 <see cref="XSerialize.ISerializer.Format"/>。
        /// <para>当 <see cref="DataBlockSnapshot.format"/> 为空时使用此值，默认 "json"。</para>
        /// </summary>
        public string defaultFormat;

        /// <summary>数据块快照列表。</summary>
        public List<DataBlockSnapshot> blocks = new();
    }

    /// <summary>
    /// 单个 <see cref="IDataBlock"/> 的序列化快照。
    /// </summary>
    [Serializable]
    public sealed class DataBlockSnapshot
    {
        /// <summary>
        /// 数据块名称，对应 <see cref="IDataBlock.BlockName"/>。
        /// <para>替代 AssemblyQualifiedName，读档时直接通过名称索引已注册的 block，避免反射。</para>
        /// </summary>
        public string blockName;

        /// <summary>
        /// 序列化后的数据（Base64 编码的字节数组）。
        /// <para>由 <see cref="XSerialize.ISerializer.Serialize"/> 生成的原始字节经 Base64 编码后存入，
        /// 兼容 JsonUtility 外层容器。</para>
        /// </summary>
        public string data;

        /// <summary>
        /// 该数据块使用的序列化格式，对应 <see cref="XSerialize.ISerializer.Format"/>。
        /// <para>为 null 或空时使用 <see cref="DataSnapshot.defaultFormat"/>。</para>
        /// </summary>
        public string format;
    }
}