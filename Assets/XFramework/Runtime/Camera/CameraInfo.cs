using System;

namespace XFramework.XCamera
{
    /// <summary>
    /// 单台已注册相机的诊断信息，由 <c>CameraManager.CopyCameras(List&lt;CameraInfo&gt;)</c> 填充。
    /// <para>它是快照：不作为身份，不随状态更新；需要实时状态请重新复制。</para>
    /// </summary>
    public readonly struct CameraInfo
    {
        /// <summary>相机 id。</summary>
        public readonly string CameraId;

        /// <summary>是否为运行时生成的动态 id（<c>@auto/N</c>）。</summary>
        public readonly bool IsGenerated;

        /// <summary>是否为相机栈的基础相机（<c>SwitchTo</c> 设定）。</summary>
        public readonly bool IsBase;

        /// <summary>是否位于相机栈中（基础或覆盖层）。</summary>
        public readonly bool IsInStack;

        /// <summary>复制时是否为当前活动相机（过渡期间 incoming 也算）。</summary>
        public readonly bool IsActive;

        /// <summary>复制时是否为本管理器在途过渡的目标。</summary>
        public readonly bool IsTransitionTarget;

        /// <summary>后端对象是否仍存活（Unity 假 null 为 <c>false</c>）。</summary>
        public readonly bool IsAlive;

        /// <summary>构造快照项。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="isGenerated">是否为生成 id。</param>
        /// <param name="isBase">是否为栈基础相机。</param>
        /// <param name="isInStack">是否在栈中。</param>
        /// <param name="isActive">是否为当前活动相机。</param>
        /// <param name="isTransitionTarget">是否为在途过渡目标。</param>
        /// <param name="isAlive">后端对象是否存活。</param>
        public CameraInfo(string cameraId, bool isGenerated, bool isBase, bool isInStack,
            bool isActive, bool isTransitionTarget, bool isAlive)
        {
            CameraId = cameraId;
            IsGenerated = isGenerated;
            IsBase = isBase;
            IsInStack = isInStack;
            IsActive = isActive;
            IsTransitionTarget = isTransitionTarget;
            IsAlive = isAlive;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "CameraInfo(" + (CameraId ?? "(null)") +
                   (IsBase ? ", base" : string.Empty) +
                   (IsInStack ? ", stack" : string.Empty) +
                   (IsActive ? ", active" : string.Empty) +
                   (IsTransitionTarget ? ", target" : string.Empty) +
                   (IsAlive ? string.Empty : ", dead") + ")";
        }
    }
}
