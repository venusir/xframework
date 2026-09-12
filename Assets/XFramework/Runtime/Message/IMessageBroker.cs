using System;

namespace XFramework.XMessage
{
    // 本文件同时承载两种可见性的契约（文件名沿用历史，不代表只有 IMessageBroker）：
    // - IMessagePublisher / IMessageSubscriber：对外公开，是 MessageManager 扩展方法的接收者类型；
    // - IMessageBroker：仅供内部与测试使用，理由见其 XML 注释。

    /// <summary>
    /// 消息发布器标记接口。
    /// <para>节点实现此接口后，可通过 <see cref="MessageManager"/> 的扩展方法发布消息。</para>
    /// </summary>
    public interface IMessagePublisher
    {
    }

    /// <summary>
    /// 消息订阅器标记接口。
    /// <para>节点实现此接口后，可通过 <see cref="MessageManager"/> 的扩展方法订阅消息。</para>
    /// </summary>
    public interface IMessageSubscriber
    {
    }

    /// <summary>
    /// 消息代理。发布 + 订阅合一。
    /// <para>
    /// <b>本接口为 <c>internal</c> 是刻意收敛，不是遗漏。</b>Message 属框架的「零配置纯静态服务」
    /// （与 <c>LockManager</c>/<c>UpdateManager</c> 同族），不提供对外的实现注入点——
    /// 门面 <see cref="MessageManager"/> 的实例替换只对测试开放（经 <c>InternalsVisibleTo</c>）。
    /// 对外声明为 <c>public</c> 只会得到一个既无法实现、也无处注入的契约。
    /// </para>
    /// <para>它对内的职责仅是给 <see cref="MessageBroker"/> 一个类型标识，并合并两个标记接口的视图。</para>
    /// </summary>
    internal interface IMessageBroker : IMessagePublisher, IMessageSubscriber
    {
        /// <summary>清理所有订阅和缓存。</summary>
        void Clear();
    }
}
