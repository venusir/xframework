using System;
using System.Collections.Generic;
using System.Linq;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务独立实现。与节点树无关，可直接 <c>new LockService()</c> 创建使用。
    /// <para>锁由三要素组成：锁对象（lockSubject，null 表示全局）、锁类型（lockType）、锁本身（lock，不能为 null）。</para>
    /// <para>通过 <see cref="LockSystem"/> 获取全局单例，或自行创建独立实例。</para>
    /// </summary>
    public class LockService : ILockService
    {
        #region Events

        public event Action<object, int, object> OnLocked;
        public event Action<object, int, object> OnUnlocked;

        #endregion

        #region Private Fields

        /// <summary>全局锁的哨兵 key，用于代替 null。</summary>
        private static readonly object GlobalSentinel = new object();

        /// <summary>lockSubject → lockType → HashSet<lock>。</summary>
        private readonly Dictionary<object, Dictionary<int, HashSet<object>>> _locks = new Dictionary<object, Dictionary<int, HashSet<object>>>();

        /// <summary>是否已释放。</summary>
        private bool _disposed;

        #endregion

        #region ILockService Implementation - Acquire

        /// <summary>
        /// 请求一个全局锁（不针对特定 lockSubject）。
        /// </summary>
        public LockHandle Acquire(int lockType, object lockObj)
        {
            return Acquire(GlobalSentinel, lockType, lockObj);
        }

        /// <summary>
        /// 请求一个针对特定 lockSubject 的锁。
        /// </summary>
        public LockHandle Acquire(object lockSubject, int lockType, object lockObj)
        {
            if (lockObj == null)
                throw new ArgumentNullException(nameof(lockObj), "lock cannot be null.");

            object subjectKey = lockSubject ?? GlobalSentinel;

            // 获取或创建 lockSubject 层
            if (!_locks.TryGetValue(subjectKey, out var typeDict))
            {
                typeDict = new Dictionary<int, HashSet<object>>();
                _locks[subjectKey] = typeDict;
            }

            // 获取或创建 lockType 层
            if (!typeDict.TryGetValue(lockType, out var lockSet))
            {
                lockSet = new HashSet<object>();
                typeDict[lockType] = lockSet;
            }

            bool wasEmpty = lockSet.Count == 0;

            // HashSet 幂等添加
            lockSet.Add(lockObj);

            // 从"空"变为"非空"时触发事件
            if (wasEmpty && lockSet.Count == 1)
            {
                OnLocked?.Invoke(lockSubject, lockType, lockObj);
            }

            return new LockHandle(this, lockSubject, lockType, lockObj);
        }

        #endregion

        #region ILockService Implementation - Release

        /// <summary>
        /// 释放一个全局锁。
        /// </summary>
        public void Release(int lockType, object lockObj)
        {
            Release(GlobalSentinel, lockType, lockObj);
        }

        /// <summary>
        /// 释放一个针对特定 lockSubject 的锁。
        /// </summary>
        public void Release(object lockSubject, int lockType, object lockObj)
        {
            if (lockObj == null)
                throw new ArgumentNullException(nameof(lockObj), "lock cannot be null.");

            object subjectKey = lockSubject ?? GlobalSentinel;

            if (!_locks.TryGetValue(subjectKey, out var typeDict))
                return;

            if (!typeDict.TryGetValue(lockType, out var lockSet))
                return;

            bool wasNonEmpty = lockSet.Count > 0;

            lockSet.Remove(lockObj);

            // 从"非空"变为"空"时触发事件
            if (wasNonEmpty && lockSet.Count == 0)
            {
                OnUnlocked?.Invoke(lockSubject, lockType, lockObj);

                // 清理空字典
                typeDict.Remove(lockType);
                if (typeDict.Count == 0)
                {
                    _locks.Remove(subjectKey);
                }
            }
        }

        #endregion

        #region ILockService Implementation - Query

        /// <summary>
        /// 指定类型的锁是否处于激活状态（任意 lockSubject 下存在锁）。
        /// </summary>
        public bool IsLocked(int lockType)
        {
            foreach (var typeDict in _locks.Values)
            {
                if (typeDict.TryGetValue(lockType, out var lockSet) && lockSet.Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 指定 lockSubject 下该类型的锁是否处于激活状态。
        /// </summary>
        public bool IsLocked(object lockSubject, int lockType)
        {
            object subjectKey = lockSubject ?? GlobalSentinel;

            if (_locks.TryGetValue(subjectKey, out var typeDict))
            {
                return typeDict.TryGetValue(lockType, out var lockSet) && lockSet.Count > 0;
            }
            return false;
        }

        /// <summary>
        /// 获取指定类型锁的全局锁对象数量（所有 lockSubject 合计）。
        /// </summary>
        public int GetLockCount(int lockType)
        {
            int count = 0;
            foreach (var typeDict in _locks.Values)
            {
                if (typeDict.TryGetValue(lockType, out var lockSet))
                {
                    count += lockSet.Count;
                }
            }
            return count;
        }

        /// <summary>
        /// 获取指定类型的所有锁对象副本（调试用）。
        /// </summary>
        public IReadOnlyList<object> GetLockObjects(int lockType)
        {
            var result = new List<object>();
            foreach (var typeDict in _locks.Values)
            {
                if (typeDict.TryGetValue(lockType, out var lockSet))
                {
                    result.AddRange(lockSet);
                }
            }
            return result;
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
