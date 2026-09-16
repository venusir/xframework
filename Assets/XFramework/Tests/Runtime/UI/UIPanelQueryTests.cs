using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 面板查询 API 测试。
    /// <para>此前查询只能逐类型试 <c>IsOpen&lt;T&gt;()</c>——「现在开着哪些面板」「哪个在最前面」
    /// 这类问题无从回答，除非调用方自己维护一份账。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelQueryTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_QueryTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();
            _factory.RegisterPanel<FakePanelC>();

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

        #region 计数与栈顶

        [Test]
        public async Task Counts_TrackOpenAndClose()
        {
            Assert.AreEqual(0, UIManager.Panel.OpenCount);
            Assert.IsFalse(UIManager.Panel.IsAnyOpen);
            Assert.IsNull(UIManager.Panel.GetTopPanel(), "无面板时栈顶为 null");

            await UIManager.Panel.OpenAsync<FakePanel>("ui/a");
            await UIManager.Stack.PushAsync<FakePanelB>("ui/b");

            Assert.AreEqual(2, UIManager.Panel.OpenCount);
            Assert.IsTrue(UIManager.Panel.IsAnyOpen);

            await UIManager.Panel.CloseAsync<FakePanelB>();
            Assert.AreEqual(1, UIManager.Panel.OpenCount);

            await UIManager.Panel.CloseAsync<FakePanel>();
            Assert.AreEqual(0, UIManager.Panel.OpenCount);
            Assert.IsNull(UIManager.Panel.GetTopPanel());
        }

        [Test]
        public async Task GetTopPanel_FollowsDisplayStack()
        {
            var a = await UIManager.Panel.OpenAsync<FakePanel>("ui/a");
            Assert.AreSame(a, UIManager.Panel.GetTopPanel());

            var b = await UIManager.Stack.PushAsync<FakePanelB>("ui/b");
            Assert.AreSame(b, UIManager.Panel.GetTopPanel(), "后打开的在最前");

            await UIManager.Stack.PopAsync();
            Assert.AreSame(a, UIManager.Panel.GetTopPanel(), "弹出后回到前一个");
        }

        #endregion

        #region 缓冲区填充

        [Test]
        public async Task CopyPanels_ReturnsDisplayOrderBottomToTop()
        {
            var a = await UIManager.Panel.OpenAsync<FakePanel>("ui/a");
            var b = await UIManager.Stack.PushAsync<FakePanelB>("ui/b");
            var c = await UIManager.Stack.PushAsync<FakePanelC>("ui/c");

            var buffer = new List<UIPanelBase>();
            int count = UIManager.Panel.CopyPanels(buffer);

            Assert.AreEqual(3, count);
            Assert.AreSame(a, buffer[0], "底 → 顶");
            Assert.AreSame(b, buffer[1]);
            Assert.AreSame(c, buffer[2]);
        }

        [Test]
        public async Task CopyPanels_ClearsBufferFirst()
        {
            var buffer = new List<UIPanelBase> { null, null, null, null, null };
            await UIManager.Panel.OpenAsync<FakePanel>("ui/a");

            int count = UIManager.Panel.CopyPanels(buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, buffer.Count, "缓冲区应先被清空，而不是追加");
        }

        [Test]
        public async Task CopyPanelsInLayer_FiltersByLayer()
        {
            var top = await UIManager.Panel.OpenAsync<FakePanel>("ui/top", UILayers.Top);
            var mid = await UIManager.Panel.OpenAsync<FakePanelB>("ui/mid", UILayers.Default);
            var bottom = await UIManager.Panel.OpenAsync<FakePanelC>("ui/bottom", UILayers.Background);

            var buffer = new List<UIPanelBase>();

            Assert.AreEqual(1, UIManager.Panel.CopyPanelsInLayer(UILayers.Default, buffer));
            Assert.AreSame(mid, buffer[0]);

            Assert.AreEqual(1, UIManager.Panel.CopyPanelsInLayer(UILayers.Top, buffer));
            Assert.AreSame(top, buffer[0]);

            Assert.AreEqual(1, UIManager.Panel.CopyPanelsInLayer(UILayers.Background, buffer));
            Assert.AreSame(bottom, buffer[0]);

            Assert.AreEqual(0, UIManager.Panel.CopyPanelsInLayer(UILayers.Popup, buffer));
        }

        [Test]
        public void CopyPanels_NullBuffer_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => UIManager.Panel.CopyPanels(null));
        }

        #endregion

        #region 零分配

        [Test]
        public async Task CopyPanels_ReusesCallerBuffer_WithoutAllocating()
        {
            for (int i = 0; i < 3; i++)
                await UIManager.Panel.OpenAsync<FakePanel>($"ui/p{i}");

            var buffer = new List<UIPanelBase>(8);
            for (int i = 0; i < 4; i++)
                UIManager.Panel.CopyPanels(buffer);   // 预热：容量与内部状态就位

            long before = System.GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 100; i++)
                UIManager.Panel.CopyPanels(buffer);

            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated,
                "CopyPanels 是零分配主入口——由调用方持有缓冲区，逐帧查询也不该产生 GC");
        }

        [Test]
        public async Task Panels_IsLiveView_NotACopy()
        {
            await UIManager.Panel.OpenAsync<FakePanel>("ui/a");

            var view = UIManager.Panel.Panels;
            Assert.AreEqual(1, view.Count);

            await UIManager.Stack.PushAsync<FakePanelB>("ui/b");

            Assert.AreEqual(2, view.Count,
                "Panels 是活视图，面板开合后随之变化——不要跨帧缓存它");
            Assert.AreSame(UIManager.Panel.GetTopPanel(), view[1]);
        }

        #endregion
    }
}
