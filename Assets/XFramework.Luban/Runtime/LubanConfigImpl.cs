using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XLog;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 配置运行时实现：加载数据 → 构造 Tables → 注册进 ConfigManager。
    /// <para>所有成员都必须在主线程调用（与 Config / Asset 门面的线程契约一致）。</para>
    /// </summary>
    internal sealed class LubanConfigImpl
    {
        #region Private Fields

        private readonly ILubanDataProvider _provider;
        private ILubanConfigAdapter _adapter;
        private object _tables;
        private bool _initialized;

        #endregion

        #region Construction

        internal LubanConfigImpl(ILubanDataProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        #endregion

        #region Properties

        internal bool IsInitialized => _initialized;

        internal object Tables
        {
            get
            {
                EnsureInitialized();
                return _tables;
            }
        }

        #endregion

        #region Lifecycle

        internal async UniTask InitializeAsync(
            ILubanConfigAdapter adapter,
            LubanOptions options,
            IProgress<LubanInitReport> progress,
            CancellationToken cancellationToken)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));

            if (_initialized)
            {
                LogManager.Warning(LubanLog.Category,
                    "InitializeAsync was called more than once. Ignoring duplicate.");
                return;
            }

            options ??= new LubanOptions();
            var files = BuildFiles(adapter, options);

            progress?.Report(new LubanInitReport(0.01f, "Loading Luban data..."));

            Dictionary<string, byte[]> bytes;
            try
            {
                bytes = await _provider.LoadAsync(files, options.LoadMode, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LubanConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new LubanConfigException("[LubanConfig] Failed to load Luban data files.", ex);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (bytes == null)
            {
                throw new LubanConfigException("[LubanConfig] Data provider returned null.");
            }

            progress?.Report(new LubanInitReport(0.8f, "Building Luban tables..."));

            object tables;
            try
            {
                tables = adapter.CreateTables(name =>
                {
                    if (bytes.TryGetValue(name, out var data))
                    {
                        return data;
                    }

                    throw new LubanConfigException(
                        "[LubanConfig] Adapter requested table '" + name +
                        "' which is not present in TableNames.");
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LubanConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new LubanConfigException("[LubanConfig] Failed to create Luban tables.", ex);
            }

            if (tables == null)
            {
                throw new LubanConfigException("[LubanConfig] Adapter CreateTables returned null.");
            }

            EnsureConfigManagerInitialized();

            try
            {
                adapter.RegisterToConfigManager(tables);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LubanConfigException)
            {
                TryUnregister(adapter);
                throw;
            }
            catch (Exception ex)
            {
                TryUnregister(adapter);
                throw new LubanConfigException(
                    "[LubanConfig] Failed to register Luban tables into ConfigManager.", ex);
            }

            _adapter = adapter;
            _tables = tables;
            _initialized = true;

            progress?.Report(new LubanInitReport(1f, "Luban configs loaded."));
        }

        internal void Destroy()
        {
            if (_initialized && _adapter != null)
            {
                if (ConfigManager.IsInitialized)
                {
                    try
                    {
                        _adapter.UnregisterFromConfigManager();
                    }
                    catch (Exception ex)
                    {
                        LogManager.Warning(LubanLog.Category,
                            "Failed to unregister Luban tables: {0}", ex.Message);
                    }
                }
                else
                {
                    LogManager.Warning(LubanLog.Category,
                        "ConfigManager is not initialized; skipping Luban table unregistration.");
                }
            }

            _adapter = null;
            _tables = null;
            _initialized = false;
        }

        /// <summary>
        /// 关闭域重载会话复位：只丢引用，不 unregister（ConfigManager 自己也会复位）。
        /// </summary>
        internal void ResetForNewSession()
        {
            _adapter = null;
            _tables = null;
            _initialized = false;
        }

        #endregion

        #region Query

        internal T GetTables<T>() where T : class
        {
            EnsureInitialized();
            if (_tables is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException(
                "[LubanConfig] Tables instance is '" + _tables.GetType().FullName +
                "', not '" + typeof(T).FullName + "'.");
        }

        internal bool TryGetTables<T>(out T tables) where T : class
        {
            if (_initialized && _tables is T typed)
            {
                tables = typed;
                return true;
            }

            tables = null;
            return false;
        }

        #endregion

        #region Internal

        private static List<LubanTableFile> BuildFiles(
            ILubanConfigAdapter adapter,
            LubanOptions options)
        {
            string extension = adapter.DataFileExtension;
            if (string.IsNullOrEmpty(extension) || extension[0] != '.' || extension.Length == 1)
            {
                throw new LubanConfigException(
                    "[LubanConfig] Adapter DataFileExtension must be a non-empty extension " +
                    "starting with '.', e.g. '.bytes' or '.json'.");
            }

            var tableNames = adapter.TableNames;
            if (tableNames == null || tableNames.Count == 0)
            {
                throw new LubanConfigException(
                    "[LubanConfig] Adapter TableNames must contain at least one table.");
            }

            if (options.LocationResolver == null &&
                string.IsNullOrWhiteSpace(options.DataRoot))
            {
                throw new ArgumentException(
                    "[LubanConfig] LubanOptions.DataRoot cannot be empty when LocationResolver is null.",
                    nameof(options));
            }

            var files = new List<LubanTableFile>(tableNames.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < tableNames.Count; i++)
            {
                string name = tableNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    throw new LubanConfigException(
                        "[LubanConfig] Adapter TableNames contains a null or empty entry.");
                }

                if (!seen.Add(name))
                {
                    throw new LubanConfigException(
                        "[LubanConfig] Adapter TableNames contains duplicate entry '" + name + "'.");
                }

                string location;
                try
                {
                    location = options.LocationResolver != null
                        ? options.LocationResolver(name)
                        : BuildDefaultLocation(options.DataRoot, name, extension);
                }
                catch (Exception ex)
                {
                    throw new LubanConfigException(
                        "[LubanConfig] LocationResolver failed for table '" + name + "'.", ex);
                }

                if (string.IsNullOrEmpty(location))
                {
                    throw new LubanConfigException(
                        "[LubanConfig] Location for table '" + name + "' is null or empty.");
                }

                files.Add(new LubanTableFile(name, location));
            }

            return files;
        }

        private static string BuildDefaultLocation(string dataRoot, string tableName, string extension)
        {
            string root = dataRoot.TrimEnd('/');
            return root.Length == 0
                ? tableName + extension
                : root + "/" + tableName + extension;
        }

        private static void EnsureConfigManagerInitialized()
        {
            if (ConfigManager.IsInitialized)
            {
                return;
            }

            ConfigManager.Initialize();
            LogManager.Info(LubanLog.Category,
                "ConfigManager was not initialized; initialized by LubanConfig.");
        }

        private static void TryUnregister(ILubanConfigAdapter adapter)
        {
            if (!ConfigManager.IsInitialized)
            {
                return;
            }

            try
            {
                adapter.UnregisterFromConfigManager();
            }
            catch (Exception ex)
            {
                LogManager.Warning(LubanLog.Category,
                    "Failed to roll back Luban table registration: {0}", ex.Message);
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized || _tables == null)
            {
                throw new InvalidOperationException(
                    "[LubanConfig] LubanConfig 尚未初始化。请先调用 " +
                    "LubanConfig.InitializeAsync(adapter) 或登记 LubanBootstrapStage。");
            }
        }

        #endregion
    }
}
