using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    public abstract class EntityNode : ParentNode
    {
        Dictionary<Type, BaseNode> dicts = new Dictionary<Type, BaseNode>();

        public T Get<T>() where T : BaseNode
        {
            if (dicts.TryGetValue(typeof(T), out var node) && node is T)
                return (T)node;

            foreach (var child in children)
            {
                if (child is T)
                {
                    dicts[typeof(T)] = child as T;
                    return (T)child;
                }
            }
            return null;
        }

        public T Add<T>() where T : BaseNode, new()
        {
            if (dicts.TryGetValue(typeof(T), out BaseNode node))
                return (T)node;

            T component = new T();
            dicts[typeof(T)] = component;
            AddChild(component);
            return component;
        }

        public bool Remove<T>() where T : BaseNode
        {
            if (dicts.TryGetValue(typeof(T), out BaseNode node))
            {
                dicts.Remove(typeof(T));
                RemoveChild(node);
                return true;
            }
            return false;
        }

        public BaseNode Add(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(BaseNode).IsAssignableFrom(type))
                throw new ArgumentException($"Add {type} failed, it is not a BaseNode");

            if (dicts.TryGetValue(type, out BaseNode node))
                return node;

            node = Activator.CreateInstance(type) as BaseNode;
            dicts[type] = node;
            AddChild(node);
            return node;
        }

        public bool Remove(BaseNode node)
        {
            if (node == null) return false;
            if (dicts.ContainsKey(node.GetType()))
            {
                dicts.Remove(node.GetType());
            }
            RemoveChild(node);
            return true;
        }
    }
}
