using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// <see cref="LubanProcessOutputBuffer"/> 的上限与截断用例，以及
    /// <see cref="LubanProcessRunner.Run"/> 的成功/非零/超时/大输出/中文（R4-LBN-003/015）。
    /// <para>用当前平台的 shell 构造可控子进程；非 Windows 用 <c>/bin/sh</c>。</para>
    /// </summary>
    [TestFixture]
    public sealed class LubanProcessRunnerTests
    {
        private static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        private static LubanCommand ShellCommand(string script)
        {
            return IsWindows
                ? new LubanCommand("cmd.exe", "/c " + script, null)
                : new LubanCommand("/bin/sh", "-c \"" + script.Replace("\"", "\\\"") + "\"", null);
        }

        [Test]
        public void Run_Success_CapturesOutputAndExitCode()
        {
            var result = LubanProcessRunner.Run(ShellCommand("echo hello"), 10_000);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.StandardOutput, Does.Contain("hello"));
            Assert.That(result.TimedOut, Is.False);
        }

        [Test]
        public void Run_NonZeroExit_ReportsCode()
        {
            var result = LubanProcessRunner.Run(ShellCommand("exit 3"), 10_000);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(3));
        }

        [Test]
        public void Run_Timeout_KillsProcessAndReportsTimeout()
        {
            var result = LubanProcessRunner.Run(
                ShellCommand(IsWindows ? "ping -n 6 127.0.0.1 > nul" : "sleep 6"), 200);

            Assert.That(result.TimedOut, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(-1));
        }

        [Test]
        public void Run_LargeOutput_DoesNotDeadlock()
        {
            string payload = new string('x', 4000);
            var result = LubanProcessRunner.Run(ShellCommand("echo " + payload), 10_000);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.StandardOutput, Does.Contain(payload));
        }

        [Test]
        public void Run_ChineseOutput_DecodedAsUtf8()
        {
            if (!IsWindows)
                Assert.Ignore("中文编码探针只在 Windows 编辑器的 PowerShell 下验证");

            // cmd 的 echo 受控制台码页影响；用 PowerShell 显式把 stdout 设成 UTF-8。
            var command = new LubanCommand(
                "powershell.exe",
                "-NoProfile -Command \"[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Write-Output '中文输出'\"",
                null);

            var result = LubanProcessRunner.Run(command, 30_000);

            Assert.That(result.Succeeded, Is.True, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("中文输出"),
                "显式 UTF-8 编码下中文输出不得乱码");
        }

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
