using System;

namespace XFramework.XPool
{
    /// <summary>
    /// 池化对象的 using 包装器。在 using 块结束时自动归还实例到池。
    /// </summary>
    /// <typeparam name="T">池中存储的对象类型</typeparam>
    /// <remarks>
    /// <para>值类型（struct），栈分配，无 GC。</para>
    /// <para>实现 <see cref="IDisposable"/> 以支持 using 语法，编译后为 try-finally + Dispose()，不会装箱。</para>
    /// <para>default 构造的实例（_pool 为 null）调用 Dispose() 是安全的空操作。</para>
    /// <para><see cref="Dispose"/> 幂等，重复调用不会重复归还——但只对同一个存储位置成立，
    /// 见 <see cref="Dispose"/> 的说明。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using (PoolManager.GetPooled&lt;BulletData&gt;(out var bullet))
    /// {
    ///     bullet.Position = transform.position;
    /// } // 自动 PoolManager.Return(bullet)
    /// </code>
    /// </example>
    public struct PooledObject<T> : IDisposable where T : class
    {
        private readonly IPool<T> _pool;
        private bool _disposed;

        /// <summary>
        /// 从池中取出的实例。
        /// </summary>
        public T Value { get; }

        internal PooledObject(IPool<T> pool, T value)
        {
            _pool = pool;
            Value = value;
            _disposed = false;
        }

        /// <summary>
        /// 归还实例到池。using 块结束时自动调用。
        /// <para><b>幂等：</b>同一个包装器重复 <c>Dispose</c> 是安全空操作。这是值类型必须自己兜住的一环——
        /// 包装器每被赋值或传参就复制一份，各份都指向同一个实例，没有这道闸门时第二次归还会把同一实例
        /// 重复入栈（Release 下没有重复归还检测兜底），此后可能被两次 <c>Get</c> 同时拿到。</para>
        /// <para>只保护<b>同一个存储位置</b>：复制出去的副本（<c>var h2 = h;</c>，或经只读字段/属性取值时
        /// 产生的防御性副本）各有一份尚未置位的标志，分别 Dispose 仍会归还两次。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _pool?.Return(Value);
        }
    }
}