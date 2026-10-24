using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 一次 Luban 子进程的运行结果。
    /// </summary>
    internal readonly struct LubanProcessResult
    {
        internal int ExitCode { get; }

        internal string StandardOutput { get; }

        internal string StandardError { get; }

        internal bool TimedOut { get; }

        internal bool Succeeded => !TimedOut && ExitCode == 0;

        internal LubanProcessResult(
            int exitCode,
            string standardOutput,
            string standardError,
            bool timedOut)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            TimedOut = timedOut;
        }
    }

    /// <summary>
    /// 同步执行 Luban 子进程并捕获输出。菜单在 Editor 主线程调用，运行时阻塞但带进度条。
    /// </summary>
    internal static class LubanProcessRunner
    {
        internal const int DefaultTimeoutMilliseconds = 5 * 60 * 1000;

        internal static LubanProcessResult Run(
            LubanCommand command,
            int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                WorkingDirectory = command.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var output = new StringBuilder();
            var error = new StringBuilder();
            using var outputDone = new ManualResetEventSlim(false);
            using var errorDone = new ManualResetEventSlim(false);
            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data == null)
                {
                    outputDone.Set();
                }
                else
                {
                    lock (output)
                    {
                        output.AppendLine(args.Data);
                    }
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null)
                {
                    errorDone.Set();
                }
                else
                {
                    lock (error)
                    {
                        error.AppendLine(args.Data);
                    }
                }
            };

            try
            {
                if (!process.Start())
                {
                    return new LubanProcessResult(
                        -1, string.Empty, "Failed to start process.", false);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(timeoutMilliseconds);
                if (!exited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // 进程可能已自行退出；忽略。
                    }

                    process.WaitForExit(2000);
                }

                outputDone.Wait(2000);
                errorDone.Wait(2000);

                string standardOutput;
                string standardError;
                lock (output)
                {
                    standardOutput = output.ToString();
                }

                lock (error)
                {
                    standardError = error.ToString();
                }

                return new LubanProcessResult(
                    exited ? process.ExitCode : -1,
                    standardOutput,
                    standardError,
                    !exited);
            }
            catch (Exception ex)
            {
                string standardOutput;
                string standardError;
                lock (output)
                {
                    standardOutput = output.ToString();
                }

                lock (error)
                {
                    standardError = error.ToString();
                }

                return new LubanProcessResult(
                    -1,
                    standardOutput,
                    string.IsNullOrEmpty(standardError) ? ex.Message : standardError + Environment.NewLine + ex.Message,
                    false);
            }
        }
    }
}
