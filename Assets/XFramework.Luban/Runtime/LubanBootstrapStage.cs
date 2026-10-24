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

            // 标记在 await 之前：Shutdown 可能在初始化在途时到来，此时本阶段确实接管了这次初始化。
            // 失败路径必须复位，否则初始化失败后使用方手动注入的状态会在 Shutdown 时被销毁。
            _initializedByThisStage = true;
            try
            {
                await LubanConfig.InitializeAsync(
                    _adapter, _options, new PipelineProgressRelay(context), cancellationToken);
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
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
            {
                return;
            }

            _initializedByThisStage = false;
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
