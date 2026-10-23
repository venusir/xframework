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

        public string LastFollowId { get; private set; }

        public Transform LastFollow { get; private set; }

        public string LastLookAtId { get; private set; }

        public Transform LastLookAt { get; private set; }

        public string LastFieldOfViewId { get; private set; }

        public float LastFieldOfView { get; private set; }

        public int ShakeCount { get; private set; }

        public float LastShakeStrength { get; private set; }

        public float LastShakeDuration { get; private set; }

        public IReadOnlyList<string> RegisteredIds => _order;

        /// <summary>为 true 时 <see cref="Activate"/> 立即把目标记为活动（同步测试用）；
        /// 为 false 时只记在途目标，等待测试手动 <see cref="RaiseRequestSettled"/>。</summary>
        public bool AutoCompleteActivation { get; set; } = true;

        public event Action<CameraBackendActivation> Activated;

        public event Action<string> Deactivated;

        public event Action<string> BlendCompleted;

        public event Action<string, CameraTransitionResult> RequestSettled;

        public void RaiseActivated(string incomingCameraId, string outgoingCameraId, bool isCut)
        {
            Activated?.Invoke(new CameraBackendActivation(incomingCameraId, outgoingCameraId, isCut));
        }

        public void RaiseDeactivated(string cameraId)
        {
            Deactivated?.Invoke(cameraId);
        }

        public void RaiseBlendCompleted(string cameraId)
        {
            BlendCompleted?.Invoke(cameraId);
        }

        public void RaiseRequestSettled(string cameraId, CameraTransitionResult result)
        {
            RequestSettled?.Invoke(cameraId, result);
        }

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
                if (AutoCompleteActivation)
                {
                    ActiveCameraId = cameraId;
                    HasActiveCamera = true;
                    TransitionTargetId = null;
                    IsTransitioning = false;
                }
                else
                {
                    TransitionTargetId = cameraId;
                    IsTransitioning = duration > 0.0001f && style != CameraBlendStyle.Cut;
                }
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

        public void SetFollow(string cameraId, Transform follow)
        {
            LastFollowId = cameraId;
            LastFollow = follow;
        }

        public void SetLookAt(string cameraId, Transform lookAt)
        {
            LastLookAtId = cameraId;
            LastLookAt = lookAt;
        }

        public void SetFieldOfView(string cameraId, float fieldOfView)
        {
            LastFieldOfViewId = cameraId;
            LastFieldOfView = fieldOfView;
        }

        public void Shake(float strength, float duration)
        {
            ShakeCount++;
            LastShakeStrength = strength;
            LastShakeDuration = duration;
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
