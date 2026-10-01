using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XEvent.Internal
{
    /// <summary>
    /// <see cref="IEventStream{T}"/> 的默认实现:锁 + 快照的事件流。
    /// <para>订阅即注册一个回调;投递时对所有存活订阅回调(后订阅先收到)。</para>
    /// </summary>
    /// <remarks>
    /// 线程模型:锁 + 快照。
    /// - 链表结构(SubscriptionNode)与 completed 状态由 _sync 锁保护
    /// - Emit 在锁内收集存活节点快照,锁外逐个调用 handler,避免在持锁状态调用用户代码(防锁序反转)
    /// - 派发中退订:节点置 Disposed 标志,快照遍历时跳过(安全);已完成派发的节点由下一次 Emit 的快照收集剔除
    /// - 重入 Emit:递归快照,通过 _publishDepth 计数禁止派发中回池(防节点复用导致 ABA)
    /// 异常语义:订阅回调异常被捕获并记 Error 日志,不传播给 Emit 调用方;
    /// 异常订阅者不被移除,同一轮遍历中后续订阅者照常收到消息。
    /// <para>
    /// 子类化约定:基类与派生类(BufferedEventStreamImpl)各持一把锁,两者永远顺序获取、绝不嵌套
    /// (派生方法先完成自己的锁内工作并离开锁,再调用 base 实现)。后续维护者不得为「省一把锁」
    /// 而让派生类复用基类的 _sync —— 那会使锁内调用用户代码与锁序反转同时复活。
    /// </para>
    /// <para>
    /// <b>接口必须隐式实现</b>(不是 <c>void IEventStream&lt;T&gt;.Emit</c> 那种显式实现):
    /// 派生类 override 的是本类的方法,经 <see cref="IEventStream{T}"/> 引用的调用必须一并落到派生实现上,
    /// 否则缓冲流永远不写缓存、重放静默失效。用例 <c>BufferedEventStream_ThroughInterfaceReference_StillCaches</c> 锁这条。
    /// </para>
    /// </remarks>
    internal class EventStreamImpl<T> : IEventStream<T>
    {
        #region Private Fields

        private readonly object _sync = new object();
        private readonly Action _onEmpty;
        private SubscriptionNode<T> _head;
        private int _publishDepth;
        private int _subscriptionCount;

        /// <summary>
        /// 终止标志。写入恒在 _sync 锁内;标为 volatile 是因为派生类需要无锁读取
        /// (见 <see cref="IsCompleted"/>),避免每次投递都多取一把锁。
        /// </summary>
        private volatile bool _completed;

        #endregion

        #region Lifecycle

        /// <summary>创建事件流。</summary>
        /// <param name="onEmpty">
        /// 「订阅清零」通知(见 <see cref="EventStream.Create{T}"/>);<c>null</c> 表示不参与上层回收。
        /// 构造期一次性接线,订阅 / 退订路径零额外分配。
        /// </param>
        internal EventStreamImpl(Action onEmpty) => _onEmpty = onEmpty;

        #endregion

        #region IEventStream

        /// <inheritdoc/>
        public virtual IDisposable Subscribe(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock (_sync)
            {
                // completed 之后订阅:返回共享空句柄(不再投递;空句柄只有一处来源,见 ActionDisposable.Empty)
                if (_completed)
                    return ActionDisposable.Empty;

                var node = SubscriptionNodePool<T>.Rent();
                node.Set(handler);
                node.Next = _head;
                _head = node;
                _subscriptionCount++;
                SubscriptionTracker.OnSubscribe();
                return new EventSubscription(this, node);
            }
        }

        /// <inheritdoc/>
        public virtual void Emit(T value)
        {
            // 锁内快照:收集当前存活节点到池化 List,锁外逐个调用
            // 不使用节点 Next 字段串快照(会破坏主链表结构),用独立 List
            List<SubscriptionNode<T>> snapshot = null;
            lock (_sync)
            {
                if (_completed)
                    return;

                snapshot = DispatchListPool<SubscriptionNode<T>>.Rent();
                var current = _head;
                while (current != null)
                {
                    if (!current.IsDisposed)
                        snapshot.Add(current);
                    current = current.Next;
                }

                if (snapshot.Count == 0)
                {
                    DispatchListPool<SubscriptionNode<T>>.Return(snapshot);
                    return;
                }
            }

            // 锁外逐个调用(快照顺序 = 链表顺序 = 后订阅先收到)
            Interlocked.Increment(ref _publishDepth);
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var node = snapshot[i];
                    // 派发中退订的节点跳过(IsDisposed 标志由快照外执行退订的一方置位:
                    // 可能是本线程的重入退订,也可能是被容忍的并发退订线程)
                    if (!node.IsDisposed)
                        Deliver(value, node.Handler);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _publishDepth);
                DispatchListPool<SubscriptionNode<T>>.Return(snapshot);
            }
        }

        /// <inheritdoc/>
        public virtual void Complete()
        {
            lock (_sync)
            {
                _completed = true;
            }
        }

        /// <inheritdoc/>
        public virtual void Dispose()
        {
            lock (_sync)
            {
                _completed = true;
                var node = _head;
                _head = null;
                while (node != null)
                {
                    var next = node.Next;
                    node.IsDisposed = true;
                    ReturnNode(node);
                    node = next;
                }
                // holder terminated: drop remaining at once
                SubscriptionTracker.OnUnsubscribe(_subscriptionCount);
                _subscriptionCount = 0;
            }

            // 刻意不回调 _onEmpty:本方法是持有者主动终止流(清理 / 淘汰)的路径,持有者已在自行回收结构,
            // 回调只会在其遍历中改字典。已经发出的订阅句柄此后 Dispose 时,节点因不在链表中而被安全忽略。
        }

        /// <inheritdoc/>
        public int SubscriptionCount => _subscriptionCount;

        #endregion

        #region Protected

        /// <summary>
        /// 事件流是否已终止(<see cref="Complete"/> 或 <see cref="Dispose"/> 之后为 <c>true</c>)。
        /// <para>供派生类在写入自有状态前短路,避免留下永远不会被投递或重放的数据。</para>
        /// <para>无锁读取:已终止的流不会再改变状态,读到 <c>false</c> 但随后被并发终止时,
        /// 最多多写一次自有状态,无正确性影响(本引擎使用场景为主线程)。</para>
        /// </summary>
        protected bool IsCompleted => _completed;

        #endregion

        #region Private

        /// <summary>
        /// 统一投递语义:订阅回调异常隔离(记 Error 日志后继续)。
        /// <para>派生类(缓冲流)的重放路径复用此方法,保证重放与实时行为一致。</para>
        /// </summary>
        internal static void Deliver(T value, Action<T> handler)
        {
            try
            {
                handler(value);
            }
            catch (Exception e)
            {
                LogManager.Error(LogCategories.Event, "EventStream handler threw exception: {0}", e);
            }
        }

        private void ReturnNode(SubscriptionNode<T> node)
        {
            // 派发中(有重入 Emit 快照仍引用该节点)不回池,防复用后旧快照误写
            if (Interlocked.CompareExchange(ref _publishDepth, 0, 0) == 0)
                SubscriptionNodePool<T>.Return(node);
        }

        /// <summary>
        /// 退订入口:从链表移除节点并回池(派发中则仅置标志,由快照遍历跳过)。
        /// <para>
        /// 节点不在本流链表中时直接返回——说明它已被 <see cref="Dispose"/> 回收,
        /// 此刻该节点可能已回池并被其他订阅者租用。若继续置 <c>IsDisposed</c> 或回池,
        /// 会让新订阅者静默失联(标志误置),或让同一节点被池重复发放(重复回池)。
        /// </para>
        /// <para>订阅数归零时在锁外回调 <c>_onEmpty</c>,避免持锁回调持有者而锁序反转。</para>
        /// <para>本方法是 private:唯一调用者是嵌套的 <see cref="EventSubscription"/>(嵌套类型可访问外层私有成员)。</para>
        /// </summary>
        private void Unsubscribe(SubscriptionNode<T> node)
        {
            bool becameEmpty;
            lock (_sync)
            {
                if (!Remove(node))
                    return;

                node.IsDisposed = true;
                _subscriptionCount--;
                SubscriptionTracker.OnUnsubscribe();
                ReturnNode(node);
                becameEmpty = _subscriptionCount == 0;
            }

            if (becameEmpty)
                _onEmpty?.Invoke();
        }

        /// <summary>从链表摘除节点;节点不属于本流时返回 <c>false</c>(链表为空也返回 false)。</summary>
        private bool Remove(SubscriptionNode<T> target)
        {
            var current = _head;
            SubscriptionNode<T> prev = null;
            while (current != null)
            {
                if (current == target)
                {
                    if (prev == null)
                        _head = current.Next;
                    else
                        prev.Next = current.Next;
                    return true;
                }
                prev = current;
                current = current.Next;
            }
            return false;
        }

        private sealed class EventSubscription : IDisposable
        {
            private EventStreamImpl<T> _stream;
            private SubscriptionNode<T> _node;

            public EventSubscription(EventStreamImpl<T> stream, SubscriptionNode<T> node)
            {
                _stream = stream;
                _node = node;
            }

            public void Dispose()
            {
                var s = Interlocked.Exchange(ref _stream, null);
                var n = Interlocked.Exchange(ref _node, null);
                if (s != null && n != null)
                    s.Unsubscribe(n);
            }
        }

        #endregion
    }

    /// <summary>
    /// 订阅链表节点,经静态对象池复用。
    /// <para>池按 T 泛型独立,仅完成回池的节点复用;派发中退订的节点放弃回池(防 ABA)。</para>
    /// </summary>
    internal sealed class SubscriptionNode<T>
    {
        public Action<T> Handler;
        public SubscriptionNode<T> Next;
        public bool IsDisposed;

        public void Set(Action<T> handler)
        {
            Handler = handler;
            Next = null;
            IsDisposed = false;
        }
    }

    /// <summary>SubscriptionNode 静态对象池。</summary>
    internal static class SubscriptionNodePool<T>
    {
        private static readonly Stack<SubscriptionNode<T>> _pool = new();

        public static SubscriptionNode<T> Rent()
        {
            lock (_pool)
            {
                return _pool.Count > 0 ? _pool.Pop() : new SubscriptionNode<T>();
            }
        }

        public static void Return(SubscriptionNode<T> node)
        {
            node.Handler = null;
            lock (_pool)
            {
                _pool.Push(node);
            }
        }
    }
}
