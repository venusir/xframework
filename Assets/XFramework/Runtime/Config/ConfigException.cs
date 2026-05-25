using System;

namespace XFramework.XConfig
{
    /// <summary>
    /// 配置模块专用异常，用于区分配置加载/查询错误与其他运行时异常。
    /// </summary>
    public class ConfigException : Exception
    {
        public ConfigException(string message) : base($"[Config] {message}")
        {
        }

        public ConfigException(string message, Exception innerException)
            : base($"[Config] {message}", innerException)
        {
        }
    }
}