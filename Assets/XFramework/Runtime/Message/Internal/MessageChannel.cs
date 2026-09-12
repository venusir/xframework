using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XMessage.Internal
{
    /// <summary>
    /// 一条消息通道:聚合同一 (消息类型[, Key]) 下的同步订阅流、缓冲订阅流与异步订阅登记。
    /// <para>三者均惰性创建——从未被使用的方向保持为 <c>null</c>,不产生空分配。</para>
    /// </summary>
    /// <remarks>
    /// 引入本类型是为让「按 (类型, Key) 聚合」成为唯一的数据组织方式:
    /// 投递、清理、淘汰与统计都沿同一条遍历路径,避免为每种流各维护一套字典。
    /// </remarks>
    internal sealed class MessageChannel<TMessage> : IMessageChannel
    {
        #region Private Fields

        private readonly Action _onEmpty;
        private EventStream<TMessage> _sync;
        private BufferedEventStream<TMessage> _buffered;
        private List<AsyncSubscription<TMessage>> _async;

        #endregion

        #region Lifecycle

        /// <summary>创建通道。</summary>
        /// <param name="onEmpty">
        /// 「订阅清零」通知:同步订阅数由 1 归零、或异步登记表被清空时回调,
        /// 由持有者据此判定并回收空通道;<c>null</c> 表示不参与自动回收。
        /// </param>
        internal MessageChannel(Action onEmpty) => _onEmpty = onEmpty;

        #endregion

        #region Accessors

        /// <summary>
        /// 同步订阅流;从未被订阅时为 <c>null</c>。
        /// <para>投递路径用此只读视图判空,避免为「发布过但无人订阅」的类型创建空流。</para>
        /// </summary>
        internal EventStream<TMessage> Sync => _sync;

        /// <summary>获取或创建同步订阅流。</summary>
        internal EventStream<TMessage> GetOrCreateSync()
        {
            if (_sync == null)
            {
                _sync = new EventStream<TMessage>();
                _sync.OnEmpty = _onEmpty;
            }
            return _sync;
        }

        /// <summary>
        /// 获取或创建缓冲订阅流。首次投递或首次缓冲订阅时创建,
        /// 以保证「订阅前发布的消息可重放」这一语义。
        /// </summary>
        internal BufferedEventStream<TMessage> GetOrCreateBuffered()
            => _buffered ??= new BufferedEventStream<TMessage>();

        /// <summary>异步订阅登记表;从未有异步订阅时为 <c>null</c>。</summary>
        internal List<AsyncSubscription<TMessage>> Async => _async;

        /// <summary>创建异步订阅并登记。调用方负责已取消等前置判定。</summary>
        internal AsyncSubscription<TMessage> AddAsync(
            Predicate<TMessage> filter,
            Func<TMessage, CancellationToken, UniTask> handler,
            CancellationToken cancellationToken)
        {
            _async ??= new List<AsyncSubscription<TMessage>>();
            var subscription = new AsyncSubscription<TMessage>(
                _async, filter, handler, cancellationToken, _onEmpty);

            // 构造期间外部令牌可能已被取消:Register 会同步内联触发退订,而此刻本项尚未入表,
            // 退订的 owner.Remove 落空,因而不会回调 _onEmpty。
            // 若仍把这条已退订的登记入表,登记表将永远非空 → IsReclaimable 恒为 false:
            // 自动回收与 TrimEmptyChannels 共用该谓词,双双失效,只能等 Clear()。
            if (!subscription.IsDisposed)
                _async.Add(subscription);

            return subscription;
        }

        #endregion

        #region IMessageChannel

        /// <summary>
        /// 是否可整条回收:无同步订阅、无异步订阅,且不持有缓冲流。
        /// <para>持有缓冲流即持有重放缓存,回收会破坏「订阅前发布可重放」语义,
        /// 故缓冲通道只能经 <see cref="EvictBuffered"/> 显式淘汰后才可能被回收。</para>
        /// </summary>
        public bool IsReclaimable =>
            (_sync == null || _sync.SubscriptionCount == 0)
            && (_async == null || _async.Count == 0)
            && _buffered == null;

        /// <summary>累加本通道的计数到 <paramref name="acc"/>。</summary>
        public void Accumulate(ref MessageStatsAccumulator acc)
        {
            acc.ChannelCount++;

            if (_sync != null)
                acc.SyncSubscriptionCount += _sync.SubscriptionCount;

            if (_buffered != null)
            {
                acc.BufferedChannelCount++;
                acc.SyncSubscriptionCount += _buffered.SubscriptionCount;
            }

            if (_async != null)
                acc.AsyncSubscriptionCount += _async.Count;
        }

        /// <summary>
        /// 淘汰缓冲流(丢弃其重放缓存)。返回是否确有缓冲流被淘汰。
        /// <para>
        /// 释放后已发出的缓冲订阅句柄即失效;此后对这些句柄调用 Dispose 会被事件流安全忽略
        /// (节点已不在链表中),不会重复回池。
        /// </para>
        /// <para>
        /// 本方法刻意只丢缓存,不改动通道归属(不回调 <c>_onEmpty</c>):它会被
        /// <see cref="KeyedChannelStore{TKey,TMessage}.EvictBufferedAllAndReclaimEmpty"/> 在自己的
        /// 字典遍历中调用,此处若回调回收就是在遍历中改表,必然抛 InvalidOperationException。
        /// 回收一律由持有者在遍历之外完成。
        /// </para>
        /// </summary>
        public bool EvictBuffered()
        {
            if (_buffered == null)
                return false;

            _buffered.Dispose();
            _buffered = null;
            return true;
        }

        /// <summary>释放本通道持有的全部事件流与异步订阅。幂等。</summary>
        public void DisposeAll()
        {
            _sync?.Dispose();
            _buffered?.Dispose();

            if (_async != null)
            {
                // 先与登记表断开再逐个释放:否则 Dispose 会边遍历边改表
                for (int i = 0; i < _async.Count; i++)
                    _async[i].DisposeDetached();
                _async.Clear();
            }

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
        /// <summary>是否可整条回收(无同步订阅且不持有缓冲流)。</summary>
        bool IsReclaimable { get; }

        /// <summary>淘汰缓冲流(丢弃重放缓存)。返回是否确有缓冲流被淘汰。</summary>
        bool EvictBuffered();

        /// <summary>累加本通道的计数到 <paramref name="acc"/>。</summary>
        void Accumulate(ref MessageStatsAccumulator acc);

        /// <summary>释放本通道持有的全部事件流。</summary>
        void DisposeAll();
    }

    /// <summary>
    /// 统计累加器。以 <c>ref</c> 传给各通道逐层累加,避免诊断路径产生装箱或中间分配。
    /// </summary>
    internal struct MessageStatsAccumulator
    {
        /// <summary>通道数。</summary>
        public int ChannelCount;

        /// <summary>同步订阅数(普通 + 缓冲)。</summary>
        public int SyncSubscriptionCount;

        /// <summary>异步订阅数。</summary>
        public int AsyncSubscriptionCount;

        /// <summary>持有重放缓存的通道数。</summary>
        public int BufferedChannelCount;
    }

    /// <summary>
    /// 键值通道存储:同一消息类型下按 Key 分组的通道集合。
    /// <para>整体作为一个条目挂在 broker 的类型表上,使键值通道与类型通道共用同一套遍历路径。</para>
    /// </summary>
    internal sealed class KeyedChannelStore<TKey, TMessage> : IKeyedChannelStore
    {
        #region Private Fields

        private readonly Dictionary<TKey, MessageChannel<TMessage>> _channels = new();
        private readonly Action<TKey> _onChannelEmpty;

        #endregion

        #region Lifecycle

        /// <summary>创建键值通道存储。</summary>
        /// <param name="onChannelEmpty">
        /// 某个 Key 的通道同步订阅数归零时的回调(参数为该 Key),由 broker 用于回收;
        /// <c>null</c> 表示不参与自动回收。
        /// </param>
        internal KeyedChannelStore(Action<TKey> onChannelEmpty) => _onChannelEmpty = onChannelEmpty;

        #endregion

        #region Accessors

        /// <summary>获取或创建指定 Key 的通道。</summary>
        internal MessageChannel<TMessage> GetOrCreate(TKey key)
        {
            if (!_channels.TryGetValue(key, out var channel))
            {
                var captured = key;
                channel = new MessageChannel<TMessage>(() => _onChannelEmpty?.Invoke(captured));
                _channels[key] = channel;
            }
            return channel;
        }

        /// <summary>尝试获取指定 Key 的通道;不存在时返回 <c>false</c>,且不创建。</summary>
        internal bool TryGet(TKey key, out MessageChannel<TMessage> channel)
            => _channels.TryGetValue(key, out channel);

        /// <summary>移除指定 Key 的通道条目。</summary>
        internal void Remove(TKey key) => _channels.Remove(key);

        #endregion

        #region IKeyedChannelStore

        /// <summary>当前 Key 的通道数量。</summary>
        public int Count => _channels.Count;

        /// <summary>回收本存储内全部可回收的空通道,返回回收数量。</summary>
        public int TrimEmpty()
        {
            var removed = 0;
            var keys = ListPool<TKey>.Rent();
            try
            {
                // 先收集再删除:遍历中改字典会抛 InvalidOperationException
                foreach (var pair in _channels)
                {
                    if (pair.Value.IsReclaimable)
                        keys.Add(pair.Key);
                }

                for (int i = 0; i < keys.Count; i++)
                {
                    _channels.Remove(keys[i]);
                    removed++;
                }
            }
            finally
            {
                ListPool<TKey>.Return(keys);
            }
            return removed;
        }

        /// <summary>
        /// 淘汰本存储内全部缓冲通道,并顺带回收因此变空的通道;返回淘汰数量(不含回收数)。
        /// <para>两步合成一个方法,是为了让调用方无从把顺序写反——先 trim 再 evict 恰好会留下
        /// 本该消灭的空壳;同时「遍历中不改本字典」的两阶段纪律留在拥有该字典的类型里。</para>
        /// </summary>
        public int EvictBufferedAllAndReclaimEmpty()
        {
            var evicted = EvictBufferedAll();
            TrimEmpty();
            return evicted;
        }

        /// <summary>淘汰本存储内全部缓冲通道(丢弃重放缓存),返回淘汰数量。不回收空通道。</summary>
        private int EvictBufferedAll()
        {
            var removed = 0;
            foreach (var pair in _channels)
            {
                if (pair.Value.EvictBuffered())
                    removed++;
            }
            return removed;
        }

        /// <summary>
        /// 淘汰指定 Key 的缓冲通道,并回收因此变空的该 Key 通道;返回是否淘汰发生。
        /// <para>供 broker 按「Key」维度跨消息类型淘汰时经非泛型视图调用:调用方已按
        /// <c>KeyType == typeof(TKey)</c> 过滤,故 <paramref name="key"/> 必可转换。</para>
        /// <para><paramref name="key"/> 为 <c>object</c> 会让值类型 Key 装箱,但淘汰不在热路径上,
        /// 换来的是无需按 TMessage 反射构造泛型存储类型。</para>
        /// </summary>
        public bool EvictBufferedByKey(object key)
        {
            var typedKey = (TKey)key;
            if (!_channels.TryGetValue(typedKey, out var channel) || !channel.EvictBuffered())
                return false;

            // 与 ReclaimKeyedChannelIfEmpty 同一条闸门:淘汰只丢重放缓存,仍有活订阅者的通道不得回收
            if (channel.IsReclaimable)
                _channels.Remove(typedKey);
            return true;
        }

        /// <summary>累加本存储内全部通道的计数到 <paramref name="acc"/>。</summary>
        public void Accumulate(ref MessageStatsAccumulator acc)
        {
            foreach (var pair in _channels)
                pair.Value.Accumulate(ref acc);
        }

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
        /// <summary>当前 Key 的通道数量。</summary>
        int Count { get; }

        /// <summary>回收本存储内全部可回收的空通道,返回回收数量。</summary>
        int TrimEmpty();

        /// <summary>淘汰本存储内全部缓冲通道并回收因此变空的通道,返回淘汰数量(不含回收数)。</summary>
        int EvictBufferedAllAndReclaimEmpty();

        /// <summary>
        /// 淘汰指定 Key 的缓冲通道并回收因此变空的该 Key 通道,返回是否淘汰发生。
        /// <para><paramref name="key"/> 的运行时类型必须是本存储的 Key 类型。</para>
        /// </summary>
        bool EvictBufferedByKey(object key);

        /// <summary>累加本存储内全部通道的计数到 <paramref name="acc"/>。</summary>
        void Accumulate(ref MessageStatsAccumulator acc);

        /// <summary>释放并清空全部键值通道。</summary>
        void DisposeAll();
    }
}
