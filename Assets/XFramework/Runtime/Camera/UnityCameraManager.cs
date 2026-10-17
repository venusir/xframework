using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUpdate;

namespace XFramework.XCamera
{
    /// <summary>
    /// <see cref="ICameraManager"/> 的默认实现：直接驱动 <see cref="UnityEngine.Camera"/>。
    /// <para><b>一次只有一台活动相机</b>：<see cref="Activate"/> / <see cref="TransitionTo"/> 会关闭其它
    /// 已注册相机。它假设相机是静态的且投影模式一致，过渡只插值位置 / 旋转 / FOV（正交为
    /// <c>orthographicSize</c>），不混合 culling mask / background / RenderTexture——移动、跟随、瞄准、
    /// 震屏请用 Cinemachine 扩展包。</para>
    /// <para><b>调度</b>：过渡在第一次 <see cref="TransitionTo"/> 时懒注册到 <see cref="UpdateManager"/>
    /// 的 LateUpdate 时机，完成即注销；空闲时零调度成本。注册时使用 Scaled 时间轴——暂停会冻结过渡。</para>
    /// </summary>
    public sealed class UnityCameraManager : ICameraManager, ILateUpdateable, IDisposable
    {
        #region Constants

        private const float Epsilon = 0.0001f;

        #endregion

        #region Private Fields

        private readonly Dictionary<string, Camera> _cameras =
            new Dictionary<string, Camera>(StringComparer.Ordinal);

        private string _activeId;
        private string _transitionTargetId;

        private Vector3 _fromPosition;
        private Quaternion _fromRotation;
        private float _fromFieldOfView;
        private float _fromOrthographicSize;

        private Vector3 _toPosition;
        private Quaternion _toRotation;
        private float _toFieldOfView;
        private float _toOrthographicSize;

        private float _elapsed;
        private float _duration;
        private CameraBlendStyle _style;
        private bool _registered;
        private bool _disposed;

        #endregion

        #region Construction

        /// <summary>用一组 id → 相机构造默认实现。</summary>
        /// <param name="cameras">相机注册表；构造时复制。所有相机初始为关闭状态，
        /// 需要调用 <see cref="Activate"/> 或 <see cref="TransitionTo"/> 确立活动相机。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameras"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">id 或相机为 <c>null</c>。</exception>
        public UnityCameraManager(IReadOnlyDictionary<string, Camera> cameras)
        {
            if (cameras == null)
                throw new ArgumentNullException(nameof(cameras));

            foreach (var pair in cameras)
            {
                if (pair.Key == null)
                    throw new ArgumentException("相机 id 不能为 null。", nameof(cameras));
                if (pair.Value == null)
                    throw new ArgumentException("相机 '" + pair.Key + "' 为 null。", nameof(cameras));

                pair.Value.enabled = false;
                _cameras.Add(pair.Key, pair.Value);
            }
        }

        #endregion

        #region Public API — Registry

        /// <summary>注册或替换一台相机；同一 id 若正在活动 / 过渡，会先反激活它。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="camera">相机；注册后初始为关闭状态。</param>
        public void Register(string cameraId, Camera camera)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            if (IsActive(cameraId))
                Deactivate(cameraId);

            camera.enabled = false;
            _cameras[cameraId] = camera;
        }

        /// <summary>注销一台相机；若它正在活动 / 过渡，会先反激活它。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <returns>确实移除了才返回 <c>true</c>。</returns>
        public bool Unregister(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (IsActive(cameraId))
                Deactivate(cameraId);

            return _cameras.Remove(cameraId);
        }

        #endregion

        #region ICameraManager

        /// <inheritdoc/>
        public Camera OutputCamera
        {
            get
            {
                ThrowIfDisposed();
                var id = _transitionTargetId ?? _activeId;
                return id != null && _cameras.TryGetValue(id, out var camera) && camera != null
                    ? camera
                    : null;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<string> CameraIds
        {
            get
            {
                ThrowIfDisposed();
                return _cameras.Keys;
            }
        }

        /// <inheritdoc/>
        public string ActiveCameraId
        {
            get
            {
                ThrowIfDisposed();
                return _transitionTargetId ?? _activeId;
            }
        }

        /// <inheritdoc/>
        public bool IsTransitioning
        {
            get
            {
                ThrowIfDisposed();
                return _transitionTargetId != null;
            }
        }

        /// <inheritdoc/>
        public void Activate(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_cameras.TryGetValue(cameraId, out var target) || target == null)
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            CancelTransition();
            _activeId = cameraId;
            SetOnlyActive(cameraId);
        }

        /// <inheritdoc/>
        public void Deactivate(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_cameras.ContainsKey(cameraId))
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            if (_transitionTargetId == cameraId)
            {
                CancelTransition();
                _activeId = null;
                Disable(cameraId);
                return;
            }

            if (_activeId == cameraId)
            {
                Disable(cameraId);
                _activeId = null;
                return;
            }

            Disable(cameraId);
        }

        /// <inheritdoc/>
        public void TransitionTo(string cameraId, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_cameras.TryGetValue(cameraId, out var target) || target == null)
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            if (duration <= Epsilon || style == CameraBlendStyle.Cut)
            {
                Activate(cameraId);
                return;
            }

            if (_transitionTargetId == null && _activeId == cameraId)
                return;

            // 旧过渡的目标与它的原始位姿必须在覆盖 _to* 之前取出。
            Camera oldTransitionTarget = null;
            if (_transitionTargetId != null)
                _cameras.TryGetValue(_transitionTargetId, out oldTransitionTarget);

