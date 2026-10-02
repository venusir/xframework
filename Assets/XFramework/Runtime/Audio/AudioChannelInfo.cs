namespace XFramework.XAudio
{
    /// <summary>
    /// 一个音频通道的状态（诊断用只读快照）。
    /// <para><b>回答什么问题</b>：哪些通道被用过、各自的音量与静音、各有多少路声音在播——「声音听不见」
    /// 的第一件事就是看这里（通道被静音 / 音量为 0 是常见现场）。</para>
    /// <para><b>怎么取</b>：<see cref="AudioManager.CopyChannels(System.Collections.Generic.List{AudioChannelInfo})"/>。
    /// 只列出**已创建**的通道——通道是首次在该通道上播放时惰性创建的，没播过的通道不在这张表里
    /// （<c>GetChannelVolume</c> 之类的查询不会创建它）。</para>
    /// </summary>
    public readonly struct AudioChannelInfo
    {
        /// <summary>通道名（字符串通道，不是枚举）。</summary>
        public readonly string Channel;

        /// <summary>通道音量（不含主音量）。</summary>
        public readonly float Volume;

        /// <summary>通道自身是否静音（不含主静音）。</summary>
        public readonly bool IsMuted;

        /// <summary>该通道当前在播的路数。</summary>
        public readonly int ActiveVoiceCount;

        internal AudioChannelInfo(string channel, float volume, bool isMuted, int activeVoiceCount)
        {
            Channel = channel;
            Volume = volume;
            IsMuted = isMuted;
            ActiveVoiceCount = activeVoiceCount;
        }

        /// <summary>单行摘要，形如 <c>BGM(音量 0.80, 在播 1)</c>。</summary>
        public override string ToString()
            => IsMuted
                ? $"{Channel}(静音, 在播 {ActiveVoiceCount})"
                : $"{Channel}(音量 {Volume:F2}, 在播 {ActiveVoiceCount})";
    }
}
