using System;
using System.Collections.Generic;

namespace XFramework.XLock

{
    /// <summary>
    /// <see cref="ILockable"/> 的扩展方法，提供全局锁服务的快捷访问。
    /// <para>实现 <see cref="ILockable"/> 的节点可直接通过 <c>this.Acquire(...)</c> 等语法调用全局锁服务，<c>this</c> 自动作为 lockSubject。</para>
    /// </summary>
    public static class LockableExtensions
    {
        #region Acquire

        /// <summary>
        /// 通过全局锁服务请求一个锁，<c>this</c> 自动作为 lockSubject。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// </summary>
        public static LockHandle Acquire(this ILockable self, int lockType, object lockObj)
            => LockService.Acquire(self, lockType, lockObj);

        #endregion

        #region Release

        /// <summary>
        /// 通过全局锁服务释放一个锁，<c>this</c> 自动作为 lockSubject。
        /// </summary>
        public static void Release(this ILockable self, int lockType, object lockObj)
            => LockService.Release(self, lockType, lockObj);

        #endregion

        #region Query

        /// <summary>
        /// 通过全局锁服务查询 <c>this</c> 下该类型的锁是否处于激活状态。
        /// </summary>
        public static bool IsLocked(this ILockable self, int lockType)
            => LockService.IsLocked(self, lockType);

        /// <summary>
        /// 通过全局锁服务获取 <c>this</c> 下该类型锁的对象数量。
        /// </summary>
        public static int GetLockCount(this ILockable self, int lockType)
            => LockService.GetLockCount(self, lockType);

        /// <summary>
        /// 通过全局锁服务获取 <c>this</c> 下该类型的所有锁对象副本（调试用）。
        /// </summary>
        public static IReadOnlyList<object> GetLockObjects(this ILockable self, int lockType)
            => LockService.GetLockObjects(self, lockType);

        #endregion

        #region Subject Event Subscription

        /// <summary>
        /// 订阅 <c>this</c> 的锁定事件。
        /// <para>全局锁（<see cref="LockService.Global"/>）的锁定也会触发此回调。</para>
        /// <para>返回 <see cref="IDisposable"/>，调用 <c>Dispose()</c> 可取消订阅。</para>
        /// </summary>
        public static IDisposable OnLocked(this ILockable self, Action<int> handler)
            => LockService.OnLocked(self, handler);

        /// <summary>
        /// 订阅 <c>this</c> 的解锁事件。
        /// <para>全局锁（<see cref="LockService.Global"/>）的解锁也会触发此回调。</para>
        /// <para>返回 <see cref="IDisposable"/>，调用 <c>Dispose()</c> 可取消订阅。</para>
        /// </summary>
        public static IDisposable OnUnlocked(this ILockable self, Action<int> handler)
            => LockService.OnUnlocked(self, handler);

        #endregion
    }
}
