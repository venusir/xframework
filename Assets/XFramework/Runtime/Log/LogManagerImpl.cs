using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using XFramework.XLog.Internal;

namespace XFramework.XLog
{
    /// <summary>
    /// 日志管理器默认实现。
    /// <para><b>永不抛</b>：日志调用路径上的任何失败都被吞掉（sink 抛异常→静默摘除并计数；抓栈失败→null）。
    /// 「因为日志坏了而记一条日志」会递归，且在测试里会被当成意外 Error 判失败。</para>
    /// <para><b>线程模型</b>：任意线程可调用。sink 数组 copy-on-write，写路径加锁、读路径无锁；
    /// 序号用 <see cref="Interlocked"/> 分配，因此并发写入后仍能靠 <c>Sequence</c> 定序。</para>
    /// </summary>
    internal sealed class LogManagerImpl : ILogManager
    {
        #region Private Fields

        private readonly object _sinkLock = new object();
        private readonly bool _captureStackTrace;
        private readonly bool _captureUnityLogs;
        private readonly LogLevel _stackTraceMinLevel;

        private ILogSink[] _sinks;
        private int _minimumLevel;
        private int _droppedSinkCount;
        private long _sequence;

        /// <summary>主线程 id；由 <c>AutoInit</c> 在主线程登记。非主线程不读 <c>Time.frameCount</c>（Unity 的主线程约束）。</summary>
        private static int _mainThreadId = -1;

        /// <summary>最近一次在主线程读到的帧号；非主线程的条目取它。</summary>
        private static int _cachedFrame;

        #endregion

        #region Constructors

        /// <summary>按配置构建实现。</summary>
        /// <param name="options">配置；不可为 null（门面做参数防御）。</param>
        internal LogManagerImpl(LogOptions options)
        {
            _minimumLevel = (int)options.MinimumLevel;
            _captureStackTrace = options.CaptureStackTrace;
            _captureUnityLogs = options.CaptureUnityLogs;
            _stackTraceMinLevel = options.StackTraceMinLevel;

            var sinks = new List<ILogSink>(2);
            if (options.EnableConsoleSink)
                sinks.Add(new ConsoleLogSink());
            if (options.EnableFileSink)
                sinks.Add(new LogFileSink(options));

            _sinks = sinks.Count == 0 ? Array.Empty<ILogSink>() : sinks.ToArray();
        }

        #endregion

        #region Internal API

        /// <summary>登记主线程 id（在主线程调用）。帧号缓存与「非主线程不读 Time」依赖它。</summary>
        internal static void MarkMainThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 捕获一条来自 Unity 日志系统的外部日志（引擎 / 第三方 / 未捕获异常）。
        /// <para>档位过滤与框架日志同一套（否则 <c>MinimumLevel</c> 的「一律丢弃」承诺就自相矛盾）；
        /// 但<b>只分发给非控制台输出端</b>——把捕获到的日志再交给控制台就是回显，会递归。</para>
        /// <para>分类从 <c>[标签]</c> 前缀解析：能解析出就注册成同名分类（幂等），否则用
        /// <c>default</c>（文件里呈现为 <c>Unregistered</c>）。</para>
        /// </summary>
        /// <param name="condition">Unity 日志正文（原样保留，不裁剪）。</param>
        /// <param name="stackTrace">Unity 附带的堆栈（错误及以上通常非空）。</param>
        /// <param name="type">Unity 的日志类型。</param>
        internal void CaptureUnityLog(string condition, string stackTrace, LogType type)
        {
            if (!_captureUnityLogs)
                return;

            LogLevel level = MapUnityLevel(type);
            LogCategory category = ParseCategory(condition);
            if (!IsEnabled(level, category))
                return;

            var entry = new LogEntry(
                level,
                category,
                condition ?? string.Empty,
                DateTime.UtcNow,
                SafeFrame(),
                Thread.CurrentThread.ManagedThreadId,
                LogSource.Unity,
                Interlocked.Increment(ref _sequence),
                null,
                string.IsNullOrEmpty(stackTrace) ? null : stackTrace);

            DispatchCaptured(in entry);
        }

