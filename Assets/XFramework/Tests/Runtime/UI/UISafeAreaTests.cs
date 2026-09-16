using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 安全区适配测试。
    /// <para>换算逻辑抽成了纯函数，故不依赖 <see cref="Screen"/> 的当前状态即可验证——
    /// 否则这类逻辑只能靠在特定设备上碰运气。</para>
    /// </summary>
    [TestFixture]
    public class UISafeAreaTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);

            _go = null;
        }

        #region 换算

        [Test]
        public void FullScreen_ReturnsUnitRect()
        {
            var screen = new Vector2(1080f, 1920f);
            var safe = new Rect(0f, 0f, 1080f, 1920f);

            UISafeArea.CalculateAnchors(safe, screen, out var min, out var max);

            Assert.AreEqual(0f, min.x, 0.0001f);
            Assert.AreEqual(0f, min.y, 0.0001f);
            Assert.AreEqual(1f, max.x, 0.0001f);
            Assert.AreEqual(1f, max.y, 0.0001f);
        }

        [Test]
        public void TopNotch_InsetsTopAnchor()
        {
            // 1080x1920 的屏幕，顶部刘海 100px、底部手势条 60px
            var screen = new Vector2(1080f, 1920f);
            var safe = new Rect(0f, 60f, 1080f, 1920f - 100f - 60f);

            UISafeArea.CalculateAnchors(safe, screen, out var min, out var max);

            Assert.AreEqual(0f, min.x, 0.0001f, "左右无内缩");
            Assert.AreEqual(60f / 1920f, min.y, 0.0001f, "底部手势条");
            Assert.AreEqual(1f, max.x, 0.0001f);
            Assert.AreEqual(1f - 100f / 1920f, max.y, 0.0001f, "顶部刘海");
        }

        [Test]
        public void LeftNotch_InsetsHorizontalAnchors()
        {
            // 横屏、左侧挖孔 120px
            var screen = new Vector2(2400f, 1080f);
            var safe = new Rect(120f, 0f, 2400f - 120f, 1080f);

            UISafeArea.CalculateAnchors(safe, screen, out var min, out var max);

            Assert.AreEqual(120f / 2400f, min.x, 0.0001f);
            Assert.AreEqual(0f, min.y, 0.0001f);
            Assert.AreEqual(1f, max.x, 0.0001f);
            Assert.AreEqual(1f, max.y, 0.0001f);
        }

        [Test]
        public void ZeroScreenSize_FallsBackToFullRect()
        {
            // 某些批处理/未初始化环境下 Screen 尺寸为 0，除零会得到 NaN 锚点
            UISafeArea.CalculateAnchors(new Rect(10f, 10f, 100f, 100f), Vector2.zero,
                out var min, out var max);

            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        #endregion

        #region 应用

        [Test]
        public void Apply_ZeroesOffsets()
        {
            _go = new GameObject("SafeArea", typeof(RectTransform));
            var rect = _go.GetComponent<RectTransform>();

            // 先给一组非零偏移，验证 Apply 会清掉它——否则旧偏移会把安全区又推开
            rect.offsetMin = new Vector2(30f, 30f);
            rect.offsetMax = new Vector2(-30f, -30f);

            var safeArea = _go.AddComponent<UISafeArea>();
            safeArea.Apply();

            Assert.AreEqual(Vector2.zero, rect.offsetMin);
            Assert.AreEqual(Vector2.zero, rect.offsetMax);
        }

        [Test]
        public void Apply_ProducesValidUnitRangeAnchors()
        {
            _go = new GameObject("SafeArea", typeof(RectTransform));
            var rect = _go.GetComponent<RectTransform>();

            var safeArea = _go.AddComponent<UISafeArea>();
            safeArea.Apply();

            Assert.GreaterOrEqual(rect.anchorMin.x, 0f);
            Assert.GreaterOrEqual(rect.anchorMin.y, 0f);
            Assert.LessOrEqual(rect.anchorMax.x, 1f);
            Assert.LessOrEqual(rect.anchorMax.y, 1f);
            Assert.LessOrEqual(rect.anchorMin.x, rect.anchorMax.x);
            Assert.LessOrEqual(rect.anchorMin.y, rect.anchorMax.y);
        }

        #endregion
    }
}
