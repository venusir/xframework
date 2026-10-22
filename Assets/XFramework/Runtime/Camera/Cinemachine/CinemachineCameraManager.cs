using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera.Messages;
using XFramework.XLog;
using XFramework.XMessage;

namespace XFramework.XCamera.Cinemachine
{
    /// <summary>
    /// <see cref="ICameraManager"/> 的 Cinemachine 3.x 实现：命名 / 动态相机统一注册为槽位，
    /// 用 vcam 优先级做激活，用 <see cref="CinemachineBrain"/> 的混合做过渡。
    /// <para><b>一次一台活动相机</b>：激活 / 过渡只把目标提升到「场景内同通道最大优先级 + 1」，
    /// 不把其它相机压成 0；切换时恢复上一个目标由管理器改过的优先级 / 启用状态。</para>
    /// <para><b>过渡请求</b>：管理器安装 <see cref="CinemachineCore.GetBlendOverride"/>，对本次请求返回
    /// 调用方指定的 duration / style；<b>不修改</b> <see cref="CinemachineBrain.DefaultBlend"/>。
    /// 该委托是全局单委托，一个进程建议只创建一个相机管理器。</para>
    /// <para><b>Timeline / 过场边界</b>：过场相机由 Timeline 接管 Brain，不经过本管理器；若 Brain 的活动相机未注册
    /// （Timeline / 外来 vcam），激活 / 过渡会告警「可能与过场冲突」。</para>
    /// <para><b>只支持 Cinemachine 3.x</b>（按 3.1.7 的 API 编写）：2.x 的类型名与字段名不同，需自己的
    /// <see cref="ICameraManager"/> 实现。</para>
    /// </summary>
    public sealed class CinemachineCameraManager : ICameraManager, ICameraManagerSessionReset, IDisposable
    {
        #region Constants

        private const float CutThreshold = 0.0001f;
        private const int SuppressedPriority = int.MinValue;

        #endregion

        #region Private Types

        /// <summary>一个 vcam 槽位。槽位下标就是 <see cref="CameraHandle.Id"/>。</summary>
        private sealed class Slot
        {
            public CinemachineVirtualCameraBase Camera;
            public string NamedId;
            public int Generation;
            public bool Used;

            public PrioritySettings OriginalPriority;
            public bool OriginalActiveSelf;
            public bool OriginalEnabled;
            public bool PriorityOwned;
            public bool GameObjectEnabledByManager;
            public bool ComponentEnabledByManager;
        }

        /// <summary>一次由管理器发起的过渡请求。</summary>
        private sealed class PendingTransition
        {
            public int SlotIndex;
            public int Generation;
            public CinemachineVirtualCameraBase Camera;
            public float Duration;
            public CameraBlendStyle Style;
            public CinemachineBlendDefinition Definition;
            public bool CountsAsTransition;
            public UniTaskCompletionSource<CameraTransitionResult> Completion;
        }

        #endregion

        #region Private Fields

        private readonly CinemachineBrain _brain;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly CinemachineCore.GetBlendOverrideDelegate _previousBlendOverride;
        private readonly CinemachineCore.GetBlendOverrideDelegate _blendOverrideDelegate;
        private PendingTransition _pending;
        private int _boostedIndex = -1;
        private bool _eventsBound;
        private bool _disposed;
        private bool _detached;

        #endregion

        #region Construction

        /// <summary>用 Brain 构造实现；命名相机在构造后逐台 <see cref="Register{T}(string, T)"/>。</summary>
        /// <param name="brain">场景里控制 Unity Camera 的 Brain。</param>
        /// <exception cref="ArgumentNullException"><paramref name="brain"/> 为 <c>null</c>。</exception>
        public CinemachineCameraManager(CinemachineBrain brain)
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

        #region Public API — Registry

        /// <summary>注册一台命名相机；不激活、不改变场景状态。</summary>
        /// <typeparam name="T">必须是 <see cref="CinemachineVirtualCameraBase"/>。</typeparam>
        /// <param name="cameraId">命名 id。</param>
        /// <param name="camera">vcam。</param>
        /// <returns>相机句柄。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 或 <paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">相机已注册为另一 id、id 已被另一相机占用、对象已销毁或类型不符。</exception>
        public CameraHandle Register<T>(string cameraId, T camera) where T : class
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            var vcam = ValidateCamera(camera, nameof(camera));
            ReleaseDestroyedSlots();

