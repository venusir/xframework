using System;

namespace XFramework
{
    /// <summary>
    /// 锁句柄。通过 <see cref="ILockNode.Acquire"/> 或 <see cref="ILockService.Acquire"/> 获取，Dispose 时自动释放锁。
    /// <para>支持 <c>using</c> 语法，也支持手动 <see cref="Dispose"/>。</para>
    /// </summary>
    public readonly struct LockHandle : IDisposable
    {
        readonly ILockService _service;
        readonly int _lockType;
        readonly object _owner;

        internal LockHandle(ILockService service, int lockType, object owner)
        {
            _service = service;
            _lockType = lockType;
            _owner = owner;
        }

        /// <summary>
        /// 释放锁，引用计数 -1。
        /// <para>多次调用是安全的，第二次及后续调用不会产生任何效果。</para>
        /// </summary>
        public void Dispose()
        {
            _service?.Release(_lockType, _owner);
        }
    }

}
