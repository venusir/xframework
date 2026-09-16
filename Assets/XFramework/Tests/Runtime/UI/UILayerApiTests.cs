using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 层级 API 测试。
    /// <para>回归一：<c>SetLayerVisibility</c> / <c>SetLayerInteractive</c> 只在 <see cref="IUIManager"/> 上，
    /// 而门面既没有转发、也没有实例属性——第三方实际上根本调不到。</para>
    /// <para>回归二：层的「整体禁交互」会被任何一次焦点变化撤销。因为 <c>OnFocus</c> 无条件把 raycaster
    /// 打开，它并不知道层被整体禁用过。</para>
    /// </summary>
    [TestFixture]
    public class UILayerApiTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_LayerTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
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

        #region 门面可达性

        [Test]
        public void Facade_ExposesLayerApiAndRoot()
        {
            Assert.AreSame(_root.transform, UIManager.UIRoot, "UIRoot 应可直接读到");

            UIManager.Layer.SetInteractive(UILayers.Default, true);
            UIManager.Layer.SetVisibility(UILayers.Default, true);
        }

        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            var property = typeof(UIManager).GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);

            // 这不是洁癖：一旦暴露实例属性，可见面就从「门面显式转发的那部分」扩大到
            // IUIManager 的全部——后续给它加成员会自动成为公开 API，跳过评审
            Assert.IsNull(property,
                "门面刻意不暴露实例属性；框架内与测试用 internal 的 UIManager.Current");
        }

        #endregion

        #region 层交互开关

        [Test]
        public async Task SetLayerInteractive_TogglesOpenPanelRaycaster()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            Assert.IsTrue(panel.Raycaster.enabled, "前置条件：刚打开时可交互");

            UIManager.Layer.SetInteractive(UILayers.Default, false);
            Assert.IsFalse(panel.Raycaster.enabled, "整层禁用交互应关掉已打开面板的射线");

            UIManager.Layer.SetInteractive(UILayers.Default, true);
            Assert.IsTrue(panel.Raycaster.enabled);
        }

        [Test]
        public async Task SetLayerInteractiveFalse_NotUndoneByFocusChange()
        {
            var covered = await UIManager.Panel.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            await UIManager.Stack.PushAsync<FakePanelB>("ui/b", UILayers.Default);

            UIManager.Layer.SetInteractive(UILayers.Default, false);

            // Pop 会让第一个面板重新获得焦点，OnFocus 会无条件打开 raycaster
            await UIManager.Stack.PopAsync();

            Assert.IsTrue(covered.IsOpen);
            Assert.IsFalse(covered.Raycaster.enabled,
                "层的整体禁交互优先于焦点恢复——否则任何一次焦点变化都会把它撤销");
        }

        [Test]
        public async Task SetLayerInteractiveFalse_NotUndoneByOpen()
        {
            UIManager.Layer.SetInteractive(UILayers.Default, false);

            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/a", UILayers.Default);

            Assert.IsFalse(panel.Raycaster.enabled,
                "在已禁用交互的层上新开面板，也不该绕过层的开关");
        }

        [Test]
        public async Task SetLayerInteractive_IsPerLayer()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/a", UILayers.Default);

            UIManager.Layer.SetInteractive(UILayers.Popup, false);

            Assert.IsTrue(panel.Raycaster.enabled, "禁用的是别的层，本层不受影响");
        }

        [Test]
        public async Task SetLayerInteractive_AppliesToPanelsOpenedLater()
        {
            UIManager.Layer.SetInteractive(UILayers.Default, false);

            var first = await UIManager.Panel.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            await UIManager.Panel.CloseAsync<FakePanel>();
            var second = await UIManager.Panel.OpenAsync<FakePanel>("ui/a", UILayers.Default);

            Assert.IsFalse(first.Raycaster.enabled);
            Assert.AreSame(first, second, "复用池中实例");
            Assert.IsFalse(second.Raycaster.enabled,
                "回池再取出也不该丢掉层的开关");
        }

        #endregion

        #region 层显隐

        [Test]
        public async Task SetLayerVisibility_TogglesContainer()
        {
            await UIManager.Panel.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            var container = _root.transform.Find($"Layer_{UILayers.Default}");
            Assert.IsNotNull(container, "首次打开时应创建层级容器");

            UIManager.Layer.SetVisibility(UILayers.Default, false);
            Assert.IsFalse(container.gameObject.activeSelf);

            UIManager.Layer.SetVisibility(UILayers.Default, true);
            Assert.IsTrue(container.gameObject.activeSelf);
        }

        #endregion
    }
}
