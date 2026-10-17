using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera.Cinemachine
{
    /// <summary>
    /// <see cref="ICameraManager"/> 的 Cinemachine 3.x 实现：用 vcam 的优先级做激活 / 反激活，
    /// 用 <see cref="CinemachineBrain"/> 的混合做过渡。
    /// <para><b>一次一台活动相机</b>：<see cref="Activate"/> / <see cref="TransitionTo"/> 把目标优先级拉到最高、
    /// 其余已注册 vcam 压到 0；未注册 vcam 的优先级不受管理。</para>
    /// <para><b>只支持 Cinemachine 3.x</b>（按 3.1.7 的 API 编写）：2.x 的类型名与字段名不同，需自己的
    /// <see cref="ICameraManager"/> 实现。</para>
    /// </summary>
    public sealed class CinemachineCameraManager : ICameraManager, IDisposable
    {
        #region Constants

        private const int ActivePriority = int.MaxValue;
        private const int StandbyPriority = 0;
        private const float Epsilon = 0.0001f;

        #endregion

        #region Private Fields

        private readonly CinemachineBrain _brain;
        private readonly Dictionary<string, CinemachineVirtualCameraBase> _cameras =
            new Dictionary<string, CinemachineVirtualCameraBase>(StringComparer.Ordinal);
        private readonly Dictionary<ICinemachineCamera, string> _ids =
            new Dictionary<ICinemachineCamera, string>();
        private bool _disposed;

        #endregion

        #region Construction

        /// <summary>用 Brain 与一组 id → vcam 构造实现；不拥有场景对象。</summary>
        /// <param name="brain">场景里控制 Unity Camera 的 Brain。</param>
        /// <param name="cameras">vcam 注册表；构造时复制。</param>
        /// <exception cref="ArgumentNullException">参数为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">id 或 vcam 为 <c>null</c>。</exception>
        public CinemachineCameraManager(CinemachineBrain brain,
            IReadOnlyDictionary<string, CinemachineVirtualCameraBase> cameras)
        {
            if (brain == null)
                throw new ArgumentNullException(nameof(brain));
            if (cameras == null)
                throw new ArgumentNullException(nameof(cameras));

            _brain = brain;
            foreach (var pair in cameras)
            {
                if (pair.Key == null)
                    throw new ArgumentException("相机 id 不能为 null。", nameof(cameras));
                if (pair.Value == null)
                    throw new ArgumentException("相机 '" + pair.Key + "' 为 null。", nameof(cameras));

                _cameras.Add(pair.Key, pair.Value);
                _ids[pair.Value] = pair.Key;
            }
        }

        #endregion

        #region Public API — Registry

        /// <summary>注册或替换一台 vcam；替换只更新注册表与反查表，不改变场景状态。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="camera">vcam。</param>
        public void Register(string cameraId, CinemachineVirtualCameraBase camera)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            if (_cameras.TryGetValue(cameraId, out var old) && old != null)
                _ids.Remove(old);

            _cameras[cameraId] = camera;
            _ids[camera] = cameraId;
        }

        /// <summary>注销一台 vcam；不改变场景状态（活动相机请先 <see cref="Deactivate"/>）。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <returns>确实移除了才返回 <c>true</c>。</returns>
        public bool Unregister(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (!_cameras.TryGetValue(cameraId, out var camera))
                return false;

            _cameras.Remove(cameraId);
            if (camera != null)
                _ids.Remove(camera);
            return true;
        }

        #endregion

        #region ICameraManager

        /// <inheritdoc/>
        public Camera OutputCamera
        {
            get
            {
                ThrowIfDisposed();
                return _brain.OutputCamera;
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
                var active = _brain.ActiveVirtualCamera;
                return active != null && _ids.TryGetValue(active, out var id) ? id : null;
            }
        }

        /// <inheritdoc/>
        public bool IsTransitioning
        {
            get
            {
                ThrowIfDisposed();
                return _brain.IsBlending;
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

            _brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            ApplyPriorities(cameraId);
            target.gameObject.SetActive(true);

            // 已在混合且目标就是当前活动时，改优先级不会重启混合——短暂失活/激活强制重新入队。
            // 副作用：vcam 的 PreviousStateIsValid 重置。
            if (_brain.IsBlending && ReferenceEquals(_brain.ActiveVirtualCamera, target))
            {
                target.gameObject.SetActive(false);
                target.gameObject.SetActive(true);
            }
        }

        /// <inheritdoc/>
        public void Deactivate(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_cameras.TryGetValue(cameraId, out var target) || target == null)
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            target.gameObject.SetActive(false);
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

            if (!_brain.IsBlending && ReferenceEquals(_brain.ActiveVirtualCamera, target))
                return;

            _brain.DefaultBlend = new CinemachineBlendDefinition(Map(style), duration);
            ApplyPriorities(cameraId);
            target.gameObject.SetActive(true);
        }

        /// <inheritdoc/>
        public bool IsActive(string cameraId)
        {
            ThrowIfDisposed();
            return cameraId != null &&
                   string.Equals(ActiveCameraId, cameraId, StringComparison.Ordinal);
        }

        #endregion

        #region IDisposable

        /// <summary>清空注册表与反查表；不改变场景对象。</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cameras.Clear();
            _ids.Clear();
        }

        #endregion

        #region Internal

        private void ApplyPriorities(string activeId)
        {
            foreach (var pair in _cameras)
            {
                var camera = pair.Value;
                if (camera == null)
                    continue;

                camera.Priority = string.Equals(pair.Key, activeId, StringComparison.Ordinal)
                    ? ActivePriority
                    : StandbyPriority;

                // Priority 是普通字段：不调用 Prioritize()，Brain 的优先级队列要等该 vcam 下一帧
                // Update 才重排；同步测试与同帧切换都依赖这次立即重排。
                camera.Prioritize();
            }
        }

        private static CinemachineBlendDefinition.Styles Map(CameraBlendStyle style)
        {
            switch (style)
            {
                case CameraBlendStyle.Cut:
                    return CinemachineBlendDefinition.Styles.Cut;
                case CameraBlendStyle.EaseIn:
                    return CinemachineBlendDefinition.Styles.EaseIn;
                case CameraBlendStyle.EaseOut:
                    return CinemachineBlendDefinition.Styles.EaseOut;
                case CameraBlendStyle.Linear:
                    return CinemachineBlendDefinition.Styles.Linear;
                default:
                    return CinemachineBlendDefinition.Styles.EaseInOut;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CinemachineCameraManager));
        }

        #endregion
    }
}
