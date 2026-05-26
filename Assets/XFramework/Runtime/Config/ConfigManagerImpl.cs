using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XConfig
{
    /// <summary>
    /// <see cref="IConfigManager"/> 的默认实现，管理所有配置的注册、加载、查询和卸载。
    /// <para>Table 类型缓存为 <c>Dictionary{TKey, T}</c>（存储为 <see cref="IDictionary"/>），Global 类型缓存为单个 <c>T</c> 实例。</para>
    /// <para>所有配置加载后常驻内存，不进行 LRU 淘汰（配置数据体量小，不会造成显著内存压力）。</para>
    /// </summary>
    internal sealed class ConfigManagerImpl : IConfigManager
    {
        #region Fields

        /// <summary>
        /// 已加载的 Table 数据。key: 配置行类型, value: Dictionary{TKey, T}（存储为 <see cref="IDictionary"/>）。
        /// </summary>
        private readonly Dictionary<Type, IDictionary> _tables = new();

        /// <summary>
        /// 已加载的 Global 数据。key: 配置类型, value: 配置实例（class）。
        /// </summary>
        private readonly Dictionary<Type, object> _globals = new();

        /// <summary>
        /// 已缓存的 Table 包装器。key: 配置行类型, value: <c>ConfigTable<T></c> 实例（存储为 object）。
        /// <para>与 <see cref="_tables"/> 生命周期同步，避免每次查询时重复分配包装器。</para>
        /// </summary>
        private readonly Dictionary<Type, object> _tableWrappers = new();

        /// <summary>
        /// 已加载配置的 assetPath 记录。key: 类型, value: 首次加载时使用的路径。
        /// <para>用于检测重复加载时路径变化并给出 Warning。</para>
        /// </summary>
        private readonly Dictionary<Type, string> _assetPaths = new();

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

            // 已加载：检查 assetPath 变化 + 返回缓存的包装器
            if (_tables.TryGetValue(type, out var existingDict))
            {
                if (HasAssetPathChanged(type, assetPath))
                    LogAssetPathChanged(type.Name, _assetPaths[type], assetPath);
                return GetOrCreateWrapper<T>(type, existingDict);
            }

            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException(
                    $"assetPath must be provided when preloading Table '{type.Name}' for the first time.");

            var keyType = ConfigTypeHelper.GetKeyType(type);
            try
            {
                var loader = Loaders[format];
                var tableObj = await InvokeLoader(loader, type, keyType, assetPath);
                var table = (ConfigTable<T>)tableObj;
                _tables[type] = table._dict;
                _tableWrappers[type] = table;
                _assetPaths[type] = assetPath;
                return table;
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
            {
                if (HasAssetPathChanged(type, assetPath))
                    LogAssetPathChanged(type.Name, _assetPaths[type], assetPath);
                return;
            }

            try
            {
                var loader = Loaders[format];
                var config = await loader.LoadGlobalAsync<T>(assetPath);
                _globals[type] = config;
                _assetPaths[type] = assetPath;
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

            // 已加载：检查 assetPath 变化 + 返回缓存的包装器
            if (_tables.TryGetValue(type, out var existingDict))
            {
                if (HasAssetPathChanged(type, assetPath))
                    LogAssetPathChanged(type.Name, _assetPaths[type], assetPath);
                return GetOrCreateWrapper<T>(type, existingDict);
            }

            if (loader == null)
                throw new ConfigException($"loader cannot be null when preloading Table '{type.Name}'.");
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException($"assetPath must be provided when preloading Table '{type.Name}'.");

            var keyType = ConfigTypeHelper.GetKeyType(type);
            try
            {
                var tableObj = await InvokeLoader(loader, type, keyType, assetPath);
                var table = (ConfigTable<T>)tableObj;
                _tables[type] = table._dict;
                _tableWrappers[type] = table;
                _assetPaths[type] = assetPath;
                return table;
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
            {
                if (HasAssetPathChanged(type, assetPath))
                    LogAssetPathChanged(type.Name, _assetPaths[type], assetPath);
                return;
            }

            try
            {
                var config = await loader.LoadGlobalAsync<T>(assetPath);
                _globals[type] = config;
                _assetPaths[type] = assetPath;
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
        public void RegisterTable<T>(ConfigTable<T> table) where T : IConfigRow
        {
            if (table == null)
                throw new ConfigException(
                    $"Cannot register null table for Table '{typeof(T).Name}'.");
            var type = typeof(T);
            _tables[type] = table._dict;
            _tableWrappers[type] = table;
        }

        /// <summary>
        /// 非泛型注册 Table 数据，供反射调用（如动态遍历 Luban Tables 的 Tb 属性）。
        /// <para>接收 <see cref="IConfigTable"/> 实例（<c>ConfigTable<T></c> 实现了此接口），
        /// 通过 <see cref="IConfigTable.Data"/> 直接获取内部字典，零反射开销。</para>
        /// </summary>
        public void RegisterTable(Type rowType, IConfigTable table)
        {
            if (rowType == null)
                throw new ConfigException("rowType cannot be null.");
            if (table == null)
                throw new ConfigException(
                    $"Cannot register null table for Table '{rowType.Name}'.");
            _tables[rowType] = table.Data;
            _tableWrappers[rowType] = table;
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
        public ConfigTable<T> GetTable<T>() where T : IConfigRow
        {
            var type = typeof(T);
            if (!_tables.TryGetValue(type, out var dict))
                throw new ConfigException(
                    $"Table '{type.Name}' is not loaded. Call PreloadAsync<{type.Name}>() first.");
            return GetOrCreateWrapper<T>(type, dict);
        }

        /// <summary>
        /// 安全获取 Table 包装器。未加载时返回 <c>false</c>。
        /// </summary>
        public bool TryGetTable<T>(out ConfigTable<T> table) where T : IConfigRow
        {
            if (_tables.TryGetValue(typeof(T), out var dict))
            {
                table = GetOrCreateWrapper<T>(typeof(T), dict);
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
            _tableWrappers.Remove(type);
            _assetPaths.Remove(type);
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
        /// 通过反射调用 IConfigLoader.LoadTableAsync<T, TKey>，返回装箱的 ConfigTable。
        /// <para>每次行类型首次加载时反射一次 MakeGenericMethod，后续同类型走缓存字典路径，零反射开销。</para>
        /// <para>UniTask<T> 是值类型，需通过 AsTask() 转为 Task<T> 再 await，避免装箱后丢失状态。</para>
        /// </summary>
        private static async UniTask<object> InvokeLoader(
            IConfigLoader loader, Type rowType, Type keyType, string assetPath)
        {
            var method = typeof(IConfigLoader)
                .GetMethod(nameof(IConfigLoader.LoadTableAsync), BindingFlags.Instance | BindingFlags.Public);
            var genericMethod = method.MakeGenericMethod(rowType, keyType);
            var taskObj = genericMethod.Invoke(loader, new object[] { assetPath });
            // UniTask<ConfigTable<T>> 是值类型，不能直接转型。
            // 通过 AsTask() 转为 Task<ConfigTable<T>>（class），await 其父类 Task 等待完成，再读 Result。
            var asTaskMethod = taskObj.GetType().GetMethod("AsTask");
            var task = (System.Threading.Tasks.Task)asTaskMethod.Invoke(taskObj, null);
            await task;
            var resultProp = task.GetType().GetProperty("Result");
            return resultProp.GetValue(task);
        }

        /// <summary>
        /// 获取或创建 Table 包装器。优先返回缓存的 <c>ConfigTable<T></c>，避免重复分配。
        /// </summary>
        private ConfigTable<T> GetOrCreateWrapper<T>(Type type, IDictionary dict) where T : IConfigRow
        {
            if (_tableWrappers.TryGetValue(type, out var cached) && cached is ConfigTable<T> table)
                return table;
            var newTable = new ConfigTable<T>(dict);
            _tableWrappers[type] = newTable;
            return newTable;
        }

        /// <summary>
        /// 检查 assetPath 是否与首次加载时不同。
        /// </summary>
        /// <returns><c>true</c> 表示路径已变化（需要 Warning）。</returns>
        private bool HasAssetPathChanged(Type type, string assetPath)
        {
            if (_assetPaths.TryGetValue(type, out var oldPath))
            {
                // assetPath 为 null 或空时视为"未指定"，不报警
                if (!string.IsNullOrEmpty(assetPath) && oldPath != assetPath)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 打印 assetPath 变化的 Warning 日志。
        /// </summary>
        private static void LogAssetPathChanged(string typeName, string oldPath, string newPath)
        {
            Debug.LogWarning(
                $"[Config] '{typeName}' is already loaded from '{oldPath}', " +
                $"ignoring new assetPath '{newPath}'. " +
                $"To load from a different path, call Unload<{typeName}>() first.");
        }

        #endregion
    }
}