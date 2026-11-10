using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLog;
using XFramework.XReactive;
using XFramework.XUI.Data;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 把定义资产里的 <see cref="UIDataBinding"/> 绑定到面板 / HUD / 对话框的节点上。
    /// <para>只在打开时执行一次；所有订阅登记到视图的 <c>Track</c>，回池时由基类统一释放。
    /// 缺失 provider / key / 目标组件一律只告警、不抛异常，静态绑定照常生效。</para>
    /// </summary>
    internal static class UIDeclarativeDataBinder
    {
        /// <summary>
        /// 绑定定义里的全部数据绑定。
        /// <para>注册表为空时只告警一次（逐条告警会把一次配置缺失刷成 N 条噪音）。</para>
        /// </summary>
        internal static void Bind(UIViewBase view, UIPanelDefinition definition)
        {
            if (view == null || definition == null || definition.DataBindings == null)
                return;

            int count = definition.DataBindings.Count;
            if (count == 0)
                return;

            if (UIDataProviderRegistry.Count == 0)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "No IUIDataProvider registered; {0} data binding(s) skipped.", count);
                return;
            }

            for (int i = 0; i < count; i++)
                BindOne(view, definition.DataBindings[i]);
        }

        private static void BindOne(UIViewBase view, UIDataBinding binding)
        {
            if (binding == null || string.IsNullOrEmpty(binding.NodeName))
                return;

            if (string.IsNullOrEmpty(binding.DataKey))
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Data binding: node '{0}' has an empty data key.", binding.NodeName);
                return;
            }

            Transform node = UIDeclarativeNodeFinder.Find(view.transform, binding.NodeName);
            if (node == null)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Data binding: node '{0}' not found.", binding.NodeName);
                return;
            }

            switch (binding.Kind)
            {
                case UIDataBindingKind.Text:
                    BindText(view, node, binding);
                    return;

                case UIDataBindingKind.Sprite:
                    BindSprite(view, node, binding);
                    return;

                case UIDataBindingKind.Color:
                    BindColor(view, node, binding);
                    return;

                case UIDataBindingKind.FillAmount:
                    BindFillAmount(view, node, binding);
                    return;

                case UIDataBindingKind.Slider:
                    BindSlider(view, node, binding);
                    return;

                case UIDataBindingKind.Toggle:
                    BindToggle(view, node, binding);
                    return;

                case UIDataBindingKind.Active:
                    BindActive(view, node, binding);
                    return;

                default:
                    LogManager.Warning(UIAuthoringLog.Category,
                        "Data binding: unknown kind '{0}' for node '{1}'.", binding.Kind, binding.NodeName);
                    return;
            }
        }

        private static void BindText(UIViewBase view, Transform node, UIDataBinding binding)
        {
            if (!TryResolve(binding, out IReactiveProperty<string> source))
                return;

            Func<string, string> format = string.IsNullOrEmpty(binding.Format)
                ? null
                : value => string.Format(binding.Format, value);

            TMP_Text tmp = node.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                view.Track(source.BindToText(tmp, format));
                return;
            }

            Text text = node.GetComponent<Text>();
            if (text != null)
            {
                view.Track(source.Subscribe(value =>
                    text.text = format != null ? format(value) : value ?? string.Empty));
                return;
            }

            LogManager.Warning(UIAuthoringLog.Category,
                "Data binding: node '{0}' has neither TMP_Text nor Text.", binding.NodeName);
        }

        private static void BindSprite(UIViewBase view, Transform node, UIDataBinding binding)
        {
            if (!TryResolve(binding, out IReactiveProperty<Sprite> source))
                return;

            Image image = RequireImage(node, binding);
            if (image == null)
                return;

            view.Track(source.BindToSprite(image));
        }

        private static void BindColor(UIViewBase view, Transform node, UIDataBinding binding)
        {
            if (!TryResolve(binding, out IReactiveProperty<Color> source))
                return;

            Image image = RequireImage(node, binding);
            if (image == null)
                return;

            view.Track(source.Subscribe(value => image.color = value));
        }

        private static void BindFillAmount(UIViewBase view, Transform node, UIDataBinding binding)
        {
            if (!TryResolve(binding, out IReactiveProperty<float> source))
                return;

            Image image = RequireImage(node, binding);
            if (image == null)
                return;

            view.Track(source.BindToFillAmount(image));
        }

        private static void BindSlider(UIViewBase view, Transform node, UIDataBinding binding)
        {
            if (!TryResolve(binding, out IReactiveProperty<float> source))
                return;

            Slider slider = node.GetComponent<Slider>();
            if (slider == null)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Data binding: node '{0}' has no Slider.", binding.NodeName);
                return;
            }

            if (binding.TwoWay)
            {
                if (source is IReactivePropertyWriter<float> writer)
                {
                    view.Track(slider.BindTwoWay(writer));
                    return;
                }

                LogManager.Warning(UIAuthoringLog.Category,
                    "Data binding: key '{0}' is read-only; 'TwoWay' degraded to one-way for node '{1}'.",
                    binding.DataKey, binding.NodeName);
            }

            view.Track(source.BindToSlider(slider));
        }

        private static void BindToggle(UIViewBase view, Transform node, UIDataBinding binding)
        {
            if (!TryResolve(binding, out IReactiveProperty<bool> source))
                return;

            Toggle toggle = node.GetComponent<Toggle>();
            if (toggle == null)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Data binding: node '{0}' has no Toggle.", binding.NodeName);
                return;
            }

            if (binding.TwoWay)
            {
                if (source is IReactivePropertyWriter<bool> writer)
                {
                    view.Track(toggle.BindTwoWay(writer));
                    return;
                }

                LogManager.Warning(UIAuthoringLog.Category,
                    "Data binding: key '{0}' is read-only; 'TwoWay' degraded to one-way for node '{1}'.",
                    binding.DataKey, binding.NodeName);
            }

            view.Track(source.BindToToggle(toggle));
        }

        private static void BindActive(UIViewBase view, Transform node, UIDataBinding binding)
        {
            if (!TryResolve(binding, out IReactiveProperty<bool> source))
                return;

            view.Track(source.BindToActive(node.gameObject));
        }

        private static Image RequireImage(Transform node, UIDataBinding binding)
        {
            Image image = node.GetComponent<Image>();
            if (image == null)
                LogManager.Warning(UIAuthoringLog.Category,
                    "Data binding: node '{0}' has no Image.", binding.NodeName);

            return image;
        }

        private static bool TryResolve<T>(UIDataBinding binding, out IReactiveProperty<T> source)
        {
            if (UIDataProviderRegistry.TryGet(binding.DataKey, out source))
                return true;

            LogManager.Warning(UIAuthoringLog.Category,
                "Data binding: key '{0}' not found or type mismatch for node '{1}'.",
                binding.DataKey, binding.NodeName);
            return false;
        }
    }
}
