using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XReactive.Internal;

namespace XFramework.XReactive
{
    /// <summary>
    /// 消息代理实现:消息分发层,订阅直接落到自研事件流并返回 IDisposable(无中间订阅源)。
    /// 支持普通消息、键值消息、异步消息、缓冲消息和消息过滤器。
    /// </summary>
    /// <remarks>
    /// GC 优化说明:
    /// - 过滤/异步等订阅配置以一次性闭包表达,仅在订阅时分配一次(非热路径)
    /// - ApplyFilters 使用预构建的 pipeline 缓存 + 无 LINQ 遍历,无过滤器时零分配
    /// - 键值消息使用两层字典结构,值类型 Key 无 boxing
    /// - buffered 通道在首次 Publish 时惰性创建 BufferedEventStream(每类型一次),之后零额外分配
    /// - 投递热路径(Publish/OnNext)除锁与池化快照外零分配
    /// </remarks>
    internal sealed class MessageBroker : IMessageBroker
    {
        #region Private Fields

        /// <summary>按消息类型缓存的事件流。</summary>
        private readonly Dictionary<Type, object> _streams = new();

        /// <summary>按消息类型 -> (TKey -> 事件流{TMessage}) 的两层字典，避免值类型 Key 的 boxing。</summary>
        private readonly Dictionary<Type, object> _keyedStreams = new();

        /// <summary>按消息类型缓存的 BufferedEventStream（缓冲 1 条）。</summary>
        private readonly Dictionary<Type, object> _bufferedStreams = new();

        /// <summary>按消息类型 -> (TKey -> BufferedEventStream{TMessage}) 的两层字典，避免值类型 Key 的 boxing。</summary>
        private readonly Dictionary<Type, object> _keyedBufferedStreams = new();

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

            // 推送给普通订阅者
            if (_streams.TryGetValue(type, out var sub))
                ((EventStream<TMessage>)sub).OnNext(message);

            // 推送给缓冲订阅者(GetOrAdd:确保订阅前发布的消息也被缓存,新订阅者可重放最近一条)
            GetOrAddBufferedStream<TMessage>().OnNext(message);
        }

        public void Publish<TKey, TMessage>(TKey key, TMessage message)
        {
            var type = typeof(TMessage);

            // 执行过滤器管道
            if (!ApplyFilters(type, message))
                return;

            // 推送给键值订阅者（使用两层字典，值类型 TKey 无 boxing）
            if (_keyedStreams.TryGetValue(type, out var innerObj))
            {
                var dict = (Dictionary<TKey, EventStream<TMessage>>)innerObj;
                if (dict.TryGetValue(key, out var stream))
                    stream.OnNext(message);
            }

            // 推送给键值缓冲订阅者(GetOrAdd:同上,订阅前发布的消息可重放)
            GetOrAddKeyedBufferedStream<TKey, TMessage>(key).OnNext(message);
        }

        #endregion

        #region Subscribe

        /// <summary>订阅指定类型的消息,直接落事件流并返回退订句柄。</summary>
        public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
            => GetOrAddStream<TMessage>().Subscribe(handler);

        /// <summary>订阅指定类型的消息,附加订阅级过滤条件(返回 false 的消息不投递给该订阅)。</summary>
        /// <remarks>
        /// 过滤与回调组合为订阅期一次性闭包:过滤条件抛异常时由事件流的异常隔离兜底
        /// (记 Error 日志、本条不投递、订阅保留、其他订阅者不受影响)。
        /// </remarks>
        public IDisposable Subscribe<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
            => GetOrAddStream<TMessage>().Subscribe(m =>
            {
                if (filter.Invoke(m)) handler(m);
            });

        /// <summary>订阅指定键值的消息。</summary>
        public IDisposable Subscribe<TKey, TMessage>(TKey key, Action<TMessage> handler)
            => GetOrAddKeyedStream<TKey, TMessage>(key).Subscribe(handler);

        /// <summary>订阅指定键值的消息,附加订阅级过滤条件。</summary>
        public IDisposable Subscribe<TKey, TMessage>(TKey key, Predicate<TMessage> filter, Action<TMessage> handler)
            => GetOrAddKeyedStream<TKey, TMessage>(key).Subscribe(m =>
            {
                if (filter.Invoke(m)) handler(m);
            });

