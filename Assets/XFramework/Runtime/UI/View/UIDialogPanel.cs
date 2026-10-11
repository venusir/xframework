using Cysharp.Threading.Tasks;

namespace XFramework.XUI.View
{
    /// <summary>
    /// 对话框的结果通道。框架内部使用——让 <c>UIManagerImpl.RecyclePanel</c> 能在不接触
    /// <see cref="UIDialogPanel{TResult}"/> 泛型参数的前提下，于面板回池时兜底完成挂起的结果并复位。
    /// <para>刻意是 <c>internal</c>：它是框架与面板基类之间的实现契约，不是给使用方的扩展点。</para>
    /// </summary>
    internal interface IDialogResultChannel
    {
        /// <summary>
        /// 面板回池时调用：兜底完成仍未兑现的结果，并为下一次打开复位。
        /// <para>实现必须<b>幂等</b>——关闭路径与 <c>Dispose</c> 路径共用回池点。</para>
        /// </summary>
        void CompleteOnRecycle();
    }

    /// <summary>
    /// 对话框面板基类：在一次打开-关闭周期内，把用户的选择回传给等待方。
    /// <para>配合 <c>UIManager.ShowDialogAsync</c> 使用——该入口负责打开面板、挂上模态遮罩并等待结果。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>结果一定会兑现，这是本类存在的理由。</b>朴素做法（自己 new 一个
    /// <c>UniTaskCompletionSource</c> 塞进 <c>userData</c>、在确定/取消按钮回调里完成它）有一个
    /// 静默故障：面板被<b>非按钮途径</b>关闭时（<c>CloseAllAsync</c> 切场景、<c>CloseLayerAsync</c>、
    /// 管理器 <c>Dispose</c>），等待方<b>永远不会恢复</b>。本类把兜底挪到框架的回池点，于是
    /// 每一条关闭路径都会兑现结果。</para>
    /// <para><b>为什么兜底不在 <c>OnClose</c> 里</b>：<c>UIManagerImpl.Dispose</c> 同步回池全部面板，
    /// <b>刻意不走关闭路径</b>（它不 await 关闭动画），因此 <c>OnClose</c> 在那一路上根本不会被调用。
    /// 兜底挂在关闭与 <c>Dispose</c> 共用的回池出口，两条路才都覆盖得到。这与框架对「在途打开的等待者
    /// 不能悬着」的处理是同一个取向。</para>
    /// <para><b>回池会复位</b>：面板是池化复用的，不复位会让重开的同类型对话框立即返回<b>上一次</b>的结果。</para>
    /// <para><b>子类契约与 <see cref="UIPanelBase"/> 完全一致</b>——照常实现 <see cref="UIPanelBase.OnOpen"/>
    /// 与 <see cref="UIPanelBase.OnClose"/>，本类没有增加也没有封闭任何生命周期钩子。要在关闭时给出
    /// 一个领域默认值（如「取消」），在 <c>OnClose</c> 里调 <see cref="SetResult"/> 即可；
    /// 兜底在回池点、晚于 <c>OnClose</c>，故不会被抢先。</para>
    /// </remarks>
    /// <typeparam name="TResult">用户选择的结果类型（枚举、记录、可空对象均可）。</typeparam>
    public abstract class UIDialogPanel<TResult> : UIPanelBase, IDialogResultChannel
    {
        #region Result Channel

        /// <summary>挂起中的等待方。到关闭为止都没人等待时不分配。</summary>
        private UniTaskCompletionSource<TResult> _source;

        /// <summary>结果是否已兑现（无论是面板自己设的还是回池兜底的）。</summary>
        private bool _hasResult;

        /// <summary>已兑现的结果值。</summary>
        private TResult _result;

        /// <summary>
        /// 兑现结果。<b>幂等——先到者胜</b>：已经兑现过就什么也不做。
        /// <para>在确定/取消按钮的回调里调用它；也可以在 <see cref="UIPanelBase.OnClose"/> 里调用，
        /// 给「被别的途径关掉」补一个领域默认值（如 `<c>SetResult(ItemAction.Cancel)</c>`）。</para>
        /// </summary>
        /// <param name="result">要回传给等待方的结果。</param>
        public void SetResult(TResult result)
        {
            if (_hasResult)
                return;

            _hasResult = true;
            _result = result;

            // 此刻可能还没有等待方（面板在 OnOpen 里就兑现了结果）——那就只留值，
            // 等 WaitResultAsync 来取。反过来若已有等待方，TrySetResult 立即唤醒它
            _source?.TrySetResult(result);
        }

        /// <summary>
        /// 等待用户的选择。
        /// <para>已兑现则立即返回；否则挂起到面板关闭（含管理器 <c>Dispose</c>）——<b>不会永远等下去</b>。
        /// 关闭时仍未兑现则返回 <c>default(TResult)</c>。</para>
        /// </summary>
        /// <returns>结果。</returns>
        public UniTask<TResult> WaitResultAsync()
        {
            if (_hasResult)
                return UniTask.FromResult(_result);

            _source ??= new UniTaskCompletionSource<TResult>();
            return _source.Task;
        }

        /// <summary>
        /// 回池点：兜底兑现 + 为下一次打开复位。由 <c>UIManagerImpl.RecyclePanel</c> 调用。
        /// </summary>
        void IDialogResultChannel.CompleteOnRecycle()
        {
            // 兜底：走到这里说明本轮open-close周期结束了，等待方无论如何都该被唤醒。
            // SetResult 幂等，故面板已经给过结果时这里什么都不做
            SetResult(default);

            // 复位：面板进池，下次取出时必须是一张白纸——否则重开的对话框会立刻返回上一轮的结果
            _source = null;
            _hasResult = false;
            _result = default;
        }

        #endregion
    }
}
