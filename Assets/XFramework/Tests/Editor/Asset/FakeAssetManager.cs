using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XAsset;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="IAssetManager"/> 假实现：记录调用计数与关键参数，供门面/扩展方法转发测试断言。
    /// <para>不依赖 YooAsset 运行环境，所有异步方法立即完成，句柄类成员返回 default。</para>
    /// </summary>
    internal class FakeAssetManager : IAssetManager
    {
        public int LoadCallCount;
        public int InstantiateCallCount;
        public int SceneLoadCallCount;
        public int PreloadCallCount;
        public int InitializePackageCallCount;
        public int UnloadUnusedAssetsCallCount;
        public int TryUnloadUnusedAssetCallCount;
        public string LastTryUnloadLocation;
        public string LastCheckLocation;
        public string LastNeedDownloadLocation;
        public int RequestPackageVersionCallCount;
        public string LastRequestedPackageName;
        public int UpdatePackageManifestCallCount;
        public int PreDownloadContentCallCount;
        public int CreateDownloaderCallCount;
        public int DownloadAssetsCallCount;
        public int LoadSyncCallCount;
        public int InstantiateSyncCallCount;
        public int LoadSubAssetsCallCount;
        public int LoadRawFileCallCount;
        public int SetPoolMaxSizeCallCount;
        public int DestroyInstanceCallCount;
        public bool Disposed;

        /// <summary>InitializeAsync 调用计数。</summary>
        public int InitCallCount;

        /// <summary>InitializeAsync 返回的任务。默认立即完成；测试可注入挂起任务（UniTaskCompletionSource）模拟并发。</summary>
        public UniTask InitTask = UniTask.CompletedTask;

        /// <summary>最近一次收到的初始化进度上报接收方(转发断言用)。</summary>
        public IProgress<AssetInitReport> LastInitProgress;

        /// <summary>最近一次收到的初始化选项（断言「阶段携带的 options 真的传下去了」用）。</summary>
        public AssetInitOptions LastInitOptions;

        public UniTask InitializeAsync(AssetInitOptions options = null, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            InitCallCount++;
            LastInitProgress = progress;
            LastInitOptions = options;
            return InitTask;
        }

        public UniTask InitializePackageAsync(AssetInitOptions options, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            InitializePackageCallCount++;
            LastInitProgress = progress;
            return UniTask.CompletedTask;
        }

        public UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            UnloadUnusedAssetsCallCount++;
            return UniTask.CompletedTask;
        }

        public void TryUnloadUnusedAsset(string location, string packageName = null)
        {
            TryUnloadUnusedAssetCallCount++;
            LastTryUnloadLocation = location;
        }

        public bool CheckLocationValid(string location, string packageName = null)
        {
            LastCheckLocation = location;
            return true;
        }

        public bool IsNeedDownloadFromRemote(string location, string packageName = null)
        {
            LastNeedDownloadLocation = location;
            return false;
        }

        public UniTask<string> RequestPackageVersionAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            RequestPackageVersionCallCount++;
            LastRequestedPackageName = packageName;
            return UniTask.FromResult("1.0.0");
        }

        public UniTask UpdatePackageManifestAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            UpdatePackageManifestCallCount++;
            return UniTask.CompletedTask;
        }

        /// <summary>最近一次预检的版本号。</summary>
        public string LastPreDownloadVersion;

        public UniTask PreDownloadContentAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            PreDownloadContentCallCount++;
            LastPreDownloadVersion = packageVersion;
            return UniTask.CompletedTask;
        }

        public string GetPackageVersion(string packageName = null) => "1.0.0";

        public AssetDownloaderHandle CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8, int failedRetryCount = 3, string packageName = null)
        {
            CreateDownloaderCallCount++;
            return null;
        }

        public UniTask<bool> DownloadAssetsAsync(string[] tags = null, Action<float> progress = null, string packageName = null, CancellationToken cancellationToken = default)
        {
            DownloadAssetsCallCount++;
            return UniTask.FromResult(true);
        }

        public UniTask<AssetHandle<T>> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            LoadCallCount++;
            return UniTask.FromResult(default(AssetHandle<T>));
        }

        /// <summary>最近一次带优先级加载的 priority（转发断言用）。</summary>
        public int LastLoadPriority;

        public UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            LoadCallCount++;
            LastLoadPriority = priority;
            return UniTask.FromResult(default(AssetHandle<T>));
        }

        /// <summary>最近一次带位置旋转的实例化参数（转发断言用）。</summary>
        public Vector3 LastInstantiatePosition;
        public Quaternion LastInstantiateRotation;
        public Transform LastInstantiateParent;

        public UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            InstantiateCallCount++;
            LastInstantiateParent = parent;
            return UniTask.FromResult<GameObject>(null);
        }

        public UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            InstantiateCallCount++;
            LastInstantiatePosition = position;
            LastInstantiateRotation = rotation;
            LastInstantiateParent = parent;
            return UniTask.FromResult<GameObject>(null);
        }

        public UniTask<T> InstantiateAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            InstantiateCallCount++;
            return UniTask.FromResult<T>(null);
        }

        public UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            InstantiateCallCount++;
            return UniTask.FromResult<T>(null);
        }

        public UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            SceneLoadCallCount++;
            return UniTask.FromResult(default(Scene));
        }

        public UniTask PreloadAllAsync(IEnumerable<string> locations, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            PreloadCallCount++;
            return UniTask.CompletedTask;
        }

        /// <summary>LoadAllAsync 调用计数与最近一次传入的 locations（按序快照）。</summary>
        public int LoadAllCallCount;
        public string[] LastLoadAllLocations;

        public UniTask<AssetHandle<T>[]> LoadAllAsync<T>(IReadOnlyList<string> locations, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            LoadAllCallCount++;
            LastLoadAllLocations = new string[locations.Count];
            for (int i = 0; i < locations.Count; i++) LastLoadAllLocations[i] = locations[i];
            return UniTask.FromResult(new AssetHandle<T>[locations.Count]);
        }

        public AssetHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object
        {
            LoadSyncCallCount++;
            return default;
        }

        public GameObject InstantiateSync(string location, Transform parent = null)
        {
            InstantiateSyncCallCount++;
            return null;
        }

        public GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            InstantiateSyncCallCount++;
            LastInstantiatePosition = position;
            LastInstantiateRotation = rotation;
            LastInstantiateParent = parent;
            return null;
        }

        public UniTask<SubAssetsHandle> LoadSubAssetsAsync(string location, CancellationToken cancellationToken = default)
        {
            LoadSubAssetsCallCount++;
            return UniTask.FromResult(default(SubAssetsHandle));
        }

        public SubAssetsHandle LoadSubAssetsSync(string location)
        {
            LoadSubAssetsCallCount++;
            return default;
        }

        public UniTask<RawFileHandle> LoadRawFileAsync(string location, CancellationToken cancellationToken = default)
        {
            LoadRawFileCallCount++;
            return UniTask.FromResult(default(RawFileHandle));
        }

        public RawFileHandle LoadRawFileSync(string location)
        {
            LoadRawFileCallCount++;
            return default;
        }

        /// <summary>最近一次池容量配置（转发断言用）。</summary>
        public string LastSetPoolMaxSizeLocation;
        public int LastSetPoolMaxSizeValue;

        public void SetPoolMaxSize(string location, int maxSize)
        {
            SetPoolMaxSizeCallCount++;
            LastSetPoolMaxSizeLocation = location;
            LastSetPoolMaxSizeValue = maxSize;
        }

        public (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
            => (0, 0, 0);

        /// <summary>最近一次被销毁的实例（两个重载各记各的，用于区分重载归属）。</summary>
        public GameObject LastDestroyedInstance;
        public Component LastDestroyedComponent;

        public void DestroyInstance(GameObject instance)
        {
            DestroyInstanceCallCount++;
            LastDestroyedInstance = instance;
        }

        public void DestroyInstance<T>(T component) where T : Component
        {
            DestroyInstanceCallCount++;
            LastDestroyedComponent = component;
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// 实现池清理能力接口（<see cref="IAssetPoolController"/>）的替身：供门面「能力探测通过」那条分支断言。
    /// <para>基类 <see cref="FakeAssetManager"/> 刻意**不**实现该接口——「能力缺失 → 返回 0 并告警一次」
    /// 那条分支正需要它，两者不能合并。</para>
    /// </summary>
    internal sealed class PoolCapableAssetManager : FakeAssetManager, IAssetPoolController
    {
        public int ClearPoolCallCount;
        public int ClearAllPoolsCallCount;
        public string LastClearPoolLocation;

        /// <summary>ClearPool 的返回值（测试可改，用于断言「原样透传」）。</summary>
        public int ClearPoolResult = 1;

        /// <summary>ClearAllPools 的返回值。</summary>
        public int ClearAllPoolsResult = 2;

        public int ClearPool(string location)
        {
            ClearPoolCallCount++;
            LastClearPoolLocation = location;
            return ClearPoolResult;
        }

        public int ClearAllPools()
        {
            ClearAllPoolsCallCount++;
            return ClearAllPoolsResult;
        }
    }
}
