using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XData
{
    /// <summary>
    /// 运行时可变 Table 数据的包装器，通过 <see cref="IDataManager.GetTable{T}"/> 获取。
    /// <para>与只读的 <see cref="XConfig.ConfigTable{T}"/> 不同，DataTable 支持运行时对行数据的增/删/改。</para>
    /// <para>TKey 隐藏在类型内部，通过 .Get(key) / .TryGet(key) 查询时由实参自动推断主键类型。</para>
    /// </summary>
    /// <typeparam name="T">数据行类型，需实现 <see cref="IDataRow{TKey}"/>。</typeparam>
    public sealed class DataTable<T> : IDataTable where T : IDataRow
    {
        internal IDictionary _dict;
        /// <summary>所有值的数组快照，在数据变更时重建。</summary>
        internal T[] _allValues;

        internal DataTable()
        {
        }

        #region Query

        /// <summary>
        /// 按主键查询行数据。TKey 由实参自动推断。
        /// <para>主键不存在或 Table 类型与主键类型不匹配时抛出 <see cref="DataException"/>。</para>
        /// </summary>
        public T Get<TKey>(TKey key)
        {
            if (_dict is Dictionary<TKey, T> d)
            {
                if (d.TryGetValue(key, out var value))
                    return value;
                throw new DataException(
                    $"Key '{key}' not found in DataTable '{typeof(T).Name}'.");
            }
            throw new DataException(
                $"DataTable '{typeof(T).Name}' key type mismatch. Expected '{typeof(TKey).Name}'.");
        }

        /// <summary>
        /// 安全查询行数据。TKey 由实参自动推断。
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
                $"[Data] DataTable '{typeof(T).Name}' key type mismatch in TryGet.");
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
            return false;
        }

        /// <summary>
        /// 获取表中所有数据行（零额外 GC，返回内部缓存的数组）。
        /// <para>返回的是内部数组引用，请勿修改元素。</para>
        /// </summary>
        public T[] GetAll()
        {
            return _allValues;
        }

        /// <summary>表中行数。</summary>
        public int Count => _allValues?.Length ?? 0;

        #endregion

        #region Mutation

        /// <summary>添加或更新一行数据。</summary>
        public void Upsert<TKey>(T row)
        {
            if (row is IDataRow<TKey> keyed)
            {
                var dict = GetOrCreateDict<TKey>();
                var key = keyed.Id;
                bool exists = dict.ContainsKey(key);
                dict[key] = (T)(object)keyed;
                if (!exists)
                    RebuildArray();
            }
            else
            {
                throw new DataException(
                    $"Row type '{row.GetType().Name}' does not implement IDataRow<>.");
            }
        }

        /// <summary>删除一行数据。</summary>
        public bool Remove<TKey>(TKey key)
        {
            var dict = GetOrCreateDict<TKey>();
            if (dict.Remove(key))
            {
                RebuildArray();
                return true;
            }
            return false;
        }

        /// <summary>清空所有行。</summary>
        public void Clear()
        {
            _dict?.Clear();
            _allValues = Array.Empty<T>();
        }

        #endregion

        #region IDataTable (非泛型接口，供框架内部使用)

        IDictionary IDataTable.Data => _dict;

        void IDataTable.UpsertRow(IDataRow row)
        {
            if (row == null) return;

            // 通过 IDataRow.RowKey 获取主键（非反射、无装箱的安全访问）
            object key = row.RowKey;
            if (key == null)
                throw new DataException($"Row '{row.GetType().Name}' has null Id.");

            // 将普通 IDataRow 创建为 T 的复制品
            // 对于存档恢复场景，row 已经是完整类型 T，直接强转
            if (row is T typedRow)
            {
                if (_dict == null)
                {
                    // 初始化字典（以 key 的实际类型作为 TKey）
                    var dictType = typeof(Dictionary<,>).MakeGenericType(key.GetType(), typeof(T));
                    _dict = (IDictionary)Activator.CreateInstance(dictType);
                    _allValues = Array.Empty<T>();
                }

                bool exists = _dict.Contains(key);
                _dict[key] = typedRow;
                if (!exists)
                    RebuildArray();
            }
            else
            {
                throw new DataException(
                    $"Row type '{row.GetType().Name}' is not assignable to '{typeof(T).Name}'.");
            }
        }

        void IDataTable.Clear()
        {
            Clear();
        }

        #endregion

        #region Internal

        private Dictionary<TKey, T> GetOrCreateDict<TKey>()
        {
            if (_dict == null)
            {
                _dict = new Dictionary<TKey, T>();
                _allValues = Array.Empty<T>();
            }
            return (Dictionary<TKey, T>)_dict;
        }

        private void RebuildArray()
        {
            if (_dict == null)
            {
                _allValues = Array.Empty<T>();
                return;
            }
            _allValues = new T[_dict.Count];
            _dict.Values.CopyTo(_allValues, 0);
        }

        #endregion
    }

    /// <summary>
    /// DataTable 非泛型接口，供框架内部（存档/快照等非泛型路径）使用。
    /// </summary>
    public interface IDataTable
    {
        /// <summary>暴露内部字典，供序列化/快照等非泛型路径。</summary>
        IDictionary Data { get; }

        /// <summary>
        /// 添加或更新一行。
        /// <para>通过 <see cref="IDataRow.RowKey"/> 获取主键，无需反射。</para>
        /// </summary>
        void UpsertRow(IDataRow row);

        /// <summary>清空所有行。</summary>
        void Clear();
    }
}