using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLog;
using XFramework.XLocalization;
using XFramework.XUI.Data;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 把定义资产绑定到面板预制体：文本、按钮动作、页签。
    /// <para>所有订阅/句柄登记到面板的 <c>Track</c>，由回池/销毁统一释放；本类不定义自清理路径。</para>
    /// </summary>
    internal sealed class UIDeclarativeBinder
    {
        private readonly UIViewBase _view;
        private readonly UIPanelDefinition _definition;

        internal UIDeclarativeBinder(UIViewBase view, UIPanelDefinition definition)
        {
            _view = view;
            _definition = definition;
        }

        internal void Bind(UIActionExecutor executor)
        {
            BindTexts();
            BindData();

            if (_view == null || _definition == null || _definition.ActionBindings == null)
                return;

            for (int i = 0; i < _definition.ActionBindings.Count; i++)
                BindAction(_definition.ActionBindings[i], executor);
        }

        /// <summary>
        /// 只绑定文本：HUD（2b）没有动作，面板/对话框经 <see cref="Bind"/> 顺带绑定按钮。
        /// </summary>
        internal void BindTexts()
        {
            if (_view == null || _definition == null || _definition.TextBindings == null)
                return;

            for (int i = 0; i < _definition.TextBindings.Count; i++)
                BindText(_definition.TextBindings[i]);
        }

        /// <summary>
        /// 绑定数据（阶段 3）：在静态文本之后执行，同节点冲突时数据绑定胜出。
        /// </summary>
        internal void BindData()
        {
            UIDeclarativeDataBinder.Bind(_view, _definition);
        }

        internal void SelectTab(string groupName, int index)
        {
            if (_definition?.TabGroups == null || string.IsNullOrEmpty(groupName))
                return;

            UITabGroup group = null;
            for (int i = 0; i < _definition.TabGroups.Count; i++)
            {
                if (_definition.TabGroups[i] != null
                    && string.Equals(_definition.TabGroups[i].GroupName, groupName, StringComparison.Ordinal))
                {
                    group = _definition.TabGroups[i];
                    break;
                }
            }

            if (group == null)
            {
                Warn("SelectTab: group '{0}' not found in definition.", groupName);
                return;
            }

            if (group.NodeNames == null || index < 0 || index >= group.NodeNames.Count)
            {
                Warn("SelectTab: index {0} out of range for group '{1}'.", index, groupName);
                return;
            }

            for (int i = 0; i < group.NodeNames.Count; i++)
            {
                Transform node = UIDeclarativeNodeFinder.Find(_view.transform, group.NodeNames[i]);
                if (node == null)
                {
                    Warn("SelectTab: node '{0}' not found.", group.NodeNames[i]);
                    continue;
                }

                node.gameObject.SetActive(i == index);
            }
        }

        internal void SetActive(string nodeName, bool active)
        {
            Transform node = UIDeclarativeNodeFinder.Find(_view.transform, nodeName);
            if (node == null)
            {
                Warn("SetActive: node '{0}' not found.", nodeName);
                return;
            }

            node.gameObject.SetActive(active);
        }

        private void BindText(UITextBinding binding)
        {
            if (binding == null || string.IsNullOrEmpty(binding.NodeName))
                return;

            Transform node = UIDeclarativeNodeFinder.Find(_view.transform, binding.NodeName);
            if (node == null)
            {
                Warn("Text binding: node '{0}' not found.", binding.NodeName);
                return;
            }

            TMP_Text tmp = node.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                BindTmpText(tmp, binding);
                return;
            }

            Text text = node.GetComponent<Text>();
            if (text != null)
            {
                BindUguiText(text, binding);
                return;
            }

            Warn("Text binding: node '{0}' has neither TMP_Text nor Text.", binding.NodeName);
        }

        private void BindTmpText(TMP_Text text, UITextBinding binding)
        {
            if (!binding.UseLocalization)
            {
                text.text = binding.Literal ?? string.Empty;
                return;
            }

            if (string.IsNullOrEmpty(binding.LocalizationKey))
            {
                Warn("Text binding: node '{0}' uses localization but has no key.", binding.NodeName);
                return;
            }

            try
            {
                // 显式传 owner：订阅会登记到面板 Track，回池时统一释放。
                text.BindToLocalizedText(binding.LocalizationKey, _view);
            }
            catch (Exception ex)
            {
                Warn("Text binding: localization failed for node '{0}': {1}", binding.NodeName, ex.Message);
            }
        }

        private void BindUguiText(Text text, UITextBinding binding)
        {
            if (!binding.UseLocalization)
            {
                text.text = binding.Literal ?? string.Empty;
                return;
            }

            if (string.IsNullOrEmpty(binding.LocalizationKey))
            {
                Warn("Text binding: node '{0}' uses localization but has no key.", binding.NodeName);
                return;
            }

            try
            {
                string key = binding.LocalizationKey;
                text.text = LocalizationManager.Get(key);
                IDisposable subscription = LocalizationManager.Subscribe(
                    _ => text.text = LocalizationManager.Get(key));
                _view.Track(subscription);
            }
            catch (Exception ex)
            {
                Warn("Text binding: localization failed for node '{0}': {1}", binding.NodeName, ex.Message);
            }
        }

        private void BindAction(UIActionBinding binding, UIActionExecutor executor)
        {
            if (binding == null || string.IsNullOrEmpty(binding.ButtonNodeName))
                return;

            Transform node = UIDeclarativeNodeFinder.Find(_view.transform, binding.ButtonNodeName);
            if (node == null)
            {
                Warn("Action binding: button node '{0}' not found.", binding.ButtonNodeName);
                return;
            }

            Button button = node.GetComponent<Button>();
            if (button == null)
            {
                Warn("Action binding: node '{0}' has no Button.", binding.ButtonNodeName);
                return;
            }

            IDisposable handle = button.BindToClick(() => executor.ExecuteAsync(binding).Forget());
            _view.Track(handle);
        }

        private static void Warn(string format, params object[] args)
        {
            // 显式 string.Format：直接传 params object[] 会被 LogManager 的单参泛型重载
            // Warning<T1>(category, template, T1) 抢走（T1 = object[]），格式串原样输出。
            LogManager.Warning(UIAuthoringLog.Category, string.Format(format, args));
        }
    }
}
