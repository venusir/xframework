using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XReactive;
using XFramework.XUI.Data;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="UIBinder"/> 公开绑定方法的直接覆盖（R3-UIB-012）。
    /// <para>此前四个公开绑定方法零直接构造：只有面板 / 绑定组件的间接路径。TMP 相关方法（`BindToText` /
    /// `BindToLocalizedText`）不在射程内——本工程未导入 TMP Essentials，实例化 TMP_Text 会直接失败。</para>
    /// </summary>
    [TestFixture]
    public class UIBinderTests
    {
        private readonly System.Collections.Generic.List<GameObject> _created =
            new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private T Create<T>(string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            _created.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void BindToFillAmount_UpdatesOnWrite_AndDisposeStops()
        {
            var image = Create<Image>("Image");
            var source = new ReactiveProperty<float>(0f);
            var sub = source.BindToFillAmount(image);

            source.Value = 0.5f;
            Assert.AreEqual(0.5f, image.fillAmount, 1e-4f);

            sub.Dispose();
            source.Value = 0.75f;
            Assert.AreEqual(0.5f, image.fillAmount, 1e-4f, "释放后不应继续更新");
            source.Dispose();
        }

        [Test]
        public void BindToSlider_UpdatesOnWrite()
        {
            var slider = Create<Slider>("Slider");
            var source = new ReactiveProperty<float>(0f);
            using var _ = source.BindToSlider(slider);

            source.Value = 0.3f;
            Assert.AreEqual(0.3f, slider.value, 1e-4f);
            source.Dispose();
        }

        [Test]
        public void BindToToggle_UpdatesOnWrite()
        {
            var toggle = Create<Toggle>("Toggle");
            var source = new ReactiveProperty<bool>(false);
            using var _ = source.BindToToggle(toggle);

            source.Value = true;
            Assert.IsTrue(toggle.isOn);
            source.Dispose();
        }

        [Test]
        public void BindToActive_TogglesGameObject()
        {
            var target = new GameObject("Target");
            _created.Add(target);
            var source = new ReactiveProperty<bool>(true);
            using var _ = source.BindToActive(target);

            source.Value = false;
            Assert.IsFalse(target.activeSelf);
            source.Dispose();
        }

        [Test]
        public void BindToClick_InvokesAndDisposeStops()
        {
            var button = Create<Button>("Button");
            int clicks = 0;
            var sub = button.BindToClick(() => clicks++);

            button.onClick.Invoke();
            Assert.AreEqual(1, clicks);

            sub.Dispose();
            button.onClick.Invoke();
            Assert.AreEqual(1, clicks, "释放后不应再响应点击");
        }
    }
}
