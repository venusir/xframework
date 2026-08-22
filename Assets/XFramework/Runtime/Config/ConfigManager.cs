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
    /// <para>内置 <see cref="ConfigFormat.Json"/>、<see cref="ConfigFormat.ScriptableObject"/>、
    /// <see cref="ConfigFormat.Csv"/> 加载器。
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
        /// 配置变更事件。Table 注册/加载、Global 注册/加载、卸载后触发。类型为配置行类型或 Global 配置类型。
        /// <para>仅在静态类全局实例（通过 <see cref="Initialize"/> 或 <see cref="SetInstance"/> 设置）操作时触发。
        /// 注入的自定义实现需要自行派发。</para>
        /// </summary>
        public static event Action<Type> ConfigChanged;

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

            var impl = new ConfigManagerImpl();
            SubscribeImplEvents(impl);
            _instance = impl;
            _instanceInitialized = true;
        }

        /// <summary>
        /// 注入自定义 <see cref="IConfigManager"/> 实例（可用于测试、依赖注入或完全替换内部实现）。
        /// <para>调用前若已有实例将被覆盖，请确保之前未 Initialize 或已 Destroy。</para>
        /// <para>框架以强引用持有注入的实例，不会主动销毁；其生命周期由调用方管理。</para>
        /// </summary>
        /// <param name="instance">自定义实现实例，为 null 时报错。</param>
        public static void SetInstance(IConfigManager instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            // 如果之前已通过 Initialize 创建了内部实例，先清理事件订阅
            if (_instance is ConfigManagerImpl oldImpl)
                UnsubscribeImplEvents(oldImpl);

            SubscribeImplEvents(instance);
            _instance = instance;
            _instanceInitialized = true;
        }

        /// <summary>
        /// 销毁全局配置管理器，释放所有已加载的配置数据。
        /// <para>销毁后需重新 <see cref="Initialize"/> 或 <see cref="SetInstance"/> 才能使用，通常仅在应用退出或场景完全重置时调用。</para>
        /// </summary>
        public static void Destroy()
        {
            if (_instance is ConfigManagerImpl impl)
                UnsubscribeImplEvents(impl);
            _instance = null;
            _instanceInitialized = false;
        }

        #endregion

        #region Public API — Preload

        /// <summary>
        /// 预加载 Table 类型的配置。主键类型由 <typeparamref name="T"/> 通过反射自动提取。
        /// <para>首次调用时需传入 <paramref name="assetPath"/> 指定资源位置；已加载后重复调用可省略路径。</para>
        /// <para>返回 <see cref="ConfigTable{T}"/> 包装器，后续通过 .Get(key) / .TryGet(key, out) 直接按 Id 查询，完全无需关心 TKey。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址），首次加载时必填。</param>
        /// <param name="format">配置格式，默认 <see cref="ConfigFormat.Json"/>。</param>
        /// <param name="cancellationToken">取消令牌（可选）。取消仅中断当前等待；底层加载仍会完成并注册数据。</param>
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
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址），首次加载时必填。</param>
        /// <param name="format">配置格式，默认 <see cref="ConfigFormat.Json"/>。</param>
        /// <param name="cancellationToken">取消令牌（可选）。取消仅中断当前等待；底层加载仍会完成并注册数据。</param>
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
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（由 Loader 自行解析），首次加载时必填。</param>
        /// <param name="loader">自定义加载器实例。</param>
        /// <param name="cancellationToken">取消令牌（可选）。取消仅中断当前等待；底层加载仍会完成并注册数据。</param>
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
        /// <param name="cancellationToken">取消令牌（可选）。取消仅中断当前等待；底层加载仍会完成并注册数据。</param>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        public static async UniTask PreloadGlobalAsync<T>(string assetPath, IConfigLoader loader, CancellationToken cancellationToken = default)
            where T : class, new()
        {
            EnsureGlobalInitialized();
            await _instance.PreloadGlobalAsync<T>(assetPath, loader).AttachExternalCancellation(cancellationToken);
        }

        #endregion

        #region Public API — Batch Preload

        /// <summary>
        /// 按分组名批量预加载 <paramref name="manifest"/> 中匹配的配置。
        /// </summary>
        /// <param name="groupName">分组名，仅加载 <see cref="ConfigManifest.AddTable{T}"/> /
        /// <see cref="ConfigManifest.AddGlobal{T}"/> 时传入相同 group 的条目。</param>
        /// <param name="manifest">配置加载清单。</param>
        /// <param name="cancellationToken">取消令牌（可选）。取消仅中断当前等待；底层加载仍会完成并注册数据。</param>
        public static async UniTask PreloadGroupAsync(string groupName, ConfigManifest manifest,
            CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            await _instance.PreloadGroupAsync(groupName, manifest)
                .AttachExternalCancellation(cancellationToken);
        }

        /// <summary>
        /// 预加载 <paramref name="manifest"/> 中的所有配置。
        /// </summary>
        /// <param name="manifest">配置加载清单。</param>
        /// <param name="cancellationToken">取消令牌（可选）。取消仅中断当前等待；底层加载仍会完成并注册数据。</param>
        public static async UniTask PreloadAllAsync(ConfigManifest manifest,
            CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            await _instance.PreloadAllAsync(manifest)
                .AttachExternalCancellation(cancellationToken);
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
        /// var table = new ConfigTable<ItemRow>(dict);
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
        /// <para>接收 <see cref="IConfigTable"/> 实例（<c>ConfigTable<T></c> 实现了此接口）。</para>
        /// </summary>
        /// <param name="rowType">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</param>
        /// <param name="table"><see cref="IConfigTable"/> 实例。</param>
        public static void RegisterTable(Type rowType, IConfigTable table)
        {
            EnsureGlobalInitialized();
            _instance.RegisterTable(rowType, table);
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

        /// <summary>
        /// 按主键直接获取配置行。
        /// <para>Table 未加载或键不存在时抛出 <see cref="ConfigException"/>。
        /// 适合零散的单次查询，无需先获取包装器。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <typeparam name="TKey">主键类型。</typeparam>
        /// <param name="key">主键值。</param>
        /// <returns>配置行实例。</returns>
        /// <exception cref="ConfigException">Table 未加载或键不存在时抛出。</exception>
        /// <example>
        /// <code>
        /// var row = ConfigManager.Get<ItemRow, int>(1001);
        /// </code>
        /// </example>
        public static T Get<T, TKey>(TKey key) where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.Get<T, TKey>(key);
        }

        /// <summary>
        /// 安全按主键获取配置行。
        /// <para>Table 未加载或键不存在时返回 <c>false</c>。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <typeparam name="TKey">主键类型。</typeparam>
        /// <param name="key">主键值。</param>
        /// <param name="value">输出的配置行，失败时为 <c>default</c>。</param>
        /// <returns>成功获取时返回 <c>true</c>。</returns>
        /// <example>
        /// <code>
        /// ConfigManager.TryGet<ItemRow, int>(1002, out var row);
        /// </code>
        /// </example>
        public static bool TryGet<T, TKey>(TKey key, out T value) where T : IConfigRow
        {
            EnsureGlobalInitialized();
            return _instance.TryGet(key, out value);
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
                    "[ConfigManager] ConfigManager 尚未初始化。请先调用 ConfigManager.Initialize() 完成初始化。");
        }

        /// <summary>
        /// 订阅 ConfigManagerImpl 的内部事件以驱动 <see cref="ConfigChanged"/>。
        /// </summary>
        private static void SubscribeImplEvents(IConfigManager instance)
        {
            if (instance is ConfigManagerImpl impl)
                impl.InternalConfigChanged += OnInternalConfigChanged;
        }

        /// <summary>
        /// 取消订阅 ConfigManagerImpl 的内部事件。
        /// </summary>
        private static void UnsubscribeImplEvents(IConfigManager instance)
        {
            if (instance is ConfigManagerImpl impl)
                impl.InternalConfigChanged -= OnInternalConfigChanged;
        }

        /// <summary>
        /// 传播内部事件到公共静态事件。
        /// </summary>
        private static void OnInternalConfigChanged(Type type)
        {
            ConfigChanged?.Invoke(type);
        }

        #endregion
    }
}