using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog.Internal;

namespace XFramework.XLog
{
    /// <summary>
    /// 日志管理器外观。分级、分类过滤、模板化调用与可插拔输出端。
    /// <para><b>它比 Unity 自带的 <c>Debug.unityLogger</c> + 自定义 <c>ILogHandler</c> 多给什么</b>
    /// （Roadmap §2.2 的立项门槛）：① <b>按分类过滤</b>——Unity 只有全局开关，没有「只关 Save 的 Debug」；
    /// ② <b>零 GC 调用路径</b>——<c>Debug.Log($"…")</c> 无论开不开都必然分配，这里未启用就不格式化；
    /// ③ <b>前缀不再靠人手抄</b>——<c>[模块]</c> 由分类渲染层补，约定从文档纪律变成代码保证；
    /// ④ <b>结构化落盘</b>——每条日志一行 JSONL，把引擎/第三方/未捕获异常也收进同一时间线，供 AI 分析。</para>
    /// <para><b>零配置、无需初始化</b>：本模块是纯静态服务，无配置时按 <see cref="LogOptions.Default"/> 构建默认
    /// 实现，靠 <see cref="AutoInit"/> 自初始化（同 <c>UpdateManager</c> / <c>TimerManager</c>）。
    /// <b>永不抛</b>是本门面相对其它门面的有意豁免——日志是错误路径的最后一张面孔，
    /// 让「模块没初始化」把它升级成二次故障，代价比一致性大得多。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 1. 基本用法：分类从 LogCategories 取，前缀由它渲染
    /// LogManager.Warning(LogCategories.Save, "跳过空存档文件: {0}", path);
    ///
    /// // 2. 自定义分类（第三方）
    /// private static readonly LogCategory MyGame = LogCategory.Get("MyGame");
    /// LogManager.Info(MyGame, "关卡 {0} 加载完成，用时 {1:F2}s", levelId, seconds);
    ///
    /// // 3. 只想关掉某模块的诊断日志
    /// LogManager.SetCategoryLevel(LogCategories.UpdateScheduler, LogLevel.Warning);
    ///
    /// // 4. 追加自己的输出端（上报 / 写 UI 面板）
    /// LogManager.AddSink(new MyRemoteSink());
    /// </code>
    /// </example>
    public static class LogManager
    {
        #region Private Fields

        /// <summary>门面锁：只保护 <see cref="_impl"/> 的替换，不跨 sink 操作持有。</summary>
        private static readonly object Gate = new object();

        private static ILogManager _impl;

        #endregion

        #region Private — Impl

        /// <summary>
        /// 全量捕获的目标：仅当当前实现是内置实现时返回它。
        /// <para><b>刻意不走 <see cref="Impl"/></b>：那会让一条第三方日志凭空创建出一个日志实现
        /// （连同它的文件输出端）。注入自定义实现时捕获自动停用——那是自定义实现自己的策略。</para>
        /// </summary>
        internal static LogManagerImpl CaptureTarget => _impl as LogManagerImpl;

        /// <summary>取当前实现；未装入时按默认配置构建（懒初始化，「日志永不因未初始化而失效」）。</summary>
        private static ILogManager Impl
        {
            get
            {
                ILogManager impl = _impl;
                if (impl != null)
                    return impl;

                lock (Gate)
                {
                    return _impl ??= new LogManagerImpl(LogOptions.Default);
                }
            }
        }

        #endregion

        #region Lifecycle

        /// <summary>当前是否已装入实现。门面按需创建，通常为 <c>true</c>。</summary>
        public static bool IsInitialized => _impl != null;

        /// <summary>
        /// 注入自定义实现（整体替换后端；测试也用它注入替身）。
        /// <para><b>注入非内置实现后，Unity 全量捕获随之停用</b>（<see cref="CaptureTarget"/> 只认
        /// <see cref="LogManagerImpl"/>）——引擎与第三方库的日志不会再进任何输出端。因此在替换<b>之前</b>
        /// 提醒一次：那一刻当前实现必然是框架实现（含懒建的默认实现），所以这条提醒不会走你注入的实现、
        /// 不可能因它的异常而失效、也不可能递归——这是本模块「自身永不记日志」的<b>唯一例外</b>，
        /// 理由是它既不是故障路径、也不经被替换者的手。需要保留全量捕获请改用
        /// <see cref="Configure"/>（它重建的正是内置实现）。</para>
        /// </summary>
        /// <param name="impl">实现；为 null 时抛 <see cref="ArgumentNullException"/>。</param>
        public static void Initialize(ILogManager impl)
        {
            if (impl == null)
                throw new ArgumentNullException(nameof(impl));

            if (!(impl is LogManagerImpl))
            {
                Warning(LogCategories.LogManager,
                    "注入的实现不是内置 LogManagerImpl（{0}）：Unity 全量捕获随之停用——" +
                    "引擎与第三方库的日志不会再进任何输出端；需要保留请改用 LogManager.Configure(LogOptions)。",
                    impl.GetType().Name);
            }

            ILogManager previous;
            lock (Gate)
            {
                previous = _impl;
                _impl = impl;
            }

            if (!ReferenceEquals(previous, impl))
                previous?.Shutdown();
        }

