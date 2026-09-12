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
        readonly bool _acquired;

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
            _acquired = true;
        }

        /// <summary>
        /// 本句柄当前是否<b>仍持有</b>锁。
        /// <para><b>这是一次实时查询而非缓存字段</b>：本结构是不可变的值类型（<c>readonly struct</c> + readonly 字段，
        /// 出于零 GC 与值语义的考虑），<see cref="Dispose"/> 无法改写自身状态，故答案只能向
        /// <see cref="LockManager.IsLockedBy"/> 现取。</para>
        /// <para><b>与 <see cref="LockManager.IsLocked"/> 不同</b>：后者是聚合语义（该主体该类型下还有任一持有者即为 true），
        /// 本属性逐句柄精确——别的对象持锁不影响本句柄的答案。</para>
        /// <para><b>不存在「获取失败」的判断</b>：本框架的加锁不会失败（同一主体同类型的锁是持有者集合，可叠加），
        /// 任何经 <see cref="LockManager.AddLock"/> 得到的句柄都获取成功。本属性回答的是「我这把还在不在」。</para>
        /// </summary>
        public bool IsHeld => _acquired && LockManager.IsLockedBy(_lockSubject, _lockType, _lockObj);

        /// <summary>
        /// 释放锁。
        /// <para>多次调用是安全的，第二次及后续调用不会产生任何效果。</para>
        /// </summary>
        public void Dispose()
        {
            // _acquired 同时承担「默认句柄的 Dispose 必须安全」：default(LockHandle) 的 _lockObj 为 null，
            // 直接调 RemoveLock 会抛 ArgumentNullException
            if (_acquired)
            {
                LockManager.RemoveLock(_lockSubject, _lockType, _lockObj);
            }
        }
    }
}