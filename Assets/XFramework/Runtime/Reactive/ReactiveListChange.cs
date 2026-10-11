namespace XFramework.XReactive
{
    /// <summary>
    /// 列表变更载荷：<see cref="ReactiveList{T}"/> 一次变更一条，经 <see cref="IReactiveList{T}.Subscribe"/> 投递。
    /// <para><b>各字段按 <see cref="Kind"/> 取用，其余为默认值</b>：</para>
    /// <para>· <see cref="ReactiveListChangeKind.Add"/> / <see cref="ReactiveListChangeKind.Remove"/>：
    /// 用 <see cref="Index"/> 与 <see cref="Item"/>。</para>
    /// <para>· <see cref="ReactiveListChangeKind.Replace"/>：用 <see cref="Index"/>、<see cref="Item"/>（新值）
    /// 与 <see cref="OldItem"/>（旧值）。</para>
    /// <para>· <see cref="ReactiveListChangeKind.Reset"/>：全部字段无意义，重读列表。</para>
    /// <para><b><see cref="Index"/> 只在当次通知内有效</b>——它是事件的即时描述，<b>不是可重放的操作日志</b>：
    /// 不要拿它把变更拼进镜像，要全量状态请重读列表本身。</para>
    /// <para><b>通知时列表已是变更后的状态</b>：收到任何变更后重读列表即可，不需要第二处真相。</para>
    /// <para>readonly struct：投递零 GC 分配（引用类型元素只搬引用，值类型元素内联）。构造公开是刻意的
    /// ——<see cref="IReactiveList{T}"/> 承诺第三方可实现，第三方实现必须能造出合法的载荷。</para>
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    public readonly struct ReactiveListChange<T>
    {
        /// <summary>本条变更的种类。</summary>
        public readonly ReactiveListChangeKind Kind;

        /// <summary>发生变更的下标；仅 <see cref="ReactiveListChangeKind.Reset"/> 为 -1（无意义）。</summary>
        public readonly int Index;

        /// <summary>新增 / 移除 / 替换后的元素；<see cref="ReactiveListChangeKind.Reset"/> 为 default。</summary>
        public readonly T Item;

        /// <summary>替换前的旧元素，仅 <see cref="ReactiveListChangeKind.Replace"/> 有意义，其余为 default。</summary>
        public readonly T OldItem;

        /// <summary>
        /// 构造载荷。
        /// <para>公开构造是刻意的：<see cref="IReactiveList{T}"/> 承诺第三方可实现，
        /// 第三方实现必须能造出合法的载荷（先例：Audio 的播放句柄公开构造）。</para>
        /// </summary>
        /// <param name="kind">变更种类。</param>
        /// <param name="index">发生变更的下标；Reset 传 -1。</param>
        /// <param name="item">新增 / 移除 / 替换后的元素。</param>
        /// <param name="oldItem">替换前的旧元素，其余种类传 default。</param>
        public ReactiveListChange(ReactiveListChangeKind kind, int index, T item, T oldItem)
        {
            Kind = kind;
            Index = index;
            Item = item;
            OldItem = oldItem;
        }
    }
}
