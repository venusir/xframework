using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 锚定弹窗（<c>UIManager.ShowPopupAsync</c>）的行为契约。
    /// <para><b>位置断言用几何关系而不是坐标数值</b>（「弹窗底边贴锚点底边之下」「左边缘左对齐」），
    /// 因为可用区域取的是 <see cref="Screen.safeArea"/>——把期望值写成绝对坐标会让用例随跑测机器的
    /// 分辨率与刘海变化。关系式同时更贴近契约本身。</para>
    /// <para><b>点击用 <c>Button.onClick.Invoke()</c></b>，与仓内 <c>UIMaskTests</c> 同一做法：
    /// 真实指针事件需要项目自带 EventSystem 与输入模块，而测试壳两者都没有。这条前提写在
    /// README 里——本节验的是**接线**（谁在挡、点了关谁），不是 UGUI 的射线管道本身。</para>
    /// </summary>
    [TestFixture]
    public class UIPopupTests
    {
        private GameObject _root;
        private GameObject _anchorGo;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_PopupTest", typeof(RectTransform));

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

            if (_anchorGo != null)
                UnityEngine.Object.DestroyImmediate(_anchorGo);

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            _anchorGo = null;
            _root = null;

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #region 辅助

        /// <summary>
        /// 造一个锚点控件，摆在屏幕正中——四周都留得下，于是断言不会被收敛逻辑干扰。
        /// pivot 取左下，便于把「锚点矩形」与「屏幕坐标」直接对上。
        /// </summary>
        private RectTransform CreateAnchor(Vector2 size)
        {
            _anchorGo = new GameObject("Anchor", typeof(RectTransform));
            var rect = _anchorGo.GetComponent<RectTransform>();
            rect.SetParent(_root.transform, false);
            rect.pivot = Vector2.zero;
            rect.sizeDelta = size;
            rect.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            return rect;
        }

        private Transform FindCatcher() => _root.transform.Find("UIManager_PopupCatcher");

        private static Button CatcherButton(Transform catcher) =>
            catcher != null ? catcher.GetComponent<Button>() : null;

        #endregion

        #region 摆位置

        [Test]
        public async Task Popup_IsPlacedBelowAnchor_AndLeftAligned()
        {
            var anchor = CreateAnchor(new Vector2(100f, 40f));

            var panel = await UIManager.ShowPopupAsync<FakePanel>(
                anchor, "PF_FakePopup", UIPopupDirection.Down, UIPopupAlign.Start,
                closeOnClickOutside: false);

            var anchorRect = UIPopupPosition.GetScreenRect(anchor);
            var popupRect = UIPopupPosition.GetScreenRect((RectTransform)panel.transform);

            Assert.AreEqual(anchorRect.xMin, popupRect.xMin, 0.5f, "Align.Start 应左对齐锚点");
            Assert.AreEqual(anchorRect.yMin, popupRect.yMax, 0.5f, "Direction.Down 应贴在锚点下方");
        }

        [Test]
        public async Task Popup_HonoursUpDirection()
        {
            var anchor = CreateAnchor(new Vector2(100f, 40f));

            var panel = await UIManager.ShowPopupAsync<FakePanel>(
                anchor, "PF_FakePopup", UIPopupDirection.Up, UIPopupAlign.Start,
                closeOnClickOutside: false);

            var anchorRect = UIPopupPosition.GetScreenRect(anchor);
            var popupRect = UIPopupPosition.GetScreenRect((RectTransform)panel.transform);

            Assert.AreEqual(anchorRect.yMax, popupRect.yMin, 0.5f, "Direction.Up 应贴在锚点上方");
        }

        [Test]
        public void NullAnchor_Throws()
        {
            // 锚点是本 API 的意义所在；静默摆到屏幕角落比抛异常难查得多
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await UIManager.ShowPopupAsync<FakePanel>(null, "PF_FakePopup"));
        }

        [Test]
        public async Task OpenFailure_ReturnsNullWithoutThrowing()
        {
            _factory.RegisterPanel<FakePanel>(parent => null);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to instantiate panel"));

            var panel = await UIManager.ShowPopupAsync<FakePanel>(
                CreateAnchor(new Vector2(100f, 40f)), "PF_FakePopup");

            Assert.IsNull(panel, "与 OpenAsync 的「失败返回 null」一致");
        }

        #endregion

        #region 点击外部关闭

        [Test]
        public async Task CloseOnClickOutside_CreatesCatcherBelowPopup()
        {
            var panel = await UIManager.ShowPopupAsync<FakePanel>(
                CreateAnchor(new Vector2(100f, 40f)), "PF_FakePopup",
                layer: UILayers.Popup, closeOnClickOutside: true);

            var catcher = FindCatcher();
            Assert.IsNotNull(catcher, "closeOnClickOutside 为真时必须挂捕获层");
            Assert.IsTrue(catcher.gameObject.activeSelf, "捕获层应处于显示中");

            int catcherOrder = catcher.GetComponent<Canvas>().sortingOrder;
            Assert.AreEqual(UISorting.PanelOrder(UILayers.Popup, 0), catcherOrder,
                "捕获层应取该层第 0 号槽位");
            Assert.Less(catcherOrder, panel.Canvas.sortingOrder,
                "捕获层必须在弹窗之下，否则会把弹窗自己挡掉");
        }

        [Test]
        public async Task Catcher_HasGraphicRaycaster()
        {
            // ★ 这是它能挡输入的**前提**而不是可选装饰：UGUI 的 raycaster 只查自己 GameObject 上
            //   那个 Canvas 下注册的图形，而 Graphic.CacheCanvas 取 GetComponentsInParent 的
            //   第一个（自身优先）。少了它，捕获层就是一张接不到点击的透明图片
            await UIManager.ShowPopupAsync<FakePanel>(
                CreateAnchor(new Vector2(100f, 40f)), "PF_FakePopup", closeOnClickOutside: true);

            var catcher = FindCatcher();
            Assert.IsNotNull(catcher.GetComponent<GraphicRaycaster>(),
                "捕获层必须自带 GraphicRaycaster，否则挡不住输入");
            Assert.IsNotNull(catcher.GetComponent<Image>(), "挡输入靠的是可被射线命中的图形");
        }

        [Test]
        public async Task CloseOnClickOutsideFalse_CreatesNoCatcher()
        {
            await UIManager.ShowPopupAsync<FakePanel>(
                CreateAnchor(new Vector2(100f, 40f)), "PF_FakePopup", closeOnClickOutside: false);

            var catcher = FindCatcher();
            Assert.IsTrue(catcher == null || !catcher.gameObject.activeSelf,
                "显式关闭时不应挂捕获层");
        }

        [Test]
        public async Task CatcherClick_ClosesPopup()
        {
            var anchor = CreateAnchor(new Vector2(100f, 40f));
            await UIManager.ShowPopupAsync<FakePanel>(
                anchor, "PF_FakePopup", closeOnClickOutside: true);
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "前提：弹窗已打开");

            CatcherButton(FindCatcher()).onClick.Invoke();

            // 关闭是 fire-and-forget（与遮罩的 OnMaskClicked 同一形状）
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.IsFalse(UIManager.IsOpen<FakePanel>(), "点捕获层应关掉它替谁挡的那个弹窗");
        }

        [Test]
        public async Task ClosingPopup_HidesCatcher()
        {
            // 面板被**别的途径**关掉时捕获层也必须收——否则它会留在屏幕上继续吃点击
            var panel = await UIManager.ShowPopupAsync<FakePanel>(
                CreateAnchor(new Vector2(100f, 40f)), "PF_FakePopup", closeOnClickOutside: true);

            await UIManager.CloseAsync(panel);

            var catcher = FindCatcher();
            Assert.IsTrue(catcher == null || !catcher.gameObject.activeSelf,
                "弹窗关闭后捕获层必须收起");
        }

        #endregion
    }
}
