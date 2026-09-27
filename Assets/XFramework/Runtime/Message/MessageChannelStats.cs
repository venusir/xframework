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

        /// <summary>
        /// 该通道是否建有缓冲订阅流。
        /// <para><b>不等于「有可重放的值」:</b>订阅过缓冲、但该类型从未发布过的通道同样为 <c>true</c>,
        /// 此时新订阅者不会收到任何重放,该通道也属可回收之列(见 <c>MessageManager.TrimEmptyChannels</c>)。
        /// </para>
        /// </summary>
        public bool HasBufferedValue { get; }

        /// <summary>
        /// 该消息类型下的键值通道数。
        /// <para>仅在按消息类型查询时有意义;按具体 Key 查询与通道不存在时均为 0。</para>
        /// <para>与 <c>MessageTypeStats.KeyedChannelCount</c> 是同一份「按消息类型扫表」结果的两种视图
        /// (共用 <c>MessageBroker.CountKeyedChannels</c>),不是各自算的——两者应当永远相等。</para>
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
               $"有缓冲流 {HasBufferedValue}, 键值通道 {KeyedChannelCount})";
    }
}
