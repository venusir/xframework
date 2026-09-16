using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Controller
{
    /// <summary>
    /// 默认 UI 控制器。所有操作直接放行。
    /// <para>当未通过 <see cref="XUI.UIManager.Panel.SetController(IUIController)"/> 设置自定义控制器时使用此默认实现。</para>
    /// <para><b>默认静默</b>：每次拦截都会打日志，意味着每开一个面板就是五条带字符串插值的日志——
    /// 在生产环境纯属噪音。需要观察拦截流程时用 <c>new UIDefaultController(verbose: true)</c>。</para>
    /// </summary>
    public sealed class UIDefaultController : IUIController
    {
        #region Fields

        private readonly bool _verbose;

        #endregion

        #region Construction

        /// <summary>
        /// 创建默认控制器。
        /// </summary>
        /// <param name="verbose">为 true 时输出每次拦截的日志，用于排查生命周期流程。默认 false（静默）。</param>
        public UIDefaultController(bool verbose = false)
        {
            _verbose = verbose;
        }

        #endregion

        #region IUIController Implementation

        public UniTask<bool> OnBeforeOpenAsync(Type panelType, string assetPath, int layer, object userData,
            CancellationToken cancellationToken = default)
        {
            if (_verbose)
            {
                Debug.Log(
                    $"[UIDefaultController] 允许打开面板: {panelType?.Name}, 资源路径: {assetPath}, 层级: {layer}");
            }

            return UniTask.FromResult(true);
        }

        public UniTask OnAfterOpenAsync(Type panelType, UIPanelBase panel, object userData,
            CancellationToken cancellationToken = default)
        {
            if (_verbose)
                Debug.Log($"[UIDefaultController] 面板已打开: {panelType?.Name}");

            return UniTask.CompletedTask;
        }

        public UniTask<bool> OnBeforeCloseAsync(Type panelType, UIPanelBase panel, bool immediate,
            CancellationToken cancellationToken = default)
        {
            if (_verbose)
            {
                Debug.Log(
                    $"[UIDefaultController] 允许关闭面板: {panelType?.Name}, immediate: {immediate}");
            }

            return UniTask.FromResult(true);
        }

        public UniTask OnAfterCloseAsync(Type panelType, CancellationToken cancellationToken = default)
        {
            if (_verbose)
                Debug.Log($"[UIDefaultController] 面板已关闭: {panelType?.Name}");

            return UniTask.CompletedTask;
        }

        public UniTask OnAllPanelsClosedAsync(CancellationToken cancellationToken = default)
        {
            if (_verbose)
                Debug.Log("[UIDefaultController] 所有面板已关闭");

            return UniTask.CompletedTask;
        }

        #endregion
    }
}