using System;
using System.Globalization;
using NUnit.Framework;
using XFramework.XLog.Internal;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 模板格式化的逐字保真：<see cref="LogFormatter"/> 的语义必须与字符串插值<b>完全一致</b>。
    /// <para><b>为什么每条都跟插值对照</b>：迁移把 193 处 <c>$"[Save] …{x}"</c> 改成模板 + 参数，唯一能
    /// 机械验证「文本没变」的办法就是让两边在同一输入下输出同一字符串。转义、null、格式说明符、culture
    /// 四处只要有一处不同，30+ 处 <c>LogAssert</c> 就会红——把风险钉在单元测试里，而不是等集成测试发现。</para>
    /// </summary>
    [TestFixture]
    public class LogFormatterTests
    {
        #region 基本形态

        [Test]
        public void Format_WithoutArgs_ReturnsMessage()
        {
            Assert.AreEqual("没有洞的消息", LogFormatter.Format("没有洞的消息"));
        }

        [Test]
        public void Format_NullMessage_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, LogFormatter.Format(null));
        }

        [Test]
        public void Format_OneArg_MatchesInterpolation()
        {
            const string path = "slot/1.sav";
            Assert.AreEqual($"[Save] 跳过空存档文件: {path}", "[Save] " + LogFormatter.Format("跳过空存档文件: {0}", path));
        }

        [Test]
        public void Format_TwoArgs_KeepsHoleOrder()
        {
            Assert.AreEqual("a=1, b=two", LogFormatter.Format("a={0}, b={1}", 1, "two"));
        }

        [Test]
        public void Format_ThreeArgs_KeepsHoleOrder()
        {
            Assert.AreEqual("1-2-3", LogFormatter.Format("{0}-{2}-{1}", 1, 3, 2));
        }

        #endregion

        #region 与插值同源的四个语义

        [Test]
        public void Format_NullArgument_RendersEmptyLikeInterpolation()
        {
            string expected = $"值: {(string)null}";
            Assert.AreEqual(expected, LogFormatter.Format("值: {0}", (string)null));
        }

        [Test]
        public void Format_EscapedBraces_MatchInterpolation()
        {
            string expected = $"版本 {1} 的结构: {{Version, Data}}";
            Assert.AreEqual(expected, LogFormatter.Format("版本 {0} 的结构: {{Version, Data}}", 1));
        }

        [Test]
        public void Format_FormatSpecifier_MatchInterpolation()
        {
            const float seconds = 12.3456f;
            Assert.AreEqual($"用时 {seconds:F2}s", LogFormatter.Format("用时 {0:F2}s", seconds));
            Assert.AreEqual($"占比 {seconds:0.##}", LogFormatter.Format("占比 {0:0.##}", seconds));
        }

        [Test]
        public void Format_UsesCurrentCulture_LikeInterpolation()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // 小数逗号：能把 culture 差异照出来

                const double value = 1234.5d;
                Assert.AreEqual($"{value:N2}", LogFormatter.Format("{0:N2}", value));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        #endregion

        #region 永不抛

        [Test]
        public void Format_RawBraceInTemplate_ReturnsTemplateInsteadOfThrowing()
        {
            Assert.AreEqual("JSON: {", LogFormatter.Format("JSON: {"));
        }

        [Test]
        public void Format_ThrowingToString_FallsBackToTemplate()
        {
            Assert.AreEqual("坏参数: {0}", LogFormatter.Format("坏参数: {0}", new ThrowingToString()));
        }

        private sealed class ThrowingToString
        {
            public override string ToString()
            {
                throw new InvalidOperationException("ToString 抛了");
            }
        }

        #endregion
    }
}
