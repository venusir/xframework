using System;
using System.Collections.Generic;

namespace XFramework.XData
{
    /// <summary>
    /// 存档数据结构（JSON 兼容，可直接由 Unity JsonUtility 序列化）。
    /// <para>每个 <see cref="IDataBlock"/> 通过 <see cref="BlockSnap"/> 持久化。</para>
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>存档格式版本号，用于向前兼容。</summary>
        public string version;

        /// <summary>存档时间戳（ISO 8601）。</summary>
        public string timestamp;

        /// <summary>数据块快照列表。</summary>
        public List<BlockSnap> blocks = new();
    }

    /// <summary>
    /// 单个 <see cref="IDataBlock"/> 的序列化快照。
    /// </summary>
    [Serializable]
    public sealed class BlockSnap
    {
        /// <summary>
        /// 数据块类型全名（AssemblyQualifiedName），用于读档时反射还原类型。
        /// </summary>
        public string blockType;

        /// <summary>
        /// <see cref="IDataBlock.OnSave"/> 返回值经 JsonUtility 序列化后的 JSON 文本。
        /// </summary>
        public string json;
    }
}