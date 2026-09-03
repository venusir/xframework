using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XAsset;
using XFramework.XPipeline;

namespace XFramework.XNode
{
    /// <summary>
    /// <see cref="AssetManager"/> 的引导阶段节点。将资源管理器的初始化封装为启动管线中的相位阶段。
    /// <para>继承 <see cref="LeafNode"/>，实现 <see cref="IPhaseStage"/>（Phase = 0、Name = 类型名、Weight = 1），
    /// 会被 <see cref="StartupExtensions"/> 自动收集并按相位分组调度。</para>
    /// </summary>
    internal sealed class AssetBootstrapNode : LeafNode, IPhaseStage
    {
        #region IPhaseStage

        /// <summary>Phase = 0。确保 Asset 模块最先被初始化（内置相位约定见 Pipeline 模块 README）。</summary>
        public int Phase => 0;

        public string Name => GetType().Name;

        public float Weight => 1f;

        /// <summary>
        /// 执行 Asset 模块初始化。已初始化直接返回（契约兜底补完成）；否则写描述后 await
        /// <see cref="AssetManager.InitializeAsync"/>，取消经 OCE 冒泡（禁止吞 OperationCanceledException）。
        /// </summary>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            if (AssetManager.IsInitialized)
            {
                context.SetState(PipelineStageState.Completed);
                return;
            }

            context.SetDescription("Initializing Asset Manager...");
            await AssetManager.InitializeAsync(options: null, progress: new AssetInitProgressRelay(context), cancellationToken: cancellationToken);

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        /// <summary>
        /// 进度直写桥：<see cref="AssetInitReport"/> → 阶段上下文。每次 Report 同步写进度与描述，
        /// 一次 Report 恰触发一次组级聚合（阶段写面由 <see cref="PipelineStageContext"/> 保证事件驱动）。
        /// </summary>
        private sealed class AssetInitProgressRelay : IProgress<AssetInitReport>
        {
            readonly PipelineStageContext _context;

            public AssetInitProgressRelay(PipelineStageContext context)
            {
                _context = context;
            }

            public void Report(AssetInitReport value)
            {
                _context.SetProgress(value.Progress);
                _context.SetDescription(value.Description);
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
