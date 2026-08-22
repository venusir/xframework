using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XAsset;
using XFramework.XLoader;

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

        public UniTask InitializeAsync(LoadProgress progress, AssetInitOptions options = null, CancellationToken cancellationToken = default)
        {
            InitCallCount++;
            return InitTask;
        }

        public UniTask InitializePackageAsync(AssetInitOptions options, LoadProgress progress, CancellationToken cancellationToken = default)
        {
            InitializePackageCallCount++;
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

        public UniTask PreDownloadContentAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            PreDownloadContentCallCount++;
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

        public UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            LoadCallCount++;
            return UniTask.FromResult(default(AssetHandle<T>));
        }

        public UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            InstantiateCallCount++;
            return UniTask.FromResult<GameObject>(null);
        }

        public UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            InstantiateCallCount++;
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

        public void SetPoolMaxSize(string location, int maxSize)
        {
            SetPoolMaxSizeCallCount++;
        }

        public (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
            => (0, 0, 0);

        public void DestroyInstance(GameObject instance)
        {
            DestroyInstanceCallCount++;
        }

        public void DestroyInstance<T>(T component) where T : Component
        {
            DestroyInstanceCallCount++;
        }

        public void Dispose() => Disposed = true;
    }
}
