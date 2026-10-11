namespace XFramework.XReactive
{
    /// <summary>
    /// 字典变更载荷：<see cref="ReactiveDictionary{TKey, TValue}"/> 一次变更一条，经
    /// <see cref="IReactiveDictionary{TKey, TValue}.Subscribe"/> 投递。
    /// <para><b>各字段按 <see cref="Kind"/> 取用，其余为默认值</b>：</para>
    /// <para>· <see cref="ReactiveCollectionChangeKind.Add"/> / <see cref="ReactiveCollectionChangeKind.Remove"/>：
    /// 用 <see cref="Key"/> 与 <see cref="Value"/>（<b>Remove 的 <see cref="Value"/> 是被移除的值</b>）。</para>
    /// <para>· <see cref="ReactiveCollectionChangeKind.Replace"/>：用 <see cref="Key"/>、<see cref="Value"/>（新值）
    /// 与 <see cref="OldValue"/>（旧值）。</para>
    /// <para>· <see cref="ReactiveCollectionChangeKind.Reset"/>：全部字段无意义——<b><see cref="Key"/> 也取
    /// <c>default</c></b>（<c>TKey</c> 没有通用非法值，不给哨兵；这是与列表载荷 <c>Index = -1</c> 的差别），
    /// 重读字典。</para>
    /// <para><b><see cref="Key"/> 是稳定的</b>（不像列表的索引会被位移），但它描述的条目<b>可能已被移除</b>：
    /// 回调内改动会嵌套派发（嵌套那一轮先跑完），外层载荷投出时该键可能已不在字典里——
    /// 要状态请重读字典，不要从载荷重建。</para>
    /// <para><b>通知时字典已是变更后的状态</b>。</para>
    /// <para>readonly struct：投递零 GC 分配（引用类型键值只搬引用）。构造公开是刻意的
    /// ——<see cref="IReactiveDictionary{TKey, TValue}"/> 承诺第三方可实现，第三方实现必须能造出合法的载荷。</para>
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    public readonly struct ReactiveDictionaryChange<TKey, TValue>
    {
        /// <summary>本条变更的种类。</summary>
        public readonly ReactiveCollectionChangeKind Kind;

        /// <summary>发生变更的键；仅 <see cref="ReactiveCollectionChangeKind.Reset"/> 为 <c>default</c>（无意义，不要读）。</summary>
        public readonly TKey Key;

        /// <summary>新增 / 替换后的值，或<b>被移除的值</b>；<see cref="ReactiveCollectionChangeKind.Reset"/> 为 default。</summary>
        public readonly TValue Value;

        /// <summary>替换前的旧值，仅 <see cref="ReactiveCollectionChangeKind.Replace"/> 有意义，其余为 default。</summary>
        public readonly TValue OldValue;

        /// <summary>
        /// 构造载荷。
        /// <para>公开构造是刻意的：<see cref="IReactiveDictionary{TKey, TValue}"/> 承诺第三方可实现，
        /// 第三方实现必须能造出合法的载荷（先例：Audio 的播放句柄公开构造）。</para>
        /// </summary>
        /// <param name="kind">变更种类。</param>
        /// <param name="key">发生变更的键；Reset 传 <c>default</c>。</param>
        /// <param name="value">新增 / 替换后的值，或对 Remove 而言被移除的值。</param>
        /// <param name="oldValue">替换前的旧值，其余种类传 default。</param>
        public ReactiveDictionaryChange(ReactiveCollectionChangeKind kind, TKey key, TValue value, TValue oldValue)
        {
            Kind = kind;
            Key = key;
            Value = value;
            OldValue = oldValue;
        }
    }
}
