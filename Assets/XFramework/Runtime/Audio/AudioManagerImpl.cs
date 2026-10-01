using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUpdate;

namespace XFramework.XAudio
{
    /// <summary>
    /// <see cref="IAudioManager"/> 的默认实现：自持一个隐藏宿主，池化 <c>AudioSource</c> 承载播放。
    /// <para><b>资源只走 location</b>：本实现经 <see cref="IAudioClipLoader"/> 从 <c>XAsset</c> 加载，
    /// 播放期间槽位持有资源句柄——它同时是释放凭据与保活凭据，释放点必须恰好一次。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>C3 / C4 尚未实现</b>：音量、通道配置、暂停与部分查询成员仍抛
    /// <see cref="NotImplementedException"/>，在后续提交中逐个补全并配上各自的用例。</para>
    /// </remarks>
    internal sealed class AudioManagerImpl : IAudioManager
    {
        #region Constants

        /// <summary>
        /// 保留通道名。<c>"master"</c> 不是通道——总音量由 <see cref="MasterVolume"/> /
        /// <see cref="MasterMuted"/> 表达。把它当通道用会抛 <see cref="ArgumentException"/>，
        /// 免得出现「看起来能控总音量、实际只控了一个没人收听的通道」这种静默坑。
        /// </summary>
        internal const string ReservedMasterChannel = "master";

        #endregion

        #region Private Fields

        private readonly AudioSourcePool _pool;
        private readonly AudioTicker _ticker;
        private readonly IAudioClipLoader _clipLoader;

        /// <summary>
        /// 实例级生命周期令牌。<see cref="Dispose"/> 取消它，从而中断全部在途加载。
        /// <para><b>刻意不 Dispose 这个 CTS</b>：取消之后仍可能有续体在读它的令牌
        /// （<c>ThrowIfCancellationRequested</c>），而 <c>Dispose</c> 后再取 <c>Token</c> 会抛
        /// <c>ObjectDisposedException</c>。一个 CTS 的托管资源代价可以忽略，稳定优先。</para>
        /// </summary>
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        /// <summary>
        /// 通道状态表。惰性创建：只有被<b>写入</b>过的通道才会进表。
        /// <para>用 <see cref="StringComparer.Ordinal"/>：通道名区分大小写，这是公开承诺的一部分
        /// （<c>"BGM"</c> 与 <c>"bgm"</c> 是两个通道）。</para>
        /// </summary>
        private readonly Dictionary<string, AudioChannelState> _channels =
            new Dictionary<string, AudioChannelState>(StringComparer.Ordinal);

        private float _masterVolume = 1f;
        private bool _masterMuted;
        private bool _paused;

        private bool _tickerRegistered;
        private bool _disposed;

        #endregion

        #region Constructors

        internal AudioManagerImpl(AudioInitOptions options)
            : this(options, null)
        {
        }

        /// <summary>测试用的构造重载：注入假的加载缝以控制加载时序与失败。</summary>
        internal AudioManagerImpl(AudioInitOptions options, IAudioClipLoader clipLoader)
        {
            Options = options ?? new AudioInitOptions();
            _clipLoader = clipLoader ?? new AudioClipLoader();
            _pool = new AudioSourcePool(Options.MaxVoices);
            _ticker = new AudioTicker(this);

            _masterVolume = Mathf.Clamp01(Options.MasterVolume);
            _masterMuted = Options.MasterMuted;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 本次初始化使用的选项。原样保存、<b>未归一化</b>——归一化发生在各自的使用点
        /// （如 <see cref="AudioInitOptions.MaxVoices"/> 在建池时钳到下限 1）。
        /// </summary>
        internal AudioInitOptions Options { get; }

        /// <summary>
        /// 供 <see cref="AudioTicker"/> 每拍调用：回收播完的槽位，全部回收完时注销 ticker。
        /// </summary>
        internal void SweepVoices()
        {
            if (_disposed)
                return;

            if (!_pool.Sweep())
                UnregisterTicker();
        }

        /// <summary>
        /// 通道名归一化与保留名校验。
        /// <para><c>null</c>/空 → <see cref="AudioChannels.Default"/>（照 <c>LockManager</c> 的
        /// <c>null → Global</c> 的手感）；<c>"master"</c> → 抛。</para>
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

        /// <summary>
        /// 释放本实现持有的全部资源：取消在途加载、注销 ticker、停播并销毁全部槽位与宿主。
        /// <para>幂等；在 <c>Application.quitting</c> 上也会被触发。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _lifetimeCts.Cancel();
            UnregisterTicker();
            _pool.Dispose();
        }

