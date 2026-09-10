using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XMessage
{

    /// <summary>
    /// 全局消息总线。提供静态 API 和节点扩展方法两种使用方式。
    /// <para>静态 API：非节点类可通过 <see cref="MessageManager"/> 直接发布/订阅消息。</para>
    /// <para>扩展方法：节点实现 <see cref="IMessagePublisher"/> / <see cref="IMessageSubscriber"/> 后，
    /// 可通过 <c>this.Publish()</c> / <c>this.Subscribe()</c> 使用，订阅会自动绑定节点生命周期。</para>
    /// </summary>
    public static class MessageManager
    {
        #region Private Fields

        private static MessageBroker _broker = new MessageBroker();

        /// <summary>请求处理器表。键仅为 <c>typeof(TRequest)</c>,与响应类型无关。</summary>
        private static readonly Dictionary<Type, object> _requestHandlers = new();

        /// <summary>保护 <see cref="_requestHandlers"/> 的同步门:查找与增删在锁内,处理器调用在锁外。</summary>
        private static readonly object _requestGate = new();

        #endregion

        #region Static API

        /// <summary>发布指定类型的消息。</summary>
        public static void Publish<TMessage>(TMessage message) => _broker.Publish(message);

        /// <summary>发布带键值的消息。相同 Key 的消息在同一通道中传递。</summary>
        public static void Publish<TKey, TMessage>(TKey key, TMessage message) => _broker.Publish(key, message);

        /// <summary>订阅指定类型的消息。</summary>
        public static IDisposable Subscribe<TMessage>(Action<TMessage> handler)
            => _broker.Subscribe(handler);

        /// <summary>订阅指定类型的消息，并附加过滤条件。</summary>
        public static IDisposable Subscribe<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
            => _broker.Subscribe(filter, handler);

        /// <summary>订阅指定键值的消息。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(TKey key, Action<TMessage> handler)
            => _broker.Subscribe(key, handler);

        /// <summary>订阅指定键值的消息，并附加过滤条件。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(TKey key, Predicate<TMessage> filter, Action<TMessage> handler)
            => _broker.Subscribe(key, filter, handler);

        /// <summary>异步订阅。消息到达时执行异步处理器。</summary>
        public static IDisposable SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler)
            => _broker.SubscribeAsync(asyncHandler);

        /// <summary>异步订阅，并附加过滤条件。</summary>
        public static IDisposable SubscribeAsync<TMessage>(Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
            => _broker.SubscribeAsync(filter, asyncHandler);

        /// <summary>订阅带缓冲的消息。新订阅者会立即收到最近一次发布的消息。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(Action<TMessage> handler)
            => _broker.SubscribeBuffered(handler);

        /// <summary>订阅带缓冲的键值消息。新订阅者会立即收到最近一次发布的消息。</summary>
        public static IDisposable SubscribeBuffered<TKey, TMessage>(TKey key, Action<TMessage> handler)
            => _broker.SubscribeBuffered(key, handler);

        /// <summary>订阅带缓冲的消息，并附加过滤条件。新订阅者会立即收到最近一次发布的消息。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(Predicate<TMessage> filter, Action<TMessage> handler)
            => _broker.SubscribeBuffered(filter, handler);

        /// <summary>注册全局消息过滤器。</summary>
        /// <param name="filter">过滤器实例,不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="filter"/> 为 <c>null</c> 时抛出。</exception>
        public static void AddFilter<TMessage>(IMessageFilter<TMessage> filter) => _broker.AddFilter(filter);

        /// <summary>移除已注册的全局过滤器(同一实例重复注册时移除首个匹配项)。返回是否找到并移除。</summary>
        /// <param name="filter">要移除的过滤器实例,不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="filter"/> 为 <c>null</c> 时抛出。</exception>
        public static bool RemoveFilter<TMessage>(IMessageFilter<TMessage> filter) => _broker.RemoveFilter(filter);

        /// <summary>移除指定消息类型的全部全局过滤器,返回移除数量。</summary>
        public static int ClearFilters<TMessage>() => _broker.ClearFilters<TMessage>();

        /// <summary>
        /// 淘汰指定消息类型的类型级缓冲通道,丢弃其重放缓存。
        /// <para>用于按键/按实体高频发布后主动释放内存:缓冲通道会永久持有最后一条消息,
        /// 不再需要重放时应显式淘汰。</para>
        /// </summary>
        /// <returns>是否存在该缓冲通道并已淘汰。</returns>
        public static bool EvictBufferedChannel<TMessage>() => _broker.EvictBufferedChannel<TMessage>();

        /// <summary>
        /// 淘汰指定键值的缓冲通道,丢弃其重放缓存。
        /// <para>键基数高的场景(如按实体 Id 发布)应在实体的生命周期结束时调用本方法,
        /// 否则每个 Key 都会永久持有一条消息直至整表清理。</para>
        /// </summary>
        /// <returns>是否存在该缓冲通道并已淘汰。</returns>
        public static bool EvictBufferedChannel<TKey, TMessage>(TKey key)
            => _broker.EvictBufferedChannel<TKey, TMessage>(key);

        /// <summary>淘汰指定消息类型的全部缓冲通道(类型级 + 所有 Key),返回淘汰数量。</summary>
        public static int EvictBufferedChannels<TMessage>() => _broker.EvictBufferedChannels<TMessage>();

        /// <summary>
        /// 回收所有无订阅者且无可重放缓存的空通道,返回回收的通道数量。
        /// <para>订阅清零的通道已由事件流自动回收,本方法是兜底手段,用于批量清理历史遗留的空条目。</para>
        /// <para>持有重放缓存的缓冲通道不受影响——它们只能经 <see cref="EvictBufferedChannel{TMessage}()"/>
        /// 系列显式淘汰。</para>
        /// </summary>
        public static int TrimEmptyChannels() => _broker.TrimEmptyChannels();

        /// <summary>
        /// 注册请求处理器。一个请求类型只能注册一个处理器。
        /// </summary>
        /// <typeparam name="TRequest">请求类型。处理器表的键只取此类型,与响应类型无关。</typeparam>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="handler">
        /// 异步处理器。它收到的取消令牌即 <see cref="RequestAsync{TRequest, TResponse}"/> 调用方传入的令牌,
        /// 便于把取消继续传递给下游异步操作。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 <c>null</c> 时抛出。</exception>
        /// <exception cref="InvalidOperationException">同一请求类型重复注册时抛出。</exception>
        public static void Register<TRequest, TResponse>(Func<TRequest, CancellationToken, UniTask<TResponse>> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var type = typeof(TRequest);
            lock (_requestGate)
            {
                if (_requestHandlers.ContainsKey(type))
                    throw new InvalidOperationException(
                        $"[Message] 请求类型 '{type.Name}' 已注册处理器,同一请求类型只能注册一个;" +
                        $"如需替换请先调用 MessageManager.Unregister<{type.Name}, TResponse>()。");

                _requestHandlers[type] = handler;
            }
        }

        /// <summary>
        /// 移除已注册的请求处理器。返回是否找到并移除。
        /// <para>请求处理器表的键仅为请求类型,故 <typeparamref name="TResponse"/> 只用于与
        /// <see cref="Register{TRequest, TResponse}"/> 的调用形态对称。</para>
        /// </summary>
        /// <typeparam name="TRequest">请求类型。</typeparam>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        public static bool Unregister<TRequest, TResponse>()
        {
            lock (_requestGate)
            {
                return _requestHandlers.Remove(typeof(TRequest));
            }
        }

        /// <summary>发送请求并等待响应。</summary>
        /// <typeparam name="TRequest">请求类型。</typeparam>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="request">请求对象。</param>
        /// <param name="cancellationToken">
        /// 调用方令牌,原样转发给处理器(处理器据此把取消传递到下游异步操作);
        /// 取消不额外中断本方法的等待,是否响应取消由处理器决定。
        /// </param>
        /// <returns>响应对象。</returns>
        /// <exception cref="InvalidOperationException">未注册对应的处理器时抛出。</exception>
        public static UniTask<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request, CancellationToken cancellationToken = default)
        {
            Func<TRequest, CancellationToken, UniTask<TResponse>> handler;
            lock (_requestGate)
            {
                if (!_requestHandlers.TryGetValue(typeof(TRequest), out var stored))
                    throw new InvalidOperationException(
                        $"[Message] 未注册请求类型 '{typeof(TRequest).Name}' 的处理器;" +
                        $"请先调用 MessageManager.Register<{typeof(TRequest).Name}, TResponse>()。");

                handler = (Func<TRequest, CancellationToken, UniTask<TResponse>>)stored;
            }

            // 锁外调用:禁止持锁执行用户代码,也避免处理器中的 await 长期占用同步门
            return handler(request, cancellationToken);
        }

        /// <summary>清理所有订阅、缓存和请求处理器。</summary>
        public static void Clear()
        {
            _broker.Clear();
            // 换新实例并非冗余:上一句已把旧 broker 的订阅节点归还到静态池,
            // 此处只是丢弃旧容器,勿当作死代码清理。
            _broker = new MessageBroker();

            lock (_requestGate)
            {
                _requestHandlers.Clear();
            }
        }

        #endregion

        #region Auto Lifecycle

        /// <summary>
        /// 自动在游戏退出时清理消息总线，无需外部调用。
        /// <para>在 Editor 中 Domain Reload 或停止播放时也会触发清理。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        static void AutoInit()
        {
            Application.quitting += Clear;
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
            var disposable = _broker.Subscribe(handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅指定类型的消息，并附加过滤条件。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable Subscribe<TMessage>(this IMessageSubscriber subscriber, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe(filter, handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅指定键值的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(this IMessageSubscriber subscriber, TKey key, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe(key, handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅指定键值的消息，并附加过滤条件。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable Subscribe<TKey, TMessage>(this IMessageSubscriber subscriber, TKey key, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.Subscribe(key, filter, handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>异步订阅。消息到达时执行异步处理器。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeAsync<TMessage>(this IMessageSubscriber subscriber, Func<TMessage, UniTask> asyncHandler)
        {
            var disposable = _broker.SubscribeAsync(asyncHandler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>异步订阅，并附加过滤条件。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeAsync<TMessage>(this IMessageSubscriber subscriber, Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
        {
            var disposable = _broker.SubscribeAsync(filter, asyncHandler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅带缓冲的消息。新订阅者会立即收到最近一次发布的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(this IMessageSubscriber subscriber, Action<TMessage> handler)
        {
            var disposable = _broker.SubscribeBuffered(handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅带缓冲的键值消息。新订阅者会立即收到最近一次发布的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeBuffered<TKey, TMessage>(this IMessageSubscriber subscriber, TKey key, Action<TMessage> handler)
        {
            var disposable = _broker.SubscribeBuffered(key, handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        /// <summary>订阅带缓冲的消息，并附加过滤条件。新订阅者会立即收到最近一次发布的消息。订阅会自动绑定到对象的生命周期。</summary>
        public static IDisposable SubscribeBuffered<TMessage>(this IMessageSubscriber subscriber, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            var disposable = _broker.SubscribeBuffered(filter, handler);
            TryBindToDestroy(subscriber, disposable);
            return disposable;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 设置全新的 <see cref="MessageBroker"/> 实例，同时清理旧 Broker 的所有订阅和请求处理器。
        /// <para>适用于测试场景（如注入 mock broker）或需要在运行时彻底重置消息系统。</para>
        /// </summary>
        /// <param name="broker">新的 Broker 实例，不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="broker"/> 为 <c>null</c> 时抛出。</exception>
        internal static void SetInstance(MessageBroker broker)
        {
            if (broker == null) throw new ArgumentNullException(nameof(broker));
            _broker.Clear();
            _broker = broker;
            lock (_requestGate)
            {
                _requestHandlers.Clear();
            }
        }

        /// <summary>
        /// 将订阅绑定到订阅者的生命周期，对象销毁时自动取消。
        /// <para>如果订阅者是 <see cref="MonoBehaviour"/>，使用其 <see cref="MonoBehaviour.destroyCancellationToken"/>。</para>
        /// <para>其他生命周期类型（如节点树的 <c>IDestroyCancellationToken</c>）由 Core 层扩展方法负责桥接。</para>
        /// </summary>
        private static void TryBindToDestroy(object subscriber, IDisposable disposable)
        {
            if (subscriber is MonoBehaviour mono)
            {
                // 内联实现 AddTo(destroyCancellationToken):
                // 语义与 NodeExtensions.AddTo 一致:已取消则立即释放,否则注册到取消回调
                var token = mono.destroyCancellationToken;
                if (!token.CanBeCanceled || token.IsCancellationRequested)
                {
                    disposable.Dispose();
                }
                else
                {
                    token.Register(s => ((IDisposable)s).Dispose(), disposable);
                }
            }
        }
        #endregion
    }
}
