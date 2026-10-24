namespace XFramework.XCamera
{
    /// <summary>
    /// 相机过渡的曲线样式。
    /// <para>这是<b>后端中立</b>的请求值：Cinemachine 后端把它映射到
    /// <c>CinemachineBlendDefinition.Styles</c>，并通过 <c>CinemachineCore.GetBlendOverride</c>
    /// 让本次请求优先于项目的 <c>CustomBlends</c>。第三方后端可自行解释；无法表达的样式应按
    /// cut 或最接近的曲线降级，并在其文档中写明。</para>
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

        /// <summary>从 outgoing 平滑离开、末段硬切入 incoming（曲线前段平缓、末段陡峭）。</summary>
        HardIn,

        /// <summary>从 outgoing 硬切离开、末段平滑进入 incoming（曲线前段陡峭、末段平缓）。</summary>
        HardOut,
    }
}
