using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using XFramework.XLoader;

namespace XFramework.XAsset
{

    /// <summary>
    /// 资源管理器具体实现。实现 <see cref="IAssetManager"/>，提供资源加载、实例化与生命周期管理。
    /// <para>内部使用 YooAsset，自动管理引用计数、对象池、延迟卸载、场景加载、预加载。</para>
    /// <para>通常不直接使用，由 <see cref="AssetManager"/> 外观类持有并委派调用。</para>
    /// </summary>
    internal class AssetManagerImpl : IAssetManager
    {
        #region Private Fields

        private YooAssetManagerImpl _managerImpl;
        private ResourcePackage _package;
        private bool _initialized;

        /// <summary>location → 对象池（已 deactive 的闲置实例）。</summary>
        private readonly Dictionary<string, Stack<GameObject>> _pools = new Dictionary<string, Stack<GameObject>>();

        /// <summary>location → 对象池最大容量。</summary>
        private readonly Dictionary<string, int> _poolMaxSizes = new Dictionary<string, int>();

        /// <summary>默认每种预制体最多保留的闲置实例数。</summary>
        private const int DefaultPoolSize = 5;

        /// <summary>是否已释放。</summary>
        private bool _disposed;

        #endregion

        #region Initialize

        public async UniTask InitializeAsync(LoadProgress progress, CancellationToken cancellationToken = default)
        {
            await InitializeInstanceAsync(progress, cancellationToken);
        }

        private async UniTask InitializeInstanceAsync(LoadProgress progress, CancellationToken cancellationToken = default)
        {
            if (_initialized) return;

            _managerImpl = new YooAssetManagerImpl();

            ReportProgress(progress, 0f, "Initializing YooAsset...");

            // 1. 初始化 YooAsset 全局环境
            if (!YooAssets.Initialized)
            {
                YooAssets.Initialize();
            }

            ReportProgress(progress, 0.2f, "Getting resource package...");

            // 2. 获取或创建资源包
            _package = YooAssets.TryGetPackage("DefaultPackage");
            if (_package == null)
            {
                _package = YooAssets.CreatePackage("DefaultPackage");
            }

            ReportProgress(progress, 0.4f, "Initializing resource package...");

            // 3. 初始化资源包（使用离线模式参数）
            var initParameters = new OfflinePlayModeParameters();
            var initOperation = _package.InitializeAsync(initParameters);
            await initOperation.WithCancellation(cancellationToken);

            if (initOperation.Status != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException($"Package init failed: {initOperation.Error}");
            }

            ReportProgress(progress, 0.7f, "Requesting package version...");

            // 4. 获取资源版本号
            var versionOperation = _package.RequestPackageVersionAsync();
            await versionOperation.WithCancellation(cancellationToken);

            if (versionOperation.Status != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException($"Version request failed: {versionOperation.Error}");
            }

            ReportProgress(progress, 0.8f, "Updating package manifest...");

            // 5. 更新资源清单
            var updateOperation = _package.UpdatePackageManifestAsync(versionOperation.PackageVersion);
            await updateOperation.WithCancellation(cancellationToken);

            if (updateOperation.Status != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException($"Manifest update failed: {updateOperation.Error}");
            }

            ReportProgress(progress, 1f, "YooAsset initialized.");

            _initialized = true;
        }

        private static void ReportProgress(LoadProgress progress, float value, string description)
        {
            if (progress != null)
            {
                progress.SetOverallProgress(value);
                progress.SetDescription(description);
            }
        }

        #endregion

        #region Load — UniTask

        public async UniTask<AssetHandle<T>> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureInitialized();
            var asset = await _managerImpl.LoadAsync<T>(location, cancellationToken: cancellationToken);
            return new AssetHandle<T>(asset, location, this);
        }

