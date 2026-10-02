using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XBootstrap;
using XFramework.XPipeline;
using XFramework.XUI.Controller;

namespace XFramework.XUI
{

    /// <summary>
    /// <see cref="UIManager"/> 的引导阶段：把 UI 的初始化交给框架启动流程。
    /// <para>Phase = <see cref="BootstrapPhases.UI"/>（2），晚于 Asset——面板预制体经地址加载。</para>
    /// <para><b>已初始化则早退、且不接管</b>：<c>UIRootNode.Awake</c> 会自动初始化、使用方也可能手动
    /// <c>Initialize</c>；那两种情况下本阶段直接完成并标记「不是本阶段初始化的」，<see cref="Shutdown"/>
    /// 因此不会销毁别人的管理器（照 <c>LocalizationBootstrapStage</c> 的「谁初始化谁清理」先例）。</para>
    /// </summary>
    public sealed class UIBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly Transform _uiRoot;
        private readonly IUIController _controller;

        /// <summary>本阶段是否<b>确实</b>完成了初始化。清理据此决定要不要销毁（见类型注释）。</summary>
        private bool _initializedByThisStage;

        #endregion

        #region Construction

        /// <summary>构造引导阶段。</summary>
        /// <param name="uiRoot">UI 根节点（场景里的 Canvas 根，通常是 <c>UIRootNode</c> 的 transform）。</param>
        /// <param name="controller">可选的面板控制器；<c>null</c> 用默认实现。</param>
        /// <exception cref="ArgumentNullException"><paramref name="uiRoot"/> 为 null 时抛出。</exception>
        public UIBootstrapStage(Transform uiRoot, IUIController controller = null)
        {
            if (uiRoot == null)
                throw new ArgumentNullException(nameof(uiRoot));

            _uiRoot = uiRoot;
            _controller = controller;
        }

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = <see cref="BootstrapPhases.UI"/>（2），晚于 Asset。</summary>
        public int Phase => BootstrapPhases.UI;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>
        /// 初始化 UI 管理器。同步瞬时阶段（没有可取消的 await），忽略 <c>cancellationToken</c>。
        /// </summary>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing UI Manager...");

            if (UIManager.IsInitialized)
            {
                // 已被 UIRootNode.Awake / 使用方初始化：不重复调用（那只会打一条重复初始化告警），
                // 也不接管——Shutdown 时不销毁别人的管理器
                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
                return UniTask.CompletedTask;
            }

            UIManager.Initialize(_uiRoot, _controller);
            _initializedByThisStage = true;

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 反向清理：<b>只销毁本阶段自己初始化的那份</b>（空转与早退路径都不碰别人的管理器）。幂等。
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;
            UIManager.Destroy();
        }

        #endregion
    }
}
