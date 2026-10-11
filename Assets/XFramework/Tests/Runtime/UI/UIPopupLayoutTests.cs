using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 守卫一条 <b>UGUI 的前提</b>，不是守卫本框架的代码。
    /// <para><b>这条前提是 <c>UIManager.ShowPopupAsync</c> 存在理由的一半</b>：布局驱动的尺寸在
    /// 「本轮布局跑完」之前读到的仍是**上一次**的值。若它不成立（Unity 某天改成即时生效），
    /// 那个入口的价值会静默退化成「省四行」——所以把它钉成用例，而不是留一句注释。</para>
    /// <para><b>刻意测「池化复用」而不是「新建实例」</b>：预制体里由 <see cref="ContentSizeFitter"/> 驱动的
    /// 对象，其序列化尺寸往往是**上一次布局的结果**，于是新建路径看起来总是对的；真正会出错的是
    /// 池化取出后换了内容——那才是弹窗复用的真实形态。用例模拟的正是后者。</para>
    /// <para>与 <c>UIPopupPosition</c> 的分工：那边是纯几何（怎么摆），这边守的是「什么时候能量得准」。</para>
    /// </summary>
    [TestFixture]
    public class UIPopupLayoutTests
    {
        private GameObject _canvasGo;
        private GameObject _popupGo;

        [TearDown]
        public void TearDown()
        {
            if (_popupGo != null)
                Object.DestroyImmediate(_popupGo);

            if (_canvasGo != null)
                Object.DestroyImmediate(_canvasGo);

            _popupGo = null;
            _canvasGo = null;
        }

        /// <summary>
        /// 建一个由 <see cref="ContentSizeFitter"/> 驱动宽度的对象，返回它的 RectTransform 与
        /// 用于改内容的 <see cref="LayoutElement"/>。
        /// </summary>
        private (RectTransform rect, LayoutElement element) CreateFittedPopup()
        {
            _canvasGo = new GameObject("ProbeCanvas", typeof(Canvas));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            _popupGo = new GameObject("Popup", typeof(RectTransform));
            var rect = _popupGo.GetComponent<RectTransform>();
            rect.SetParent(_canvasGo.transform, false);

            var fitter = _popupGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var element = _popupGo.AddComponent<LayoutElement>();

            return (rect, element);
        }

        [Test]
        public void LayoutDrivenWidth_IsStaleUntilLayoutRebuild()
        {
            var (rect, element) = CreateFittedPopup();

            // 基线：先把布局跑一遍，拿到一个「已知正确」的宽度
            element.preferredWidth = 300f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Assert.AreEqual(300f, rect.rect.width, 0.5f, "前提：强制布局后宽度应等于 preferred");

            // 模拟池化复用的弹窗换了内容
            element.preferredWidth = 500f;

            float beforeRebuild = rect.rect.width;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            float afterRebuild = rect.rect.width;

            Assert.AreEqual(300f, beforeRebuild, 0.5f,
                "前提不成立：换了内容之后、布局跑完之前，读到的尺寸竟然已经是新的。" +
                "若这条红了，说明 UGUI 的布局已变成即时生效——ShowPopupAsync 的「强制布局」那一步" +
                "不再是必要的，该入口的价值只剩「省四行」，请回来重新裁定要不要保留它。");

            Assert.AreEqual(500f, afterRebuild, 0.5f, "强制布局后应拿到新内容对应的宽度");
        }
    }
}
