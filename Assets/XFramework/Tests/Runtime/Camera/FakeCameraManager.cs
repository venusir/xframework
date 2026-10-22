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

        public CameraHandle ActiveHandle { get; set; }

        public bool IsTransitioning { get; set; }

        public int CameraCount { get; set; }

        public Dictionary<string, CameraHandle> NamedHandles { get; } =
            new Dictionary<string, CameraHandle>(StringComparer.Ordinal);

        public bool Disposed { get; private set; }

        /// <summary>为 true 时 Dispose 抛异常，用于验证门面的闩锁顺序（CAM-002）。</summary>
        public bool ThrowOnDispose { get; set; }

        public CameraHandle RegisterResult { get; set; }

        public bool UnregisterResult { get; set; } = true;

        public object LastRegistered { get; private set; }

        public string LastRegisteredId { get; private set; }

        public CameraHandle LastUnregistered { get; private set; }

        public CameraHandle LastActivatedHandle { get; private set; }

        public CameraHandle LastDeactivatedHandle { get; private set; }

        public CameraHandle LastTransitionHandle { get; private set; }

        public float LastTransitionDuration { get; private set; }

        public CameraBlendStyle LastTransitionStyle { get; private set; }

        public bool TryGetHandle(string cameraId, out CameraHandle handle)
        {
            return NamedHandles.TryGetValue(cameraId, out handle);
        }

        public CameraHandle Register<T>(string cameraId, T camera) where T : class
        {
            LastRegisteredId = cameraId;
            LastRegistered = camera;
            return RegisterResult;
        }

        public CameraHandle Register<T>(T camera) where T : class
        {
            LastRegisteredId = null;
            LastRegistered = camera;
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
            LastTransitionDuration = duration;
            LastTransitionStyle = style;
        }

        public bool IsActive(CameraHandle handle)
        {
            return handle == ActiveHandle;
        }

        public void Dispose()
        {
            Disposed = true;
            if (ThrowOnDispose)
                throw new InvalidOperationException("dispose boom");
        }
    }
}
