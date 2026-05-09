using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 全局锁服务静态类。提供基于 <see cref="ILockable"/>、lockType、lock 三要素的全局锁管理能力。
    /// <para>内部持有全局单例，所有静态方法委托给该单例。</para>
    /// <para>锁由三要素组成：锁主体（<see cref="ILockable"/>，null 表示全局）、锁类型（lockType）、锁本身（lock，不能为 null）。</para>
    /// </summary>
    public static class LockService
    {
        #region Internal Implementation

        /// <summary>
        /// 锁服务内部实现，封装锁的存储与操作逻辑。
        /// </summary>
        private sealed class LockServiceImpl
        {
            #region Events

            public event Action<ILockable, int, object> OnLocked;
            public event Action<ILockable, int, object> OnUnlocked;

            #endregion

            #region Private Fields

            /// <summary>全局锁的哨兵 key，用于代替 null。</summary>
            private static readonly object GlobalSentinel = new object();

            /// <summary>lockSubject → lockType → HashSet<lock>。</summary>
            private readonly Dictionary<object, Dictionary<int, HashSet<object>>> _locks = new Dictionary<object, Dictionary<int, HashSet<object>>>();

            /// <summary>是否已释放。</summary>
            private bool _disposed;

            #endregion

            #region Acquire

            public LockHandle Acquire(int lockType, object lockObj)
            {
                return Acquire((ILockable)null, lockType, lockObj);
            }

            public LockHandle Acquire(ILockable lockSubject, int lockType, object lockObj)
            {
                if (lockObj == null)
                    throw new ArgumentNullException(nameof(lockObj), "lock cannot be null.");

                object subjectKey = lockSubject ?? GlobalSentinel;

                if (!_locks.TryGetValue(subjectKey, out var typeDict))
                {
                    typeDict = new Dictionary<int, HashSet<object>>();
                    _locks[subjectKey] = typeDict;
                }

                if (!typeDict.TryGetValue(lockType, out var lockSet))
                {
                    lockSet = new HashSet<object>();
                    typeDict[lockType] = lockSet;
                }

                bool wasEmpty = lockSet.Count == 0;
                lockSet.Add(lockObj);

                if (wasEmpty && lockSet.Count == 1)
                {
                    OnLocked?.Invoke(lockSubject, lockType, lockObj);
                }

                return new LockHandle(() => Release(lockSubject, lockType, lockObj));
            }

            #endregion

            #region Release

            public void Release(int lockType, object lockObj)
            {
                Release((ILockable)null, lockType, lockObj);
            }

            public void Release(ILockable lockSubject, int lockType, object lockObj)
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

                if (wasNonEmpty && lockSet.Count == 0)
                {
                    OnUnlocked?.Invoke(lockSubject, lockType, lockObj);

                    typeDict.Remove(lockType);
                    if (typeDict.Count == 0)
                    {
                        _locks.Remove(subjectKey);
                    }
                }
            }

            #endregion

            #region Query

            public bool IsLocked(int lockType)
            {
                foreach (var typeDict in _locks.Values)
                {
                    if (typeDict.TryGetValue(lockType, out var lockSet) && lockSet.Count > 0)
                        return true;
                }
                return false;
            }

            public bool IsLocked(ILockable lockSubject, int lockType)
            {
                object subjectKey = lockSubject ?? GlobalSentinel;

                if (_locks.TryGetValue(subjectKey, out var typeDict))
                {
                    return typeDict.TryGetValue(lockType, out var lockSet) && lockSet.Count > 0;
                }
                return false;
            }

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

            public int GetLockCount(ILockable lockSubject, int lockType)
            {
                object subjectKey = lockSubject ?? GlobalSentinel;

                if (_locks.TryGetValue(subjectKey, out var typeDict))
                {
                    if (typeDict.TryGetValue(lockType, out var lockSet))
                    {
                        return lockSet.Count;
                    }
                }
                return 0;
            }

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

            public IReadOnlyList<object> GetLockObjects(ILockable lockSubject, int lockType)
            {
                object subjectKey = lockSubject ?? GlobalSentinel;

                if (_locks.TryGetValue(subjectKey, out var typeDict))
                {
                    if (typeDict.TryGetValue(lockType, out var lockSet))
                    {
                        return new List<object>(lockSet);
                    }
                }
                return Array.Empty<object>();
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

        #endregion

        #region Private Fields

        private static LockServiceImpl _default;

        private static LockServiceImpl Default => _default ??= new LockServiceImpl();

        #endregion

        #region Acquire

        /// <summary>
        /// 请求一个全局锁（不针对特定 lockSubject）。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// </summary>
        public static LockHandle Acquire(int lockType, object lockObj)
            => Default.Acquire(lockType, lockObj);

        /// <summary>
        /// 请求一个针对特定 <see cref="ILockable"/> 的锁。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// </summary>
        public static LockHandle Acquire(ILockable lockSubject, int lockType, object lockObj)
            => Default.Acquire(lockSubject, lockType, lockObj);

        #endregion

        #region Release

        /// <summary>
        /// 释放一个全局锁。
        /// </summary>
        public static void Release(int lockType, object lockObj)
            => Default.Release(lockType, lockObj);

        /// <summary>
        /// 释放一个针对特定 <see cref="ILockable"/> 的锁。
        /// </summary>
        public static void Release(ILockable lockSubject, int lockType, object lockObj)
            => Default.Release(lockSubject, lockType, lockObj);

        #endregion

        #region Query

        /// <summary>
        /// 指定类型的锁是否处于激活状态（任意 lockSubject 下存在锁）。
        /// </summary>
        public static bool IsLocked(int lockType)
            => Default.IsLocked(lockType);

        /// <summary>
        /// 指定 <see cref="ILockable"/> 下该类型的锁是否处于激活状态。
        /// </summary>
        public static bool IsLocked(ILockable lockSubject, int lockType)
            => Default.IsLocked(lockSubject, lockType);

        /// <summary>
        /// 获取指定类型锁的全局锁对象数量（所有 lockSubject 合计）。
        /// </summary>
        public static int GetLockCount(int lockType)
            => Default.GetLockCount(lockType);

        /// <summary>
        /// 获取指定 <see cref="ILockable"/> 下该类型锁的对象数量。
        /// </summary>
        public static int GetLockCount(ILockable lockSubject, int lockType)
            => Default.GetLockCount(lockSubject, lockType);

        /// <summary>
        /// 获取指定类型的所有锁对象副本（调试用）。
        /// </summary>
        public static IReadOnlyList<object> GetLockObjects(int lockType)
            => Default.GetLockObjects(lockType);

        /// <summary>
        /// 获取指定 <see cref="ILockable"/> 下该类型的所有锁对象副本（调试用）。
        /// </summary>
        public static IReadOnlyList<object> GetLockObjects(ILockable lockSubject, int lockType)
            => Default.GetLockObjects(lockSubject, lockType);

        #endregion

        #region Events

        /// <summary>
        /// 锁定事件：当某锁被添加时触发。
        /// <para>参数为 (lockSubject, lockType, lock)。</para>
        /// </summary>
        public static event Action<ILockable, int, object> OnLocked
        {
            add => Default.OnLocked += value;
            remove => Default.OnLocked -= value;
        }

        /// <summary>
        /// 解锁事件：当某锁被移除时触发。
        /// <para>参数为 (lockSubject, lockType, lock)。</para>
        /// </summary>
        public static event Action<ILockable, int, object> OnUnlocked
        {
            add => Default.OnUnlocked += value;
            remove => Default.OnUnlocked -= value;
        }

        #endregion
    }
}
