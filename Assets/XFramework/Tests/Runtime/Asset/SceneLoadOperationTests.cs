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

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// <see cref="YooAssetManagerImpl"/> 场景加载路径的**缝级**测试。
    /// <para><b>为什么必须在这条缝上测</b>：Runtime 测试程序集不引用 YooAsset，测试环境也没有可运行的资源包
    /// ——真路径在用例里跑不起来。<see cref="ISceneLoadSource"/> 之上是框架自己的逻辑（轮询、进度、失败释放、
    /// 取消善后、取场景对象、句柄登记与卸载），之下才是对 YooAsset 的机械转发。本 fixture 打的是上面那一半；
    /// 下面那一半只能靠读源码核对与人工验收——如实标注射程，不假装已覆盖。</para>
    /// <para><b>覆盖不到的一处</b>：「拒绝卸载当前唯一的场景」分支在测试壳里不可达——用例造出来的场景
    /// 必然是 sceneCount 里的第二个。该分支只有推理级保证，同样记在模块文档里。</para>
    /// <para><b>复位</b>：PlayMode 下所有用例共享一个 player 实例。<c>SceneManager.CreateScene</c> 造出来的
    /// 场景不卸载就会留给下一个用例，故 TearDown 必须把它卸掉。</para>
    /// </summary>
    [TestFixture]
    public class SceneLoadOperationTests
    {
        #region Private Fields

        private const string Location = "scene/level_01";

        private FakeSceneLoadSource _source;
        private FakeSceneLoadOperation _operation;
        private YooAssetManagerImpl _manager;
        private Scene _createdScene;

        #endregion

        #region Lifecycle

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _source = new FakeSceneLoadSource();
            _operation = new FakeSceneLoadOperation();
            _source.NextOperation = _operation;
            _manager = new YooAssetManagerImpl("TestPackage", _source);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_createdScene.IsValid() && _createdScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(_createdScene);
                if (unload != null) yield return unload;
            }

            _createdScene = default;
            _manager = null;
            _operation = null;
            _source = null;
            yield return null;
        }

        #endregion

        #region Tests — 基本形态

        [Test]
        public async Task SucceededLoad_ReturnsValidHandleCarryingTheOperationsScene()
        {
            var scene = CreateRealScene();
            _operation.IsDone = true;
            _operation.IsSucceed = true;
            _operation.SceneObject = scene;

            var handle = await _manager.LoadSceneAsync(Location);

            Assert.IsFalse(handle.IsDefault, "成功加载必须交出有效句柄");
            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(scene, handle.Scene, "句柄里的场景必须来自操作自己，不能来自按名回查");
            Assert.AreEqual(0, _operation.ReleaseCount, "成功路径不得释放句柄——场景仍在场");
            Assert.AreEqual(Location, _source.LastLocation);
            Assert.AreEqual(LoadSceneMode.Single, _source.LastMode, "缺省模式应为 Single");
        }

        [Test]
        public async Task ExternalSceneUnload_PrunesSceneOperationRegistration()
        {
            // AST-003 / SCN-003：Single 模式切场景或外部卸载不会经过 UnloadSceneAsync，
            // 登记表必须靠 sceneUnloaded 回收，否则会话内线性增长。
            var scene = CreateRealScene();
            _operation.IsDone = true;
            _operation.IsSucceed = true;
            _operation.SceneObject = scene;

            var handle = await _manager.LoadSceneAsync(Location);
            Assert.IsFalse(handle.IsDefault, "前提：加载成功");
            Assert.AreEqual(1, _manager.SceneOperationCount, "前提：登记项已写入");

            var unload = SceneManager.UnloadSceneAsync(scene);
            while (unload != null && !unload.isDone)
                await UniTask.Yield();
            await UniTask.Yield(); // 让 sceneUnloaded 回调跑完

            Assert.AreEqual(0, _manager.SceneOperationCount,
                "场景被外部卸载后登记项必须被清理（AST-003/SCN-003）");
        }

        /// <summary>
        /// 反例守卫：**不许按名字回查场景**。假件的场景名与 location 故意不同——一旦实现退回
        /// <c>SceneManager.GetSceneByName(location)</c> 这类查表，本用例立刻红。
        /// </summary>
        [Test]
        public async Task SucceededLoad_IgnoresNameLookup()
        {
            var scene = CreateRealScene();
            _operation.IsDone = true;
            _operation.IsSucceed = true;
            _operation.SceneObject = scene;

            var handle = await _manager.LoadSceneAsync(Location);

            Assert.AreNotEqual(Location, handle.Scene.name, "前提：假件的场景名与 location 不同");
            Assert.AreEqual(scene, handle.Scene, "场景对象只能来自操作，不能来自按名回查");
        }

        [Test]
        public async Task AdditiveLoad_PassesAdditiveMode()
        {
            _operation.IsDone = true;
            _operation.IsSucceed = true;
            _operation.SceneObject = CreateRealScene();

            await _manager.LoadSceneAsync(Location, LoadSceneMode.Additive);

            Assert.AreEqual(LoadSceneMode.Additive, _source.LastMode);
        }

        [Test]
        public async Task FailedLoad_ReturnsDefaultHandle()
        {
            LogAssert.Expect(LogType.Error, new Regex("Failed to load scene"));

            _operation.IsDone = true;
            _operation.IsSucceed = false;
            _operation.LastError = "not found";

            var handle = await _manager.LoadSceneAsync(Location);

            Assert.IsTrue(handle.IsDefault, "加载失败必须返回 default(AssetSceneHandle)");
            Assert.IsFalse(handle.IsValid);
        }

        /// <summary>
        /// **失败路径的句柄泄漏**：失败时没有场景，YooAsset 挂在 <c>sceneUnloaded</c> 上的兜底永不触发，
        /// 不显式释放则该 provider 永久留在 <c>SceneHandles</c> 里。
        /// </summary>
        [Test]
        public async Task FailedLoad_ReleasesTheHandle()
        {
            LogAssert.Expect(LogType.Error, new Regex("Failed to load scene"));

            _operation.IsDone = true;
            _operation.IsSucceed = false;
            _operation.LastError = "not found";

            await _manager.LoadSceneAsync(Location);

            Assert.AreEqual(1, _operation.ReleaseCount,
                "失败路径必须显式释放句柄，否则 provider 与 bundle 引用永久驻留");
        }

        [Test]
        public async Task PackageUnavailable_ReturnsDefaultHandle()
        {
            _source.NextOperation = null;

            var handle = await _manager.LoadSceneAsync(Location);

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(1, _source.LoadCount);
        }

        /// <summary>
        /// 进度契约：按轮询顺序上报，成功时以 <c>1f</c> 收尾。
        /// <para>回调里推进假件的状态，是为了让每一轮读到的值可预期——否则「同一帧内读两次同一个值」
        /// 会让断言写成一个只对当前实现成立的样子。</para>
        /// </summary>
        [Test]
        public async Task LoadSceneAsync_ReportsProgressInOrderAndCompletesWithOne()
        {
            var scene = CreateRealScene();
            var reported = new List<float>();
            _operation.Progress = 0.25f;

            var task = _manager.LoadSceneAsync(Location, LoadSceneMode.Single, p =>
            {
                reported.Add(p);
                _operation.Progress = 0.75f;
                if (reported.Count >= 2)
                {
                    _operation.IsDone = true;
                    _operation.IsSucceed = true;
                    _operation.SceneObject = scene;
                }
            }, CancellationToken.None);

            var handle = await task;

            CollectionAssert.AreEqual(new[] { 0.25f, 0.75f, 1f }, reported,
                "进度应按轮询顺序上报，且只在成功路径补发 1f");
            Assert.AreEqual(scene, handle.Scene);
        }

        #endregion

        #region Tests — 取消善后

        /// <summary>
        /// **取消后的残留场景（Additive）**：YooAsset 的场景加载中止不了，取消只让等待方提前退出。
        /// 框架接力到后台把它卸掉，世界回到调用前的样子。
        /// </summary>
        [Test]
        public async Task CancelledAdditiveLoad_WhenItCompletes_UnloadsTheScene()
        {
            var cts = new CancellationTokenSource();
            var task = _manager.LoadSceneAsync(Location, LoadSceneMode.Additive, null, cts.Token);
            await UniTask.Yield(PlayerLoopTiming.Update);

            cts.Cancel();
            await AssertCancelled(task);

            // 底层照跑完——这才是取消之后真实会发生的事
            _operation.IsDone = true;
            _operation.IsSucceed = true;
            await UniTask.DelayFrame(3);

            Assert.AreEqual(1, _operation.UnloadCount,
                "被取消的叠加加载完成后应当被卸载，否则场景会凭空多出来且无人认领");
            Assert.AreEqual(0, _operation.ReleaseCount, "卸载成功路径不应再补释放");
        }

        /// <summary>
        /// **取消后的残留场景（Single）**：不卸载也<b>不</b>释放——卸载会把工程退化成「一个场景都没有」；
        /// 释放会让引用计数归零，此后一次 <c>UnloadUnusedAssetsAsync</c> 就能回收掉这个仍在场的场景
        /// 所引用的 bundle。句柄交给 YooAsset 的 <c>sceneUnloaded</c> 账本，只留一条告警。
        /// </summary>
        [Test]
        public async Task CancelledSingleLoad_KeepsTheSceneAndReleasesNothing()
        {
            var cts = new CancellationTokenSource();
            var task = _manager.LoadSceneAsync(Location, LoadSceneMode.Single, null, cts.Token);
            await UniTask.Yield(PlayerLoopTiming.Update);

            cts.Cancel();
            await AssertCancelled(task);

            LogAssert.Expect(LogType.Warning, new Regex("cannot be aborted"));
            _operation.IsDone = true;
            _operation.IsSucceed = true;
            await UniTask.DelayFrame(3);

            Assert.AreEqual(0, _operation.UnloadCount, "Single 模式不得卸载——那会让工程没有任何场景");
            Assert.AreEqual(0, _operation.ReleaseCount, "Single 模式不得释放——场景仍在场引用着它的 bundle");
        }

        /// <summary>取消后底层<b>失败</b>：没有场景留下，句柄必须释放（与失败路径同一条理由）。</summary>
        [Test]
        public async Task CancelledLoad_WhenItFails_ReleasesTheHandle()
        {
            var cts = new CancellationTokenSource();
            var task = _manager.LoadSceneAsync(Location, LoadSceneMode.Additive, null, cts.Token);
            await UniTask.Yield(PlayerLoopTiming.Update);

            cts.Cancel();
            await AssertCancelled(task);

            _operation.IsDone = true;
            _operation.IsSucceed = false;
            _operation.LastError = "load failed after cancel";
            await UniTask.DelayFrame(3);

            Assert.AreEqual(1, _operation.ReleaseCount, "取消后失败同样不得留下悬空句柄");
            Assert.AreEqual(0, _operation.UnloadCount);
        }

        #endregion

        #region Tests — 卸载

        [Test]
        public async Task Unload_ForwardsToTheRegisteredOperation()
        {
            var handle = await LoadRealScene();

            var unloaded = await _manager.UnloadSceneAsync(handle);

            Assert.IsTrue(unloaded);
            Assert.AreEqual(1, _operation.UnloadCount);
        }

        /// <summary>卸载成功后登记项应当消失——再卸一次拿到的是 <c>false</c>，而不是重放一次卸载。</summary>
        [Test]
        public async Task Unload_IsIdempotent()
        {
            var handle = await LoadRealScene();
            await _manager.UnloadSceneAsync(handle);

            var again = await _manager.UnloadSceneAsync(handle);

            Assert.IsFalse(again, "第二次卸载应当因登记项已清而返回 false");
            Assert.AreEqual(1, _operation.UnloadCount, "不得重放卸载");
        }

        [Test]
        public async Task Unload_DefaultHandle_ReturnsFalse()
        {
            var unloaded = await _manager.UnloadSceneAsync(default);

            Assert.IsFalse(unloaded);
            Assert.AreEqual(0, _source.LoadCount, "无效句柄不该惊动后端");
        }

        /// <summary>
        /// 句柄与登记项必须指同一次加载。这里手工造一个「Id 对得上但场景对不上」的句柄——
        /// 真实场景里它来自上一任适配器（<c>Id</c> 各自从 1 开始分配）。只比 <c>Id</c> 会把
        /// 「卸载 A」变成「卸载 B」。
        /// </summary>
        [Test]
        public async Task Unload_HandleForAnotherScene_ReturnsFalse()
        {
            var handle = await LoadRealScene();
            var other = new AssetSceneHandle(handle.Id, SceneManager.CreateScene("SceneLoadOperationTests_Other"));

            var unloaded = await _manager.UnloadSceneAsync(other);

            Assert.IsFalse(unloaded, "Id 相同但场景不同的句柄必须被拒绝");
            Assert.AreEqual(0, _operation.UnloadCount);
        }

        /// <summary>场景已被别人卸掉（例如 Single 模式切场景时 Unity 卸的旧场景）：登记项顺手清掉，不重放卸载。</summary>
        [Test]
        public async Task Unload_WhenSceneWasUnloadedExternally_DropsTheStaleEntry()
        {
            var handle = await LoadRealScene();
            var unload = SceneManager.UnloadSceneAsync(_createdScene);
            if (unload != null) await unload.ToUniTask();
            _createdScene = default;

            var unloaded = await _manager.UnloadSceneAsync(handle);

            Assert.IsFalse(unloaded);
            // 新语义（AST-003/SCN-003）：sceneUnloaded 事件已经清掉登记项；真实适配器里
            // YooAsset 也在同一事件内自行 Release，管理器不得二次释放。假件不模拟
            // YooAsset 的自释放，故这里只能断言「管理器没有再次 Release / Unload」。
            Assert.AreEqual(0, _manager.SceneOperationCount, "sceneUnloaded 应已清掉登记项");
            Assert.AreEqual(0, _operation.ReleaseCount,
                "管理器不得在 sceneUnloaded 之后二次 Release（YooAsset 已自行释放）");
            Assert.AreEqual(0, _operation.UnloadCount, "不得重放卸载");
        }

        #endregion

        #region Internal Methods

        /// <summary>发一次「成功加载真场景」并返回句柄；用例的清理仍依赖 TearDown。</summary>
        private async UniTask<AssetSceneHandle> LoadRealScene()
        {
            var scene = CreateRealScene();
            _operation.IsDone = true;
            _operation.IsSucceed = true;
            _operation.SceneObject = scene;
            return await _manager.LoadSceneAsync(Location);
        }

        /// <summary>造一个真场景；本 fixture 的清理全部依赖它被 TearDown 卸载。</summary>
        private Scene CreateRealScene()
        {
            _createdScene = SceneManager.CreateScene("SceneLoadOperationTests_Temp");
            return _createdScene;
        }

        /// <summary>断言任务以取消收场；未取消则直接判失败。</summary>
        private static async UniTask AssertCancelled(UniTask task)
        {
            var cancelled = false;
            try
            {
                await task;
            }
            catch (System.OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.IsTrue(cancelled, "取消应当抛出 OperationCanceledException");
        }

        #endregion
    }
}
