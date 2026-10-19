using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUpdate;

namespace XFramework.XCamera
{
    /// <summary>
    /// <see cref="ICameraManager"/> 的默认实现：直接驱动 <see cref="UnityEngine.Camera"/>。
    /// <para><b>命名 / 动态双键</b>：常驻相机用 string id 注册（构造 map / <see cref="Register(string, Camera)"/>）；
    /// 动态相机用 <see cref="Register{T}(T)"/> 取得 <see cref="CameraHandle"/>，之后按句柄操作。</para>
    /// <para><b>一次只有一台活动相机</b>：<see cref="Activate(string)"/> / <see cref="TransitionTo(string, float, CameraBlendStyle)"/>
    /// 与它们的句柄重载会关闭其它已注册相机。它假设相机是静态的且投影模式一致，过渡只插值位置 / 旋转 / FOV
    ///（正交为 <c>orthographicSize</c>），不混合 culling mask / background / RenderTexture——移动、跟随、瞄准、
    /// 震屏请用 Cinemachine 扩展包。</para>
    /// <para><b>调度</b>：过渡在第一次过渡时懒注册到 <see cref="UpdateManager"/> 的 LateUpdate 时机，完成即注销；
    /// 空闲时零调度成本。注册时使用 Scaled 时间轴——暂停会冻结过渡。</para>
    /// </summary>
    public sealed class UnityCameraManager : ICameraManager, ILateUpdateable, IDisposable
    {
        #region Constants

        private const float Epsilon = 0.0001f;

        #endregion

        #region Private Types

        /// <summary>一个相机槽位。槽位下标就是 <see cref="CameraHandle.Id"/>；动态相机的 <see cref="Id"/> 为 null。</summary>
        private sealed class Slot
        {
            public Camera Camera;
            public string Id;
            public int Generation;
            public bool Used;
        }

        #endregion

        #region Private Fields

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<string, int> _idToSlot = new Dictionary<string, int>(StringComparer.Ordinal);

        private int _activeSlot = -1;
        private int _transitionSlot = -1;

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
        /// <param name="cameras">命名相机注册表；构造时复制。所有相机初始为关闭状态，
        /// 需要调用 <see cref="Activate(string)"/> 或过渡确立活动相机。</param>
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

