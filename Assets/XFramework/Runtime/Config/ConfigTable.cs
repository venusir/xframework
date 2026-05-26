using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XConfig
{
    /// <summary>
    /// Table 配置数据的只读包装器，通过 <see cref="IConfigManager.GetTable{T}"/> 获取。
    /// <para>TKey 隐藏在类型内部，通过 .Get(key) / .TryGet(key) 查询时由实参自动推断主键类型。</para>
    /// <para>建议缓存此包装器以复用后续查询，避免每次查字典。</para>
    /// </summary>
    /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow"/>。</typeparam>
    public sealed class ConfigTable<T> : IConfigTable where T : IConfigRow
    {
        internal readonly IDictionary _dict;
        private readonly T[] _allValues;
        private readonly Dictionary<string, object> _indices = new();

        /// <summary>
        /// 构造 <see cref="ConfigTable{T}"/>。
        /// <para>通常由框架内部创建，第三方注入数据时也可直接构造（如 Luban / protobuf 等自定义格式）。</para>
        /// </summary>
        /// <param name="dict">内部字典（<see cref="Dictionary{TKey, T}"/>，存储为 <see cref="IDictionary"/>）。</param>
        public ConfigTable(IDictionary dict)
        {
            _dict = dict;
            _allValues = new T[_dict.Count];
            _dict.Values.CopyTo(_allValues, 0);
        }

        #region Query

        /// <summary>
        /// 按主键查询配置行。TKey 由实参自动推断。
        /// <para>主键不存在或 Table 类型与主键类型不匹配时抛出 <see cref="ConfigException"/>。</para>
        /// </summary>
        /// <example>
        /// <code>
        /// var items = ConfigManager.GetTable<ItemRow>();
        /// var row = items.Get(1001); // TKey 自动推断为 int
        /// </code>
        /// </example>
        public T Get<TKey>(TKey key)
        {
            if (_dict is Dictionary<TKey, T> d)
            {
                if (d.TryGetValue(key, out var value))
                    return value;
                throw new ConfigException(
                    $"Id '{key}' not found in Table '{typeof(T).Name}'.");
            }
            throw new ConfigException(
                $"Table '{typeof(T).Name}' key type mismatch. Expected '{typeof(TKey).Name}'.");
        }

        /// <summary>
        /// 安全查询配置行。TKey 由实参自动推断。
        /// <para>主键不存在时返回 <c>false</c>，<paramref name="value"/> 为 <c>default</c>。</para>
        /// </summary>
        public bool TryGet<TKey>(TKey key, out T value)
        {
            if (_dict is Dictionary<TKey, T> d)
            {
                if (d.TryGetValue(key, out value))
                    return true;
                value = default;
                return false;
            }
            Debug.LogWarning(
                $"[Config] Table '{typeof(T).Name}' key type mismatch in TryGet. " +
                $"Actual key type is unknown (stored as IDictionary). " +
                $"You passed '{typeof(TKey).Name}', but the Table was loaded with a different key type.");
            value = default;
            return false;
        }

        /// <summary>
        /// 判断表中是否包含指定主键的行。
        /// </summary>
        public bool Contains<TKey>(TKey key)
        {
            if (_dict is Dictionary<TKey, T> d)
                return d.ContainsKey(key);
            Debug.LogWarning(
                $"[Config] Table '{typeof(T).Name}' key type mismatch in Contains. " +
                $"Actual key type is unknown (stored as IDictionary). " +
                $"You passed '{typeof(TKey).Name}', but the Table was loaded with a different key type.");
            return false;
        }

        /// <summary>
        /// 获取表中所有配置行（零 GC 分配，返回构造函数中预缓存的数组）。
        /// <para>返回的是内部数组引用，请勿修改元素（struct 类型修改不生效，class 类型应遵守只读约定）。</para>
        /// <para>完全不涉及 TKey，无需指定主键类型。</para>
        /// </summary>
        public T[] GetAll()
        {
            return _allValues;
        }

        /// <summary>表中行数。</summary>
        public int Count => _allValues.Length;

        #endregion

        #region Index

        /// <summary>
        /// 构建或获取非主键索引，按 <paramref name="keySelector"/> 分组。
        /// <para>同一 <paramref name="indexName"/> 只构建一次，后续调用直接返回缓存。</para>
        /// <para>构建时 O(n) 遍历全表，查询 O(1)，零额外 GC。</para>
        /// </summary>
        /// <typeparam name="TIndex">索引键类型。</typeparam>
        /// <param name="indexName">索引名称（同表内唯一），建议使用字段名如 "Quality"。</param>
        /// <param name="keySelector">索引键选择器，如 r => r.Quality。</param>
        /// <returns><see cref="ConfigIndexView{T, TIndex}"/> 只读视图。</returns>
        /// <example>
        /// <code>
        /// var items = ConfigManager.GetTable<ItemRow>();
        /// var byQuality = items.BuildIndex("Quality", r => r.Quality);
        /// var epics = byQuality.Get(ItemQuality.Epic); // List<ItemRow>
        /// </code>
        /// </example>
        public ConfigIndexView<T, TIndex> BuildIndex<TIndex>(string indexName, Func<T, TIndex> keySelector)
        {
            if (_indices.TryGetValue(indexName, out var cached))
                return (ConfigIndexView<T, TIndex>)cached;

            var dict = new Dictionary<TIndex, List<T>>();
            var all = GetAll();
            for (int i = 0; i < all.Length; i++)
            {
                var key = keySelector(all[i]);
                if (!dict.TryGetValue(key, out var list))
                {
                    list = new List<T>();
                    dict[key] = list;
                }
                list.Add(all[i]);
            }
            var view = new ConfigIndexView<T, TIndex>(dict);
            _indices[indexName] = view;
            return view;
        }

        #endregion

        #region IConfigTable

        /// <summary>暴露内部字典，供非泛型反射路径访问。</summary>
        IDictionary IConfigTable.Data => _dict;

        #endregion
    }
}
