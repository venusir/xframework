using System;

namespace XFramework.XData
{
    /// <summary>
    /// Data 模块专用异常，用于区分数据存取错误与其他运行时异常。
    /// </summary>
    public class DataException : InvalidOperationException
    {
        /// <summary>
        /// 用给定消息创建异常。
        /// </summary>
        /// <param name="message">异常消息（构造时会自动加上 <c>[Data]</c> 前缀）。</param>
        public DataException(string message) : base($"[Data] {message}")
        {
        }

        /// <summary>
        /// 用给定消息与内部异常创建异常。
        /// </summary>
        /// <param name="message">异常消息（构造时会自动加上 <c>[Data]</c> 前缀）。</param>
        /// <param name="innerException">触发本异常的内部异常。</param>
        public DataException(string message, Exception innerException)
            : base($"[Data] {message}", innerException)
        {
        }
    }
}