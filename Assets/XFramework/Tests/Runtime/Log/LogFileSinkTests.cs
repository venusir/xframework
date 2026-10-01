using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// JSONL 文件输出端的端到端用例：schema、会话头、一行一条、flush 策略、切分、轮转、失败即静默。
    /// <para><b>为什么断言落在「文件读回来的内容」而不是内部状态</b>：这份文件的消费者是 AI 与外部分析脚本，
    /// 契约就是磁盘上那几行文本。内部状态对了而落盘错了（键序、转义、行尾）等于没对。</para>
    /// <para><b>解析用 <c>JsonUtility</c></b>：它同时充当「输出是不是合法 JSON」的验证者——转义写错时它会抛。
    /// 数字字段在 DTO 里一律用 <c>int</c>（Unity 的序列化器不支持 <c>long</c>），不影响断言语义。</para>
    /// </summary>
    [TestFixture]
    public class LogFileSinkTests
    {
        #region Test Doubles

        [Serializable]
        private class LogLineDto
        {
            public string t;
            public int v;
            public string session;
            public int seq;
            public string ts;
            public int up;
            public string lvl;
            public string cat;
            public string msg;
            public int frame;
            public int thread;
            public string src;
            public string exc;
            public string stack;
            public int part;
            public string unity;
        }

        #endregion

        #region Fixture

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "XLogTests-" + Guid.NewGuid().ToString("N"));
            LogTestKit.SetUp();
            ConfigureFileSink();
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

        private void ConfigureFileSink(long maxFileBytes = 32 * 1024 * 1024, int maxRetainedFiles = 10, string directory = null)
        {
            LogManager.Configure(new LogOptions
            {
                MinimumLevel = LogLevel.Verbose,
                EnableConsoleSink = true,
                CaptureStackTrace = false,
                EnableFileSink = true,
                FileDirectory = directory ?? _directory,
                MaxFileBytes = maxFileBytes,
                MaxRetainedFiles = maxRetainedFiles,
                ImmediateFlushMinLevel = LogLevel.Warning,
                FlushEveryEntries = 64,
            });
        }

        #endregion

        #region Helpers

        private string[] Files()
        {
            string[] files = Directory.GetFiles(_directory, "*.jsonl");
            Array.Sort(files, StringComparer.Ordinal); // 文件名带时间戳，序号序即时间序
            return files;
        }

        /// <summary>
        /// 读取文件全部行。
        /// <para><b>必须显式传 <c>FileShare.ReadWrite</c></b>：Windows 的共享规则是双向的，写入端还开着写句柄，
        /// 读者只声明「允许别人读」照样冲突——<c>File.ReadAllLines</c> 内部就是这种声明，所以不能用它。
        /// 这条同时是给外部分析脚本的提示（模块 README 已写明）。</para>
        /// </summary>
        private static string[] Lines(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var lines = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
                return lines.ToArray();
            }
        }

        private static LogLineDto Parse(string line)
        {
            return JsonUtility.FromJson<LogLineDto>(line);
        }

        #endregion

        #region Schema

        [Test]
        public void FirstLineIsSessionHeader()
        {
            LogManager.Info(LogCategories.Save, "hello");
            LogManager.Flush();

            LogLineDto header = Parse(Lines(Files()[0])[0]);

            Assert.AreEqual("session", header.t);
            Assert.AreEqual(1, header.v);
            Assert.AreEqual(1, header.part);
            Assert.IsNotEmpty(header.session);
            Assert.IsNotEmpty(header.unity, "会话头要能回答「这是哪个引擎版本跑出来的」");
            Assert.AreEqual("verbose", header.lvl, "会话头记录的是配置的全局档位");
        }

        [Test]
        public void EachEntryIsOneLine_WithRequiredFields()
        {
            LogAssert.Expect(LogType.Warning, "[Save] 跳过空存档文件: slot/1.sav");
            LogAssert.Expect(LogType.Error, "[Data] 未注册的数据块: demo");

            LogManager.Warning(LogCategories.Save, "跳过空存档文件: {0}", "slot/1.sav");
            LogManager.Error(LogCategories.Data, "未注册的数据块: {0}", "demo");
            LogManager.Flush();

            string[] lines = Lines(Files()[0]);
            Assert.AreEqual(3, lines.Length, "头行 + 两条日志");

            LogLineDto header = Parse(lines[0]);
            LogLineDto first = Parse(lines[1]);
            LogLineDto second = Parse(lines[2]);

            Assert.AreEqual("log", first.t);
            Assert.AreEqual(header.session, first.session);
            Assert.AreEqual("warning", first.lvl);
            Assert.AreEqual("Save", first.cat);
            Assert.AreEqual("跳过空存档文件: slot/1.sav", first.msg);
            Assert.AreEqual("fw", first.src);
            Assert.IsNotEmpty(first.ts);
            Assert.Greater(first.thread, 0);
            Assert.Less(first.seq, second.seq, "seq 必须能定序");

            Assert.AreEqual("error", second.lvl);
            Assert.AreEqual("Data", second.cat);
        }

        [Test]
        public void SeverityTokens_AreStableLowercase()
        {
            LogAssert.Expect(LogType.Warning, "[Timer] w");
            LogAssert.Expect(LogType.Error, "[Timer] e");
            LogAssert.Expect(LogType.Error, "[Timer] f");

            LogManager.Verbose(LogCategories.Timer, "v");
            LogManager.Debug(LogCategories.Timer, "d");
            LogManager.Info(LogCategories.Timer, "i");
            LogManager.Warning(LogCategories.Timer, "w");
            LogManager.Error(LogCategories.Timer, "e");
            LogManager.Fatal(LogCategories.Timer, "f");
            LogManager.Flush();

            string[] lines = Lines(Files()[0]);

            Assert.AreEqual("verbose", Parse(lines[1]).lvl);
            Assert.AreEqual("debug", Parse(lines[2]).lvl);
            Assert.AreEqual("info", Parse(lines[3]).lvl);
            Assert.AreEqual("warning", Parse(lines[4]).lvl);
            Assert.AreEqual("error", Parse(lines[5]).lvl);
            Assert.AreEqual("fatal", Parse(lines[6]).lvl);
        }

        [Test]
        public void MessageWithLineBreaks_StaysOnOnePhysicalLine()
        {
            LogManager.Info(LogCategories.Save, "第一行\n第二行");

            LogManager.Flush();

            string[] lines = Lines(Files()[0]);
            Assert.AreEqual(2, lines.Length, "消息里的换行必须转义，不能把一行日志拆成两行");
            Assert.AreEqual("第一行\n第二行", Parse(lines[1]).msg);
        }

        [Test]
        public void ExceptionEntry_CarriesExcField()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Save\] 应用存档失败"));

            LogManager.Exception(LogCategories.Save, new InvalidOperationException("存档损坏"), "应用存档失败");
            LogManager.Flush();

            LogLineDto entry = Parse(Lines(Files()[0])[1]);

            Assert.AreEqual("error", entry.lvl);
            Assert.IsNotEmpty(entry.exc, "异常条目要把异常文本带进文件（AI 侧分析崩溃现场的关键）");
        }

        #endregion

        #region Flush 策略

        [Test]
        public void WarningAndAbove_AreFlushedImmediately()
        {
            LogAssert.Expect(LogType.Error, "[Save] 立即落盘");

            LogManager.Error(LogCategories.Save, "立即落盘");

            // 不调 Flush，直接读盘
            Assert.AreEqual(2, Lines(Files()[0]).Length);
        }

        [Test]
        public void BelowImmediateFlush_StaysBufferedUntilFlush()
        {
            LogManager.Info(LogCategories.Save, "缓冲中");

            Assert.AreEqual(1, Lines(Files()[0]).Length, "未到门槛的条目允许留在缓冲里");

            LogManager.Flush();

            Assert.AreEqual(2, Lines(Files()[0]).Length);
        }

        [Test]
        public void Shutdown_FlushesBeforeClosing()
        {
            LogManager.Info(LogCategories.Save, "退出前");

            LogManager.Shutdown();

            Assert.AreEqual(2, Lines(Files()[0]).Length);
        }

        #endregion

        #region 切分与轮转

        [Test]
        public void MaxFileBytes_SplitsIntoParts_EachWithHeader()
        {
            ConfigureFileSink(maxFileBytes: 512);

            for (int i = 0; i < 20; i++)
                LogManager.Info(LogCategories.Save, "填充内容填充内容填充内容 {0}", i);
            LogManager.Flush();

            string[] files = Files();
            Assert.Greater(files.Length, 1, "超过上限应切分");

            foreach (string file in files)
                Assert.AreEqual("session", Parse(Lines(file)[0]).t, "每个分片都要自描述");
        }

        [Test]
        public void MaxRetainedFiles_DeletesOldestOnOpen()
        {
            Directory.CreateDirectory(_directory);
            for (int i = 0; i < 3; i++)
                File.WriteAllText(Path.Combine(_directory, $"xlog-2020010{i}-000000-deadbeef-p1.jsonl"), "{}\n");

            ConfigureFileSink(maxRetainedFiles: 2);

            LogManager.Info(LogCategories.Save, "触发打开");
            LogManager.Flush();

            string[] files = Files();
            Assert.AreEqual(2, files.Length, "保留上限 = 2：一条最新的旧文件 + 本次新建");
            Assert.IsTrue(files[0].Contains("20200102"), $"最旧的先删，剩下的是最新的旧文件；实际：{files[0]}");
        }

        #endregion

        #region 失败即静默

        /// <summary>
        /// 目录被一个同名文件挡住时：文件输出端静默停用，**不产生任何日志**，控制台通路照常。
        /// <para>「因为日志坏了而记一条日志」会递归；而控制台那一路是 Unity 自己 Player.log 的职责，
        /// 它没坏就不该受影响。</para>
        /// </summary>
        [Test]
        public void UnwritableDirectory_GoesSilent_AndConsoleKeepsWorking()
        {
            string blocker = Path.Combine(_directory, "blocked");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(blocker, "x");

            ConfigureFileSink(directory: Path.Combine(blocker, "sub"));

            LogAssert.Expect(LogType.Warning, "[Save] 仍然可见");
            LogManager.Warning(LogCategories.Save, "仍然可见");
            LogManager.Flush();

            LogAssert.NoUnexpectedReceived();
        }

        #endregion
    }
}