        /// <summary>
        /// 按配置重建内置实现（<b>已添加的自定义 sink 会随旧实现一并释放，需要重新添加</b>）。
        /// <para><b>重建也会复位分类档位</b>（旧实现走 <c>Shutdown</c> 的语义）——设置档位请在
        /// <c>Configure</c> <b>之后</b>做。</para>
        /// <para>刻意不叫 <c>Initialize(LogOptions)</c>：与注入重载同名会造成「传 null 是配默认还是清空」的二义。</para>
        /// </summary>
        /// <param name="options">配置；为 null 时抛 <see cref="ArgumentNullException"/>。</param>
        public static void Configure(LogOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var next = new LogManagerImpl(options);
            ILogManager previous;
            lock (Gate)
            {
                previous = _impl;
                _impl = next;
            }

            previous?.Shutdown();
        }

        /// <summary>
        /// 冲刷并释放当前实现，随后回到「未装入」状态——<b>日志不会因此失效</b>：下一次使用会按默认配置
        /// 重新构建。它同时是公开、语义正确的复位入口（测试的每个 fixture 用它复位）。
        /// </summary>
        public static void Shutdown()
        {
            ILogManager impl;
            lock (Gate)
            {
                impl = _impl;
                _impl = null;
            }

            impl?.Shutdown();
        }

        /// <summary>把所有 sink 的缓冲刷到目标（崩溃可读性的下限；文件 sink 在 Error/Fatal 时也会自行即刷）。</summary>
        public static void Flush() => Impl.Flush();

        #endregion

        #region Level

        /// <summary>全局最低档位：低于它的日志一律丢弃（分类未设覆盖时按它判定）。</summary>
        public static LogLevel MinimumLevel
        {
            get => Impl.MinimumLevel;
            set => Impl.MinimumLevel = value;
        }

        /// <summary>设置分类档位覆盖，覆盖该分类的全局档位判定。</summary>
        /// <param name="category">分类；未注册（<c>default</c>）时抛 <see cref="ArgumentException"/>。</param>
        /// <param name="level">该分类的最低档位；<see cref="LogLevel.Off"/> 表示静默。</param>
        public static void SetCategoryLevel(LogCategory category, LogLevel level) => Impl.SetCategoryLevel(category, level);

        /// <summary>还原分类档位为「跟随全局档」。</summary>
        /// <param name="category">分类；未注册时抛 <see cref="ArgumentException"/>。</param>
        public static void ResetCategoryLevel(LogCategory category) => Impl.ResetCategoryLevel(category);

        /// <summary>该级别 + 分类是否会被输出。用于「先探测再构造昂贵参数」。</summary>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <returns>是否启用。</returns>
        public static bool IsEnabled(LogLevel level, LogCategory category) => Impl.IsEnabled(level, category);

        #endregion

        #region Sinks

        /// <summary>因 <see cref="ILogSink.Write"/> 抛异常被静默摘除的 sink 次数。诊断用。</summary>
        public static int DroppedSinkCount => Impl.DroppedSinkCount;

        /// <summary>追加一个 sink。重复追加同一个实例会写两次（框架不做去重）。</summary>
        /// <param name="sink">输出端。</param>
        public static void AddSink(ILogSink sink) => Impl.AddSink(sink);

        /// <summary>摘除一个 sink（按引用，移除首个匹配）；实现 <see cref="IDisposable"/> 的会被释放。</summary>
        /// <param name="sink">输出端。</param>
        /// <returns>是否移除成功。</returns>
        public static bool RemoveSink(ILogSink sink) => Impl.RemoveSink(sink);

        #endregion

        #region Diagnostics — 回读

        // 这四个成员刻意不进 ILogManager：主接口是对第三方开放的替换点，加成员会让所有实现者编译不过
        // （仓内就有 ForeignLogManager 这样的替身）。回读只对内置实现成立——门面按 CaptureTarget 探测，
        // 注入第三方实现时一律返回空。这是本仓「可选能力接口 / 内部缝 + 门面探测」那条惯例的又一例。

