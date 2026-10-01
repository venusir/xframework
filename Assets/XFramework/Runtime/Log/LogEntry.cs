using System;

namespace XFramework.XLog
{
    /// <summary>
    /// 一次日志的完整快照，供 <see cref="ILogSink"/> 消费。
    /// <para><b>只读且自包含</b>：sink 可以把它投进队列延后处理，不必担心被复用或失效。</para>
    /// </summary>
    public readonly struct LogEntry
    {
        /// <summary>构造一条日志条目（第三方 sink 的单测可用）。</summary>
        /// <param name="level">级别。</param>
        /// <param name="category">分类；<c>default</c> 合法（呈现为 <c>[Unregistered]</c>）。</param>
        /// <param name="message">已格式化的正文，<b>不含</b> <c>[Category] </c> 前缀。</param>
        /// <param name="utcTime">UTC 时间。</param>
        /// <param name="frame">主线程帧号；非主线程取最近已知值。</param>
        /// <param name="threadId">托管线程 id。</param>
        /// <param name="source">来源。</param>
        /// <param name="sequence">会话内单调递增序号（从 1 起）。</param>
        /// <param name="exception">关联异常，可为 null。</param>
        /// <param name="stackTrace">调用点托管堆栈；未开启或级别不够时为 null。</param>
        public LogEntry(LogLevel level, LogCategory category, string message, DateTime utcTime,
            int frame, int threadId, LogSource source, long sequence,
            Exception exception = null, string stackTrace = null)
        {
            Level = level;
            Category = category;
            Message = message;
            UtcTime = utcTime;
            Frame = frame;
            ThreadId = threadId;
            Source = source;
            Sequence = sequence;
            Exception = exception;
            StackTrace = stackTrace;
        }

        /// <summary>级别。</summary>
        public LogLevel Level { get; }

        /// <summary>分类。</summary>
        public LogCategory Category { get; }

        /// <summary>已格式化的正文，<b>不含</b> <c>[Category] </c> 前缀；框架条目由渲染层补前缀。</summary>
        public string Message { get; }

        /// <summary>UTC 时间。</summary>
        public DateTime UtcTime { get; }

        /// <summary>主线程帧号；非主线程取最近已知值。</summary>
        public int Frame { get; }

        /// <summary>托管线程 id。</summary>
        public int ThreadId { get; }

        /// <summary>来源：框架自产 or 从 Unity 日志系统捕获。</summary>
        public LogSource Source { get; }

        /// <summary>会话内单调递增序号（从 1 起）。并发写入后仍能定序。</summary>
        public long Sequence { get; }

        /// <summary>关联异常；无则为 null。</summary>
        public Exception Exception { get; }

        /// <summary>调用点托管堆栈；未开启抓栈或级别不够时为 null。</summary>
        public string StackTrace { get; }
    }
}
