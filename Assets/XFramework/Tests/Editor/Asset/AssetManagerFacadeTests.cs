using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XAsset;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// AssetManager 静态门面测试。通过 <see cref="AssetManager.SetInstance"/> 注入假实现，不依赖 YooAsset 运行环境。
    /// <para>覆盖：初始化状态、进度上报转发、方法委托转发、幂等、未初始化保护。</para>
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
            AssetManager.InitializeAsync().GetAwaiter().GetResult();

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

            // 三个并发调用：第一个创建任务（创建者），后两个注册信号等待广播（加入者）
            var t1 = AssetManager.InitializeAsync();
            var t2 = AssetManager.InitializeAsync();
            var t3 = AssetManager.InitializeAsync();

            Assert.AreEqual(1, factoryCalls, "并发调用应共享同一初始化任务，只创建一次实例");

            tcs.TrySetResult();
            t1.GetAwaiter().GetResult();
            t2.GetAwaiter().GetResult();
            t3.GetAwaiter().GetResult();
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

            var t1 = AssetManager.InitializeAsync();
            var tJoin = AssetManager.InitializeAsync(); // join 者，与创建者共享同一失败
            tcs.TrySetException(new InvalidOperationException("模拟初始化失败"));
            var e1 = Assert.Throws<InvalidOperationException>(() => t1.GetAwaiter().GetResult());
            var e2 = Assert.Throws<InvalidOperationException>(() => tJoin.GetAwaiter().GetResult());
            Assert.AreSame(e1, e2, "创建者与加入者应收到同一异常实例");
            Assert.IsFalse(AssetManager.IsInitialized);

            // 失败后缓存已清空可重试；第二次返回全新实例，不复用已失败的任务
            first = new FakeAssetManager();
            var t2 = AssetManager.InitializeAsync();
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

            var t1 = AssetManager.InitializeAsync();
            AssetManager.Destroy(); // 在途初始化期间销毁

            tcs.TrySetResult();
            Assert.Throws<InvalidOperationException>(() => t1.GetAwaiter().GetResult(),
                "被作废的初始化必须抛异常——await 正常返回等价于「已初始化」，不能靠调用方自查");
            Assert.IsFalse(AssetManager.IsInitialized, "销毁后的在途初始化结果应被丢弃，不得复活");
            Assert.IsTrue(fake.Disposed, "被丢弃的实例应释放");
        }

        [Test]
        public void Destroy_DuringInit_FaultsJoinerAndCreatorAlike()
        {
            AssetManager.Destroy();
            var tcs = new UniTaskCompletionSource();
            AssetManager.ImplFactory = () => new FakeAssetManager { InitTask = tcs.Task };

            var tCreator = AssetManager.InitializeAsync();
            var tJoiner = AssetManager.InitializeAsync();

            AssetManager.Destroy(); // 作废：两条路径的调用方都应立刻看到，而不是等旧创建者收尾

            Assert.Throws<InvalidOperationException>(() => tJoiner.GetAwaiter().GetResult());
            Assert.AreEqual(UniTaskStatus.Pending, tCreator.Status, "创建者的作废判定是惰性的：等底层实例初始化返回后才抛出");
            tcs.TrySetResult();
            Assert.Throws<InvalidOperationException>(() => tCreator.GetAwaiter().GetResult());
        }

        [Test]
        public void Destroy_ThenReinitialize_StaleCompletion_DoesNotReleaseNewJoiners()
        {
            AssetManager.Destroy();

            // 第一代：创建者 A 挂起、加入者 B 登记
            var staleTcs = new UniTaskCompletionSource();
            var stale = new FakeAssetManager { InitTask = staleTcs.Task };
            AssetManager.ImplFactory = () => stale;
            var tA = AssetManager.InitializeAsync();
            var tB = AssetManager.InitializeAsync();

            // 作废第一代后重新初始化：第二代创建者 C 挂起、加入者 E 登记
            AssetManager.Destroy();
            var freshTcs = new UniTaskCompletionSource();
            var fresh = new FakeAssetManager { InitTask = freshTcs.Task };
            AssetManager.ImplFactory = () => fresh;
            var tC = AssetManager.InitializeAsync();
            var tE = AssetManager.InitializeAsync();

            // 旧代际收尾：不得放行新代际的加入者（改前：E 被提前放行，醒来时 IsInitialized 仍为 false）
            staleTcs.TrySetResult();
            Assert.Throws<InvalidOperationException>(() => tB.GetAwaiter().GetResult(), "旧代际的加入者应被作废");
            Assert.Throws<InvalidOperationException>(() => tA.GetAwaiter().GetResult(), "旧代际的创建者应被作废");
            Assert.AreEqual(UniTaskStatus.Pending, tE.Status, "旧代际完成不得提前放行新代际的加入者");
            Assert.IsTrue(stale.Disposed, "被作废的实例应释放");

            // 新代际完成后才放行
            freshTcs.TrySetResult();
            tC.GetAwaiter().GetResult();
            tE.GetAwaiter().GetResult();
            Assert.IsTrue(AssetManager.IsInitialized);
        }

        [Test]
        public void Destroy_ThenReinitialize_StaleFailure_DoesNotFaultNewJoiners()
        {
            AssetManager.Destroy();

            var staleTcs = new UniTaskCompletionSource();
            var stale = new FakeAssetManager { InitTask = staleTcs.Task };
            AssetManager.ImplFactory = () => stale;
            var tA = AssetManager.InitializeAsync();

            AssetManager.Destroy();

            var freshTcs = new UniTaskCompletionSource();
            var fresh = new FakeAssetManager { InitTask = freshTcs.Task };
            AssetManager.ImplFactory = () => fresh;
            var tC = AssetManager.InitializeAsync();
            var tE = AssetManager.InitializeAsync();

            // 旧代际以失败收场：异常不得广播给新代际的加入者（改前：E 收到旧代际的异常）
            staleTcs.TrySetException(new InvalidOperationException("旧代际失败"));
            Assert.Throws<InvalidOperationException>(() => tA.GetAwaiter().GetResult());
            Assert.AreEqual(UniTaskStatus.Pending, tE.Status, "旧代际的失败不得污染新代际的加入者");

            freshTcs.TrySetResult();
            tC.GetAwaiter().GetResult();
            tE.GetAwaiter().GetResult();
            Assert.IsTrue(AssetManager.IsInitialized, "新代际应正常完成");
        }

        #endregion

        #region 初始化进度上报转发

        [Test]
        public void InitializeAsync_ForwardsProgressSinkToInstance()
        {
            AssetManager.Destroy();
            var fake = new FakeAssetManager();
            AssetManager.ImplFactory = () => fake;
            var sink = new RecordingInitProgress();

            AssetManager.InitializeAsync(options: null, progress: sink).GetAwaiter().GetResult();

            Assert.IsTrue(AssetManager.IsInitialized);
            Assert.AreEqual(1, fake.InitCallCount);
            Assert.AreSame(sink, fake.LastInitProgress, "进度上报接收方应原样转发到底层实例");
        }

        [Test]
        public void InitializePackageAsync_ForwardsProgressSinkToInstance()
        {
            var options = new AssetInitOptions { PackageName = "ExtraPackage" };
            var sink = new RecordingInitProgress();

            AssetManager.InitializePackageAsync(options, sink).GetAwaiter().GetResult();

            Assert.AreEqual(1, _fake.InitializePackageCallCount);
            Assert.AreSame(sink, _fake.LastInitProgress, "进度上报接收方应原样转发到底层实例");
        }

        [Test]
        public void InitializeAsync_NullProgress_DoesNotThrow()
        {
            AssetManager.Destroy();
            var fake = new FakeAssetManager();
            AssetManager.ImplFactory = () => fake;

            AssetManager.InitializeAsync().GetAwaiter().GetResult();

            Assert.IsTrue(AssetManager.IsInitialized, "progress 为 null(默认)不应影响初始化");
            Assert.IsNull(fake.LastInitProgress);
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
            AssetManager.InitializePackageAsync(options).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.InitializePackageCallCount);
        }

        [Test]
        public void LoadAsync_ForwardsToInstance()
        {
            AssetManager.LoadAsync<GameObject>("characters/player").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.LoadCallCount);
        }

        [Test]
        public void LoadAllAsync_ForwardsToInstance()
        {
            var locations = new[] { "characters/hero", "characters/enemy", "effects/explosion" };
            var handles = AssetManager.LoadAllAsync<GameObject>(locations).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.LoadAllCallCount);
            Assert.AreEqual(locations.Length, handles.Length, "句柄数组应与 locations 按序对应");
            CollectionAssert.AreEqual(locations, _fake.LastLoadAllLocations);
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

        /// <summary>记录型进度接收方：记录每次 Report 载荷供断言。</summary>
        private sealed class RecordingInitProgress : IProgress<AssetInitReport>
        {
            public readonly List<AssetInitReport> Reports = new List<AssetInitReport>();

            public void Report(AssetInitReport value)
            {
                Reports.Add(value);
            }
        }
    }
}
