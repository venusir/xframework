using System;
using System.Collections;
using System.Collections.Generic;
using XFramework.XEvent;

namespace XFramework.XReactive
{
    /// <summary>
    /// 响应式列表：可订阅增删改的列表。基于 XEvent 事件流引擎实现，变更以
    /// <see cref="ReactiveListChange{T}"/> 单流投递（零 GC 载荷）。
    /// <para>适用于怪物存活表、弹幕、队伍编成等「一组东西变了要告诉别人」的场景。
    /// 不依赖任何场景对象，可在任意 C# 类中使用。实现 <see cref="IReactiveList{T}"/> 接口，
    /// 只读视图就是该接口本身（写面只在本类）。使用完毕后需调用 <see cref="Dispose"/>。</para>
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <remarks>
    /// 行为契约:
    /// - 通知时列表<b>已完成变更</b>：收到任何变更后<b>重读列表</b>即可，不要用增量拼镜像
    ///   （索引只在当次通知内有效，见 <see cref="ReactiveListChange{T}"/>）
    /// - <see cref="Clear"/> 与 <see cref="Sort"/> 发一条 <see cref="ReactiveCollectionChangeKind.Reset"/>；
    ///   增删改<b>逐项</b>通知——批量装载请走接受 <c>IEnumerable&lt;T&gt;</c> 的构造（静默、不发通知）
    /// - 无变化不发通知：空表 <see cref="Clear"/>、与旧值相等的索引器赋值都不发；
    ///   但 <see cref="Sort"/> 只要元素不少于 2 个就发 Reset（<b>即使次序恰好未变</b>——检测它要一趟全量比较，不做）
    /// - <see cref="Subscribe"/> <b>不做立即回调</b>（与 <see cref="IReactiveProperty{T}.Subscribe"/> 相反）：
    ///   订阅后立即读一次列表即为一致状态
    /// - <b>回调里不要改动本列表</b>：改动会嵌套派发（嵌套那一轮先跑完），此后外层把按旧索引算出的载荷
    ///   继续投给剩余订阅者（索引可能已指向别的元素）；回调里遍历本列表也不安全——他人改动会让
    ///   <c>foreach</c> 抛 <see cref="InvalidOperationException"/>，而索引 <c>for</c> 不抛、静默读到位移后的元素。
    ///   回调里<b>订阅与退订是安全的</b>（引擎语义：退订同轮立即生效）
    /// - <see cref="Dispose"/> 后<b>读取</b>（索引器 / <see cref="Count"/> / 枚举）仍返回最后状态（宽容读取）；
    ///   <b>写入</b>与再次 <see cref="Subscribe"/> 抛 <see cref="ObjectDisposedException"/>
    /// </remarks>
    public sealed class ReactiveList<T> : IReactiveList<T>, IDisposable
    {
        #region Private Fields

        private readonly List<T> _items;
        private readonly IEventStream<ReactiveListChange<T>> _stream = EventStream.Create<ReactiveListChange<T>>();
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建空列表。
        /// </summary>
        public ReactiveList()
        {
            _items = new List<T>();
        }

        /// <summary>
        /// 创建空列表并预分配容量。
        /// </summary>
        /// <param name="capacity">初始容量。</param>
        public ReactiveList(int capacity)
        {
            _items = new List<T>(capacity);
        }

        /// <summary>
        /// 用现有元素创建列表。<b>静默装载：不发通知</b>——这是批量装载的正解
        /// （逐项 <see cref="Add"/> 会逐项通知）。
        /// </summary>
        /// <param name="items">初始元素。</param>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> 为 null 时抛出。</exception>
        public ReactiveList(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new List<T>(items);
        }

        #endregion

        #region Public Properties

        /// <summary>当前元素数。</summary>
        public int Count => _items.Count;

