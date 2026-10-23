namespace XFramework.XCamera
{
    /// <summary>
    /// 异步相机命令（<c>SwitchToAsync</c> / <c>PushAsync</c> / <c>PopAsync</c> / <c>PopAllAsync</c>）的完成结果。
    /// <para>取消（<see cref="System.Threading.CancellationToken"/>）不返回结果，而是抛
    /// <see cref="System.OperationCanceledException"/>；它只停止等待，不中止相机过渡。</para>
    /// </summary>
    public enum CameraTransitionResult
    {
        /// <summary>目标成为活动相机且混合完成；cut 在激活时即完成。</summary>
        Completed = 0,

        /// <summary>在完成前被另一台相机 / 新请求 / 外部接管取代。</summary>
        Superseded = 1,

        /// <summary>目标 id 未知、已注销或相机已被外部销毁。</summary>
        CameraNotFound = 2,

        /// <summary>管理器被 <c>Destroy</c> 或发生会话复位。</summary>
        ManagerDisposed = 3,
    }
}
