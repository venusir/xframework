using System;
using System.Collections;
using System.Collections.Generic;
using XFramework.XEvent;

namespace XFramework.XReactive
{
    /// <summary>
    /// 响应式字典：可订阅增删改的字典（按键寻址的集合型响应式）。基于 XEvent 事件流引擎实现，
    /// 变更以 <see cref="ReactiveDictionaryChange{TKey, TValue}"/> 单流投递（零 GC 载荷）。
    /// <para>适用于实体 id → 状态、槽位 → 内容、叠加状态 → 层数、资源 id → 计数这类
    /// 「按键寻址、变了要告诉别人」的场景。不依赖任何场景对象，可在任意 C# 类中使用。
    /// 实现 <see cref="IReactiveDictionary{TKey, TValue}"/> 接口，只读视图就是该接口本身
    /// （写面只在本类）。使用完毕后需调用 <see cref="Dispose"/>。</para>
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    /// <remarks>
    /// 行为契约:
    /// - 通知时字典<b>已完成变更</b>：收到任何变更后<b>重读字典</b>即可，不要用增量拼镜像
    /// - 索引器 <c>set</c> 是 <b>upsert</b>：新键发 <see cref="ReactiveCollectionChangeKind.Add"/>、
    ///   已有键发 <see cref="ReactiveCollectionChangeKind.Replace"/>；与当前值<b>相等则什么都不发生</b>
    ///   ——不发通知，且<b>连实例都不替换</b>（与 <see cref="ReactiveList{T}"/> 的索引器同法）
    /// - <see cref="Clear"/> 非空才发一条 <see cref="ReactiveCollectionChangeKind.Reset"/>；
    ///   <see cref="Remove"/> 未命中返回 false 且静默——增删改<b>逐项</b>通知，
    ///   批量装载请走接受 <c>IEnumerable&lt;KeyValuePair&lt;TKey, TValue&gt;&gt;</c> 的构造（静默、不发通知）
    /// - <b>遍历顺序不作承诺</b>（不保证插入序）
    /// - <see cref="Subscribe"/> <b>不做立即回调</b>（与 <see cref="IReactiveProperty{T}.Subscribe"/> 相反）：
    ///   订阅后立即读一次字典即为一致状态
    /// - <b>回调里不要改动本字典</b>：改动会嵌套派发（嵌套那一轮先跑完），此后外层把载荷继续投出时
    ///   那个键可能已被移除——键是稳定的，条目不是；回调里遍历本字典也不安全（他人改动使枚举器失效）。
    ///   回调里<b>订阅与退订是安全的</b>（引擎语义：退订同轮立即生效）
    /// - <see cref="Dispose"/> 后<b>读取</b>（索引器 / <see cref="TryGetValue"/> / <see cref="Count"/> /
    ///   <see cref="Keys"/> / 枚举）仍返回最后状态（宽容读取）；<b>写入</b>与再次 <see cref="Subscribe"/>
    ///   抛 <see cref="ObjectDisposedException"/>
    /// </remarks>
    public sealed class ReactiveDictionary<TKey, TValue> : IReactiveDictionary<TKey, TValue>, IDisposable
    {
        #region Private Fields

        private readonly Dictionary<TKey, TValue> _items;
        private readonly IEventStream<ReactiveDictionaryChange<TKey, TValue>> _stream
            = EventStream.Create<ReactiveDictionaryChange<TKey, TValue>>();
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建空字典，使用默认键比较器。
        /// </summary>
        public ReactiveDictionary()
        {
            _items = new Dictionary<TKey, TValue>();
        }

        /// <summary>
        /// 创建空字典并预分配容量。
        /// </summary>
        /// <param name="capacity">初始容量。</param>
        public ReactiveDictionary(int capacity)
        {
            _items = new Dictionary<TKey, TValue>(capacity);
        }

        /// <summary>
        /// 用现有键值对创建字典。<b>静默装载：不发通知</b>——这是批量装载的正解。
        /// </summary>
        /// <param name="items">初始键值对。</param>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="items"/> 含重复键时抛出（<see cref="Dictionary{TKey, TValue}"/> 语义）。</exception>
        public ReactiveDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new Dictionary<TKey, TValue>(items);
        }

