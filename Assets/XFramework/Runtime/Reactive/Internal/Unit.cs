namespace XFramework.XReactive.Internal
{
    /// <summary>
    /// 零开销占位类型(零字段,无分配)。
    /// <para>用于无载荷事件流的泛型实参,如 InputManager 帧脉冲的 <c>Subject&lt;Unit&gt;</c>。</para>
    /// </summary>
    internal readonly struct Unit
    {
    }
}