        // 分类数没有在这里加别名：LogCategory.RegisteredCount 已经公开提供同一个数，
        // 两个名字指向同一个值就是第二份真相。

        /// <summary>
        /// 把所有分类的档位状态写入缓冲区（先清空），返回条数。
        /// <para>回答「这条分类现在会不会输出、为什么」：<see cref="LogCategoryInfo.EffectiveLevel"/> 是在
        /// 本次调用时现算的（设了覆盖取覆盖、否则取当前全局档）。</para>
        /// <para><b>诊断接口</b>：低频调用，允许分配；不要放进每帧路径。注入第三方实现时返回 0
        /// （缓冲区被清空）。</para>
        /// </summary>
        /// <param name="buffer">接收结果的缓冲区；会被先清空。</param>
        /// <returns>写入的分类数。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> 为 null。</exception>
        public static int CopyCategories(List<LogCategoryInfo> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            LogManagerImpl target = CaptureTarget;
            if (target == null)
            {
                buffer.Clear();
                return 0;
            }

            return target.CopyCategories(buffer);
        }

        /// <summary>当前输出端数量。<b>只对内置实现成立</b>（注入第三方实现时恒为 0）。</summary>
        public static int SinkCount => CaptureTarget != null ? CaptureTarget.SinkCount : 0;

        /// <summary>
        /// 把当前输出端写入缓冲区（先清空），返回条数。
        /// <para><see cref="ILogSink"/> 没有名字——行身份只能用类型名（给它加 Name 会破坏第三方输出端的实现，
        /// 不做）。顺序即注册顺序。</para>
        /// <para><b>诊断接口</b>：低频调用，允许分配；不要放进每帧路径。注入第三方实现时返回 0。</para>
        /// </summary>
        /// <param name="buffer">接收结果的缓冲区；会被先清空。</param>
        /// <returns>写入的输出端数。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> 为 null。</exception>
        public static int CopySinks(List<ILogSink> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            LogManagerImpl target = CaptureTarget;
            if (target == null)
            {
                buffer.Clear();
                return 0;
            }

            return target.CopySinks(buffer);
        }

        #endregion

        #region Log Entry — 通用

        /// <summary>写一条指定级别的日志。</summary>
        /// <param name="level">级别；<see cref="LogLevel.Off"/> 不会产生输出。</param>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文（不含 <c>[Category] </c> 前缀）。</param>
        public static void Log(LogLevel level, LogCategory category, string message) => Impl.Log(level, category, message);

        /// <summary>写一条指定级别的日志，正文由模板 + 1 个参数格式化。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <param name="template">模板，语义与 <c>string.Format</c> 一致（与插值同源）。</param>
        /// <param name="arg1">参数。</param>
        public static void Log<T1>(LogLevel level, LogCategory category, string template, T1 arg1) => Impl.Log(level, category, template, arg1);

        /// <summary>写一条指定级别的日志，正文由模板 + 2 个参数格式化。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        public static void Log<T1, T2>(LogLevel level, LogCategory category, string template, T1 arg1, T2 arg2) => Impl.Log(level, category, template, arg1, arg2);

        /// <summary>写一条指定级别的日志，正文由模板 + 3 个参数格式化（不分配 <c>object[]</c> 的上限）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        public static void Log<T1, T2, T3>(LogLevel level, LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Impl.Log(level, category, template, arg1, arg2, arg3);

        /// <summary>写一条异常日志（级别固定 <see cref="LogLevel.Error"/>，正文默认取 <c>Exception.Message</c>）。</summary>
        /// <param name="category">分类。</param>
        /// <param name="exception">异常；为 null 时什么都不做。</param>
        /// <param name="message">正文；为 null 时用 <c>Exception.Message</c>。</param>
        public static void Exception(LogCategory category, Exception exception, string message = null) => Impl.Exception(category, exception, message);

        /// <summary>
        /// 写一条带异常对象的日志（级别可指定）。异常进 JSONL 的 <c>exc</c> 字段；控制台在正文后追加异常详情。
        /// <para><b>正文不要重复异常文本</b>：异常类型与消息由渲染层补上。</para>
        /// </summary>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <param name="exception">异常；为 null 时什么都不做。</param>
        /// <param name="message">正文；为 null 时用 <c>Exception.Message</c>。</param>
        public static void Exception(LogLevel level, LogCategory category, Exception exception, string message = null) => Impl.Exception(level, category, exception, message);

        #endregion

