using System;
using System.Collections.Generic;

namespace XFramework.XConfig
{
    /// <summary>
    /// 非主键索引的只读视图，由 <see cref="ConfigTable{T}.BuildIndex{TIndex}"/> 构建。
    /// <para>构建时遍历全表一次（O(n)），后续查询 O(1)，零额外 GC 分配。</para>
    /// <para>同一个索引名对同一张表只构建一次，重复调用返回缓存。</para>
    /// </summary>
    /// <typeparam name="T">配置行类型。</typeparam>
    /// <typeparam name="TIndex">索引键类型。</typeparam>
    public sealed class ConfigIndexView<T, TIndex>
    {
        private readonly Dictionary<TIndex, List<T>> _dict;

        internal ConfigIndexView(Dictionary<TIndex, List<T>> dict)
        {
            _dict = dict;
        }

        /// <summary>
        /// 按索引键获取所有匹配行。键不存在时返回空数组（非 null）。
        /// </summary>
        public IReadOnlyList<T> Get(TIndex key)
        {
            if (_dict.TryGetValue(key, out var list))
                return list;
            return Array.Empty<T>();
        }

        /// <summary>
        /// 安全按索引键获取匹配行。
        /// </summary>
        public bool TryGet(TIndex key, out IReadOnlyList<T> values)
        {
            if (_dict.TryGetValue(key, out var list))
            {
                values = list;
                return true;
            }
            values = default;
            return false;
        }
    }
}