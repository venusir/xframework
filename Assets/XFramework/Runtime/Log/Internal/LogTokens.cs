namespace XFramework.XLog.Internal
{
    /// <summary>写进 JSONL 的稳定 token。<b>全小写自描述</b>——AI 或脚本无表可读时也能直接理解。</summary>
    internal static class LogTokens
    {
        /// <summary>级别 token：<c>verbose</c> / <c>debug</c> / <c>info</c> / <c>warning</c> / <c>error</c> / <c>fatal</c> / <c>off</c>。</summary>
        /// <param name="level">级别。</param>
        /// <returns>token 字符串（常量，可直接写进缓冲）。</returns>
        internal static string Level(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose: return "verbose";
                case LogLevel.Debug: return "debug";
                case LogLevel.Info: return "info";
                case LogLevel.Warning: return "warning";
                case LogLevel.Fatal: return "fatal";
                case LogLevel.Off: return "off";
                default: return "error";
            }
        }

        /// <summary>来源 token：<c>fw</c>（框架自产）或 <c>unity</c>（从 Unity 日志系统捕获）。</summary>
        /// <param name="source">来源。</param>
        /// <returns>token 字符串。</returns>
        internal static string Source(LogSource source)
        {
            return source == LogSource.Framework ? "fw" : "unity";
        }
    }
}
