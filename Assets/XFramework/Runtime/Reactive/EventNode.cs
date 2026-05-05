using System;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 事件节点。挂载在节点树下，提供树作用域的消息传递能力。
    /// <para>通过 <see cref="BaseNode.Get{T}"/> 沿父链向上查找 IEventNode 即可使用。</para>
    /// </summary>
    public class EventNode : LeafNode, IEventNode
    {
        #region Private Fields

        private MessageBroker _broker;

        #endregion

        #region Lifecycle Overrides

        protected override void OnAwake()
        {
            base.OnAwake();
            _broker = new MessageBroker();
        }

        protected override void OnDestroy()
        {
            _broker?.Clear();
            _broker = null;
            base.OnDestroy();
        }

        #endregion

        #region IMessagePublisher

        public void Publish<TMessage>(TMessage message)
            => _broker.Publish(message);

        public void Publish<TKey, TMessage>(TKey key, TMessage message)
            => _broker.Publish(key, message);

        #endregion

        #region IMessageSubscriber

        public IReadonlySignal<TMessage> Subscribe<TMessage>()
            => _broker.Subscribe<TMessage>();

        public IReadonlySignal<TMessage> Subscribe<TMessage>(Predicate<TMessage> filter)
            => _broker.Subscribe(filter);

        public IReadonlySignal<TMessage> Subscribe<TKey, TMessage>(TKey key)
            => _broker.Subscribe<TKey, TMessage>(key);

        public IReadonlySignal<TMessage> SubscribeAsync<TMessage>(Func<TMessage, UniTask> asyncHandler)
            => _broker.SubscribeAsync(asyncHandler);

        public IReadonlySignal<TMessage> SubscribeAsync<TMessage>(Predicate<TMessage> filter, Func<TMessage, UniTask> asyncHandler)
            => _broker.SubscribeAsync(filter, asyncHandler);

        public IReadonlySignal<TMessage> SubscribeBuffered<TMessage>()
            => _broker.SubscribeBuffered<TMessage>();

        public IReadonlySignal<TMessage> SubscribeBuffered<TKey, TMessage>(TKey key)
            => _broker.SubscribeBuffered<TKey, TMessage>(key);

        #endregion

        #region Filters

        /// <summary>注册消息过滤器。</summary>
        public void AddFilter<TMessage>(IMessageFilter<TMessage> filter)
            => _broker.AddFilter(filter);

        #endregion
    }
}
