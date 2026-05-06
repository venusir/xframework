using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 基于 R3 的消息代理实现。支持普通消息、键值消息、异步消息、缓冲消息和消息过滤器。
    /// </summary>
    internal sealed class MessageBroker : IMessageBroker
    {
        #region Private Fields

        /// <summary>按消息类型缓存的 Subject。</summary>
        private readonly Dictionary<Type, object> _subjects = new();

        /// <summary>按 (消息类型, Key) 缓存的 Subject。</summary>
        private readonly Dictionary<(Type type, object key), object> _keyedSubjects = new();

        /// <summary>按消息类型缓存的 ReplaySubject（缓冲 1 条）。</summary>
        private readonly Dictionary<Type, object> _bufferedSubjects = new();

        /// <summary>按 (消息类型, Key) 缓存的 ReplaySubject（缓冲 1 条）。</summary>
        private readonly Dictionary<(Type type, object key), object> _keyedBufferedSubjects = new();

        /// <summary>全局消息过滤器列表。</summary>
        private readonly List<(Type type, object filter)> _globalFilters = new();

        #endregion

        #region IMessagePublisher

        public void Publish<TMessage>(TMessage message)
        {
            var type = typeof(TMessage);

            // 执行过滤器管道
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
            var lookup = (typeof(TMessage), (object)key);

            // 执行过滤器管道
            if (!ApplyFilters(typeof(TMessage), message))
                return;

            // 推送给键值订阅者
            if (_keyedSubjects.TryGetValue(lookup, out var sub))
                ((Subject<TMessage>)sub).OnNext(message);

            // 推送给键值缓冲订阅者
            if (_keyedBufferedSubjects.TryGetValue(lookup, out var bufSub))
                ((ReplaySubject<TMessage>)bufSub).OnNext(message);
        }

        #endregion

        #region IMessageSubscriber

        public IReadonlySignal<TMessage> Subscribe<TMessage>()
        {
            var subject = GetOrAddSubject<TMessage>();
            return new ObservableSignal<TMessage>(subject);
        }

        public IReadonlySignal<TMessage> Subscribe<TMessage>(Predicate<TMessage> filter)
        {
            var subject = GetOrAddSubject<TMessage>();
            return new ObservableSignal<TMessage>(subject.Where(m => filter(m)));
        }

        public IReadonlySignal<TMessage> Subscribe<TKey, TMessage>(TKey key)
        {
            var subject = GetOrAddKeyedSubject<TKey, TMessage>(key);
            return new ObservableSignal<TMessage>(subject);
        }

        public IReadonlySignal<TMessage> SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler)
        {
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
            var subject = GetOrAddSubject<TMessage>();
            return new ObservableSignal<TMessage>(
                subject.Where(m => filter(m)).Select(m =>
                {
                    asyncHandler(m).Forget();
                    return m;
                })
            );
        }

        public IReadonlySignal<TMessage> SubscribeBuffered<TMessage>()
        {
            var subject = GetOrAddBufferedSubject<TMessage>();
            return new ObservableSignal<TMessage>(subject);
        }

        public IReadonlySignal<TMessage> SubscribeBuffered<TKey, TMessage>(TKey key)
        {
            var subject = GetOrAddKeyedBufferedSubject<TKey, TMessage>(key);
            return new ObservableSignal<TMessage>(subject);
        }

        #endregion

        #region Filters

        /// <summary>注册全局消息过滤器。</summary>
        public void AddFilter<TMessage>(IMessageFilter<TMessage> filter)
        {
            _globalFilters.Add((typeof(TMessage), filter));
        }

        /// <summary>应用过滤器管道。返回 false 表示消息被拦截。</summary>
        private bool ApplyFilters<TMessage>(Type type, TMessage message)
        {
            // 收集该类型的所有过滤器
            var filters = _globalFilters
                .Where(f => f.type == type)
                .Select(f => f.filter)
                .Cast<IMessageFilter<TMessage>>()
                .ToList();

            if (filters.Count == 0)
                return true;

            // 构建过滤器管道（类似 ASP.NET Core Middleware）
            int index = 0;
            Action<TMessage> next = null;
            next = msg =>
            {
                if (index >= filters.Count) return;
                var filter = filters[index++];
                filter.Invoke(msg, next);
            };

            try
            {
                next(message);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        #endregion

        #region IMessageBroker

        public void Clear()
        {
            DisposeAll(_subjects);
            DisposeAll(_keyedSubjects);
            DisposeAll(_bufferedSubjects);
            DisposeAll(_keyedBufferedSubjects);
            _subjects.Clear();
            _keyedSubjects.Clear();
            _bufferedSubjects.Clear();
            _keyedBufferedSubjects.Clear();
            _globalFilters.Clear();
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
            var lookup = (typeof(TMessage), (object)key);
            if (!_keyedSubjects.TryGetValue(lookup, out var sub))
            {
                sub = new Subject<TMessage>();
                _keyedSubjects[lookup] = sub;
            }
            return (Subject<TMessage>)sub;
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
            var lookup = (typeof(TMessage), (object)key);
            if (!_keyedBufferedSubjects.TryGetValue(lookup, out var sub))
            {
                sub = new ReplaySubject<TMessage>(1);
                _keyedBufferedSubjects[lookup] = sub;
            }
            return (ReplaySubject<TMessage>)sub;
        }

        private static void DisposeAll(IEnumerable<KeyValuePair<Type, object>> dict)
        {
            foreach (var kv in dict)
            {
                if (kv.Value is IDisposable d)
                    d.Dispose();
            }
        }

        private static void DisposeAll(IEnumerable<KeyValuePair<(Type, object), object>> dict)
        {
            foreach (var kv in dict)
            {
                if (kv.Value is IDisposable d)
                    d.Dispose();
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
