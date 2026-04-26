using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 节点工厂。按节点类型自动管理缓存池，提供统一的创建/回收 API。
    /// <para>所有节点通过此工厂创建时，销毁后会自动回池复用。</para>
    /// </summary>
    public static class NodeFactory
    {
        #region Private Fields

        /// <summary>按节点类型存储的缓存池字典。</summary>
        private static readonly Dictionary<Type, object> _pools = new Dictionary<Type, object>();

        #endregion

        #region Public Methods

        /// <summary>
        /// 获取指定类型的节点。
        /// <para>优先从缓存池中复用，池为空时创建新节点。</para>
        /// <para>获取的节点处于"已销毁"状态，需要通过 <see cref="ParentNode.AddChild"/> 或手动调用 <see cref="BaseNode.Awake"/> 初始化。</para>
        /// </summary>
        /// <typeparam name="T">节点类型，必须有无参构造函数。</typeparam>
        /// <returns>节点实例。</returns>
        public static T GetNode<T>() where T : BaseNode, new()
        {
            var pool = GetOrCreatePool<T>();
            return pool.Get();
        }

        /// <summary>
        /// 手动回收节点到缓存池中。
        /// <para>通常不需要手动调用，节点调用 <see cref="BaseNode.Destroy"/> 后会自动回池。</para>
        /// </summary>
        /// <typeparam name="T">节点类型。</typeparam>
        /// <param name="node">要回收的节点。</param>
        public static void ReturnNode<T>(T node) where T : BaseNode, new()
        {
            if (node == null) return;

            var pool = GetOrCreatePool<T>();
            pool.Return(node);
        }

        /// <summary>
        /// 预热指定类型的缓存池，预创建指定数量的节点。
        /// </summary>
        /// <typeparam name="T">节点类型。</typeparam>
        /// <param name="count">预创建的数量。</param>
        public static void Prewarm<T>(int count) where T : BaseNode, new()
        {
            var pool = GetOrCreatePool<T>();
            pool.Prewarm(count);
        }

        /// <summary>
        /// 清空指定类型的缓存池。
        /// </summary>
        /// <typeparam name="T">节点类型。</typeparam>
        public static void ClearPool<T>() where T : BaseNode, new()
        {
            var type = typeof(T);
            if (_pools.TryGetValue(type, out var pool))
            {
                ((NodePool<T>)pool).Clear();
            }
        }

        /// <summary>
        /// 清空所有缓存池。
        /// </summary>
        public static void ClearAllPools()
        {
            _pools.Clear();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 获取或创建指定类型的缓存池。
        /// </summary>
        private static NodePool<T> GetOrCreatePool<T>() where T : BaseNode, new()
        {
            var type = typeof(T);
            if (!_pools.TryGetValue(type, out var pool))
            {
                pool = new NodePool<T>();
                _pools[type] = pool;
            }
            return (NodePool<T>)pool;
        }

        #endregion
    }
}
