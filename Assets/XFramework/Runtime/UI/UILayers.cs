namespace XFramework.XUI
{
    /// <summary>
    /// 推荐层级常量。数值越大越靠前，同层内按打开顺序（显示栈次序）排列。
    /// <para>这是一份**建议值**，不是框架约束：层级就是 <see cref="int"/>，项目可自由定义自己的常量。
    /// 但请勿超过 <see cref="UISorting.MaxPanelLayer"/>，那之上是遮罩/HUD/Tip 的保留带。</para>
    /// </summary>
    public static class UILayers
    {
        /// <summary>背景层（主界面背景等）。</summary>
        public const int Background = 0;

        /// <summary>默认层（大部分面板）。</summary>
        public const int Default = 100;

        /// <summary>弹出层（弹窗、确认框）。</summary>
        public const int Popup = 200;

        /// <summary>顶层（Toast、加载提示、系统消息）。</summary>
        public const int Top = 300;

        /// <summary>模态遮罩层。用 <c>UIManager.Mask.Show()</c> 的默认值。</summary>
        public const int Mask = 500;
    }
}
