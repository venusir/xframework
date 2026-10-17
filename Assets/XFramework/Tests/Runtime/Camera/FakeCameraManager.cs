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

        public CameraHandle ActiveHandle { get; set; }

        public bool IsTransitioning { get; set; }

        public bool Disposed { get; private set; }

        public CameraHandle RegisterResult { get; set; }

        public bool UnregisterResult { get; set; } = true;

        public object LastRegistered { get; private set; }

        public CameraHandle LastUnregistered { get; private set; }

        public string LastActivated { get; private set; }

        public string LastDeactivated { get; private set; }

        public string LastTransitionTarget { get; private set; }

        public float LastTransitionDuration { get; private set; }

        public CameraBlendStyle LastTransitionStyle { get; private set; }

        public CameraHandle LastActivatedHandle { get; private set; }

        public CameraHandle LastDeactivatedHandle { get; private set; }

        public CameraHandle LastTransitionHandle { get; private set; }

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
                   string.Equals(ActiveHandle.NamedId, cameraId, StringComparison.Ordinal);
        }

        public CameraHandle Register<T>(T handle) where T : class
        {
            LastRegistered = handle;
            return RegisterResult;
        }

        public bool Unregister(CameraHandle handle)
        {
            LastUnregistered = handle;
            return UnregisterResult;
        }

        public void Activate(CameraHandle handle)
        {
            LastActivatedHandle = handle;
        }

        public void Deactivate(CameraHandle handle)
        {
            LastDeactivatedHandle = handle;
        }

        public void TransitionTo(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            LastTransitionHandle = handle;
        }

        public bool IsActive(CameraHandle handle)
        {
            return handle == ActiveHandle;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
