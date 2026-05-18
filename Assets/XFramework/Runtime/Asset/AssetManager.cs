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
    /// <para>使用前需调用 <see cref="InitializeAsync(CancellationToken)"/> 初始化。</para>
    /// </summary>
    public static class AssetManager
    {
        #region Static — Global Singleton

        private static IAssetManager _instance;
        private static bool _instanceInitialized;
        private static bool _isInitializing;

        /// <summary>
        /// 全局资源管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化全局资源管理器（无进度版本）。
        /// </summary>
        public static async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_instanceInitialized) return;
            if (_isInitializing) return;
            _isInitializing = true;

            try
            {
                var impl = new AssetManagerImpl();
                await impl.InitializeAsync(cancellationToken);

                if (!_instanceInitialized)
                {
                    _instance = impl;
                    _instanceInitialized = true;
                }
                else
                {
                    impl.Dispose();
                }
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// 初始化全局资源管理器（带进度报告版本）。
        /// </summary>
        public static async UniTask InitializeAsync(IProgress<LoadContext> progress, CancellationToken cancellationToken = default)
        {
            if (_instanceInitialized) return;
            if (_isInitializing) return;
            _isInitializing = true;

            try
            {
                var impl = new AssetManagerImpl();
                await impl.InitializeAsync(progress, cancellationToken);

                if (!_instanceInitialized)
                {
                    _instance = impl;
                    _instanceInitialized = true;
                }
                else
                {
                    impl.Dispose();
                }
            }
            finally
            {
                _isInitializing = false;
            }
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

        #region Public API — Load (UniTask)

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, CancellationToken)"/>
        public static UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAsync<T>(location, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, int, CancellationToken)"/>
        public static UniTask<T> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAsync<T>(location, priority, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Transform)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Transform parent = null)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync(location, parent);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Vector3, Quaternion, Transform)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync(location, position, rotation, parent);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Transform)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Transform parent = null) where T : Component
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync<T>(location, parent);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Vector3, Quaternion, Transform)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync<T>(location, position, rotation, parent);
        }

        /// <inheritdoc cref="IAssetManager.LoadSceneAsync(string, bool, Action{float})"/>
        public static UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null)
        {
            EnsureGlobalInitialized();
            return _instance.LoadSceneAsync(location, additive, progress);
        }

        /// <inheritdoc cref="IAssetManager.PreloadAllAsync(IEnumerable{string})"/>
        public static UniTask PreloadAllAsync(IEnumerable<string> locations)
        {
            EnsureGlobalInitialized();
            return _instance.PreloadAllAsync(locations);
        }

        #endregion

        #region Public API — Load (Callback)

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, Action{T}, Action{string})"/>
        public static void LoadAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            _instance.LoadAsync(location, onCompleted, onError);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Action{GameObject}, Action{string}, Transform)"/>
        public static void InstantiateAsync(string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null)
        {
            EnsureGlobalInitialized();
            _instance.InstantiateAsync(location, onCompleted, onError, parent);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Action{T}, Action{string}, Transform)"/>
        public static void InstantiateAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component
        {
            EnsureGlobalInitialized();
            _instance.InstantiateAsync(location, onCompleted, onError, parent);
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

        /// <inheritdoc cref="IAssetManager.Release(UnityEngine.Object)"/>
        public static void Release(UnityEngine.Object asset)
        {
            EnsureGlobalInitialized();
            _instance.Release(asset);
        }

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