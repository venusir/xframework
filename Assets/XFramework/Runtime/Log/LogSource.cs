namespace XFramework.XLog
{
    /// <summary>日志来源：框架自产，还是从 Unity 日志系统捕获。</summary>
    public enum LogSource
    {
        /// <summary>经 <c>LogManager</c> 发出的日志。</summary>
        Framework = 0,

        /// <summary>从 <c>Application.logMessageReceivedThreaded</c> 捕获的外部日志（引擎 / 第三方库 / 未捕获异常）。</summary>
        Unity = 1,
    }
}
