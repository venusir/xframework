namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式对话框的结果状态。
    /// <para>2a 不区分「父面板关闭」与「用户取消」，统一为 <see cref="Canceled"/>。</para>
    /// </summary>
    public enum UIAuthoringDialogStatus
    {
        /// <summary>用户做出了选择；<c>ResultId</c> 有效。</summary>
        Selected = 0,

        /// <summary>已打开但未选择就关闭。</summary>
        Canceled = 1,

        /// <summary>没有成功打开（资源加载失败、被控制器拦下、已有对话框在途）。</summary>
        OpenFailed = 2,
    }
}
