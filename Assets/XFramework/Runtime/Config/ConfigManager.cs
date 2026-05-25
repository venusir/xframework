using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XConfig
{
    /// <summary>
    /// 全局配置管理器外观。提供静态方法直接访问配置数据的注册、加载、查询和卸载。
    /// <para>内部持有 <see cref="ConfigManagerImpl"/> 实例，所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="Initialize"/> 初始化（无参，仅创建内部实例）。</para>
    /// <para>支持三种配置格式：<see cref="ConfigFormat.Json"/>、<see cref="ConfigFormat.ScriptableObject"/>、
    /// <see cref="ConfigFormat.Luban"/>，由各 <see cref="IConfigLoader"/> 实现加载。</para>
    /// <para>Table 类型按 <see cref="IConfigRow.Id"/> 索引查询，Global 类型直接获取单例。</para>
    /// <para>所有配置加载后常驻内存，不进行 LRU 淘汰（配置数据体量小，不会造成显著内存压力），
    /// 仅在显式调用 <see cref="Unload{T}"/> 时释放。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 1. 定义 Row（struct，零 GC）
    /// [Serializable]
    /// public struct ItemRow : IConfigRow
    /// {
    ///     public int Id { get; set; }
    ///     public string Name;
    ///     public int Price;
    /// }
    /// 
    /// // 2. 初始化 & 预加载
    /// ConfigManager.Initialize();
    /// await ConfigManager.PreloadTableAsync<ItemRow>("config/items");
    /// 
    /// // 3. 查询
    /// var row = ConfigManager.Get<ItemRow>(1001);
    /// Debug.Log($"Item: {row.Name}, Price: {row.Price}");
    /// </code>
    /// </example>
    public static class ConfigManager
    {
        #region Static — Global Singleton

        private static ConfigManagerImpl _instance;
        private static bool _instanceInitialized;

        /// <summary>
        /// 全局配置管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化全局配置管理器（无参，仅创建内部实现实例）。
        /// <para>初始化后可立即调用 <see cref="PreloadTableAsync{T}"/> 或 <see cref="PreloadGlobalAsync{T}"/> 加载配置。</para>
        /// </summary>
        public static void Initialize()
        {
            if (_instanceInitialized)
            {
                Debug.LogWarning("[ConfigManager] Initialize was called more than once. Ignoring duplicate.");
                return;
            }

            _instance = new ConfigManagerImpl();
            _instanceInitialized = true;
        }

        /// <summary>
        /// 销毁全局配置管理器，释放所有已加载的配置数据。
        /// <para>销毁后需重新 <see cref="Initialize"/> 才能使用，通常仅在应用退出或场景完全重置时调用。</para>
        /// </summary>
        public static void Destroy()
        {
            if (_instance != null)
            {
                _instance = null;
            }
            _instanceInitialized = false;
        }

        #endregion

        #region Public API — Preload

        /// <summary>
        /// 预加载 Table 类型的配置。如果已加载则直接返回。
        /// <para>首次调用时需传入 <paramref name="assetPath"/> 指定资源位置；已加载后重复调用可省略路径。</para>
        /// <para>支持 <paramref name="cancellationToken"/> 取消正在进行的加载任务。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址），首次加载时必填。</param>
        /// <param name="format">配置格式，默认 <see cref="ConfigFormat.Json"/>。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        public static async UniTask PreloadTableAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json, CancellationToken cancellationToken = default)
            where T : IConfigRow, new()
        {
            EnsureGlobalInitialized();
            await _instance.PreloadTableAsync<T>(assetPath, format).AttachExternalCancellation(cancellationToken);
        }

        /// <summary>
        /// 预加载 Global 类型的配置。如果已加载则直接返回。
        /// <para>首次调用时需传入 <paramref name="assetPath"/> 指定资源位置；已加载后重复调用可省略路径。</para>
        /// <para>支持 <paramref name="cancellationToken"/> 取消正在进行的加载任务。</para>
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址），首次加载时必填。</param>
        /// <param name="format">配置格式，默认 <see cref="ConfigFormat.Json"/>。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        public static async UniTask PreloadGlobalAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json, CancellationToken cancellationToken = default)
            where T : class, new()
        {
            EnsureGlobalInitialized();
            await _instance.PreloadGlobalAsync<T>(assetPath, format).AttachExternalCancellation(cancellationToken);
        }

        #endregion

        #region Public API — Load (Luban Tables)

        /// <summary>
        /// 加载 Luban 生成的 Tables（完整格式）。
        /// <para>一次加载所有表，自动按 Row 类型注册到 ConfigManager 中。
        /// 后续通过 <see cref="Get{T}(int)"/>、<see cref="TryGet{T}(int, out T)"/> 等查询接口直接使用。</para>
        /// <para>重复调用时，跳过已加载的表。</para>
        /// <para>若 XFramework 的 Luban 程序集未安装，则抛出 <see cref="ConfigException"/>。</para>
        /// </summary>
        /// <typeparam name="TTables">Luban 生成的 Tables 类型。</typeparam>
        /// <param name="assetPath">Tables 二进制文件的资源路径。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <exception cref="ConfigException">Luban 未安装或加载失败时抛出。</exception>
        /// <example>
        /// <code>
        /// // 加载 Luban 生成的全部配置表
        /// await ConfigManager.LoadAsync<GameTables>("config/tables");
        ///
        /// // 按 Row 类型直接查询
        /// var item = ConfigManager.Get<ItemRow>(1001);
        /// var hero = ConfigManager.Get<HeroRow>(1);
        /// </code>
        /// </example>
        public static async UniTask LoadAsync<TTables>(string assetPath, CancellationToken cancellationToken = default)
            where TTables : class, new()
        {
            EnsureGlobalInitialized();
            await _instance.LoadAsync<TTables>(assetPath).AttachExternalCancellation(cancellationToken);
        }

        #endregion

        #region Public API — Query Table

        /// <summary>
        /// 获取 Table 中指定 Id 的配置行。不存在时抛出 <see cref="ConfigException"/>。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <returns>对应的配置行。</returns>
        /// <exception cref="ConfigException">Table 未加载或 Id 不存在时抛出。</exception>
        public static T Get<T>(int id) where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.Get<T>(id);
        }

        /// <summary>
        /// 安全查询 Table 中指定 Id 的配置行。Table 未加载或 Id 不存在时返回 <c>false</c>。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <param name="row">输出的配置行，失败时为 <c>default</c>。</param>
        /// <returns>成功获取时返回 <c>true</c>。</returns>
        public static bool TryGet<T>(int id, out T row) where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.TryGet(id, out row);
        }

        /// <summary>
        /// 获取 Table 中所有配置行（复制为新数组）。
        /// <para>会产生少量 GC（数组分配），频繁调用请使用 <see cref="Get{T}(int)"/> 按 Id 查询。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <returns>所有配置行的数组。</returns>
        public static T[] GetAll<T>() where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.GetAll<T>();
        }

        /// <summary>
        /// 判断 Table 中是否包含指定 Id 的配置行。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <returns>存在时返回 <c>true</c>。</returns>
        public static bool Contains<T>(int id) where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.Contains<T>(id);
        }

        /// <summary>
        /// 获取 Table 中配置行的总数。Table 未加载时返回 0。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <returns>配置行数量。</returns>
        public static int Count<T>() where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.Count<T>();
        }

        #endregion

        #region Public API — Query Global

        /// <summary>
        /// 获取 Global 配置。未加载时抛出 <see cref="ConfigException"/>。
        /// </summary>
        /// <typeparam name="T">配置类型。</typeparam>
        /// <returns>全局配置单例。</returns>
        /// <exception cref="ConfigException">配置未加载时抛出。</exception>
        public static T GetGlobal<T>() where T : class
        {
            EnsureGlobalInitialized();
            return _instance.GetGlobal<T>();
        }

        /// <summary>
        /// 安全获取 Global 配置。未加载时返回 <c>false</c>。
        /// </summary>
        /// <typeparam name="T">配置类型。</typeparam>
        /// <param name="config">输出的配置实例，失败时为 <c>default</c>。</param>
        /// <returns>成功获取时返回 <c>true</c>。</returns>
        public static bool TryGetGlobal<T>(out T config) where T : class
        {
            EnsureGlobalInitialized();
            return _instance.TryGetGlobal(out config);
        }

        #endregion

        #region Public API — Unload & Status

        /// <summary>
        /// 卸载指定类型的配置（Table 或 Global），释放引用。
        /// <para>卸载后再次调用 <see cref="PreloadTableAsync{T}"/> 或 <see cref="PreloadGlobalAsync{T}"/> 可重新加载。</para>
        /// </summary>
        public static void Unload<T>()
        {
            EnsureGlobalInitialized();
            _instance.Unload<T>();
        }

        /// <summary>
        /// 判断指定类型的配置是否已加载。
        /// </summary>
        public static bool IsLoaded<T>()
        {
            EnsureGlobalInitialized();
            return _instance.IsLoaded<T>();
        }

        #endregion

        #region Internal

        private static void EnsureGlobalInitialized()
        {
            if (!_instanceInitialized || _instance == null)
                throw new InvalidOperationException(
                    "ConfigManager is not initialized. Call ConfigManager.Initialize() first.");
        }

        #endregion
    }
}