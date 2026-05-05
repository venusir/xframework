using System;

namespace XFramework
{
    /// <summary>
    /// 事件节点。挂载在节点树下，提供树作用域的消息传递能力。
    /// <para>通过 <see cref="BaseNode.Get{T}"/> 沿父链向上查找，可实现分层消息隔离。</para>
    /// </summary>
    public interface IEventNode : IBaseNode, IMessagePublisher, IMessageSubscriber
    {
    }
}
