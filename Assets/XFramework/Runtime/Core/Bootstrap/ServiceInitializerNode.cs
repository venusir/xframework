namespace XFramework.XCore
{
    /// <summary>
    /// 服务初始化节点。在启动阶段统一管理需要参与加载管线的模块（如 AssetManager 等）的生命周期。
    /// <para>LockManager、MessageManager 等纯静态服务已通过 [RuntimeInitializeOnLoadMethod] 自动管理生命周期，无需在此注册。</para>
    /// <para><see cref="AssetBootstrapNode"/> 实现了 <see cref="XLoader.ILoadable"/>，
    /// 在加载管线中异步初始化 <see cref="XAsset.AssetManager"/>。</para>
    /// <para>可子类化并重写 <see cref="OnRegisterModules"/> 来自定义启动模块列表。</para>
    /// </summary>
    public class ServiceInitializerNode : EntityNode
    {
        #region Protected API

        /// <summary>
        /// 注册启动模块的回调。子类可重写此方法来注册自定义的启动模块节点。
        /// <para>此方法在 <see cref="OnAwake"/> 中调用，早于加载管线的执行。</para>
        /// </summary>
        protected virtual void OnRegisterModules()
        {
            AddNode<AssetBootstrapNode>();
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