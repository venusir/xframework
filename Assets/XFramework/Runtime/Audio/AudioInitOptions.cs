namespace XFramework.XAudio
{
    /// <summary>
    /// <see cref="AudioManager.Initialize"/> 的初始化选项。
    /// <para><b>选项在初始化时读取一次并快照</b>，之后修改本实例不再生效——与 <c>SettingsOptions</c>、
    /// <c>AssetInitOptions</c> 同一取舍。</para>
    /// </summary>
    public sealed class AudioInitOptions
    {
        /// <summary>
        /// 同时播放的上限，即播放源池的容量（默认 <c>32</c>）。
        /// <para><b>这是本模块唯一的并发策略</b>：取不到空闲播放源时 <see cref="AudioManager.Play"/> /
        /// <see cref="AudioManager.PlayAsync"/> 返回 <c>default(AudioHandle)</c> 并记一条限流告警
        /// （同一段耗尽期只告警一次，不刷屏）。<b>框架不做优先级抢占</b>——抢占必须先回答「抢谁」，
        /// 那是替使用方决定混音策略。</para>
        /// <para>小于 <c>1</c> 会被钳到 <c>1</c>。</para>
        /// </summary>
        public int MaxVoices = 32;

        /// <summary>初始主音量，线性值，钳到 <c>[0, 1]</c>。</summary>
        public float MasterVolume = 1f;

        /// <summary>初始主静音状态。</summary>
        public bool MasterMuted;

        /// <summary>
        /// 是否在初始化时一次性建满 <see cref="MaxVoices"/> 个播放源。
        /// <para>默认 <c>false</c>：惰性扩容，从不播放的项目一个 GameObject 都不会建。</para>
        /// </summary>
        public bool PrewarmVoices;
    }
}
