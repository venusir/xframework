using UnityEngine;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机模块的只读诊断快照：注册数量、活动相机、管理器过渡状态与 Brain 真实混合状态。
    /// <para>由 <c>GetState()</c> 返回；单次遍历、无额外分配。逐相机明细用
    /// <c>CopyCameras(List&lt;CameraInfo&gt;)</c>。</para>
    /// </summary>
    public readonly struct CameraStateSnapshot
    {
        /// <summary>已注册槽位数量（含已被外部销毁但尚未注销的槽位）。</summary>
        public readonly int RegisteredCount;

        /// <summary>命名相机数量。</summary>
        public readonly int NamedCount;

        /// <summary>动态相机数量。</summary>
        public readonly int DynamicCount;

        /// <summary>Brain 当前活动相机解析后的句柄；无活动 / 未注册为 <c>default</c>。</summary>
        public readonly CameraHandle ActiveHandle;

        /// <summary>本管理器 pending 过渡的目标；仅 <see cref="IsTransitioning"/> 时非 <c>default</c>。</summary>
        public readonly CameraHandle TransitionTarget;

        /// <summary>本管理器发起的非 cut 过渡是否在途。</summary>
        public readonly bool IsTransitioning;

        /// <summary>Brain 的 <c>IsBlending</c>（含 Timeline / 外部混合）。</summary>
        public readonly bool IsBrainBlending;

        /// <summary>管理器过渡进度（0..1）；无管理器过渡时为 0。</summary>
        public readonly float TransitionProgress;

        /// <summary>Brain 的输出相机；可能为 <c>null</c>。</summary>
        public readonly Camera OutputCamera;

        /// <summary>活动相机的命名 id；动态 / 无活动时为 <c>null</c>。</summary>
        public string ActiveCameraId => ActiveHandle.NamedId;

        /// <summary>构造快照。</summary>
        /// <param name="registeredCount">已注册槽位数量。</param>
        /// <param name="namedCount">命名相机数量。</param>
        /// <param name="dynamicCount">动态相机数量。</param>
        /// <param name="activeHandle">活动相机句柄。</param>
        /// <param name="transitionTarget">pending 过渡目标句柄。</param>
        /// <param name="isTransitioning">管理器过渡是否在途。</param>
        /// <param name="isBrainBlending">Brain 是否在混合。</param>
        /// <param name="transitionProgress">管理器过渡进度（0..1）。</param>
        /// <param name="outputCamera">Brain 输出相机。</param>
        public CameraStateSnapshot(int registeredCount, int namedCount, int dynamicCount,
            CameraHandle activeHandle, CameraHandle transitionTarget, bool isTransitioning,
            bool isBrainBlending, float transitionProgress, Camera outputCamera)
        {
            RegisteredCount = registeredCount;
            NamedCount = namedCount;
            DynamicCount = dynamicCount;
            ActiveHandle = activeHandle;
            TransitionTarget = transitionTarget;
            IsTransitioning = isTransitioning;
            IsBrainBlending = isBrainBlending;
            TransitionProgress = transitionProgress;
            OutputCamera = outputCamera;
        }
    }
}
