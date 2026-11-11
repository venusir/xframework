using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 动作执行器依赖的 UI 运行时接缝。
    /// <para>生产实现是 <see cref="UIManagerActionRuntime"/>；测试注入 fake，避免依赖核心 internal 的工厂。</para>
    /// </summary>
    internal interface IUIActionRuntime
    {
        bool IsDeclarativePanelOpen { get; }

        UIDeclarativePanel GetDeclarativePanel();

        UniTask<UIDeclarativePanel> OpenDeclarativeAsync(string assetPath, int layer, CancellationToken cancellationToken);

        UniTask<UIPanelBase> OpenCSharpAsync(string assetPath, int layer, CancellationToken cancellationToken);

        UniTask<UIPanelBase> PushCSharpAsync(string assetPath, int layer, CancellationToken cancellationToken);

        /// <summary>
        /// 关闭当前声明式面板并打开目标声明式面板。
        /// <para>关闭会使当前 session 失效；因此「关闭后打开」必须在同一运行时方法内完成，
        /// 执行器不能在 <c>CloseAsync</c> 返回后自行继续。</para>
        /// <para><b>返回是否真的切换</b>：控制器可以拦下关闭（面板仍开着），此时不打开目标面板并返回
        /// <c>false</c>——调用方必须据此保留当前会话，不能把「面板还在、按钮全失效」留给使用方。</para>
        /// </summary>
        UniTask<bool> ReplaceDeclarativeAsync(UIPanelBase current, string targetAssetPath, int layer,
            CancellationToken cancellationToken);

        UniTask PopAsync(bool immediate, CancellationToken cancellationToken);

        /// <summary>
        /// 关闭指定面板。
        /// <para><b>返回是否真的关闭</b>：<c>IUIController.OnBeforeCloseAsync</c> 返回 <c>false</c> 时
        /// 核心只告警并保留面板，本方法返回 <c>false</c>。调用方据此决定会话是否失效。</para>
        /// </summary>
        UniTask<bool> CloseAsync(UIPanelBase panel, bool immediate, CancellationToken cancellationToken);

        UniTask CloseAllAsync(bool immediate, CancellationToken cancellationToken);

        UniTask ShowTipAsync(string text, TipConfig config, CancellationToken cancellationToken);

        bool IsDeclarativeDialogOpen { get; }

        UIDeclarativeDialogPanel GetDeclarativeDialog();

        /// <summary>
        /// 打开声明式对话框并等待结果。
        /// <para><b>取消会向调用方抛出 <see cref="System.OperationCanceledException"/></b>，与
        /// <see cref="UIAuthoringNavigation.OpenAsync"/> 同一口径；取消不是业务结果，不返回
        /// <c>OpenFailed</c>、也不发布结果消息。</para>
        /// </summary>
        UniTask<UIAuthoringDialogResult> ShowDeclarativeDialogAsync(string assetPath, string actionId,
            CancellationToken cancellationToken);

        bool IsDeclarativePopupOpen { get; }

        UIDeclarativePopupPanel GetDeclarativePopup();

        /// <summary>
        /// 打开声明式锚定弹窗。守卫（同地址重摆 / 异地址拒绝）由 <see cref="UIAuthoringNavigation"/>
        /// 统一承担；本方法是裸操作，生产实现直接转发核心 <c>UIManager.ShowPopupAsync</c>。
        /// </summary>
        UniTask<UIDeclarativePopupPanel> ShowDeclarativePopupAsync(RectTransform anchor,
            string assetPath, UIPopupDirection direction, UIPopupAlign align, bool closeOnClickOutside,
            CancellationToken cancellationToken);

        void SetLayerVisibility(int layer, bool visible);

        void SetLayerInteractive(int layer, bool interactive);
    }
}
