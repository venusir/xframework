using System;
using System.Collections.Generic;

namespace XFramework.XCore
{

    /// <summary>
    /// 容器节点接口。提供子节点的添加和移除能力。
    /// </summary>
    public interface IContainerNode : IParentNode
    {
        /// <summary>添加子节点。</summary>
        void AddNode(BaseNode node);

        /// <summary>移除并销毁子节点。</summary>
        void RemoveNode(BaseNode node);
    }

    /// <summary>
    /// 容器节点。
    /// <para>继承自 <see cref="ParentNode"/>，对外暴露添加/移除子节点的公共 API。</para>
    /// </summary>
    public abstract class ContainerNode : ParentNode, IContainerNode
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
            if (RemoveChild(node))
            {
                node.Destroy();
            }
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
