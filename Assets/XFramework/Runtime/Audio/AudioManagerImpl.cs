using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUpdate;

namespace XFramework.XAudio
{
    /// <summary>
    /// 音频语义层：通道、三档音量、暂停闸门、计数、归一化、核心句柄与诊断。
    /// <para><b>实际播放委托给 <see cref="IAudioProvider"/></b>；内置实现是 <see cref="AudioSourceProvider"/>。
    /// 本类不再直接接触 <c>AudioSource</c>/<c>AudioClip</c>。</para>
    /// <para>第三方中间件推荐实现 <see cref="IAudioProvider"/> 并经 <see cref="AudioManager.SetProvider"/>
    /// 注入；整体替换本类（<see cref="IAudioManager"/>）仍是保留的逃生口。</para>
    /// </summary>
    internal sealed class AudioManagerImpl : IAudioManager
    {
        #region Constants

        /// <summary>
        /// 保留通道名。<c>"master"</c> 不是通道——总音量由 <see cref="MasterVolume"/> /
        /// <see cref="MasterMuted"/> 表达。把它当通道用会抛 <see cref="ArgumentException"/>。
        /// </summary>
        internal const string ReservedMasterChannel = "master";

        /// <summary>Starting 状态的兜底拍数（Tier1 × 60 ≈ 2 秒）。</summary>
        internal const int StartingTimeoutTicks = 60;

        #endregion

        #region Private Fields

        /// <summary>同步播放入口的 Profiler 标记（异步路径的实际耗时在加载，不在这里）。</summary>
        private static readonly ProfilerMarker PlayMarker = new("XFramework.Audio.Play");

        private readonly IAudioProvider _provider;
        private readonly AudioManagedVoice[] _voices;
        private readonly int _maxVoices;
        private readonly AudioTicker _ticker;

        /// <summary>
        /// 实例级生命周期令牌。<see cref="Dispose"/> 取消它，从而中断全部在途加载。
        /// <para><b>刻意不 Dispose 这个 CTS</b>：取消之后仍可能有续体在读它的令牌
        /// （<c>ThrowIfCancellationRequested</c>），而 <c>Dispose</c> 后再取 <c>Token</c> 会抛
        /// <c>ObjectDisposedException</c>。一个 CTS 的托管资源代价可以忽略，稳定优先。</para>
        /// </summary>
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        /// <summary>
        /// 通道状态表。惰性创建：只有被<b>写入</b>过的通道才会进表。
        /// <para>用 <see cref="StringComparer.Ordinal"/>：通道名区分大小写，这是公开承诺的一部分。</para>
        /// </summary>
        private readonly Dictionary<string, AudioChannelState> _channels =
            new Dictionary<string, AudioChannelState>(StringComparer.Ordinal);

        private float _masterVolume = 1f;
        private bool _masterMuted;
        private bool _paused;

        private bool _tickerRegistered;
        private bool _disposed;
        private bool _exhaustionWarned;

        #endregion

        #region Constructors

        internal AudioManagerImpl(AudioInitOptions options)
            : this(new AudioSourceProvider(options), options)
        {
        }

        /// <summary>测试用的构造重载：注入假的加载缝以控制加载时序与失败。</summary>
        internal AudioManagerImpl(AudioInitOptions options, IAudioClipLoader clipLoader)
            : this(new AudioSourceProvider(options, clipLoader), options)
        {
        }

        /// <summary>注入自定义播放引擎。</summary>
        internal AudioManagerImpl(IAudioProvider provider, AudioInitOptions options)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Options = options ?? new AudioInitOptions();

            _maxVoices = Options.MaxVoices < 1 ? 1 : Options.MaxVoices;
            _voices = new AudioManagedVoice[_maxVoices];
            for (int i = 0; i < _voices.Length; i++)
                _voices[i] = new AudioManagedVoice { State = AudioManagedVoiceState.Free };

            _ticker = new AudioTicker(this);

            _masterVolume = Mathf.Clamp01(Options.MasterVolume);
            _masterMuted = Options.MasterMuted;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 本次初始化使用的选项。原样保存、<b>未归一化</b>——归一化发生在各自的使用点。
        /// </summary>
        internal AudioInitOptions Options { get; }

        /// <summary>当前播放引擎（测试与诊断用）。</summary>
        internal IAudioProvider Provider => _provider;

