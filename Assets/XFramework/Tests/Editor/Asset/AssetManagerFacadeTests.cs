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
            public bool Disposed;

            public UniTask InitializeAsync(LoadProgress progress, AssetInitOptions options = null, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;

            public UniTask InitializePackageAsync(AssetInitOptions options, LoadProgress progress, CancellationToken cancellationToken = default)
            {
                InitializePackageCallCount++;
                return UniTask.CompletedTask;
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

            public UniTask PreloadAllAsync(IEnumerable<string> locations, CancellationToken cancellationToken = default)
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
