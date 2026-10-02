using XFramework.XBootstrap;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Bootstrap 页签：登记表。
    /// <para>回答「启动流程里到底会跑哪些阶段、按什么顺序」——登记是显式的，这张表就是那份清单。</para>
    /// </summary>
    internal sealed class BootstrapDiagnosticPanel : IDiagnosticPanel
    {
        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Bootstrap";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 100;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            var stages = Bootstrap.Stages;

            report.Section("概览");
            report.KeyValue("已登记阶段数", stages.Count.ToString());

            report.Section("登记表（按登记顺序）");
            if (stages.Count == 0)
            {
                report.Text("(没有登记任何引导阶段)");
                return;
            }

            var table = report.BeginTable(null, "阶段", "相位", "权重");
            for (int i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                table.AddRow(stage.Name, stage.Phase.ToString(), stage.Weight.ToString());
            }
        }

        #endregion
    }
}
