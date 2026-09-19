using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 状态快照与导出测试。
    /// <para>用于回答「面板是不是漏关了」「遮罩为什么还亮着」「有几个打开卡在半路」这类
    /// 只能靠翻运行时状态定位的问题。</para>
    /// </summary>
    [TestFixture]
    public class UIDiagnosticsTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_DiagTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Gate = null;
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #region 快照

        [Test]
        public void GetState_ReflectsCounters()
        {
            var empty = UIManager.Diagnostic.GetState();
            Assert.AreEqual(0, empty.OpenCount);
            Assert.AreEqual(0, empty.MaskRefCount);
            Assert.IsFalse(empty.IsMaskShowing);
            Assert.IsFalse(empty.CanGoBack);
        }

        [Test]
        public async Task GetState_TracksPanelsAndMask()
        {
            await UIManager.Panel.OpenAsync<FakePanel>("ui/a");
            await UIManager.Stack.PushAsync<FakePanelB>("ui/b");
            var mask = UIManager.Mask.Show();

            var state = UIManager.Diagnostic.GetState();

            Assert.AreEqual(2, state.OpenCount);
            Assert.AreEqual(1, state.MaskRefCount, "遮罩被持有一份");
            Assert.IsTrue(state.IsMaskShowing);
            Assert.IsTrue(state.CanGoBack);

            var second = UIManager.Mask.Show();
            Assert.AreEqual(2, UIManager.Diagnostic.GetState().MaskRefCount, "第二份持有应计入");

            mask.Dispose();
            second.Dispose();
            Assert.AreEqual(0, UIManager.Diagnostic.GetState().MaskRefCount);
            Assert.IsFalse(UIManager.Diagnostic.GetState().IsMaskShowing, "引用归零后遮罩应隐藏");
        }

        [Test]
        public async Task GetState_CountsInFlightOpens()
        {
            _factory.Gate = new UniTaskCompletionSource<object>();

            var pending = UIManager.Panel.OpenAsync<FakePanel>("ui/slow").Preserve();
            await UniTask.Yield();

            Assert.AreEqual(1, UIManager.Diagnostic.GetState().InFlightOpenCount,
                "卡在半路的打开应能被看见——这正是它存在的意义");

            _factory.Gate.TrySetResult(null);
            await pending;

            Assert.AreEqual(0, UIManager.Diagnostic.GetState().InFlightOpenCount, "完成后应清零");
        }

        [Test]
        public async Task GetState_DoesNotAllocate()
        {
            await UIManager.Panel.OpenAsync<FakePanel>("ui/a");

            for (int i = 0; i < 4; i++)
                UIManager.Diagnostic.GetState();   // 预热

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                UIManager.Diagnostic.GetState();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated,
                "快照是纯值类型，调试面板以 2Hz 刷新也不该产生 GC");
        }

        #endregion

        #region 导出

        [Test]
        public async Task DumpState_ContainsPerPanelDetails()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanelB>("ui/a", UILayers.Popup);
            panel.UpdateTier = UpdateTier.Tier2;

            string dump = UIManager.Diagnostic.DumpState();

            StringAssert.Contains("FakePanelB", dump, "应列出面板类型");
            StringAssert.Contains($"layer={UILayers.Popup}", dump);
            StringAssert.Contains("tier=Tier2", dump, "应列出档位——排查降频问题要用");
            StringAssert.Contains("focused", dump);
        }

        [Test]
        public void DumpState_EmptyState_IsReadable()
        {
            string dump = UIManager.Diagnostic.DumpState();

            StringAssert.Contains("open=0", dump);
            StringAssert.Contains("(none)", dump);
        }

        [Test]
        public async Task DumpState_ShowsBlurredAndPaused()
        {
            await UIManager.Panel.OpenAsync<FakePanel>("ui/a");
            await UIManager.Stack.PushAsync<FakePanelB>("ui/b");

            string dump = UIManager.Diagnostic.DumpState();

            StringAssert.Contains("blurred", dump, "被覆盖的面板应标出失焦");
            StringAssert.Contains("paused", dump);
        }

        #endregion
    }
}
