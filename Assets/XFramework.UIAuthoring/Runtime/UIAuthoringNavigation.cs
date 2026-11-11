using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XMessage;
using XFramework.XUI;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 的推荐入口：单根守卫 + 对话框单飞。
    /// <para>直接调用 <c>UIManager.OpenAsync&lt;UIDeclarativePanel&gt;</c> 无法阻止第二个地址；
    /// 核心会按具体类型去重并静默返回第一个面板。</para>
    /// </summary>
    public static class UIAuthoringNavigation
    {
        /// <summary>
        /// 打开声明式面板。已有声明式面板时：同地址重聚焦，异地址拒绝并 Warning。
        /// </summary>
        public static UniTask<UIDeclarativePanel> OpenAsync(
            string assetPath,
            int layer = UILayers.Default,
            CancellationToken cancellationToken = default)
        {
            return OpenAsync(new UIManagerActionRuntime(), assetPath, layer, cancellationToken);
        }

        internal static async UniTask<UIDeclarativePanel> OpenAsync(
            IUIActionRuntime runtime,
            string assetPath,
            int layer,
            CancellationToken cancellationToken)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));

            if (string.IsNullOrEmpty(assetPath))
            {
                LogManager.Warning(UIAuthoringLog.Category, "OpenAsync: assetPath is empty.");
                return null;
            }

            UIDeclarativePanel existing = runtime.GetDeclarativePanel();
            if (existing != null)
            {
                if (string.Equals(existing.AssetPath, assetPath, StringComparison.Ordinal))
                    return await runtime.OpenDeclarativeAsync(assetPath, layer, cancellationToken);

                LogManager.Warning(UIAuthoringLog.Category,
                    "OpenAsync rejected: a different declarative panel is already open ('{0}').",
                    existing.AssetPath);
                return existing;
            }

            return await runtime.OpenDeclarativeAsync(assetPath, layer, cancellationToken);
        }

        /// <summary>
        /// 打开声明式对话框并等待结果状态；结果详情由 <see cref="UIAuthoringDialogResultMessage"/> 发布。
        /// </summary>
        public static UniTask<UIAuthoringDialogStatus> ShowDialogAsync(
            string assetPath,
            string actionId,
            CancellationToken cancellationToken = default)
        {
            return ShowDialogAsync(new UIManagerActionRuntime(), assetPath, actionId, cancellationToken);
        }

        internal static async UniTask<UIAuthoringDialogStatus> ShowDialogAsync(
            IUIActionRuntime runtime,
            string assetPath,
            string actionId,
            CancellationToken cancellationToken)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));

            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(actionId))
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "ShowDialogAsync: assetPath or actionId is empty.");

                // 与 UIManagerActionRuntime 的同一分支保持可观测性一致：有 actionId 就发布失败结果
                if (!string.IsNullOrEmpty(actionId))
                {
                    MessageManager.Publish<string, UIAuthoringDialogResultMessage>(
                        actionId,
                        new UIAuthoringDialogResultMessage(
                            actionId, UIAuthoringDialogStatus.OpenFailed, null));
                }
                return UIAuthoringDialogStatus.OpenFailed;
            }

            UIAuthoringDialogResult result = await runtime.ShowDeclarativeDialogAsync(
                assetPath, actionId, cancellationToken);
            return result.Status;
        }

        /// <summary>
        /// 打开声明式锚定弹窗。已有声明式弹窗时：同地址重聚焦并重摆到新锚点，异地址拒绝并 Warning。
        /// <para>可同时打开一个声明式面板、一个声明式对话框与一个声明式弹窗（三者是不同具体类型）。</para>
        /// </summary>
        public static UniTask<UIDeclarativePopupPanel> ShowPopupAsync(
            RectTransform anchor,
            string assetPath,
            UIPopupDirection direction = UIPopupDirection.Auto,
            UIPopupAlign align = UIPopupAlign.Start,
            bool closeOnClickOutside = true,
            CancellationToken cancellationToken = default)
        {
            return ShowPopupAsync(new UIManagerActionRuntime(), anchor, assetPath,
                direction, align, closeOnClickOutside, cancellationToken);
        }

        internal static async UniTask<UIDeclarativePopupPanel> ShowPopupAsync(
            IUIActionRuntime runtime,
            RectTransform anchor,
            string assetPath,
            UIPopupDirection direction,
            UIPopupAlign align,
            bool closeOnClickOutside,
            CancellationToken cancellationToken)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));

            if (anchor == null)
            {
                LogManager.Warning(UIAuthoringLog.Category, "ShowPopupAsync: anchor is null.");
                return null;
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                LogManager.Warning(UIAuthoringLog.Category, "ShowPopupAsync: assetPath is empty.");
                return null;
            }

            UIDeclarativePopupPanel existing = runtime.GetDeclarativePopup();
            if (existing != null)
            {
                if (string.Equals(existing.AssetPath, assetPath, StringComparison.Ordinal))
                {
                    return await runtime.ShowDeclarativePopupAsync(anchor, assetPath, direction, align,
                        closeOnClickOutside, cancellationToken);
                }

                LogManager.Warning(UIAuthoringLog.Category,
                    "ShowPopupAsync rejected: a different declarative popup is already open ('{0}').",
                    existing.AssetPath);
                return existing;
            }

            return await runtime.ShowDeclarativePopupAsync(anchor, assetPath, direction, align,
                closeOnClickOutside, cancellationToken);
        }
    }
}