        #endregion

        #region ILogManager — 状态

        /// <inheritdoc/>
        public bool IsInitialized => true;

        /// <inheritdoc/>
        public LogLevel MinimumLevel
        {
            get => (LogLevel)Volatile.Read(ref _minimumLevel);
            set => Volatile.Write(ref _minimumLevel, (int)value);
        }

        /// <inheritdoc/>
        public void SetCategoryLevel(LogCategory category, LogLevel level)
        {
            if (!category.IsValid)
            {
                throw new ArgumentException(
                    "分类未注册。SetCategoryLevel 需要真实分类——框架内置的见 LogCategories，自定义的用 LogCategory.Get(\"名字\")。",
                    nameof(category));
            }

            category.State.SetLevelOverride(level);
        }

        /// <inheritdoc/>
        public void ResetCategoryLevel(LogCategory category)
        {
            if (!category.IsValid)
            {
                throw new ArgumentException(
                    "分类未注册。ResetCategoryLevel 需要真实分类——框架内置的见 LogCategories。",
                    nameof(category));
            }

            category.State.ResetLevelOverride();
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel level, LogCategory category)
        {
            int value = (int)level;
            if (value < (int)LogLevel.Verbose || value > (int)LogLevel.Fatal)
                return false; // Off 或越界值：不是可写入的级别

            int threshold = category.LevelOverride;
            if (threshold < 0)
                threshold = Volatile.Read(ref _minimumLevel);

            return value >= threshold;
        }

        #endregion

        #region ILogManager — 通用入口

        /// <inheritdoc/>
        public void Log(LogLevel level, LogCategory category, string message)
        {
            if (!IsEnabled(level, category))
                return;

            Emit(level, category, LogFormatter.Format(message), null);
        }

        /// <inheritdoc/>
        public void Log<T1>(LogLevel level, LogCategory category, string template, T1 arg1)
        {
            if (!IsEnabled(level, category))
                return;

            Emit(level, category, LogFormatter.Format(template, arg1), null);
        }

        /// <inheritdoc/>
        public void Log<T1, T2>(LogLevel level, LogCategory category, string template, T1 arg1, T2 arg2)
        {
            if (!IsEnabled(level, category))
                return;

            Emit(level, category, LogFormatter.Format(template, arg1, arg2), null);
        }

        /// <inheritdoc/>
        public void Log<T1, T2, T3>(LogLevel level, LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3)
        {
            if (!IsEnabled(level, category))
                return;

            Emit(level, category, LogFormatter.Format(template, arg1, arg2, arg3), null);
        }

        /// <inheritdoc/>
        public void Exception(LogCategory category, Exception exception, string message = null)
        {
            Exception(LogLevel.Error, category, exception, message);
        }

        /// <inheritdoc/>
        public void Exception(LogLevel level, LogCategory category, Exception exception, string message = null)
        {
            if (exception == null || !IsEnabled(level, category))
                return;

            string body = string.IsNullOrEmpty(message) ? exception.Message : message;
            Emit(level, category, body ?? string.Empty, exception);
        }

        #endregion

        #region ILogManager — 分级入口

        /// <inheritdoc/>
        public void Verbose(LogCategory category, string message) => Log(LogLevel.Verbose, category, message);

        /// <inheritdoc/>
        public void Verbose<T1>(LogCategory category, string template, T1 arg1) => Log(LogLevel.Verbose, category, template, arg1);

        /// <inheritdoc/>
        public void Verbose<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Log(LogLevel.Verbose, category, template, arg1, arg2);

        /// <inheritdoc/>
        public void Verbose<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Log(LogLevel.Verbose, category, template, arg1, arg2, arg3);

        /// <inheritdoc/>
        public void Debug(LogCategory category, string message) => Log(LogLevel.Debug, category, message);

