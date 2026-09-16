using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XUI.View;

namespace XFramework.XUI.Controller
{
    /// <summary>
    /// UI 控制器接口。控制面板打开/关闭的额外业务逻辑。
    /// <para>示例如：权限校验 → 跳转登录面板 / 关闭前保存数据 / 面板打开后播放音效。</para>
    /// <para>由 <see cref="XUI.UIManager"/> 在面板打开/关闭流程中自动调用。</para>
    /// </summary>
    public interface IUIController
    {
        #region Open

        /// <summary>
        /// 面板打开前调用。返回 false 则取消本次打开操作。
        /// <para>此时面板尚未实例化，适合进行权限校验、资源检查等。</para>
        /// </summary>
        /// <param name="panelType">面板类型。</param>
        /// <param name="assetPath">面板资源的 YooAsset 地址。</param>
        /// <param name="layer">面板层级。</param>
        /// <param name="userData">调用 OpenAsync 时传入的自定义数据。</param>
        /// <param name="cancellationToken">调用方的取消令牌。异步校验应把它透传给自己的 await。</param>
        /// <returns>true 允许打开，false 拦截并取消打开。</returns>
        UniTask<bool> OnBeforeOpenAsync(Type panelType, string assetPath, int layer, object userData,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 面板打开后调用（面板已实例化、激活并完成 OnOpen）。
        /// <para>适合在面板完全就绪后执行额外逻辑，如播放音效、埋点上报。</para>
        /// </summary>
        /// <param name="panelType">面板类型。</param>
        /// <param name="panel">面板实例。</param>
        /// <param name="userData">调用 OpenAsync 时传入的自定义数据。</param>
        /// <param name="cancellationToken">调用方的取消令牌。此时面板已打开，取消不会再撤销它。</param>
        UniTask OnAfterOpenAsync(Type panelType, UIPanelBase panel, object userData,
            CancellationToken cancellationToken = default);

        #endregion

        #region Close

        /// <summary>
        /// 面板关闭前调用。返回 false 则取消本次关闭操作。
        /// <para>适合在关闭前进行数据保存确认、未保存提醒等。</para>
        /// </summary>
        /// <param name="panelType">面板类型。</param>
        /// <param name="panel">面板实例。</param>
        /// <param name="immediate">是否跳过关闭动画。</param>
        /// <param name="cancellationToken">调用方的取消令牌。返回 true 即视为提交，此后取消不再生效。</param>
        /// <returns>true 允许关闭，false 拦截并取消关闭。</returns>
        UniTask<bool> OnBeforeCloseAsync(Type panelType, UIPanelBase panel, bool immediate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 面板关闭后调用（面板已销毁）。
        /// <para>适合清理与该面板相关的全局状态。</para>
        /// </summary>
        /// <param name="panelType">面板类型。</param>
        /// <param name="cancellationToken">调用方的取消令牌。此时面板已回池，取消不会再撤销它。</param>
        UniTask OnAfterCloseAsync(Type panelType, CancellationToken cancellationToken = default);

        #endregion

        #region All Closed

        /// <summary>
        /// 所有面板关闭后调用（CloseAllAsync 之后）。
        /// </summary>
        /// <param name="cancellationToken">调用方的取消令牌。</param>
        UniTask OnAllPanelsClosedAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}