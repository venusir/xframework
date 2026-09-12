using System;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>发布调用次数(含键值发布与 PublishAsync,也含被全局过滤器拦截的)。</summary>
        private int _publishCount;

        /// <summary>按消息类型存储的过滤器列表。</summary>
        private readonly Dictionary<Type, List<object>> _filtersByType = new();

        /// <summary>预构建的过滤器 pipeline 缓存（在 AddFilter 时失效重建）。</summary>
        private readonly Dictionary<Type, Delegate> _filterPipelines = new();

        /// <summary>
        /// 空订阅句柄:令牌已取消等「无需登记」路径共用。
        /// <para>无资源可释放,共享实例安全;不得返回 <c>null</c>——调用方
        /// (<c>MessageManager.TryBindToDestroy</c> 等)会直接调 Dispose,不做判空。</para>
        /// </summary>
        private static readonly IDisposable EmptySubscription = ActionDisposable.Create(static () => { });

        #endregion

        #region Publish

        public void Publish<TMessage>(TMessage message)
        {
            Interlocked.Increment(ref _publishCount);

            var type = typeof(TMessage);

            // 执行过滤器管道（无过滤器时零分配）
            if (!ApplyFilters(type, message))
                return;

            var channel = GetOrCreateChannel<TMessage>();

            // 推送给普通订阅者(Sync 为 null 表示从未有人订阅,跳过)
            channel.Sync?.OnNext(message);

            // 推送给缓冲订阅者(GetOrCreate:确保订阅前发布的消息也被缓存,新订阅者可重放最近一条)
            channel.GetOrCreateBuffered().OnNext(message);

            // 异步处理器排在同步投递之后,fire-and-forget 启动
            DispatchAsyncFireAndForget(channel, message);
        }

        public void Publish<TKey, TMessage>(TKey key, TMessage message)
        {
            Interlocked.Increment(ref _publishCount);

            var type = typeof(TMessage);

            // 执行过滤器管道
            if (!ApplyFilters(type, message))
                return;

            var channel = GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key);

            // 推送给键值订阅者(Sync 为 null 表示该 Key 从未有人订阅,跳过)
            channel.Sync?.OnNext(message);

            // 推送给键值缓冲订阅者(GetOrCreate:同上,订阅前发布的消息可重放)
            channel.GetOrCreateBuffered().OnNext(message);

            // 异步处理器排在同步投递之后,fire-and-forget 启动
            DispatchAsyncFireAndForget(channel, message);
        }

        #endregion

        #region PublishAsync

        /// <summary>
        /// 异步发布:同步投递与缓冲写入先行完成,随后启动异步处理器并等待其全部完成。
        /// </summary>
        public UniTask PublishAsync<TMessage>(
            TMessage message, MessagePublishStrategy strategy, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _publishCount);

            var type = typeof(TMessage);

            if (!ApplyFilters(type, message))
                return UniTask.CompletedTask;

            var channel = GetOrCreateChannel<TMessage>();

            // 同步段与 Publish 完全同序:同步订阅者 -> 缓冲通道写入 -> 异步处理器
            channel.Sync?.OnNext(message);
            channel.GetOrCreateBuffered().OnNext(message);

            return DispatchAsyncAwaitable(channel, message, strategy, cancellationToken);
        }

        /// <summary>异步发布指定键值的消息。</summary>
        public UniTask PublishAsync<TKey, TMessage>(
            TKey key, TMessage message, MessagePublishStrategy strategy, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _publishCount);

            var type = typeof(TMessage);

            if (!ApplyFilters(type, message))
                return UniTask.CompletedTask;

            var channel = GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key);

            channel.Sync?.OnNext(message);
            channel.GetOrCreateBuffered().OnNext(message);

            return DispatchAsyncAwaitable(channel, message, strategy, cancellationToken);
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
        /// 异步订阅:消息到达时触发异步处理器。
        /// <para>处理器独立登记在通道的异步列表中,不占同步订阅链;同步 Publish 以 fire-and-forget 触发它,
        /// PublishAsync 则逐个 await。</para>
        /// </summary>
        public IDisposable SubscribeAsync<TMessage>(
            Func<TMessage, CancellationToken, UniTask> asyncHandler,
            CancellationToken cancellationToken = default)
        {
            if (asyncHandler == null) throw new ArgumentNullException(nameof(asyncHandler));

            // 令牌守卫必须留在各重载内、且在建通道之前,不能只放在 AddAsyncSubscription 里:
            // 实参会先于被调方法求值,若在 helper 内判令牌,GetOrCreateChannel<TMessage>() 已经建出了通道
            // (键值版连整条 _keyedChannels 表项),却永远没有订阅者来触发回收,只剩 TrimEmptyChannels 兜底。
            // 判空必须先于判令牌,否则 SubscribeAsync(null, 已取消令牌) 会吞掉 ArgumentNullException。
            if (cancellationToken.IsCancellationRequested) return EmptySubscription;

            return AddAsyncSubscription(
                GetOrCreateChannel<TMessage>(), null, asyncHandler, cancellationToken);
        }

        /// <summary>异步订阅,附加订阅级过滤条件。</summary>
        public IDisposable SubscribeAsync<TMessage>(
            Predicate<TMessage> filter,
            Func<TMessage, CancellationToken, UniTask> asyncHandler,
            CancellationToken cancellationToken = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (asyncHandler == null) throw new ArgumentNullException(nameof(asyncHandler));
            if (cancellationToken.IsCancellationRequested) return EmptySubscription;

            return AddAsyncSubscription(
                GetOrCreateChannel<TMessage>(), filter, asyncHandler, cancellationToken);
        }

        /// <summary>异步订阅指定键值的消息。</summary>
        public IDisposable SubscribeAsync<TKey, TMessage>(
            TKey key,
            Func<TMessage, CancellationToken, UniTask> asyncHandler,
            CancellationToken cancellationToken = default)
        {
            if (asyncHandler == null) throw new ArgumentNullException(nameof(asyncHandler));
            if (cancellationToken.IsCancellationRequested) return EmptySubscription;

            return AddAsyncSubscription(
                GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key),
                null, asyncHandler, cancellationToken);
        }

        /// <summary>异步订阅指定键值的消息,附加订阅级过滤条件。</summary>
        public IDisposable SubscribeAsync<TKey, TMessage>(
            TKey key,
            Predicate<TMessage> filter,
            Func<TMessage, CancellationToken, UniTask> asyncHandler,
            CancellationToken cancellationToken = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (asyncHandler == null) throw new ArgumentNullException(nameof(asyncHandler));
            if (cancellationToken.IsCancellationRequested) return EmptySubscription;

            return AddAsyncSubscription(
                GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key),
                filter, asyncHandler, cancellationToken);
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

        /// <summary>
        /// 订阅带缓冲的键值消息。新订阅者立即同步收到最近一次发布的消息(若有)。
        /// <para>该 Key 的重放缓存与实体生命周期绑定:实体销毁时须
        /// <see cref="EvictBufferedChannel{TKey, TMessage}(TKey)"/>,否则复用同一 Key 的新实体会重放到旧值。</para>
        /// </summary>
        public IDisposable SubscribeBuffered<TKey, TMessage>(TKey key, Action<TMessage> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key)
                .GetOrCreateBuffered().Subscribe(handler);
        }

        /// <summary>
        /// 订阅带缓冲的键值消息,附加订阅级过滤条件(重放与实时共用同一过滤)。
        /// <para>该 Key 的重放缓存与实体生命周期绑定:实体销毁时须
        /// <see cref="EvictBufferedChannel{TKey, TMessage}(TKey)"/>,否则复用同一 Key 的新实体会重放到旧值。</para>
        /// </summary>
        public IDisposable SubscribeBuffered<TKey, TMessage>(
            TKey key, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return GetOrCreateKeyedChannelStore<TKey, TMessage>().GetOrCreate(key)
                .GetOrCreateBuffered().Subscribe(m =>
                {
                    if (filter.Invoke(m)) handler(m);
                });
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

        /// <summary>
        /// 淘汰指定消息类型的类型级缓冲通道(丢弃其重放缓存),并顺带回收因此变空的通道。
        /// <para>返回是否存在该缓冲通道并已淘汰。</para>
        /// </summary>
        public bool EvictBufferedChannel<TMessage>()
        {
            if (!_channels.TryGetValue(typeof(TMessage), out var channel) || !channel.EvictBuffered())
                return false;

            // 只在淘汰确实发生后才回收:无条件回收会出现「返回 false 但状态已变」的诡异语义
            ReclaimChannelIfEmpty(typeof(TMessage));
            return true;
        }

        /// <summary>
        /// 淘汰指定键值的缓冲通道(丢弃其重放缓存),并顺带回收因此变空的通道与存储。
        /// <para>返回是否存在该缓冲通道并已淘汰。</para>
        /// </summary>
        public bool EvictBufferedChannel<TKey, TMessage>(TKey key)
        {
            var storeKey = (MessageType: typeof(TMessage), KeyType: typeof(TKey));
            if (!_keyedChannels.TryGetValue(storeKey, out var storeObj))
                return false;

            var store = (KeyedChannelStore<TKey, TMessage>)storeObj;
            if (!store.TryGet(key, out var channel) || !channel.EvictBuffered())
                return false;

            // TMessage 无法从参数推断(storeKey 只是两个 Type 值),故显式给出类型实参
            ReclaimKeyedChannelIfEmpty<TKey, TMessage>(storeKey, key);
            return true;
        }

        /// <summary>
        /// 淘汰指定消息类型的全部缓冲通道(类型级 + 该消息类型下所有 Key 类型的所有 Key),
        /// 并顺带回收因此变空的通道。
        /// <para>返回淘汰的缓冲通道数量,不含顺带回收的通道数。</para>
        /// </summary>
        public int EvictBufferedChannels<TMessage>()
        {
            var removed = 0;

            if (_channels.TryGetValue(typeof(TMessage), out var channel) && channel.EvictBuffered())
            {
                removed++;
                ReclaimChannelIfEmpty(typeof(TMessage));
            }

            // Key 类型不是本方法的类型参数,故按消息类型扫全表。
            // 淘汰与回收只改各存储的内层字典;外层表项的摘除必须推迟到遍历结束之后,
            // 否则就是在遍历中改 _keyedChannels。
            var emptyStoreKeys = ListPool<(Type MessageType, Type KeyType)>.Rent();
            try
            {
                foreach (var pair in _keyedChannels)
                {
                    if (pair.Key.MessageType != typeof(TMessage))
                        continue;

                    removed += pair.Value.EvictBufferedAllAndReclaimEmpty();
                    if (pair.Value.Count == 0)
                        emptyStoreKeys.Add(pair.Key);
                }

                for (int i = 0; i < emptyStoreKeys.Count; i++)
                    _keyedChannels.Remove(emptyStoreKeys[i]);
            }
            finally
            {
                ListPool<(Type MessageType, Type KeyType)>.Return(emptyStoreKeys);
            }

            return removed;
        }

        /// <summary>
        /// 淘汰指定 Key 在<b>所有消息类型</b>下的缓冲通道,并顺带回收因此变空的通道与存储。
        /// <para>用途是把淘汰纪律绑到实体的生命周期上:实体销毁处一次调用即覆盖它参与过的全部
        /// 消息类型,无需按消息类型逐个淘汰——键基数高的场景下漏掉一个类型,就是一处静默的
        /// 陈旧值重放(见模块 README「带 Key 的淘汰是语义要求」)。</para>
        /// <para>返回淘汰的缓冲通道数量,不含顺带回收的通道数。</para>
        /// </summary>
        public int EvictBufferedChannels<TKey>(TKey key)
        {
            var removed = 0;

            // 消息类型不是本方法的类型参数,故按 Key 类型扫全表。
            // 淘汰只改各存储的内层字典;外层表项的摘除必须推迟到遍历结束之后,
            // 否则就是在遍历中改 _keyedChannels。
            var emptyStoreKeys = ListPool<(Type MessageType, Type KeyType)>.Rent();
            try
            {
                foreach (var pair in _keyedChannels)
                {
                    if (pair.Key.KeyType != typeof(TKey))
                        continue;

                    if (pair.Value.EvictBufferedByKey(key))
                        removed++;

                    if (pair.Value.Count == 0)
                        emptyStoreKeys.Add(pair.Key);
                }

                for (int i = 0; i < emptyStoreKeys.Count; i++)
                    _keyedChannels.Remove(emptyStoreKeys[i]);
            }
            finally
            {
                ListPool<(Type MessageType, Type KeyType)>.Return(emptyStoreKeys);
            }

            return removed;
        }

        /// <summary>
        /// 回收所有无可重放缓存且无订阅者的空通道,返回回收的通道数量。
        /// <para>不触碰缓冲通道——持有重放缓存的通道不受本方法影响,只能经
        /// <see cref="EvictBufferedChannel{TMessage}()"/> 系列显式淘汰;而淘汰本身已顺带回收因此变空的通道,
        /// 故本方法属兜底与诊断手段,常规路径下返回 0。</para>
        /// <para><b>计数口径</b>:只计被回收的通道数。某键值存储整体清空而被一并摘除时,
        /// 该存储表项不计入返回值——结构清理一律不计入返回值,与
        /// <see cref="EvictBufferedChannels{TMessage}()"/> 只计淘汰数的取向一致。</para>
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
            _publishCount = 0;
        }

        #endregion

        #region Stats

        /// <summary>
        /// 组装只读统计快照。
        /// <para>请求-响应状态由静态门面持有(见 <see cref="MessageManager"/>),故由调用方传入。</para>
        /// </summary>
        internal MessageBusStats GetStats(int requestHandlerCount, int requestCount)
        {
            var acc = new MessageStatsAccumulator();

            foreach (var pair in _channels)
                pair.Value.Accumulate(ref acc);

            foreach (var pair in _keyedChannels)
                pair.Value.Accumulate(ref acc);

            var filterCount = 0;
            foreach (var pair in _filtersByType)
                filterCount += pair.Value.Count;

            return new MessageBusStats(
                channelStoreCount: _channels.Count + _keyedChannels.Count,
                channelCount: acc.ChannelCount,
                syncSubscriptionCount: acc.SyncSubscriptionCount,
                asyncSubscriptionCount: acc.AsyncSubscriptionCount,
                bufferedChannelCount: acc.BufferedChannelCount,
                publishCount: Volatile.Read(ref _publishCount),
                requestCount: requestCount,
                requestHandlerCount: requestHandlerCount,
                filterCount: filterCount);
        }

        /// <summary>获取指定消息类型的通道统计;不存在时返回全 0 快照。</summary>
        internal MessageChannelStats GetChannelStats<TMessage>()
        {
            var type = typeof(TMessage);
            var acc = new MessageStatsAccumulator();

            if (_channels.TryGetValue(type, out var channel))
                channel.Accumulate(ref acc);

            // Key 类型不是本方法的类型参数,故按消息类型汇总键值通道数
            var keyedChannelCount = 0;
            foreach (var pair in _keyedChannels)
            {
                if (pair.Key.MessageType == type)
                    keyedChannelCount += pair.Value.Count;
            }

            return new MessageChannelStats(
                acc.SyncSubscriptionCount, acc.AsyncSubscriptionCount,
                acc.BufferedChannelCount > 0, keyedChannelCount);
        }

        /// <summary>获取指定键值通道的统计;通道不存在时返回全 0 快照。</summary>
        internal MessageChannelStats GetChannelStats<TKey, TMessage>(TKey key)
        {
            if (!_keyedChannels.TryGetValue((typeof(TMessage), typeof(TKey)), out var storeObj))
                return default;

            var store = (KeyedChannelStore<TKey, TMessage>)storeObj;
            if (!store.TryGet(key, out var channel))
                return default;

            var acc = new MessageStatsAccumulator();
            channel.Accumulate(ref acc);

            return new MessageChannelStats(
                acc.SyncSubscriptionCount, acc.AsyncSubscriptionCount,
                acc.BufferedChannelCount > 0, 0);
        }

        #endregion

        #region Async Dispatch

        /// <summary>
        /// 登记一条异步订阅;令牌已取消时返回空句柄(不登记,与 AddTo 语义一致)。
        /// <para>本守卫是纵深防御:各 <c>SubscribeAsync</c> 重载已先行判过令牌(必须在建通道之前,
        /// 因为实参会先于本方法求值)。此处再判一次只为兜住「重载判定之后、本方法执行之前被取消」
        /// 的竞态——那种情况下通道已建出,留下的至少是可被 <c>TrimEmptyChannels</c> 回收的空壳。</para>
        /// </summary>
        private static IDisposable AddAsyncSubscription<TMessage>(
            MessageChannel<TMessage> channel,
            Predicate<TMessage> filter,
            Func<TMessage, CancellationToken, UniTask> asyncHandler,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return EmptySubscription;

            return channel.AddAsync(filter, asyncHandler, cancellationToken);
        }

        /// <summary>
        /// 同步派发路径的异步处理器:启动后即放手(fire-and-forget)。
        /// <para>处理器同步抛出的异常就地兜底;异步段的异常交给 UniTask 的 Forget 语义。</para>
        /// </summary>
        private static void DispatchAsyncFireAndForget<TMessage>(MessageChannel<TMessage> channel, TMessage message)
        {
            var handlers = channel.Async;
            if (handlers == null || handlers.Count == 0)
                return;

            var targets = ListPool<AsyncSubscription<TMessage>>.Rent();
            try
            {
                CollectAsyncTargets(handlers, message, targets);

                for (int i = 0; i < targets.Count; i++)
                {
                    var subscription = targets[i];

                    // 与同步路径一致:快照收集后若已被退订(前一个处理器退掉了它)则跳过
                    if (subscription.IsDisposed)
                        continue;

                    try
                    {
                        subscription.Handler(message, subscription.Token).Forget();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Message] Async handler threw exception: {e}");
                    }
                }
            }
            finally
            {
                ListPool<AsyncSubscription<TMessage>>.Return(targets);
            }
        }

        /// <summary>
        /// 启动并等待异步处理器,按 <paramref name="strategy"/> 决定并行或顺序。
        /// <para>无异步订阅时返回已完成任务,调用方 await 不会产生额外等待。</para>
        /// </summary>
        private static UniTask DispatchAsyncAwaitable<TMessage>(
            MessageChannel<TMessage> channel,
            TMessage message,
            MessagePublishStrategy strategy,
            CancellationToken cancellationToken)
        {
            var handlers = channel.Async;
            if (handlers == null || handlers.Count == 0)
                return UniTask.CompletedTask;

            return strategy == MessagePublishStrategy.Sequential
                ? DispatchSequentialAsync(handlers, message, cancellationToken)
                : DispatchParallelAsync(handlers, message, cancellationToken);
        }

        /// <summary>并行:全部处理器先启动,再统一等待。</summary>
        private static UniTask DispatchParallelAsync<TMessage>(
            List<AsyncSubscription<TMessage>> handlers, TMessage message, CancellationToken cancellationToken)
        {
            var targets = ListPool<AsyncSubscription<TMessage>>.Rent();
            try
            {
                CollectAsyncTargets(handlers, message, targets);
                if (targets.Count == 0)
                    return UniTask.CompletedTask;

                // 逐元素调用即逐个启动(异步方法同步执行到首个未完成的 await);
                // 任务数组是唯一分配,且元素经 InvokeGuardedAsync 包裹后不会 fault
                var tasks = new UniTask[targets.Count];
                for (int i = 0; i < targets.Count; i++)
                {
                    // 与同步路径一致:启动过程中前一个处理器可能退订了后面的订阅
                    var subscription = targets[i];
                    tasks[i] = subscription.IsDisposed
                        ? UniTask.CompletedTask
                        : InvokeGuardedAsync(subscription, message);
                }

                var whenAll = UniTask.WhenAll(tasks);
                return cancellationToken.CanBeCanceled
                    ? whenAll.AttachExternalCancellation(cancellationToken)
                    : whenAll;
            }
            finally
            {
                // 池化列表只用于收集,不跨 await 持有
                ListPool<AsyncSubscription<TMessage>>.Return(targets);
            }
        }

        /// <summary>
        /// 顺序:按订阅先后逐个 await。
        /// <para>池化快照会跨 await 持有——快照内容必须在每次 await 后仍然可用,
        /// 故只能在方法结束时归还(未 await 就被丢弃的返回值会暂缓归还,影响有界)。</para>
        /// </summary>
        private static async UniTask DispatchSequentialAsync<TMessage>(
            List<AsyncSubscription<TMessage>> handlers, TMessage message, CancellationToken cancellationToken)
        {
            var targets = ListPool<AsyncSubscription<TMessage>>.Rent();
            try
            {
                CollectAsyncTargets(handlers, message, targets);

                for (int i = 0; i < targets.Count; i++)
                {
                    var subscription = targets[i];
                    if (subscription.IsDisposed)
                        continue;

                    await InvokeGuardedAsync(subscription, message);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                ListPool<AsyncSubscription<TMessage>>.Return(targets);
            }
        }

        /// <summary>
        /// 调用单个异步处理器并吞掉异常:异常就地记日志,不打断其余处理器、不抛给发布方。
        /// <para>仅当订阅自身已退订时,其 <see cref="OperationCanceledException"/> 静默丢弃。</para>
        /// </summary>
        private static async UniTask InvokeGuardedAsync<TMessage>(
            AsyncSubscription<TMessage> subscription, TMessage message)
        {
            try
            {
                await subscription.Handler(message, subscription.Token);
            }
            catch (OperationCanceledException) when (subscription.Token.IsCancellationRequested)
            {
                // 订阅已退订(或外部令牌取消):在途处理器随之结束,属预期
            }
            catch (Exception e)
            {
                Debug.LogError($"[Message] Async handler threw exception: {e}");
            }
        }

        /// <summary>
        /// 收集本轮应投递的异步订阅到池化快照(派发中退订不影响本轮)。
        /// <para>过滤条件抛异常时记日志并跳过该订阅,与同步路径的订阅级过滤语义一致。</para>
        /// </summary>
        private static void CollectAsyncTargets<TMessage>(
            List<AsyncSubscription<TMessage>> handlers,
            TMessage message,
            List<AsyncSubscription<TMessage>> targets)
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                var subscription = handlers[i];
                if (subscription.IsDisposed)
                    continue;

                var filter = subscription.Filter;
                if (filter != null)
                {
                    bool passed;
                    try
                    {
                        passed = filter.Invoke(message);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Message] Async subscription filter threw exception: {e}");
                        continue;
                    }

                    if (!passed)
                        continue;
                }

                targets.Add(subscription);
            }
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
