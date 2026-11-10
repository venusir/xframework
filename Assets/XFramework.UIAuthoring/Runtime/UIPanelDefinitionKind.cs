namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 定义的种类。
    /// <para>每个种类对应一个具体根组件，<c>Kind</c> 必须与预制体根组件一致。</para>
    /// </summary>
    public enum UIPanelDefinitionKind
    {
        /// <summary>普通面板（<c>UIDeclarativePanel</c>）。</summary>
        Panel = 0,

        /// <summary>世界空间 HUD（<c>UIDeclarativeHudItem</c>）。</summary>
        Hud = 1,

        /// <summary>模态对话框（<c>UIDeclarativeDialogPanel</c>）。</summary>
        Dialog = 2,

        /// <summary>锚定弹窗（<c>UIDeclarativePopupPanel</c>）。</summary>
        Popup = 3,
    }
}