        /// <inheritdoc/>
        public void Debug<T1>(LogCategory category, string template, T1 arg1) => Log(LogLevel.Debug, category, template, arg1);

        /// <inheritdoc/>
        public void Debug<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Log(LogLevel.Debug, category, template, arg1, arg2);

        /// <inheritdoc/>
        public void Debug<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Log(LogLevel.Debug, category, template, arg1, arg2, arg3);

        /// <inheritdoc/>
        public void Info(LogCategory category, string message) => Log(LogLevel.Info, category, message);

        /// <inheritdoc/>
        public void Info<T1>(LogCategory category, string template, T1 arg1) => Log(LogLevel.Info, category, template, arg1);

        /// <inheritdoc/>
        public void Info<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Log(LogLevel.Info, category, template, arg1, arg2);

        /// <inheritdoc/>
        public void Info<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Log(LogLevel.Info, category, template, arg1, arg2, arg3);

        /// <inheritdoc/>
        public void Warning(LogCategory category, string message) => Log(LogLevel.Warning, category, message);

        /// <inheritdoc/>
        public void Warning<T1>(LogCategory category, string template, T1 arg1) => Log(LogLevel.Warning, category, template, arg1);

        /// <inheritdoc/>
        public void Warning<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Log(LogLevel.Warning, category, template, arg1, arg2);

        /// <inheritdoc/>
        public void Warning<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Log(LogLevel.Warning, category, template, arg1, arg2, arg3);

        /// <inheritdoc/>
        public void Error(LogCategory category, string message) => Log(LogLevel.Error, category, message);

        /// <inheritdoc/>
        public void Error<T1>(LogCategory category, string template, T1 arg1) => Log(LogLevel.Error, category, template, arg1);

        /// <inheritdoc/>
        public void Error<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Log(LogLevel.Error, category, template, arg1, arg2);

        /// <inheritdoc/>
        public void Error<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Log(LogLevel.Error, category, template, arg1, arg2, arg3);

        /// <inheritdoc/>
        public void Fatal(LogCategory category, string message) => Log(LogLevel.Fatal, category, message);

        /// <inheritdoc/>
        public void Fatal<T1>(LogCategory category, string template, T1 arg1) => Log(LogLevel.Fatal, category, template, arg1);

        /// <inheritdoc/>
        public void Fatal<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Log(LogLevel.Fatal, category, template, arg1, arg2);

        /// <inheritdoc/>
        public void Fatal<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Log(LogLevel.Fatal, category, template, arg1, arg2, arg3);

        #endregion

        #region ILogManager — 输出端与生命周期

        /// <inheritdoc/>
        public int DroppedSinkCount => Volatile.Read(ref _droppedSinkCount);

        /// <inheritdoc/>
        public void AddSink(ILogSink sink)
        {
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));

            lock (_sinkLock)
            {
                ILogSink[] current = _sinks;
                var next = new ILogSink[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = sink;
                _sinks = next;
            }
        }

        /// <inheritdoc/>
        public bool RemoveSink(ILogSink sink)
        {
            if (sink == null)
                return false;

            if (!TryRemoveSink(sink))
                return false;

            DisposeSink(sink);
            return true;
        }

        /// <inheritdoc/>
        public void Flush()
        {
            ILogSink[] sinks = _sinks;
            for (int i = 0; i < sinks.Length; i++)
            {
                try
                {
                    sinks[i].Flush();
                }
                catch (Exception)
                {
                    DropSink(sinks[i]);
                }
            }
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            ILogSink[] sinks;
            lock (_sinkLock)
            {
                sinks = _sinks;
                _sinks = Array.Empty<ILogSink>();
            }

            for (int i = 0; i < sinks.Length; i++)
            {
                try
                {
                    sinks[i].Flush();
                }
                catch (Exception)
                {
                    // 关闭路径上的 flush 失败无处可去，也不该再记日志（防递归）
                }

                DisposeSink(sinks[i]);
            }

            LogRegistry.ResetAllLevels();
        }

