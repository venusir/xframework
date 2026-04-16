using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    public abstract class DictionaryNode<TKey> : ParentNode
    {
        Dictionary<TKey, BaseNode> dicts = new Dictionary<TKey, BaseNode>();

        public void Add<T>(TKey key, T node) where T : BaseNode
        {
            if (node != null && !dicts.ContainsKey(key))
            {
                dicts[key] = node;
                AddChild(node);
            }
        }

        public void Remove(TKey key)
        {
            if (dicts.TryGetValue(key, out BaseNode node))
            {
                dicts.Remove(key);
                RemoveChild(node);
            }
        }

        public bool Contains(TKey key)
        {
            return dicts.ContainsKey(key);
        }

        public bool TryGet<T>(TKey key, out T node) where T : BaseNode
        {
            if (dicts.TryGetValue(key, out var outNode))
            {
                if (outNode is T)
                {
                    node = outNode as T;
                    return true;
                }
                else
                {
                    Debug.LogError($"DictionaryNode.TryGet: key {key} is not type of {typeof(T)}");
                }
            }
            node = null;
            return false;
        }

        public T Get<T>(TKey key) where T : BaseNode
        {
            if (dicts.TryGetValue(key, out BaseNode node))
                return node as T;
            return null;
        }
    }
}
