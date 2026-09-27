using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XPool
{
    /// <summary>
    /// 非泛型归还入口。
    /// <para>只服务 <see cref="PoolManager.Return{T}"/>：池按类型定键，编译期类型查不到时得改用实例的运行时类型
    /// 再试一次，而那一刻手里只有一个 <c>object</c>。由 <see cref="Pool{T}"/> 显式实现，不对外暴露。</para>
    /// </summary>
    internal interface IUntypedPool
    {
        /// <summary>
        /// 把实例归还到本池；实例类型与本池不符时静默忽略。
        /// <para>调用方是按 <c>GetType()</c> 找过来的，正常情况下不会不符——这道判断只是让入口自洽。</para>
        /// </summary>
        /// <param name="item">要归还的实例</param>
        void ReturnUntyped(object item);
    }

    /// <summary>
    /// 泛型对象池。
    /// <para>线程不安全，应在主线程使用。</para>
    /// <para>GC 友好：内部使用 <see cref="Stack{T}"/> 存储闲置实例，预分配容量，无装箱。</para>
    /// <para>回调优先级：委托 > <see cref="IPoolable"/> 接口。同时存在时仅调用委托。</para>
    /// <para><typeparamref name="T"/> 限定为引用类型（<c>where T : class</c>）：实例复用依赖引用同一性，
    /// 值类型会被复制，池化没有意义。</para>
    /// </summary>
    /// <typeparam name="T">池中存储的对象类型</typeparam>
    /// <example>
    /// <code>
    /// // 直接构造
    /// var pool = new Pool&lt;MyData&gt;(() => new MyData(), new PoolConfig { PrewarmSize = 10 });
    /// var item = pool.Get();
    /// pool.Return(item);
    /// </code>
    /// </example>
    public sealed class Pool<T> : IPool<T>, IDisposable, IUntypedPool where T : class
    {
        private readonly Stack<T> _stack;
        private readonly Func<T> _generator;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly Action<T> _onDestroy;
        private readonly int _maxSize;
        private int _totalCreated;
        private int _activeCount;

#if UNITY_EDITOR
        private readonly HashSet<T> _activeSet;
#endif

        /// <inheritdoc />
        public int CountInactive => _stack.Count;

        /// <inheritdoc />
        public int CountAll => _totalCreated;

        /// <inheritdoc />
        public int CountActive => _activeCount;

        /// <summary>
        /// 创建泛型对象池。
        /// </summary>
        /// <param name="generator">无参构造器，池空时调用</param>
        /// <param name="config">池配置，留空则用 <see cref="PoolConfig.Default"/>
        /// （无预热、不限容量、Editor 下启用重复归还检测）</param>
        /// <param name="onRent">取出时的委托回调（可选，优先于 <see cref="IPoolable.OnRent"/>）</param>
        /// <param name="onReturn">归还时的委托回调（可选，优先于 <see cref="IPoolable.OnReturn"/>）</param>
        /// <param name="onDestroy">实例因池满被丢弃时的委托回调（可选，优先于 <see cref="IPoolDiscardable.OnDiscard"/>）。
        /// 归还回调仍会先触发——它负责「清理状态」，丢弃回调负责「释放资源」</param>
        public Pool(
            Func<T> generator,
            PoolConfig? config = null,
            Action<T> onRent = null,
            Action<T> onReturn = null,
            Action<T> onDestroy = null)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));

            // 用 PoolConfig.Default 而非 default(PoolConfig)：后者 CollectionCheck 为 false，
            // 会让「直接 new」与「经 PoolManager」两条路径的调试安全性不同
            var cfg = config ?? PoolConfig.Default;

            _maxSize = cfg.MaxSize > 0 ? cfg.MaxSize : int.MaxValue;
            _onRent = onRent;
            _onReturn = onReturn;
            _onDestroy = onDestroy;

            // 预热数量与栈容量一并钳到上限：上限是「池里最多留多少」，预热是「要提前造多少」，
            // 前者没理由被后者压过——否则一建池闲置数就超限，多造的实例还会白占栈容量
            int prewarm = Math.Clamp(cfg.PrewarmSize, 0, _maxSize);
            _stack = new Stack<T>(prewarm > 0 ? prewarm : 8);

#if UNITY_EDITOR
            if (cfg.CollectionCheck)
                _activeSet = new HashSet<T>(InstanceReferenceComparer.Instance);
