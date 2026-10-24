using System;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 配置扩展在加载 / 构造 / 注册阶段抛出的异常。
    /// <para>框架自身抛出的消息按约定带 <c>[LubanConfig]</c> 前缀；第三方/核心异常作为
    /// <see cref="Exception.InnerException"/> 保留原文，外层消息会拼接其可操作提示。</para>
    /// </summary>
    public sealed class LubanConfigException : Exception
    {
        /// <summary>
        /// 用消息构造异常。
        /// </summary>
        /// <param name="message">异常消息。</param>
        public LubanConfigException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 用消息与内部异常构造异常。
        /// </summary>
        /// <param name="message">异常消息。</param>
        /// <param name="innerException">内部异常。</param>
        public LubanConfigException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
