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
        /// <para>数组顺序为构造时字典枚举的快照顺序（无删除操作时通常等于插入序，但<b>不保证</b>与配置文件行序一致）；依赖严格顺序请按字段自行排序。</para>
        /// <para>完全不涉及 TKey，无需指定主键类型。</para>
        /// </summary>
        public T[] GetAll()
        {
            return _allValues;
        }

        /// <summary>表中行数。</summary>
        public int Count => _allValues.Length;

        #endregion

        #region Condition Query

        /// <summary>
        /// 查找第一条满足条件的配置行。
        /// <para>按 <see cref="GetAll"/> 快照数组顺序扫描（字典快照序，非配置顺序），找到返回 <c>true</c> 并输出该行；未找到返回 <c>false</c>，<paramref name="value"/> 为 <c>default</c>。</para>
        /// <para>零分配。适合一次性/低频条件查询；高频固定条件查询请用 <see cref="BuildIndex{TIndex}"/>（O(1) 查询优于本方法的 O(n) 扫描）。</para>
        /// <para>与 <see cref="TryGet{TKey}(TKey, out T)"/> 为同名重载：实参为 lambda/委托时自动匹配本方法，实参为主键值时匹配按键查询。</para>
        /// </summary>
        /// <param name="predicate">筛选条件，如 <c>r =&gt; r.Quality &gt;= 3</c>。</param>
        /// <param name="value">匹配的行；未找到时为 <c>default</c>。</param>
        /// <returns>是否找到匹配的行。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var items = ConfigManager.GetTable&lt;ItemRow&gt;();
        /// if (items.TryGet(r =&gt; r.Quality &gt;= 3, out var row))
        /// {
        ///     // 使用 row
        /// }
        /// </code>
        /// </example>
        public bool TryGet(Predicate<T> predicate, out T value)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            for (int i = 0; i < _allValues.Length; i++)
            {
                if (predicate(_allValues[i]))
                {
                    value = _allValues[i];
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// 查找所有满足条件的配置行，追加填充到 <paramref name="result"/>。
        /// <para>按 <see cref="GetAll"/> 快照数组顺序扫描追加（字典快照序，非配置顺序），<b>不先清空</b> <paramref name="result"/>——调用方负责传入已清空的列表或自行处理追加语义。</para>
        /// <para>此方法允许调用方复用已有的 <see cref="List{T}"/> 实例以减少 GC 分配（零分配路径）。高频调用时请同时将谓词委托缓存为字段，避免闭包分配。</para>
        /// <para>适合一次性/低频条件查询；高频固定条件查询请用 <see cref="BuildIndex{TIndex}"/>。</para>
        /// </summary>
        /// <param name="predicate">筛选条件，如 <c>r =&gt; r.Quality == 3</c>。</param>
        /// <param name="result">接收匹配行的列表，不能为 null。匹配行按全表顺序追加到末尾。</param>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 或 <paramref name="result"/> 为 null 时抛出。</exception>
        public void GetRows(Predicate<T> predicate, List<T> result)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            for (int i = 0; i < _allValues.Length; i++)
            {
                if (predicate(_allValues[i]))
                    result.Add(_allValues[i]);
            }
        }

        /// <summary>
        /// 查找所有满足条件的配置行，返回新列表。
        /// <para>内部创建新 <see cref="List{T}"/> 并填充匹配行；未匹配到任何行时返回空列表。</para>
        /// <para>适合低频调用；高频路径请使用缓冲版 <see cref="GetRows(Predicate{T}, List{T})"/> 以复用列表。</para>
        /// </summary>
        /// <param name="predicate">筛选条件，如 <c>r =&gt; r.Quality == 3</c>。</param>
        /// <returns>包含所有匹配行的新列表，未找到时为空列表。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 为 null 时抛出。</exception>
        public List<T> GetRows(Predicate<T> predicate)
        {
            var result = new List<T>();
            GetRows(predicate, result);
            return result;
        }

        /// <summary>
        /// 查找所有满足条件的配置行并按 <paramref name="comparison"/> 排序，追加填充到 <paramref name="result"/>。
        /// <para>先按 <see cref="GetAll"/> 快照顺序扫描追加，再对 <paramref name="result"/> <b>整个列表</b>原地排序（<see cref="Comparison{T}"/> 重载，零分配）。</para>
        /// <para>与无排序版一致<b>不先清空</b> <paramref name="result"/>；排序将包含调用方预填的数据，需要纯净结果请传入空列表。</para>
        /// <para>适合一次性/低频条件查询；高频固定条件查询请用 <see cref="BuildIndex{TIndex}"/>。</para>
        /// </summary>
        /// <param name="predicate">筛选条件，如 <c>r =&gt; r.Quality &gt;= 3</c>。</param>
        /// <param name="comparison">排序比较器，如 <c>(a, b) =&gt; a.Quality.CompareTo(b.Quality)</c>。</param>
        /// <param name="result">接收匹配行的列表，不能为 null。匹配行追加到末尾后整体排序。</param>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/>、<paramref name="comparison"/> 或 <paramref name="result"/> 为 null 时抛出。</exception>
        public void GetRows(Predicate<T> predicate, Comparison<T> comparison, List<T> result)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (comparison == null)
                throw new ArgumentNullException(nameof(comparison));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            for (int i = 0; i < _allValues.Length; i++)
            {
                if (predicate(_allValues[i]))
                    result.Add(_allValues[i]);
            }
            result.Sort(comparison);
        }

        /// <summary>
        /// 查找所有满足条件的配置行并按 <paramref name="comparison"/> 排序，返回新列表。
        /// <para>内部创建新 <see cref="List{T}"/> 填充匹配行后排序；未匹配到任何行时返回空列表。</para>
        /// <para>适合低频调用；高频路径请使用缓冲版 <see cref="GetRows(Predicate{T}, Comparison{T}, List{T})"/> 以复用列表。</para>
        /// <para>注意：二参调用传 null 字面量会产生重载二义性（第二参可匹配 <see cref="List{T}"/> 或 <see cref="Comparison{T}"/>），请显式转型或传非 null。</para>
        /// </summary>
        /// <param name="predicate">筛选条件，如 <c>r =&gt; r.Quality &gt;= 3</c>。</param>
        /// <param name="comparison">排序比较器，如 <c>(a, b) =&gt; a.Quality.CompareTo(b.Quality)</c>。</param>
        /// <returns>包含所有匹配行并已排序的新列表，未找到时为空列表。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 或 <paramref name="comparison"/> 为 null 时抛出。</exception>
        public List<T> GetRows(Predicate<T> predicate, Comparison<T> comparison)
        {
            var result = new List<T>();
            GetRows(predicate, comparison, result);
            return result;
        }

        /// <summary>
        /// 判断是否存在满足条件的配置行。
        /// <para>零分配。用于「只关心有无、不关心具体行」的场景，与 <see cref="Contains{TKey}(TKey)"/> 的键存在判断对应。</para>
        /// </summary>
        /// <param name="predicate">筛选条件，如 <c>r =&gt; r.Quality &gt;= 3</c>。</param>
        /// <returns>是否存在至少一行满足条件。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 为 null 时抛出。</exception>
        public bool Exists(Predicate<T> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            for (int i = 0; i < _allValues.Length; i++)
            {
                if (predicate(_allValues[i]))
                    return true;
            }
            return false;
        }

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
