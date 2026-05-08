using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务独立实现。与节点树无关，可直接 <c>new LockService()</c> 创建使用。
    /// <para>提供按类型引用计数的锁管理能力，游戏工程自行定义锁类型（如 Update、Input、Display 等），通过 int 标识。</para>
    /// <para>通过 <see cref="LockSystem"/> 获取全局单例，或自行创建独立实例。</para>
    /// </summary>
    public class LockService : ILockService
    {
        #region Events

        public event Action<int, object> OnLocked;
        public event Action<int, object> OnUnlocked;

        #endregion

        #region Private Fields

        /// <summary>锁类型 → 引用计数。</summary>
        private readonly Dictionary<int, int> _locks = new Dictionary<int, int>();

        /// <summary>是否已释放。</summary>
        private bool _disposed;

        #endregion

        #region ILockService Implementation

        /// <summary>
        /// 请求一个锁，引用计数 +1。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放，也可手动调用 <see cref="Release"/>。</para>
        /// </summary>
        public LockHandle Acquire(int lockType, object owner)
        {
            bool wasLocked = _locks.ContainsKey(lockType);

            if (_locks.TryGetValue(lockType, out int count))
            {
                _locks[lockType] = count + 1;
            }
            else
            {
                _locks[lockType] = 1;
            }

            // 从"无锁"变为"有锁"时触发事件
            if (!wasLocked)
            {
                OnLocked?.Invoke(lockType, owner);
            }

            return new LockHandle(this, lockType, owner);
        }

        /// <summary>
        /// 释放一个锁，引用计数 -1。
        /// <para>与 <see cref="Acquire"/> 配对使用，适用于跨作用域的生命周期管理。</para>
        /// <para>如果引用计数归零，则从字典中移除该锁类型。</para>
        /// </summary>
        public void Release(int lockType, object owner)
        {
            if (_locks.TryGetValue(lockType, out int count))
            {
                if (count <= 1)
                {
                    _locks.Remove(lockType);
                    // 从"有锁"变为"无锁"时触发事件
                    OnUnlocked?.Invoke(lockType, owner);
                }
                else
                {
                    _locks[lockType] = count - 1;
                }
            }
        }

        /// <summary>
        /// 指定类型的锁是否处于激活状态（引用计数 > 0）。
        /// </summary>
        public bool IsLocked(int lockType)
        {
            return _locks.TryGetValue(lockType, out int count) && count > 0;
        }

        /// <summary>
        /// 获取指定类型锁的引用计数。
        /// </summary>
        public int GetLockCount(int lockType)
        {
            return _locks.TryGetValue(lockType, out int count) ? count : 0;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _locks.Clear();
            OnLocked = null;
            OnUnlocked = null;
        }

        #endregion
    }
}
