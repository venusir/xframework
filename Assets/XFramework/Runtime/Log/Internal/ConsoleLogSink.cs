using System;
using UnityEngine;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// 默认输出端：把条目渲染成 <c>[Category] message</c> 后交给 Unity 控制台。
    /// <para><b>它是「迁移逐字保真」的支点</b>：渲染格式与迁移前手抄的 <c>$"[Save] …"</c> 完全一致，
    /// 且 LogType 映射与迁移前的 <c>Debug.Log/LogWarning/LogError</c> 一一对应，所以 30+ 处
    /// <c>LogAssert</c> 正则与精确匹配都不必改。</para>
    /// </summary>
    internal sealed class ConsoleLogSink : ILogSink
    {
        #region Private Fields

        /// <summary>
        /// 回显深度（线程本地）：本输出端调 <c>Debug.Log*</c> 期间为 1。
        /// <para>Unity 的日志回调会把我们自己的输出再看见一次，<see cref="UnityLogCapture"/> 据此丢弃。
        /// 线程本地是必需的——日志可能来自任意线程，而回调在调用线程上触发。</para>
        /// </summary>
        [ThreadStatic]
        private static int _echoDepth;

        #endregion

        #region Internal API

        /// <summary>当前线程是否正处在「本输出端调用 <c>Debug.Log*</c>」的窗口内。</summary>
        internal static bool IsEchoing => _echoDepth > 0;

        #endregion

        #region ILogSink

        /// <summary>写入一条日志。任意线程调用（与 <c>Debug.Log</c> 同约束）。</summary>
        /// <param name="entry">日志条目。</param>
        public void Write(in LogEntry entry)
        {
            string body = entry.Exception != null
                ? entry.Message + "\n" + entry.Exception
                : entry.Message;

            string line = "[" + entry.Category.Name + "] " + body;

            _echoDepth++;
            try
            {
                switch (entry.Level)
                {
                    case LogLevel.Warning:
                        Debug.LogWarning(line);
                        break;

                    case LogLevel.Error:
                    case LogLevel.Fatal:
                        Debug.LogError(line);
                        break;

                    default:
                        Debug.Log(line);
                        break;
                }
            }
            finally
            {
                _echoDepth--;
            }
        }

        /// <summary>无缓冲，空实现。</summary>
        public void Flush()
        {
        }

        #endregion
    }
}
