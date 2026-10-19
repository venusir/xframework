using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUI.View;

namespace XFramework.XUI.Controller
{
    /// <summary>
    /// 默认 UI 控制器。所有操作直接放行。
    /// <para>当未通过 <see cref="XUI.UIManager.SetController(IUIController)"/> 设置自定义控制器时使用此默认实现。</para>
    /// <para><b>默认静默</b>：拦截流程默认不产生日志；需要观察时用
    /// <c>new UIDefaultController(verbose: true)</c>，verbose 模式下每次拦截打一条模板化日志
    /// （写模板 + 参数，不预先插值）。</para>
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

        /// <inheritdoc/>
        public UniTask<bool> OnBeforeOpenAsync(Type panelType, string assetPath, int layer, object userData,
            CancellationToken cancellationToken = default)
        {
            if (_verbose)
            {
                LogManager.Info(LogCategories.UIDefaultController,
                    "允许打开面板: {0}, 资源路径: {1}, 层级: {2}", panelType?.Name, assetPath, layer);
            }

            return UniTask.FromResult(true);
        }

        /// <inheritdoc/>
        public UniTask OnAfterOpenAsync(Type panelType, UIPanelBase panel, object userData,
            CancellationToken cancellationToken = default)
        {
            if (_verbose)
                LogManager.Info(LogCategories.UIDefaultController, "面板已打开: {0}", panelType?.Name);

            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        public UniTask<bool> OnBeforeCloseAsync(Type panelType, UIPanelBase panel, bool immediate,
            CancellationToken cancellationToken = default)
        {
            if (_verbose)
            {
                LogManager.Info(LogCategories.UIDefaultController,
                    "允许关闭面板: {0}, immediate: {1}", panelType?.Name, immediate);
            }

            return UniTask.FromResult(true);
        }

        /// <inheritdoc/>
        public UniTask OnAfterCloseAsync(Type panelType, CancellationToken cancellationToken = default)
        {
            if (_verbose)
                LogManager.Info(LogCategories.UIDefaultController, "面板已关闭: {0}", panelType?.Name);

            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        public UniTask OnAllPanelsClosedAsync(CancellationToken cancellationToken = default)
        {
            if (_verbose)
                LogManager.Info(LogCategories.UIDefaultController, "所有面板已关闭");

            return UniTask.CompletedTask;
        }

        #endregion
    }
}