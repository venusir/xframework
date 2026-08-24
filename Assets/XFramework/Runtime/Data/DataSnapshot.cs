using System;
using System.Collections.Generic;

namespace XFramework.XData
{
    /// <summary>
    /// 数据快照结构（JSON 兼容，可直接由 Unity JsonUtility 序列化）。
    /// <para>每个 <see cref="IDataBlock"/> 通过 <see cref="DataBlockSnapshot"/> 持久化。</para>
    /// <para>实际数据块的序列化/反序列化委托给 <see cref="XSerialize.Serializer"/>，
    /// 通过 <see cref="DataBlockSnapshot.format"/> 指定序列化格式，默认使用 <see cref="DataSnapshot.defaultFormat"/>。</para>
    /// <para>第三方可继承此类以扩展存档元数据，
    /// 并重写 <see cref="CreateMeta"/> 返回配对的 <see cref="XSave.SaveMeta"/> 子类。</para>
    /// </summary>
    [Serializable]
    public class DataSnapshot
    {
        /// <summary>
        /// 创建 <see cref="DataSnapshot"/> 实例的工厂委托。
        /// <para>第三方可替换此委托以返回自定义子类（如 <c>MySnapshot : DataSnapshot</c>），
        /// 从而在 <see cref="DataManagerImpl"/> 及 <see cref="XSave.SaveManagerImpl"/> 中自动使用扩展字段。</para>
        /// </summary>
        public static Func<DataSnapshot> Factory = () => new DataSnapshot();

        /// <summary>存档格式版本号，用于向前兼容迁移。数值越大版本越新。</summary>
        public int version;

        /// <summary>存档时间戳（ISO 8601）。</summary>
        public string timestamp;

        /// <summary>
        /// 存档级默认序列化格式，对应 <see cref="XSerialize.ISerializer.Format"/>。
        /// <para>当 <see cref="DataBlockSnapshot.format"/> 为空时使用此值，默认 "json"。</para>
        /// </summary>
        public string defaultFormat;

        /// <summary>数据块快照列表。</summary>
        public List<DataBlockSnapshot> blocks = new();

        /// <summary>
        /// 创建与此快照配对的 <see cref="XSave.SaveMeta"/> 实例。
        /// <para>默认实现填充 <see cref="version"/> 和 <see cref="timestamp"/>，
        /// 第三方子类可重写此方法以构造自定义 <see cref="XSave.SaveMeta"/> 子类并填充扩展字段。</para>
        /// <para>调用方（<see cref="XSave.SaveManagerImpl"/>）在拿到返回的 Meta 后会继续填充
        /// playerId / slot / relativePath / fileSize 等运行时字段。</para>
        /// </summary>
        /// <returns>配对的存档元数据实例。</returns>
        public virtual XSave.SaveMeta CreateMeta()
        {
            return new XSave.SaveMeta
            {
                version = this.version,
                timestamp = this.timestamp
            };
        }
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
        /// 数据块结构版本号，写入时等于 <see cref="IDataBlock.DataVersion"/>。
        /// <para>读档时若此值低于 Block 当前 <see cref="IDataBlock.DataVersion"/>，
        /// 会按版本差执行 <see cref="IDataBlock.OnMigrate"/> 迁移链后再调用 <see cref="IDataBlock.OnLoad"/>；
        /// 高于当前版本（如代码回滚）则跳过该块并输出警告。</para>
        /// <para>旧存档无此字段时为 0，自动进入迁移链。</para>
        /// </summary>
        public int version;

        /// <summary>
        /// <see cref="IDataBlock.OnSave"/> 返回对象的类型全名（AssemblyQualifiedName），
        /// 用于读档时按原类型反序列化。
        /// <para>为空或无法解析时回退使用 <see cref="IDataBlock"/> 自身类型（旧存档兼容）。</para>
        /// </summary>
        public string saveType;

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