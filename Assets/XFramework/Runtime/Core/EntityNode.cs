using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 实体节点接口。按类型（Type）缓存子节点，提供高效的组件式访问。
    /// <para>类似于 Unity 的 GetComponent/AddComponent 模式，但基于纯 C# 节点树实现。</para>
    /// </summary>
    public interface IEntityNode : IParentNode
    {
        /// <summary>获取指定类型的子节点。优先从缓存中查找，未找到时可根据参数自动创建。</summary>
        T GetNode<T>(bool autoCreate = true) where T : IBaseNode;

        /// <summary>添加指定类型的子节点。如果已存在同类型节点则直接返回。</summary>
        T AddNode<T>() where T : BaseNode, new();

        /// <summary>添加指定类型的子节点，并传入初始化参数。如果已存在同类型节点则直接返回。</summary>
        T AddNode<T>(object arg) where T : BaseNode, new();

        /// <summary>通过运行时类型添加子节点。如果已存在同类型节点则直接返回。</summary>
        BaseNode AddNode(Type type);

        /// <summary>通过运行时类型添加子节点，并传入初始化参数。如果已存在同类型节点则直接返回。</summary>
        BaseNode AddNode(Type type, object arg);

        /// <summary>添加一个子节点，并立即执行异步启动。</summary>
        UniTask<T> AddNodeAsync<T>() where T : BaseNode, IParentNode, new();

        /// <summary>添加一个子节点（带参数），并立即执行异步启动。</summary>
        UniTask<T> AddNodeAsync<T>(object arg) where T : BaseNode, IParentNode, new();

        /// <summary>通过运行时类型添加一个子节点，并立即执行异步启动。</summary>
        UniTask<BaseNode> AddNodeAsync(Type type);

        /// <summary>通过运行时类型添加一个子节点（带参数），并立即执行异步启动。</summary>
        UniTask<BaseNode> AddNodeAsync(Type type, object arg);

        /// <summary>移除指定类型的子节点。</summary>
        bool RemoveNode<T>() where T : IBaseNode;

        /// <summary>通过运行时类型移除子节点。</summary>
        bool RemoveNode(Type type);

        /// <summary>移除指定的子节点实例。</summary>
        bool RemoveNode(BaseNode node);
    }

    /// <summary>
    /// 实体节点，按类型（Type）缓存子节点，提供高效的组件式访问。
    /// <para>类似于 Unity 的 GetComponent/AddComponent 模式，但基于纯 C# 节点树实现。</para>
    /// </summary>
    public abstract class EntityNode : ParentNode, IEntityNode
    {
        #region Private Fields

        /// <summary>按具体类类型缓存的子节点字典。</summary>
        private Dictionary<Type, BaseNode> _typeCache;

        /// <summary>按接口类型缓存的子节点字典。</summary>
        private Dictionary<Type, IBaseNode> _interfaceCache;

        #endregion

        #region Public Methods

        /// <summary>
        /// 获取指定类型的子节点。优先从缓存中查找，未找到时可根据参数自动创建。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="autoCreate">未找到时是否自动创建。默认为 true。
        /// <para>注意：当 T 为接口类型时，无法自动创建，此参数无效。</para></param>
        /// <returns>匹配的子节点，未找到且无法自动创建时返回 null。</returns>
        public T GetNode<T>(bool autoCreate = true) where T : IBaseNode
        {
            Type type = typeof(T);

            // 优先从对应缓存中查找
            if (type.IsInterface)
            {
                if (_interfaceCache.TryGetValue(type, out var cached) && cached is T node)
                    return node;
            }
            else
            {
                if (_typeCache.TryGetValue(type, out var cached) && cached is T node)
                    return node;
            }

            // 未找到且允许自动创建，且 T 是具体类（非接口）且是 BaseNode 的子类
            if (autoCreate && !type.IsInterface && typeof(BaseNode).IsAssignableFrom(type))
            {
                // 通过 NodeFactory 创建节点
                var component = NodeFactory.GetNode(type);
                _typeCache[type] = component;
                AddChild(component);
                return (T)(IBaseNode)component;
            }

            return default;
        }

        /// <summary>
        /// 添加指定类型的子节点。如果已存在同类型节点则直接返回。
        /// <para>节点通过 <see cref="NodeFactory"/> 获取，支持缓存池复用。</para>
        /// </summary>
        /// <typeparam name="T">要添加的子节点类型，必须有无参构造函数。</typeparam>
        /// <returns>已添加或已存在的子节点。</returns>
        public T AddNode<T>() where T : BaseNode, new()
        {
            if (_typeCache.TryGetValue(typeof(T), out BaseNode node))
                return (T)node;

            T component = NodeFactory.GetNode<T>();
            _typeCache[typeof(T)] = component;
            AddChild(component);
            return component;
        }

        /// <summary>
        /// 添加指定类型的子节点，并传入初始化参数。如果已存在同类型节点则直接返回。
        /// <para>节点通过 <see cref="NodeFactory"/> 获取，支持缓存池复用。</para>
        /// </summary>
        /// <typeparam name="T">要添加的子节点类型，必须有无参构造函数。</typeparam>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已添加或已存在的子节点。</returns>
        public T AddNode<T>(object arg) where T : BaseNode, new()
        {
            if (_typeCache.TryGetValue(typeof(T), out BaseNode node))
                return (T)node;

            T component = NodeFactory.GetNode<T>(arg);
            _typeCache[typeof(T)] = component;
            AddChild(component);
            return component;
        }

        /// <summary>
        /// 通过运行时类型添加子节点。如果已存在同类型节点则直接返回。
        /// <para>节点通过 <see cref="NodeFactory"/> 获取，支持缓存池复用。</para>
        /// </summary>
        /// <param name="type">要添加的子节点类型，必须是 <see cref="BaseNode"/> 的子类。</param>
        /// <returns>已添加或已存在的子节点。</returns>
        /// <exception cref="ArgumentNullException">type 为 null。</exception>
        /// <exception cref="ArgumentException">type 不是 BaseNode 的子类。</exception>
        public BaseNode AddNode(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(BaseNode).IsAssignableFrom(type))
                throw new ArgumentException($"Add {type} failed, it is not a BaseNode");

            if (_typeCache.TryGetValue(type, out BaseNode node))
                return node;

            node = NodeFactory.GetNode(type);
            _typeCache[type] = node;
            AddChild(node);
            return node;
        }

        /// <summary>
        /// 通过运行时类型添加子节点，并传入初始化参数。如果已存在同类型节点则直接返回。
        /// <para>节点通过 <see cref="NodeFactory"/> 获取，支持缓存池复用。</para>
        /// </summary>
        /// <param name="type">要添加的子节点类型，必须是 <see cref="BaseNode"/> 的子类。</param>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已添加或已存在的子节点。</returns>
        /// <exception cref="ArgumentNullException">type 为 null。</exception>
        /// <exception cref="ArgumentException">type 不是 BaseNode 的子类。</exception>
        public BaseNode AddNode(Type type, object arg)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(BaseNode).IsAssignableFrom(type))
                throw new ArgumentException($"Add {type} failed, it is not a BaseNode");

            if (_typeCache.TryGetValue(type, out BaseNode node))
                return node;

            node = NodeFactory.GetNode(type, arg);
            _typeCache[type] = node;
            AddChild(node);
            return node;
        }

        /// <summary>
        /// 添加一个子节点，并立即执行异步启动。
        /// <para>节点会先挂入树（此时可访问父节点上的服务），再执行异步加载管线，
        /// 加载完成后自动触发 <see cref="BaseNode.Start"/>。</para>
        /// </summary>
        /// <typeparam name="T">子节点类型，必须有无参构造函数且实现 <see cref="IParentNode"/>。</typeparam>
        /// <returns>已添加并完成异步启动的子节点实例。</returns>
        public async UniTask<T> AddNodeAsync<T>() where T : BaseNode, IParentNode, new()
        {
            T node = NodeFactory.GetNode<T>();
            AddChild(node, deferStart: true);
            await ((IParentNode)node).StartupAsync();
            return node;
        }

        /// <summary>
        /// 添加一个子节点（带参数），并立即执行异步启动。
        /// </summary>
        /// <typeparam name="T">子节点类型，必须有无参构造函数且实现 <see cref="IParentNode"/>。</typeparam>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已添加并完成异步启动的子节点实例。</returns>
        public async UniTask<T> AddNodeAsync<T>(object arg) where T : BaseNode, IParentNode, new()
        {
            T node = NodeFactory.GetNode<T>(arg);
            AddChild(node, deferStart: true);
            await ((IParentNode)node).StartupAsync();
            return node;
        }

        /// <summary>
        /// 通过运行时类型添加一个子节点，并立即执行异步启动。
        /// <para>节点会先挂入树（此时可访问父节点上的服务），再执行异步加载管线，
        /// 加载完成后自动触发 <see cref="BaseNode.Start"/>。</para>
        /// </summary>
        /// <param name="type">子节点类型，必须是 <see cref="BaseNode"/> 的子类且实现 <see cref="IParentNode"/>。</param>
        /// <returns>已添加并完成异步启动的子节点实例。</returns>
        /// <exception cref="ArgumentNullException">type 为 null。</exception>
        /// <exception cref="ArgumentException">type 不是 BaseNode 的子类或未实现 IParentNode。</exception>
        public async UniTask<BaseNode> AddNodeAsync(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(BaseNode).IsAssignableFrom(type))
                throw new ArgumentException($"AddNodeAsync {type} failed, it is not a BaseNode");

            if (!typeof(IParentNode).IsAssignableFrom(type))
                throw new ArgumentException($"AddNodeAsync {type} failed, it does not implement IParentNode");

            BaseNode node = NodeFactory.GetNode(type);
            AddChild(node, deferStart: true);
            await ((IParentNode)node).StartupAsync();
            return node;
        }

        /// <summary>
        /// 通过运行时类型添加一个子节点（带参数），并立即执行异步启动。
        /// </summary>
        /// <param name="type">子节点类型，必须是 <see cref="BaseNode"/> 的子类且实现 <see cref="IParentNode"/>。</param>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已添加并完成异步启动的子节点实例。</returns>
        /// <exception cref="ArgumentNullException">type 为 null。</exception>
        /// <exception cref="ArgumentException">type 不是 BaseNode 的子类或未实现 IParentNode。</exception>
        public async UniTask<BaseNode> AddNodeAsync(Type type, object arg)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(BaseNode).IsAssignableFrom(type))
                throw new ArgumentException($"AddNodeAsync {type} failed, it is not a BaseNode");

            if (!typeof(IParentNode).IsAssignableFrom(type))
                throw new ArgumentException($"AddNodeAsync {type} failed, it does not implement IParentNode");

            BaseNode node = NodeFactory.GetNode(type, arg);
            AddChild(node, deferStart: true);
            await ((IParentNode)node).StartupAsync();
            return node;
        }

        /// <summary>
        /// 移除指定类型的子节点。
        /// </summary>
        /// <typeparam name="T">要移除的子节点类型。</typeparam>
        /// <returns>是否成功移除。</returns>
        public bool RemoveNode<T>() where T : IBaseNode
        {
            Type type = typeof(T);

            if (type.IsInterface)
            {
                if (_interfaceCache.TryGetValue(type, out var cached))
                {
                    _interfaceCache.Remove(type);

                    var node = cached as BaseNode;
                    if (node == null) return false;
                    RemoveFromAllCaches(node);
                    RemoveChild(node);
                    return true;
                }
                return false;
            }
            else
            {
                if (_typeCache.TryGetValue(type, out BaseNode node))
                {
                    _typeCache.Remove(type);
                    RemoveFromAllCaches(node);
                    RemoveChild(node);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 通过运行时类型移除子节点。
        /// </summary>
        /// <param name="type">要移除的子节点类型。</param>
        /// <returns>是否成功移除。</returns>
        public bool RemoveNode(Type type)
        {
            if (type == null) return false;

            if (type.IsInterface)
            {
                if (_interfaceCache.TryGetValue(type, out var cached))
                {
                    _interfaceCache.Remove(type);

                    var node = cached as BaseNode;
                    if (node == null) return false;
                    RemoveFromAllCaches(node);
                    RemoveChild(node);
                    return true;
                }
                return false;
            }
            else
            {
                if (_typeCache.TryGetValue(type, out BaseNode node))
                {
                    _typeCache.Remove(type);
                    RemoveFromAllCaches(node);
                    RemoveChild(node);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 移除指定的子节点实例。
        /// </summary>
        /// <param name="node">要移除的子节点。</param>
        /// <returns>是否成功移除。</returns>
        public bool RemoveNode(BaseNode node)
        {
            if (node == null) return false;

            RemoveFromAllCaches(node);
            RemoveChild(node);
            return true;
        }

        #endregion

        #region Internal Methods

        internal sealed override void AwakeInternal()
        {
            base.AwakeInternal();

            _typeCache = new Dictionary<Type, BaseNode>();
            _interfaceCache = new Dictionary<Type, IBaseNode>();
        }

        internal sealed override void StartInternal()
        {
            base.StartInternal();
        }

        internal sealed override void DestroyInternal()
        {
            _typeCache.Clear();
            _typeCache = null;
            _interfaceCache.Clear();
            _interfaceCache = null;

            base.DestroyInternal();
        }

        protected override void OnChildRemoved(BaseNode node, bool internalCall = true)
        {
            base.OnChildRemoved(node, internalCall);

            if (!internalCall)
            {
                RemoveFromAllCaches(node);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 从两个缓存中同时移除该节点相关的所有条目。
        /// </summary>
        /// <param name="node">要移除的节点。</param>
        private void RemoveFromAllCaches(BaseNode node)
        {
            // 从 _typeCache 中移除（按具体类型）
            Type concreteType = node.GetType();
            if (_typeCache.TryGetValue(concreteType, out var cachedNode) && cachedNode == node)
            {
                _typeCache.Remove(concreteType);
            }

            // 从 _interfaceCache 中移除所有指向该节点的条目
            // 使用手动遍历而非 LINQ 以避免分配
            List<Type> keysToRemove = null;
            foreach (var kvp in _interfaceCache)
            {
                if (kvp.Value == node)
                {
                    keysToRemove ??= new List<Type>();
                    keysToRemove.Add(kvp.Key);
                }
            }
            if (keysToRemove != null)
            {
                foreach (var key in keysToRemove)
                {
                    _interfaceCache.Remove(key);
                }
            }
        }

        #endregion
    }
}
