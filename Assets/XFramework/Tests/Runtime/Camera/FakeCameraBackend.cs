using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// <see cref="ICameraBackend"/> 的测试替身：记录导演层 / 门面转发过来的调用，不碰场景。
    /// <para>只实现提交 1 的核心原语；事件、异步与玩法绑定随后续提交补充。</para>
    /// </summary>
    internal sealed class FakeCameraBackend : ICameraBackend, ICameraBackendSessionReset, IDisposable
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, object> _cameras =
            new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly HashSet<string> _generated = new HashSet<string>(StringComparer.Ordinal);

        public string ActiveCameraId { get; set; }

        public bool HasActiveCamera { get; set; }

        public Camera OutputCamera { get; set; }

        public bool IsBlending { get; set; }

        public bool IsTransitioning { get; set; }

        public float TransitionProgress { get; set; }

        public string TransitionTargetId { get; set; }

        public int CameraCount => _order.Count;

        public int NamedCount => CountGenerated(false);

        public int GeneratedCount => CountGenerated(true);

        public bool Disposed { get; private set; }

        /// <summary>为 true 时 Dispose 抛异常，用于验证门面的闩锁顺序（CAM-002）。</summary>
        public bool ThrowOnDispose { get; set; }

        public int UnbindCount { get; private set; }

        /// <summary>为 true 时 UnbindSessionEvents 抛异常，用于验证门面的异常隔离。</summary>
        public bool ThrowOnUnbind { get; set; }

        public string LastRegisteredId { get; private set; }

        public object LastRegistered { get; private set; }

        public bool LastRegisteredIsGenerated { get; private set; }

        public string LastUnregisteredId { get; private set; }

        public string LastActivatedId { get; private set; }

        public int ActivateCount { get; private set; }

        public float LastActivateDuration { get; private set; }

        public CameraBlendStyle LastActivateStyle { get; private set; }

        public int ClearActiveCount { get; private set; }

        public IReadOnlyList<string> RegisteredIds => _order;

        public bool Register(string cameraId, object camera, bool isGenerated)
        {
            LastRegisteredId = cameraId;
            LastRegistered = camera;
            LastRegisteredIsGenerated = isGenerated;

            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (ReferenceEquals(camera, null))
                throw new ArgumentNullException(nameof(camera));
            if (camera is UnityEngine.Object unityObject && unityObject == null)
                throw new ArgumentException("相机对象已被销毁。", nameof(camera));

            if (_cameras.TryGetValue(cameraId, out var existing))
                return ReferenceEquals(existing, camera);

            _cameras.Add(cameraId, camera);
            _order.Add(cameraId);
            if (isGenerated)
                _generated.Add(cameraId);
            return true;
        }

        public bool Unregister(string cameraId)
        {
            LastUnregisteredId = cameraId;
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_cameras.Remove(cameraId))
                return false;

            _order.Remove(cameraId);
            _generated.Remove(cameraId);
            return true;
        }

        public bool IsRegistered(string cameraId)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            return _cameras.ContainsKey(cameraId);
        }

        public bool TryGetCamera(string cameraId, out object camera)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (_cameras.TryGetValue(cameraId, out camera) &&
                !(camera is UnityEngine.Object unityObject && unityObject == null))
            {
                return true;
            }

            camera = null;
            return false;
        }

        public int CopyCameraIds(List<string> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            buffer.AddRange(_order);
            return buffer.Count;
        }

        public void Activate(string cameraId, float duration, CameraBlendStyle style)
        {
            ActivateCount++;
            LastActivatedId = cameraId;
            LastActivateDuration = duration;
            LastActivateStyle = style;

            if (cameraId != null && _cameras.ContainsKey(cameraId))
            {
                ActiveCameraId = cameraId;
                HasActiveCamera = true;
                TransitionTargetId = null;
            }
        }

        public void ClearActive()
        {
            ClearActiveCount++;
            ActiveCameraId = null;
            HasActiveCamera = false;
            TransitionTargetId = null;
            IsTransitioning = false;
            TransitionProgress = 0f;
        }

        public void Dispose()
        {
            Disposed = true;
            if (ThrowOnDispose)
                throw new InvalidOperationException("dispose boom");
        }

        void ICameraBackendSessionReset.UnbindSessionEvents()
        {
            UnbindCount++;
            if (ThrowOnUnbind)
                throw new InvalidOperationException("unbind boom");
        }

        private int CountGenerated(bool generated)
        {
            var count = 0;
            for (var i = 0; i < _order.Count; i++)
            {
                if (_generated.Contains(_order[i]) == generated)
                    count++;
            }

            return count;
        }
    }
}
