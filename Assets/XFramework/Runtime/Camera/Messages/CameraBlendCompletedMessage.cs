namespace XFramework.XCamera.Messages
{
    /// <summary>
    /// 相机过渡完成消息：一台相机完成 blend-in（或作为 cut 被激活）时发布。
    /// <para>blend 在混合完成事件发布；cut 在激活的同一帧发布，视为零时长完成。未注册 / 外部相机
    /// 的 <see cref="CameraId"/> 为 <c>null</c>。</para>
    /// </summary>
    public readonly struct CameraBlendCompletedMessage
    {
        /// <summary>完成过渡的相机 id；外部 / 未注册相机为 <c>null</c>。</summary>
        public readonly string CameraId;

        /// <summary>该相机是否为外部 / 未注册相机。</summary>
        public bool IsExternal => CameraId == null;

        /// <summary>构造消息。</summary>
        /// <param name="cameraId">相机 id；外部传 <c>null</c>。</param>
        public CameraBlendCompletedMessage(string cameraId)
        {
            CameraId = cameraId;
        }
    }
}
