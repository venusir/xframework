using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XFramework.XAsset
{

    /// <summary>
    /// 全局资源管理器外观。提供静态方法直接访问资源加载、实例化与生命周期管理。
    /// <para>内部持有 <see cref="IAssetManager"/> 实例（<see cref="AssetManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="InitializeAsync()"/> 初始化。</para>
    /// </summary>
    public static class AssetManager
    {
        #region Static — Global Singleton

        private static IAssetManager _instance;
        private static bool _instanceInitialized;

        /// <summary>进行中的初始化任务。并发调用共享同一任务，避免重复创建实例；完成后清空，允许失败重试与 Destroy 后重建。</summary>
        private static UniTask _initializeTask;

        /// <summary>等待初始化的加入者信号（UniTask promise 只支持单个 continuation，加入者各自持有信号等待创建者广播）。</summary>
        private static readonly List<UniTaskCompletionSource> _initWaiters = new();

        /// <summary>初始化失败异常（创建者 catch 记录，广播给加入者，保持同一异常实例）。</summary>
        private static Exception _initException;

        /// <summary>初始化代际号。Destroy()/SetInstance() 时递增，使在途初始化结果作废，防止销毁后实例"复活"。</summary>
        private static int _initGeneration;

        /// <summary>测试钩子：实例工厂。默认创建 <see cref="AssetManagerImpl"/>；测试注入假实现以验证并发共享语义。</summary>
        internal static Func<IAssetManager> ImplFactory;

        /// <summary>
        /// 全局资源管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化全局资源管理器（默认包）。
        /// <para>并发调用共享同一进行中的初始化任务；共享任务的取消令牌取首个调用者，其余调用者的令牌不参与该任务。</para>
        /// </summary>
        /// <param name="options">初始化配置。为 null 时使用默认配置（默认包 + 离线模式）。</param>
        /// <param name="progress">初始化进度上报（可空），见 <see cref="AssetInitReport"/>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async UniTask InitializeAsync(AssetInitOptions options = null, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            if (_instanceInitialized)
            {
                Debug.LogWarning("[AssetManager] InitializeAsync was called more than once. Ignoring duplicate.");
                return;
            }

            // 并发调用共享同一进行中的任务；加入者不直接 await 共享任务
            //（UniTask promise 只支持单个 continuation），注册自己的信号等待创建者广播
            if (_initializeTask.Status == UniTaskStatus.Pending)
            {
                await AwaitInitBroadcast();
                return;
            }

            var task = InitializeAsyncCore(progress, options, cancellationToken);
            _initializeTask = task;
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                _initException = ex;
                throw;
            }
            finally
            {
                // 成功/失败/取消后均广播加入者并清空缓存，允许再次初始化（失败重试、Destroy 后重建）；
                // 仅当缓存仍指向本次任务时清除，避免误清并发中新启动的任务
                BroadcastInitResult();
                if (_initializeTask.Equals(task))
                {
                    _initializeTask = default;
                }
            }
        }

        /// <summary>
        /// 加入者等待初始化完成广播。注册自己的完成信号而非直接 await 共享任务
        /// （UniTask promise 只支持单个 continuation）。
        /// </summary>
        private static async UniTask AwaitInitBroadcast()
        {
            var tcs = new UniTaskCompletionSource();
            _initWaiters.Add(tcs);
            await tcs.Task;
        }

        /// <summary>
        /// 创建者完成时广播结果给所有加入者并清空信号（成功 TrySetResult，失败/取消 TrySetException）。
        /// </summary>
        private static void BroadcastInitResult()
        {
            if (_initException != null)
            {
                for (int i = 0; i < _initWaiters.Count; i++)
                    _initWaiters[i].TrySetException(_initException);
            }
            else
            {
                for (int i = 0; i < _initWaiters.Count; i++)
                    _initWaiters[i].TrySetResult();
            }
            _initWaiters.Clear();
            _initException = null;
        }

        /// <summary>
        /// 实际初始化流程：创建实例 → 初始化 → 校验代际号后置入全局。
        /// </summary>
        private static async UniTask InitializeAsyncCore(IProgress<AssetInitReport> progress, AssetInitOptions options, CancellationToken cancellationToken)
        {
            int generation = _initGeneration;
            var impl = ImplFactory?.Invoke() ?? new AssetManagerImpl();
            try
            {
                await impl.InitializeAsync(options, progress, cancellationToken);
            }
            catch
            {
                // 初始化失败：释放半初始化状态，不让 _instance 占用，允许重试
                impl.Dispose();
                throw;
            }

            // Destroy()/SetInstance() 与初始化并发时，丢弃在途结果，防止销毁后实例"复活"
            if (generation != _initGeneration)
            {
                impl.Dispose();
                return;
            }

            _instance = impl;
            _instanceInitialized = true;
        }

        /// <inheritdoc cref="IAssetManager.InitializePackageAsync(AssetInitOptions, IProgress{AssetInitReport}, CancellationToken)"/>
        public static UniTask InitializePackageAsync(AssetInitOptions options, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.InitializePackageAsync(options, progress, cancellationToken);
        }

        /// <summary>
        /// 设置外部已创建的实例作为全局管理器。
        /// <para>适用于依赖注入或单元测试场景。</para>
        /// </summary>
        public static void SetInstance(IAssetManager manager)
        {
            _instance = manager ?? throw new ArgumentNullException(nameof(manager));
            _instanceInitialized = true;

            // 作废在途初始化：注入实例优先，在途任务结果不得覆盖
            _initGeneration++;
            _initializeTask = default;
        }

        /// <summary>
        /// 销毁全局资源管理器，释放所有资源。
        /// </summary>
        public static void Destroy()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
            _instanceInitialized = false;

            // 作废在途初始化任务：结果丢弃，可立即重新初始化
            _initGeneration++;
            _initializeTask = default;
        }

        #endregion

        #region Public API — Unload & Query

        /// <inheritdoc cref="IAssetManager.UnloadUnusedAssetsAsync(string, CancellationToken)"/>
        public static UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.UnloadUnusedAssetsAsync(packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.TryUnloadUnusedAsset(string, string)"/>
        public static void TryUnloadUnusedAsset(string location, string packageName = null)
        {
            EnsureGlobalInitialized();
            _instance.TryUnloadUnusedAsset(location, packageName);
        }

        /// <inheritdoc cref="IAssetManager.CheckLocationValid(string, string)"/>
        public static bool CheckLocationValid(string location, string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.CheckLocationValid(location, packageName);
        }

        /// <inheritdoc cref="IAssetManager.IsNeedDownloadFromRemote(string, string)"/>
        public static bool IsNeedDownloadFromRemote(string location, string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.IsNeedDownloadFromRemote(location, packageName);
        }

        #endregion

        #region Public API — Hot Update

        /// <inheritdoc cref="IAssetManager.RequestPackageVersionAsync(string, CancellationToken)"/>
        public static UniTask<string> RequestPackageVersionAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.RequestPackageVersionAsync(packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.UpdatePackageManifestAsync(string, string, CancellationToken)"/>
        public static UniTask UpdatePackageManifestAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.UpdatePackageManifestAsync(packageVersion, packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.PreDownloadContentAsync(string, string, CancellationToken)"/>
        public static UniTask PreDownloadContentAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.PreDownloadContentAsync(packageVersion, packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.GetPackageVersion(string)"/>
        public static string GetPackageVersion(string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.GetPackageVersion(packageName);
        }

        /// <inheritdoc cref="IAssetManager.CreateDownloader(string[], int, int, string)"/>
        public static AssetDownloaderHandle CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8, int failedRetryCount = 3, string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.CreateDownloader(tags, downloadingMaxNumber, failedRetryCount, packageName);
        }

        /// <inheritdoc cref="IAssetManager.DownloadAssetsAsync(string[], Action{float}, string, CancellationToken)"/>
        public static UniTask<bool> DownloadAssetsAsync(string[] tags = null, Action<float> progress = null, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.DownloadAssetsAsync(tags, progress, packageName, cancellationToken);
        }

        #endregion

        #region Public API — Load (UniTask)

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, CancellationToken)"/>
        public static UniTask<AssetHandle<T>> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAsync<T>(location, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, int, CancellationToken)"/>
        public static UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAsync<T>(location, priority, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Transform, CancellationToken)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync(location, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Vector3, Quaternion, Transform, CancellationToken)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync(location, position, rotation, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Transform, CancellationToken)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync<T>(location, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Vector3, Quaternion, Transform, CancellationToken)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync<T>(location, position, rotation, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadSceneAsync(string, bool, Action{float}, CancellationToken)"/>
        public static UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.LoadSceneAsync(location, additive, progress, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.PreloadAllAsync(IEnumerable{string}, Action{float}, CancellationToken)"/>
        public static UniTask PreloadAllAsync(IEnumerable<string> locations, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.PreloadAllAsync(locations, progress, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadAllAsync{T}(IReadOnlyList{string}, CancellationToken)"/>
        public static UniTask<AssetHandle<T>[]> LoadAllAsync<T>(IReadOnlyList<string> locations, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAllAsync<T>(locations, cancellationToken);
        }

        #endregion

        #region Public API — Load (Sync)

        /// <inheritdoc cref="IAssetManager.LoadSync{T}(string)"/>
        public static AssetHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadSync<T>(location);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateSync(string, Transform)"/>
        public static GameObject InstantiateSync(string location, Transform parent = null)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateSync(location, parent);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateSync(string, Vector3, Quaternion, Transform)"/>
        public static GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateSync(location, position, rotation, parent);
        }

        #endregion

        #region Public API — Sub Assets

        /// <inheritdoc cref="IAssetManager.LoadSubAssetsAsync(string, CancellationToken)"/>
        public static UniTask<SubAssetsHandle> LoadSubAssetsAsync(string location, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.LoadSubAssetsAsync(location, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadSubAssetsSync(string)"/>
        public static SubAssetsHandle LoadSubAssetsSync(string location)
        {
            EnsureGlobalInitialized();
            return _instance.LoadSubAssetsSync(location);
        }

        #endregion

        #region Public API — Raw File

        /// <inheritdoc cref="IAssetManager.LoadRawFileAsync(string, CancellationToken)"/>
        public static UniTask<RawFileHandle> LoadRawFileAsync(string location, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.LoadRawFileAsync(location, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadRawFileSync(string)"/>
        public static RawFileHandle LoadRawFileSync(string location)
        {
            EnsureGlobalInitialized();
            return _instance.LoadRawFileSync(location);
        }

        #endregion

        #region Public API — Pool Config

        /// <inheritdoc cref="IAssetManager.SetPoolMaxSize(string, int)"/>
        public static void SetPoolMaxSize(string location, int maxSize)
        {
            EnsureGlobalInitialized();
            _instance.SetPoolMaxSize(location, maxSize);
        }

        /// <inheritdoc cref="IAssetManager.GetPoolStatus(string)"/>
        public static (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
        {
            EnsureGlobalInitialized();
            return _instance.GetPoolStatus(location);
        }

        #endregion

        #region Public API — Lifecycle

        /// <inheritdoc cref="IAssetManager.DestroyInstance(GameObject)"/>
        public static void DestroyInstance(GameObject instance)
        {
            EnsureGlobalInitialized();
            _instance.DestroyInstance(instance);
        }

        /// <inheritdoc cref="IAssetManager.DestroyInstance{T}(T)"/>
        public static void DestroyInstance<T>(T component) where T : Component
        {
            EnsureGlobalInitialized();
            _instance.DestroyInstance(component);
        }

        #endregion

        #region Internal

        private static void EnsureGlobalInitialized()
        {
            if (!_instanceInitialized || _instance == null)
                throw new InvalidOperationException(
                    "[AssetManager] AssetManager 尚未初始化。请先调用 AssetManager.InitializeAsync() 完成初始化（或由节点树 AssetBootstrapNode 自动初始化）。");
        }

        #endregion
    }
}