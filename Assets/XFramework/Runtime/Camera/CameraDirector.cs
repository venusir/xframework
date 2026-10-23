using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera
{
    /// <summary>
    /// 门面与后端之间的中立导演层：唯一 id 生成、相机栈、快照组装与参数校验。
    /// <para>栈的语义：<c>[0]</c> 是基础相机，后续是覆盖层；<c>SwitchTo</c> 重置为单元素，
    /// <c>Push</c> 压入、<c>Pop</c> 弹出、<c>PopAll</c> 回到基础、<c>Clear</c> 释放。</para>
    /// <para>本类型只决定「目标 id」；优先级、混合与场景状态由 <see cref="ICameraBackend"/> 实现。</para>
    /// </summary>
    internal sealed class CameraDirector
    {
        #region Constants

        /// <summary>动态相机 id 前缀；显式 id 不得使用。</summary>
        internal const string AutoIdPrefix = "@auto/";

        #endregion

        #region Private Fields

        private readonly ICameraBackend _backend;
        private readonly List<string> _stack = new List<string>();
        private readonly List<string> _idBuffer = new List<string>();
        private long _nextGeneratedId = 1;
        private bool _disposed;

        #endregion

        #region Construction

        internal CameraDirector(ICameraBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        #endregion

        #region Registry

        internal string Register(string cameraId, UnityEngine.Object camera)
        {
            ValidateCameraId(cameraId);
            _backend.Register(cameraId, camera, false);
            return cameraId;
        }

        internal string Register(UnityEngine.Object camera)
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

        internal bool TryGetCamera<T>(string cameraId, out T camera) where T : class
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (_backend.TryGetCamera(cameraId, out var value))
            {
                camera = value as T;
                return camera != null;
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

            _stack.Clear();
            _stack.Add(cameraId);

            if (!(wasTop && alreadyActive))
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
                if (!alreadyActive)
                    _backend.Activate(cameraId, duration, style);
                return;
            }

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
            if (top != null)
                _backend.Activate(top, duration, style);
        }

        internal void PopAll(float duration, CameraBlendStyle style)
        {
            ValidateDuration(duration);
            _stack.RemoveAll(id => !IsAlive(id));

            if (_stack.Count == 0)
                return;

            var baseId = _stack[0];
            if (_stack.Count == 1)
            {
                if (!string.Equals(_backend.ActiveCameraId, baseId, StringComparison.Ordinal) &&
                    _backend.TransitionTargetId == null)
                {
                    _backend.Activate(baseId, duration, style);
                }

                return;
            }

            _stack.Clear();
            _stack.Add(baseId);
            _backend.Activate(baseId, duration, style);
        }

        internal void Clear()
        {
            _stack.Clear();
            _backend.ClearActive();
        }

        #endregion

        #region Queries

        internal string ActiveCameraId => _backend.ActiveCameraId;

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
            _backend.Dispose();
        }

        internal void ResetForNewSession()
        {
            _disposed = true;
            _stack.Clear();

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
