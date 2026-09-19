using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 面板级档位与失焦剔除测试。
    /// <para>面板可声明较低的更新档位（用不到每帧的倒计时、进度插值等），按 2^k 个节拍格的周期
    /// 被派发；被别的面板盖住而失焦的面板则完全不派发。</para>
    /// <para><c>IsPaused</c> 此前是个无人消费的死字段——<c>OnBlur</c> 置位、从无读者，
    /// 本组用例是它第一次真正生效。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelTierTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_TierTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<UpdateRecordingPanel>();
            _factory.RegisterPanel<FakePanelB>();

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

        #region 驱动器懒注册

        [Test]
        public void Initialize_RegistersOnlyFrameDriver()
        {
            Assert.AreEqual(1, UpdateManager.TotalCount,
                "还没有面板时不注册任何档位驱动器——用不到的档位不该占调度器条目");
        }

        [Test]
        public async Task OpenTier2Panel_RegistersTier2Driver()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            panel.UpdateTier = UpdateTier.Tier2;

            UpdateManager.Tick(0.016f);

            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), "每帧驱动器常驻（还承载 HUD）");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier2), "出现该档面板后应注册对应驱动器");
            Assert.AreEqual(2, UpdateManager.TotalCount);
        }

        [Test]
        public async Task CloseTier2Panel_UnregistersTier2Driver()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            panel.UpdateTier = UpdateTier.Tier2;
            UpdateManager.Tick(0.016f);
            Assert.AreEqual(2, UpdateManager.TotalCount, "前置条件：该档驱动器已注册");

            await UIManager.CloseAsync<UpdateRecordingPanel>();
            UpdateManager.Tick(0.032f);

            Assert.AreEqual(0, UpdateManager.GetCount(UpdateTier.Tier2), "该档没面板后应注销");
            Assert.AreEqual(1, UpdateManager.TotalCount, "不应留下悬挂驱动器");
        }

        /// <summary>
        /// 换实例时要把旧实例收干净：它的分档驱动器必须注销，门面自己创建的那个还要被销毁。
        /// <para>分档驱动器各自直接持有旧 <c>UIManagerImpl</c> 引用，而注册/注销只由档位需求变化驱动
        /// ——换实例后没人再上报需求，它们会永远留在调度器里，每个周期驱动一次那个已经没人认领的
        /// 管理器（它的面板谁也看不见，却照跑）。旧实例的语言订阅同样没人退。</para>
        /// </summary>
        [Test]
        public async Task SetInstance_ReleasesPreviousInstanceAndItsTierDrivers()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            panel.UpdateTier = UpdateTier.Tier2;
            UpdateManager.Tick(0.016f);

            Assert.AreEqual(1, UIManager.TierDriverCount, "前置：该档驱动器已注册");
            Assert.AreEqual(0, _factory.ReleaseCount, "前置：面板还开着");

            UIManager.SetInstance(new UIManagerImpl());

            Assert.AreEqual(0, UIManager.TierDriverCount, "旧实例的分档驱动器应被注销");
            Assert.AreEqual(0, UpdateManager.GetCount(UpdateTier.Tier2),
                "调度器里不该留下仍指向旧实例的驱动器");
            Assert.AreEqual(1, _factory.ReleaseCount,
                "门面自己创建的旧实例应被销毁——它开着的面板要经工厂回池");
        }

        [Test]
        public async Task RuntimeTierChange_BackToTier0_UnregistersSlicedDriver()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            panel.UpdateTier = UpdateTier.Tier3;
            UpdateManager.Tick(0.016f);
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier3), "前置条件：Tier3 驱动器已注册");

            panel.UpdateTier = UpdateTier.Tier0;
            UpdateManager.Tick(0.032f);

            Assert.AreEqual(0, UpdateManager.GetCount(UpdateTier.Tier3), "改回每帧档后应注销");
            Assert.AreEqual(1, UpdateManager.TotalCount);
        }

        #endregion

        #region 降频派发

        [Test]
        public async Task Tier2Panel_DrivenLessOften_WithAccumulatedDelta()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            panel.UpdateTier = UpdateTier.Tier2;

            const int steps = 20;
            for (int i = 0; i <= steps; i++)
                UpdateManager.Tick(i / 60f);

            Assert.Greater(panel.UpdateCount, 0, "降频不等于不派发");
            Assert.Less(panel.UpdateCount, steps, "Tier2 按 2^2 个节拍格的周期派发，不该每帧都被调用");
            Assert.Greater(panel.LastDeltaTime, 1f / 60f * 1.5f,
                "降频派发时的 deltaTime 是累计间隔，不是单帧的 Time.deltaTime——面板积分必须用它");
        }

        #endregion

        #region 失焦剔除

        [Test]
        public async Task BlurredPanel_NotDriven()
        {
            var covered = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            UpdateManager.Tick(0.016f);

            int before = covered.UpdateCount;
            Assert.Greater(before, 0, "前置条件：A 正在被驱动");

            await UIManager.PushAsync<FakePanelB>("ui/b");
            Assert.IsTrue(covered.IsPaused, "Push 覆盖后 A 应失焦");

            for (int i = 0; i < 5; i++)
                UpdateManager.Tick(0.016f * (i + 2));

            Assert.AreEqual(before, covered.UpdateCount,
                "失焦面板被盖住、看不见也没交互，不该再被派发");
        }

        [Test]
        public async Task PanelRegainsFocus_IsDrivenAgain()
        {
            var covered = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");
            UpdateManager.Tick(0.016f);

            int whileCovered = covered.UpdateCount;

            await UIManager.PopAsync();
            UpdateManager.Tick(0.032f);

            Assert.IsFalse(covered.IsPaused, "Pop 后 A 应恢复焦点");
            Assert.Greater(covered.UpdateCount, whileCovered, "恢复焦点后应重新被派发");
        }

        #endregion
    }
}
