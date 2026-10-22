using XFramework.XCamera;

namespace XFramework.XCamera.Messages
{
    /// <summary>
    /// 相机激活消息：Brain 的 incoming 切换到一个已注册相机时发布。
    /// <para>发布时机是 <c>CinemachineCore.CameraActivatedEvent</c>（混合开始帧）；cut 会在同一帧再发布一条
    /// <see cref="CameraBlendCompletedMessage"/>。未注册的 incoming（Timeline / 外来 vcam）不发布本消息。</para>
    /// </summary>
    public readonly struct CameraActivatedMessage
    {
        /// <summary>incoming 相机的句柄。</summary>
        public readonly CameraHandle Handle;

        /// <summary>outgoing 相机的句柄；outgoing 未注册或为 cut 时可能为 <c>default</c>。</summary>
        public readonly CameraHandle PreviousHandle;

        /// <summary>本次切换是否为 cut（零时长）。</summary>
        public readonly bool IsCut;

        /// <summary>构造消息。</summary>
        /// <param name="handle">incoming 句柄。</param>
        /// <param name="previousHandle">outgoing 句柄；未知时传 <c>default</c>。</param>
        /// <param name="isCut">是否为 cut。</param>
        public CameraActivatedMessage(CameraHandle handle, CameraHandle previousHandle, bool isCut)
        {
            Handle = handle;
            PreviousHandle = previousHandle;
            IsCut = isCut;
        }
    }
}
