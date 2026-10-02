using System;
using System.Text;
using NUnit.Framework;
using XFramework.XDiagnostics;

namespace XFramework.XDiagnostics.Tests
{
    /// <summary>
    /// <see cref="DiagnosticReportFormatter"/>：报告 → 纯文本的逐字输出。
    /// <para>「复制」按钮与「把状态贴出去」的场景都吃这条通道，故断言到字符级（含列宽与行尾空白口径）。</para>
    /// </summary>
    [TestFixture]
    public class DiagnosticReportFormatterTests
    {
        #region Empty / Basic

        [Test]
        public void EmptyReport_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, DiagnosticReportFormatter.ToText(new DiagnosticReport()));
        }

        [Test]
        public void Section_FormatsWithDelimiters()
        {
            var report = new DiagnosticReport();
            report.Section("小节");

            Assert.AreEqual("== 小节 ==", DiagnosticReportFormatter.ToText(report));
        }

        [Test]
        public void KeyValue_FormatsAsLabelColonValue()
        {
            var report = new DiagnosticReport();
            report.KeyValue("名", "值");

            Assert.AreEqual("名: 值", DiagnosticReportFormatter.ToText(report));
        }

        [Test]
        public void Text_IsVerbatim()
        {
            var report = new DiagnosticReport();
            report.Text("第一行\n第二行");

            Assert.AreEqual("第一行\n第二行", DiagnosticReportFormatter.ToText(report));
        }

        [Test]
        public void Notice_TagsByLevel()
        {
            var report = new DiagnosticReport();
            report.Notice(DiagnosticLevel.Info, "i");
            report.Notice(DiagnosticLevel.Warning, "w");
            report.Notice(DiagnosticLevel.Error, "e");

            Assert.AreEqual("[INFO] i\n[WARN] w\n[ERROR] e", DiagnosticReportFormatter.ToText(report));
        }

        [Test]
        public void UnknownLevel_FallsBackToInfo()
        {
            var report = new DiagnosticReport();
            report.Notice((DiagnosticLevel)99, "x");

            Assert.AreEqual("[INFO] x", DiagnosticReportFormatter.ToText(report));
        }

        [Test]
        public void Items_AreSeparatedBySingleNewline()
        {
            var report = new DiagnosticReport();
            report.Section("A");
            report.Text("B");
            report.KeyValue("C", "D");

            Assert.AreEqual("== A ==\nB\nC: D", DiagnosticReportFormatter.ToText(report));
        }

        #endregion

        #region Tables

        [Test]
        public void Table_AlignsColumnsAndOmitsTrailingPadding()
        {
            var report = new DiagnosticReport();
            var table = report.BeginTable("池", "A", "B");
            table.AddRow("1", "22");
            table.AddRow("333", "4");

            string text = DiagnosticReportFormatter.ToText(report);
            var lines = text.Split('\n');

            Assert.AreEqual(5, lines.Length);
            Assert.AreEqual("--- 池 ---", lines[0]);
            Assert.AreEqual("A   | B", lines[1]);
            Assert.AreEqual("----+---", lines[2]);   // 分隔线按列宽画，与表头左边缘对位
            Assert.AreEqual("1   | 22", lines[3]);
            Assert.AreEqual("333 | 4", lines[4]);
        }

        [Test]
        public void Table_WithoutTitle_OmitsTitleLine()
        {
            var report = new DiagnosticReport();
            var table = report.BeginTable(string.Empty, "A");
            table.AddRow("1");

            var lines = DiagnosticReportFormatter.ToText(report).Split('\n');

            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("A", lines[0]);
            Assert.AreEqual("-", lines[1]);
            Assert.AreEqual("1", lines[2]);
        }

        [Test]
        public void Table_EmptyTable_RendersHeaderOnly()
        {
            var report = new DiagnosticReport();
            report.BeginTable("空表", "A", "B");

            Assert.AreEqual("--- 空表 ---\nA | B\n--+--", DiagnosticReportFormatter.ToText(report));
        }

        [Test]
        public void Table_MiddleMissingCell_KeepsAlignment()
        {
            var report = new DiagnosticReport();
            var table = report.BeginTable("t", "AA", "BB", "CC");
            table.AddRow("1", null, "3");

            var lines = DiagnosticReportFormatter.ToText(report).Split('\n');

            Assert.AreEqual("AA | BB | CC", lines[1]);
            Assert.AreEqual("1  |    | 3", lines[3]);   // 缺的格子按列宽补空格，后续列仍对位
        }

        [Test]
        public void Table_TrailingEmptyCells_ProduceNoTrailingWhitespace()
        {
            var report = new DiagnosticReport();
            var table = report.BeginTable("t", "AA", "BB");
            table.AddRow("1");

            var lines = DiagnosticReportFormatter.ToText(report).Split('\n');

            Assert.AreEqual("1", lines[3]);
        }

        [Test]
        public void Table_CellNewline_ReplacedBySpace()
        {
            var report = new DiagnosticReport();
            var table = report.BeginTable("t", "A");
            table.AddRow("x\ny");

            var lines = DiagnosticReportFormatter.ToText(report).Split('\n');

            Assert.AreEqual(4, lines.Length);   // 标题 + 表头 + 分隔线 + 一行；格内换行不产生额外的行
            Assert.AreEqual("x y", lines[3]);
        }

        #endregion

        #region StringBuilder Overload

        [Test]
        public void ToText_AppendsToExistingBuilder()
        {
            var report = new DiagnosticReport();
            report.Text("新");

            var builder = new StringBuilder("前缀|");
            DiagnosticReportFormatter.ToText(report, builder);

            Assert.AreEqual("前缀|新", builder.ToString());
        }

        [Test]
        public void ToText_NullReport_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DiagnosticReportFormatter.ToText(null));
            Assert.Throws<ArgumentNullException>(() => DiagnosticReportFormatter.ToText(null, new StringBuilder()));
        }

        [Test]
        public void ToText_NullBuilder_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DiagnosticReportFormatter.ToText(new DiagnosticReport(), null));
        }

        #endregion
    }
}
