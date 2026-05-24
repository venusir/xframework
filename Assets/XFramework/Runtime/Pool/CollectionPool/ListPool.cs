using System.Collections.Generic;

namespace XFramework.XPool
{
    /// <summary>
    /// <see cref="List{T}"/> 对象池。
    /// <para><c>Return(list)</c> 时自动调用 <c>list.Clear()</c>，无需手动清空。</para>
    /// <para>内部复用 <see cref="Pool{T}"/>，享有一致的容量管理和 Editor 重复归还检测。</para>
    /// </summary>
    /// <typeparam name="T">列表元素类型</typeparam>
    /// <example>
    /// <code>
    /// var list = ListPool<Vector3>.Get();
    /// list.Add(Vector3.zero);
    /// ListPool<Vector3>.Return(list);  // 自动 Clear()
    /// </code>
    /// </example>
    public static class ListPool<T>
    {
        private static Pool<List<T>> _pool = new(
            () => new List<T>(),
            PoolConfig.Default,
            onReturn: list => list.Clear());

        static ListPool()
        {
            CollectionPoolManager.Register(() => _pool.Clear());
        }

        /// <summary>
        /// 从池中获取一个 <see cref="List{T}"/>。池空时自动创建。
        /// </summary>
        public static List<T> Get()
        {
            return _pool.Get();
        }

        /// <summary>
        /// 以 using 方式获取 <see cref="List{T}"/>，并在 using 块结束时自动归还（自动 Clear()）。
        /// </summary>
        /// <param name="list">从池中取出的列表实例</param>
        /// <returns>实现 <see cref="IDisposable"/> 的包装器，用于 using 语句</returns>
        public static PooledObject<List<T>> GetPooled(out List<T> list)
        {
            return _pool.GetPooled(out list);
        }

        /// <summary>
        /// 归还 <see cref="List{T}"/> 到池，归还前自动 <c>Clear()</c>。
        /// </summary>
        /// <param name="list">要归还的列表实例</param>
        public static void Return(List<T> list)
        {
            _pool.Return(list);
        }

        /// <summary>
        /// 池中当前闲置的 <see cref="List{T}"/> 数量。
        /// </summary>
        public static int CountInactive => _pool.CountInactive;

        /// <summary>
        /// 池历史创建的 <see cref="List{T}"/> 总数。
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
                    $"[ListPool<{typeof(T).Name}>] 已有 {oldCount} 个活跃实例，Configure 已忽略。请在首次 Get 前调用 Configure。");
                return;
            }

            _pool = new Pool<List<T>>(() => new List<T>(), config, onReturn: list => list.Clear());
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
        public static IPool<List<T>> GetPool()
        {
            return _pool;
        }
    }
}