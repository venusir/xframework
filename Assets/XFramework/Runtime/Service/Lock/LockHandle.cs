using System;

namespace XFramework
{
    /// <summary>
    /// 锁句柄。通过 <see cref="ILockService.Acquire(int, object)"/> 或 <see cref="ILockService.Acquire(object, int, object)"/> 获取，Dispose 时自动释放锁。
    /// <para>支持 <c>using</c> 语法，也支持手动 <see cref="Dispose"/>。</para>
    /// </summary>
    public readonly struct LockHandle : IDisposable
    {
        readonly ILockService _service;
        readonly object _lockSubject;
        readonly int _lockType;
        readonly object _lockObj;

        internal LockHandle(ILockService service, object lockSubject, int lockType, object lockObj)
        {
            _service = service;
            _lockSubject = lockSubject;
            _lockType = lockType;
            _lockObj = lockObj;
        }

        /// <summary>
        /// 释放锁。
        /// <para>多次调用是安全的，第二次及后续调用不会产生任何效果。</para>
        /// </summary>
        public void Dispose()
        {
            if (_lockSubject != null)
            {
                _service?.Release(_lockSubject, _lockType, _lockObj);
            }
            else
            {
                _service?.Release(_lockType, _lockObj);
            }
        }
    }
}
