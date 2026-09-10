namespace XFramework.XMessage
{
    /// <summary>
    /// <see cref="MessageManager"/> 的 PublishAsync 等待异步处理器时采用的调度策略。
    /// <para>仅影响异步处理器的启动与等待方式,同步订阅者与缓冲通道的投递不受影响。</para>
    /// </summary>
    public enum MessagePublishStrategy
    {
        /// <summary>
        /// 并行:全部异步处理器先启动,再统一等待它们完成(默认,吞吐优先)。
        /// <para>处理器之间无先后保证,适合彼此独立的响应方。</para>
        /// </summary>
        Parallel = 0,

        /// <summary>
        /// 顺序:按订阅先后逐个 await,前一个完成后才启动下一个。
        /// <para>适合响应方之间存在顺序依赖的场景,总耗时等于各处理器之和。</para>
        /// </summary>
        Sequential = 1,
    }
}
