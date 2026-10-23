using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera.Messages;
using XFramework.XLog;
using XFramework.XMessage;

namespace XFramework.XCamera
{
    /// <summary>
    /// 门面与后端之间的中立导演层：唯一 id 生成、相机栈、异步等待、消息发布、快照组装与参数校验。
    /// <para>栈的语义：<c>[0]</c> 是基础相机，后续是覆盖层；<c>SwitchTo</c> 重置为单元素，
    /// <c>Push</c> 压入、<c>Pop</c> 弹出、<c>PopAll</c> 回到基础、<c>Clear</c> 释放。</para>
    /// <para>本类型只决定「目标 id」；优先级、混合与场景状态由 <see cref="ICameraBackend"/> 实现。</para>
    /// </summary>
    internal sealed class CameraDirector
    {
        #region Constants

        /// <summary>动态相机 id 前缀；显式 id 不得使用。</summary>
        internal const string AutoIdPrefix = "@auto/";

        /// <summary>小于等于该时长视为 cut。</summary>
        private const float CutThreshold = 0.0001f;

        #endregion

        #region Private Types

        /// <summary>等待一次激活请求结算的异步等待者。</summary>
        private sealed class PendingRequest
        {
            public string CameraId;
            public float Duration;
            public CameraBlendStyle Style;
            public bool IsCut;
            public UniTaskCompletionSource<CameraTransitionResult> Completion;
        }

        #endregion

        #region Private Fields

        private readonly ICameraBackend _backend;
        private readonly List<string> _stack = new List<string>();
        private readonly List<string> _idBuffer = new List<string>();
        private long _nextGeneratedId = 1;
        private PendingRequest _pending;
        private bool _issuing;
        private bool _disposed;

        #endregion

        #region Construction

        internal CameraDirector(ICameraBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _backend.Activated += OnBackendActivated;
            _backend.Deactivated += OnBackendDeactivated;
            _backend.BlendCompleted += OnBackendBlendCompleted;
            _backend.RequestSettled += OnRequestSettled;
        }

        #endregion

        #region Registry

        internal string Register(string cameraId, CinemachineVirtualCameraBase camera)
        {
            ValidateCameraId(cameraId);
            _backend.Register(cameraId, camera, false);
            return cameraId;
        }

        internal string Register(CinemachineVirtualCameraBase camera)
        {
            var cameraId = AutoIdPrefix + _nextGeneratedId.ToString(CultureInfo.InvariantCulture);
            _nextGeneratedId++;
            _backend.Register(cameraId, camera, true);
            return cameraId;
        }

        internal bool Unregister(string cameraId)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            var removedTop = _stack.Count > 0 &&
                             string.Equals(_stack[_stack.Count - 1], cameraId, StringComparison.Ordinal);
            if (!_backend.Unregister(cameraId))
                return false;

            _stack.RemoveAll(id => string.Equals(id, cameraId, StringComparison.Ordinal));

            if (removedTop)
            {
                var top = TopAliveId();
                if (top != null)
                    _backend.Activate(top, 0f, CameraBlendStyle.Cut);
            }

            return true;
        }

        internal bool TryGetCamera(string cameraId, out CinemachineVirtualCameraBase camera)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (_backend.TryGetCamera(cameraId, out var value) &&
                value is CinemachineVirtualCameraBase vcam)
            {
                camera = vcam;
                return true;
            }

