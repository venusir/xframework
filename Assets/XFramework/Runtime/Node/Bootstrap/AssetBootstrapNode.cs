using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XAsset;
using XFramework.XPipeline;

namespace XFramework.XNode
{
    /// <summary>
    /// <see cref="AssetManager"/> 的启动节点。将资源管理器的初始化封装为节点树中的一个加载任务。
    /// <para>继承 <see cref="LeafNode"/>，实现 <see cref="ILoadable"/>，会被 <see cref="StartupExtensions"/>
    /// 自动收集并在加载阶段按 Phase 顺序执行。</para>
    /// </summary>
    internal sealed class AssetBootstrapNode : LeafNode, ILoadable
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

            // 临时中继(过渡):AssetManager 进度参数已解耦为 AssetInitReport,
            // 此处把步骤描述转写回 LoadProgress,保持加载阶段描述流不变;引导节点阶段化(IPhaseStage)后删除。
            await AssetManager.InitializeAsync(options: null, progress: new LoadProgressRelay(progress), cancellationToken: cancellationToken);

            progress.SetProgress(1f);
            progress.SetState(LoadState.Completed);
        }

        /// <summary>
        /// 临时进度中继(内部):AssetInitReport → LoadProgress 描述/整体进度转写,
        /// 与旧 YooAsset 上报行为一致(SetDescription 触发门铃镜像进组聚合,Overall 仅落字段)。
        /// </summary>
        private sealed class LoadProgressRelay : IProgress<AssetInitReport>
        {
            readonly LoadProgress _progress;

            public LoadProgressRelay(LoadProgress progress)
            {
                _progress = progress;
            }

            public void Report(AssetInitReport value)
            {
                _progress.SetOverallProgress(value.Progress);
                _progress.SetDescription(value.Description);
            }
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