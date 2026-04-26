using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 节点缓存池。节点销毁时自动回池，减少 GC 分配。
    /// <para>通过 <see cref="NodeFactory"/> 使用，也可独立创建使用。</para>
    /// </summary>
    /// <typeparam name="T">节点类型，必须有无参构造函数。</typeparam>
    public class NodePool<T> where T : BaseNode, new()
    {
        #region Private Fields

        /// <summary>池中可复用的节点栈。</summary>
        private readonly Stack<T> _pool = new Stack<T>();

        #endregion

        #region Public Methods

        /// <summary>
        /// 从池中获取一个节点。
        /// <para>如果池中有可用节点则复用，否则创建新节点。</para>
        /// <para>获取的节点处于"已销毁"状态，需要调用 <see cref="BaseNode.Awake"/> 或通过 <see cref="ParentNode.AddChild"/> 重新初始化。</para>
        /// </summary>
        /// <returns>可用的节点实例。</returns>
        public T Get()
        {
            T node = _pool.Count > 0 ? _pool.Pop() : new T();
            node.OnReturnToPool += OnNodeReturned;
            return node;
        }

        /// <summary>
        /// 手动将节点回收到池中。
        /// <para>通常不需要手动调用，节点调用 <see cref="BaseNode.Destroy"/> 后会自动回池。</para>
        /// <para>如果节点尚未销毁，会先调用 <see cref="BaseNode.Destroy"/>。</para>
        /// </summary>
        /// <param name="node">要回收的节点。</param>
        public void Return(T node)
        {
            if (node == null) return;

            node.OnReturnToPool -= OnNodeReturned;

            if (!node.Destroyed)
            {
                node.Destroy();
            }

            _pool.Push(node);
        }

        /// <summary>
        /// 预热池，预创建指定数量的节点。
        /// <para>预热后的节点处于"已销毁"状态，可直接通过 <see cref="Get"/> 获取使用。</para>
        /// </summary>
        /// <param name="count">预创建的数量。</param>
        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                T node = new T();
                node.Destroy();
                _pool.Push(node);
            }
        }

        /// <summary>
        /// 清空池中所有节点。
        /// </summary>
        public void Clear()
        {
            _pool.Clear();
        }

        /// <summary>池中当前可用节点数量。</summary>
        public int Count => _pool.Count;

        #endregion

        #region Private Methods

        /// <summary>
        /// 节点销毁完成时的回调，自动将节点回收到池中。
        /// </summary>
        private void OnNodeReturned(BaseNode node)
        {
            var tNode = (T)node;
            tNode.OnReturnToPool -= OnNodeReturned;
            _pool.Push(tNode);
        }

        #endregion
    }
}
