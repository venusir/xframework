using System;
using XFramework.XPipeline;

namespace XFramework.XAsset
{

    /// <summary>
    /// 进度直写桥：<see cref="AssetInitReport"/> → <see cref="PipelineStageContext"/>。
    /// <para>每次 <see cref="Report"/> 同步写进度与描述；阶段写面由 <see cref="PipelineStageContext"/> 保证事件驱动，
    /// 因此一次 Report 恰触发一次组级聚合。</para>
    /// <para>只依赖 Asset 的进度载荷与 Pipeline 的上下文，故可被任何引导阶段复用。</para>
    /// </summary>
    internal sealed class AssetInitProgressRelay : IProgress<AssetInitReport>
    {
        private readonly PipelineStageContext _context;

        /// <summary>构造进度桥。</summary>
        /// <param name="context">要写入的阶段上下文。</param>
        public AssetInitProgressRelay(PipelineStageContext context)
        {
            _context = context;
        }

        /// <inheritdoc/>
        public void Report(AssetInitReport value)
        {
            _context.SetProgress(value.Progress);
            _context.SetDescription(value.Description);
        }
    }
}
