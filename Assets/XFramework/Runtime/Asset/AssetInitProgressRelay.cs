using System;
using XFramework.XPipeline;

namespace XFramework.XAsset
{

    /// <summary>
    /// 进度直写桥：<see cref="AssetInitReport"/> → <see cref="PipelineStageContext"/>。
    /// <para>每次 <see cref="Report"/> 同步写进度与描述；阶段写面由 <see cref="PipelineStageContext"/> 保证事件驱动，
    /// 因此一次 Report 恰触发一次组级聚合。</para>
    /// <para><b>本类型与节点系统无关</b>——它只依赖 Asset 的进度载荷与 Pipeline 的上下文，
    /// 因此既能被节点树时代的引导节点使用，也能被纯 C# 的引导阶段使用。</para>
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
