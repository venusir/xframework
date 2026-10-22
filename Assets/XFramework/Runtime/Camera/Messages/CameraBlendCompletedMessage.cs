using XFramework.XCamera;

namespace XFramework.XCamera.Messages
{
    /// <summary>
    /// 相机过渡完成消息：一台已注册相机完成 blend-in（或作为 cut 被激活）时发布。
    /// <para>blend 在 <c>CinemachineCore.BlendFinishedEvent</c> 发布；cut 在 <c>CameraActivatedEvent</c> 的同一帧发布，
    /// 视为零时长完成。未注册相机不发布本消息。</para>
    /// </summary>
    public readonly struct CameraBlendCompletedMessage
    {
        /// <summary>完成过渡的相机句柄。</summary>
        public readonly CameraHandle Handle;

        /// <summary>构造消息。</summary>
        /// <param name="handle">相机句柄。</param>
        public CameraBlendCompletedMessage(CameraHandle handle)
        {
            Handle = handle;
        }
    }
}
