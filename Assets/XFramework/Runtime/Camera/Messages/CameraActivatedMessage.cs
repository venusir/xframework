namespace XFramework.XCamera.Messages
{
    /// <summary>
    /// 相机激活消息：输出驱动的 incoming 切换时发布。
    /// <para>发布时机是混合开始帧；cut 会在同一帧再发布一条
    /// <see cref="CameraBlendCompletedMessage"/>。未注册 / 外部 incoming 的
    /// <see cref="CameraId"/> 为 <c>null</c>（<see cref="IsExternal"/> 为 <c>true</c>）。</para>
    /// </summary>
    public readonly struct CameraActivatedMessage
    {
        /// <summary>incoming 相机 id；外部 / 未注册相机为 <c>null</c>。</summary>
        public readonly string CameraId;

        /// <summary>outgoing 相机 id；无 outgoing 或它未注册时为 <c>null</c>。</summary>
        public readonly string PreviousCameraId;

        /// <summary>本次切换是否为 cut（零时长）。</summary>
        public readonly bool IsCut;

        /// <summary>incoming 是否为外部 / 未注册相机。</summary>
        public bool IsExternal => CameraId == null;

        /// <summary>构造消息。</summary>
        /// <param name="cameraId">incoming 相机 id；外部传 <c>null</c>。</param>
        /// <param name="previousCameraId">outgoing 相机 id；未知传 <c>null</c>。</param>
        /// <param name="isCut">是否为 cut。</param>
        public CameraActivatedMessage(string cameraId, string previousCameraId, bool isCut)
        {
            CameraId = cameraId;
            PreviousCameraId = previousCameraId;
            IsCut = isCut;
        }
    }
}
