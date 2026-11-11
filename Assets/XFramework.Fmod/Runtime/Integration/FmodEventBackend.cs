using System;
using System.Collections.Generic;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod.Unity
{
    /// <summary>
    /// 真实 FMOD 2.04 backend。把 <see cref="IFmodEventBackend"/> 的 <c>int</c> 句柄映射到
    /// <see cref="EventInstance"/>，负责事件、bank、bus、snapshot 与状态查询。
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// </summary>
    internal sealed class FmodEventBackend : IFmodEventBackend
    {
        #region Private Fields

        private readonly Dictionary<int, EventInstance> _instances =
            new Dictionary<int, EventInstance>();

        private readonly Dictionary<string, EventInstance> _snapshots =
            new Dictionary<string, EventInstance>(StringComparer.Ordinal);

        private readonly List<string> _ownedBanks = new List<string>();
        private readonly bool _ownsBanks;

        private int _nextId = 1;
        private bool _disposed;

        #endregion

        #region Constructors

        internal FmodEventBackend(bool ownsBanks)
        {
            _ownsBanks = ownsBanks;
        }

        #endregion

        #region IFmodEventBackend

        /// <inheritdoc/>
        public int Create(string eventPath)
        {
            if (_disposed || string.IsNullOrWhiteSpace(eventPath))
                return 0;

            EventInstance instance;
            try
            {
                instance = RuntimeManager.CreateInstance(eventPath);
            }
            catch (EventNotFoundException)
            {
                return 0;
            }
            catch (SystemNotInitializedException)
            {
                return 0;
            }

            if (!instance.isValid())
            {
                SafeStopAndRelease(instance, FMOD.Studio.STOP_MODE.IMMEDIATE);
                return 0;
            }

            int id = AllocateId();
            _instances.Add(id, instance);
            return id;
        }

        /// <inheritdoc/>
        public bool Start(int instanceId)
        {
            if (!TryGet(instanceId, out EventInstance instance))
                return false;

            return instance.start() == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool Stop(int instanceId)
        {
            if (!TryGet(instanceId, out EventInstance instance))
                return false;

            return instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE) == RESULT.OK;
        }

        /// <inheritdoc/>
        public void Release(int instanceId)
        {
            if (!_instances.TryGetValue(instanceId, out EventInstance instance))
                return;

            _instances.Remove(instanceId);
            SafeStopAndRelease(instance, FMOD.Studio.STOP_MODE.IMMEDIATE);
        }

        /// <inheritdoc/>
        public bool SetPaused(int instanceId, bool paused)
        {
            if (!TryGet(instanceId, out EventInstance instance))
                return false;

            return instance.setPaused(paused) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool SetVolume(int instanceId, float volume)
        {
            if (!TryGet(instanceId, out EventInstance instance))
                return false;

            return instance.setVolume(volume) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool SetPitch(int instanceId, float pitch)
        {
            if (!TryGet(instanceId, out EventInstance instance))
                return false;

            return instance.setPitch(pitch) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool Set3DAttributes(int instanceId, Vector3 position)
        {
            if (!TryGet(instanceId, out EventInstance instance))
                return false;

            return instance.set3DAttributes(position.To3DAttributes()) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool SetParameter(int instanceId, AudioParameter parameter)
        {
            if (!TryGet(instanceId, out EventInstance instance))
                return false;

            if (string.IsNullOrWhiteSpace(parameter.Name))
                return false;

            if (parameter.IsLabel)
            {
                if (string.IsNullOrEmpty(parameter.Label))
                    return false;

                return instance.setParameterByNameWithLabel(
                    parameter.Name, parameter.Label) == RESULT.OK;
            }

            return instance.setParameterByName(parameter.Name, parameter.Value) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool SetGlobalParameter(AudioParameter parameter)
        {
            if (_disposed || string.IsNullOrWhiteSpace(parameter.Name))
                return false;

            // 不在这里检查 RuntimeManager 的初始化标记：StudioSystem 会经 RuntimeManager 懒初始化，
            // 而「先设全局参数再起播」是常见顺序——早退会让第一次调用静默失败。真正的初始化失败由
            // 下面的 SystemNotInitializedException 兜底（R5-FMD-001）。
            try
            {
                if (parameter.IsLabel)
                {
                    if (string.IsNullOrEmpty(parameter.Label))
                        return false;

                    return RuntimeManager.StudioSystem.setParameterByNameWithLabel(
                        parameter.Name, parameter.Label) == RESULT.OK;
                }

                return RuntimeManager.StudioSystem.setParameterByName(
                    parameter.Name, parameter.Value) == RESULT.OK;
            }
            catch (SystemNotInitializedException)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool LoadBank(string bank, bool loadSamples)
        {
            if (_disposed || string.IsNullOrWhiteSpace(bank))
                return false;

            if (IsBankLoaded(bank))
                return true;

            try
            {
                RuntimeManager.LoadBank(bank, loadSamples);
            }
            catch (BankLoadException)
            {
                return false;
            }
            catch (SystemNotInitializedException)
            {
                return false;
            }

            if (_ownsBanks && !_ownedBanks.Contains(bank))
                _ownedBanks.Add(bank);

            return true;
        }

        /// <inheritdoc/>
        public bool UnloadBank(string bank)
        {
            if (_disposed || string.IsNullOrWhiteSpace(bank))
                return false;

            try
            {
                RuntimeManager.UnloadBank(bank);
                bool unloaded = !RuntimeManager.HasBankLoaded(bank);
                if (unloaded)
                    _ownedBanks.Remove(bank);

                return unloaded;
            }
            catch (SystemNotInitializedException)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool IsBankLoaded(string bank)
        {
            if (_disposed || string.IsNullOrWhiteSpace(bank))
                return false;

            try
            {
                return RuntimeManager.HasBankLoaded(bank);
            }
            catch (SystemNotInitializedException)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool SetBusVolume(string bus, float volume)
        {
            if (!TryGetBus(bus, out Bus busInstance))
                return false;

            return busInstance.setVolume(volume) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool SetBusMuted(string bus, bool muted)
        {
            if (!TryGetBus(bus, out Bus busInstance))
                return false;

            return busInstance.setMute(muted) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool SetBusPaused(string bus, bool paused)
        {
            if (!TryGetBus(bus, out Bus busInstance))
                return false;

            return busInstance.setPaused(paused) == RESULT.OK;
        }

        /// <inheritdoc/>
        public bool StartSnapshot(string snapshot)
        {
            if (_disposed || string.IsNullOrWhiteSpace(snapshot))
                return false;

            if (_snapshots.ContainsKey(snapshot))
                return true;

            EventInstance instance;
            try
            {
                instance = RuntimeManager.CreateInstance(snapshot);
            }
            catch (EventNotFoundException)
            {
                return false;
            }
            catch (SystemNotInitializedException)
            {
                return false;
            }

            if (!instance.isValid())
            {
                SafeStopAndRelease(instance, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                return false;
            }

            if (instance.start() != RESULT.OK)
            {
                SafeStopAndRelease(instance, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                return false;
            }

            _snapshots.Add(snapshot, instance);
            return true;
        }

        /// <inheritdoc/>
        public bool StopSnapshot(string snapshot)
        {
            if (_disposed || string.IsNullOrWhiteSpace(snapshot))
                return false;

            if (!_snapshots.TryGetValue(snapshot, out EventInstance instance))
                return false;

            _snapshots.Remove(snapshot);
            SafeStopAndRelease(instance, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            return true;
        }

        /// <inheritdoc/>
        public AudioProviderPlaybackState Poll(int instanceId)
        {
            if (!TryGet(instanceId, out EventInstance instance) || !instance.isValid())
                return AudioProviderPlaybackState.Invalid;

            PLAYBACK_STATE state = PLAYBACK_STATE.STOPPED;
            bool paused = false;

            try
            {
                if (instance.getPlaybackState(out state) != RESULT.OK)
                    return AudioProviderPlaybackState.Invalid;

                if (instance.getPaused(out paused) != RESULT.OK)
                    paused = false;
            }
            catch (Exception)
            {
                return AudioProviderPlaybackState.Invalid;
            }

            return MapState(paused, state);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (KeyValuePair<int, EventInstance> entry in _instances)
                SafeStopAndRelease(entry.Value, FMOD.Studio.STOP_MODE.IMMEDIATE);
            _instances.Clear();

            foreach (KeyValuePair<string, EventInstance> entry in _snapshots)
                SafeStopAndRelease(entry.Value, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _snapshots.Clear();

            if (_ownsBanks)
            {
                for (int i = _ownedBanks.Count - 1; i >= 0; i--)
                {
                    string bank = _ownedBanks[i];
                    try
                    {
                        if (RuntimeManager.HasBankLoaded(bank))
                            RuntimeManager.UnloadBank(bank);
                    }
                    catch (Exception)
                    {
                        // Dispose 不抛；单个 bank 卸载失败不阻断其余清理。
                    }
                }
            }

            _ownedBanks.Clear();
        }

        #endregion

        #region Internal

        /// <summary>
        /// 把 FMOD 的播放状态映射为核心 provider 状态；paused 优先于 playback state。
        /// </summary>
        internal static AudioProviderPlaybackState MapState(bool paused, PLAYBACK_STATE state)
        {
            if (paused && state != PLAYBACK_STATE.STOPPED)
                return AudioProviderPlaybackState.Paused;

            switch (state)
            {
                case PLAYBACK_STATE.STARTING:
                    return AudioProviderPlaybackState.Starting;
                case PLAYBACK_STATE.PLAYING:
                case PLAYBACK_STATE.SUSTAINING:
                case PLAYBACK_STATE.STOPPING:
                    return AudioProviderPlaybackState.Playing;
                case PLAYBACK_STATE.STOPPED:
                    return AudioProviderPlaybackState.Stopped;
                default:
                    return AudioProviderPlaybackState.Invalid;
            }
        }

        #endregion

        #region Private

        private int AllocateId()
        {
            while (true)
            {
                int id = _nextId++;
                if (_nextId == 0)
                    _nextId = 1;

                if (id != 0 && !_instances.ContainsKey(id))
                    return id;
            }
        }

        private bool TryGet(int instanceId, out EventInstance instance)
        {
            instance = default;
            return !_disposed && _instances.TryGetValue(instanceId, out instance);
        }

        private bool TryGetBus(string busPath, out Bus bus)
        {
            bus = default;

            if (_disposed || string.IsNullOrWhiteSpace(busPath))
                return false;

            try
            {
                bus = RuntimeManager.GetBus(busPath);
                return bus.isValid();
            }
            catch (BusNotFoundException)
            {
                return false;
            }
            catch (SystemNotInitializedException)
            {
                return false;
            }
        }

        private static void SafeStopAndRelease(EventInstance instance,
            FMOD.Studio.STOP_MODE stopMode)
        {
            try
            {
                if (!instance.isValid())
                    return;
            }
            catch (Exception)
            {
                // isValid 本身失败也不阻断 Dispose 的其余清理。
                return;
            }

            try
            {
                instance.stop(stopMode);
            }
            catch (Exception)
            {
                // 停止失败不阻断释放。
            }

            try
            {
                instance.release();
            }
            catch (Exception)
            {
                // 释放失败不抛；句柄随对象失效。
            }
        }

        #endregion
    }
}
