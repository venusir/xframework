namespace XFramework.XReactive
{
    /// <summary>
    /// <see cref="ReactiveListChange{T}"/> 的种类。各取值决定载荷里哪些字段有意义
    /// （对应关系见 <see cref="ReactiveListChange{T}"/> 的说明）。
    /// </summary>
    public enum ReactiveListChangeKind
    {
        /// <summary>在 <see cref="ReactiveListChange{T}.Index"/> 处插入了元素（<c>Add</c> / <c>Insert</c>）。</summary>
        Add = 0,

        /// <summary>从 <see cref="ReactiveListChange{T}.Index"/> 处移除了元素（<c>Remove</c> / <c>RemoveAt</c>）。</summary>
        Remove = 1,

        /// <summary>
        /// <see cref="ReactiveListChange{T}.Index"/> 处的元素被替换（索引器赋值）。
        /// </summary>
        Replace = 2,

        /// <summary>
        /// 整表变化（<c>Clear</c> / <c>Sort</c>）：重读列表，不要尝试从载荷重建。
        /// </summary>
        Reset = 3,
    }
}
