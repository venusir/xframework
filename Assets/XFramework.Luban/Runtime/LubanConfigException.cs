using System;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 配置扩展在加载 / 构造 / 注册阶段抛出的异常。
    /// <para>消息统一带 <c>[LubanConfig]</c> 前缀；加载失败会保留原始异常为
    /// <see cref="Exception.InnerException"/>。</para>
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
