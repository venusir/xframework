namespace XFramework.XAudio
{
    /// <summary>
    /// <see cref="IAudioProvider.Poll"/> 的返回值。核心 ticker 据此决定状态迁移与回收。
    /// </summary>
    public enum AudioProviderPlaybackState
    {
        /// <summary>
        /// 句柄失效：未知 / 过期 / 播放源被外部销毁。
        /// <para><b>默认值</b>，实现返回 default 时按失效处理。</para>
        /// </summary>
        Invalid = 0,

        /// <summary>已起播，但尚未观察到真正在播。</summary>
        Starting = 1,

        /// <summary>正在播放。</summary>
        Playing = 2,

        /// <summary>引擎侧暂停（内置 provider 的 <c>AudioListener.pause</c>）；核心保留槽位、不计起播超时。</summary>
        Paused = 3,

        /// <summary>自然结束。</summary>
        Stopped = 4,
    }
}
