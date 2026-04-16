using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    public abstract class CompositeNode : ParentNode
    {
        public void Add(BaseNode node)
        {
            if (node == null) return;
            if (children.Contains(node)) return;

            AddChild(node);
        }

        public void Remove(BaseNode node)
        {
            if (node == null) return;
            if (!children.Contains(node)) return;

            RemoveChild(node);
        }

        public T Get<T>(System.Predicate<T> predicate = null) where T : BaseNode
        {
            foreach (var child in children)
            {
                if (child is T && (predicate == null || predicate((T)child)))
                    return (T)child;
            }
            return null;
        }

        public void Find<T>(List<T> list, System.Predicate<T> predicate = null) where T : BaseNode
        {
            if (this is T node && (predicate == null || predicate(node)))
                list.Add(node);

            foreach (var child in children)
            {
                if (child is T && (predicate == null || predicate((T)child)))
                    list.Add((T)child);
            }
        }

        public List<T> Find<T>(System.Predicate<T> predicate = null) where T : BaseNode
        {
            List<T> list = new List<T>();

            if (this is T node && (predicate == null || predicate(node)))
                list.Add(node);

            foreach (var child in children)
            {
                if (child is T && (predicate == null || predicate((T)child)))
                    list.Add((T)child);
            }

            return list;
        }

        public void FindAll<T>(List<T> list, System.Predicate<T> predicate = null) where T : BaseNode
        {
            if (this is T node && (predicate == null || predicate(node)))
                list.Add(node);

            foreach (var child in children)
            {
                FindAll(list, predicate);
            }
        }

        public List<T> FindAll<T>(System.Predicate<T> predicate = null) where T : BaseNode
        {
            List<T> list = new List<T>();

            if (this is T node && (predicate == null || predicate(node)))
                list.Add(node);

            foreach (var child in children)
            {
                FindAll(list, predicate);
            }
            return list;
        }
    }
}
