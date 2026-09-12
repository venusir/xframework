using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XReactive;
using XFramework.XUI.Data;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 绑定接收者从具体类型 <c>ReactiveProperty&lt;T&gt;</c> 放宽到 <see cref="IReactiveProperty{T}"/>
    /// 后的行为测试。
    /// </summary>
    /// <remarks>
    /// 每一例都用 <see cref="FakeProperty{T}"/>——一个<b>非框架</b>的接口实现。这正是本组改动的意义：
    /// 接收者是具体类型时，这类实参连编译都过不去。若将来有人把接收者改回具体类型，本文件会先编译失败。
    /// </remarks>
    [TestFixture]
    public class UIBindingInterfaceTests
    {
        #region Test Doubles

        /// <summary>自定义 <see cref="IReactiveProperty{T}"/> 实现，契约与 <c>ReactiveProperty&lt;T&gt;</c> 对齐：订阅即回调当前值。</summary>
        private sealed class FakeProperty<T> : IReactiveProperty<T>
        {
            private readonly List<Action<T>> _handlers = new();

            public FakeProperty(T initialValue)
            {
                Value = initialValue;
            }

            public T Value { get; private set; }

            public IDisposable Subscribe(Action<T> onNext)
            {
                if (onNext == null)
                    throw new ArgumentNullException(nameof(onNext));

                _handlers.Add(onNext);
                onNext(Value); // 与 ReactiveProperty 契约一致：订阅时立即同步回调当前值
                return new Subscription(this, onNext);
            }

            /// <summary>模拟值变化并推送。</summary>
            public void Push(T value)
            {
                Value = value;
                var snapshot = _handlers.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                    snapshot[i](value);
            }

            private sealed class Subscription : IDisposable
            {
                private readonly FakeProperty<T> _owner;
                private readonly Action<T> _handler;

                public Subscription(FakeProperty<T> owner, Action<T> handler)
                {
                    _owner = owner;
                    _handler = handler;
                }

                public void Dispose() => _owner._handlers.Remove(_handler);
            }
        }

        #endregion

        #region 通用绑定

        [Test]
        public void Bind_CustomImplementation_ReceivesValuesAndUnsubscribes()
        {
            var source = new FakeProperty<int>(1);
            var received = new List<int>();

            using var handle = source.Bind(received.Add);

            Assert.AreEqual(1, received.Count, "订阅即回调当前值");

            source.Push(2);
            CollectionAssert.AreEqual(new[] { 1, 2 }, received);

            handle.Dispose();
            source.Push(3);
            CollectionAssert.AreEqual(new[] { 1, 2 }, received, "退订后不再推送");
        }

        #endregion

        #region 具体控件绑定

        [Test]
        public void BindToActive_CustomImplementation_TogglesGameObject()
        {
            var target = new GameObject("bind-active-target");
            try
            {
                var source = new FakeProperty<bool>(false);

                using var handle = source.BindToActive(target);

                Assert.IsFalse(target.activeSelf, "订阅即回调当前值 false");

                source.Push(true);
                Assert.IsTrue(target.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BindToSlider_CustomImplementation_SetsValue()
        {
            var target = new GameObject("bind-slider-target");
            try
            {
                var slider = target.AddComponent<Slider>();
                var source = new FakeProperty<float>(0.25f);

                using var handle = source.BindToSlider(slider);

                Assert.AreEqual(0.25f, slider.value, 1e-5f, "订阅即回调当前值");

                source.Push(0.75f);
                Assert.AreEqual(0.75f, slider.value, 1e-5f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        #endregion

        #region 按命名约定绑定

        [Test]
        public void BindByConvention_FloatProperty_BindsToImageFillAmount()
        {
            // 这一例专门覆盖 UIPanelBinding 里 `source is IReactiveProperty<float>` 那三处判断：
            // 若判断仍写具体类型，FakeProperty<float> 会掉进「未找到匹配组件」的静默分支
            var root = new GameObject("panel");
            try
            {
                var child = new GameObject("img_Fill");
                child.transform.SetParent(root.transform);
                var image = child.AddComponent<Image>();

                var binding = root.AddComponent<UIPanelBinding>();
                binding.CacheComponents(root.transform);

                var source = new FakeProperty<float>(0.4f);

                binding.BindByConvention("Fill", source);

                Assert.AreEqual(0.4f, image.fillAmount, 1e-5f, "float 属性应绑到 img_ 前缀的 fillAmount");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BindByConvention_TextProperty_BindsToTextComponent()
        {
            var root = new GameObject("panel");
            try
            {
                var child = new GameObject("txt_Title");
                child.transform.SetParent(root.transform);
                var text = child.AddComponent<Text>();

                var binding = root.AddComponent<UIPanelBinding>();
                binding.CacheComponents(root.transform);

                var source = new FakeProperty<string>("hello");

                binding.BindByConvention("Title", source);

                Assert.AreEqual("hello", text.text, "txt_ 分支优先级最高，且应接受任意接口实现");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        #endregion
    }
}
