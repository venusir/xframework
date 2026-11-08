namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 定义的种类。
    /// <para><see cref="Hud"/> 预留给后续阶段；2a 只实现 <see cref="Panel"/> 与 <see cref="Dialog"/>。</para>
    /// </summary>
    public enum UIPanelDefinitionKind
    {
        /// <summary>普通面板（<c>UIDeclarativePanel</c>）。</summary>
        Panel = 0,

        /// <summary>世界空间 HUD（<c>UIDeclarativeHudItem</c>，后续阶段）。</summary>
        Hud = 1,

        /// <summary>模态对话框（<c>UIDeclarativeDialogPanel</c>）。</summary>
        Dialog = 2,
    }
}
