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
            AssetManager.ImplFactory = null;
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

        [Test]
        public void InitializeAsync_ConcurrentCalls_ShareSingleTask()
        {
            AssetManager.Destroy();
            var factoryCalls = 0;
            var tcs = new UniTaskCompletionSource();
            var fake = new FakeAssetManager { InitTask = tcs.Task };
            AssetManager.ImplFactory = () => { factoryCalls++; return fake; };

            var t1 = AssetManager.InitializeAsync(new LoadProgress());
            var t2 = AssetManager.InitializeAsync(new LoadProgress());

            Assert.AreEqual(1, factoryCalls, "并发调用应共享同一初始化任务，只创建一次实例");

            tcs.TrySetResult();
            t1.GetAwaiter().GetResult();
            t2.GetAwaiter().GetResult();
            Assert.IsTrue(AssetManager.IsInitialized);
            Assert.AreEqual(1, factoryCalls);
        }

        [Test]
        public void InitializeAsync_Failed_AllowsRetry()
        {
            AssetManager.Destroy();
            var factoryCalls = 0;
            var tcs = new UniTaskCompletionSource();
            var first = new FakeAssetManager { InitTask = tcs.Task };
            AssetManager.ImplFactory = () => { factoryCalls++; return first; };

            var t1 = AssetManager.InitializeAsync(new LoadProgress());
            tcs.TrySetException(new InvalidOperationException("模拟初始化失败"));
            Assert.Throws<InvalidOperationException>(() => t1.GetAwaiter().GetResult());
            Assert.IsFalse(AssetManager.IsInitialized);

            // 失败后缓存已清空可重试；第二次返回全新实例，不复用已失败的任务
            first = new FakeAssetManager();
            var t2 = AssetManager.InitializeAsync(new LoadProgress());
            t2.GetAwaiter().GetResult();
            Assert.AreEqual(2, factoryCalls);
            Assert.IsTrue(AssetManager.IsInitialized);
        }

        [Test]
        public void Destroy_DuringInit_DiscardsInflightResult()
        {
            AssetManager.Destroy();
            var factoryCalls = 0;
            var tcs = new UniTaskCompletionSource();
            var fake = new FakeAssetManager { InitTask = tcs.Task };
            AssetManager.ImplFactory = () => { factoryCalls++; return fake; };

            var t1 = AssetManager.InitializeAsync(new LoadProgress());
            AssetManager.Destroy(); // 在途初始化期间销毁

            tcs.TrySetResult();
            t1.GetAwaiter().GetResult();
            Assert.IsFalse(AssetManager.IsInitialized, "销毁后的在途初始化结果应被丢弃，不得复活");
            Assert.IsTrue(fake.Disposed, "被丢弃的实例应释放");
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
    }
}
