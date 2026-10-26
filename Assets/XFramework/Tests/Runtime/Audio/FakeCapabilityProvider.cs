using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 实现 <see cref="IAudioProvider"/> 与逐次参数 / 3D 位置能力接口的替身，记录控制面调用。
    /// <para>与 <see cref="FakeAudioProvider"/> 分开：后者刻意不实现能力接口，用来验证「provider 不支持时
    /// 门面返回 false」。本替身只服务正向转发与异常隔离用例。</para>
    /// </summary>
    internal sealed class FakeCapabilityProvider : IAudioProvider, IAudioParameterProvider, IAudioSpatialProvider,
        IAudioBankProvider, IAudioBusProvider, IAudioSnapshotProvider
    {
        #region Fields

        private readonly List<AudioProviderHandle> _live = new List<AudioProviderHandle>();
        private int _nextId;

        internal readonly List<(AudioProviderHandle Handle, AudioParameter Parameter)> Parameters =
            new List<(AudioProviderHandle, AudioParameter)>();

        internal readonly List<AudioParameter> GlobalParameters = new List<AudioParameter>();
        internal readonly List<(AudioProviderHandle Handle, Vector3 Position)> Positions =
            new List<(AudioProviderHandle, Vector3)>();

        internal readonly List<(string Bank, bool LoadSamples)> BankLoads =
            new List<(string, bool)>();
        internal readonly List<string> BankUnloads = new List<string>();
        internal readonly HashSet<string> LoadedBanks =
            new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<(string Bus, float Volume)> BusVolumes =
            new List<(string, float)>();
        internal readonly List<(string Bus, bool Muted)> BusMutes =
            new List<(string, bool)>();
        internal readonly List<(string Bus, bool Paused)> BusPauses =
            new List<(string, bool)>();
        internal readonly List<string> StartedSnapshots = new List<string>();
        internal readonly List<string> StoppedSnapshots = new List<string>();

        internal bool ThrowOnParameter;
        internal bool ThrowOnGlobalParameter;
        internal bool ThrowOnPosition;
        internal bool ThrowOnBankLoad;
        internal bool ThrowOnBankUnload;
        internal bool ThrowOnBankQuery;
        internal bool ThrowOnBusVolume;
        internal bool ThrowOnBusMuted;
        internal bool ThrowOnBusPaused;
        internal bool ThrowOnSnapshotStart;
        internal bool ThrowOnSnapshotStop;

        internal bool RejectParameter;
        internal bool RejectGlobalParameter;
        internal bool RejectPosition;
        internal bool RejectBankLoad;
        internal bool RejectBankUnload;
        internal bool RejectBankQuery;
        internal bool RejectBusVolume;
        internal bool RejectBusMuted;
        internal bool RejectBusPaused;
        internal bool RejectSnapshotStart;
        internal bool RejectSnapshotStop;

        internal bool Disposed;
        internal int StopCalls;
        internal int ReleaseCalls;
        internal int SetPausedCalls;
        internal int SetVolumeCalls;
        internal int PollCalls;

        #endregion

        #region IAudioProvider

        public UniTask<AudioProviderHandle> PlayAsync(string location, AudioPlaybackRequest request,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            if (request.ValidityToken.IsCancellationRequested)
                return UniTask.FromResult(default(AudioProviderHandle));

            var handle = new AudioProviderHandle(_nextId++, 1);
            _live.Add(handle);
            return UniTask.FromResult(handle);
        }

        public bool Stop(AudioProviderHandle handle)
        {
            StopCalls++;
            return _live.Remove(handle);
        }

        public void Release(AudioProviderHandle handle)
        {
            ReleaseCalls++;
            _live.Remove(handle);
        }

        public bool SetPaused(AudioProviderHandle handle, bool paused)
        {
            SetPausedCalls++;
            return _live.Contains(handle);
        }

        public bool SetVolume(AudioProviderHandle handle, float volume)
        {
            SetVolumeCalls++;
            return _live.Contains(handle);
        }

        public AudioProviderPlaybackState Poll(AudioProviderHandle handle)
        {
            PollCalls++;
            return _live.Contains(handle)
                ? AudioProviderPlaybackState.Playing
                : AudioProviderPlaybackState.Invalid;
        }

        public void Dispose()
        {
            Disposed = true;
            _live.Clear();
        }

        #endregion

        #region IAudioParameterProvider

        bool IAudioParameterProvider.TrySetParameter(AudioProviderHandle handle, AudioParameter parameter)
        {
            if (ThrowOnParameter)
                throw new InvalidOperationException("fake parameter boom");
            if (RejectParameter || !_live.Contains(handle))
                return false;

            Parameters.Add((handle, parameter));
            return true;
        }

        bool IAudioParameterProvider.TrySetGlobalParameter(AudioParameter parameter)
        {
            if (ThrowOnGlobalParameter)
                throw new InvalidOperationException("fake global parameter boom");
            if (RejectGlobalParameter)
                return false;

            GlobalParameters.Add(parameter);
            return true;
        }

        #endregion

        #region IAudioSpatialProvider

        bool IAudioSpatialProvider.TrySetPosition(AudioProviderHandle handle, Vector3 position)
        {
            if (ThrowOnPosition)
                throw new InvalidOperationException("fake position boom");
            if (RejectPosition || !_live.Contains(handle))
                return false;

            Positions.Add((handle, position));
            return true;
        }

        #endregion

        #region IAudioBankProvider

        bool IAudioBankProvider.TryLoadBank(string bank, bool loadSamples)
        {
            if (ThrowOnBankLoad)
                throw new InvalidOperationException("fake bank load boom");
            if (RejectBankLoad)
                return false;

            BankLoads.Add((bank, loadSamples));
            LoadedBanks.Add(bank);
            return true;
        }

        bool IAudioBankProvider.TryUnloadBank(string bank)
        {
            if (ThrowOnBankUnload)
                throw new InvalidOperationException("fake bank unload boom");
            if (RejectBankUnload)
                return false;

            BankUnloads.Add(bank);
            LoadedBanks.Remove(bank);
            return true;
        }

        bool IAudioBankProvider.IsBankLoaded(string bank)
        {
            if (ThrowOnBankQuery)
                throw new InvalidOperationException("fake bank query boom");
            if (RejectBankQuery)
                return false;

            return LoadedBanks.Contains(bank);
        }

        #endregion

        #region IAudioBusProvider

        bool IAudioBusProvider.TrySetBusVolume(string bus, float volume)
        {
            if (ThrowOnBusVolume)
                throw new InvalidOperationException("fake bus volume boom");
            if (RejectBusVolume)
                return false;

            BusVolumes.Add((bus, volume));
            return true;
        }

        bool IAudioBusProvider.TrySetBusMuted(string bus, bool muted)
        {
            if (ThrowOnBusMuted)
                throw new InvalidOperationException("fake bus muted boom");
            if (RejectBusMuted)
                return false;

            BusMutes.Add((bus, muted));
            return true;
        }

        bool IAudioBusProvider.TrySetBusPaused(string bus, bool paused)
        {
            if (ThrowOnBusPaused)
                throw new InvalidOperationException("fake bus paused boom");
            if (RejectBusPaused)
                return false;

            BusPauses.Add((bus, paused));
            return true;
        }

        #endregion

        #region IAudioSnapshotProvider

        bool IAudioSnapshotProvider.TryStartSnapshot(string snapshot)
        {
            if (ThrowOnSnapshotStart)
                throw new InvalidOperationException("fake snapshot start boom");
            if (RejectSnapshotStart)
                return false;

            StartedSnapshots.Add(snapshot);
            return true;
        }

        bool IAudioSnapshotProvider.TryStopSnapshot(string snapshot)
        {
            if (ThrowOnSnapshotStop)
                throw new InvalidOperationException("fake snapshot stop boom");
            if (RejectSnapshotStop)
                return false;

            StoppedSnapshots.Add(snapshot);
            return true;
        }

        #endregion
    }
}
