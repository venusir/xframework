using UnityEngine;

namespace XFramework.XUI
{
    /// <summary>
    /// 模态遮罩的样式。
    /// <para>刻意不用「0 表示未指定」这类哨兵：层级 <c>0</c> 是合法层级（<see cref="UILayers.Background"/>），
    /// 全透明遮罩也是合法需求（挡住输入但不显示任何东西）。需要默认样式时显式用 <see cref="Default"/>。</para>
    /// </summary>
    public struct UIMaskStyle
    {
        /// <summary>遮罩所在层级。决定它挡住哪些面板——见 <see cref="UISorting.MaskOrder"/>。</summary>
        public int Layer;

        /// <summary>遮罩颜色（含透明度）。<c>a = 0</c> 表示全透明，仍然挡输入。</summary>
        public Color Color;

        /// <summary>点击遮罩是否关闭显示栈顶部的面板。</summary>
        public bool ClickToClose;

        /// <summary>
        /// 构造遮罩样式。
        /// </summary>
        /// <param name="layer">遮罩层级，通常用 <see cref="UILayers.Mask"/>。</param>
        /// <param name="color">遮罩颜色（含透明度）。</param>
        /// <param name="clickToClose">点击遮罩是否关闭栈顶面板。</param>
        public UIMaskStyle(int layer, Color color, bool clickToClose = false)
        {
            Layer = layer;
            Color = color;
            ClickToClose = clickToClose;
        }

        /// <summary>
        /// 库默认样式：<see cref="UILayers.Mask"/> 层、半透明黑（0.5）、点击不关闭。
        /// </summary>
        public static UIMaskStyle Default
        {
            get { return new UIMaskStyle(UILayers.Mask, new Color(0f, 0f, 0f, 0.5f)); }
        }
    }
}
