namespace XFramework
{
    /// <summary>
    /// 根节点。作为节点树的入口，提供便捷的节点创建方法。
    /// <para>封装了从 <see cref="NodeFactory"/> 获取节点并自动挂接到当前根节点的常见模式。</para>
    /// </summary>
    public class RootNode : CompositeNode
    {
        /// <summary>
        /// 创建一个指定类型的子节点，并自动挂接到当前根节点。
        /// <para>内部调用 <see cref="NodeFactory.GetNode{T}()"/> 获取节点，再通过 <see cref="CompositeNode.AddNode"/> 挂接。</para>
        /// </summary>
        /// <typeparam name="T">节点类型，必须有无参构造函数。</typeparam>
        /// <returns>创建的子节点实例。</returns>
        public T CreateNode<T>() where T : BaseNode, new()
        {
            T node = NodeFactory.GetNode<T>();
            AddNode(node);
            return node;
        }

        /// <summary>
        /// 创建一个指定类型的子节点，传入初始化参数，并自动挂接到当前根节点。
        /// <para>内部调用 <see cref="NodeFactory.GetNode{T}(object)"/> 获取节点，再通过 <see cref="CompositeNode.AddNode"/> 挂接。</para>
        /// </summary>
        /// <typeparam name="T">节点类型，必须有无参构造函数。</typeparam>
        /// <param name="arg">初始化参数。</param>
        /// <returns>创建的子节点实例。</returns>
        public T CreateNode<T>(object arg) where T : BaseNode, new()
        {
            T node = NodeFactory.GetNode<T>(arg);
            AddNode(node);
            return node;
        }
    }
}