            camera = null;
            return false;
        }

        #endregion

        #region Playback

        internal void SwitchTo(string cameraId, float duration, CameraBlendStyle style)
        {
            ValidateDuration(duration);
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!EnsureAlive(cameraId))
                return;

            var wasTop = _stack.Count > 0 &&
                         string.Equals(_stack[_stack.Count - 1], cameraId, StringComparison.Ordinal);
            var alreadyActive = !_backend.IsBlending &&
                                string.Equals(_backend.ActiveCameraId, cameraId, StringComparison.Ordinal);
            var activate = !(wasTop && alreadyActive);

            _stack.Clear();
            _stack.Add(cameraId);

            if (!activate)
                return;

            if (!_issuing)
                SettlePending(CameraTransitionResult.Superseded);

            _backend.Activate(cameraId, duration, style);
        }

        internal void Push(string cameraId, float duration, CameraBlendStyle style)
        {
            ValidateDuration(duration);
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            if (!EnsureAlive(cameraId))
                return;

            if (_stack.Count > 0 &&
                string.Equals(TopAliveId(), cameraId, StringComparison.Ordinal))
            {
                var alreadyActive = !_backend.IsBlending &&
                                    string.Equals(_backend.ActiveCameraId, cameraId, StringComparison.Ordinal);
                if (alreadyActive)
                    return;

                if (!_issuing)
                    SettlePending(CameraTransitionResult.Superseded);

                _backend.Activate(cameraId, duration, style);
                return;
            }

            if (!_issuing)
                SettlePending(CameraTransitionResult.Superseded);

            _stack.Add(cameraId);
            _backend.Activate(cameraId, duration, style);
        }

        internal void Pop(float duration, CameraBlendStyle style)
        {
            ValidateDuration(duration);
            PruneDeadTops();

            if (_stack.Count <= 1)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机栈没有可弹出的覆盖层；Pop 已忽略。");
                return;
            }

            _stack.RemoveAt(_stack.Count - 1);
            var top = TopAliveId();
            if (top == null)
                return;

            if (!_issuing)
                SettlePending(CameraTransitionResult.Superseded);

            _backend.Activate(top, duration, style);
        }

        internal void PopAll(float duration, CameraBlendStyle style)
        {
            ValidateDuration(duration);
            _stack.RemoveAll(id => !IsAlive(id));

            if (_stack.Count == 0)
                return;

            var baseId = _stack[0];
            var targetId = _backend.TransitionTargetId;
            var activeIsBase = string.Equals(_backend.ActiveCameraId, baseId, StringComparison.Ordinal);
            var headingToBase = string.Equals(targetId, baseId, StringComparison.Ordinal);

            if (_stack.Count == 1)
            {
                if (!headingToBase && (!activeIsBase || targetId != null))
                {
                    if (!_issuing)
                        SettlePending(CameraTransitionResult.Superseded);

                    _backend.Activate(baseId, duration, style);
                }

                return;
            }

            if (!_issuing)
                SettlePending(CameraTransitionResult.Superseded);

            _stack.Clear();
            _stack.Add(baseId);
            _backend.Activate(baseId, duration, style);
        }

        internal void Clear()
        {
            if (!_issuing)
                SettlePending(CameraTransitionResult.Superseded);

            _stack.Clear();
            _backend.ClearActive();
        }

        internal void SetFollow(string cameraId, Transform follow)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            _backend.SetFollow(cameraId, follow);
        }

        internal void SetLookAt(string cameraId, Transform lookAt)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            _backend.SetLookAt(cameraId, lookAt);
        }

        internal void SetFieldOfView(string cameraId, float fieldOfView)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            _backend.SetFieldOfView(cameraId, fieldOfView);
        }

        internal void Shake(float strength, float duration)
        {
            _backend.Shake(strength, duration);
        }

        #endregion

        #region Async Playback

        internal UniTask<CameraTransitionResult> SwitchToAsync(string cameraId, float duration,
            CameraBlendStyle style, CancellationToken cancellationToken)
        {
            ValidateDuration(duration);
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureAlive(cameraId))
                return UniTask.FromResult(CameraTransitionResult.CameraNotFound);

            if (_stack.Count == 1 &&
                string.Equals(_stack[0], cameraId, StringComparison.Ordinal) &&
                !_backend.IsBlending &&
                string.Equals(_backend.ActiveCameraId, cameraId, StringComparison.Ordinal))
            {
                return UniTask.FromResult(CameraTransitionResult.Completed);
            }

            if (TryJoinPending(cameraId, duration, style))
                return _pending.Completion.Task.AttachExternalCancellation(cancellationToken);

            SettlePending(CameraTransitionResult.Superseded);
            var pending = CreatePending(cameraId, duration, style);
            _issuing = true;
            try
            {
                SwitchTo(cameraId, duration, style);
            }
            finally
            {
                _issuing = false;
            }

            SettleIfSynchronouslyCompleted(pending);
            return pending.Completion.Task.AttachExternalCancellation(cancellationToken);
        }

        internal UniTask<CameraTransitionResult> PushAsync(string cameraId, float duration,
            CameraBlendStyle style, CancellationToken cancellationToken)
        {
            ValidateDuration(duration);
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureAlive(cameraId))
                return UniTask.FromResult(CameraTransitionResult.CameraNotFound);

            if (_stack.Count > 0 &&
                string.Equals(TopAliveId(), cameraId, StringComparison.Ordinal) &&
                !_backend.IsBlending &&
                string.Equals(_backend.ActiveCameraId, cameraId, StringComparison.Ordinal))
            {
                return UniTask.FromResult(CameraTransitionResult.Completed);
            }

            if (TryJoinPending(cameraId, duration, style))
                return _pending.Completion.Task.AttachExternalCancellation(cancellationToken);

            SettlePending(CameraTransitionResult.Superseded);
            var pending = CreatePending(cameraId, duration, style);
            _issuing = true;
            try
            {
                Push(cameraId, duration, style);
            }
            finally
            {
                _issuing = false;
            }

            SettleIfSynchronouslyCompleted(pending);
            return pending.Completion.Task.AttachExternalCancellation(cancellationToken);
        }

        internal UniTask<CameraTransitionResult> PopAsync(float duration, CameraBlendStyle style,
            CancellationToken cancellationToken)
        {
            ValidateDuration(duration);
            cancellationToken.ThrowIfCancellationRequested();

            _stack.RemoveAll(id => !IsAlive(id));
            if (_stack.Count <= 1)
                return UniTask.FromResult(CameraTransitionResult.Completed);

            var targetId = _stack[_stack.Count - 2];
            if (TryJoinPending(targetId, duration, style))
                return _pending.Completion.Task.AttachExternalCancellation(cancellationToken);

            SettlePending(CameraTransitionResult.Superseded);
            var pending = CreatePending(targetId, duration, style);
            _issuing = true;
            try
            {
                Pop(duration, style);
            }
            finally
            {
                _issuing = false;
            }

            SettleIfSynchronouslyCompleted(pending);
            return pending.Completion.Task.AttachExternalCancellation(cancellationToken);
        }

        internal UniTask<CameraTransitionResult> PopAllAsync(float duration, CameraBlendStyle style,
            CancellationToken cancellationToken)
        {
            ValidateDuration(duration);
            cancellationToken.ThrowIfCancellationRequested();

            _stack.RemoveAll(id => !IsAlive(id));
            if (_stack.Count == 0)
                return UniTask.FromResult(CameraTransitionResult.Completed);

            var targetId = _stack[0];
            if (_stack.Count == 1 &&
                _backend.TransitionTargetId == null &&
                !_backend.IsBlending &&
                string.Equals(_backend.ActiveCameraId, targetId, StringComparison.Ordinal))
            {
                return UniTask.FromResult(CameraTransitionResult.Completed);
            }

            if (TryJoinPending(targetId, duration, style))
                return _pending.Completion.Task.AttachExternalCancellation(cancellationToken);

            SettlePending(CameraTransitionResult.Superseded);
            var pending = CreatePending(targetId, duration, style);
            _issuing = true;
            try
            {
                PopAll(duration, style);
            }
            finally
            {
                _issuing = false;
            }

            SettleIfSynchronouslyCompleted(pending);
            return pending.Completion.Task.AttachExternalCancellation(cancellationToken);
        }

        #endregion

        #region Queries

        internal string ActiveCameraId => _backend.ActiveCameraId;

        internal CinemachineVirtualCameraBase ActiveCamera => _backend.ActiveCamera;

        internal string BaseCameraId => BaseAliveId();

        internal int StackDepth => _stack.Count;

        internal bool HasActiveCamera => _backend.HasActiveCamera;

        internal int CameraCount => _backend.CameraCount;

        internal Camera OutputCamera => _backend.OutputCamera;

        internal bool IsBlending => _backend.IsBlending;

        internal bool IsExternallyControlled
        {
            get
            {
                if (!_backend.HasActiveCamera)
                    return false;
                if (_backend.TransitionTargetId != null)
                    return false;

                return !string.Equals(_backend.ActiveCameraId, TopAliveId(), StringComparison.Ordinal);
            }
        }

        internal CameraStateSnapshot GetState()
        {
            return new CameraStateSnapshot(
                _backend.CameraCount,
                _backend.NamedCount,
                _backend.GeneratedCount,
                BaseAliveId(),
                _backend.ActiveCameraId,
                _backend.HasActiveCamera,
                _stack.Count,
                _backend.IsTransitioning,
                _backend.TransitionTargetId,
                _backend.IsBlending,
                _backend.TransitionProgress,
                IsExternallyControlled,
                _backend.OutputCamera);
        }

        internal int CopyCameras(List<CameraInfo> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            _backend.CopyCameraIds(_idBuffer);

            var activeId = _backend.ActiveCameraId;
            var targetId = _backend.TransitionTargetId;

            for (var i = 0; i < _idBuffer.Count; i++)
            {
                var cameraId = _idBuffer[i];
                var isAlive = _backend.TryGetCamera(cameraId, out _);
                var isBase = _stack.Count > 0 &&
                             string.Equals(_stack[0], cameraId, StringComparison.Ordinal);
                var isInStack = _stack.Contains(cameraId);

                buffer.Add(new CameraInfo(
                    cameraId,
                    cameraId.StartsWith(AutoIdPrefix, StringComparison.Ordinal),
                    isBase,
                    isInStack,
                    string.Equals(activeId, cameraId, StringComparison.Ordinal),
                    string.Equals(targetId, cameraId, StringComparison.Ordinal),
                    isAlive));
            }

            return buffer.Count;
        }

        #endregion

        #region Lifecycle

        internal void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stack.Clear();
            SettlePending(CameraTransitionResult.ManagerDisposed);
            UnsubscribeBackend();
            _backend.Dispose();
        }

        internal void ResetForNewSession()
        {
            _disposed = true;
            _stack.Clear();
            SettlePending(CameraTransitionResult.ManagerDisposed);
            UnsubscribeBackend();

            if (_backend is ICameraBackendSessionReset reset)
            {
                try
                {
                    reset.UnbindSessionEvents();
                }
                catch (Exception exception)
                {
                    LogManager.Warning(LogCategories.CameraManager,
                        "CameraManager.ResetForNewSession 退订旧后端时抛出异常，已忽略：{0}", exception);
                }
            }
        }

        #endregion

        #region Internal

        private bool TryJoinPending(string cameraId, float duration, CameraBlendStyle style)
        {
            if (_pending == null)
                return false;

            return string.Equals(_pending.CameraId, cameraId, StringComparison.Ordinal) &&
                   _pending.IsCut == IsCut(duration, style) &&
                   _pending.Style == style &&
                   Mathf.Approximately(_pending.Duration, duration);
        }

        private PendingRequest CreatePending(string cameraId, float duration, CameraBlendStyle style)
        {
            var pending = new PendingRequest
            {
                CameraId = cameraId,
                Duration = duration,
                Style = style,
                IsCut = IsCut(duration, style),
                Completion = new UniTaskCompletionSource<CameraTransitionResult>(),
            };

            _pending = pending;
            return pending;
        }

        private void SettlePending(CameraTransitionResult result)
        {
            var pending = _pending;
            if (pending == null)
                return;

            _pending = null;
            pending.Completion.TrySetResult(result);
        }

        private void SettleIfSynchronouslyCompleted(PendingRequest pending)
        {
            if (!ReferenceEquals(_pending, pending))
                return;
            if (_backend.TransitionTargetId != null)
                return;

            var result = string.Equals(_backend.ActiveCameraId, pending.CameraId, StringComparison.Ordinal)
                ? CameraTransitionResult.Completed
                : CameraTransitionResult.Superseded;
            SettlePending(result);
        }

        private static bool IsCut(float duration, CameraBlendStyle style)
            => duration <= CutThreshold || style == CameraBlendStyle.Cut;

        private void OnBackendActivated(CameraBackendActivation activation)
        {
            MessageManager.Publish(new CameraActivatedMessage(
                activation.IncomingCameraId, activation.OutgoingCameraId, activation.IsCut));
        }

        private void OnBackendDeactivated(string cameraId)
        {
            MessageManager.Publish(new CameraDeactivatedMessage(cameraId));

            if (cameraId == null)
                return;

            var wasTop = _stack.Count > 0 &&
                         string.Equals(_stack[_stack.Count - 1], cameraId, StringComparison.Ordinal);
            var removed = _stack.RemoveAll(id =>
                string.Equals(id, cameraId, StringComparison.Ordinal) && !IsAlive(id));

            if (removed == 0 || !wasTop || _issuing)
                return;
            if (_backend.TransitionTargetId != null)
                return;

            var top = TopAliveId();
            if (top != null)
                _backend.Activate(top, 0f, CameraBlendStyle.Cut);
        }

        private void OnBackendBlendCompleted(string cameraId)
        {
            MessageManager.Publish(new CameraBlendCompletedMessage(cameraId));
        }

        private void OnRequestSettled(string cameraId, CameraTransitionResult result)
        {
            if (_issuing || _pending == null)
                return;
            if (!string.Equals(_pending.CameraId, cameraId, StringComparison.Ordinal))
                return;

            SettlePending(result);
        }

        private void UnsubscribeBackend()
        {
            _backend.Activated -= OnBackendActivated;
            _backend.Deactivated -= OnBackendDeactivated;
            _backend.BlendCompleted -= OnBackendBlendCompleted;
            _backend.RequestSettled -= OnRequestSettled;
        }

        private bool EnsureAlive(string cameraId)
        {
            if (!_backend.IsRegistered(cameraId))
            {
                LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
                return false;
            }

            if (!_backend.TryGetCamera(cameraId, out _))
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "相机 id {0} 对应的相机已被外部销毁，操作已忽略。", cameraId);
                return false;
            }

            return true;
        }

        private bool IsAlive(string cameraId) => _backend.TryGetCamera(cameraId, out _);

        private string TopAliveId()
        {
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                if (IsAlive(_stack[i]))
                    return _stack[i];
            }

            return null;
        }

        private string BaseAliveId()
        {
            for (var i = 0; i < _stack.Count; i++)
            {
                if (IsAlive(_stack[i]))
                    return _stack[i];
            }

            return null;
        }

        private void PruneDeadTops()
        {
            while (_stack.Count > 0 && !IsAlive(_stack[_stack.Count - 1]))
                _stack.RemoveAt(_stack.Count - 1);
        }

        private static void ValidateCameraId(string cameraId)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (string.IsNullOrWhiteSpace(cameraId))
            {
                throw new ArgumentException(
                    "[Camera] 相机 id 不能为空或全空白。", nameof(cameraId));
            }

            if (cameraId.StartsWith(AutoIdPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "[Camera] 相机 id 不得以保留前缀 '" + AutoIdPrefix + "' 开头。", nameof(cameraId));
            }
        }

        private static void ValidateDuration(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "[Camera] 过渡时长必须是有限值；NaN / ±∞ 会让过渡永不结束。");
            }
        }

        #endregion
    }
}
