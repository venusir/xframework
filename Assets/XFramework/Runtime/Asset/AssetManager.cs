using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XLoader;

namespace XFramework.XAsset
{

    /// <summary>
    /// 全局资源管理器外观。提供静态方法直接访问资源加载、实例化与生命周期管理。
    /// <para>内部持有 <see cref="IAssetManager"/> 实例（<see cref="AssetManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="InitializeAsync(LoadProgress, CancellationToken)"/> 初始化。</para>
    /// </summary>
    public static class AssetManager
    {
        #region Static — Global Singleton

        private static IAssetManager _instance;
        private static bool _instanceInitialized;
        /// <summary>
        /// 全局资源管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化全局资源管理器（默认包）。
        /// </summary>
        /// <param name="progress">初始化进度回调。</param>
        /// <param name="options">初始化配置。为 null 时使用默认配置（默认包 + 离线模式）。</param>
        public static async UniTask InitializeAsync(LoadProgress progress, AssetInitOptions options = null, CancellationToken cancellationToken = default)
        {
            if (_instanceInitialized)
            {
                Debug.LogWarning("[AssetManager] InitializeAsync was called more than once. Ignoring duplicate.");
                return;
            }

            var impl = new AssetManagerImpl();
            await impl.InitializeAsync(progress, options, cancellationToken);

            _instance = impl;
            _instanceInitialized = true;
        }

        /// <inheritdoc cref="IAssetManager.InitializePackageAsync(AssetInitOptions, LoadProgress, CancellationToken)"/>
        public static UniTask InitializePackageAsync(AssetInitOptions options, LoadProgress progress, CancellationToken cancellationToken = default)
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

        /// <inheritdoc cref="IAssetManager.PreloadAllAsync(IEnumerable{string}, CancellationToken)"/>
        public static UniTask PreloadAllAsync(IEnumerable<string> locations, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.PreloadAllAsync(locations, cancellationToken);
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
                    "AssetManager is not initialized. Call AssetManager.InitializeAsync() first.");
        }

        #endregion
    }
}