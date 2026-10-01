namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 日志测试的共用夹具。
    /// <para><b>复位为什么用 <c>Shutdown</c> 而不是专门的测试 API</b>：<c>Shutdown</c> 本身就是公开、
    /// 语义正确的复位入口（冲刷 → 释放 sink → 复位档位 → 回到未装入状态，下次使用按默认配置重建）。
    /// 为测试再造一个 <c>ResetForTests</c> 只会多一条与生产路径不同的复位路径。</para>
    /// <para><b>配置固定为「全局 Verbose + 控制台开 + 不抓栈」</b>：让用例自己用档位控制输出，
    /// 不依赖 Editor/Development 的默认档位；不抓栈则让 Error 用例不背上 <c>StackTrace</c> 的分配与耗时。</para>
    /// </summary>
    internal static class LogTestKit
    {
        /// <summary>复位门面并装入确定性配置。每个用例的 SetUp 调用。</summary>
        internal static void SetUp()
        {
            LogManager.Shutdown();
            LogManager.Configure(new LogOptions
            {
                MinimumLevel = LogLevel.Verbose,
                EnableConsoleSink = true,
                CaptureStackTrace = false,
                StackTraceMinLevel = LogLevel.Error,
            });
        }

        /// <summary>复位门面。每个用例的 TearDown 调用——PlayMode 下所有用例共享一个 player 实例。</summary>
        internal static void TearDown()
        {
            LogManager.Shutdown();
        }
    }
}
