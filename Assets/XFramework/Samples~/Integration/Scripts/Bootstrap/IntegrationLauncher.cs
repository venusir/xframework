using UnityEngine;
using XFramework.XBootstrap;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例启动器：在 <see cref="DefaultGameLauncher"/> 的字段面之上追加 Settings 与「启动完成」两个阶段。
    /// <para><b>Inspector 字段</b>（由基类提供）：Asset Play Mode = <c>EditorSimulate</c>、UI Root、
    /// Input Options、默认语言与引导语言表。<b>本类只多一个</b> <c>_appFlow</c>。</para>
    /// <para>示例刻意不覆写 <c>Start</c>/<c>OnDestroy</c>：启动、取消与逆序清理都由
    /// <see cref="GameLauncher"/> 底座承担，业务流程只等 <see cref="AppReadyBootstrapStage"/> 的信号。</para>
    /// </summary>
    public sealed class IntegrationLauncher : DefaultGameLauncher
    {
        [Tooltip("启动完成后接收通知的业务流程控制器。")]
        [SerializeField] private AppFlowController _appFlow;

        /// <inheritdoc/>
        protected override void ConfigureStages()
        {
            // 先登记框架默认组合 + 基类字段面（Asset / Input / UI / Data / Save / Localization）
            base.ConfigureStages();

            // 再追加示例自己的阶段：Settings(90) / Config(90) 与 Localization(90) 并行；
            // AppReady(91) 收尾。
            Bootstrap.Register(new GameSettingsBootstrapStage());
            Bootstrap.Register(new GameConfigBootstrapStage());
            Bootstrap.Register(new AppReadyBootstrapStage(_appFlow));
        }
    }
}
