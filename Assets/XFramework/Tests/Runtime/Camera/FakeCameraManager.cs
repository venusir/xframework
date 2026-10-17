using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// <see cref="ICameraManager"/> 的测试替身：记录门面转发过来的调用，不碰场景。
    /// </summary>
    internal sealed class FakeCameraManager : ICameraManager, IDisposable
    {
        public Camera OutputCamera { get; set; }

        public IReadOnlyCollection<string> CameraIds { get; set; } = Array.Empty<string>();

        public string ActiveCameraId { get; set; }

        public bool IsTransitioning { get; set; }

        public bool Disposed { get; private set; }

        public string LastActivated { get; private set; }

        public string LastDeactivated { get; private set; }

        public string LastTransitionTarget { get; private set; }

        public float LastTransitionDuration { get; private set; }

        public CameraBlendStyle LastTransitionStyle { get; private set; }

        public void Activate(string cameraId)
        {
            LastActivated = cameraId;
        }

        public void Deactivate(string cameraId)
        {
            LastDeactivated = cameraId;
        }

        public void TransitionTo(string cameraId, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            LastTransitionTarget = cameraId;
            LastTransitionDuration = duration;
            LastTransitionStyle = style;
        }

        public bool IsActive(string cameraId)
        {
            return cameraId != null &&
                   string.Equals(ActiveCameraId, cameraId, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