        #region Log Entry — 分级

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        public static void Verbose(LogCategory category, string message) => Impl.Verbose(category, message);

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        public static void Verbose<T1>(LogCategory category, string template, T1 arg1) => Impl.Verbose(category, template, arg1);

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        public static void Verbose<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Impl.Verbose(category, template, arg1, arg2);

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        public static void Verbose<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Impl.Verbose(category, template, arg1, arg2, arg3);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        public static void Debug(LogCategory category, string message) => Impl.Debug(category, message);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        public static void Debug<T1>(LogCategory category, string template, T1 arg1) => Impl.Debug(category, template, arg1);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        public static void Debug<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Impl.Debug(category, template, arg1, arg2);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        public static void Debug<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Impl.Debug(category, template, arg1, arg2, arg3);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        public static void Info(LogCategory category, string message) => Impl.Info(category, message);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        public static void Info<T1>(LogCategory category, string template, T1 arg1) => Impl.Info(category, template, arg1);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        public static void Info<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Impl.Info(category, template, arg1, arg2);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        public static void Info<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Impl.Info(category, template, arg1, arg2, arg3);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        public static void Warning(LogCategory category, string message) => Impl.Warning(category, message);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        public static void Warning<T1>(LogCategory category, string template, T1 arg1) => Impl.Warning(category, template, arg1);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        public static void Warning<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Impl.Warning(category, template, arg1, arg2);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        public static void Warning<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Impl.Warning(category, template, arg1, arg2, arg3);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        public static void Error(LogCategory category, string message) => Impl.Error(category, message);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        public static void Error<T1>(LogCategory category, string template, T1 arg1) => Impl.Error(category, template, arg1);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        public static void Error<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Impl.Error(category, template, arg1, arg2);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        public static void Error<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Impl.Error(category, template, arg1, arg2, arg3);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（控制台仍走 <c>LogError</c>，文件里与 Error 区分）。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        public static void Fatal(LogCategory category, string message) => Impl.Fatal(category, message);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        public static void Fatal<T1>(LogCategory category, string template, T1 arg1) => Impl.Fatal(category, template, arg1);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        public static void Fatal<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2) => Impl.Fatal(category, template, arg1, arg2);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        public static void Fatal<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3) => Impl.Fatal(category, template, arg1, arg2, arg3);

        #endregion

        #region Internal — Auto Lifecycle

        /// <summary>
        /// 自动初始化：登记主线程、丢弃上一会话的实现、补挂退出时冲刷的订阅。幂等。
        /// <para><b>两个特性都要挂</b>：编辑器特性只在程序集加载（含重编译引发的域重载）时执行；关闭
        /// Reload Domain 后进入播放不会重新加载程序集，<see cref="RuntimeInitializeOnLoadMethodAttribute"/>
        /// 是唯一能补挂它的时机（同 <c>TimerManager</c>）。</para>
        /// <para><b>订阅必须幂等</b>：关闭域重载时 <c>Application.quitting</c> 的订阅表跨播放会话存活，
        /// 重复 <c>+=</c> 会逐次累积——先 <c>-=</c> 再 <c>+=</c> 保证恰好一条。</para>
        /// <para><b>档位取 <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/></b>：它是最早的一档，
        /// 主线程登记与全量捕获的挂接越早越好。<b>注意同档内的相对顺序没有契约</b>——Unity 不承诺、
        /// <c>AutoInitTests</c> 锁的也只是档位而非先后，所以「本方法先于同档其它 AutoInit
        /// （Update / Message / Timer）执行」不是保障，只是「那三个都不打日志」这一事实的结果。
        /// 将来若本档新增会打日志的 AutoInit，请把它挪出本档——否则那些日志会落在随后被本方法
        /// 丢弃的默认实现上。想改档位请同步更新 <c>Tests/Runtime/Architecture/AutoInitTests</c>
        /// 的族清单，否则守卫会红。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void AutoInit()
        {
            LogSession.Reset(); // 新会话：新的 session id 与计时起点（关闭域重载时静态字段跨播放会话存活）
            LogManagerImpl.MarkMainThread();

            ILogManager previous;
            lock (Gate)
            {
                previous = _impl;
                _impl = null;
            }

            previous?.Shutdown();

            UnityLogCapture.Attach();

            // 立即装入默认实现：会话文件与全量捕获从启动第一帧就生效。若等到第一次框架日志再懒创建，
            // 「启动阶段」的引擎/第三方日志（恰恰是排查启动失败最需要的那些）会整段丢失。
            _ = Impl;

            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        /// <summary>
        /// 退出清理：冲刷缓冲（崩溃/退出时文件里不能缺最后几条）。
        /// <para>不在这里退订——正确性依赖 <see cref="AutoInit"/> 里的先 <c>-=</c> 再 <c>+=</c>。</para>
        /// </summary>
        private static void OnQuitting()
        {
            _impl?.Flush();
        }

        #endregion
    }
}
