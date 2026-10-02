using System;
using NUnit.Framework;
using XFramework.XDiagnostics;

namespace XFramework.XDiagnostics.Tests
{
    /// <summary>
    /// <see cref="DiagnosticReport"/> / <see cref="DiagnosticTable"/>：条目的写入与读取口径。
    /// <para>这里锁的是**数据契约**——渲染方（编辑器窗口、文本渲染、将来的运行时覆盖层）都按这套口径读。</para>
    /// </summary>
    [TestFixture]
    public class DiagnosticReportTests
    {
        private DiagnosticReport _report;

        [SetUp]
        public void SetUp()
        {
            _report = new DiagnosticReport();
        }

        #region Items

        [Test]
        public void Section_Text_KeyValue_Notice_AppendInOrder()
        {
            _report.Section("小节");
            _report.Text("正文");
            _report.KeyValue("名", "值");
            _report.Notice(DiagnosticLevel.Warning, "注意");

            Assert.AreEqual(4, _report.Items.Count);

            Assert.AreEqual(DiagnosticItemKind.Section, _report.Items[0].Kind);
            Assert.AreEqual("小节", _report.Items[0].Text);

            Assert.AreEqual(DiagnosticItemKind.Text, _report.Items[1].Kind);
            Assert.AreEqual("正文", _report.Items[1].Text);

            Assert.AreEqual(DiagnosticItemKind.KeyValue, _report.Items[2].Kind);
            Assert.AreEqual("名", _report.Items[2].Text);
            Assert.AreEqual("值", _report.Items[2].Value);

            Assert.AreEqual(DiagnosticItemKind.Notice, _report.Items[3].Kind);
            Assert.AreEqual(DiagnosticLevel.Warning, _report.Items[3].Level);
            Assert.AreEqual("注意", _report.Items[3].Text);
        }

        [Test]
        public void NullText_IsNormalizedToEmptyString()
        {
            _report.Section(null);
            _report.Text(null);
            _report.KeyValue(null, null);
            _report.Notice(DiagnosticLevel.Info, null);

            Assert.AreEqual(string.Empty, _report.Items[0].Text);
            Assert.AreEqual(string.Empty, _report.Items[1].Text);
            Assert.AreEqual(string.Empty, _report.Items[2].Text);
            Assert.AreEqual(string.Empty, _report.Items[2].Value);
            Assert.AreEqual(string.Empty, _report.Items[3].Text);
        }

        [Test]
        public void NewReport_IsEmpty()
        {
            Assert.IsTrue(_report.IsEmpty);
        }

        [Test]
        public void Clear_Empties()
        {
            _report.Text("x");
            Assert.IsFalse(_report.IsEmpty);

            _report.Clear();

            Assert.IsTrue(_report.IsEmpty);
            Assert.AreEqual(0, _report.Items.Count);
        }

        #endregion

        #region BeginTable

        [Test]
        public void BeginTable_AppendsTableItem_AndReturnsFillableTable()
        {
            var table = _report.BeginTable("标题", "列A", "列B");
            table.AddRow("1", "2");

            Assert.AreEqual(1, _report.Items.Count);
            Assert.AreEqual(DiagnosticItemKind.Table, _report.Items[0].Kind);

            var written = _report.Items[0].Table;
            Assert.IsNotNull(written);
            Assert.AreSame(table, written);   // 报告与调用方共享同一实例
            Assert.AreEqual("标题", written.Title);
            Assert.AreEqual(2, written.ColumnCount);
            Assert.AreEqual(1, written.RowCount);
            Assert.AreEqual("1", written.GetCell(0, 0));
            Assert.AreEqual("2", written.GetCell(0, 1));
        }

        [Test]
        public void BeginTable_NullTitle_BecomesEmptyString()
        {
            var table = _report.BeginTable(null, "列");

            Assert.AreEqual(string.Empty, table.Title);
        }

        [Test]
        public void BeginTable_NullColumns_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _report.BeginTable("t", null));
        }

        [Test]
        public void BeginTable_EmptyColumns_Throws()
        {
            Assert.Throws<ArgumentException>(() => _report.BeginTable("t", Array.Empty<string>()));
        }

        #endregion

        #region DiagnosticTable

        [Test]
        public void Table_ColumnsAreCopied()
        {
            var columns = new[] { "原名" };
            var table = _report.BeginTable("t", columns);

            columns[0] = "改过";

            Assert.AreEqual("原名", table.Columns[0]);
        }

        [Test]
        public void Table_AddRow_CopiesCells()
        {
            var table = _report.BeginTable("t", "列");
            var cells = new[] { "原值" };
            table.AddRow(cells);

            cells[0] = "改过";

            Assert.AreEqual("原值", table.GetCell(0, 0));
        }

        [Test]
        public void Table_AddRowFewerCells_GetCellReturnsNull()
        {
            var table = _report.BeginTable("t", "A", "B", "C");
            table.AddRow("1");

            Assert.AreEqual(1, table.RowCount);
            Assert.AreEqual("1", table.GetCell(0, 0));
            Assert.IsNull(table.GetCell(0, 1));
            Assert.IsNull(table.GetCell(0, 2));
        }

        [Test]
        public void Table_AddRowMoreCells_ExtraIgnored()
        {
            var table = _report.BeginTable("t", "A");
            table.AddRow("1", "2", "3");

            Assert.AreEqual("1", table.GetCell(0, 0));
            Assert.IsNull(table.GetCell(0, 1));
        }

        [Test]
        public void Table_AddNullRow_AddsEmptyRow()
        {
            var table = _report.BeginTable("t", "A");
            table.AddRow(null);

            Assert.AreEqual(1, table.RowCount);
            Assert.IsNull(table.GetCell(0, 0));
        }

        [Test]
        public void Table_GetCell_OutOfRange_ReturnsNull()
        {
            var table = _report.BeginTable("t", "A");
            table.AddRow("1");

            Assert.IsNull(table.GetCell(-1, 0));
            Assert.IsNull(table.GetCell(1, 0));
            Assert.IsNull(table.GetCell(0, -1));
            Assert.IsNull(table.GetCell(0, 1));
        }

        #endregion
    }
}
