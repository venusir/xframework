using System;

namespace XFramework.XReactive

{
    /// <summary>
    /// 消息发布器标记接口。
    /// <para>节点实现此接口后，可通过 <see cref="MessageBus"/> 的扩展方法发布消息。</para>
    /// </summary>
    public interface IMessagePublisher
    {
    }

    /// <summary>
    /// 消息订阅器标记接口。
    /// <para>节点实现此接口后，可通过 <see cref="MessageBus"/> 的扩展方法订阅消息。</para>
    /// </summary>
    public interface IMessageSubscriber
    {
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
