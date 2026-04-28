using System;

namespace XFramework
{
    /// <summary>
    /// 根节点。作为节点树的入口，提供便捷的节点创建方法。
    /// </summary>
    public class RootNode : EntityNode
    {
        #region Static Methods

        /// <summary>
        /// 创建根节点。
        /// <para>通过 <see cref="NodeFactory"/> 获取实例并自动调用 <see cref="BaseNode.Awake"/> 完成初始化。</para>
        /// </summary>
        /// <returns>已初始化的根节点实例。</returns>
        public static RootNode Create()
        {
            RootNode root = NodeFactory.GetNode<RootNode>();
            root.Awake();
            return root;
        }

        /// <summary>
        /// 创建根节点，并传入初始化参数。
        /// <para>通过 <see cref="NodeFactory"/> 获取实例，设置参数后自动调用 <see cref="BaseNode.Awake"/> 完成初始化。</para>
        /// </summary>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已初始化的根节点实例。</returns>
        public static RootNode Create(object arg)
        {
            RootNode root = NodeFactory.GetNode<RootNode>(arg);
            root.Awake();
            return root;
        }

        #endregion
    }
}
