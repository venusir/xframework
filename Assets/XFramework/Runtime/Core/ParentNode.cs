using System.Collections.Generic;
using System;

namespace XFramework.XCore
{

    /// <summary>
    /// 可包含子节点的接口。提供只读的子节点访问能力。
    /// <para>外部代码应通过此接口访问 <see cref="ParentNode"/> 的公共只读 API。</para>
    /// </summary>
    public interface IParentNode : IBaseNode
    {
        /// <summary>子节点数量。</summary>
        int ChildCount { get; }

        /// <summary>按索引访问子节点。</summary>
        BaseNode this[int index] { get; }

        /// <summary>直接子节点添加事件。</summary>
        event Action<BaseNode> OnNodeAdded;

        /// <summary>直接子节点移除事件。</summary>
        event Action<BaseNode> OnNodeRemoved;

        /// <summary>任意子孙节点添加事件（递归冒泡）。</summary>
        event Action<BaseNode> OnDescendantAdded;

        /// <summary>任意子孙节点移除事件（递归冒泡）。</summary>
        event Action<BaseNode> OnDescendantRemoved;

        /// <summary>任意子孙节点 Start 完成事件（递归冒泡）。</summary>
        event Action<BaseNode> OnDescendantStarted;

        /// <summary>获取遍历子节点的迭代器，支持 foreach 语法。</summary>
        IEnumerator<BaseNode> GetEnumerator();

        /// <summary>获取第一个指定类型的子节点。</summary>
        T GetNode<T>() where T : BaseNode;

        /// <summary>获取第一个满足条件的指定类型子节点。</summary>
        T GetNode<T>(Predicate<T> predicate) where T : BaseNode;

        /// <summary>获取所有匹配指定类型的子节点，并填充到指定列表中。</summary>
        void GetNodes<T>(List<T> nodes, bool recursive, Predicate<T> predicate = null) where T : BaseNode;

        /// <summary>获取所有匹配指定类型的子节点。</summary>
        List<T> GetNodes<T>(bool recursive, Predicate<T> predicate = null) where T : BaseNode;

        /// <summary>遍历所有子节点并执行回调，支持递归。</summary>
        void ForEach(Action<BaseNode> callback, bool recursive = false);
    }

    /// <summary>
    /// 可包含子节点的抽象基类。
    /// <para>管理子节点列表，提供添加/移除子节点的功能，并在销毁时递归销毁所有子节点。</para>
    /// </summary>
    public abstract class ParentNode : BaseNode, IParentNode
    {
        public event Action<BaseNode> OnNodeAdded;
        public event Action<BaseNode> OnNodeRemoved;
        public event Action<BaseNode> OnDescendantAdded;
        public event Action<BaseNode> OnDescendantRemoved;
        public event Action<BaseNode> OnDescendantStarted;

        /// <summary>子节点列表。</summary>
        List<BaseNode> children;

        /// <summary>子节点数量。</summary>
        public int ChildCount => children.Count;

        /// <summary>按索引访问子节点。</summary>
        /// <param name="index">子节点索引。</param>
        /// <returns>指定索引处的子节点。</returns>
        public BaseNode this[int index] => children[index];

        /// <summary>
        /// 获取遍历子节点的迭代器，支持 foreach 语法。
        /// </summary>
        /// <returns>子节点迭代器。</returns>
        public IEnumerator<BaseNode> GetEnumerator() => children.GetEnumerator();

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
        /// 遍历所有子节点并执行回调，支持递归。
        /// </summary>
        /// <param name="callback">对每个子节点执行的回调。</param>
        /// <param name="recursive">是否递归遍历所有子孙节点。</param>
        public void ForEach(Action<BaseNode> callback, bool recursive = false)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                callback(child);