        /// <summary>
        /// 异步订阅:消息到达时触发异步处理器(fire-and-forget)。
        /// <para>异步处理器经 Forget() 烧录进订阅回调,后续异常由 UniTask 的 Forget 语义兜底。</para>
        /// </summary>
        public IDisposable SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler)
            => GetOrAddStream<TMessage>().Subscribe(m => asyncHandler(m).Forget());

        /// <summary>异步订阅,附加订阅级过滤条件。</summary>
        public IDisposable SubscribeAsync<TMessage>(Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
            => GetOrAddStream<TMessage>().Subscribe(m =>
            {
                if (filter.Invoke(m)) asyncHandler(m).Forget();
            });

        /// <summary>订阅带缓冲的消息:新订阅者立即同步收到最近一次发布的消息(若有),之后转为实时。</summary>
        public IDisposable SubscribeBuffered<TMessage>(Action<TMessage> handler)
            => GetOrAddBufferedStream<TMessage>().Subscribe(handler);

        /// <summary>订阅带缓冲的消息,附加订阅级过滤条件(重放与实时共用同一过滤)。</summary>
        public IDisposable SubscribeBuffered<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
            => GetOrAddBufferedStream<TMessage>().Subscribe(m =>
            {
                if (filter.Invoke(m)) handler(m);
            });

        /// <summary>订阅带缓冲的键值消息。新订阅者立即同步收到最近一次发布的消息(若有)。</summary>
        public IDisposable SubscribeBuffered<TKey, TMessage>(TKey key, Action<TMessage> handler)
            => GetOrAddKeyedBufferedStream<TKey, TMessage>(key).Subscribe(handler);

        #endregion

        #region Filters

        /// <summary>注册全局消息过滤器。</summary>
        public void AddFilter<TMessage>(IMessageFilter<TMessage> filter)
        {
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
                Debug.LogException(e);
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

        #region IMessageBroker

        public void Clear()
        {
            DisposeAll(_streams);
            DisposeAllKeyed(_keyedStreams);
            DisposeAll(_bufferedStreams);
            DisposeAllKeyed(_keyedBufferedStreams);
            _streams.Clear();
            _keyedStreams.Clear();
            _bufferedStreams.Clear();
            _keyedBufferedStreams.Clear();
            _filtersByType.Clear();
            _filterPipelines.Clear();
        }

        #endregion

        #region Private Helpers

        private EventStream<TMessage> GetOrAddStream<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_streams.TryGetValue(type, out var sub))
            {
                sub = new EventStream<TMessage>();
                _streams[type] = sub;
            }
            return (EventStream<TMessage>)sub;
        }

        private EventStream<TMessage> GetOrAddKeyedStream<TKey, TMessage>(TKey key)
        {
            var type = typeof(TMessage);
            if (!_keyedStreams.TryGetValue(type, out var innerObj))
            {
                var dict = new Dictionary<TKey, EventStream<TMessage>>();
                _keyedStreams[type] = dict;
                var newStream = new EventStream<TMessage>();
                dict[key] = newStream;
                return newStream;
            }

            var innerDict = (Dictionary<TKey, EventStream<TMessage>>)innerObj;
            if (!innerDict.TryGetValue(key, out var found))
            {
                found = new EventStream<TMessage>();
                innerDict[key] = found;
            }
            return found;
        }

        private BufferedEventStream<TMessage> GetOrAddBufferedStream<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_bufferedStreams.TryGetValue(type, out var sub))
            {
                sub = new BufferedEventStream<TMessage>();
                _bufferedStreams[type] = sub;
            }
            return (BufferedEventStream<TMessage>)sub;
        }

        private BufferedEventStream<TMessage> GetOrAddKeyedBufferedStream<TKey, TMessage>(TKey key)
        {
            var type = typeof(TMessage);
            if (!_keyedBufferedStreams.TryGetValue(type, out var innerObj))
            {
                var dict = new Dictionary<TKey, BufferedEventStream<TMessage>>();
                _keyedBufferedStreams[type] = dict;
                var newStream = new BufferedEventStream<TMessage>();
                dict[key] = newStream;
                return newStream;
            }

            var innerDict = (Dictionary<TKey, BufferedEventStream<TMessage>>)innerObj;
            if (!innerDict.TryGetValue(key, out var found))
            {
                found = new BufferedEventStream<TMessage>();
                innerDict[key] = found;
            }
            return found;
        }

        private static void DisposeAll(IEnumerable<KeyValuePair<Type, object>> dict)
        {
            foreach (var kv in dict)
            {
                if (kv.Value is IDisposable d)
                    d.Dispose();
            }
        }

        private static void DisposeAllKeyed(IEnumerable<KeyValuePair<Type, object>> dict)
        {
            foreach (var kv in dict)
            {
                if (kv.Value is not IDictionary inner)
                    continue;

                foreach (var value in inner.Values)
                {
                    if (value is IDisposable d)
                        d.Dispose();
                }
            }
        }

        #endregion
    }
}
