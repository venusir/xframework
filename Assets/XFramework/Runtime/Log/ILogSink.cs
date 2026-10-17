namespace XFramework.XLog
{
    /// <summary>
    /// 日志输出端。框架把每条经过过滤的日志交给所有已挂 sink。
    /// <para><b>契约</b>：① 任意线程调用；② <see cref="Write"/> <b>不得抛异常</b>——抛出的 sink 会被
    /// <b>静默摘除</b>并计入 <c>LogManager.DroppedSinkCount</c>，因为「因为日志坏了而记一条日志」会递归
    /// （且测试把意外 Error 当失败）；③ 实现应自行保证内部线程安全（框架可能并发调用）。</para>
    /// <para><b>重入</b>：在 <see cref="Write"/> 里经 <c>LogManager</c> 再写日志会正常嵌套投递（既有行为，
    /// 请自行收口递归）；经 <c>Debug.Log*</c> 再写则会被丢弃——同步捕获没有闩锁会无限递归到同一个 sink。</para>
    /// <para><b>生命周期可选</b>：实现 <see cref="System.IDisposable"/> 时框架在
    /// <c>Shutdown</c> / 摘除时释放它（同 <c>IAtomicFileProvider</c> 的可选能力接口形态）。</para>
    /// </summary>
    public interface ILogSink
    {
        /// <summary>写入一条日志。任意线程调用，不得抛异常。</summary>
        /// <param name="entry">日志条目（只读快照）。</param>
        void Write(in LogEntry entry);

        /// <summary>把缓冲刷到目标；无缓冲时可为空实现。任意线程调用，不得抛异常。</summary>
        void Flush();
    }
}
