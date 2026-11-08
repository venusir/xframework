namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// <see cref="UIActionKind.ShowDialog"/> 发布的结果消息。
    /// <para>不带面板引用；项目按 <see cref="ActionId"/> 订阅。</para>
    /// </summary>
    public readonly struct UIAuthoringDialogResultMessage
    {
        /// <summary>动作 ID（发布时的 key）。</summary>
        public readonly string ActionId;

        /// <summary>结果状态。</summary>
        public readonly UIAuthoringDialogStatus Status;

        /// <summary>按钮结果 ID；仅 <see cref="UIAuthoringDialogStatus.Selected"/> 时有效。</summary>
        public readonly string ResultId;

        /// <summary>创建一条对话框结果消息。</summary>
        public UIAuthoringDialogResultMessage(string actionId, UIAuthoringDialogStatus status, string resultId)
        {
            ActionId = actionId;
            Status = status;
            ResultId = resultId;
        }
    }
}
