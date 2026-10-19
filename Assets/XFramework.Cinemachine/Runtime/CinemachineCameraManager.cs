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
    /// <para><b>命名 / 动态双键</b>：常驻 vcam 用 string id 注册（构造 map / <see cref="Register(string, CinemachineVirtualCameraBase)"/>）；
    /// 随预制创建 / 销毁的动态 vcam 用 <see cref="Register{T}(T)"/> 取得 <see cref="CameraHandle"/>，之后按句柄操作。</para>
    /// <para><b>一次一台活动相机</b>：激活 / 过渡把目标优先级拉到最高、其余已注册 vcam 压到 0；未注册 vcam 的优先级
    /// 不受管理。</para>
    /// <para><b>Timeline / 过场边界</b>：过场相机由 Timeline 接管 Brain，不经过本管理器；若 Brain 的活动相机未注册
    /// （Timeline / 外来 vcam），<see cref="Activate(string)"/> / <see cref="TransitionTo(string, float, CameraBlendStyle)"/>
    /// 会告警「可能与过场冲突」。</para>
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

        #region Private Types

        /// <summary>一个 vcam 槽位。槽位下标就是 <see cref="CameraHandle.Id"/>；动态 vcam 的 <see cref="Id"/> 为 null。</summary>
        private sealed class Slot
        {
            public CinemachineVirtualCameraBase Camera;
            public string Id;
            public int Generation;
            public bool Used;
        }

        #endregion

        #region Private Fields

        private readonly CinemachineBrain _brain;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<string, int> _idToSlot = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<ICinemachineCamera, int> _reverse = new Dictionary<ICinemachineCamera, int>();
        private bool _disposed;

        #endregion

        #region Construction

        /// <summary>用 Brain 与一组 id → vcam 构造实现；不拥有场景对象。</summary>
        /// <param name="brain">场景里控制 Unity Camera 的 Brain。</param>
        /// <param name="cameras">命名 vcam 注册表；构造时复制。</param>
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

                Register(pair.Key, pair.Value);
            }
        }

        #endregion

        #region Public API — Registry

        /// <summary>注册或替换一台命名 vcam；替换只更新注册表与反查表，不改变场景状态。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="camera">vcam。</param>
        public void Register(string cameraId, CinemachineVirtualCameraBase camera)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            if (_idToSlot.TryGetValue(cameraId, out var existing))
            {
                var slot = _slots[existing];
                if (slot.Camera != null)
                    _reverse.Remove(slot.Camera);
                slot.Camera = camera;
                _reverse[camera] = existing;
                return;
            }

            var index = AllocateSlot(camera, cameraId);
            _reverse[camera] = index;
        }

        /// <summary>注册一台动态 vcam，返回其后端句柄；不改变场景状态。</summary>
        /// <typeparam name="T">必须是 <see cref="CinemachineVirtualCameraBase"/>。</typeparam>
        /// <param name="handle">vcam。</param>
        /// <exception cref="ArgumentNullException"><paramref name="handle"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">类型不是 <see cref="CinemachineVirtualCameraBase"/>。</exception>
        public CameraHandle Register<T>(T handle) where T : class
        {
            ThrowIfDisposed();
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));
            if (handle is not CinemachineVirtualCameraBase camera)
            {
                throw new ArgumentException(
                    "CinemachineCameraManager 的动态相机句柄必须是 CinemachineVirtualCameraBase。", nameof(handle));
            }

            var index = AllocateSlot(camera, null);
            _reverse[camera] = index;
            return new CameraHandle(index, _slots[index].Generation);
        }

        /// <summary>注销一台命名 vcam；不改变场景状态（活动 vcam 请先 <see cref="Deactivate(string)"/>）。</summary>
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

        /// <summary>注销一台动态 vcam；不改变场景状态。</summary>
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
                return _brain.OutputCamera;
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
                var active = _brain.ActiveVirtualCamera;
                return active != null && _reverse.TryGetValue(active, out var index)
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
                return _brain.IsBlending;
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

            _slots[index].Camera.gameObject.SetActive(false);
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

            TransitionToSlot(index, duration, style);
        }

        /// <inheritdoc/>
        public bool IsActive(string cameraId)
        {
            ThrowIfDisposed();
            return cameraId != null &&
                   TryGetActiveSlot(out var index) &&
                   string.Equals(_slots[index].Id, cameraId, StringComparison.Ordinal);
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

            _slots[index].Camera.gameObject.SetActive(false);
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

            TransitionToSlot(index, duration, style);
        }

        /// <inheritdoc/>
        public bool IsActive(CameraHandle handle)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            return index >= 0 && TryGetActiveSlot(out var active) && index == active;
        }

        #endregion

        #region IDisposable

        /// <summary>清空注册表与反查表；不改变场景对象。</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _slots.Clear();
            _idToSlot.Clear();
            _reverse.Clear();
        }

        #endregion

        #region Internal

        private int AllocateSlot(CinemachineVirtualCameraBase camera, string id)
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

                return i;
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

            return _slots.Count - 1;
        }

        private void ReleaseSlot(int index)
        {
            var slot = _slots[index];
            if (slot.Camera != null)
                _reverse.Remove(slot.Camera);
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

        private bool TryGetActiveSlot(out int index)
        {
            var active = _brain.ActiveVirtualCamera;
            if (active != null && _reverse.TryGetValue(active, out index))
                return true;

            index = -1;
            return false;
        }

        private void ActivateSlot(int index)
        {
            var target = _slots[index].Camera;
            if (target == null)
                return;

            WarnIfActiveIsUnregistered();

            _brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            ApplyPriorities(index);
            target.gameObject.SetActive(true);

            // 已在混合且目标就是当前活动时，改优先级不会重启混合——直接清掉当前 blend 强制完成它。
            // 上游官方说明（CinemachineBrain.ActiveBlend）：把 ActiveBlend 设为 null 正是「强制完成混合」
            // 的受支持入口。不要用短暂失活/激活的 hack：同步 off/on 后下一帧仍是同一目标，
            // Brain 不会新建混合，原过渡会按原时长继续（R3-CIN-001）。
            if (_brain.IsBlending && ReferenceEquals(_brain.ActiveVirtualCamera, target))
            {
                _brain.ActiveBlend = null;
            }
        }

        private void TransitionToSlot(int index, float duration, CameraBlendStyle style)
        {
            var target = _slots[index].Camera;
            if (target == null)
                return;

            if (duration <= Epsilon || style == CameraBlendStyle.Cut)
            {
                ActivateSlot(index);
                return;
            }

            if (!_brain.IsBlending && ReferenceEquals(_brain.ActiveVirtualCamera, target))
                return;

            WarnIfActiveIsUnregistered();

            _brain.DefaultBlend = new CinemachineBlendDefinition(Map(style), duration);
            ApplyPriorities(index);
            target.gameObject.SetActive(true);
        }

        private void ApplyPriorities(int activeIndex)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.Used || slot.Camera == null)
                    continue;

                slot.Camera.Priority = i == activeIndex ? ActivePriority : StandbyPriority;

                // Priority 是普通字段：不调用 Prioritize()，Brain 的优先级队列要等该 vcam 下一帧
                // Update 才重排；同步测试与同帧切换都依赖这次立即重排。
                slot.Camera.Prioritize();
            }
        }

        private void WarnIfActiveIsUnregistered()
        {
            var active = _brain.ActiveVirtualCamera;
            if (active != null && !_reverse.ContainsKey(active))
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "Brain 的当前活动相机未在 CameraManager 注册（可能由 Timeline / 过场接管）；" +
                    "此时 Activate / TransitionTo 可能与过场冲突。");
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
