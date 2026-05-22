using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using XFramework.XLocalization;
using XFramework.XReactive;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// UI 数据绑定工具。将 <see cref="ReactiveProperty{T}"/> / <see cref="ReadOnlyReactiveProperty{T}"/> 自动绑定到 UI 组件。
    /// <para>所有绑定方法都返回 <see cref="IDisposable"/>，可通过 <see cref="UIPanelBinding.RegisterBinding{T}"/> 统一管理。</para>
    /// <para>与 <see cref="UIPanelBinding"/> 互补：
    /// - UIPanelBinding：命名约定自动绑（适合标准面板）
    /// - UIBinder：手动精确绑（适合 format 格式化、按钮点击、非标准组件）
    /// </para>
    /// </summary>
    public static class UIBinder
    {
        #region TMP_Text

        /// <summary>将 ReactiveProperty 绑定到 TMP_Text 的 text 属性。支持 format 格式化。</summary>
        public static IDisposable BindToText<T>(this ReactiveProperty<T> source, TMP_Text text, Func<T, string> format = null)
        {
            if (source == null || text == null) return null;
            return source.Subscribe(v => text.text = format?.Invoke(v) ?? v?.ToString() ?? string.Empty);
        }

        /// <summary>将 ReadOnlyReactiveProperty 绑定到 TMP_Text 的 text 属性。支持 format 格式化。</summary>
        public static IDisposable BindToText<T>(this ReadOnlyReactiveProperty<T> source, TMP_Text text, Func<T, string> format = null)
        {
            if (source == null || text == null) return null;
            return source.Subscribe(v => text.text = format?.Invoke(v) ?? v?.ToString() ?? string.Empty);
        }

        #endregion

        #region Slider

        /// <summary>将 ReactiveProperty 绑定到 Slider 的 value 属性。</summary>
        public static IDisposable BindToSlider(this ReactiveProperty<float> source, Slider slider)
        {
            if (source == null || slider == null) return null;
            return source.Subscribe(v => slider.value = v);
        }

        /// <summary>将 ReadOnlyReactiveProperty 绑定到 Slider 的 value 属性。</summary>
        public static IDisposable BindToSlider(this ReadOnlyReactiveProperty<float> source, Slider slider)
        {
            if (source == null || slider == null) return null;
            return source.Subscribe(v => slider.value = v);
        }

        #endregion

        #region Image (fillAmount)

        /// <summary>将 ReactiveProperty 绑定到 Image 的 fillAmount 属性。</summary>
        public static IDisposable BindToFillAmount(this ReactiveProperty<float> source, Image image)
        {
            if (source == null || image == null) return null;
            return source.Subscribe(v => image.fillAmount = v);
        }

        /// <summary>将 ReadOnlyReactiveProperty 绑定到 Image 的 fillAmount 属性。</summary>
        public static IDisposable BindToFillAmount(this ReadOnlyReactiveProperty<float> source, Image image)
        {
            if (source == null || image == null) return null;
            return source.Subscribe(v => image.fillAmount = v);
        }

        #endregion

        #region Image (sprite)

        /// <summary>将 ReactiveProperty 绑定到 Image 的 sprite 属性。</summary>
        public static IDisposable BindToSprite(this ReactiveProperty<Sprite> source, Image image)
        {
            if (source == null || image == null) return null;
            return source.Subscribe(v => image.sprite = v);
        }

        /// <summary>将 ReadOnlyReactiveProperty 绑定到 Image 的 sprite 属性。</summary>
        public static IDisposable BindToSprite(this ReadOnlyReactiveProperty<Sprite> source, Image image)
        {
            if (source == null || image == null) return null;
            return source.Subscribe(v => image.sprite = v);
        }

        #endregion

        #region Toggle

        /// <summary>将 ReactiveProperty 绑定到 Toggle 的 isOn 属性。</summary>
        public static IDisposable BindToToggle(this ReactiveProperty<bool> source, Toggle toggle)
        {
            if (source == null || toggle == null) return null;
            return source.Subscribe(v => toggle.isOn = v);
        }

        /// <summary>将 ReadOnlyReactiveProperty 绑定到 Toggle 的 isOn 属性。</summary>
        public static IDisposable BindToToggle(this ReadOnlyReactiveProperty<bool> source, Toggle toggle)
        {
            if (source == null || toggle == null) return null;
            return source.Subscribe(v => toggle.isOn = v);
        }

        #endregion

        #region GameObject (active)

        /// <summary>将 ReactiveProperty 绑定到 GameObject 的 active 属性。</summary>
        public static IDisposable BindToActive(this ReactiveProperty<bool> source, GameObject target)
        {
            if (source == null || target == null) return null;
            return source.Subscribe(v => target.SetActive(v));
        }

        /// <summary>将 ReadOnlyReactiveProperty 绑定到 GameObject 的 active 属性。</summary>
        public static IDisposable BindToActive(this ReadOnlyReactiveProperty<bool> source, GameObject target)
        {
            if (source == null || target == null) return null;
            return source.Subscribe(v => target.SetActive(v));
        }

        #endregion

        #region Button (Action 回调)

        /// <summary>将 Button 的点击事件绑定到 Action 回调。返回的 IDisposable 可用于取消绑定。</summary>
        public static IDisposable BindToClick(this Button button, Action onClick)
        {
            if (button == null || onClick == null) return null;
            var handler = new UnityEngine.Events.UnityAction(onClick);
            button.onClick.AddListener(handler);
            return R3.Disposable.Create(() => button.onClick.RemoveListener(handler));
        }

        #endregion

        #region Localization

        /// <summary>将 TMP_Text 绑定到本地化键值。语言切换时自动刷新文本。</summary>
        public static IDisposable BindToLocalizedText(this TMP_Text text, string localizationKey)
        {
            if (text == null || string.IsNullOrEmpty(localizationKey)) return null;

            // 设置初始文本
            text.text = LocalizationManager.Get(localizationKey);

            // 订阅语言变更消息，自动刷新
            return MessageManager.Subscribe<LanguageChangedMessage>(_ =>
                text.text = LocalizationManager.Get(localizationKey));
        }

        #endregion

        #region Generic (Custom Binding)

        /// <summary>自定义绑定。将 ReactiveProperty 值通过自定义 setter 同步到目标。</summary>
        public static IDisposable Bind<T>(this ReactiveProperty<T> source, Action<T> setter)
        {
            if (source == null || setter == null) return null;
            return source.Subscribe(setter);
        }

        /// <summary>自定义绑定。将 ReadOnlyReactiveProperty 值通过自定义 setter 同步到目标。</summary>
        public static IDisposable Bind<T>(this ReadOnlyReactiveProperty<T> source, Action<T> setter)
        {
            if (source == null || setter == null) return null;
            return source.Subscribe(setter);
        }

        #endregion
    }
}