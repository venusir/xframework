using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 实体节点，按类型（Type）缓存子节点，提供高效的组件式访问。
    /// <para>类似于 Unity 的 GetComponent/AddComponent 模式，但基于纯 C# 节点树实现。</para>
    /// </summary>
    public abstract class EntityNode : ParentNode
    {
        #region Private Fields

        /// <summary>按具体类类型缓存的子节点字典。</summary>
        private Dictionary<Type, BaseNode> _typeCache;

        /// <summary>按接口类型缓存的子节点字典。</summary>
        private Dictionary<Type, IBaseNode> _interfaceCache;

        #endregion

        #region Public Methods

        /// <summary>
        /// 获取指定类型的子节点。优先从缓存中查找，未命中则遍历子节点。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <returns>匹配的子节点，未找到则返回 null。</returns>
        public T GetComponent<T>() where T : IBaseNode
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

            return default;
        }

        /// <summary>
        /// 添加指定类型的子节点。如果已存在同类型节点则直接返回。
        /// </summary>
        /// <typeparam name="T">要创建的子节点类型，必须有无参构造函数。</typeparam>
        /// <returns>创建或已存在的子节点。</returns>
        public T AddComponent<T>() where T : BaseNode, new()
        {
            if (_typeCache.TryGetValue(typeof(T), out BaseNode node))
                return (T)node;

            T component = new T();
            _typeCache[typeof(T)] = component;
            AddChild(component);
            return component;
        }

        /// <summary>
        /// 移除指定类型的子节点。
        /// </summary>
        /// <typeparam name="T">要移除的子节点类型。</typeparam>
        /// <returns>是否成功移除。</returns>
        public bool RemoveComponent<T>() where T : IBaseNode
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
        /// 通过运行时类型添加子节点。
        /// </summary>
        /// <param name="type">要创建的子节点类型，必须是 <see cref="BaseNode"/> 的子类。</param>
        /// <returns>创建或已存在的子节点。</returns>
        /// <exception cref="ArgumentNullException">type 为 null。</exception>
        /// <exception cref="ArgumentException">type 不是 BaseNode 的子类。</exception>
        public BaseNode AddComponent(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(BaseNode).IsAssignableFrom(type))
                throw new ArgumentException($"Add {type} failed, it is not a BaseNode");

            if (_typeCache.TryGetValue(type, out BaseNode node))
                return node;

            node = Activator.CreateInstance(type) as BaseNode;
            _typeCache[type] = node;
            AddChild(node);
            return node;
        }

        /// <summary>
        /// 移除指定的子节点实例。
        /// </summary>
        /// <param name="node">要移除的子节点。</param>
        /// <returns>是否成功移除。</returns>
        public bool RemoveComponent(BaseNode node)
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