        #endregion

        #region 播放

        /// <inheritdoc/>
        public AudioHandle Play(string location, AudioPlayOptions options = default)
        {
            EnsureNotDisposed();
            ValidateLocation(location);

            string channel = NormalizeChannel(options.Channel);

            // 预占发生在发起加载之前：池满时一次 IO 都不会浪费
            if (!_pool.TryReserve(channel, location, out int index, out AudioHandle handle))
                return default;

            EnsureTickerRegistered();
            RunFireAndForget(index, handle.Generation, location, options);
            return handle;
        }

        /// <inheritdoc/>
        public UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options = default,
            CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            ValidateLocation(location);

            string channel = NormalizeChannel(options.Channel);

            if (!_pool.TryReserve(channel, location, out int index, out AudioHandle handle))
                return UniTask.FromResult(default(AudioHandle));

            EnsureTickerRegistered();
            return PlayCoreAsync(index, handle.Generation, location, options, cancellationToken);
        }

        /// <summary>
        /// 播放的公共内核：加载 → 竞态检查 → 起播。两条公开路径共用它，差别只在
        /// 「谁在什么时候拿到句柄」。
        /// </summary>
        private async UniTask<AudioHandle> PlayCoreAsync(int index, int generation, string location,
            AudioPlayOptions options, CancellationToken cancellationToken)
        {
            // 同步取令牌：本方法在首个 await 之前是同步执行的，此后 Dispose 才能安全地取消它
            CancellationToken lifetime = _lifetimeCts.Token;

            AudioClipLease clipLease = default;
            CancellationTokenSource linked = null;

            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
                    clipLease = await _clipLoader.LoadAsync(location, linked.Token);
                }
                else
                {
                    clipLease = await _clipLoader.LoadAsync(location, lifetime);
                }
            }
            catch (OperationCanceledException)
            {
                // 收尾：把预占的槽位还回去，且不吞掉取消异常
                InvalidateReservation(index, generation);
                throw;
            }
            catch (Exception e)
            {
                // 加载缝的契约是「失败返回 default 而不抛」，真抛了说明是未预期异常
                LogManager.Exception(LogCategories.Audio, e,
                    string.Format("加载音频资源 '{0}' 时发生未预期异常", location));
                InvalidateReservation(index, generation);
                return default;
            }
            finally
            {
                linked?.Dispose();
            }

            if (!_pool.IsCurrent(index, generation))
            {
                // 期间被 Stop / StopAll / Destroy 作废：释放已加载的资源后静默退出
                SafeDisposeLease(clipLease, location);
                return default;
            }

            if (!clipLease.IsValid)
            {
                SafeDisposeLease(clipLease, location);
                _pool.Release(index);
                LogManager.Warning(LogCategories.Audio,
                    "播放失败，clip 加载未成功：'{0}'（底层错误见上一条 YooAsset 日志）。", location);
                return default;
            }

            var voice = _pool.GetSlot(index);
            voice.ClipLease = clipLease;
            voice.Loop = options.Loop;
            voice.VolumeScale = NormalizeVolumeScale(options.VolumeScale);
            ConfigureSource(voice.Source, clipLease.Clip, options);
            ApplyVoiceVolume(voice);
            voice.Source.Play();
            voice.StartingTicks = 0;

            // 管理器处于暂停态时，新起的播放也立即进入暂停——Pause 是管理器级闸门，
            // 不是「把此刻在播的那些冻住」。否则「暂停期间新起的声音照放、Resume 后不受管」自相矛盾。
            if (_paused)
            {
                voice.Source.Pause();
                voice.State = AudioVoiceState.Paused;
            }
            else
            {
                voice.State = AudioVoiceState.Starting;
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
            if (!_pool.IsCurrent(handle.Id, handle.Generation))
                return false;

            var voice = _pool.GetSlot(handle.Id);

            if (voice.State == AudioVoiceState.Loading)
            {
                // 加载中：只作废槽位。已加载的句柄由加载续体的竞态检查负责释放——释放点恰好一次
                _pool.Invalidate(handle.Id);
            }
            else
            {
                _pool.Release(handle.Id);
            }

            return true;
        }

        /// <inheritdoc/>
        public int StopAll(string channel = null)
        {
            EnsureNotDisposed();

            string filter = channel == null ? null : NormalizeChannel(channel);
            int stopped = 0;

            // 槽位只置空闲、不移动下标，故边遍历边释放是安全的
            for (int i = 0; i < _pool.SlotCount; i++)
            {
                var voice = _pool.GetSlot(i);

                if (voice.State == AudioVoiceState.Free)
                    continue;

                if (filter != null && !string.Equals(voice.Channel, filter, StringComparison.Ordinal))
                    continue;

                if (voice.State == AudioVoiceState.Loading)
                    _pool.Invalidate(i);
                else
                    _pool.Release(i);

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

            for (int i = 0; i < _pool.SlotCount; i++)
            {
                var voice = _pool.GetSlot(i);

                if (voice.State != AudioVoiceState.Starting && voice.State != AudioVoiceState.Playing)
                    continue;   // 空闲与加载中的槽位不动：前者没什么可暂停，后者会在起播时就进入暂停

                voice.Source.Pause();
                voice.State = AudioVoiceState.Paused;
            }
        }

        /// <inheritdoc/>
        public void Resume()
        {
            EnsureNotDisposed();

            if (!_paused)
                return;

            _paused = false;

            for (int i = 0; i < _pool.SlotCount; i++)
            {
                var voice = _pool.GetSlot(i);

                if (voice.State != AudioVoiceState.Paused)
                    continue;

                voice.Source.UnPause();

                // 回到 Starting 而不是直接 Playing：由 ticker 下一拍确认 isPlaying 真的回来了，
                // 与起播路径同形。若某个平台上 UnPause 不生效，超时兜底仍然管用。
                voice.State = AudioVoiceState.Starting;
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
            if (!_pool.IsCurrent(handle.Id, handle.Generation))
                return false;

            var state = _pool.GetSlot(handle.Id).State;

            // 加载中的播放不算「在播」——这与 PlayAsync 的「句柄返回时必定已加载成功」互为表里。
            // 暂停中的算：它不是被停掉，只是暂时不出声；Stop 与音量变更对它照常有效。
            return state == AudioVoiceState.Starting
                || state == AudioVoiceState.Playing
                || state == AudioVoiceState.Paused;
        }

        /// <inheritdoc/>
        public int ActiveVoiceCount => _pool.CountActive();

        /// <inheritdoc/>
        public int GetActiveVoiceCount(string channel)
        {
            string filter = NormalizeChannel(channel);
            int count = 0;

            for (int i = 0; i < _pool.SlotCount; i++)
            {
                var voice = _pool.GetSlot(i);

                if (voice.State != AudioVoiceState.Free &&
                    string.Equals(voice.Channel, filter, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        #endregion

        #region Private

        /// <summary>把预占的槽位还回去——只在它仍是当初那一代时才动。</summary>
        private void InvalidateReservation(int index, int generation)
        {
            if (_pool.IsCurrent(index, generation))
                _pool.Invalidate(index);
        }

        /// <summary>把公开参数写进播放源。音量不在这里——见 <see cref="ApplyVoiceVolume"/>。</summary>
        private static void ConfigureSource(AudioSource source, AudioClip clip, AudioPlayOptions options)
        {
            source.clip = clip;
            source.loop = options.Loop;

            // 归一化：非正值一律按 1 处理，使 default(AudioPlayOptions) 等价于「原速原音量」
            source.pitch = options.Pitch > 0f ? options.Pitch : 1f;

            float blend = options.SpatialBlend;
            if (blend < 0f)
                blend = 0f;
            else if (blend > 1f)
                blend = 1f;

            source.spatialBlend = blend;

            if (blend > 0f)
                source.transform.position = options.Position;
        }

        /// <summary>
        /// 单次播放的音量缩放归一化：非正值一律按 <c>1</c> 处理。
        /// <para>这是让 <c>default(AudioPlayOptions)</c> 安全的关键——它的 <c>VolumeScale</c> 是 <c>0</c>，
        /// 不归一化的话「忘了写 options」会得到一个完全静音的声音，比报错更难查。</para>
        /// <para>刻意<b>不</b>在这里钳上限：放大由三档相乘后的最终钳制统一处理（见
        /// <see cref="ComputeVoiceVolume"/>），这样「用缩放的相对关系」不会被提前压平。</para>
        /// </summary>
        private static float NormalizeVolumeScale(float volumeScale)
            => volumeScale > 0f ? volumeScale : 1f;

        /// <summary>
        /// 一次播放的实际音量：主音量 × 通道音量 × 单次缩放，任一档静音即为 <c>0</c>，
        /// 最后钳到 <c>[0, 1]</c> 再写进播放源。
        /// <para><b>框架自己钳制</b>而不依赖引擎：实测表明 <c>AudioSource.volume</c> 的 setter 确实会把值钳到
        /// <c>[0, 1]</c>，但公开承诺不该押在引擎行为上（换个平台或版本就可能变）。</para>
        /// </summary>
        private float ComputeVoiceVolume(AudioVoice voice)
        {
            if (_masterMuted)
                return 0f;

            var channel = GetOrCreateChannel(voice.Channel);
            if (channel.Muted)
                return 0f;

            return Mathf.Clamp01(_masterVolume * channel.Volume * voice.VolumeScale);
        }

        /// <summary>把当前音量写进某个播放源。播完/空闲的槽位调用它是无害的空写。</summary>
        private void ApplyVoiceVolume(AudioVoice voice)
        {
            voice.Source.volume = ComputeVoiceVolume(voice);
        }

        /// <summary>
        /// 音量变更后重算<b>正在播放</b>的声音。
        /// <para>事件式而非逐帧：只在变更发生时扫一遍槽位（O(池容量)、无分配）。槽位表本身就是活跃播放的
        /// 注册表，不另维护一张「谁在播」的表——那会多出一处可能不同步的状态。</para>
        /// </summary>
        /// <param name="channelFilter">只重算该通道；<c>null</c> 表示全部（主音量变更走这条）。</param>
        private void RefreshVolumes(string channelFilter)
        {
            for (int i = 0; i < _pool.SlotCount; i++)
            {
                var voice = _pool.GetSlot(i);

                if (voice.State == AudioVoiceState.Free)
                    continue;

                if (channelFilter != null &&
                    !string.Equals(voice.Channel, channelFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                ApplyVoiceVolume(voice);
            }
        }

        /// <summary>取通道状态，不存在则按默认值建一个。<b>只有写入路径会走到这里</b>——查询不建状态。</summary>
        private AudioChannelState GetOrCreateChannel(string channel)
        {
            if (_channels.TryGetValue(channel, out var state))
                return state;

            state = new AudioChannelState();
            _channels[channel] = state;
            return state;
        }

        /// <summary>
        /// 释放一个由加载路径拿到、但没能交到槽位手上的资源租约。
        /// <para>用 try/catch 包住：<c>Dispose</c> 可能由 <c>Application.quitting</c> 触发，而处理器顺序
        /// 不保证 <c>XAsset</c> 的包还没销毁——那时释放会抛。退出路径上不该再炸一次。</para>
        /// </summary>
        private static void SafeDisposeLease(AudioClipLease lease, string location)
        {
            if (!lease.IsValid)
                return;

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
                    "[Audio] 本实例已销毁。请重新调用 AudioManager.Initialize() 或 AudioManager.SetInstance(...)。");
            }
        }

        /// <summary>首个播放时注册 ticker；之后每次注册都是空操作。</summary>
        private void EnsureTickerRegistered()
        {
            if (_tickerRegistered)
                return;

            // 时间轴用 Unscaled：回收是记账而不是玩法逻辑，timeScale = 0 期间照样要能把播完的槽位收回来
            UpdateManager.Register(_ticker, 0, UpdateTier.Tier1, UpdateTimeMode.Unscaled);
            _tickerRegistered = true;
        }

        /// <summary>最后一个播放结束时注销 ticker，闲置时零帧开销。</summary>
        private void UnregisterTicker()
        {
            if (!_tickerRegistered)
                return;

            UpdateManager.Unregister(_ticker);
            _tickerRegistered = false;
        }

        #endregion
    }
}
