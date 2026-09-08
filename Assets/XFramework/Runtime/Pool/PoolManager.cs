using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XPool
{
    /// <summary>
    /// 全局对象池管理器。
    /// <para>惰性自动创建：第一次 <c>Get<T>()</c> 时自动创建默认池，零配置开箱即用。</para>
    /// <para>可选预配置：通过 <c>Configure<T>(config)</c> 在首次使用前自定义容量、预热、生成器。</para>
    /// <para>与 <see cref="AssetManager"/> 的对象池解耦：本管理器仅管理纯 C# 对象，不涉及 GameObject 和资源引用。</para>
    /// </summary>
    /// <remarks>
    /// <b>快速开始：</b>
    /// <code>
    /// // 零配置 — 自动 new T()
    /// var data = PoolManager.Get<MyData>();
    /// PoolManager.Return(data);
    ///
    /// // 预热 + 容量限制
    /// PoolManager.Configure<BulletData>(new PoolConfig { PrewarmSize = 20, MaxSize = 100 });
    ///
    /// // 自定义生成器
    /// PoolManager.Configure<Enemy>(new PoolConfig { MaxSize = 50 },
    ///     generator: () => new Enemy(levelConfig));
    /// </code>
    /// </remarks>
    public static class PoolManager
    {
        #region Static — Global Singleton

        private static readonly Dictionary<Type, object> _pools = new();
        private static readonly Dictionary<Type, object> _configs = new();

        static PoolManager()
        {
            // 订阅仅此一次、永不退订：ClearAll 改为幂等清闲置后，退出与切场景多次调用均安全；
            // 禁用域重载时静态状态跨会话存活，后续会话退出仍需要清理
            Application.quitting += ClearAll;
        }

        #endregion

        #region Public API — Get / Return

        /// <summary>
        /// 从池获取一个实例。池空时自动 <c>new T()</c>。
        /// <para><typeparamref name="T"/> 需具有无参构造函数。</para>
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型且具有无参构造函数</typeparam>
        public static T Get<T>() where T : class, new()
        {
            return GetPool<T>().Get();
        }

        /// <summary>
        /// 从池获取一个实例，使用自定义生成器（池空时调用）。
        /// <para>适用于无无参构造函数的类型，或需要自定义初始化逻辑的场景。</para>
        /// <para>generator 仅在本类型的首次建池时生效；池已创建后传入的生成器会被忽略
        /// （同一类型全局单池，后续 Get 仍使用建池时确定的生成器）。</para>
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型</typeparam>
        /// <param name="generator">池空时的实例生成器（首次建池时生效）</param>
        public static T Get<T>(Func<T> generator) where T : class
        {
            return GetOrCreatePoolCore(generator, true).Get();
        }

        /// <summary>
        /// 以 using 方式获取实例，并在 using 块结束时自动归还。
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型且具有无参构造函数</typeparam>
        /// <param name="item">从池中取出的实例</param>
        /// <returns>实现 <see cref="IDisposable"/> 的包装器，用于 using 语句</returns>
        public static PooledObject<T> GetPooled<T>(out T item) where T : class, new()
        {
            return GetPool<T>().GetPooled(out item);
        }

        /// <summary>
        /// 以 using 方式获取实例（自定义生成器），并在 using 块结束时自动归还。
        /// <para>generator 的生效语义同 <see cref="Get{T}(Func{T})"/>：仅首次建池时生效。</para>
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型</typeparam>
        /// <param name="generator">池空时的实例生成器（首次建池时生效）</param>
        /// <param name="item">从池中取出的实例</param>
        /// <returns>实现 <see cref="IDisposable"/> 的包装器，用于 using 语句</returns>
        public static PooledObject<T> GetPooled<T>(Func<T> generator, out T item) where T : class
        {
            return GetOrCreatePoolCore(generator, true).GetPooled(out item);
        }

        /// <summary>
        /// 归还实例到池。
        /// <para>若类型从未注册（从未调用 <c>Get<T>()</c>），则静默忽略。</para>
        /// <para>归还后可安全将引用置 null，池会保留实例供后续复用。</para>
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="item">要归还的实例</param>
        public static void Return<T>(T item) where T : class
        {
            if (item == null) return;
            if (_pools.TryGetValue(typeof(T), out var poolObj) && poolObj is IPool<T> pool)
            {
                pool.Return(item);
            }
            // 未注册类型：静默忽略（可能从未调用 Get<T>）
        }

        #endregion

        #region Public API — Configure

        /// <summary>
        /// 预配置池参数，在该类型首次建池时一次性消费生效。
        /// <para>适用于只需自定义容量 / 预热数量，无需自定义生成器的场景。</para>
        /// <para>若该类型的池已创建，则告警并忽略本次配置；重新配置请先调用
        /// <see cref="RemovePool{T}"/>。重复 Configure（池未创建时）以最后一次为准。</para>
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型且具有无参构造函数</typeparam>
        /// <param name="config">池配置</param>
        public static void Configure<T>(PoolConfig config) where T : class, new()
        {
            if (_pools.ContainsKey(typeof(T)))
            {
                Debug.LogWarning(
                    $"[PoolManager] 类型 {typeof(T).Name} 的池已创建，Configure 已忽略。如需重新配置，请先调用 RemovePool<{typeof(T).Name}>()。");
                return;
            }

            _configs[typeof(T)] = config;
        }

        /// <summary>
        /// 预配置池参数及自定义生成器，在该类型首次建池时一次性消费生效。
        /// <para>适用于需要自定义构造逻辑（如注入依赖）的场景。</para>
        /// <para>若该类型的池已创建，则告警并忽略本次配置；重新配置请先调用
        /// <see cref="RemovePool{T}"/>。无参入口建池时使用本生成器；显式传入生成器的
        /// <see cref="Get{T}(Func{T})"/> 以显式生成器优先（配置的容量 / 预热仍生效）。</para>
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型</typeparam>
        /// <param name="config">池配置</param>
        /// <param name="generator">池空时的实例生成器（首次建池时生效）</param>
        public static void Configure<T>(PoolConfig config, Func<T> generator) where T : class
        {
            if (_pools.ContainsKey(typeof(T)))
            {
                Debug.LogWarning(
                    $"[PoolManager] 类型 {typeof(T).Name} 的池已创建，Configure 已忽略。如需重新配置，请先调用 RemovePool<{typeof(T).Name}>()。");
                return;
            }

            _configs[typeof(T)] = (config, generator);
        }

        #endregion

        #region Public API — Query

        /// <summary>
        /// 指定类型的池是否已创建。
        /// </summary>
        public static bool HasPool<T>()
        {
            return _pools.ContainsKey(typeof(T));
        }

        /// <summary>
        /// 获取指定类型的池实例。若池不存在则按已配置参数创建
        /// （<see cref="Configure{T}(PoolConfig)"/> 的一次性配置，否则默认配置）。
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型且具有无参构造函数</typeparam>
        public static IPool<T> GetPool<T>() where T : class, new()
        {
            if (_pools.TryGetValue(typeof(T), out var poolObj))
                return (IPool<T>)poolObj;

            return GetOrCreatePoolCore(() => new T(), false);
        }

        /// <summary>
        /// 移除并清空指定类型的池。
        /// <para>已取出的活跃实例不受影响，但归还时池已不存在，会被静默忽略。</para>
        /// </summary>
        public static void RemovePool<T>()
        {
            var type = typeof(T);
            if (_pools.TryGetValue(type, out var poolObj) && poolObj is IPool<T> pool)
            {
                pool.Clear();
                _pools.Remove(type);
            }
        }

        /// <summary>
        /// 清空所有类型池的闲置实例（等价对每池调用 <see cref="Pool{T}.Clear"/>）。
        /// <para>已取出的活跃实例不受影响，归还时重新入池；池注册与 <c>Configure</c> 配置保留，
        /// 之后 <c>Get</c> / <c>Return</c> 照常工作。</para>
        /// <para>幂等，可重复调用，切场景与退出均安全。应用退出时自动调用
        /// （Editor 中退出播放模式同样触发）。</para>
        /// </summary>
        public static void ClearAll()
        {
            foreach (var poolObj in _pools.Values)
            {
                if (poolObj is IDisposable d) d.Dispose();
            }
        }

        #endregion

        #region Internal

        /// <summary>
        /// 建池统一入口：池已存在直接返回（同一类型全局单池）；否则消费 <c>_configs</c> 中
        /// 该类型的一次性配置后创建。
        /// <para>显式传入的生成器（<c>generatorIsExplicit</c>）优先于 Configure 的生成器，
        /// 但配置中的容量 / 预热仍生效；裸 <c>PoolConfig</c> 条目不影响生成器。</para>
        /// </summary>
        private static IPool<T> GetOrCreatePoolCore<T>(Func<T> generator, bool generatorIsExplicit)
            where T : class
        {
            if (_pools.TryGetValue(typeof(T), out var poolObj))
                return (IPool<T>)poolObj;

            PoolConfig config = PoolConfig.Default;

            if (_configs.TryGetValue(typeof(T), out var cfgObj))
            {
                if (cfgObj is (PoolConfig cfg, Func<T> gen))
                {
                    config = cfg;
                    if (!generatorIsExplicit)
                        generator = gen;
                }
                else if (cfgObj is PoolConfig cfgOnly)
                {
                    config = cfgOnly;
                }
                _configs.Remove(typeof(T));
            }

            var pool = new Pool<T>(generator, config);
            _pools[typeof(T)] = pool;
            return pool;
        }

        #endregion
    }
}