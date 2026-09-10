using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XMessage.Internal;

namespace XFramework.XMessage
{
    /// <summary>
    /// 消息代理实现:消息分发层,订阅直接落到自研事件流并返回 IDisposable(无中间订阅源)。
    /// 支持普通消息、键值消息、异步消息、缓冲消息和消息过滤器。
    /// </summary>
    /// <remarks>
    /// 通道结构:
    /// - 非键值消息:类型表 _channels(消息类型 -> 通道)
    /// - 键值消息:类型表 _keyedChannels((消息类型, Key 类型) -> Key -> 通道),
    ///   三层的中间两层均为字典,故值类型 Key 与值类型消息都无 boxing
    /// - 一条 MessageChannel 聚合该 (消息类型[, Key]) 下的同步流与缓冲流,
    ///   使投递、清理、淘汰与统计共用同一条遍历路径
    ///
    /// GC 优化说明:
    /// - 过滤/异步等订阅配置以一次性闭包表达,仅在订阅时分配一次(非热路径)
    /// - ApplyFilters 使用预构建的 pipeline 缓存 + 无 LINQ 遍历,无过滤器时零分配
    /// - 同步流与缓冲流均惰性创建,发布过但无人订阅的类型不会产生空流
    /// - 投递热路径(Publish/OnNext)除锁与池化快照外零分配
    /// </remarks>
    internal sealed class MessageBroker : IMessageBroker
    {
        #region Private Fields

        /// <summary>按消息类型缓存的通道(承载该类型的同步流与缓冲流)。</summary>
        private readonly Dictionary<Type, IMessageChannel> _channels = new();

        /// <summary>
        /// 按 (消息类型, Key 类型) 缓存的键值通道存储((Type,Type) -> Key -> 通道)。
        /// <para>
        /// 必须同时以 Key 类型为键:同一消息类型允许配多种 Key 类型
        /// (如 <c>Publish("Score", 1)</c> 与 <c>Publish(1, 1)</c>),仅以消息类型为键会让后者
        /// 强转到前者创建的存储并抛 InvalidCastException。
        /// </para>
        /// </summary>
        private readonly Dictionary<(Type MessageType, Type KeyType), IKeyedChannelStore> _keyedChannels = new();

        /// <summary>按消息类型存储的过滤器列表。</summary>
        private readonly Dictionary<Type, List<object>> _filtersByType = new();

        /// <summary>预构建的过滤器 pipeline 缓存（在 AddFilter 时失效重建）。</summary>
        private readonly Dictionary<Type, Delegate> _filterPipelines = new();

        #endregion

        #region Publish

        public void Publish<TMessage>(TMessage message)
        {
            var type = typeof(TMessage);

            // 执行过滤器管道（无过滤器时零分配）
            if (!ApplyFilters(type, message))
                return;

            var channel = GetOrCreateChannel<TMessage>();

            // 推送给普通订阅者(Sync 为 null 表示从未有人订阅,跳过)
            channel.Sync?.OnNext(message);

            // 推送给缓冲订阅者(GetOrCreate:确保订阅前发布的消息也被缓存,新订阅者可重放最近一条)
            channel.GetOrCreateBuffered().OnNext(message);
        }

        public void Publish<TKey, TMessage>(TKey key, TMessage message)
        {
            var type = typeof(TMessage);

            // 执行过滤器管道
            if (!ApplyFilters(type, message))
                return;

            var channel = GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key);

            // 推送给键值订阅者(Sync 为 null 表示该 Key 从未有人订阅,跳过)
            channel.Sync?.OnNext(message);

            // 推送给键值缓冲订阅者(GetOrCreate:同上,订阅前发布的消息可重放)
            channel.GetOrCreateBuffered().OnNext(message);
        }

        #endregion

        #region Subscribe