            var existing = FindSlotByCamera(vcam);
            if (existing >= 0)
            {
                if (string.Equals(_slots[existing].NamedId, cameraId, StringComparison.Ordinal))
                    return HandleOf(existing);

                throw new ArgumentException(
                    "该相机已注册为 '" + FormatNamedId(_slots[existing].NamedId) +
                    "'，不能重复注册为 '" + cameraId + "'。", nameof(camera));
            }

            if (FindSlotByName(cameraId) >= 0)
            {
                throw new ArgumentException(
                    "相机 id '" + cameraId + "' 已被另一台相机占用；请先 Unregister。", nameof(cameraId));
            }

            return HandleOf(AllocateSlot(vcam, cameraId));
        }

        /// <summary>注册一台动态相机；不激活、不改变场景状态。</summary>
        /// <typeparam name="T">必须是 <see cref="CinemachineVirtualCameraBase"/>。</typeparam>
        /// <param name="camera">vcam。</param>
        /// <returns>相机句柄；<see cref="CameraHandle.NamedId"/> 为 <c>null</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">相机已注册为命名相机、对象已销毁或类型不符。</exception>
        public CameraHandle Register<T>(T camera) where T : class
        {
            ThrowIfDisposed();
            var vcam = ValidateCamera(camera, nameof(camera));
            ReleaseDestroyedSlots();

            var existing = FindSlotByCamera(vcam);
            if (existing >= 0)
            {
                if (_slots[existing].NamedId != null)
                {
                    throw new ArgumentException(
                        "该相机已注册为命名相机 '" + _slots[existing].NamedId +
                        "'；请先 Unregister 再按动态相机注册。", nameof(camera));
                }

                return HandleOf(existing);
            }

            return HandleOf(AllocateSlot(vcam, null));
        }

        /// <summary>注销一台相机；不销毁相机对象。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <returns>确实移除了才返回 <c>true</c>。</returns>
        public bool Unregister(CameraHandle handle)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            if (index < 0)
                return false;

            if (_pending != null && _pending.SlotIndex == index)
                CompletePending(CameraTransitionResult.TargetUnregistered);

            var slot = _slots[index];
            if (slot.Camera != null)
                RestoreOwnedState(slot);

