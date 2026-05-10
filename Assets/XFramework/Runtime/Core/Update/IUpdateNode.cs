namespace XFramework
{
    /// <summary>
    /// 更新服务接口。提供对 <see cref="IUpdateable"/> 节点的启用/禁用控制。
    /// <para>通过 <see cref="BaseNode.Get{T}"/> 获取此服务：<c>this.Get<IUpdateNode>()</c></para>
    /// </summary>
    public interface IUpdateNode
    {
        /// <summary>
        /// 启用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnEnable"/>。</para>
        /// </summary>
        void Enable(IUpdateable node);

        /// <summary>
        /// 禁用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// </summary>
        void Disable(IUpdateable node);

        /// <summary>
        /// 检查节点是否处于启用状态。
        /// </summary>
        bool IsEnabled(IUpdateable node);

        /// <summary>
        /// 立即对指定节点执行一次更新并重新调整 LOD。
        /// <para>用于外部逻辑变化时需要立即响应，不等下一次时间切片。</para>
        /// </summary>
        void ProcessImmediate(IUpdateable node, float deltaTime, float time);
    }
}
