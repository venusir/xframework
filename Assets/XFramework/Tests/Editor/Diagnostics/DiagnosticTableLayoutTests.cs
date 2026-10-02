using System;
using NUnit.Framework;
using XFramework.Editor.Diagnostics;

namespace XFramework.Editor.Diagnostics.Tests
{
    /// <summary>
    /// <see cref="DiagnosticTableLayout"/>：列宽分配的策略锁。
    /// <para>渲染层里唯一能脱离 IMGUI 测试的部分（GUILayout 调用本身不可测），因此策略的每条分界
    /// ——放得下 / 放不下 / 余地用尽 / 已在下限——都在这里钉死。</para>
    /// </summary>
    [TestFixture]
    public class DiagnosticTableLayoutTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void Allocate_FitsAvailable_ReturnsNaturalWidths()
        {
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 50f, 80f }, 200f, 40f);

            Assert.AreEqual(50f, widths[0], Tolerance);
            Assert.AreEqual(80f, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_ExactFit_ReturnsNaturalWidths()
        {
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 50f, 80f }, 130f, 40f);

            Assert.AreEqual(50f, widths[0], Tolerance);
            Assert.AreEqual(80f, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_Overflows_ShrinksProportionallyToShrinkableRoom()
        {
            // 自然宽 100/200（总 300），可用 240 → 需收缩 60；可收缩余地 = 90 + 190 = 280
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 100f, 200f }, 240f, 10f);

            const float factor = 60f / 280f;
            Assert.AreEqual(100f - 90f * factor, widths[0], Tolerance);
            Assert.AreEqual(200f - 190f * factor, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_OverflowBeyondRoom_ClampsEveryColumnToMinWidth()
        {
            // 余地 180、溢出 195 > 余地 → 全部压到下限
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 100f, 100f }, 5f, 10f);

            Assert.AreEqual(10f, widths[0], Tolerance);
            Assert.AreEqual(10f, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_OverflowEqualsRoom_ClampsEveryColumnToMinWidth()
        {
            // 余地 180、溢出恰好 180 → 正好全部压到下限
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 100f, 100f }, 20f, 10f);

            Assert.AreEqual(10f, widths[0], Tolerance);
            Assert.AreEqual(10f, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_ColumnNarrowerThanMin_IsNotGrown()
        {
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 5f, 100f }, 105f, 10f);

            Assert.AreEqual(5f, widths[0], Tolerance);
            Assert.AreEqual(100f, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_NoColumnCanShrink_ReturnsNaturalWidths()
        {
            // 两列都已在下限：宁可溢出，也不继续压（压下去只剩省略号）
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 10f, 10f }, 5f, 10f);

            Assert.AreEqual(10f, widths[0], Tolerance);
            Assert.AreEqual(10f, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_NonPositiveAvailable_ReturnsNaturalWidths()
        {
            // 视图宽度未知（0）时不做任何压缩——压缩的前提是知道可用多少
            float[] widths = DiagnosticTableLayout.Allocate(new[] { 100f, 100f }, 0f, 10f);

            Assert.AreEqual(100f, widths[0], Tolerance);
            Assert.AreEqual(100f, widths[1], Tolerance);
        }

        [Test]
        public void Allocate_NullOrEmpty_ReturnsEmptyArray()
        {
            Assert.AreEqual(0, DiagnosticTableLayout.Allocate(null, 100f, 10f).Length);
            Assert.AreEqual(0, DiagnosticTableLayout.Allocate(Array.Empty<float>(), 100f, 10f).Length);
        }
    }
}
