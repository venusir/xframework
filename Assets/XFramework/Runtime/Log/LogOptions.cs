using UnityEngine;

namespace XFramework.XLog
{
    /// <summary>
    /// 日志配置。仅在调用 <c>LogManager.Configure</c> 时读取；之后改本对象不影响已生效的配置。
    /// <para><b>不配置也能用</b>：门面在没有任何配置时用 <see cref="Default"/> 构建默认实现——
    /// 日志不能有「必须先初始化」这道门槛。</para>
    /// </summary>
    public sealed class LogOptions
    {
        /// <summary>
        /// 构造一份默认配置。默认值随运行环境推导：<b>Editor / Development 构建</b>下
        /// 全局档位为 <see cref="LogLevel.Debug"/>、默认抓栈；<b>Release</b> 下为 <see cref="LogLevel.Info"/>、不抓栈。
        /// <para>Release 不得低于 <see cref="LogLevel.Info"/>：迁移前 18 处 <c>Debug.Log</c> 映射到 Info，
        /// 低于它就会静默改变现有可见性。</para>
        /// </summary>
        public LogOptions()
        {
            bool development = Application.isEditor || Debug.isDebugBuild;

            MinimumLevel = development ? LogLevel.Debug : LogLevel.Info;
            EnableConsoleSink = true;
            CaptureStackTrace = development;
            StackTraceMinLevel = LogLevel.Error;
        }

        /// <summary>
        /// 默认配置实例。<b>每次访问都返回新实例</b>——共享可变实例曾把别的模块的 <c>Default</c> 变成一个
        /// 「改一处静音全局」的陷阱，不重蹈。
        /// </summary>
        public static LogOptions Default => new LogOptions();

        /// <summary>全局最低档位。默认：Editor/Development 为 <see cref="LogLevel.Debug"/>，其余为 <see cref="LogLevel.Info"/>。</summary>
        public LogLevel MinimumLevel { get; set; }

        /// <summary>是否渲染到 Unity 控制台。默认 <c>true</c>——关掉它 <c>LogAssert</c> 就看不到任何东西。</summary>
        public bool EnableConsoleSink { get; set; }

        /// <summary>是否捕获调用点托管堆栈。默认：Editor/Development <c>true</c>，Release <c>false</c>（抓栈不便宜）。</summary>
        public bool CaptureStackTrace { get; set; }

        /// <summary>抓栈的最低级别。默认 <see cref="LogLevel.Error"/>——Warning 及以下默认不抓。</summary>
        public LogLevel StackTraceMinLevel { get; set; }
    }
}
