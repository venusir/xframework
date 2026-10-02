using XFramework.XDiagnostics;
using XFramework.XInput;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Input 页签：设备类型与常用动作的可用性。
    /// <para>回答「按键为什么没反应」——先确认设备识别对不对、动作名存不存在。</para>
    /// <para><b>已知限制</b>：当前启用的 ActionMap 列表门面没有暴露（要动 <c>IInputProvider</c> 接口，
    /// 会波及第三方 Provider 实现），绑定覆盖用 <c>SaveBindingOverrides</c> 自查。</para>
    /// </summary>
    internal sealed class InputDiagnosticPanel : IDiagnosticPanel
    {
        #region Constants

        /// <summary>探测动作存在性时列出的常见动作名（<c>HasAction</c> 需要调用方提供名字）。</summary>
        private static readonly string[] ProbeActions =
        {
            "Move", "Look", "Jump", "Attack", "Interact", "Submit", "Cancel", "Pause",
        };

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Input";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 130;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", InputManager.IsInitialized ? "是" : "否");
            report.KeyValue("当前手柄类型", InputManager.ActiveGamepadType.ToString());
            report.KeyValue("最近活跃设备", InputManager.LastActiveDeviceType.ToString());

            if (!InputManager.IsInitialized)
                report.Notice(DiagnosticLevel.Info, "InputManager 尚未初始化（它的查询是宽容语义：未初始化时返回默认值）。");

            report.Section("常见动作是否存在（HasAction）");
            var table = report.BeginTable(null, "动作", "已绑定");
            for (int i = 0; i < ProbeActions.Length; i++)
                table.AddRow(ProbeActions[i], InputManager.HasAction(ProbeActions[i]) ? "是" : "-");

            report.Text("只探测一组常见动作名——动作名由项目自定，完整清单请看项目的输入资产。");
        }

        #endregion
    }
}
