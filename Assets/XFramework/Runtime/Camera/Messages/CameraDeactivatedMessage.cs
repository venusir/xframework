using XFramework.XCamera;

namespace XFramework.XCamera.Messages
{
    /// <summary>
    /// 相机反激活消息：一台已注册相机不再 live 时发布。
    /// <para>发布时机是 <c>CinemachineCore.CameraDeactivatedEvent</c>；未注册相机不发布本消息。</para>
    /// </summary>
    public readonly struct CameraDeactivatedMessage
    {
        /// <summary>不再 live 的相机句柄。</summary>
        public readonly CameraHandle Handle;

        /// <summary>构造消息。</summary>
        /// <param name="handle">相机句柄。</param>
        public CameraDeactivatedMessage(CameraHandle handle)
        {
            Handle = handle;
        }
    }
}
