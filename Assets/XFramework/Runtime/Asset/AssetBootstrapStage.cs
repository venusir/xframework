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

        /// <summary>
        /// 本阶段是否<b>确实</b>发起了初始化。<see cref="Shutdown"/> 据此决定要不要销毁——
        /// 「本阶段初始化的服务才由本阶段清理」，空转 / 已初始化早退 / 仅加入别人的在途初始化
        /// 都不该把使用方手动注入或别人初始化的管理器一起销毁。
        /// </summary>
        private bool _initializedByThisStage;

        /// <summary>本阶段发起初始化时的代际号；<see cref="Shutdown"/> 比对它判断实例是否已被换掉。</summary>
        private int _ownedGeneration;

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

            // 所有权在 await 之前判定：另一调用方已在途时本阶段只是加入，不得把自己记为所有者
            // （R4-AST-002）。同时捕获代际号：使用方中途 SetInstance / Destroy 会递增它，
            // Shutdown 据此拒绝销毁已被换掉的实例。
            bool ownsInit = !AssetManager.IsInitializing;
            int generation = AssetManager.CurrentInitGeneration;

            // 标记在 await 之前：Shutdown 可能在初始化在途时到来（启动途中销毁 GameLauncher 即此形态），
            // 此时本阶段确实接管了这次初始化，必须由它作废在途任务并清理半初始化实例——
            // 标记放在 await 之后会让这条路径跳过清理，在途初始化反而在 Shutdown 之后完成。
            // **失败路径必须复位**（AST-004）：否则初始化失败后使用方手动注入的实例会在 Shutdown
            // 时被一并销毁——那不是本阶段建的那份。
            _initializedByThisStage = ownsInit;
            _ownedGeneration = generation;
            try
            {
                await AssetManager.InitializeAsync(options: _options,
                                                   progress: new AssetInitProgressRelay(context),
                                                   cancellationToken: cancellationToken);
            }
            catch
            {
                _initializedByThisStage = false;
                throw;
            }

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        /// <summary>
        /// 反向清理：<b>只销毁本阶段自己初始化的那份</b>——已初始化早退路径什么都没建，
        /// 仅加入别人在途初始化时也不拥有；使用方 <see cref="AssetManager.SetInstance"/> 换过实例
        /// （代际号变化）同样不碰。幂等。
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;

            if (AssetManager.CurrentInitGeneration != _ownedGeneration)
                return;

            AssetManager.Destroy();
        }

        #endregion
    }
}
