using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 复合节点接口。提供子节点的添加和移除能力。
    /// </summary>
    public interface ICompositeNode : IParentNode
    {
        /// <summary>添加子节点。</summary>
        void AddNode(BaseNode node);

        /// <summary>移除并销毁子节点。</summary>
        void RemoveNode(BaseNode node);
    }

    /// <summary>
    /// 复合节点。
    /// <para>继承自 <see cref="ParentNode"/> 。</para>
    /// </summary>
    public abstract class CompositeNode : ParentNode, ICompositeNode
    {
        /// <summary>
        /// 添加子节点。
        /// </summary>
        /// <param name="node">要添加的子节点。</param>
        public void AddNode(BaseNode node)
        {
            AddChild(node);
        }

        /// <summary>
        /// 移除并销毁子节点。
        /// </summary>
        /// <param name="node">要移除的子节点。</param>
        public void RemoveNode(BaseNode node)
        {
            RemoveChild(node);
            node.Destroy();
        }

        internal sealed override void AwakeInternal()
        {
            base.AwakeInternal();
        }

        internal sealed override void StartInternal()
        {
            base.StartInternal();
        }

        internal sealed override void DestroyInternal()
        {
            base.DestroyInternal();
        }
    }
}
