using UnityEngine;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机模块的只读诊断快照：注册数量、活动相机、栈状态、管理器过渡与输出驱动的真实混合状态。
    /// <para>由 <c>CameraManager.GetState()</c> 返回；逐相机明细用
    /// <c>CameraManager.CopyCameras(List&lt;CameraInfo&gt;)</c>。</para>
    /// </summary>
    public readonly struct CameraStateSnapshot
    {
        /// <summary>已注册槽位数量（含已被外部销毁但尚未注销的槽位）。</summary>
        public readonly int RegisteredCount;

        /// <summary>显式 id 相机数量（含已销毁未注销的槽位）。</summary>
        public readonly int NamedCount;

        /// <summary>生成 id 相机数量（含已销毁未注销的槽位）。</summary>
        public readonly int GeneratedCount;

        /// <summary>相机栈的基础相机 id（栈底起第一台存活相机）；栈空或全部项已销毁时为 <c>null</c>。</summary>
        /// <remarks>与 <see cref="CameraInfo.IsBase"/> 不同：后者是「栈底下标 0」的身份标记，可能是已销毁槽位。</remarks>
        public readonly string BaseCameraId;

        /// <summary>当前活动相机 id；无活动或活动相机未注册时为 <c>null</c>。</summary>
        public readonly string ActiveCameraId;

        /// <summary>输出驱动当前是否有活动相机（即使它未注册）。</summary>
        public readonly bool HasActiveCamera;

        /// <summary>相机栈深度（含基础相机与已销毁但尚未剪除的项）。</summary>
        public readonly int StackDepth;

        /// <summary>本管理器发起的非 cut 过渡是否在途。</summary>
        public readonly bool IsTransitioning;

        /// <summary>在途请求的目标 id；无请求时为 <c>null</c>。</summary>
        public readonly string TransitionTargetId;

        /// <summary>输出驱动的真实混合状态（含外部系统发起的混合）。</summary>
        public readonly bool IsBrainBlending;

        /// <summary>管理器过渡进度（0..1）；无管理器过渡时为 0。</summary>
        public readonly float TransitionProgress;

        /// <summary>输出驱动的活动相机是否不是本管理器请求的栈顶（外部接管）。</summary>
        public readonly bool IsExternallyControlled;

        /// <summary>输出驱动当前输出相机；可能为 <c>null</c>。</summary>
        public readonly Camera OutputCamera;

        /// <summary>构造快照。</summary>
        /// <param name="registeredCount">已注册槽位数量。</param>
        /// <param name="namedCount">显式 id 相机数量。</param>
        /// <param name="generatedCount">生成 id 相机数量。</param>
        /// <param name="baseCameraId">栈基础相机 id。</param>
        /// <param name="activeCameraId">活动相机 id。</param>
        /// <param name="hasActiveCamera">输出驱动是否有活动相机。</param>
        /// <param name="stackDepth">栈深度。</param>
        /// <param name="isTransitioning">管理器非 cut 过渡是否在途。</param>
        /// <param name="transitionTargetId">在途请求目标 id。</param>
        /// <param name="isBrainBlending">输出驱动是否在混合。</param>
        /// <param name="transitionProgress">管理器过渡进度。</param>
        /// <param name="isExternallyControlled">是否被外部系统接管。</param>
        /// <param name="outputCamera">输出相机。</param>
        public CameraStateSnapshot(int registeredCount, int namedCount, int generatedCount,
            string baseCameraId, string activeCameraId, bool hasActiveCamera, int stackDepth,
            bool isTransitioning, string transitionTargetId, bool isBrainBlending,
            float transitionProgress, bool isExternallyControlled, Camera outputCamera)
        {
            RegisteredCount = registeredCount;
            NamedCount = namedCount;
            GeneratedCount = generatedCount;
            BaseCameraId = baseCameraId;
            ActiveCameraId = activeCameraId;
            HasActiveCamera = hasActiveCamera;
            StackDepth = stackDepth;
            IsTransitioning = isTransitioning;
            TransitionTargetId = transitionTargetId;
            IsBrainBlending = isBrainBlending;
            TransitionProgress = transitionProgress;
            IsExternallyControlled = isExternallyControlled;
            OutputCamera = outputCamera;
        }
    }
}
