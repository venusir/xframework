using System.Collections.Generic;

namespace XFramework.XPool
{
    /// <summary>
    /// <see cref="HashSet{T}"/> 对象池。
    /// <para><c>Return(set)</c> 时自动调用 <c>set.Clear()</c>，无需手动清空。</para>
    /// <para>内部复用 <see cref="Pool{T}"/>，享有一致的容量管理和 Editor 重复归还检测。</para>
    /// </summary>
    /// <typeparam name="T">集合元素类型</typeparam>
    /// <example>
    /// <code>
    /// var set = HashSetPool<int>.Get();
    /// set.Add(42);
    /// HashSetPool<int>.Return(set);  // 自动 Clear()
    /// </code>
    /// </example>
    public static class HashSetPool<T>
    {
        private static Pool<HashSet<T>> _pool = new(
            () => new HashSet<T>(),
            PoolConfig.Default,
            onReturn: set => set.Clear());

        static HashSetPool()
        {
            CollectionPoolManager.Register(() => _pool.Clear());
        }

        /// <summary>
        /// 从池中获取一个 <see cref="HashSet{T}"/>。池空时自动创建。
        /// </summary>
        public static HashSet<T> Get()
        {
            return _pool.Get();
        }

        /// <summary>
        /// 归还 <see cref="HashSet{T}"/> 到池，归还前自动 <c>Clear()</c>。
        /// </summary>
        /// <param name="set">要归还的 HashSet 实例</param>
        public static void Return(HashSet<T> set)
        {
            _pool.Return(set);
        }

        /// <summary>
        /// 池中当前闲置的 <see cref="HashSet{T}"/> 数量。
        /// </summary>
        public static int CountInactive => _pool.CountInactive;

        /// <summary>
        /// 池历史创建的 <see cref="HashSet{T}"/> 总数。
        /// </summary>
        public static int CountAll => _pool.CountAll;

        /// <summary>
        /// 预配置池参数。仅在一次都未 <c>Get()</c> 时有效。
        /// </summary>
        /// <param name="config">池配置</param>
        public static void Configure(PoolConfig config)
        {
            var oldCount = _pool.CountAll - _pool.CountInactive;
            if (oldCount > 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[HashSetPool<{typeof(T).Name}>] 已有 {oldCount} 个活跃实例，Configure 已忽略。请在首次 Get 前调用 Configure。");
                return;
            }

            _pool = new Pool<HashSet<T>>(() => new HashSet<T>(), config, onReturn: set => set.Clear());
        }

        /// <summary>
        /// 清空池内所有闲置实例。
        /// </summary>
        public static void Clear()
        {
            _pool.Clear();
        }

        /// <summary>
        /// 获取内部 <see cref="IPool{T}"/> 接口，用于依赖反转。
        /// </summary>
        public static IPool<HashSet<T>> GetPool()
        {
            return _pool;
        }
    }
}