using NUnit.Framework;
using UnityEngine;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 弹窗定位原语的测试。
    /// <para>几何计算抽成了纯函数，故不需要场景即可验证全部边界——否则「贴屏幕下沿要不要翻」
    /// 「锚点在角落怎么收敛」这类分支只能在真机上碰运气。与 <see cref="XFramework.XUI.View.UISafeArea"/>
    /// 同一理由。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>：本 fixture 只调纯静态函数，不 Initialize、不 SetInstance、
    /// 不写任何静态字段，故没有需要复位的门面。只有 <see cref="GetScreenRect"/> 那组会建 GameObject，
    /// 它们在各自的 TearDown 里自行清理。</para>
    /// </summary>
    [TestFixture]
    public class UIPopupPositionTests
    {
        /// <summary>基准屏幕。取 1000×1000 是为了让期望值全部是整数，边界一眼可见。</summary>
        private static readonly Rect ScreenRect = new Rect(0f, 0f, 1000f, 1000f);

        /// <summary>基准锚点：100×40，四周都留得下 200×150 的弹窗。</summary>
        private static readonly Rect Anchor = new Rect(400f, 400f, 100f, 40f);

        private static readonly Vector2 PopupSize = new Vector2(200f, 150f);

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);

            _go = null;
        }

        #region 辅助

        /// <summary>按基准屏幕、零 offset、左下 pivot 定位，丢弃 resolved。</summary>
        private static Vector2 Place(Rect anchor, UIPopupDirection direction,
            UIPopupAlign align = UIPopupAlign.Start, Vector2? popupSize = null, float offset = 0f)
        {
            return UIPopupPosition.CalculatePosition(anchor, popupSize ?? PopupSize,
                direction, align, ScreenRect, Vector2.zero, out _, offset);
        }

        private static void AssertPoint(float expectedX, float expectedY, Vector2 actual, string message = null)
        {
            Assert.AreEqual(expectedX, actual.x, 0.0001f, message);
            Assert.AreEqual(expectedY, actual.y, 0.0001f, message);
        }

        #endregion

        #region 方向

        [Test]
        public void Down_PlacesTopEdgeAtAnchorBottom()
        {
            // 弹窗左下角 y = 锚点底边 400 − 弹窗高 150
            AssertPoint(400f, 250f, Place(Anchor, UIPopupDirection.Down));
        }

        [Test]
        public void Up_PlacesBottomEdgeAtAnchorTop()
        {
            // 弹窗左下角 y = 锚点顶边 440
            AssertPoint(400f, 440f, Place(Anchor, UIPopupDirection.Up));
        }

        [Test]
        public void Right_PlacesLeftEdgeAtAnchorRight()
        {
            AssertPoint(500f, 400f, Place(Anchor, UIPopupDirection.Right));
        }

        [Test]
        public void Left_PlacesRightEdgeAtAnchorLeft()
        {
            // 弹窗右边贴锚点左边 400
            AssertPoint(200f, 400f, Place(Anchor, UIPopupDirection.Left));
        }

        [Test]
        public void Offset_IsAppliedAwayFromAnchorOnMainAxis()
        {
            // Down 的间隙把弹窗再往下推 8：400 − 8 − 150
            AssertPoint(400f, 242f, Place(Anchor, UIPopupDirection.Down, offset: 8f));

            // Right 的间隙把它往右推 8：500 + 8
            AssertPoint(508f, 400f, Place(Anchor, UIPopupDirection.Right, offset: 8f));
        }

        #endregion

        #region 副轴对齐

        [Test]
        public void Align_OnVerticalDirection_ControlsHorizontal()
        {
            // 锚点水平区间 400~500，中心 450
            AssertPoint(400f, 250f, Place(Anchor, UIPopupDirection.Down, UIPopupAlign.Start));
            AssertPoint(350f, 250f, Place(Anchor, UIPopupDirection.Down, UIPopupAlign.Center));
            AssertPoint(300f, 250f, Place(Anchor, UIPopupDirection.Down, UIPopupAlign.End));
        }

        [Test]
        public void Align_OnHorizontalDirection_ControlsVertical()
        {
            // 锚点垂直区间 400~440，中心 420；弹窗高 150
            AssertPoint(500f, 400f, Place(Anchor, UIPopupDirection.Right, UIPopupAlign.Start));
            AssertPoint(500f, 345f, Place(Anchor, UIPopupDirection.Right, UIPopupAlign.Center));
            AssertPoint(500f, 290f, Place(Anchor, UIPopupDirection.Right, UIPopupAlign.End));
        }

        #endregion

        #region 收敛

        [Test]
        public void AnchorAtLeftEdge_WidePopup_IsClampedToLeft()
        {
            // 回归：只收敛右侧时，这个组合会把弹窗推出屏幕左边
            // （锚点 xMin=0、宽 50，弹窗宽 200，右对齐算得 −150，旧实现原样返回）
            var anchor = new Rect(0f, 400f, 50f, 40f);

            Vector2 pos = Place(anchor, UIPopupDirection.Down, UIPopupAlign.End);

            Assert.AreEqual(0f, pos.x, 0.0001f, "左边缘必须收敛回屏幕内");
        }

        [Test]
        public void AnchorAtRightEdge_IsClampedToRight()
        {
            var anchor = new Rect(900f, 400f, 100f, 40f);

            Vector2 pos = Place(anchor, UIPopupDirection.Down);

            // 900 + 200 越界，回退到 1000 − 200
            AssertPoint(800f, 250f, pos);
        }

        [Test]
        public void ForcedDown_NearBottom_IsClampedAndMayOverlapAnchor()
        {
            // 显式指定方向时不翻转到别的方向，只收敛——代价是可能盖住锚点，这是有意的语义
            var anchor = new Rect(400f, 20f, 100f, 40f);
            var popup = new Vector2(200f, 150f);

            Vector2 pos = Place(anchor, UIPopupDirection.Down, popupSize: popup);

            Assert.AreEqual(0f, pos.y, 0.0001f, "20 − 150 为负，收敛到屏幕底边");
        }

        [Test]
        public void ForcedUp_NearTop_IsClampedToTop()
        {
            var anchor = new Rect(400f, 900f, 100f, 40f);
            var popup = new Vector2(200f, 150f);

            Vector2 pos = Place(anchor, UIPopupDirection.Up, popupSize: popup);

            // 940 + 150 越界，回退到 1000 − 150
            Assert.AreEqual(850f, pos.y, 0.0001f);
        }

        [Test]
        public void PopupLargerThanScreen_IsPinnedToLowEdge()
        {
            var popup = new Vector2(1200f, 1200f);

            Vector2 pos = Place(Anchor, UIPopupDirection.Down, popupSize: popup);

            // 不是 Mathf.Clamp：它在「上界 < 下界」时的返回值取决于输入值，会静默给出错误结果
            AssertPoint(0f, 0f, pos);
        }

        #endregion

        #region 自动选向

        [Test]
        public void Auto_WhenDownFits_PicksDown()
        {
            UIPopupPosition.CalculatePosition(Anchor, PopupSize, UIPopupDirection.Auto,
                UIPopupAlign.Start, ScreenRect, Vector2.zero, out var resolved);

            Assert.AreEqual(UIPopupDirection.Down, resolved);
        }

        [Test]
        public void Auto_WhenDownOverflows_PicksUp()
        {
            var anchor = new Rect(400f, 50f, 100f, 40f);

            UIPopupPosition.CalculatePosition(anchor, PopupSize, UIPopupDirection.Auto,
                UIPopupAlign.Start, ScreenRect, Vector2.zero, out var resolved);

            Assert.AreEqual(UIPopupDirection.Up, resolved, "下方 50 放不下 150，应翻到上方");
        }

        [Test]
        public void Auto_WhenBothVerticalSidesOverflow_PicksRight()
        {
            // 通高侧边栏：下 100、上 100 都不足以容纳 150
            var anchor = new Rect(400f, 100f, 100f, 800f);

            UIPopupPosition.CalculatePosition(anchor, PopupSize, UIPopupDirection.Auto,
                UIPopupAlign.Start, ScreenRect, Vector2.zero, out var resolved);

            Assert.AreEqual(UIPopupDirection.Right, resolved);
        }

        [Test]
        public void Auto_WhenNoDirectionFits_PicksLargestSpace()
        {
            var popup = new Vector2(1200f, 1200f);

            // 可用空间：下 400、上 560、右 500、左 400 —— 上方最大
            UIPopupPosition.CalculatePosition(Anchor, popup, UIPopupDirection.Auto,
                UIPopupAlign.Start, ScreenRect, Vector2.zero, out var resolved);

            Assert.AreEqual(UIPopupDirection.Up, resolved);
        }

        [Test]
        public void Auto_ReturnsResolvedSameAsExplicitDirection()
        {
            UIPopupPosition.CalculatePosition(Anchor, PopupSize, UIPopupDirection.Left,
                UIPopupAlign.Start, ScreenRect, Vector2.zero, out var resolved);

            Assert.AreEqual(UIPopupDirection.Left, resolved);
        }

        #endregion

        #region Pivot

        [Test]
        public void Pivot_Center_ReturnsRectCenter()
        {
            // 左下角是 (400, 250)，中心 pivot 应返回 (500, 325)
            Vector2 pos = UIPopupPosition.CalculatePosition(Anchor, PopupSize,
                UIPopupDirection.Down, UIPopupAlign.Start, ScreenRect,
                new Vector2(0.5f, 0.5f), out _);

            AssertPoint(500f, 325f, pos);
        }

        [Test]
        public void Pivot_TopLeft_ReturnsRectTopLeft()
        {
            Vector2 pos = UIPopupPosition.CalculatePosition(Anchor, PopupSize,
                UIPopupDirection.Down, UIPopupAlign.Start, ScreenRect,
                new Vector2(0f, 1f), out _);

            AssertPoint(400f, 400f, pos);
        }

        #endregion

        #region 退化输入

        [Test]
        public void ZeroPopupSize_DoesNotThrow()
        {
            Vector2 pos = Place(Anchor, UIPopupDirection.Down, popupSize: Vector2.zero);

            // 零尺寸弹窗没有「放不下」可言，定位退化为锚点起点
            AssertPoint(400f, 400f, pos);
        }

        [Test]
        public void NegativePopupSize_IsTreatedAsZero()
        {
            Vector2 pos = Place(Anchor, UIPopupDirection.Down, popupSize: new Vector2(-50f, -50f));

            AssertPoint(400f, 400f, pos);
        }

        [Test]
        public void ZeroScreenRect_DoesNotThrow()
        {
            // 某些批处理环境下 Screen 尺寸为 0，此时不应抛异常
            Assert.DoesNotThrow(() => UIPopupPosition.CalculatePosition(Anchor, PopupSize,
                UIPopupDirection.Auto, UIPopupAlign.Start, new Rect(0f, 0f, 0f, 0f),
                Vector2.zero, out _));
        }

        [Test]
        public void GetScreenRect_Null_ReturnsZeroRect()
        {
            Assert.AreEqual(default(Rect), UIPopupPosition.GetScreenRect(null));
        }

        #endregion

        #region 屏幕矩形换算

        [Test]
        public void GetScreenRect_AtOrigin_ReturnsLocalRect()
        {
            _go = new GameObject("Anchor", typeof(RectTransform));
            var rect = _go.GetComponent<RectTransform>();
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(100f, 50f);

            // 根节点、pivot 为左下：屏幕矩形即本地矩形
            Rect result = UIPopupPosition.GetScreenRect(rect);

            Assert.AreEqual(0f, result.xMin, 0.0001f);
            Assert.AreEqual(0f, result.yMin, 0.0001f);
            Assert.AreEqual(100f, result.width, 0.0001f);
            Assert.AreEqual(50f, result.height, 0.0001f);
        }

        [Test]
        public void GetScreenRect_CenterPivot_IsOffsetByHalfSize()
        {
            _go = new GameObject("Anchor", typeof(RectTransform));
            var rect = _go.GetComponent<RectTransform>();
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100f, 50f);

            Rect result = UIPopupPosition.GetScreenRect(rect);

            // 中心 pivot 时本地矩形以原点为中心
            Assert.AreEqual(-50f, result.xMin, 0.0001f);
            Assert.AreEqual(-25f, result.yMin, 0.0001f);
            Assert.AreEqual(100f, result.width, 0.0001f);
            Assert.AreEqual(50f, result.height, 0.0001f);
        }

        [Test]
        public void GetScreenRect_Rotated_ReturnsBoundingBox()
        {
            _go = new GameObject("Anchor", typeof(RectTransform));
            var rect = _go.GetComponent<RectTransform>();
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100f, 50f);
            rect.localRotation = Quaternion.Euler(0f, 0f, 90f);

            Rect result = UIPopupPosition.GetScreenRect(rect);

            // 旋转 90° 后外接矩形宽高互换——定位要的正是外接矩形，不是四个角本身
            Assert.AreEqual(50f, result.width, 0.0001f);
            Assert.AreEqual(100f, result.height, 0.0001f);
        }

        #endregion
    }
}
