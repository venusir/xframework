using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig
{
    /// <summary>
    /// <see cref="IConfigManager"/> 的默认实现，管理所有配置的注册、加载、查询和卸载。
    /// <para>Table 类型缓存为 <c>Dictionary{TKey, T}</c>（装箱为 object），Global 类型缓存为单个 <c>T</c> 实例。</para>
    /// <para>所有配置加载后常驻内存，不进行 LRU 淘汰（配置数据体量小，不会造成显著内存压力）。</para>
    /// </summary>
    internal sealed class ConfigManagerImpl : IConfigManager
    {
        #region Fields

        /// <summary>
        /// 已加载的 Table 数据。key: 配置行类型, value: Dictionary<TKey, T>（存储为 object 以避免额外泛型开销）。
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
        /// 预加载 Table 类型的配置。主键类型由 <typeparamref name="T"/> 通过反射自动提取。
        /// </summary>
        /// <exception cref="ConfigException">assetPath 为空、类型未实现 IConfigRow<> 或加载失败时抛出。</exception>
        public async UniTask<ConfigTable<T>> PreloadTableAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json)
            where T : IConfigRow, new()
        {
            var type = typeof(T);

            // 已加载直接返回包装器
            if (_tables.TryGetValue(type, out var existing))
                return new ConfigTable<T>(existing, ((System.Collections.IDictionary)existing).Count);

            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException(
                    $"assetPath must be provided when preloading Table '{type.Name}' for the first time.");

            var keyType = ConfigTypeHelper.GetKeyType(type);
            try
            {
                var loader = Loaders[format];
                var dict = await InvokeLoader(loader, type, keyType, assetPath);
                _tables[type] = dict;
                return new ConfigTable<T>(dict, dict.Count);
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Table '{type.Name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 预加载 Global 类型的配置。首次调用时自动注册并加载。
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
        /// </summary>
        /// <exception cref="ConfigException">loader 为 null、类型未实现 IConfigRow<> 或加载失败时抛出。</exception>
        public async UniTask<ConfigTable<T>> PreloadTableAsync<T>(string assetPath, IConfigLoader loader)
            where T : IConfigRow, new()
        {
            var type = typeof(T);

            // 已加载直接返回包装器
            if (_tables.TryGetValue(type, out var existing))
                return new ConfigTable<T>(existing, ((System.Collections.IDictionary)existing).Count);

            if (loader == null)
                throw new ConfigException($"loader cannot be null when preloading Table '{type.Name}'.");
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException($"assetPath must be provided when preloading Table '{type.Name}'.");

            var keyType = ConfigTypeHelper.GetKeyType(type);
            try
            {
                var dict = await InvokeLoader(loader, type, keyType, assetPath);
                _tables[type] = dict;
                return new ConfigTable<T>(dict, dict.Count);
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Table '{type.Name}' with custom loader: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 使用自定义 Loader 预加载 Global 配置。
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
        /// 构造 <see cref="ConfigTable{T}"/> 并调用此方法注入。</para>
        /// </summary>
        public void RegisterTable<T>(ConfigTable<T> table)
        {
            if (table == null)
                throw new ConfigException(
                    $"Cannot register null table for Table '{typeof(T).Name}'.");
            _tables[typeof(T)] = ConfigTableUtil.GetDict(table);
        }

        /// <summary>
        /// 非泛型注册 Table 数据，供反射调用（如动态遍历 Luban Tables 的 Tb 属性）。
        /// </summary>
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
        /// 获取指定 Table 的只读包装器。未加载时抛出 <see cref="ConfigException"/>。
        /// </summary>
        public ConfigTable<T> GetTable<T>()
        {
            var type = typeof(T);
            if (!_tables.TryGetValue(type, out var dict))
                throw new ConfigException(
                    $"Table '{type.Name}' is not loaded. Call PreloadAsync<{type.Name}>() first.");
            return new ConfigTable<T>(dict, ((System.Collections.IDictionary)dict).Count);
        }

        /// <summary>
        /// 安全获取 Table 包装器。未加载时返回 <c>false</c>。
        /// </summary>
        public bool TryGetTable<T>(out ConfigTable<T> table)
        {
            if (_tables.TryGetValue(typeof(T), out var dict))
            {
                table = new ConfigTable<T>(dict, ((System.Collections.IDictionary)dict).Count);
                return true;
            }
            table = null;
            return false;
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

        /// <summary>
        /// 通过反射调用 IConfigLoader.LoadTableAsync<T, TKey>。
        /// <para>每次行类型首次加载时反射一次 MakeGenericMethod，后续同类型走缓存字典路径，零反射开销。</para>
        /// <para>UniTask<T> 是值类型，需通过 AsTask() 转为 Task<T> 再 await，避免装箱后丢失状态。</para>
        /// </summary>
        private static async UniTask<System.Collections.IDictionary> InvokeLoader(
            IConfigLoader loader, Type rowType, Type keyType, string assetPath)
        {
            var method = typeof(IConfigLoader)
                .GetMethod(nameof(IConfigLoader.LoadTableAsync), BindingFlags.Instance | BindingFlags.Public);
            var genericMethod = method.MakeGenericMethod(rowType, keyType);
            var taskObj = genericMethod.Invoke(loader, new object[] { assetPath });
            // UniTask<T> 是值类型，不能直接 (UniTask) 转型。
            // 通过 AsTask() 转为 Task<T>（class），await 其父类 Task 等待完成，再读 Result。
            var asTaskMethod = taskObj.GetType().GetMethod("AsTask");
            var task = (System.Threading.Tasks.Task)asTaskMethod.Invoke(taskObj, null);
            await task;
            var resultProp = task.GetType().GetProperty("Result");
            return (System.Collections.IDictionary)resultProp.GetValue(task);
        }

        #endregion
    }

    /// <summary>
    /// 内部工具类，用于提取 <see cref="ConfigTable{T}"/> 包装的内部 Dictionary。
    /// </summary>
    internal static class ConfigTableUtil
    {
        private static readonly FieldInfo DictField = typeof(ConfigTable<>).GetField("_dict",
            BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 提取 ConfigTable 内部存储的 Dictionary<TKey, T>（装箱为 object）。
        /// </summary>
        internal static object GetDict<T>(ConfigTable<T> table)
        {
            var field = typeof(ConfigTable<T>).GetField("_dict",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field.GetValue(table);
        }
    }
}