using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 最后一个启动阶段：通知 <see cref="AppFlowController"/> 开始跑业务流程。
    /// <para><b>为什么需要它</b>：<c>GameLauncher.Start</c> 调用 <c>Bootstrap.RunAsync</c> 后不转发进度，
    /// 业务流程需要一个「全部阶段已完成」的明确信号。本阶段取 91（晚于全部 90），
    /// 在 <see cref="ExecuteAsync"/> 里完成一个 <c>UniTaskCompletionSource</c>。</para>
    /// </summary>
    public sealed class AppReadyBootstrapStage : IBootstrapStage
    {
        private readonly AppFlowController _flow;

        /// <summary>构造阶段。</summary>
        /// <param name="flow">启动完成后要通知的业务控制器。</param>
        public AppReadyBootstrapStage(AppFlowController flow)
        {
            _flow = flow;
        }

        /// <inheritdoc/>
        public int Phase => BootstrapPhases.UserStart + 1;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <inheritdoc/>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Ready");
            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);

            _flow?.NotifyBootstrapped();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            // 没有自己初始化的服务；幂等空转。
        }
    }
}
