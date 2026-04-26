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
        private static readonly Dictionary<Type, INodePool> _pools = new Dictionary<Type, INodePool>();

        #endregion

        #region Public Methods - Generic

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
                pool.Clear();
            }
        }

        #endregion

        #region Public Methods - Type Key

        /// <summary>
        /// 通过运行时类型获取节点。
        /// <para>优先从缓存池中复用，池为空时创建新节点。</para>
        /// </summary>
        /// <param name="type">节点类型，必须是 <see cref="BaseNode"/> 的子类且有无参构造函数。</param>
        /// <returns>节点实例。</returns>
        /// <exception cref="ArgumentNullException">type 为 null。</exception>
        /// <exception cref="ArgumentException">type 不是 BaseNode 的子类。</exception>
        public static BaseNode GetNode(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(BaseNode).IsAssignableFrom(type))
                throw new ArgumentException($"GetNode failed: {type} is not a BaseNode.");

            var pool = GetOrCreatePool(type);
            return pool.Get();
        }

        /// <summary>
        /// 通过运行时类型手动回收节点到缓存池中。
        /// </summary>
        /// <param name="type">节点类型。</param>
        /// <param name="node">要回收的节点。</param>
        /// <exception cref="ArgumentNullException">type 或 node 为 null。</exception>
        public static void ReturnNode(Type type, BaseNode node)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (node == null)
                throw new ArgumentNullException(nameof(node));

            var pool = GetOrCreatePool(type);
            pool.Return(node);
        }

        /// <summary>
        /// 通过运行时类型预热缓存池。
        /// </summary>
        /// <param name="type">节点类型。</param>
        /// <param name="count">预创建的数量。</param>
        /// <exception cref="ArgumentNullException">type 为 null。</exception>
        public static void Prewarm(Type type, int count)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var pool = GetOrCreatePool(type);
            pool.Prewarm(count);
        }

        /// <summary>
        /// 通过运行时类型清空缓存池。
        /// </summary>
        /// <param name="type">节点类型。</param>
        public static void ClearPool(Type type)
        {
            if (type == null) return;

            if (_pools.TryGetValue(type, out var pool))
            {
                pool.Clear();
            }
        }

        #endregion

        #region Public Methods - Pool Management

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
        /// 获取或创建指定类型的缓存池（泛型版本）。
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

        /// <summary>
        /// 获取或创建指定类型的缓存池（运行时 Type 版本）。
        /// <para>通过反射创建 <see cref="NodePool{T}"/> 实例。</para>
        /// </summary>
        private static INodePool GetOrCreatePool(Type type)
        {
            if (!_pools.TryGetValue(type, out var pool))
            {
                var poolType = typeof(NodePool<>).MakeGenericType(type);
                pool = (INodePool)Activator.CreateInstance(poolType);
                _pools[type] = pool;
            }
            return pool;
        }

        #endregion
    }
}