        /// <summary>
        /// 按索引读写元素。get 直通底层列表（越界即抛）。
        /// <para>set 是<b>替换</b>：写入后发一条 <see cref="ReactiveCollectionChangeKind.Replace"/>；
        /// 与当前元素<b>相等则不发生任何事、不发通知</b>（去重语义，比较器为
        /// <see cref="EqualityComparer{T}.Default"/>——引用类型元素即引用相等，与
        /// <see cref="ReactiveProperty{T}"/> 的去重同源）。</para>
        /// </summary>
        /// <param name="index">下标。</param>
        /// <exception cref="ObjectDisposedException"><b>写入</b>已释放的列表时抛出；读取不抛。</exception>
        public T this[int index]
        {
            get => _items[index];
            set
            {
                ThrowIfDisposed();
                if (EqualityComparer<T>.Default.Equals(_items[index], value))
                    return;
                var oldItem = _items[index];
                _items[index] = value;
                _stream.Emit(new ReactiveListChange<T>(ReactiveCollectionChangeKind.Replace, index, value, oldItem));
            }
        }

        /// <summary>
        /// 当前存活订阅数。
        /// <para><b>诊断用</b>——订阅泄漏在运行时是无声的，本属性是唯一的观察手段：列表长期存活时
        /// 这个数只增不减，即说明有订阅没人释放。</para>
        /// <para>仅供排查，<b>不要</b>拿它做逻辑分支：它是实现的当前状态，不是契约。</para>
        /// </summary>
        public int SubscriptionCount => _stream.SubscriptionCount;

        #endregion

        #region Public API

        /// <summary>
        /// 追加元素并发一条 <see cref="ReactiveCollectionChangeKind.Add"/>（<see cref="ReactiveListChange{T}.Index"/>
        /// 为追加后的下标）。
        /// </summary>
        /// <param name="item">要追加的元素。</param>
        /// <exception cref="ObjectDisposedException">列表已释放时抛出。</exception>
        public void Add(T item)
        {
            ThrowIfDisposed();
            _items.Add(item);
            _stream.Emit(new ReactiveListChange<T>(ReactiveCollectionChangeKind.Add, _items.Count - 1, item, default));
        }

        /// <summary>
        /// 在指定下标处插入元素并发一条 <see cref="ReactiveCollectionChangeKind.Add"/>。
        /// </summary>
        /// <param name="index">插入位置。</param>
        /// <param name="item">要插入的元素。</param>
        /// <exception cref="ArgumentOutOfRangeException">下标越界时抛出。</exception>
        /// <exception cref="ObjectDisposedException">列表已释放时抛出。</exception>
        public void Insert(int index, T item)
        {
            ThrowIfDisposed();
            _items.Insert(index, item);
            _stream.Emit(new ReactiveListChange<T>(ReactiveCollectionChangeKind.Add, index, item, default));
        }

        /// <summary>
        /// 移除第一个与 <paramref name="item"/> 相等的元素并发一条
        /// <see cref="ReactiveCollectionChangeKind.Remove"/>（载荷带被移除的元素）。
        /// <para>相等按 <see cref="EqualityComparer{T}.Default"/>——引用类型元素即引用相等。
        /// 未命中则什么都不发生、不发通知。</para>
        /// </summary>
        /// <param name="item">要移除的元素。</param>
        /// <returns>确实移除了返回 true。</returns>
        /// <exception cref="ObjectDisposedException">列表已释放时抛出。</exception>
        public bool Remove(T item)
        {
            ThrowIfDisposed();
            var index = _items.IndexOf(item);
            if (index < 0)
                return false;
            var removed = _items[index];
            _items.RemoveAt(index);
            _stream.Emit(new ReactiveListChange<T>(ReactiveCollectionChangeKind.Remove, index, removed, default));
            return true;
        }

        /// <summary>
        /// 移除指定下标处的元素并发一条 <see cref="ReactiveCollectionChangeKind.Remove"/>。
        /// </summary>
        /// <param name="index">要移除的下标。</param>
        /// <exception cref="ArgumentOutOfRangeException">下标越界时抛出（不留下半完成状态）。</exception>
        /// <exception cref="ObjectDisposedException">列表已释放时抛出。</exception>
        public void RemoveAt(int index)
        {
            ThrowIfDisposed();
            // 先取再删:越界在读取处抛出,不留半完成状态
            var removed = _items[index];
            _items.RemoveAt(index);
            _stream.Emit(new ReactiveListChange<T>(ReactiveCollectionChangeKind.Remove, index, removed, default));
        }

