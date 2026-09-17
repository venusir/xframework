using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XAsset
{

    /// <summary>
    /// <see cref="AssetManager"/> 的引导阶段：把资源管理器的初始化交给框架启动流程。
    /// <para>Phase = 0，<b>排在最早</b>——本地化、UI 等模块的数据都要经 YooAsset 地址加载，必须等它就绪。</para>
    /// </summary>
    public sealed class AssetBootstrapStage : IBootstrapStage
    {
        #region IBootstrapStage

        /// <summary>Phase = 0。内置相位约定见 Pipeline 模块 README。</summary>
        public int Phase => 0;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>
        /// 初始化 <see cref="AssetManager"/>。已初始化则直接置完成（契约兜底）；
        /// 否则把 <see cref="AssetInitReport"/> 桥接为阶段进度后 await。
        /// <para>取消经 <see cref="System.OperationCanceledException"/> 冒泡，不吞。</para>
        /// </summary>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            if (AssetManager.IsInitialized)
            {
                context.SetState(PipelineStageState.Completed);
                return;
            }

            context.SetDescription("Initializing Asset Manager...");
            await AssetManager.InitializeAsync(options: null,
                                               progress: new AssetInitProgressRelay(context),
                                               cancellationToken: cancellationToken);

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            AssetManager.Destroy();
        }

        #endregion
    }
}
