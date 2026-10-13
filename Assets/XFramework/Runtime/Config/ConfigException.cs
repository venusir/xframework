using System;

namespace XFramework.XConfig
{
    /// <summary>
    /// 配置模块专用异常，用于区分配置加载/查询错误与其他运行时异常。
    /// </summary>
    public class ConfigException : Exception
    {
        /// <summary>
        /// 用给定消息创建异常。
        /// </summary>
        /// <param name="message">异常消息（构造时会自动加上 <c>[Config]</c> 前缀）。</param>
        public ConfigException(string message) : base($"[Config] {message}")
        {
        }

        /// <summary>
        /// 用给定消息与内部异常创建异常。
        /// </summary>
        /// <param name="message">异常消息（构造时会自动加上 <c>[Config]</c> 前缀）。</param>
        /// <param name="innerException">触发本异常的内部异常。</param>
        public ConfigException(string message, Exception innerException)
            : base($"[Config] {message}", innerException)
        {
        }
    }
}