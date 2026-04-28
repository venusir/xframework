namespace XFramework
{
    /// <summary>
    /// 叶子节点，不包含子节点。
    /// <para>作为节点树的末端节点使用，通常用于存储具体的数据或行为。</para>
    /// </summary>
    public class LeafNode : BaseNode
    {
        /// <summary>
        /// 获取直接父节点（EntityNode）中指定类型的子节点。
        /// <para>类似于 Unity 的 GetComponent，仅在直接父节点层级查找。</para>
        /// </summary>
        /// <typeparam name="T">要查找的节点类型，必须实现 IBaseNode 且有无参构造函数。</typeparam>
        /// <returns>找到的节点，未找到则返回 null。</returns>
        protected T GetNode<T>(bool autoCreate = true) where T : IBaseNode
        {
            var entity = Parent as IEntityNode;
            if (entity == null) return default;
            return entity.GetNode<T>(autoCreate);
        }

        /// <summary>
        /// 沿父链向上遍历，查找第一个匹配指定类型的节点。
        /// <para>类似于 Unity 的 GetComponentInParent，会递归向上查找所有祖先 EntityNode。</para>
        /// </summary>
        /// <typeparam name="T">要查找的节点类型，必须实现 IBaseNode 且有无参构造函数。</typeparam>
        /// <returns>找到的节点，未找到则返回 null。</returns>
        protected T GetNodeInParent<T>() where T : IBaseNode
        {
            BaseNode current = Parent;
            while (current != null)
            {
                if (current is EntityNode entity)
                {
                    var component = entity.GetNode<T>(false);
                    if (component != null)
                        return component;
                }
                current = current.Parent;
            }
            return default;
        }
    }
}
