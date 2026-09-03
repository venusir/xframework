using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XFramework.XAsset
{

    /// <summary>
    /// 资源管理器具体实现。实现 <see cref="IAssetManager"/>，提供资源加载、实例化与生命周期管理。
    /// <para>内部使用 YooAsset。生命周期由 <see cref="AssetHandle{T}"/> 管理，
    /// Dispose 时直接调用 <see cref="YooAsset.AssetHandle.Release"/>。</para>
    /// <para>通常不直接使用，由 <see cref="AssetManager"/> 外观类持有并委派调用。</para>
    /// </summary>
    internal class AssetManagerImpl : IAssetManager
    {
        #region Private Fields

        private YooAssetManagerImpl _managerImpl;
        private bool _initialized;

        /// <summary>进行中的初始化任务（直接使用本类时的并发保护，模式同 AssetManager 门面）。</summary>
        private UniTask _initializeTask;

        /// <summary>等待初始化的加入者信号（UniTask promise 只支持单个 continuation，加入者各自持有信号等待创建者广播）。</summary>
        private readonly List<UniTaskCompletionSource> _initWaiters = new();

        /// <summary>初始化失败异常（创建者 catch 记录，广播给加入者，保持同一异常实例）。</summary>
        private Exception _initException;

        /// <summary>location → 对象池（已 deactive 的闲置实例）。</summary>
        private readonly Dictionary<string, Stack<GameObject>> _pools = new Dictionary<string, Stack<GameObject>>();

        /// <summary>location → 对象池最大容量。</summary>
        private readonly Dictionary<string, int> _poolMaxSizes = new Dictionary<string, int>();

        /// <summary>默认 YooAsset 资源包名。多资源包场景需扩展为配置注入。</summary>
        private const string DefaultPackageName = "DefaultPackage";

        /// <summary>默认每种预制体最多保留的闲置实例数。</summary>
        private const int DefaultPoolSize = 5;

        /// <summary>是否已释放。</summary>
        private bool _disposed;

        /// <summary>是否已订阅低内存事件（由 options.AutoReclaimOnLowMemory 决定）。</summary>
        private bool _autoReclaimOnLowMemory;

        #endregion

        #region Initialize

        public async UniTask InitializeAsync(AssetInitOptions options = null, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            if (_initialized) return;
            if (_disposed) throw new ObjectDisposedException(nameof(AssetManagerImpl));

            // 并发调用共享同一进行中的初始化任务；加入者不直接 await 共享任务
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
        private async UniTask AwaitInitBroadcast()
        {
            var tcs = new UniTaskCompletionSource();
            _initWaiters.Add(tcs);
            await tcs.Task;
        }

        /// <summary>
        /// 创建者完成时广播结果给所有加入者并清空信号（成功 TrySetResult，失败/取消 TrySetException）。
        /// </summary>
        private void BroadcastInitResult()
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
        /// 实际初始化流程。失败时不重置 _managerImpl（YooAsset 包复用语义已保证重试安全）。
        /// </summary>
        private async UniTask InitializeAsyncCore(IProgress<AssetInitReport> progress, AssetInitOptions options, CancellationToken cancellationToken)
        {
            _managerImpl ??= new YooAssetManagerImpl(DefaultPackageName);
            var initOptions = options ?? new AssetInitOptions();
            await _managerImpl.InitializePackageAsync(initOptions, progress, cancellationToken);
            _initialized = true;

            // 初始化成功后才订阅低内存事件（失败不订阅，避免回调中未初始化报错噪音）
            _autoReclaimOnLowMemory = initOptions.AutoReclaimOnLowMemory;
            if (_autoReclaimOnLowMemory)
            {
                Application.lowMemory += OnLowMemory;
            }
        }

        public async UniTask InitializePackageAsync(AssetInitOptions options, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            _managerImpl ??= new YooAssetManagerImpl(DefaultPackageName);
            await _managerImpl.InitializePackageAsync(options, progress, cancellationToken);
        }

        #endregion

        #region Unload & Query

        public async UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            await _managerImpl.UnloadUnusedAssetsAsync(packageName, cancellationToken);
        }

        public void TryUnloadUnusedAsset(string location, string packageName = null)
        {
            EnsureInitialized();
            _managerImpl.TryUnloadUnusedAsset(location, packageName);
        }

        public bool CheckLocationValid(string location, string packageName = null)
        {
            EnsureInitialized();
            return _managerImpl.CheckLocationValid(location, packageName);
        }

        public bool IsNeedDownloadFromRemote(string location, string packageName = null)
        {
            EnsureInitialized();
            return _managerImpl.IsNeedDownloadFromRemote(location, packageName);
        }

        #endregion

        #region Hot Update

        public async UniTask<string> RequestPackageVersionAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return await _managerImpl.RequestPackageVersionAsync(packageName, cancellationToken);
        }

        public async UniTask UpdatePackageManifestAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            await _managerImpl.UpdatePackageManifestAsync(packageVersion, packageName, cancellationToken);
        }

        public async UniTask PreDownloadContentAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            await _managerImpl.PreDownloadContentAsync(packageVersion, packageName, cancellationToken);
        }

        public string GetPackageVersion(string packageName = null)
        {
            EnsureInitialized();
            return _managerImpl.GetPackageVersion(packageName);
        }

        public AssetDownloaderHandle CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8, int failedRetryCount = 3, string packageName = null)
        {
            EnsureInitialized();
            return _managerImpl.CreateDownloader(tags, downloadingMaxNumber, failedRetryCount, packageName);
        }

        public async UniTask<bool> DownloadAssetsAsync(string[] tags = null, Action<float> progress = null, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return await _managerImpl.DownloadAssetsAsync(tags, progress, packageName, cancellationToken);
        }

        #endregion

        #region Load — UniTask

        public async UniTask<AssetHandle<T>> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureInitialized();
            return await _managerImpl.LoadAsync<T>(location, cancellationToken: cancellationToken);
        }

        public async UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureInitialized();
            return await _managerImpl.LoadAsync<T>(location, (uint)Math.Max(0, priority), cancellationToken);
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            return await InstantiateAsyncInternal(location, null, null, parent, cancellationToken);
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            return await InstantiateAsyncInternal(location, position, rotation, parent, cancellationToken);
        }

        public async UniTask<T> InstantiateAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            var go = await InstantiateAsyncInternal(location, null, null, parent, cancellationToken);
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

        public async UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            var go = await InstantiateAsyncInternal(location, position, rotation, parent, cancellationToken);
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

        public async UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return await _managerImpl.LoadSceneAsync(location, additive, progress, cancellationToken);
        }

        public async UniTask PreloadAllAsync(IEnumerable<string> locations, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            // 先收集全部 location：避免重复枚举，且预加载过程中的总数/计数基于快照
            var list = new List<string>();
            foreach (var location in locations)
            {
                list.Add(location);
            }

            int total = list.Count;
            if (total == 0)
            {
                progress?.Invoke(1f);
                return;
            }

            int completed = 0;
            var tasks = new List<UniTask>(total);
            foreach (var location in list)
            {
                tasks.Add(PreloadOneAsync(location, () =>
                {
                    completed++;
                    progress?.Invoke((float)completed / total);
                }, cancellationToken));
            }

            await UniTask.WhenAll(tasks);
            // 兜底补发最终值（最后一个完成回调可能因取消/失败未触发）
            progress?.Invoke(1f);
        }

        public async UniTask<AssetHandle<T>[]> LoadAllAsync<T>(IReadOnlyList<string> locations, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureInitialized();
            if (locations == null) throw new ArgumentNullException(nameof(locations));

            int count = locations.Count;
            var handles = new AssetHandle<T>[count];

            // 手写 for 并行发起全部加载，避免 LINQ 闭包；index 按值传入，无循环变量捕获
            var tasks = new UniTask[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = LoadOneAsync(locations[i], i, handles, cancellationToken);
            }

            try
            {
                await UniTask.WhenAll(tasks);
            }
            catch
            {
                // 取消/异常时释放已完成项的句柄，防止部分加载成功导致引用泄漏
                for (int i = 0; i < count; i++)
                {
                    if (handles[i].IsValid) handles[i].Dispose();
                }
                throw;
            }
            return handles;
        }

        #endregion

        #region Load — Sync

        public AssetHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object
        {
            EnsureInitialized();
            return _managerImpl.LoadSync<T>(location);
        }

        public GameObject InstantiateSync(string location, Transform parent = null)
        {
            return InstantiateSyncInternal(location, null, null, parent);
        }

        public GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return InstantiateSyncInternal(location, position, rotation, parent);
        }

        #endregion

        #region Load — Sub Assets / Raw File

        public async UniTask<SubAssetsHandle> LoadSubAssetsAsync(string location, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return await _managerImpl.LoadSubAssetsAsync(location, cancellationToken);
        }

        public SubAssetsHandle LoadSubAssetsSync(string location)
        {
            EnsureInitialized();
            return _managerImpl.LoadSubAssetsSync(location);
        }

        public async UniTask<RawFileHandle> LoadRawFileAsync(string location, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return await _managerImpl.LoadRawFileAsync(location, cancellationToken);
        }

        public RawFileHandle LoadRawFileSync(string location)
        {
            EnsureInitialized();
            return _managerImpl.LoadRawFileSync(location);
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
            // activeCount 由 InstanceTracker 按 SetActive(true) 状态实时统计
            int active = InstanceTracker.GetActiveCount(location);
            int maxSize = _poolMaxSizes.TryGetValue(location, out var size) ? size : DefaultPoolSize;
            return (pooled, active, maxSize);
        }

        #endregion

        #region Lifecycle

        public void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;

            var tracker = instance.GetComponent<InstanceTracker>();
            if (tracker != null)
            {
                // 回池成功：句柄保留（资源保活），实例可随时取出复用
                if (TryReturnToPool(tracker.Location, instance)) return;

                // 池满：销毁前释放资源引用（幂等，OnDestroy 不会重复释放）
                tracker.DisposeHandle();
                UnityEngine.Object.Destroy(instance);
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

            // 静态事件必须退订，防止实例被事件持有导致泄漏
            if (_autoReclaimOnLowMemory)
            {
                Application.lowMemory -= OnLowMemory;
                _autoReclaimOnLowMemory = false;
            }

            DestroyAllPooledInstances();

            _managerImpl?.Destroy();
            _managerImpl = null;

            _poolMaxSizes.Clear();
            _initialized = false;
        }

        /// <summary>
        /// 清理所有池中的闲置实例：先释放句柄再销毁（池满时实例尚未销毁过，句柄仍有效）。
        /// <para>Dispose 与低内存回收共用；清理后取池路径的判空保护（pooled != null）保证安全。</para>
        /// </summary>
        private void DestroyAllPooledInstances()
        {
            foreach (var kvp in _pools)
            {
                foreach (var go in kvp.Value)
                {
                    if (go != null)
                    {
                        go.GetComponent<InstanceTracker>()?.DisposeHandle();
                        UnityEngine.Object.Destroy(go);
                    }
                }
            }
            _pools.Clear();
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// 内部同步实例化逻辑。与异步路径共用对象池：优先取池，池空时同步加载并实例化。
        /// </summary>
        private GameObject InstantiateSyncInternal(string location, Vector3? position, Quaternion? rotation, Transform parent)
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
                    return pooled;
                }
            }

            // 2. 同步加载资源（阻塞至完成）
            var handle = _managerImpl.LoadSync<GameObject>(location);
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
        /// 单个加载，结果写入共享数组（按序对应）。失败返回 default 句柄，不抛（与 LoadAsync 一致）。
        /// </summary>
        private async UniTask LoadOneAsync<T>(string location, int index, AssetHandle<T>[] handles, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            handles[index] = await LoadAsync<T>(location, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 预加载单个资源。无论成功失败都回调完成计数，保证整体进度可收敛到 1f。
        /// </summary>
        private async UniTask PreloadOneAsync(string location, Action onCompleted, CancellationToken cancellationToken)
        {
            try
            {
                await _managerImpl.PreloadAsync(location, cancellationToken);
            }
            finally
            {
                onCompleted();
            }
        }

        /// <summary>
        /// 内部实例化逻辑。优先从对象池获取，否则加载资源并实例化。
        /// <para>实例化后通过 <see cref="InstanceTracker"/> 自动管理资源引用生命周期。</para>
        /// </summary>
        private async UniTask<GameObject> InstantiateAsyncInternal(string location, Vector3? position, Quaternion? rotation, Transform parent, CancellationToken cancellationToken = default)
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

                    // 回池实例的 AssetHandle 在回池时保留（方案 A：资源保活），取出即用，无需重新加载
                    return pooled;
                }
            }

            // 2. 通过 LoadAsync 加载资源（引用计数 +1）
            var handle = await LoadAsync<GameObject>(location, cancellationToken: cancellationToken);
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
        /// 尝试将实例回池。池未满则回池并返回 true；池满返回 false，由调用方销毁实例。
        /// <para>回池不释放实例的 AssetHandle（资源保活），销毁时才释放。</para>
        /// </summary>
        private bool TryReturnToPool(string location, GameObject instance)
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
                return true;
            }

            return false;
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException("[AssetManager] AssetManagerImpl 尚未初始化。请先调用 InitializeAsync() 完成初始化。");
        }

        /// <summary>
        /// 低内存回调。fire-and-forget：事件回调内不阻塞、不抛出。
        /// </summary>
        private void OnLowMemory()
        {
            if (_disposed || !_initialized) return;
            ReclaimMemoryAsync().Forget();
        }

        /// <summary>
        /// 内存回收主流程：清池 → 卸载未使用资源 → 请求 Unity 回收。
        /// <para>池中闲置实例的资源引用是最大可回收内存：不清池则句柄引用计数大于 0，YooAsset 无法卸载任何内容。</para>
        /// </summary>
        private async UniTask ReclaimMemoryAsync()
        {
            try
            {
                DestroyAllPooledInstances();

                if (_managerImpl != null)
                {
                    await _managerImpl.UnloadUnusedAssetsAllAsync();
                }

                await Resources.UnloadUnusedAssets().ToUniTask();
            }
            catch (Exception e)
            {
                // 回收失败不影响主流程，只记录
                Debug.LogError($"[AssetManager] Low memory reclaim failed: {e.Message}");
            }
        }

        #endregion
    }
}