using System;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// 全量捕获：把 Unity 日志系统里的**外部**日志（引擎、第三方库、未捕获异常）收进同一份时间线。
    /// <para><b>为什么挂 <c>logMessageReceivedThreaded</c> 而不是 <c>logMessageReceived</c></b>：后者只报主线程
    /// 的日志，而崩溃现场往往来自工作线程；「全量」的意义就在于不漏。</para>
    /// <para><b>为什么在 <c>AutoInit</c> 无条件挂载</b>（而不是等配置了文件 sink 再挂）：回调里第一行就按
    /// 「有没有可写的 sink」早退，挂载成本只是每条 Unity 日志一次静态委托调用；换来的是**没有时序陷阱**——
    /// 「在第一次 <c>LogManager</c> 调用之前发生的日志捕获不到」这种坑不存在。</para>
    /// <para><b>去重</b>：框架自己的日志经控制台输出端调 <c>Debug.Log*</c>，会被这个回调再看见一次。
    /// <see cref="ConsoleLogSink"/> 在自己的 <c>Debug</c> 调用前后增减一个线程本地的回显深度，
    /// 回调据此丢弃——它精确且零成本（前提是回调在调用线程上同步触发，有对应用例判定）。</para>
    /// </summary>
    internal static class UnityLogCapture
    {
        #region Internal API

        /// <summary>挂接捕获回调（幂等：先 <c>-=</c> 再 <c>+=</c>，关闭域重载时订阅跨播放会话存活）。</summary>
        internal static void Attach()
        {
            Application.logMessageReceivedThreaded -= OnUnityLog;
            Application.logMessageReceivedThreaded += OnUnityLog;
        }

        #endregion

        #region Private

        /// <summary>
        /// 捕获回调。<b>永不抛</b>：它挂在 Unity 的日志回调链上，抛出去会打断别人的日志调用——
        /// 那比丢一条日志严重得多。
        /// </summary>
        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                if (ConsoleLogSink.IsEchoing)
                    return; // 这一条是我们自己刚调 Debug.Log* 产生的回显

                LogManagerImpl impl = LogManager.CaptureTarget;
                impl?.CaptureUnityLog(condition, stackTrace, type);
            }
            catch (Exception)
            {
                // 捕获失败等于丢一条日志，仅此而已
            }
        }

        #endregion
    }
}
