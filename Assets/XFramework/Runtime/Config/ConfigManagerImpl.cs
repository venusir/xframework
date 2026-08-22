using System;
using System.Collections;
using System.Collections.Generic;
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
        /// 进行中的加载任务共享句柄。key: 类型（Table 行类型或 Global 配置类型）, value: 首次启动的加载任务。
        /// <para>仅存进行中任务，完成后在 finally 中移除；并发调用共享同一任务，失败后允许重试。</para>
        /// </summary>
        private readonly Dictionary<Type, InFlightLoad> _inFlightLoads = new();

        /// <summary>
        /// Loader 实例缓存，按 <see cref="ConfigFormat"/> 索引。
        /// </summary>
        private static readonly Dictionary<ConfigFormat, IConfigLoader> Loaders = new()
        {
            { ConfigFormat.Json, new JsonLoader() },
            { ConfigFormat.ScriptableObject, new ScriptableObjectLoader() },
            { ConfigFormat.Csv, new CsvLoader() },
        };

        /// <summary>
        /// 内部配置变更事件，由 <see cref="ConfigManager"/> 订阅以驱动 <c>ConfigManager.ConfigChanged</c>。
        /// </summary>
        internal event Action<Type> InternalConfigChanged;

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

            // 并发调用共享同一进行中的任务；任务成功后 _tables 已写入，直接取缓存包装器
            if (_inFlightLoads.TryGetValue(type, out var load))
            {
                await AwaitInFlight(load);
                return GetOrCreateWrapper<T>(type, _tables[type]);
            }

            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException(
                    $"assetPath must be provided when preloading Table '{type.Name}' for the first time.");

            var task = PreloadTableCoreAsync<T>(assetPath, format, null);
            var inFlight = new InFlightLoad { Task = task };
            _inFlightLoads[type] = inFlight;
            try
            {
                return await task;
            }
            catch (Exception ex)
            {
                inFlight.Exception = ex;
                throw;
            }
            finally
            {
                CompleteInFlight(type, inFlight);
            }
        }

        /// <summary>
        /// 预加载 Global 类型的配置。首次调用时自动注册并加载。
        /// </summary>
        /// <exception cref="ConfigException">assetPath 为空或加载失败时抛出。</exception>
        public async UniTask PreloadGlobalAsync<T>(string assetPath, ConfigFormat format = ConfigFormat.Json)
            where T : class, new()
        {
            var type = typeof(T);

            // 已加载：检查 assetPath 变化 + 直接返回（与 Table 版对齐，已加载后省略路径不报错）
            if (_globals.ContainsKey(type))
            {
                if (HasAssetPathChanged(type, assetPath))
                    LogAssetPathChanged(type.Name, _assetPaths[type], assetPath);
                return;
            }

            // 并发调用共享同一进行中的任务
            if (_inFlightLoads.TryGetValue(type, out var load))
            {
                await AwaitInFlight(load);
                return;
            }

            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException(
                    $"assetPath must be provided when preloading Global config '{type.Name}' for the first time.");

            var task = PreloadGlobalCoreAsync<T>(assetPath, format, null);
            var inFlight = new InFlightLoad { Task = task };
            _inFlightLoads[type] = inFlight;
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                inFlight.Exception = ex;
                throw;
            }
            finally
            {
                CompleteInFlight(type, inFlight);
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

            // 并发调用共享同一进行中的任务；任务成功后 _tables 已写入，直接取缓存包装器
            if (_inFlightLoads.TryGetValue(type, out var load))
            {
                await AwaitInFlight(load);
                return GetOrCreateWrapper<T>(type, _tables[type]);
            }

            if (loader == null)
                throw new ConfigException($"loader cannot be null when preloading Table '{type.Name}'.");
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException($"assetPath must be provided when preloading Table '{type.Name}'.");

            var task = PreloadTableCoreAsync<T>(assetPath, default, loader);
            var inFlight = new InFlightLoad { Task = task };
            _inFlightLoads[type] = inFlight;
            try
            {
                return await task;
            }
            catch (Exception ex)
            {
                inFlight.Exception = ex;
                throw;
            }
            finally
            {
                CompleteInFlight(type, inFlight);
            }
        }

        /// <summary>
        /// Table 实际加载流程。两个 <see cref="PreloadTableAsync{T}(string, ConfigFormat)"/> 重载共用；
        /// <paramref name="customLoader"/> 为 null 时按 <paramref name="format"/> 从内置 Loaders 解析。
        /// </summary>
        private async UniTask<ConfigTable<T>> PreloadTableCoreAsync<T>(
            string assetPath, ConfigFormat format, IConfigLoader customLoader)
            where T : IConfigRow, new()
        {
            var type = typeof(T);
            var keyType = ConfigTypeHelper.GetKeyType(type);
            try
            {
                var loader = customLoader ?? Loaders[format];
                var tableObj = await InvokeLoader<T>(loader, keyType, assetPath);
                var table = (ConfigTable<T>)tableObj;
                _tables[type] = table._dict;
                _tableWrappers[type] = table;
                _assetPaths[type] = assetPath;
                InternalConfigChanged?.Invoke(type);
                return table;
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                // 按重载区分历史文案，消息逐字保留
                var prefix = customLoader == null
                    ? $"Failed to preload Table '{type.Name}'"
                    : $"Failed to preload Table '{type.Name}' with custom loader";
                throw new ConfigException($"{prefix}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 使用自定义 Loader 预加载 Global 配置。
        /// </summary>
        /// <exception cref="ConfigException">loader 为 null、assetPath 为空或加载失败时抛出。</exception>
        public async UniTask PreloadGlobalAsync<T>(string assetPath, IConfigLoader loader)
            where T : class, new()
        {
            var type = typeof(T);

            // 已加载：检查 assetPath 变化 + 直接返回（与 Table 版对齐，已加载后省略路径不报错）
            if (_globals.ContainsKey(type))
            {
                if (HasAssetPathChanged(type, assetPath))
                    LogAssetPathChanged(type.Name, _assetPaths[type], assetPath);
                return;
            }

            // 并发调用共享同一进行中的任务
            if (_inFlightLoads.TryGetValue(type, out var load))
            {
                await AwaitInFlight(load);
                return;
            }

            if (loader == null)
                throw new ConfigException($"loader cannot be null when preloading Global config '{type.Name}'.");
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException($"assetPath must be provided when preloading Global config '{type.Name}'.");

            var task = PreloadGlobalCoreAsync<T>(assetPath, default, loader);
            var inFlight = new InFlightLoad { Task = task };
            _inFlightLoads[type] = inFlight;
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                inFlight.Exception = ex;
                throw;
            }
            finally
            {
                CompleteInFlight(type, inFlight);
            }
        }

        /// <summary>
        /// Global 实际加载流程。两个 <see cref="PreloadGlobalAsync{T}(string, ConfigFormat)"/> 重载共用；
        /// <paramref name="customLoader"/> 为 null 时按 <paramref name="format"/> 从内置 Loaders 解析。
        /// </summary>
        private async UniTask PreloadGlobalCoreAsync<T>(
            string assetPath, ConfigFormat format, IConfigLoader customLoader)
            where T : class, new()
        {
            var type = typeof(T);
            try
            {
                var loader = customLoader ?? Loaders[format];
                var config = await loader.LoadGlobalAsync<T>(assetPath);
                _globals[type] = config;
                _assetPaths[type] = assetPath;
                InternalConfigChanged?.Invoke(type);
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                // 按重载区分历史文案，消息逐字保留
                var prefix = customLoader == null
                    ? $"Failed to preload Global config '{type.Name}'"
                    : $"Failed to preload Global config '{type.Name}' with custom loader";
                throw new ConfigException($"{prefix}: {ex.Message}", ex);
            }
        }

        #endregion

        #region Batch Preload

        /// <inheritdoc/>
        public async UniTask PreloadGroupAsync(string groupName, ConfigManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("groupName cannot be null or empty.", nameof(groupName));

            foreach (var entry in manifest.Entries)
            {
                if (entry.Group != groupName)
                    continue;
                await LoadManifestEntry(entry);
            }
        }

        /// <inheritdoc/>
        public async UniTask PreloadAllAsync(ConfigManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            foreach (var entry in manifest.Entries)
            {
                await LoadManifestEntry(entry);
            }
        }

        /// <summary>
        /// 按清单条目调度到正确的加载路径（仅首次未加载时触发加载）。
        /// <para>已加载的条目自动跳过；并发调用共享同一进行中的任务。</para>
        /// <para>通过委托缓存调用运行时 Type 版本，仅清单加载时用一次，非热路径。</para>
        /// </summary>
        private async UniTask LoadManifestEntry(ConfigManifestEntry entry)
        {
            if (entry.IsTable)
            {
                // 已加载则跳过
                if (_tables.ContainsKey(entry.RowType))
                    return;

                // 并发调用共享同一进行中的任务
                if (_inFlightLoads.TryGetValue(entry.RowType, out var load))
                {
                    await AwaitInFlight(load);
                    return;
                }

                var task = LoadManifestTableCoreAsync(entry);
                var inFlight = new InFlightLoad { Task = task };
                _inFlightLoads[entry.RowType] = inFlight;
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    inFlight.Exception = ex;
                    throw;
                }
                finally
                {
                    CompleteInFlight(entry.RowType, inFlight);
                }
            }
            else
            {
                // Global
                if (_globals.ContainsKey(entry.RowType))
                    return;

                // 并发调用共享同一进行中的任务
                if (_inFlightLoads.TryGetValue(entry.RowType, out var load))
                {
                    await AwaitInFlight(load);
                    return;
                }

                var task = LoadManifestGlobalCoreAsync(entry);
                var inFlight = new InFlightLoad { Task = task };
                _inFlightLoads[entry.RowType] = inFlight;
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    inFlight.Exception = ex;
                    throw;
                }
                finally
                {
                    CompleteInFlight(entry.RowType, inFlight);
                }
            }
        }

        /// <summary>
        /// 清单 Table 条目的实际加载流程（运行时 Type 路径，仅清单批量加载使用）。
        /// </summary>
        private async UniTask LoadManifestTableCoreAsync(ConfigManifestEntry entry)
        {
            var keyType = ConfigTypeHelper.GetKeyType(entry.RowType);
            try
            {
                var loader = Loaders[entry.Format];
                var tableObj = await InvokeLoaderByType(loader, entry.RowType, keyType, entry.AssetPath);
                if (!(tableObj is IConfigTable configTable))
                    throw new ConfigException($"Failed to get _dict from loaded Table '{entry.RowType.Name}'.");
                _tables[entry.RowType] = configTable.Data;
                _tableWrappers[entry.RowType] = tableObj;
                _assetPaths[entry.RowType] = entry.AssetPath;
                InternalConfigChanged?.Invoke(entry.RowType);
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Table '{entry.RowType.Name}' from manifest: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 清单 Global 条目的实际加载流程（运行时 Type 路径，仅清单批量加载使用）。
        /// </summary>
        private async UniTask LoadManifestGlobalCoreAsync(ConfigManifestEntry entry)
        {
            try
            {
                var loader = Loaders[entry.Format];
                var config = await ConfigLoadHelper.InvokeGlobalAsync(loader, entry.RowType, entry.AssetPath);
                _globals[entry.RowType] = config;
                _assetPaths[entry.RowType] = entry.AssetPath;
                InternalConfigChanged?.Invoke(entry.RowType);
            }
            catch (Exception ex) when (ex is not ConfigException)
            {
                throw new ConfigException(
                    $"Failed to preload Global '{entry.RowType.Name}' from manifest: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 非泛型版本的 Loader 调用（运行时 Type），仅用于清单批量加载。
        /// </summary>
        private static UniTask<object> InvokeLoaderByType(
            IConfigLoader loader, Type rowType, Type keyType, string assetPath)
        {
            return ConfigLoadHelper.InvokeAsync(loader, rowType, keyType, assetPath);
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
            InternalConfigChanged?.Invoke(type);
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
            InternalConfigChanged?.Invoke(rowType);
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
            InternalConfigChanged?.Invoke(typeof(T));
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

        /// <summary>
        /// 按主键直接获取配置行。Table 未加载或键不存在时抛异常。
        /// </summary>
        public T Get<T, TKey>(TKey key) where T : IConfigRow
        {
            return GetTable<T>().Get(key);
        }

        /// <summary>
        /// 安全按主键获取配置行。Table 未加载或键不存在时返回 <c>false</c>。
        /// </summary>
        public bool TryGet<T, TKey>(TKey key, out T value) where T : IConfigRow
        {
            if (TryGetTable<T>(out var table))
                return table.TryGet(key, out value);
            value = default;
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
        /// <para>若该类型正在加载中（in-flight），加载完成仍会注册数据，此处给出 Warning 提示竞态。</para>
        /// </summary>
        public void Unload<T>()
        {
            var type = typeof(T);
            if (_inFlightLoads.ContainsKey(type))
                Debug.LogWarning(
                    $"[Config] Unloading '{type.Name}' while a load is in progress; " +
                    $"the in-flight load will still complete and register the data.");
            _tables.Remove(type);
            _globals.Remove(type);
            _tableWrappers.Remove(type);
            _assetPaths.Remove(type);
            InternalConfigChanged?.Invoke(type);
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
        /// 通过缓存的强类型委托调用 IConfigLoader.LoadTableAsync<T, TKey>，返回装箱的 ConfigTable。
        /// <para>每对 (rowType, keyType) 仅首次通过反射构造委托，后续走缓存零反射开销。</para>
        /// </summary>
        private static UniTask<object> InvokeLoader<T>(
            IConfigLoader loader, Type keyType, string assetPath)
        {
            return ConfigLoadHelper.InvokeAsync(loader, typeof(T), keyType, assetPath);
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
        /// 进行中的加载任务共享句柄。UniTask 的 promise 只支持单个 continuation，
        /// 多个等待者不能直接 await 同一进行中任务；由创建者 await 实际任务，
        /// 加入者各自持有完成信号（Waiters），创建者完成时同步广播结果。
        /// </summary>
        private sealed class InFlightLoad
        {
            /// <summary>实际加载任务（仅创建者 await）。</summary>
            public UniTask Task;

            /// <summary>加载失败异常（创建者 catch 记录，广播给加入者，保持同一异常实例）。</summary>
            public Exception Exception;

            /// <summary>加入者的完成信号（创建者完成时广播 TrySetResult/TrySetException）。</summary>
            public List<UniTaskCompletionSource> Waiters = new();
        }

        /// <summary>
        /// 加入者等待广播。注册自己的完成信号而非直接 await 共享任务（UniTask promise 只支持单个 continuation）。
        /// <para>单线程模型下，进入「进行中」分支时创建者任务必未完成，广播不会漏掉。</para>
        /// </summary>
        private static async UniTask AwaitInFlight(InFlightLoad load)
        {
            var tcs = new UniTaskCompletionSource();
            load.Waiters.Add(tcs);
            await tcs.Task;
        }

        /// <summary>
        /// 创建者完成时广播结果给所有加入者并清理登记。
        /// <para>仅当字典登记仍指向本次加载时清理，避免误清并发中新启动的任务（等价于原 RemoveInFlightIfMine 语义）。</para>
        /// </summary>
        private void CompleteInFlight(Type type, InFlightLoad load)
        {
            if (!_inFlightLoads.TryGetValue(type, out var current) || !ReferenceEquals(current, load))
                return;
            if (load.Exception != null)
            {
                for (int i = 0; i < load.Waiters.Count; i++)
                    load.Waiters[i].TrySetException(load.Exception);
            }
            else
            {
                for (int i = 0; i < load.Waiters.Count; i++)
                    load.Waiters[i].TrySetResult();
            }
            _inFlightLoads.Remove(type);
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