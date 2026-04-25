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
        /// <summary>子节点数量。</summary>
        public int ChildCount => children.Count;

        /// <summary>按索引访问子节点。</summary>
        /// <param name="index">子节点索引。</param>
        /// <returns>指定索引处的子节点。</returns>
        public BaseNode this[int index] => children[index];

        /// <summary>子节点列表。</summary>
        protected List<BaseNode> children;

        /// <summary>
        /// 添加子节点。添加前会自动调用 <see cref="BaseNode.Initialize"/> 初始化该节点。
        /// </summary>
        /// <param name="node">要添加的子节点。</param>
        internal void AddChild(BaseNode node)
        {
            if (node != null && !children.Contains(node))
            {
                node.Initialize();
                children.Add(node);
                node.SetParent(this);
                OnChildAdded(node);
            }
        }

        /// <summary>
        /// 移除子节点。移除后会自动销毁该子节点。
        /// <para>注意：如果仅需从当前父节点移除而不销毁，请使用 <see cref="DetachChild"/>。</para>
        /// </summary>
        /// <param name="node">要移除并销毁的子节点。</param>
        internal void RemoveChild(BaseNode node)
        {
            if (node != null && children.Contains(node))
            {
                children.Remove(node);
                OnChildRemoved(node);
                node.Destroy();
            }
        }

        /// <summary>
        /// 将子节点从当前父节点分离，但不销毁。
        /// <para>分离后子节点的 Parent 会被置为 null，成为根节点。</para>
        /// </summary>
        /// <param name="node">要分离的子节点。</param>
        internal void DetachChild(BaseNode node)
        {
            if (node != null && children.Contains(node))
            {
                children.Remove(node);
                node.SetParent(null);
                OnChildRemoved(node);
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
        protected virtual void OnChildRemoved(BaseNode node) { }

        /// <summary>
        /// 内部初始化方法。初始化子节点列表。
        /// </summary>
        internal override void AwakeInternal()
        {
            base.AwakeInternal();

            children = new List<BaseNode>();
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
