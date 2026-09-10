using System.Collections.Generic;

namespace XFramework.XMessage.Internal
{
    /// <summary>
    /// 一条消息通道:聚合同一 (消息类型[, Key]) 下的同步订阅流与缓冲订阅流。
    /// <para>两条流均惰性创建——从未被订阅/发布的方向保持为 <c>null</c>,不产生空流分配。</para>
    /// </summary>
    /// <remarks>
    /// 引入本类型是为让「按 (类型, Key) 聚合」成为唯一的数据组织方式:
    /// 投递、清理、淘汰与统计都沿同一条遍历路径,避免为每种流各维护一套字典。
    /// </remarks>
    internal sealed class MessageChannel<TMessage> : IMessageChannel
    {
        #region Private Fields

        private EventStream<TMessage> _sync;
        private BufferedEventStream<TMessage> _buffered;

        #endregion

        #region Accessors

        /// <summary>
        /// 同步订阅流;从未被订阅时为 <c>null</c>。
        /// <para>投递路径用此只读视图判空,避免为「发布过但无人订阅」的类型创建空流。</para>
        /// </summary>
        internal EventStream<TMessage> Sync => _sync;

        /// <summary>获取或创建同步订阅流。</summary>
        internal EventStream<TMessage> GetOrCreateSync() => _sync ??= new EventStream<TMessage>();

        /// <summary>
        /// 获取或创建缓冲订阅流。首次投递或首次缓冲订阅时创建,
        /// 以保证「订阅前发布的消息可重放」这一语义。
        /// </summary>
        internal BufferedEventStream<TMessage> GetOrCreateBuffered()
            => _buffered ??= new BufferedEventStream<TMessage>();

        #endregion

        #region IMessageChannel

        /// <summary>释放本通道持有的全部事件流。幂等。</summary>
        public void DisposeAll()
        {
            _sync?.Dispose();
            _buffered?.Dispose();
            _sync = null;
            _buffered = null;
        }

        #endregion
    }

    /// <summary>
    /// 消息通道的非泛型视图。供 broker 以 <c>Dictionary&lt;Type, ...&gt;</c> 统一持有与遍历,
    /// 避免非泛型 <c>IDictionary</c> 枚举带来的装箱分配。
    /// </summary>
    internal interface IMessageChannel
    {
        /// <summary>释放本通道持有的全部事件流。</summary>
        void DisposeAll();
    }

    /// <summary>
    /// 键值通道存储:同一消息类型下按 Key 分组的通道集合。
    /// <para>整体作为一个条目挂在 broker 的类型表上,使键值通道与类型通道共用同一套遍历路径。</para>
    /// </summary>
    internal sealed class KeyedChannelStore<TKey, TMessage> : IKeyedChannelStore
    {
        #region Private Fields

        private readonly Dictionary<TKey, MessageChannel<TMessage>> _channels = new();

        #endregion

        #region Accessors

        /// <summary>获取或创建指定 Key 的通道。</summary>
        internal MessageChannel<TMessage> GetOrCreate(TKey key)
        {
            if (!_channels.TryGetValue(key, out var channel))
            {
                channel = new MessageChannel<TMessage>();
                _channels[key] = channel;
            }
            return channel;
        }

        #endregion

        #region IKeyedChannelStore

        /// <summary>释放并清空全部键值通道。</summary>
        public void DisposeAll()
        {
            foreach (var pair in _channels)
                pair.Value.DisposeAll();
            _channels.Clear();
        }

        #endregion
    }

    /// <summary>
    /// 键值通道存储的非泛型视图。供 broker 以 <c>Dictionary&lt;Type, ...&gt;</c> 统一持有与遍历。
    /// </summary>
    internal interface IKeyedChannelStore
    {
        /// <summary>释放并清空全部键值通道。</summary>
        void DisposeAll();
    }
}
