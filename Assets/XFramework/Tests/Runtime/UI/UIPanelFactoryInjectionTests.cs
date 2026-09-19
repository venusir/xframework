using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 可注入面板实例来源的冒烟测试。
    /// <para>此前 Runtime 测试无法打开真实面板——<c>AssetManager.ImplFactory</c> 只对 Editor 测试开放，
    /// 而面板实例化写死走 <c>AssetManager.InstantiateAsync</c>。抽出 <see cref="IUIPanelFactory"/> 后，
    /// 每个行为变更都能在无 YooAsset 的环境下立刻带上测试。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelFactoryInjectionTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_FactoryTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            // 钩子有两层兜底：Initialize 消费即清，Destroy 再次复位（见下面两条用例）
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        [Test]
        public async Task OpenAsync_UsesInjectedFactory_AndRegistersPanel()
        {
            var panel = await UIManager.OpenAsync<FakePanel>("ui/fake", layer: 100, userData: "hi");

            Assert.IsNotNull(panel, "注入假工厂后应能打开面板");
            Assert.AreEqual(1, _factory.CreateCount, "面板实例应由注入的工厂创建");
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "打开后面板应处于活动状态");
            Assert.AreSame(panel, UIManager.GetPanel<FakePanel>());

            Assert.AreEqual("hi", panel.LastUserData, "userData 应透传到 OnOpen");
            Assert.AreEqual(100, panel.Layer);
            Assert.AreEqual("ui/fake", panel.AssetPath);

            Assert.AreEqual("Layer_100", panel.transform.parent.name, "面板应挂在该层级的容器下");
        }

        [Test]
        public async Task CloseAsync_ReleasesThroughFactory()
        {
            await UIManager.OpenAsync<FakePanel>("ui/fake");
            await UIManager.CloseAsync<FakePanel>();

            Assert.AreEqual(1, _factory.ReleaseCount, "关闭应经工厂回池，而非直接销毁");
            Assert.IsFalse(UIManager.IsOpen<FakePanel>());
            Assert.AreEqual(1, _factory.PooledCount, "回池的面板应留在池中");
        }

        [Test]
        public async Task Destroy_ReleasesOpenPanelsThroughFactory()
        {
            await UIManager.OpenAsync<FakePanel>("ui/fake");

            UIManager.Destroy();

            Assert.AreEqual(1, _factory.ReleaseCount, "销毁管理器应把活动面板经工厂回池");
        }

        /// <summary>
        /// 钩子是一次性注入点：<see cref="UIManager.Initialize"/> 取走它时就该顺手清掉。
        /// </summary>
        [Test]
        public void Initialize_ConsumesFactoryHook()
        {
            // SetUp 里设过钩子并随即 Initialize，故此刻它应当已经空了。
            // 留着它的话，一次没走 Destroy 的 fixture 会把假工厂留给下一个 fixture 的 Initialize——
            // PlayMode 下所有用例共享一个 player 实例，这种泄漏不报错，只让下一个 fixture 用着别人的工厂。
            Assert.IsNull(UIManager.PanelFactoryFactory,
                "Initialize 应当消费掉钩子，而不是把它留到下一次 Initialize");
        }

        /// <summary>
        /// Destroy 自己的那层兜底。
        /// <para>Initialize 消费之后，「Destroy 也复位钩子」在正常序列里恒真、再也测不出来，
        /// 故这里刻意<b>不</b> Initialize，直接验 Destroy。</para>
        /// </summary>
        [Test]
        public void Destroy_WithoutInitialize_StillResetsFactoryHook()
        {
            UIManager.PanelFactoryFactory = () => _factory;

            UIManager.Destroy();

            Assert.IsNull(UIManager.PanelFactoryFactory, "销毁应复位测试钩子，避免污染后续 fixture");
        }

        /// <summary>
        /// Dispose 中途抛异常时，门面仍须完成状态复位。
        /// <para>驱动器在这一步之前就已全部摘掉，若此时 <c>IsInitialized</c> 还报 true，门面就停在
        /// 「声称没事、实际没有任何人来驱动」的半死状态——而且没有任何后续调用会把它拉回来。</para>
        /// </summary>
        [Test]
        public async Task Destroy_WhenDisposeThrows_StillResetsState()
        {
            await UIManager.OpenAsync<FakePanel>("ui/fake");

            _factory.ThrowOnRelease = true;   // 让回池这一步失败，模拟 Dispose 中途抛异常

            Assert.Throws<InvalidOperationException>(() => UIManager.Destroy());

            Assert.IsFalse(UIManager.IsInitialized,
                "Dispose 抛异常也必须复位状态，否则门面停在半死状态");
            Assert.AreEqual(0, UpdateManager.TotalCount, "驱动器不该留下悬挂回调");
            Assert.IsNull(UIManager.PanelFactoryFactory, "测试钩子同样要复位");
        }
    }
}
