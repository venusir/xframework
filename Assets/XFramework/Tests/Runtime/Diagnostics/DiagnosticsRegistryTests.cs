using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XDiagnostics;

namespace XFramework.XDiagnostics.Tests
{
    /// <summary>
    /// <see cref="DiagnosticsManager"/>：登记 / 注销 / 排序 / 去重 / 拷贝。
    /// <para>登记表是静态的——PlayMode 下所有用例共享一个 player 实例，故 SetUp/TearDown 都调
    /// <see cref="DiagnosticsManager.Clear"/> 复位。</para>
    /// </summary>
    [TestFixture]
    public class DiagnosticsRegistryTests
    {
        #region Test Doubles

        private sealed class FakePanel : IDiagnosticPanel
        {
            public FakePanel(string title, int order)
            {
                Title = title;
                Order = order;
            }

            public string Title { get; }

            public int Order { get; }

            public int CollectCount { get; private set; }

            public void Collect(IDiagnosticReport report)
            {
                CollectCount++;
                report.Text(Title);
            }
        }

        /// <summary>Title 的 getter 返回指定值（含 null / 空白）。</summary>
        private sealed class RawTitlePanel : IDiagnosticPanel
        {
            private readonly string _title;

            public RawTitlePanel(string title) => _title = title;

            public string Title => _title;

            public int Order => 0;

            public void Collect(IDiagnosticReport report) { }
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

        #region Register

        [Test]
        public void Register_AddsPanel()
        {
            var panel = new FakePanel("A", 0);

            DiagnosticsManager.Register(panel);

            Assert.AreEqual(1, DiagnosticsManager.PanelCount);
            Assert.IsTrue(DiagnosticsManager.IsRegistered(panel));
        }

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DiagnosticsManager.Register(null));
        }

        [Test]
        public void Register_BlankTitle_Throws()
        {
            Assert.Throws<ArgumentException>(() => DiagnosticsManager.Register(new RawTitlePanel(null)));
            Assert.Throws<ArgumentException>(() => DiagnosticsManager.Register(new RawTitlePanel("   ")));
            Assert.AreEqual(0, DiagnosticsManager.PanelCount);
        }

        [Test]
        public void Register_SameInstanceTwice_IsIdempotent()
        {
            var panel = new FakePanel("A", 0);

            DiagnosticsManager.Register(panel);
            DiagnosticsManager.Register(panel);   // 关闭域重载时两条注册路径都会重跑，必须幂等

            Assert.AreEqual(1, DiagnosticsManager.PanelCount);
        }

        [Test]
        public void Register_SameTitleDifferentInstances_BothKept()
        {
            var first = new FakePanel("同名", 0);
            var second = new FakePanel("同名", 0);

            DiagnosticsManager.Register(first);
            DiagnosticsManager.Register(second);

            Assert.AreEqual(2, DiagnosticsManager.PanelCount);
            Assert.IsTrue(DiagnosticsManager.IsRegistered(second));
        }

        #endregion

        #region Ordering

        [Test]
        public void CopyPanels_OrdersByOrderThenRegistration()
        {
            var late = new FakePanel("late", 1000);
            var first = new FakePanel("first", -5);
            var sameA = new FakePanel("sameA", 10);
            var sameB = new FakePanel("sameB", 10);

            DiagnosticsManager.Register(late);
            DiagnosticsManager.Register(first);
            DiagnosticsManager.Register(sameA);
            DiagnosticsManager.Register(sameB);

            var buffer = new List<IDiagnosticPanel>();
            int count = DiagnosticsManager.CopyPanels(buffer);

            Assert.AreEqual(4, count);
            Assert.AreSame(first, buffer[0]);
            Assert.AreSame(sameA, buffer[1]);   // 同 Order：先登记的在前（稳定）
            Assert.AreSame(sameB, buffer[2]);
            Assert.AreSame(late, buffer[3]);
        }

        #endregion

        #region CopyPanels

        [Test]
        public void CopyPanels_ClearsBufferFirst()
        {
            DiagnosticsManager.Register(new FakePanel("A", 0));

            var buffer = new List<IDiagnosticPanel> { new FakePanel("junk", 0) };
            DiagnosticsManager.CopyPanels(buffer);

            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual("A", buffer[0].Title);
        }

        [Test]
        public void CopyPanels_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DiagnosticsManager.CopyPanels(null));
        }

        #endregion

        #region Unregister / IsRegistered / Clear

        [Test]
        public void Unregister_RemovesInstance()
        {
            var panel = new FakePanel("A", 0);
            DiagnosticsManager.Register(panel);

            Assert.IsTrue(DiagnosticsManager.Unregister(panel));
            Assert.AreEqual(0, DiagnosticsManager.PanelCount);
            Assert.IsFalse(DiagnosticsManager.IsRegistered(panel));
        }

        [Test]
        public void Unregister_UnknownInstance_ReturnsFalse()
        {
            DiagnosticsManager.Register(new FakePanel("A", 0));

            Assert.IsFalse(DiagnosticsManager.Unregister(new FakePanel("A", 0)));
            Assert.AreEqual(1, DiagnosticsManager.PanelCount);
        }

        [Test]
        public void Unregister_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DiagnosticsManager.Unregister(null));
        }

        [Test]
        public void IsRegistered_Null_ReturnsFalse()
        {
            Assert.IsFalse(DiagnosticsManager.IsRegistered(null));
        }

        [Test]
        public void Clear_EmptiesRegistry()
        {
            DiagnosticsManager.Register(new FakePanel("A", 0));
            DiagnosticsManager.Register(new FakePanel("B", 1));

            DiagnosticsManager.Clear();

            Assert.AreEqual(0, DiagnosticsManager.PanelCount);
        }

        #endregion
    }
}
