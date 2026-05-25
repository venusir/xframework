using XFramework.XNode;

namespace XFramework.XUpdate
{
    /// <summary>
    /// <see cref="UpdateManager"/> 的扩展方法。
    /// <para>提供对 <see cref="BaseNode"/> 的便捷注册与注销方法。</para>
    /// </summary>
    public static class UpdateManagerExtensions
    {
        /// <summary>
        /// 将节点注册到更新管理器。节点需实现 <see cref="IUpdateable"/> 接口。
        /// <para>通常由 <see cref="UpdateNode"/> 自动处理，仅在不使用节点树事件时手动调用。</para>
        /// </summary>
        /// <param name="node">要注册的节点。</param>
        /// <param name="initialLOD">初始 LOD 等级，默认为 <see cref="UpdateLOD.Frame1"/>。</param>
        public static void RegisterUpdate(this BaseNode node, UpdateLOD initialLOD = UpdateLOD.Frame1)
        {
            if (node is IUpdateable updateable)
            {
                UpdateManager.Register(updateable, node.Depth, initialLOD);
            }
        }

        /// <summary>
        /// 从更新管理器注销节点。
        /// <para>通常由 <see cref="UpdateNode"/> 自动处理，仅在不使用节点树事件时手动调用。</para>
        /// </summary>
        /// <param name="node">要注销的节点。</param>
        public static void UnregisterUpdate(this BaseNode node)
        {
            if (node is IUpdateable updateable)
            {
                UpdateManager.Unregister(updateable);
            }
        }
    }
}