using System;
using R3;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace XFramework
{
    /// <summary>
    /// UI 数据绑定工具。将 <see cref="IReadonlySignal{T}"/> 自动绑定到 UI 组件。
    /// <para>所有绑定方法都返回 <see cref="IDisposable"/>，可通过 <see cref="IDestroyCancellationToken"/> 自动取消绑定。</para>
    /// </summary>
    public static class UIBinder
    {
        #region TMP_Text

        /// <summary>将信号绑定到 TMP_Text 的 text 属性。</summary>
        public static IDisposable BindToText<T>(this IReadonlySignal<T> signal, TMP_Text text, Func<T, string> format = null)
        {
            return signal.Subscribe(v => text.text = format?.Invoke(v) ?? v?.ToString() ?? string.Empty);
        }

        /// <summary>将信号绑定到 TMP_Text 的 text 属性，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToText<T>(this IReadonlySignal<T> signal, TMP_Text text, IDestroyCancellationToken token, Func<T, string> format = null)
        {
            var disposable = signal.BindToText(text, format);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion

        #region Slider

        /// <summary>将信号绑定到 Slider 的 value 属性。</summary>
        public static IDisposable BindToSlider(this IReadonlySignal<float> signal, Slider slider)
        {
            return signal.Subscribe(v => slider.value = v);
        }

        /// <summary>将信号绑定到 Slider 的 value 属性，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToSlider(this IReadonlySignal<float> signal, Slider slider, IDestroyCancellationToken token)
        {
            var disposable = signal.BindToSlider(slider);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion

        #region Image (fillAmount)

        /// <summary>将信号绑定到 Image 的 fillAmount 属性。</summary>
        public static IDisposable BindToFillAmount(this IReadonlySignal<float> signal, Image image)
        {
            return signal.Subscribe(v => image.fillAmount = v);
        }

        /// <summary>将信号绑定到 Image 的 fillAmount 属性，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToFillAmount(this IReadonlySignal<float> signal, Image image, IDestroyCancellationToken token)
        {
            var disposable = signal.BindToFillAmount(image);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion

        #region Image (sprite)

        /// <summary>将信号绑定到 Image 的 sprite 属性。</summary>
        public static IDisposable BindToSprite(this IReadonlySignal<Sprite> signal, Image image)
        {
            return signal.Subscribe(v => image.sprite = v);
        }

        /// <summary>将信号绑定到 Image 的 sprite 属性，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToSprite(this IReadonlySignal<Sprite> signal, Image image, IDestroyCancellationToken token)
        {
            var disposable = signal.BindToSprite(image);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion

        #region Toggle

        /// <summary>将信号绑定到 Toggle 的 isOn 属性。</summary>
        public static IDisposable BindToToggle(this IReadonlySignal<bool> signal, Toggle toggle)
        {
            return signal.Subscribe(v => toggle.isOn = v);
        }

        /// <summary>将信号绑定到 Toggle 的 isOn 属性，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToToggle(this IReadonlySignal<bool> signal, Toggle toggle, IDestroyCancellationToken token)
        {
            var disposable = signal.BindToToggle(toggle);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion

        #region GameObject (active)

        /// <summary>将信号绑定到 GameObject 的 active 属性。</summary>
        public static IDisposable BindToActive(this IReadonlySignal<bool> signal, GameObject target)
        {
            return signal.Subscribe(v => target.SetActive(v));
        }

        /// <summary>将信号绑定到 GameObject 的 active 属性，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToActive(this IReadonlySignal<bool> signal, GameObject target, IDestroyCancellationToken token)
        {
            var disposable = signal.BindToActive(target);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion

        #region Button (ReactiveCommand)

        /// <summary>将 ReactiveCommand 绑定到 Button 的点击事件。</summary>
        public static IDisposable BindToButton(this ReactiveCommand command, Button button)
        {
            var handler = new UnityEngine.Events.UnityAction(() => command.Execute(Unit.Default));
            button.onClick.AddListener(handler);
            return Disposable.Create(() => button.onClick.RemoveListener(handler));
        }

        /// <summary>将 ReactiveCommand 绑定到 Button 的点击事件，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToButton(this ReactiveCommand command, Button button, IDestroyCancellationToken token)
        {
            var disposable = command.BindToButton(button);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        /// <summary>将带参数的 ReactiveCommand 绑定到 Button 的点击事件。</summary>
        public static IDisposable BindToButton<T>(this ReactiveCommand<T> command, Button button, T parameter)
        {
            var handler = new UnityEngine.Events.UnityAction(() => command.Execute(parameter));
            button.onClick.AddListener(handler);
            return Disposable.Create(() => button.onClick.RemoveListener(handler));
        }

        /// <summary>将带参数的 ReactiveCommand 绑定到 Button 的点击事件，并自动绑定到对象生命周期。</summary>
        public static IDisposable BindToButton<T>(this ReactiveCommand<T> command, Button button, T parameter, IDestroyCancellationToken token)
        {
            var disposable = command.BindToButton(button, parameter);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion

        #region Generic (custom binding)

        /// <summary>自定义绑定。将信号值通过自定义 setter 同步到目标。</summary>
        public static IDisposable Bind<T>(this IReadonlySignal<T> signal, Action<T> setter)
        {
            return signal.Subscribe(setter);
        }

        /// <summary>自定义绑定，并自动绑定到对象生命周期。</summary>
        public static IDisposable Bind<T>(this IReadonlySignal<T> signal, Action<T> setter, IDestroyCancellationToken token)
        {
            var disposable = signal.Bind(setter);
            disposable.RegisterTo(token.DestroyCancellationToken);
            return disposable;
        }

        #endregion
    }
}
