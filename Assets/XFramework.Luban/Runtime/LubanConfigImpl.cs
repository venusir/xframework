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

        private const string InvalidatedMessage =
            "[LubanConfig] 初始化在完成前被 Destroy()/ResetForNewSession() 作废，结果已丢弃。请重新调用 InitializeAsync()。";

        private readonly ILubanDataProvider _provider;
        private readonly List<InitWaiter> _initWaiters = new();
        private ILubanConfigAdapter _adapter;
        private object _tables;
        private bool _initialized;
        private int _generation;
        private bool _isInitializing;
        private CancellationTokenSource _inflightCts;

        /// <summary>在途初始化的首个调用方 adapter（R4-LBN-010：加入者 adapter 不同时告警）。</summary>
        private ILubanConfigAdapter _inflightAdapter;

        private readonly struct InitWaiter
        {
            internal readonly int Generation;
            internal readonly UniTaskCompletionSource Completion;

            internal InitWaiter(int generation, UniTaskCompletionSource completion)
            {
                Generation = generation;
                Completion = completion;
            }
        }

        #endregion

        #region Construction

        internal LubanConfigImpl(ILubanDataProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        #endregion

        #region Properties

        internal bool IsInitialized => _initialized;

        internal bool IsInitializing => _isInitializing;

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

            if (_isInitializing)
            {
                // R4-LBN-010：首个调用方的 adapter 生效；加入者的 adapter 被静默忽略会让
                // 「注册了哪套表」与它以为的不同。
                if (_inflightAdapter != null && !ReferenceEquals(_inflightAdapter, adapter))
                {
                    LogManager.Warning(LubanLog.Category,
                        "InitializeAsync joined an in-flight initialization with a different adapter instance; " +
                        "the first caller's adapter will be used ({0} instead of {1}).",
                        _inflightAdapter.GetType().Name, adapter.GetType().Name);
                }

                await AwaitInitBroadcast(cancellationToken);
                return;
            }

            int generation = _generation;
            _isInitializing = true;
            _inflightAdapter = adapter;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _inflightCts = cts;

            Exception error = null;
            try
            {
                await InitializeCoreAsync(
                    adapter, options ?? new LubanOptions(), progress, cts.Token, generation);
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                _isInitializing = false;
                if (ReferenceEquals(_inflightAdapter, adapter))
                {
                    _inflightAdapter = null;
                }
                if (ReferenceEquals(_inflightCts, cts))
                {
                    _inflightCts = null;
                }

                BroadcastInitResult(generation, error);
                cts.Dispose();
            }
        }

        private async UniTask InitializeCoreAsync(
            ILubanConfigAdapter adapter,
            LubanOptions options,
            IProgress<LubanInitReport> progress,
            CancellationToken cancellationToken,
            int generation)
        {
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
                // R4-LBN-006：provider 级失败（如 AssetManager 未初始化）也要带出首个表名/location，
                // 否则使用方只看到「Failed to load Luban data files」而不知道查哪张表。
                string context = files.Count > 0
                    ? $" (first table '{files[0].TableName}' at '{files[0].Location}')"
                    : string.Empty;
                throw new LubanConfigException(
                    "[LubanConfig] Failed to load Luban data files" + context + ": " + ex.Message, ex);
            }

            ThrowIfInvalidated(generation, cancellationToken);
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

            ThrowIfInvalidated(generation, cancellationToken);

            if (tables == null)
            {
                throw new LubanConfigException("[LubanConfig] Adapter CreateTables returned null.");
            }

            EnsureConfigManagerInitialized();
            ThrowIfInvalidated(generation, cancellationToken);

            try
            {
                adapter.RegisterToConfigManager(tables);
            }
            catch (OperationCanceledException)
            {
                TryUnregister(adapter);
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

            if (generation != _generation)
            {
                TryUnregister(adapter);
                throw new OperationCanceledException(InvalidatedMessage, cancellationToken);
            }

            _adapter = adapter;
            _tables = tables;
            _initialized = true;

            progress?.Report(new LubanInitReport(1f, "Luban configs loaded."));
        }

        internal void Destroy()
        {
            InvalidateInflight();

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
            InvalidateInflight();

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

        private async UniTask AwaitInitBroadcast(CancellationToken cancellationToken)
        {
            var completion = new UniTaskCompletionSource();
            var waiter = new InitWaiter(_generation, completion);
            lock (_initWaiters)
            {
                _initWaiters.Add(waiter);
            }

            try
            {
                using (cancellationToken.Register(
                           () => completion.TrySetCanceled(cancellationToken)))
                {
                    await completion.Task;
                }
            }
            finally
            {
                lock (_initWaiters)
                {
                    _initWaiters.Remove(waiter);
                }
            }
        }

        private void BroadcastInitResult(int generation, Exception error)
        {
            List<InitWaiter> waiters = null;
            lock (_initWaiters)
            {
                for (int i = _initWaiters.Count - 1; i >= 0; i--)
                {
                    if (_initWaiters[i].Generation != generation)
                    {
                        continue;
                    }

                    waiters ??= new List<InitWaiter>();
                    waiters.Add(_initWaiters[i]);
                    _initWaiters.RemoveAt(i);
                }
            }

            if (waiters == null)
            {
                return;
            }

            for (int i = 0; i < waiters.Count; i++)
            {
                if (error == null)
                {
                    waiters[i].Completion.TrySetResult();
                }
                else
                {
                    waiters[i].Completion.TrySetException(error);
                }
            }
        }

        private void InvalidateInflight()
        {
            int oldGeneration = _generation;
            _generation++;
            _inflightCts?.Cancel();

            List<InitWaiter> waiters = null;
            lock (_initWaiters)
            {
                for (int i = _initWaiters.Count - 1; i >= 0; i--)
                {
                    if (_initWaiters[i].Generation != oldGeneration)
                    {
                        continue;
                    }

                    waiters ??= new List<InitWaiter>();
                    waiters.Add(_initWaiters[i]);
                    _initWaiters.RemoveAt(i);
                }
            }

            if (waiters == null)
            {
                return;
            }

            var exception = new OperationCanceledException(InvalidatedMessage);
            for (int i = 0; i < waiters.Count; i++)
            {
                waiters[i].Completion.TrySetException(exception);
            }
        }

        private void ThrowIfInvalidated(int generation, CancellationToken cancellationToken)
        {
            if (generation != _generation)
            {
                throw new OperationCanceledException(InvalidatedMessage, cancellationToken);
            }
        }

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
