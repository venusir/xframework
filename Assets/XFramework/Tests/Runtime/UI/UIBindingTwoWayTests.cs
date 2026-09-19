using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using XFramework.XReactive;
using XFramework.XUI.Data;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 双向绑定与约定式绑定的补全测试。
    /// <para>此前 <c>UIPanelBinding</c> 的缓存里躺着 <c>btn_</c>/<c>sld_</c>/<c>tgl_</c> 三类组件，
    /// 却没有任何代码消费它们——缓存了却没人用。绑定也全是单向的，滑块/开关无法写回。</para>
    /// </summary>
    [TestFixture]
    public class UIBindingTwoWayTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        #region 双向：滑块

        [Test]
        public void Slider_WriteBacksToTarget()
        {
            var slider = MakeSlider();
            var target = new ReactiveProperty<float>(0f);
            UIBinder.BindTwoWay(slider, target);

            slider.value = 0.5f;

            Assert.AreEqual(0.5f, target.Value, 0.0001f, "拖动滑块应写回属性");
        }

        [Test]
        public void Slider_FollowsTarget()
        {
            var slider = MakeSlider();
            var target = new ReactiveProperty<float>(0f);
            UIBinder.BindTwoWay(slider, target);

            target.Value = 0.8f;

            Assert.AreEqual(0.8f, slider.value, 0.0001f, "属性变化应回填滑块");
        }

        [Test]
        public void Slider_InitialValueComesFromTarget()
        {
            var slider = MakeSlider();
            slider.value = 1f;

            var target = new ReactiveProperty<float>(0.3f);
            UIBinder.BindTwoWay(slider, target);

            Assert.AreEqual(0.3f, slider.value, 0.0001f,
                "绑定建立时应以属性为准——控件上的旧值不该反过来覆盖属性");
        }

        [Test]
        public void Slider_NoFeedbackLoop()
        {
            var slider = MakeSlider();
            var target = new RoundingWriter();
            UIBinder.BindTwoWay(slider, target);

            slider.value = 0.74f;

            Assert.AreEqual(1, target.WriteCount,
                "写入触发的通知不该反过来再写一次——缺重入守卫时这里会是 2");
            Assert.AreEqual(0.8f, target.Value, 0.0001f);
            Assert.AreEqual(0.8f, slider.value, 0.0001f,
                "目标规范化了值（取整/钳制）时，滑块应回填到真实值而不是停在用户拖到的位置");
        }

        [Test]
        public void Slider_DisposeUnhooksBothDirections()
        {
            var slider = MakeSlider();
            var target = new ReactiveProperty<float>(0f);
            var binding = UIBinder.BindTwoWay(slider, target);

            binding.Dispose();

            slider.value = 0.5f;
            Assert.AreEqual(0f, target.Value, 0.0001f, "释放后控件不应再写回");

            target.Value = 0.9f;
            Assert.AreEqual(0.5f, slider.value, 0.0001f, "释放后属性不应再回填");
        }

        [Test]
        public void Slider_DisposedTarget_DoesNotThrow()
        {
            var slider = MakeSlider();
            var target = new ReactiveProperty<float>(0f);
            UIBinder.BindTwoWay(slider, target);

            target.Dispose();

            Assert.DoesNotThrow(() => slider.value = 0.5f,
                "目标失效时 TryWriteValue 应返回 false，而不是把异常抛进 UI 事件回调");
        }

        #endregion

        #region 双向：开关

        [Test]
        public void Toggle_WriteBacksAndFollows()
        {
            var toggle = MakeToggle();
            var target = new ReactiveProperty<bool>(false);
            UIBinder.BindTwoWay(toggle, target);

            toggle.isOn = true;
            Assert.IsTrue(target.Value, "切换开关应写回属性");

            target.Value = false;
            Assert.IsFalse(toggle.isOn, "属性变化应回填开关");
        }

        #endregion

        #region 约定式绑定补全

        [Test]
        public void BindByConvention_Slider_Binds()
        {
            var root = MakeRoot();
            var slider = AddNamed<Slider>(root.transform, "sld_Volume");

            var binding = root.AddComponent<UIPanelBinding>();
            binding.CacheComponents(root.transform);

            var source = new ReactiveProperty<float>(0.4f);
            binding.BindByConvention("Volume", source);

            Assert.AreEqual(0.4f, slider.value, 0.0001f, "绑定时应立即同步当前值");

            source.Value = 0.6f;
            Assert.AreEqual(0.6f, slider.value, 0.0001f);
        }

        [Test]
        public void BindByConvention_Toggle_Binds()
        {
            var root = MakeRoot();
            var toggle = AddNamed<Toggle>(root.transform, "tgl_Mute");

            var binding = root.AddComponent<UIPanelBinding>();
            binding.CacheComponents(root.transform);

            var source = new ReactiveProperty<bool>(true);
            binding.BindByConvention("Mute", source);

            Assert.IsTrue(toggle.isOn);

            source.Value = false;
            Assert.IsFalse(toggle.isOn);
        }

        // 这条用例只在编辑器下存在：被测的告警本身是 #if UNITY_EDITOR 编译的
        // （见 UIPanelBinding.BindByConvention，刻意避免 Release 版 GC），Player 构建里那段代码
        // 根本不存在，LogAssert 永远等不到它——不圈起来的话，Test Runner 的「Run all in Player」
        // 会稳定地在这一条上失败，而那不是缺陷、是断言写错了适用范围
