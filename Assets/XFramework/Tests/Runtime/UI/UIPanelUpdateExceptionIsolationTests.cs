using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 帧通路的异常隔离测试：面板的 <c>OnUpdate</c> 抛异常时，坏面板停更，同帧其余面板与驱动器不受影响。
    /// <para><b>为什么必须有这一条</b>：派发循环此前直接调用 <c>panel.OnUpdate</c>，异常会一路逃逸到
    /// <see cref="UpdateManager"/> 的调度器——而调度器对节点回调的契约是「抛异常即记 LogError 并注销该节点」。
    /// 驱动器一旦被注销就再也回不来（门面记账是单向的：<c>_frameDriver</c> 非空即早退），于是
    /// <b>面板 / HUD / Tip 的整条每帧通路永久死亡</b>，且除了那条以内部类名结尾的 <c>[UpdateScheduler]</c>
    /// 日志外没有任何线索。同类防护在 <c>InputManager.PulseFrame</c> 已有先例。</para>
    /// <para><b>停更而不是继续驱动</b>：<c>OnUpdate</c> 的典型异常是持续态（空引用等），继续驱动等于每帧
    /// 刷一条带栈日志。停更标记随回池复位（<c>UIManagerImpl.RecyclePanel</c>），
    /// 池化实例重新打开后照常被驱动。</para>
    /// <para><b>「停更后不再产生第二条日志」怎么锁</b>：语义由断言锁住——停更意味着 <c>UpdateCount</c>
    /// 停在 1，真被继续驱动的话计数必然增长。日志侧再加一层：每条用例只注册<b>一次</b>
    /// <c>LogAssert.Expect</c>，多出来的错误日志会被测试框架判为未预期日志（用例其余断言通过时即现形）。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelUpdateExceptionIsolationTests
    {
        #region 前置状态

        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_UpdateExceptionTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<ThrowingOnUpdatePanel>();
            _factory.RegisterPanel<UpdateRecordingPanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #endregion

        #region 每帧档（FrameDriver）

        /// <summary>
        /// 坏面板抛异常后，同帧排在它后面的面板仍被驱动，且每帧驱动器仍在调度器里。
        /// <para>打开顺序即栈序，也就是桶内派发顺序——坏面板先抛，好面板必须照跑。修复前两条断言
        /// 同时红：循环在坏面板处中断（好面板计数为 0），驱动器被注销（<c>GetCount</c> 为 0）。</para>
        /// </summary>
        [Test]
        public async Task BadPanel_Throws_SiblingsStillDrivenAndDriverStaysRegistered()
        {
            var bad = await UIManager.OpenAsync<ThrowingOnUpdatePanel>("ui/bad");
            var good = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/good");

            // OpenAsync 不模糊已打开的面板（那是 PushAsync 的事），两块都在派发集合里
            Assert.IsFalse(bad.IsPaused, "前置：坏面板未被暂停");
            Assert.IsFalse(good.IsPaused, "前置：好面板未被暂停");

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[UIManager\] Panel 'ThrowingOnUpdatePanel'\.OnUpdate threw exception"));

            UpdateManager.Tick(0.016f);

            Assert.AreEqual(1, bad.UpdateCount, "坏面板确实被驱动过（计数在抛出前累加，空过不会通过）");
            Assert.Greater(good.UpdateCount, 0, "同帧排在后面的面板不受前一个面板异常的影响");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0),
                "驱动器不得因面板异常被调度器注销——它一旦被摘，UI 的整条每帧通路就永久死亡");
        }

        /// <summary>
        /// 抛出后的后续帧，UI 每帧通路仍然活着；坏面板停更，不再产生第二条日志。
        /// </summary>
        [Test]
        public async Task AfterBadPanelThrows_LaterFramesStillDriveOtherPanels()
        {
            var bad = await UIManager.OpenAsync<ThrowingOnUpdatePanel>("ui/bad");
            var good = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/good");

            // 只注册这一次期望：坏面板若继续每帧抛，第二条日志会把本用例判红
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[UIManager\] Panel 'ThrowingOnUpdatePanel'"));

            UpdateManager.Tick(0.016f);
            Assert.AreEqual(1, bad.UpdateCount, "前置：坏面板已抛过一次");

            int afterFirstFrame = good.UpdateCount;
            for (int i = 0; i < 3; i++)
                UpdateManager.Tick(0.016f * (i + 2));

            Assert.Greater(good.UpdateCount, afterFirstFrame, "后续帧仍在驱动其余面板——通路没有死");
            Assert.AreEqual(1, bad.UpdateCount, "坏面板已停更：不再被驱动，也就不会每帧刷日志");
        }

        #endregion

        #region 分档（TierDriver）

        /// <summary>
        /// 分档面板抛异常时，同档驱动器同样存活，同档其余面板照常按周期被驱动。
        /// <para>切片路径与每帧档走同一个 <c>DriveTier</c>，但它是<b>另一条</b>驱动器注册——注销同样是
        /// 永久性的，故此用例不能由每帧档的用例代偿。</para>
        /// </summary>
        [Test]
        public async Task TieredBadPanel_DriverSurvives_AndSiblingKeepsTicking()
        {
            var bad = await UIManager.OpenAsync<ThrowingOnUpdatePanel>("ui/bad");
            var good = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/good");
            bad.UpdateTier = UpdateTier.Tier2;
            good.UpdateTier = UpdateTier.Tier2;

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[UIManager\] Panel 'ThrowingOnUpdatePanel'"));

            // 切片档按时间推进：给足跨过 Tier2 周期（2^2 格）的时间，两个桶内下标才都能轮到
            for (int i = 0; i <= 20; i++)
                UpdateManager.Tick(i / 60f);

            Assert.Greater(bad.UpdateCount, 0, "前置：分档路径真的驱动过坏面板");
            Assert.Greater(good.UpdateCount, 0, "同档其余面板照常被驱动");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier2), "分档驱动器同样不得被注销");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), "每帧驱动器常驻，不受牵连");
        }

        #endregion

        #region 门面驱动器兜底

        /// <summary>
        /// 注入实现的 <c>Update</c> 抛异常时，驱动器同样不得被调度器摘掉。
        /// <para>这条走的是门面驱动器那一层兜底：逐面板隔离在 <c>DriveTier</c> 里，管不到注入的
        /// 第三方实现（它可能根本没经过 <c>DriveTier</c>）。异常一旦逃逸，后果与坏面板完全一样——
        /// 驱动器被注销且永不重注册。</para>
        /// </summary>
        [Test]
        public void InjectedManagerThrows_DriverStaysRegisteredAndKeepsDriving()
        {
            // 回到「什么都没注册」，本用例只走注入这条路径
            UIManager.Destroy();

            var fake = new FakeUIManager { ThrowOnUpdate = true };
            UIManager.SetInstance(fake);

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[UIManager\] Frame driver: UIManager\.Update threw"));

            UpdateManager.Tick(0.016f);

            Assert.AreEqual(1, fake.UpdateCount, "前置：注入的实现确实被每帧驱动器转发驱动");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0),
                "注入实现的异常也不得把驱动器从调度器里摘掉");

            // 后续帧照常驱动：异常只吃掉抛出的那一帧，通路没有死
            fake.ThrowOnUpdate = false;
            UpdateManager.Tick(0.032f);

            Assert.AreEqual(2, fake.UpdateCount, "驱动器仍在册，下一帧继续转发");
        }

        #endregion

        #region provider 隔离

        /// <summary>
        /// HUD provider 抛异常时：同帧 Tip 照常被驱动，驱动器仍在册，且日志只记首条（阻尼）。
        /// <para>provider 是公开可替换的面，第三方实现可能抛；两者此前共用一条调用序列，前一个抛就把
        /// 后一个的帧吃掉。provider 没有「停更」机制（停掉它等于停掉全部 HUD / Tip），故改为
        /// 「继续驱动 + 首次记日志」——第二次不再记，只注册过一次 Expect，多出来的日志即未预期。</para>
        /// </summary>
        [Test]
        public void HudProviderThrows_TipStillDrivenSameFrame_AndLogIsDamped()
        {
            var hud = new FaultyHudProvider { ThrowOnUpdate = true };
            var tip = new CountingTipProvider();
            UIManager.SetHudProvider(hud);
            UIManager.SetTipProvider(tip);

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[UIManager\] HUD provider 'FaultyHudProvider'\.Update threw"));

            UpdateManager.Tick(0.016f);

            Assert.AreEqual(1, hud.UpdateCount, "前置：HUD provider 确实被驱动过");
            Assert.AreEqual(1, tip.UpdateCount, "一个 provider 抛异常不该吃掉同帧另一个 provider 的更新");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), "驱动器仍在册");

            UpdateManager.Tick(0.032f);

            Assert.AreEqual(2, hud.UpdateCount, "provider 没有停更机制，下一帧继续被驱动");
            Assert.AreEqual(2, tip.UpdateCount, "同帧 Tip 照常推进");
        }

        #endregion

        #region 停更标记的复位

        /// <summary>
        /// 坏面板关闭（回池）后重新打开，应恢复被驱动——停更标记随回池复位。
        /// <para>若标记不复位，池化实例会变成「开着但永不更新」的僵尸：比每帧抛异常更难查。
        /// 前置断言锁住「假工厂确实复用了同一实例」，否则本用例证明不了复位这件事。</para>
        /// </summary>
        [Test]
        public async Task FaultedPanel_ReopenedAfterRecycle_IsDrivenAgain()
        {
            var bad = await UIManager.OpenAsync<ThrowingOnUpdatePanel>("ui/bad");

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[UIManager\] Panel 'ThrowingOnUpdatePanel'"));

            UpdateManager.Tick(0.016f);
            Assert.AreEqual(1, bad.UpdateCount, "前置：坏面板已抛过一次并停更");

            await UIManager.CloseAsync<ThrowingOnUpdatePanel>();
            bad.ShouldThrow = false;

            var reopened = await UIManager.OpenAsync<ThrowingOnUpdatePanel>("ui/bad");
            Assert.IsTrue(ReferenceEquals(reopened, bad),
                "前置：假工厂按地址复用实例——不复用的话本用例证明不了「池化实例的停更标记被复位」");

            UpdateManager.Tick(0.032f);

            Assert.AreEqual(2, bad.UpdateCount, "回池复位后重新打开应恢复被驱动");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0));
        }

        #endregion
    }
}