        /// <summary>
        /// 清空列表。<b>非空时</b>发一条 <see cref="ReactiveCollectionChangeKind.Reset"/>；
        /// 空表清空不发通知（无变化即无通知）。
        /// </summary>
        /// <exception cref="ObjectDisposedException">列表已释放时抛出。</exception>
        public void Clear()
        {
            ThrowIfDisposed();
            if (_items.Count == 0)
                return;
            _items.Clear();
            _stream.Emit(new ReactiveListChange<T>(ReactiveCollectionChangeKind.Reset, -1, default, default));
        }

        /// <summary>
        /// 排序。元素少于 2 个时静默；否则发一条 <see cref="ReactiveCollectionChangeKind.Reset"/>
        /// ——<b>即使排序后次序恰好未变也发</b>（检测次序变化要一趟全量比较，不做）。
        /// </summary>
        /// <param name="comparer">比较器；<c>null</c> 等价 <see cref="Comparer{T}.Default"/>。</param>
        /// <exception cref="ObjectDisposedException">列表已释放时抛出。</exception>
        public void Sort(IComparer<T> comparer = null)
        {
            ThrowIfDisposed();
            if (_items.Count < 2)
                return;
            _items.Sort(comparer);
            _stream.Emit(new ReactiveListChange<T>(ReactiveCollectionChangeKind.Reset, -1, default, default));
        }

        /// <summary>
        /// 枚举元素。
        /// <para><b>零 GC 提示</b>：本类型<b>不提供</b>无装箱的枚举——<c>foreach</c> 无论直接用还是经
        /// <see cref="IReadOnlyList{T}"/> 接口，都会把 <c>List&lt;T&gt;</c> 的结构体枚举器装箱一次
        /// （对比：<c>List&lt;T&gt;</c> 本身另有公开的结构体 <c>GetEnumerator</c>，直接 <c>foreach</c> 不装箱）。
        /// 热路径请用索引 <c>for</c>。</para>
        /// </summary>
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        #endregion

        #region Subscribe

        /// <summary>
        /// 订阅变更。<b>不做立即回调</b>——当前状态直接读列表（订阅后立即读一次即为一致状态）。
        /// <para>返回的 <see cref="IDisposable"/> 可用于手动取消订阅；
        /// 调用 <see cref="Dispose"/> 时也会自动取消所有订阅。</para>
        /// </summary>
        /// <param name="handler">变更回调。</param>
        /// <returns>订阅句柄，可用于取消订阅。</returns>
        /// <exception cref="ArgumentNullException">handler 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">列表已释放时抛出。</exception>
        public IDisposable Subscribe(Action<ReactiveListChange<T>> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ThrowIfDisposed();
            // 无立即回调,故不存在注册期异常要回滚——裸透传即可(登记由引擎完成)
            return _stream.Subscribe(handler);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放内部事件流，取消所有订阅。<b>不清空元素</b>：此后读取仍返回最后状态（宽容读取）。
        /// <para>此后<b>写入</b>或再次 <see cref="Subscribe"/> 会抛出 <see cref="ObjectDisposedException"/>。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _stream.Dispose();
        }

        #endregion

        #region Private

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name,
                    $"[Reactive] ReactiveList<{typeof(T).Name}> 已释放,请勿再写入或订阅。");
        }

        #endregion

        #region Object

        /// <summary>
        /// 诊断展示：类型与元素数（如 <c>ReactiveList&lt;Int32&gt;(Count=3)</c>）。订阅泄漏请另看
        /// <see cref="SubscriptionCount"/>。
        /// <para>宽容读取：已释放后仍展示最后状态，不抛异常。</para>
        /// </summary>
        public override string ToString()
        {
            return $"{nameof(ReactiveList<T>)}<{typeof(T).Name}>(Count={_items.Count})";
        }

        #endregion
    }
}
