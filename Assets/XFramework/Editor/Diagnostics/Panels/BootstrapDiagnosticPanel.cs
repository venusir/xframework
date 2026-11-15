using XFramework.XBootstrap;
using XFramework.XDiagnostics;
using XFramework.XPipeline;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Bootstrap 页签：运行状态 + 最近进度 + 登记表。
    /// <para>回答三个问题：启动跑到哪了（<see cref="Bootstrap.State"/> / <see cref="Bootstrap.LastProgress"/>）、
    /// 失败时最后的描述是什么、登记表里到底会跑哪些阶段、按什么顺序。</para>
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
            report.KeyValue("状态", Bootstrap.State.ToString());
            report.KeyValue("已登记阶段数", stages.Count.ToString());

            CollectProgress(report);

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

        #region Private Methods

        private static void CollectProgress(IDiagnosticReport report)
        {
            PipelineProgress progress = Bootstrap.LastProgress;
            if (progress == null)
            {
                report.KeyValue("进度", "(尚未运行或会话已复位)");
                return;
            }

            report.KeyValue("进度", progress.OverallProgress.ToString("P0"));
            report.KeyValue("当前描述", string.IsNullOrEmpty(progress.Description) ? "(空)" : progress.Description);
            report.KeyValue("当前阶段", string.IsNullOrEmpty(progress.CurrentStageName) ? "(空)" : progress.CurrentStageName);
            report.KeyValue("当前任务", string.IsNullOrEmpty(progress.CurrentTaskName) ? "(空)" : progress.CurrentTaskName);
            report.KeyValue("阶段计数",
                $"完成 {progress.CompletedStageCount} / 失败 {progress.FailedStageCount} / 总 {progress.TotalStageCount}");
        }

        #endregion
    }
}
