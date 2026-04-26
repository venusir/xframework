using System.Collections.Generic;
using System;

namespace XFramework
{
    /// <summary>
    /// 可包含子节点的抽象基类。
    /// <para>管理子节点列表，提供添加/移除子节点的功能，并在销毁时递归销毁所有子节点。</para>
    /// </summary>
    public abstract class ParentNode : BaseNode
    {
        public event Action<BaseNode> OnNodeAdded;
        public event Action<BaseNode> OnNodeRemoved;

        /// <summary>子节点列表。</summary>
        List<BaseNode> children;

        /// <summary>子节点数量。</summary>
        public int ChildCount => children.Count;

        /// <summary>按索引访问子节点。</summary>
        /// <param name="index">子节点索引。</param>
        /// <returns>指定索引处的子节点。</returns>
        public BaseNode this[int index] => children[index];

        /// <summary>
        /// 获取第一个指定类型的子节点。
        /// </summary>
        /// <typeparam name="T">子节点类型。</typeparam>
        /// <returns>第一个匹配的子节点，未找到则返回 null。</returns>
        public T GetNode<T>() where T : BaseNode
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
        /// <param name="predicate">筛选条件。为 null 时等同于 <see cref="GetNode{T}"/>。</param>
        /// <returns>第一个匹配的子节点，未找到则返回 null。</returns>
        public T GetNode<T>(Predicate<T> predicate) where T : BaseNode
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
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
        /// 获取所有匹配指定类型的子节点，并填充到指定列表中。
        /// <para>此方法允许调用方复用已有的 <see cref="List{T}"/> 实例以减少 GC 分配。</para>
        /// </summary>
        /// <typeparam name="T">要查找的子节点类型。</typeparam>
        /// <param name="nodes">用于存储匹配结果的列表。不能为 null。</param>
        /// <param name="recursive">是否递归查找所有子孙节点。</param>
        /// <param name="predicate">筛选条件。为 null 时匹配所有指定类型的节点。</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodes"/> 为 null 时抛出。</exception>
        public void GetNodes<T>(List<T> nodes, bool recursive, Predicate<T> predicate = null) where T : BaseNode
        {
            if (nodes == null)
                throw new ArgumentNullException(nameof(nodes));

            foreach (var child in children)
            {
                if (child is T node && (predicate == null || predicate(node)))
                {
                    nodes.Add(node);
                }

                if (recursive && child is ParentNode parentNode)
                {
                    CollectNodesRecursive(parentNode, nodes, recursive, predicate);
                }
            }
        }

        /// <summary>
        /// 获取所有匹配指定类型的子节点。
        /// <para>创建一个新的 <see cref="List{T}"/> 并返回所有匹配的子节点。</para>
        /// </summary>
        /// <typeparam name="T">要查找的子节点类型。</typeparam>
        /// <param name="recursive">是否递归查找所有子孙节点。</param>
        /// <param name="predicate">筛选条件。为 null 时匹配所有指定类型的节点。</param>
        /// <returns>包含所有匹配子节点的列表。未找到时返回空列表。</returns>
        public List<T> GetNodes<T>(bool recursive, Predicate<T> predicate = null) where T : BaseNode
        {
            List<T> nodes = new List<T>();
            GetNodes(nodes, recursive, predicate);
            return nodes;
        }

        /// <summary>
        /// 添加子节点。添加前会自动调用 <see cref="BaseNode.Awake"/> 初始化该节点。
        /// <para>如果父节点已执行过 <see cref="BaseNode.Start"/>，则新添加的子节点会立即自动调用 <see cref="BaseNode.Start"/>。</para>
        /// </summary>
        /// <param name="node">要添加的子节点。</param>
        internal void AddChild(BaseNode node)
        {
            if (node != null && !children.Contains(node))
            {
                node.Awake();
                children.Add(node);
                node.SetParent(this);
                OnChildAdded(node);
                OnNodeAdded?.Invoke(node);

                // 如果父节点已 Start，新子节点应自动 Start（递归传播给其子节点）
                if (Started)
                {
                    node.Start();
                }
            }
        }

        /// <summary>
        /// 移除子节点。移除后会自动销毁该子节点。
        /// <para>注意：如果仅需从当前父节点移除而不销毁，请使用 <see cref="DetachChild"/>。</para>
        /// </summary>
        /// <param name="node">要移除并销毁的子节点。</param>
        internal void RemoveChild(BaseNode node, bool internalCall = true)
        {
            if (node != null && children.Contains(node))
            {
                children.Remove(node);
                OnNodeRemoved?.Invoke(node);
                OnChildRemoved(node, internalCall);
                node.Destroy();
            }
        }

        /// <summary>
        /// 递归收集指定父节点下所有匹配的子节点。
        /// </summary>
        /// <typeparam name="T">要查找的子节点类型。</typeparam>
        /// <param name="parent">要遍历的父节点。</param>
        /// <param name="recursive">是否继续向下递归。</param>
        /// <param name="predicate">筛选条件。为 null 时匹配所有指定类型的节点。</param>
        /// <param name="nodes">用于存储匹配结果的列表。</param>
        void CollectNodesRecursive<T>(ParentNode parent, List<T> nodes, bool recursive, Predicate<T> predicate) where T : BaseNode
        {
            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent[i];

                if (child is T node && (predicate == null || predicate(node)))
                {
                    nodes.Add(node);
                }

                if (recursive && child is ParentNode childParent)
                {
                    CollectNodesRecursive(childParent, nodes, recursive, predicate);
                }
            }
        }

        #region Lifecycle Overrides

        /// <summary>
        /// 子节点添加时的回调。
        /// </summary>
        /// <param name="node">被添加的子节点。</param>
        protected virtual void OnChildAdded(BaseNode node) { }

        /// <summary>
        /// 子节点移除时的回调。
        /// </summary>
        /// <param name="node">被移除的子节点。</param>
        protected virtual void OnChildRemoved(BaseNode node, bool internalCall = true) { }

        /// <summary>
        /// 内部初始化方法。初始化子节点列表。
        /// </summary>
        internal override void AwakeInternal()
        {
            base.AwakeInternal();

            children = new List<BaseNode>();
        }

        /// <summary>
        /// 内部启动方法。先递归启动所有子节点，再调用自身的 OnStart。
        /// <para>传播顺序与 Unity 语义一致：先子节点后父节点。</para>
        /// </summary>
        internal override void StartInternal()
        {
            for (int i = 0; i < children.Count; i++)
            {
                children[i].Start();
            }

            base.StartInternal();
        }

        /// <summary>
        /// 内部销毁方法。递归销毁所有子节点后清理列表。
        /// </summary>
        internal override void DestroyInternal()
        {
            foreach (var child in children)
            {
                child.DestroyInternal();
            }
            children.Clear();
            children = null;

            base.DestroyInternal();
        }

        #endregion
    }
}
