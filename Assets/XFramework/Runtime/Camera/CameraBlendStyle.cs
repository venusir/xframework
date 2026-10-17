namespace XFramework.XCamera
{
    /// <summary>
    /// 相机过渡的曲线样式。
    /// <para>这是<b>后端中立</b>的提示值：默认实现按它选择缓动函数，Cinemachine 扩展包将它映射到
    /// Cinemachine 的混合样式。后端可以把它当作建议而不是强制契约——例如项目在 Brain 上配置了
    /// custom blends 时，Cinemachine 会以自己的配置为准。</para>
    /// </summary>
    public enum CameraBlendStyle
    {
        /// <summary>零时长切换（cut）。</summary>
        Cut,

        /// <summary>S 形曲线：起步与收尾都平滑。</summary>
        EaseInOut,

        /// <summary>缓入：起步慢，收尾线性。</summary>
        EaseIn,

        /// <summary>缓出：起步线性，收尾慢。</summary>
        EaseOut,

        /// <summary>线性插值。</summary>
        Linear,
    }
}