        /// <summary>供 <see cref="AudioTicker"/> 每拍调用：回收播完的槽位，全部回收完时注销 ticker。</summary>
        internal void SweepVoices()
        {
            if (_disposed)
                return;

            int active = 0;

            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];

                switch (voice.State)
                {
                    case AudioManagedVoiceState.Free:
                        break;

                    case AudioManagedVoiceState.Loading:
                    case AudioManagedVoiceState.Paused:
                        active++;
                        break;

                    case AudioManagedVoiceState.Starting:
                    case AudioManagedVoiceState.Playing:
                        if (_paused)
                        {
                            active++;
                            break;
                        }

                        AudioProviderPlaybackState polled;
                        try
                        {
                            polled = _provider.Poll(voice.ProviderHandle);
                        }
                        catch (Exception e)
                        {
                            LogManager.Exception(LogCategories.Audio, e,
                                "轮询音频播放状态时发生异常，回收槽位");
                            ReleaseVoice(voice);
                            break;
                        }

                        switch (polled)
                        {
                            case AudioProviderPlaybackState.Invalid:
                            case AudioProviderPlaybackState.Stopped:
                                ReleaseVoice(voice);
                                break;

                            case AudioProviderPlaybackState.Paused:
                                active++;
                                break;

                            case AudioProviderPlaybackState.Starting:
                                if (voice.State == AudioManagedVoiceState.Starting)
                                {
                                    voice.StartingTicks++;
                                    if (voice.StartingTicks > StartingTimeoutTicks)
                                    {
                                        LogManager.Warning(LogCategories.Audio,
                                            "播放源起播超时（'{0}'）：连续 {1} 拍从未观察到 isPlaying 变真，" +
                                            "槽位被强制回收。这通常意味着该平台的 AudioSource 在 Play() 后" +
                                            "不会报告 isPlaying。",
                                            voice.Location, StartingTimeoutTicks);
                                        ReleaseVoice(voice);
                                        break;
                                    }
                                }

                                active++;
                                break;

                            case AudioProviderPlaybackState.Playing:
                                if (voice.State == AudioManagedVoiceState.Starting)
                                {
                                    voice.State = AudioManagedVoiceState.Playing;
                                    voice.StartingTicks = 0;
                                }

                                active++;
                                break;
                        }

                        break;
                }
            }

