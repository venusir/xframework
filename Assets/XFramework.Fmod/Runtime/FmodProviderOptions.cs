namespace XFramework.XAudio.Fmod
{
    /// <summary>
    /// provider 需要的不可变快照：只承载 <see cref="EventPathPrefix"/> 与 <see cref="MaxVoices"/>；
    /// 其余配置（Banks / LoadBankSamples / OwnsBanks / 三档音量）由安装器或核心选项直接读取。
    /// <para>资产在启动时读取一次；之后修改资产不影响已构造的 provider。</para>
    /// </summary>
    internal readonly struct FmodProviderOptions
    {
        #region Fields

        /// <summary>事件路径前缀；空字符串表示不补前缀。<c>null</c> 归一为 <c>"event:/"</c>。</summary>
        internal readonly string EventPathPrefix;

        /// <summary>provider 槽位容量；钳到至少 <c>1</c>。</summary>
        internal readonly int MaxVoices;

        #endregion

        #region Constructors

        internal FmodProviderOptions(string eventPathPrefix, int maxVoices)
        {
            EventPathPrefix = eventPathPrefix ?? "event:/";
            MaxVoices = maxVoices < 1 ? 1 : maxVoices;
        }

        #endregion
    }
}
