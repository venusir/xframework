using System;
using NUnit.Framework;
using XFramework.Editor.Diagnostics;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics.Tests
{
    /// <summary>
    /// <see cref="DiagnosticsWindowModel"/>：节流判据、选中、页签表重载、异常隔离、自绘路径选择。
    /// <para>时钟由用例喂进来，因此不必等真实时间；IMGUI 调用不在本类里（那是窗口的 GUILayout 部分）。</para>
    /// <para>登记表是静态的——SetUp/TearDown 都调 <see cref="DiagnosticsManager.Clear"/> 复位。</para>
    /// </summary>
    [TestFixture]
    public class DiagnosticsWindowModelTests
    {
        #region Test Doubles

        private sealed class FakePanel : IDiagnosticPanel
        {
            private readonly Action<IDiagnosticReport> _collect;

            public FakePanel(string title, Action<IDiagnosticReport> collect = null, int order = 0)
            {
                Title = title;
                _collect = collect;
                Order = order;
            }

            public string Title { get; }

            public int Order { get; }

            public int CollectCount { get; private set; }

            public void Collect(IDiagnosticReport report)
            {
                CollectCount++;
                _collect?.Invoke(report);
            }
        }

        /// <summary>登记时 Title 正常，之后可以「坏掉」——模拟模块销毁后标题不可读。</summary>
        private sealed class BreakableTitlePanel : IDiagnosticPanel
        {
            public bool Broken { get; set; }

            public string Title => Broken ? throw new InvalidOperationException("标题没得读了") : "可读";

            public int Order => 0;

            public void Collect(IDiagnosticReport report) { }
        }

        /// <summary>采集会抛的页签；<see cref="ThrowOnce"/> 为 true 时只抛第一次。</summary>
        private sealed class ThrowingPanel : IDiagnosticPanel
        {
            public ThrowingPanel(bool throwOnce)
            {
                ThrowOnce = throwOnce;
            }

            public bool ThrowOnce { get; }

            private bool _thrown;

            public string Title => "会抛的";

            public int Order => 0;

            public void Collect(IDiagnosticReport report)
            {
                if (ThrowOnce && _thrown)
                {
                    report.Text("恢复");
                    return;
                }

                _thrown = true;
                report.Text("这一条会被丢掉");
                throw new InvalidOperationException("采集炸了");
            }
        }

        /// <summary>自带渲染的页签。</summary>
        private sealed class SelfDrawnPanel : IDiagnosticPanel, IDiagnosticPanelView
        {
            public string Title => "自绘";

            public int Order => 0;

            public void Collect(IDiagnosticReport report) { }

            public void Draw(DiagnosticReport report) { }
        }

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            DiagnosticsManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            DiagnosticsManager.Clear();
        }

        #endregion

        #region Tick / 节流

        [Test]
        public void Tick_NoPanels_ReturnsFalse()
        {
            var model = new DiagnosticsWindowModel();

            Assert.IsFalse(model.Tick(0d, manual: false));
            Assert.IsFalse(model.HasPanels);
            Assert.AreEqual(-1, model.SelectedIndex);
        }

        [Test]
        public void Tick_CollectsSelectedPanel()
        {
            DiagnosticsManager.Register(new FakePanel("A", r => r.KeyValue("A", "1")));
            var model = new DiagnosticsWindowModel();

            Assert.IsTrue(model.Tick(0d, manual: false));
            Assert.AreEqual(1, model.Report.Items.Count);
            Assert.AreEqual("A", model.Report.Items[0].Text);
        }

        [Test]
        public void Tick_ThrottlesWithinRefreshInterval()
        {
            DiagnosticsManager.Register(new FakePanel("A"));
            var model = new DiagnosticsWindowModel();

            Assert.IsTrue(model.Tick(0d, manual: false));
            Assert.IsFalse(model.Tick(0.2d, manual: false));   // 还没到点
            Assert.IsTrue(model.Tick(0.5d, manual: false));    // 到点
        }

        [Test]
        public void Tick_ManualBypassesThrottle()
        {
            DiagnosticsManager.Register(new FakePanel("A"));
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);

            Assert.IsTrue(model.Tick(0.01d, manual: true));
        }

        [Test]
        public void Tick_AutoRefreshOff_StopsCollecting()
        {
            var panel = new FakePanel("A");
            DiagnosticsManager.Register(panel);
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);          // 首次（脏）照采
            model.AutoRefresh = false;
            int before = panel.CollectCount;

            Assert.IsFalse(model.Tick(10d, manual: false));
            Assert.AreEqual(before, panel.CollectCount);
            Assert.IsTrue(model.Tick(10d, manual: true));   // 手动仍可
        }

        [Test]
        public void Tick_PicksUpNewlyRegisteredPanels()
        {
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);

            DiagnosticsManager.Register(new FakePanel("A"));
            model.Tick(1d, manual: false);

            Assert.AreEqual(1, model.PanelCount);
        }

        #endregion

        #region Select

        [Test]
        public void Select_DifferentIndex_CollectsDespiteAutoRefreshOff()
        {
            DiagnosticsManager.Register(new FakePanel("A", r => r.Text("来自 A")));
            DiagnosticsManager.Register(new FakePanel("B", r => r.Text("来自 B")));
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);
            model.AutoRefresh = false;

            model.Select(1);
            Assert.IsTrue(model.Tick(0.01d, manual: false));   // 换页签必须重采

            Assert.AreEqual("来自 B", model.Report.Items[0].Text);
        }

        [Test]
        public void Select_SameIndex_IsNoOp()
        {
            DiagnosticsManager.Register(new FakePanel("A"));
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);
            model.AutoRefresh = false;

            model.Select(0);

            Assert.IsFalse(model.Tick(10d, manual: false));
        }

        [Test]
        public void Select_ClampsOutOfRange()
        {
            DiagnosticsManager.Register(new FakePanel("A"));
            DiagnosticsManager.Register(new FakePanel("B"));
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);

            model.Select(99);

            Assert.AreEqual(1, model.SelectedIndex);
        }

        [Test]
        public void Reload_ClampsSelectionWhenPanelsShrink()
        {
            var a = new FakePanel("A");
            var b = new FakePanel("B");
            DiagnosticsManager.Register(a);
            DiagnosticsManager.Register(b);
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);
            model.Select(1);

            DiagnosticsManager.Unregister(b);
            model.Tick(1d, manual: false);

            Assert.AreEqual(0, model.SelectedIndex);
            Assert.AreEqual(1, model.PanelCount);
        }

        #endregion

        #region 异常隔离

        [Test]
        public void Collect_Throws_IsIsolatedAndReportedInReport()
        {
            DiagnosticsManager.Register(new ThrowingPanel(throwOnce: false));
            var model = new DiagnosticsWindowModel();

            Assert.IsTrue(model.Tick(0d, manual: false));
            Assert.IsTrue(model.Failed);

            // 抛之前的半截内容被丢掉，报告里换成错误说明
            Assert.AreEqual(2, model.Report.Items.Count);
            Assert.AreEqual(DiagnosticItemKind.Notice, model.Report.Items[0].Kind);
            Assert.AreEqual(DiagnosticLevel.Error, model.Report.Items[0].Level);
            Assert.AreEqual(DiagnosticItemKind.Text, model.Report.Items[1].Kind);
            StringAssert.Contains("采集炸了", model.Report.Items[1].Text);
        }

        [Test]
        public void Collect_ThrowsOnce_RecoversOnNextTick()
        {
            DiagnosticsManager.Register(new ThrowingPanel(throwOnce: true));
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);
            Assert.IsTrue(model.Failed);

            model.Tick(1d, manual: false);

            Assert.IsFalse(model.Failed);
            Assert.AreEqual("恢复", model.Report.Items[0].Text);
        }

        [Test]
        public void GetTitle_BrokenTitle_FallsBackToTypeName()
        {
            var panel = new BreakableTitlePanel();
            DiagnosticsManager.Register(panel);
            var model = new DiagnosticsWindowModel();
            panel.Broken = true;   // 登记之后才坏

            model.Tick(0d, manual: false);

            StringAssert.Contains(nameof(BreakableTitlePanel), model.GetTitle(0));
        }

        #endregion

        #region 自绘 / 复制

        [Test]
        public void TryGetCustomView_DetectsSelfDrawnPanel()
        {
            DiagnosticsManager.Register(new SelfDrawnPanel());
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);

            Assert.IsTrue(model.TryGetCustomView(out var view));
            Assert.IsNotNull(view);
        }

        [Test]
        public void TryGetCustomView_PlainPanel_ReturnsFalse()
        {
            DiagnosticsManager.Register(new FakePanel("A"));
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);

            Assert.IsFalse(model.TryGetCustomView(out _));
        }

        [Test]
        public void BuildCopyText_MatchesReport()
        {
            DiagnosticsManager.Register(new FakePanel("A", r => r.KeyValue("键", "值")));
            var model = new DiagnosticsWindowModel();
            model.Tick(0d, manual: false);

            Assert.AreEqual("键: 值", model.BuildCopyText());
        }

        #endregion
    }
}
