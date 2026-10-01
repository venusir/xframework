using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using LogLineDto = XFramework.XLog.Tests.LogTestKit.LogLineDto;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 并发契约：日志可能来自任意线程，文件必须一行不烂、序号必须能定序。
    /// <para><b>为什么把控制台关掉</b>：本 fixture 要量的是文件输出端与序号分配，跨线程调 <c>Debug.Log</c>
    /// 会把 Unity 自己的日志链路扯进来（那是 <see cref="LogCaptureTests"/> 的题目）。关掉它，800 条一个不多一个不少。</para>
    /// <para><b>这一条锁的是「AI 分析的前提」</b>：并发下若出现交错残行或重复序号，整份文件就不再是可解析的时间线。</para>
    /// </summary>
    [TestFixture]
    public class LogConcurrencyTests
    {
        #region Fixture

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "XLogConcurrencyTests-" + Guid.NewGuid().ToString("N"));
            LogTestKit.SetUp();
            LogManager.Configure(new LogOptions
            {
                MinimumLevel = LogLevel.Verbose,
                EnableConsoleSink = false,
                CaptureStackTrace = false,
                EnableFileSink = true,
                CaptureUnityLogs = false,
                FileDirectory = _directory,
                ImmediateFlushMinLevel = LogLevel.Warning,
                FlushEveryEntries = 64,
            });
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Shutdown();

            try
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, true);
            }
            catch (Exception)
            {
                // 临时目录删不掉不影响断言
            }
        }

        #endregion

        [Test]
        public void ConcurrentWriters_ProduceIntactLinesAndUniqueSequence()
        {
            const int threadCount = 4;
            const int perThread = 200;

            var workers = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int workerId = t;
                workers[t] = new Thread(() =>
                {
                    for (int i = 0; i < perThread; i++)
                        LogManager.Info(LogCategories.Save, "线程 {0} 第 {1} 条", workerId, i);
                });
                workers[t].Start();
            }

            foreach (Thread worker in workers)
                worker.Join();

            LogManager.Flush();

            List<LogLineDto> entries = LogTestKit.ReadEntries(_directory);

            Assert.AreEqual(threadCount * perThread, entries.Count, "并发写入不许丢条目");

            var sequences = new HashSet<int>();
            var seen = new HashSet<string>();
            foreach (LogLineDto entry in entries)
            {
                Assert.IsTrue(sequences.Add(entry.seq), $"序号重复：{entry.seq}（并发下无法定序）");
                Assert.IsTrue(seen.Add(entry.msg), $"条目缺失或重复：{entry.msg}");
            }

            // 每一条都能被自己的正文认领 —— 交错残行会让这条对不上号
            for (int t = 0; t < threadCount; t++)
            {
                for (int i = 0; i < perThread; i++)
                    Assert.IsTrue(seen.Contains($"线程 {t} 第 {i} 条"), $"缺少：线程 {t} 第 {i} 条");
            }
        }

        /// <summary>
        /// 工作线程上的 <c>Debug.Log</c> 也要被捕获——这是挂
        /// <c>logMessageReceivedThreaded</c>（而不是非 Threaded 的那个）的全部理由。
        /// </summary>
        [Test]
        public void UnityLogFromWorkerThread_IsCaptured()
        {
            LogManager.Configure(new LogOptions
            {
                MinimumLevel = LogLevel.Verbose,
                EnableConsoleSink = false,
                CaptureStackTrace = false,
                EnableFileSink = true,
                CaptureUnityLogs = true,
                FileDirectory = _directory,
                ImmediateFlushMinLevel = LogLevel.Warning,
                FlushEveryEntries = 64,
            });

            var worker = new Thread(() => Debug.Log("来自工作线程"));
            worker.Start();
            worker.Join(); // 回调在调用线程上同步触发，Join 返回即已落盘

            LogManager.Flush();

            LogLineDto found = null;
            foreach (LogLineDto entry in LogTestKit.ReadEntries(_directory))
            {
                if (entry.msg == "来自工作线程")
                    found = entry;
            }

            Assert.IsNotNull(found, "工作线程的 Unity 日志必须同样被捕获");
            Assert.AreEqual("unity", found.src);
            Assert.AreNotEqual(0, found.thread);
        }
    }
}