#if UNITY_EDITOR
        [Test]
        public void BindByConvention_UnknownProperty_WarnsInEditor()
        {
            var root = MakeRoot();
            var binding = root.AddComponent<UIPanelBinding>();
            binding.CacheComponents(root.transform);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("未找到 'Nothing' 对应的 UI 组件"));

            binding.BindByConvention("Nothing", new ReactiveProperty<int>(0));
        }
#endif

        #endregion

        #region 按钮点击

        [Test]
        public void BindClick_InvokesHandler()
        {
            var root = MakeRoot();
            var button = AddNamed<Button>(root.transform, "btn_Close");

            var binding = root.AddComponent<UIPanelBinding>();
            binding.CacheComponents(root.transform);

            int clicks = 0;
            binding.BindClick("Close", () => clicks++);

            button.onClick.Invoke();

            Assert.AreEqual(1, clicks);
        }

        [Test]
        public void BindClick_UnbindDetaches()
        {
            var root = MakeRoot();
            var button = AddNamed<Button>(root.transform, "btn_Close");

            var binding = root.AddComponent<UIPanelBinding>();
            binding.CacheComponents(root.transform);

            int clicks = 0;
            binding.BindClick("Close", () => clicks++);
            binding.Unbind();

            button.onClick.Invoke();

            Assert.AreEqual(0, clicks, "解绑后不该再回调——回池时由 Unbind 统一释放");
        }

        #endregion

        #region Test Doubles

        /// <summary>
        /// 写入时把值向上取整到 0.1 的属性，用于暴露回灌环路：
        /// 若绑定层没有重入守卫，写入触发的通知会反过来再写一次。
        /// </summary>
        private sealed class RoundingWriter : IReactivePropertyWriter<float>
        {
            private readonly ReactiveProperty<float> _inner = new ReactiveProperty<float>();

            public int WriteCount { get; private set; }

            public float Value => _inner.Value;

            public IDisposable Subscribe(Action<float> onNext) => _inner.Subscribe(onNext);

            public bool TryWriteValue(float value)
            {
                WriteCount++;
                _inner.Value = Mathf.Ceil(value * 10f) / 10f;
                return true;
            }
        }

        #endregion

        #region Helpers

        private GameObject MakeRoot()
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            _created.Add(go);
            return go;
        }

        private static T AddNamed<T>(Transform parent, string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        private Slider MakeSlider() => AddNamed<Slider>(MakeRoot().transform, "Slider");

        private Toggle MakeToggle()
        {
            var root = MakeRoot();
            return AddNamed<Toggle>(root.transform, "Toggle");
        }

        #endregion
    }
}
