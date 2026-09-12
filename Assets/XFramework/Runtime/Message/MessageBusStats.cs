namespace XFramework.XMessage
{
    /// <summary>
    /// 消息总线的只读统计快照。
    /// <para>
    /// 用于诊断两类常见问题:订阅泄漏(订阅数只增不减)与缓冲通道内存驻留
    /// (<see cref="BufferedChannelCount"/> 对应每个 Key 各持有一条消息的通道数)。
    /// </para>
    /// <para>获取一次需遍历全部通道,属诊断接口,不适合每帧调用。</para>
    /// </summary>
    public readonly struct MessageBusStats
    {
        /// <summary>
        /// 通道存储表项数(类型通道表 + 键值通道表)。
        /// <para><b>不等于「消息类型的个数」</b>——同一消息类型若既有类型通道又配了键值通道,
        /// 或配了多种 Key 类型,各占一项而重复计数。本属性衡量的是两张表的表项规模。</para>
        /// </summary>
        public int ChannelStoreCount { get; }

        /// <summary>当前存活通道总数(类型通道 + 所有键值通道)。</summary>
        public int ChannelCount { get; }

        /// <summary>当前同步订阅总数(普通订阅 + 缓冲订阅)。</summary>
        public int SyncSubscriptionCount { get; }

        /// <summary>当前异步订阅总数。</summary>
        public int AsyncSubscriptionCount { get; }

        /// <summary>当前持有重放缓存的通道数(排查缓冲内存驻留的主要指标)。</summary>
        public int BufferedChannelCount { get; }

        /// <summary>自上次 Clear/SetInstance 以来的发布次数(含键值发布与 PublishAsync)。</summary>
        public int PublishCount { get; }

        /// <summary>自上次 Clear/SetInstance 以来的请求次数。</summary>
        public int RequestCount { get; }

        /// <summary>当前请求处理器数量。</summary>
        public int RequestHandlerCount { get; }

        /// <summary>当前全局过滤器数量(所有消息类型合计)。</summary>
        public int FilterCount { get; }

        /// <summary>创建统计快照。仅供模块内部构造。</summary>
        internal MessageBusStats(
            int channelStoreCount,
            int channelCount,
            int syncSubscriptionCount,
            int asyncSubscriptionCount,
            int bufferedChannelCount,
            int publishCount,
            int requestCount,
            int requestHandlerCount,
            int filterCount)
        {
            ChannelStoreCount = channelStoreCount;
            ChannelCount = channelCount;
            SyncSubscriptionCount = syncSubscriptionCount;
            AsyncSubscriptionCount = asyncSubscriptionCount;
            BufferedChannelCount = bufferedChannelCount;
            PublishCount = publishCount;
            RequestCount = requestCount;
            RequestHandlerCount = requestHandlerCount;
            FilterCount = filterCount;
        }

        /// <summary>返回便于日志阅读的紧凑描述。</summary>
        public override string ToString()
            => $"MessageBusStats(通道表 {ChannelStoreCount}, 通道 {ChannelCount}, 同步订阅 {SyncSubscriptionCount}, " +
               $"异步订阅 {AsyncSubscriptionCount}, 缓冲通道 {BufferedChannelCount}, 发布 {PublishCount}, " +
               $"请求 {RequestCount}, 请求处理器 {RequestHandlerCount}, 过滤器 {FilterCount})";
    }
}
