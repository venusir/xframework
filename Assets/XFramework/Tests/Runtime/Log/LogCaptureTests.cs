using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LogLineDto = XFramework.XLog.Tests.LogTestKit.LogLineDto;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 全量捕获：引擎 / 第三方 / 未捕获异常的日志进同一份 JSONL，且**不与自己重复**。
    /// <para><b>本 fixture 判定一条设计上的推理</b>：回显去重依赖「Unity 的日志回调在调用线程上同步触发」
    /// （见 <c>ConsoleLogSink</c> 的回显深度）。<c>FrameworkLog_IsNotEchoedIntoCapture</c> 就是这条推理的判据——
    /// 它若红，说明回调是延后触发的，去重必须换成基于「线程 + 级别 + 正文」的窗口匹配。</para>
    /// <para><b>用 <c>[UnityTest]</c> + <c>yield return null</c></b>：无论回调是同步还是延后一帧，断言之前都先让出
    /// 一帧，这样「同步实现」与「延后实现」都跑得完，测的是结果不是时序。</para>
    /// </summary>
    [TestFixture]
    public class LogCaptureTests
    {
        #region Fixture

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "XLogCaptureTests-" + Guid.NewGuid().ToString("N"));
            LogTestKit.SetUp();
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

        private void ConfigureCapture(bool capture = true, LogLevel minimumLevel = LogLevel.Verbose)
        {
            LogManager.Configure(new LogOptions
            {
                MinimumLevel = minimumLevel,
                EnableConsoleSink = true,
                CaptureStackTrace = false,
                EnableFileSink = true,
                CaptureUnityLogs = capture,
                FileDirectory = _directory,
                ImmediateFlushMinLevel = LogLevel.Warning,
                FlushEveryEntries = 64,
            });
        }

        #endregion

        #region Helpers

        private List<LogLineDto> Entries()
        {
            return LogTestKit.ReadEntries(_directory);
        }

        /// <summary>找第一条正文匹配的条目；找不到返回 null。</summary>
        private LogLineDto Find(string message)
        {
            foreach (LogLineDto entry in Entries())
            {
                if (entry.msg == message)
                    return entry;
            }

            return null;
        }

        private int CountOf(string source, string message)
        {
            int count = 0;
            foreach (LogLineDto entry in Entries())
            {
                if (entry.src == source && entry.msg == message)
                    count++;
            }

            return count;
        }

        #endregion

        #region 去重（本条判定 R1 推理）

        [UnityTest]
        public IEnumerator FrameworkLog_IsNotEchoedIntoCapture()
        {
            ConfigureCapture();

            LogAssert.Expect(LogType.Warning, "[Save] 去重测试");
            LogManager.Warning(LogCategories.Save, "去重测试");

            yield return null; // 给「回调延后触发」的实现在这里暴露
            LogManager.Flush();

            Assert.AreEqual(1, CountOf("fw", "去重测试"), "框架日志应恰好写一条");
            Assert.AreEqual(0, CountOf("unity", "[Save] 去重测试"),
                "框架自己的输出被回显捕获 = 同一件事写了两遍（回调不是同步触发的，去重机制需要换成窗口匹配）");
        }

        #endregion

        #region 捕获形态

        [UnityTest]
        public IEnumerator RawUnityLog_IsCapturedAsIs()
        {
            ConfigureCapture();

            Debug.Log("裸日志");

            yield return null;
            LogManager.Flush();

            LogLineDto entry = Find("裸日志");
            Assert.IsNotNull(entry, "没有 [标签] 前缀的外部日志也要被收进来");
            Assert.AreEqual("unity", entry.src);
            Assert.AreEqual("info", entry.lvl);
            Assert.AreEqual("Unregistered", entry.cat, "解析不出 [标签] 时用可见的哨兵名");
        }

        [UnityTest]
        public IEnumerator TaggedWarning_IsCapturedWithCategory()
        {
            ConfigureCapture();

            LogAssert.Expect(LogType.Warning, "[Acme] 第三方警告");
            Debug.LogWarning("[Acme] 第三方警告");

            yield return null;
            LogManager.Flush();

            LogLineDto entry = Find("[Acme] 第三方警告");
            Assert.IsNotNull(entry);
            Assert.AreEqual("unity", entry.src);
            Assert.AreEqual("warning", entry.lvl);
            Assert.AreEqual("Acme", entry.cat, "第三方按 [标签] 约定打的日志要能被解析出分类");
        }

        [UnityTest]
        public IEnumerator UnityError_CarriesStack()
        {
            ConfigureCapture();

            LogAssert.Expect(LogType.Error, "[Acme] 炸了");
            Debug.LogError("[Acme] 炸了");

            yield return null;
            LogManager.Flush();

            LogLineDto entry = Find("[Acme] 炸了");
            Assert.IsNotNull(entry);
            Assert.AreEqual("error", entry.lvl);
            Assert.IsNotEmpty(entry.stack, "错误及以上要带上 Unity 给的堆栈——AI 定位崩溃现场靠它");
        }

        #endregion

        #region 开关与过滤

        [UnityTest]
        public IEnumerator CaptureDisabled_WritesNothing()
        {
            ConfigureCapture(capture: false);

            Debug.Log("不该被捕获");

            yield return null;
            LogManager.Flush();

            Assert.IsNull(Find("不该被捕获"));
        }

        [UnityTest]
        public IEnumerator MinimumLevel_AppliesToCapturedLogs()
        {
            ConfigureCapture(minimumLevel: LogLevel.Warning);

            LogAssert.Expect(LogType.Warning, "[Acme] 够门槛");
            Debug.Log("低于门槛");
            Debug.LogWarning("[Acme] 够门槛");

            yield return null;
            LogManager.Flush();

            Assert.IsNull(Find("低于门槛"), "捕获不该绕过档位过滤——否则 MinimumLevel 的「一律丢弃」自相矛盾");
            Assert.IsNotNull(Find("[Acme] 够门槛"));
        }

        #endregion
    }
}
