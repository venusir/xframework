using UnityEngine;

namespace XFramework.XUI
{
    /// <summary>
    /// Tip 显示配置。通过 <see cref="UITipManager.ShowTip"/> 传入控制显示行为。
    /// <para>所有字段均有默认值，可仅设置需要覆盖的字段。</para>
    /// </summary>
    public struct TipConfig
    {
        /// <summary>
        /// 世界坐标位置。为 null 时屏幕居中显示；否则将世界坐标转为屏幕坐标。
        /// </summary>
        public Vector3? WorldPos;

        /// <summary>
        /// 文字颜色。默认为白色。
        /// </summary>
        public Color Color;

        /// <summary>
        /// 显示时长（秒），默认 2 秒。
        /// </summary>
        public float Duration;

        /// <summary>
        /// 上飘距离（像素）。0 表示原地固定不动。默认 0。
        /// </summary>
        public float FloatDistance;

        /// <summary>
        /// 字号。0 表示使用预制体默认字号。默认 0。
        /// </summary>
        public float FontSize;

        /// <summary>
        /// 创建默认配置。
        /// </summary>
        public static TipConfig Default => new TipConfig
        {
            Color = Color.white,
            Duration = 2f,
            FloatDistance = 0f,
            FontSize = 0f,
            WorldPos = null
        };
    }
}