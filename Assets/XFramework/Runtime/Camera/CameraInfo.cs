using System;

namespace XFramework.XCamera
{
    /// <summary>
    /// 单台已注册相机的诊断信息，由 <c>CopyCameras(List&lt;CameraInfo&gt;)</c> 填充。
    /// <para>它是快照：不作为身份，不随状态更新；需要实时状态请重新复制。</para>
    /// </summary>
    public readonly struct CameraInfo
    {
        /// <summary>相机句柄。</summary>
        public readonly CameraHandle Handle;

        /// <summary>复制时是否为 Brain 当前活动相机（过渡期间 incoming 也算）。</summary>
        public readonly bool IsActive;

        /// <summary>复制时是否为本管理器 pending 过渡的目标。</summary>
        public readonly bool IsTransitionTarget;

        /// <summary>后端对象是否仍存活（Unity 假 null 为 <c>false</c>）。</summary>
        public readonly bool IsAlive;

        /// <summary>是否为命名相机（<see cref="CameraHandle.NamedId"/> 非 <c>null</c>）。</summary>
        public bool IsNamed => Handle.NamedId != null;

        /// <summary>构造快照项。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <param name="isActive">复制时是否为 Brain 活动相机。</param>
        /// <param name="isTransitionTarget">复制时是否为 pending 目标。</param>
        /// <param name="isAlive">后端对象是否仍存活。</param>
        public CameraInfo(CameraHandle handle, bool isActive, bool isTransitionTarget, bool isAlive)
        {
            Handle = handle;
            IsActive = isActive;
            IsTransitionTarget = isTransitionTarget;
            IsAlive = isAlive;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var name = Handle.NamedId ?? "(动态)";
            return "CameraInfo(" + Handle.Id + ", " + Handle.Generation + ", " + name +
                   (IsActive ? ", active" : string.Empty) +
                   (IsTransitionTarget ? ", target" : string.Empty) +
                   (IsAlive ? string.Empty : ", dead") + ")";
        }
    }
}
