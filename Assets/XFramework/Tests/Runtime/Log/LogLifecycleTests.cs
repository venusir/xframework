using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 生命周期与 sink 契约：复位语义、替换语义、以及「日志永不抛」在 sink 违反契约时的表现。
    /// <para><b>这些用例守的是同一条底线</b>：日志是错误路径的最后一张面孔。它自己死掉、卡死或抛异常，
    /// 都会把「本来只是想记一条日志」升级成二次故障——所以每一条都值得单独钉住。</para>
    /// </summary>
    [TestFixture]
    public class LogLifecycleTests
    {
        #region Fixture

        [SetUp]
        public void SetUp()
        {
            LogTestKit.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            LogTestKit.TearDown();
        }

        #endregion

        #region Test Doubles

        /// <summary>计数 + 可观测释放的 sink。</summary>
        private sealed class CountingSink : ILogSink, IDisposable
        {
            public int Writes;
            public bool Disposed;

            public void Write(in LogEntry entry) => Writes++;

            public void Flush()
            {
            }

            public void Dispose() => Disposed = true;
        }

        /// <summary>违反契约（<c>Write</c> 抛异常）的 sink。</summary>
        private sealed class ThrowingSink : ILogSink
        {
            public int Writes;

            public void Write(in LogEntry entry)
            {
                Writes++;
                throw new InvalidOperationException("sink 自己坏了");
            }

            public void Flush()
            {
            }
        }

        /// <summary>在 <c>Write</c> 里反过来打日志的 sink：验证分发路径可重入、不会自锁或栈溢出。</summary>
        private sealed class ReentrantSink : ILogSink
        {
            public int MaxDepth;

            private int _depth;

            public void Write(in LogEntry entry)
            {
                if (_depth >= 3)
                    return; // 自己收口，否则就是用例造出来的无限递归

                _depth++;
                if (_depth > MaxDepth)
                    MaxDepth = _depth;

                LogManager.Info(LogCategories.Data, "嵌套 {0}", _depth);

                _depth--;
            }

            public void Flush()
            {
            }
        }

        #endregion

        #region 复位与替换

        /// <summary>
        /// <c>AutoInit</c> 会丢弃实现、重挂退出订阅、复位会话；重复执行必须无害
        /// （关闭域重载时它每次进入播放都会跑，且编辑器侧还会先跑一次）。
        /// </summary>
        [Test]
        public void AutoInit_RunTwice_KeepsLoggingUsable()
        {
            LogManager.AutoInit();
            LogManager.AutoInit();

            LogTestKit.SetUp(); // AutoInit 会装入默认配置的实现；用例在此回到确定性配置

            LogAssert.Expect(LogType.Log, "[Save] 两次 AutoInit 之后仍可用");
            LogManager.Info(LogCategories.Save, "两次 AutoInit 之后仍可用");
        }

        /// <summary>
        /// <c>Shutdown</c> 是复位入口：冲刷、释放输出端、**还原分类档位**，并把门面置回未装入。
        /// 但日志**没有「死掉」状态**——下一次使用按默认配置重建。
        /// </summary>
        [Test]
        public void Shutdown_ResetsCategoryLevels_AndStaysUsable()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);
            Assert.IsFalse(LogManager.IsEnabled(LogLevel.Error, LogCategories.Save));

            LogManager.Shutdown();

            Assert.IsTrue(LogManager.IsEnabled(LogLevel.Error, LogCategories.Save),
                "Shutdown 必须把分类档位还原为「跟随全局」，否则上一个会话的静音会漏进下一个会话");

            LogManager.Configure(new LogOptions
            {
                MinimumLevel = LogLevel.Verbose,
                EnableConsoleSink = true,
                EnableFileSink = false,
                CaptureUnityLogs = false,
            });

            LogAssert.Expect(LogType.Log, "[Save] Shutdown 之后仍然可用");
            LogManager.Info(LogCategories.Save, "Shutdown 之后仍然可用");
        }

        /// <summary><c>Configure</c> 重建实现：旧的自定义 sink 被释放、且不再收到条目。</summary>
        [Test]
        public void Configure_ReplacesAndDisposesPreviousSinks()
        {
            var custom = new CountingSink();
            LogManager.AddSink(custom);

            // 直接走 Configure（而不是 LogTestKit.SetUp——它内部先 Shutdown，释放就归 Shutdown 了）
            LogManager.Configure(new LogOptions
            {
                MinimumLevel = LogLevel.Verbose,
                EnableConsoleSink = true,
                EnableFileSink = false,
                CaptureUnityLogs = false,
            });

            Assert.IsTrue(custom.Disposed, "被替换的 sink 应被释放（可选能力接口的约定）");

            LogAssert.Expect(LogType.Log, "[Save] 新实现");
            LogManager.Info(LogCategories.Save, "新实现");

            Assert.AreEqual(0, custom.Writes, "旧 sink 不该再收到任何条目");
        }

        #endregion

        #region Sink 违约

        /// <summary>
        /// <c>Write</c> 抛异常的 sink 被静默摘除并计数。
        /// <para><b>「静默」本身由测试框架兜底</b>：摘除路径若记了日志，那会是一条意外 Error，本用例直接判红
        /// ——不需要额外的断言。</para>
        /// </summary>
        [Test]
        public void ThrowingSink_IsSilentlyDroppedAndCounted()
        {
            int droppedBefore = LogManager.DroppedSinkCount;

            var sink = new ThrowingSink();
            LogManager.AddSink(sink);

            LogAssert.Expect(LogType.Log, "[Save] 第一次");
            LogManager.Info(LogCategories.Save, "第一次");

            LogAssert.Expect(LogType.Log, "[Save] 第二次");
            LogManager.Info(LogCategories.Save, "第二次");

            Assert.AreEqual(1, sink.Writes, "抛异常的 sink 应在第一次抛出后被摘除");
            Assert.AreEqual(droppedBefore + 1, LogManager.DroppedSinkCount);
        }

        /// <summary>
        /// sink 在自己的 <c>Write</c> 里再写日志：分发路径不得自锁、不得栈溢出。
        /// <para>能成立的原因是分发**不持锁**——sink 数组是 copy-on-write 快照，写路径的锁只在增删 sink 时持有。</para>
        /// </summary>
        [Test]
        public void ReentrantSink_DoesNotDeadlockOrOverflow()
        {
            var reentrant = new ReentrantSink();
            LogManager.AddSink(reentrant);

            LogAssert.Expect(LogType.Log, "[Save] 外层");
            LogManager.Info(LogCategories.Save, "外层");

            Assert.AreEqual(3, reentrant.MaxDepth, "嵌套写入应当真的发生过，否则这条用例什么都没测");
        }

        #endregion
    }
}
