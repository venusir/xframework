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
        /// <summary>写入一条日志。任意线程调用（与 <c>Debug.Log</c> 同约束）。</summary>
        /// <param name="entry">日志条目。</param>
        public void Write(in LogEntry entry)
        {
            string body = entry.Exception != null
                ? entry.Message + "\n" + entry.Exception
                : entry.Message;

            string line = "[" + entry.Category.Name + "] " + body;

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

        /// <summary>无缓冲，空实现。</summary>
        public void Flush()
        {
        }
    }
}
