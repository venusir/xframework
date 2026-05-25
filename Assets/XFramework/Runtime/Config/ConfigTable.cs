using System.Collections.Generic;

namespace XFramework.XConfig
{
    /// <summary>
    /// Table 配置数据的只读包装器，通过 <see cref="IConfigManager.GetTable{T}"/> 获取。
    /// <para>TKey 隐藏在类型内部，通过 .Get(key) / .TryGet(key) 查询时由实参自动推断主键类型。</para>
    /// <para>建议缓存此包装器以复用后续查询，避免每次查字典。</para>
    /// </summary>
    /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
    public sealed class ConfigTable<T>
    {
        private readonly object _dict; // Dictionary<TKey, T>
        private readonly int _count;

        /// <summary>
        /// 构造 <see cref="ConfigTable{T}"/>。
        /// <para>通常由框架内部创建，第三方注入数据时也可直接构造（如 Luban / protobuf 等自定义格式）。</para>
        /// </summary>
        /// <param name="dict">内部字典（<see cref="Dictionary{TKey, T}"/>，装箱为 object）。</param>
        /// <param name="count">字典中元素数量。</param>
        public ConfigTable(object dict, int count)
        {
            _dict = dict;
            _count = count;
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
            var d = _dict as Dictionary<TKey, T>;
            if (d == null)
                throw new ConfigException(
                    $"Table '{typeof(T).Name}' key type mismatch. Expected '{typeof(TKey).Name}'.");
            if (!d.TryGetValue(key, out var value))
                throw new ConfigException(
                    $"Id '{key}' not found in Table '{typeof(T).Name}'.");
            return value;
        }

        /// <summary>
        /// 安全查询配置行。TKey 由实参自动推断。
        /// <para>主键不存在时返回 <c>false</c>，<paramref name="value"/> 为 <c>default</c>。</para>
        /// </summary>
        public bool TryGet<TKey>(TKey key, out T value)
        {
            if (_dict is Dictionary<TKey, T> d && d.TryGetValue(key, out value))
                return true;
            value = default;
            return false;
        }

        /// <summary>
        /// 判断表中是否包含指定主键的行。
        /// </summary>
        public bool Contains<TKey>(TKey key)
        {
            return _dict is Dictionary<TKey, T> d && d.ContainsKey(key);
        }

        /// <summary>
        /// 获取表中所有配置行（复制为新数组，有少量 GC 分配）。
        /// <para>完全不涉及 TKey，无需指定主键类型。</para>
        /// </summary>
        public T[] GetAll()
        {
            var arr = new T[_count];
            ((System.Collections.IDictionary)_dict).Values.CopyTo(arr, 0);
            return arr;
        }

        /// <summary>表中行数。</summary>
        public int Count => _count;

        #endregion
    }
}