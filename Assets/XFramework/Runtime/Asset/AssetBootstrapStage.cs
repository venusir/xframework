using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XAsset
{

    /// <summary>
    /// <see cref="AssetManager"/> 的引导阶段：把资源管理器的初始化交给框架启动流程。
    /// <para>Phase = <see cref="BootstrapPhases.Asset"/>（0），<b>排在最早</b>——本地化、UI 等模块的数据都要经 YooAsset 地址加载，必须等它就绪。</para>
    /// </summary>
    public sealed class AssetBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly AssetInitOptions _options;

        #endregion

        #region Construction

        /// <summary>构造引导阶段。</summary>
        /// <param name="options">资源初始化选项（包名 / 运行模式 / 远端服务 / 解密服务 / 低内存回收）；
        /// <c>null</c> 表示用默认值（默认包 + 离线模式）。</param>
        public AssetBootstrapStage(AssetInitOptions options = null)
        {
            _options = options;
        }

        #endregion

        #region Internal

        /// <summary>本阶段携带的选项（测试缝，用于断言字段面真的注入了）。</summary>
        internal AssetInitOptions Options => _options;

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = <see cref="BootstrapPhases.Asset"/>（0，最早）。</summary>
        public int Phase => BootstrapPhases.Asset;

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
            await AssetManager.InitializeAsync(options: _options,
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