#endif

            // 预热
            for (int i = 0; i < prewarm; i++)
            {
                var item = _generator();
                _totalCreated++;
                _stack.Push(item);
            }
        }

        /// <summary>
        /// 以 using 方式获取实例，并在 using 块结束时自动归还。
        /// <para>返回的 <see cref="PooledObject{T}"/> 是值类型（struct），零 GC。</para>
        /// </summary>
        /// <param name="item">从池中取出的实例</param>
        /// <returns>实现 <see cref="IDisposable"/> 的包装器，用于 using 语句</returns>
        public PooledObject<T> GetPooled(out T item)
        {
            item = Get();
            return new PooledObject<T>(this, item);
        }

        /// <summary>
        /// 获取一个实例。池空时自动调用生成器新建。
        /// </summary>
        public T Get()
        {
            T item;
            if (_stack.Count > 0)
            {
                item = _stack.Pop();
            }
            else
            {
                item = _generator();
                _totalCreated++;
            }

            // 每次取出都进入活跃态：从栈弹出与新建皆是
            _activeCount++;

#if UNITY_EDITOR
            _activeSet?.Add(item);
#endif

            // 委托优先，其次接口
            if (_onRent != null)
                _onRent(item);
            else if (item is IPoolable poolable)
                poolable.OnRent();

            return item;
        }

        /// <summary>
        /// 归还实例。池满时丢弃。
        /// </summary>
        public void Return(T item)
        {
            if (item == null) return;

#if UNITY_EDITOR
            if (_activeSet != null && !_activeSet.Remove(item))
            {
                Debug.LogError(
                    $"[Pool<{typeof(T).Name}>] Return() 传入的对象并非从本池租出，或已被重复归还。已忽略此操作。");
                return;
            }
#endif

            // 委托优先，其次接口
            if (_onReturn != null)
                _onReturn(item);
            else if (item is IPoolable poolable)
                poolable.OnReturn();

            // 归还即离开活跃态：无论接下来是入栈复用还是超容丢弃，都不再有人持有它。
            // 钳在 0 是因为 CollectionCheck 关闭时的重复归还会走到这里两次，不该把计数减成负数。
            if (_activeCount > 0)
                _activeCount--;

            if (_stack.Count < _maxSize)
            {
                _stack.Push(item);
            }
            else
            {
                // 超出容量：实例不会再有被复用的机会，交给丢弃回调释放资源（委托优先，其次接口）
                if (_onDestroy != null)
                    _onDestroy(item);
                else if (item is IPoolDiscardable discardable)
                    discardable.OnDiscard();
            }
        }

        /// <summary>
        /// 清空池内所有闲置实例。
        /// <para>已取出的活跃实例不受影响，但 <see cref="Return"/> 时会重新入池。</para>
        /// <para>Editor 下的活跃追踪集保留（已租出实例归还时仍能通过重复归还检测）；
        /// 内部栈容量保留，以便后续归还复用时不再扩容。</para>
        /// </summary>
        public void Clear()
        {
            _stack.Clear();
        }

        /// <summary>
        /// 显式实现 <see cref="IDisposable"/>：等价 <see cref="Clear"/>，仅供统一清理路径
        /// （如 <see cref="PoolManager.ClearAll"/>）按接口遍历调用。
        /// <para>可重复调用，调用后池仍可继续使用，不应被理解为「销毁池」。</para>
        /// </summary>
        void IDisposable.Dispose() => Clear();

        /// <inheritdoc />
        void IUntypedPool.ReturnUntyped(object item)
        {
            if (item is T typed)
                Return(typed);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 以<b>引用同一</b>判定实例身份的相等比较器。
        /// <para><b>为什么必须指定：</b>活跃追踪集要回答的是「是不是同一个实例」，而
        /// <see cref="HashSet{T}"/> 默认走 <see cref="EqualityComparer{T}.Default"/>——重写了
        /// <c>Equals</c>/<c>GetHashCode</c> 的池化类型（POCO、记录式数据类）会被判成同一个。
        /// 后果不是报错而是<b>合法归还被误拒</b>：两个逻辑相等但引用不同的实例先后归还时，第二次
        /// <c>Remove</c> 落空，于是记一条「重复归还」错误日志并把实例丢给 GC。</para>
        /// <para>按引用判定不会削弱对真正重复归还的检出——同一实例的第二次 <c>Remove</c> 同样落空；
        /// 它只是让这把尺子与「池化复用依赖引用同一性」的语义对齐。
        /// （.NET 5 的 <c>ReferenceEqualityComparer</c> 在本项目的 API 级别 .NET Standard 2.1 下不可用。）</para>
        /// </summary>
        private sealed class InstanceReferenceComparer : IEqualityComparer<T>
        {
            public static readonly InstanceReferenceComparer Instance = new InstanceReferenceComparer();

            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
#endif
    }
}