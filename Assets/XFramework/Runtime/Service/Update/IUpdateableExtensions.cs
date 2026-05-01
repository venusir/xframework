namespace XFramework
{
    /// <summary>
    /// <see cref="IUpdateable"/> 的扩展方法，提供在节点上直接调用启用/禁用 Update 的能力。
    /// <para>内部通过 <see cref="UpdateBinder"/> 实现，需要在游戏启动时调用 <see cref="SetBinder(UpdateBinder)"/> 注册。</para>
    /// </summary>
    public static class IUpdateableExtensions
    {
        static UpdateBinder _binder;

        /// <summary>
        /// 设置全局 <see cref="UpdateBinder"/> 实例。
        /// <para>通常在 <see cref="GameLauncher.Awake"/> 中调用。</para>
        /// </summary>
        /// <param name="binder">更新绑定器实例。</param>
        internal static void SetBinder(UpdateBinder binder)
        {
            _binder = binder;
        }

        /// <summary>
        /// 启用当前节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnEnable"/>。</para>
        /// </summary>
        /// <param name="node">当前节点。</param>
        public static void EnableUpdate(this IUpdateable node)
        {
            _binder?.EnableNode(node);
        }

        /// <summary>
        /// 禁用当前节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// </summary>
        /// <param name="node">当前节点。</param>
        public static void DisableUpdate(this IUpdateable node)
        {
            _binder?.DisableNode(node);
        }

        /// <summary>
        /// 检查当前节点是否处于启用状态。
        /// </summary>
        /// <param name="node">当前节点。</param>
        /// <returns>如果节点未被禁用则返回 true。</returns>
        public static bool IsUpdateEnabled(this IUpdateable node)
        {
            return _binder?.IsNodeEnabled(node) ?? true;
        }
    }
}
