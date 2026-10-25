using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

        /// <summary>Title 的 getter 抛异常（R3-DIAG-001：模块销毁后 getter 可能变坏）。</summary>
        private sealed class ThrowingTitlePanel : IDiagnosticPanel
        {
            public string Title => throw new InvalidOperationException("标题 getter 坏了");

            public int Order => 0;

            public void Collect(IDiagnosticReport report) { }
        }

        /// <summary>Order 的 getter 抛异常（R4-DIAG-001）。</summary>
        private sealed class ThrowingOrderPanel : IDiagnosticPanel
        {
            public string Title => "ThrowingOrder";

            public int Order => throw new InvalidOperationException("Order getter 坏了");

            public void Collect(IDiagnosticReport report) { }
        }

        /// <summary>登记后可由测试把 Order getter 弄坏（R4-DIAG-001 第二条路径）。</summary>
        private sealed class BreakableOrderPanel : IDiagnosticPanel
        {
            public bool Broken;

            public string Title => "Breakable";

            public int Order => Broken
                ? throw new InvalidOperationException("Order getter 坏了")
                : 0;

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

        [Test]
        public void Register_TitleGetterThrows_StillRegisters()
        {
            var panel = new ThrowingTitlePanel();

            LogAssert.Expect(LogType.Error, new Regex("读取诊断页签 Title 抛出异常"));

            DiagnosticsManager.Register(panel);

            Assert.AreEqual(1, DiagnosticsManager.PanelCount);
            Assert.IsTrue(DiagnosticsManager.IsRegistered(panel));
        }

        [Test]
        public void Register_SecondThrowingPanel_CollisionCheckDoesNotThrow()
        {
            var first = new ThrowingTitlePanel();
            var second = new ThrowingTitlePanel();

            LogAssert.Expect(LogType.Error, new Regex("读取诊断页签 Title 抛出异常"));
            DiagnosticsManager.Register(first);

            // 第二次登记：新页签读一次 + 冲突扫描对已在册页签读一次，两处都必须走兜底；
            // 同型兜底名 "(ThrowingTitlePanel)" 相等，还会命中标题重复的 Warning 分支。
            LogAssert.Expect(LogType.Error, new Regex("读取诊断页签 Title 抛出异常"));
            LogAssert.Expect(LogType.Error, new Regex("读取诊断页签 Title 抛出异常"));
            LogAssert.Expect(LogType.Warning, new Regex("已被 ThrowingTitlePanel 占用"));

            DiagnosticsManager.Register(second);

            Assert.AreEqual(2, DiagnosticsManager.PanelCount);
            Assert.IsTrue(DiagnosticsManager.IsRegistered(first));
            Assert.IsTrue(DiagnosticsManager.IsRegistered(second));
        }

        [Test]
        public void Register_OrderGetterThrows_StillRegistersAtEnd()
        {
            DiagnosticsManager.Register(new FakePanel("Normal", 0));
            var broken = new ThrowingOrderPanel();

            LogAssert.Expect(LogType.Error, new Regex("读取诊断页签 Order 抛出异常"));

            DiagnosticsManager.Register(broken);

            var buffer = new List<IDiagnosticPanel>();
            DiagnosticsManager.CopyPanels(buffer);
            Assert.AreEqual(2, buffer.Count);
            Assert.AreSame(broken, buffer[1], "坏 Order 应兜底排到末尾（int.MaxValue）");
        }

        [Test]
        public void Register_ExistingPanelOrderThrows_DoesNotBlockNewPanel()
        {
            var broken = new BreakableOrderPanel();
            DiagnosticsManager.Register(broken);
            broken.Broken = true;

            LogAssert.Expect(LogType.Error, new Regex("读取诊断页签 Order 抛出异常"));
            var added = new FakePanel("New", 10);
            DiagnosticsManager.Register(added);

            Assert.AreEqual(2, DiagnosticsManager.PanelCount);
            Assert.IsTrue(DiagnosticsManager.IsRegistered(added), "已登记页签的坏 Order 不得阻断新页签登记");
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
