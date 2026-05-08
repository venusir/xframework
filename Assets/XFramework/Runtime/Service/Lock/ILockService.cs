using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务公共接口。与节点树无关，可供任何对象直接使用。
    /// <para>通过 <see cref="LockSystem"/> 静态入口获取全局实例，或自行 <c>new LockService()</c> 创建独立实例。</para>
    /// <para>锁由三要素组成：锁对象（lockSubject，null 表示全局）、锁类型（lockType）、锁本身（lock，不能为 null）。</para>
    /// </summary>
    public interface ILockService : IDisposable
    {
        #region Acquire

        /// <summary>
        /// 请求一个全局锁（不针对特定 lockSubject）。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放，也可手动调用 <see cref="Release(int, object)"/>。</para>
        /// </summary>
        /// <param name="lockType">锁类型标识，由游戏工程自行定义。</param>
        /// <param name="lock">锁本身，不能为 null。</param>
        /// <returns>锁句柄，Dispose 时自动释放锁。</returns>
        LockHandle Acquire(int lockType, object lockObj);

        /// <summary>
        /// 请求一个针对特定 lockSubject 的锁。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放，也可手动调用 <see cref="Release(object, int, object)"/>。</para>
        /// </summary>
        /// <param name="lockSubject">锁作用的对象，null 表示全局。</param>
        /// <param name="lockType">锁类型标识，由游戏工程自行定义。</param>
        /// <param name="lock">锁本身，不能为 null。</param>
        /// <returns>锁句柄，Dispose 时自动释放锁。</returns>
        LockHandle Acquire(object lockSubject, int lockType, object lockObj);

        #endregion

        #region Release

        /// <summary>
        /// 释放一个全局锁。
        /// <para>与 <see cref="Acquire(int, object)"/> 配对使用。</para>
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <param name="lock">锁本身。</param>
        void Release(int lockType, object lockObj);

        /// <summary>
        /// 释放一个针对特定 lockSubject 的锁。
        /// <para>与 <see cref="Acquire(object, int, object)"/> 配对使用。</para>
        /// </summary>
        /// <param name="lockSubject">锁作用的对象。</param>
        /// <param name="lockType">锁类型标识。</param>
        /// <param name="lock">锁本身。</param>
        void Release(object lockSubject, int lockType, object lockObj);

        #endregion

        #region Query

        /// <summary>
        /// 指定类型的锁是否处于激活状态（任意 lockSubject 下存在锁）。
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>如果存在任意锁则返回 true。</returns>
        bool IsLocked(int lockType);

        /// <summary>
        /// 指定 lockSubject 下该类型的锁是否处于激活状态。
        /// </summary>
        /// <param name="lockSubject">锁作用的对象。</param>
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>如果存在锁则返回 true。</returns>
        bool IsLocked(object lockSubject, int lockType);

        /// <summary>
        /// 获取指定类型锁的全局锁对象数量（所有 lockSubject 合计）。
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>当前锁对象数量。</returns>
        int GetLockCount(int lockType);

        /// <summary>
        /// 获取指定类型的所有锁对象副本（调试用）。
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>锁对象列表。</returns>
        IReadOnlyList<object> GetLockObjects(int lockType);

        #endregion

        #region Events

        /// <summary>
        /// 锁定事件：当某锁被添加时触发。
        /// <para>参数为 (lockSubject, lockType, lock)。</para>
        /// </summary>
        event Action<object, int, object> OnLocked;

        /// <summary>
        /// 解锁事件：当某锁被移除时触发。
        /// <para>参数为 (lockSubject, lockType, lock)。</para>
        /// </summary>
        event Action<object, int, object> OnUnlocked;

        #endregion
    }
}
