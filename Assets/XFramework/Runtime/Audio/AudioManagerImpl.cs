using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XAudio
{
    /// <summary>
    /// <see cref="IAudioManager"/> 的默认实现：自持一个隐藏宿主，池化 <c>AudioSource</c> 承载播放。
    /// </summary>
    /// <remarks>
    /// <para><b>C1 提交只实现生命周期</b>：播放 / 控制 / 音量 / 查询成员一律抛
    /// <see cref="NotImplementedException"/>，在 C2–C4 提交中逐个补全并配上各自的用例。
    /// 本类在提交历史里短暂处于「半实现」状态是计划的一部分——公开 API 面先一次锁死，
    /// 好让传发完备性守卫从第一条起就有牙齿。</para>
    /// </remarks>
    internal sealed class AudioManagerImpl : IAudioManager
    {
        #region Constructors

        internal AudioManagerImpl(AudioInitOptions options)
        {
            Options = options ?? new AudioInitOptions();
        }

        #endregion

        #region Internal

        /// <summary>
        /// 本次初始化使用的选项。原样保存、<b>未归一化</b>——归一化发生在各自的使用点
        /// （如 <see cref="AudioInitOptions.MaxVoices"/> 在建池时钳到下限 1）。
        /// </summary>
        internal AudioInitOptions Options { get; }

        #endregion

        #region Lifecycle

        /// <summary>
        /// 释放本实现持有的全部资源。
        /// <para>C1 阶段本实现尚无自持资源（宿主与播放源池在 C2 建立）。</para>
        /// </summary>
        public void Dispose()
        {
        }

        #endregion

        #region 播放

        /// <inheritdoc/>
        public AudioHandle Play(string location, AudioPlayOptions options = default)
            => throw new NotImplementedException("[Audio] Play 尚未实现（C2 提交补全）。");

        /// <inheritdoc/>
        public UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options = default,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException("[Audio] PlayAsync 尚未实现（C2 提交补全）。");

        #endregion

        #region 控制

        /// <inheritdoc/>
        public bool Stop(AudioHandle handle)
            => throw new NotImplementedException("[Audio] Stop 尚未实现（C2 提交补全）。");

        /// <inheritdoc/>
        public int StopAll(string channel = null)
            => throw new NotImplementedException("[Audio] StopAll 尚未实现（C2 提交补全）。");

        /// <inheritdoc/>
        public void Pause()
            => throw new NotImplementedException("[Audio] Pause 尚未实现（C4 提交补全）。");

        /// <inheritdoc/>
        public void Resume()
            => throw new NotImplementedException("[Audio] Resume 尚未实现（C4 提交补全）。");

        /// <inheritdoc/>
        public bool IsPaused
            => throw new NotImplementedException("[Audio] IsPaused 尚未实现（C4 提交补全）。");

        #endregion

        #region 音量

        /// <inheritdoc/>
        public float MasterVolume
        {
            get => throw new NotImplementedException("[Audio] MasterVolume 尚未实现（C3 提交补全）。");
            set => throw new NotImplementedException("[Audio] MasterVolume 尚未实现（C3 提交补全）。");
        }

        /// <inheritdoc/>
        public bool MasterMuted
        {
            get => throw new NotImplementedException("[Audio] MasterMuted 尚未实现（C3 提交补全）。");
            set => throw new NotImplementedException("[Audio] MasterMuted 尚未实现（C3 提交补全）。");
        }

        /// <inheritdoc/>
        public void SetChannelVolume(string channel, float volume)
            => throw new NotImplementedException("[Audio] SetChannelVolume 尚未实现（C3 提交补全）。");

        /// <inheritdoc/>
        public float GetChannelVolume(string channel)
            => throw new NotImplementedException("[Audio] GetChannelVolume 尚未实现（C3 提交补全）。");

        /// <inheritdoc/>
        public void SetChannelMuted(string channel, bool muted)
            => throw new NotImplementedException("[Audio] SetChannelMuted 尚未实现（C3 提交补全）。");

        /// <inheritdoc/>
        public bool IsChannelMuted(string channel)
            => throw new NotImplementedException("[Audio] IsChannelMuted 尚未实现（C3 提交补全）。");

        /// <inheritdoc/>
        public void RegisterChannel(string channel, AudioChannelConfig config)
            => throw new NotImplementedException("[Audio] RegisterChannel 尚未实现（C3 提交补全）。");

        #endregion

        #region 查询

        /// <inheritdoc/>
        public bool IsPlaying(AudioHandle handle)
            => throw new NotImplementedException("[Audio] IsPlaying 尚未实现（C2 提交补全）。");

        /// <inheritdoc/>
        public int ActiveVoiceCount
            => throw new NotImplementedException("[Audio] ActiveVoiceCount 尚未实现（C4 提交补全）。");

        /// <inheritdoc/>
        public int GetActiveVoiceCount(string channel)
            => throw new NotImplementedException("[Audio] GetActiveVoiceCount 尚未实现（C4 提交补全）。");

        #endregion
    }
}