            if (_brain != null && _brain.IsBlending)
                _brain.ActiveBlend = null;

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
                return _brain != null ? _brain.OutputCamera : null;
            }
        }

        /// <inheritdoc/>
        public CameraHandle ActiveHandle
        {
            get
            {
                ThrowIfDisposed();
                if (_brain == null)
                    return default;

                var active = _brain.ActiveVirtualCamera;
                if (active == null)
                    return default;

                var index = FindSlotByCamera(active);
                return index >= 0 ? HandleOf(index) : default;
            }
        }

        /// <inheritdoc/>
        public bool IsTransitioning => !_disposed && !_detached && _pending != null && _pending.CountsAsTransition;

        /// <inheritdoc/>
        public int CameraCount
        {
            get
            {
                ThrowIfDisposed();

                var count = 0;
                for (var i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Used)
                        count++;
                }

                return count;
            }
        }

        /// <inheritdoc/>
        public int CopyCameras(List<CameraInfo> buffer)
        {
            ThrowIfDisposed();
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            var active = _brain != null ? _brain.ActiveVirtualCamera : null;

            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.Used)
                    continue;

                var isActive = slot.Camera != null && active != null &&
                               ReferenceEquals(active, slot.Camera);
                var isTarget = _pending != null && _pending.SlotIndex == i;
                buffer.Add(new CameraInfo(HandleOf(i), isActive, isTarget, slot.Camera != null));
            }

            return buffer.Count;
        }

        /// <inheritdoc/>
        public CameraStateSnapshot GetState()
        {
            ThrowIfDisposed();

            var registered = 0;
            var named = 0;
            var dynamic = 0;
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.Used)
                    continue;

                registered++;
                if (slot.NamedId != null)
                    named++;
                else
                    dynamic++;
            }

            var pending = _pending;
            var isTransitioning = pending != null && pending.CountsAsTransition;
            var transitionTarget = isTransitioning ? HandleOf(pending.SlotIndex) : default;

            var progress = 0f;
            if (isTransitioning && _brain != null && _brain.ActiveBlend != null && pending.Duration > 0f)
                progress = Mathf.Clamp01(_brain.ActiveBlend.TimeInBlend / pending.Duration);

            return new CameraStateSnapshot(
                registered, named, dynamic,
                ActiveHandle, transitionTarget,
                isTransitioning,
                _brain != null && _brain.IsBlending,
                progress,
                _brain != null ? _brain.OutputCamera : null);
        }

        /// <inheritdoc/>
        public bool TryGetHandle(string cameraId, out CameraHandle handle)
        {
            ThrowIfDisposed();
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            var index = FindSlotByName(cameraId);
            if (index >= 0)
            {
                handle = HandleOf(index);
                return true;
            }

            handle = default;
            return false;
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

            if (_slots[index].Camera == null)
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

            if (_slots[index].Camera == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机句柄 {0} 对应的相机已被外部销毁，反激活已忽略。", handle);
                return;
            }

            DeactivateSlot(index);
        }

        /// <inheritdoc/>
        public void TransitionTo(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            ThrowIfDisposed();

            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "[Camera] 过渡时长必须是有限值；NaN / ±∞ 会让过渡永不结束。");
            }

            var index = Resolve(handle);
            if (index < 0)
            {
                LogManager.Warning(LogCategories.CameraManager, "相机句柄无效或已注销: {0}", handle);
                return;
            }

            if (_slots[index].Camera == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机句柄 {0} 对应的相机已被外部销毁，过渡已忽略。", handle);
                return;
            }

            if (duration <= CutThreshold || style == CameraBlendStyle.Cut)
            {
                ActivateSlot(index);
                return;
            }

            TransitionToSlot(index, duration, style);
        }

        /// <inheritdoc/>
        public UniTask<CameraTransitionResult> TransitionToAsync(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "[Camera] 过渡时长必须是有限值；NaN / ±∞ 会让过渡永不结束。");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var index = Resolve(handle);
            if (index < 0)
            {
                LogManager.Warning(LogCategories.CameraManager, "相机句柄无效或已注销: {0}", handle);
                return UniTask.FromResult(CameraTransitionResult.TargetUnregistered);
            }

            var slot = _slots[index];
            var target = slot.Camera;
            if (target == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机句柄 {0} 对应的相机已被外部销毁，过渡已忽略。", handle);
                return UniTask.FromResult(CameraTransitionResult.TargetUnregistered);
            }

            if (_brain == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "CinemachineBrain 已被外部销毁，过渡已忽略。");
                return UniTask.FromResult(CameraTransitionResult.TargetUnregistered);
            }

            if (!_brain.IsBlending && ReferenceEquals(_brain.ActiveVirtualCamera, target))
                return UniTask.FromResult(CameraTransitionResult.Completed);

            if (duration <= CutThreshold || style == CameraBlendStyle.Cut)
            {
                var cutCompletion = new UniTaskCompletionSource<CameraTransitionResult>();
                ActivateSlot(index, cutCompletion);
                return cutCompletion.Task.AttachExternalCancellation(cancellationToken);
            }

            if (_pending != null &&
                _pending.SlotIndex == index &&
                _pending.Generation == slot.Generation &&
                _pending.CountsAsTransition &&
                _pending.Style == style &&
                Mathf.Approximately(_pending.Duration, duration))
            {
                if (_pending.Completion == null)
                    _pending.Completion = new UniTaskCompletionSource<CameraTransitionResult>();

                return _pending.Completion.Task.AttachExternalCancellation(cancellationToken);
            }

            var completion = new UniTaskCompletionSource<CameraTransitionResult>();
            TransitionToSlot(index, duration, style, completion);
            return completion.Task.AttachExternalCancellation(cancellationToken);
        }

        /// <inheritdoc/>
        public bool IsActive(CameraHandle handle)
        {
            ThrowIfDisposed();
            var index = Resolve(handle);
            if (index < 0 || _slots[index].Camera == null || _brain == null)
                return false;

            var active = _brain.ActiveVirtualCamera;
            return active != null && ReferenceEquals(active, _slots[index].Camera);
        }

        #endregion

        #region IDisposable

        /// <summary>恢复管理器改过的场景状态、退订静态事件、清空注册表；不销毁 Brain / vcam。</summary>
        public void Dispose()
        {
            if (_disposed || _detached)
                return;

            _disposed = true;
            UnsubscribeEvents();
            CompletePending(CameraTransitionResult.ManagerDisposed);

            _boostedIndex = -1;

            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Used && slot.Camera != null)
                    RestoreOwnedState(slot);
            }

            _slots.Clear();
        }

        void ICameraManagerSessionReset.UnbindSessionEvents()
        {
            if (_disposed || _detached)
                return;

            // 会话边界：只退订静态事件并清引用，绝不访问场景对象（上一会话的 Brain / vcam 可能已销毁）。
            _detached = true;
            UnsubscribeEvents();
            CompletePending(CameraTransitionResult.ManagerDisposed);
            _boostedIndex = -1;
            _slots.Clear();
        }

        #endregion

        #region Activation / Transition

        private void ActivateSlot(int index,
            UniTaskCompletionSource<CameraTransitionResult> completion = null)
        {
            var target = _slots[index].Camera;
            if (target == null)
            {
                completion?.TrySetResult(CameraTransitionResult.TargetUnregistered);
                return;
            }

            CompletePending(CameraTransitionResult.Superseded);
            WarnIfActiveIsUnregistered();

            // cut 也走 GetBlendOverride：给本次激活一个 Cut 定义，避免 Brain 用项目默认混合
            // 把「立即切换」变成一次过渡。CountsAsTransition=false 表示它不进入 IsTransitioning。
            _pending = new PendingTransition
            {
                SlotIndex = index,
                Generation = _slots[index].Generation,
                Camera = target,
                Duration = 0f,
                Style = CameraBlendStyle.Cut,
                Definition = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.Cut, 0f),
                CountsAsTransition = false,
                Completion = completion,
            };

            RestorePreviousBoosted(index);
            Boost(index);

            // 已在混合且目标就是当前活动时，改优先级不会重启混合——直接清掉当前 blend 强制完成它。
            // 上游官方说明（CinemachineBrain.ActiveBlend）：把 ActiveBlend 设为 null 正是「强制完成混合」
            // 的受支持入口（R3-CIN-001）。
            if (_brain != null && _brain.IsBlending)
                _brain.ActiveBlend = null;
        }

        private void TransitionToSlot(int index, float duration, CameraBlendStyle style,
            UniTaskCompletionSource<CameraTransitionResult> completion = null)
        {
            var target = _slots[index].Camera;
            if (target == null)
            {
                completion?.TrySetResult(CameraTransitionResult.TargetUnregistered);
                return;
            }

            if (_brain == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "CinemachineBrain 已被外部销毁，过渡已忽略。");
                completion?.TrySetResult(CameraTransitionResult.TargetUnregistered);
                return;
            }

            if (!_brain.IsBlending && ReferenceEquals(_brain.ActiveVirtualCamera, target))
            {
                completion?.TrySetResult(CameraTransitionResult.Completed);
                return;
            }

            CompletePending(CameraTransitionResult.Superseded);
            WarnIfActiveIsUnregistered();

            _pending = new PendingTransition
            {
                SlotIndex = index,
                Generation = _slots[index].Generation,
                Camera = target,
                Duration = duration,
                Style = style,
                Definition = new CinemachineBlendDefinition(Map(style), duration),
                CountsAsTransition = true,
                Completion = completion,
            };

            RestorePreviousBoosted(index);
            Boost(index);
        }

        private void DeactivateSlot(int index)
        {
            var slot = _slots[index];

            if (_pending != null && _pending.SlotIndex == index)
                CompletePending(CameraTransitionResult.Superseded);

            if (slot.Camera == null)
                return;

            var wasEnabledByManager = slot.GameObjectEnabledByManager && !slot.OriginalActiveSelf;
            RestoreOwnedState(slot);

            if (!wasEnabledByManager && slot.Camera != null && slot.Camera.gameObject.activeSelf)
            {
                slot.Camera.Priority = SuppressedPriority;
                slot.PriorityOwned = true;
                slot.Camera.Prioritize();
            }

            if (_boostedIndex == index)
                _boostedIndex = -1;
        }

        private void Boost(int index)
        {
            var slot = _slots[index];
            var target = slot.Camera;
            if (target == null)
                return;

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
            _boostedIndex = index;
        }

        private void RestorePreviousBoosted(int index)
        {
            if (_boostedIndex < 0 || _boostedIndex == index)
                return;

            if (_boostedIndex < _slots.Count)
            {
                var previous = _slots[_boostedIndex];
                if (previous.Used && previous.Camera != null)
                    RestoreOwnedState(previous);
            }

            _boostedIndex = -1;
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
                if (!slot.Used || slot.Camera == null)
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
                    if (FindSlotByCamera(vcam) >= 0)
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
            if (!_disposed && ReferenceEquals(owner, _brain) && _pending != null &&
                ReferenceEquals(toVcam, _pending.Camera))
            {
                return _pending.Definition;
            }

            return _previousBlendOverride != null
                ? _previousBlendOverride(fromVcam, toVcam, defaultBlend, owner)
                : defaultBlend;
        }

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

            var incomingIndex = FindSlotByCamera(evt.IncomingCamera);
            var outgoingIndex = FindSlotByCamera(evt.OutgoingCamera);

            // 新 incoming 不是本管理器的 pending 目标 → 本次过渡已被取代；
            // 若 pending 目标本身已被外部销毁，则按目标注销报告。
            if (_pending != null && incomingIndex != _pending.SlotIndex)
            {
                CompletePending(_pending.Camera == null
                    ? CameraTransitionResult.TargetUnregistered
                    : CameraTransitionResult.Superseded);
            }

            if (incomingIndex < 0)
                return; // Timeline / 外来 vcam：不发布激活消息

            var incoming = HandleOf(incomingIndex);
            var previous = outgoingIndex >= 0 ? HandleOf(outgoingIndex) : default;
            MessageManager.Publish(new CameraActivatedMessage(incoming, previous, evt.IsCut));

            if (evt.IsCut)
            {
                MessageManager.Publish(new CameraBlendCompletedMessage(incoming));
                CompletePending(CameraTransitionResult.Completed);
            }
        }

        private void OnCameraDeactivated(ICinemachineMixer mixer, ICinemachineCamera camera)
        {
            if (_disposed || _detached || !ReferenceEquals(mixer, _brain))
                return;

            var index = FindSlotByCamera(camera);
            if (index >= 0)
                MessageManager.Publish(new CameraDeactivatedMessage(HandleOf(index)));
        }

        private void OnBlendFinished(ICinemachineMixer mixer, ICinemachineCamera camera)
        {
            if (_disposed || _detached || !ReferenceEquals(mixer, _brain))
                return;

            var index = FindSlotByCamera(camera);
            if (index >= 0)
                MessageManager.Publish(new CameraBlendCompletedMessage(HandleOf(index)));

            if (_pending != null && _pending.SlotIndex == index)
                CompletePending(CameraTransitionResult.Completed);
        }

        private void OnCameraUpdated(CinemachineBrain brain)
        {
            if (_disposed || _detached || _pending == null)
                return;

            if (!ReferenceEquals(brain, _brain))
                return;

            if (_pending.Camera == null)
            {
                CompletePending(CameraTransitionResult.TargetUnregistered);
                return;
            }

            if (_brain == null)
            {
                CompletePending(CameraTransitionResult.TargetUnregistered);
                return;
            }

            if (!_pending.CountsAsTransition)
            {
                // cut 请求只对「下一次激活」有效：Brain 已经用 GetBlendOverride 取过定义，
                // 这一帧结束时即可清掉，避免污染之后的混合。
                var active = _brain.ActiveVirtualCamera;
                CompletePending(ReferenceEquals(active, _pending.Camera)
                    ? CameraTransitionResult.Completed
                    : CameraTransitionResult.Superseded);
                return;
            }

            if (!_brain.IsBlending)
            {
                var active = _brain.ActiveVirtualCamera;
                CompletePending(ReferenceEquals(active, _pending.Camera)
                    ? CameraTransitionResult.Completed
                    : CameraTransitionResult.Superseded);
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

        #endregion

        #region Internal — Slots

        private CinemachineVirtualCameraBase ValidateCamera<T>(T camera, string paramName) where T : class
        {
            if (camera == null)
                throw new ArgumentNullException(paramName);

            if (camera is UnityEngine.Object unityObject && unityObject == null)
            {
                throw new ArgumentException(
                    "[Camera] 相机对象已被销毁，不能注册。", paramName);
            }

            if (camera is not CinemachineVirtualCameraBase vcam)
            {
                throw new ArgumentException(
                    "CinemachineCameraManager 只接受 CinemachineVirtualCameraBase 类型的相机。", paramName);
            }

            return vcam;
        }

        private int AllocateSlot(CinemachineVirtualCameraBase camera, string namedId)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Used)
                    continue;

                InitializeSlot(slot, camera, namedId);
                return i;
            }

            var newSlot = new Slot();
            _slots.Add(newSlot);
            InitializeSlot(newSlot, camera, namedId);
            return _slots.Count - 1;
        }

        private static void InitializeSlot(Slot slot, CinemachineVirtualCameraBase camera, string namedId)
        {
            slot.Used = true;
            slot.Camera = camera;
            slot.NamedId = namedId;
            slot.Generation = slot.Generation == int.MaxValue ? 1 : slot.Generation + 1;
            slot.OriginalPriority = camera.Priority;
            slot.OriginalActiveSelf = camera.gameObject.activeSelf;
            slot.OriginalEnabled = camera.enabled;
            slot.PriorityOwned = false;
            slot.GameObjectEnabledByManager = false;
            slot.ComponentEnabledByManager = false;
        }

        private void ReleaseSlot(int index)
        {
            var slot = _slots[index];

            if (_pending != null && _pending.SlotIndex == index)
                CompletePending(CameraTransitionResult.TargetUnregistered);
            if (_boostedIndex == index)
                _boostedIndex = -1;

            slot.Camera = null;
            slot.NamedId = null;
            slot.Used = false;
            slot.PriorityOwned = false;
            slot.GameObjectEnabledByManager = false;
            slot.ComponentEnabledByManager = false;
        }

        private void ReleaseDestroyedSlots()
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                if (_slots[i].Used && _slots[i].Camera == null)
                    ReleaseSlot(i);
            }
        }

        private int FindSlotByCamera(ICinemachineCamera camera)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.Used || slot.Camera == null)
                    continue;

                if (ReferenceEquals(slot.Camera, camera))
                    return i;
            }

            return -1;
        }

        private int FindSlotByName(string cameraId)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Used && string.Equals(slot.NamedId, cameraId, StringComparison.Ordinal))
                    return i;
            }

            return -1;
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

        private CameraHandle HandleOf(int index)
        {
            var slot = _slots[index];
            return new CameraHandle(index, slot.Generation, slot.NamedId);
        }

        private static string FormatNamedId(string namedId)
        {
            return namedId ?? "(动态)";
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

            if (FindSlotByCamera(active) < 0)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "Brain 的当前活动相机未在 CameraManager 注册（可能由 Timeline / 过场接管）；" +
                    "此时 Activate / TransitionTo 可能与过场冲突。");
            }
        }

        private void CompletePending(CameraTransitionResult result)
        {
            var pending = _pending;
            if (pending == null)
                return;

            _pending = null;
            pending.Completion?.TrySetResult(result);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || _detached)
                throw new ObjectDisposedException(nameof(CinemachineCameraManager));
        }

        #endregion
    }
}
