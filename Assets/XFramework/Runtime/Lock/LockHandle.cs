using System;

namespace XFramework.XLock
{

    /// <summary>
    /// 锁句柄。通过 <see cref="ILockable.Acquire(int, object)"/> 或 <see cref="LockManager.AddLock"/> 获取，Dispose 时自动释放锁。
    /// <para>支持 <c>using</c> 语法，也支持手动 <see cref="Dispose"/>。</para>
    /// <para>零 GC 分配：直接存储锁的三要素，而非委托。</para>
    /// </summary>
    public readonly struct LockHandle : IDisposable
    {
        readonly ILockable _lockSubject;
        readonly int _lockType;
        readonly object _lockObj;
        readonly bool _active;

        /// <summary>
        /// 创建一个锁句柄。
        /// </summary>
        /// <param name="lockSubject">锁主体。</param>
        /// <param name="lockType">锁类型。</param>
        /// <param name="lockObj">锁对象。</param>
        internal LockHandle(ILockable lockSubject, int lockType, object lockObj)
        {
            _lockSubject = lockSubject;
            _lockType = lockType;
            _lockObj = lockObj;
            _active = true;
        }

        /// <summary>
        /// 锁句柄是否有效（即是否已成功获取锁）。
        /// </summary>
        public bool IsValid => _active;

        /// <summary>
        /// 释放锁。
        /// <para>多次调用是安全的，第二次及后续调用不会产生任何效果。</para>
        /// </summary>
        public void Dispose()
        {
            if (_active)
            {
                LockManager.RemoveLock(_lockSubject, _lockType, _lockObj);
            }
        }
    }
}