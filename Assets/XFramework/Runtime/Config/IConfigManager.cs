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
        /// 预加载 Table 类型的配置。首次调用时自动注册并加载。
        /// <para>如果已加载则直接返回（传入的 <paramref name="assetPath"/> 在重复调用时可选不传）。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="assetPath">配置资源路径。</param>
        /// <param name="format">配置格式，默认 JSON。</param>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        UniTask PreloadTableAsync<T, TKey>(string assetPath, ConfigFormat format = ConfigFormat.Json)
            where T : IConfigRow<TKey>, new();

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
        /// <para>适用于 protobuf、MessagePack 等一文件一表的自定义格式。
        /// 对于一文件多表的格式（如 Luban），请使用 <see cref="RegisterTable{T, TKey}(Dictionary{TKey, T})"/>。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="assetPath">配置资源路径。</param>
        /// <param name="loader">自定义配置加载器。</param>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        UniTask PreloadTableAsync<T, TKey>(string assetPath, IConfigLoader loader)
            where T : IConfigRow<TKey>, new();

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
        /// 调用此方法将数据注入，后续可通过 <see cref="Get{T, TKey}(TKey)"/> 等接口统一查询。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="data">按 Id 索引的字典。</param>
        /// <exception cref="ConfigException">data 为 null 时抛出。</exception>
        void RegisterTable<T, TKey>(Dictionary<TKey, T> data) where T : IConfigRow<TKey>;

        /// <summary>
        /// 非泛型注册 Table 数据，供反射调用（如动态遍历 Luban Tables 的 Tb 属性）。
        /// </summary>
        /// <param name="rowType">配置行类型。</param>
        /// <param name="data">按 Id 索引的 <see cref="System.Collections.IDictionary"/>。</param>
        /// <exception cref="ConfigException">rowType 或 data 为 null 时抛出。</exception>
        void RegisterTable(Type rowType, System.Collections.IDictionary data);

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
        ConfigTable<T> GetTable<T>();

        /// <summary>
        /// 安全获取 Table 包装器。未加载时返回 <c>false</c>。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="table">输出的包装器实例。</param>
        /// <returns>Table 已加载时返回 <c>true</c>。</returns>
        bool TryGetTable<T>(out ConfigTable<T> table);

        /// <summary>
        /// 获取 Table 中指定 Id 的配置行。不存在时抛异常。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <returns>对应的配置行实例。</returns>
        /// <exception cref="ConfigException">Table 未加载或 Id 不存在时抛出。</exception>
        T Get<T, TKey>(TKey id) where T : IConfigRow<TKey>;

        /// <summary>
        /// 安全查询 Table 中指定 Id 的配置行。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <param name="row">输出的配置行实例，如果不存在则为 <c>default</c>。</param>
        /// <returns>成功获取时返回 <c>true</c>。</returns>
        bool TryGet<T, TKey>(TKey id, out T row) where T : IConfigRow<TKey>;

        /// <summary>
        /// 获取 Table 中所有配置行（复制为新数组）。
        /// <para>会产生少量 GC（数组分配），频繁调用请使用 <see cref="Get{T, TKey}(TKey)"/> 按 Id 查询。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <returns>所有配置行的数组。</returns>
        /// <exception cref="ConfigException">Table 未加载时抛出。</exception>
        T[] GetAll<T, TKey>() where T : IConfigRow<TKey>;

        /// <summary>
        /// 判断 Table 中是否包含指定 Id 的配置行。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="id">配置行主键。</param>
        /// <returns>存在时返回 <c>true</c>。</returns>
        bool Contains<T, TKey>(TKey id) where T : IConfigRow<TKey>;

        /// <summary>
        /// 获取 Table 中配置行的数量。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <returns>配置行数量，未加载时返回 0。</returns>
        int Count<T, TKey>() where T : IConfigRow<TKey>;

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