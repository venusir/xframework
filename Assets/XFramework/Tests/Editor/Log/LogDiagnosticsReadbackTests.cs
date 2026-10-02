using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XLog;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 日志回读面：<see cref="LogManager.CopyCategories"/> / <see cref="LogManager.CopySinks"/> /
    /// <see cref="LogManager.SinkCount"/>。
    /// <para>锁的是「生效档位」的口径：设了覆盖取覆盖值，否则取<b>当前</b>全局档——两者都在调用时现算，
    /// 因此改过全局档之后回读不会给出陈旧值。</para>
    /// </summary>
    [TestFixture]
    public class LogDiagnosticsReadbackTests
    {
        private LogCategory _category;
        private LogLevel _originalMinimum;

        [SetUp]
        public void SetUp()
        {
            _category = LogCategory.Get("DiagnosticsReadbackTests");
            _originalMinimum = LogManager.MinimumLevel;
            LogManager.ResetCategoryLevel(_category);
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.MinimumLevel = _originalMinimum;
            LogManager.ResetCategoryLevel(_category);
        }

        private static LogCategoryInfo Find(List<LogCategoryInfo> buffer, string name)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].Name == name)
                    return buffer[i];
            }

            Assert.Fail($"缓冲区里没有分类 '{name}'");
            return default;
        }

        [Test]
        public void CopyCategories_ClearsBufferAndContainsRequestedCategory()
        {
            var buffer = new List<LogCategoryInfo> { default, default };

            int count = LogManager.CopyCategories(buffer);

            Assert.AreEqual(buffer.Count, count);
            Assert.Greater(count, 0);
            var info = Find(buffer, _category.Name);
            Assert.AreEqual(_category.Name, info.Name);
        }

        [Test]
        public void CopyCategories_FollowsGlobalLevelWhenNotOverridden()
        {
            LogManager.MinimumLevel = LogLevel.Fatal;

            var buffer = new List<LogCategoryInfo>();
            LogManager.CopyCategories(buffer);

            var info = Find(buffer, _category.Name);
            Assert.IsFalse(info.IsOverridden);
            Assert.AreEqual(LogLevel.Fatal, info.EffectiveLevel);

            // 改全局档后立刻反映——不是注册时快照的旧值
            LogManager.MinimumLevel = LogLevel.Verbose;
            LogManager.CopyCategories(buffer);
            Assert.AreEqual(LogLevel.Verbose, Find(buffer, _category.Name).EffectiveLevel);
        }

        [Test]
        public void CopyCategories_ReportsOverride()
        {
            LogManager.SetCategoryLevel(_category, LogLevel.Error);

            var buffer = new List<LogCategoryInfo>();
            LogManager.CopyCategories(buffer);

            var info = Find(buffer, _category.Name);
            Assert.IsTrue(info.IsOverridden);
            Assert.AreEqual(LogLevel.Error, info.EffectiveLevel);
        }

        [Test]
        public void CopyCategories_NullBuffer_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => LogManager.CopyCategories(null));
        }

        [Test]
        public void CopySinks_IncludesAddedSink()
        {
            var sink = new RecordingSink();
            LogManager.AddSink(sink);
            try
            {
                var buffer = new List<ILogSink>();
                int count = LogManager.CopySinks(buffer);

                Assert.AreEqual(buffer.Count, count);
                CollectionAssert.Contains(buffer, sink);
                Assert.GreaterOrEqual(LogManager.SinkCount, 1);
            }
            finally
            {
                LogManager.RemoveSink(sink);
            }
        }

        [Test]
        public void CopySinks_NullBuffer_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => LogManager.CopySinks(null));
        }

        private sealed class RecordingSink : ILogSink
        {
            public void Write(in LogEntry entry) { }

            public void Flush() { }
        }
    }
}
