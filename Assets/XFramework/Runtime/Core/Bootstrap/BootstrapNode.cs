namespace XFramework.XCore
{
    /// <summary>
    /// 引导节点。在启动阶段统一管理所有非节点树的模块（如 AssetManager、LockManager、
    /// MessageManager 等）的生命周期。
    /// <para><see cref="AssetBootstrapNode"/> 实现了 <see cref="XLoader.ILoadable"/>，
    /// 在加载管线中异步初始化 <see cref="XAsset.AssetManager"/>。</para>
    /// <para><see cref="LockBootstrapNode"/> 和 <see cref="MessageBootstrapNode"/> 仅用于
    /// <see cref="OnDestroy"/> 时的资源清理，不参与加载管线。</para>
    /// <para>模块销毁由子节点的 <see cref="OnDestroy"/> 自动处理，BootstrapNode 本身无需管理销毁逻辑。</para>
    /// <para>可子类化并重写 <see cref="OnRegisterModules"/> 来自定义启动模块列表。</para>
    /// </summary>
    public class BootstrapNode : EntityNode
    {
        #region Protected API

        /// <summary>
        /// 注册启动模块的回调。子类可重写此方法来注册自定义的启动模块节点。
        /// <para>此方法在 <see cref="OnAwake"/> 中调用，早于加载管线的执行。</para>
        /// </summary>
        protected virtual void OnRegisterModules()
        {
            AddNode<AssetBootstrapNode>();
            AddNode<LockBootstrapNode>();
            AddNode<MessageBootstrapNode>();
        }

        #endregion

        #region Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            OnRegisterModules();
        }

        #endregion
    }
}