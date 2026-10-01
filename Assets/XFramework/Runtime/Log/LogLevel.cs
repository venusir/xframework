namespace XFramework.XLog
{
    /// <summary>
    /// 日志级别。数值越大越严重，比较即过滤（<c>level &gt;= 阈值</c> 即输出）。
    /// <para><b>与 Unity 控制台的对照</b>：<see cref="Verbose"/> / <see cref="Debug"/> / <see cref="Info"/>
    /// 走 <c>Debug.Log</c>，<see cref="Warning"/> 走 <c>Debug.LogWarning</c>，
    /// <see cref="Error"/> / <see cref="Fatal"/> 走 <c>Debug.LogError</c>——迁移前的
    /// <c>Debug.Log/LogWarning/LogError</c> 因此逐一对得上，可见性不变。</para>
    /// <para><see cref="Off"/> <b>仅作档位阈值</b>（用于静默某个分类），把它作为写入级别传给
    /// <c>LogManager</c> 的日志方法不会产生任何输出（不会产生「比 Fatal 更严重」的级别）。</para>
    /// </summary>
    public enum LogLevel
    {
        /// <summary>最啰嗦的诊断信息。<b>默认关闭</b>——它存在的意义就是「平时不出现，出事时能开」。</summary>
        Verbose = 0,

        /// <summary>开发期诊断信息。Editor 与 Development 构建默认开启，Release 默认关闭。</summary>
        Debug = 1,

        /// <summary>常规运行信息。</summary>
        Info = 2,

        /// <summary>可恢复的异常情况；预期内的失败用它，不要用异常。</summary>
        Warning = 3,

        /// <summary>错误。</summary>
        Error = 4,

        /// <summary>致命错误（启动失败等）。文件里与 <see cref="Error"/> 区分，控制台同为 <c>LogError</c>。</summary>
        Fatal = 5,

        /// <summary>仅作阈值：静默该分类。</summary>
        Off = 6,
    }
}