        public async UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureInitialized();
            var asset = await _managerImpl.LoadAsync<T>(location, (uint)Math.Max(0, priority), cancellationToken);
            return new AssetHandle<T>(asset, location, this);
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Transform parent = null)
        {
            return await InstantiateAsyncInternal(location, null, null, parent);
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return await InstantiateAsyncInternal(location, position, rotation, parent);
        }

        public async UniTask<T> InstantiateAsync<T>(string location, Transform parent = null) where T : Component
        {
            var go = await InstantiateAsyncInternal(location, null, null, parent);
            if (go == null) return null;

            var component = go.GetComponent<T>();
            if (component == null)
            {
                Debug.LogWarning($"[AssetManager] Prefab at '{location}' lacks component {typeof(T).Name}. " +
                                 "Destroying instance to prevent resource leak.");
                DestroyInstance(go);
                return null;
            }
            return component;
        }

        public async UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
        {
            var go = await InstantiateAsyncInternal(location, position, rotation, parent);
            if (go == null) return null;

            var component = go.GetComponent<T>();
            if (component == null)
            {
                Debug.LogWarning($"[AssetManager] Prefab at '{location}' lacks component {typeof(T).Name}. " +
                                 "Destroying instance to prevent resource leak.");
                DestroyInstance(go);
                return null;
            }
            return component;
        }

        public async UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null)
        {
            EnsureInitialized();
            return await _managerImpl.LoadSceneAsync(location, additive, progress);
        }

        public async UniTask PreloadAllAsync(IEnumerable<string> locations)
        {
            EnsureInitialized();
            var tasks = new List<UniTask>();
            foreach (var location in locations)
            {
                tasks.Add(_managerImpl.PreloadAsync(location));
            }
            await UniTask.WhenAll(tasks);
        }

        #endregion

        #region Pool Config

        public void SetPoolMaxSize(string location, int maxSize)
        {
            _poolMaxSizes[location] = Math.Max(1, maxSize);
        }

        public (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
        {
            int pooled = _pools.TryGetValue(location, out var pool) ? pool.Count : 0;
            // activeCount 由 InstanceTracker 管理，不再通过 _locationCounts 跟踪
            // 此处返回 0 作为占位，完整统计需要额外的实例计数器
            int active = 0;
            int maxSize = _poolMaxSizes.TryGetValue(location, out var size) ? size : DefaultPoolSize;
            return (pooled, active, maxSize);
        }

        #endregion

        #region Lifecycle

        public void Release(string location)
        {
            if (string.IsNullOrEmpty(location)) return;
            _managerImpl?.Release(location);
        }

        public void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;

            var tracker = instance.GetComponent<InstanceTracker>();
            if (tracker != null)
            {
                tracker.IsBeingReleased = true;
                var location = tracker.Location;

                // 回池或销毁
                ReturnToPoolOrDestroy(location, instance);

                // 释放资源引用（AssetHandle.Dispose → Release(location)）
                tracker.DisposeHandle();
            }
            else
            {
                // 非托管实例，直接销毁
                UnityEngine.Object.Destroy(instance);
            }
        }

        public void DestroyInstance<T>(T component) where T : Component
        {
            if (component == null) return;
            DestroyInstance(component.gameObject);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 清理所有池中的实例
            foreach (var kvp in _pools)
            {
                foreach (var go in kvp.Value)
                {
                    if (go != null)
                        UnityEngine.Object.Destroy(go);
                }
            }
            _pools.Clear();

            _managerImpl?.Destroy();
            _managerImpl = null;

            _poolMaxSizes.Clear();
            _initialized = false;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// 内部实例化逻辑。优先从对象池获取，否则加载资源并实例化。
        /// <para>实例化后通过 <see cref="InstanceTracker"/> 自动管理资源引用生命周期。</para>
        /// </summary>
        private async UniTask<GameObject> InstantiateAsyncInternal(string location, Vector3? position, Quaternion? rotation, Transform parent)
        {
            EnsureInitialized();

            // 1. 优先从对象池获取
            if (_pools.TryGetValue(location, out var pool) && pool.Count > 0)
            {
                var pooled = pool.Pop();
                if (pooled != null)
                {
                    // 重置变换
                    pooled.transform.SetParent(parent);
                    pooled.transform.localPosition = position ?? Vector3.zero;
                    pooled.transform.localRotation = rotation ?? Quaternion.identity;
                    pooled.transform.localScale = Vector3.one;
                    pooled.SetActive(true);

                    // 对象池取出的实例仍持有 AssetHandle，无需额外操作
                    var tracker = pooled.GetComponent<InstanceTracker>();
                    if (tracker != null)
                        tracker.IsBeingReleased = false;

                    return pooled;
                }
            }

            // 2. 通过 LoadAsync 加载资源（引用计数 +1）
            var handle = await LoadAsync<GameObject>(location);
            var prefab = handle.Asset;
            if (prefab == null) return null;

            // 3. 实例化
            GameObject go;
            if (position.HasValue && rotation.HasValue)
            {
                go = UnityEngine.Object.Instantiate(prefab, position.Value, rotation.Value, parent);
            }
            else
            {
                go = UnityEngine.Object.Instantiate(prefab, parent);
            }

            // 4. 挂载 InstanceTracker，持有 AssetHandle 以维持资源引用
            var instanceTracker = go.AddComponent<InstanceTracker>();
            instanceTracker.SetHandle(handle, location);

            return go;
        }

        /// <summary>
        /// 将实例回池或销毁。池满时销毁最旧的实例。
        /// </summary>
        private void ReturnToPoolOrDestroy(string location, GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(null);

            if (!_pools.TryGetValue(location, out var pool))
            {
                pool = new Stack<GameObject>();
                _pools[location] = pool;
            }

            int maxSize = _poolMaxSizes.TryGetValue(location, out var configuredSize) ? configuredSize : DefaultPoolSize;

            if (pool.Count < maxSize)
            {
                pool.Push(instance);
            }
            else
            {
                UnityEngine.Object.Destroy(instance);
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException("AssetManagerImpl is not initialized. Call InitializeAsync() first.");
        }

        #endregion
    }
}