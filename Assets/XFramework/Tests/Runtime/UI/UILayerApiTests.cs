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

            UIManager.SetLayerInteractive(UILayers.Default, true);
            UIManager.SetLayerVisibility(UILayers.Default, true);
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
            var panel = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            Assert.IsTrue(panel.Raycaster.enabled, "前置条件：刚打开时可交互");

            UIManager.SetLayerInteractive(UILayers.Default, false);
            Assert.IsFalse(panel.Raycaster.enabled, "整层禁用交互应关掉已打开面板的射线");

            UIManager.SetLayerInteractive(UILayers.Default, true);
            Assert.IsTrue(panel.Raycaster.enabled);
        }

        [Test]
        public async Task SetLayerInteractiveFalse_NotUndoneByFocusChange()
        {
            var covered = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            await UIManager.PushAsync<FakePanelB>("ui/b", UILayers.Default);

            UIManager.SetLayerInteractive(UILayers.Default, false);

            // Pop 会让第一个面板重新获得焦点，OnFocus 会无条件打开 raycaster
            await UIManager.PopAsync();

            Assert.IsTrue(covered.IsOpen);
            Assert.IsFalse(covered.Raycaster.enabled,
                "层的整体禁交互优先于焦点恢复——否则任何一次焦点变化都会把它撤销");
        }

        [Test]
        public async Task SetLayerInteractiveFalse_NotUndoneByOpen()
        {
            UIManager.SetLayerInteractive(UILayers.Default, false);

            var panel = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);

            Assert.IsFalse(panel.Raycaster.enabled,
                "在已禁用交互的层上新开面板，也不该绕过层的开关");
        }

        [Test]
        public async Task SetLayerInteractive_IsPerLayer()
        {
            var panel = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);

            UIManager.SetLayerInteractive(UILayers.Popup, false);

            Assert.IsTrue(panel.Raycaster.enabled, "禁用的是别的层，本层不受影响");
        }

        [Test]
        public async Task SetLayerInteractive_AppliesToPanelsOpenedLater()
        {
            UIManager.SetLayerInteractive(UILayers.Default, false);

            var first = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            await UIManager.CloseAsync<FakePanel>();
            var second = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);

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
            await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            var container = _root.transform.Find($"Layer_{UILayers.Default}");
            Assert.IsNotNull(container, "首次打开时应创建层级容器");

            UIManager.SetLayerVisibility(UILayers.Default, false);
            Assert.IsFalse(container.gameObject.activeSelf);

            UIManager.SetLayerVisibility(UILayers.Default, true);
            Assert.IsTrue(container.gameObject.activeSelf);
        }

        /// <summary>
        /// 层容器还没建出来时隐藏该层，隐藏必须留到容器建好之后才生效。
        /// <para>层容器是「该层第一次开面板」时才创建的。此前 <c>SetLayerVisibility</c> 只对已存在的
        /// 容器生效、也不记期望值，于是「先隐藏、后开面板」的序列会让隐藏被悄悄撤销——层又显示出来了。</para>
        /// </summary>
        [Test]
        public async Task SetLayerVisibility_BeforeContainerExists_AppliesWhenCreated()
        {
            UIManager.SetLayerVisibility(UILayers.Popup, false);
            Assert.IsNull(_root.transform.Find($"Layer_{UILayers.Popup}"),
                "前置：该层还没有容器");

            await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Popup);

            var container = _root.transform.Find($"Layer_{UILayers.Popup}");
            Assert.IsNotNull(container, "首次打开时应创建层级容器");
            Assert.IsFalse(container.gameObject.activeSelf,
                "先隐藏、后开面板：隐藏不该被新建容器撤销");
        }

        /// <summary>
        /// 恢复整层交互时，不该把被覆盖（失焦）面板的射线一并点亮。
        /// <para>层开关的语义是「允许这一层交互」，不是「让这一层里每个面板都可交互」——后者会让失焦
        /// 面板隔着上层弹窗吃点击，等于一次跨过焦点的越权。</para>
        /// </summary>
        [Test]
        public async Task SetLayerInteractive_Restore_KeepsCoveredPanelNonInteractive()
        {
            var covered = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            var top = await UIManager.PushAsync<FakePanelB>("ui/b", UILayers.Default);

            Assert.IsTrue(top.IsFocused, "前置：后开的是栈顶，有焦点");
            Assert.IsFalse(covered.IsFocused, "前置：先开的已被覆盖，失焦");

            UIManager.SetLayerInteractive(UILayers.Default, false);
            UIManager.SetLayerInteractive(UILayers.Default, true);

            Assert.IsTrue(top.Raycaster.enabled, "有焦点的面板应随层恢复交互");
            Assert.IsFalse(covered.Raycaster.enabled,
                "失焦面板不该被层开关顺手点亮——它正被上层盖着");
        }

        #endregion
    }
}
