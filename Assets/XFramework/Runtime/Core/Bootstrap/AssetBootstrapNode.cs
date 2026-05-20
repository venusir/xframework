using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XAsset;
using XFramework.XLoader;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="AssetManager"/> 的启动节点。将资源管理器的初始化封装为节点树中的一个加载任务。
    /// <para>继承 <see cref="EntityNode"/>，实现 <see cref="ILoadable"/>，会被 <see cref="StartupExtensions"/>
    /// 自动收集并在加载阶段按 Phase 顺序执行。</para>
    /// </summary>
    internal sealed class AssetBootstrapNode : EntityNode, ILoadable
    {
        #region ILoadable

        /// <summary>
        /// Phase = 0。确保 Asset 模块最先被加载。
        /// </summary>
        public int Phase => 0;

        public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
        {
            if (AssetManager.IsInitialized)
            {
                progress.SetProgress(1f);
                progress.SetState(LoadState.Completed);
                return;
            }

            progress.SetDescription("Initializing Asset Manager...");
            await AssetManager.InitializeAsync(progress, cancellationToken);

            progress.SetProgress(1f);
            progress.SetState(LoadState.Completed);
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            AssetManager.Destroy();
            base.OnDestroy();
        }

        #endregion

    }
}