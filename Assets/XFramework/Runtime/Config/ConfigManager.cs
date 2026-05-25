using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XConfig
{
    /// <summary>
    /// 全局配置管理器外观。提供静态方法直接访问配置数据的注册、加载、查询和卸载。
    /// <para>内部持有 <see cref="ConfigManagerImpl"/> 实例，所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="Initialize"/> 初始化（无参，仅创建内部实例）。</para>
    /// <para>内置 <see cref="ConfigFormat.Json"/>、<see cref="ConfigFormat.ScriptableObject"/> 加载器。
    /// 第三方可自行实现 <see cref="IConfigLoader"/> 并调用 <see cref="RegisterTable{T, TKey}(Dictionary{TKey, T})"/> /
    /// <see cref="RegisterGlobal{T}"/> 注入自定义格式的配置数据。</para>
    /// <para>Table 类型按 <see cref="IConfigRow{TKey}.Id"/> 索引查询，Global 类型直接获取单例。</para>
    /// <para>所有配置加载后常驻内存，不进行 LRU 淘汰（配置数据体量小，不会造成显著内存压力），
    /// 仅在显式调用 <see cref="Unload{T}"/> 时释放。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 1. 定义 Row（struct，零 GC）
    /// [Serializable]
    /// public struct ItemRow : IConfigRow<int>
    /// {
    ///     public int Id { get; set; }
    ///     public string Name;
    ///     public int Price;
    /// }
    /// 
    /// // 2. 初始化 & 预加载
    /// ConfigManager.Initialize();
    /// await ConfigManager.PreloadTableAsync<ItemRow, int>("config/items");
    /// 
    /// // 3. 查询
    /// var row = ConfigManager.Get<ItemRow, int>(1001);
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
        /// <para>初始化后可立即调用 <see cref="PreloadTableAsync{T, TKey}"/> 或 <see cref="PreloadGlobalAsync{T}"/> 加载配置。</para>
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
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址），首次加载时必填。</param>
        /// <param name="format">配置格式，默认 <see cref="ConfigFormat.Json"/>。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        public static async UniTask PreloadTableAsync<T, TKey>(string assetPath, ConfigFormat format = ConfigFormat.Json, CancellationToken cancellationToken = default)
            where T : IConfigRow<TKey>, new()
        {
            EnsureGlobalInitialized();
            await _instance.PreloadTableAsync<T, TKey>(assetPath, format).AttachExternalCancellation(cancellationToken);
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

        /// <summary>
        /// 使用自定义 Loader 预加载 Table 配置。
        /// <para>Loader 为临时策略对象，框架不持有引用，调用后可由 GC 回收。</para>
        /// <para>适用于 protobuf、MessagePack 等一文件一表的自定义格式。
        /// 对于一文件多表的格式（如 Luban），请使用 <see cref="RegisterTable{T, TKey}(Dictionary{TKey, T})"/>。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="assetPath">资源路径（由 Loader 自行解析），首次加载时必填。</param>
        /// <param name="loader">自定义加载器实例。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        public static async UniTask PreloadTableAsync<T, TKey>(string assetPath, IConfigLoader loader, CancellationToken cancellationToken = default)
            where T : IConfigRow<TKey>, new()
        {
            EnsureGlobalInitialized();
            await _instance.PreloadTableAsync<T, TKey>(assetPath, loader).AttachExternalCancellation(cancellationToken);
        }

        /// <summary>
        /// 使用自定义 Loader 预加载 Global 配置。
        /// <para>Loader 为临时策略对象，框架不持有引用。</para>
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（由 Loader 自行解析），首次加载时必填。</param>
        /// <param name="loader">自定义加载器实例。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        public static async UniTask PreloadGlobalAsync<T>(string assetPath, IConfigLoader loader, CancellationToken cancellationToken = default)
            where T : class, new()
        {
            EnsureGlobalInitialized();
            await _instance.PreloadGlobalAsync<T>(assetPath, loader).AttachExternalCancellation(cancellationToken);
        }

        #endregion

        #region Public API — Register (第三方注入)

        /// <summary>
        /// 注册已反序列化的 Table 数据到配置管理器。
        /// <para>第三方使用 Luban / protobuf / MessagePack 等工具自行反序列化后，
        /// 调用此方法将数据注入，后续可通过 <see cref="Get{T, TKey}(TKey)"/> 等接口统一查询。</para>
        /// <para>注意：配置行类型需实现 <see cref="IConfigRow{TKey}"/> 接口。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="data">按 Id 索引的 Dictionary。</param>
        /// <example>
        /// <code>
        /// // Luban 示例：反序列化后注册
        /// var tables = new GameTables(byteBuf);
        /// ConfigManager.RegisterTable(tables.TbItem.DataList.ToDictionary(r => r.Id));
        /// // 之后可通过 ConfigManager.Get<ItemRow, int>(id) 查询
        /// </code>
        /// </example>
        public static void RegisterTable<T, TKey>(Dictionary<TKey, T> data) where T : IConfigRow<TKey>
        {
            EnsureGlobalInitialized();
            _instance.RegisterTable<T, TKey>(data);
        }

        /// <summary>
        /// 非泛型注册 Table 数据，供反射调用（如动态遍历 Luban Tables 的 Tb 属性）。
        /// </summary>
        /// <param name="rowType">配置行类型。</param>
        /// <param name="data">按 Id 索引的 <see cref="System.Collections.IDictionary"/>。</param>
        public static void RegisterTable(Type rowType, System.Collections.IDictionary data)
        {
            EnsureGlobalInitialized();
            _instance.RegisterTable(rowType, data);
        }

        /// <summary>
        /// 注册 Global 配置单例到配置管理器。
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class。</typeparam>
        /// <param name="config">配置单例实例。</param>
        public static void RegisterGlobal<T>(T config) where T : class
        {
            EnsureGlobalInitialized();
            _instance.RegisterGlobal(config);
        }

        #endregion

        #region Public API — Query Table

        /// <summary>
        /// 获取 Table 中指定 Id 的配置行。不存在时抛出 <see cref="ConfigException"/>。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <returns>对应的配置行。</returns>
        /// <exception cref="ConfigException">Table 未加载或 Id 不存在时抛出。</exception>
        public static T Get<T, TKey>(TKey id) where T : IConfigRow<TKey>
        {
            EnsureGlobalInitialized();
            return _instance.Get<T, TKey>(id);
        }

        /// <summary>
        /// 安全查询 Table 中指定 Id 的配置行。Table 未加载或 Id 不存在时返回 <c>false</c>。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <param name="row">输出的配置行，失败时为 <c>default</c>。</param>
        /// <returns>成功获取时返回 <c>true</c>。</returns>
        public static bool TryGet<T, TKey>(TKey id, out T row) where T : IConfigRow<TKey>
        {
            EnsureGlobalInitialized();
            return _instance.TryGet<T, TKey>(id, out row);
        }

        /// <summary>
        /// 获取 Table 中所有配置行（复制为新数组）。
        /// <para>会产生少量 GC（数组分配），频繁调用请使用 <see cref="Get{T, TKey}(TKey)"/> 按 Id 查询。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <returns>所有配置行的数组。</returns>
        public static T[] GetAll<T, TKey>() where T : IConfigRow<TKey>
        {
            EnsureGlobalInitialized();
            return _instance.GetAll<T, TKey>();
        }

        /// <summary>
        /// 判断 Table 中是否包含指定 Id 的配置行。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <returns>存在时返回 <c>true</c>。</returns>
        public static bool Contains<T, TKey>(TKey id) where T : IConfigRow<TKey>
        {
            EnsureGlobalInitialized();
            return _instance.Contains<T, TKey>(id);
        }

        /// <summary>
        /// 获取 Table 中配置行的总数。Table 未加载时返回 0。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <returns>配置行数量。</returns>
        public static int Count<T, TKey>() where T : IConfigRow<TKey>
        {
            EnsureGlobalInitialized();
            return _instance.Count<T, TKey>();
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
        /// <para>卸载后再次调用 <see cref="PreloadTableAsync{T, TKey}"/> 或 <see cref="PreloadGlobalAsync{T}"/> 可重新加载。</para>
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