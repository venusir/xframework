using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，提供按类型引用计数的锁管理能力。
    /// <para>游戏工程自行定义锁类型（如 Update、Input、Display 等），通过 int 标识。</para>
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// </summary>
    public class LockNode : LeafNode, ILockNode
    {
        #region Events

        public event Action<int, object> OnLocked;
        public event Action<int, object> OnUnlocked;

        #endregion

        #region Private Fields

        /// <summary>锁类型 → 引用计数。</summary>
        readonly Dictionary<int, int> _locks = new Dictionary<int, int>();

        #endregion

        #region ILockNode Implementation

        /// <summary>
        /// 请求一个锁，引用计数 +1。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放，也可手动调用 <see cref="Release"/>。</para>
        /// </summary>
        /// <param name="lockType">锁类型标识，由游戏工程自行定义。</param>
        /// <param name="owner">锁的持有者，用于调试和日志。</param>
        /// <returns>锁句柄，Dispose 时自动释放锁。</returns>
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
        /// <param name="lockType">锁类型标识。</param>
        /// <param name="owner">锁的持有者，仅用于日志记录。</param>
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
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>如果引用计数大于 0 则返回 true。</returns>
        public bool IsLocked(int lockType)
        {
            return _locks.TryGetValue(lockType, out int count) && count > 0;
        }

        /// <summary>
        /// 获取指定类型锁的引用计数。
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>当前引用计数，未锁定则返回 0。</returns>
        public int GetLockCount(int lockType)
        {
            return _locks.TryGetValue(lockType, out int count) ? count : 0;
        }

        #endregion
    }
}
