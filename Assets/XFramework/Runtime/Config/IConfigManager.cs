using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig
{
    /// <summary>
    /// 配置管理器公共接口。与节点树无关，可供任何对象（MonoBehaviour、纯 C# 类等）直接使用。
    /// <para>通过 <see cref="ConfigManager"/> 的静态方法直接调用，或注入 <see cref="IConfigManager"/> 实例使用。</para>
    /// </summary>
    public interface IConfigManager
    {
        #region Preload

        /// <summary>
        /// 预加载 Table 类型的配置。主键类型由 <typeparamref name="T"/> 通过反射自动提取。
        /// <para>首次调用时自动注册并加载，已加载则直接返回现有包装器。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">配置资源路径。</param>
        /// <param name="format">配置格式，默认 JSON。</param>
        /// <returns>可缓存的 <see cref="ConfigTable{T}"/> 只读包装器，通过 .Get(key) / .TryGet(key) 查询。</returns>
        /// <exception cref="ConfigException">assetPath 为空、类型未实现 IConfigRow<> 或加载失败时抛出。</exception>
        UniTask<ConfigTable<T>> PreloadTableAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json)
            where T : IConfigRow, new();

        /// <summary>
        /// 预加载 Global 类型的配置。首次调用时自动注册并加载。
        /// <para>如果已加载则直接返回。</para>
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class 并有无参构造函数。</typeparam>
        /// <param name="assetPath">配置资源路径。</param>
        /// <param name="format">配置格式，默认 JSON。</param>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        UniTask PreloadGlobalAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json)
            where T : class, new();

        /// <summary>
        /// 使用自定义 Loader 预加载 Table 配置。
        /// <para>Loader 为临时策略对象，调用后不被持有，可由 GC 回收。</para>
        /// <para>适用于 protobuf、MessagePack 等一文件一表的自定义格式。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">配置资源路径。</param>
        /// <param name="loader">自定义配置加载器。</param>
        /// <returns>可缓存的 <see cref="ConfigTable{T}"/> 只读包装器。</returns>
        /// <exception cref="ConfigException">loader 为 null、类型未实现 IConfigRow<> 或加载失败时抛出。</exception>
        UniTask<ConfigTable<T>> PreloadTableAsync<T>(string assetPath, IConfigLoader loader)
            where T : IConfigRow, new();

        /// <summary>
        /// 使用自定义 Loader 预加载 Global 配置。
        /// <para>Loader 为临时策略对象，调用后不被持有。</para>
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class 并有无参构造函数。</typeparam>
        /// <param name="assetPath">配置资源路径。</param>
        /// <param name="loader">自定义配置加载器。</param>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        UniTask PreloadGlobalAsync<T>(string assetPath, IConfigLoader loader)
            where T : class, new();

        #endregion

        #region Register (第三方注入)

        /// <summary>
        /// 注册已反序列化的 Table 数据到配置管理器。
        /// <para>第三方使用 Luban / protobuf / MessagePack 等工具自行反序列化后，
        /// 构造 <see cref="ConfigTable{T}"/> 并调用此方法注入，后续可通过 <see cref="GetTable{T}"/> 统一查询。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <param name="table">Table 包装器实例。</param>
        /// <exception cref="ConfigException">table 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var dict = tables.TbItem.DataList.ToDictionary(r => r.Id);
        /// var table = new ConfigTable<ItemRow>(dict);
        /// ConfigManager.RegisterTable(table);
        /// </code>
        /// </example>
        void RegisterTable<T>(ConfigTable<T> table) where T : IConfigRow;

        /// <summary>
        /// 非泛型注册 Table 数据，供反射调用（如动态遍历 Luban Tables 的 Tb 属性）。
        /// <para>传入装箱的 <c>ConfigTable<T></c> 实例，框架通过反射提取内部字典。</para>
        /// <para>第三方反射调用示例（推荐使用泛型版，避免手动构造 ConfigTable）：</para>
        /// </summary>
        /// <param name="rowType">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</param>
        /// <param name="table"><see cref="IConfigTable"/> 实例（<c>ConfigTable<T></c> 实现了此接口）。</param>
        /// <exception cref="ConfigException">rowType 或 table 为 null 时抛出。</exception>
        void RegisterTable(Type rowType, IConfigTable table);

        /// <summary>
        /// 注册 Global 配置单例到配置管理器。
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class。</typeparam>
        /// <param name="config">配置单例实例。</param>
        /// <exception cref="ConfigException">config 为 null 时抛出。</exception>
        void RegisterGlobal<T>(T config) where T : class;

        #endregion

        #region Query — Table

        /// <summary>
        /// 获取指定 Table 的只读包装器。未加载时抛出 <see cref="ConfigException"/>。
        /// <para>包装器可缓存，后续通过 .Get(key) / .TryGet(key) 查询，主键类型由实参自动推断。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <returns>Table 包装器实例。</returns>
        /// <exception cref="ConfigException">Table 未加载时抛出。</exception>
        ConfigTable<T> GetTable<T>() where T : IConfigRow;

        /// <summary>
        /// 安全获取 Table 包装器。未加载时返回 <c>false</c>。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="table">输出的包装器实例。</param>
        /// <returns>Table 已加载时返回 <c>true</c>。</returns>
        bool TryGetTable<T>(out ConfigTable<T> table) where T : IConfigRow;

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
        T Get<T, TKey>(TKey key) where T : IConfigRow;

        /// <summary>
        /// 安全按主键获取配置行。
        /// <para>Table 未加载或键不存在时返回 <c>false</c>。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <typeparam name="TKey">主键类型。</typeparam>
        /// <param name="key">主键值。</param>
        /// <param name="value">输出的配置行，失败时为 <c>default</c>。</param>
        /// <returns>成功获取时返回 <c>true</c>。</returns>
        bool TryGet<T, TKey>(TKey key, out T value) where T : IConfigRow;

        #endregion

        #region Query — Global

        /// <summary>
        /// 获取 Global 配置。未加载时抛异常。
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class。</typeparam>
        /// <returns>配置单例实例。</returns>
        /// <exception cref="ConfigException">Global 配置未加载时抛出。</exception>
        T GetGlobal<T>() where T : class;

        /// <summary>
        /// 安全获取 Global 配置。
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class。</typeparam>
        /// <param name="config">输出的配置实例，如果不存在则为 <c>default</c>。</param>
        /// <returns>成功获取时返回 <c>true</c>。</returns>
        bool TryGetGlobal<T>(out T config) where T : class;

        #endregion

        #region Batch Preload

        /// <summary>
        /// 按分组名批量预加载 <paramref name="manifest"/> 中匹配的配置。
        /// <para>已加载的条目自动跳过。</para>
        /// </summary>
        /// <param name="groupName">分组名，仅加载 <see cref="ConfigManifest.AddTable{T}"/> /
        /// <see cref="ConfigManifest.AddGlobal{T}"/> 时传入相同 group 的条目。</param>
        /// <param name="manifest">配置加载清单。</param>
        UniTask PreloadGroupAsync(string groupName, ConfigManifest manifest);

        /// <summary>
        /// 预加载 <paramref name="manifest"/> 中的所有配置。
        /// <para>已加载的条目自动跳过。</para>
        /// </summary>
        /// <param name="manifest">配置加载清单。</param>
        UniTask PreloadAllAsync(ConfigManifest manifest);

        #endregion

        #region Unload

        /// <summary>
        /// 卸载指定类型的配置（Table 或 Global）。
        /// </summary>
        /// <typeparam name="T">配置行类型或 Global 配置类型。</typeparam>
        void Unload<T>();

        /// <summary>
        /// 判断指定类型的配置是否已加载。
        /// </summary>
        /// <typeparam name="T">配置行类型或 Global 配置类型。</typeparam>
        /// <returns>已加载时返回 <c>true</c>。</returns>
        bool IsLoaded<T>();

        #endregion
    }
}