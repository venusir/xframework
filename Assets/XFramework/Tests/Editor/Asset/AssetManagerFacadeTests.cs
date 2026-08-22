using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XAsset;
using XFramework.XLoader;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// AssetManager 静态门面测试。通过 <see cref="AssetManager.SetInstance"/> 注入假实现，不依赖 YooAsset 运行环境。
    /// <para>覆盖：初始化状态、方法委托转发、幂等、未初始化保护。</para>
    /// <para>AssetManagerImpl 的对象池/引用计数逻辑依赖 YooAsset 实际运行环境，属集成测试范畴，未在此覆盖。</para>
    /// </summary>
    class AssetManagerFacadeTests
    {
        private FakeAssetManager _fake;

        [SetUp]
        public void SetUp()
        {
            _fake = new FakeAssetManager();
            AssetManager.SetInstance(_fake);
        }

        [TearDown]
        public void TearDown()
        {
            AssetManager.Destroy();
        }

        #region 初始化状态

        [Test]
        public void IsInitialized_AfterSetInstance_ReturnsTrue()
        {
            Assert.IsTrue(AssetManager.IsInitialized);
        }

        [Test]
        public void SetInstance_Null_ThrowsArgumentNullException()
        {
            AssetManager.Destroy();
            Assert.Throws<ArgumentNullException>(() => AssetManager.SetInstance(null));
        }

        [Test]
        public void InitializeAsync_AfterSetInstance_IsIgnored()
        {
            // 已初始化时重复 InitializeAsync 应 LogWarning 忽略，不替换现有实例
            AssetManager.InitializeAsync(new LoadProgress()).GetAwaiter().GetResult();

            Assert.IsTrue(AssetManager.IsInitialized);
            AssetManager.LoadAsync<GameObject>("dummy").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.LoadCallCount, "调用应仍转发到原实例");
        }

        [Test]
        public void Destroy_AfterSetInstance_ResetsState()
        {
            AssetManager.Destroy();

            Assert.IsFalse(AssetManager.IsInitialized);
            Assert.IsTrue(_fake.Disposed, "Destroy 应 Dispose 底层实例");
            Assert.Throws<InvalidOperationException>(() => AssetManager.LoadAsync<GameObject>("dummy"));
        }

        #endregion

        #region 未初始化保护

        [Test]
        public void LoadAsync_BeforeInitialize_ThrowsInvalidOperationException()
        {
            AssetManager.Destroy();
            Assert.Throws<InvalidOperationException>(() => AssetManager.LoadAsync<GameObject>("dummy"));
        }

        [Test]
        public void InstantiateAsync_BeforeInitialize_ThrowsInvalidOperationException()
        {
            AssetManager.Destroy();
            Assert.Throws<InvalidOperationException>(() => AssetManager.InstantiateAsync("dummy"));
        }

        [Test]
        public void GetPoolStatus_BeforeInitialize_ThrowsInvalidOperationException()
        {
            AssetManager.Destroy();
            Assert.Throws<InvalidOperationException>(() => AssetManager.GetPoolStatus("dummy"));
        }

        #endregion

        #region 方法委托转发

        [Test]
        public void InitializePackageAsync_ForwardsToInstance()
        {
            var options = new AssetInitOptions { PackageName = "ExtraPackage" };
            AssetManager.InitializePackageAsync(options, new LoadProgress()).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.InitializePackageCallCount);
        }

        [Test]
        public void LoadAsync_ForwardsToInstance()
        {
            AssetManager.LoadAsync<GameObject>("characters/player").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.LoadCallCount);
        }

        [Test]
        public void InstantiateAsync_ForwardsToInstance()
        {
            AssetManager.InstantiateAsync("characters/player").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.InstantiateCallCount);
        }

        [Test]
        public void LoadSceneAsync_ForwardsToInstance()
        {
            AssetManager.LoadSceneAsync("scenes/main").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.SceneLoadCallCount);
        }

        [Test]
        public void RequestPackageVersionAsync_ForwardsToInstance()
        {
            var version = AssetManager.RequestPackageVersionAsync().GetAwaiter().GetResult();
            Assert.AreEqual("1.0.0", version);
            Assert.AreEqual(1, _fake.RequestPackageVersionCallCount);
        }

        [Test]
        public void UpdatePackageManifestAsync_ForwardsToInstance()
        {
            AssetManager.UpdatePackageManifestAsync("1.0.1").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.UpdatePackageManifestCallCount);
        }

        [Test]
        public void DownloadAssetsAsync_ForwardsToInstance()
        {
            var success = AssetManager.DownloadAssetsAsync(new[] { "hot" }).GetAwaiter().GetResult();
            Assert.IsTrue(success);
            Assert.AreEqual(1, _fake.DownloadAssetsCallCount);
        }

        [Test]
        public void LoadSync_ForwardsToInstance()
        {
            var handle = AssetManager.LoadSync<GameObject>("characters/player");
            Assert.IsFalse(handle.IsValid, "假实现返回 default 句柄");
            Assert.AreEqual(1, _fake.LoadSyncCallCount);
        }

        [Test]
        public void InstantiateSync_ForwardsToInstance()
        {
            AssetManager.InstantiateSync("characters/player");
            Assert.AreEqual(1, _fake.InstantiateSyncCallCount);
        }

        [Test]
        public void LoadSubAssetsAsync_ForwardsToInstance()
        {
            var handle = AssetManager.LoadSubAssetsAsync("ui/icon_atlas").GetAwaiter().GetResult();
            Assert.AreEqual(0, handle.Count, "假实现返回 default 句柄");
            Assert.AreEqual(1, _fake.LoadSubAssetsCallCount);
        }

        [Test]
        public void LoadRawFileAsync_ForwardsToInstance()
        {
            var handle = AssetManager.LoadRawFileAsync("configs/server_list").GetAwaiter().GetResult();
            Assert.AreEqual(string.Empty, handle.LastError, "假实现返回 default 句柄");
            Assert.AreEqual(1, _fake.LoadRawFileCallCount);
        }

        [Test]
        public void UnloadUnusedAssetsAsync_ForwardsToInstance()
        {
            AssetManager.UnloadUnusedAssetsAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.UnloadUnusedAssetsCallCount);
        }

        [Test]
        public void TryUnloadUnusedAsset_ForwardsToInstance()
        {
            AssetManager.TryUnloadUnusedAsset("characters/player");
            Assert.AreEqual(1, _fake.TryUnloadUnusedAssetCallCount);
            Assert.AreEqual("characters/player", _fake.LastTryUnloadLocation);
        }

        [Test]
        public void CheckLocationValid_ForwardsToInstance()
        {
            Assert.IsTrue(AssetManager.CheckLocationValid("characters/player"));
            Assert.AreEqual("characters/player", _fake.LastCheckLocation);
        }

        [Test]
        public void IsNeedDownloadFromRemote_ForwardsToInstance()
        {
            Assert.IsFalse(AssetManager.IsNeedDownloadFromRemote("characters/player"));
            Assert.AreEqual("characters/player", _fake.LastNeedDownloadLocation);
        }

        [Test]
        public void PreloadAllAsync_ForwardsToInstance()
        {
            AssetManager.PreloadAllAsync(new[] { "a", "b" }).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.PreloadCallCount);
        }

        #endregion

        /// <summary>
        /// 假实现：记录调用计数，供断言验证委托转发。
        /// </summary>
        private class FakeAssetManager : IAssetManager
        {
            public int LoadCallCount;
            public int InstantiateCallCount;
            public int SceneLoadCallCount;
            public int PreloadCallCount;
            public int InitializePackageCallCount;
            public int LoadSyncCallCount;
            public int InstantiateSyncCallCount;
            public int LoadSubAssetsCallCount;
            public int LoadRawFileCallCount;
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
            public bool Disposed;

            public UniTask InitializeAsync(LoadProgress progress, AssetInitOptions options = null, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;

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

            public void SetPoolMaxSize(string location, int maxSize)
            {
            }

            public (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
                => (0, 0, 0);

            public void DestroyInstance(GameObject instance)
            {
            }

            public void DestroyInstance<T>(T component) where T : Component
            {
            }

            public void Dispose() => Disposed = true;
        }
    }
}
