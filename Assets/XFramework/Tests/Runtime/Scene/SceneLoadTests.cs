using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XFramework.XAsset;
using XFramework.XScene;

// 本文件的命名空间里就有 XFramework.XScene.SceneManager（判的是类型名），想指 Unity 那个必须起别名
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace XFramework.XScene.Tests
{
    /// <summary>
    /// <see cref="SceneManager"/> 的加载编排测试：模式与选项下传、进度转接、失败形态、前置校验、
    /// 两步后处理（激活 / 回收）与取消。
    /// <para><b>为什么在 PlayMode</b>：激活那几条要真的造场景（<c>SceneManager.CreateScene</c>），
    /// EditMode 下造不出来。</para>
    /// <para><b>复位</b>：PlayMode 下所有用例共享一个 player 实例——TearDown 必须卸载自建场景、
    /// 复位 <c>AssetManager</c>（含 <c>ImplFactory</c>），否则下一个 fixture 见到的是一个被注入了假件的门面。</para>
    /// </summary>
    [TestFixture]
    public class SceneLoadTests
    {
        #region Private Fields

        private const string Location = "scenes/main";

        private FakeSceneAssetManager _fake;
        private Scene _createdScene;

        #endregion

        #region Lifecycle

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _fake = new FakeSceneAssetManager();
            AssetManager.SetInstance(_fake);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_createdScene.IsValid() && _createdScene.isLoaded)
            {
                var unload = UnitySceneManager.UnloadSceneAsync(_createdScene);
                if (unload != null) yield return unload;
            }

            _createdScene = default;
            AssetManager.ImplFactory = null;
            AssetManager.Destroy();

            // 能力缺失那条用例会把「只报一次」闩锁打开，复位它是本 fixture 的责任
            AssetManager.ResetForNewSession();
            yield return null;
        }

        #endregion

        #region Tests — 模式与选项下传

        [Test]
        public async Task LoadSceneAsync_PassesLocationAndSingleModeByDefault()
        {
            await SceneManager.LoadSceneAsync(Location);

            Assert.AreEqual(1, _fake.SceneLoadCallCount);
            Assert.AreEqual(Location, _fake.LastSceneLocation);
            Assert.AreEqual(LoadSceneMode.Single, _fake.LastSceneMode, "缺省模式必须是 Single");
        }

        [Test]
        public async Task LoadSceneAsync_PassesAdditiveModeFromOptions()
        {
            await SceneManager.LoadSceneAsync(Location, SceneLoadOptions.Additive);

            Assert.AreEqual(LoadSceneMode.Additive, _fake.LastSceneMode);
        }

        #endregion

        #region Tests — 进度

        [Test]
        public async Task LoadSceneAsync_WiresProgressCarryingTheLocation()
        {
            var progress = new RecordingProgress();

            await SceneManager.LoadSceneAsync(Location, default, progress);

            Assert.IsNotNull(_fake.LastSceneProgress, "进度回调必须接到 XAsset 的 Action<float> 上");
            _fake.LastSceneProgress(0.5f);

            Assert.AreEqual(1, progress.Reports.Count);
            Assert.AreEqual(0.5f, progress.Reports[0].Progress);
            Assert.AreEqual(Location, progress.Reports[0].Location, "载荷必须带上是哪个场景的进度（可能并发加载多个）");
        }

        [Test]
        public async Task LoadSceneAsync_WithoutProgress_DoesNotThrow()
        {
            await SceneManager.LoadSceneAsync(Location);

            Assert.IsNull(_fake.LastSceneProgress, "不传进度时不应凭空造一个回调");
        }

        #endregion

        #region Tests — 失败与前置校验

        [Test]
        public async Task LoadSceneAsync_Failed_ReturnsDefaultHandle()
        {
            _fake.SceneLoadResult = default; // 后端失败：返回无效句柄

            var handle = await SceneManager.LoadSceneAsync(Location);

            Assert.IsTrue(handle.IsDefault, "后端失败必须原样透出无效句柄，本模块不抛异常");
        }

        [Test]
        public async Task LoadSceneAsync_EmptyLocation_ThrowsArgumentException()
        {
            var task = SceneManager.LoadSceneAsync("  ");

            var ex = await CaptureAsync<ArgumentException>(task);

            Assert.IsNotNull(ex, "空地址应当在调用 XAsset 之前就被拒绝");
            Assert.AreEqual(0, _fake.SceneLoadCallCount, "参数校验失败不得惊动后端");
        }

        [Test]
        public async Task LoadSceneAsync_BeforeAssetInitialized_ThrowsWithSceneManagerPrefix()
        {
            AssetManager.Destroy();

            var ex = await CaptureAsync<InvalidOperationException>(SceneManager.LoadSceneAsync(Location));

            Assert.IsNotNull(ex, "资源管理器未初始化时必须给出可读的错误，而不是让使用方去猜无效句柄");
            StringAssert.Contains("[SceneManager]", ex.Message, "消息必须带模块前缀");
            StringAssert.Contains("InitializeAsync", ex.Message, "消息必须带修复提示");
        }

        [Test]
        public async Task LoadSceneAsync_WhenCapabilityMissing_ReturnsDefaultHandle()
        {
            // 换成不实现 IAssetSceneLoader 的基类假件（SetInstance 会覆盖前一个实例）
            AssetManager.SetInstance(new XAsset.Tests.FakeAssetManager());
            LogAssert.Expect(LogType.Warning, new Regex("不支持 IAssetSceneLoader"));

            var handle = await SceneManager.LoadSceneAsync(Location);

            Assert.IsTrue(handle.IsDefault, "后端不支持场景能力时应当降级为无效句柄");
        }

        #endregion

        #region Tests — 两步后处理

        [Test]
        public async Task LoadSceneAsync_ActivateAfterLoad_SetsTheNewSceneActive()
        {
            var scene = UnitySceneManager.CreateScene("SceneLoadTests_Additive");
            _createdScene = scene;
            _fake.SceneLoadResult = new AssetSceneHandle(1, scene);

            await SceneManager.LoadSceneAsync(Location, new SceneLoadOptions(LoadSceneMode.Additive, activateAfterLoad: true));

            Assert.AreEqual(scene, SceneManager.ActiveScene, "叠加加载 + 激活选项之后，新场景应当成为激活场景");
        }

        [Test]
        public async Task LoadSceneAsync_WithoutActivateAfterLoad_KeepsActiveScene()
        {
            var scene = UnitySceneManager.CreateScene("SceneLoadTests_Additive");
            _createdScene = scene;
            _fake.SceneLoadResult = new AssetSceneHandle(1, scene);
            var before = SceneManager.ActiveScene;

            await SceneManager.LoadSceneAsync(Location, SceneLoadOptions.Additive);

            Assert.AreEqual(before, SceneManager.ActiveScene, "叠加加载默认不得改变激活场景（新物体会挂到旧场景下）");
        }

        [Test]
        public async Task LoadSceneAsync_UnloadUnusedAfterLoad_Reclaims()
        {
            _fake.SceneLoadResult = new AssetSceneHandle(1, default);

            await SceneManager.LoadSceneAsync(Location, new SceneLoadOptions(LoadSceneMode.Single, unloadUnusedAfterLoad: true));

            Assert.AreEqual(1, _fake.UnloadUnusedCallCount, "选项打开时应当在返回前完成一次回收");
        }

        [Test]
        public async Task LoadSceneAsync_UnloadUnusedAfterLoad_NotSet_DoesNotReclaim()
        {
            _fake.SceneLoadResult = new AssetSceneHandle(1, default);

            await SceneManager.LoadSceneAsync(Location, SceneLoadOptions.Single);

            Assert.AreEqual(0, _fake.UnloadUnusedCallCount, "默认不做回收——什么时候划算由使用方决定");
        }

        [Test]
        public async Task LoadSceneAsync_Canceled_ThrowsAndSkipsPostSteps()
        {
            _fake.DelayOneFrame = true;
            _fake.SceneLoadResult = new AssetSceneHandle(1, default);
            var cts = new CancellationTokenSource();

            var task = SceneManager.LoadSceneAsync(Location,
                new SceneLoadOptions(LoadSceneMode.Single, unloadUnusedAfterLoad: true), null, cts.Token);
            cts.Cancel();

            var ex = await CaptureAsync<OperationCanceledException>(task);

            Assert.IsNotNull(ex, "取消应当抛出 OperationCanceledException");
            Assert.AreEqual(0, _fake.UnloadUnusedCallCount, "取消路径不得执行后处理");
        }

        [Test]
        public async Task LoadSceneAsync_CanceledDuringCleanup_StillReturnsHandle()
        {
            _fake.SceneLoadResult = new AssetSceneHandle(1, default);
            _fake.Gate = new UniTaskCompletionSource();
            var cts = new CancellationTokenSource();

            var task = SceneManager.LoadSceneAsync(Location,
                new SceneLoadOptions(LoadSceneMode.Single, unloadUnusedAfterLoad: true), null, cts.Token);

            Assert.AreEqual(1, _fake.UnloadUnusedCallCount, "回收段应已开始（加载同步完成）");

            // 加载已同步完成、回收段在途——此刻取消不得丢掉已加载成功的句柄（SCN-001）
            cts.Cancel();
            _fake.Gate.TrySetResult();

            AssetSceneHandle handle = default;
            try
            {
                handle = await task;
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("加载成功后的回收阶段不应再受调用方令牌约束——取消丢掉了已加载成功的句柄");
            }
            finally
            {
                cts.Dispose();
            }

            Assert.AreEqual(1, handle.Id, "加载成功后的回收阶段不应再受调用方令牌约束");
            Assert.AreEqual(CancellationToken.None, _fake.LastUnloadUnusedToken,
                "回收段应收到 CancellationToken.None");
        }

        [Test]
        public async Task LoadSceneAsync_UnloadUnusedThrows_StillReturnsHandle()
        {
            _fake.SceneLoadResult = new AssetSceneHandle(1, default);
            _fake.UnloadUnusedException = new InvalidOperationException("回收后端失败");

            LogAssert.Expect(LogType.Error, new Regex("资源回收失败"));

            var handle = await SceneManager.LoadSceneAsync(Location,
                new SceneLoadOptions(LoadSceneMode.Single, unloadUnusedAfterLoad: true));

            Assert.AreEqual(1, handle.Id,
                "回收抛非取消异常时句柄仍必须交回——否则场景已进来、外部却无卸载凭据（SCN-002）");
        }

        #endregion

        #region Internal Methods

        /// <summary>把异步异常捕获成返回值——UniTask 的异常在 await 处才抛出，<c>Assert.Throws</c> 抓不到。</summary>
        private static async UniTask<TException> CaptureAsync<TException>(UniTask task) where TException : Exception
        {
            try
            {
                await task;
            }
            catch (TException ex)
            {
                return ex;
            }

            return null;
        }

        /// <summary>同步记录型进度接收方（<c>Progress&lt;T&gt;</c> 会 Post 到同步上下文，断言等不到）。</summary>
        private sealed class RecordingProgress : IProgress<SceneLoadProgress>
        {
            public readonly List<SceneLoadProgress> Reports = new List<SceneLoadProgress>();

            public void Report(SceneLoadProgress value) => Reports.Add(value);
        }

        #endregion
    }
}
