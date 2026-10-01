using System;

namespace XFramework.XLog
{
    /// <summary>
    /// 日志管理器。第三方可整体替换（注入自己的实现走别的后端），也可只追加 <see cref="ILogSink"/>。
    /// <para><b>为什么叫 <c>ILogManager</c> 而不是 <c>ILogger</c></b>：后者已被 <c>UnityEngine.ILogger</c>
    /// 占用（见 Roadmap §2.2）。</para>
    /// <para><b>模板 + 参数</b>：<c>Xxx(category, "x {0}", arg)</c> 形式的重载在<b>档位未启用时不格式化</b>
    /// ——这是本模块相对 <c>Debug.Log($"…")</c> 的核心性能承诺。参数仍在调用方求值（与插值一致），
    /// 代价高的先用 <see cref="IsEnabled"/> 探测。泛型重载避免的是 <c>params object[]</c> 数组分配，
    /// 值类型参数在<b>启用路径</b>仍会装箱——这是刻意的取舍。</para>
    /// <para><b>永不抛</b>：日志调用路径不得抛异常。sink 抛异常会被静默摘除并计入
    /// <c>LogManager.DroppedSinkCount</c>。参数防御（null 配置等）由门面负责。</para>
    /// </summary>
    public interface ILogManager
    {
        #region 状态

        /// <summary>当前是否已装入实现。门面按需创建，通常为 <c>true</c>。</summary>
        bool IsInitialized { get; }

        /// <summary>全局最低档位：低于它的日志一律丢弃（分类未设覆盖时按它判定）。</summary>
        LogLevel MinimumLevel { get; set; }

        /// <summary>设置分类档位覆盖，覆盖该分类的全局档位判定。</summary>
        /// <param name="category">分类；未注册（<c>default</c>）时抛 <see cref="ArgumentException"/>——静默忽略只会让配置错误更难查。</param>
        /// <param name="level">该分类的最低档位；<see cref="LogLevel.Off"/> 表示静默。</param>
        void SetCategoryLevel(LogCategory category, LogLevel level);

        /// <summary>还原分类档位为「跟随全局档」。</summary>
        /// <param name="category">分类；未注册时抛 <see cref="ArgumentException"/>。</param>
        void ResetCategoryLevel(LogCategory category);

        /// <summary>该级别 + 分类是否会被输出。用于「先探测再构造昂贵参数」。</summary>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <returns>是否启用。</returns>
        bool IsEnabled(LogLevel level, LogCategory category);

        #endregion

        #region 通用入口

        /// <summary>写一条指定级别的日志。</summary>
        /// <param name="level">级别；<see cref="LogLevel.Off"/> 不会产生输出。</param>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文（不含 <c>[Category] </c> 前缀）。</param>
        void Log(LogLevel level, LogCategory category, string message);

        /// <summary>写一条指定级别的日志，正文由模板 + 1 个参数格式化。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <param name="template">模板，语义与 <c>string.Format</c> 一致（与插值同源）。</param>
        /// <param name="arg1">参数。</param>
        void Log<T1>(LogLevel level, LogCategory category, string template, T1 arg1);

        /// <summary>写一条指定级别的日志，正文由模板 + 2 个参数格式化。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="level">级别。</param>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        void Log<T1, T2>(LogLevel level, LogCategory category, string template, T1 arg1, T2 arg2);

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
        void Log<T1, T2, T3>(LogLevel level, LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3);

        /// <summary>写一条异常日志（级别固定 <see cref="LogLevel.Error"/>）。</summary>
        /// <param name="category">分类。</param>
        /// <param name="exception">异常；为 null 时什么都不做。</param>
        /// <param name="message">正文；为 null 时用 <c>Exception.Message</c>。</param>
        void Exception(LogCategory category, Exception exception, string message = null);

        #endregion

        #region 分级入口

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        void Verbose(LogCategory category, string message);

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        void Verbose<T1>(LogCategory category, string template, T1 arg1);

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        void Verbose<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2);

        /// <summary>写一条 <see cref="LogLevel.Verbose"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        void Verbose<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        void Debug(LogCategory category, string message);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        void Debug<T1>(LogCategory category, string template, T1 arg1);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        void Debug<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2);

        /// <summary>写一条 <see cref="LogLevel.Debug"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        void Debug<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        void Info(LogCategory category, string message);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        void Info<T1>(LogCategory category, string template, T1 arg1);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        void Info<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2);

        /// <summary>写一条 <see cref="LogLevel.Info"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        void Info<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        void Warning(LogCategory category, string message);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        void Warning<T1>(LogCategory category, string template, T1 arg1);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        void Warning<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2);

        /// <summary>写一条 <see cref="LogLevel.Warning"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        void Warning<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        void Error(LogCategory category, string message);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        void Error<T1>(LogCategory category, string template, T1 arg1);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        void Error<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2);

        /// <summary>写一条 <see cref="LogLevel.Error"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        void Error<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（控制台仍走 <c>LogError</c>，文件里与 Error 区分）。</summary>
        /// <param name="category">分类。</param>
        /// <param name="message">现成字符串正文。</param>
        void Fatal(LogCategory category, string message);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（模板 + 1 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数。</param>
        void Fatal<T1>(LogCategory category, string template, T1 arg1);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（模板 + 2 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        void Fatal<T1, T2>(LogCategory category, string template, T1 arg1, T2 arg2);

        /// <summary>写一条 <see cref="LogLevel.Fatal"/> 日志（模板 + 3 参，未启用时不格式化）。</summary>
        /// <typeparam name="T1">参数 1 类型。</typeparam>
        /// <typeparam name="T2">参数 2 类型。</typeparam>
        /// <typeparam name="T3">参数 3 类型。</typeparam>
        /// <param name="category">分类。</param>
        /// <param name="template">模板。</param>
        /// <param name="arg1">参数 1。</param>
        /// <param name="arg2">参数 2。</param>
        /// <param name="arg3">参数 3。</param>
        void Fatal<T1, T2, T3>(LogCategory category, string template, T1 arg1, T2 arg2, T3 arg3);

        #endregion

        #region 输出端与生命周期

        /// <summary>因 <see cref="ILogSink.Write"/> 抛异常被静默摘除的 sink 次数。诊断用——该模块因此不再产生任何日志。</summary>
        int DroppedSinkCount { get; }

        /// <summary>追加一个 sink。重复追加同一个实例会写两次（框架不做去重）。</summary>
        /// <param name="sink">输出端。</param>
        void AddSink(ILogSink sink);

        /// <summary>摘除一个 sink（按引用，移除首个匹配）。</summary>
        /// <param name="sink">输出端。</param>
        /// <returns>是否移除成功。</returns>
        bool RemoveSink(ILogSink sink);

        /// <summary>把所有 sink 的缓冲刷到目标。</summary>
        void Flush();

        /// <summary>冲刷并释放所有 sink，复位档位；实现随即不可用（门面会另建默认实现）。</summary>
        void Shutdown();

        #endregion
    }
}
