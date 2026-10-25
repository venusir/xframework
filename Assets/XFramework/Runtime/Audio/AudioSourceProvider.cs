using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XAudio
{
    /// <summary>
    /// 内置播放引擎：宿主 / 播放源池 / 加载 / 暂停 / 回收判据。
    /// <para>核心语义层（<see cref="AudioManagerImpl"/>）保留通道、三档音量、暂停闸门、计数与诊断；
    /// 本类只做播放原语。它是 internal 的实现细节，不是公开替换点。</para>
    /// </summary>
    internal sealed class AudioSourceProvider : IAudioProvider
    {
        #region Private Fields

        private readonly AudioSourcePool _pool;
        private readonly IAudioClipLoader _clipLoader;

        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>生产路径：使用默认 <see cref="AudioClipLoader"/>（经 XAsset）。</summary>
        internal AudioSourceProvider(AudioInitOptions options)
            : this(options, null)
        {
        }

        /// <summary>测试路径：注入假的加载缝。</summary>
        internal AudioSourceProvider(AudioInitOptions options, IAudioClipLoader clipLoader)
        {
            var init = options ?? new AudioInitOptions();
            _pool = new AudioSourcePool(init.MaxVoices);
            _clipLoader = clipLoader ?? new AudioClipLoader();

            // 预热是显式选择：不开时连宿主都不建（「从不播放的项目一个 GameObject 都不该建」有用例钉着）
            if (init.PrewarmVoices)
                _pool.Prewarm();
        }

        #endregion

        #region IAudioProvider

        /// <inheritdoc/>
        public async UniTask<AudioProviderHandle> PlayAsync(
            string location,
            AudioPlaybackRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return default;

            if (!_pool.TryReserve(location, out int index, out AudioProviderHandle handle))
                return default;

            AudioClipLease lease = default;

            // 核心槽位被 Stop/StopAll 作废时立即归还池槽：等到加载完成再归还的话，
            // 紧接其后的重播会撞上「池满」窗口，拿到必然失效的预占句柄（P1-1）。
            CancellationTokenRegistration validityRegistration = default;

            try
            {
                if (request.ValidityToken.CanBeCanceled)
                {
                    int reservedIndex = index;
                    int reservedGeneration = handle.Generation;
                    validityRegistration = request.ValidityToken.Register(
                        () => _pool.Invalidate(reservedIndex, reservedGeneration));
                }

                // 令牌在注册前就已取消：回调已同步执行并作废槽位，不必再发起加载
                if (request.ValidityToken.IsCancellationRequested)
                {
                    ReleaseReservation(index, handle.Generation, lease, location);
                    return default;
                }

                try
                {
                    lease = await _clipLoader.LoadAsync(location, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 调用方/生命周期取消：释放预占槽位，不吞取消
                    ReleaseReservation(index, handle.Generation, lease, location);
                    throw;
                }
                catch (Exception e)
                {
                    // 加载缝的契约是「失败返回 default 而不抛」，真抛了说明是未预期异常
                    LogManager.Exception(LogCategories.Audio, e,
                        string.Format("加载音频资源 '{0}' 时发生未预期异常", location));
                    ReleaseReservation(index, handle.Generation, lease, location);
                    return default;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    ReleaseReservation(index, handle.Generation, lease, location);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // 核心槽位在加载期间被 Stop/StopAll 作废：归还租约，不 start，也不抛 OCE。
                // 代际检查同时挡住「作废后池槽已被新播放复用」的晚到续体——那时绝不碰池槽（P1-2）。
                if (request.ValidityToken.IsCancellationRequested ||
                    !_pool.IsCurrent(index, handle.Generation))
                {
                    ReleaseReservation(index, handle.Generation, lease, location);
                    return default;
                }

                if (!lease.IsValid)
                {
                    ReleaseReservation(index, handle.Generation, lease, location);
                    LogManager.Warning(LogCategories.Audio,
                        "播放失败，clip 加载未成功：'{0}'（底层错误见上一条 YooAsset 日志）。", location);
                    return default;
                }

                var voice = _pool.GetSlot(index);

                // 播放源可能被外部销毁（Unity 假 null）；继续下去会在 ConfigureSource 处抛 MissingReference，
                // 留下永久 Loading 且持有租约的槽位（AUD-003）。
                if (voice.Source == null)
                {
                    ReleaseReservation(index, handle.Generation, lease, location);
                    LogManager.Warning(LogCategories.Audio,
                        "播放源已被外部销毁，无法起播：'{0}'。", location);
                    return default;
                }

                try
                {
                    voice.Loop = request.Loop;
                    ConfigureSource(voice.Source, lease.Clip, request);
                    voice.Source.volume = request.Volume;
                    voice.Source.Play();

                    // 管理器处于暂停态时，新起的播放也立即进入暂停——Pause 是管理器级闸门。
                    if (request.Paused)
                    {
                        voice.Source.Pause();
                        voice.State = AudioVoiceState.Paused;
                    }
                    else if (voice.Source.isPlaying)
                    {
                        // P-06 实测：Play() 之后同步读一次 isPlaying。短于一个扫尾节拍的 one-shot
                        // 在下一次 Poll 时已经播完——只看下一拍的 isPlaying 会把它误判为「从未起播」，
                        // 卡在 Starting 到 60 拍超时（AUD-001）。
                        voice.State = AudioVoiceState.Playing;
                    }
                    else
                    {
                        voice.State = AudioVoiceState.Starting;
                    }

                    // 租约只在起播全部成功后交给槽位：此前任何失败/取消都由本地释放，
                    // 保证「释放点恰好一次」。
                    voice.ClipLease = lease;
                }
                catch (Exception e)
                {
                    // 起播段异常（被销毁的播放源、异常插件等）：不清理会留下永久 Loading、
                    // 永远持有租约的槽位。
                    LogManager.Exception(LogCategories.Audio, e,
                        string.Format("播放 '{0}' 时发生未预期异常", location));
                    ReleaseReservation(index, handle.Generation, lease, location);
                    return default;
                }

                return handle;
            }
            finally
            {
                validityRegistration.Dispose();
            }
        }

        /// <inheritdoc/>
        public bool Stop(AudioProviderHandle handle)
        {
            if (_disposed || !IsCurrent(handle))
                return false;

            var voice = _pool.GetSlot(handle.Id);
            if (voice.Source == null)
                return false;

            voice.Source.Stop();
            return true;
        }

        /// <inheritdoc/>
        public void Release(AudioProviderHandle handle)
        {
            if (_disposed || !IsCurrent(handle))
                return;

            _pool.Release(handle.Id);
        }

        /// <inheritdoc/>
        public bool SetPaused(AudioProviderHandle handle, bool paused)
        {
            if (_disposed || !IsCurrent(handle))
                return false;

            var voice = _pool.GetSlot(handle.Id);
            if (voice.Source == null)
                return false;

            if (paused)
            {
                voice.Source.Pause();
                voice.State = AudioVoiceState.Paused;
                return true;
            }

            if (voice.State == AudioVoiceState.Paused)
            {
                voice.Source.UnPause();
                // 回到 Starting 而不是直接 Playing：由核心 ticker 下一拍确认 isPlaying 真的回来了，
                // 与起播路径同形。若某个平台上 UnPause 不生效，超时兜底仍然管用。
                voice.State = AudioVoiceState.Starting;
            }

            return true;
        }

        /// <inheritdoc/>
        public bool SetVolume(AudioProviderHandle handle, float volume)
        {
            if (_disposed || !IsCurrent(handle))
                return false;

            var voice = _pool.GetSlot(handle.Id);
            if (voice.Source == null)
                return false;

            voice.Source.volume = volume;
            return true;
        }

        /// <inheritdoc/>
        public AudioProviderPlaybackState Poll(AudioProviderHandle handle)
        {
            if (_disposed || !IsCurrent(handle))
                return AudioProviderPlaybackState.Invalid;

            var voice = _pool.GetSlot(handle.Id);

            // 被外部销毁的播放源是 Unity 假 null；继续读 isPlaying 会抛 MissingReference，
            // 让整轮扫尾中断并留下永久占用的槽位（AUD-003）。
            if (voice.Source == null)
            {
                LogManager.Warning(LogCategories.Audio,
                    "播放源已被外部销毁，回收槽位：'{0}'。", voice.Location);
                return AudioProviderPlaybackState.Invalid;
            }

            // 全局暂停期间 isPlaying 为 false 是暂停而非结束（AUD-013）。
            if (AudioListener.pause)
                return AudioProviderPlaybackState.Paused;

            switch (voice.State)
            {
                case AudioVoiceState.Starting:
                    if (voice.Source.isPlaying)
                    {
                        voice.State = AudioVoiceState.Playing;
                        return AudioProviderPlaybackState.Playing;
                    }

                    return AudioProviderPlaybackState.Starting;

                case AudioVoiceState.Playing:
                    // 循环的播放永不被自动回收——必须显式 Stop。
                    if (voice.Loop || voice.Source.isPlaying)
                        return AudioProviderPlaybackState.Playing;

                    return AudioProviderPlaybackState.Stopped;

                case AudioVoiceState.Paused:
                    return AudioProviderPlaybackState.Paused;

                default:
                    return AudioProviderPlaybackState.Invalid;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _pool.Dispose();
        }

        #endregion

        #region Internal

        /// <summary>测试用：取 provider 句柄对应的播放源；找不到返回 <c>null</c>。</summary>
        internal AudioSource GetSource(AudioProviderHandle handle)
        {
            if (_disposed || !IsCurrent(handle))
                return null;

            return _pool.GetSlot(handle.Id).Source;
        }

        #endregion

        #region Private

        private bool IsCurrent(AudioProviderHandle handle)
        {
            if (handle.Generation == 0 || handle.Id < 0 || handle.Id >= _pool.SlotCount)
                return false;

            return _pool.IsCurrent(handle.Id, handle.Generation);
        }

        /// <summary>把规范化后的请求写进播放源。音量不在这里——由 <see cref="SetVolume"/> 单独写。</summary>
        private static void ConfigureSource(AudioSource source, AudioClip clip, AudioPlaybackRequest request)
        {
            source.clip = clip;
            source.loop = request.Loop;
            source.pitch = request.Pitch;
            source.spatialBlend = request.SpatialBlend;

            if (request.SpatialBlend > 0f)
                source.transform.position = request.Position;
        }

        /// <summary>
        /// 归还一次未成功起播的预占：释放本地租约，并在池槽仍属于本次预占时归还它。
        /// <para><b>先查代际再碰池槽</b>：失效令牌的回调会立即 <see cref="AudioSourcePool.Invalidate"/>
        /// 归还池槽，新播放可能已经复用同一下标；晚到的续体若无条件 <c>Release</c> 会把新播放杀掉（P1-2）。
        /// 此时只归还自己手里的租约——它的释放点仍在本次调用里，恰好一次。</para>
        /// </summary>
        private void ReleaseReservation(int index, int generation, AudioClipLease lease, string location)
        {
            SafeDisposeLease(lease, location);

            if (_pool.IsCurrent(index, generation))
                _pool.Release(index);
        }

        /// <summary>
        /// 释放一个由加载路径拿到、但没能交到槽位手上的资源租约。
        /// <para>用 try/catch 包住：<c>Dispose</c> 可能由 <c>Application.quitting</c> 触发，而处理器顺序
        /// 不保证 <c>XAsset</c> 的包还没销毁——那时释放会抛。退出路径上不该再炸一次。</para>
        /// </summary>
        private static void SafeDisposeLease(AudioClipLease lease, string location)
        {
            // 不能按 IsValid 早退（AUD-004）：加载成功但 location 指向非 AudioClip 时，租约没有资源本体，
            // 却仍持有底层 YooAsset 句柄的释放动作——跳过它就是引用计数无界增长。
            try
            {
                lease.Dispose();
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e,
                    string.Format("释放音频资源租约时发生异常（'{0}'）", location));
            }
        }

        #endregion
    }
}
