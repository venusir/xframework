using System;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 消息发布器。按类型推送消息。
    /// </summary>
    public interface IMessagePublisher
    {
        /// <summary>发布指定类型的消息。</summary>
        void Publish<TMessage>(TMessage message);

        /// <summary>发布带键值的消息。相同 Key 的消息在同一通道中传递。</summary>
        void Publish<TKey, TMessage>(TKey key, TMessage message);
    }

    /// <summary>
    /// 消息订阅器。按类型订阅消息。
    /// </summary>
    public interface IMessageSubscriber
    {
        /// <summary>订阅指定类型的消息。</summary>
        IReadonlySignal<TMessage> Subscribe<TMessage>();

        /// <summary>订阅指定类型的消息，并附加过滤条件。</summary>
        IReadonlySignal<TMessage> Subscribe<TMessage>(Predicate<TMessage> filter);

        /// <summary>订阅指定键值的消息。</summary>
        IReadonlySignal<TMessage> Subscribe<TKey, TMessage>(TKey key);

        /// <summary>异步订阅。消息到达时执行异步处理器。</summary>
        IReadonlySignal<TMessage> SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler);

        /// <summary>异步订阅，并附加过滤条件。</summary>
        IReadonlySignal<TMessage> SubscribeAsync<TMessage>(Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler);

        /// <summary>订阅带缓冲的消息。新订阅者会立即收到最近一次发布的消息。</summary>
        IReadonlySignal<TMessage> SubscribeBuffered<TMessage>();

        /// <summary>订阅带缓冲的键值消息。新订阅者会立即收到最近一次发布的消息。</summary>
        IReadonlySignal<TMessage> SubscribeBuffered<TKey, TMessage>(TKey key);
    }

    /// <summary>
    /// 消息代理。发布 + 订阅合一。
    /// </summary>
    public interface IMessageBroker : IMessagePublisher, IMessageSubscriber
    {
        /// <summary>清理所有订阅和缓存。</summary>
        void Clear();
    }
}
