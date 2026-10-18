using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using XFramework.XLog;
using XFramework.XLog.Internal;

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
            // LOG-007：原断言往缓冲里塞 default 项——CopyCategories 即使不清空也照样通过。
            // 用带名字的哨兵才有判别力。
            var buffer = new List<LogCategoryInfo>
            {
                new LogCategoryInfo("__stale_sentinel__", LogLevel.Fatal, true),
            };

            int count = LogManager.CopyCategories(buffer);

            Assert.AreEqual(buffer.Count, count);
            Assert.Greater(count, 0);
            for (int i = 0; i < buffer.Count; i++)
                Assert.AreNotEqual("__stale_sentinel__", buffer[i].Name, "CopyCategories 必须先清空缓冲区");
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

        [Test]
        public void RemoveSink_DisposesSinkExactlyOnce()
        {
            // LOG-007：RemoveSink 的释放契约此前零覆盖
            var sink = new RecordingSink();
            LogManager.AddSink(sink);

            Assert.IsTrue(LogManager.RemoveSink(sink), "已登记的 sink 应被移除");
            Assert.AreEqual(1, sink.DisposeCount, "摘除时应释放一次");
            Assert.IsFalse(LogManager.RemoveSink(sink), "已摘除的 sink 再移除返回 false");
            Assert.AreEqual(1, sink.DisposeCount, "不得二次释放");
        }

        [Test]
        public void FileSink_UnwritableDirectory_GoesDeadAndIsDropped()
        {
            // LOG-007：IsDead 测试缝此前未被使用——验证 IO 失败后 sink 静默停用、管理器把它摘除并计数
            var blockingFile = Path.GetTempFileName();
            var sink = new LogFileSink(new LogOptions
            {
                FileDirectory = Path.Combine(blockingFile, "nested"), // 父路径是文件 → 建目录必失败
            });

            try
            {
                LogManager.AddSink(sink);
                int before = LogManager.DroppedSinkCount;

                // 第一次派发：sink 尝试开文件失败 → 自我停用并被摘除、计数（同一次派发内完成）
                LogManager.Info(_category, "probe");
                Assert.IsTrue(sink.IsDead, "IO 失败后文件 sink 应静默停用");
                Assert.Greater(LogManager.DroppedSinkCount, before,
                    "死 sink 应被摘除并计入 DroppedSinkCount（LOG-003）");
            }
            finally
            {
                LogManager.RemoveSink(sink); // 若仍在表里则摘除并释放
                sink.Dispose();
                File.Delete(blockingFile);
            }
        }

        private sealed class RecordingSink : ILogSink, System.IDisposable
        {
            public int DisposeCount;

            public void Write(in LogEntry entry) { }

            public void Flush() { }

            public void Dispose() => DisposeCount++;
        }
    }
}