                Register(pair.Key, pair.Value);
            }
        }

        #endregion

        #region Public API — Registry

        /// <summary>注册或替换一台命名相机；同一 id 若正在活动 / 过渡，会先反激活它。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="camera">相机；注册后初始为关闭状态。</param>
        public void Register(string cameraId, Camera camera)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            if (_idToSlot.TryGetValue(cameraId, out var existing))
            {
                if (existing == _activeSlot || existing == _transitionSlot)
                    DeactivateSlot(existing);

                _slots[existing].Camera = camera;
                camera.enabled = false;
                return;
            }

            AllocateSlot(camera, cameraId);
            camera.enabled = false;
        }

        /// <summary>注册一台动态相机，返回其后端句柄；注册后初始为关闭状态。</summary>
        /// <typeparam name="T">必须是 <see cref="Camera"/>。</typeparam>
        /// <param name="handle">相机对象。</param>
        /// <exception cref="ArgumentNullException"><paramref name="handle"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">类型不是 <see cref="Camera"/>。</exception>
        public CameraHandle Register<T>(T handle) where T : class
        {
            ThrowIfDisposed();
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));
            if (handle is not Camera camera)
            {
                throw new ArgumentException(
                    "UnityCameraManager 的动态相机句柄必须是 UnityEngine.Camera。", nameof(handle));
            }

            var result = AllocateSlot(camera, null);
            camera.enabled = false;
            return result;
        }

        /// <summary>注销一台命名相机；若它正在活动 / 过渡，会先取消过渡并清掉活动状态。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <returns>确实移除了才返回 <c>true</c>。</returns>
        public bool Unregister(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (!_idToSlot.TryGetValue(cameraId, out var index))
                return false;

            ReleaseSlot(index);
            return true;
        }

        /// <summary>注销一台动态相机；若它正在活动 / 过渡，会先取消过渡并清掉活动状态。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <returns>确实移除了才返回 <c>true</c>；<c>default</c> / 已注销 / 无效句柄返回 <c>false</c>。</returns>
        public bool Unregister(CameraHandle handle)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            if (index < 0)
                return false;

            ReleaseSlot(index);
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
                var index = ActiveSlot;
                // 被外部销毁的相机是 Unity 假 null：直接返回引用会让调用方拿到一个「非 null 但不可用」的对象。
                // 查询面统一按 null 报告（CAM-001）。
                return IsUsable(index) ? _slots[index].Camera : null;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<string> CameraIds
        {
            get
            {
                ThrowIfDisposed();
                return _idToSlot.Keys;
            }
        }

        /// <inheritdoc/>
        public CameraHandle ActiveHandle
        {
            get
            {
                ThrowIfDisposed();
                var index = ActiveSlot;
                return IsUsable(index)
                    ? new CameraHandle(index, _slots[index].Generation, _slots[index].Id)
                    : default;
            }
        }

        /// <inheritdoc/>
        public bool IsTransitioning
        {
            get
            {
                ThrowIfDisposed();
                return _transitionSlot >= 0;
            }
        }

        /// <inheritdoc/>
        public void Activate(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_idToSlot.TryGetValue(cameraId, out var index))
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            // 被外部销毁的相机在 Unity 里是假 null：槽位仍看似有效，SetOnlyActive 会跳过它并
            // 把其它活相机全部关掉——激活静默「成功」却没有任何相机在输出。变更面必须挡在
            // 这里（CAM-002）。
            if (!IsUsable(index))
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机 '{0}' 已被外部销毁，激活已忽略。", cameraId);
                return;
            }

            ActivateSlot(index);
        }

        /// <inheritdoc/>
        public void Deactivate(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_idToSlot.TryGetValue(cameraId, out var index))
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            DeactivateSlot(index);
        }

        /// <inheritdoc/>
        public void TransitionTo(string cameraId, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!_idToSlot.TryGetValue(cameraId, out var index))
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            if (!IsUsable(index))
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机 '{0}' 已被外部销毁，过渡已忽略。", cameraId);
                return;
            }

            TransitionToSlot(index, duration, style);
        }

        /// <inheritdoc/>
        public bool IsActive(string cameraId)
        {
            ThrowIfDisposed();
            return cameraId != null &&
                   _idToSlot.TryGetValue(cameraId, out var index) &&
                   index == ActiveSlot &&
                   IsUsable(index);
        }

        /// <inheritdoc/>
        public void Activate(CameraHandle handle)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            if (index < 0)
            {
                LogManager.Warning(LogCategories.CameraManager, "相机句柄无效或已注销: {0}", handle);
                return;
            }

            if (!IsUsable(index))
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机句柄 {0} 对应的相机已被外部销毁，激活已忽略。", handle);
                return;
            }

            ActivateSlot(index);
        }

        /// <inheritdoc/>
        public void Deactivate(CameraHandle handle)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            if (index < 0)
            {
                LogManager.Warning(LogCategories.CameraManager, "相机句柄无效或已注销: {0}", handle);
                return;
            }

            DeactivateSlot(index);
        }

        /// <inheritdoc/>
        public void TransitionTo(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            if (index < 0)
            {
                LogManager.Warning(LogCategories.CameraManager, "相机句柄无效或已注销: {0}", handle);
                return;
            }

            if (!IsUsable(index))
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机句柄 {0} 对应的相机已被外部销毁，过渡已忽略。", handle);
                return;
            }

            TransitionToSlot(index, duration, style);
        }

        /// <inheritdoc/>
        public bool IsActive(CameraHandle handle)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            return index >= 0 && index == ActiveSlot && IsUsable(index);
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
            if (_transitionSlot < 0)
                return UpdateTier.Tier0;

            _elapsed += deltaTime;
            var target = _slots[_transitionSlot].Camera;
            if (target == null)
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
                _activeSlot = _transitionSlot;
                _transitionSlot = -1;
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
            _slots.Clear();
            _idToSlot.Clear();
            _activeSlot = -1;
            _transitionSlot = -1;
        }

        #endregion

        #region Internal

        private int ActiveSlot => _transitionSlot >= 0 ? _transitionSlot : _activeSlot;

        /// <summary>槽位是否仍持有一台可用的相机；被外部销毁的相机在 Unity 里是假 null（CAM-001）。</summary>
        private bool IsUsable(int index)
            => index >= 0 && index < _slots.Count && _slots[index].Used && _slots[index].Camera != null;

        private CameraHandle AllocateSlot(Camera camera, string id)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Used)
                    continue;

                slot.Used = true;
                slot.Camera = camera;
                slot.Id = id;
                slot.Generation = slot.Generation == int.MaxValue ? 1 : slot.Generation + 1;
                if (id != null)
                    _idToSlot[id] = i;

                return new CameraHandle(i, slot.Generation);
            }

            var newSlot = new Slot
            {
                Camera = camera,
                Id = id,
                Generation = 1,
                Used = true,
            };
            _slots.Add(newSlot);
            if (id != null)
                _idToSlot[id] = _slots.Count - 1;

            return new CameraHandle(_slots.Count - 1, 1);
        }

        private void ReleaseSlot(int index)
        {
            if (index == _transitionSlot)
                CancelTransition();
            if (index == _activeSlot)
                _activeSlot = -1;

            var slot = _slots[index];
            if (slot.Camera != null)
                slot.Camera.enabled = false;
            if (slot.Id != null)
                _idToSlot.Remove(slot.Id);

            slot.Camera = null;
            slot.Id = null;
            slot.Used = false;
            // Generation 保留：槽位复用时代际递增，旧句柄按代际失配自然失效。
        }

        private int Resolve(CameraHandle handle)
        {
            if (handle.IsDefault)
                return -1;

            var index = handle.Id;
            if (index < 0 || index >= _slots.Count)
                return -1;

            var slot = _slots[index];
            return slot.Used && slot.Generation == handle.Generation ? index : -1;
        }

        private void ActivateSlot(int index)
        {
            CancelTransition();
            _activeSlot = index;
            _transitionSlot = -1;
            SetOnlyActive(index);
        }

        private void DeactivateSlot(int index)
        {
            if (index == _transitionSlot)
            {
                CancelTransition();
                _activeSlot = -1;
                DisableSlot(index);
                return;
            }

            if (index == _activeSlot)
            {
                DisableSlot(index);
                _activeSlot = -1;
                return;
            }

            DisableSlot(index);
        }

        private void DisableSlot(int index)
        {
            var camera = _slots[index].Camera;
            if (camera != null)
                camera.enabled = false;
        }

        private void TransitionToSlot(int targetIndex, float duration, CameraBlendStyle style)
        {
            // 非有限时长会让过渡永不结束（NaN 比较恒 false）或永久挂起（+∞），并逐帧写坏位姿（CAM-003）。
            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "[Camera] 过渡时长必须是有限值；NaN / ±∞ 会让过渡永不结束或逐帧写坏位姿。");
            }

            var target = _slots[targetIndex].Camera;
            if (target == null)
            {
                ActivateSlot(targetIndex);
                return;
            }

            if (duration <= Epsilon || style == CameraBlendStyle.Cut)
            {
                ActivateSlot(targetIndex);
                return;
            }

            if (_transitionSlot < 0 && _activeSlot == targetIndex)
                return;

            Camera oldTarget = null;
            if (_transitionSlot >= 0)
                oldTarget = _slots[_transitionSlot].Camera;

            var oldToPosition = _toPosition;
            var oldToRotation = _toRotation;
            var oldToFieldOfView = _toFieldOfView;
            var oldToOrthographicSize = _toOrthographicSize;

            Camera source;
            if (oldTarget != null)
                source = oldTarget;
            else if (_activeSlot >= 0)
                source = _slots[_activeSlot].Camera;
            else
                source = null;

            if (source == null || source.orthographic != target.orthographic)
            {
                ActivateSlot(targetIndex);
                return;
            }

            _fromPosition = source.transform.position;
            _fromRotation = source.transform.rotation;
            _fromFieldOfView = source.fieldOfView;
            _fromOrthographicSize = source.orthographicSize;

            if (ReferenceEquals(oldTarget, target))
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

                if (oldTarget != null)
                {
                    ApplyPose(oldTarget, oldToPosition, oldToRotation,
                        oldToFieldOfView, oldToOrthographicSize);
                }
            }

            _style = style;
            _duration = duration;
            _elapsed = 0f;
            _transitionSlot = targetIndex;
            _activeSlot = -1;

            SetOnlyActive(targetIndex);
            ApplyPose(target, _fromPosition, _fromRotation, _fromFieldOfView, _fromOrthographicSize);

            if (!_registered)
            {
                UpdateManager.RegisterLate(this, 0, UpdateTier.Tier0, UpdateTimeMode.Scaled);
                _registered = true;
            }
        }

        private void SetOnlyActive(int index)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.Used || slot.Camera == null)
                    continue;

                slot.Camera.enabled = i == index;
            }
        }

        private void CancelTransition()
        {
            if (_transitionSlot < 0)
                return;

            var target = _slots[_transitionSlot].Camera;
            if (target != null)
                ApplyPose(target, _toPosition, _toRotation, _toFieldOfView, _toOrthographicSize);

            _transitionSlot = -1;
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
