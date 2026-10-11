namespace XFramework.XReactive
{
    /// <summary>
    /// 集合变更的种类——本模块集合类型的**共享词表**：<see cref="ReactiveListChange{T}"/> 与
    /// <see cref="ReactiveDictionaryChange{TKey, TValue}"/> 用它表示同一组语义。
    /// <para><b>各 Kind 对应载荷里哪些字段，以各载荷自己的说明为准</b>；本文件只给一行索引，
    /// 不复写映射（避免第二份真相）。</para>
    /// </summary>
    public enum ReactiveCollectionChangeKind
    {
        /// <summary>新增（列表：插入 <c>Index</c> / <c>Item</c>；字典：写入新键 <c>Key</c> / <c>Value</c>）。</summary>
        Add = 0,

        /// <summary>移除（列表：<c>Index</c> / <c>Item</c>；字典：<c>Key</c> 与被移除的 <c>Value</c>）。</summary>
        Remove = 1,

        /// <summary>替换（列表：<c>Index</c> / <c>Item</c> / <c>OldItem</c>；字典：<c>Key</c> / <c>Value</c> / <c>OldValue</c>）。</summary>
        Replace = 2,

        /// <summary>整表变化（列表的 <c>Clear</c> / <c>Sort</c>，字典的 <c>Clear</c>）：重读集合，不要尝试从载荷重建。</summary>
        Reset = 3,
    }
}
