using System;

namespace XFramework
{
    /// <summary>
    /// 锁句柄。通过 <see cref="ILockable.Acquire(int, object)"/> 或 <see cref="ILockable.Acquire(object, int, object)"/> 获取，Dispose 时自动释放锁。
    /// <para>支持 <c>using</c> 语法，也支持手动 <see cref="Dispose"/>。</para>
    /// </summary>
    public readonly struct LockHandle : IDisposable
    {
        readonly Action _releaseAction;
        readonly bool _active;

        /// <summary>
        /// 创建一个锁句柄。
        /// </summary>
        /// <param name="releaseAction">释放锁时调用的委托。通常为 <c>() => service.Release(lockSubject, lockType, lockObj)</c>。</param>
        internal LockHandle(Action releaseAction)
        {
            _releaseAction = releaseAction;
            _active = true;
        }

        /// <summary>
        /// 释放锁。
        /// <para>多次调用是安全的，第二次及后续调用不会产生任何效果。</para>
        /// </summary>
        public void Dispose()
        {
            if (_active)
            {
                _releaseAction?.Invoke();
            }
        }
    }
}
