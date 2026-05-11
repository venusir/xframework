using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using XFramework.XCore;

namespace XFramework.XReactive
{

    /// <summary>
    /// 全局消息总线。提供静态 API 和节点扩展方法两种使用方式。
    /// <para>静态 API：非节点类可通过 <see cref="MessageBus"/> 直接发布/订阅消息。</para>
    /// <para>扩展方法：节点实现 <see cref="IMessagePublisher"/> / <see cref="IMessageSubscriber"/> 后，
    /// 可通过 <c>this.Publish()</c> / <c>this.Subscribe()</c> 使用，订阅会自动绑定节点生命周期。</para>
    /// </summary>
    public static class MessageBus
    {
        #region Private Fields

        private static MessageBroker _broker = new MessageBroker();
        private static readonly Dictionary<Type, object> _requestHandlers = new();

        #endregion

        #region Static API

        /// <summary>发布指定类型的消息。</summary>
        public static void Publish<TMessage>(TMessage message) => _broker.Publish(message);

        /// <summary>发布带键值的消息。相同 Key 的消息在同一通道中传递。</summary>
        public static void Publish<TKey, TMessage>(TKey key, TMessage message) => _broker.Publish(key, message);

        /// <summary>订阅指定类型的消息。</summary>
        public static IDisposable Subscribe<TMessage>(Action<TMessage> handler)
            => _broker.Subscribe<TMessage>().Subscribe(handler);

        /// <summary>订阅指定类型的消息，并附加过滤条件。</summary>
        public static IDisposable Subscribe<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
            => _broker.Subscribe(filter).Subscribe(handler);

        /// <summary>订阅指定键值的消息。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(TKey key, Action<TMessage> handler)
            => _broker.Subscribe<TKey, TMessage>(key).Subscribe(handler);

