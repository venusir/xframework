using System;

namespace XFramework
{
    /// <summary>
    /// 消息过滤器。类似 ASP.NET Core Middleware，在消息传递时执行横切逻辑。
    /// <para>可用于日志记录、权限检查、数据校验等场景。</para>
    /// </summary>
    /// <typeparam name="TMessage">要过滤的消息类型。</typeparam>
    public interface IMessageFilter<TMessage>
    {
        /// <summary>
        /// 执行过滤逻辑。调用 <paramref name="next"/> 将消息传递给下一个过滤器或最终订阅者。
        /// </summary>
        /// <param name="message">消息内容。</param>
        /// <param name="next">下一个处理步骤。</param>
        void Invoke(TMessage message, Action<TMessage> next);
    }
}
