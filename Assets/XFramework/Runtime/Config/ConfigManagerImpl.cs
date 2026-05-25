using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XConfig
{
    /// <summary>
    /// <see cref="ConfigManager"/> 的内部实现，管理所有配置的注册、加载、查询和卸载。
    /// <para>Table 类型缓存为 <c>Dictionary<int, T></c>，Global 类型缓存为单个 <c>T</c> 实例。</para>
    /// <para>所有配置加载后常驻内存，不进行 LRU 淘汰（配置数据体量小，不会造成显著内存压力）。</para>
    /// </summary>
    internal sealed class ConfigManagerImpl
    {
        #region Fields

        /// <summary>
        /// 已加载的 Table 数据。key: 配置行类型, value: Dictionary<int, T>（存储为 object 以避免装箱开销）。
        /// </summary>
        private readonly Dictionary<Type, object> _tables = new();

        /// <summary>
        /// 已加载的 Global 数据。key: 配置类型, value: 配置实例（class）。
        /// </summary>
        private readonly Dictionary<Type, object> _globals = new();

        /// <summary>
        /// Loader 实例缓存，按 <see cref="ConfigFormat"/> 索引。
        /// </summary>
        private static readonly Dictionary<ConfigFormat, IConfigLoader> Loaders = new()
        {
            { ConfigFormat.Json, new JsonLoader() },
            { ConfigFormat.ScriptableObject, new ScriptableObjectLoader() },
        };

        #endregion

        #region Preload

        /// <summary>
        /// 预加载 Table 类型的配置。首次调用时自动注册并加载。
        /// <para>如果已加载则直接返回（传入的 <paramref name="assetPath"/> 在重复调用时可选不传）。</para>
        /// </summary>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        public async UniTask PreloadTableAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json)
            where T : IConfigRow, new()
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException(
                    $"assetPath must be provided when preloading Table '{typeof(T).Name}' for the first time.");

            var type = typeof(T);
            if (_tables.ContainsKey(type))
                return;

            try
            {
                var loader = Loaders[format];
                var dict = await loader.LoadTableAsync<T>(assetPath);
                _tables[type] = dict;
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Table '{type.Name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 预加载 Global 类型的配置。首次调用时自动注册并加载。
        /// <para>如果已加载则直接返回。</para>
        /// </summary>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        public async UniTask PreloadGlobalAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json)
            where T : class, new()
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException(
                    $"assetPath must be provided when preloading Global config '{typeof(T).Name}' for the first time.");

            var type = typeof(T);
            if (_globals.ContainsKey(type))
                return;

            try
            {
                var loader = Loaders[format];
                var config = await loader.LoadGlobalAsync<T>(assetPath);
                _globals[type] = config;
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Global config '{type.Name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 使用自定义 Loader 预加载 Table 配置。
        /// <para>Loader 为临时策略对象，调用后不被持有，可由 GC 回收。</para>
        /// <para>适用于 protobuf、MessagePack 等一文件一表的自定义格式。
        /// 对于一文件多表的格式（如 Luban），请使用 <see cref="RegisterTable{T}(Dictionary{int, T})"/>。</para>
        /// </summary>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        public async UniTask PreloadTableAsync<T>(string assetPath, IConfigLoader loader)
            where T : IConfigRow, new()
        {
            if (loader == null)
                throw new ConfigException($"loader cannot be null when preloading Table '{typeof(T).Name}'.");
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException($"assetPath must be provided when preloading Table '{typeof(T).Name}'.");

            var type = typeof(T);
            if (_tables.ContainsKey(type))
                return;

            try
            {
                var dict = await loader.LoadTableAsync<T>(assetPath);
                _tables[type] = dict;
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Table '{type.Name}' with custom loader: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 使用自定义 Loader 预加载 Global 配置。
        /// <para>Loader 为临时策略对象，调用后不被持有。</para>
        /// </summary>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        public async UniTask PreloadGlobalAsync<T>(string assetPath, IConfigLoader loader)
            where T : class, new()
        {
            if (loader == null)
                throw new ConfigException($"loader cannot be null when preloading Global config '{typeof(T).Name}'.");
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException($"assetPath must be provided when preloading Global config '{typeof(T).Name}'.");

            var type = typeof(T);
            if (_globals.ContainsKey(type))
                return;

            try
            {
                var config = await loader.LoadGlobalAsync<T>(assetPath);
                _globals[type] = config;
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Global config '{type.Name}' with custom loader: {ex.Message}", ex);
            }
        }

        #endregion

        #region Register (第三方注入)

        /// <summary>
        /// 注册已反序列化的 Table 数据到配置管理器。
        /// <para>第三方使用 Luban / protobuf / MessagePack 等工具自行反序列化后，
        /// 调用此方法将数据注入，后续可通过 <see cref="Get{T}(int)"/> 等接口统一查询。</para>
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow"/>。</typeparam>
        /// <param name="data">按 Id 索引的字典。</param>
        public void RegisterTable<T>(Dictionary<int, T> data) where T : IConfigRow
        {
            if (data == null)
                throw new ConfigException(
                    $"Cannot register null data for Table '{typeof(T).Name}'.");
            _tables[typeof(T)] = data;
        }

        /// <summary>
        /// 非泛型注册 Table 数据，供反射调用（如动态遍历 Luban Tables 的 Tb 属性）。
        /// </summary>
        /// <param name="rowType">配置行类型。</param>
        /// <param name="data">按 Id 索引的 <see cref="System.Collections.IDictionary"/>。</param>
        public void RegisterTable(Type rowType, System.Collections.IDictionary data)
        {
            if (rowType == null)
                throw new ConfigException("rowType cannot be null.");
            if (data == null)
                throw new ConfigException(
                    $"Cannot register null data for Table '{rowType.Name}'.");
            _tables[rowType] = data;
        }

        /// <summary>
        /// 注册 Global 配置单例到配置管理器。
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class。</typeparam>
        /// <param name="config">配置单例实例。</param>
        public void RegisterGlobal<T>(T config) where T : class
        {
            if (config == null)
                throw new ConfigException(
                    $"Cannot register null config for Global '{typeof(T).Name}'.");
            _globals[typeof(T)] = config;
        }

        #endregion

        #region Query — Table

        /// <summary>
        /// 获取 Table 中指定 Id 的配置行。不存在时抛异常。
        /// </summary>
        public T Get<T>(int id) where T : IConfigRow
        {
            var dict = GetTableDict<T>();
            if (!dict.TryGetValue(id, out var row))
                throw new ConfigException(
                    $"Id '{id}' not found in Table '{typeof(T).Name}'.");
            return row;
        }

        /// <summary>
        /// 安全查询 Table 中指定 Id 的配置行。
        /// </summary>
        public bool TryGet<T>(int id, out T row) where T : IConfigRow
        {
            if (!_tables.TryGetValue(typeof(T), out var obj))
            {
                row = default;
                return false;
            }
            return ((Dictionary<int, T>)obj).TryGetValue(id, out row);
        }

        /// <summary>
        /// 获取 Table 中所有配置行的只读 Span（避免 GC 分配）。
        /// <para>内部由 Dictionary<int, T>.Values 构造，会产生少量 GC。如需零 GC 遍历请使用迭代器模式。</para>
        /// </summary>
        public T[] GetAll<T>() where T : IConfigRow
        {
            var dict = GetTableDict<T>();
            var result = new T[dict.Count];
            dict.Values.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// 判断 Table 中是否包含指定 Id 的配置行。
        /// </summary>
        public bool Contains<T>(int id) where T : IConfigRow
        {
            if (!_tables.TryGetValue(typeof(T), out var obj))
                return false;
            return ((Dictionary<int, T>)obj).ContainsKey(id);
        }

        /// <summary>
        /// 获取 Table 中配置行的数量。
        /// </summary>
        public int Count<T>() where T : IConfigRow
        {
            if (!_tables.TryGetValue(typeof(T), out var obj))
                return 0;
            return ((Dictionary<int, T>)obj).Count;
        }

        #endregion

        #region Query — Global

        /// <summary>
        /// 获取 Global 配置。未加载时抛异常。
        /// </summary>
        public T GetGlobal<T>() where T : class
        {
            var type = typeof(T);
            if (!_globals.TryGetValue(type, out var obj))
                throw new ConfigException(
                    $"Global config '{type.Name}' is not loaded. Call PreloadAsync<{type.Name}>() first.");
            return (T)obj;
        }

        /// <summary>
        /// 安全获取 Global 配置。
        /// </summary>
        public bool TryGetGlobal<T>(out T config) where T : class
        {
            if (_globals.TryGetValue(typeof(T), out var obj))
            {
                config = (T)obj;
                return true;
            }
            config = default;
            return false;
        }

        #endregion

        #region Unload

        /// <summary>
        /// 卸载指定类型的配置（Table 或 Global）。
        /// </summary>
        public void Unload<T>()
        {
            var type = typeof(T);
            _tables.Remove(type);
            _globals.Remove(type);
        }

        /// <summary>
        /// 判断指定类型的配置是否已加载。
        /// </summary>
        public bool IsLoaded<T>()
        {
            var type = typeof(T);
            return _tables.ContainsKey(type) || _globals.ContainsKey(type);
        }

        #endregion

        #region Internal

        private Dictionary<int, T> GetTableDict<T>() where T : IConfigRow
        {
            var type = typeof(T);
            if (!_tables.TryGetValue(type, out var obj))
                throw new ConfigException(
                    $"Table '{type.Name}' is not loaded. Call PreloadAsync<{type.Name}>() first.");
            return (Dictionary<int, T>)obj;
        }

        #endregion
    }
}