        /// <summary>订阅指定类型的消息,直接落事件流并返回退订句柄。</summary>
        public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateChannel<TMessage>().GetOrCreateSync().Subscribe(handler);
        }

        /// <summary>订阅指定类型的消息,附加订阅级过滤条件(返回 false 的消息不投递给该订阅)。</summary>
        /// <remarks>
        /// 过滤与回调组合为订阅期一次性闭包:过滤条件抛异常时由事件流的异常隔离兜底
        /// (记 Error 日志、本条不投递、订阅保留、其他订阅者不受影响)。
        /// </remarks>
        public IDisposable Subscribe<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateChannel<TMessage>().GetOrCreateSync().Subscribe(m =>
            {
                if (filter.Invoke(m)) handler(m);
            });
        }

        /// <summary>订阅指定键值的消息。</summary>
        public IDisposable Subscribe<TKey, TMessage>(TKey key, Action<TMessage> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key)
                .GetOrCreateSync().Subscribe(handler);
        }

        /// <summary>订阅指定键值的消息,附加订阅级过滤条件。</summary>
        public IDisposable Subscribe<TKey, TMessage>(TKey key, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key)
                .GetOrCreateSync().Subscribe(m =>
                {
                    if (filter.Invoke(m)) handler(m);
                });
        }

        /// <summary>
        /// 异步订阅:消息到达时触发异步处理器(fire-and-forget)。
        /// <para>异步处理器经 Forget() 烧录进订阅回调,后续异常由 UniTask 的 Forget 语义兜底。</para>
        /// </summary>
        public IDisposable SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler)
        {
            if (asyncHandler == null) throw new ArgumentNullException(nameof(asyncHandler));
            return GetOrCreateChannel<TMessage>().GetOrCreateSync()
                .Subscribe(m => asyncHandler(m).Forget());
        }

        /// <summary>异步订阅,附加订阅级过滤条件。</summary>
        public IDisposable SubscribeAsync<TMessage>(Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (asyncHandler == null) throw new ArgumentNullException(nameof(asyncHandler));
            return GetOrCreateChannel<TMessage>().GetOrCreateSync().Subscribe(m =>
            {
                if (filter.Invoke(m)) asyncHandler(m).Forget();
            });
        }

        /// <summary>订阅带缓冲的消息:新订阅者立即同步收到最近一次发布的消息(若有),之后转为实时。</summary>
        public IDisposable SubscribeBuffered<TMessage>(Action<TMessage> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateChannel<TMessage>().GetOrCreateBuffered().Subscribe(handler);
        }

        /// <summary>订阅带缓冲的消息,附加订阅级过滤条件(重放与实时共用同一过滤)。</summary>
        public IDisposable SubscribeBuffered<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateChannel<TMessage>().GetOrCreateBuffered().Subscribe(m =>
            {
                if (filter.Invoke(m)) handler(m);
            });
        }

        /// <summary>订阅带缓冲的键值消息。新订阅者立即同步收到最近一次发布的消息(若有)。</summary>
        public IDisposable SubscribeBuffered<TKey, TMessage>(TKey key, Action<TMessage> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key)
                .GetOrCreateBuffered().Subscribe(handler);
        }

        #endregion

        #region Filters

        /// <summary>注册全局消息过滤器。</summary>
        public void AddFilter<TMessage>(IMessageFilter<TMessage> filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var type = typeof(TMessage);
            if (!_filtersByType.TryGetValue(type, out var list))
            {
                list = new List<object>();
                _filtersByType[type] = list;
            }
            list.Add(filter);
            // 使缓存 pipeline 失效，下次 Publish 时重建
            _filterPipelines.Remove(type);
        }

        /// <summary>移除已注册的全局过滤器(同一实例重复注册时移除首个匹配项)。返回是否找到并移除。</summary>
        public bool RemoveFilter<TMessage>(IMessageFilter<TMessage> filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var type = typeof(TMessage);
            if (!_filtersByType.TryGetValue(type, out var list))
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i], filter))
                    continue;

                list.RemoveAt(i);
                // 使缓存 pipeline 失效，下次 Publish 时重建
                _filterPipelines.Remove(type);
                return true;
            }

            return false;
        }

        /// <summary>移除指定消息类型的全部全局过滤器,返回移除数量。</summary>
        public int ClearFilters<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_filtersByType.TryGetValue(type, out var list))
                return 0;

            var count = list.Count;
            _filtersByType.Remove(type);
            _filterPipelines.Remove(type);
            return count;
        }

        /// <summary>
        /// 应用过滤器管道。返回 false 表示消息被拦截。
        /// 无过滤器时零堆分配。
        /// </summary>
        private bool ApplyFilters<TMessage>(Type type, TMessage message)
        {
            // 快速路径：该类型无过滤器 -> 零分配
            if (!_filtersByType.TryGetValue(type, out var filters) || filters.Count == 0)
                return true;

            // 获取或构建缓存的 pipeline
            if (!_filterPipelines.TryGetValue(type, out var pipelineObj) || pipelineObj == null)
            {
                var pipeline = BuildFilterPipeline<TMessage>(filters);
                _filterPipelines[type] = pipeline;
                pipelineObj = pipeline;
            }

            if (pipelineObj == null)
                return true;

            try
            {
                // pipeline 返回 bool:过滤器链全部放行才为 true
                return ((Func<TMessage, bool>)pipelineObj)(message);
            }
            catch (Exception e)
            {
                // 与订阅回调的异常日志同形:统一 [Message] 前缀,便于按模块检索
                Debug.LogError($"[Message] Global filter threw exception: {e}");
                return false;
            }
        }

        /// <summary>
        /// 构建过滤器管道（类似 ASP.NET Core Middleware）。
        /// 在注册过滤器时分配（一次性开销），Publish 时零分配。
        /// <para>过滤器通过「不调用 next」拦截消息;passed 变量在每次调用时创建(lambda 内声明),
        /// 避免重入 Publish(同类型)时嵌套调用覆写外层拦截状态。</para>
        /// </summary>
        private static Func<TMessage, bool> BuildFilterPipeline<TMessage>(List<object> filterObjects)
        {
            var typedFilters = new IMessageFilter<TMessage>[filterObjects.Count];
            for (int i = 0; i < filterObjects.Count; i++)
                typedFilters[i] = (IMessageFilter<TMessage>)filterObjects[i];

            // 构建调用链：终端放行
            Func<TMessage, bool> pipeline = _ => true;
            for (int i = typedFilters.Length - 1; i >= 0; i--)
            {
                var filter = typedFilters[i];
                var next = pipeline;
                pipeline = msg =>
                {
                    var passed = false;
                    filter.Invoke(msg, m =>
                    {
                        if (next(m)) passed = true;
                    });
                    return passed;
                };
            }
            return pipeline;
        }

        #endregion

        #region Eviction

        /// <summary>淘汰指定消息类型的类型级缓冲通道(丢弃其重放缓存)。返回是否存在并已淘汰。</summary>
        public bool EvictBufferedChannel<TMessage>()
            => _channels.TryGetValue(typeof(TMessage), out var channel) && channel.EvictBuffered();

        /// <summary>淘汰指定键值的缓冲通道(丢弃其重放缓存)。返回是否存在并已淘汰。</summary>
        public bool EvictBufferedChannel<TKey, TMessage>(TKey key)
            => _keyedChannels.TryGetValue((typeof(TMessage), typeof(TKey)), out var storeObj)
               && ((KeyedChannelStore<TKey, TMessage>)storeObj).TryGet(key, out var channel)
               && channel.EvictBuffered();

        /// <summary>淘汰指定消息类型的全部缓冲通道(类型级 + 该消息类型下所有 Key 类型的所有 Key),返回淘汰数量。</summary>
        public int EvictBufferedChannels<TMessage>()
        {
            var removed = 0;

            if (_channels.TryGetValue(typeof(TMessage), out var channel) && channel.EvictBuffered())
                removed++;

            // Key 类型不是本方法的类型参数,故按消息类型扫全表
            foreach (var pair in _keyedChannels)
            {
                if (pair.Key.MessageType == typeof(TMessage))
                    removed += pair.Value.EvictBufferedAll();
            }

            return removed;
        }

        /// <summary>
        /// 回收所有无可重放缓存且无订阅者的空通道,返回回收的通道数量。
        /// <para>不触碰缓冲通道——持有重放缓存的通道不受本方法影响,只能经
        /// <see cref="EvictBufferedChannel{TMessage}()"/> 系列显式淘汰。</para>
        /// </summary>
        public int TrimEmptyChannels()
        {
            var removed = 0;
            var emptyTypes = ListPool<Type>.Rent();
            var emptyStoreKeys = ListPool<(Type MessageType, Type KeyType)>.Rent();
            try
            {
                // 类型通道:先收集再删除,避免遍历中改字典
                foreach (var pair in _channels)
                {
                    if (pair.Value.IsReclaimable)
                        emptyTypes.Add(pair.Key);
                }

                for (int i = 0; i < emptyTypes.Count; i++)
                {
                    _channels.Remove(emptyTypes[i]);
                    removed++;
                }

                // 键值通道:先回收各区空通道,再回收整体为空的存储
                foreach (var pair in _keyedChannels)
                {
                    removed += pair.Value.TrimEmpty();
                    if (pair.Value.Count == 0)
                        emptyStoreKeys.Add(pair.Key);
                }

                for (int i = 0; i < emptyStoreKeys.Count; i++)
                    _keyedChannels.Remove(emptyStoreKeys[i]);
            }
            finally
            {
                ListPool<Type>.Return(emptyTypes);
                ListPool<(Type MessageType, Type KeyType)>.Return(emptyStoreKeys);
            }

            return removed;
        }

        #endregion

        #region IMessageBroker

        /// <summary>
        /// 释放全部通道与过滤器。
        /// <para>
        /// 遍历中无需屏蔽回收回调:回收只由「订阅数归零」触发(<see cref="EventStream{T}.Unsubscribe"/>),
        /// 而本方法走的是 Dispose 路径——事件流的 Dispose 刻意不回调 OnEmpty,
        /// 故此处不会出现「回调改字典」与「遍历字典」并发。
        /// </para>
        /// </summary>
        public void Clear()
        {
            foreach (var pair in _channels)
                pair.Value.DisposeAll();
            foreach (var pair in _keyedChannels)
                pair.Value.DisposeAll();

            _channels.Clear();
            _keyedChannels.Clear();
            _filtersByType.Clear();
            _filterPipelines.Clear();
        }

        #endregion

        #region Private Helpers

        private MessageChannel<TMessage> GetOrCreateChannel<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_channels.TryGetValue(type, out var channel))
            {
                channel = new MessageChannel<TMessage>(() => ReclaimChannelIfEmpty(type));
                _channels[type] = channel;
            }
            return (MessageChannel<TMessage>)channel;
        }

        private KeyedChannelStore<TKey, TMessage> GetOrCreateKeyedChannelStore<TKey, TMessage>()
        {
            var storeKey = (MessageType: typeof(TMessage), KeyType: typeof(TKey));
            if (!_keyedChannels.TryGetValue(storeKey, out var store))
            {
                store = new KeyedChannelStore<TKey, TMessage>(
                    key => ReclaimKeyedChannelIfEmpty<TKey, TMessage>(storeKey, key));
                _keyedChannels[storeKey] = store;
            }
            return (KeyedChannelStore<TKey, TMessage>)store;
        }

        /// <summary>
        /// 类型通道订阅清零时的回收:通道已空则从类型表摘除。
        /// <para>由事件流在锁外回调,此处只碰 broker 自有字典,不构成锁序反转。</para>
        /// </summary>
        private void ReclaimChannelIfEmpty(Type type)
        {
            if (_channels.TryGetValue(type, out var channel) && channel.IsReclaimable)
                _channels.Remove(type);
        }

        /// <summary>键值通道订阅清零时的回收:该 Key 已空则摘除,存储整体为空则连存储一并摘除。</summary>
        private void ReclaimKeyedChannelIfEmpty<TKey, TMessage>((Type MessageType, Type KeyType) storeKey, TKey key)
        {
            if (!_keyedChannels.TryGetValue(storeKey, out var storeObj))
                return;

            var store = (KeyedChannelStore<TKey, TMessage>)storeObj;
            if (store.TryGet(key, out var channel) && channel.IsReclaimable)
                store.Remove(key);

            if (store.Count == 0)
                _keyedChannels.Remove(storeKey);
        }

        #endregion
    }
}
