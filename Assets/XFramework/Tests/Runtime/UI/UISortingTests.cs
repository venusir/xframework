using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 排序空间与按栈位重排的测试。
    /// <para>回归：面板排序原先由无限递增的计数器给出，同一层第 1000 次打开后会溢进下一层的区间；
    /// 且 <c>GetTopSortingOrder</c> 与 <c>GetNextSortingOrder</c> 两处口径不一致（前者算错了层偏移）。
    /// 现改为按显示栈次序重排，序号即栈内相对位置，与栈是同一份真相。</para>
    /// </summary>
    [TestFixture]
    public class UISortingTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_SortingTest", typeof(RectTransform));

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

        #region 保留带次序

        [Test]
        public void ReservedBands_AreOrderedAndNonOverlapping()
        {
            int maxPanelOrder = UISorting.PanelOrder(UISorting.MaxPanelLayer, UISorting.MaxIndexInLayer);

            Assert.Greater(UISorting.HudOrder, maxPanelOrder,
                "HUD 带必须高于全部面板层，否则会被普通面板盖住");
            Assert.Greater(UISorting.TipOrder, UISorting.HudOrder, "Tip 必须高于 HUD");
            Assert.Greater(UISorting.SystemOrder, UISorting.TipOrder, "系统带留在最上");

            Assert.Greater(UISorting.MaskOrder(UILayers.Mask),
                UISorting.PanelOrder(UILayers.Mask, UISorting.MaxIndexInLayer - 1),
                "遮罩取层内末位，应挡住同层的普通面板");
            Assert.Less(UISorting.MaskOrder(UILayers.Mask), UISorting.PanelOrder(UILayers.Mask + 1, 1),
                "遮罩应被更高层盖住");
        }

        [Test]
        public void EveryBandValue_SurvivesCanvasRoundTrip()
        {
            // 这是本组最重要的一条：Canvas.sortingOrder 运行时是 16 位有符号量，
            // 超出 [-32768, 32767] 的写入会被静默截断回绕。既有的 layer*1000 方案
            // 在层号 ≥ 33 时全部越界（层 300 实存 -27679、层 200 实存 +3393，
            // 于是 Top 层反而渲染在 Popup 之下），而此前没有任何用例断言过 sortingOrder。
            var go = new GameObject("SortingProbe", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;

            try
            {
                AssertRoundTrip(canvas, UISorting.PanelOrder(0, 0), "第 0 层容器");
                AssertRoundTrip(canvas, UISorting.PanelOrder(UISorting.MaxPanelLayer, UISorting.MaxIndexInLayer),
                    "面板层上限");
                AssertRoundTrip(canvas, UISorting.MaskOrder(UILayers.Mask), "遮罩");
                AssertRoundTrip(canvas, UISorting.HudOrder, "HUD");
                AssertRoundTrip(canvas, UISorting.TipOrder, "Tip");
                AssertRoundTrip(canvas, UISorting.SystemOrder, "系统保留带");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void AssertRoundTrip(Canvas canvas, int value, string what)
        {
            canvas.sortingOrder = value;

            Assert.AreEqual(value, canvas.sortingOrder,
                $"{what} 的排序值 {value} 无法原样写入 Canvas：sortingOrder 是 16 位有符号量，" +
                $"超出 [{UISorting.MinSortingOrder}, {UISorting.MaxSortingOrder}] 会被截断回绕且不报错");
        }

        #endregion

        #region 按栈位排序

        [Test]
        public async Task SameLayer_OrderFollowsOpenSequence()
        {
            var a = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            var b = await UIManager.OpenAsync<FakePanelB>("ui/b", UILayers.Default);

            Assert.AreEqual(UISorting.PanelOrder(UILayers.Default, 1), a.Canvas.sortingOrder);
            Assert.AreEqual(UISorting.PanelOrder(UILayers.Default, 2), b.Canvas.sortingOrder,
                "后打开的排在更前");
            Assert.IsTrue(b.Canvas.overrideSorting);
        }

        [Test]
        public async Task DifferentLayers_AreIndependentOfOpenSequence()
        {
            await UIManager.OpenAsync<FakePanel>("ui/top", UILayers.Top);
            var lower = await UIManager.OpenAsync<FakePanelB>("ui/default", UILayers.Default);
            var top = UIManager.GetPanel<FakePanel>();

            Assert.Greater(top.Canvas.sortingOrder, lower.Canvas.sortingOrder,
                "层级优先于打开顺序：后打开的底层面板仍应排在高层之下");
        }

        [Test]
        public async Task CloseAndReopen_DoesNotDrift()
        {
            var a = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            var b = await UIManager.OpenAsync<FakePanelB>("ui/b", UILayers.Default);

            await UIManager.CloseAsync<FakePanelB>();
            var c = await UIManager.OpenAsync<FakePanelC>("ui/c", UILayers.Default);

            Assert.AreEqual(UISorting.PanelOrder(UILayers.Default, 1), a.Canvas.sortingOrder,
                "A 仍是层内第一个");
            Assert.AreEqual(UISorting.PanelOrder(UILayers.Default, 2), c.Canvas.sortingOrder,
                "序号按栈位重算，不随历史打开次数增长——计数器方案会在这里漂移");
            Assert.AreNotEqual(b.Canvas.sortingOrder, c.Canvas.sortingOrder);
        }

        [Test]
        public async Task BringToFront_RestacksAndReorders()
        {
            var a = await UIManager.OpenAsync<FakePanel>("ui/a", UILayers.Default);
            var b = await UIManager.OpenAsync<FakePanelB>("ui/b", UILayers.Default);

            UIManager.BringToFront(a);

            Assert.AreEqual(UISorting.PanelOrder(UILayers.Default, 2), a.Canvas.sortingOrder,
                "被提到最前的面板应拿到层内最大序号");
            Assert.AreEqual(UISorting.PanelOrder(UILayers.Default, 1), b.Canvas.sortingOrder);
        }

        #endregion

        #region 层级钳制

        [Test]
        public async Task LayerAboveLimit_IsClampedIntoPanelBand()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("exceeds the panel layer limit"));

            var panel = await UIManager.OpenAsync<FakePanel>("ui/high", UISorting.MaxPanelLayer + 10);

            Assert.AreEqual(UISorting.MaxPanelLayer, panel.Layer, "超限层级应被钳制");
            Assert.Less(panel.Canvas.sortingOrder, UISorting.HudOrder,
                "钳制后不得撞进 HUD 保留带");
        }

        #endregion
    }
}
