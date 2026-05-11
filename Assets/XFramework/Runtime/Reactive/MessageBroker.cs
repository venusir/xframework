using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace XFramework.XReactive
{
    /// <summary>
    /// 基于 R3 的消息代理实现。支持普通消息、键值消息、异步消息、缓冲消息和消息过滤器。
    /// </summary>
    /// <remarks>
    /// GC 优化说明:
    /// - ApplyFilters 使用预构建的 pipeline 缓存 + 无 LINQ 遍历，无过滤器时零分配
    /// - 键值消息使用两层字典结构，值类型 Key 无 boxing
    /// </remarks>
    internal sealed class MessageBroker : IMessageBroker
    {
        #region Private Fields

        /// <summary>按消息类型缓存的 Subject。</summary>
        private readonly Dictionary<Type, object> _subjects = new();

        /// <summary>按消息类型 -> (TKey -> Subject{TMessage}) 的两层字典，避免值类型 Key 的 boxing。</summary>
        private readonly Dictionary<Type, object> _keyedSubjects = new();

        /// <summary>按消息类型缓存的 ReplaySubject（缓冲 1 条）。</summary>
        private readonly Dictionary<Type, object> _bufferedSubjects = new();

        /// <summary>按消息类型 -> (TKey -> ReplaySubject{TMessage}) 的两层字典，避免值类型 Key 的 boxing。</summary>
        private readonly Dictionary<Type, object> _keyedBufferedSubjects = new();

        /// <summary>按消息类型存储的过滤器列表。</summary>
        private readonly Dictionary<Type, List<object>> _filtersByType = new();

        /// <summary>预构建的过滤器 pipeline 缓存（在 AddFilter 时失效重建）。</summary>
        private readonly Dictionary<Type, Delegate> _filterPipelines = new();

        /// <summary>按消息类型缓存的 ObservableSignal（避免每次 Subscribe 都 new）。</summary>
        private readonly Dictionary<Type, object> _signalCache = new();

        /// <summary>按 (消息类型 -> TKey -> ObservableSignal) 缓存的键值信号。</summary>
        private readonly Dictionary<Type, object> _keyedSignalCache = new();

        /// <summary>按消息类型缓存的缓冲 ObservableSignal。</summary>
        private readonly Dictionary<Type, object> _bufferedSignalCache = new();

        /// <summary>按 (消息类型 -> TKey -> ObservableSignal) 缓存的键值缓冲信号。</summary>
        private readonly Dictionary<Type, object> _keyedBufferedSignalCache = new();

        #endregion

        #region IMessagePublisher

        public void Publish<TMessage>(TMessage message)
        {
            var type = typeof(TMessage);

            // 执行过滤器管道（无过滤器时零分配）
            if (!ApplyFilters(type, message))
                return;

            // 推送给普通订阅者
            if (_subjects.TryGetValue(type, out var sub))
                ((Subject<TMessage>)sub).OnNext(message);

            // 推送给缓冲订阅者
            if (_bufferedSubjects.TryGetValue(type, out var bufSub))
                ((ReplaySubject<TMessage>)bufSub).OnNext(message);
        }

        public void Publish<TKey, TMessage>(TKey key, TMessage message)
        {
            var type = typeof(TMessage);

            // 执行过滤器管道
            if (!ApplyFilters(type, message))
                return;

            // 推送给键值订阅者（使用两层字典，值类型 TKey 无 boxing）
            if (_keyedSubjects.TryGetValue(type, out var innerObj))
            {
                var dict = (Dictionary<TKey, Subject<TMessage>>)innerObj;
                if (dict.TryGetValue(key, out var subject))
                    subject.OnNext(message);
            }

            // 推送给键值缓冲订阅者
            if (_keyedBufferedSubjects.TryGetValue(type, out var innerBufObj))
            {
                var dict = (Dictionary<TKey, ReplaySubject<TMessage>>)innerBufObj;
                if (dict.TryGetValue(key, out var subject))
                    subject.OnNext(message);
            }
        }

        #endregion

        #region IMessageSubscriber

        public IReadonlySignal<TMessage> Subscribe<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_signalCache.TryGetValue(type, out var cached))
            {
                cached = new ObservableSignal<TMessage>(GetOrAddSubject<TMessage>());
                _signalCache[type] = cached;
            }
            return (IReadonlySignal<TMessage>)cached;
        }

        public IReadonlySignal<TMessage> Subscribe<TMessage>(Predicate<TMessage> filter)
        {
            // 带 filter 的订阅无法缓存，每个 filter 是不同的 Observable 链
            // 使用 filter.Invoke 替代 lambda 包装，消除闭包分配（仅 1 个委托，无闭包）
            var subject = GetOrAddSubject<TMessage>();
            return new ObservableSignal<TMessage>(subject.Where(filter.Invoke));
        }

        public IReadonlySignal<TMessage> Subscribe<TKey, TMessage>(TKey key)
        {
            var type = typeof(TMessage);
            if (!_keyedSignalCache.TryGetValue(type, out var innerObj))
            {
                var dict = new Dictionary<TKey, IReadonlySignal<TMessage>>();
                _keyedSignalCache[type] = dict;
                var signal = new ObservableSignal<TMessage>(GetOrAddKeyedSubject<TKey, TMessage>(key));
                dict[key] = signal;
                return signal;
            }

            var innerDict = (Dictionary<TKey, IReadonlySignal<TMessage>>)innerObj;
            if (!innerDict.TryGetValue(key, out var cached))
            {
                cached = new ObservableSignal<TMessage>(GetOrAddKeyedSubject<TKey, TMessage>(key));
                innerDict[key] = cached;
            }
            return cached;
        }

        public IReadonlySignal<TMessage> SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler)
        {
            // 带 asyncHandler 的订阅无法缓存
            var subject = GetOrAddSubject<TMessage>();
            return new ObservableSignal<TMessage>(
                subject.Select(m =>
                {
                    asyncHandler(m).Forget();
                    return m;
                })
            );
        }

        public IReadonlySignal<TMessage> SubscribeAsync<TMessage>(Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
        {
            // 带 filter + asyncHandler 的订阅无法缓存
            // 使用 filter.Invoke 替代 lambda 包装，消除闭包分配
            var subject = GetOrAddSubject<TMessage>();
            return new ObservableSignal<TMessage>(
                subject.Where(filter.Invoke).Select(m =>
                {
                    asyncHandler(m).Forget();
                    return m;
                })
            );
        }

        public IReadonlySignal<TMessage> SubscribeBuffered<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_bufferedSignalCache.TryGetValue(type, out var cached))
            {
                cached = new ObservableSignal<TMessage>(GetOrAddBufferedSubject<TMessage>());
                _bufferedSignalCache[type] = cached;
            }
            return (IReadonlySignal<TMessage>)cached;
        }

        public IReadonlySignal<TMessage> SubscribeBuffered<TKey, TMessage>(TKey key)
        {
            var type = typeof(TMessage);
            if (!_keyedBufferedSignalCache.TryGetValue(type, out var innerObj))
            {
                var dict = new Dictionary<TKey, IReadonlySignal<TMessage>>();
                _keyedBufferedSignalCache[type] = dict;
                var signal = new ObservableSignal<TMessage>(GetOrAddKeyedBufferedSubject<TKey, TMessage>(key));
                dict[key] = signal;
                return signal;
            }

            var innerDict = (Dictionary<TKey, IReadonlySignal<TMessage>>)innerObj;
            if (!innerDict.TryGetValue(key, out var cached))
            {
                cached = new ObservableSignal<TMessage>(GetOrAddKeyedBufferedSubject<TKey, TMessage>(key));
                innerDict[key] = cached;
            }
            return cached;
        }

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
                ((Action<TMessage>)pipelineObj)(message);
                return true;
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
        /// </summary>
        private static Action<TMessage> BuildFilterPipeline<TMessage>(List<object> filterObjects)
        {
            var typedFilters = new IMessageFilter<TMessage>[filterObjects.Count];
            for (int i = 0; i < filterObjects.Count; i++)
                typedFilters[i] = (IMessageFilter<TMessage>)filterObjects[i];

            // 构建调用链：终端为 no-op
            Action<TMessage> pipeline = msg => { };
            for (int i = typedFilters.Length - 1; i >= 0; i--)
            {
                var filter = typedFilters[i];
                var next = pipeline;
                pipeline = msg => filter.Invoke(msg, next);
            }
            return pipeline;
        }

        #endregion

        #region IMessageBroker

        public void Clear()
        {
            DisposeAll(_subjects);
            DisposeAllKeyed(_keyedSubjects);
            DisposeAll(_bufferedSubjects);
            DisposeAllKeyed(_keyedBufferedSubjects);
            _subjects.Clear();
            _keyedSubjects.Clear();
            _bufferedSubjects.Clear();
            _keyedBufferedSubjects.Clear();
            _filtersByType.Clear();
            _filterPipelines.Clear();
            _signalCache.Clear();
            _keyedSignalCache.Clear();
            _bufferedSignalCache.Clear();
            _keyedBufferedSignalCache.Clear();
        }

        #endregion

        #region Private Helpers

        private Subject<TMessage> GetOrAddSubject<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_subjects.TryGetValue(type, out var sub))
            {
                sub = new Subject<TMessage>();
                _subjects[type] = sub;
            }
            return (Subject<TMessage>)sub;
        }

        private Subject<TMessage> GetOrAddKeyedSubject<TKey, TMessage>(TKey key)
        {
            var type = typeof(TMessage);
            if (!_keyedSubjects.TryGetValue(type, out var innerObj))
            {
                var dict = new Dictionary<TKey, Subject<TMessage>>();
                _keyedSubjects[type] = dict;
                var newSubject = new Subject<TMessage>();
                dict[key] = newSubject;
                return newSubject;
            }

            var innerDict = (Dictionary<TKey, Subject<TMessage>>)innerObj;
            if (!innerDict.TryGetValue(key, out var found))
            {
                found = new Subject<TMessage>();
                innerDict[key] = found;
            }
            return found;
        }

        private ReplaySubject<TMessage> GetOrAddBufferedSubject<TMessage>()
        {
            var type = typeof(TMessage);
            if (!_bufferedSubjects.TryGetValue(type, out var sub))
            {
                sub = new ReplaySubject<TMessage>(1);
                _bufferedSubjects[type] = sub;
            }
            return (ReplaySubject<TMessage>)sub;
        }

        private ReplaySubject<TMessage> GetOrAddKeyedBufferedSubject<TKey, TMessage>(TKey key)
        {
            var type = typeof(TMessage);
            if (!_keyedBufferedSubjects.TryGetValue(type, out var innerObj))
            {
                var dict = new Dictionary<TKey, ReplaySubject<TMessage>>();
                _keyedBufferedSubjects[type] = dict;
                var newSubject = new ReplaySubject<TMessage>(1);
                dict[key] = newSubject;
                return newSubject;
            }

            var innerDict = (Dictionary<TKey, ReplaySubject<TMessage>>)innerObj;
            if (!innerDict.TryGetValue(key, out var found))
            {
                found = new ReplaySubject<TMessage>(1);
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

        #region ObservableSignal Wrapper

        /// <summary>
        /// 将 R3 Observable 包装为 IReadonlySignal。
        /// </summary>
        private sealed class ObservableSignal<T> : IReadonlySignal<T>
        {
            private readonly Observable<T> _observable;

            public ObservableSignal(Observable<T> observable)
            {
                _observable = observable;
            }

            public IDisposable Subscribe(Action<T> onNext)
                => _observable.Subscribe(onNext);
        }

        #endregion
    }
}