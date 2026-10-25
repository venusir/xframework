using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// 把 Luban 配置加载接入启动流程的引导阶段。
    /// <para>Phase 默认 <see cref="BootstrapPhases.UserStart"/>（90，与 Localization 同相位并行）；
    /// 依赖本地化已就绪时可在构造函数传入更大的相位号。</para>
    /// <para>前置条件：AssetManager 已初始化（Asset 阶段先运行）。</para>
    /// </summary>
    public sealed class LubanBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly ILubanConfigAdapter _adapter;
        private readonly LubanOptions _options;
        private readonly int _phase;
        private bool _initializedByThisStage;

        /// <summary>本阶段发起初始化时创建的实现实例；Shutdown 用它比对身份（R4-LBN-005）。</summary>
        private LubanConfigImpl _ownedImpl;

        #endregion

        #region Construction

        /// <summary>
        /// 构造引导阶段。
        /// </summary>
        /// <param name="adapter">生成的强类型适配器。</param>
        /// <param name="options">加载选项；null 时使用默认值。</param>
        /// <param name="phase">相位号；默认 <see cref="BootstrapPhases.UserStart"/>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="adapter"/> 为 null。</exception>
        public LubanBootstrapStage(
            ILubanConfigAdapter adapter,
            LubanOptions options = null,
            int phase = BootstrapPhases.UserStart)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _options = options;
            _phase = phase;
        }

        #endregion

        #region IBootstrapStage

        /// <inheritdoc />
        public int Phase => _phase;

        /// <inheritdoc />
        public string Name => GetType().Name;

        /// <inheritdoc />
        public float Weight => 1f;

        /// <inheritdoc />
        public async UniTask ExecuteAsync(
            PipelineStageContext context,
            CancellationToken cancellationToken)
        {
            if (LubanConfig.IsInitialized)
            {
                context.SetState(PipelineStageState.Completed);
                return;
            }

            context.SetDescription("Loading Luban configs...");

            // 所有权在 await 之前判定：另一调用方已在途时本阶段只是加入，不得把自己记为所有者
            // （R4-LBN-005）。标记仍在 await 之前——Shutdown 可能在初始化在途时到来，此时本阶段
            // 确实接管了这次初始化，必须由它作废在途任务。
            // 失败路径必须复位，否则初始化失败后使用方手动注入的状态会在 Shutdown 时被销毁。
            bool ownsInit = !LubanConfig.IsInitializing;
            _initializedByThisStage = ownsInit;
            try
            {
                var task = LubanConfig.InitializeAsync(
                    _adapter, _options, new PipelineProgressRelay(context), cancellationToken);

                // InitializeAsync 在首个 await 之前已创建/复用 _impl；此刻捕获到的就是本次要用的实例。
                if (ownsInit)
                    _ownedImpl = LubanConfig.CurrentImpl;

                await task;
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
        /// 反向清理：只销毁本阶段自己初始化的那份。幂等。
        /// <para>仅加入别人在途初始化时标记为 false；本阶段发起后外部又 <c>Destroy</c> 并重建时，
        /// 当前实例与 <see cref="_ownedImpl"/> 不同，也不再销毁（R4-LBN-005）。</para>
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
            {
                return;
            }

            _initializedByThisStage = false;
            var owned = _ownedImpl;
            _ownedImpl = null;

            var current = LubanConfig.CurrentImpl;
            if (current != null && !ReferenceEquals(current, owned))
                return;

            LubanConfig.Destroy();
        }

        #endregion

        #region Internal

        private sealed class PipelineProgressRelay : IProgress<LubanInitReport>
        {
            private readonly PipelineStageContext _context;

            internal PipelineProgressRelay(PipelineStageContext context)
            {
                _context = context;
            }

            public void Report(LubanInitReport value)
            {
                _context.SetProgress(value.Progress);
                if (!string.IsNullOrEmpty(value.Description))
                {
                    _context.SetDescription(value.Description);
                }
            }
        }

        #endregion
    }
}
