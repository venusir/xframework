using XFramework.XLog;

namespace XFramework.XPool.Internal
{
    /// <summary>
    /// 泛型池的日志分类缓存：池的标签<b>带类型名</b>（<c>[Pool&lt;Foo&gt;]</c>），无法做成
    /// <see cref="LogCategories"/> 里的常量。
    /// <para>每个封闭泛型类型首次使用时注册一次，之后零成本——<c>LogCategory.Get</c> 幂等，
    /// 且静态字段只在首次访问时初始化；渲染出的 <c>[Pool&lt;Foo&gt;]</c> 与迁移前逐字一致，
    /// 因此既有 <c>LogAssert</c> 一条都不用改。</para>
    /// <para><b>与 <see cref="LogCategories"/> 的分工</b>：静态标签（<c>PoolManager</c>、
    /// <c>StringBuilderPool</c>）走内置分类表；带类型参数的走这里。</para>
    /// </summary>
    internal static class PoolLogCategory<T>
    {
        internal static readonly LogCategory Value = LogCategory.Get($"Pool<{typeof(T).Name}>");
    }

    /// <summary><c>ListPool&lt;T&gt;</c> 的分类缓存，语义同 <see cref="PoolLogCategory{T}"/>。</summary>
    internal static class ListPoolLogCategory<T>
    {
        internal static readonly LogCategory Value = LogCategory.Get($"ListPool<{typeof(T).Name}>");
    }

    /// <summary><c>HashSetPool&lt;T&gt;</c> 的分类缓存，语义同 <see cref="PoolLogCategory{T}"/>。</summary>
    internal static class HashSetPoolLogCategory<T>
    {
        internal static readonly LogCategory Value = LogCategory.Get($"HashSetPool<{typeof(T).Name}>");
    }

    /// <summary>
    /// <c>DictionaryPool&lt;TKey, TValue&gt;</c> 的分类缓存。
    /// <para>注意逗号后的空格——标签原文就是 <c>DictionaryPool&lt;K, V&gt;</c>，少一个空格就改了控制台文案。</para>
    /// </summary>
    internal static class DictionaryPoolLogCategory<TKey, TValue>
    {
        internal static readonly LogCategory Value =
            LogCategory.Get($"DictionaryPool<{typeof(TKey).Name}, {typeof(TValue).Name}>");
    }
}