            if (active == 0)
                UnregisterTicker();
        }

        /// <summary>
        /// 取核心句柄对应的 provider 句柄。探测型：未初始化/已销毁/default/过期/Loading 返回 false。
        /// </summary>
        internal bool TryGetProviderHandle(AudioHandle handle, out AudioProviderHandle providerHandle)
        {
            providerHandle = default;

            if (_disposed || !IsCurrent(handle))
                return false;

            var voice = _voices[handle.Id];
            if (voice.State == AudioManagedVoiceState.Loading ||
                voice.State == AudioManagedVoiceState.Free)
            {
                return false;
            }

            providerHandle = voice.ProviderHandle;
            return !providerHandle.IsDefault;
        }

        /// <summary>
        /// 通道名归一化与保留名校验。
        /// </summary>
        internal static string NormalizeChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel))
                return AudioChannels.Default;

            if (string.Equals(channel, ReservedMasterChannel, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"[Audio] \"{ReservedMasterChannel}\" 是保留名而不是通道名；总音量请用 " +
                    "AudioManager.MasterVolume / AudioManager.MasterMuted。");
            }

            return channel;
        }

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _lifetimeCts.Cancel();
            UnregisterTicker();

            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];
                voice.ValidityCts?.Cancel();
                voice.State = AudioManagedVoiceState.Free;
                voice.ProviderHandle = default;
                voice.Channel = null;
                voice.Location = null;
                voice.VolumeScale = 1f;
                voice.StartingTicks = 0;
            }

            _provider.Dispose();
        }

        #endregion

        #region 播放

        /// <inheritdoc/>
        public AudioHandle Play(string location, AudioPlayOptions options = default)
        {
            using (PlayMarker.Auto())
            {
                EnsureNotDisposed();
                ValidateLocation(location);

                string channel = NormalizeChannel(options.Channel);
                float volumeScale = NormalizeVolumeScale(options.VolumeScale);

                // 预占发生在发起加载之前：池满时一次 IO 都不会浪费
                if (!TryReserveVoice(channel, location, volumeScale, out int index, out AudioHandle handle))
                    return default;

                EnsureTickerRegistered();
                RunFireAndForget(index, handle.Generation, location, options);
                return handle;
            }
        }

        /// <inheritdoc/>
        public UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options = default,
            CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            ValidateLocation(location);

            string channel = NormalizeChannel(options.Channel);
            float volumeScale = NormalizeVolumeScale(options.VolumeScale);

            if (!TryReserveVoice(channel, location, volumeScale, out int index, out AudioHandle handle))
                return UniTask.FromResult(default(AudioHandle));

            EnsureTickerRegistered();
            return PlayCoreAsync(index, handle.Generation, location, options, cancellationToken);
        }

        /// <summary>
        /// 播放的公共内核：构造最终请求 → 委托 provider → 记录 provider 句柄并同步当前暂停/音量。
        /// </summary>
        private async UniTask<AudioHandle> PlayCoreAsync(int index, int generation, string location,
            AudioPlayOptions options, CancellationToken cancellationToken)
        {
            CancellationToken lifetime = _lifetimeCts.Token;
            var voice = _voices[index];
            CancellationToken validity = voice.ValidityCts.Token;

            var request = new AudioPlaybackRequest(
                options.Loop,
                NormalizePitch(options.Pitch),
                Mathf.Clamp01(options.SpatialBlend),
                options.Position,
                ComputeVolumeNoCreate(voice.Channel, voice.VolumeScale),
                _paused,
                validity);

            AudioProviderHandle providerHandle = default;
            CancellationTokenSource linked = null;

            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
                    providerHandle = await _provider.PlayAsync(location, request, linked.Token);
                }
                else
                {
                    providerHandle = await _provider.PlayAsync(location, request, lifetime);
                }
            }
            catch (OperationCanceledException)
            {
                bool callerCancelled = cancellationToken.IsCancellationRequested ||
                                       _lifetimeCts.IsCancellationRequested;

                if (IsCurrent(index, generation))
                    FreeVoice(voice);

                if (callerCancelled)
                    throw;

                // 内部 Stop/StopAll 作废：返回 default，而不是把取消抛给调用方
                return default;
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e,
                    string.Format("播放 '{0}' 时发生未预期异常", location));

                if (IsCurrent(index, generation))
                    FreeVoice(voice);

                return default;
            }
            finally
            {
                linked?.Dispose();
            }

            if (!IsCurrent(index, generation))
            {
                // 期间被 Stop / StopAll / Destroy 作废：provider 已返回的句柄必须收掉
                SafeStopAndRelease(providerHandle);
                return default;
            }

            if (providerHandle.IsDefault)
            {
                FreeVoice(voice);
                return default;
            }

            voice.ProviderHandle = providerHandle;
            voice.State = AudioManagedVoiceState.Starting;
            voice.StartingTicks = 0;

            // 起播后无条件同步当前暂停态与音量：加载窗口内两者都可能变过
            bool pausedApplied;
            try
            {
                pausedApplied = _provider.SetPaused(providerHandle, _paused);
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e, "同步音频暂停态时发生异常");
                ReleaseVoice(voice);
                return default;
            }

            if (!pausedApplied)
            {
                ReleaseVoice(voice);
                return default;
            }

            if (_paused)
                voice.State = AudioManagedVoiceState.Paused;

            float volume = ComputeVoiceVolume(voice);
            bool volumeApplied;
            try
            {
                volumeApplied = _provider.SetVolume(providerHandle, volume);
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e, "同步音频音量时发生异常");
                ReleaseVoice(voice);
                return default;
            }

            if (!volumeApplied)
            {
                ReleaseVoice(voice);
                return default;
            }

            return new AudioHandle(index, generation);
        }

        /// <summary>
        /// fire-and-forget 的异常收口。
        /// <para><b>不能裸 <c>.Forget()</c></b>：异步段的异常会落到 <c>UniTaskScheduler</c>，记成没有
        /// 模块前缀的日志。这里包一层 try/catch，取消静默、其余记 <c>[Audio]</c> 错误。</para>
        /// </summary>
        private void RunFireAndForget(int index, int generation, string location, AudioPlayOptions options)
            => RunFireAndForgetAsync(index, generation, location, options).Forget();

        private async UniTaskVoid RunFireAndForgetAsync(int index, int generation, string location,
            AudioPlayOptions options)
        {
            try
            {
                await PlayCoreAsync(index, generation, location, options, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // 只有 Destroy 会取消内部令牌，此时已无调用方可通知
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e,
                    string.Format("播放 '{0}' 时发生未预期异常", location));
            }
        }

        #endregion

        #region 控制

        /// <inheritdoc/>
        public bool Stop(AudioHandle handle)
        {
            if (!IsCurrent(handle))
                return false;

            StopVoice(handle.Id);
            return true;
        }

        /// <inheritdoc/>
        public int StopAll(string channel = null)
        {
            EnsureNotDisposed();

            string filter = channel == null ? null : NormalizeChannel(channel);
            int stopped = 0;

            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];

                if (voice.State == AudioManagedVoiceState.Free)
                    continue;

                if (filter != null &&
                    !string.Equals(voice.Channel, filter, StringComparison.Ordinal))
                {
                    continue;
                }

                StopVoice(i);
                stopped++;
            }

            return stopped;
        }

        /// <inheritdoc/>
        public void Pause()
        {
            EnsureNotDisposed();

            if (_paused)
                return;

            _paused = true;

            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];

                if (voice.State != AudioManagedVoiceState.Starting &&
                    voice.State != AudioManagedVoiceState.Playing)
                {
                    continue;   // 空闲/加载/已暂停不动：加载中的续体起播后会补暂停
                }

                bool applied;
                try
                {
                    applied = _provider.SetPaused(voice.ProviderHandle, true);
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategories.Audio, e, "暂停音频播放时发生异常");
                    applied = false;
                }

                if (!applied)
                {
                    ReleaseVoice(voice);
                    continue;
                }

                voice.State = AudioManagedVoiceState.Paused;
            }
        }

        /// <inheritdoc/>
        public void Resume()
        {
            EnsureNotDisposed();

            if (!_paused)
                return;

            _paused = false;

            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];

                if (voice.State != AudioManagedVoiceState.Paused)
                    continue;

                bool applied;
                try
                {
                    applied = _provider.SetPaused(voice.ProviderHandle, false);
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategories.Audio, e, "恢复音频播放时发生异常");
                    applied = false;
                }

                if (!applied)
                {
                    ReleaseVoice(voice);
                    continue;
                }

                voice.State = AudioManagedVoiceState.Starting;
                voice.StartingTicks = 0;
            }
        }

        /// <inheritdoc/>
        public bool IsPaused => _paused;

        #endregion

        #region 音量

        /// <inheritdoc/>
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                RefreshVolumes(null);   // 主音量影响所有通道
            }
        }

        /// <inheritdoc/>
        public bool MasterMuted
        {
            get => _masterMuted;
            set
            {
                if (_masterMuted == value)
                    return;

                _masterMuted = value;
                RefreshVolumes(null);
            }
        }

        /// <inheritdoc/>
        public void SetChannelVolume(string channel, float volume)
        {
            string normalized = NormalizeChannel(channel);
            GetOrCreateChannel(normalized).Volume = Mathf.Clamp01(volume);
            RefreshVolumes(normalized);
        }

        /// <inheritdoc/>
        public float GetChannelVolume(string channel)
        {
            string normalized = NormalizeChannel(channel);

            // 查询不建状态：只读的人不该留下副作用
            return _channels.TryGetValue(normalized, out var state) ? state.Volume : 1f;
        }

        /// <inheritdoc/>
        public void SetChannelMuted(string channel, bool muted)
        {
            string normalized = NormalizeChannel(channel);
            var state = GetOrCreateChannel(normalized);   // 写入路径：通道不存在则惰性建状态

            if (state.Muted == muted)
                return;   // 值没变，没有播放源需要重算

            state.Muted = muted;
            RefreshVolumes(normalized);
        }

        /// <inheritdoc/>
        public bool IsChannelMuted(string channel)
        {
            string normalized = NormalizeChannel(channel);

            // 查询不建状态；注意这里读的是**通道自己的**静音，不含主静音
            return _channels.TryGetValue(normalized, out var state) && state.Muted;
        }

        /// <inheritdoc/>
        public void RegisterChannel(string channel, AudioChannelConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config),
                    "[Audio] config 不能为 null。要恢复默认值请传 new AudioChannelConfig()。");

            string normalized = NormalizeChannel(channel);

            // 调用时读取一次并快照：之后再改这个实例不影响已注册的通道
            _channels[normalized] = new AudioChannelState
            {
                Volume = Mathf.Clamp01(config.Volume),
                Muted = config.Muted,
            };

            RefreshVolumes(normalized);
        }

        #endregion

        #region 查询

        /// <inheritdoc/>
        public bool IsPlaying(AudioHandle handle)
        {
            if (!IsCurrent(handle))
                return false;

            var state = _voices[handle.Id].State;

            // 加载中的播放不算「在播」；暂停中的算。
            return state == AudioManagedVoiceState.Starting
                || state == AudioManagedVoiceState.Playing
                || state == AudioManagedVoiceState.Paused;
        }

        /// <inheritdoc/>
        public int ActiveVoiceCount
        {
            get
            {
                int active = 0;
                for (int i = 0; i < _voices.Length; i++)
                {
                    if (_voices[i].State != AudioManagedVoiceState.Free)
                        active++;
                }

                return active;
            }
        }

        /// <summary>把已创建的通道写入缓冲区（先清空），返回条数。诊断回读用。</summary>
        internal int CopyChannels(List<AudioChannelInfo> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            foreach (KeyValuePair<string, AudioChannelState> pair in _channels)
            {
                AudioChannelState state = pair.Value;
                buffer.Add(new AudioChannelInfo(pair.Key, state.Volume, state.Muted,
                    GetActiveVoiceCount(pair.Key)));
            }

            return buffer.Count;
        }

        /// <inheritdoc/>
        public int GetActiveVoiceCount(string channel)
        {
            string filter = NormalizeChannel(channel);
            int count = 0;

            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];

                if (voice.State != AudioManagedVoiceState.Free &&
                    string.Equals(voice.Channel, filter, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        #endregion

        #region Private

        private bool TryReserveVoice(string channel, string location, float volumeScale,
            out int index, out AudioHandle handle)
        {
            index = -1;
            handle = default;

            int free = -1;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].State == AudioManagedVoiceState.Free)
                {
                    free = i;
                    break;
                }
            }

            if (free < 0)
            {
                WarnExhaustedOnce();
                return false;
            }

            var voice = _voices[free];
            voice.Generation = NextGeneration(voice.Generation);
            voice.State = AudioManagedVoiceState.Loading;
            voice.ProviderHandle = default;
            voice.Channel = channel;
            voice.Location = location;
            voice.VolumeScale = volumeScale;
            voice.StartingTicks = 0;
            voice.ValidityCts = new CancellationTokenSource();

            index = free;
            handle = new AudioHandle(free, voice.Generation);
            return true;
        }

        private bool IsCurrent(AudioHandle handle) => IsCurrent(handle.Id, handle.Generation);

        private bool IsCurrent(int index, int generation)
        {
            if (generation == 0 || index < 0 || index >= _voices.Length)
                return false;

            var voice = _voices[index];
            return voice.Generation == generation && voice.State != AudioManagedVoiceState.Free;
        }

        private void StopVoice(int index)
        {
            var voice = _voices[index];

            if (voice.State == AudioManagedVoiceState.Loading)
            {
                // 取消有效性令牌：provider 加载完成后不会 start，由它归还租约
                try
                {
                    voice.ValidityCts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 已在别处释放：无妨
                }

                FreeVoice(voice);
                return;
            }

            SafeStopAndRelease(voice.ProviderHandle);
            FreeVoice(voice);
        }

        private static int NextGeneration(int current)
        {
            int next = current + 1;
            return next == 0 ? 1 : next;
        }

        private void WarnExhaustedOnce()
        {
            if (_exhaustionWarned)
                return;

            _exhaustionWarned = true;
            LogManager.Warning(LogCategories.Audio,
                "播放源已耗尽（MaxVoices={0}），本次播放被丢弃。" +
                "调大 AudioInitOptions.MaxVoices，或缩短同时播放的音效数量。",
                _maxVoices);
        }

        private void SafeStopAndRelease(AudioProviderHandle providerHandle)
        {
            if (providerHandle.IsDefault)
                return;

            try
            {
                _provider.Stop(providerHandle);
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e, "停止音频播放时发生异常");
            }

            try
            {
                _provider.Release(providerHandle);
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e, "释放音频播放时发生异常");
            }
        }

        private void ReleaseVoice(AudioManagedVoice voice)
        {
            if (!voice.ProviderHandle.IsDefault)
                SafeStopAndRelease(voice.ProviderHandle);

            FreeVoice(voice);
        }

        private void FreeVoice(AudioManagedVoice voice)
        {
            voice.State = AudioManagedVoiceState.Free;
            voice.ProviderHandle = default;
            voice.Channel = null;
            voice.Location = null;
            voice.VolumeScale = 1f;
            voice.StartingTicks = 0;
            voice.ValidityCts = null;
            _exhaustionWarned = false;
        }

        private float ComputeVoiceVolume(AudioManagedVoice voice)
        {
            if (_masterMuted)
                return 0f;

            var channel = GetOrCreateChannel(voice.Channel);
            if (channel.Muted)
                return 0f;

            return Mathf.Clamp01(_masterVolume * channel.Volume * voice.VolumeScale);
        }

        /// <summary>
        /// 发起播放时的音量：用<b>不建状态</b>的通道读取。否则一次加载失败的播放会
        /// 在 <c>CopyChannels</c> 里多出一行没有播放过的通道。
        /// </summary>
        private float ComputeVolumeNoCreate(string channel, float volumeScale)
        {
            if (_masterMuted)
                return 0f;

            if (_channels.TryGetValue(channel, out var state) && state.Muted)
                return 0f;

            float channelVolume = state?.Volume ?? 1f;
            return Mathf.Clamp01(_masterVolume * channelVolume * volumeScale);
        }

        /// <summary>
        /// 音量变更后重算正在播放的声音。事件式而非逐帧：只在变更发生时扫一遍槽位。
        /// </summary>
        private void RefreshVolumes(string channelFilter)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];

                if (voice.State == AudioManagedVoiceState.Free ||
                    voice.State == AudioManagedVoiceState.Loading)
                {
                    continue;
                }

                if (channelFilter != null &&
                    !string.Equals(voice.Channel, channelFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                bool applied;
                try
                {
                    applied = _provider.SetVolume(voice.ProviderHandle, ComputeVoiceVolume(voice));
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategories.Audio, e, "刷新音频音量时发生异常");
                    applied = false;
                }

                if (!applied)
                    ReleaseVoice(voice);
            }
        }

        /// <summary>取通道状态，不存在则按默认值建一个。<b>只有写入路径会走到这里</b>。</summary>
        private AudioChannelState GetOrCreateChannel(string channel)
        {
            if (_channels.TryGetValue(channel, out var state))
                return state;

            state = new AudioChannelState();
            _channels[channel] = state;
            return state;
        }

        private static float NormalizeVolumeScale(float volumeScale)
            => volumeScale > 0f ? volumeScale : 1f;

        private static float NormalizePitch(float pitch)
            => pitch > 0f ? pitch : 1f;

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException(
                    "[Audio] location 不能为 null 或空白。请传入 XAsset 能解析的资源地址。", nameof(location));
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new InvalidOperationException(
                    "[Audio] 本实例已销毁。请重新调用 AudioManager.Initialize()、" +
                    "AudioManager.SetInstance(...) 或 AudioManager.SetProvider(...)。");
            }
        }

        /// <summary>首个播放时注册 ticker；之后每次注册都是空操作。</summary>
        private void EnsureTickerRegistered()
        {
            // 不能只信本地记账：UpdateManager.Clear() 或异常自动注销会把 ticker 从调度器里摘掉，
            // 而 _tickerRegistered 仍为 true——此后 Play 不再注册，回收永久停摆（AUD-002）。
            if (_tickerRegistered && UpdateManager.IsRegistered(_ticker))
                return;

            // 时间轴用 Unscaled：回收是记账而不是玩法逻辑，timeScale = 0 期间照样要能把播完的槽位收回来
            UpdateManager.Register(_ticker, 0, UpdateTier.Tier1, UpdateTimeMode.Unscaled);
            _tickerRegistered = true;
        }

        /// <summary>最后一个播放结束时注销 ticker，闲置时零帧开销。</summary>
        private void UnregisterTicker()
        {
            if (UpdateManager.IsRegistered(_ticker))
                UpdateManager.Unregister(_ticker);

            _tickerRegistered = false;
        }

        #endregion
    }
}