            var oldToPosition = _toPosition;
            var oldToRotation = _toRotation;
            var oldToFieldOfView = _toFieldOfView;
            var oldToOrthographicSize = _toOrthographicSize;

            Camera source;
            if (oldTransitionTarget != null)
                source = oldTransitionTarget;
            else if (_activeId != null && _cameras.TryGetValue(_activeId, out var active))
                source = active;
            else
                source = null;

            if (source == null || source.orthographic != target.orthographic)
            {
                Activate(cameraId);
                return;
            }

            _fromPosition = source.transform.position;
            _fromRotation = source.transform.rotation;
            _fromFieldOfView = source.fieldOfView;
            _fromOrthographicSize = source.orthographicSize;

            if (oldTransitionTarget != null && oldTransitionTarget == target)
            {
                // 对同一目标重开过渡：终点是它自己的原始位姿（旧 _to*），不是当前插值位姿。
                _toPosition = oldToPosition;
                _toRotation = oldToRotation;
                _toFieldOfView = oldToFieldOfView;
                _toOrthographicSize = oldToOrthographicSize;
            }
            else
            {
                _toPosition = target.transform.position;
                _toRotation = target.transform.rotation;
                _toFieldOfView = target.fieldOfView;
                _toOrthographicSize = target.orthographicSize;

                if (oldTransitionTarget != null)
                {
                    ApplyPose(oldTransitionTarget, oldToPosition, oldToRotation,
                        oldToFieldOfView, oldToOrthographicSize);
                }
            }

            _style = style;
            _duration = duration;
            _elapsed = 0f;
            _transitionTargetId = cameraId;
            _activeId = null;

            SetOnlyActive(cameraId);
            ApplyPose(target, _fromPosition, _fromRotation, _fromFieldOfView, _fromOrthographicSize);

            if (!_registered)
            {
                UpdateManager.RegisterLate(this, 0, UpdateTier.Tier0, UpdateTimeMode.Scaled);
                _registered = true;
            }
        }

        /// <inheritdoc/>
        public bool IsActive(string cameraId)
        {
            ThrowIfDisposed();
            return cameraId != null &&
                   string.Equals(_transitionTargetId ?? _activeId, cameraId, StringComparison.Ordinal);
        }

        #endregion

        #region ILateUpdateable

        void IUpdateLifecycle.OnEnable()
        {
        }

        void IUpdateLifecycle.OnDisable()
        {
        }

        UpdateTier ILateUpdateable.OnLateUpdate(float deltaTime, float time)
        {
            if (_transitionTargetId == null)
                return UpdateTier.Tier0;

            _elapsed += deltaTime;
            if (!_cameras.TryGetValue(_transitionTargetId, out var target) || target == null)
            {
                CancelTransition();
                return UpdateTier.Tier0;
            }

            var t = Mathf.Clamp01(_elapsed / _duration);
            var eased = Ease(t, _style);
            ApplyPose(target,
                Vector3.LerpUnclamped(_fromPosition, _toPosition, eased),
                Quaternion.SlerpUnclamped(_fromRotation, _toRotation, eased),
                Mathf.LerpUnclamped(_fromFieldOfView, _toFieldOfView, eased),
                Mathf.LerpUnclamped(_fromOrthographicSize, _toOrthographicSize, eased));

            if (t >= 1f)
            {
                ApplyPose(target, _toPosition, _toRotation, _toFieldOfView, _toOrthographicSize);
                _activeId = _transitionTargetId;
                _transitionTargetId = null;
                UnregisterTicker();
            }

            return UpdateTier.Tier0;
        }

        #endregion

        #region IDisposable

        /// <summary>注销调度并清空注册表；不销毁相机对象。</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            UnregisterTicker();
            _cameras.Clear();
            _activeId = null;
            _transitionTargetId = null;
        }

        #endregion

        #region Internal

        private void SetOnlyActive(string cameraId)
        {
            foreach (var pair in _cameras)
            {
                var camera = pair.Value;
                if (camera == null)
                    continue;

                camera.enabled = string.Equals(pair.Key, cameraId, StringComparison.Ordinal);
            }
        }

        private void Disable(string cameraId)
        {
            if (_cameras.TryGetValue(cameraId, out var camera) && camera != null)
                camera.enabled = false;
        }

        private void CancelTransition()
        {
            if (_transitionTargetId == null)
                return;

            if (_cameras.TryGetValue(_transitionTargetId, out var target) && target != null)
                ApplyPose(target, _toPosition, _toRotation, _toFieldOfView, _toOrthographicSize);

            _transitionTargetId = null;
            _elapsed = 0f;
            UnregisterTicker();
        }

        private void UnregisterTicker()
        {
            if (!_registered)
                return;

            _registered = false;
            UpdateManager.Unregister(this);
        }

        private static void ApplyPose(Camera camera, Vector3 position, Quaternion rotation,
            float fieldOfView, float orthographicSize)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fieldOfView;
            camera.orthographicSize = orthographicSize;
        }

        private static float Ease(float t, CameraBlendStyle style)
        {
            switch (style)
            {
                case CameraBlendStyle.Cut:
                    return 1f;
                case CameraBlendStyle.EaseIn:
                    return t * t;
                case CameraBlendStyle.EaseOut:
                    return 1f - (1f - t) * (1f - t);
                case CameraBlendStyle.Linear:
                    return t;
                default:
                    return t * t * (3f - 2f * t);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnityCameraManager));
        }

        #endregion
    }
}
