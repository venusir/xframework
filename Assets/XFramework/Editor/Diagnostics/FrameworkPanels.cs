using UnityEditor;
using XFramework.Editor.Diagnostics.Panels;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics
{
    /// <summary>
    /// 框架自带页签的**唯一登记点**（显式登记，无反射）。
    /// <para>挂在 <c>[InitializeOnLoadMethod]</c> 上：编辑器加载程序集时登记一次，因此**未进入播放模式
    /// 也能看到框架页签**——此时模块多半未初始化，页签自己会写「未初始化」而不是抛异常。</para>
    /// <para>关闭域重载时该回调不会重跑，但登记表与这里是同一份静态状态，页签仍在；域重载开着时静态状态
    /// 被清空、回调重跑，登记照旧。页签实例做成 <c>static readonly</c>：两条时序下 <c>Register</c> 的
    /// 按实例幂等都能兜住，不依赖「静态字段一定被清空」这个假设。</para>
    /// <para>第三方不需要也不应该改这里——用自己的代码调 <see cref="DiagnosticsManager.Register"/> 即可。</para>
    /// </summary>
    internal static class FrameworkPanels
    {
        #region Panels

        private static readonly UiDiagnosticPanel UiPanel = new UiDiagnosticPanel();

        private static readonly LogDiagnosticPanel LogPanel = new LogDiagnosticPanel();

        private static readonly PoolDiagnosticPanel PoolPanel = new PoolDiagnosticPanel();

        private static readonly ConfigDiagnosticPanel ConfigPanel = new ConfigDiagnosticPanel();

        private static readonly SettingsDiagnosticPanel SettingsPanel = new SettingsDiagnosticPanel();

        private static readonly UpdateDiagnosticPanel UpdatePanel = new UpdateDiagnosticPanel();

        private static readonly LocalizationDiagnosticPanel LocalizationPanel = new LocalizationDiagnosticPanel();

        private static readonly AudioDiagnosticPanel AudioPanel = new AudioDiagnosticPanel();

        private static readonly TimerDiagnosticPanel TimerPanel = new TimerDiagnosticPanel();

        private static readonly OverviewDiagnosticPanel OverviewPanel = new OverviewDiagnosticPanel();

        private static readonly MessageDiagnosticPanel MessagePanel = new MessageDiagnosticPanel();

        private static readonly LockDiagnosticPanel LockPanel = new LockDiagnosticPanel();

        private static readonly DataDiagnosticPanel DataPanel = new DataDiagnosticPanel();

        private static readonly BootstrapDiagnosticPanel BootstrapPanel = new BootstrapDiagnosticPanel();

        private static readonly SaveDiagnosticPanel SavePanel = new SaveDiagnosticPanel();

        private static readonly FileDiagnosticPanel FilePanel = new FileDiagnosticPanel();

        private static readonly InputDiagnosticPanel InputPanel = new InputDiagnosticPanel();

        private static readonly SerializeDiagnosticPanel SerializePanel = new SerializeDiagnosticPanel();

        #endregion

        #region Registration

        [InitializeOnLoadMethod]
        private static void Register()
        {
            DiagnosticsManager.Register(OverviewPanel);
            DiagnosticsManager.Register(UiPanel);
            DiagnosticsManager.Register(LogPanel);
            DiagnosticsManager.Register(PoolPanel);
            DiagnosticsManager.Register(ConfigPanel);
            DiagnosticsManager.Register(SettingsPanel);
            DiagnosticsManager.Register(UpdatePanel);
            DiagnosticsManager.Register(LocalizationPanel);
            DiagnosticsManager.Register(AudioPanel);
            DiagnosticsManager.Register(TimerPanel);
            DiagnosticsManager.Register(MessagePanel);
            DiagnosticsManager.Register(LockPanel);
            DiagnosticsManager.Register(DataPanel);
            DiagnosticsManager.Register(BootstrapPanel);
            DiagnosticsManager.Register(SavePanel);
            DiagnosticsManager.Register(FilePanel);
            DiagnosticsManager.Register(InputPanel);
            DiagnosticsManager.Register(SerializePanel);
        }

        #endregion
    }
}