                if (recursive && child is ParentNode parentNode)
                {
                    parentNode.ForEach(callback, recursive: true);
                }
            }
        }

        /// <summary>
        /// 添加子节点。添加前会自动调用 <see cref="BaseNode.Awake"/> 初始化该节点。
        /// <para>如果父节点已执行过 <see cref="BaseNode.Start"/>，则新添加的子节点会立即自动调用 <see cref="BaseNode.Start"/>。</para>
        /// </summary>
        /// <param name="node">要添加的子节点。</param>
        /// <param name="deferStart">是否延迟 Start 调用。为 true 时，即使父节点已 Start 也不会立即调用子节点的 Start，
        /// 适用于需要先异步加载再触发 Start 的场景。</param>
        internal void AddChild(BaseNode node, bool deferStart = false)
        {
            if (node != null && !children.Contains(node))
            {
                // 防止将已销毁的节点添加到树中
                if (node.Destroyed)
                {
                    UnityEngine.Debug.LogWarning($"ParentNode.AddChild: node {node.GetType().Name} is already destroyed, ignoring.");
                    return;
                }

                node.Awake();
                node.SetParent(this);
                children.Add(node);
                OnChildAdded(node);
                OnNodeAdded?.Invoke(node);
                OnDescendantAdded?.Invoke(node);

                // 如果父节点已 Start，新子节点应自动 Start（递归传播给其子节点）
                // deferStart 为 true 时跳过，由调用方在异步加载完成后手动触发 Start
                if (Started && !deferStart)
                {
                    node.Start();
                }
            }
        }

        /// <summary>
        /// 从父节点的子节点列表中移除指定节点。
        /// <para>此方法仅负责从列表移除和事件通知，不调用节点的销毁逻辑。</para>
        /// <para>节点的销毁由调用方通过 <see cref="BaseNode.Destroy"/> 或 <see cref="DestroyInternal"/> 负责。</para>
        /// </summary>
        /// <param name="node">要移除的子节点。</param>
        /// <param name="fromChild">是否为子节点自身触发的移除（即子节点调用 Destroy 时）。</param>
        internal void RemoveChild(BaseNode node, bool fromChild = false)
        {
            if (node != null && children.Contains(node))
            {
                children.Remove(node);
                OnNodeRemoved?.Invoke(node);
                OnDescendantRemoved?.Invoke(node);
                OnChildRemoved(node, fromChild);
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
        /// <para>订阅子节点的 <see cref="BaseNode.OnNodeStarted"/> 事件，当子节点 Start 完成时触发自身的 <see cref="OnDescendantStarted"/> 冒泡。</para>
        /// <para>如果子节点是 <see cref="ParentNode"/>，额外订阅其 <see cref="OnDescendantAdded"/>、<see cref="OnDescendantRemoved"/> 和 <see cref="OnDescendantStarted"/> 事件，
        /// 实现递归冒泡，使祖先节点也能收到所有子孙节点的添加/移除/Start 通知。</para>
        /// </summary>
        /// <param name="node">被添加的子节点。</param>
        protected virtual void OnChildAdded(BaseNode node)
        {
            node.OnNodeStarted += RelayNodeStarted;

            if (node is ParentNode parentNode)
            {
                parentNode.OnDescendantAdded += RelayDescendantAdded;
                parentNode.OnDescendantRemoved += RelayDescendantRemoved;
                parentNode.OnDescendantStarted += RelayDescendantStarted;
            }
        }

        /// <summary>
        /// 子节点移除时的回调。
        /// <para>取消订阅子节点的事件。</para>
        /// </summary>
        /// <param name="node">被移除的子节点。</param>
        /// <param name="fromChild">是否为子节点自身触发的移除（即子节点调用 Destroy 时）。</param>
        protected virtual void OnChildRemoved(BaseNode node, bool fromChild = false)
        {
            node.OnNodeStarted -= RelayNodeStarted;

            if (node is ParentNode parentNode)
            {
                parentNode.OnDescendantAdded -= RelayDescendantAdded;
                parentNode.OnDescendantRemoved -= RelayDescendantRemoved;
                parentNode.OnDescendantStarted -= RelayDescendantStarted;
            }
        }

        /// <summary>
        /// 中继子节点的 <see cref="BaseNode.OnNodeStarted"/> 事件，向上冒泡为 <see cref="OnDescendantStarted"/>。
        /// </summary>
        void RelayNodeStarted(BaseNode node) => OnDescendantStarted?.Invoke(node);

        /// <summary>
        /// 中继子 <see cref="ParentNode"/> 的 <see cref="OnDescendantAdded"/> 事件，向上冒泡。
        /// </summary>
        void RelayDescendantAdded(BaseNode node) => OnDescendantAdded?.Invoke(node);

        /// <summary>
        /// 中继子 <see cref="ParentNode"/> 的 <see cref="OnDescendantRemoved"/> 事件，向上冒泡。
        /// </summary>
        void RelayDescendantRemoved(BaseNode node) => OnDescendantRemoved?.Invoke(node);

        /// <summary>
        /// 中继子 <see cref="ParentNode"/> 的 <see cref="OnDescendantStarted"/> 事件，向上冒泡。
        /// </summary>
        void RelayDescendantStarted(BaseNode node) => OnDescendantStarted?.Invoke(node);

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
        /// <para>子节点的 <see cref="BaseNode.Destroy"/> 会自动触发 <see cref="RemoveChild"/>，
        /// 因此无需在此处手动调用 <see cref="RemoveChild"/>，避免重复事件触发。</para>
        /// <para>使用反向遍历避免额外的堆分配（无需复制列表快照）。</para>
        /// </summary>
        internal override void DestroyInternal()
        {
            // 反向遍历：从后往前销毁，已销毁的元素不会影响前面未遍历元素的索引
            // child.Destroy() 内部会自动调用 RemoveChild(this, fromChild: true)，
            // 触发 OnNodeRemoved 事件，因此无需在此处手动 RemoveChild
            for (int i = children.Count - 1; i >= 0; i--)
            {
                children[i].Destroy();
            }
            children.Clear();
            children = null;

            base.DestroyInternal();
        }

        #endregion
    }
}
