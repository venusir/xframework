using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XConfig;
using XFramework.XPipeline;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 自定义启动阶段：初始化 Config 模块并预载模式表与全局规则。
    /// <para>Config 的三种内置格式都经 <c>AssetManager</c> 读取，故本阶段必须晚于 Asset（Phase 0）；
    /// 这里取 <see cref="BootstrapPhases.UserStart"/>（90），与 Settings / Localization 同相位并行。</para>
    /// </summary>
    public sealed class GameConfigBootstrapStage : IBootstrapStage
    {
        private bool _initializedByThisStage;

        /// <inheritdoc/>
        public int Phase => BootstrapPhases.UserStart;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <inheritdoc/>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Loading config tables...");

            ConfigManager.Initialize();
            await ConfigManager.PreloadTableAsync<GameModeRow>(
                IntegrationAddresses.GameModes, ConfigFormat.Json, cancellationToken);
            await ConfigManager.PreloadGlobalAsync<IntegrationRules>(
                IntegrationAddresses.Rules, ConfigFormat.Json, cancellationToken);

            _initializedByThisStage = true;
            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;
            ConfigManager.Destroy();
        }
    }
}
