namespace XFramework.XCamera.Messages
{
    /// <summary>
    /// 相机反激活消息：一台相机不再 live 时发布。
    /// <para>发布时机是输出驱动的反激活事件（混合结束时才触发）。未注册 / 外部相机
    /// 的 <see cref="CameraId"/> 为 <c>null</c>。</para>
    /// </summary>
    public readonly struct CameraDeactivatedMessage
    {
        /// <summary>不再 live 的相机 id；外部 / 未注册相机为 <c>null</c>。</summary>
        public readonly string CameraId;

        /// <summary>该相机是否为外部 / 未注册相机。</summary>
        public bool IsExternal => CameraId == null;

        /// <summary>构造消息。</summary>
        /// <param name="cameraId">相机 id；外部传 <c>null</c>。</param>
        public CameraDeactivatedMessage(string cameraId)
        {
            CameraId = cameraId;
        }
    }
}
