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
            context.SetDescription("Loading Luban configs...");
            await LubanConfig.InitializeAsync(
                _adapter, _options, new PipelineProgressRelay(context), cancellationToken);
            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        /// <inheritdoc />
        public void Shutdown()
        {
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
