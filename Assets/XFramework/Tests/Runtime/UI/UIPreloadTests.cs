using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 预加载记账的语义测试。
    /// <para>这组 API 原先名为 <c>UnloadAsset</c> / <c>ClearAssetCache</c>，文档声称「释放内存」，
    /// 实际只清一个记账字典、不碰资源。现已正名为记账语义，并把真实作用写进 XML doc。</para>
    /// <para><b>未覆盖</b>：<c>PreloadAsync</c> 自身——它经 <c>AssetManager.PreloadAllAsync</c> 走真实
    /// 资源系统，测试环境没有 YooAsset。此处覆盖的是记账侧的改名与容错。</para>
    /// </summary>
    [TestFixture]
    public class UIPreloadTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_PreloadTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();

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

        [Test]
        public void ForgetPreload_NothingTracked_IsNoOp()
        {
            Assert.DoesNotThrow(() => UIManager.ForgetPreload<FakePanel>());
        }

        [Test]
        public void ClearPreloads_NothingTracked_IsNoOp()
        {
            Assert.DoesNotThrow(() => UIManager.ClearPreloads());
        }

        [Test]
        public async Task AccountingCalls_DoNotDisturbOpenPanels()
        {
            var panel = await UIManager.OpenAsync<FakePanel>("ui/a");

            UIManager.ForgetPreload<FakePanel>();
            UIManager.ClearPreloads();

            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "记账与面板实例池是两回事，清记账不该动到打开的面板");
            Assert.AreSame(panel, UIManager.GetPanel<FakePanel>());
            Assert.AreEqual(1, UIManager.OpenCount);
        }
    }
}
