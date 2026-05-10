using System;
using System.Collections.Generic;

namespace XFramework.XLock
{

    /// <summary>
    /// 全局锁服务静态类。提供基于 <see cref="ILockable"/>、lockType、lock 三要素的全局锁管理能力。
    /// <para>锁由三要素组成：锁主体（<see cref="ILockable"/>，null 表示全局）、锁类型（lockType）、锁本身（lock，不能为 null）。</para>
    /// <para>全局锁请使用 <see cref="Global"/> 作为 lockSubject。</para>
    /// </summary>
    public static class LockService
    {
        #region Global Sentinel

        /// <summary>
        /// 全局锁哨兵。实现 <see cref="ILockable"/>，作为全局锁的 lockSubject 使用。
        /// <para>例如：<c>LockService.Acquire(LockService.Global, lockType, lockObj)</c></para>
        /// </summary>
        private sealed class GlobalSentinel : ILockable { }

        public static readonly ILockable Global = new GlobalSentinel();

        #endregion

        #region Events

        /// <summary>
        /// 全局锁定事件：当某锁被添加时触发。
        /// <para>参数为 (lockSubject, lockType, lock)。</para>
        /// </summary>
        public static event Action<ILockable, int, object> OnGlobalLocked;

        /// <summary>
        /// 全局解锁事件：当某锁被移除时触发。
        /// <para>参数为 (lockSubject, lockType, lock)。</para>
        /// </summary>
        public static event Action<ILockable, int, object> OnGlobalUnlocked;

        #endregion

        #region Private Fields

        /// <summary>lockSubject → lockType → HashSet<lock>。</summary>
        private static readonly Dictionary<ILockable, Dictionary<int, HashSet<object>>> _locks
            = new Dictionary<ILockable, Dictionary<int, HashSet<object>>>();

        /// <summary>每个 subject 的锁定事件订阅。</summary>
        private static readonly Dictionary<ILockable, Action<int>> _onLockedSubjects
            = new Dictionary<ILockable, Action<int>>();

        /// <summary>每个 subject 的解锁事件订阅。</summary>
        private static readonly Dictionary<ILockable, Action<int>> _onUnlockedSubjects
            = new Dictionary<ILockable, Action<int>>();

        /// <summary>是否已释放。</summary>
        private static bool _disposed;

        #endregion

        #region Subject Event Subscription

        /// <summary>
        /// 订阅指定 <see cref="ILockable"/> 的锁定事件。
        /// <para>全局锁（<see cref="Global"/>）的锁定也会触发此回调。</para>
        /// <para>返回 <see cref="IDisposable"/>，调用 <c>Dispose()</c> 可取消订阅。</para>
        /// </summary>
        public static IDisposable OnLocked(ILockable subject, Action<int> handler)
        {
            if (!_onLockedSubjects.ContainsKey(subject))
            {
                _onLockedSubjects[subject] = null;
            }
            _onLockedSubjects[subject] += handler;

            return ActionDisposable.Rent(subject, handler, _onLockedSubjects);
        }

        /// <summary>
        /// 订阅指定 <see cref="ILockable"/> 的解锁事件。
        /// <para>全局锁（<see cref="Global"/>）的解锁也会触发此回调。</para>
        /// <para>返回 <see cref="IDisposable"/>，调用 <c>Dispose()</c> 可取消订阅。</para>
        /// </summary>
        public static IDisposable OnUnlocked(ILockable subject, Action<int> handler)
        {
            if (!_onUnlockedSubjects.ContainsKey(subject))
            {
                _onUnlockedSubjects[subject] = null;
            }
            _onUnlockedSubjects[subject] += handler;

            return ActionDisposable.Rent(subject, handler, _onUnlockedSubjects);
        }

