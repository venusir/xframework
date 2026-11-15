using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.Editor.Diagnostics.Panels;
using XFramework.XBootstrap;
using XFramework.XDiagnostics;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// Bootstrap 诊断页签：状态 / 进度快照 / 登记表三块的采集。
    /// <para>面板是纯采集逻辑（IMGUI 不在里面），可直接用真实 <see cref="DiagnosticReport"/> 断言条目。</para>
    /// </summary>
    class BootstrapDiagnosticPanelTests
    {
        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        [Test]
        public void Collect_Empty_ReportsIdleAndEmptyRegistry()
        {
            var report = new DiagnosticReport();

            new BootstrapDiagnosticPanel().Collect(report);

            Assert.AreEqual("Idle", FindKeyValue(report, "状态"));
            Assert.AreEqual("0", FindKeyValue(report, "已登记阶段数"));
            Assert.IsNotNull(FindKeyValue(report, "进度"), "未运行时进度应给出占位说明");
            Assert.IsTrue(HasText(report, "(没有登记任何引导阶段)"));
        }

        [Test]
        public void Collect_AfterCompletedRun_ReportsStateAndProgress()
        {
            Bootstrap.Register(BootstrapStage.Create(0, "diagnostic-stage", _ => UniTask.CompletedTask));
            Bootstrap.RunAsync().GetAwaiter().GetResult();

            var report = new DiagnosticReport();
            new BootstrapDiagnosticPanel().Collect(report);

            Assert.AreEqual("Completed", FindKeyValue(report, "状态"));
            Assert.AreEqual("1", FindKeyValue(report, "已登记阶段数"));
            Assert.AreEqual("Completed", FindKeyValue(report, "当前描述"));
            StringAssert.Contains("完成 1", FindKeyValue(report, "阶段计数"));
        }

        #region Helpers

        private static string FindKeyValue(DiagnosticReport report, string label)
        {
            for (int i = 0; i < report.Items.Count; i++)
            {
                var item = report.Items[i];
                if (item.Kind == DiagnosticItemKind.KeyValue && item.Text == label)
                    return item.Value;
            }

            return null;
        }

        private static bool HasText(DiagnosticReport report, string text)
        {
            for (int i = 0; i < report.Items.Count; i++)
            {
                if (report.Items[i].Kind == DiagnosticItemKind.Text && report.Items[i].Text == text)
                    return true;
            }

            return false;
        }

        #endregion
    }
}
