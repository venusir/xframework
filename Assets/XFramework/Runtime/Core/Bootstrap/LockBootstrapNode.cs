using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLoader;
using XFramework.XLock;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="LockManager"/> 的启动节点。将锁管理器的清理封装为节点树中的一个加载任务。
    /// <para>LockManager 是纯静态类，无需异步初始化。</para>
    /// <para>继承 <see cref="EntityNode"/>，实现 <see cref="ILoadable"/>，会被 <see cref="StartupExtensions"/>
    /// 自动收集并在加载阶段按 Phase 顺序执行。</para>
    /// </summary>
    internal sealed class LockBootstrapNode : EntityNode, ILoadable
    {
        #region ILoadable

        /// <summary>
        /// Phase = 1。在 Asset 模块之后执行。
        /// </summary>
        public int Phase => 1;

        public UniTask LoadAsync(LoadContext context, CancellationToken cancellationToken)
        {
            context.SetProgress(1f);
            context.SetState(LoadState.Completed);
            return UniTask.CompletedTask;
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            LockManager.Dispose();
            base.OnDestroy();
        }

        #endregion
    }
}