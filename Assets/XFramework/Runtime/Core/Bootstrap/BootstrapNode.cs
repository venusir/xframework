namespace XFramework.XCore
{
    /// <summary>
    /// 引导节点。在启动阶段统一管理所有非节点树的模块（如 AssetManager、LockManager、
    /// MessageManager 等）的生命周期。
    /// <para>模块初始化由子节点（<see cref="AssetBootstrapNode"/>、<see cref="LockBootstrapNode"/>、
    /// <see cref="MessageBootstrapNode"/>）分别以 <see cref="XLoader.ILoadable"/> 的形式承载，
    /// 由 <see cref="XLoader.StartupExtensions"/> 自动收集并按 Phase 顺序执行。</para>
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