namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 支持的封闭动作集。
    /// <para>只含 UI 级动词；领域语义走 <see cref="Custom"/>。</para>
    /// </summary>
    public enum UIActionKind
    {
        /// <summary>关闭当前面板（必须最后一步）。</summary>
        CloseSelf = 0,

        /// <summary>关闭所有面板（连带 HUD/Tip；必须最后一步）。</summary>
        CloseAll = 1,

        /// <summary>关闭当前声明式面板并打开另一个声明式面板。</summary>
        Replace = 2,

        /// <summary>打开一个 C# 面板（目标不能是声明式面板或对话框）。</summary>
        Open = 3,

        /// <summary>压栈一个 C# 面板（目标不能是声明式面板或对话框）。</summary>
        Push = 4,

        /// <summary>退栈。</summary>
        Pop = 5,

        /// <summary>显示 Tip。</summary>
        ShowTip = 6,

        /// <summary>打开声明式对话框并等待结果。</summary>
        ShowDialog = 7,

        /// <summary>整层显隐。</summary>
        SetLayerVisibility = 8,

        /// <summary>整层交互。</summary>
        SetLayerInteractive = 9,

        /// <summary>子树显隐。</summary>
        SetActive = 10,

        /// <summary>页签切换。</summary>
        SelectTab = 11,

        /// <summary>发布 keyed 自定义消息。</summary>
        Custom = 12,

        /// <summary>设置对话框结果并关闭（仅 Dialog）。</summary>
        CloseWithResult = 13,

        /// <summary>隐藏 HUD 自身（仅 HUD；2a 校验器拒绝）。</summary>
        HideSelf = 14,
    }
}
