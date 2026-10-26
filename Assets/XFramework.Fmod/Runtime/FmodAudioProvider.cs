using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAudio;
using XFramework.XLog;

namespace XFramework.XAudio.Fmod
{
    /// <summary>
    /// FMOD 的 FMOD-free provider：只依赖 <see cref="IFmodEventBackend"/> 的 <c>int</c> 句柄，
    /// 干净克隆（无 FMOD）也能编译与用 fake backend 测试。
    /// <para><b>provider 只做播放原语</b>；通道、三档音量、暂停闸门、计数与诊断仍由核心
    /// <c>AudioManagerImpl</c> 负责。provider 句柄是 <c>槽位下标 + 代际</c>，不把 FMOD 实例塞进
    /// <c>int</c>。</para>
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// </summary>
    internal sealed class FmodAudioProvider : IAudioProvider
    {
        #region Private Fields

        private readonly IFmodEventBackend _backend;
        private readonly FmodProviderOptions _options;
        private readonly FmodEventSlotTable _slots;

        private bool _disposed;

        #endregion

        #region Constructors

        internal FmodAudioProvider(IFmodEventBackend backend, FmodProviderOptions options)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _options = options;
            _slots = new FmodEventSlotTable(options.MaxVoices);
        }

        #endregion

        #region IAudioProvider

        /// <inheritdoc/>
        public UniTask<AudioProviderHandle> PlayAsync(string location, AudioPlaybackRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return UniTask.FromResult(default(AudioProviderHandle));

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            if (request.ValidityToken.IsCancellationRequested)
                return UniTask.FromResult(default(AudioProviderHandle));

            if (!_slots.TryReserve(out int slotIndex, out AudioProviderHandle handle))
            {
                LogManager.Warning(LogCategories.Audio,
                    "FMOD provider 槽位已满（MaxVoices={0}），播放 '{1}' 失败。",
                    _options.MaxVoices, location);
                return UniTask.FromResult(default(AudioProviderHandle));
            }

            string eventPath = BuildEventPath(location);
            int backendId = 0;

            try
            {
                backendId = _backend.Create(eventPath);
                if (backendId == 0)
                {
                    _slots.ReleaseSlot(slotIndex);
                    LogManager.Warning(LogCategories.Audio, "FMOD 事件 '{0}' 创建失败。", eventPath);
                    return UniTask.FromResult(default(AudioProviderHandle));
                }

                _slots.SetBackendId(slotIndex, backendId);
                ApplyInitialParameters(backendId, eventPath, request.InitialParameters);

                if (!_backend.SetVolume(backendId, request.Volume) ||
                    !_backend.SetPitch(backendId, request.Pitch) ||
                    (request.SpatialBlend > 0f &&
                     !_backend.Set3DAttributes(backendId, request.Position)))
                {
                    ReleaseBackendAndSlot(slotIndex, backendId);
                    LogManager.Warning(LogCategories.Audio,
                        "FMOD 事件 '{0}' 的播放参数设置失败，已释放。", eventPath);
                    return UniTask.FromResult(default(AudioProviderHandle));
                }

                if (!_backend.Start(backendId))
                {
                    ReleaseBackendAndSlot(slotIndex, backendId);
                    LogManager.Warning(LogCategories.Audio, "FMOD 事件 '{0}' 起播失败。", eventPath);
                    return UniTask.FromResult(default(AudioProviderHandle));
                }

                if (request.Paused && !_backend.SetPaused(backendId, true))
                {
                    ReleaseBackendAndSlot(slotIndex, backendId);
                    LogManager.Warning(LogCategories.Audio,
                        "FMOD 事件 '{0}' 的暂停设置失败，已释放。", eventPath);
                    return UniTask.FromResult(default(AudioProviderHandle));
                }

                if (request.ValidityToken.IsCancellationRequested)
                {
                    ReleaseBackendAndSlot(slotIndex, backendId);
                    return UniTask.FromResult(default(AudioProviderHandle));
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    ReleaseBackendAndSlot(slotIndex, backendId);
                    backendId = 0; // 已释放，避免 catch 分支重复 Release
                    throw new OperationCanceledException(cancellationToken);
                }

                return UniTask.FromResult(handle);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ReleaseBackendAndSlot(slotIndex, backendId);
                throw;
            }
            catch (Exception e)
            {
                ReleaseBackendAndSlot(slotIndex, backendId);
                LogException(e, "PlayAsync");
                return UniTask.FromResult(default(AudioProviderHandle));
            }
        }

