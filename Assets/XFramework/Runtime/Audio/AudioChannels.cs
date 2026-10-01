namespace XFramework.XAudio
{
    /// <summary>
    /// 推荐通道名常量。
    /// <para><b>这些是建议值，不是约束</b>：通道是开放的字符串域，项目可以自由使用任意名字
    /// （<see cref="AudioManager.RegisterChannel"/> 可预先声明音量与静音，未声明的通道首次使用时按默认值惰性建状态）。
    /// 与 <c>XUI.UILayers</c> 给推荐层级、<c>XInput</c> 不定义动作名同一取舍——框架锁死枚举就等于
    /// 「使用方必须按某种架构组织音频」。</para>
    /// <para>命名的比较语义是<b>序数（Ordinal）</b>，即区分大小写；<c>"BGM"</c> 与 <c>"bgm"</c> 是两个通道。</para>
    /// <para><b>保留名</b>：<c>"master"</c> 不是通道——总音量由 <see cref="AudioManager.MasterVolume"/> /
    /// <see cref="AudioManager.MasterMuted"/> 表达。把它传给通道 API 会抛 <c>ArgumentException</c>，
    /// 免得出现「看起来能控总音量、实际只控了一个没人收听的通道」这种静默坑。</para>
    /// </summary>
    public static class AudioChannels
    {
        /// <summary>默认通道。所有 <c>null</c>/空字符串的通道参数都归一化到它。</summary>
        public const string Default = "default";

        /// <summary>背景音乐。</summary>
        public const string Bgm = "bgm";

        /// <summary>音效。</summary>
        public const string Se = "se";

        /// <summary>语音 / 配音。</summary>
        public const string Voice = "voice";

        /// <summary>界面交互音。</summary>
        public const string Ui = "ui";
    }
}
