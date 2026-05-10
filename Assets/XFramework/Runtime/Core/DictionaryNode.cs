using System.Collections.Generic;
using System;

namespace XFramework.XCore
{

    /// <summary>
    /// 字典节点接口。按自定义键（Key）缓存子节点，提供高效的键值对式访问。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    public interface IDictionaryNode<TKey> : IParentNode
    {
        /// <summary>子节点数量。</summary>
        int NodeCount { get; }

        /// <summary>所有键的集合。</summary>
        IEnumerable<TKey> Keys { get; }

        /// <summary>所有子节点的集合。</summary>
        IEnumerable<BaseNode> Values { get; }

        /// <summary>添加子节点并关联指定键。</summary>
        void AddNode<T>(TKey key, T node) where T : BaseNode;

        /// <summary>设置子节点并关联指定键。如果键已存在，会先移除旧的子节点再添加新的。</summary>
        void SetNode<T>(TKey key, T node) where T : BaseNode;

        /// <summary>移除指定键关联的子节点。</summary>
        void RemoveNode(TKey key);

        /// <summary>清空所有子节点。</summary>
        void ClearNodes();

        /// <summary>是否包含指定键。</summary>
        bool ContainsNode(TKey key);

        /// <summary>尝试获取指定键关联的子节点（非泛型版本）。</summary>
        bool TryGetNode(TKey key, out BaseNode node);

        /// <summary>尝试获取指定键关联的指定类型子节点。</summary>
        bool TryGetNode<T>(TKey key, out T node) where T : BaseNode;

        /// <summary>获取指定键关联的指定类型子节点。</summary>
        T GetNode<T>(TKey key) where T : BaseNode;
    }

    /// <summary>
    /// 字典节点，按自定义键（Key）缓存子节点，提供高效的键值对式访问。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    public abstract class DictionaryNode<TKey> : ParentNode, IDictionaryNode<TKey>
    {
        /// <summary>按键映射子节点的字典。</summary>
        private Dictionary<TKey, BaseNode> _nodeDict;

        /// <summary>子节点数量。</summary>
        public int NodeCount => _nodeDict.Count;

        /// <summary>所有键的集合。</summary>
        public IEnumerable<TKey> Keys => _nodeDict.Keys;

        /// <summary>所有子节点的集合。</summary>
        public IEnumerable<BaseNode> Values => _nodeDict.Values;

        /// <summary>
        /// 添加子节点并关联指定键。
        /// <para>如果键已存在，会输出警告并忽略本次操作。</para>
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="key">关联的键。</param>
        /// <param name="node">要添加的子节点。</param>
        public void AddNode<T>(TKey key, T node) where T : BaseNode
        {
            if (node == null)
            {
                UnityEngine.Debug.LogWarning($"DictionaryNode.AddNode: node is null, key '{key}'.");
                return;
            }

            if (_nodeDict.ContainsKey(key))
            {
                UnityEngine.Debug.LogWarning($"DictionaryNode.AddNode: key '{key}' already exists. Use SetNode to overwrite.");
                return;
            }

            _nodeDict[key] = node;
            AddChild(node);
        }

        /// <summary>
        /// 设置子节点并关联指定键。如果键已存在，会先移除并销毁旧的子节点再添加新的。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="key">关联的键。</param>
        /// <param name="node">要设置的子节点。</param>
        public void SetNode<T>(TKey key, T node) where T : BaseNode
        {
            if (node == null)
            {
                UnityEngine.Debug.LogWarning($"DictionaryNode.SetNode: node is null, key '{key}'.");
                return;
            }

            if (_nodeDict.TryGetValue(key, out var oldNode))
            {
                _nodeDict.Remove(key);
                RemoveChild(oldNode);
                oldNode.Destroy();
            }

            _nodeDict[key] = node;
            AddChild(node);
        }

        /// <summary>
        /// 移除指定键关联的子节点。移除后会自动销毁该子节点。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveNode(TKey key)
        {
            if (_nodeDict.TryGetValue(key, out BaseNode node))
            {
                _nodeDict.Remove(key);
                RemoveChild(node);
                node.Destroy();
            }
        }

        /// <summary>
        /// 清空所有子节点。
        /// </summary>
        public void ClearNodes()
        {
            // 收集所有键，避免遍历时修改字典
            var keys = new List<TKey>(_nodeDict.Keys);
            foreach (var key in keys)
            {
                RemoveNode(key);
            }
        }

        /// <summary>
        /// 是否包含指定键。
        /// </summary>
        /// <param name="key">要检查的键。</param>
        /// <returns>如果存在该键则返回 true。</returns>
        public bool ContainsNode(TKey key)
        {
            return _nodeDict.ContainsKey(key);
        }

        /// <summary>
        /// 尝试获取指定键关联的子节点（非泛型版本）。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <param name="node">输出参数，找到的节点。</param>
        /// <returns>是否成功找到。</returns>
        public bool TryGetNode(TKey key, out BaseNode node)
        {
            return _nodeDict.TryGetValue(key, out node);
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
            if (_nodeDict.TryGetValue(key, out var outNode))
            {
                node = outNode as T;
                if (node != null)
                    return true;

                UnityEngine.Debug.LogError($"DictionaryNode.TryGetNode: key '{key?.ToString() ?? "null"}' is not of type {typeof(T)}");
            }

            node = null;
            return false;
        }

        /// <summary>
        /// 获取指定键关联的指定类型子节点。
        /// <para>未找到或类型不匹配时返回 null，并输出警告日志。</para>
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="key">要查找的键。</param>
        /// <returns>找到的节点，未找到或类型不匹配则返回 null。</returns>
        public T GetNode<T>(TKey key) where T : BaseNode
        {
            if (_nodeDict.TryGetValue(key, out BaseNode node))
            {
                var result = node as T;
                if (result == null)
                {
                    UnityEngine.Debug.LogWarning($"DictionaryNode.GetNode: key '{key?.ToString() ?? "null"}' exists but is not of type {typeof(T)}");
                }
                return result;
            }

            return null;
        }

        #region Lifecycle Overrides

        internal sealed override void AwakeInternal()
        {
            base.AwakeInternal();

            _nodeDict = new Dictionary<TKey, BaseNode>();
        }

        internal sealed override void StartInternal()
        {
            base.StartInternal();
        }

        /// <summary>
        /// 节点销毁时的回调。清理字典。
        /// </summary>
        internal sealed override void DestroyInternal()
        {
            _nodeDict.Clear();
            _nodeDict = null;

            base.DestroyInternal();
        }

        protected override void OnChildRemoved(BaseNode node, bool fromChild = false)
        {
            base.OnChildRemoved(node, fromChild);

            if (fromChild)
            {
                RemoveFromAllNodes(node);
            }
        }

        #endregion

        void RemoveFromAllNodes(BaseNode node)
        {
            // 从 _nodeDict 中移除所有指向该节点的条目
            // 使用手动遍历而非 LINQ 以避免分配
            List<TKey> keysToRemove = null;
            foreach (var kvp in _nodeDict)
            {
                if (kvp.Value == node)
                {
                    keysToRemove ??= new List<TKey>();
                    keysToRemove.Add(kvp.Key);
                }
            }
            if (keysToRemove != null)
            {
                foreach (var key in keysToRemove)
                {
                    _nodeDict.Remove(key);
                }
            }
        }
    }
}