        /// <summary>订阅指定键值的消息，并附加过滤条件。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(TKey key, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe<TKey, TMessage>(key).Subscribe(m =>
            {
                if (filter(m)) handler(m);
            });
            return disposable;
        }

        /// <summary>异步订阅。消息到达时执行异步处理器。</summary>
        public static IDisposable SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler)
        {
            var disposable = _broker.SubscribeAsync(asyncHandler).Subscribe(_ => { });
            return disposable;
        }

        /// <summary>异步订阅，并附加过滤条件。</summary>
        public static IDisposable SubscribeAsync<TMessage>(Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
        {
            var disposable = _broker.SubscribeAsync(filter, asyncHandler).Subscribe(_ => { });
            return disposable;
        }

        /// <summary>订阅带缓冲的消息。新订阅者会立即收到最近一次发布的消息。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(Action<TMessage> handler)
            => _broker.SubscribeBuffered<TMessage>().Subscribe(handler);

        /// <summary>订阅带缓冲的键值消息。新订阅者会立即收到最近一次发布的消息。</summary>
        public static IDisposable SubscribeBuffered<TKey, TMessage>(TKey key, Action<TMessage> handler)
            => _broker.SubscribeBuffered<TKey, TMessage>(key).Subscribe(handler);

        /// <summary>订阅带缓冲的消息，并附加过滤条件。新订阅者会立即收到最近一次发布的消息。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.SubscribeBuffered<TMessage>().Subscribe(m =>
            {
                if (filter(m)) handler(m);
            });
            return disposable;
        }

        /// <summary>注册全局消息过滤器。</summary>
        public static void AddFilter<TMessage>(IMessageFilter<TMessage> filter) => _broker.AddFilter(filter);

        /// <summary>注册请求处理器。一个请求类型只能注册一个处理器。</summary>
        /// <typeparam name="TRequest">请求类型。</typeparam>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="handler">异步处理器。</param>
        /// <exception cref="InvalidOperationException">同一请求类型重复注册时抛出。</exception>
        public static void Register<TRequest, TResponse>(Func<TRequest, UniTask<TResponse>> handler)
        {
            var type = typeof(TRequest);
            if (_requestHandlers.ContainsKey(type))
                throw new InvalidOperationException(
                    $"A handler for request type '{type.Name}' is already registered.");
            _requestHandlers[type] = handler;
        }

        /// <summary>发送请求并等待响应。</summary>
        /// <typeparam name="TRequest">请求类型。</typeparam>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="request">请求对象。</param>
        /// <returns>响应对象。</returns>
        /// <exception cref="InvalidOperationException">未注册对应的处理器时抛出。</exception>
        public static UniTask<TResponse> RequestAsync<TRequest, TResponse>(TRequest request)
        {
            if (_requestHandlers.TryGetValue(typeof(TRequest), out var handler))
            {
                return ((Func<TRequest, UniTask<TResponse>>)handler)(request);
            }
            throw new InvalidOperationException(
                $"No handler registered for request type '{typeof(TRequest).Name}'.");
        }

        /// <summary>清理所有订阅、缓存和请求处理器。</summary>
        public static void Clear()
        {
            _broker.Clear();
            _broker = new MessageBroker();
            _requestHandlers.Clear();
        }

        #endregion

        #region Extension Methods (for IMessagePublisher)

        /// <summary>发布指定类型的消息。</summary>
        public static void Publish<TMessage>(this IMessagePublisher publisher, TMessage message)
            => _broker.Publish(message);

        /// <summary>发布带键值的消息。相同 Key 的消息在同一通道中传递。</summary>
        public static void Publish<TKey, TMessage>(this IMessagePublisher publisher, TKey key, TMessage message)
            => _broker.Publish(key, message);

        #endregion

        #region Extension Methods (for IMessageSubscriber)

        /// <summary>订阅指定类型的消息。订阅会自动绑定到对象的生命周期，对象销毁时自动取消。</summary>
        public static IDisposable Subscribe<TMessage>(this IMessageSubscriber subscriber, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe<TMessage>().Subscribe(handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅指定类型的消息，并附加过滤条件。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable Subscribe<TMessage>(this IMessageSubscriber subscriber, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe(filter).Subscribe(handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅指定键值的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(this IMessageSubscriber subscriber, TKey key, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe<TKey, TMessage>(key).Subscribe(handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅指定键值的消息，并附加过滤条件。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(this IMessageSubscriber subscriber, TKey key, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe<TKey, TMessage>(key).Subscribe(m =>
            {
                if (filter(m)) handler(m);
            });
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>异步订阅。消息到达时执行异步处理器。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeAsync<TMessage>(this IMessageSubscriber subscriber, Func<TMessage, UniTask> asyncHandler)
        {
            var disposable = _broker.SubscribeAsync(asyncHandler).Subscribe(_ => { });
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>异步订阅，并附加过滤条件。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeAsync<TMessage>(this IMessageSubscriber subscriber, Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
        {
            var disposable = _broker.SubscribeAsync(filter, asyncHandler).Subscribe(_ => { });
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅带缓冲的消息。新订阅者会立即收到最近一次发布的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(this IMessageSubscriber subscriber, Action<TMessage> handler)
        {
            var disposable = _broker.SubscribeBuffered<TMessage>().Subscribe(handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅带缓冲的键值消息。新订阅者会立即收到最近一次发布的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeBuffered<TKey, TMessage>(this IMessageSubscriber subscriber, TKey key, Action<TMessage> handler)
        {
            var disposable = _broker.SubscribeBuffered<TKey, TMessage>(key).Subscribe(handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅带缓冲的消息，并附加过滤条件。新订阅者会立即收到最近一次发布的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(this IMessageSubscriber subscriber, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.SubscribeBuffered<TMessage>().Subscribe(m =>
            {
                if (filter(m)) handler(m);
            });
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 替换内部 Broker 实例（用于测试或自定义 Broker）。
        /// </summary>
        internal static void Replace(MessageBroker broker) => _broker = broker;

        /// <summary>
        /// 如果订阅者实现了 <see cref="IDestroyCancellationToken"/>，将订阅绑定到其生命周期，对象销毁时自动取消。
        /// <para>通过 <see cref="NodeExtensions.AddTo{T}(T, CancellationToken)"/> 实现，复用统一的绑定逻辑。</para>
        /// </summary>
        private static void TryBindToDestroy(object subscriber, IDisposable disposable)
        {
            if (subscriber is IDestroyCancellationToken provider)
            {
                disposable.AddTo(provider.DestroyCancellationToken);
            }
            else if (subscriber is MonoBehaviour mono)
            {
                disposable.AddTo(mono.destroyCancellationToken);
            }
        }
        #endregion
    }
}
