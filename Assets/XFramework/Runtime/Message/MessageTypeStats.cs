using System;

namespace XFramework.XMessage
{
    /// <summary>
    /// 单个消息类型的通道统计快照:该类型下<b>类型级通道与全部键值通道的合计</b>。
    /// <para>用途是回答「是哪个消息类型在泄漏」——<see cref="MessageBusStats"/> 只给总数,
    /// <see cref="MessageChannelStats"/> 要先知道类型或 Key,而排查泄漏时恰恰不知道该查谁。</para>
    /// <para><b>与 <see cref="MessageChannelStats"/> 的分工</b>:后者是<b>单个通道</b>的快照
    /// (按类型查询时只算类型级通道、不含键值通道),本结构是<b>按消息类型合并</b>的视图
    /// (类型级 + 该类型下所有 Key 类型的所有 Key)。两者共用同一份「按类型扫表」的实现,
    /// 但口径不同,不是重复 API。</para>
    /// <para><b>零分配</b>:本结构由调用方提供的缓冲区承载,见 <c>MessageManager.CopyTypeStats</c>。</para>
    /// </summary>
    public readonly struct MessageTypeStats
    {
        /// <summary>消息类型。</summary>
        public Type MessageType { get; }

        /// <summary>该类型下的通道总数(类型级通道 + 全部键值通道)。</summary>
        public int ChannelCount { get; }

        /// <summary>该类型下的同步订阅总数(普通订阅 + 缓冲订阅)。</summary>
        public int SyncSubscriptionCount { get; }

        /// <summary>该类型下的异步订阅总数。</summary>
        public int AsyncSubscriptionCount { get; }

        /// <summary>
        /// 该类型下建有缓冲订阅流的通道数。
        /// <para>口径与 <see cref="MessageBusStats.BufferedChannelCount"/> 一致:<b>不等于「持有重放缓存」</b>
        /// ——订阅过缓冲、但该类型从未发布过的通道同样计入,它们没有值可重放,且可被
        /// <see cref="MessageManager.TrimEmptyChannels"/> 回收。</para>
        /// </summary>
        public int BufferedChannelCount { get; }

        /// <summary>该类型下的键值通道数(所有 Key 类型、所有 Key 合计)。</summary>
        public int KeyedChannelCount { get; }

        /// <summary>创建统计行。仅供模块内部构造。</summary>
        internal MessageTypeStats(
            Type messageType,
            int channelCount,
            int syncSubscriptionCount,
            int asyncSubscriptionCount,
            int bufferedChannelCount,
            int keyedChannelCount)
        {
            MessageType = messageType;
            ChannelCount = channelCount;
            SyncSubscriptionCount = syncSubscriptionCount;
            AsyncSubscriptionCount = asyncSubscriptionCount;
            BufferedChannelCount = bufferedChannelCount;
            KeyedChannelCount = keyedChannelCount;
        }

        /// <summary>返回便于日志阅读的紧凑描述。</summary>
        public override string ToString()
            => $"MessageTypeStats({MessageType?.Name}, 通道 {ChannelCount}, 同步订阅 {SyncSubscriptionCount}, " +
               $"异步订阅 {AsyncSubscriptionCount}, 缓冲通道 {BufferedChannelCount}, 键值通道 {KeyedChannelCount})";
    }
}
