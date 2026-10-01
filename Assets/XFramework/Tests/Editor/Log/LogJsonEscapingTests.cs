using System;
using System.Text;
using NUnit.Framework;
using XFramework.XLog.Internal;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// JSONL 转义的逐字断言。
    /// <para><b>为什么值得单独一个 fixture</b>：「一条日志一行」与「中文可读」是 AI 分析面的两条硬前提，
    /// 而它们的实现只有三个转义分支。任何一处写漏（例如忘了 <c>\r</c>）都不会让功能用例变红——
    /// 只会让日志文件在某台机器上悄悄错行，直到有人拿它去分析才发现。</para>
    /// </summary>
    [TestFixture]
    public class LogJsonEscapingTests
    {
        #region 转义

        [Test]
        public void QuoteAndBackslash_AreEscaped()
        {
            Assert.AreEqual("\"a\\\"b\"", Escape("a\"b"));
            Assert.AreEqual("\"a\\\\b\"", Escape("a\\b"));
        }

        [Test]
        public void LineBreaksAndTabs_AreEscaped()
        {
            Assert.AreEqual("\"a\\nb\\tc\\rd\"", Escape("a\nb\tc\rd"));
        }

        [Test]
        public void ControlCharacters_UseUnicodeEscape()
        {
            Assert.AreEqual("\"\\u0001\"", Escape("\u0001"));
            Assert.AreEqual("\"\\u001f\"", Escape("\u001f"));
        }

        /// <summary>中文原样保留——转成 <c>\uXXXX</c> 只会让人读不懂、AI 读更慢。</summary>
        [Test]
        public void NonAscii_IsKeptRaw()
        {
            Assert.AreEqual("\"存档损坏：slot/1.sav\"", Escape("存档损坏：slot/1.sav"));
        }

        [Test]
        public void Null_BecomesEmptyString()
        {
            Assert.AreEqual("\"\"", Escape(null));
        }

        #endregion

        #region 时间戳

        [Test]
        public void IsoTimestamp_HasMillisecondPrecisionInUtc()
        {
            var sb = new StringBuilder();
            JsonLineWriter.AppendIsoTimestamp(sb, new DateTime(2026, 10, 1, 11, 30, 12, 345, DateTimeKind.Utc));

            Assert.AreEqual("2026-10-01T11:30:12.345Z", sb.ToString());
        }

        [Test]
        public void IsoTimestamp_IsCultureIndependent()
        {
            var sb = new StringBuilder();
            JsonLineWriter.AppendIsoTimestamp(sb, new DateTime(2026, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc));

            Assert.AreEqual("2026-01-02T03:04:05.006Z", sb.ToString());
        }

        #endregion

        #region 缓冲复用

        /// <summary>线程本地缓冲每次 <c>Rent</c> 必须清空——否则上一条日志的尾巴会粘到下一条上。</summary>
        [Test]
        public void Rent_ReturnsClearedBuffer()
        {
            StringBuilder first = JsonLineWriter.Rent();
            first.Append("残留内容");

            StringBuilder second = JsonLineWriter.Rent();

            Assert.AreSame(first, second, "同一线程应复用同一个缓冲");
            Assert.AreEqual(0, second.Length, "复用前必须清空");
        }

        #endregion

        private static string Escape(string value)
        {
            var sb = new StringBuilder();
            JsonLineWriter.AppendEscaped(sb, value);
            return sb.ToString();
        }
    }
}
