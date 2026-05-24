using System.Collections.Generic;

namespace XFramework.XPool
{
    /// <summary>
    /// <see cref="Dictionary{TKey, TValue}"/> 对象池。
    /// <para><c>Return(dict)</c> 时自动调用 <c>dict.Clear()</c>，无需手动清空。</para>
    /// <para>内部复用 <see cref="Pool{T}"/>，享有一致的容量管理和 Editor 重复归还检测。</para>
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TValue">字典值类型</typeparam>
    /// <example>
    /// <code>
    /// var dict = DictionaryPool<string, int>.Get();
    /// dict["score"] = 100;
    /// DictionaryPool<string, int>.Return(dict);  // 自动 Clear()
    /// </code>
    /// </example>
    public static class DictionaryPool<TKey, TValue>
    {
        private static Pool<Dictionary<TKey, TValue>> _pool = new(
            () => new Dictionary<TKey, TValue>(),
            PoolConfig.Default,
            onReturn: dict => dict.Clear());

        static DictionaryPool()
        {
            CollectionPoolManager.Register(() => _pool.Clear());
        }

        /// <summary>
        /// 从池中获取一个 <see cref="Dictionary{TKey, TValue}"/>。池空时自动创建。
        /// </summary>
        public static Dictionary<TKey, TValue> Get()
        {
            return _pool.Get();
        }

        /// <summary>
        /// 以 using 方式获取 <see cref="Dictionary{TKey, TValue}"/>，并在 using 块结束时自动归还（自动 Clear()）。
        /// </summary>
        /// <param name="dict">从池中取出的字典实例</param>
        /// <returns>实现 <see cref="IDisposable"/> 的包装器，用于 using 语句</returns>
        public static PooledObject<Dictionary<TKey, TValue>> GetPooled(out Dictionary<TKey, TValue> dict)
        {
            return _pool.GetPooled(out dict);
        }

        /// <summary>
        /// 归还 <see cref="Dictionary{TKey, TValue}"/> 到池，归还前自动 <c>Clear()</c>。
        /// </summary>
        /// <param name="dict">要归还的字典实例</param>
        public static void Return(Dictionary<TKey, TValue> dict)
        {
            _pool.Return(dict);
        }

        /// <summary>
        /// 池中当前闲置的 <see cref="Dictionary{TKey, TValue}"/> 数量。
        /// </summary>
        public static int CountInactive => _pool.CountInactive;

        /// <summary>
        /// 池历史创建的 <see cref="Dictionary{TKey, TValue}"/> 总数。
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
                    $"[DictionaryPool<{typeof(TKey).Name}, {typeof(TValue).Name}>] 已有 {oldCount} 个活跃实例，Configure 已忽略。请在首次 Get 前调用 Configure。");
                return;
            }

            _pool = new Pool<Dictionary<TKey, TValue>>(() => new Dictionary<TKey, TValue>(), config, onReturn: dict => dict.Clear());
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
        public static IPool<Dictionary<TKey, TValue>> GetPool()
        {
            return _pool;
        }
    }
}