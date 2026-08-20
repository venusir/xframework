namespace XFramework.XReactive.Internal
{
    /// <summary>
    /// 无参数信号的占位类型(零字段,无分配)。
    /// <para>替代 R3.Unit(移除 R3 依赖计划 Phase 2):UniTask 仅有 AsyncUnit 语义不符,故自研零开销占位。</para>
    /// </summary>
    internal readonly struct Unit
    {
    }
}
