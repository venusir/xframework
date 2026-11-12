using XFramework.XLog;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 本示例自己的日志分类。
    /// <para>第三方可以用 <see cref="LogCategory.Get(string)"/> 自建分类；框架内置分类从
    /// <c>LogCategories</c> 取。示例里所有日志都走这一处，便于统一过滤。</para>
    /// </summary>
    public static class IntegrationLogCategories
    {
        /// <summary>示例流程（启动、切屏、退出）。</summary>
        public static readonly LogCategory Flow = LogCategory.Get("Integration");
    }
}
