using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Controller
{
    /// <summary>
    /// 默认 UI 控制器。所有操作直接放行，仅输出调试日志。
    /// <para>当未通过 <see cref="XUI.UIManager.SetController(IUIController)"/> 设置自定义控制器时使用此默认实现。</para>
    /// </summary>
    public sealed class UIDefaultController : IUIController
    {
        #region IUIController Implementation

        public UniTask<bool> OnBeforeOpenAsync(Type panelType, string assetPath, int layer, object userData)
        {
            // 默认放行，无任何校验
            Debug.Log(
                $"[UIDefaultController] 允许打开面板: {panelType?.Name}, 资源路径: {assetPath}, 层级: {layer}");
            return UniTask.FromResult(true);
        }

        public UniTask OnAfterOpenAsync(Type panelType, UIPanelBase panel, object userData)
        {
            // 默认不做额外操作
            Debug.Log($"[UIDefaultController] 面板已打开: {panelType?.Name}");
            return UniTask.CompletedTask;
        }

        public UniTask<bool> OnBeforeCloseAsync(Type panelType, UIPanelBase panel, bool immediate)
        {
            // 默认放行，无任何校验
            Debug.Log(
                $"[UIDefaultController] 允许关闭面板: {panelType?.Name}, immediate: {immediate}");
            return UniTask.FromResult(true);
        }

        public UniTask OnAfterCloseAsync(Type panelType)
        {
            // 默认不做额外操作
            Debug.Log($"[UIDefaultController] 面板已关闭: {panelType?.Name}");
            return UniTask.CompletedTask;
        }

        public UniTask OnAllPanelsClosedAsync()
        {
            Debug.Log("[UIDefaultController] 所有面板已关闭");
            return UniTask.CompletedTask;
        }

        #endregion
    }
}