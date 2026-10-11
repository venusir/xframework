using XFramework.XLog;

namespace XFramework.Example
{
    /// <summary>
    /// 示例自己的日志分类——**这是第三方接入的示范**。
    /// <para>框架内置分类在 <see cref="LogCategories"/>（`[模块]` 前缀的唯一真相）；使用方自己的分类用
    /// <see cref="LogCategory.Get"/> 注册，幂等且任意线程安全，命名建议与日志里那个 <c>[标签]</c> 一致
    /// ——分类名就是渲染出来的前缀。</para>
    /// <para>分类是按需注册的：第一次 <c>Get</c> 时建立，之后零成本；想单独调某个示例的日志音量，
    /// 用 <c>LogManager.SetCategoryLevel(SampleLogCategories.Coin, LogLevel.Warning)</c> 即可，
    /// 不影响别的分类。</para>
    /// </summary>
    internal static class SampleLogCategories
    {
        internal static readonly LogCategory Example = LogCategory.Get("Example");
        internal static readonly LogCategory Coin = LogCategory.Get("Coin");
        internal static readonly LogCategory Keyed = LogCategory.Get("Keyed");
        internal static readonly LogCategory Buffered = LogCategory.Get("Buffered");
        internal static readonly LogCategory Async = LogCategory.Get("Async");
        internal static readonly LogCategory Request = LogCategory.Get("Request");
        internal static readonly LogCategory Stats = LogCategory.Get("Stats");
        internal static readonly LogCategory Health = LogCategory.Get("Health");
        internal static readonly LogCategory Score = LogCategory.Get("Score");
        internal static readonly LogCategory UI = LogCategory.Get("UI");
        internal static readonly LogCategory UISample = LogCategory.Get("UISample");
    }
}