        #endregion

        #region Private — 分发

        /// <summary>构造条目并分发。调用方已过档位过滤。</summary>
        private void Emit(LogLevel level, LogCategory category, string message, Exception exception)
        {
            var entry = new LogEntry(
                level,
                category,
                message ?? string.Empty,
                DateTime.UtcNow,
                SafeFrame(),
                Thread.CurrentThread.ManagedThreadId,
                LogSource.Framework,
                Interlocked.Increment(ref _sequence),
                exception,
                CaptureStack(level));

            Dispatch(in entry);
        }

        private void Dispatch(in LogEntry entry)
        {
            ILogSink[] sinks = _sinks; // 快照：分发期间增删 sink 不影响本轮

            for (int i = 0; i < sinks.Length; i++)
            {
                try
                {
                    sinks[i].Write(in entry);
                }
                catch (Exception)
                {
                    DropSink(sinks[i]);
                }
            }
        }

        /// <summary>分发捕获到的外部日志：跳过控制台输出端（回显会递归），其余与框架日志同一套摘除策略。</summary>
        private void DispatchCaptured(in LogEntry entry)
        {
            ILogSink[] sinks = _sinks;

            for (int i = 0; i < sinks.Length; i++)
            {
                if (sinks[i] is ConsoleLogSink)
                    continue;

                try
                {
                    sinks[i].Write(in entry);
                }
                catch (Exception)
                {
                    DropSink(sinks[i]);
                }
            }
        }

        /// <summary>Unity 日志类型 → 本模块档位。<c>Assert</c> 归 <c>Error</c>：断言失败在发布版里必须可见。</summary>
        private static LogLevel MapUnityLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return LogLevel.Warning;

                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return LogLevel.Error;

                default:
                    return LogLevel.Info;
            }
        }

        /// <summary>从 <c>[标签] 正文</c> 解析分类；解析不出返回 <c>default</c>（呈现为 <c>Unregistered</c>）。</summary>
        private static LogCategory ParseCategory(string condition)
        {
            if (string.IsNullOrEmpty(condition) || condition[0] != '[')
                return default;

            int end = condition.IndexOf(']');
            if (end <= 1)
                return default;

            return LogCategory.Get(condition.Substring(1, end - 1));
        }

        /// <summary>摘除抛异常的 sink：不记日志（会递归）、不向上抛（日志永不抛），只计数。</summary>
        private void DropSink(ILogSink sink)
        {
            if (!TryRemoveSink(sink))
                return;

            Interlocked.Increment(ref _droppedSinkCount);
            DisposeSink(sink);
        }

        private bool TryRemoveSink(ILogSink sink)
        {
            lock (_sinkLock)
            {
                ILogSink[] current = _sinks;
                int index = Array.IndexOf(current, sink);
                if (index < 0)
                    return false;

                var next = new ILogSink[current.Length - 1];
                Array.Copy(current, 0, next, 0, index);
                Array.Copy(current, index + 1, next, index, current.Length - index - 1);
                _sinks = next;
                return true;
            }
        }

        private static void DisposeSink(ILogSink sink)
        {
            if (sink is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                    // 可选能力接口：释放失败不致命，也不记日志（防递归）
                }
            }
        }

        #endregion

        #region Private — 环境信息

        /// <summary>抓调用点堆栈；未开启或级别不够时返回 null。绝不抛。</summary>
        private string CaptureStack(LogLevel level)
        {
            if (!_captureStackTrace || level < _stackTraceMinLevel)
                return null;

            try
            {
                return new StackTrace(fNeedFileInfo: false).ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>主线程取当前帧号并缓存；非主线程取缓存值（不触碰 Unity API）。</summary>
        private static int SafeFrame()
        {
            if (_mainThreadId < 0 || Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                return Volatile.Read(ref _cachedFrame);

            int frame = Time.frameCount;
            Volatile.Write(ref _cachedFrame, frame);
            return frame;
        }

        #endregion
    }
}
