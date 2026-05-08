using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务接口。提供基于 lockSubject、lockType、lock 三要素的锁管理能力。
    /// <para>游戏工程自行定义锁类型（如 Update、Input、Display 等），通过 int 标识。</para>
    /// <para>通过 <see cref="BaseNode.Get{T}"/> 获取此服务：<c>this.Get{ILockNode}()</c></para>
    /// </summary>
    public interface ILockNode
    {
        #region Acquire

        /// <summary>
        /// 请求一个全局锁（不针对特定 lockSubject）。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// </summary>
        LockHandle Acquire(int lockType, object lockObj);

        /// <summary>
        /// 请求一个针对特定 lockSubject 的锁。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// </summary>
        LockHandle Acquire(object lockSubject, int lockType, object lockObj);

        #endregion

        #region Release

        /// <summary>
        /// 释放一个全局锁。
        /// </summary>
        void Release(int lockType, object lockObj);

        /// <summary>
        /// 释放一个针对特定 lockSubject 的锁。
        /// </summary>
        void Release(object lockSubject, int lockType, object lockObj);

        #endregion

        #region Query

        /// <summary>
        /// 指定类型的锁是否处于激活状态（任意 lockSubject 下存在锁）。
        /// </summary>
        bool IsLocked(int lockType);

        /// <summary>
        /// 指定 lockSubject 下该类型的锁是否处于激活状态。
        /// </summary>
        bool IsLocked(object lockSubject, int lockType);

        /// <summary>
        /// 获取指定类型锁的全局锁对象数量（所有 lockSubject 合计）。
        /// </summary>
        int GetLockCount(int lockType);

        /// <summary>
        /// 获取指定类型的所有锁对象副本（调试用）。
        /// </summary>
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
