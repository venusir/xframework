namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 动作/导航内部使用的对话框结果。
    /// </summary>
    internal readonly struct UIAuthoringDialogResult
    {
        internal UIAuthoringDialogResult(UIAuthoringDialogStatus status, string resultId)
        {
            Status = status;
            ResultId = resultId;
        }

        internal UIAuthoringDialogStatus Status { get; }

        internal string ResultId { get; }
    }
}
