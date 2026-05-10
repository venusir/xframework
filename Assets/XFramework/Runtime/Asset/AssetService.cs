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
    /// 资源服务独立实现。与节点树无关，可直接 <c>new AssetService()</c> 创建使用。
    /// <para>内部使用 YooAsset 实现资源加载，自动管理引用计数、对象池、延迟卸载、场景加载、预加载。</para>
    /// <para>通过 <see cref="AssetSystem"/> 获取全局单例，或自行创建独立实例。</para>
    /// </summary>
    public class AssetService : IAssetService
    {
        #region Private Fields

        private YooAssetServiceImpl _serviceImpl;
        private ResourcePackage _package;
        private bool _initialized;

        /// <summary>资源实例 → location 映射（用于 Release 通过资源实例查找 location）。</summary>
        private readonly Dictionary<UnityEngine.Object, string> _assetToLocation = new Dictionary<UnityEngine.Object, string>();

        /// <summary>实例 GameObject → location 映射（用于 DestroyInstance 查找 location）。</summary>
        private readonly Dictionary<GameObject, string> _instanceToLocation = new Dictionary<GameObject, string>();

        /// <summary>location → 当前活跃实例数。</summary>
        private readonly Dictionary<string, int> _locationCounts = new Dictionary<string, int>();

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

        /// <summary>
        /// 初始化资源服务（无进度版本）。
        /// </summary>
        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            await InitializeAsync(null, cancellationToken);
        }

        /// <summary>
        /// 初始化资源服务（带进度报告版本）。
        /// </summary>
        public async UniTask InitializeAsync(IProgress<LoadContext> progress, CancellationToken cancellationToken = default)
        {
            if (_initialized) return;

            _serviceImpl = new YooAssetServiceImpl();

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

        private static void ReportProgress(IProgress<LoadContext> progress, float value, string description)
        {
            progress?.Report(new LoadContext
            {
                OverallProgress = value,
                Description = description,
            });
        }

        #endregion

        #region Load — UniTask

        public async UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureInitialized();
            var asset = await _serviceImpl.LoadAsync<T>(location, cancellationToken: cancellationToken);
            if (asset != null)
            {
                _assetToLocation[asset] = location;
            }
            return asset;
        }

        public async UniTask<T> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureInitialized();
            var asset = await _serviceImpl.LoadAsync<T>(location, (uint)Math.Max(0, priority), cancellationToken);
            if (asset != null)
            {
                _assetToLocation[asset] = location;
            }
            return asset;
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
                Debug.LogWarning($"[AssetService] Prefab at '{location}' lacks component {typeof(T).Name}. " +
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
                Debug.LogWarning($"[AssetService] Prefab at '{location}' lacks component {typeof(T).Name}. " +
                                 "Destroying instance to prevent resource leak.");
                DestroyInstance(go);
                return null;
            }
            return component;
        }

        public async UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null)
        {
            EnsureInitialized();
            return await _serviceImpl.LoadSceneAsync(location, additive, progress);
        }

        public async UniTask PreloadAllAsync(IEnumerable<string> locations)
        {
            EnsureInitialized();
            var tasks = new List<UniTask>();
            foreach (var location in locations)
            {
                tasks.Add(_serviceImpl.PreloadAsync(location));
            }
            await UniTask.WhenAll(tasks);
        }

        #endregion

        #region Load — Callback

        public async void LoadAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null) where T : UnityEngine.Object
        {
            var result = await LoadAsync<T>(location);
            if (result != null)
                onCompleted?.Invoke(result);
            else
                onError?.Invoke($"Failed to load asset: {location}");
        }

        public async void InstantiateAsync(string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null)
        {
            var result = await InstantiateAsync(location, parent);
            if (result != null)
                onCompleted?.Invoke(result);
            else
                onError?.Invoke($"Failed to instantiate: {location}");
        }

        public async void InstantiateAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component
        {
            var result = await InstantiateAsync<T>(location, parent);
            if (result != null)
                onCompleted?.Invoke(result);
            else
                onError?.Invoke($"Failed to instantiate: {location}");
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
            int active = _locationCounts.TryGetValue(location, out var cnt) ? cnt : 0;
            int maxSize = _poolMaxSizes.TryGetValue(location, out var size) ? size : DefaultPoolSize;
            return (pooled, active, maxSize);
        }

        #endregion

        #region Lifecycle

        public void Release(UnityEngine.Object asset)
        {
            if (asset == null) return;

            if (_assetToLocation.TryGetValue(asset, out var location))
            {
                _serviceImpl?.Release(location);
                _assetToLocation.Remove(asset);
            }
        }

        public void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;

            // 标记 tracker 避免重复通知
            var tracker = instance.GetComponent<InstanceTracker>();
            if (tracker != null)
            {
                tracker.IsBeingReleased = true;
            }

            if (_instanceToLocation.TryGetValue(instance, out var location))
            {
                _instanceToLocation.Remove(instance);

                // 引用计数 -1
                if (_locationCounts.TryGetValue(location, out var count))
                {
                    count--;
                    if (count <= 0)
                    {
                        _locationCounts.Remove(location);
                        _serviceImpl?.Release(location);
                    }
                    else
                    {
                        _locationCounts[location] = count;
                    }
                }

                // 回池或销毁
                ReturnToPoolOrDestroy(location, instance);
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

        /// <summary>
        /// 由 <see cref="InstanceTracker.OnDestroy"/> 调用。当用户直接 Destroy(go) 时自动释放引用。
        /// </summary>
        internal void OnInstanceDestroyed(string location, GameObject instance)
        {
            if (instance == null) return;

            _instanceToLocation.Remove(instance);

            // 引用计数 -1
            if (_locationCounts.TryGetValue(location, out var count))
            {
                count--;
                if (count <= 0)
                {
                    _locationCounts.Remove(location);
                    _serviceImpl?.Release(location);
                }
                else
                {
                    _locationCounts[location] = count;
                }
            }
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

            _serviceImpl?.Destroy();
            _serviceImpl = null;

            _assetToLocation.Clear();
            _instanceToLocation.Clear();
            _locationCounts.Clear();
            _poolMaxSizes.Clear();

            _initialized = false;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// 内部实例化逻辑。优先从对象池获取，否则加载资源并实例化。
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

                    // 记录映射
                    _instanceToLocation[pooled] = location;
                    _locationCounts[location] = _locationCounts.TryGetValue(location, out var c) ? c + 1 : 1;

                    return pooled;
                }
            }

            // 2. 加载资源
            var prefab = await _serviceImpl.LoadAsync<GameObject>(location);
            if (prefab == null) return null;

            // 记录资源映射（首次加载时）
            _assetToLocation[prefab] = location;

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

            // 4. 挂载 InstanceTracker（自动防泄漏）
            var tracker = go.AddComponent<InstanceTracker>();
            tracker.OwnerService = this;
            tracker.Location = location;

            // 5. 记录实例映射
            _instanceToLocation[go] = location;
            _locationCounts[location] = _locationCounts.TryGetValue(location, out var cnt) ? cnt + 1 : 1;

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
                throw new InvalidOperationException("AssetService is not initialized. Call InitializeAsync() first.");
        }

        #endregion
    }
}
