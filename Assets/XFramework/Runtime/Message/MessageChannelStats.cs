namespace XFramework.XMessage
{
    /// <summary>
    /// 单个消息通道的只读统计快照。通道不存在时各字段为 0 / <c>false</c>。
    /// </summary>
    public readonly struct MessageChannelStats
    {
        /// <summary>该通道的同步订阅数(普通订阅 + 缓冲订阅)。</summary>
        public int SyncSubscriptionCount { get; }

        /// <summary>该通道的异步订阅数。</summary>
        public int AsyncSubscriptionCount { get; }

        /// <summary>该通道是否持有可重放缓存。</summary>
        public bool HasBufferedValue { get; }

        /// <summary>
        /// 该消息类型下的键值通道数。
        /// <para>仅在按消息类型查询时有意义;按具体 Key 查询与通道不存在时均为 0。</para>
        /// </summary>
        public int KeyedChannelCount { get; }

        /// <summary>创建通道统计快照。仅供模块内部构造。</summary>
        internal MessageChannelStats(
            int syncSubscriptionCount,
            int asyncSubscriptionCount,
            bool hasBufferedValue,
            int keyedChannelCount)
        {
            SyncSubscriptionCount = syncSubscriptionCount;
            AsyncSubscriptionCount = asyncSubscriptionCount;
            HasBufferedValue = hasBufferedValue;
            KeyedChannelCount = keyedChannelCount;
        }

        /// <summary>返回便于日志阅读的紧凑描述。</summary>
        public override string ToString()
            => $"MessageChannelStats(同步订阅 {SyncSubscriptionCount}, 异步订阅 {AsyncSubscriptionCount}, " +
               $"有重放缓存 {HasBufferedValue}, 键值通道 {KeyedChannelCount})";
    }
}
