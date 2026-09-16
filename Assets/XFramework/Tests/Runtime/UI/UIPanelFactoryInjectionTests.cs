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
            // Destroy 会一并复位 PanelFactoryFactory（见 UIManager.Destroy）
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                Object.DestroyImmediate(_root);

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
            Assert.AreEqual(1, _factory.Pool.Count, "回池的面板应留在池中");
        }

        [Test]
        public async Task Destroy_ReleasesOpenPanelsThroughFactory()
        {
            await UIManager.OpenAsync<FakePanel>("ui/fake");

            UIManager.Destroy();

            Assert.AreEqual(1, _factory.ReleaseCount, "销毁管理器应把活动面板经工厂回池");
            Assert.IsNull(UIManager.PanelFactoryFactory, "销毁应复位测试钩子，避免污染后续 fixture");
        }
    }
}
