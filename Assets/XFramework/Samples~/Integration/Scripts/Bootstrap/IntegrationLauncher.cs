using XFramework.XBootstrap;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例启动器：在 <see cref="DefaultGameLauncher"/> 的 Profile 之上追加 Settings 与 Config 两个阶段。
    /// <para><b>Inspector 字段</b>（由基类提供）：<c>_profile</c>（<c>Integration.BootstrapProfile</c>——
    /// Asset Play Mode = <c>EditorSimulate</c>、Input Options、默认语言与引导语言表）与 <c>_uiRoot</c>。</para>
    /// <para>示例刻意不覆写 <c>Start</c>/<c>OnDestroy</c>：启动、取消与逆序清理都由
    /// <see cref="GameLauncher"/> 底座承担；业务流程等 <see cref="Bootstrap.WaitUntilReadyAsync"/>
    /// 的启动就绪信号（不再自造「启动完成」阶段）。</para>
    /// </summary>
    public sealed class IntegrationLauncher : DefaultGameLauncher
    {
        /// <inheritdoc/>
        protected override void ConfigureStages()
        {
            // 先登记 Profile 面（Asset / Data / Save / UI / Input / Localization）
            base.ConfigureStages();

            // 再追加示例自己的阶段：Settings(90) / Config(90) 与 Localization(90) 并行。
            Bootstrap.Register(new GameSettingsBootstrapStage());
            Bootstrap.Register(new GameConfigBootstrapStage());
        }
    }
}
