namespace XFramework.XUI
{
    /// <summary>
    /// UI 排序空间的唯一真相。所有 <c>sortingOrder</c> 都由这里推导，不要在别处硬编码。
    /// <para>整体是一段互不重叠的整数区间：面板层占前段，遮罩取其所在层的末位序，
    /// HUD / Tip / 系统层各占一个高位保留值。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不能随便取值：</b><see cref="UnityEngine.Canvas.sortingOrder"/> 在运行时是
    /// <b>16 位有符号量</b>，写入超出 <c>[-32768, 32767]</c> 的值会被截断回绕且<strong>不报任何错</strong>
    /// （实测 <c>100001 → -31071</c>、<c>500000 → -24288</c>）。因此整套取值必须落在这个范围内——
    /// 这正是本类存在的首要理由：没有单一出口，这个约束会被静默违反在各个角落。</para>
    /// <para><b>为什么面板不用递增计数器：</b>计数器随打开次数单调增长，且与显示栈构成两份次序真相。
    /// 现改为按栈位重排，序号恒等于栈内相对次序。</para>
    /// <para><b>为什么不改用 <c>sortingLayer</c>：</b>具名排序层需要先在 TagManager 里存在，框架无法
    /// 可移植地创建；且与子 Canvas 的 <c>overrideSorting</c> 组合会引入额外的调试成本。</para>
    /// </remarks>
    public static class UISorting
    {
        #region Constants

        /// <summary>
        /// 每个面板层级占用的区间宽度，也是层内可容纳的最大面板数加一。
        /// <para>取值受 16 位上限约束：<c>MaxPanelLayer * LayerStride + LayerStride - 1</c> 必须留在
        /// <see cref="MaxSortingOrder"/> 以内。当前为 <c>899 * 32 + 31 = 28799</c>。</para>
        /// </summary>
        public const int LayerStride = 32;

        /// <summary>层内可用的最大序号（第 32 个及以后会被钳制到它）。</summary>
        public const int MaxIndexInLayer = LayerStride - 1;

        /// <summary>
        /// 面板层可用上限。超过它会撞进 HUD / Tip / 系统保留带，使用处会告警并钳制。
        /// </summary>
        public const int MaxPanelLayer = 899;

        /// <summary>
        /// 遮罩在其所在层内的序号。取末位，于是遮罩挡住该层及以下的所有面板，
        /// 而被更高层的面板盖住——「系统提示盖在遮罩上」这类需求交给更高的层即可。
        /// </summary>
        public const int MaskIndex = MaxIndexInLayer;

        /// <summary>HUD 带。高于全部面板层，但低于 Tip。</summary>
        public const int HudOrder = 30000;

        /// <summary>Tip 带。高于 HUD（此前两者同值，覆盖顺序不确定）。</summary>
        public const int TipOrder = 31000;

        /// <summary>预留：新手引导挖洞层、全局加载遮罩。</summary>
        public const int SystemOrder = 32000;

        /// <summary>
        /// <see cref="UnityEngine.Canvas.sortingOrder"/> 的上限。超出的写入会被截断回绕且不报错。
        /// </summary>
        public const int MaxSortingOrder = 32767;

        /// <summary><see cref="UnityEngine.Canvas.sortingOrder"/> 的下限。</summary>
        public const int MinSortingOrder = -32768;

        #endregion

        #region Derivation

        /// <summary>
        /// 面板在其所在层内的排序值。
        /// </summary>
        /// <param name="layer">面板层级，见 <see cref="ClampPanelLayer"/>。</param>
        /// <param name="indexInLayer">层内序号，从 1 起，按显示栈自底向顶递增；会被钳制到
        /// <see cref="MaxIndexInLayer"/>。</param>
        public static int PanelOrder(int layer, int indexInLayer)
        {
            int index = indexInLayer < 0 ? 0
                : indexInLayer > MaxIndexInLayer ? MaxIndexInLayer
                : indexInLayer;

            return layer * LayerStride + index;
        }

        /// <summary>
        /// 遮罩的排序值。
        /// </summary>
        /// <param name="maskLayer">遮罩所在层级，见 <see cref="UILayers.Mask"/>。</param>
        public static int MaskOrder(int maskLayer)
        {
            return maskLayer * LayerStride + MaskIndex;
        }

        /// <summary>
        /// 把层级钳制到面板可用范围内。超出上限会撞进保留带，故钳制并告警。
        /// </summary>
        /// <param name="layer">调用方请求的层级。</param>
        /// <returns>可安全用于面板的层级。</returns>
        public static int ClampPanelLayer(int layer)
        {
            if (layer < 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[UIManager] Layer {layer} is below 0; clamped to 0.");
                return 0;
            }

            if (layer > MaxPanelLayer)
            {
                UnityEngine.Debug.LogWarning(
                    $"[UIManager] Layer {layer} exceeds the panel layer limit {MaxPanelLayer}; clamped. " +
                    $"Above that are the HUD/Tip/system reserved bands (see UISorting).");
                return MaxPanelLayer;
            }

            return layer;
        }

        #endregion
    }
}
