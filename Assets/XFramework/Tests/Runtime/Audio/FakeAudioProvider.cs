using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// <see cref="IAudioProvider"/> 的纯 C# 替身：不碰 Unity 音频，记录核心发给它的每一个请求。
    /// </summary>
    internal sealed class FakeAudioProvider : IAudioProvider
    {
        #region Nested

        internal sealed class Slot
        {
            internal AudioProviderHandle Handle;
            internal AudioProviderPlaybackState State;
            internal float Volume;
            internal bool Paused;
            internal bool Released;
        }

        #endregion

        #region Private Fields

        private readonly List<Slot> _slots = new List<Slot>();

        private int _nextId;

        #endregion

        #region Internal

        internal readonly List<(string Location, AudioPlaybackRequest Request)> Plays =
            new List<(string, AudioPlaybackRequest)>();

        internal int StopCalls;
        internal int ReleaseCalls;
        internal int SetPausedCalls;
        internal int SetVolumeCalls;
        internal int PollCalls;

        internal bool Disposed;
        internal bool FailNext;

        internal bool ThrowOnPlay;
        internal bool ThrowOnPoll;
        internal bool ThrowOnSetPaused;
        internal bool ThrowOnSetVolume;

        internal AudioPlaybackRequest LastRequest;
        internal string LastLocation;

        internal Slot Find(AudioProviderHandle handle)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Handle == handle)
                    return _slots[i];
            }

            return null;
        }

        internal void End(AudioProviderHandle handle)
        {
            var slot = Find(handle);
            if (slot != null && !slot.Released)
                slot.State = AudioProviderPlaybackState.Stopped;
        }

        #endregion

        #region IAudioProvider

        public UniTask<AudioProviderHandle> PlayAsync(
            string location,
            AudioPlaybackRequest request,
            CancellationToken cancellationToken = default)
        {
            Plays.Add((location, request));
            LastLocation = location;
            LastRequest = request;

            if (ThrowOnPlay)
                throw new InvalidOperationException("fake provider play boom");

            if (FailNext)
            {
                FailNext = false;
                return UniTask.FromResult(default(AudioProviderHandle));
            }

            var handle = new AudioProviderHandle(_nextId++, 1);
            _slots.Add(new Slot
            {
                Handle = handle,
                State = AudioProviderPlaybackState.Starting,
                Volume = request.Volume,
                Paused = request.Paused,
            });

            return UniTask.FromResult(handle);
        }

        public bool Stop(AudioProviderHandle handle)
        {
            var slot = Find(handle);
            if (slot == null || slot.Released)
                return false;

            StopCalls++;
            slot.State = AudioProviderPlaybackState.Stopped;
            return true;
        }

        public void Release(AudioProviderHandle handle)
        {
            var slot = Find(handle);
            if (slot == null || slot.Released)
                return;

            slot.Released = true;
            ReleaseCalls++;
        }

        public bool SetPaused(AudioProviderHandle handle, bool paused)
        {
            if (ThrowOnSetPaused)
                throw new InvalidOperationException("fake provider pause boom");

            var slot = Find(handle);
            if (slot == null || slot.Released)
                return false;

            SetPausedCalls++;
            slot.Paused = paused;
            return true;
        }

        public bool SetVolume(AudioProviderHandle handle, float volume)
        {
            if (ThrowOnSetVolume)
                throw new InvalidOperationException("fake provider volume boom");

            var slot = Find(handle);
            if (slot == null || slot.Released)
                return false;

            SetVolumeCalls++;
            slot.Volume = volume;
            return true;
        }

        public AudioProviderPlaybackState Poll(AudioProviderHandle handle)
        {
            PollCalls++;

            if (ThrowOnPoll)
                throw new InvalidOperationException("fake provider poll boom");

            var slot = Find(handle);
            if (slot == null || slot.Released)
                return AudioProviderPlaybackState.Invalid;

            return slot.State;
        }

        public void Dispose() => Disposed = true;

        #endregion
    }
}
