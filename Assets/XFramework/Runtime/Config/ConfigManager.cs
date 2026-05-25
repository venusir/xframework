using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XConfig
{
    /// <summary>
    /// 全局配置管理器外观。提供静态方法直接访问配置数据的注册、加载、查询和卸载。
    /// <para>内部持有 <see cref="IConfigManager"/> 实例（<see cref="ConfigManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="Initialize"/> 初始化（无参，仅创建内部实例）。</para>
    /// <para>内置 <see cref="ConfigFormat.Json"/>、<see cref="ConfigFormat.ScriptableObject"/> 加载器。
    /// 第三方可自行实现 <see cref="IConfigLoader"/> 并调用 <see cref="RegisterTable{T}(ConfigTable{T})"/> /
    /// <see cref="RegisterGlobal{T}"/> 注入自定义格式的配置数据。</para>
    /// <para>Table 类型通过 <see cref="ConfigTable{T}"/> 包装器查询，主键类型由实参自动推断。
    /// Global 类型直接获取单例。</para>
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
    /// var items = await ConfigManager.PreloadTableAsync<ItemRow>("config/items");
    /// 
    /// // 3. 查询（TKey 由实参自动推断）
    /// var row = items.Get(1001);
    /// Debug.Log($"Item: {row.Name}, Price: {row.Price}");
    /// </code>
    /// </example>
    public static class ConfigManager
    {
        #region Static — Global Singleton

        private static IConfigManager _instance;
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
        /// 预加载 Table 类型的配置。主键类型由 <typeparamref name="T"/> 通过反射自动提取。
        /// <para>首次调用时需传入 <paramref name="assetPath"/> 指定资源位置；已加载后重复调用可省略路径。</para>
        /// <para>返回 <see cref="ConfigTable{T}"/> 包装器，后续通过 .Get(key) / .TryGet(key, out) 直接按 Id 查询，完全无需关心 TKey。</para>
        /// <para>支持 <paramref name="cancellationToken"/> 取消正在进行的加载任务。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址），首次加载时必填。</param>
        /// <param name="format">配置格式，默认 <see cref="ConfigFormat.Json"/>。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <returns>Table 包装器实例。</returns>
        /// <exception cref="ConfigException">assetPath 为空、类型未实现 IConfigRow<> 或加载失败时抛出。</exception>
        /// <example>
        /// <code>
        /// var items = await ConfigManager.PreloadTableAsync<ItemRow>("config/items");
        /// var row = items.Get(1001); // TKey 自动推断为 int
        /// </code>
        /// </example>
        public static async UniTask<ConfigTable<T>> PreloadTableAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json, CancellationToken cancellationToken = default)
            where T : IConfigRow, new()
        {
            EnsureGlobalInitialized();
            return await _instance.PreloadTableAsync<T>(assetPath, format).AttachExternalCancellation(cancellationToken);
        }

        /// <summary>
        /// 预加载 Global 类型的配置。如果已加载则直接返回。
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
        /// <para>适用于 protobuf、MessagePack 等一文件一表的自定义格式。</para>
        /// <para>支持 <paramref name="cancellationToken"/> 取消正在进行的加载任务。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（由 Loader 自行解析），首次加载时必填。</param>
        /// <param name="loader">自定义加载器实例。</param>
        /// <param name="cancellationToken">取消令牌（可选）。</param>
        /// <returns>Table 包装器实例。</returns>
        /// <exception cref="ConfigException">loader 为 null、类型未实现 IConfigRow<> 或加载失败时抛出。</exception>
        public static async UniTask<ConfigTable<T>> PreloadTableAsync<T>(string assetPath, IConfigLoader loader, CancellationToken cancellationToken = default)
            where T : IConfigRow, new()
        {
            EnsureGlobalInitialized();
            return await _instance.PreloadTableAsync<T>(assetPath, loader).AttachExternalCancellation(cancellationToken);
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
        /// 构造 <see cref="ConfigTable{T}"/> 并调用此方法注入，后续可通过 <see cref="GetTable{T}"/> 统一查询。</para>
        /// <para>注意：配置行类型需实现 <see cref="IConfigRow{TKey}"/> 接口。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="table">Table 包装器实例。</param>
        /// <example>
        /// <code>
        /// // Luban 示例：反序列化后构造 ConfigTable 注册
        /// var tables = new GameTables(byteBuf);
        /// var dict = tables.TbItem.DataList.ToDictionary(r => r.Id);
        /// var table = new ConfigTable<ItemRow>(dict, dict.Count);
        /// ConfigManager.RegisterTable(table);
        /// // 之后可通过 ConfigManager.GetTable<ItemRow>().Get(id) 查询
        /// </code>
        /// </example>
        public static void RegisterTable<T>(ConfigTable<T> table) where T : IConfigRow
        {
            EnsureGlobalInitialized();
            _instance.RegisterTable(table);
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
        /// 获取指定 Table 的只读包装器。未加载时抛出 <see cref="ConfigException"/>。
        /// <para>包装器可缓存，后续通过 .Get(key) / .TryGet(key, out) 查询时主键类型由实参自动推断，无需每次指定 TKey。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <returns>Table 包装器实例。</returns>
        /// <exception cref="ConfigException">Table 未加载时抛出。</exception>
        /// <example>
        /// <code>
        /// var items = ConfigManager.GetTable<ItemRow>();
        /// var row = items.Get(1001);        // TKey 自动推断为 int
        /// items.TryGet(1002, out var row2);
        /// var all = items.GetAll();          // 完全不涉及 TKey
        /// </code>
        /// </example>
        public static ConfigTable<T> GetTable<T>() where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.GetTable<T>();
        }

        /// <summary>
        /// 安全获取 Table 包装器。未加载时返回 <c>false</c>。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="table">输出的包装器实例。</param>
        /// <returns>Table 已加载时返回 <c>true</c>。</returns>
        public static bool TryGetTable<T>(out ConfigTable<T> table) where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.TryGetTable(out table);
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