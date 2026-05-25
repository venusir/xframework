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
        /// <para>LubanLoader 通过反射动态解析，避免主程序集硬依赖 Luban 程序集。</para>
        /// </summary>
        private static readonly Dictionary<ConfigFormat, IConfigLoader> Loaders = new()
        {
            { ConfigFormat.Json, new JsonLoader() },
            { ConfigFormat.ScriptableObject, new ScriptableObjectLoader() },
            { ConfigFormat.Luban, TryCreateLubanLoader() },
        };

        /// <summary>
        /// 通过反射尝试创建 LubanLoader 实例。
        /// <para>如果未安装 Luban（缺少 <c>Venusy609.Xframework.Luban</c> 程序集），
        /// 则返回 <c>null</c>，后续 Luban 操作会抛出明确的错误信息。</para>
        /// </summary>
        private static IConfigLoader TryCreateLubanLoader()
        {
            try
            {
                var type = Type.GetType("XFramework.XConfig.LubanLoader, Venusy609.Xframework.Luban");
                if (type != null && typeof(IConfigLoader).IsAssignableFrom(type))
                    return (IConfigLoader)Activator.CreateInstance(type);
            }
            catch
            {
                // Silently fail — 将返回 null
            }
            return null;
        }

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

        #endregion

        #region Load (Luban Tables)

        /// <summary>
        /// 加载 Luban 生成的 Tables（完整格式）。
        /// <para>自动反射提取所有 Tb 表，逐表写入 <c>_tables</c>。重复调用时跳过已加载的表。</para>
        /// <para>若未安装 Luban 程序集则抛出 <see cref="ConfigException"/>。</para>
        /// </summary>
        /// <typeparam name="TTables">Luban 生成的 Tables 类型。</typeparam>
        /// <param name="assetPath">Tables 二进制文件的资源路径。</param>
        /// <exception cref="ConfigException">Luban 未安装或加载失败时抛出。</exception>
        public async UniTask LoadAsync<TTables>(string assetPath)
            where TTables : class, new()
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException(
                    $"assetPath must be provided when loading Luban Tables " +
                    $"'{typeof(TTables).Name}' for the first time.");

            var loader = Loaders[ConfigFormat.Luban];
            if (loader == null)
                throw new ConfigException(
                    "LubanLoader is not available. To use this feature, " +
                    "install the Luban package and generate code with XFramework's Luban templates. " +
                    "See README for details.");

            try
            {
                var allTables = await loader.LoadTablesAsync<TTables>(assetPath);
                if (allTables == null || allTables.Count == 0)
                    throw new ConfigException(
                        $"Luban Tables '{typeof(TTables).Name}' from '{assetPath}' " +
                        "produced no table data.");

                // 逐表写入 _tables，已加载的表跳过
                foreach (var kv in allTables)
                {
                    var rowType = kv.Key;
                    if (!_tables.ContainsKey(rowType))
                        _tables[rowType] = kv.Value;
                }
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    $"Failed to load Luban Tables '{typeof(TTables).Name}' " +
                    $"from '{assetPath}': {ex.Message}", ex);
            }
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