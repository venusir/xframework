using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XMessage;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 执行一条动作链。串行执行；同一 session 同时只允许一条链。
    /// </summary>
    internal sealed class UIActionExecutor
    {
        private readonly UIActionSession _session;
        private readonly UIDeclarativeBinder _binder;
        private bool _isExecuting;

        internal UIActionExecutor(UIActionSession session, UIPanelDefinition definition, UIDeclarativeBinder binder)
        {
            _session = session;
            _binder = binder;
        }

        internal UniTask ExecuteAsync(UIActionBinding binding)
        {
            if (binding == null || binding.Actions == null || binding.Actions.Count == 0)
                return UniTask.CompletedTask;

            if (!_session.IsValid)
                return UniTask.CompletedTask;

            if (_isExecuting)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Action chain ignored: another chain is already running on this panel.");
                return UniTask.CompletedTask;
            }

            _isExecuting = true;
            return ExecuteCoreAsync(binding);
        }

        private async UniTask ExecuteCoreAsync(UIActionBinding binding)
        {
            try
            {
                for (int i = 0; i < binding.Actions.Count; i++)
                {
                    if (!_session.IsValid)
                        return;

                    await ExecuteOneAsync(binding.Actions[i]);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(UIAuthoringLog.Category, "Action chain failed: {0}", ex);
            }
            finally
            {
                _isExecuting = false;
            }
        }

        private async UniTask ExecuteOneAsync(UIAction action)
        {
            if (action == null)
                return;

            switch (action.Kind)
            {
                case UIActionKind.CloseSelf:
                    await _session.Runtime.CloseAsync(_session.Panel, false, default);
                    _session.Invalidate();
                    return;

                case UIActionKind.CloseAll:
                    await _session.Runtime.CloseAllAsync(false, default);
                    return;

                case UIActionKind.Replace:
                    await _session.Runtime.ReplaceDeclarativeAsync(
                        _session.Panel, action.AssetPath, action.Layer, default);
                    _session.Invalidate();
                    return;

                case UIActionKind.Open:
                    await _session.Runtime.OpenCSharpAsync(action.AssetPath, action.Layer, default);
                    return;

                case UIActionKind.Push:
                    await _session.Runtime.PushCSharpAsync(action.AssetPath, action.Layer, default);
                    return;

                case UIActionKind.Pop:
                    await _session.Runtime.PopAsync(false, default);
                    return;

                case UIActionKind.ShowTip:
                    {
                        UITipActionConfig tipConfig = action.TipConfig ?? new UITipActionConfig();
                        await _session.Runtime.ShowTipAsync(action.Text, tipConfig.ToTipConfig(), default);
                        return;
                    }

                case UIActionKind.SetLayerVisibility:
                    _session.Runtime.SetLayerVisibility(action.Layer, action.BoolValue);
                    return;

                case UIActionKind.SetLayerInteractive:
                    _session.Runtime.SetLayerInteractive(action.Layer, action.BoolValue);
                    return;

                case UIActionKind.SetActive:
                    _binder.SetActive(action.NodeName, action.BoolValue);
                    return;

                case UIActionKind.SelectTab:
                    _binder.SelectTab(action.GroupName, action.Index);
                    return;

                case UIActionKind.Custom:
                    if (string.IsNullOrEmpty(action.ActionId))
                    {
                        LogManager.Warning(UIAuthoringLog.Category, "Custom action has an empty actionId.");
                        return;
                    }

                    MessageManager.Publish<string, UIAuthoringActionMessage>(
                        action.ActionId,
                        new UIAuthoringActionMessage(action.ActionId, action.Payload));
                    return;

                case UIActionKind.CloseWithResult:
                    {
                        if (_session.Panel is UIDeclarativeDialogPanel dialog)
                        {
                            await dialog.CloseWithResultAsync(action.ResultId);
                            _session.Invalidate();
                        }
                        else
                        {
                            LogManager.Warning(UIAuthoringLog.Category,
                                "CloseWithResult requires a declarative dialog.");
                        }
                        return;
                    }

                case UIActionKind.ShowDialog:
                    await _session.Runtime.ShowDeclarativeDialogAsync(
                        action.AssetPath, action.ActionId, default);
                    return;

                case UIActionKind.ShowPopup:
                    await ShowPopupAsync(action);
                    return;

                case UIActionKind.HideSelf:
                    LogManager.Warning(UIAuthoringLog.Category, "HideSelf is only valid for HUD.");
                    return;

                default:
                    LogManager.Warning(UIAuthoringLog.Category,
                        "Unknown action kind '{0}'.", action.Kind);
                    return;
            }
        }

        /// <summary>
        /// 打开声明式弹窗：锚点取自当前面板子树；失败只告警，不中断/半拆面板。
        /// </summary>
        private async UniTask ShowPopupAsync(UIAction action)
        {
            if (string.IsNullOrEmpty(action.AnchorNodeName))
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "ShowPopup: anchor node name is empty.");
                return;
            }

            Transform anchor = UIDeclarativeNodeFinder.Find(
                _session.Panel.transform, action.AnchorNodeName);
            if (anchor == null)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "ShowPopup: anchor node '{0}' not found.", action.AnchorNodeName);
                return;
            }

            if (!(anchor is RectTransform rect))
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "ShowPopup: anchor node '{0}' has no RectTransform.", action.AnchorNodeName);
                return;
            }

            UIDeclarativePopupPanel popup = await UIAuthoringNavigation.ShowPopupAsync(
                _session.Runtime, rect, action.AssetPath, action.Direction, action.Align,
                action.CloseOnClickOutside, default);

            if (popup == null)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "ShowPopup: opening '{0}' failed or was rejected.", action.AssetPath);
            }
        }
    }
}
