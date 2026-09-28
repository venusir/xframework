using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XLock
{

    /// <summary>
    /// 全局锁管理器静态类。提供基于 <see cref="ILockable"/>、lockType、lock 三要素的全局锁管理能力。
    /// <para>锁由三要素组成：锁主体（<see cref="ILockable"/>，null 表示全局）、锁类型（lockType）、锁本身（lock，不能为 null）。</para>
    /// <para>全局锁请使用 <see cref="Global"/> 作为 lockSubject。</para>
    /// </summary>
    public static class LockManager
    {
        #region Global Sentinel

        /// <summary>
        /// 全局锁哨兵。实现 <see cref="ILockable"/>，作为全局锁的 lockSubject 使用。
        /// <para>例如：<c>LockManager.AddLock(LockManager.Global, lockType, lockObj)</c></para>
        /// </summary>
        private sealed class GlobalSentinel : ILockable { }

        public static readonly ILockable Global = new GlobalSentinel();

        #endregion

        #region Auto Lifecycle

        /// <summary>
        /// 自动在游戏退出时清理锁状态，无需外部调用。
        /// <para>在 Editor 中 Domain Reload 或停止播放时也会触发清理。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        static void AutoInit()
        {
            Application.quitting += Dispose;
        }

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

        /// <summary>lockSubject → lockType → HashSet&lt;lock&gt;。</summary>
        private static Dictionary<ILockable, Dictionary<int, HashSet<object>>> _locks
            = new Dictionary<ILockable, Dictionary<int, HashSet<object>>>();

        /// <summary>每个 subject 的锁定事件订阅。</summary>
        private static Dictionary<ILockable, Action<int>> _onLockedSubjects
            = new Dictionary<ILockable, Action<int>>();

        /// <summary>每个 subject 的解锁事件订阅。</summary>
        private static Dictionary<ILockable, Action<int>> _onUnlockedSubjects
            = new Dictionary<ILockable, Action<int>>();

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
        /// 通知指定 subject 的锁定事件订阅者。派发语义见 <see cref="DispatchSubscribers"/>。
        /// </summary>
        private static void NotifyOnLocked(ILockable lockSubject, int lockType, object lockObj)
            => DispatchSubscribers(_onLockedSubjects, lockSubject ?? Global, lockType, nameof(OnLocked));

        /// <summary>
        /// 通知指定 subject 的解锁事件订阅者。派发语义见 <see cref="DispatchSubscribers"/>。
        /// </summary>
        private static void NotifyOnUnlocked(ILockable lockSubject, int lockType, object lockObj)
            => DispatchSubscribers(_onUnlockedSubjects, lockSubject ?? Global, lockType, nameof(OnUnlocked));

        /// <summary>
        /// 通知某个订阅表里的订阅者：全局锁通知所有订阅者（跳过以 <see cref="Global"/> 为键的条目），
        /// 普通锁只通知该 subject 的订阅者。
        /// <para><b>派发基于快照</b>：全局分支先把订阅者取进快照再遍历，故回调里的增删订阅打不崩本轮
        /// 派发——从下一轮起生效（主体分支取到的是多播委托的本地副本，本来就是同一语义）。</para>
        /// <para><b>异常隔离</b>：全局分支逐订阅者隔离，排在抛异常者之后的订阅者仍会收到通知；
        /// 主体分支是整条多播一次隔离（见 <see cref="DispatchSafely(Action{int}, int, string, ILockable)"/>）。</para>
        /// </summary>
        private static void DispatchSubscribers(Dictionary<ILockable, Action<int>> table, ILockable subjectKey,
            int lockType, string eventName)
        {
            if (subjectKey == Global)
            {
                var snapshot = LockSnapshotPool.Rent();
                try
                {
                    // 快照阶段没有任何用户代码进入，此处枚举是安全的
                    foreach (var kvp in table)
                    {
                        if (kvp.Key == Global || kvp.Value == null)
                            continue;

                        snapshot.Add(kvp);
                    }

                    for (int i = 0; i < snapshot.Count; i++)
                        DispatchSafely(snapshot[i].Value, lockType, eventName, snapshot[i].Key);
                }
                finally
                {
                    LockSnapshotPool.Return(snapshot);
                }
            }
            else if (table.TryGetValue(subjectKey, out var handler))
            {
                DispatchSafely(handler, lockType, eventName, subjectKey);
            }
        }

        /// <summary>
        /// 派发一个订阅者，并隔离其异常——订阅者的 bug 不得冒到加锁/解锁路径上。
        /// <para><b>为什么必须隔离</b>：<see cref="AddLock"/> 先入表再派发，异常若逃出去，调用方就拿不到
        /// 那个句柄，而锁已经留在表里——成了没人能释放的永久锁（实测 <see cref="GetLockCount"/> 残留 1）。</para>
        /// <para><b>粒度</b>：本重载包的是**一整条多播委托**，故抛异常者会饿死同一条多播里排在它后面的
        /// 订阅者（下一次派发照常，且日志已记下）。不用 <c>GetInvocationList</c> 逐个隔离——那要给每次
        /// 派发分配一个委托数组，与 Pipeline 的同名方法取舍一致。</para>
        /// </summary>
        private static void DispatchSafely(Action<int> handler, int lockType, string eventName, ILockable subject)
        {
            if (handler == null)
                return;

            try
            {
                handler.Invoke(lockType);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lock] {eventName} subscriber threw (subject: {NameOf(subject)}): {e}");
            }
        }

        /// <summary>两个公开事件的重载：载荷不同，隔离语义同上游版本。</summary>
        private static void DispatchSafely(Action<ILockable, int, object> handler, ILockable subject, int lockType,
            object lockObj, string eventName)
        {
            if (handler == null)
                return;

            try
            {
                handler.Invoke(subject, lockType, lockObj);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lock] {eventName} subscriber threw (subject: {NameOf(subject)}): {e}");
            }
        }

        /// <summary>
        /// 诊断用的主体名。
        /// <para><b>不用 <c>ToString()</c></b>：那是用户代码，可能在 catch 里二次抛；<c>GetType().Name</c>
        /// 是纯托管调用。全局哨兵要特判，否则日志里出现的是私有嵌套类型名。</para>
        /// </summary>
        private static string NameOf(ILockable subject)
        {
            if (subject == null)
                return "null";

            if (ReferenceEquals(subject, Global))
                return nameof(Global);

            return subject.GetType().Name;
        }

        #endregion

        #region Acquire

        /// <summary>
        /// 请求一个针对特定 <see cref="ILockable"/> 的锁。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// <para>全局锁请使用 <see cref="Global"/> 作为 lockSubject。</para>
        /// </summary>
        public static LockHandle AddLock(ILockable lockSubject, int lockType, object lockObj)
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
                DispatchSafely(OnGlobalLocked, lockSubject, lockType, lockObj, nameof(OnGlobalLocked));
                NotifyOnLocked(lockSubject, lockType, lockObj);
            }

            return new LockHandle(lockSubject, lockType, lockObj);
        }

        /// <summary>
        /// 请求一个全局锁（lockSubject 自动设为 <see cref="Global"/>）。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// </summary>
        public static LockHandle AddLock(int lockType, object lockObj)
            => AddLock(Global, lockType, lockObj);

        #endregion

        #region Release

        /// <summary>
        /// 释放一个针对特定 <see cref="ILockable"/> 的锁。
        /// <para>全局锁请使用 <see cref="Global"/> 作为 lockSubject。</para>
        /// </summary>
        public static void RemoveLock(ILockable lockSubject, int lockType, object lockObj)
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
                DispatchSafely(OnGlobalUnlocked, lockSubject, lockType, lockObj, nameof(OnGlobalUnlocked));
                NotifyOnUnlocked(lockSubject, lockType, lockObj);

                typeDict.Remove(lockType);
                if (typeDict.Count == 0)
                {
                    _locks.Remove(subjectKey);
                }
            }
        }

        /// <summary>
        /// 释放一个全局锁（lockSubject 自动设为 <see cref="Global"/>）。
        /// </summary>
        public static void RemoveLock(int lockType, object lockObj)
            => RemoveLock(Global, lockType, lockObj);

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
        /// 指定锁对象是否仍持有该主体下该类型的锁。
        /// <para>与 <see cref="IsLocked"/> 的区别：后者是<b>聚合</b>语义（该类型下只要还有任一持有者即为 true），
        /// 本方法<b>逐句柄精确</b>——用于回答「我这一把还在不在」。<see cref="LockHandle.IsHeld"/> 基于它实现。</para>
        /// <para><b>不检查全局锁</b>：全局锁是别人的持有关系，不构成本锁对象仍在持有。</para>
        /// </summary>
        /// <param name="lockSubject">锁主体。</param>
        /// <param name="lockType">锁类型。</param>
        /// <param name="lockObj">锁对象（即持有者标识）。</param>
        public static bool IsLockedBy(ILockable lockSubject, int lockType, object lockObj)
        {
            if (lockObj == null)
                return false;

            ILockable subjectKey = lockSubject ?? Global;

            return _locks.TryGetValue(subjectKey, out var typeDict)
                && typeDict.TryGetValue(lockType, out var lockSet)
                && lockSet.Contains(lockObj);
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

        #region Reset

        /// <summary>
        /// 重置所有锁状态，清空锁数据和事件订阅。
        /// <para>多次调用是安全的，每次都会重新分配内部集合，彻底切断旧引用。</para>
        /// <para>主要用于单元测试隔离，生产环境中通常不需要调用此方法。</para>
        /// </summary>
        public static void Dispose()
        {
            _locks = new Dictionary<ILockable, Dictionary<int, HashSet<object>>>();
            _onLockedSubjects = new Dictionary<ILockable, Action<int>>();
            _onUnlockedSubjects = new Dictionary<ILockable, Action<int>>();
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

    /// <summary>
    /// 派发快照的取用点（模块内自持，不引其它模块的池）。
    /// <para><b>为什么必须是池而不是复用单一字段</b>：订阅者在回调里再加一把全局锁会**重入**派发，
    /// 单一字段会被内层清空，外层正在遍历的缓冲随之报废。池在任意嵌套深度都正确。</para>
    /// <para>元素是 <c>KeyValuePair</c> 结构体，装进 <c>List</c> 不装箱；归还前按容量决定是否留用——
    /// 订阅者极多时的一次超大派发不该把那份数组永久驻留在静态根上。</para>
    /// </summary>
    internal static class LockSnapshotPool
    {
        /// <summary>留用上限：超过此容量的快照在归还时直接丢弃。</summary>
        private const int MaxRetainedCapacity = 256;

        private static readonly Stack<List<KeyValuePair<ILockable, Action<int>>>> _pool =
            new Stack<List<KeyValuePair<ILockable, Action<int>>>>(4);

        /// <summary>取一个空快照（内部可能带已归还的容量）。</summary>
        internal static List<KeyValuePair<ILockable, Action<int>>> Rent()
            => _pool.Count > 0 ? _pool.Pop() : new List<KeyValuePair<ILockable, Action<int>>>(8);

        /// <summary>归还快照：清空内容，容量过大则丢弃（见 <see cref="MaxRetainedCapacity"/>）。</summary>
        internal static void Return(List<KeyValuePair<ILockable, Action<int>>> snapshot)
        {
            if (snapshot == null)
                return;

            snapshot.Clear();

            if (snapshot.Capacity > MaxRetainedCapacity)
                return;

            _pool.Push(snapshot);
        }
    }
}
