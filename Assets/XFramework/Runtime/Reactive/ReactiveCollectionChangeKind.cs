namespace XFramework.XReactive
{
    /// <summary>
    /// 集合变更的种类——本模块集合类型的**共享词表**。
    /// <para>「哪种 Kind 对应载荷里哪些字段」写在各自的载荷说明里（现为
    /// <see cref="ReactiveListChange{T}"/>），本文件不复制那份映射，避免第二份真相。</para>
    /// </summary>
    public enum ReactiveCollectionChangeKind
    {
        /// <summary>新增。<see cref="ReactiveListChange{T}"/>：在 <see cref="ReactiveListChange{T}.Index"/> 处插入了
        /// <see cref="ReactiveListChange{T}.Item"/>（<c>Add</c> / <c>Insert</c>）。</summary>
        Add = 0,

        /// <summary>移除。<see cref="ReactiveListChange{T}"/>：从 <see cref="ReactiveListChange{T}.Index"/> 处移除了
        /// <see cref="ReactiveListChange{T}.Item"/>（<c>Remove</c> / <c>RemoveAt</c>）。</summary>
        Remove = 1,

        /// <summary>替换。<see cref="ReactiveListChange{T}"/>：<see cref="ReactiveListChange{T}.Index"/> 处的元素由
        /// <see cref="ReactiveListChange{T}.OldItem"/> 换成 <see cref="ReactiveListChange{T}.Item"/>（索引器赋值）。</summary>
        Replace = 2,

        /// <summary>整表变化（列表的 <c>Clear</c> / <c>Sort</c>）：重读集合，不要尝试从载荷重建。</summary>
        Reset = 3,
    }
}
