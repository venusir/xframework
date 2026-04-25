using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 字典节点，按自定义键（Key）缓存子节点，提供高效的键值对式访问。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    public abstract class DictionaryNode<TKey> : ParentNode
    {
        /// <summary>按键缓存的子节点字典。</summary>
        private Dictionary<TKey, BaseNode> _nodeCache = new Dictionary<TKey, BaseNode>();

        /// <summary>
        /// 添加子节点并关联指定键。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="key">关联的键。</param>
        /// <param name="node">要添加的子节点。</param>
        public void AddNode<T>(TKey key, T node) where T : BaseNode
        {
            if (node != null && !_nodeCache.ContainsKey(key))
            {
                _nodeCache[key] = node;
                AddChild(node);
            }
        }

        /// <summary>
        /// 移除指定键关联的子节点。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveNode(TKey key)
        {
            if (_nodeCache.TryGetValue(key, out BaseNode node))
            {
                _nodeCache.Remove(key);
                RemoveChild(node);
            }
        }

        /// <summary>
        /// 是否包含指定键。
        /// </summary>
        /// <param name="key">要检查的键。</param>
        /// <returns>如果存在该键则返回 true。</returns>
        public bool ContainsNode(TKey key)
        {
            return _nodeCache.ContainsKey(key);
        }

        /// <summary>
        /// 尝试获取指定键关联的指定类型子节点。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="key">要查找的键。</param>
        /// <param name="node">输出参数，找到的节点。</param>
        /// <returns>是否成功找到且类型匹配。</returns>
        public bool TryGetNode<T>(TKey key, out T node) where T : BaseNode
        {
            if (_nodeCache.TryGetValue(key, out var outNode))
            {
                if (outNode is T)
                {
                    node = outNode as T;
                    return true;
                }
                else
                {
                    UnityEngine.Debug.LogError($"DictionaryNode.TryGet: key {key} is not type of {typeof(T)}");
                }
            }
            node = null;
            return false;
        }

        /// <summary>
        /// 获取指定键关联的指定类型子节点。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="key">要查找的键。</param>
        /// <returns>找到的节点，未找到或类型不匹配则返回 null。</returns>
        public T GetNode<T>(TKey key) where T : BaseNode
        {
            if (_nodeCache.TryGetValue(key, out BaseNode node))
                return node as T;
            return null;
        }
    }
}
