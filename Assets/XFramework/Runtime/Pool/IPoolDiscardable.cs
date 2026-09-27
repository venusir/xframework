namespace XFramework.XPool
{
    /// <summary>
    /// 实例被池丢弃时的回调（可选能力接口）。
    /// <para>池内闲置实例已达 <see cref="PoolConfig.MaxSize"/> 时，归还的实例不再入池而是交给 GC；
    /// 实现此接口可在此时释放需要确定性回收的资源（原生句柄、事件订阅、缓存引用等），
    /// 而不必等终结器或让调用方自己盯着池容量。</para>
    /// <para>与 <see cref="IPoolable"/> 分开是刻意的：只关心「被丢弃」的类型不必被迫实现出入池回调，
    /// 而且给已发布的接口加成员会打断第三方已有的实现（参考 <see cref="IPoolable"/> 的用法）。</para>
    /// <para>传入了 <c>onDestroy</c> 委托时本接口不触发——与 <see cref="IPoolable"/> 同为「委托优先」。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// public class NativeBuffer : IPoolDiscardable
    /// {
    ///     private IntPtr _handle;
    ///
    ///     void IPoolDiscardable.OnDiscard() => ReleaseHandle();
    /// }
    /// </code>
    /// </example>
    public interface IPoolDiscardable
    {
        /// <summary>
        /// 实例超出池容量被丢弃时调用。
        /// <para>此时实例已不在池的引用范围内，释放资源后即可被 GC 回收。</para>
        /// </summary>
        void OnDiscard();
    }
}
