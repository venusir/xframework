using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XPool
{
    /// <summary>
    /// 泛型对象池。
    /// <para>线程不安全，应在主线程使用。</para>
    /// <para>GC 友好：内部使用 <see cref="Stack{T}"/> 存储闲置实例，预分配容量，无装箱。</para>
    /// <para>回调优先级：委托 > <see cref="IPoolable"/> 接口。同时存在时仅调用委托。</para>
    /// </summary>
    /// <typeparam name="T">池中存储的对象类型</typeparam>
    /// <example>
    /// <code>
    /// // 直接构造
    /// var pool = new Pool<MyData>(() => new MyData(), new PoolConfig { PrewarmSize = 10 });
    /// var item = pool.Get();
    /// pool.Return(item);
    /// </code>
    /// </example>
    public sealed class Pool<T> : IPool<T>
    {
        private readonly Stack<T> _stack;
        private readonly Func<T> _generator;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly int _maxSize;
        private int _totalCreated;

#if UNITY_EDITOR
        private readonly HashSet<T> _activeSet;
#endif

        /// <inheritdoc />
        public int CountInactive => _stack.Count;

        /// <inheritdoc />
        public int CountAll => _totalCreated;

        /// <summary>
        /// 创建泛型对象池。
        /// </summary>
        /// <param name="generator">无参构造器，池空时调用</param>
        /// <param name="config">池配置</param>
        /// <param name="onRent">取出时的委托回调（可选，优先于 <see cref="IPoolable.OnRent"/>）</param>
        /// <param name="onReturn">归还时的委托回调（可选，优先于 <see cref="IPoolable.OnReturn"/>）</param>
        public Pool(
            Func<T> generator,
            PoolConfig config = default,
            Action<T> onRent = null,
            Action<T> onReturn = null)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _maxSize = config.MaxSize > 0 ? config.MaxSize : int.MaxValue;
            _onRent = onRent;
            _onReturn = onReturn;
            _stack = new Stack<T>(config.PrewarmSize > 0 ? config.PrewarmSize : 8);

#if UNITY_EDITOR
            if (config.CollectionCheck)
                _activeSet = new HashSet<T>();
#endif

            // 预热
            for (int i = 0; i < config.PrewarmSize; i++)
            {
                var item = _generator();
                _totalCreated++;
                _stack.Push(item);
            }
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

            if (_stack.Count < _maxSize)
            {
                _stack.Push(item);
            }
            // else: 超出容量，丢弃，由 GC 回收
        }

        /// <summary>
        /// 清空池内所有闲置实例。
        /// <para>已取出的活跃实例不受影响，但 <see cref="Return"/> 时会重新入池。</para>
        /// </summary>
        public void Clear()
        {
#if UNITY_EDITOR
            _activeSet?.Clear();
#endif
            _stack.Clear();
        }
    }
}