        /// <summary>
        /// 通知指定 subject 的锁定事件订阅者。
        /// <para>如果是全局锁，通知所有非 Global 的订阅者。</para>
        /// </summary>
        private static void NotifyOnLocked(ILockable lockSubject, int lockType, object lockObj)
        {
            if (lockSubject == Global)
            {
                // 全局锁：通知所有订阅者
                foreach (var kvp in _onLockedSubjects)
                {
                    if (kvp.Key != Global)
                    {
                        kvp.Value?.Invoke(lockType);
                    }
                }
            }
            else
            {
                // 普通锁：只通知该 subject 的订阅者
                if (_onLockedSubjects.TryGetValue(lockSubject, out var handler))
                {
                    handler?.Invoke(lockType);
                }
            }
        }

        /// <summary>
        /// 通知指定 subject 的解锁事件订阅者。
        /// <para>如果是全局锁，通知所有非 Global 的订阅者。</para>
        /// </summary>
        private static void NotifyOnUnlocked(ILockable lockSubject, int lockType, object lockObj)
        {
            if (lockSubject == Global)
            {
                // 全局锁：通知所有订阅者
                foreach (var kvp in _onUnlockedSubjects)
                {
                    if (kvp.Key != Global)
                    {
                        kvp.Value?.Invoke(lockType);
                    }
                }
            }
            else
            {
                // 普通锁：只通知该 subject 的订阅者
                if (_onUnlockedSubjects.TryGetValue(lockSubject, out var handler))
                {
                    handler?.Invoke(lockType);
                }
            }
        }

        #endregion

        #region Acquire

        /// <summary>
        /// 请求一个针对特定 <see cref="ILockable"/> 的锁。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// <para>全局锁请使用 <see cref="Global"/> 作为 lockSubject。</para>
        /// </summary>
        public static LockHandle Acquire(ILockable lockSubject, int lockType, object lockObj)
        {
            if (lockObj == null)
                throw new ArgumentNullException(nameof(lockObj), "lock cannot be null.");

            ILockable subjectKey = lockSubject ?? Global;

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
                OnGlobalLocked?.Invoke(lockSubject, lockType, lockObj);
                NotifyOnLocked(lockSubject, lockType, lockObj);
            }

            return new LockHandle(lockSubject, lockType, lockObj);
        }

        #endregion

        #region Release

        /// <summary>
        /// 释放一个针对特定 <see cref="ILockable"/> 的锁。
        /// <para>全局锁请使用 <see cref="Global"/> 作为 lockSubject。</para>
        /// </summary>
        public static void Release(ILockable lockSubject, int lockType, object lockObj)
        {
            if (lockObj == null)
                throw new ArgumentNullException(nameof(lockObj), "lock cannot be null.");

            ILockable subjectKey = lockSubject ?? Global;

            if (!_locks.TryGetValue(subjectKey, out var typeDict))
                return;

            if (!typeDict.TryGetValue(lockType, out var lockSet))
                return;

            bool wasNonEmpty = lockSet.Count > 0;
            lockSet.Remove(lockObj);

            if (wasNonEmpty && lockSet.Count == 0)
            {
                OnGlobalUnlocked?.Invoke(lockSubject, lockType, lockObj);
                NotifyOnUnlocked(lockSubject, lockType, lockObj);

                typeDict.Remove(lockType);
                if (typeDict.Count == 0)
                {
                    _locks.Remove(subjectKey);
                }
            }
        }

        #endregion

        #region Query

        /// <summary>
        /// 指定 <see cref="ILockable"/> 下该类型的锁是否处于激活状态。
        /// <para>同时检查 <see cref="Global"/> 全局锁，全局锁生效时所有 subject 均视为被锁定。</para>
        /// </summary>
        public static bool IsLocked(ILockable lockSubject, int lockType)
        {
            ILockable subjectKey = lockSubject ?? Global;

            if (_locks.TryGetValue(subjectKey, out var typeDict))
            {
                if (typeDict.TryGetValue(lockType, out var lockSet) && lockSet.Count > 0)
                    return true;
            }

            // 同时检查全局锁
            if (subjectKey != Global && _locks.TryGetValue(Global, out var globalDict))
            {
                return globalDict.TryGetValue(lockType, out var globalSet) && globalSet.Count > 0;
            }

            return false;
        }

