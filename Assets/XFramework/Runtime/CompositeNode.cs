using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 复合节点，提供类型安全的子节点查询和遍历功能。
    /// <para>继承自 <see cref="ParentNode"/>，支持按类型查找子节点、递归查找等操作。</para>
    /// </summary>
    public abstract class CompositeNode : ParentNode
    {
        /// <summary>
        /// 添加子节点。
        /// </summary>
        /// <param name="node">要添加的子节点。</param>
        public void Add(BaseNode node)
        {
            AddChild(node);
        }

        /// <summary>
        /// 移除并销毁子节点。
        /// </summary>
        /// <param name="node">要移除的子节点。</param>
        public void Remove(BaseNode node)
        {
            RemoveChild(node);
        }

        /// <summary>
        /// 获取第一个指定类型的子节点。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <returns>第一个匹配的子节点，未找到则返回 null。</returns>
        public T GetChild<T>() where T : BaseNode
        {
            foreach (var child in children)
            {
                if (child is T node)
                    return node;
            }
            return null;
        }

        /// <summary>
        /// 获取第一个满足条件的指定类型子节点。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="predicate">筛选条件。为 null 时等同于 <see cref="GetChild{T}"/>。</param>
        /// <returns>第一个匹配的子节点，未找到则返回 null。</returns>
        public T GetChild<T>(Predicate<T> predicate) where T : BaseNode
        {
            if (predicate == null)
            {
                return GetChild<T>();
            }

            foreach (var child in children)
            {
                if (child is T node && predicate(node))
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取所有指定类型的子节点，写入外部列表。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="outList">输出列表，内容会被清空后填充。</param>
        public void GetChildren<T>(ref List<T> outList) where T : BaseNode
        {
            outList.Clear();
            foreach (var child in children)
            {
                if (child is T node)
                {
                    outList.Add(node);
                }
            }
        }

        /// <summary>
        /// 获取所有满足条件的指定类型子节点，写入外部列表。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <param name="outList">输出列表，内容会被清空后填充。</param>
        /// <param name="predicate">筛选条件。为 null 时等同于 <see cref="GetChildren{T}(ref List{T})"/>。</param>
        public void GetChildren<T>(ref List<T> outList, Predicate<T> predicate) where T : BaseNode
        {
            if (predicate == null)
            {
                GetChildren(ref outList);
            }
            else
            {
                foreach (var child in children)
                {
                    if (child is T node && predicate(node))
                    {
                        outList.Add(node);
                    }
                }
            }
        }

        /// <summary>
        /// 查找当前节点及直接子节点中所有指定类型的节点，写入外部列表。
        /// <para>注意：此方法仅查找当前层级（自身 + 直接子节点），不递归。</para>
        /// </summary>
        /// <typeparam name="T">节点类型。</typeparam>
        /// <param name="list">输出列表。</param>
        /// <param name="predicate">筛选条件。为 null 时匹配所有指定类型。</param>
        public void Find<T>(List<T> list, Predicate<T> predicate = null) where T : BaseNode
        {
            if (this is T node && (predicate == null || predicate(node)))
                list.Add(node);

            foreach (var child in children)
            {
                if (child is T && (predicate == null || predicate((T)child)))
                    list.Add((T)child);
            }
        }

        /// <summary>
        /// 查找当前节点及直接子节点中所有指定类型的节点，返回新列表。
        /// <para>注意：此方法仅查找当前层级（自身 + 直接子节点），不递归。</para>
        /// </summary>
        /// <typeparam name="T">节点类型。</typeparam>
        /// <param name="predicate">筛选条件。为 null 时匹配所有指定类型。</param>
        /// <returns>匹配的节点列表。</returns>
        public List<T> Find<T>(Predicate<T> predicate = null) where T : BaseNode
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

        /// <summary>
        /// 递归查找所有指定类型的节点，写入外部列表。
        /// </summary>
        /// <typeparam name="T">节点类型。</typeparam>
        /// <param name="list">输出列表。</param>
        /// <param name="predicate">筛选条件。为 null 时匹配所有指定类型。</param>
        public void FindAll<T>(List<T> list, Predicate<T> predicate = null) where T : BaseNode
        {
            if (this is T node && (predicate == null || predicate(node)))
                list.Add(node);

            foreach (var child in children)
            {
                if (child is CompositeNode compositeNode)
                {
                    compositeNode.FindAll(list, predicate);
                }
                else if (child is T && (predicate == null || predicate((T)child)))
                {
                    list.Add((T)child);
                }
            }
        }

        /// <summary>
        /// 递归查找所有指定类型的节点，返回新列表。
        /// </summary>
        /// <typeparam name="T">节点类型。</typeparam>
        /// <param name="predicate">筛选条件。为 null 时匹配所有指定类型。</param>
        /// <returns>匹配的节点列表。</returns>
        public List<T> FindAll<T>(Predicate<T> predicate = null) where T : BaseNode
        {
            List<T> list = new List<T>();

            if (this is T node && (predicate == null || predicate(node)))
                list.Add(node);

            foreach (var child in children)
            {
                if (child is CompositeNode compositeNode)
                {
                    compositeNode.FindAll(list, predicate);
                }
                else if (child is T && (predicate == null || predicate((T)child)))
                {
                    list.Add((T)child);
                }
            }

            return list;
        }
    }
}
