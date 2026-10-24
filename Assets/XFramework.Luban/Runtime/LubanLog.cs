using XFramework.XLog;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// 扩展包的日志分类。第三方自定义分类，不改核心 <c>LogCategories</c>。
    /// </summary>
    internal static class LubanLog
    {
        internal static readonly LogCategory Category = LogCategory.Get("Luban");
    }
}
