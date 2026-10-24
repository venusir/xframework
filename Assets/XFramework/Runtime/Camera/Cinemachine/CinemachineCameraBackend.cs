using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera.Cinemachine
{
    /// <summary>
    /// <see cref="ICameraBackend"/> 的 Cinemachine 3.x 实现：用 vcam 优先级做激活，用
    /// <see cref="CinemachineBrain"/> 的混合做过渡。
    /// <para><b>一次一台活动相机</b>：激活 / 过渡只把目标提升到「场景内同通道最大优先级 + 1」，
    /// 不把其它相机压成 0；切换时恢复上一个目标由管理器改过的优先级 / 启用状态。</para>
    /// <para><b>过渡请求</b>：管理器安装 <see cref="CinemachineCore.GetBlendOverride"/>，对本次请求返回
    /// 调用方指定的 duration / style；<b>不修改</b> <see cref="CinemachineBrain.DefaultBlend"/>。
    /// 该委托是全局单委托，一个进程建议只创建一个相机后端。</para>
    /// <para><b>Timeline / 过场边界</b>：过场相机由 Timeline 接管 Brain，不经过本后端；若 Brain 的活动相机未注册
    /// （Timeline / 外来 vcam），激活 / 过渡会告警「可能与过场冲突」。Timeline 的 override 栈优先于 vcam 优先级，
    /// 因此本后端的 boost 不会挡住过场；过场释放后 Brain 重选，已 boost 的栈顶自然恢复。</para>
    /// <para><b>只支持 Cinemachine 3.x</b>（按 3.1.7 的 API 编写）：2.x 的类型名与字段名不同，需自己的
    /// <see cref="ICameraBackend"/> 实现。</para>
    /// </summary>
    internal sealed class CinemachineCameraBackend : ICameraBackend, ICameraBackendSessionReset, IDisposable
    {
        #region Constants

        private const float CutThreshold = 0.0001f;

        #endregion

        #region Private Types

        /// <summary>一台已注册相机及其被管理器改写前的场景状态。</summary>
        private sealed class Slot
        {
            public string CameraId;
            public bool IsGenerated;
            public CinemachineVirtualCameraBase Camera;

            public PrioritySettings OriginalPriority;
            public bool OriginalActiveSelf;
            public bool OriginalEnabled;
            public bool PriorityOwned;
            public bool GameObjectEnabledByManager;
            public bool ComponentEnabledByManager;
        }

        /// <summary>一次由管理器发起的激活 / 过渡请求。</summary>
        private sealed class Request
        {
            public Slot Target;
            public float Duration;
            public CameraBlendStyle Style;
            public CinemachineBlendDefinition Definition;
            public bool CountsAsTransition;
        }

        #endregion

        #region Private Fields

        private readonly CinemachineBrain _brain;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<string, Slot> _byId =
            new Dictionary<string, Slot>(StringComparer.Ordinal);
        private readonly CinemachineCore.GetBlendOverrideDelegate _previousBlendOverride;
        private readonly CinemachineCore.GetBlendOverrideDelegate _blendOverrideDelegate;
        private Request _request;
        private Slot _boostedSlot;
        private string _lastActiveCameraId;
        private CinemachineImpulse _impulse;
        private bool _warnedFieldOfViewGroup;
        private bool _eventsBound;
        private bool _disposed;
        private bool _detached;

        #endregion

        #region Construction

        /// <summary>用 Brain 构造后端；相机在构造后经 <see cref="Register"/> 注册。</summary>
        /// <param name="brain">场景里控制 Unity Camera 的 Brain。</param>
        /// <exception cref="ArgumentNullException"><paramref name="brain"/> 为 <c>null</c>。</exception>
        public CinemachineCameraBackend(CinemachineBrain brain)
        {
            if (brain == null)
                throw new ArgumentNullException(nameof(brain));

            _brain = brain;
            _previousBlendOverride = CinemachineCore.GetBlendOverride;
            _blendOverrideDelegate = OverrideBlend;
            CinemachineCore.GetBlendOverride = _blendOverrideDelegate;
            BindSessionEvents();
        }

        #endregion

        #region ICameraBackend — Queries

        /// <inheritdoc/>
        public string ActiveCameraId
        {
            get
            {
                ThrowIfDisposed();
                if (_brain == null)
                    return null;

                var active = _brain.ActiveVirtualCamera;
                if (active == null)
                    return null;
                if (active is UnityEngine.Object unityObject && unityObject == null)
                    return null;

                return ResolveCameraId(active);
            }
        }

        /// <inheritdoc/>
        public CinemachineVirtualCameraBase ActiveCamera
        {
            get
            {
                ThrowIfDisposed();
                if (_brain == null)
                    return null;

                var active = _brain.ActiveVirtualCamera;
                if (active == null)
                    return null;
                if (active is UnityEngine.Object unityObject && unityObject == null)
                    return null;

                return active as CinemachineVirtualCameraBase;
            }
        }

        /// <inheritdoc/>
        public bool HasActiveCamera
        {
            get
            {
                ThrowIfDisposed();
                if (_brain == null)
                    return false;

                var active = _brain.ActiveVirtualCamera;
                return active != null && !(active is UnityEngine.Object unityObject && unityObject == null);
            }
        }

        /// <inheritdoc/>
        public Camera OutputCamera
        {
            get
            {
                ThrowIfDisposed();
                return _brain != null ? _brain.OutputCamera : null;
            }
        }

        /// <inheritdoc/>
        public bool IsBlending
        {
            get
            {
                ThrowIfDisposed();
                return _brain != null && _brain.IsBlending;
            }
        }

        /// <inheritdoc/>
        public bool IsTransitioning =>
            !_disposed && !_detached && _request != null && _request.CountsAsTransition;

        /// <inheritdoc/>
        public float TransitionProgress
        {
            get
            {
                ThrowIfDisposed();

                if (_request == null || !_request.CountsAsTransition || _request.Duration <= 0f)
                    return 0f;

                var blend = _brain != null ? _brain.ActiveBlend : null;
                return blend != null
                    ? Mathf.Clamp01(blend.TimeInBlend / _request.Duration)
                    : 0f;
            }
        }

        /// <inheritdoc/>
        public string TransitionTargetId
        {
            get
            {
                ThrowIfDisposed();
                return _request != null ? _request.Target.CameraId : null;
            }
        }

        /// <inheritdoc/>
        public int CameraCount
        {
            get
            {
                ThrowIfDisposed();
                return _slots.Count;
            }
        }

        /// <inheritdoc/>
        public int NamedCount
        {
            get
            {
                ThrowIfDisposed();

                var count = 0;
                for (var i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].IsGenerated)
                        count++;
                }

                return count;
            }
        }

        /// <inheritdoc/>
        public int GeneratedCount
        {
            get
            {
                ThrowIfDisposed();

                var count = 0;
                for (var i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].IsGenerated)
                        count++;
                }

                return count;
            }
        }

        #endregion

        #region ICameraBackend — Registry

        /// <inheritdoc/>
        public bool Register(string cameraId, object camera, bool isGenerated)
        {
            ThrowIfDisposed();

            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (string.IsNullOrWhiteSpace(cameraId))
                throw new ArgumentException("[Camera] 相机 id 不能为空或全空白。", nameof(cameraId));
            if (ReferenceEquals(camera, null))
                throw new ArgumentNullException(nameof(camera));

            if (camera is UnityEngine.Object unityObject && unityObject == null)
            {
                throw new ArgumentException(
                    "[Camera] 相机对象已被销毁，不能注册。", nameof(camera));
            }

            if (camera is not CinemachineVirtualCameraBase vcam)
            {
                throw new ArgumentException(
                    "CinemachineCameraBackend 只接受 CinemachineVirtualCameraBase 类型的相机。", nameof(camera));
            }

            ReleaseDestroyedSlots();

            var existing = FindSlotByCamera(vcam);
            if (existing != null)
            {
                if (string.Equals(existing.CameraId, cameraId, StringComparison.Ordinal))
                    return false;

                throw new ArgumentException(
                    "该相机已注册为 '" + existing.CameraId + "'，不能重复注册为 '" + cameraId + "'。",
                    nameof(camera));
            }

            if (_byId.ContainsKey(cameraId))
            {
                throw new ArgumentException(
                    "相机 id '" + cameraId + "' 已被另一台相机占用；请先 Unregister。", nameof(cameraId));
            }

            var slot = new Slot
            {
                CameraId = cameraId,
                IsGenerated = isGenerated,
                Camera = vcam,
            };

            _slots.Add(slot);
            _byId.Add(cameraId, slot);
            return true;
        }

        /// <inheritdoc/>
        public bool Unregister(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (!_byId.TryGetValue(cameraId, out var slot))
                return false;

            var wasRequestTarget = _request != null && ReferenceEquals(_request.Target, slot);
            if (wasRequestTarget)
                SettleRequest(CameraTransitionResult.CameraNotFound);

            if (slot.Camera != null)
                RestoreOwnedState(slot);

            // 只在放弃本次请求的目标时强制完成混合；注销无关相机不应截断别人的在途过渡。
            if (wasRequestTarget && _brain != null && _brain.IsBlending)
                _brain.ActiveBlend = null;

            ReleaseSlot(slot);
            return true;
        }

        /// <inheritdoc/>
        public bool IsRegistered(string cameraId)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            return _byId.ContainsKey(cameraId);
        }

        /// <inheritdoc/>
        public bool TryGetCamera(string cameraId, out object camera)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (_byId.TryGetValue(cameraId, out var slot) && slot.Camera != null)
            {
                camera = slot.Camera;
                return true;
            }

            camera = null;
            return false;
        }

        /// <inheritdoc/>
        public int CopyCameraIds(List<string> buffer)
        {
            ThrowIfDisposed();
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            for (var i = 0; i < _slots.Count; i++)
                buffer.Add(_slots[i].CameraId);

            return buffer.Count;
        }

        #endregion

        #region ICameraBackend — Activation

        /// <inheritdoc/>
        public void Activate(string cameraId, float duration, CameraBlendStyle style)
        {
            ThrowIfDisposed();

            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "[Camera] 过渡时长必须是有限值；NaN / ±∞ 会让过渡永不结束。");
            }

            if (!_byId.TryGetValue(cameraId, out var slot))
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return;
            }

            if (slot.Camera == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机 id {0} 对应的相机已被外部销毁，激活已忽略。", cameraId);
                return;
            }

            if (_brain == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "CinemachineBrain 已被外部销毁，激活已忽略。");
                return;
            }

            if (duration <= CutThreshold || style == CameraBlendStyle.Cut)
                ActivateSlot(slot);
            else
                TransitionToSlot(slot, duration, style);
        }

        /// <inheritdoc/>
        public void ClearActive()
        {
            ThrowIfDisposed();

            SettleRequest(CameraTransitionResult.Superseded);
            RestoreBoosted();

            if (_brain != null && _brain.IsBlending)
                _brain.ActiveBlend = null;
        }

        #endregion

        #region ICameraBackend — Bindings

        /// <inheritdoc/>
        public void SetFollow(string cameraId, Transform follow)
        {
            ThrowIfDisposed();

            var slot = GetLiveSlot(cameraId, "SetFollow");
            if (slot == null)
                return;

            if (follow != null)
            {
                slot.Camera.Follow = follow;
                return;
            }

            // 分组相机的 Follow setter 会顺带把 DefaultTarget.Enabled 置 true；清除目标不应改变该状态。
            if (slot.Camera is CinemachineCameraManagerBase manager)
            {
                manager.DefaultTarget.Target.TrackingTarget = null;
                return;
            }

            slot.Camera.Follow = null;
        }

        /// <inheritdoc/>
        public void SetLookAt(string cameraId, Transform lookAt)
        {
            ThrowIfDisposed();

            var slot = GetLiveSlot(cameraId, "SetLookAt");
            if (slot == null)
                return;

            if (lookAt != null)
            {
                slot.Camera.LookAt = lookAt;
                return;
            }

            // CM 的 LookAt setter 会留下一个空的本地覆盖（CustomLookAtTarget = true），此后不再回退到
            // TrackingTarget；「清除本地覆盖」必须直接写字段，不能走 setter。
            if (slot.Camera is CinemachineCamera camera)
            {
                camera.Target.CustomLookAtTarget = false;
                camera.Target.LookAtTarget = null;
                return;
            }

            if (slot.Camera is CinemachineCameraManagerBase manager)
            {
                manager.DefaultTarget.Target.CustomLookAtTarget = false;
                manager.DefaultTarget.Target.LookAtTarget = null;
                return;
            }

            slot.Camera.LookAt = null;
        }

        /// <inheritdoc/>
        public void SetFieldOfView(string cameraId, float fieldOfView)
        {
            ThrowIfDisposed();

            if (float.IsNaN(fieldOfView) || float.IsInfinity(fieldOfView) || fieldOfView <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldOfView), fieldOfView,
                    "[Camera] 视场角必须是有限正数。");
            }

            var slot = GetLiveSlot(cameraId, "SetFieldOfView");
            if (slot == null)
                return;

            if (slot.Camera is CinemachineCamera camera)
            {
                camera.Lens.FieldOfView = fieldOfView;
                return;
            }

            if (!_warnedFieldOfViewGroup)
            {
                _warnedFieldOfViewGroup = true;
                LogManager.Warning(LogCategories.CameraManager,
                    "相机 id {0} 不是 CinemachineCamera（分组相机没有 Lens），" +
                    "SetFieldOfView 已忽略；请用 TryGetCamera 取 Lens。", cameraId);
            }
        }

        /// <inheritdoc/>
        public void Shake(float strength, float duration)
        {
            ThrowIfDisposed();

            if (float.IsNaN(strength) || float.IsInfinity(strength) || strength <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(strength), strength,
                    "[Camera] 震屏强度必须是有限正数。");
            }

            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "[Camera] 震屏时长必须是有限正数。");
            }

            _impulse ??= new CinemachineImpulse();
            _impulse.Shake(_brain, strength, duration);
        }

        #endregion

        #region ICameraBackend — Events

        /// <inheritdoc/>
        public event Action<CameraBackendActivation> Activated;

        /// <inheritdoc/>
        public event Action<string> Deactivated;

        /// <inheritdoc/>
        public event Action<string> BlendCompleted;

        /// <inheritdoc/>
        public event Action<string, CameraTransitionResult> RequestSettled;

        #endregion

        #region IDisposable / Session Reset

        /// <summary>恢复管理器改过的场景状态、退订静态事件、清空注册表；不销毁 Brain / vcam。</summary>
        public void Dispose()
        {
            if (_disposed || _detached)
                return;

            _disposed = true;
            UnsubscribeEvents();
            SettleRequest(CameraTransitionResult.ManagerDisposed);

            _impulse?.Dispose();
            _impulse = null;
            _boostedSlot = null;
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Camera != null)
                    RestoreOwnedState(slot);
            }

            _slots.Clear();
            _byId.Clear();
        }

        /// <inheritdoc/>
        void ICameraBackendSessionReset.UnbindSessionEvents()
        {
            if (_disposed || _detached)
                return;

            // 会话边界：只退订静态事件并清引用，绝不访问场景对象（上一会话的 Brain / vcam 可能已销毁）。
            _detached = true;
            UnsubscribeEvents();
            SettleRequest(CameraTransitionResult.ManagerDisposed);
            _boostedSlot = null;
            _slots.Clear();
            _byId.Clear();
        }

        #endregion

        #region Activation / Transition

        private void ActivateSlot(Slot slot)
        {
            var target = slot.Camera;
            if (target == null)
            {
                SettleRequest(CameraTransitionResult.CameraNotFound);
                return;
            }

            SettleRequest(CameraTransitionResult.Superseded);
            WarnIfActiveIsUnregistered();

            // cut 也走 GetBlendOverride：给本次激活一个 Cut 定义，避免 Brain 用项目默认混合
            // 把「立即切换」变成一次过渡。CountsAsTransition=false 表示它不进入 IsTransitioning。
            _request = new Request
            {
                Target = slot,
                Duration = 0f,
                Style = CameraBlendStyle.Cut,
                Definition = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.Cut, 0f),
                CountsAsTransition = false,
            };

            RestorePreviousBoosted(slot);
            Boost(slot);

            // 已在混合且目标就是当前活动时，改优先级不会重启混合——直接清掉当前 blend 强制完成它。
            // 上游官方说明（CinemachineBrain.ActiveBlend）：把 ActiveBlend 设为 null 正是「强制完成混合」
            // 的受支持入口（R3-CIN-001）。
            if (_brain != null && _brain.IsBlending)
                _brain.ActiveBlend = null;
        }

        private void TransitionToSlot(Slot slot, float duration, CameraBlendStyle style)
        {
            var target = slot.Camera;
            if (target == null)
            {
                SettleRequest(CameraTransitionResult.CameraNotFound);
                return;
            }

            if (!_brain.IsBlending && ReferenceEquals(_brain.ActiveVirtualCamera, target))
            {
                SettleRequest(CameraTransitionResult.Completed);
                return;
            }

            SettleRequest(CameraTransitionResult.Superseded);
            WarnIfActiveIsUnregistered();

            _request = new Request
            {
                Target = slot,
                Duration = duration,
                Style = style,
                Definition = new CinemachineBlendDefinition(Map(style), duration),
                CountsAsTransition = true,
            };

            RestorePreviousBoosted(slot);
            Boost(slot);
        }

        private void RestoreBoosted()
        {
            var boosted = _boostedSlot;
            _boostedSlot = null;

            if (boosted != null && boosted.Camera != null)
                RestoreOwnedState(boosted);
        }

        private void RestorePreviousBoosted(Slot slot)
        {
            if (_boostedSlot == null || ReferenceEquals(_boostedSlot, slot))
                return;

            var previous = _boostedSlot;
            _boostedSlot = null;

            if (previous.Camera != null)
                RestoreOwnedState(previous);
        }

        private void Boost(Slot slot)
        {
            var target = slot.Camera;
            if (target == null)
                return;

            // 作者状态必须在「首次改写前」抓取，而不是注册时：注册后作者仍可能改优先级 / 启停。
            // 只在尚未接管时抓，避免同一目标二次 boost 时把上一次的 boost 值当成作者值。
            if (!slot.PriorityOwned)
                slot.OriginalPriority = target.Priority;

            if (!target.gameObject.activeSelf && !slot.GameObjectEnabledByManager)
                slot.OriginalActiveSelf = target.gameObject.activeSelf;

            if (!target.enabled && !slot.ComponentEnabledByManager)
                slot.OriginalEnabled = target.enabled;

            if (!target.gameObject.activeSelf)
            {
                target.gameObject.SetActive(true);
                slot.GameObjectEnabledByManager = true;
            }

            if (!target.enabled)
            {
                target.enabled = true;
                slot.ComponentEnabledByManager = true;
            }

            target.Priority = ComputeBoostPriority();
            slot.PriorityOwned = true;
            target.Prioritize();
            _boostedSlot = slot;
        }

        private void RestoreOwnedState(Slot slot)
        {
            var camera = slot.Camera;
            if (camera == null)
            {
                slot.PriorityOwned = false;
                slot.GameObjectEnabledByManager = false;
                slot.ComponentEnabledByManager = false;
                return;
            }

            if (slot.PriorityOwned)
            {
                camera.Priority = slot.OriginalPriority;
                slot.PriorityOwned = false;
                camera.Prioritize();
            }

            if (slot.ComponentEnabledByManager)
            {
                camera.enabled = slot.OriginalEnabled;
                slot.ComponentEnabledByManager = false;
            }

            if (slot.GameObjectEnabledByManager)
            {
                camera.gameObject.SetActive(slot.OriginalActiveSelf);
                slot.GameObjectEnabledByManager = false;
            }
        }

        private int ComputeBoostPriority()
        {
            var max = int.MinValue;

            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Camera == null)
                    continue;

                var priority = slot.PriorityOwned ? slot.OriginalPriority.Value : slot.Camera.Priority.Value;
                if (priority > max)
                    max = priority;
            }

            if (_brain != null)
            {
                var count = CinemachineCore.VirtualCameraCount;
                for (var i = 0; i < count; i++)
                {
                    var vcam = CinemachineCore.GetVirtualCamera(i);
                    if (vcam == null || !_brain.IsValidChannel(vcam))
                        continue;

                    // 已注册相机由上面的槽位循环按「作者优先级」计入；这里只补未注册 vcam，
                    // 避免把目标自己上一轮的 boost 值当成场景最大优先级而逐次抬高。
                    if (FindSlotByCamera(vcam) != null)
                        continue;

                    var priority = vcam.Priority.Value;
                    if (priority > max)
                        max = priority;
                }
            }

            if (max == int.MaxValue)
                return int.MaxValue;

            return max < 0 ? 1 : max + 1;
        }

        #endregion

        #region Blend Override

        private CinemachineBlendDefinition OverrideBlend(
            ICinemachineCamera fromVcam, ICinemachineCamera toVcam,
            CinemachineBlendDefinition defaultBlend, UnityEngine.Object owner)
        {
            if (!_disposed && ReferenceEquals(owner, _brain) && _request != null &&
                ReferenceEquals(toVcam, _request.Target.Camera))
            {
                return _request.Definition;
            }

            return _previousBlendOverride != null
                ? _previousBlendOverride(fromVcam, toVcam, defaultBlend, owner)
                : defaultBlend;
        }

        #endregion

        #region Cinemachine Events

        private void BindSessionEvents()
        {
            if (_eventsBound)
                return;

            _eventsBound = true;
            CinemachineCore.CameraActivatedEvent.AddListener(OnCameraActivated);
            CinemachineCore.CameraDeactivatedEvent.AddListener(OnCameraDeactivated);
            CinemachineCore.BlendFinishedEvent.AddListener(OnBlendFinished);
            CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
        }

        private void UnsubscribeEvents()
        {
            if (!_eventsBound)
                return;

            _eventsBound = false;
            CinemachineCore.CameraActivatedEvent.RemoveListener(OnCameraActivated);
            CinemachineCore.CameraDeactivatedEvent.RemoveListener(OnCameraDeactivated);
            CinemachineCore.BlendFinishedEvent.RemoveListener(OnBlendFinished);
            CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);

            if (ReferenceEquals(CinemachineCore.GetBlendOverride, _blendOverrideDelegate))
                CinemachineCore.GetBlendOverride = _previousBlendOverride;
        }

        private void OnCameraActivated(ICinemachineCamera.ActivationEventParams evt)
        {
            if (_disposed || _detached || !ReferenceEquals(evt.Origin, _brain))
                return;

            var incomingId = ResolveCameraId(evt.IncomingCamera);
            // blend 开始帧的 OutgoingCamera 可能是 NestedBlendSource（不是原 vcam），
            // 用最近一次 incoming 的 id 兜底，保证消息里的 PreviousCameraId 有意义。
            var outgoingId = ResolveCameraId(evt.OutgoingCamera) ?? _lastActiveCameraId;
            CameraTransitionResult? settlement = null;

            if (_request != null && !ReferenceEquals(evt.IncomingCamera, _request.Target.Camera))
            {
                settlement = _request.Target.Camera == null
                    ? CameraTransitionResult.CameraNotFound
                    : CameraTransitionResult.Superseded;
            }
            else if (_request != null && evt.IsCut)
            {
                settlement = CameraTransitionResult.Completed;
            }

            Activated?.Invoke(new CameraBackendActivation(incomingId, outgoingId, evt.IsCut));
            if (evt.IsCut)
                BlendCompleted?.Invoke(incomingId);

            _lastActiveCameraId = incomingId;

            if (settlement.HasValue)
                SettleRequest(settlement.Value);
        }

        private void OnCameraDeactivated(ICinemachineMixer mixer, ICinemachineCamera camera)
        {
            if (_disposed || _detached || !ReferenceEquals(mixer, _brain))
                return;

            Deactivated?.Invoke(ResolveCameraId(camera));
        }

        private void OnBlendFinished(ICinemachineMixer mixer, ICinemachineCamera camera)
        {
            if (_disposed || _detached || !ReferenceEquals(mixer, _brain))
                return;

            BlendCompleted?.Invoke(ResolveCameraId(camera));

            if (_request != null && ReferenceEquals(camera, _request.Target.Camera))
                SettleRequest(CameraTransitionResult.Completed);
        }

        private void OnCameraUpdated(CinemachineBrain brain)
        {
            if (_disposed || _detached || _request == null || !ReferenceEquals(brain, _brain))
                return;

            if (_request.Target.Camera == null || _brain == null)
            {
                SettleRequest(CameraTransitionResult.CameraNotFound);
                return;
            }

            if (!_request.CountsAsTransition)
            {
                // cut 请求只对「下一次激活」有效：Brain 已经用 GetBlendOverride 取过定义，
                // 这一帧结束时即可清掉，避免污染之后的混合。
                var active = _brain.ActiveVirtualCamera;
                SettleRequest(ReferenceEquals(active, _request.Target.Camera)
                    ? CameraTransitionResult.Completed
                    : CameraTransitionResult.Superseded);
                return;
            }

            if (!_brain.IsBlending)
            {
                var active = _brain.ActiveVirtualCamera;
                SettleRequest(ReferenceEquals(active, _request.Target.Camera)
                    ? CameraTransitionResult.Completed
                    : CameraTransitionResult.Superseded);
            }
        }

        private void SettleRequest(CameraTransitionResult result)
        {
            var request = _request;
            if (request == null)
                return;

            _request = null;

            if (!_disposed && !_detached)
                RequestSettled?.Invoke(request.Target.CameraId, result);
        }

        #endregion

        #region Internal — Registry

        private Slot GetLiveSlot(string cameraId, string action)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (!_byId.TryGetValue(cameraId, out var slot))
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return null;
            }

            if (slot.Camera == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机 id {0} 对应的相机已被外部销毁，{1} 已忽略。", cameraId, action);
                return null;
            }

            return slot;
        }

        private Slot FindSlotByCamera(CinemachineVirtualCameraBase camera)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Camera != null && ReferenceEquals(slot.Camera, camera))
                    return slot;
            }

            return null;
        }

        private string ResolveCameraId(ICinemachineCamera camera)
        {
            var vcam = camera as CinemachineVirtualCameraBase;

            // 分组相机（ClearShot / MixingCamera）的活动子相机向上解析到已注册的父相机。
            for (var depth = 0; vcam != null && depth < 32; depth++)
            {
                var slot = FindSlotByCamera(vcam);
                if (slot != null)
                    return slot.CameraId;

                vcam = vcam.ParentCamera as CinemachineVirtualCameraBase;
            }

            return null;
        }

        private void ReleaseDestroyedSlots()
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                if (_slots[i].Camera == null)
                    ReleaseSlot(_slots[i]);
            }
        }

        private void ReleaseSlot(Slot slot)
        {
            if (_request != null && ReferenceEquals(_request.Target, slot))
                SettleRequest(CameraTransitionResult.CameraNotFound);
            if (ReferenceEquals(_boostedSlot, slot))
                _boostedSlot = null;
            if (string.Equals(_lastActiveCameraId, slot.CameraId, StringComparison.Ordinal))
                _lastActiveCameraId = null;

            _slots.Remove(slot);
            _byId.Remove(slot.CameraId);
        }

        #endregion

        #region Internal — Diagnostics / Warnings

        private void WarnIfActiveIsUnregistered()
        {
            if (_brain == null)
                return;

            var active = _brain.ActiveVirtualCamera;
            if (active == null)
                return;
            if (active is UnityEngine.Object unityObject && unityObject == null)
                return;

            if (ResolveCameraId(active) == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "Brain 的当前活动相机未在 CameraManager 注册（可能由 Timeline / 过场接管）；" +
                    "此时切换可能与过场冲突。");
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
                case CameraBlendStyle.EaseInOut:
                    return CinemachineBlendDefinition.Styles.EaseInOut;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style,
                        "[Camera] 未知的过渡样式。");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || _detached)
                throw new ObjectDisposedException(nameof(CinemachineCameraBackend));
        }

        #endregion
    }
}
