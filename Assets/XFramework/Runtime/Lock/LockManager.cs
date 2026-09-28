using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using XFramework.XMessage;

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
        /// 框架级锁定事件：**任意主体**的某类型锁从「无」变「有」时触发一次。
        /// <para><b>名字里的 Global 指「框架级事件总线」，不是「只有全局锁才触发」</b>——它对每一把首次锁
        /// 都触发（主体锁也算）。想要「只关心全局锁」请用 <see cref="OnLocked"/> 并以 <see cref="Global"/>
        /// 订阅；想要「关心某个主体的聚合锁定状态是否翻转」请用 <see cref="OnLockStateChanged"/>。</para>
        /// <para>同一 (主体,类型) 上叠加的多把锁只触发一次——「还有别人持锁」不算新事件。</para>
        /// <para>参数为 (lockSubject, lockType, lock)，其中 <c>lockSubject</c> 已归一化——调用方传 null 时
        /// 这里收到的是 <see cref="Global"/>。</para>
        /// </summary>
        public static event Action<ILockable, int, object> OnGlobalLocked;

        /// <summary>
        /// 框架级解锁事件：**任意主体**的某类型锁从「有」变「无」时触发一次。
        /// <para>命名与影响范围同 <see cref="OnGlobalLocked"/>（对每一把最后一次解锁都触发，不限全局锁）。</para>
        /// <para>参数为 (lockSubject, lockType, lock)，其中 <c>lockSubject</c> 已归一化。</para>
        /// </summary>
        public static event Action<ILockable, int, object> OnGlobalUnlocked;

        #endregion

        #region Private Fields

        /// <summary>lockSubject → lockType → HashSet&lt;lock&gt;。</summary>
        private static Dictionary<ILockable, Dictionary<int, HashSet<object>>> _locks
            = new Dictionary<ILockable, Dictionary<int, HashSet<object>>>(LockSubjectReferenceComparer.Instance);

        /// <summary>每个 subject 的锁定事件订阅。</summary>
        private static Dictionary<ILockable, Action<int>> _onLockedSubjects
            = new Dictionary<ILockable, Action<int>>(LockSubjectReferenceComparer.Instance);

        /// <summary>每个 subject 的解锁事件订阅。</summary>
        private static Dictionary<ILockable, Action<int>> _onUnlockedSubjects
            = new Dictionary<ILockable, Action<int>>(LockSubjectReferenceComparer.Instance);

        /// <summary>subject → 销毁令牌注册（每主体一次；主体空闲或门面重置时注销）。</summary>
        private static Dictionary<ILockable, CancellationTokenRegistration> _destroyBindings
            = new Dictionary<ILockable, CancellationTokenRegistration>(LockSubjectReferenceComparer.Instance);

        #endregion

        #region Destroy Binding

        /// <summary>
        /// 主体销毁时是否自动释放它的锁与订阅（默认 <c>true</c>）。
        /// <para>绑定的是**主体的**销毁令牌：<see cref="MonoBehaviour"/>（用其 <c>destroyCancellationToken</c>）
        /// 或实现了 <see cref="IDestroyCancellationToken"/> 的普通对象。两者皆非时不绑定、也不告警——
        /// 那种主体由调用方用 <see cref="RemoveAllLocks"/>/<see cref="RemoveAllSubscriptions"/> 显式收口。</para>
        /// <para>绑定的时机是「主体首次进入任一容器」；关掉它只是不再新建绑定（已存在的不解除）。</para>
        /// <para><see cref="Dispose"/> 会把它复位为 <c>true</c>。</para>
        /// </summary>
        public static bool AutoReleaseOnDestroy { get; set; } = true;

        /// <summary>主体若提供销毁令牌，确保已登记「销毁时清理」。每主体只登记一次。</summary>
        private static void EnsureDestroyBinding(ILockable subjectKey)
        {
            if (!AutoReleaseOnDestroy || _destroyBindings.ContainsKey(subjectKey))
                return;

            if (!TryGetDestroyToken(subjectKey, out var token) ||
                !token.CanBeCanceled || token.IsCancellationRequested)
                return;

            // 静态 lambda + state 参数：不产生闭包分配；subjectKey 是接口引用，作为 state 传入不装箱
            _destroyBindings[subjectKey] = token.Register(static state => ReleaseSubject((ILockable)state), subjectKey);
        }

        /// <summary>取主体的销毁令牌：<see cref="MonoBehaviour"/> 与 <see cref="IDestroyCancellationToken"/> 两条路径。</summary>
        private static bool TryGetDestroyToken(ILockable subject, out CancellationToken token)
        {
            if (subject is MonoBehaviour mono)
            {
                // 只在此刻读一次——调用点都在主体必然存活的路径上（首次入容器 / 入口短路检查）
                token = mono.destroyCancellationToken;
                return true;
            }

            if (subject is IDestroyCancellationToken destroyable)
            {
                token = destroyable.DestroyCancellationToken;
                return true;
            }

            token = default;
            return false;
        }

        /// <summary>
        /// 主体是否已销毁（销毁令牌已取消）。用于入口短路：不短路的话，锁会先加上、再被「注册即触发」的
        /// 回调清掉——中间那一对「加了又解」的幻影事件，订阅者无从与真实事件区分。
        /// </summary>
        private static bool IsSubjectDestroyed(ILockable subjectKey)
        {
            if (!AutoReleaseOnDestroy)
                return false;

            return TryGetDestroyToken(subjectKey, out var token) && token.IsCancellationRequested;
        }

        /// <summary>
        /// 主体销毁时的清理：先丢订阅、再放锁，最后摘掉绑定表项。
        /// <para>顺序有意如此：濒死对象自己的回调不该在销毁过程中再跑一遍用户代码，而**别人**的订阅者
        /// （含两个总线事件）照常收到解锁——批量清理与逐把手动释放走同一条通知语义。</para>
        /// <para>此处不 <c>Dispose</c> registration：令牌正在触发中（回调方持有它），这时 Dispose 会等
        /// 自己。令牌已取消，registration 本就作废，摘掉表项即可。</para>
        /// </summary>
        private static void ReleaseSubject(ILockable subject)
        {
            _destroyBindings.Remove(subject);
            RemoveAllSubscriptions(subject);
            RemoveAllLocks(subject);
        }

        /// <summary>主体已空闲（无锁、无订阅）时注销它的销毁绑定，免得回调节点挂在长寿命令牌上。</summary>
        /// <remarks>
        /// 做成 <c>internal</c> 而非 <c>private</c>：同文件的顶层辅助类 <see cref="ActionDisposable"/> 在
        /// 退订清掉最后一个订阅后要能叫它（同 <c>MessageManager.TryBindToDestroy</c> 的「程序集内共享」取舍）。
        /// </remarks>
        internal static void TryUnbindDestroyBinding(ILockable subjectKey)
        {
            if (!_destroyBindings.TryGetValue(subjectKey, out var registration))
                return;

            if (_locks.ContainsKey(subjectKey) ||
                _onLockedSubjects.ContainsKey(subjectKey) ||
                _onUnlockedSubjects.ContainsKey(subjectKey))
                return;

            _destroyBindings.Remove(subjectKey);
            registration.Dispose();
        }

        #endregion

        #region Subject Event Subscription

        /// <summary>
        /// 订阅指定 <see cref="ILockable"/> 的锁定事件。
        /// <para><paramref name="subject"/> 为 null 等价于 <see cref="Global"/>——与 <see cref="AddLock"/>、
        /// <see cref="IsLocked"/> 等入口的约定一致。</para>
        /// <para>全局锁的锁定会触发所有主体订阅者；反过来，以 <see cref="Global"/> 订阅只收到全局锁的
        /// 加锁事件，不接收任何主体自己加的锁。</para>
        /// <para>返回 <see cref="IDisposable"/>，调用 <c>Dispose()</c> 可取消订阅。</para>
        /// </summary>
        /// <param name="subject">锁主体；null 表示全局。</param>
        /// <param name="handler">回调，参数为 lockType。不可为 null——空回调会留下一条没有任何通知的
        /// 幽灵订阅，并让「该主体是否还有订阅」这类判定永远为真。</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 null 时抛出。</exception>
        public static IDisposable OnLocked(ILockable subject, Action<int> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler), "handler cannot be null.");

            ILockable subjectKey = subject ?? Global;

            if (IsSubjectDestroyed(subjectKey))
            {
                Debug.LogWarning($"[Lock] subscription ignored: subject '{NameOf(subjectKey)}' has already been destroyed " +
                                 "(its destroy token is cancelled). Set LockManager.AutoReleaseOnDestroy to false to opt out.");
                return EmptyDisposable.Instance;
            }

            if (!_onLockedSubjects.ContainsKey(subjectKey))
            {
                _onLockedSubjects[subjectKey] = null;
            }
            _onLockedSubjects[subjectKey] += handler;

            EnsureDestroyBinding(subjectKey);

            return ActionDisposable.Rent(subjectKey, handler, _onLockedSubjects);
        }

        /// <summary>
        /// 订阅指定 <see cref="ILockable"/> 的解锁事件。
        /// <para>主体约定与全局锁影响范围同 <see cref="OnLocked"/>；返回 <see cref="IDisposable"/>，
        /// 调用 <c>Dispose()</c> 可取消订阅。</para>
        /// </summary>
        /// <param name="subject">锁主体；null 表示全局。</param>
        /// <param name="handler">回调，参数为 lockType，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 null 时抛出。</exception>
        public static IDisposable OnUnlocked(ILockable subject, Action<int> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler), "handler cannot be null.");

            ILockable subjectKey = subject ?? Global;

            if (IsSubjectDestroyed(subjectKey))
            {
                Debug.LogWarning($"[Lock] subscription ignored: subject '{NameOf(subjectKey)}' has already been destroyed " +
                                 "(its destroy token is cancelled). Set LockManager.AutoReleaseOnDestroy to false to opt out.");
                return EmptyDisposable.Instance;
            }

            if (!_onUnlockedSubjects.ContainsKey(subjectKey))
            {
                _onUnlockedSubjects[subjectKey] = null;
            }
            _onUnlockedSubjects[subjectKey] += handler;

            EnsureDestroyBinding(subjectKey);

            return ActionDisposable.Rent(subjectKey, handler, _onUnlockedSubjects);
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
        /// 通知某个订阅表里的订阅者：**全局锁**通知所有订阅者（含以 <see cref="Global"/> 订阅者——
        /// 以它订阅即「只关心全局锁」），**普通锁**只通知该 subject 的订阅者。
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
                        if (kvp.Value == null)
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

        /// <summary>
        /// 把主体当前已锁定的类型逐个交给回调——<see cref="OnLockStateChanged"/> 播报初始状态用。
        /// </summary>
        /// <remarks>
        /// 先收集再回调：回调里会跑用户代码（可以加锁/解锁），直接在 <c>_locks</c> 上边遍历边回调，
        /// 枚举器会被它改坏。只有确实存在已锁定类型时才分配（订阅期的一次小分配）。
        /// </remarks>
        private static void NotifyCurrentLockedTypes(ILockable subjectKey, Action<int> onLocked)
        {
            List<int> types = null;

            if (_locks.TryGetValue(subjectKey, out var typeDict))
            {
                foreach (var kvp in typeDict)
                {
                    if (kvp.Value.Count == 0)
                        continue;

                    if (types == null)
                        types = new List<int>(4);

                    types.Add(kvp.Key);
                }
            }

            if (subjectKey != Global && _locks.TryGetValue(Global, out var globalDict))
            {
                foreach (var kvp in globalDict)
                {
                    if (kvp.Value.Count == 0)
                        continue;

                    if (types == null)
                        types = new List<int>(4);

                    types.Add(kvp.Key);
                }
            }

            if (types == null)
                return;

            for (int i = 0; i < types.Count; i++)
                onLocked(types[i]);
        }

        /// <summary>
        /// 聚合状态订阅的实现：把两条原始边沿事件（<see cref="OnLocked"/>/<see cref="OnUnlocked"/>）合成
        /// 「聚合值真的翻转」这一条语义。
        /// <para>每个 lockType 缓存上次播报的值：同一 (主体,类型) 的聚合值对所有订阅者都一样，但缓存放在
        /// 订阅项上最省事——各自比较即可，不必新增共享表，也就不引入淘汰问题。类型被锁过才会进表，
        /// 随订阅释放一起丢弃。</para>
        /// </summary>
        private sealed class LockStateSubscription : IDisposable
        {
            private readonly ILockable _subject;
            private readonly Action<int, bool> _handler;
            private readonly Dictionary<int, bool> _lastValues = new Dictionary<int, bool>();

            private IDisposable _onLocked;
            private IDisposable _onUnlocked;
            private bool _disposed;

            internal LockStateSubscription(ILockable subject, Action<int, bool> handler)
            {
                _subject = subject;
                _handler = handler;
            }

            /// <summary>挂上两条原始订阅，再播报一次当前状态（顺序不能反：先播报会漏掉这中间的边沿）。</summary>
            internal void Start()
            {
                _onLocked = LockManager.OnLocked(_subject, OnEdge);
                _onUnlocked = LockManager.OnUnlocked(_subject, OnEdge);

                LockManager.NotifyCurrentLockedTypes(_subject, OnEdge);
            }

            private void OnEdge(int lockType)
            {
                bool now = LockManager.IsLocked(_subject, lockType);
                if (_lastValues.TryGetValue(lockType, out var last) && last == now)
                    return;

                _lastValues[lockType] = now;
                _handler(lockType, now);
            }

            /// <summary>退订两条原始订阅。幂等：重复调用不再产生效果。</summary>
            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _onLocked?.Dispose();
                _onUnlocked?.Dispose();
                _onLocked = null;
                _onUnlocked = null;
                _lastValues.Clear();
            }
        }

        /// <summary>
        /// 订阅主体的**聚合锁定状态**变化：只在 <see cref="IsLocked"/> 的返回值真的翻转时回调。
        /// <para><b>与 <see cref="OnLocked"/>/<see cref="OnUnlocked"/> 的分工</b>：那两个是每个 (主体,类型)
        /// 集合的**边沿**，会与聚合不一致——全局锁释放时，仍被自己的锁挡住的主体照样收到
        /// <c>OnUnlocked</c>。订阅者照边沿维护「现在能不能动」这类镜像会显示错状态；本事件以聚合为准，
        /// 订阅者不必自己再查一次 <see cref="IsLocked"/>。</para>
        /// <para><b>订阅即回调</b>：订阅时立即同步播报该主体当前处于锁定状态的每个 lockType（值为 true）。
        /// 未锁定的类型不会被回调——lockType 是开放的 <c>int</c> 域，无从枚举「所有为假的类型」，
        /// 订阅者把「没收到」当作 false 即可。</para>
        /// <para>回调参数为 (lockType, isLocked)。返回 <see cref="IDisposable"/>，调用 <c>Dispose()</c> 退订。</para>
        /// </summary>
        /// <param name="subject">锁主体；null 表示全局（与 <see cref="OnLocked"/> 一致）。</param>
        /// <param name="handler">回调，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 null 时抛出。</exception>
        public static IDisposable OnLockStateChanged(ILockable subject, Action<int, bool> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler), "handler cannot be null.");

            var subscription = new LockStateSubscription(subject ?? Global, handler);
            subscription.Start();
            return subscription;
        }

        #endregion

        #region AddLock

        /// <summary>
        /// 请求一个针对特定 <see cref="ILockable"/> 的锁。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// <para>全局锁请使用 <see cref="Global"/> 作为 lockSubject。</para>
        /// <para><b>唯一的失败形态</b>：主体已销毁（<see cref="AutoReleaseOnDestroy"/> 生效且其销毁令牌
        /// 已取消）时本调用被忽略——返回的句柄不持有锁（<see cref="LockHandle.IsHeld"/> 为 <c>false</c>）
        /// 并记一条 <c>[Lock]</c> 告警。除此之外加锁不会失败（同一主体同类型是持有者集合，可叠加）。</para>
        /// </summary>
        public static LockHandle AddLock(ILockable lockSubject, int lockType, object lockObj)
        {
            if (lockObj == null)
                throw new ArgumentNullException(nameof(lockObj), "lock cannot be null.");

            ILockable subjectKey = lockSubject ?? Global;

            if (IsSubjectDestroyed(subjectKey))
            {
                Debug.LogWarning($"[Lock] AddLock ignored: subject '{NameOf(subjectKey)}' has already been destroyed " +
                                 "(its destroy token is cancelled). Set LockManager.AutoReleaseOnDestroy to false to opt out.");
                return default;
            }

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
                DispatchSafely(OnGlobalLocked, subjectKey, lockType, lockObj, nameof(OnGlobalLocked));
                NotifyOnLocked(lockSubject, lockType, lockObj);
            }

            EnsureDestroyBinding(subjectKey);

            return new LockHandle(lockSubject, lockType, lockObj);
        }

        /// <summary>
        /// 请求一个全局锁（lockSubject 自动设为 <see cref="Global"/>）。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放。</para>
        /// </summary>
        public static LockHandle AddLock(int lockType, object lockObj)
            => AddLock(Global, lockType, lockObj);

        #endregion

        #region RemoveLock

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
                DispatchSafely(OnGlobalUnlocked, subjectKey, lockType, lockObj, nameof(OnGlobalUnlocked));
                NotifyOnUnlocked(lockSubject, lockType, lockObj);

                typeDict.Remove(lockType);
                if (typeDict.Count == 0)
                {
                    _locks.Remove(subjectKey);
                    TryUnbindDestroyBinding(subjectKey);
                }
            }
        }

        /// <summary>
        /// 释放一个全局锁（lockSubject 自动设为 <see cref="Global"/>）。
        /// </summary>
        public static void RemoveLock(int lockType, object lockObj)
            => RemoveLock(Global, lockType, lockObj);

        /// <summary>
        /// 释放该主体的**全部**锁（所有类型），返回释放的锁数量。
        /// <para>与逐把 <see cref="RemoveLock"/> 的差别只在载荷：整批摘除后每类型只派发一次解锁边沿，
        /// 事件里的 <c>lockObj</c> 为 <c>null</c>（一次清掉多把，没有单一的代表对象）。</para>
        /// <para><b>先整块摘下再派发</b>：摘除阶段不跑用户代码；派发遍历的是已摘下的副本，故回调里再加锁
        /// 不会打坏本轮清理（那些落进新的条目，与本轮无关）。</para>
        /// <para>主体传 <see cref="Global"/> 时清空全局锁——合法用法。</para>
        /// </summary>
        public static int RemoveAllLocks(ILockable subject)
        {
            ILockable subjectKey = subject ?? Global;

            if (!_locks.TryGetValue(subjectKey, out var typeDict))
                return 0;

            _locks.Remove(subjectKey);

            int removed = 0;
            foreach (var kvp in typeDict)
            {
                if (kvp.Value.Count == 0)
                    continue;

                removed += kvp.Value.Count;

                DispatchSafely(OnGlobalUnlocked, subjectKey, kvp.Key, null, nameof(OnGlobalUnlocked));
                DispatchSubscribers(_onUnlockedSubjects, subjectKey, kvp.Key, nameof(OnUnlocked));
            }

            TryUnbindDestroyBinding(subjectKey);
            return removed;
        }

        /// <summary>
        /// 丢弃该主体的全部订阅（加锁/解锁/聚合状态三张表都覆盖）。
        /// <para>不返回数量：多播委托的条数要物化调用列表（一次分配），而调用方关心的是「不再有回调」。</para>
        /// <para>调用方手上的旧句柄随后 <c>Dispose</c> 是安全的——表里已无该键，退订退化为空操作。</para>
        /// <para>不影响该主体的锁：锁与订阅分开清理，正是为了让「只清订阅」这件事可表达。</para>
        /// </summary>
        public static void RemoveAllSubscriptions(ILockable subject)
        {
            ILockable subjectKey = subject ?? Global;

            _onLockedSubjects.Remove(subjectKey);
            _onUnlockedSubjects.Remove(subjectKey);

            TryUnbindDestroyBinding(subjectKey);
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
        /// <para>销毁绑定一并注销（否则上一轮的令牌回调会在下一轮改容器——跟踪器那类「自己成为泄漏源」
        /// 的同型问题），<see cref="AutoReleaseOnDestroy"/> 复位为 <c>true</c>。</para>
        /// <para>主要用于单元测试隔离，生产环境中通常不需要调用此方法。</para>
        /// </summary>
        public static void Dispose()
        {
            foreach (var registration in _destroyBindings.Values)
                registration.Dispose();

            _destroyBindings = new Dictionary<ILockable, CancellationTokenRegistration>(LockSubjectReferenceComparer.Instance);
            _locks = new Dictionary<ILockable, Dictionary<int, HashSet<object>>>(LockSubjectReferenceComparer.Instance);
            _onLockedSubjects = new Dictionary<ILockable, Action<int>>(LockSubjectReferenceComparer.Instance);
            _onUnlockedSubjects = new Dictionary<ILockable, Action<int>>(LockSubjectReferenceComparer.Instance);
            OnGlobalLocked = null;
            OnGlobalUnlocked = null;
            AutoReleaseOnDestroy = true;
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

            // 键可能已被批量清理或主体销毁摘掉——不能用索引器（键缺失会抛 KeyNotFoundException），
            // 而调用方手里这个句柄是合法的，退订退化成空操作才对
            if (_targetDict.TryGetValue(_subject, out var current))
            {
                var next = current - _handler;
                if (next == null)
                    _targetDict.Remove(_subject);
                else
                    _targetDict[_subject] = next;

                LockManager.TryUnbindDestroyBinding(_subject);
            }

            // 清空引用，归还至池中
            _subject = null;
            _handler = null;
            _targetDict = null;
            _pool.Push(this);
        }
    }

    /// <summary>
    /// 空句柄：订阅被短路（主体已销毁）时返回的共享实例。
    /// <para><b>不得池化</b>：共享实例一旦被归还进池再发放，后来的持有者会以为自己拿到了可用的句柄
    /// （同 <c>XMessage.Internal.ActionDisposable.Empty</c> 的三条不变量；那份在别的模块的 Internal 里，
    /// 按仓内「不为十行适配器建立跨模块实现依赖」的既定取舍，本模块自持一份）。</para>
    /// </summary>
    internal sealed class EmptyDisposable : IDisposable
    {
        /// <summary>共享实例：释放动作是空操作，且 <see cref="Dispose"/> 幂等。</summary>
        internal static readonly IDisposable Instance = new EmptyDisposable();

        private EmptyDisposable()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 主体键的**引用同一**比较器：四个容器（锁、两张订阅表、销毁绑定）共用。
    /// <para><b>为什么需要它</b>：游戏实体按 Id 重写 <c>Equals</c> 很常见。用默认比较器时，两个
    /// **不同**实体只要值相等就是同一个键——给 A 加锁连 B 一起锁、A 的订阅被 B 的锁惊动、
    /// <c>RemoveAllLocks(A)</c> 把 B 的锁一并清掉，全是静默的。</para>
    /// <para>仓内先例：<c>XPool.Pool&lt;T&gt;.InstanceReferenceComparer</c> 与
    /// <c>UpdateScheduler.NodeReferenceComparer</c>（后者注释明写「身份判定用引用同一」）；
    /// .NET Standard 2.1 下没有现成的 <c>ReferenceEqualityComparer</c>，故自持一份。</para>
    /// <para><b>代价</b>：<see cref="ILockable"/> 若由 struct 实现，每次装箱都是新身份、键永不相等
    /// ——因此接口文档写明必须由引用类型实现。</para>
    /// </summary>
    internal sealed class LockSubjectReferenceComparer : IEqualityComparer<ILockable>
    {
        internal static readonly LockSubjectReferenceComparer Instance = new LockSubjectReferenceComparer();

        private LockSubjectReferenceComparer()
        {
        }

        public bool Equals(ILockable x, ILockable y) => ReferenceEquals(x, y);

        public int GetHashCode(ILockable obj) => RuntimeHelpers.GetHashCode(obj);
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
