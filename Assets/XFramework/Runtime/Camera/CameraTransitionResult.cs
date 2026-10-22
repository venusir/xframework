namespace XFramework.XCamera
{
    /// <summary>
    /// <c>TransitionToAsync</c> 的完成结果。
    /// <para>取消（<see cref="System.Threading.CancellationToken"/>）不返回结果，而是抛
    /// <see cref="System.OperationCanceledException"/>；它只停止等待，不中止相机过渡。</para>
    /// </summary>
    public enum CameraTransitionResult
    {
        /// <summary>目标成为活动相机且混合完成；cut 在激活时即完成。</summary>
        Completed = 0,

        /// <summary>在完成前被另一台相机 / 新请求取代，或目标被 <c>Deactivate</c>。</summary>
        Superseded = 1,

        /// <summary>目标被注销、外部销毁，或提交时句柄已无效。</summary>
        TargetUnregistered = 2,

        /// <summary>管理器被 <c>Dispose</c> 或发生会话复位。</summary>
        ManagerDisposed = 3,
    }
}
