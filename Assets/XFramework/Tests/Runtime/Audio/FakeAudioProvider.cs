using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// <see cref="IAudioProvider"/> 的纯 C# 替身：不碰 Unity 音频，记录核心发给它的每一个请求。
    /// <para><see cref="DelayNext"/> + <see cref="CompletePendingPlay"/> 可构造「provider 仍在加载」的窗口；
    /// <see cref="MaxConcurrent"/> 可模拟容量小于核心 <c>MaxVoices</c> 的自定义 provider。</para>
    /// <para><b>每个用例结束前必须放行挂起的加载</b>：否则 <c>PlayAsync</c> 的续体永远停在
    /// <see cref="UniTaskCompletionSource"/> 上，后续用例会看到上一轮的残留。</para>
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
        private UniTaskCompletionSource _pendingPlay;

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

        /// <summary>下一次 <see cref="PlayAsync"/> 挂起，直到 <see cref="CompletePendingPlay"/>。</summary>
        internal bool DelayNext;

        /// <summary>为 true 时忽略调用方令牌（模拟不守契约的 provider，验证核心返回后的补检查）。</summary>
        internal bool IgnoreCancellation;

        /// <summary>非 <c>0</c> 时限制同时存活的实例数（模拟容量小于核心 <c>MaxVoices</c>）。</summary>
        internal int MaxConcurrent;

        internal bool ThrowOnPlay;
        internal bool ThrowOnPoll;
        internal bool ThrowOnSetPaused;
        internal bool ThrowOnSetVolume;
        internal bool ThrowOnStop;
        internal bool ThrowOnRelease;
        internal bool ThrowOnDispose;

        /// <summary>为 true 时在 validity 令牌上注册一个会抛异常的取消回调。</summary>
        internal bool ThrowOnValidityCancel;

        /// <summary>为 true 时 <see cref="SetPaused"/> 返回 <c>false</c>（模拟句柄已失效）。</summary>
        internal bool RejectSetPaused;

        /// <summary>为 true 时 <see cref="SetVolume"/> 返回 <c>false</c>（模拟句柄已失效）。</summary>
        internal bool RejectSetVolume;

        /// <summary>非 <c>null</c> 时 <see cref="Poll"/> 恒返回该值（用于未知状态兜底）。</summary>
        internal AudioProviderPlaybackState? PollOverride;

        internal AudioPlaybackRequest LastRequest;
        internal string LastLocation;

        /// <summary>最近一次成功返回的句柄；失败时为 default。</summary>
        internal AudioProviderHandle LastSuccessHandle;

        /// <summary>仍有挂起的加载窗口。</summary>
        internal bool HasPendingPlay => _pendingPlay != null;

        /// <summary>尚未释放的实例数。</summary>
        internal int LiveSlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].Released)
                        count++;
                }

                return count;
            }
        }

        /// <summary>放行挂起的加载窗口（若有）。</summary>
        internal void CompletePendingPlay()
        {
            var pending = _pendingPlay;
            _pendingPlay = null;
            pending?.TrySetResult();
        }

        internal Slot Find(AudioProviderHandle handle)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Handle == handle && !slot.Released)
                    return slot;
            }

            return null;
        }

        internal void End(AudioProviderHandle handle)
        {
            var slot = Find(handle);
            if (slot != null)
                slot.State = AudioProviderPlaybackState.Stopped;
        }

        #endregion

        #region IAudioProvider

        public async UniTask<AudioProviderHandle> PlayAsync(
            string location,
            AudioPlaybackRequest request,
            CancellationToken cancellationToken = default)
        {
            Plays.Add((location, request));
            LastLocation = location;
            LastRequest = request;

            if (ThrowOnPlay)
                throw new InvalidOperationException("fake provider play boom");

            CancellationTokenRegistration validityRegistration = default;

            if (ThrowOnValidityCancel)
            {
                validityRegistration = request.ValidityToken.Register(
                    () => throw new InvalidOperationException("fake provider validity cancel boom"));
            }

            try
            {
                if (DelayNext)
                {
                    DelayNext = false;
                    var pending = new UniTaskCompletionSource();
                    _pendingPlay = pending;
                    await pending.Task;
                }

                if (!IgnoreCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // validity 取消不是 OCE：核心槽位已作废，直接返回 default
                    if (request.ValidityToken.IsCancellationRequested)
                        return default;
                }

                if (FailNext)
                {
                    FailNext = false;
                    return default;
                }

                if (MaxConcurrent > 0 && LiveSlotCount >= MaxConcurrent)
                    return default;

                var handle = new AudioProviderHandle(_nextId++, 1);
                _slots.Add(new Slot
                {
                    Handle = handle,
                    State = AudioProviderPlaybackState.Starting,
                    Volume = request.Volume,
                    Paused = request.Paused,
                });

                LastSuccessHandle = handle;
                return handle;
            }
            finally
            {
                validityRegistration.Dispose();
            }
        }

        public bool Stop(AudioProviderHandle handle)
        {
            if (ThrowOnStop)
                throw new InvalidOperationException("fake provider stop boom");

            var slot = Find(handle);
            if (slot == null)
                return false;

            StopCalls++;
            slot.State = AudioProviderPlaybackState.Stopped;
            return true;
        }

        public void Release(AudioProviderHandle handle)
        {
            if (ThrowOnRelease)
                throw new InvalidOperationException("fake provider release boom");

            var slot = Find(handle);
            if (slot == null)
                return;

            slot.Released = true;
            ReleaseCalls++;
        }

        public bool SetPaused(AudioProviderHandle handle, bool paused)
        {
            if (ThrowOnSetPaused)
                throw new InvalidOperationException("fake provider pause boom");

            if (RejectSetPaused)
                return false;

            var slot = Find(handle);
            if (slot == null)
                return false;

            SetPausedCalls++;
            slot.Paused = paused;
            return true;
        }

        public bool SetVolume(AudioProviderHandle handle, float volume)
        {
            if (ThrowOnSetVolume)
                throw new InvalidOperationException("fake provider volume boom");

            if (RejectSetVolume)
                return false;

            var slot = Find(handle);
            if (slot == null)
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

            if (PollOverride.HasValue)
                return PollOverride.Value;

            var slot = Find(handle);
            if (slot == null)
                return AudioProviderPlaybackState.Invalid;

            return slot.State;
        }

        /// <summary>
        /// 契约要求 <c>Dispose</c> 释放全部实例与租约：未释放的槽位在这里结算，
        /// 避免 seam 测试把「Dispose 没起作用」误当成已经释放。
        /// </summary>
        public void Dispose()
        {
            if (ThrowOnDispose)
                throw new InvalidOperationException("fake provider dispose boom");

            Disposed = true;

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Released)
                    continue;

                _slots[i].Released = true;
                ReleaseCalls++;
            }
        }

        #endregion
    }
}
