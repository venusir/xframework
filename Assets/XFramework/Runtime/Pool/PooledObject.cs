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
    /// </remarks>
    /// <example>
    /// <code>
    /// using (PoolManager.GetPooled<BulletData>(out var bullet))
    /// {
    ///     bullet.Position = transform.position;
    /// } // 自动 PoolManager.Return(bullet)
    /// </code>
    /// </example>
    public struct PooledObject<T> : IDisposable
    {
        private readonly IPool<T> _pool;

        /// <summary>
        /// 从池中取出的实例。
        /// </summary>
        public T Value { get; }

        internal PooledObject(IPool<T> pool, T value)
        {
            _pool = pool;
            Value = value;
        }

        /// <summary>
        /// 归还实例到池。using 块结束时自动调用。
        /// </summary>
        public void Dispose()
        {
            _pool?.Return(Value);
        }
    }
}