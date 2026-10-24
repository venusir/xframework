using NUnit.Framework;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// <see cref="LubanProcessOutputBuffer"/> 的上限与截断用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanProcessRunnerTests
    {
        [Test]
        public void OutputBuffer_AppendsLines()
        {
            var buffer = new LubanProcessOutputBuffer();

            buffer.AppendLine("a");
            buffer.AppendLine("b");

            Assert.That(buffer.ToString(), Does.Contain("a").And.Contain("b"));
            Assert.That(buffer.IsTruncated, Is.False);
        }

        [Test]
        public void OutputBuffer_CapsAtMaxChars()
        {
            var buffer = new LubanProcessOutputBuffer(20);

            for (int i = 0; i < 100; i++)
            {
                buffer.AppendLine("0123456789");
            }

            string text = buffer.ToString();
            Assert.That(buffer.IsTruncated, Is.True);
            Assert.That(text, Does.Contain("output truncated"));
            Assert.That(text.Length, Is.LessThanOrEqualTo(
                20 + "... [output truncated]".Length + 2));
        }

        [Test]
        public void OutputBuffer_AfterTruncation_IgnoresAdditionalLines()
        {
            var buffer = new LubanProcessOutputBuffer(5);
            buffer.AppendLine("1234567890");
            string first = buffer.ToString();

            buffer.AppendLine("more");

            Assert.That(buffer.ToString(), Is.EqualTo(first));
        }
    }
}
