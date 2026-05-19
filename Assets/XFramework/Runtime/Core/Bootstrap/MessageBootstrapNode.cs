using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLoader;
using XFramework.XReactive;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="MessageManager"/> 的启动节点。将消息总线的清理封装为节点树中的一个加载任务。
    /// <para>MessageManager 是纯静态类，无需异步初始化。</para>
    /// <para>继承 <see cref="EntityNode"/>，实现 <see cref="ILoadable"/>，会被 <see cref="StartupExtensions"/>
    /// 自动收集并在加载阶段按 Phase 顺序执行。</para>
    /// </summary>
    internal sealed class MessageBootstrapNode : EntityNode, ILoadable
    {
        #region ILoadable

        /// <summary>
        /// Phase = 2。在 Lock 模块之后执行。
        /// </summary>
        public int Phase => 2;

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
            MessageManager.Clear();
            base.OnDestroy();
        }

        #endregion
    }
}