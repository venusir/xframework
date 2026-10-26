using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// <see cref="IFmodEventBackend"/> 的纯 C# 替身：记录调用顺序与参数，可注入失败/异常。
    /// </summary>
    internal sealed class FakeFmodEventBackend : IFmodEventBackend
    {
        #region Records

        internal readonly List<string> Calls = new List<string>();
        internal readonly List<AudioParameter> Parameters = new List<AudioParameter>();
        internal readonly List<AudioParameter> GlobalParameters = new List<AudioParameter>();
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
        internal readonly List<int> ReleasedIds = new List<int>();
        internal readonly HashSet<int> LiveIds = new HashSet<int>();

        internal int CreateCalls;
        internal int StartCalls;
        internal int StopCalls;
        internal int SetPausedCalls;
        internal int SetVolumeCalls;
        internal int SetPitchCalls;
        internal int Set3DAttributesCalls;
        internal int SetParameterCalls;
        internal int SetGlobalParameterCalls;
        internal int PollCalls;
        internal int DisposeCalls;

        internal string LastEventPath;
        internal int LastCreatedId;
        internal float LastVolume;
        internal float LastPitch;
        internal Vector3 LastPosition;
        internal bool Disposed;

        #endregion

        #region Failure Injection

        internal bool FailCreate;
        internal bool FailStart;
        internal bool FailStop;
        internal bool FailSetPaused;
        internal bool FailSetVolume;
        internal bool FailSetPitch;
        internal bool FailSet3D;
        internal bool FailSetParameter;
        internal bool FailGlobalParameter;
        internal bool FailBankLoad;
        internal bool FailBankUnload;
        internal bool FailBankQuery;
        internal bool FailBusVolume;
        internal bool FailBusMuted;
        internal bool FailBusPaused;
        internal bool FailSnapshotStart;
        internal bool FailSnapshotStop;

        internal bool ThrowOnCreate;
        internal bool ThrowOnStart;
        internal bool ThrowOnSetParameter;
        internal bool ThrowOnRelease;
        internal bool ThrowOnDispose;

        internal AudioProviderPlaybackState PollState = AudioProviderPlaybackState.Playing;
        internal bool PollUnknown;

        private int _nextId = 1;

        #endregion

        #region IFmodEventBackend

        public int Create(string eventPath)
        {
            CreateCalls++;
            LastEventPath = eventPath;
            Calls.Add("Create:" + eventPath);

            if (ThrowOnCreate)
                throw new InvalidOperationException("fake create boom");
            if (FailCreate)
                return 0;

            int id = _nextId++;
            LiveIds.Add(id);
            LastCreatedId = id;
            return id;
        }

        public bool Start(int instanceId)
        {
            StartCalls++;
            Calls.Add("Start:" + instanceId);

            if (ThrowOnStart)
                throw new InvalidOperationException("fake start boom");

            return !FailStart && LiveIds.Contains(instanceId);
        }

        public bool Stop(int instanceId)
        {
            StopCalls++;
            Calls.Add("Stop:" + instanceId);
            return !FailStop && LiveIds.Contains(instanceId);
        }

        public void Release(int instanceId)
        {
            Calls.Add("Release:" + instanceId);
            ReleasedIds.Add(instanceId);
            LiveIds.Remove(instanceId);

            if (ThrowOnRelease)
                throw new InvalidOperationException("fake release boom");
        }

        public bool SetPaused(int instanceId, bool paused)
        {
            SetPausedCalls++;
            Calls.Add("SetPaused:" + instanceId + ":" + paused);
            return !FailSetPaused && LiveIds.Contains(instanceId);
        }

        public bool SetVolume(int instanceId, float volume)
        {
            SetVolumeCalls++;
            LastVolume = volume;
            Calls.Add("SetVolume:" + instanceId + ":" + volume);
            return !FailSetVolume && LiveIds.Contains(instanceId);
        }

        public bool SetPitch(int instanceId, float pitch)
        {
            SetPitchCalls++;
            LastPitch = pitch;
            Calls.Add("SetPitch:" + instanceId + ":" + pitch);
            return !FailSetPitch && LiveIds.Contains(instanceId);
        }

        public bool Set3DAttributes(int instanceId, Vector3 position)
        {
            Set3DAttributesCalls++;
            LastPosition = position;
            Calls.Add("Set3D:" + instanceId + ":" + position);
            return !FailSet3D && LiveIds.Contains(instanceId);
        }

        public bool SetParameter(int instanceId, AudioParameter parameter)
        {
            SetParameterCalls++;
            Calls.Add("SetParameter:" + instanceId + ":" + parameter.Name);
            Parameters.Add(parameter);

            if (ThrowOnSetParameter)
                throw new InvalidOperationException("fake parameter boom");

            return !FailSetParameter && LiveIds.Contains(instanceId);
        }

        public bool SetGlobalParameter(AudioParameter parameter)
        {
            SetGlobalParameterCalls++;
            Calls.Add("SetGlobalParameter:" + parameter.Name);
            GlobalParameters.Add(parameter);
            return !FailGlobalParameter;
        }

        public bool LoadBank(string bank, bool loadSamples)
        {
            Calls.Add("LoadBank:" + bank + ":" + loadSamples);
            if (FailBankLoad)
                return false;

            BankLoads.Add((bank, loadSamples));
            LoadedBanks.Add(bank);
            return true;
        }

        public bool UnloadBank(string bank)
        {
            Calls.Add("UnloadBank:" + bank);
            if (FailBankUnload)
                return false;

            BankUnloads.Add(bank);
            LoadedBanks.Remove(bank);
            return true;
        }

        public bool IsBankLoaded(string bank)
        {
            Calls.Add("IsBankLoaded:" + bank);
            return !FailBankQuery && LoadedBanks.Contains(bank);
        }

        public bool SetBusVolume(string bus, float volume)
        {
            Calls.Add("SetBusVolume:" + bus + ":" + volume);
            if (FailBusVolume)
                return false;

            BusVolumes.Add((bus, volume));
            return true;
        }

        public bool SetBusMuted(string bus, bool muted)
        {
            Calls.Add("SetBusMuted:" + bus + ":" + muted);
            if (FailBusMuted)
                return false;

            BusMutes.Add((bus, muted));
            return true;
        }

        public bool SetBusPaused(string bus, bool paused)
        {
            Calls.Add("SetBusPaused:" + bus + ":" + paused);
            if (FailBusPaused)
                return false;

            BusPauses.Add((bus, paused));
            return true;
        }

        public bool StartSnapshot(string snapshot)
        {
            Calls.Add("StartSnapshot:" + snapshot);
            if (FailSnapshotStart)
                return false;

            StartedSnapshots.Add(snapshot);
            return true;
        }

        public bool StopSnapshot(string snapshot)
        {
            Calls.Add("StopSnapshot:" + snapshot);
            if (FailSnapshotStop)
                return false;

            StoppedSnapshots.Add(snapshot);
            return true;
        }

        public AudioProviderPlaybackState Poll(int instanceId)
        {
            PollCalls++;
            if (PollUnknown)
                return (AudioProviderPlaybackState)99;

            return LiveIds.Contains(instanceId)
                ? PollState
                : AudioProviderPlaybackState.Invalid;
        }

        public void Dispose()
        {
            DisposeCalls++;
            Disposed = true;
            LiveIds.Clear();

            if (ThrowOnDispose)
                throw new InvalidOperationException("fake dispose boom");
        }

        #endregion
    }
}
