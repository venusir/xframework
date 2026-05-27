using System;
using System.Collections.Generic;

namespace XFramework.XData
{
    /// <summary>
    /// 存档数据结构（JSON 兼容，可直接由 Unity JsonUtility 序列化）。
    /// <para>Table 数据按名称索引（<c>List<TableSnap></c>），Global 单例同理。</para>
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>存档格式版本号，用于向前兼容。</summary>
        public string version;

        /// <summary>存档时间戳（ISO 8601）。</summary>
        public string timestamp;

        /// <summary>Table 快照列表。</summary>
        public List<TableSnap> tables = new();

        /// <summary>Global 快照列表。</summary>
        public List<GlobalSnap> globals = new();
    }

    /// <summary>
    /// 单张 Table 的序列化快照。
    /// </summary>
    [Serializable]
    public sealed class TableSnap
    {
        /// <summary>Table 名称（即 <see cref="DataTable{T}"/> 的注册名）。</summary>
        public string tableName;

        /// <summary>序列化后的 JSON 文本（单个 DataSet 数组）。</summary>
        /// <para>各行由 <see cref="DataManagerImpl"/> 内部通过 JsonUtility 逐行序列化后拼接。</para>
        public string json;
    }

    /// <summary>
    /// 单个 Global 单例的序列化快照。
    /// </summary>
    [Serializable]
    public sealed class GlobalSnap
    {
        /// <summary>Global 名称（即注册名）。</summary>
        public string globalName;

        /// <summary>序列化后的 JSON 文本。</summary>
        public string json;
    }
}