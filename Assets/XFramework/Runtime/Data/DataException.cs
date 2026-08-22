using System;

namespace XFramework.XData
{
    /// <summary>
    /// Data 模块专用异常，用于区分数据存取错误与其他运行时异常。
    /// </summary>
    public class DataException : InvalidOperationException
    {
        public DataException(string message) : base($"[Data] {message}")
        {
        }

        public DataException(string message, Exception innerException)
            : base($"[Data] {message}", innerException)
        {
        }
    }
}