        /// <summary>
        /// 获取指定 <see cref="ILockable"/> 下该类型锁的对象数量。
        /// <para>包含 <see cref="Global"/> 全局锁的数量。</para>
        /// </summary>
        public static int GetLockCount(ILockable lockSubject, int lockType)
        {
            ILockable subjectKey = lockSubject ?? Global;
            int count = 0;

            if (_locks.TryGetValue(subjectKey, out var typeDict))
            {
                if (typeDict.TryGetValue(lockType, out var lockSet))
                {
                    count += lockSet.Count;
                }
            }

            // 同时统计全局锁
            if (subjectKey != Global && _locks.TryGetValue(Global, out var globalDict))
            {
                if (globalDict.TryGetValue(lockType, out var globalSet))
                {
                    count += globalSet.Count;
                }
            }

            return count;
        }

        /// <summary>
        /// 获取指定 <see cref="ILockable"/> 下该类型的所有锁对象副本（调试用）。
        /// <para>包含 <see cref="Global"/> 全局锁的对象。</para>
        /// </summary>
        public static IReadOnlyList<object> GetLockObjects(ILockable lockSubject, int lockType)
        {
            ILockable subjectKey = lockSubject ?? Global;
            var result = new List<object>();

            if (_locks.TryGetValue(subjectKey, out var typeDict))
            {
                if (typeDict.TryGetValue(lockType, out var lockSet))
                {
                    result.AddRange(lockSet);
                }
            }

            // 同时获取全局锁对象
            if (subjectKey != Global && _locks.TryGetValue(Global, out var globalDict))
            {
                if (globalDict.TryGetValue(lockType, out var globalSet))
                {
                    result.AddRange(globalSet);
                }
            }

            return result;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放所有锁资源，清空锁状态。
        /// </summary>
        public static void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _locks.Clear();
            _onLockedSubjects.Clear();
            _onUnlockedSubjects.Clear();
            OnGlobalLocked = null;
            OnGlobalUnlocked = null;
        }

        #endregion
    }

    /// <summary>
    /// 可复用的 <see cref="IDisposable"/> 实现，存储取消订阅所需原始数据而非委托，实现零 GC。
    /// <para>内部维护静态对象池，避免频繁 GC Alloc。</para>
    /// </summary>
    internal sealed class ActionDisposable : IDisposable
    {
        private static readonly Stack<ActionDisposable> _pool = new(capacity: 8);

        private ILockable _subject;
        private Action<int> _handler;
        private Dictionary<ILockable, Action<int>> _targetDict;

        /// <summary>
        /// 从池中租用一个 <see cref="IDisposable"/>，
        /// <see cref="Dispose()"/> 时会从 <paramref name="targetDict"/> 中移除 <paramref name="handler"/>。
        /// </summary>
        public static IDisposable Rent(ILockable subject, Action<int> handler, Dictionary<ILockable, Action<int>> targetDict)
        {
            if (_pool.Count > 0)
            {
                var d = _pool.Pop();
                d.Set(subject, handler, targetDict);
                return d;
            }
            var d2 = new ActionDisposable();
            d2.Set(subject, handler, targetDict);
            return d2;
        }

        private void Set(ILockable subject, Action<int> handler, Dictionary<ILockable, Action<int>> targetDict)
        {
            _subject = subject;
            _handler = handler;
            _targetDict = targetDict;
        }

        public void Dispose()
        {
            if (_targetDict == null) return;

            // 执行取消订阅逻辑（原本由 lambda 完成）
            _targetDict[_subject] -= _handler;
            if (_targetDict[_subject] == null)
            {
                _targetDict.Remove(_subject);
            }

            // 清空引用，归还至池中
            _subject = null;
            _handler = null;
            _targetDict = null;
            _pool.Push(this);
        }
    }
}
