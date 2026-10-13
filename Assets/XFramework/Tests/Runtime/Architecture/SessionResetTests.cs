using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAsset;
using XFramework.XAsset.Tests;
using XFramework.XBootstrap;
using XFramework.XConfig;
using XFramework.XFileManager;
using XFramework.XLocalization;
using XFramework.XPipeline;
using XFramework.XUI;
using XFramework.XUpdate;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 会话级复位的回归用例（**改前必红**）：门面在「会话 1 → 退出 → 会话 2」之后必须能重新初始化。
    /// <para><b>它锁的缺陷</b>：关闭域重载（Project Settings → Editor → Enter Play Mode Options →
    /// 取消 Reload Domain）时，上一播放会话的静态字段存活到第二会话，而门面的 <c>Initialize</c> 会因
    /// 门锁仍为真而**告警后早退**——<c>Destroy</c> 能复位一切，但正常游戏流程不会调它
    /// （「谁初始化谁清理」只在退出路径成立，而使用方在启动时调一次 <c>Initialize</c> 之后就不再碰了）。</para>
    /// <para><b>为什么能在单个播放会话里验</b>：缺陷需要「静态状态跨会话存活」与「<c>Initialize</c> 早退」
    /// 同时成立——后者在单会话里手动造得出来：<see cref="SimulateNewSession"/> 做两件事，
    /// 拆掉旧调度器（<c>UpdateManager.OnQuitting</c>，<c>Application.quitting</c> 上跑的正是它）
    /// 与调用各门面的 <c>ResetForNewSession</c>（那正是 <c>SubsystemRegistration</c> 上会跑的东西）。
    /// <b>拿掉任一复位即红</b>——这就是「改前必红」的证明路径。</para>
    /// </summary>
    [TestFixture]
    public class SessionResetTests
    {
        #region Test Doubles

        /// <summary>最小引导阶段：登记表用例只关心「在不在表里」。</summary>
        private sealed class StubStage : IBootstrapStage
        {
            public int Phase => 0;
            public string Name => nameof(StubStage);
            public float Weight => 1f;

            public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
                => UniTask.CompletedTask;

            public void Shutdown()
            {
            }
        }

        #endregion

        #region Fixture

        private GameObject _uiRoot;

        [SetUp]
        public void SetUp()
        {
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            ConfigManager.Destroy();
            LocalizationManager.Destroy();
            FileManager.Destroy();
            AssetManager.Destroy();
            AssetManager.ImplFactory = null;
            Bootstrap.Clear();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _uiRoot = new GameObject("UIRoot_SessionResetTest", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            ConfigManager.Destroy();
            LocalizationManager.Destroy();
            FileManager.Destroy();
            AssetManager.Destroy();
            AssetManager.ImplFactory = null;
            Bootstrap.Clear();

            if (_uiRoot != null)
                UnityEngine.Object.DestroyImmediate(_uiRoot);
        }

        /// <summary>
        /// 模拟「退出播放 → 第二个会话开始」：<c>SubsystemRegistration</c> 上真正会跑的就是这些复位钩子。
        /// </summary>
        private static void SimulateNewSession()
        {
            UpdateManager.OnQuitting();
            UpdateManager.AutoInit();

            UIManager.ResetForNewSession();
            ConfigManager.ResetForNewSession();
            LocalizationManager.ResetForNewSession();
            FileManager.ResetForNewSession();
            AssetManager.ResetForNewSession();
            Bootstrap.ResetForNewSession();
            InstanceTracker.AutoInit();
        }

        #endregion

        #region 整条会话序列

        /// <summary>
        /// 六个门面 + 登记表在会话 1 全部就位 → 退出 → 会话 2 全部重新就位。
        /// <para>与上面逐门面的用例互补：那些各自只初始化一个门面（对其余门面的复位是空操作），
        /// 这条验的是「它们同时在场时不互相干扰」——真实应用启动就是这种形状。</para>
        /// </summary>
        [Test]
        public async Task FullSessionCycle_AllFacadesComeBack()
        {
            await BootSessionAsync();
            AssertAllBooted("前提校验：会话 1");

            SimulateNewSession();
            Assert.IsFalse(UIManager.IsInitialized || ConfigManager.IsInitialized ||
                           LocalizationManager.IsInitialized || FileManager.IsInitialized ||
                           AssetManager.IsInitialized || Bootstrap.Stages.Count > 0,
                "会话边界过后，门面与登记表都应回到「未初始化」");

            await BootSessionAsync();
            AssertAllBooted("第二个会话");
        }

        /// <summary>按默认引导形状把六个门面与一个阶段都拉起来。</summary>
        private async UniTask BootSessionAsync()
        {
            UIManager.Initialize(_uiRoot.transform);
            ConfigManager.Initialize();
            LocalizationManager.Initialize("zh_Hans", new Dictionary<string, string>());
            FileManager.Initialize();

            // 复位会清掉测试钩子，故每个会话都要重新注入
            AssetManager.ImplFactory = () => new FakeAssetManager();
            await AssetManager.InitializeAsync();

            Bootstrap.Register(new StubStage());
        }

        /// <summary>断言六个门面与登记表都已就位。</summary>
        private static void AssertAllBooted(string label)
        {
            Assert.IsTrue(UIManager.IsInitialized, $"{label}：UI 应当已就位");
            Assert.IsTrue(ConfigManager.IsInitialized, $"{label}：Config 应当已就位");
            Assert.IsTrue(LocalizationManager.IsInitialized, $"{label}：Localization 应当已就位");
            Assert.IsTrue(FileManager.IsInitialized, $"{label}：File 应当已就位");
            Assert.IsTrue(AssetManager.IsInitialized, $"{label}：Asset 应当已就位");
            Assert.AreEqual(1, Bootstrap.Stages.Count, $"{label}：登记表应当有 1 个阶段");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), $"{label}：UI 的帧驱动器应当在调度器里");
        }

        #endregion

        #region 静态登记表

        /// <summary>
        /// 登记表是静态的，同样跨会话存活——而会话 2 只能**追加**新阶段实例，
        /// 旧实例会留在表里被一起装配。所以会话开始必须清空它。
        /// </summary>
        /// <summary>
        /// <c>InstanceTracker</c> 的活跃计数是静态的，同样跨会话存活。它此前靠**静态构造函数 + 匿名 lambda**
        /// 订阅退出清理——退订不了、族清单也扫不到；现在改成具名 <c>AutoInit</c>（幂等订阅 + 会话开始清空）。
        /// </summary>
        [Test]
        public void InstanceTracker_ResetClearsStaleActiveCounts()
        {
            var go = new GameObject("InstanceTracker_SessionResetTest");
            var tracker = go.AddComponent<InstanceTracker>();
            tracker.Location = "ui/a";

            // AddComponent 时的 OnEnable 早于 Location 赋值（被跳过），这里靠一次失活/激活补记
            go.SetActive(false);
            go.SetActive(true);
            Assert.AreEqual(1, InstanceTracker.GetActiveCount("ui/a"), "前提校验：活跃计数应当已记上");

            SimulateNewSession();

            Assert.AreEqual(0, InstanceTracker.GetActiveCount("ui/a"),
                "会话开始时活跃计数必须清零——上一会话的实例早已随域销毁，留着就是幽灵计数");

            // 直接销毁是安全的：从未 SetHandle 过的追踪器持有 default 句柄，其 Dispose 是空操作
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Bootstrap_ResetClearsTheStageRegistry()
        {
            Bootstrap.Register(new StubStage());
            Assert.AreEqual(1, Bootstrap.Stages.Count, "前提校验：阶段应当已登记");

            SimulateNewSession();

            Assert.AreEqual(0, Bootstrap.Stages.Count,
                "会话开始时登记表必须清空——否则第二会话的 ConfigureStages 只能追加，" +
                "旧阶段实例会被一起装配（同相位并行双跑），退出时还会被双份 Shutdown");
        }

        #endregion

        #region 门面逐个复位

        [Test]
        public void UI_ReinitializesAfterSessionReset()
        {
            UIManager.Initialize(_uiRoot.transform);
            Assert.IsTrue(UIManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(UIManager.IsInitialized, "复位后门面应回到未初始化态");

            UIManager.Initialize(_uiRoot.transform);

            Assert.IsTrue(UIManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), "帧驱动器应重新进调度器");
        }

        [Test]
        public void Config_ReinitializesAfterSessionReset()
        {
            ConfigManager.Initialize();
            Assert.IsTrue(ConfigManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(ConfigManager.IsInitialized, "复位后门面应回到未初始化态");

            ConfigManager.Initialize();

            Assert.IsTrue(ConfigManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
        }

        [Test]
        public void Localization_ReinitializesAfterSessionReset()
        {
            LocalizationManager.Initialize("zh_Hans", new Dictionary<string, string>());
            Assert.IsTrue(LocalizationManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(LocalizationManager.IsInitialized, "复位后门面应回到未初始化态");

            LocalizationManager.Initialize("zh_Hans", new Dictionary<string, string>());

            Assert.IsTrue(LocalizationManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
        }

        [Test]
        public async Task Asset_ReinitializesAfterSessionReset()
        {
            AssetManager.ImplFactory = () => new FakeAssetManager();
            await AssetManager.InitializeAsync();
            Assert.IsTrue(AssetManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(AssetManager.IsInitialized, "复位后门面应回到未初始化态");

            // 复位会一并清掉测试钩子（陈旧静态不该跨会话存活），第二会话由用例重新注入假实现
            AssetManager.ImplFactory = () => new FakeAssetManager();
            await AssetManager.InitializeAsync();

            Assert.IsTrue(AssetManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警早退");
        }

        /// <summary>
        /// Asset 比其它门面多一件：**作废在途初始化**。上一会话半路中断的初始化不得在第二会话里
        /// 让实例「复活」——那正是代际号存在的理由。
        /// </summary>
        [Test]
        public void Asset_ResetInvalidatesTheInFlightInit()
        {
            var gate = new UniTaskCompletionSource();
            var fake = new FakeAssetManager { InitTask = gate.Task };
            AssetManager.ImplFactory = () => fake;

            UniTask inFlight = AssetManager.InitializeAsync();
            Assert.AreEqual(1, fake.InitCallCount, "前提校验：初始化应当已发起");

            AssetManager.ResetForNewSession();

            gate.TrySetResult(); // 上一会话的初始化此刻才「完成」——它必须被丢弃

            Assert.Throws<InvalidOperationException>(() => inFlight.GetAwaiter().GetResult(),
                "上一会话在途的初始化结果不得在第二会话里让实例复活");
            Assert.IsFalse(AssetManager.IsInitialized);
            Assert.IsTrue(fake.Disposed, "被作废的那个实例应当已被释放，而不是留在门面里");
        }

        /// <summary>
        /// 两个「只报一次」的闩锁是**每会话一次**的提示，不是每进程一次——不复位则第二会话静默。
        /// 这里用能力缺失分支（假件不实现 <c>IAssetPoolController</c>）来观测它。
        /// </summary>
        [Test]
        public async Task Asset_ResetReArmsTheOnceOnlyPoolWarning()
        {
            AssetManager.ImplFactory = () => new FakeAssetManager();
            await AssetManager.InitializeAsync();

            LogAssert.Expect(LogType.Warning, new Regex("不支持 IAssetPoolController"));
            AssetManager.ClearPool("ui/a");

            AssetManager.ResetForNewSession();

            AssetManager.ImplFactory = () => new FakeAssetManager(); // 复位清掉了钩子，第二会话重新注入
            await AssetManager.InitializeAsync();

            LogAssert.Expect(LogType.Warning, new Regex("不支持 IAssetPoolController"));
            AssetManager.ClearPool("ui/a");
        }

        /// <summary>
        /// 同上的场景能力那一把闩锁（<c>_sceneCapabilityWarned</c>）：不复位则第二会话里
        /// 「不支持 IAssetSceneLoader」这条告警不会再来一次——而那正是**每会话一次**的承诺。
        /// </summary>
        [Test]
        public async Task Asset_ResetReArmsTheOnceOnlySceneWarning()
        {
            AssetManager.ImplFactory = () => new FakeAssetManager();
            await AssetManager.InitializeAsync();

            LogAssert.Expect(LogType.Warning, new Regex("不支持 IAssetSceneLoader"));
            await AssetManager.LoadSceneAsync("scenes/main");

            AssetManager.ResetForNewSession();

            AssetManager.ImplFactory = () => new FakeAssetManager(); // 复位清掉了钩子，第二会话重新注入
            await AssetManager.InitializeAsync();

            LogAssert.Expect(LogType.Warning, new Regex("不支持 IAssetSceneLoader"));
            await AssetManager.LoadSceneAsync("scenes/main");
        }

        [Test]
        public void File_ReinitializesAfterSessionReset()
        {
            FileManager.Initialize();
            Assert.IsTrue(FileManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(FileManager.IsInitialized, "复位后门面应回到未初始化态");

            FileManager.Initialize();

            Assert.IsTrue(FileManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
        }

        #endregion
    }
}