        /// <inheritdoc/>
        public bool Stop(AudioProviderHandle handle)
        {
            if (_disposed || !_slots.TryGet(handle, out _, out int backendId))
                return false;

            try
            {
                return _backend.Stop(backendId);
            }
            catch (Exception e)
            {
                LogException(e, "Stop");
                return false;
            }
        }

        /// <inheritdoc/>
        public void Release(AudioProviderHandle handle)
        {
            if (_disposed || !_slots.TryGet(handle, out int slotIndex, out int backendId))
                return;

            try
            {
                _backend.Release(backendId);
            }
            catch (Exception e)
            {
                LogException(e, "Release");
            }
            finally
            {
                _slots.ReleaseSlot(slotIndex);
            }
        }

        /// <inheritdoc/>
        public bool SetPaused(AudioProviderHandle handle, bool paused)
        {
            if (_disposed || !_slots.TryGet(handle, out _, out int backendId))
                return false;

            try
            {
                return _backend.SetPaused(backendId, paused);
            }
            catch (Exception e)
            {
                LogException(e, "SetPaused");
                return false;
            }
        }

        /// <inheritdoc/>
        public bool SetVolume(AudioProviderHandle handle, float volume)
        {
            if (_disposed || !_slots.TryGet(handle, out _, out int backendId))
                return false;

            try
            {
                return _backend.SetVolume(backendId, volume);
            }
            catch (Exception e)
            {
                LogException(e, "SetVolume");
                return false;
            }
        }

        /// <inheritdoc/>
        public AudioProviderPlaybackState Poll(AudioProviderHandle handle)
        {
            if (_disposed || !_slots.TryGet(handle, out _, out int backendId))
                return AudioProviderPlaybackState.Invalid;

            try
            {
                var state = _backend.Poll(backendId);
                return (uint)state <= (uint)AudioProviderPlaybackState.Stopped
                    ? state
                    : AudioProviderPlaybackState.Invalid;
            }
            catch (Exception e)
            {
                LogException(e, "Poll");
                return AudioProviderPlaybackState.Invalid;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            for (int i = 0; i < _slots.Capacity; i++)
            {
                if (!_slots.TryGetLive(i, out int backendId))
                    continue;

                try
                {
                    _backend.Release(backendId);
                }
                catch (Exception e)
                {
                    LogException(e, "Dispose/Release");
                }
            }

            _slots.Clear();

            try
            {
                _backend.Dispose();
            }
            catch (Exception e)
            {
                LogException(e, "Dispose");
            }
        }

        #endregion

        #region Private

        private string BuildEventPath(string location)
        {
            string prefix = _options.EventPathPrefix;
            if (string.IsNullOrEmpty(prefix))
                return location;

            if (location.StartsWith(prefix, StringComparison.Ordinal))
                return location;

            return prefix + location;
        }

        private void ApplyInitialParameters(int backendId, string eventPath, AudioParameter[] parameters)
        {
            if (parameters == null)
                return;

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (string.IsNullOrWhiteSpace(parameter.Name))
                    continue;

                bool applied;
                try
                {
                    applied = _backend.SetParameter(backendId, parameter);
                }
                catch (Exception e)
                {
                    LogException(e, "SetParameter");
                    continue;
                }

                if (!applied)
                {
                    LogManager.Warning(LogCategories.Audio,
                        "FMOD 事件 '{0}' 的参数 '{1}' 应用失败，已忽略。",
                        eventPath, parameter.Name);
                }
            }
        }

        private void ReleaseBackendAndSlot(int slotIndex, int backendId)
        {
            if (backendId != 0)
            {
                try
                {
                    _backend.Release(backendId);
                }
                catch (Exception e)
                {
                    LogException(e, "Release");
                }
            }

            _slots.ReleaseSlot(slotIndex);
        }

        private static void LogException(Exception exception, string operation)
        {
            LogManager.Exception(LogCategories.Audio, exception,
                string.Format("FMOD provider 的 {0} 调用抛出异常，已隔离。", operation));
        }

        #endregion
    }
}
