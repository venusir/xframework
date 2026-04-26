using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 复合节点。
    /// <para>继承自 <see cref="ParentNode"/> 。</para>
    /// </summary>
    public abstract class CompositeNode : ParentNode
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
        }
    }
}
