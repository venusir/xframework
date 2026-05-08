using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XFramework
{
    /// <summary>
    /// 全局资源系统静态入口。提供与节点树无关的公共资源加载 API。
    /// <para>所有方法委托到 <see cref="Instance"/>，需先调用 <see cref="InitializeAsync"/> 初始化。</para>
    /// <para>使用示例:</para>
    /// <code>
    /// await AssetSystem.InitializeAsync();
    /// var asset = await AssetSystem.LoadAsync<GameObject>("characters/player");
    /// AssetSystem.Release(asset);
    /// </code>
    /// </summary>
    public static class AssetSystem
    {
        #region Private Fields

        private static IAssetService _instance;
        private static bool _initialized;

        #endregion

        #region Public Properties

        /// <summary>
        /// 全局资源服务实例。未初始化时访问会抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        public static IAssetService Instance
        {
            get
            {
                if (!_initialized || _instance == null)
                    throw new InvalidOperationException(
                        "AssetSystem is not initialized. Call AssetSystem.InitializeAsync() first.");
                return _instance;
            }
        }

        /// <summary>
        /// 是否已初始化。
        /// </summary>
        public static bool IsInitialized => _initialized && _instance != null;

        #endregion

        #region Initialize

        /// <summary>
        /// 初始化全局资源系统（无进度版本）。
        /// <para>通常在 <see cref="GameLauncher"/> 中显式调用。</para>
        /// </summary>
        public static async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized) return;

            var service = new AssetService();
            await service.InitializeAsync(cancellationToken);

            _instance = service;
            _initialized = true;
        }

        /// <summary>
        /// 初始化全局资源系统（带进度报告版本）。
        /// </summary>
        /// <param name="progress">初始化进度回调。</param>
        public static async UniTask InitializeAsync(IProgress<LoadContext> progress, CancellationToken cancellationToken = default)
        {
            if (_initialized) return;

            var service = new AssetService();
            await service.InitializeAsync(progress, cancellationToken);

            _instance = service;
            _initialized = true;
        }

        /// <summary>
        /// 使用外部已创建的 <see cref="IAssetService"/> 实例设置为全局服务。
        /// <para>适用于依赖注入或单元测试场景。</para>
        /// </summary>
        /// <param name="service">已初始化的资源服务实例。</param>
        public static void SetInstance(IAssetService service)
        {
            _instance = service ?? throw new ArgumentNullException(nameof(service));
            _initialized = true;
        }

        /// <summary>
        /// 销毁全局资源系统，释放所有资源。
        /// </summary>
        public static void Destroy()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
            _initialized = false;
        }

        #endregion

        #region Static Delegates — Load (UniTask)

        /// <inheritdoc cref="IAssetService.LoadAsync{T}(string, CancellationToken)"/>
        public static UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => Instance.LoadAsync<T>(location, cancellationToken);

        /// <inheritdoc cref="IAssetService.LoadAsync{T}(string, int, CancellationToken)"/>
        public static UniTask<T> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => Instance.LoadAsync<T>(location, priority, cancellationToken);

        /// <inheritdoc cref="IAssetService.InstantiateAsync(string, Transform)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Transform parent = null)
            => Instance.InstantiateAsync(location, parent);

        /// <inheritdoc cref="IAssetService.InstantiateAsync(string, Vector3, Quaternion, Transform)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
            => Instance.InstantiateAsync(location, position, rotation, parent);

        /// <inheritdoc cref="IAssetService.InstantiateAsync{T}(string, Transform)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Transform parent = null) where T : Component
            => Instance.InstantiateAsync<T>(location, parent);

        /// <inheritdoc cref="IAssetService.InstantiateAsync{T}(string, Vector3, Quaternion, Transform)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
            => Instance.InstantiateAsync<T>(location, position, rotation, parent);

        /// <inheritdoc cref="IAssetService.LoadSceneAsync(string, bool, Action{float})"/>
        public static UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null)
            => Instance.LoadSceneAsync(location, additive, progress);

        /// <inheritdoc cref="IAssetService.PreloadAllAsync(IEnumerable{string})"/>
        public static UniTask PreloadAllAsync(IEnumerable<string> locations)
            => Instance.PreloadAllAsync(locations);

        #endregion

        #region Static Delegates — Load (Callback)

        /// <inheritdoc cref="IAssetService.LoadAsync{T}(string, Action{T}, Action{string})"/>
        public static void LoadAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null) where T : UnityEngine.Object
            => Instance.LoadAsync(location, onCompleted, onError);

        /// <inheritdoc cref="IAssetService.InstantiateAsync(string, Action{GameObject}, Action{string}, Transform)"/>
        public static void InstantiateAsync(string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null)
            => Instance.InstantiateAsync(location, onCompleted, onError, parent);

        /// <inheritdoc cref="IAssetService.InstantiateAsync{T}(string, Action{T}, Action{string}, Transform)"/>
        public static void InstantiateAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component
            => Instance.InstantiateAsync(location, onCompleted, onError, parent);

        #endregion

        #region Static Delegates — Pool Config

        /// <inheritdoc cref="IAssetService.SetPoolMaxSize(string, int)"/>
        public static void SetPoolMaxSize(string location, int maxSize)
            => Instance.SetPoolMaxSize(location, maxSize);

        /// <inheritdoc cref="IAssetService.GetPoolStatus(string)"/>
        public static (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
            => Instance.GetPoolStatus(location);

        #endregion

        #region Static Delegates — Lifecycle

        /// <inheritdoc cref="IAssetService.Release(UnityEngine.Object)"/>
        public static void Release(UnityEngine.Object asset)
            => Instance.Release(asset);

        /// <inheritdoc cref="IAssetService.DestroyInstance(GameObject)"/>
        public static void DestroyInstance(GameObject instance)
            => Instance.DestroyInstance(instance);

        /// <inheritdoc cref="IAssetService.DestroyInstance{T}(T)"/>
        public static void DestroyInstance<T>(T component) where T : Component
            => Instance.DestroyInstance(component);

        #endregion
    }
}