        /// <summary>
        /// 创建空字典并指定<b>键比较器</b>——大小写不敏感、引用相等、自定义相等语义都从这里注入。
        /// <para>引用类型作键时尤其值得注意：默认比较器走 <c>Equals</c>，两个「不同实例但相等」的对象会被
        /// 视为同一个键；要按实例区分请传引用比较器。</para>
        /// </summary>
        /// <param name="comparer">键比较器；<c>null</c> 等价 <see cref="EqualityComparer{TKey}.Default"/>。</param>
        public ReactiveDictionary(IEqualityComparer<TKey> comparer)
        {
            _items = new Dictionary<TKey, TValue>(comparer);
        }

        /// <summary>
        /// 用现有键值对创建字典并指定键比较器。
        /// </summary>
        /// <param name="items">初始键值对。</param>
        /// <param name="comparer">键比较器；<c>null</c> 等价 <see cref="EqualityComparer{TKey}.Default"/>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="items"/> 含重复键时抛出。</exception>
        public ReactiveDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items, IEqualityComparer<TKey> comparer)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new Dictionary<TKey, TValue>(items, comparer);
        }

        #endregion

        #region Public Properties

        /// <summary>当前键值对数量。</summary>
        public int Count => _items.Count;

        /// <summary>
        /// 按键读写值。
        /// <para>get：键不存在时抛 <see cref="KeyNotFoundException"/>（<see cref="IReadOnlyDictionary{TKey, TValue}"/>
        /// 语义）；不想抛请用 <see cref="TryGetValue"/>。</para>
        /// <para>set：<b>upsert</b>——新键发一条 <see cref="ReactiveCollectionChangeKind.Add"/>，
        /// 已有键发一条 <see cref="ReactiveCollectionChangeKind.Replace"/>；与当前值<b>相等则什么都不发生</b>
        /// （不发通知、也不替换实例；相等按 <see cref="EqualityComparer{TValue}.Default"/>）。</para>
        /// </summary>
        /// <param name="key">键。</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> 为 null 时抛出（比较器也救不了）。</exception>
        /// <exception cref="ObjectDisposedException"><b>写入</b>已释放的字典时抛出；读取不抛。</exception>
        public TValue this[TKey key]
        {
            get => _items[key];
            set
            {
                ThrowIfDisposed();
                if (_items.TryGetValue(key, out var oldValue))
                {
                    // 去重语义:与当前值相等即空操作,不发通知、也不替换实例(与列表索引器同法)
                    if (EqualityComparer<TValue>.Default.Equals(oldValue, value))
                        return;
                    _items[key] = value;
                    _stream.Emit(new ReactiveDictionaryChange<TKey, TValue>(
                        ReactiveCollectionChangeKind.Replace, key, value, oldValue));
                    return;
                }

                _items.Add(key, value);
                _stream.Emit(new ReactiveDictionaryChange<TKey, TValue>(
                    ReactiveCollectionChangeKind.Add, key, value, default));
            }
        }

        /// <summary>
        /// 键比较器（构造注入的实例，或 <see cref="EqualityComparer{TKey}.Default"/>）。
        /// <para><b>这是具体类的事实，接口不承诺</b>——<see cref="IReactiveDictionary{TKey, TValue}"/> 继承的
        /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> 本身不带比较器。</para>
        /// </summary>
        public IEqualityComparer<TKey> Comparer => _items.Comparer;

        /// <summary>
        /// 所有键（<b>活视图</b>：改动会使其枚举器失效；<b>顺序不作承诺</b>）。
        /// </summary>
        public IEnumerable<TKey> Keys => _items.Keys;

        /// <summary>
        /// 所有值（<b>活视图</b>：改动会使其枚举器失效；<b>顺序不作承诺</b>）。
        /// </summary>
        public IEnumerable<TValue> Values => _items.Values;

        /// <summary>
        /// 当前存活订阅数。
        /// <para><b>诊断用</b>——订阅泄漏在运行时是无声的，本属性是唯一的观察手段：字典长期存活时
        /// 这个数只增不减，即说明有订阅没人释放。</para>
        /// <para>仅供排查，<b>不要</b>拿它做逻辑分支：它是实现的当前状态，不是契约。</para>
        /// </summary>
        public int SubscriptionCount => _stream.SubscriptionCount;

        #endregion

        #region Public API

        /// <summary>
        /// 追加一个键值对并发一条 <see cref="ReactiveCollectionChangeKind.Add"/>。
        /// <para><b>键已存在时抛 <see cref="ArgumentException"/></b>（<see cref="Dictionary{TKey, TValue}"/>
        /// 镜像）——「存在就覆盖」是索引器的语义，两者刻意分开。</para>
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">键已存在时抛出（不留下半完成状态）。</exception>
        /// <exception cref="ObjectDisposedException">字典已释放时抛出。</exception>
        public void Add(TKey key, TValue value)
        {
            ThrowIfDisposed();
            // 重复键 / null 键在这里抛,不会留下半完成状态
            _items.Add(key, value);
            _stream.Emit(new ReactiveDictionaryChange<TKey, TValue>(
                ReactiveCollectionChangeKind.Add, key, value, default));
        }

        /// <summary>
        /// 按键移除并发一条 <see cref="ReactiveCollectionChangeKind.Remove"/>（载荷带<b>被移除的值</b>）。
        /// <para>键不存在则什么都不发生、不发通知。</para>
        /// </summary>
        /// <param name="key">要移除的键。</param>
        /// <returns>确实移除了返回 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">字典已释放时抛出。</exception>
        public bool Remove(TKey key)
        {
            ThrowIfDisposed();
            if (!_items.TryGetValue(key, out var removed))
                return false;
            _items.Remove(key);
            _stream.Emit(new ReactiveDictionaryChange<TKey, TValue>(
                ReactiveCollectionChangeKind.Remove, key, removed, default));
            return true;
        }

        /// <summary>
        /// 清空字典。<b>非空时</b>发一条 <see cref="ReactiveCollectionChangeKind.Reset"/>；
        /// 空表清空不发通知（无变化即无通知）。
        /// </summary>
        /// <exception cref="ObjectDisposedException">字典已释放时抛出。</exception>
        public void Clear()
        {
            ThrowIfDisposed();
            if (_items.Count == 0)
                return;
            _items.Clear();
            _stream.Emit(new ReactiveDictionaryChange<TKey, TValue>(
                ReactiveCollectionChangeKind.Reset, default, default, default));
        }

        /// <summary>键是否存在。</summary>
        /// <param name="key">键。</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> 为 null 时抛出。</exception>
        public bool ContainsKey(TKey key) => _items.ContainsKey(key);

        /// <summary>取键对应的值；不存在返回 false（不抛）。</summary>
        /// <param name="key">键。</param>
        /// <param name="value">键对应的值；不存在为 default。</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> 为 null 时抛出。</exception>
        public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value);

        /// <summary>
        /// 枚举键值对。<b>遍历顺序不作承诺</b>。
        /// <para><b>零 GC</b>：返回 <c>Dictionary&lt;TKey, TValue&gt;</c> 的结构体枚举器——直接用本类型
        /// <c>foreach</c> 零分配、不装箱（与 BCL 同法）；<b>经接口</b>（<see cref="IReadOnlyDictionary{TKey, TValue}"/>
        /// 等）<c>foreach</c> 仍会装箱一次枚举器，无法避免。</para>
        /// <para><see cref="Keys"/> / <see cref="Values"/> 是 <c>Dictionary</c> 的投影视图：
        /// <b>每次访问该属性都会分配一个视图对象</b>，遍历它们另有装箱——热路径请直接用本类型的
        /// <c>foreach</c>，或用 <see cref="TryGetValue"/> / 索引器。</para>
        /// </summary>
        public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => _items.GetEnumerator();

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        #endregion

        #region Subscribe

        /// <summary>
        /// 订阅变更。<b>不做立即回调</b>——当前状态直接读字典（订阅后立即读一次即为一致状态）。
        /// <para>返回的 <see cref="IDisposable"/> 可用于手动取消订阅；
        /// 调用 <see cref="Dispose"/> 时也会自动取消所有订阅。</para>
        /// </summary>
        /// <param name="handler">变更回调。</param>
        /// <returns>订阅句柄，可用于取消订阅。</returns>
        /// <exception cref="ArgumentNullException">handler 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">字典已释放时抛出。</exception>
        public IDisposable Subscribe(Action<ReactiveDictionaryChange<TKey, TValue>> handler)
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
                    $"[Reactive] ReactiveDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}> 已释放,请勿再写入或订阅。");
        }

        #endregion

        #region Object

        /// <summary>
        /// 诊断展示：类型与键值对数量（如 <c>ReactiveDictionary&lt;String, Int32&gt;(Count=3)</c>）。
        /// 订阅泄漏请另看 <see cref="SubscriptionCount"/>。
        /// <para>宽容读取：已释放后仍展示最后状态，不抛异常。</para>
        /// </summary>
        public override string ToString()
        {
            return $"{nameof(ReactiveDictionary<TKey, TValue>)}<{typeof(TKey).Name}, {typeof(TValue).Name}>(Count={_items.Count})";
        }

        #endregion
    }
}
